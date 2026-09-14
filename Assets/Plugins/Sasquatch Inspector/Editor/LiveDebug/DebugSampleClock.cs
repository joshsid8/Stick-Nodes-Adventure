using System;
using UnityEditor;
using UnityEngine.LowLevel;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The clock Live Debug records on: three callbacks inserted into the GAME's player loop, not the editor's
    /// update loop.
    ///
    /// <para><b>Why this exists, and why it is the spine of the feature.</b> Pass 2 sampled from
    /// <c>root.schedule.Execute(...).Every(16)</c>, which rides the EDITOR update loop. That loop runs at a
    /// rate the editor picks — roughly 10 to 57 times a second depending on what else it is doing — and it has
    /// no relationship to the game's frame rate. So two consecutive samples were anywhere from one to five game
    /// frames apart. Three things followed from that one choice, and all three were reported as separate bugs:
    /// stepping "one frame" jumped the frame counter by four; the X axis was labelled time but was really
    /// "editor repaints"; and there was no honest way to offer a tick-rate choice, because the thing doing the
    /// ticking was not a clock the game shared.
    /// </para>
    ///
    /// <para><b>Public API only.</b> <see cref="PlayerLoop"/>, <see cref="PlayerLoopSystem"/> and the
    /// <c>UnityEngine.PlayerLoop.*</c> marker structs are shipped runtime API. Nothing here names, casts to, or
    /// reflects over an editor internal — Asset Store rule 2.5.g.</para>
    ///
    /// <para><b>THE EDITOR TICKS A PLAYER LOOP TOO.</b> This class used to say "the loop only runs in Play
    /// Mode, and this installs and removes itself around it", and every gate in it was written on that
    /// sentence. It is false. Systems spliced in during Edit Mode really are called, so Live Debug quietly
    /// recorded the EDITOR: a full ring of each watched script's own serialized values, never changing, filled
    /// the moment any inspector with the toggle on was built. That is the "open the layout designer, close it,
    /// and every graph is a flat blue line" report. Worse, the <see cref="Installed"/> flag it left behind then
    /// made the following Play Mode session record nothing at all — see <see cref="Install"/>. Play Mode is now
    /// a state this class TRACKS (<see cref="InPlaySession"/>), never one it infers from the loop existing.</para>
    ///
    /// <para><b>Install/remove, never reset.</b> Uninstall walks the tree and drops only our own systems rather
    /// than calling <c>SetPlayerLoop(GetDefaultPlayerLoop())</c> — that shortcut would silently delete every
    /// OTHER package's insertions too, which is a spectacular way to break someone's project from a debug
    /// tool.</para>
    /// </summary>
    internal static class DebugSampleClock
    {
        // Marker types. A PlayerLoopSystem is identified by its `type`, so these exist purely to be findable
        // again at uninstall — they are never instantiated.
        private struct SasquatchDebugInit { }
        private struct SasquatchDebugUpdate { }
        private struct SasquatchDebugLateUpdate { }
        private struct SasquatchDebugFixedUpdate { }

        /// <summary>True while our systems are in the player loop.</summary>
        internal static bool Installed { get; private set; }

        /// <summary>
        /// True from just before the first scene loads until Play Mode is left — the window in which sampling
        /// is meaningful.
        ///
        /// <para><b>Not <see cref="EditorApplication.isPlaying"/>.</b> Unity still reports false at the runtime
        /// prime hook, and refusing there is precisely how frame 1 got lost. This latch is set by that hook, so
        /// it is already true when the very first sample is taken and false everywhere in Edit Mode.</para>
        ///
        /// <para><b>Why a gate is needed at all.</b> <see cref="Install"/> is called from every armed inspector
        /// build (<c>DebugRunner</c>), and an inspector is built constantly in Edit Mode. Without this the
        /// systems went into the EDIT MODE player loop — which does tick — and every watched script recorded a
        /// full ring of its own serialized values, unchanging, forever: the "all my graphs are flat blue lines"
        /// report. It then poisoned Play Mode as well; see <see cref="OnPlayStarting"/>.</para>
        /// </summary>
        internal static bool InPlaySession { get; private set; }

        /// <summary>
        /// The one type the Showcase Sandbox is driving with its own demo clock, or null (the normal case).
        ///
        /// <para><b>This is a CHROME seam, never a sampling one.</b> <see cref="InPlaySession"/> stays the sole
        /// authority over whether <see cref="Install"/> splices systems in and whether
        /// <c>DebugRecorder.SampleAllArmed</c> writes — the sandbox does neither. It calls the per-recorder
        /// <c>DebugRecorder.Sample</c> directly, on ITS recorder only, from an editor tick it owns. Widening
        /// <see cref="InPlaySession"/> instead would put every recorder in the editor back on the edit-mode
        /// player loop, which is exactly the "all my graphs are flat blue lines" bug that gate exists to
        /// prevent.</para>
        ///
        /// <para>What it buys: the transport bar and the graph ghost text ask
        /// <see cref="ClockRunningFor"/> rather than <see cref="EditorApplication.isPlaying"/>, so the sandbox
        /// shows a working Record button and a live readout instead of "press play to record" while samples
        /// are visibly arriving. Set and cleared by <c>ShowcaseLiveDebugDriver</c>; there is no sandbox in an
        /// exported editor, which is why the kit's twin of this helper has no second term.</para>
        /// </summary>
        internal static Type SandboxType;

        /// <summary>
        /// Is something actually WRITING samples for <paramref name="type"/> right now? The question the debug
        /// chrome means every time it used to ask <see cref="EditorApplication.isPlaying"/>.
        /// </summary>
        internal static bool ClockRunningFor(Type type)
            => EditorApplication.isPlaying || (SandboxType != null && SandboxType == type);

        /// <summary>
        /// True when at least one anchor could not be found, so one of the tick sources is not really running.
        /// The transport says so out loud rather than quietly recording on a worse clock — a degraded mode you
        /// cannot see is the same thing as a lie.
        /// </summary>
        internal static bool Degraded { get; private set; }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // BEFORE the first scene loads, which is earlier than any editor callback fires. EnteredPlayMode
            // does not arrive until frame 1 has already been processed, so installing there begins recording on
            // frame 2 — and "the timeline will not go back to frame 1" is what that looks like to a user.
            Runtime.SasquatchDebug.PlayStarting += OnPlayStarting;
            // Delegates held by the player loop point INTO this assembly. Leaving them there across a reload
            // hands the loop a dead method pointer, so they come out first.
            AssemblyReloadEvents.beforeAssemblyReload += Uninstall;
            // A reload while already playing (a script edit mid-session) re-runs this method with the loop
            // already stripped, so re-arm immediately instead of waiting for a play-mode transition that has
            // already happened.
            if (EditorApplication.isPlaying)
            {
                InPlaySession = true;
                EditorApplication.delayCall += Install;
            }
        }

        /// <summary>
        /// A play session is starting, before the first scene loads.
        ///
        /// <para>Unity REPLACES the player loop on both Play Mode edges, so nothing spliced in earlier is still
        /// there by the time this runs. <see cref="Install"/> settles that by reading the loop rather than a
        /// flag — see the note on its first lines.</para>
        /// </summary>
        private static void OnPlayStarting()
        {
            InPlaySession = true;
            Install();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Belt and braces for a session entered with the Runtime assembly somehow unhooked. Install is
                // idempotent against the LIVE loop, so calling it twice in one session cannot double-splice.
                InPlaySession = true;
                Install();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                InPlaySession = false;
                Uninstall();
            }
        }

        /// <summary>
        /// Adds the three sampling systems. Idempotent, and cheap enough to call from any arming path — the
        /// callbacks themselves early-out when nothing is recording, so being installed costs one dictionary
        /// count check per game frame with the feature switched off.
        /// </summary>
        internal static void Install()
        {
            // EDIT MODE RECORDS NOTHING, and this is the line that enforces it. The editor ticks a player loop
            // too, so systems spliced in outside a play session really do run — every watched script filled its
            // whole ring with its own unchanging serialized values, which is the "open the designer, close it,
            // every graph is a flat blue line" report. DebugRunner calls this from every armed build and an
            // inspector is built constantly, so the gate belongs here rather than at each call site.
            //
            // InPlaySession, NOT EditorApplication.isPlaying: the runtime prime hook fires while Unity still
            // reports false, and refusing there is precisely how frame 1 got lost.
            if (!InPlaySession) return;

            var loop = PlayerLoop.GetCurrentPlayerLoop();

            // ASK THE LOOP, NOT THE FLAG. Unity replaces the player loop on both Play Mode edges, and with
            // `Enter Play Mode Options` set to skip the domain reload — this project does, and buyers will —
            // every static survives that replacement. A cached bool could therefore claim systems Unity had
            // already discarded (the session records nothing at all) or, once cleared, allow a second copy to
            // be spliced beside a live one (everything records twice). The loop itself cannot be stale.
            if (ContainsOurs(ref loop)) { Installed = true; return; }

            // Initialization is the FIRST thing Unity runs in a frame, before FixedUpdate, before Start, before
            // Update. Two things depend on being there:
            //   * "On Awake" means something. At this point Awake and OnEnable have run and nothing else has,
            //     so the first sample really is the state Awake left behind.
            //   * Frame 1 stops being unreachable. The Update anchor alone could not capture it (see Prime).
            bool z = InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.Initialization),
                typeof(SasquatchDebugInit), TickInit);

            bool a = InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                typeof(SasquatchDebugUpdate), TickUpdate);
            bool b = InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate),
                typeof(SasquatchDebugLateUpdate), TickLateUpdate);
            bool c = InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
                typeof(SasquatchDebugFixedUpdate), TickFixedUpdate);

            if (!a && !b && !c && !z)
            {
                Degraded = true;
                return;
            }

            PlayerLoop.SetPlayerLoop(loop);
            Installed = true;
            Degraded = !(a && b && c && z);
        }

        /// <summary>Removes only our own systems, leaving every other insertion in the loop untouched.</summary>
        internal static void Uninstall()
        {
            Installed = false;
            Degraded = false;

            // Swept unconditionally rather than behind the flag, for the same reason Install reads the loop:
            // the flag can be false while systems are genuinely still spliced in (a static reset by a domain
            // reload that the loop did not follow). RemoveOurs already no-ops when it finds nothing, so the
            // only cost of asking is one walk of a few hundred nodes on a path that runs at reloads.
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (RemoveOurs(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        // ---- the three callbacks -----------------------------------------------------------------------

        // Each is one static call that returns immediately when no recorder wants that clock. This runs every
        // game frame forever once installed, so the early-out has to be the FIRST thing, not the first thing
        // after a foreach — see DebugRecorder.SampleAllArmed, which iterates with the dictionary's own struct
        // enumerator precisely so a 60Hz sweep allocates nothing.
        //
        // THE ORDER OF THESE THREE LINES IS THE FEATURE, not an implementation detail:
        //
        //   starts  ->  sample  ...  sample  ->  stops
        //
        // Starts run BEFORE the frame is sampled, so a recording that begins on frame N contains frame N.
        // Stops run at the LateUpdate point, after every clock has written, so whatever ENDED the recording is
        // itself inside it. That second one is what makes the dashcam work: an error logged from a user's
        // Update would otherwise stop the trace one sample short of the frame that failed — discarding the
        // single most important frame in the whole capture.
        /// <summary>
        /// Frame start. Runs the OnAwake start trigger and the fixed-rate clock, both of which want to be as
        /// early in the frame as possible.
        /// </summary>
        private static void TickInit()
        {
            DebugCaptureController.EvaluateInitStarts();
            DebugRecorder.SampleAllArmed(DebugTickSource.Interval);
        }

        private static void TickUpdate()
        {
            // Polled from inside the player loop rather than from an editor callback, because that is the only
            // place a key press reaches while the GAME VIEW has focus — which is where you are looking when you
            // want to hit record. Compiles away entirely without the Input System package.
            DebugShortcuts.PollInputSystem();
            DebugCaptureController.EvaluateStarts();
            DebugRecorder.SampleAllArmed(DebugTickSource.Update);
        }

        private static void TickLateUpdate()
        {
            DebugRecorder.SampleAllArmed(DebugTickSource.LateUpdate);
            DebugCaptureController.EvaluateStops();
        }

        private static void TickFixedUpdate() => DebugRecorder.SampleAllArmed(DebugTickSource.FixedUpdate);

        // ---- player-loop surgery -----------------------------------------------------------------------

        /// <summary>
        /// Splices <paramref name="ownType"/> in directly after <paramref name="anchor"/>, wherever the anchor
        /// sits in the tree. Returns false when the anchor is absent, which is how an editor version that moved
        /// it becomes a visible degraded mode instead of a feature that silently records nothing.
        /// </summary>
        private static bool InsertAfter(ref PlayerLoopSystem node, Type anchor, Type ownType,
            PlayerLoopSystem.UpdateFunction fn)
        {
            var subs = node.subSystemList;
            if (subs == null) return false;

            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type != anchor) continue;

                var grown = new PlayerLoopSystem[subs.Length + 1];
                Array.Copy(subs, 0, grown, 0, i + 1);
                grown[i + 1] = new PlayerLoopSystem { type = ownType, updateDelegate = fn };
                Array.Copy(subs, i + 1, grown, i + 2, subs.Length - i - 1);
                node.subSystemList = grown;
                return true;
            }

            // Depth-first: the anchors live one level down in a stock loop, but nothing guarantees that after
            // another package has restructured things.
            for (int i = 0; i < subs.Length; i++)
            {
                if (!InsertAfter(ref subs[i], anchor, ownType, fn)) continue;
                node.subSystemList = subs;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Is any of our own systems already spliced into <paramref name="node"/>'s tree? The truthful answer
        /// to "am I installed?", which a cached bool is not across a play-mode loop swap.
        /// </summary>
        private static bool ContainsOurs(ref PlayerLoopSystem node)
        {
            var subs = node.subSystemList;
            if (subs == null) return false;

            for (int i = 0; i < subs.Length; i++)
            {
                if (IsOurs(subs[i].type)) return true;
                if (ContainsOurs(ref subs[i])) return true;
            }
            return false;
        }

        private static bool RemoveOurs(ref PlayerLoopSystem node)
        {
            var subs = node.subSystemList;
            if (subs == null) return false;

            bool changed = false;
            int keep = 0;
            for (int i = 0; i < subs.Length; i++)
            {
                if (IsOurs(subs[i].type)) { changed = true; continue; }
                subs[keep++] = subs[i];
            }

            if (changed)
            {
                var trimmed = new PlayerLoopSystem[keep];
                Array.Copy(subs, trimmed, keep);
                subs = trimmed;
            }

            for (int i = 0; i < subs.Length; i++)
            {
                if (RemoveOurs(ref subs[i])) changed = true;
            }

            if (changed) node.subSystemList = subs;
            return changed;
        }

        private static bool IsOurs(Type t) =>
            t == typeof(SasquatchDebugInit) ||
            t == typeof(SasquatchDebugUpdate) ||
            t == typeof(SasquatchDebugLateUpdate) ||
            t == typeof(SasquatchDebugFixedUpdate);
    }
}
