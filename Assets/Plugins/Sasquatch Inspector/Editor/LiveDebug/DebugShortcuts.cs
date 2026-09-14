using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Keyboard control of Live Debug recording, by two independent routes.
    ///
    /// <para><b>Why two.</b> They fail in opposite situations, and the moment you want a hotkey is exactly the
    /// moment one of them is unavailable.</para>
    ///
    /// <list type="number">
    /// <item><b>Unity's Shortcut Manager</b> (<see cref="ShortcutAttribute"/>) — zero dependencies, rebindable
    /// by the user in <c>Edit ▸ Shortcuts</c>, and works from any editor window. But while the GAME VIEW has
    /// focus and your game is consuming input, Unity can route the key to the game instead, and that is
    /// precisely when you are watching for the bug.</item>
    /// <item><b>The Input System</b> (<c>SASQ_INPUTSYSTEM</c>, compiled out entirely when the package is
    /// absent) — reads the keyboard from inside the player loop, so it fires reliably with the Game view
    /// focused. It is polled only while at least one script is being watched, so it costs nothing otherwise.</item>
    /// </list>
    ///
    /// <para>Both do the same thing: toggle recording for every type Live Debug is currently switched on for.
    /// Deliberately NOT scoped to the selection — you press this while looking at the Game view, and requiring
    /// the right object to be selected first would defeat the point.</para>
    ///
    /// <para><b>§2.5.g:</b> <see cref="ShortcutAttribute"/> is public editor API and the Input System is a
    /// public package. Nothing here reflects into an editor internal.</para>
    /// </summary>
    internal static class DebugShortcuts
    {
        internal const string ToggleId = "Sasquatch Inspector/Live Debug/Toggle Recording";
        internal const string MarkId = "Sasquatch Inspector/Live Debug/Drop a Marker";

        /// <summary>
        /// Toggles recording for every watched script. <b>Ctrl/Cmd + Alt + R</b> out of the box.
        ///
        /// <para>Shipping a default is a small gamble against a real cost. The gamble: any combination can
        /// collide with another package, and Unity resolves a collision by leaving BOTH unbound until someone
        /// opens the shortcut window. The cost of not shipping one: a hotkey nobody has bound is a feature
        /// nobody has, and "go and configure it first" is a poor introduction to a convenience.</para>
        ///
        /// <para>Ctrl+Alt is the quieter of the two modifier pairs in Unity — the editor's own defaults live
        /// overwhelmingly on Ctrl+Shift — and R reads as Record. The settings card shows the live binding and
        /// links straight to Edit ▸ Shortcuts, so a collision is visible and one click from fixed.</para>
        /// </summary>
        [Shortcut(ToggleId, KeyCode.R, ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        private static void ToggleRecordingShortcut() => ToggleAllWatched();

        /// <summary>Drops a marker on the timeline. <b>Ctrl/Cmd + Alt + M</b>. Same reasoning as above.</summary>
        [Shortcut(MarkId, KeyCode.M, ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        private static void MarkShortcut() => Runtime.SasquatchDebug.Mark("Marked by shortcut");

        /// <summary>
        /// Flips every watched type between recording and held. Returns how many it touched, so a caller can
        /// tell "nothing was watching" from "it worked".
        /// </summary>
        internal static int ToggleAllWatched()
        {
            if (!EditorApplication.isPlaying) return 0;

            // De-duplicate by TYPE before toggling. LiveMap is keyed per RECORDER — one per inspected object —
            // while Toggle is per type, so two objects of the same script (a locked inspector beside the main
            // one, two enemies selected, an inline editor showing a second instance) toggled it twice and the
            // hotkey did nothing at all. An even number of instances made the key a silent no-op; an odd
            // number made it work. That is the worst possible shape for a debug control.
            var toggled = new HashSet<Type>();
            foreach (var kv in DebugRecorder.LiveMap)
            {
                var type = kv.Value.TargetType;
                if (type == null || !DebugModeState.IsOn(type)) continue;
                if (!toggled.Add(type)) continue;
                DebugCaptureController.Toggle(type);
            }
            return toggled.Count;
        }

        /// <summary>
        /// The user's current binding, rendered for the settings card. Read from the Shortcut Manager rather
        /// than hard-coded, so the card cannot advertise a key the user has since rebound.
        /// </summary>
        internal static string CurrentBinding()
        {
            try
            {
                var binding = ShortcutManager.instance.GetShortcutBinding(ToggleId);
                string text = binding.ToString();
                return string.IsNullOrEmpty(text) || text == "None"
                    ? "unassigned — click to set one"
                    : text;
            }
            catch (Exception)
            {
                // A shortcut id that has not been registered yet (first domain load after adding it) throws
                // rather than returning empty. Not worth a hard failure in a settings card.
                return "unassigned — click to set one";
            }
        }

        /// <summary>True when the Input System package is present, so the second route exists at all.</summary>
        internal static bool InputSystemAvailable
        {
#if SASQ_INPUTSYSTEM
            get { return true; }
#else
            get { return false; }
#endif
        }

        /// <summary>True when the Input System route is compiled in AND has a key it can actually watch.</summary>
        internal static bool InputSystemRouteActive
        {
#if SASQ_INPUTSYSTEM
            // The modifiers are irrelevant to "is there a key to watch" — a binding with no key is what makes
            // the route inert, and Ctrl+Alt+R is as watchable as a bare R.
            get
            {
                ShortcutModifiers ignored;
                return ResolveInputSystemKey(out ignored) != UnityEngine.InputSystem.Key.None;
            }
#else
            get { return false; }
#endif
        }

        /// <summary>True once the user has bound anything at all to the toggle shortcut.</summary>
        internal static bool HasBinding()
        {
            try
            {
                foreach (var _ in ShortcutManager.instance.GetShortcutBinding(ToggleId).keyCombinationSequence)
                    return true;
                return false;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// The one line the settings card shows under the shortcut, covering the three states honestly.
        ///
        /// <para>The first draft said "install the Input System package" whether or not it was installed,
        /// because it keyed off "is the route ACTIVE" — which is false both when the package is missing and
        /// when no key is bound. Telling someone to install a package they already have is the kind of small
        /// wrongness that makes people stop reading the other messages.</para>
        /// </summary>
        internal static string FocusCaveat()
        {
            if (!HasBinding()) return null;   // the "unassigned" line above already said the useful thing
            return InputSystemRouteActive
                ? "Also fires while the Game view has focus."
                : InputSystemAvailable
                    ? "The Game view may swallow this key; the Input System route could not map it."
                    : "Editor windows only — the Game view can swallow it. The Input System package fixes that.";
        }

#if SASQ_INPUTSYSTEM
        // ---- Input System route ------------------------------------------------------------------------

        private static bool s_downLastFrame;

        /// <summary>
        /// The Input System key to watch, MIRRORED from the user's Shortcut Manager binding rather than stored
        /// separately.
        ///
        /// <para>Two bindings for one action is a worse feature than one. The user rebinds in
        /// <c>Edit</c> ▸ <c>Shortcuts</c> like every other editor shortcut, and this route simply honours the
        /// same key so it keeps working when the Game view swallows it — which is the entire reason the second
        /// route exists. A separate picker would let the two disagree, and the failure mode of that is "my key
        /// works sometimes", which is worse than no key at all.</para>
        /// </summary>
        private static UnityEngine.InputSystem.Key ResolveInputSystemKey(out ShortcutModifiers modifiers)
        {
            // MEMOIZED, because this is a per-game-frame call and the answer changes only when the user
            // rebinds. The lookup is not cheap — a ShortcutManager query, a foreach over an interface-typed
            // IEnumerable<KeyCombination> that boxes its enumerator, a KeyCode.ToString() and an Enum.TryParse
            // over ~110 members — and recomputing it sixty times a second produced an identical result every
            // time. Invalidated from the Shortcut Manager's own public events, so a rebind still takes effect
            // immediately; nothing here polls for a change.
            if (s_bindingCached)
            {
                modifiers = s_cachedModifiers;
                return s_cachedKey;
            }

            KeyCode code = KeyCode.None;
            modifiers = ShortcutModifiers.None;
            try
            {
                var binding = ShortcutManager.instance.GetShortcutBinding(ToggleId);
                // MODIFIERS TOO, not just the key. Reading `combo.keyCode` alone made the shipped
                // Ctrl+Alt+R fire on a bare "R" in the Game view — a letter anyone might have bound to
                // movement — and it made both routes fire when the Game view did NOT swallow the real
                // combination, which toggles twice and therefore does nothing.
                foreach (var combo in binding.keyCombinationSequence)
                {
                    code = combo.keyCode;
                    modifiers = combo.modifiers;
                    break;
                }
            }
            catch (Exception) { return UnityEngine.InputSystem.Key.None; }

            s_cachedKey = MapKeyCode(code);
            s_cachedModifiers = modifiers;
            s_bindingCached = true;
            return s_cachedKey;
        }

        private static bool s_bindingCached;
        private static UnityEngine.InputSystem.Key s_cachedKey;
        private static ShortcutModifiers s_cachedModifiers;

        /// <summary>
        /// Drops the memoized binding so the next poll re-reads it. Hooked to the Shortcut Manager's own
        /// public change events, which is what keeps a rebind instant without polling for one.
        /// </summary>
        private static void InvalidateBindingCache() => s_bindingCached = false;

        [InitializeOnLoadMethod]
        private static void WatchForRebinds()
        {
            try
            {
                ShortcutManager.instance.shortcutBindingChanged += _ => InvalidateBindingCache();
                ShortcutManager.instance.activeProfileChanged += _ => InvalidateBindingCache();
            }
            catch (Exception)
            {
                // No Shortcut Manager to subscribe to: the cache simply never invalidates, which is the same
                // answer the un-memoized code would have kept giving.
            }
        }

        /// <summary>
        /// Are exactly the binding's modifiers held right now? Exact match, not "at least" — otherwise
        /// Ctrl+Alt+R would also fire on Ctrl+Alt+Shift+R, which may well be someone else's shortcut.
        /// </summary>
        private static bool ModifiersHeld(UnityEngine.InputSystem.Keyboard kb, ShortcutModifiers modifiers)
        {
            // ShortcutModifiers.Action is Cmd on macOS and Ctrl everywhere else — the same mapping the
            // Shortcut Manager uses when it renders the binding, so the two routes agree on what was bound.
#if UNITY_EDITOR_OSX
            bool action = kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;
#else
            bool action = kb.ctrlKey.isPressed;
#endif
            return kb.altKey.isPressed == ((modifiers & ShortcutModifiers.Alt) != 0)
                && kb.shiftKey.isPressed == ((modifiers & ShortcutModifiers.Shift) != 0)
                && action == ((modifiers & ShortcutModifiers.Action) != 0);
        }

        /// <summary>
        /// <see cref="KeyCode"/> to <c>InputSystem.Key</c>. Mostly a name match, with a table for the handful
        /// the two enums genuinely disagree about — digits and the numpad are spelled differently, and those
        /// are exactly the keys people reach for as a debug hotkey.
        /// </summary>
        private static UnityEngine.InputSystem.Key MapKeyCode(KeyCode code)
        {
            if (code == KeyCode.None) return UnityEngine.InputSystem.Key.None;

            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9)
                return UnityEngine.InputSystem.Key.Digit0 + (code - KeyCode.Alpha0);
            if (code >= KeyCode.Keypad0 && code <= KeyCode.Keypad9)
                return UnityEngine.InputSystem.Key.Numpad0 + (code - KeyCode.Keypad0);
            if (code == KeyCode.Return) return UnityEngine.InputSystem.Key.Enter;
            if (code == KeyCode.BackQuote) return UnityEngine.InputSystem.Key.Backquote;
            if (code == KeyCode.KeypadEnter) return UnityEngine.InputSystem.Key.NumpadEnter;

            // Letters, F-keys, arrows, Escape, Space and the rest share a spelling.
            UnityEngine.InputSystem.Key parsed;
            return Enum.TryParse(code.ToString(), out parsed)
                ? parsed
                : UnityEngine.InputSystem.Key.None;
        }

        /// <summary>
        /// Polled once per game frame from <see cref="DebugSampleClock"/>. Edge-detected, so holding the key
        /// toggles once rather than sixty times a second — which would look exactly like the feature being
        /// broken.
        /// </summary>
        internal static void PollInputSystem()
        {
            // THE GATE THIS METHOD ALWAYS CLAIMED TO HAVE. Its own doc said "polled only while at least one
            // script is being watched, so it costs nothing otherwise" — but it is the FIRST statement of
            // DebugSampleClock.TickUpdate, before any arm check, and the clock is installed on every play-mode
            // entry. So every play session of every project that merely HAS the Input System package paid a
            // ShortcutManager binding lookup, a foreach over an interface-typed IEnumerable (boxes an
            // enumerator), a KeyCode.ToString() and an Enum.TryParse over a ~110-member enum, sixty times a
            // second, recomputing an identical answer, with Live Debug switched off. Measured 4.90 us and
            // ~128 bytes per call. RecordingCount is the count of attached inspectors with the toggle on,
            // which is precisely what "being watched" means, and reading it is one int compare.
            if (DebugRunner.RecordingCount == 0) { s_downLastFrame = false; return; }

            ShortcutModifiers modifiers;
            var key = ResolveInputSystemKey(out modifiers);
            if (key == UnityEngine.InputSystem.Key.None) return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) { s_downLastFrame = false; return; }

            bool down = keyboard[key].isPressed && ModifiersHeld(keyboard, modifiers);
            if (down && !s_downLastFrame) ToggleAllWatched();
            s_downLastFrame = down;
        }
#else
        /// <summary>No Input System in the project — the whole route compiles away to this empty call.</summary>
        internal static void PollInputSystem() { }
#endif
    }
}
