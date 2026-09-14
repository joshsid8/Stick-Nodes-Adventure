using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Safety net for a layout that paints past the inspector's right edge. Unity's Inspector window scrolls
    /// vertically only, and a too-wide row doesn't even register as scrollable overflow — the row's own box
    /// stays viewport-width while its CHILDREN paint outside it, so the clipped content is simply unreachable.
    /// This wrapper polls the body for painted right-edge overflow and, only while some exists, shows a slim
    /// horizontal scroller (bottom edge) that pans the body via translate so the stray content can be seen.
    /// A correct layout never shows it: rows are built to shrink-to-fit, so the scroller appearing at all
    /// means some widget is misbehaving — the point is that the user can still SEE what's off, not scroll as
    /// a way of life.
    /// </summary>
    public static class HorizontalOverflowRescue
    {
        // Ignore the few-px native margin quirks (.unity-base-field ±3px) — only real breakage shows the rail.
        // internal so the overflow regression sweep asserts against THIS number: a test with its own threshold
        // either fails on rows the user never sees a rail for, or passes on rows where the rail is showing.
        internal const float OverflowThreshold = 4f;
        private const long ScanIntervalMs = 400;
        private const float ScrollerHeight = 13f;

        /// <summary>Wraps an inspector body so painted right-edge overflow becomes reachable. Layout-neutral
        /// while everything fits (no extra padding, no scroller, translate untouched).</summary>
        public static VisualElement Wrap(VisualElement body)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("sasq-overflow-rescue");
            wrapper.Add(body);

            Scroller scroller = null;

            void ApplyPan(float value)
            {
                body.style.translate = new Translate(-value, 0);
            }

            wrapper.schedule.Execute(() =>
            {
                float viewportWidth = wrapper.resolvedStyle.width;
                if (float.IsNaN(viewportWidth) || viewportWidth <= 0f) return;

                // LAYOUT-space measurement, deliberately blind to render transforms: a hover/press
                // animation that scales a button paints past the edge for a moment, and worldBound bakes
                // that transient scale in — summoning the rail on every hover of an edge-to-edge widget.
                // Layout rects ignore transforms entirely (including the pan translate itself, so no
                // compensation needed), leaving only genuine layout breakage to show the rail.
                float contentRight = MaxLayoutRight(body, body.layout.x);
                float overflow = contentRight - viewportWidth;

                if (overflow > OverflowThreshold)
                {
                    if (scroller == null)
                    {
                        scroller = new Scroller(0f, overflow, ApplyPan, SliderDirection.Horizontal);
                        scroller.style.position = Position.Absolute;
                        scroller.style.left = 0;
                        scroller.style.right = 0;
                        scroller.style.bottom = 0;
                        scroller.style.height = ScrollerHeight;
                        wrapper.Add(scroller);
                    }
                    scroller.highValue = overflow; // clamps value (and re-pans) if the overflow shrank
                    scroller.style.display = DisplayStyle.Flex;
                    // Reserve the rail's strip so it doesn't sit on top of the last row.
                    wrapper.style.paddingBottom = ScrollerHeight;
                }
                else if (scroller != null && scroller.resolvedStyle.display != DisplayStyle.None)
                {
                    // Overflow resolved (layout fixed / inspector widened): snap home and disappear.
                    scroller.value = 0f;
                    ApplyPan(0f);
                    scroller.style.display = DisplayStyle.None;
                    wrapper.style.paddingBottom = 0;
                }
            }).Every(ScanIntervalMs);

            return wrapper;
        }

        // Stock text inputs paint their inner text element at full content width and clip it via the
        // default stylesheet — no inline overflow to observe — so they're pruned by class instead.
        private static readonly string TextInputViewportClass = TextInputBaseField<string>.inputUssClassName;

        /// <summary>
        /// Rightmost REACHABLE laid-out edge in the subtree, in the wrapper's coordinate space
        /// (<paramref name="left"/> = the element's accumulated layout-x). Reachable meaning panning the
        /// body left would actually reveal it. Content clipped by an ancestor INSIDE the body (a frame, a
        /// scroll viewport) can never be revealed — that clip translates along with its content when
        /// panning — so recursion stops at the clipper's own box. Every Sasquatch clipper sets overflow
        /// INLINE (frames, preview wraps, ellipsized labels), so the inline style is authoritative here;
        /// the remaining stylesheet-driven clippers are ScrollView viewports and stock text-input viewports
        /// (a long field value paints its inner text element wider than the input box), each pruned at the
        /// clipper itself. Walks the full hierarchy (shadow children included) so widget-internal elements
        /// are covered. Uses layout rects, NOT worldBound — see the scan-loop comment.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> rather than private so the overflow regression suite can ask THIS function whether
        /// a row spills, instead of reimplementing the walk. A test-local copy would drift from the pruning
        /// rules above (inline overflow, ScrollView, stock text-input viewports) and then pass while the real
        /// rail still fires — which is the only thing the user ever sees.
        /// </remarks>
        internal static float MaxLayoutRight(VisualElement el, float left)
        {
            if (el.resolvedStyle.display == DisplayStyle.None) return float.NegativeInfinity;

            var rect = el.layout;
            if (float.IsNaN(rect.width)) return float.NegativeInfinity;
            float right = left + rect.width;

            if (el.style.overflow.value == Overflow.Hidden || el is ScrollView ||
                el.ClassListContains(TextInputViewportClass)) return right;

            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = el.hierarchy[i];
                float childRight = MaxLayoutRight(child, left + child.layout.x);
                if (childRight > right) right = childRight;
            }
            return right;
        }
    }
}
