using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Where an affix mounts relative to its field's input box. <see cref="Auto"/> (value 0, the default so
    /// existing layouts deserialize unchanged) resolves to <see cref="InsideField"/> when the widget exposes a
    /// real text input and <see cref="OutsideField"/> when it does not — so a Toggle / Color / Curve /
    /// ObjectField never ends up with an affix floating over a control that has nowhere to put it.
    /// Append-only — values are serialized as int.
    /// </summary>
    public enum AffixMount
    {
        Auto,
        // Overlaid INSIDE the input box, pinned to its leading/trailing edge, with matching padding added to the
        // input so typed text can never slide under it. Falls back to OutsideField when there is no text input.
        InsideField,
        // A sibling of the control inside the field's `unity-base-field__input` container — outside the box the
        // value is typed into, but still inside the field, so the label column is untouched.
        OutsideField
    }

    /// <summary>
    /// One side (prefix or suffix) of a field's affix decoration: optional text, an optional Sasquatch icon, the
    /// mount, and its own styling. Both sides empty (the default) means the field renders exactly as it did
    /// before affixes existed, and the decorator early-outs at zero cost.
    /// <para>
    /// A STRUCT on purpose, matching <see cref="TextStyle"/> / <c>LayoutStyle</c>: there is no null state to guard
    /// and the compiler forces the get-mutate-set pattern rather than letting <c>node.Suffix.text = "kg"</c>
    /// silently mutate a copy.
    /// </para>
    /// </summary>
    [Serializable]
    public struct FieldAffix
    {
        // The affix text, rendered as-is except that the gap below supplies the spacing (unlike the legacy
        // unitSuffix, which asked callers to type their own leading space). Empty = no text.
        public string text;
        // Sasquatch icon id drawn beside the text (same id space as ButtonNode.IconId). Empty = no icon.
        // Text and icon are independent: either alone, or both, in which case the icon leads on a prefix and
        // trails on a suffix (reading outward from the field).
        public string iconId;
        public AffixMount mount;
        // Per-side text styling. Unset fields fall back to the dim 10px grey the unit suffix has always used.
        public TextStyle style;
        // Unset = plain text. Set = the affix rides a soft rounded pill in this color.
        public OverridableColor pillColor;
        // Space between the affix and the control, in px. Unset = 4.
        public OverridableFloat gap;

        /// <summary>True when this side renders nothing at all — the decorator's early-out test.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(text) && string.IsNullOrEmpty(iconId);
    }
}
