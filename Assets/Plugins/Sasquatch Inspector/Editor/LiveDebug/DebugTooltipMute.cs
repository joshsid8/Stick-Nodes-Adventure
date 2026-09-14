using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The "tips off" switch: while it is on, hovering a field in a Live Debug inspector no longer raises
    /// Unity's tooltip.
    ///
    /// <para><b>Why it exists.</b> A field tooltip is a black box roughly the width of the inspector, and it
    /// opens directly UNDER the row the pointer is resting on — which, in debug mode, is exactly where the
    /// next two or three graphs are. So the act of pointing at a graph to read it is the act of covering the
    /// graphs below it. The tooltips are worth having the rest of the time, hence a switch rather than a
    /// removal.</para>
    ///
    /// <para><b>How the mute works.</b> The tooltip is not a string this package owns — it belongs to the
    /// user's own <c>[Tooltip]</c> attributes and to Unity's own controls, and clearing those strings would
    /// mean stashing and restoring every one of them across rebuilds. Instead the mute intercepts the
    /// <see cref="TooltipEvent"/> on its way DOWN to the element under the pointer: an element only fills in
    /// the tooltip text when the event reaches it, so stopping the event one step above the target means no
    /// text is ever produced and Unity shows nothing. Public event API, no reflection — see rule 2.</para>
    ///
    /// <para><b>Live Debug's own chrome is deliberately exempt</b> — the transport bar and every floating card
    /// it opens. A switch whose own controls stop explaining themselves the moment you use it is a trap: you
    /// turn it on and then have no way to work out how to turn it off. It bit immediately, and not on the bar
    /// — the moment card's copy button is an icon with no label, and with the mute on it became a control
    /// nobody could identify. The rule is: this switch silences the FIELDS, because those are what cover the
    /// graphs. It never silences the feature's own furniture.</para>
    ///
    /// <para>EditorPrefs, not SessionState: this is a preference about how one developer likes to read a
    /// graph, and it should still be true tomorrow. Same reasoning as <see cref="DebugRecordSettings"/>.</para>
    /// </summary>
    internal static class DebugTooltipMute
    {
        private const string Key = "SasquatchInspector.LiveDebug.MuteTooltips";

        /// <summary>
        /// Stamped on a build root only while Live Debug is ARMED for that script. It marks the subtree the
        /// mute governs, so a tooltip claim anywhere inside can ask "am I in a muted inspector?" without
        /// holding a reference to the root — see <see cref="SuppressedFor"/>.
        ///
        /// <para><b>Why it comes and goes rather than being stamped once.</b> The switch is a global
        /// preference, but it must only bite where the graphs are. Left on after debug mode is switched off,
        /// it would silence the tooltips on an ordinary-looking inspector with no visible cause — which is
        /// indistinguishable from the package having broken tooltips, and the user would be right to file it
        /// as a bug. Arming is per-root, so turning debug off for one script restores its tooltips
        /// immediately and leaves every other inspector alone.</para>
        /// </summary>
        internal const string HostClass = "sasq-debug-tips-host";

        /// <summary>Raised when the switch flips, so every open transport bar re-paints its pill together.</summary>
        internal static event Action Changed;

        private static bool _cached;
        private static bool _hasCache;

        /// <summary>True while field tooltips are suppressed inside Live Debug inspectors.</summary>
        internal static bool Muted
        {
            get
            {
                // Cached because the suppressor asks this on every TooltipEvent — one per pointer rest, which
                // is not hot, but the pill's Sync() also asks it up to sixty times a second.
                if (!_hasCache) { _cached = EditorPrefs.GetBool(Key, false); _hasCache = true; }
                return _cached;
            }
            set
            {
                if (_hasCache && _cached == value) return;
                _cached = value;
                _hasCache = true;
                EditorPrefs.SetBool(Key, value);
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Arms the mute on one built inspector root. Registered in the TRICKLE-DOWN phase on purpose: the
        /// tooltip text is written by the element the event is travelling TO, so the only place to stop it is
        /// before it gets there.
        /// </summary>
        /// <param name="root">The build root. Every field in the inspector is a descendant of it.</param>
        /// <param name="exempt">Subtree whose tooltips survive the mute — the transport bar.</param>
        internal static void Install(VisualElement root, VisualElement exempt)
        {
            if (root == null) return;

            // NOT armed here. Installing only wires the interception up; DebugRunner.ApplyArmed decides when
            // it is allowed to fire, and follows debug mode being switched on and off.
            root.RegisterCallback<TooltipEvent>(evt =>
            {
                if (!Suppresses(root, evt.target as VisualElement, exempt)) return;

                // Blank it as well as stopping it. Stopping alone is enough on every editor measured, but the
                // panel reads whatever is in the event when the walk ends — writing the empty string means a
                // future dispatch order cannot resurrect a stale caption.
                evt.tooltip = string.Empty;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Arms or disarms the mute for one build root, following Live Debug's own on/off state for that
        /// script. Called from <c>DebugRunner.ApplyArmed</c>, which is the single place that already knows.
        /// </summary>
        internal static void SetArmed(VisualElement root, bool armed)
        {
            root?.EnableInClassList(HostClass, armed);
        }

        /// <summary>
        /// The callback's decision, split out so the rule is testable without driving real hover events —
        /// same shape and same reason as <c>TooltipAnchor.TryResolve</c>.
        /// </summary>
        internal static bool Suppresses(VisualElement host, VisualElement hovered, VisualElement exempt)
        {
            if (!Muted) return false;
            if (host == null || !host.ClassListContains(HostClass)) return false;
            return !IsChrome(hovered, exempt);
        }

        /// <summary>
        /// True when the pointer is over something Live Debug itself drew — the transport bar, or any card it
        /// floats over the inspector.
        ///
        /// <para>Matched by marker CLASS rather than by reference: the cards are built by static helpers with
        /// no handle on the bar, and they come and go while the mute's callback is registered exactly once.
        /// The classes are the same ones the stylesheet positions them by, so a card that stopped carrying one
        /// would lose its framing at the same moment it lost its captions — one visible failure, not two
        /// invisible ones.</para>
        /// </summary>
        private static bool IsChrome(VisualElement hovered, VisualElement exempt)
        {
            for (var el = hovered; el != null; el = el.parent)
            {
                if (el == exempt) return true;
                if (el.ClassListContains("sasq-debug-card")) return true;   // moment card, settings card
                if (el.ClassListContains("sasq-debug-peek")) return true;   // the code peek beside them
            }
            return false;
        }

        /// <summary>
        /// True when <paramref name="owner"/> sits inside a muted Live Debug inspector. Exists because
        /// <see cref="Install"/>'s handler <b>cannot rely on winning the race</b>: <c>TooltipAnchor</c> claims
        /// are registered during the build, ours during installation afterwards, and two trickle-down handlers
        /// on the SAME element run in registration order. So a layout whose ROOT node carries an authored
        /// tooltip would stop the event on the root before the mute ever saw it, and the switch would silently
        /// do nothing on exactly that layout. <c>TooltipAnchor</c> asks this first instead.
        /// </summary>
        internal static bool SuppressedFor(VisualElement owner)
        {
            if (!Muted || owner == null) return false;
            if (IsChrome(owner, null)) return false;
            for (var el = owner; el != null; el = el.parent)
                if (el.ClassListContains(HostClass)) return true;
            return false;
        }

        private static bool IsUnder(VisualElement el, VisualElement ancestor)
        {
            while (el != null)
            {
                if (el == ancestor) return true;
                el = el.parent;
            }
            return false;
        }
    }
}
