namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The overall look of a <see cref="CollectionDisplayMode.List"/> collection. This is the CONTAINER-level
    /// choice — the shell, the index gutter, the density and how the ordinal presents itself. It is
    /// deliberately orthogonal to <see cref="CollectionRowSeparator"/>, which owns the ROW surface (bare /
    /// hairline / tray), so every style works with every frame.
    /// <para>
    /// Everything the styles share — the ordinal gutter whose lane doubles as the drag handle, hover-revealed
    /// row actions, the header count pill, the one-line search+sort toolbar, the footer add strip and the
    /// framed empty state — is NOT a style choice and is drawn the same way by all three.
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Rows"/> — the structured default: a bordered shell and a crisp gutter edge.</item>
    /// <item><see cref="Minimal"/> — no shell at all; rhythm from air plus a single faint gutter edge.</item>
    /// <item><see cref="Timeline"/> — a recessed well with a continuous rail running its length and each
    /// ordinal sitting on it as a node, so the SEQUENCE is what you read first.</item>
    /// </list>
    /// </summary>
    public enum CollectionListStyle
    {
        /// <summary>Bordered shell, filled content area, crisp 1px gutter edge. The default.</summary>
        Rows = 0,

        /// <summary>No shell and no content fill; generous row padding and a faint gutter edge carry it.</summary>
        Minimal = 1,

        /// <summary>
        /// A continuous vertical rail down the list with each row's ordinal as a node on it, capped at the
        /// first and last node so the list reads as a bounded sequence, over a recessed well. A list IS an
        /// ordered thing — this is the style that says so.
        /// <para>
        /// The well matters: without it this was a rail floating on nothing, which read as a variant of
        /// <see cref="Minimal"/> rather than its own style. (A separate "Well" style existed briefly and was
        /// cut for exactly that reason — it was barely distinguishable from <see cref="Rows"/>, and its
        /// recessed body does more work here.)
        /// </para>
        /// </summary>
        Timeline = 2,
    }
}
