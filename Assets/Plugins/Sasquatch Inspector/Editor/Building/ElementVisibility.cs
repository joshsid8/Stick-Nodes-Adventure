using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The single writer for <c>element.style.display</c> on layout-node elements that MORE THAN ONE feature
    /// may hide: conditions (<see cref="ConditionBinder"/>), the inspector field search
    /// (<see cref="InspectorFieldSearch"/>), and a third-party attribute drawer that collapses its field
    /// (<see cref="DrawerVisibilityBridge"/>). With independent writers on the same style property, a
    /// condition-hidden field would reappear the moment the search cleared its own hide (and vice versa) —
    /// so all of them route through this arbiter, which folds every active hide reason into one flags mask
    /// and derives display from the whole mask: visible only when NO reason wants the element hidden.
    ///
    /// A fourth joined later: the designer canvas drag (<see cref="CanvasDragController"/>) hides the cards
    /// of the nodes in hand, and its abort paths used to write <c>DisplayStyle.Flex</c> straight back onto
    /// everything they hid — force-revealing a card the canvas SEARCH filter was also hiding.
    ///
    /// Otherwise deliberately scoped. Tab-child switching and group-content folding hide DIFFERENT
    /// elements (a container's content box, not the node element) on a different axis and keep their direct
    /// writes. The arbiter never animates — reveal animations (PopIn) stay owned by the feature that decided
    /// to reveal (see ConditionBinder's lastVisible gate).
    ///
    /// State is keyed via ConditionalWeakTable — the same idiom as <see cref="LayoutNodeRegistry"/>, and for
    /// the same reason: Unity's controls/binding machinery can clobber <c>userData</c> on live elements, and
    /// weak keys let rebuilt trees drop their entries with the old elements.
    /// </summary>
    public static class ElementVisibility
    {
        /// <summary>One bit per feature that may hide a node element; the element renders only when the mask is None.</summary>
        [Flags]
        public enum Reason
        {
            None = 0,
            /// <summary>Hidden by a condition rule (<see cref="ConditionBinder"/>).</summary>
            Condition = 1,
            /// <summary>Hidden by the inspector field search filter (<see cref="InspectorFieldSearch"/>).</summary>
            SearchFilter = 2,
            /// <summary>Collapsed to nothing by the field's own third-party PropertyDrawer (<see cref="DrawerVisibilityBridge"/>).</summary>
            ExternalDrawer = 4,
            /// <summary>Lifted out of its layout for the duration of a designer canvas drag (<see cref="CanvasDragController"/>).</summary>
            Drag = 8,
        }

        private sealed class Box
        {
            public Reason Mask;
        }

        private static readonly ConditionalWeakTable<VisualElement, Box> Map =
            new ConditionalWeakTable<VisualElement, Box>();

        /// <summary>
        /// Adds or removes <paramref name="reason"/> from <paramref name="el"/>'s hide mask and writes the
        /// resolved display style: Flex when no reason remains, None otherwise. The write is unconditional
        /// (not change-gated) so that a caller re-asserting its state — ConditionBinder re-evaluates on every
        /// tracked change — makes the last evaluation win over any stray inline <c>style.display</c> write.
        /// </summary>
        public static void SetHidden(VisualElement el, Reason reason, bool hidden)
        {
            if (el == null || reason == Reason.None) return;
            var box = Map.GetOrCreateValue(el);
            box.Mask = hidden ? (box.Mask | reason) : (box.Mask & ~reason);
            var display = box.Mask == Reason.None ? DisplayStyle.Flex : DisplayStyle.None;
            el.style.display = display;
            // A row its Rows container gave an authored height sits inside a RowFloorHost that carries the
            // reserved space. Hiding only the row would leave that height behind as an empty gap, so the
            // host follows it — the host has no state of its own, it simply mirrors the arbitrated result.
            var floorHost = RowFloorHost.Of(el);
            if (floorHost != null) floorHost.style.display = display;
        }

        /// <summary>True when <paramref name="reason"/> is currently part of <paramref name="el"/>'s hide mask.</summary>
        public static bool IsHiddenBy(VisualElement el, Reason reason)
        {
            if (el == null) return false;
            return Map.TryGetValue(el, out var box) && (box.Mask & reason) != 0;
        }
    }
}
