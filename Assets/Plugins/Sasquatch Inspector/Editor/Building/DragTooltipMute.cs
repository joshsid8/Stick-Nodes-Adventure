using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Silences every tooltip inside an element for as long as that element is being DRAGGED.
    ///
    /// <para><b>Why it exists.</b> Unity anchors an editor tooltip to a RECT — whichever element under the
    /// pointer answers the <see cref="TooltipEvent"/> hands back its own bounds, and the tooltip window is
    /// placed against them (see <see cref="TooltipAnchor"/>). A row being reordered is the one case where
    /// that rect MOVES: the row travels with the pointer, and the grip the user is holding stays exactly
    /// under the cursor for the whole gesture. So the moment the pointer stops long enough to raise a
    /// tooltip, "Drag to reorder" appears — and then re-anchors on every mouse move, trailing along behind
    /// the cursor. Stock Unity's <c>ReorderableList</c> raises no tooltip at all mid-drag, and this is how
    /// we match it.</para>
    ///
    /// <para><b>How it works.</b> A marker class on the dragged element, plus one interception registered in
    /// the TRICKLE-DOWN phase on that same element. The tooltip text is written by the element the event is
    /// travelling TO, so stopping the event above the target means no text is ever produced — the same
    /// mechanism (and the same reasoning) as <see cref="LiveDebug.DebugTooltipMute"/>. Nothing is stashed and
    /// nothing is restored: the strings on the grip and on everything inside the row are left exactly as the
    /// build wrote them, so a drag that ends abruptly — a lost pointer capture, a rebuild under the drag —
    /// cannot strand a row with its captions blanked.</para>
    ///
    /// <para>The class is the state, deliberately, rather than a bool on the drag closure: a
    /// <see cref="TooltipAnchor"/> claim registered on an element ABOVE the interception would otherwise stop
    /// the event first (two trickle-down handlers on the same element run in registration order, and claims
    /// are installed during the build), so the claim has to be able to ask "am I inside something being
    /// dragged?" without holding a reference to it. Public event API only, no reflection — see rule 2.</para>
    /// </summary>
    internal static class DragTooltipMute
    {
        /// <summary>Stamped on a dragged element for exactly the duration of the gesture — press to the
        /// moment the drop finishes settling and the row is back in the hierarchy.</summary>
        internal const string DraggingClass = "sasq-drag-tooltip-mute";

        /// <summary>
        /// Wires the interception onto <paramref name="dragged"/>. Wiring is not arming: nothing is
        /// suppressed until <see cref="SetDragging"/> stamps the marker.
        /// </summary>
        internal static void Install(VisualElement dragged)
        {
            if (dragged == null) return;
            dragged.RegisterCallback<TooltipEvent>(evt =>
            {
                if (!SuppressedFor(evt.target as VisualElement)) return;
                // Blank as well as stop: the panel reads whatever is left in the event when the walk ends,
                // so writing the empty string means no dispatch order can resurrect a stale caption.
                evt.tooltip = string.Empty;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Arms or disarms the mute for one dragged element.</summary>
        internal static void SetDragging(VisualElement dragged, bool dragging)
        {
            dragged?.EnableInClassList(DraggingClass, dragging);
        }

        /// <summary>
        /// True when <paramref name="element"/> sits inside something that is currently being dragged.
        /// Split out from the callback so the rule is testable without driving real hover events — same
        /// shape and same reason as <c>TooltipAnchor.TryResolve</c>.
        /// </summary>
        internal static bool SuppressedFor(VisualElement element)
        {
            for (var el = element; el != null; el = el.parent)
            {
                if (el.ClassListContains(DraggingClass)) return true;
            }
            return false;
        }
    }
}
