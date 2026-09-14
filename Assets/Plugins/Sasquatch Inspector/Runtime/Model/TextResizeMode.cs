namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Whether a <see cref="DisplayType.Text"/> field shows a drag-to-resize handle at its bottom edge.
    /// </summary>
    public enum TextResizeMode
    {
        /// <summary>Always resizable — also forces the field multiline.</summary>
        Resizable,
        /// <summary>Never resizable — respects the standard Unity [TextArea] / [Multiline] layout, with no resize handle.</summary>
        Fixed
    }
}
