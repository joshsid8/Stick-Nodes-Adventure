using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Applies a node's <see cref="TextStyle"/> overrides to its rendered element. These are all *inherited*
    /// UITK text properties (font style, size, color, text-align, font), so setting them on the node's root element
    /// cascades to the labels/value text inside — no need to hunt for a specific label element (which is
    /// impossible to do reliably for a Unity PropertyField anyway).
    /// </summary>
    public static class TextStyleApplier
    {
        /// <summary>
        /// The smallest font size that still renders as text. Below this a run of glyphs collapses into a
        /// dim 1–2px smear that reads as a dashed line, not as a name — which is precisely how a stray
        /// value in a Size row surfaced in the field (a user reported an object field showing
        /// "-----------" where the referenced object's name belonged). Nothing legitimate is authored down
        /// here, so a smaller number is treated as no override at all rather than rendered.
        /// </summary>
        public const float MinReadableFontSize = 4f;

        /// <summary>
        /// True when <paramref name="fontSize"/> is an override worth applying: flagged AND at least
        /// <see cref="MinReadableFontSize"/>. Zero, negatives and sub-pixel sizes are not sizes. The one
        /// place this rule lives — the live applier, the emitter and the designer's Size row all ask here,
        /// so an existing layout carrying a bad value self-heals on the next render with no migration.
        /// </summary>
        public static bool IsUsableFontSize(OverridableFloat fontSize) =>
            fontSize.isOverridden && fontSize.value >= MinReadableFontSize;

        /// <summary>Applies the inherited text styling in <paramref name="text"/> (font style, size, color, align) to <paramref name="element"/>.</summary>
        public static void Apply(VisualElement element, TextStyle text)
        {
            if (element == null) return;

            if (text.fontStyle.isOverridden) element.style.unityFontStyleAndWeight = text.fontStyle.value;
            if (IsUsableFontSize(text.fontSize)) element.style.fontSize = text.fontSize.value;
            if (text.color.isOverridden) element.style.color = text.color.value;
            if (text.align.isOverridden) element.style.unityTextAlign = text.align.value;
            // unityFontDefinition takes precedence over the inherited default, so a custom font actually shows
            // (setting only unityFont can be overridden by an inherited definition). Inherited → cascades to
            // the labels/value text inside.
            if (text.font != null) element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(text.font));
        }

        /// <summary>True if any field of the text style is overridden (used to skip wiring when there's nothing to do).
        /// An unusable font size doesn't count — otherwise it would still drag the whole deferred styling pass in.</summary>
        public static bool HasAnyOverride(TextStyle text) =>
            text.fontStyle.isOverridden || IsUsableFontSize(text.fontSize) || text.color.isOverridden
            || text.align.isOverridden || text.font != null;
    }
}
