using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A purely decorative horizontal divider: a thin rule with an optional centered title, for visually
    /// separating groups of fields. A plain leaf (like <see cref="SpaceNode"/>) — never offered in the Widget
    /// Type popup; dropped from the ELEMENTS palette. The rule color resolves to the theme divider color when
    /// unset, so a divider authored before theming still matches whatever skin it's viewed in.
    /// </summary>
    [Serializable]
    public class LabeledDividerNode : LayoutNode
    {
        /// <summary>Clamp bounds for the rule thickness, shared by the setter and the builder.</summary>
        public const float MinThickness = 1f;
        public const float MaxThickness = 12f;
        /// <summary>Clamp ceiling for the rule's corner rounding (half the max thickness = a full pill end).</summary>
        public const float MaxCornerRadius = 6f;

        [SerializeField] private string title = "";
        [SerializeField] private float thickness = 1f;
        [SerializeField] private float cornerRadius = 0f; // 0 = square ends
        [SerializeField] private OverridableColor lineColor; // unset => theme divider color at build time
        [SerializeField] private TextStyle titleTextStyle;  // bold/italic/size/color/align for the caption

        /// <summary>Optional centered caption; empty ⇒ a plain full-width rule.</summary>
        public string Title
        {
            get => title;
            set => title = value;
        }

        /// <summary>Rule thickness in pixels, clamped to [<see cref="MinThickness"/>..<see cref="MaxThickness"/>].</summary>
        public float Thickness
        {
            get => thickness;
            set => thickness = Mathf.Clamp(value, MinThickness, MaxThickness);
        }

        /// <summary>Corner rounding of the rule in pixels, clamped to [0..<see cref="MaxCornerRadius"/>]; 0 = square.</summary>
        public float CornerRadius
        {
            get => cornerRadius;
            set => cornerRadius = Mathf.Clamp(value, 0f, MaxCornerRadius);
        }

        /// <summary>Rule color; unset (isOverridden=false) resolves to the theme divider color in the builder.</summary>
        public OverridableColor LineColor
        {
            get => lineColor;
            set => lineColor = value;
        }

        /// <summary>Text styling (bold/italic, size, color, align) for the divider's centered caption.</summary>
        public TextStyle TitleTextStyle
        {
            get => titleTextStyle;
            set => titleTextStyle = value;
        }
    }
}
