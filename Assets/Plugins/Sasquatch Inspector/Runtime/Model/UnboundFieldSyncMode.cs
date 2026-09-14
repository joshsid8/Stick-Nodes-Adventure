namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How the real inspector keeps a type's registered layout in sync with its script: fields added,
    /// deleted, or renamed in code after the layout was authored. (Member names predate the full
    /// reconcile and are kept for serialized-settings compatibility.)
    /// </summary>
    public enum UnboundFieldSyncMode
    {
        /// <summary>
        /// Full auto-sync: new fields are inserted to match their placement in code, deleted fields are
        /// removed (a temporarily commented-out field counts as deleted — undo restores it), and renamed
        /// fields are rewritten in place so their styling/position survive ([FormerlySerializedAs] makes
        /// a rename unambiguous; otherwise same-type/same-scope pairing is used).
        /// </summary>
        AutoAppend = 0,
        /// <summary>Never mutate automatically: a dismissible banner reports added/removed/renamed fields with a one-click Sync.</summary>
        NotificationBanner = 1,
        /// <summary>Ignore script changes; manage layouts manually in the designer. Deleted fields still render hidden.</summary>
        DoNothing = 2
    }
}
