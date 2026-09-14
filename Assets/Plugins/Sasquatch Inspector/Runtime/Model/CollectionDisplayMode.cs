namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How a <see cref="CollectionNode"/> renders its elements. This is the collection's "Display Type"
    /// (the label the designer shows), the array-level twin of a field's Display Type.
    /// <list type="bullet">
    /// <item><see cref="List"/> — stacked rows, each row showing the full ItemLayout (the original behavior).</item>
    /// <item><see cref="Table"/> — a column-per-field table: one row per element, one column per
    /// leaf field in the ItemLayout (the table/grid view).</item>
    /// <item><see cref="Default"/> — Unity's stock PropertyField list, no Sasquatch Inspector styling.</item>
    /// <item><see cref="CustomWidget"/> — render the WHOLE array as a single Custom Widget (radial graph,
    /// bar cluster…), the array feeding the widget's primary list input. Offered only for supported element
    /// types (bool / float); falls back to <see cref="Default"/> when the widget asset is missing.</item>
    /// <item><see cref="TagChips"/> — render a string collection (<c>string[]</c> / <c>List&lt;string&gt;</c>) as a
    /// wrapping row of removable chips plus an inline add field (the Tag Chips widget). Offered only for string
    /// element types; appended at the END so existing serialized ints stay stable.</item>
    /// </list>
    /// </summary>
    public enum CollectionDisplayMode
    {
        List,
        Table,
        Default,
        CustomWidget,
        TagChips
    }
}
