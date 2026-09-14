using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Decides WHERE a node's tooltip appears, and makes it stay there.
    /// <para>
    /// Unity anchors an editor tooltip to a RECT, not to the pointer: whichever element under the pointer
    /// claims the <see cref="TooltipEvent"/> hands back its own bounds, and the tooltip window is placed
    /// against them. The deepest element with a non-empty <c>tooltip</c> wins, so writing one tooltip onto
    /// a widget AND every element inside it — which is what a recursive apply does, and the only way we
    /// could previously beat the tooltip Unity stamps on a bound control — hands back a DIFFERENT rect for
    /// every sub-part the pointer crosses. The tooltip then re-anchors on each mouse move and reads as if
    /// it were dragging along behind the cursor: worst on the widgets with the most inner parts (an
    /// expandable curve or gradient, an asset preview, an in-line editor, a card).
    /// </para>
    /// <para>
    /// So the tooltip is claimed ONCE, by the node's own element, in the TRICKLE-DOWN phase — before
    /// anything inside it can answer — and anchored to one stable rect for the whole subtree. Trickling
    /// down is also what makes the claim beat a control's own tooltip without touching it: Unity stamps
    /// the field's C# <c>[Tooltip]</c> onto the bound control LATE (both <c>PropertyField</c> and a plain
    /// <c>BindProperty</c> do it, on the <c>.unity-base-field</c> root), which is why the old apply had to
    /// be re-run after the first layout pass. A claim needs neither the recursion nor the deferral, since
    /// it answers at hover time rather than racing the build.
    /// </para>
    /// <para><b>The anchor</b> is the element that NAMES the node — its field label, or its container /
    /// widget title — falling back to the node's own box when it has neither. That is stock behaviour
    /// (Unity anchors a field's tooltip to its label, not to the whole row), and it is what keeps the
    /// tooltip in a sensible place when the node is tall: an expanded curve anchors under its header
    /// instead of ~200px below, at the bottom edge of the body.
    /// </para>
    /// <para><b>Two flavours</b>, because "what may out-claim me" differs:
    /// <list type="bullet">
    /// <item><see cref="ClaimField"/> — a bound widget. Everything inside it is that widget's own chrome
    /// (a stock control's stamped attribute, a scrub hint, a "Frame the whole curve (F)" button), and an
    /// author's own tooltip out-ranks all of it — the rule the manual states and the recursive apply
    /// enforced by flattening the subtree. Only a NESTED node's claim wins, so an in-line editor's fields
    /// still speak for themselves.</item>
    /// <item><see cref="ClaimNode"/> — a container or any other node. It yields to ANY tooltip inside it,
    /// which is what a container has always done by bubbling: the field you are pointing at wins over the
    /// card it happens to sit in.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Mirrored by <c>SasqKit.SetTooltip</c> / <c>SasqKit.SetNodeTooltip</c> in <c>ExportedWidgetKit.cs</c>
    /// — an exported editor is a second renderer of the same model and has to place tooltips the same way.
    /// Keep the two in lockstep.
    /// </para>
    /// </summary>
    internal static class TooltipAnchor
    {
        /// <summary>Labels that NAME a node, in the order a node carries them. The container half mirrors
        /// <c>LayoutTreeBuilder.ApplyNodeTextStyles</c>'s title list (the classes a container's title
        /// wears) — a title that stops carrying its marker loses its styling AND its tooltip anchor
        /// together, which is the intended single failure.</summary>
        private static readonly string[] AnchorLabelClasses =
        {
            FieldLabelColumn.ColumnLabelClass, // our own field/header rows
            "sasq-node-title",                 // Card / Columns / Group / Tabs titles
            "sasq-group-title",                // an expandable widget's header (curve, gradient, preview…)
            "sasq-flow-header",
            "unity-foldout__text",             // a Foldout container's header text
            "unity-base-field__label",         // a stock control's label, when the widget wraps one
        };

        // A node's own name is always within a few levels of its root (box → header → align row → title is
        // the deepest shape we build). Bounded so a hover can never walk a large subtree, and so a nested
        // widget's label can't be mistaken for this node's.
        private const int MaxAnchorDepth = 4;
        private const int MaxAnchorVisits = 64;

        private enum Reach
        {
            /// <summary>Yields to any tooltip inside — a container never speaks over its own contents.</summary>
            Node,
            /// <summary>Speaks for the whole widget: the chrome inside it (a stamped <c>[Tooltip]</c>, a
            /// widget's own hint) is out-ranked. Only a nested node's claim wins.</summary>
            Field,
        }

        private sealed class Claim
        {
            public VisualElement Owner;
            public string Tip;
            public Reach Reach;

            public void OnTooltip(TooltipEvent evt)
            {
                // A Live Debug inspector with TIPS switched off suppresses its whole subtree, and it has to be
                // asked HERE rather than out-raced: claims register during the build and the mute registers
                // afterwards, so on a layout whose root node carries a tooltip this claim would stop the event
                // before the mute's own handler ever ran.
                // Same shape for a row that is mid-drag-reorder, and for the same reason: a claim on or above
                // the dragged row is registered during the build and would stop the event before
                // DragTooltipMute's own interception ever ran.
                if (LiveDebug.DebugTooltipMute.SuppressedFor(Owner) || DragTooltipMute.SuppressedFor(Owner))
                {
                    evt.tooltip = string.Empty;
                    evt.StopImmediatePropagation();
                    return;
                }
                if (!TryResolve(Owner, evt.target as VisualElement, out string tip, out Rect rect)) return;
                evt.tooltip = tip;
                evt.rect = rect;
                // Trickle-down + stop = "this subtree's tooltip is settled" — nothing below re-answers with
                // its own rect, which is the whole point.
                evt.StopImmediatePropagation();
            }
        }

        private static readonly ConditionalWeakTable<VisualElement, Claim> Claims =
            new ConditionalWeakTable<VisualElement, Claim>();

        /// <summary>
        /// Claims <paramref name="tip"/> for a bound widget and everything inside it. An EMPTY tip is
        /// meaningful and must still be claimed: it is how a blanked tooltip suppresses the C#
        /// <c>[Tooltip]</c> Unity stamps on the control itself.
        /// </summary>
        internal static void ClaimField(VisualElement widget, string tip) => Install(widget, tip, Reach.Field);

        /// <summary>Claims <paramref name="tip"/> for a container (or any other node), yielding to whatever
        /// the pointer is actually over inside it.</summary>
        internal static void ClaimNode(VisualElement element, string tip) => Install(element, tip, Reach.Node);

        private static void Install(VisualElement owner, string tip, Reach reach)
        {
            if (owner == null) return;
            string resolved = tip ?? string.Empty;

            // The property stays in sync with the claim: an ANCESTOR's claim reads it to decide whether to
            // yield, and it is what a reader (a test, a screen reader, our own search) expects to find.
            owner.tooltip = resolved;

            if (Claims.TryGetValue(owner, out var existing))
            {
                existing.Tip = resolved;
                // A widget claims before the shared style pass reaches the same element (an unwrapped field
                // is one element, claimed twice). Never let the later, less informed pass downgrade the
                // reach — that would hand the widget back to the stamped [Tooltip] it just replaced.
                if (reach == Reach.Field) existing.Reach = Reach.Field;
                return;
            }

            var claim = new Claim { Owner = owner, Tip = resolved, Reach = reach };
            Claims.Add(owner, claim);
            owner.RegisterCallback<TooltipEvent>(claim.OnTooltip, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// The claim's answer for a pointer over <paramref name="hovered"/>: false when something inside
        /// owns this pixel and the claim must stand aside. Split out from the callback so the rule is
        /// testable without driving real hover events.
        /// </summary>
        internal static bool TryResolve(VisualElement owner, VisualElement hovered, out string tip, out Rect rect)
        {
            tip = null;
            rect = default;
            if (owner == null) return false;
            if (!Claims.TryGetValue(owner, out var claim)) return false;
            if (hovered != null && Yields(claim, hovered)) return false;

            tip = claim.Tip;
            rect = ResolveAnchor(owner);
            return true;
        }

        /// <summary>True when an element between the pointer and the claim owner owns the tooltip instead.</summary>
        private static bool Yields(Claim claim, VisualElement hovered)
        {
            for (var ve = hovered; ve != null && ve != claim.Owner; ve = ve.parent)
            {
                // A nested node's own claim always wins — the innermost thing the AUTHOR labelled is the one
                // they meant, exactly as bubbling would have resolved it. Both reaches honour this.
                if (Claims.TryGetValue(ve, out _)) return true;
                // Everything else inside a widget is that widget's own chrome: the C# [Tooltip] Unity stamps
                // on the bound control (which this claim exists to replace or blank) and the widget's own
                // hints. An authored tooltip out-ranks all of it. A container yields to it instead.
                if (claim.Reach == Reach.Node && !string.IsNullOrEmpty(ve.tooltip)) return true;
            }
            return false;
        }

        /// <summary>
        /// The rect <paramref name="owner"/>'s tooltip anchors to: the label or title that names it, else
        /// the node's own box.
        /// </summary>
        internal static Rect ResolveAnchor(VisualElement owner)
        {
            if (owner == null) return default;
            var label = FindAnchorLabel(owner);
            if (label != null)
            {
                var labelRect = label.worldBound;
                if (IsUsable(labelRect)) return labelRect;
            }
            return owner.worldBound;
        }

        /// <summary>
        /// The label that names <paramref name="owner"/>, or null. Breadth-first and bounded, so the
        /// shallowest name wins, and never descends into a nested node — a card's tooltip must not end up
        /// hanging off the first field inside it.
        /// </summary>
        private static Label FindAnchorLabel(VisualElement owner)
        {
            var direct = FieldLabelColumn.DirectLabel(owner);
            if (IsUsableLabel(direct)) return direct;

            var frontier = new List<VisualElement>();
            var next = new List<VisualElement>();
            frontier.Add(owner);

            int visits = 0;
            for (int depth = 0; depth < MaxAnchorDepth && frontier.Count > 0; depth++)
            {
                foreach (var parent in frontier)
                {
                    foreach (var child in parent.Children())
                    {
                        if (++visits > MaxAnchorVisits) return null;
                        // Another node's subtree: its labels name IT, not us.
                        if (LayoutNodeRegistry.GetNode(child) != null) continue;
                        if (child is Label label && IsUsableLabel(label) && CarriesAnchorClass(label)) return label;
                        next.Add(child);
                    }
                }
                frontier.Clear();
                var swap = frontier; frontier = next; next = swap;
            }
            return null;
        }

        private static bool CarriesAnchorClass(VisualElement element)
        {
            foreach (var cls in AnchorLabelClasses)
            {
                if (element.ClassListContains(cls)) return true;
            }
            return false;
        }

        /// <summary>A label can only anchor a tooltip if it is actually on screen with text in it — a
        /// blanked or hidden label measures nothing, and would drop the tooltip at the panel origin.</summary>
        private static bool IsUsableLabel(Label label)
        {
            if (label == null || string.IsNullOrEmpty(label.text)) return false;
            if (label.resolvedStyle.display == DisplayStyle.None) return false;
            return IsUsable(label.worldBound);
        }

        private static bool IsUsable(Rect rect) =>
            !float.IsNaN(rect.x) && !float.IsNaN(rect.y) && rect.width > 0f && rect.height > 0f;
    }
}
