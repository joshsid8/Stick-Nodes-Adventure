using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Drive Live Debug from your own code: start a recording, stop one, or drop a marker on the timeline.
    ///
    /// <para><b>Why this exists rather than a built-in "record on event" option.</b> Every project's
    /// interesting moment is different — a boss phase change, a network reconnect, the third time a coroutine
    /// re-enters. No list of triggers a package can ship will contain yours. So the offer is the other way
    /// round: you already know when the moment is, and these five calls let you say so — from one line in
    /// your own code, an Animation Event, a state-machine behaviour, or a <c>UnityEvent</c> listener you add
    /// with <c>AddListener</c>.</para>
    ///
    /// <para><b>These are static, so they cannot be wired in the UnityEvent inspector dropdown.</b> That
    /// dropdown enumerates INSTANCE methods on the object you drop into the target slot; a static method has
    /// no target and never appears in it. Wire them from code
    /// (<c>onDeath.AddListener(() =&gt; SasquatchDebug.Mark("died"));</c>) or call them directly. If you want
    /// an inspector-wireable version, put a one-line method on your own MonoBehaviour and bind that.</para>
    ///
    /// <example><code>
    /// void OnBossPhaseChange(int phase)
    /// {
    ///     if (phase == 3) SasquatchDebug.Record(this);   // start capturing me, now
    /// }
    ///
    /// void OnDeath()
    /// {
    ///     SasquatchDebug.Mark("died");                   // pin this instant on the timeline
    ///     SasquatchDebug.Stop(this);                     // freeze the buffer while it still holds the run-up
    /// }
    /// </code></example>
    ///
    /// <para><b>Cost in a build: nothing.</b> Every body here is inside <c>#if UNITY_EDITOR</c>, so a player
    /// build compiles them to empty methods. The methods themselves still EXIST rather than being compiled
    /// out entirely, and that is the deliberate part: <i>your</i> call sites are ordinary C# in your own
    /// scripts, so a method that vanished in a player build would not "do nothing" — it would fail to
    /// COMPILE, and you would have to bracket every call in <c>#if UNITY_EDITOR</c> yourself. Leaving the
    /// calls in your shipping code is safe and intended.</para>
    ///
    /// <para>Live Debug only records a script whose layout has the debug toggle switched on and whose bug
    /// glyph is lit, so these calls are inert until you have actually asked to watch that script. They never
    /// switch Live Debug ON by themselves — a debug tool that starts recording because a shipped code path
    /// ran would be a surprise, not a feature.</para>
    /// </summary>
    public static class SasquatchDebug
    {
#if UNITY_EDITOR
        /// <summary>What a call is asking for. Editor-side only; there is no runtime dispatch in a build.</summary>
        internal enum Command { Record, Stop, Toggle, Tick, Mark }

        /// <summary>
        /// Installed by the Editor assembly at load. Null in a player and null before the editor has ever
        /// built a Live Debug strip, which is why every call below is a null-conditional invoke.
        /// </summary>
        internal static System.Action<Object, Command, string> Handler;

        /// <summary>
        /// Raised before the first scene loads, which is EARLIER than any editor callback can reach.
        ///
        /// <para>Live Debug needs this to capture frame 1. <c>PlayModeStateChange.EnteredPlayMode</c> does not
        /// arrive until the first frame has already been processed, so a player-loop insertion made there
        /// starts recording on frame 2 — and "the timeline will not go back to frame 1" is exactly what that
        /// looks like from the outside. A runtime hook is the only thing that runs early enough.</para>
        ///
        /// <para>Editor-only by construction: the whole member sits inside <c>#if UNITY_EDITOR</c>, so a player
        /// build has neither the event nor the attribute.</para>
        /// </summary>
        internal static System.Action PlayStarting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void NotifyPlayStarting()
        {
            var handler = PlayStarting;
            if (handler != null) handler();
        }
#endif

        /// <summary>
        /// Starts recording the script that owns <paramref name="target"/>.
        /// </summary>
        /// <param name="target">
        /// Pass <c>this</c> from a MonoBehaviour. Null means "every script currently watching", which is what
        /// you want from a global event like a scene load.
        /// </param>
        public static void Record(Object target = null)
        {
#if UNITY_EDITOR
            Handler?.Invoke(target, Command.Record, null);
#endif
        }

        /// <summary>
        /// Stops (freezes) the recording for <paramref name="target"/>, keeping everything captured so far.
        /// The buffer stays fully scrubbable — this is a pause on the writer, never a discard.
        /// </summary>
        public static void Stop(Object target = null)
        {
#if UNITY_EDITOR
            Handler?.Invoke(target, Command.Stop, null);
#endif
        }

        /// <summary>
        /// Starts if stopped, stops if running. Convenient wherever ONE call site has to do both — a debug
        /// key, a cheat-menu button, or a listener you add with <c>AddListener</c>.
        /// </summary>
        public static void Toggle(Object target = null)
        {
#if UNITY_EDITOR
            Handler?.Invoke(target, Command.Toggle, null);
#endif
        }

        /// <summary>
        /// Takes exactly one sample, for scripts set to the <c>Manual</c> clock. Ignored on every other clock,
        /// where the player loop is already sampling on its own schedule.
        /// </summary>
        public static void Tick(Object target = null)
        {
#if UNITY_EDITOR
            Handler?.Invoke(target, Command.Tick, null);
#endif
        }

        /// <summary>
        /// Pins a named marker on the timeline at this instant, so "the frame it looked wrong" is something you
        /// can jump to later instead of hunt for. Recorded as a log entry on the same clock as the samples, so
        /// it lands in the moment card beside whatever else moved.
        /// </summary>
        public static void Mark(string label = null)
        {
#if UNITY_EDITOR
            Handler?.Invoke(null, Command.Mark, label);
#endif
        }
    }
}
