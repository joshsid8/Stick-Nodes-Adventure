using System;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Which clock a script's Live Debug trace is sampled on.
    ///
    /// <para>This is the single most consequential setting in the feature, because it decides what one sample
    /// MEANS. Pass 2 sampled on the editor's update loop, which is not a clock the game shares: consecutive
    /// samples were one to five game frames apart, so "step one frame" could never step one frame and the
    /// X axis was not really time. Every option here is a real point in the player loop instead.</para>
    /// </summary>
    internal enum DebugTickSource
    {
        /// <summary>After every MonoBehaviour Update. One sample == one rendered frame. The default.</summary>
        Update = 0,
        /// <summary>After every LateUpdate — the same frame, but after cameras, animation and IK have moved things.</summary>
        LateUpdate = 1,
        /// <summary>After every FixedUpdate. One sample == one PHYSICS step, so several samples can share a frame.</summary>
        FixedUpdate = 2,
        /// <summary>Never automatic. Samples only on <c>SasquatchDebug.Tick()</c> or the transport's Capture button.</summary>
        Manual = 3,
        /// <summary>
        /// A rate you choose, in samples per second, independent of frame rate.
        ///
        /// <para>For watching something slow. At 60fps a 20-second ring holds 1200 frames; at 5/sec the same
        /// ring holds four MINUTES. The frame rate is still the ceiling — nothing can sample between two
        /// frames — so a rate above the game's fps quietly becomes "every frame".</para>
        /// </summary>
        Interval = 4,
    }

    /// <summary>
    /// What makes a recording START on its own.
    ///
    /// <para><b>The hotkey and the script API are deliberately NOT in this list.</b> They can always start a
    /// recording, whatever is selected here — making them modes would mean choosing between "my key works" and
    /// "it starts on play", which is a choice nobody wants to make. This enum answers one narrower question:
    /// what happens without you doing anything.</para>
    /// </summary>
    internal enum DebugStartTrigger
    {
        /// <summary>
        /// The earliest instant that can be read: frame 1's Initialization point, which Unity runs AFTER every
        /// Awake and OnEnable but BEFORE any Start or Update. So the first sample is literally the state your
        /// Awake left behind, with nothing having moved yet.
        /// </summary>
        OnAwake = 0,
        /// <summary>
        /// Frame 1's Update point — after every Start has run. One sample later than <see cref="OnAwake"/>,
        /// and the difference is exactly "what Start changed".
        ///
        /// <para>These are separate options because "on play" is uselessly vague when the bug you are chasing
        /// IS in the first frame or two: it matters enormously whether you are looking at the value before or
        /// after initialisation ran.</para>
        /// </summary>
        OnStart = 1,
        /// <summary>The first frame the inspected component is actually enabled — and again each time it re-enables.</summary>
        OnEnable = 2,
        /// <summary>A countdown from the start of Play. The digital-camera timer, for a moment you can predict.</summary>
        AfterDelay = 3,
        /// <summary>Nothing automatic. You press Record, hit the hotkey, or call the API.</summary>
        Manual = 4,
    }

    /// <summary>
    /// What makes a recording STOP on its own.
    ///
    /// <para>As with the start triggers, pressing Stop / the hotkey / the API always works regardless.</para>
    ///
    /// <para><b><see cref="OnWarningOrError"/> is the one to reach for.</b> With it the buffer behaves like a
    /// dashcam: the ring rolls continuously and freezes itself the instant something goes wrong, so what you
    /// are left holding is the twenty seconds BEFORE the exception. That is the recording nobody can ever
    /// start in time by hand.</para>
    /// </summary>
    internal enum DebugStopTrigger
    {
        /// <summary>Runs until you stop it. The ring keeps rolling, so you always hold the most recent window.</summary>
        Manual = 0,
        /// <summary>After a fixed number of seconds of recording.</summary>
        AfterSeconds = 1,
        /// <summary>After a fixed number of samples.</summary>
        AfterSamples = 2,
        /// <summary>The instant an ERROR, assert or exception is logged. The dashcam, at its most selective.</summary>
        OnError = 3,
        /// <summary>The instant a WARNING is logged. Separate from errors because plenty of code warns routinely.</summary>
        OnWarning = 4,
        /// <summary>Either of the above. The broadest net, and the right default when you do not know what breaks.</summary>
        OnWarningOrError = 5,
        /// <summary>Once the ring is full — a single window, captured from the start and then frozen.</summary>
        WhenBufferFull = 6,
    }

    /// <summary>
    /// Per-script Live Debug preferences, keyed by the type's MonoScript GUID.
    ///
    /// <para><b>Why EditorPrefs and not the layout asset.</b> How ONE developer wants to look at a script this
    /// week is not a property of the inspector the team shares — putting it on
    /// <see cref="Runtime.ScriptHeaderNode"/> would write a diff into the layout every time someone changed
    /// their mind. It is also not <see cref="DebugModeState"/>'s SessionState, because unlike "is it recording
    /// right now" a chosen trigger SHOULD still be there tomorrow morning.</para>
    ///
    /// <para>Keyed by GUID rather than type name for the same reason <see cref="DebugModeState"/> is: a
    /// namespace change must not silently strand the setting, and two same-named types in different namespaces
    /// must not share one.</para>
    /// </summary>
    internal static class DebugRecordSettings
    {
        private const string KeyPrefix = "SasquatchInspector.LiveDebug.Settings/";

        /// <summary>Bounds on the numeric settings. Wide enough to be useful, tight enough that a typo cannot hang the editor.</summary>
        internal const float MinDelay = 0f;
        internal const float MaxDelay = 600f;
        internal const int MinSamples = 1;
        internal const int MaxSamples = DebugRecorder.Capacity;
        /// <summary>Samples-per-second bounds for <see cref="DebugTickSource.Interval"/>. The frame rate is the real ceiling.</summary>
        internal const float MinRate = 0.1f;
        internal const float MaxRate = 240f;

        /// <summary>Raised when any type's settings change, so open transports can re-chrome without polling.</summary>
        internal static event Action Changed;

        private static string KeyFor(Type type, string leaf)
        {
            if (type == null) return null;
            string id = ScriptGuidUtility.TryGetGuid(type, out string guid) && !string.IsNullOrEmpty(guid)
                ? guid
                : "n:" + type.FullName;
            return KeyPrefix + id + "/" + leaf;
        }

        // ---- generic accessors -------------------------------------------------------------------------

        private static T ReadEnum<T>(Type type, string leaf, T fallback, int max) where T : struct
        {
            string key = KeyFor(type, leaf);
            if (key == null) return fallback;
            int raw = EditorPrefs.GetInt(key, Convert.ToInt32(fallback));
            // Clamped rather than cast blind: a pref written by a NEWER build must degrade to the default here
            // instead of producing an enum value no switch in the package handles.
            return raw >= 0 && raw <= max ? (T)Enum.ToObject(typeof(T), raw) : fallback;
        }

        private static void WriteEnum<T>(Type type, string leaf, T value, T current) where T : struct
        {
            string key = KeyFor(type, leaf);
            if (key == null || Equals(value, current)) return;
            EditorPrefs.SetInt(key, Convert.ToInt32(value));
            Changed?.Invoke();
        }

        // ---- the clock ---------------------------------------------------------------------------------

        internal static DebugTickSource TickSourceFor(Type type)
            => ReadEnum(type, "tick", DebugTickSource.Update, (int)DebugTickSource.Interval);

        internal static void SetTickSource(Type type, DebugTickSource source)
            => WriteEnum(type, "tick", source, TickSourceFor(type));

        /// <summary>
        /// <see cref="Mathf.Clamp(float,float,float)"/>, but NaN-safe.
        ///
        /// <para><b>Mathf.Clamp does not sanitise NaN.</b> Every IEEE comparison against NaN is false, so both
        /// of its bounds tests fail and the value falls straight through unchanged — a clamp that documents a
        /// range and then does not enforce it for the one value most likely to arrive from a hand-edited or
        /// half-written pref. It matters most on the sample rate: the Interval clock's period is
        /// <c>1 / rate</c>, and a NaN period makes "has the period elapsed?" false forever, so the clock stops
        /// recording with nothing on screen to say why.</para>
        ///
        /// <para>NaN resolves to the SHIPPED DEFAULT rather than to a bound: an unreadable setting is a setting
        /// that was never usefully expressed, and the default is what the user would have had without it.</para>
        /// </summary>
        private static float SafeClamp(float value, float min, float max, float fallback)
            => float.IsNaN(value) ? fallback : Mathf.Clamp(value, min, max);

        /// <summary>Samples per second for <see cref="DebugTickSource.Interval"/>.</summary>
        internal static float SampleRateFor(Type type)
        {
            string key = KeyFor(type, "rate");
            return key == null ? 10f : SafeClamp(EditorPrefs.GetFloat(key, 10f), MinRate, MaxRate, 10f);
        }

        internal static void SetSampleRate(Type type, float perSecond)
        {
            string key = KeyFor(type, "rate");
            if (key == null) return;
            perSecond = SafeClamp(perSecond, MinRate, MaxRate, 10f);
            if (Mathf.Approximately(SampleRateFor(type), perSecond)) return;
            EditorPrefs.SetFloat(key, perSecond);
            Changed?.Invoke();
        }

        // ---- start -------------------------------------------------------------------------------------

        internal static DebugStartTrigger StartTriggerFor(Type type)
            => ReadEnum(type, "start", DebugStartTrigger.OnStart, (int)DebugStartTrigger.Manual);

        internal static void SetStartTrigger(Type type, DebugStartTrigger trigger)
            => WriteEnum(type, "start", trigger, StartTriggerFor(type));

        /// <summary>Countdown for <see cref="DebugStartTrigger.AfterDelay"/>, in seconds of Play Mode.</summary>
        internal static float StartDelayFor(Type type)
        {
            string key = KeyFor(type, "delay");
            return key == null ? 3f : SafeClamp(EditorPrefs.GetFloat(key, 3f), MinDelay, MaxDelay, 3f);
        }

        internal static void SetStartDelay(Type type, float seconds)
        {
            string key = KeyFor(type, "delay");
            if (key == null) return;
            seconds = SafeClamp(seconds, MinDelay, MaxDelay, 3f);
            if (Mathf.Approximately(StartDelayFor(type), seconds)) return;
            EditorPrefs.SetFloat(key, seconds);
            Changed?.Invoke();
        }

        // ---- stop --------------------------------------------------------------------------------------

        internal static DebugStopTrigger StopTriggerFor(Type type)
            => ReadEnum(type, "stop", DebugStopTrigger.Manual, (int)DebugStopTrigger.WhenBufferFull);

        internal static void SetStopTrigger(Type type, DebugStopTrigger trigger)
            => WriteEnum(type, "stop", trigger, StopTriggerFor(type));

        /// <summary>Duration for <see cref="DebugStopTrigger.AfterSeconds"/>.</summary>
        internal static float StopAfterSecondsFor(Type type)
        {
            string key = KeyFor(type, "stopSecs");
            return key == null ? 5f : SafeClamp(EditorPrefs.GetFloat(key, 5f), 0.1f, MaxDelay, 5f);
        }

        internal static void SetStopAfterSeconds(Type type, float seconds)
        {
            string key = KeyFor(type, "stopSecs");
            if (key == null) return;
            seconds = SafeClamp(seconds, 0.1f, MaxDelay, 5f);
            if (Mathf.Approximately(StopAfterSecondsFor(type), seconds)) return;
            EditorPrefs.SetFloat(key, seconds);
            Changed?.Invoke();
        }

        /// <summary>Sample count for <see cref="DebugStopTrigger.AfterSamples"/>.</summary>
        internal static int StopAfterSamplesFor(Type type)
        {
            string key = KeyFor(type, "stopSamples");
            return key == null ? 120 : Mathf.Clamp(EditorPrefs.GetInt(key, 120), MinSamples, MaxSamples);
        }

        internal static void SetStopAfterSamples(Type type, int samples)
        {
            string key = KeyFor(type, "stopSamples");
            if (key == null) return;
            samples = Mathf.Clamp(samples, MinSamples, MaxSamples);
            if (StopAfterSamplesFor(type) == samples) return;
            EditorPrefs.SetInt(key, samples);
            Changed?.Invoke();
        }

        // ---- labels ------------------------------------------------------------------------------------

        internal static string Label(DebugTickSource source)
        {
            switch (source)
            {
                case DebugTickSource.LateUpdate:  return "LateUpdate";
                case DebugTickSource.FixedUpdate: return "FixedUpdate";
                case DebugTickSource.Manual:      return "Manual";
                case DebugTickSource.Interval:    return "At a set rate";
                default:                          return "Update";
            }
        }

        /// <summary>One line explaining what a sample means on this clock — shown under the picker, not as a tooltip.</summary>
        internal static string Describe(DebugTickSource source)
        {
            switch (source)
            {
                case DebugTickSource.LateUpdate:
                    return "One sample per frame, after animation and cameras have moved.";
                case DebugTickSource.FixedUpdate:
                    return "One sample per physics step — several can share a frame.";
                case DebugTickSource.Manual:
                    return "Nothing is recorded until you press Capture.";
                case DebugTickSource.Interval:
                    return "A fixed number of samples per second, whatever the frame rate.";
                default:
                    return "One sample per frame, right after your Update runs.";
            }
        }

        /// <summary>Type-aware clock description — <see cref="DebugTickSource.Interval"/> has to quote its rate.</summary>
        internal static string Describe(Type type, DebugTickSource source)
        {
            if (source != DebugTickSource.Interval) return Describe(source);
            float rate = SampleRateFor(type);
            double window = DebugRecorder.Capacity / (double)rate;
            return $"{rate:0.##} samples a second — a {window:0.#}s window in the buffer. " +
                   "The frame rate is still the ceiling.";
        }

        internal static string Label(DebugStartTrigger trigger)
        {
            switch (trigger)
            {
                case DebugStartTrigger.OnStart:    return "On Start (frame 1, after Start)";
                case DebugStartTrigger.OnEnable:   return "When the component enables";
                case DebugStartTrigger.AfterDelay: return "After a delay";
                case DebugStartTrigger.Manual:     return "Only when I say so";
                default:                           return "On Awake (frame 1, before Start)";
            }
        }

        internal static string Describe(Type type, DebugStartTrigger trigger)
        {
            switch (trigger)
            {
                case DebugStartTrigger.OnStart:
                    return "First sample on frame 1, after every Start has run — the first frame of real gameplay.";
                case DebugStartTrigger.OnEnable:
                    return "Starts the first frame this component is enabled, and again whenever it re-enables.";
                case DebugStartTrigger.AfterDelay:
                    return $"Starts {StartDelayFor(type):0.#}s into Play Mode.";
                case DebugStartTrigger.Manual:
                    return "Nothing records until you press Record, hit the shortcut, or call SasquatchDebug.Record().";
                default:
                    return "First sample on frame 1, after Awake and OnEnable but BEFORE any Start or Update — " +
                           "the state your Awake left behind.";
            }
        }

        internal static string Label(DebugStopTrigger trigger)
        {
            switch (trigger)
            {
                case DebugStopTrigger.AfterSeconds:     return "After N seconds";
                case DebugStopTrigger.AfterSamples:     return "After N samples";
                case DebugStopTrigger.OnError:         return "On an error";
                case DebugStopTrigger.OnWarning:       return "On a warning";
                case DebugStopTrigger.OnWarningOrError: return "On a warning or an error";
                case DebugStopTrigger.WhenBufferFull:   return "When the buffer fills";
                default:                                return "Only when I say so";
            }
        }

        internal static string Describe(Type type, DebugStopTrigger trigger)
        {
            switch (trigger)
            {
                case DebugStopTrigger.AfterSeconds:
                    return $"Stops {StopAfterSecondsFor(type):0.#}s after recording begins.";
                case DebugStopTrigger.AfterSamples:
                    return $"Stops after {StopAfterSamplesFor(type)} samples.";
                case DebugStopTrigger.OnError:
                    return "Freezes on the first error, assert or exception — warnings are ignored.";
                case DebugStopTrigger.OnWarning:
                    return "Freezes on the first warning. Errors are ignored, so pick this when the warning IS the symptom.";
                case DebugStopTrigger.OnWarningOrError:
                    return "Freezes the instant anything logs a warning OR an error — so you keep the run-up to it.";
                case DebugStopTrigger.WhenBufferFull:
                    return $"Records one full window ({DebugRecorder.Capacity} samples) and freezes.";
                default:
                    return "Keeps rolling. The buffer always holds the most recent window; press Stop to freeze it.";
            }
        }
    }
}
