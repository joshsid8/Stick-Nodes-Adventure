namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How a paginated collection's page navigator is presented (applies to both
    /// <see cref="CollectionDisplayMode.List"/> and <see cref="CollectionDisplayMode.Table"/>).
    /// <list type="bullet">
    /// <item><see cref="ButtonsOnly"/> — "‹ Prev / Next ›" buttons + a "Page X of Y" label (the default).</item>
    /// <item><see cref="DotsOnly"/> — iOS-style clickable page dots (the current page's dot is enlarged).</item>
    /// <item><see cref="ButtonsAndDots"/> — both the prev/next buttons and the dots.</item>
    /// </list>
    /// Only takes effect once the visible row count exceeds the page size (same gate as pagination itself).
    /// </summary>
    public enum PaginationDisplayStyle
    {
        ButtonsOnly,
        DotsOnly,
        ButtonsAndDots
    }
}
