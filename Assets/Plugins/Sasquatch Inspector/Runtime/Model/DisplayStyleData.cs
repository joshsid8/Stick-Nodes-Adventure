using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>Visual style for a Toggle widget's checkbox (Checkbox / Rounded / Square / Slim /
    /// ModernCheckbox). Serialized as int — append new styles at the END only.</summary>
    public enum ToggleStyle
    {
        Checkbox,
        Rounded,
        Square,
        Slim,
        // Vector-drawn checkbox (crisp at any size, unlike the stock bitmap tick): rounded box,
        // accent fill + Lucide check when on, outlined when off. Styled via the same toggle colors.
        ModernCheckbox
    }

    /// <summary>Overall size preset for a widget (Standard / Compact / Large).</summary>
    public enum DisplaySize
    {
        Standard,
        Compact,
        Large
    }

    /// <summary>Where the editable value field sits relative to a Slider / Min-Max slider (Right is the default).</summary>
    public enum SliderFieldPlacement
    {
        Right,
        Left,
        Above,
        Below
    }

    /// <summary>Horizontal alignment of a Slider's value field when placed Above/Below the bar. Center is value 0
    /// so it stays the default (matching the historical centered look); ignored for Left/Right placement.</summary>
    public enum SliderFieldAlignment
    {
        Center,
        Left,
        Right
    }

    /// <summary>
    /// Knob (dragger) shape for a styled slider: Default (Unity's round knob), Square, Capsule (single-knob
    /// sliders only), Rectangle, or Grip (a mixer-fader cap with engraved ridges).
    /// </summary>
    public enum SliderKnobShape
    {
        Default,
        Square,
        Capsule,
        Rectangle,
        Grip
    }

    /// <summary>Overall size of a styled slider — scales the bar and knob together (Normal = Unity's default).</summary>
    public enum SliderSize
    {
        Normal,
        Large,
        ExtraLarge
    }

    /// <summary>
    /// Per-widget visual styling: toggle look/colors, slider colors/shape/size/field placement, min-max label
    /// options, accent and corner radius. Fields default to "unset" so widgets authored before styling are unchanged.
    /// </summary>
    [Serializable]
    public struct DisplayStyleData
    {
        public ToggleStyle toggleStyle;
        public bool toggleShowOnOff;
        // Min-Max value read-outs. BOTH are INVERTED — same trick as sliderHideField below — so the struct's
        // zero value is the shipping default: the min/max numbers show, as editable number fields.
        public bool minMaxHideValues;       // false = show the min/max read-outs (default on)
        public bool minMaxReadOnlyValues;   // false = the read-outs are editable number fields (default on)
        // Legacy pair these two replaced, when both defaulted OFF. DEAD — nothing reads them — but still
        // serialized so a layout saved before the flip keeps carrying its bytes instead of silently losing them
        // (same contract as minMaxLowLimit/minMaxHighLimit on BindingNode, and what the golden corpus asserts).
        // A bool cannot tell "the user turned this off" from "the option simply defaulted off", so those layouts
        // deliberately adopt the new default rather than inheriting a choice nobody made.
        public bool minMaxShowLabels;
        public bool minMaxEditableLabels;
        public Color minMaxLabelColor;
        public float minMaxLabelSize;
        public string minMaxLabelFormat;
        public DisplaySize size;
        public OverridableColor accent;
        public OverridableFloat cornerRadius;

        // Slider look + value-field options (Slider / Min-Max). Defaults render close to Unity's. Colors:
        // alpha 0 = unset. sliderHideField is INVERTED so the value field shows by default (default false =
        // shown); set true to hide it.
        public bool sliderHideField;        // false = show the value field (default on)
        public SliderFieldPlacement sliderFieldPlacement;
        public SliderFieldAlignment sliderFieldAlignment; // horizontal align for Above/Below placement (Center default)
        public SliderKnobShape sliderKnobShape;
        public float sliderKnobRoundness;   // Square/Rectangle knob corner radius (px)
        public Color sliderBarColor;        // the track color
        public Color sliderBarHoverColor;   // track color while hovered; unset (alpha 0) = no hover tint
        public Color sliderKnobColor;       // the dragger color
        public Color sliderKnobHoverColor;  // dragger color while hovered; unset = auto-derived from sliderKnobColor
        public Color sliderCenterBarColor;  // active range color for MinMaxSlider
        public Color sliderCenterBarHoverColor; // center range color while hovered; unset = auto-derived from sliderCenterBarColor
        public SliderSize sliderSize;       // scales bar + knob together (Normal = default)

        // Toggle checkbox styling colors (Checkbox style only). Defaults: alpha 0 = unset.
        public Color toggleFillColor;       // inside fill color
        public Color toggleKnobColor;       // knob / checkmark tick color
        public Color toggleOutlineColor;    // outline border color
        public Color toggleFillColorOff;    // inside fill color when checked is false
        public SliderSize toggleSize;       // scales the toggle switch/box (Normal = default)
    }
}
