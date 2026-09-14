namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The overall look of a Sasquatch dictionary table — the CONTAINER-level choice: the shell, the column
    /// header, the density, and how the key presents itself. Deliberately the same shape as
    /// <see cref="CollectionListStyle"/> is for a List, and deliberately orthogonal to
    /// <see cref="CollectionRowSeparator"/>, which owns the ROW surface (bare / hairline / tray) — so every
    /// style works with every frame, and a dictionary reads as a sibling of the lists beside it rather than
    /// as an unrelated widget.
    /// <para>
    /// Everything the styles share — the reorder grip, the per-row right-click menu, the duplicate/empty-key
    /// banner and marks, the header count, the Add strip, the empty state, and full Sasquatch value editors
    /// — is NOT a style choice and is drawn the same way by all four.
    /// </para>
    /// <para>
    /// This drives BOTH dictionary shapes: Unity 6.6's native <c>Dictionary&lt;K,V&gt;</c> under
    /// <see cref="DisplayType.Dictionary"/>, and this package's own <c>SerializedDictionary</c>, which
    /// auto-routes to the same table. One enum, one renderer, so the two cannot drift.
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Table"/> — the structured default: a bordered shell and a Key / Value column header.</item>
    /// <item><see cref="Minimal"/> — no shell at all; quiet column hints and air carry the rhythm.</item>
    /// <item><see cref="Ledger"/> — a recessed well with zebra rows and one continuous rule down the column
    /// split, built to be SCANNED rather than read a row at a time.</item>
    /// <item><see cref="Mapping"/> — the key becomes a chip and an arrow replaces the column header, so a
    /// row reads as a sentence ("Rare → 0.09") rather than as a grid cell.</item>
    /// </list>
    /// </summary>
    public enum DictionaryTableStyle
    {
        /// <summary>Bordered shell, Key / Value column header with a rule under it. The default, and what
        /// every dictionary rendered before this enum existed — so 0 is the back-compatible value.</summary>
        Table = 0,

        /// <summary>
        /// No shell and no header rule. The column names shrink to small-caps hints and the rows take more
        /// vertical room; spacing does the work the borders did. The direct twin of
        /// <see cref="CollectionListStyle.Minimal"/>, so a Minimal list and a Minimal dictionary in one
        /// inspector agree.
        /// </summary>
        Minimal = 1,

        /// <summary>
        /// A recessed well with the rows running edge to edge inside it, zebra striping, and a continuous
        /// vertical rule between the key and value columns. The rule is what separates this from a tinted
        /// <see cref="Table"/>: it runs the full height of the body, so the eye tracks a row across without
        /// needing a box around it. The style for a dictionary with many short entries.
        /// </summary>
        Ledger = 2,

        /// <summary>
        /// The key renders as a rounded chip in a fixed lane with an arrow glyph between it and the value,
        /// and there is no column header at all — the arrow carries the meaning the header used to.
        /// <para>
        /// The chip is still the real, editable key field; only its chrome changes (the cell takes the pill
        /// and the inner input goes flat), so every key type keeps its own editor. Best on short, enum-ish
        /// keys: the lane is fixed, so a much longer key clips rather than widening every row.
        /// </para>
        /// </summary>
        Mapping = 3,
    }
}
