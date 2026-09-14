namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How collection elements can be reordered in the live inspector / preview (applies to both
    /// <see cref="CollectionDisplayMode.List"/> and <see cref="CollectionDisplayMode.Table"/>).
    /// <list type="bullet">
    /// <item><see cref="DragAndDrop"/> — a drag handle per row (default; Unity-style).</item>
    /// <item><see cref="Arrows"/> — up/down buttons per row.</item>
    /// <item><see cref="Both"/> — a drag handle and up/down buttons.</item>
    /// <item><see cref="None"/> — no reorder affordance at all (add/remove still work). This is what a
    /// <c>[NonReorderable]</c> array/list imports to.</item>
    /// </list>
    /// </summary>
    public enum CollectionReorderStyle
    {
        DragAndDrop,
        Arrows,
        Both,
        // Appended last to preserve the serialized integer values of the existing members.
        None
    }
}
