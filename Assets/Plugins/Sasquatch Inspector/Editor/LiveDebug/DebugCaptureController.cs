using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>Where a script's recording is in its life. Distinct from "is Live Debug switched on", which is <see cref="DebugModeState"/>.</summary>
    internal enum DebugCapturePhase
    {
        /// <summary>Live Debug is on for this type, but the start trigger has not fired. Nothing is being written.</summary>
        Waiting = 0,
        /// <summary>Samples are being written every tick.</summary>
        Recording = 1,
        /// <summary>Stopped. The buffer is frozen and fully scrubbable — a pause on the writer, never a discard.</summary>
        Held = 2,
    }

    /// <summary>
    /// Decides WHEN a script records, as opposed to <see cref="DebugSampleClock"/>, which decides how often.
    ///
    /// <para><b>The problem this solves.</b> "Press play and it records" only catches bugs that happen at the
    /// start of a run, and only if you were already watching the right script. The interesting moment is
    /// usually somewhere in the middle of a session, often the exact moment something goes wrong — which is
    /// the one instant you can never reach for a Record button in time. So recording gets triggers: automatic
    /// ones you configure per script, plus a shortcut and a script API that always work regardless.</para>
    ///
    /// <para><b>The dashcam is the point.</b> With <see cref="DebugStopTrigger.OnWarningOrError"/> the ring
    /// rolls continuously and freezes itself the instant anything logs an error, so what you are left holding
    /// is the twenty seconds BEFORE the exception rather than the moment after it. Note the deliberate
    /// ordering below: the stop is evaluated AFTER the frame has been sampled, so the failing frame is inside
    /// the trace and not one sample past its end.</para>
    ///
    /// <para><b>Phase is SessionState, like <see cref="DebugModeState"/>.</b> Entering Play Mode is a domain
    /// reload, so a plain static would reset at the exact moment a recording was supposed to start. Per-run
    /// bookkeeping (when did this recording begin, was the component enabled last frame) is a plain static
    /// dictionary — it genuinely should not survive, and it re-seeds itself if a mid-play script edit wipes
    /// it.</para>
    /// </summary>
    internal static class DebugCaptureController
    {
        private const string KeyPrefix = "SasquatchInspector.LiveDebug.Phase/";

        /// <summary>Raised whenever any type's phase changes, so transports re-chrome without polling.</summary>
        internal static event Action Changed;

        /// <summary>Per-run bookkeeping for one recording type. Deliberately NOT persisted.</summary>
        private sealed class Run
        {
            /// <summary>Editor timestamp the current recording began.</summary>
            public double StartedAt;
            /// <summary>Absolute sample index the current recording began at, so length is measured, not guessed.</summary>
            public int StartAbsolute;
            /// <summary>Was the inspected component enabled last frame? Drives the OnEnable edge.</summary>
            public bool WasEnabled;
            public bool SawEnabledOnce;
            /// <summary>Set by the log hook, consumed after the frame has been sampled. See the class note.</summary>
            public bool StopAfterThisFrame;
        }

        private static readonly Dictionary<Type, Run> s_runs = new Dictionary<Type, Run>();

        /// <summary>Editor timestamp Play Mode began, for the AfterDelay countdown.</summary>
        private static double s_playStarted;

        /// <summary>
        /// Types already re-armed for the current play session.
        ///
        /// <para>Re-arming cannot be a single sweep at the start of a run, for two reasons that compound.
        /// <c>PlayModeStateChange.EnteredPlayMode</c> does not fire until frame 1 is already over — so a sweep
        /// there leaves every type sitting in whatever phase the LAST run ended in (usually Held) for the whole
        /// of frame 1, and a start trigger that requires Waiting simply never fires. And a sweep at the earlier
        /// runtime hook cannot help either, because it can only iterate recorders, and the inspector has not
        /// rebuilt yet so there are none.</para>
        ///
        /// <para>So re-arming is LAZY: the first time a type is evaluated in a new session it is re-armed right
        /// there. That works whether its recorder appeared on frame 1 or frame 300.</para>
        /// </summary>
        private static readonly HashSet<Type> s_rearmedThisSession = new HashSet<Type>();

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // BEFORE the first scene loads — earlier than any editor callback. See s_rearmedThisSession.
            Runtime.SasquatchDebug.PlayStarting += OnPlayStarting;
            DebugLogCapture.Logged += OnLogged;
            DebugModeState.Changed += OnDebugToggled;
            InstallScriptApi();
        }

        // ---- phase -------------------------------------------------------------------------------------

        private static string KeyFor(Type type)
        {
            if (type == null) return null;
            return ScriptGuidUtility.TryGetGuid(type, out string guid) && !string.IsNullOrEmpty(guid)
                ? KeyPrefix + guid
                : KeyPrefix + "n:" + type.FullName;
        }

        internal static DebugCapturePhase PhaseOf(Type type)
        {
            string key = KeyFor(type);
            if (key == null) return DebugCapturePhase.Waiting;
            int raw = SessionState.GetInt(key, (int)DebugCapturePhase.Waiting);
            return raw >= 0 && raw <= (int)DebugCapturePhase.Held ? (DebugCapturePhase)raw : DebugCapturePhase.Waiting;
        }

        private static void SetPhase(Type type, DebugCapturePhase phase)
        {
            string key = KeyFor(type);
            if (key == null || PhaseOf(type) == phase) return;
            SessionState.SetInt(key, (int)phase);
            // Armed is derived from the phase, so the recorders have to be re-derived in the same breath —
            // otherwise the sampler keeps writing for a frame or two after a Stop, which is exactly the kind
            // of "off by a couple of samples" that makes a trace impossible to trust.
            DebugRecorder.RefreshArmed();
            Changed?.Invoke();
        }

        /// <summary>True when this type should be sampled right now. The single question the recorder asks.</summary>
        internal static bool IsRecording(Type type)
            => DebugModeState.IsOn(type) && PhaseOf(type) == DebugCapturePhase.Recording;

        // ---- commands ----------------------------------------------------------------------------------

        /// <summary>
        /// Begins a recording, discarding whatever the buffer held. Every start clears: a trace that ran across
        /// two separate recordings, with nothing marking the seam, is not one trace.
        /// </summary>
        internal static void Record(Type type)
        {
            if (type == null || !DebugModeState.IsOn(type)) return;

            // An explicit Record IS this session's decision for this type, so claim the lazy re-arm slot.
            // Without this, a Record() issued before the type's first evaluation — which is every call made
            // from Awake or Start — is set, and then silently undone by EnsureArmedForSession putting the
            // type back to Waiting the first time the player loop reaches it. Re-arming exists to give a
            // START TRIGGER a fresh chance; an explicit command supersedes the start trigger by definition.
            s_rearmedThisSession.Add(type);

            // EVERY recorder of this type, not merely the first one found. Several objects of one script can
            // each hold a recorder — a locked inspector beside the main one, two enemies in the scene — and
            // clearing one while the others keep their old samples produces exactly the seam this clear exists
            // to prevent, on whichever recorder you happen not to be looking at.
            foreach (var kv in DebugRecorder.LiveMap)
                if (kv.Value.TargetType == type) kv.Value.ClearSamples();

            var run = RunFor(type);
            run.StartedAt = EditorApplication.timeSinceStartup;
            // Zero because every recorder of this type was just reset, so "how long has this been recording"
            // is measured from a shared origin rather than from whichever one answered first.
            run.StartAbsolute = 0;
            run.StopAfterThisFrame = false;

            SetPhase(type, DebugCapturePhase.Recording);
        }

        /// <summary>Freezes the recording, keeping every sample. Scrubbing still works — this stops the writer, not the reader.</summary>
        internal static void Stop(Type type)
        {
            if (type == null) return;
            if (PhaseOf(type) != DebugCapturePhase.Recording) return;
            SetPhase(type, DebugCapturePhase.Held);
        }

        internal static void Toggle(Type type)
        {
            if (type == null) return;
            if (PhaseOf(type) == DebugCapturePhase.Recording) Stop(type);
            else Record(type);
        }

        /// <summary>Puts a type back to Waiting so its start trigger can fire again. Used when Live Debug is switched on.</summary>
        internal static void Rearm(Type type)
        {
            if (type == null) return;
            s_runs.Remove(type);
            SetPhase(type, DebugCapturePhase.Waiting);
        }

        private static Run RunFor(Type type)
        {
            if (!s_runs.TryGetValue(type, out var run)) { run = new Run(); s_runs[type] = run; }
            return run;
        }

        // ---- lifecycle ---------------------------------------------------------------------------------

        /// <summary>Opens a new play session. Runs before the first scene loads, before any Awake.</summary>
        private static void OnPlayStarting()
        {
            s_playStarted = EditorApplication.timeSinceStartup;
            s_runs.Clear();
            s_rearmedThisSession.Clear();
        }

        /// <summary>
        /// Puts a type back to Waiting the first time it is seen in a play session, so its start trigger gets a
        /// fresh chance no matter when its recorder came into existence. Returns true when the type is now in a
        /// state a start trigger can act on.
        /// </summary>
        private static bool EnsureArmedForSession(Type type)
        {
            if (s_rearmedThisSession.Add(type))
            {
                s_runs.Remove(type);
                SetPhase(type, DebugCapturePhase.Waiting);
            }
            return PhaseOf(type) == DebugCapturePhase.Waiting;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Belt and braces: the runtime hook above has normally already done this, but a play session
                // entered with the Runtime assembly somehow unhooked must not be left with stale phases.
                if (s_rearmedThisSession.Count == 0) s_playStarted = EditorApplication.timeSinceStartup;
                Changed?.Invoke();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Leaving Play freezes rather than resets: the recording you just made is precisely the thing
                // you want to read, and it is the moment people actually look at it.
                foreach (var kv in DebugRecorder.LiveMap)
                {
                    var t = kv.Value.TargetType;
                    if (t != null && PhaseOf(t) == DebugCapturePhase.Recording) SetPhase(t, DebugCapturePhase.Held);
                }
                Changed?.Invoke();
            }
        }

        private static void OnDebugToggled()
        {
            // Switching the bug glyph ON should behave like arriving fresh — the start trigger has not had its
            // chance yet. Switching it OFF leaves the phase alone; nothing samples while it is off anyway, and
            // preserving it means flicking the glyph does not throw away a held trace.
            foreach (var kv in DebugRecorder.LiveMap)
            {
                var t = kv.Value.TargetType;
                if (t == null || !DebugModeState.IsOn(t)) continue;
                if (PhaseOf(t) == DebugCapturePhase.Waiting) s_runs.Remove(t);
            }
        }

        /// <summary>
        /// Does this log line satisfy a "stop on a warning or error" trigger?
        ///
        /// <para>Two exclusions, both load-bearing. A <see cref="LogType.Log"/> is ordinary output and a game
        /// that logs every frame would stop the recording instantly. And a MARKER is our own writing —
        /// <c>SasquatchDebug.Mark()</c> goes through the same ring — so without the second test, marking a
        /// moment would sometimes end the recording it was called to annotate.</para>
        ///
        /// <para>Pure and internal so it can be asserted directly; the surrounding handler needs Play Mode.</para>
        /// </summary>
        internal static bool IsFailureLog(LogType type, bool isMark)
            => !isMark && type != LogType.Log;

        /// <summary>
        /// Does <paramref name="type"/> satisfy <paramref name="trigger"/>?
        ///
        /// <para>Warnings and errors are separate triggers because they are separate situations. Plenty of
        /// perfectly healthy code warns routinely — a shader fallback, a deprecated call — and stopping the
        /// recording on the first of those would make the dashcam useless in exactly the projects that need it
        /// most. Equally, sometimes the warning IS the symptom you are chasing and an error never comes.</para>
        /// </summary>
        internal static bool Satisfies(DebugStopTrigger trigger, LogType type, bool isMark)
        {
            if (isMark) return false;   // our own writing never ends a recording
            switch (trigger)
            {
                case DebugStopTrigger.OnError:
                    return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                case DebugStopTrigger.OnWarning:
                    return type == LogType.Warning;
                case DebugStopTrigger.OnWarningOrError:
                    return IsFailureLog(type, false);
                default:
                    return false;
            }
        }

        private static void OnLogged(LogType type, bool isMark)
        {
            if (isMark) return;
            if (!EditorApplication.isPlaying) return;

            foreach (var kv in DebugRecorder.LiveMap)
            {
                var t = kv.Value.TargetType;
                if (t == null || !IsRecording(t)) continue;
                if (!Satisfies(DebugRecordSettings.StopTriggerFor(t), type, false)) continue;
                // Deferred, NOT immediate: the log fires from inside the user's Update, and our sampler runs
                // after every Update has run. Stopping here would end the trace one sample BEFORE the frame
                // that failed — throwing away the single most important frame in the whole recording.
                RunFor(t).StopAfterThisFrame = true;
            }
        }

        // ---- per-frame evaluation ----------------------------------------------------------------------

        /// <summary>
        /// Start triggers. Called from the Update point BEFORE that frame is sampled, so a recording that
        /// begins this frame contains this frame.
        /// </summary>
        internal static void EvaluateStarts()
        {
            foreach (var kv in DebugRecorder.LiveMap)
            {
                var rec = kv.Value;
                var type = rec.TargetType;
                if (type == null || !DebugModeState.IsOn(type)) continue;
                if (!EnsureArmedForSession(type)) continue;

                var run = RunFor(type);
                switch (DebugRecordSettings.StartTriggerFor(type))
                {
                    case DebugStartTrigger.OnStart:
                        // Frame 1's Update point: every Start has now run.
                        Record(type);
                        break;

                    case DebugStartTrigger.AfterDelay:
                        if (EditorApplication.timeSinceStartup - s_playStarted >= DebugRecordSettings.StartDelayFor(type))
                            Record(type);
                        break;

                    case DebugStartTrigger.OnEnable:
                    {
                        // There is no callback an editor can hang off another object's OnEnable, so this is an
                        // observed EDGE rather than a hook: the first frame we see the component enabled after
                        // seeing it disabled. Honest, cheap, and one frame late at worst.
                        bool enabled = rec.TargetIsEnabled();
                        if (enabled && (!run.WasEnabled || !run.SawEnabledOnce)) Record(type);
                        run.WasEnabled = enabled;
                        run.SawEnabledOnce = true;
                        break;
                    }

                    case DebugStartTrigger.OnAwake:
                    case DebugStartTrigger.Manual:
                    default:
                        break;   // handled at the Initialization anchor, or by a button / shortcut / the API
                }
            }
        }

        /// <summary>
        /// The OnAwake start trigger, evaluated at the frame's Initialization point.
        ///
        /// <para>Separate from <see cref="EvaluateStarts"/> because the whole VALUE of "on Awake" is where in
        /// the frame it happens: Unity runs Initialization after every Awake and OnEnable but before any Start
        /// or Update, so a recording started here holds the state Awake left behind, with nothing yet moved.
        /// Evaluated at the Update anchor instead, it would be indistinguishable from OnStart.</para>
        ///
        /// <para>Deliberately NOT gated on <c>EditorApplication.isPlaying</c>: on frame 1 Unity still reports
        /// false from this point, and gating here is what made frame 1 unreachable.</para>
        /// </summary>
        internal static void EvaluateInitStarts()
        {
            foreach (var kv in DebugRecorder.LiveMap)
            {
                var type = kv.Value.TargetType;
                if (type == null || !DebugModeState.IsOn(type)) continue;
                if (!EnsureArmedForSession(type)) continue;
                if (DebugRecordSettings.StartTriggerFor(type) != DebugStartTrigger.OnAwake) continue;
                Record(type);

                // Take the first sample HERE, not at the Update anchor. Without this line OnAwake and OnStart
                // produce byte-identical traces: Record() would arm the recorder at Initialization, but the
                // first actual write would still be the Update anchor of the same frame — by which time Start
                // has run, and "the state your Awake left behind" is a frame nobody ever recorded.
                //
                // The frame dedupe then does the rest of the work: this sample stamps _lastFrame, so the Update
                // anchor skips frame 1 as a duplicate and frame 1 ends up with exactly ONE sample, the early
                // one. From frame 2 the clock behaves completely normally.
                kv.Value.Sample(kv.Value.TickSource);
            }
        }

        /// <summary>
        /// Stop triggers. Called from the LateUpdate point AFTER every clock has sampled, so whatever ends a
        /// recording is itself inside it.
        /// </summary>
        internal static void EvaluateStops()
        {
            if (!EditorApplication.isPlaying) return;

            foreach (var kv in DebugRecorder.LiveMap)
            {
                var rec = kv.Value;
                var type = rec.TargetType;
                if (type == null || !IsRecording(type)) continue;

                var run = RunFor(type);
                if (run.StopAfterThisFrame) { Stop(type); continue; }

                switch (DebugRecordSettings.StopTriggerFor(type))
                {
                    case DebugStopTrigger.AfterSeconds:
                        if (EditorApplication.timeSinceStartup - run.StartedAt >= DebugRecordSettings.StopAfterSecondsFor(type))
                            Stop(type);
                        break;

                    case DebugStopTrigger.AfterSamples:
                        if (rec.Absolute - run.StartAbsolute >= DebugRecordSettings.StopAfterSamplesFor(type))
                            Stop(type);
                        break;

                    case DebugStopTrigger.WhenBufferFull:
                        if (rec.Absolute - run.StartAbsolute >= DebugRecorder.Capacity) Stop(type);
                        break;

                    case DebugStopTrigger.OnError:
                    case DebugStopTrigger.OnWarning:
                    case DebugStopTrigger.OnWarningOrError:
                    case DebugStopTrigger.Manual:
                    default:
                        break;   // event-driven or never
                }
            }
        }

        /// <summary>Seconds this type has been recording, for the transport readout. 0 when it is not.</summary>
        internal static double ElapsedFor(Type type)
        {
            if (type == null || !s_runs.TryGetValue(type, out var run)) return 0d;
            return EditorApplication.timeSinceStartup - run.StartedAt;
        }

        /// <summary>Seconds remaining on an AfterDelay countdown, or -1 when no countdown is running.</summary>
        internal static double CountdownFor(Type type)
        {
            if (type == null || !EditorApplication.isPlaying) return -1d;
            if (PhaseOf(type) != DebugCapturePhase.Waiting) return -1d;
            if (DebugRecordSettings.StartTriggerFor(type) != DebugStartTrigger.AfterDelay) return -1d;
            double left = DebugRecordSettings.StartDelayFor(type) - (EditorApplication.timeSinceStartup - s_playStarted);
            return left > 0d ? left : 0d;
        }

        // ---- the script API ----------------------------------------------------------------------------

        /// <summary>
        /// Wires <see cref="Runtime.SasquatchDebug"/> to this controller. Installed once at editor load, so a
        /// user's <c>Record()</c> call works whether or not an inspector has been opened yet.
        /// </summary>
        /// <summary>
        /// One explicit sample for every recorder of <paramref name="type"/> on the Manual clock.
        ///
        /// <para>Unlike Record/Stop/Toggle this genuinely DOES need a recorder — a sample has to be written
        /// into a ring, and there is no ring without one. So a <c>Tick()</c> before the inspector exists is
        /// correctly a no-op: there is nowhere to put the sample. Every other clock is already sampling on
        /// its own schedule and an extra sample there would be a duplicate frame.</para>
        /// </summary>
        private static void TickManual(Type type)
        {
            if (!IsRecording(type)) return;
            foreach (var kv in DebugRecorder.LiveMap)
            {
                var rec = kv.Value;
                if (rec.TargetType != type) continue;
                if (rec.TickSource == DebugTickSource.Manual) rec.Sample(DebugTickSource.Manual);
            }
        }

        private static void InstallScriptApi()
        {
            Runtime.SasquatchDebug.Handler = (target, command, label) =>
            {
                if (command == Runtime.SasquatchDebug.Command.Mark)
                {
                    DebugLogCapture.AddMark(label);
                    return;
                }

                // A TARGETED call already names its own type, so it must NOT be filtered through the recorder
                // map. At Awake — the most natural place to put "start capturing me", and where this class's
                // own example points — the map is EMPTY: it is cleared on entering Play Mode and the
                // inspector does not rebuild until after the scene has loaded. The old
                // `foreach (LiveMap)` shape therefore made Record, Stop and Toggle silent no-ops for the
                // whole first part of every session, with no error and no log, directly contradicting the
                // doc comment above. Those three act on the TYPE and on the SessionState phase, so they need
                // no recorder at all — RefreshArmed picks up whichever one appears later.
                if (target != null)
                {
                    Type only = target.GetType();
                    if (!DebugModeState.IsOn(only)) return;
                    switch (command)
                    {
                        case Runtime.SasquatchDebug.Command.Record: Record(only); break;
                        case Runtime.SasquatchDebug.Command.Stop:   Stop(only);   break;
                        case Runtime.SasquatchDebug.Command.Toggle: Toggle(only); break;
                        case Runtime.SasquatchDebug.Command.Tick:   TickManual(only); break;
                    }
                    return;
                }

                // The broadcast form — "every script currently being watched", for a global event like a
                // scene load — genuinely has to enumerate, and by then a recorder is what "being watched"
                // means.
                foreach (var kv in DebugRecorder.LiveMap)
                {
                    var type = kv.Value.TargetType;
                    if (type == null || !DebugModeState.IsOn(type)) continue;

                    switch (command)
                    {
                        case Runtime.SasquatchDebug.Command.Record: Record(type); break;
                        case Runtime.SasquatchDebug.Command.Stop:   Stop(type);   break;
                        case Runtime.SasquatchDebug.Command.Toggle: Toggle(type); break;
                        case Runtime.SasquatchDebug.Command.Tick:
                            // Only meaningful on the Manual clock; every other clock is already sampling on
                            // its own schedule and an extra sample there would be a duplicate frame.
                            if (kv.Value.TickSource == DebugTickSource.Manual && IsRecording(type))
                                kv.Value.Sample(DebugTickSource.Manual);
                            break;
                    }
                }
            };
        }
    }
}
