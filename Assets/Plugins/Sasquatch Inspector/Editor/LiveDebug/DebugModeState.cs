using System;
using System.Collections.Generic;
using UnityEditor;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Whether Live Debug is currently RECORDING, per inspected type. Deliberately not layout data: the node's
    /// <see cref="Runtime.ScriptHeaderNode.ShowDebugToggle"/> says whether the button exists (author-time intent,
    /// shared with the team), while this says whether it is switched on right now (one developer, one sitting).
    /// Same split the Condition Debugger makes with its EditorPrefs key.
    ///
    /// <para><b>Why SessionState and not EditorPrefs.</b> Debug mode has to survive a DOMAIN RELOAD, because
    /// entering Play Mode is one — a pref that reset on play would switch itself off at the exact moment it is
    /// needed. It must NOT survive an editor restart: "leave it on for a while" is the whole usage pattern, and
    /// a graph strip that is still there next Tuesday reads as a bug in the package. SessionState is exactly that
    /// lifetime. (Contrast <c>FoldoutStateStore</c>, which picks EditorPrefs for the opposite reason.)</para>
    ///
    /// <para>Keyed by the type's MonoScript GUID rather than its name, so a namespace change or a rename doesn't
    /// silently strand the state — and two same-named types in different namespaces don't share a switch.</para>
    /// </summary>
    internal static class DebugModeState
    {
        private const string KeyPrefix = "SasquatchInspector.LiveDebug/";

        /// <summary>Raised whenever any type's debug state flips, so open inspectors can re-chrome without polling.</summary>
        internal static event Action Changed;

        // ---- OUT-OF-BAND WRITES ----------------------------------------------------------------------------
        //
        // SessionState is a SHARED namespace, and this key is deliberately shared: an EXPORTED [CustomEditor]'s
        // baked Live Debug twin (ExportedWidgetKit's SasqDebugMode) uses the same prefix on purpose, so one bug
        // glyph means one switch whether the row is taken over or released. What it does NOT share is this
        // `Changed` event — the kit raises its own — and a type that has BOTH an exported editor and a layout has
        // BOTH bodies alive in the same InspectorElement at once (the displaced one is hidden, not destroyed).
        //
        // Anything that CACHES the value off `Changed` therefore drifts the moment the other side writes.
        // DebugRunner.armed is exactly that cache, and the drift costs the user a whole press, MEASURED:
        //
        //     kit writes the key -> true    our runner still holds armed = false, nothing on screen
        //     user clicks OUR glyph         Toggle reads true, writes false, raises Changed
        //                                   the runner sees false == armed and returns: NOTHING HAPPENS
        //     user clicks again             writes true, runner finally arms: "and THEN it opened"
        //
        // So the announced value is tracked per key, and Resync raises the event the other writer never did.
        // Not a poll: it runs on the header bar's hover, which is the one thing that always precedes a click on
        // the hover-revealed glyph, and on a runner going live.
        private static readonly Dictionary<string, bool> s_announced = new Dictionary<string, bool>();

        private static string KeyFor(Type type)
        {
            if (type == null) return null;
            // The GUID map is the rename-proof identity used everywhere else in the package. When a type has no
            // backing MonoScript (rare — a type declared inside a DLL), fall back to the full name: worse across
            // renames, but a session-scoped switch losing its place is a cosmetic miss, not data loss.
            return ScriptGuidUtility.TryGetGuid(type, out string guid) && !string.IsNullOrEmpty(guid)
                ? KeyPrefix + guid
                : KeyPrefix + "n:" + type.FullName;
        }

        /// <summary>True while Live Debug is recording for <paramref name="type"/>.</summary>
        internal static bool IsOn(Type type)
        {
            string key = KeyFor(type);
            return key != null && SessionState.GetBool(key, false);
        }

        /// <summary>Switches recording on or off for <paramref name="type"/> and notifies listeners.</summary>
        internal static void Set(Type type, bool on)
        {
            string key = KeyFor(type);
            if (key == null) return;
            // Recorded even on the no-op path: "what listeners were last told" has to stay true whether or not
            // this call had anything to tell them, or the first Resync after it reads a missing entry as a
            // difference and fires a spurious event.
            s_announced[key] = on;
            if (SessionState.GetBool(key, false) == on) return;
            SessionState.SetBool(key, on);
            Changed?.Invoke();
        }

        /// <summary>
        /// Reads <paramref name="type"/>'s state AND records it as what listeners have been told, raising nothing.
        /// Callers that cache the value off <see cref="Changed"/> read their initial state through this rather
        /// than <see cref="IsOn"/>, which is what later lets <see cref="Resync"/> tell a stale cache from a
        /// current one. See the note on <c>s_announced</c>.
        /// </summary>
        internal static bool Observe(Type type)
        {
            string key = KeyFor(type);
            if (key == null) return false;
            bool value = SessionState.GetBool(key, false);
            s_announced[key] = value;
            return value;
        }

        /// <summary>
        /// Raises <see cref="Changed"/> when the stored key no longer matches what listeners were last told —
        /// i.e. when something outside this class wrote it (the exported kit's twin). A no-op in the normal case,
        /// so it is safe to call on every hover. See the note on <c>s_announced</c>.
        /// </summary>
        internal static void Resync(Type type)
        {
            string key = KeyFor(type);
            if (key == null) return;
            bool value = SessionState.GetBool(key, false);
            // No record yet means nothing has cached anything off this key, so there is nothing to correct —
            // adopt the value silently. Firing here would re-arm start triggers for a type nobody is watching.
            if (!s_announced.TryGetValue(key, out bool told)) { s_announced[key] = value; return; }
            if (told == value) return;
            s_announced[key] = value;
            Changed?.Invoke();
        }

        /// <summary>Flips recording for <paramref name="type"/> and returns the new state.</summary>
        internal static bool Toggle(Type type)
        {
            bool next = !IsOn(type);
            Set(type, next);
            return next;
        }

        /// <summary>
        /// True when the debug strip should be drawing right now for this type — recording is on AND the editor
        /// is in Play Mode. The single question every tick body and every build-time branch asks, so "when does
        /// debug show?" has exactly one answer instead of two that can drift.
        /// </summary>
        internal static bool IsLive(Type type) => EditorApplication.isPlaying && IsOn(type);

        /// <summary>Test seam: clears every recorded switch for the current session.</summary>
        internal static void ResetAllForTests()
        {
            // SessionState has no enumeration API, so this cannot sweep unknown keys — callers that need a
            // clean slate must Set(type, false) the types they touched. Kept as the single documented place
            // that fact lives, rather than each test rediscovering it.
            s_announced.Clear();
            Changed?.Invoke();
        }
    }
}
