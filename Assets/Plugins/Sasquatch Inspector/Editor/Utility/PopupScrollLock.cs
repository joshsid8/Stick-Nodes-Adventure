using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Freezes wheel scrolling in the window a picker popup was opened FROM, for as long as that popup is open.
    ///
    /// This matches what stock Unity does, for a reason that only looks cosmetic. Unity's own "Select Function"
    /// menu is a <c>GenericMenu</c>, which on every desktop platform is a NATIVE menu running its own modal
    /// message loop — while it is up the editor processes no other input at all, so the inspector behind it
    /// simply cannot scroll. Our pickers are <c>PopupWindowContent</c> in a real (non-modal) popup window, so
    /// they get none of that for free: the popup is pinned to the screen rect of the control that opened it,
    /// while the control itself is free to slide away underneath. Scroll once and the menu is left hanging over
    /// an unrelated field — and a code peek hung off the popup's edge points at nothing.
    ///
    /// The block is a capture-phase <see cref="WheelEvent"/> handler on the host panel's root: stopping there
    /// is before any ScrollView (or IMGUIContainer) in that window has seen the event, and it leaves the
    /// POPUP's own list scrollable, because a popup window is a separate panel with its own event dispatch.
    /// </summary>
    internal sealed class PopupScrollLock
    {
        private VisualElement _root;
        private EventCallback<WheelEvent> _block;

        /// <summary>Engages the lock on the panel <paramref name="anchor"/> lives in. A null anchor, or one
        /// that isn't attached to a panel, is a silent no-op — the popup simply gets no scroll lock.</summary>
        internal void Engage(VisualElement anchor)
        {
            Release();
            var root = anchor?.panel?.visualTree;
            if (root == null) return;

            _root = root;
            // Stopping in the capture phase at the panel root means the event never reaches a target at all,
            // so neither a ScrollView's callback nor an IMGUIContainer's default handling ever sees it. (This
            // is also why there's no PreventDefault call: Unity 6 folded that into StopPropagation and
            // deprecated it.)
            _block = evt => evt.StopImmediatePropagation();
            _root.RegisterCallback(_block, TrickleDown.TrickleDown);
        }

        /// <summary>Releases the lock. Safe to call when it was never engaged, and safe to call twice — which
        /// matters, because a popup can close through several paths.</summary>
        internal void Release()
        {
            if (_root != null && _block != null) _root.UnregisterCallback(_block, TrickleDown.TrickleDown);
            _root = null;
            _block = null;
        }

        /// <summary>Whether the lock is currently holding a registration (test seam).</summary>
        internal bool Engaged => _root != null;
    }
}
