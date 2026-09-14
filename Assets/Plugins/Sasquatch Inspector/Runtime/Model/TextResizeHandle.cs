namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The size/width of a resizable <see cref="DisplayType.Text"/> field's grip drag handle.
    /// </summary>
    public enum TextResizeHandleSize
    {
        /// <summary>Centered compact grabber pill (width 28).</summary>
        Compact,
        /// <summary>Left-aligned grabber pill occupying the first 35% of the width, with left margin.</summary>
        Large,
        /// <summary>Full width grabber pill with left and right margins.</summary>
        FullWidth
    }

    /// <summary>
    /// The horizontal alignment of a resizable <see cref="DisplayType.Text"/> field's grip drag handle inside the text box.
    /// </summary>
    public enum GripAlignment
    {
        Left,
        Center,
        Right
    }
}
