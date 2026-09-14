using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared list chrome for the picker popups. Its whole job is that **a picker never scrolls sideways**.
    ///
    /// A popup can't resize once it is open, so the width has to be right before the tree exists — hence
    /// <see cref="EstimateTextWidth"/>, a cheap character-count estimate the popups use from
    /// <c>GetWindowSize()</c> to open wide enough for their own content. That is the fix for the common case:
    /// if the list needs more room, the popup is simply wider.
    ///
    /// The other two pieces are the guarantee. <see cref="ClipLongText"/> makes every flexible label able to
    /// SHRINK — UI Toolkit defaults <c>flex-shrink</c> to 0, so a label with `flexGrow: 1` and nothing else
    /// still refuses to go below its text width and pushes the content box past the viewport — and
    /// <see cref="SuppressHorizontalScrolling"/> removes the horizontal scroller outright, so even an
    /// unforeseen overflow clips instead of growing a scrollbar. That last one matters because the trigger is
    /// intermittent by nature: the vertical scrollbar appearing steals ~13px of viewport, which is enough to
    /// push a row that exactly fitted into overflow.
    /// </summary>
    internal static class PopupListChrome
    {
        /// <summary>Rough per-character advance as a fraction of font size, for the editor's proportional UI
        /// font. Biased generous — over-estimating costs a few px of popup width, under-estimating costs an
        /// ellipsis.</summary>
        private const float CharWidthFactor = 0.56f;

        /// <summary>Room kept for the vertical scrollbar, which these lists almost always have.</summary>
        internal const float ScrollbarAllowance = 15f;

        /// <summary>Widest a popup may grow itself to fit its content. Past this, long entries ellipsize —
        /// one monstrous member name shouldn't turn a picker into a billboard.</summary>
        internal const float MaxAutoWidth = 560f;

        /// <summary>Approximate rendered width of <paramref name="text"/> at <paramref name="fontSize"/>.</summary>
        internal static float EstimateTextWidth(string text, float fontSize)
            => string.IsNullOrEmpty(text) ? 0f : text.Length * fontSize * CharWidthFactor;

        /// <summary>Makes a flexible row label shrink and ellipsize rather than force the row wider than the
        /// list. Apply to every label that carries variable-length text.</summary>
        internal static void ClipLongText(Label label)
        {
            if (label == null) return;
            label.style.flexShrink = 1;
            label.style.minWidth = 0;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
        }

        /// <summary>Removes a list's horizontal scroller. A picker is navigated vertically; sideways scrolling
        /// is never the answer, so overflow clips (and the labels above ellipsize) instead.</summary>
        internal static void SuppressHorizontalScrolling(ScrollView scroll)
        {
            if (scroll == null) return;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.mode = ScrollViewMode.Vertical;
        }
    }
}
