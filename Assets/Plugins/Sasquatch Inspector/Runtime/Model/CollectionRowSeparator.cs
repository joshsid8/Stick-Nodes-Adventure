namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How one element of a collection is visually separated from the next. Zebra striping tells rows apart
    /// by TINT, which stops working once a row is tall (an In-Line Editor row, or a rich ItemLayout) — the
    /// eye loses the boundary somewhere in the middle. A separator marks the boundary itself.
    /// <list type="bullet">
    /// <item><see cref="Line"/> — a hairline BETWEEN rows, sitting in its own gutter. Never drawn
    /// after the last row: a trailing rule reads as "there's more below". Best for short rows.</item>
    /// <item><see cref="Card"/> — each row is its own tray: a filled surface, rounded corners, real spacing,
    /// and an edge rail down the left side. The rail is the only cue that scales with row HEIGHT, so this is
    /// the one for tall rows (In-Line Editor rows especially).</item>
    /// <item><see cref="None"/> — no separator chrome at all (default). Separator chrome is something
    /// you opt into, not something you clear away, and a table already carries a header rule and a column
    /// divider, so a per-row hairline there would be noise on top of noise.</item>
    /// </list>
    /// A Table honours every one of these, <see cref="Card"/> included — the tray is simply drawn tighter
    /// there (a table is many rows scanned at once, so it cannot afford a List row's air). Its header takes
    /// the SAME horizontal inset as the trays, so every column stays locked under its own name.
    /// </summary>
    public enum CollectionRowSeparator
    {
        /// <summary>No separator chrome — rows are told apart only by their contents (and zebra, if on).</summary>
        None = 0,

        /// <summary>A hairline between rows, in a symmetric gutter. Not drawn after the last row.</summary>
        Line = 1,

        /// <summary>A filled, rounded tray per row with a left edge rail. A Table draws the tray tighter and
        /// insets its header to match, so the columns stay under their names.</summary>
        Card = 2,
    }
}
