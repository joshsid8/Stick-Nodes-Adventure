using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A purely decorative rich-text block: free-form text placed anywhere in the inspector, rendered as
    /// lightweight markdown (bold / italics / <c>code</c> / {Color|text} / [links](https://…)) plus raw Unity
    /// rich-text tags (&lt;b&gt;, &lt;color=…&gt;, &lt;size=…&gt;, …), with the usual
    /// text-style controls (bold/italic, size, color, alignment, font) plus an optional card background, padding
    /// and corner rounding. A plain leaf (like <see cref="SpaceNode"/> / <see cref="LabeledDividerNode"/>) — it is
    /// NOT a bound field and is dropped from the Widgets palette (distinct from the bound-string
    /// <see cref="DisplayType.Text"/> widget).
    /// </summary>
    [Serializable]
    public class TextBlockNode : LayoutNode
    {
        /// <summary>Clamp ceiling for the block's corner rounding.</summary>
        public const float MaxCornerRadius = 24f;

        [SerializeField, TextArea(2, 8)] private string text = "Text";
        [SerializeField] private TextStyle textStyle;               // bold/italic/size/color/align/font
        [SerializeField] private bool renderMarkdown = true;
        [SerializeField] private OverridableColor backgroundColor;  // unset => no card background
        [SerializeField] private float padding = 0f;                // inner padding (px) when a background is set
        [SerializeField] private float cornerRadius = 0f;           // card corner rounding (px)
        [SerializeField] private float maxWidth = 0f;               // 0 = no cap; else max text width in px

        /// <summary>The block's text; rendered as markdown when <see cref="RenderMarkdown"/> is on.</summary>
        public string Text
        {
            get => text;
            set => text = value;
        }

        /// <summary>Text styling (bold/italic, size, color, alignment, font) for the block.</summary>
        public TextStyle TextStyle
        {
            get => textStyle;
            set => textStyle = value;
        }

        /// <summary>When on, <see cref="Text"/> renders as lightweight markdown (bold / italics / code / links)
        /// plus raw Unity rich-text tags (&lt;b&gt;, &lt;color=…&gt;, &lt;size=…&gt;, …).</summary>
        public bool RenderMarkdown
        {
            get => renderMarkdown;
            set => renderMarkdown = value;
        }

        /// <summary>Optional card background behind the text; unset (isOverridden=false) = no background.</summary>
        public OverridableColor BackgroundColor
        {
            get => backgroundColor;
            set => backgroundColor = value;
        }

        /// <summary>Inner padding in pixels applied when a <see cref="BackgroundColor"/> is set. Clamped ≥ 0.</summary>
        public float Padding
        {
            get => padding;
            set => padding = Mathf.Max(0f, value);
        }

        /// <summary>Card corner rounding in pixels, clamped to [0..<see cref="MaxCornerRadius"/>].</summary>
        public float CornerRadius
        {
            get => cornerRadius;
            set => cornerRadius = Mathf.Clamp(value, 0f, MaxCornerRadius);
        }

        /// <summary>Optional maximum text width in pixels (0 = fill the inspector). Clamped ≥ 0.</summary>
        public float MaxWidth
        {
            get => maxWidth;
            set => maxWidth = Mathf.Max(0f, value);
        }
    }
}
