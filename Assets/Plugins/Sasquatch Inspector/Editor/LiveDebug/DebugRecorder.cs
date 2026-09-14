using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The sample buffer behind Live Debug: one per inspected target, holding every recorded field on
    /// <b>one shared clock</b>.
    ///
    /// <para><b>Why one recorder and not one per field.</b> Because sample <c>i</c> means the same instant in
    /// every channel, clicking a point on any graph can answer "what ELSE moved on this frame" — which is the
    /// question a scattered-<c>Debug.Log</c> session is actually trying to reconstruct. Per-field opt-in would
    /// make that impossible, which is why the header toggle records the whole script rather than a selection.</para>
    ///
    /// <para>Lifetime: keyed by <see cref="UnityObjectId"/>, which is per-session by contract, so the map is
    /// cleared on play-mode transitions and assembly reload rather than persisted. A recorder holds no Unity
    /// objects — only floats, strings and property paths — so a stale entry costs memory, never a leaked
    /// native object.</para>
    /// </summary>
    internal sealed class DebugRecorder
    {
        /// <summary>Ring length. ~20s at the 60Hz the tick reaches while the editor is repainting for Play Mode.</summary>
        internal const int Capacity = 1200;

        private static readonly Dictionary<int, DebugRecorder> s_byTarget = new Dictionary<int, DebugRecorder>();

        private readonly List<DebugChannel> _channels = new List<DebugChannel>();
        private readonly Dictionary<string, DebugChannel> _byPath = new Dictionary<string, DebugChannel>(StringComparer.Ordinal);
        private readonly double[] _time = new double[Capacity];
        /// <summary>The GAME frame each sample came from — <c>Time.frameCount</c>, not a sample ordinal.</summary>
        private readonly int[] _frame = new int[Capacity];

        private SerializedObject _so;
        /// <summary>Last game frame sampled, so a tick that beats the game to the punch doesn't record a duplicate.</summary>
        private int _lastFrame = -1;
        private int _count;
        private int _writeSlot;
        /// <summary>
        /// True while <see cref="_time"/>, read oldest-first, is non-decreasing — which it is in every real
        /// recording, because stamps come from one monotonic editor clock in sample order. It is tracked
        /// rather than assumed because <see cref="AbsoluteNearestTime"/>'s binary search is only equivalent
        /// to the linear scan it replaced while the order actually holds, and one path writes times this
        /// class did not produce: RestoreFrom, reading a persisted blob. A blob that arrives out of order
        /// (hand-edited, or truncated by a future writer) must not silently change which sample a log pin
        /// lands on — so it falls back to the scan instead.
        /// <para>Deliberately one-way within a recording: an out-of-order sample can later fall off the ring
        /// without this flipping back. That costs a linear scan and can never cost a wrong answer.</para>
        /// </summary>
        private bool _timeSorted = true;
        /// <summary>Absolute index of the NEXT sample written — never wraps, so it is a stable id for a moment.</summary>
        private int _absolute;

        /// <summary>
        /// Editor timestamp of the FIRST sample of the current recording — the zero point every "t+" readout
        /// counts from.
        ///
        /// <para>Pass 2 used the oldest sample still in the ring instead, which silently re-based itself the
        /// moment the ring wrapped at <see cref="Capacity"/>: "t+2.69s" meant seconds-since-recording-started
        /// for the first twenty seconds of a run and seconds-since-the-buffer-scrolled forever after, with
        /// nothing marking the changeover. A number whose meaning changes without saying so is worse than no
        /// number.</para>
        /// </summary>
        private double _recordStart;
        private bool _hasRecordStart;

        /// <summary>
        /// The inspected object this recorder belongs to.
        ///
        /// <para>Held so a DEAD entry can be recognised. The map is keyed by <see cref="UnityObjectId"/>, which
        /// is an instance id today and an entity-id hash on 6000.5+ — neither is reversible back to an object,
        /// so the key alone cannot answer "is this still a real thing?". A plain reference can, through Unity's
        /// overloaded <c>==</c>.</para>
        /// </summary>
        private UnityEngine.Object _target;

        /// <summary>Which clock the samples currently in the ring were taken on. See <see cref="Sample"/>.</summary>
        private DebugTickSource _sampledSource = DebugTickSource.Update;

        /// <summary>
        /// Has anything actually been written on <see cref="_sampledSource"/>? False on a fresh recorder and on
        /// one repopulated by <see cref="Adopt"/>, so the "the clock changed, start a new trace" rule cannot
        /// fire on a clock this recorder never sampled on. See its use in <see cref="Sample"/>.
        /// </summary>
        private bool _hasSampledSource;

        /// <summary>Seconds between samples on the Interval clock, cached with <see cref="TickSource"/>.</summary>
        private double _intervalSeconds = 0.1d;
        /// <summary>Editor time the Interval clock is next due. Zero means "due immediately".</summary>
        private double _intervalDueAt;

        /// <summary>
        /// False once the held SerializedObject has been found disposed. Latched rather than re-tested, so a
        /// dead recorder costs one bool read per game frame instead of a thrown exception. Cleared the moment
        /// <see cref="For"/> hands over a live handle again, which is what makes this self-healing rather than
        /// a permanent stop.
        /// </summary>
        private bool _soAlive = true;

        /// <summary>
        /// The inspected type, held so <see cref="SampleAllArmed"/> can filter without asking the UI. Set on
        /// creation and on every rebind, because a rebuild can hand the same recorder a different target.
        /// </summary>
        internal Type TargetType { get; private set; }

        /// <summary>
        /// Whether this recorder should be written by the sampling clock. Derived from
        /// <see cref="DebugModeState"/> and cached — see <see cref="RefreshArmed"/>. Deliberately NOT re-read
        /// per tick: that is a GUID resolve plus a SessionState lookup, and this is read once per recorder per
        /// game frame, forever.
        /// </summary>
        internal bool Armed { get; set; }

        // How many built inspectors are currently SHOWING this recorder. Maintained by DebugRunner across the
        // real attach/detach pair, so it counts trees that reached a panel rather than trees that were built.
        // Read only by PruneDead, to tell a recorder nothing is looking at from one a graph still holds.
        private int _viewers;

        /// <summary>
        /// Whether an inspector has EVER shown this recorder. Required alongside <see cref="_viewers"/>,
        /// because a brand-new recorder legitimately has zero viewers for a while: GetOrCreate mints it, and
        /// the graph that displays it does not attach until the inspector is built and put on a panel. Without
        /// this, the very next PruneDead — and GetOrCreate runs one on every miss — would evict a recorder
        /// that is seconds away from being used, and the tree that finished building would hold an orphan
        /// while the following lookup minted a second recorder for the same target.
        /// <para>Caught by TestLiveDebugRecorderMapEvictsDeadTargets, whose two rigs are created back to back:
        /// the second GetOrCreate pruned the first away before the test could look at it.</para>
        /// </summary>
        private bool _everViewed;

        /// <summary>An inspector showing this recorder attached. See <see cref="PruneDead"/>.</summary>
        internal void AddViewer() { _viewers++; _everViewed = true; }

        /// <summary>
        /// A build finished with this recorder without producing a single graph, so it will never gain a
        /// viewer. Marks it as SEEN — without incrementing the viewer count, because nothing is showing it —
        /// which is what lets <see cref="PruneDead"/> treat it as residue. See <see cref="_everViewed"/>.
        /// </summary>
        internal void MarkBuilt() { _everViewed = true; }

        /// <summary>
        /// Marks the recorder already held for <paramref name="so"/>'s target as built, WITHOUT minting one.
        /// The zero-graph exit from a debug-enabled build — see <see cref="MarkBuilt"/> — where calling
        /// <see cref="For"/> would create the very entry this is trying to make collectable.
        /// </summary>
        internal static void MarkBuiltFor(SerializedObject so)
        {
            var target = so != null ? so.targetObject : null;
            if (target == null) return;
            if (s_byTarget.TryGetValue(UnityObjectId.Of(target), out var rec)) rec.MarkBuilt();
        }

        /// <summary>An inspector showing this recorder was torn down for good.</summary>
        internal void RemoveViewer() { if (_viewers > 0) _viewers--; }

        /// <summary>
        /// How many objects the bound SerializedObject edits, and the name of the one actually sampled.
        ///
        /// <para><b>Only the first target is recorded, and that has to be visible.</b> Every channel on a
        /// recorder shares ONE sample clock — that shared instant is what makes "what else changed on this
        /// frame" a read rather than a guess — so a multi-selection cannot be folded into one trace without
        /// giving that up. Sampling the first target is therefore the right behaviour; sampling it while the
        /// strip looks like it describes the whole selection is not. The transport says which object it is.</para>
        /// </summary>
        internal int TargetCount { get; private set; }
        internal string TargetName { get; private set; }

        /// <summary>
        /// Cached copy of this type's chosen clock. Same reasoning as <see cref="Armed"/> — reading EditorPrefs
        /// sixty times a second to answer a question that changes twice a session is the wrong shape.
        /// </summary>
        internal DebugTickSource TickSource { get; private set; } = DebugTickSource.Update;

        /// <summary>Absolute index the playhead is parked at, or -1 while following live data.</summary>
        internal int ScrubAbsolute { get; private set; } = -1;

        /// <summary>
        /// Bumped every time the buffer is emptied for a new recording. The signal a reader uses to drop
        /// anything it derived from the previous trace — see <see cref="ClearSamples"/>.
        /// </summary>
        internal int Generation { get; private set; }

        /// <summary>Raised when the scrub position moves, so every graph on this target redraws together.</summary>
        internal event Action ScrubChanged;

        /// <summary>
        /// Property path of the row the jump-to-change buttons are scoped to, or null for "any field".
        ///
        /// <para><b>Why the jumps are LOCAL by default.</b> "Next recorded change" searched every channel at
        /// once, and on a real script several fields move on every single frame — so the answer to "is there a
        /// change after here?" was always "yes, next frame", and the button collapsed into a second Step
        /// Forward. Scoped to the row you last touched it answers the question you actually asked: <i>when did
        /// THIS field next move?</i> A field that changes once in a whole run is now one click away instead of
        /// four hundred.</para>
        ///
        /// <para>Held as a PATH rather than as a <see cref="DebugChannel"/> reference: the recorder outlives
        /// the inspector that built it, and a rebuild re-resolves channels — a held reference would pin a
        /// channel belonging to a torn-down build.</para>
        /// </summary>
        internal string FocusPath { get; private set; }

        /// <summary>Raised when <see cref="FocusPath"/> changes, so the transport can re-label its buttons.</summary>
        internal event Action FocusChanged;

        /// <summary>The focused channel, or null when nothing is focused (or the focused row has gone away).</summary>
        internal DebugChannel FocusChannel
        {
            get
            {
                if (string.IsNullOrEmpty(FocusPath)) return null;
                for (int i = 0; i < _channels.Count; i++)
                    if (_channels[i].Path == FocusPath) return _channels[i];
                return null;
            }
        }

        /// <summary>Scopes the jump buttons to one row. Pass null to go back to searching every field.</summary>
        internal void SetFocus(string path)
        {
            if (FocusPath == path) return;
            FocusPath = path;
            FocusChanged?.Invoke();
        }

        internal int Count => _count;
        internal int Absolute => _absolute;
        internal IReadOnlyList<DebugChannel> Channels => _channels;

        /// <summary>Absolute index of the oldest sample still in the ring.</summary>
        internal int OldestAbsolute => _absolute - _count;

        static DebugRecorder()
        {
            // Cleared when a NEW run starts, never when one ends — the trace you just made is precisely the thing
            // you want to read, and wiping it at the stop button threw it away at the moment it became useful.
            // (Pass 1 cleared on every transition AND on assembly reload, so it died twice over.)
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) ClearAll();
                // PERSIST AT THE STOP BUTTON, not only at the reload. "Exiting Play Mode IS a domain reload"
                // was the assumption this whole file was written on, and it is only true with Unity's DEFAULT
                // Enter Play Mode Options. Switch the domain reload off — this project has, and it is the first
                // thing anyone reaches for to make iteration fast — and beforeAssemblyReload simply never fires
                // on either play-mode edge. Nothing was ever written to the store, so the inspector that
                // rebuilt after the run restored an empty trace and said "no data recorded" about a recording
                // that had just finished. Capturing here costs one sweep at the stop button and is idempotent
                // with the reload hook, which still covers a mid-session recompile.
                else if (state == PlayModeStateChange.ExitingPlayMode) DebugTraceStore.PersistAll();
            };
            // A recompile mid-session wipes every static, so the buffers have to be handed to something that
            // outlives them. DebugTraceStore is a ScriptableSingleton, which is the one storage that does.
            AssemblyReloadEvents.beforeAssemblyReload += DebugTraceStore.PersistAll;
            // Evict recorders whose object is gone. hierarchyChanged is the cheap, occasional signal for
            // "something was created, destroyed, or a scene changed" — see PruneDead for why this is needed
            // at all rather than being handled by the map going out of scope.
            EditorApplication.hierarchyChanged += PruneDead;
            // The chosen clock is cached per recorder (see TickSource), so a change has to be pushed rather
            // than polled. Rare event, cheap sweep.
            DebugRecordSettings.Changed += RefreshTickSources;
            // Armed is DERIVED, never pushed in by a runner. Two inspectors can show one target (a locked
            // inspector beside the main one), and a per-runner push meant whichever one detached first switched
            // recording off underneath the other. Recomputing from the single source of truth cannot do that.
            DebugModeState.Changed += RefreshArmed;
        }

        private static void RefreshTickSources()
        {
            foreach (var kv in s_byTarget) kv.Value.CacheClock();
        }

        /// <summary>Re-reads this type's clock settings. EditorPrefs is far too slow to touch per game frame.</summary>
        private void CacheClock()
        {
            TickSource = DebugRecordSettings.TickSourceFor(TargetType);
            float rate = Mathf.Max(0.0001f, DebugRecordSettings.SampleRateFor(TargetType));
            _intervalSeconds = 1d / rate;
        }

        /// <summary>
        /// Re-derives every recorder's <see cref="Armed"/> flag. Two conditions, both required: Live Debug is
        /// switched on for the type AND its capture phase is Recording. Splitting those is the whole of stage 2
        /// — "I am watching this script" and "it is writing samples right now" stopped being the same fact
        /// the moment recording gained triggers.
        /// </summary>
        internal static void RefreshArmed()
        {
            foreach (var kv in s_byTarget)
                kv.Value.Armed = DebugCaptureController.IsRecording(kv.Value.TargetType);
        }

        /// <summary>
        /// Writes one sample into every armed recorder that asked for <paramref name="source"/>. Called from
        /// <see cref="DebugSampleClock"/>, which means EVERY GAME FRAME, forever, once anything has recorded.
        ///
        /// <para>Allocation-free by construction: the <c>foreach</c> is over the concrete
        /// <see cref="Dictionary{TKey,TValue}"/> field, so it binds the struct enumerator rather than boxing
        /// through <see cref="System.Collections.Generic.IEnumerable{T}"/>. Do not "tidy" this into a LINQ
        /// query or a call through <see cref="Live"/> — either one puts an allocation on a 60Hz path.</para>
        /// </summary>
        internal static void SampleAllArmed(DebugTickSource source)
        {
            // A trace is a recording of the GAME. The editor ticks a player loop of its own, so a system that
            // outlives its play session — or one spliced in while the editor was idle — writes sample after
            // sample of a script's unchanging serialized values and fills every ring with a flat line. The
            // clock is gated at DebugSampleClock.Install as well; this is the invariant restated where the
            // write actually happens, so no future caller can reintroduce it. One bool read per frame.
            if (!DebugSampleClock.InPlaySession) return;
            if (s_byTarget.Count == 0) return;
            foreach (var kv in s_byTarget)
            {
                var rec = kv.Value;
                if (!rec.Armed || rec.TickSource != source) continue;
                rec.Sample(source);
            }
        }

        /// <summary>The recorder for <paramref name="so"/>'s target, creating it on first use. Null for a null/destroyed target.</summary>
        internal static DebugRecorder For(SerializedObject so)
        {
            var target = so != null ? so.targetObject : null;
            if (target == null) return null;
            int key = UnityObjectId.Of(target);
            if (!s_byTarget.TryGetValue(key, out var rec))
            {
                // Sweep before growing, so the map self-cleans at the only moment it gets bigger. Cheap: it
                // runs once per newly inspected object, not per frame and not per rebuild.
                PruneDead();
                rec = new DebugRecorder();
                s_byTarget[key] = rec;
                // Adopt anything the previous domain left behind for this target, so the trace survives the
                // reload that exiting Play Mode performs.
                DebugTraceStore.Restore(target, rec);
            }
            // A rebuild hands us a fresh SerializedObject for the same target; the buffer survives, the handle
            // is replaced. Without this the recorder would keep sampling a SerializedObject nobody is bound to —
            // and every channel's CACHED property would still point into the dead one.
            if (!ReferenceEquals(rec._so, so))
            {
                rec._so = so;
                rec._soAlive = true;
                for (int i = 0; i < rec._channels.Count; i++) rec._channels[i].Rebind(so);
            }
            // Refreshed on every hand-off rather than only at creation: a restored recorder is adopted from the
            // store before it has ever seen a target, and the sampling clock filters on both of these.
            rec._target = target;
            rec.TargetType = target.GetType();
            var all = so.targetObjects;
            rec.TargetCount = all != null ? all.Length : 1;
            rec.TargetName = target.name;
            rec.CacheClock();
            rec.Armed = DebugCaptureController.IsRecording(rec.TargetType);
            return rec;
        }

        /// <summary>Every live recorder, for the store and the recording-count indicator.</summary>
        internal static IEnumerable<KeyValuePair<int, DebugRecorder>> Live => s_byTarget;

        /// <summary>
        /// The same map, typed CONCRETELY so a <c>foreach</c> binds the dictionary struct enumerator instead of
        /// boxing through IEnumerable. Use this on any path that runs per game frame — <see cref="Live"/> is for
        /// the occasional sweep where an allocation does not matter.
        /// </summary>
        internal static Dictionary<int, DebugRecorder> LiveMap => s_byTarget;

        /// <summary>
        /// Drops one target's recorder from the map.
        ///
        /// <para>Test seam. A suite that leaks recorders makes later tests ORDER-DEPENDENT: every leaked entry
        /// still answers to its type, so a per-type sweep finds strangers from three tests ago. That is not
        /// hypothetical — it is how the "second recording starts empty" assertion first failed.</para>
        /// </summary>
        /// <summary>
        /// Drops every recorder whose inspected object no longer exists.
        ///
        /// <para><b>Why the map cannot clean itself.</b> <see cref="s_byTarget"/> is keyed by
        /// <see cref="UnityObjectId"/>, and that id is <b>not stable across a scene reload</b> — the same
        /// component comes back with a different one. Nothing ever looks the old key up again, so the entry is
        /// unreachable from that moment on: not merely unused, but impossible to find, impossible to reuse and
        /// impossible to evict by any normal path. The only two removals in the whole type were
        /// <see cref="ClearAll"/> (entering Play Mode) and <see cref="Forget"/> (a test seam), so within one
        /// domain lifetime the map could only ever grow.</para>
        ///
        /// <para><b>Measured 2026-08-20:</b> four watched scripts, and all four entries in the map pointed at
        /// instance ids that no longer resolved to anything, while the live components had different ids. Each
        /// stale entry holds a <see cref="SerializedObject"/> handle plus its rings (~75 KB), stays
        /// <c>Armed</c>, and is walked by <see cref="SampleAllArmed"/> on every game frame — where it costs
        /// only a branch, because <see cref="Sample"/> bails on <c>_soAlive</c>, but costs it forever.</para>
        ///
        /// <para>Safe to discard: a destroyed object has no inspector to read its trace in, and a trace that
        /// must survive a reload goes through <see cref="DebugTraceStore"/>, which keys on scene path + name +
        /// type precisely because instance ids cannot be trusted across one.</para>
        /// </summary>
        internal static void PruneDead()
        {
            if (s_byTarget.Count == 0) return;

            List<int> dead = null;
            foreach (var kv in s_byTarget)
            {
                var recorder = kv.Value;

                // NOTHING AN INSPECTOR IS STILL SHOWING IS EVER COLLECTED — for EITHER reason below.
                //
                // This is not a third rule; it is the residue rule's own third bullet ("no viewers NOW"),
                // hoisted, because "who is still holding this" has nothing to do with WHY we were about to drop
                // it. It used to guard only the residue branch, and the gap was the bug: a live graph holds its
                // recorder BY REFERENCE, so evicting the map entry does not free it — it detaches the on-screen
                // rows from the sampler, which walks this map and nothing else. The inspector then goes on
                // looking armed, lit and counted by DebugRunner.RecordingCount while not one sample is ever
                // written to it again, and no rebuild is coming to notice.
                //
                // A DEAD TARGET IS NOT PERMISSION, and that is the case that shipped: leaving Play Mode restores
                // the scene, so for a couple of editor ticks EVERY inspected component is destroyed while the
                // inspector showing it is still on screen (traced live: `dead=True viewers=1` the instant
                // EnteredEditMode fires). The restored object OFTEN comes back under the same instance id —
                // measured both ways in one session, 188206 reused on one round trip and a fresh 233170 on the
                // next — and when it does, For() re-homes _target/_so and rebinds every channel rather than
                // minting a second recorder. That heal needs the entry to still be here. hierarchyChanged fires
                // constantly and runs this sweep, so whether the heal or the eviction got there first was luck,
                // which is what "sometimes it just doesn't record when I hit Play, and I can't find a pattern"
                // is made of.
                //
                // Not a leak, and bounded by something stronger than a timer: the exemption is "an inspector is
                // holding it", and an inspector cannot outlive its own teardown. PanelLifetime.OnRealDetach
                // drops the viewer — including for a tree that never reached a panel — and the very next sweep
                // collects it. The unbounded growth this method exists to stop (entries stranded by an instance
                // id that changed under a scene reload) has no viewers by definition: whatever inspector minted
                // one of those is long gone.
                if (recorder._viewers > 0) continue;

                // Unity's overloaded == : true for destroyed AND for unloaded, which are both "gone" here.
                bool targetGone = recorder._target == null;

                // DESELECT RESIDUE. Merely LOOKING at an object mints a recorder, and until now nothing ever
                // took it back: 13 entries / 656 KB after 65 selections, 9 of them pure residue with
                // armed=False and not a single sample. FOUR conditions, all required:
                //   · no samples — THE important one. Re-selecting an object to scrub the trace you just
                //     captured is the whole feature, and an evicted recorder comes back empty. A recorder
                //     with even one sample is never touched here, however idle it looks.
                //   · not armed — an armed recorder is writing, or about to.
                //   · no viewers NOW — a live graph holds its recorder by REFERENCE, so evicting the map entry
                //     would not free it; it would just make the next GetOrCreate mint a SECOND recorder for
                //     the same target, and the on-screen graph would quietly stop being the one recording.
                //   · but it HAS been viewed at least once. A freshly minted recorder has no viewer until the
                //     inspector showing it is built and attached, and GetOrCreate prunes on every miss — so
                //     without this the next selection would evict a recorder that is about to be used. See
                //     _everViewed.
                // An empty, unarmed, previously-watched-now-unwatched recorder holds nothing a fresh one would
                // not, so rebuilding it on the next selection costs a constructor and loses no state.
                //   · and NOT during a play session. Every recorder is emptied at the start of a run (see
                //     ClearAll), so for the first frames of Play Mode "no samples" describes every recorder in
                //     the map rather than a stale one — and evicting one there costs the trace the user pressed
                //     Play to get. Deselect residue is Edit Mode housekeeping; it can wait for Edit Mode.
                bool idleResidue = !targetGone && recorder._count == 0 && !recorder.Armed
                                   && recorder._viewers == 0 && recorder._everViewed
                                   && !DebugSampleClock.InPlaySession;

                // NOTE: "has no channels" is NOT a second, weaker eviction rule, however tempting it looks —
                // a graph cannot exist without a channel, so a channel-less recorder cannot be on screen. It
                // is still wrong: TWO For() calls can happen before EITHER registers a channel (an inspector
                // showing two components of one script does exactly that), and pruning on the second call
                // would drop the first object's brand-new recorder. _everViewed is the only safe signal, and
                // the zero-graph build now sets it through MarkBuilt — see LayoutTreeBuilder's debug block.

                if (!targetGone && !idleResidue) continue;
                if (dead == null) dead = new List<int>();
                dead.Add(kv.Key);
            }
            if (dead == null) return;   // the common case allocates nothing at all

            for (int i = 0; i < dead.Count; i++) s_byTarget.Remove(dead[i]);
        }

        internal static void Forget(UnityEngine.Object target)
        {
            if (target == null) return;
            s_byTarget.Remove(UnityObjectId.Of(target));
        }

        /// <summary>
        /// Is the inspected component enabled right now? Drives the OnEnable start trigger, which is an
        /// observed edge rather than a hook — nothing lets an editor subscribe to another object's OnEnable.
        /// Non-Behaviour targets (a ScriptableObject) count as always enabled, which is the only sane reading.
        /// </summary>
        internal bool TargetIsEnabled()
        {
            if (!_soAlive || _so == null) return false;
            UnityEngine.Object target;
            try { target = _so.targetObject; }
            catch (NullReferenceException) { _soAlive = false; return false; }
            if (target == null) return false;
            var behaviour = target as Behaviour;
            return behaviour == null || behaviour.isActiveAndEnabled;
        }

        /// <summary>
        /// Empties every buffer for a new run. Test seam, and the hook the play-mode handler uses.
        ///
        /// <para><b>The samples are cleared IN PLACE; the map keeps its entries.</b> A graph holds its recorder
        /// by REFERENCE, and entering Play Mode does not necessarily rebuild the inspector — with
        /// <c>Enter Play Mode Options</c> skipping the domain and scene reloads (this project's setting, and a
        /// common one to reach for) the whole UI tree survives the transition intact. Dropping the map there
        /// orphaned every graph on screen from the only recorder the sampler could see: they sat on
        /// "recording…" for the entire session, then said "no data recorded — press play" the instant Play was
        /// left, while a second invisible recorder held the trace nobody could reach. Recorders whose target
        /// really did die are collected by <see cref="PruneDead"/> on the next hierarchy change, which is where
        /// that job belongs.</para>
        /// </summary>
        internal static void ClearAll()
        {
            foreach (var kv in s_byTarget) kv.Value.ClearSamples();
            DebugTraceStore.ClearAll();
            DebugLogCapture.Clear();
        }

        /// <summary>Drops this recorder's samples but keeps its channels — the Clear button.</summary>
        internal void ClearSamples()
        {
            // A NEW RECORDING GETS A NEW NUMBER. Readers that cache something derived from the whole trace —
            // the graph's grow-only Y frame is the one that matters — cannot detect a reset from the sample
            // counters alone: Clear puts _absolute back to 0, so two samples later it reads exactly what it
            // read before, and a window fitted to a run that peaked at 1000 silently squashed the next
            // recording flat against the bottom of the well. A counter that only ever goes up cannot alias.
            Generation++;
            _count = 0;
            _writeSlot = 0;
            _absolute = 0;
            _lastFrame = -1;
            _hasRecordStart = false;
            _hasSampledSource = false;   // an empty recorder has no clock to have changed from
            _intervalDueAt = 0d;   // a fresh recording samples immediately, not one period late
            ScrubAbsolute = -1;
            for (int i = 0; i < _channels.Count; i++) _channels[i].ClearSamples();
            ScrubChanged?.Invoke();
        }

        /// <summary>
        /// Registers a field for recording, or returns the existing channel when a rebuild re-registers the same
        /// path. Returns null when the path can't be resolved, so a caller can simply skip the row.
        /// </summary>
        internal DebugChannel Register(string resolvedPath, string label, bool hasRange, float rangeMin, float rangeMax)
        {
            if (string.IsNullOrEmpty(resolvedPath) || _so == null) return null;
            if (_byPath.TryGetValue(resolvedPath, out var existing))
            {
                if (existing.IsBound) return existing;
                // Unbound means either the field is gone (rebuilding it fails the same way, harmlessly) or
                // DebugChannel.Reclassify refused it because the field changed SHAPE under a ring recorded in
                // the old shape. Either way the cached channel can never bind again, so keeping it would
                // freeze this row at "—" for the rest of the session.
                _byPath.Remove(resolvedPath);
                _channels.Remove(existing);
            }

            var prop = _so.FindProperty(resolvedPath);
            if (!DebugChannel.CanRecord(prop)) return null;

            var channel = new DebugChannel(resolvedPath, label, prop, Capacity, hasRange, rangeMin, rangeMax);
            _channels.Add(channel);
            _byPath[resolvedPath] = channel;
            return channel;
        }

        /// <summary>
        /// Appends exactly one sample to EVERY registered channel. The only writer — channels must never append
        /// on their own, or their indices would stop meaning the same instant.
        /// </summary>
        internal void Sample(DebugTickSource source = DebugTickSource.Update)
        {
            if (!_soAlive || _so == null || _channels.Count == 0) return;

            // A SerializedObject whose owning inspector was torn down has had its NATIVE side freed while the
            // managed wrapper lives on, so `_so != null` is true and the very next member access throws. There
            // is no public "is this still valid" query — the first touch is how you find out — and this matters
            // now in a way it did not before: sampling used to ride the inspector's own scheduled tick, which
            // could not outlive the inspector. Driven from the player loop it sweeps EVERY recorder, including
            // ones whose editor is long gone (an edit-mode target still in the map after the scene reloaded
            // into Play Mode is the common case, and without this it throws sixty times a second forever).
            UnityEngine.Object target;
            try { target = _so.targetObject; }
            catch (NullReferenceException) { _soAlive = false; return; }
            if (target == null) return;

            // The sample is taken from inside the player loop, so the SerializedObject has not necessarily been
            // pumped by the inspector's binding yet this frame — without this the ring records last frame's
            // values on this frame's number. Guarded on hasModifiedProperties because Update() discards
            // unapplied edits, and someone mid-drag on a slider must not have it snatched back.
            if (!_so.hasModifiedProperties) _so.UpdateIfRequiredOrScript();

            // A buffer holding both Update samples and FixedUpdate samples is not one trace, it is two traces
            // interleaved on an axis that means different things in different places — and nothing on screen
            // could tell you which. Switching clocks therefore starts a new recording rather than continuing a
            // dishonest one. Done here, not at the settings call site, so it holds no matter what flips it.
            // `_hasSampledSource`, not the field's initial value. `_sampledSource` starts at Update, so a trace
            // recorded on FixedUpdate, carried across a reload and RESTORED into a fresh recorder looked like a
            // clock change on its very next sample — and this line then threw the whole restored trace away,
            // which is the one thing the persistence exists to prevent. A recorder that has not sampled yet has
            // no clock to have changed FROM; it adopts whichever one writes to it first.
            if (_count > 0 && _hasSampledSource && _sampledSource != source) ClearSamples();
            _sampledSource = source;
            _hasSampledSource = true;

            double now = EditorApplication.timeSinceStartup;

            // The Interval clock is POLLED every frame but only WRITES when its period has elapsed. Rescheduled
            // from `now` rather than by adding the period to the previous due time: after a domain reload or a
            // long editor stall the accumulated debt would otherwise fire a burst of back-to-back samples for
            // instants that never happened in the game.
            if (source == DebugTickSource.Interval)
            {
                if (now < _intervalDueAt) return;
                _intervalDueAt = now + _intervalSeconds;
            }

            int frame = Time.frameCount;

            // ONE sample per GAME frame — but ONLY on the two clocks where "frame" is actually the unit.
            //
            // Update and LateUpdate fire once per rendered frame, so a second sample on the same frame can only
            // be a duplicate, and deduping is what keeps the ring meaningful.
            //
            // The other two are the opposite case, and both were found the hard way:
            //   * FixedUpdate — a physics step is a real, distinct instant that legitimately shares a
            //     Time.frameCount with its neighbours (measured: up to SIX per rendered frame). Deduping threw
            //     away five of every six, which is precisely the data a physics investigation came for.
            //   * Manual — the USER is the clock. Three deliberate SasquatchDebug.Tick() calls in one frame
            //     produced one sample, silently discarding two explicit requests. On a clock whose whole
            //     premise is "nothing happens until you say so", dropping what you said is the worst possible
            //     behaviour.
            //   * Interval — its period has no relationship to the frame at all, so "one per frame" is simply
            //     not its unit, and enforcing that would drop real samples at rates above the frame rate.
            bool dedupeByFrame = source == DebugTickSource.Update || source == DebugTickSource.LateUpdate;
            if (dedupeByFrame)
            {
                if (frame == _lastFrame) return;
                _lastFrame = frame;
            }

            // Same clock DebugLogCapture stamps on — the moment card correlates the two, so they must not drift
            // onto different time bases.
            if (!_hasRecordStart) { _recordStart = now; _hasRecordStart = true; }

            // Ordering guard for AbsoluteNearestTime's binary search — see _timeSorted. Compared against the
            // newest sample currently held, which is the one written on the previous tick.
            if (_count > 0 && now < _time[SlotOf(_absolute - 1)]) _timeSorted = false;

            _time[_writeSlot] = now;
            _frame[_writeSlot] = frame;

            for (int i = 0; i < _channels.Count; i++)
            {
                // A channel can lose its property mid-session (the user edited the script while playing). It
                // writes nothing and holds its last value; the other channels stay index-aligned, which is what
                // the whole co-change readout depends on.
                _channels[i].Write(_writeSlot, _absolute);
            }

            _writeSlot = (_writeSlot + 1) % Capacity;
            _absolute++;
            if (_count < Capacity) _count++;

            int oldest = OldestAbsolute;
            for (int i = 0; i < _channels.Count; i++) _channels[i].TrimChanges(oldest);

            // The ring has just moved; a playhead parked in the past may no longer be inside it.
            ReclampScrub();
        }

        /// <summary>
        /// Drags a parked playhead forward with the window it is falling out of.
        ///
        /// <para>Nothing re-validated <see cref="ScrubAbsolute"/> once it was set, so a trace left parked while
        /// recording continued kept an index the ring had long since overwritten: <see cref="SlotOf"/> returned
        /// -1, every readout showed "—", and the transport printed <c>sample -345 of 1200</c> from the same
        /// arithmetic. Clamped to the oldest sample still held rather than released, because the user asked to
        /// look at the past — the honest answer is "the oldest thing I still have", not a silent jump back to
        /// live.</para>
        /// </summary>
        private void ReclampScrub()
        {
            if (ScrubAbsolute < 0) return;
            if (_count == 0) { ScrubAbsolute = -1; ScrubChanged?.Invoke(); return; }

            int oldest = OldestAbsolute;
            int newest = _absolute - 1;
            int clamped = Mathf.Clamp(ScrubAbsolute, oldest, newest);
            if (clamped == ScrubAbsolute) return;
            ScrubAbsolute = clamped;
            ScrubChanged?.Invoke();
        }

        /// <summary>The game frame an absolute sample came from, or 0 when it is no longer held.</summary>
        internal int FrameOf(int absolute)
        {
            int slot = SlotOf(absolute);
            return slot < 0 ? 0 : _frame[slot];
        }

        // ---- persistence across the domain reload ------------------------------------------------------

        /// <summary>
        /// Flattens the tail of the ring into a store blob. The ring is unrolled oldest-first so the restored
        /// copy is a plain array — the wrap offset is meaningless once the domain it belonged to is gone.
        /// </summary>
        internal object Capture()
        {
            if (!_soAlive || _so == null || _count == 0) return null;

            // The same latch Sample() carries, for the same reason and with more at stake. A SerializedObject
            // whose inspector was torn down has had its NATIVE side freed while the managed wrapper lives on,
            // so `_so != null` is true and `.targetObject` THROWS rather than returning null. Capture runs
            // from beforeAssemblyReload across EVERY recorder in the map, so it meets dead handles routinely —
            // select A, record, select B is enough — and this was the one member of this class that neither
            // latched nor caught. See memory/serializedobject-outlives-its-inspector.md.
            UnityEngine.Object target;
            try { target = _so.targetObject; }
            catch (NullReferenceException) { _soAlive = false; return null; }
            if (target == null) return null;

            string key = DebugTraceStore.TraceKey(target);
            if (string.IsNullOrEmpty(key)) return null;

            int keep = Mathf.Min(_count, DebugTraceStore.PersistedSamples);
            int firstAbs = _absolute - keep;

            var times = new double[keep];
            var frames = new int[keep];
            for (int i = 0; i < keep; i++)
            {
                int slot = SlotOf(firstAbs + i);
                times[i] = slot < 0 ? 0d : _time[slot];
                frames[i] = slot < 0 ? 0 : _frame[slot];
            }

            var trace = DebugTraceStore.NewTrace(key, keep, _absolute, times, frames);
            for (int c = 0; c < _channels.Count; c++) _channels[c].Capture(this, trace, firstAbs, keep);
            return trace;
        }

        /// <summary>Repopulates this recorder from a stored blob. Channels are recreated bound to nothing until <see cref="For"/> rebinds them.</summary>
        internal void Adopt(object trace)
        {
            int keep, absolute, channelCount;
            double[] times;
            int[] frames;
            DebugTraceStore.ReadTrace(trace, out keep, out absolute, out times, out frames, out channelCount);
            if (keep <= 0) return;

            _count = Mathf.Min(keep, Capacity);
            _absolute = absolute;
            _writeSlot = _absolute % Capacity;
            _lastFrame = -1;
            // The blob does not carry which clock recorded it, and guessing would be worse than not knowing:
            // guessing wrong makes Sample() read a clock CHANGE and discard the trace it has just restored.
            // Unset means the next writer's clock is simply adopted. See _hasSampledSource.
            _hasSampledSource = false;
            // A restored trace is the TAIL of a recording — the head was dropped by the persistence cap — so the
            // honest zero point is where the surviving window begins. timeSinceStartup is measured from editor
            // launch, not from the domain, so these stamps stay comparable across the reload.
            _recordStart = times.Length > 0 ? times[0] : 0d;
            _hasRecordStart = times.Length > 0;

            // A restored blob is the one source of times this class did not stamp itself, so its ordering is
            // established here rather than assumed. One pass, once per restore. See _timeSorted.
            _timeSorted = true;
            double previousTime = double.NegativeInfinity;
            for (int i = 0; i < _count; i++)
            {
                int slot = SlotOf(_absolute - _count + i);
                if (slot < 0) continue;
                if (times[i] < previousTime) _timeSorted = false;
                previousTime = times[i];
                _time[slot] = times[i];
                _frame[slot] = frames[i];
            }

            for (int c = 0; c < channelCount; c++)
            {
                string path; byte[] values; int series; int[] marks; string[] text;
                bool ever; float lo, hi;
                DebugTraceStore.ReadChannel(trace, c, out path, out values, out series,
                    out marks, out text, out ever, out lo, out hi);
                var ch = DebugChannel.FromStore(path, values, series, marks, text, ever, lo, hi, Capacity, this);
                if (ch == null) continue;
                // REPLACE, never append. Two channels answering to one path would both be written on every
                // tick, both drawn, and both exported — and _byPath would silently name only the second, so the
                // first became an invisible passenger that nothing could ever remove. Unreachable in today's
                // flow (Adopt is only called on a channel-free recorder two lines old), which is exactly why it
                // needs saying here rather than relying on the caller to keep being careful.
                if (_byPath.TryGetValue(path, out var existing)) _channels.Remove(existing);
                _channels.Add(ch);
                _byPath[path] = ch;
            }
        }

        /// <summary>Ring slot holding absolute sample <paramref name="absolute"/>, or -1 when it has scrolled away.</summary>
        internal int SlotOf(int absolute)
        {
            if (absolute < OldestAbsolute || absolute >= _absolute) return -1;
            return ((absolute % Capacity) + Capacity) % Capacity;
        }

        /// <summary>Ring slot for the <paramref name="i"/>-th oldest sample currently held (0 = oldest).</summary>
        internal int SlotAtOffset(int i) => SlotOf(OldestAbsolute + i);

        /// <summary>Editor timestamp of an absolute sample, or 0 when it is no longer held.</summary>
        internal double TimeOf(int absolute)
        {
            int slot = SlotOf(absolute);
            return slot < 0 ? 0d : _time[slot];
        }

        /// <summary>
        /// Seconds from the START OF THIS RECORDING to an absolute sample — the number every "t+" readout
        /// shows. Anchored on <see cref="_recordStart"/> rather than on the oldest surviving sample, so it
        /// keeps one meaning for a whole run instead of quietly re-basing when the ring wraps.
        /// </summary>
        internal double ElapsedAt(int absolute)
        {
            int slot = SlotOf(absolute);
            return slot < 0 || !_hasRecordStart ? 0d : _time[slot] - _recordStart;
        }

        /// <summary>Seconds spanned by the samples currently in the ring.</summary>
        internal double WindowSeconds =>
            _count < 2 ? 0d : TimeOf(_absolute - 1) - TimeOf(OldestAbsolute);

        /// <summary>
        /// Parks the shared playhead at an absolute sample, or -1 to follow live data again. Every graph on this
        /// target reads the same value, which is what makes scrubbing one row move all of them.
        /// </summary>
        internal void SetScrub(int absolute)
        {
            // AN EMPTY TRACE HAS NOTHING TO PARK ON, so parking is refused rather than clamped. The old
            // Max(OldestAbsolute, _absolute - 1) turned an empty window into the single index 0 — an absolute
            // whose SlotOf is -1 — so the playhead read as parked while every readout resolved to nothing, the
            // well accepted a drag that did nothing visible, and the transport's "sample N of 1200" counted
            // from a sample that was never recorded. Released is the truthful state.
            int clamped;
            if (absolute < 0 || _count == 0) clamped = -1;
            else clamped = Mathf.Clamp(absolute, OldestAbsolute, _absolute - 1);
            if (clamped == ScrubAbsolute) return;
            ScrubAbsolute = clamped;
            ScrubChanged?.Invoke();
        }

        /// <summary>The absolute index the readouts should display: the parked playhead, or the newest sample.</summary>
        internal int ReadoutAbsolute => ScrubAbsolute >= 0 ? ScrubAbsolute : _absolute - 1;

        /// <summary>
        /// Nearest recorded change to <paramref name="absolute"/> across ALL channels, or -1 when the trace
        /// holds none. This is what makes the timeline navigable: a value that moves on 4 frames out of 1,200
        /// is unreachable by dragging (~4 samples per pixel), so every way in has to be "take me to a change"
        /// rather than "take me to a position".
        /// </summary>
        /// <param name="direction">-1 = strictly earlier, +1 = strictly later, 0 = nearest either way.</param>
        /// <param name="channel">Restrict to one channel's changes; null searches all of them.</param>
        internal int NearestChange(int absolute, int direction, DebugChannel channel = null)
        {
            int best = -1;
            int bestDist = int.MaxValue;

            for (int i = 0; i < _channels.Count; i++)
            {
                var ch = _channels[i];
                if (channel != null && ch != channel) continue;
                var marks = ch.ChangeSamples;
                for (int m = 0; m < marks.Count; m++)
                {
                    int at = marks[m];
                    if (at < OldestAbsolute || at >= _absolute) continue;
                    if (direction < 0 && at >= absolute) continue;
                    if (direction > 0 && at <= absolute) continue;
                    int d = at > absolute ? at - absolute : absolute - at;
                    if (d < bestDist) { bestDist = d; best = at; }
                }
            }
            return best;
        }

        /// <summary>
        /// The held sample closest in time to <paramref name="time"/>, or -1 when the trace is empty. Used to
        /// place a console log — which is stamped on the editor clock, not on a sample index — onto the timeline.
        /// </summary>
        internal int AbsoluteNearestTime(double time)
        {
            if (_count == 0) return -1;

            // Called once per log pin per paint — 256 pins x 44 graphs on a busy fixture, where the linear
            // scan below cost 4.01us a call and ~47ms a pass. The times are non-decreasing (see _timeSorted),
            // so the answer is reachable in O(log n) instead of O(1200).
            //
            // THE TIE-BREAK IS THE WHOLE DIFFICULTY. The scan tests `d < bestDelta`, strictly, so among
            // several samples that are equally close it keeps the LOWEST index — and equal timestamps are
            // ordinary here, not pathological: several samples land on one editor tick. So the search picks
            // the nearer of the two straddling values, preferring the EARLIER one when they tie, and then
            // returns LowerBound of that value, which is by definition its first occurrence. Anything less
            // careful moves a pin to a different sample in exactly the case that looks like a rounding
            // difference and is actually a different frame.
            if (_timeSorted)
            {
                int lo = LowerBoundTime(time);
                int bestIndex;
                if (lo <= 0)
                {
                    // Every sample is at or after `time`; the oldest is nearest, and it is its own first
                    // occurrence.
                    bestIndex = 0;
                }
                else if (lo >= _count)
                {
                    bestIndex = LowerBoundTime(TimeAtOffset(_count - 1));
                }
                else
                {
                    double previous = TimeAtOffset(lo - 1);
                    double current = TimeAtOffset(lo);
                    // `<=` keeps the earlier sample on a tie, matching the scan's strict `<`.
                    bestIndex = LowerBoundTime(time - previous <= current - time ? previous : current);
                }
                return OldestAbsolute + bestIndex;
            }

            int best = -1;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                int abs = OldestAbsolute + i;
                int slot = SlotOf(abs);
                if (slot < 0) continue;
                double d = _time[slot] > time ? _time[slot] - time : time - _time[slot];
                if (d < bestDelta) { bestDelta = d; best = abs; }
            }
            return best;
        }

        /// <summary>Timestamp of the <paramref name="offset"/>-th oldest sample held (0 = oldest).</summary>
        private double TimeAtOffset(int offset) => _time[SlotOf(OldestAbsolute + offset)];

        /// <summary>
        /// Offset of the FIRST sample whose timestamp is >= <paramref name="time"/>, or <c>_count</c> when
        /// none is. First-of-equals is the point: it is what reproduces the linear scan's lowest-index-wins
        /// tie-break, so the loop must never stop early on a match.
        /// </summary>
        private int LowerBoundTime(double time)
        {
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (TimeAtOffset(mid) < time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Is there any recorded change strictly after <paramref name="absolute"/>? O(channels) — and that
        /// matters, because it answers "should the jump-to-next-change button be live?" on a readout that
        /// refreshes up to sixty times a second, forever.
        ///
        /// <para>Constant-time per channel because <see cref="DebugChannel.ChangeSamples"/> is append-only and
        /// therefore sorted: the last entry is the latest change there is. The obvious implementation —
        /// calling <see cref="NearestChange"/> and testing the result — walks every mark in every channel
        /// instead, which on a real trace measured ~3,000 entries per pass, twice per tick, for a boolean.</para>
        /// </summary>
        /// <param name="channel">Restrict to one channel; null asks the same question of every channel.</param>
        internal bool HasChangeAfter(int absolute, DebugChannel channel = null)
        {
            for (int i = 0; i < _channels.Count; i++)
            {
                if (channel != null && _channels[i] != channel) continue;
                var marks = _channels[i].ChangeSamples;
                if (marks.Count == 0) continue;
                int last = marks[marks.Count - 1];
                if (last > absolute && last < _absolute) return true;
            }
            return false;
        }

        /// <summary>Does the trace hold any recorded change at all? Same O(channels) reasoning as <see cref="HasChangeAfter"/>.</summary>
        internal bool HasAnyChange(DebugChannel channel = null)
        {
            // ONLY MARKS INSIDE THE LIVE WINDOW COUNT, because this lights a button whose whole job is to jump
            // to one — and NearestChange, which is what the button actually calls, skips anything outside it.
            // TrimChanges deliberately retains the last aged-out entry so an Opaque readout is never blank, and
            // that retained mark was enough to make the jump-to-change button light up and then do nothing when
            // pressed. A control that cannot act must not look like it can.
            int oldest = OldestAbsolute;
            for (int i = 0; i < _channels.Count; i++)
            {
                if (channel != null && _channels[i] != channel) continue;
                var marks = _channels[i].ChangeSamples;
                for (int m = 0; m < marks.Count; m++)
                {
                    int at = marks[m];
                    if (at >= oldest && at < _absolute) return true;
                }
            }
            return false;
        }

        /// <summary>True when at least one channel changed exactly at <paramref name="absolute"/>.</summary>
        internal bool HasChangeAt(int absolute)
        {
            for (int i = 0; i < _channels.Count; i++)
                if (_channels[i].ChangeSamples.Contains(absolute)) return true;
            return false;
        }

        /// <summary>
        /// Every channel whose value differs between <paramref name="absolute"/> and the sample before it.
        /// The co-change readout — free, because the shared clock already aligned them.
        /// </summary>
        internal List<(DebugChannel Channel, string From, string To)> ChangesAt(int absolute)
        {
            var result = new List<(DebugChannel, string, string)>();
            int slot = SlotOf(absolute);
            int prevSlot = SlotOf(absolute - 1);
            if (slot < 0 || prevSlot < 0) return result;

            for (int i = 0; i < _channels.Count; i++)
            {
                var ch = _channels[i];

                // Ask the CHANGE LOG, not the formatted text. Format() renders through Trim()'s "0.##", so a
                // real 1.001 → 1.002 move rendered identically on both sides and the card reported "nothing
                // changed" on a frame the graph had explicitly marked — the two halves of the feature
                // contradicting each other, which is worse than either being wrong alone. ChangeSamples is
                // the very list the markers are drawn from, so consulting it makes them agree by
                // construction rather than by coincidence.
                if (!ch.ChangedAt(absolute)) continue;

                // A change too small for two decimals still has to READ as a change — but only the digits that
                // carry it, and only on the components that moved. See DebugChannel.FormatPair.
                ch.FormatPair(prevSlot, slot, absolute - 1, absolute, out string before, out string now);
                result.Add((ch, before, now));
            }
            return result;
        }

        /// <summary>
        /// For a VECTOR channel, the individual components that moved into <paramref name="absolute"/> — the
        /// per-axis form the moment card lists instead of one long "x, y, z → x, y, z" string. Empty for any
        /// other kind, and whenever either sample is outside the ring.
        /// </summary>
        internal List<(string Axis, string Before, string Now)> ComponentChangesAt(DebugChannel channel, int absolute)
        {
            var empty = new List<(string, string, string)>();
            if (channel == null) return empty;
            int slot = SlotOf(absolute);
            int prevSlot = SlotOf(absolute - 1);
            if (slot < 0 || prevSlot < 0) return empty;
            return channel.ChangedComponents(prevSlot, slot);
        }
    }
}
