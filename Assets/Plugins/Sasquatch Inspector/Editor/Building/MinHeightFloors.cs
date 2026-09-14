using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Arbitrates the several INDEPENDENT "grow-only floor" claims that all land on one element's
    /// <c>style.minHeight</c>. Vertical sizing is always a min-height floor and never a fixed height (see
    /// StyleApplier), which means three unrelated features end up writing the same property on the same
    /// element — and a bare write by any one of them silently erases the others:
    /// <list type="bullet">
    /// <item><b>Row</b> — a Rows container's authored per-row height (<see cref="LayoutNode.RowHeight"/>),
    /// applied by <c>LayoutTreeBuilder.ApplyRowsLayout</c> and updated live by the row/track resizers.</item>
    /// <item><b>Node</b> — the node's own bottom-edge resize grip (<c>MakeHeightResizable</c> +
    /// <c>Style.height</c>), applied by <c>StyleApplier</c> and updated live by <c>AttachNodeResizeDrag</c>.</item>
    /// <item><b>Widget</b> — a widget's intrinsic floor, e.g. a styled slider growing its control so an
    /// oversized knob fits (<c>BindingFactory.Slider</c>).</item>
    /// </list>
    /// <para>
    /// The widget claim is the dangerous one, because it is re-asserted from a <c>GeometryChangedEvent</c>
    /// handler: it re-runs on EVERY layout pass and therefore always gets the last word. That is what made an
    /// authored row height vanish in the live inspector while the designer kept it (design mode wraps every
    /// leaf in a plain card, so the row floor lands on the wrapper where no widget can reach it) — and what
    /// made dragging a row divider look inert, since the model updated and persisted correctly but the style
    /// was stomped back before the next paint.
    /// </para>
    /// <para>
    /// The floors COMPOSE: the applied value is the largest live claim, so "grow-only" holds against every
    /// one of them and no claim can shrink the element below another's. All three clear with 0.
    /// </para>
    /// </summary>
    internal static class MinHeightFloors
    {
        private sealed class Floors
        {
            public float Row;
            public float Node;
            public float Widget;
        }

        private static readonly ConditionalWeakTable<VisualElement, Floors> _floors =
            new ConditionalWeakTable<VisualElement, Floors>();

        /// <summary>A Rows/track container's authored per-row floor. 0 ⇒ the row returns to auto.</summary>
        internal static void SetRow(VisualElement element, float height)
        {
            if (element == null) return;
            if (height <= 0f && !_floors.TryGetValue(element, out _)) return; // nothing claimed, nothing to clear
            var f = Claim(element);
            f.Row = Mathf.Max(0f, height);
            Apply(element, f);
        }

        /// <summary>The node's own height-resize grip floor (<c>Style.height</c>). 0 ⇒ natural height.</summary>
        internal static void SetNode(VisualElement element, float height)
        {
            if (element == null) return;
            if (height <= 0f && !_floors.TryGetValue(element, out _)) return;
            var f = Claim(element);
            f.Node = Mathf.Max(0f, height);
            Apply(element, f);
        }

        /// <summary>
        /// A widget's own intrinsic floor (an oversized slider knob, a text box's minimum lines …). Call this
        /// instead of writing <c>style.minHeight</c> directly from anything that can re-run after build —
        /// a <c>GeometryChangedEvent</c> handler that writes the property raw will erase the row/node floors.
        /// </summary>
        internal static void SetWidget(VisualElement element, float height)
        {
            if (element == null) return;
            if (height <= 0f && !_floors.TryGetValue(element, out _))
            {
                // No arbitration in play yet — honour the caller's plain "clear" so a widget that reverts to
                // stock geometry before any container has claimed it behaves exactly as it always did.
                element.style.minHeight = StyleKeyword.Null;
                return;
            }
            var f = Claim(element);
            f.Widget = Mathf.Max(0f, height);
            Apply(element, f);
        }

        /// <summary>
        /// The widget claim standing on this element right now, or its inline min-height when nothing has
        /// claimed yet. A pass that re-asserts its own floor on every layout must compare against THIS, never
        /// against <c>resolvedStyle.minHeight</c>: the applied value is the MAX of all three claims, so a
        /// larger Row or Node claim would read to that pass as "my floor did not take" and it would schedule
        /// a correcting write every single pass.
        /// </summary>
        internal static float WidgetFloorOf(VisualElement element)
        {
            if (element == null) return 0f;
            return _floors.TryGetValue(element, out var f) ? f.Widget : ReadInlinePixels(element);
        }

        /// <summary>
        /// First claim on an element adopts whatever inline min-height is already sitting there as the WIDGET
        /// floor. Widgets that set their floor once at build time (a multiline text box, a sized button) never
        /// call in here, so without this a container's row floor would silently shrink them below their own
        /// minimum the moment it was applied.
        /// </summary>
        private static Floors Claim(VisualElement element)
        {
            if (_floors.TryGetValue(element, out var existing)) return existing;
            var f = new Floors { Widget = ReadInlinePixels(element) };
            _floors.Add(element, f);
            return f;
        }

        /// <summary>The element's inline min-height in pixels, or 0 when unset or expressed in any other unit
        /// (a percentage floor is not comparable with the pixel claims, so it is left alone rather than fought).</summary>
        private static float ReadInlinePixels(VisualElement element)
        {
            var style = element.style.minHeight;
            if (style.keyword != StyleKeyword.Undefined) return 0f;      // Undefined == "a real value is present"
            if (style.value.unit != LengthUnit.Pixel) return 0f;
            return Mathf.Max(0f, style.value.value);
        }

        private static void Apply(VisualElement element, Floors f)
        {
            float floor = Mathf.Max(f.Row, Mathf.Max(f.Node, f.Widget));
            element.style.minHeight = floor > 0f ? (StyleLength)floor : (StyleLength)StyleKeyword.Null;
        }
    }

    /// <summary>
    /// A plain wrapper that carries a Rows container's authored row height so the WIDGET never has to.
    /// <para>
    /// Making a widget element taller than its content deforms it: every part that centres or fills — a
    /// slider knob and its track (<c>top: 50%</c> of the grown container), a unit suffix, rating stars, enum
    /// badges, stepper buttons, an XY pad's readouts, a curve well — drifts toward the middle of the new
    /// height while the label stays put. Neutralising that per element is not possible in general: Unity's
    /// own theme gives <c>.unity-base-slider__input</c> a percentage height, so it re-inflates against the
    /// row no matter what the direct children are pinned to. Measured across the kitchen-sink layout, ten
    /// widget families drifted.
    /// </para>
    /// <para>
    /// So the row element is left at its NATURAL height and the reserved space goes on a host around it —
    /// exactly what design mode has always done (it wraps every leaf in a card, which is why the designer
    /// rendered these rows correctly while the live inspector did not). The host is a column box, so its
    /// child stretches horizontally and keeps its own height, pinned to the top.
    /// </para>
    /// <para>
    /// Only LIVE leaf rows need one: design mode already supplies a wrapper, and our own container elements
    /// have no Unity internals to deform. <see cref="ElementVisibility"/> mirrors a hide onto the host, or a
    /// condition-hidden row would leave its reserved height behind as an empty gap.
    /// </para>
    /// </summary>
    internal static class RowFloorHost
    {
        internal const string HostClass = "sasq-row-floor-host";

        /// <summary>The host wrapping <paramref name="row"/>, or null when it has none.</summary>
        internal static VisualElement Of(VisualElement row)
        {
            var parent = row?.parent;
            return parent != null && parent.ClassListContains(HostClass) ? parent : null;
        }

        /// <summary>
        /// The host for <paramref name="row"/>, creating it on first use. Returns <paramref name="row"/>
        /// itself when a host is neither needed nor possible (already-wrapped design rows, container rows,
        /// or an element not yet in a hierarchy) so callers can floor the result unconditionally.
        /// </summary>
        internal static VisualElement Ensure(VisualElement row, bool needed)
        {
            if (row == null) return null;
            var existing = Of(row);
            if (existing != null) return existing;
            if (!needed) return row;

            var parent = row.parent;
            if (parent == null) return row;

            int index = parent.IndexOf(row);
            var host = new VisualElement();
            host.AddToClassList(HostClass);
            // The host is pure layout: it must never swallow a click meant for the widget inside it.
            // Ignore skips only the host itself — descendants stay pickable.
            host.pickingMode = PickingMode.Ignore;
            host.style.flexDirection = FlexDirection.Column;
            // Inherit the row's cross-axis behaviour so wrapping is layout-neutral for an author who set
            // "Align in Parent" on the field: that choice belongs to the box the container now lays out.
            host.style.alignSelf = row.style.alignSelf;

            row.RemoveFromHierarchy();
            host.Add(row);
            parent.Insert(index, host);
            return host;
        }
    }
}
