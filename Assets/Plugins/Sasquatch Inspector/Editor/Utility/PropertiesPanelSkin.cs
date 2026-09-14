using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// "Rails &amp; Rhythm" — the metrics behind the Layout Designer's Properties panel.
    ///
    /// The panel used to size itself from ~10 different ad-hoc margin values and let every nesting level
    /// push the input column further right, so the field edge zig-zagged down the column. This type is the
    /// single scale everything now snaps to, and <see cref="SasquatchLayoutDesignerWindow.StylePropertiesPanel"/>
    /// is the only consumer.
    ///
    /// Three rules the numbers encode:
    /// • <b>One input column.</b> Nesting indents the LABEL and its rail, never the field. A label is pinned
    ///   to <c>column - itsOwnIndent</c>, so every input in the panel starts at the same x no matter how deep.
    /// • <b>Depth is a rail, not a guess.</b> Each section's content carries one continuous hairline down its
    ///   left edge (the <see cref="RailPad"/> gap is what keeps a nested section's chevron off that line).
    /// • <b>Two header tiers.</b> A top-level section is a small tracked micro-label with a rule to the right
    ///   edge; a nested one is plain bold at body size. Both dim when closed, so open/closed reads without
    ///   having to find the chevron.
    ///
    /// Colors deliberately live in <see cref="DesignerPalette"/>, not here — this is layout only, and the
    /// palette is already the one skin-aware authority.
    /// </summary>
    internal static class PropertiesPanelSkin
    {
        // ---- Vertical scale. These four values are the ONLY gaps the panel is allowed to use. ----
        /// <summary>Between rows inside one section.</summary>
        public const float GapRow = 2f;
        /// <summary>Between a section header and its first row, and around inline notes.</summary>
        public const float GapTight = 4f;
        /// <summary>Above a NESTED section, and below a help paragraph.</summary>
        public const float GapSub = 8f;
        /// <summary>Above a TOP-LEVEL section — the only large gap in the panel.</summary>
        public const float GapSection = 16f;

        // ---- The input column ----
        /// <summary>
        /// The label column is a FRACTION of the panel, not a constant: Properties lives in a
        /// <c>TwoPaneSplitView</c> the user drags, and a fixed column that reads well at 460px starves the
        /// input at 240px. Clamped at both ends so it never collapses or eats the field.
        /// </summary>
        public const float ColumnFraction = 0.42f;
        public const float ColumnMin = 92f;
        public const float ColumnMax = 152f;
        /// <summary>
        /// Floor for a deeply-nested label's own width. Past this the label ellipsises rather than pushing the
        /// input column right — the column is the invariant, the label is what gives.
        /// </summary>
        public const float LabelMinWidth = 54f;

        // ---- The rail ----
        /// <summary>Hairline width of a section's content rail.</summary>
        public const float RailWidth = 1f;
        /// <summary>
        /// Gap between the rail and the content it gathers. This is what stops a nested section's chevron
        /// landing on its parent's rail — the reason nested headers indent at all.
        /// </summary>
        public const float RailPad = 11f;
        /// <summary>One nesting step: the rail plus its gap. Content at depth <c>d</c> sits <c>d * Indent</c> in.</summary>
        public const float Indent = RailWidth + RailPad;

        // ---- Type ----
        /// <summary>Top-level section title — small, tracked, uppercased.</summary>
        public const float SectionFontSize = 10f;
        public const float SectionTracking = 0.6f;
        /// <summary>Nested section title — bold at body size, sentence case.</summary>
        public const float SubSectionFontSize = 11f;
        /// <summary>Row labels and inline help. One step down from Unity's 12px default, which is what buys the density.</summary>
        public const float RowFontSize = 11f;

        // ---- Misc chrome ----
        /// <summary>Right padding on the scroll body, so a colour swatch never runs under the scrollbar.</summary>
        public const float ScrollGutter = 14f;
        /// <summary>Height of an inset "+ Add …" action button.</summary>
        public const float ActionButtonHeight = 22f;

        /// <summary>The pinned label width for a label sitting <paramref name="indentX"/> px into the panel.</summary>
        public static float LabelWidthAt(float column, float indentX)
            => Mathf.Max(LabelMinWidth, column - indentX);

        /// <summary>The label column for a panel whose content box is <paramref name="contentWidth"/> px wide.</summary>
        public static float ColumnFor(float contentWidth)
            => Mathf.Clamp(contentWidth * ColumnFraction, ColumnMin, ColumnMax);
    }
}
