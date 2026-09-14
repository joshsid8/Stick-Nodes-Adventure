using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Per-node UITK style overrides (flex direction/alignment, border, background, margin, padding, size,
    /// flex grow/shrink). Each field is overridable, so unset properties fall through to the element's defaults.
    /// </summary>
    [Serializable]
    public struct LayoutStyle
    {
        public OverridableEnum<FlexDirection> flexDirection;
        public OverridableEnum<Align> alignSelf;
        public OverridableEnum<Align> alignItems;
        public OverridableEnum<Justify> justify;

        public OverridableFloat borderRadius;
        public OverridableFloat borderWidth;

        public OverridableColor backgroundColor;
        public OverridableColor borderColor;

        public OverridableFloat marginTop;
        public OverridableFloat marginBottom;
        public OverridableFloat marginLeft;
        public OverridableFloat marginRight;

        public OverridableFloat paddingTop;
        public OverridableFloat paddingBottom;
        public OverridableFloat paddingLeft;
        public OverridableFloat paddingRight;

        public OverridableFloat width;
        public OverridableFloat height;

        public OverridableFloat flexGrow;
        public OverridableFloat flexShrink;
    }
}
