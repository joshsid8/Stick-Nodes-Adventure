using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The sizing math shared by every code-peek card: how wide a snippet wants to be, and how tall it will
    /// render. Both are computed from the TEXT rather than from layout, because a peek card has to be sized in
    /// the same pass it is shown — waiting for a resolved layout would let the card flash at the wrong size,
    /// and the popup placements need the height before they can decide whether the card fits below or has to
    /// flip above.
    ///
    /// Snippets are emitted with explicit '\n' breaks and rendered <see cref="UnityEngine.UIElements.WhiteSpace.NoWrap"/>,
    /// so a line count and a per-character advance are exact enough. Everything here is deliberately
    /// screen-independent, which is what keeps it unit-testable without a live window.
    /// </summary>
    internal static class CodePeekMetrics
    {
        /// <summary>Per-character advance for Consolas @11px. Biased generous so lines never clip.</summary>
        internal const float CharWidth = 6.4f;
        /// <summary>Logical px per line at the 11px mono size (generous — never clips).</summary>
        internal const float LineHeight = 16f;
        /// <summary>Widest any peek card may grow; longer lines clip (the cards are overflow-hidden).</summary>
        internal const float MaxWidth = 680f;
        /// <summary>Gap kept to the screen / host-panel edge.</summary>
        internal const float EdgeMargin = 6f;
        /// <summary>How far a card laps the element it hangs off, so the seam reads as ONE hairline rather
        /// than two stacked 1px borders.</summary>
        internal const float JoinOverlap = 1f;

        /// <summary>The snippet body cap plus the trailing "…" line.</summary>
        internal const int MaxLines = MethodSourcePreview.MaxLines + 1;

        private const float HorizontalPadding = 16f;   // 8 either side
        private const float HorizontalBorder = 2f;     // 1 either side
        private const float WidthSlack = 8f;
        private const float VerticalPadding = 12f;     // 6 top and bottom
        private const float VerticalBorder = 2f;

        /// <summary>The width a snippet wants, clamped to <paramref name="min"/>..<paramref name="max"/>:
        /// the longest VISIBLE line × the monospace advance, plus the card's own padding and border.</summary>
        internal static float ContentWidth(string rich, float min, float max)
            => Mathf.Clamp(MaxPlainLineLength(rich) * CharWidth + HorizontalPadding + HorizontalBorder + WidthSlack,
                           min, max);

        /// <summary>The height a snippet will render at, capped at <see cref="MaxLines"/>.</summary>
        internal static float CardHeight(string rich)
            => Mathf.Clamp(CountLines(rich), 1, MaxLines) * LineHeight + VerticalPadding + VerticalBorder;

        /// <summary>Line count of a snippet (1 for empty — a card is never zero lines tall).</summary>
        internal static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 1;
            int n = 1;
            foreach (char c in s) if (c == '\n') n++;
            return n;
        }

        /// <summary>
        /// Longest VISIBLE line length (chars) across a highlighted snippet, ignoring rich-text markup so the
        /// width estimate tracks the displayed glyphs, not the &lt;color=…&gt;/&lt;noparse&gt; tags. A scan
        /// (not a Regex) to avoid a dependency: everything between '&lt;' and the next '&gt;' is tag and doesn't
        /// count. A source '&lt;' (emitted inside noparse) is undercounted by one — the generous
        /// <see cref="CharWidth"/> and slack absorb it; this only needs to be close enough to pick a
        /// comfortable card width.
        /// </summary>
        internal static int MaxPlainLineLength(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return 0;
            int max = 0, cur = 0;
            bool inTag = false;
            foreach (char c in rich)
            {
                if (c == '\n') { if (cur > max) max = cur; cur = 0; inTag = false; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }
                cur++;
            }
            if (cur > max) max = cur;
            return max;
        }
    }
}
