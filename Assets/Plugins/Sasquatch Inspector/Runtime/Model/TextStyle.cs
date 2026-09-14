using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Per-node text styling overrides (bold/italic, size, color, horizontal alignment). Applied to the node's
    /// rendered element via inherited UITK text properties, so it cascades to the labels/text inside. Every
    /// field defaults to "not overridden", so nodes authored before text styling are unchanged.
    /// </summary>
    [Serializable]
    public struct TextStyle
    {
        // Bold/italic stored as Unity's combined FontStyle (Normal/Bold/Italic/BoldAndItalic).
        public OverridableEnum<FontStyle> fontStyle;
        public OverridableFloat fontSize;
        public OverridableColor color;
        // Horizontal text alignment, stored as TextAnchor (MiddleLeft/MiddleCenter/MiddleRight).
        public OverridableEnum<TextAnchor> align;
        // Optional custom font. null = inherit the default font (no override). A direct object reference (no
        // OverridableX wrapper needed — null already encodes "not overridden").
        public Font font;
    }
}
