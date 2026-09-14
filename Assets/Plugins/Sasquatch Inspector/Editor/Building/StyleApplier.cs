using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>Applies a node's <see cref="LayoutStyle"/> (and any resolved theme token) to its rendered element.</summary>
    public static class StyleApplier
    {
        /// <summary>Applies <paramref name="node"/>'s layout style (margins, padding, size, border, background, flex) to <paramref name="element"/>, resolving any <paramref name="theme"/> token.</summary>
        public static void Apply(VisualElement element, LayoutNode node, SasquatchThemeAsset theme)
        {
            if (element == null || node == null) return;

            LayoutStyle baseStyle = ResolveBaseStyle(node.StyleToken, theme);
            LayoutStyle overrideStyle = node.Style;
            bool hasTheme = theme != null;

            // Flex Direction
            if (overrideStyle.flexDirection.isOverridden)
                element.style.flexDirection = overrideStyle.flexDirection.value;
            else if (hasTheme && baseStyle.flexDirection.isOverridden)
                element.style.flexDirection = baseStyle.flexDirection.value;

            // Align Self
            if (overrideStyle.alignSelf.isOverridden)
                element.style.alignSelf = overrideStyle.alignSelf.value;
            else if (hasTheme && baseStyle.alignSelf.isOverridden)
                element.style.alignSelf = baseStyle.alignSelf.value;

            // Align Items
            if (overrideStyle.alignItems.isOverridden)
                element.style.alignItems = overrideStyle.alignItems.value;
            else if (hasTheme && baseStyle.alignItems.isOverridden)
                element.style.alignItems = baseStyle.alignItems.value;

            // Justify
            if (overrideStyle.justify.isOverridden)
                element.style.justifyContent = overrideStyle.justify.value;
            else if (hasTheme && baseStyle.justify.isOverridden)
                element.style.justifyContent = baseStyle.justify.value;

            // Corner Radius
            if (overrideStyle.borderRadius.isOverridden)
                SetBorderRadius(element, overrideStyle.borderRadius.value);
            else if (hasTheme && baseStyle.borderRadius.isOverridden)
                SetBorderRadius(element, baseStyle.borderRadius.value);

            // Border Width
            if (overrideStyle.borderWidth.isOverridden)
                SetBorderWidth(element, overrideStyle.borderWidth.value);
            else if (hasTheme && baseStyle.borderWidth.isOverridden)
                SetBorderWidth(element, baseStyle.borderWidth.value);

            // Background Color
            if (overrideStyle.backgroundColor.isOverridden)
                element.style.backgroundColor = overrideStyle.backgroundColor.value;
            else if (hasTheme && baseStyle.backgroundColor.isOverridden)
                element.style.backgroundColor = baseStyle.backgroundColor.value;

            // Border Color
            if (overrideStyle.borderColor.isOverridden)
                SetBorderColor(element, overrideStyle.borderColor.value);
            else if (hasTheme && baseStyle.borderColor.isOverridden)
                SetBorderColor(element, baseStyle.borderColor.value);

            // Margins
            if (overrideStyle.marginTop.isOverridden) element.style.marginTop = overrideStyle.marginTop.value;
            else if (hasTheme && baseStyle.marginTop.isOverridden) element.style.marginTop = baseStyle.marginTop.value;

            if (overrideStyle.marginBottom.isOverridden) element.style.marginBottom = overrideStyle.marginBottom.value;
            else if (hasTheme && baseStyle.marginBottom.isOverridden) element.style.marginBottom = baseStyle.marginBottom.value;

            if (overrideStyle.marginLeft.isOverridden) element.style.marginLeft = overrideStyle.marginLeft.value;
            else if (hasTheme && baseStyle.marginLeft.isOverridden) element.style.marginLeft = baseStyle.marginLeft.value;

            if (overrideStyle.marginRight.isOverridden) element.style.marginRight = overrideStyle.marginRight.value;
            else if (hasTheme && baseStyle.marginRight.isOverridden) element.style.marginRight = baseStyle.marginRight.value;

            // Paddings
            if (overrideStyle.paddingTop.isOverridden) element.style.paddingTop = overrideStyle.paddingTop.value;
            else if (hasTheme && baseStyle.paddingTop.isOverridden) element.style.paddingTop = baseStyle.paddingTop.value;

            if (overrideStyle.paddingBottom.isOverridden) element.style.paddingBottom = overrideStyle.paddingBottom.value;
            else if (hasTheme && baseStyle.paddingBottom.isOverridden) element.style.paddingBottom = baseStyle.paddingBottom.value;

            if (overrideStyle.paddingLeft.isOverridden) element.style.paddingLeft = overrideStyle.paddingLeft.value;
            else if (hasTheme && baseStyle.paddingLeft.isOverridden) element.style.paddingLeft = baseStyle.paddingLeft.value;

            if (overrideStyle.paddingRight.isOverridden) element.style.paddingRight = overrideStyle.paddingRight.value;
            else if (hasTheme && baseStyle.paddingRight.isOverridden) element.style.paddingRight = baseStyle.paddingRight.value;

            // Width / Height
            bool widthApplied = false;
            if (overrideStyle.width.isOverridden) { element.style.width = overrideStyle.width.value; widthApplied = true; }
            else if (hasTheme && baseStyle.width.isOverridden) { element.style.width = baseStyle.width.value; widthApplied = true; }

            // A stored Width is a DESIGNED width, never a promise the inspector is that wide. UITK flex items
            // default to flex-shrink 0 and a fixed cross-axis width is not shrunk by the parent at all, so a
            // node designed at 400 in a 240px inspector laid out at 400 anyway — starting at x = -94 and
            // painting straight over its neighbours (measured). Nothing clips it: the containers deliberately
            // run overflow:visible. It reads worst on a shaped Frame, whose ring/fill are a big painted
            // silhouette that walks across the panel (the "circle doesn't resize with the inspector" report),
            // and whose aspect lock then derives its HEIGHT from that overflowing width — but a plain Card at
            // a fixed width overflows identically, so the cap belongs here, on the one width write, not in
            // FrameBuilder. maxWidth:100% caps the element at its parent's content box while leaving the
            // designed width intact wherever there is room, so alignSelf Left/Center/Right still means what it
            // said and the frame geometry pass re-derives radius + aspect height from the capped resolved
            // width for free. A percentage against a parent with no definite width resolves to "no
            // constraint" in Yoga, so shrink-to-fit hosts (designer cards mid-measure) are untouched.
            if (widthApplied) element.style.maxWidth = Length.Percent(100f);

            // For a CONTAINER (and any node exposing the height-resize grip), a stored height is a GROW-ONLY
            // floor (min-height), NEVER a fixed height. A fixed height below the content's natural size
            // collapses the container's flex-shrink children into each other (the overlapping label/field
            // bug): a Foldout that needs 60px but carries a stale height:40 would squish its rows. Treating
            // it as min-height means the container can add room but can never shrink below its content —
            // mirroring the row/grid resizers, and matching "containers are expandable, never squishable".
            // Leaf nodes keep a literal height; width stays a fixed size, capped at the parent (see above).
            bool heightIsFloor = node is ContainerNode || node.MakeHeightResizable;
            if (heightIsFloor)
            {
                // Through the arbiter: on a leaf with the height grip this element may ALSO be carrying a Rows
                // container's row floor and/or a widget's own intrinsic floor, and a raw write erases them.
                if (overrideStyle.height.isOverridden) MinHeightFloors.SetNode(element, overrideStyle.height.value);
                else if (hasTheme && baseStyle.height.isOverridden) MinHeightFloors.SetNode(element, baseStyle.height.value);
            }
            else
            {
                if (overrideStyle.height.isOverridden) element.style.height = overrideStyle.height.value;
                else if (hasTheme && baseStyle.height.isOverridden) element.style.height = baseStyle.height.value;
            }

            // Flex Grow / Shrink
            if (overrideStyle.flexGrow.isOverridden) element.style.flexGrow = overrideStyle.flexGrow.value;
            else if (hasTheme && baseStyle.flexGrow.isOverridden) element.style.flexGrow = baseStyle.flexGrow.value;

            if (overrideStyle.flexShrink.isOverridden) element.style.flexShrink = overrideStyle.flexShrink.value;
            else if (hasTheme && baseStyle.flexShrink.isOverridden) element.style.flexShrink = baseStyle.flexShrink.value;

            // A blanked tooltip (HiddenSentinel) is a deliberate "no tooltip" — never surface the sentinel.
            // Claimed rather than assigned so it anchors to the node's title instead of to whichever inner
            // part the pointer happens to be over (see TooltipAnchor); a bound widget has already claimed
            // its own element with the better-informed field reach, which this pass must not undo.
            if (!node.IsTooltipHidden && !string.IsNullOrEmpty(node.Tooltip))
            {
                TooltipAnchor.ClaimNode(element, node.Tooltip);
            }
        }

        private static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }

        private static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        /// <summary>
        /// True when <paramref name="node"/> already OWNS its top/bottom margin — either overridden on the node
        /// itself or supplied by a style token from <paramref name="theme"/>. Anything that wants to write a
        /// DEFAULT margin after <see cref="Apply"/> has run (LayoutTreeBuilder's uniform section rhythm) has to
        /// ask this first, or it silently overwrites the value Apply just resolved. Mirrors the exact
        /// override-then-theme precedence the margin block above uses.
        /// </summary>
        internal static bool OwnsVerticalMargin(LayoutNode node, SasquatchThemeAsset theme, bool top)
        {
            if (node == null) return false;
            var over = node.Style;
            if ((top ? over.marginTop : over.marginBottom).isOverridden) return true;
            if (theme == null) return false;
            var baseStyle = ResolveBaseStyle(node.StyleToken, theme);
            return (top ? baseStyle.marginTop : baseStyle.marginBottom).isOverridden;
        }

        private static LayoutStyle ResolveBaseStyle(string tokenString, SasquatchThemeAsset theme)
        {
            LayoutStyle mergedStyle = default;
            if (theme == null || string.IsNullOrEmpty(tokenString)) return mergedStyle;

            string[] tokens = tokenString.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (theme.TryGetStyle(token.Trim(), out var tokenStyle))
                {
                    MergeStyles(ref mergedStyle, tokenStyle);
                }
            }
            return mergedStyle;
        }

        private static void MergeStyles(ref LayoutStyle target, LayoutStyle source)
        {
            if (source.flexDirection.isOverridden) target.flexDirection = source.flexDirection;
            if (source.alignSelf.isOverridden) target.alignSelf = source.alignSelf;
            if (source.alignItems.isOverridden) target.alignItems = source.alignItems;
            if (source.justify.isOverridden) target.justify = source.justify;
            if (source.borderRadius.isOverridden) target.borderRadius = source.borderRadius;
            if (source.borderWidth.isOverridden) target.borderWidth = source.borderWidth;
            if (source.backgroundColor.isOverridden) target.backgroundColor = source.backgroundColor;
            if (source.borderColor.isOverridden) target.borderColor = source.borderColor;
            if (source.marginTop.isOverridden) target.marginTop = source.marginTop;
            if (source.marginBottom.isOverridden) target.marginBottom = source.marginBottom;
            if (source.marginLeft.isOverridden) target.marginLeft = source.marginLeft;
            if (source.marginRight.isOverridden) target.marginRight = source.marginRight;
            if (source.paddingTop.isOverridden) target.paddingTop = source.paddingTop;
            if (source.paddingBottom.isOverridden) target.paddingBottom = source.paddingBottom;
            if (source.paddingLeft.isOverridden) target.paddingLeft = source.paddingLeft;
            if (source.paddingRight.isOverridden) target.paddingRight = source.paddingRight;
            if (source.width.isOverridden) target.width = source.width;
            if (source.height.isOverridden) target.height = source.height;
            if (source.flexGrow.isOverridden) target.flexGrow = source.flexGrow;
            if (source.flexShrink.isOverridden) target.flexShrink = source.flexShrink;
        }
    }
}
