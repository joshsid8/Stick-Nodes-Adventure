namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Single source of truth for the serialized-layout SCHEMA version, and the central registry for all future
    /// structural migrations of saved layouts.
    ///
    /// Two kinds of "migration" coexist deliberately:
    ///   • Idempotent per-node VALUE clamps (e.g. a retired enum int snapped back into range) stay in each node's
    ///     <c>OnAfterDeserialize</c> — they're safe to run on every load and need no version gate.
    ///   • STRUCTURAL changes (a node reshaped, a field split, an enum whose meaning changed) go through
    ///     <see cref="Migrate"/>, gated on <see cref="CurrentVersion"/>. Bump the version and add a case whenever a
    ///     saved tree needs a one-time transform to stay correct under new code.
    ///
    /// The version is stamped onto <see cref="SasquatchInspectorDatabase"/> and <see cref="InspectorLayoutAsset"/>
    /// at every save (see <c>LayoutRegistry</c>); on load, a file stamped HIGHER than <see cref="CurrentVersion"/>
    /// triggers a single non-blocking "saved by a newer Sasquatch Inspector" warning, and a LOWER stamp runs
    /// <see cref="Migrate"/>. This is insurance against a future structural change silently
    /// corrupting old layouts.
    /// </summary>
    public static class SasquatchSchema
    {
        /// <summary>
        /// Current layout-schema version, stamped onto every saved database / standalone layout asset. A legacy
        /// asset written before the stamp existed deserializes its version field as 0, which is correctly treated
        /// as "older than v1" and migrated. Bump this AND add a block to <see cref="Migrate"/>
        /// whenever a structural change needs a one-time transform of already-saved layouts.
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Brings a single layout tree (<paramref name="root"/>) saved at <paramref name="storedVersion"/> up to
        /// <see cref="CurrentVersion"/> by applying every structural migration in between, in ascending order.
        /// Returns the version the tree is now at (always <see cref="CurrentVersion"/> when
        /// <paramref name="storedVersion"/> ≤ it).
        ///
        /// CONTRACT: migrations here MUST be idempotent (safe to re-run on an already-migrated tree). LayoutRegistry
        /// runs this on load and does NOT force-persist the version bump, so the same <paramref name="storedVersion"/>
        /// can be seen again next session. If a future migration can't be made idempotent, also arrange to persist
        /// the bump (mark the asset dirty + flush) at the call site.
        ///
        /// The one structural migration so far is v1 → v2 (the flat Conditions list → outcome-grouped ConditionRules).
        /// Add future steps as: <c>if (storedVersion &lt; N) { /* transform root in place */ }</c>, ascending, no
        /// early return between them.
        /// </summary>
        public static int Migrate(LayoutNode root, int storedVersion)
        {
            // (root may legitimately be null for an empty entry — nothing to transform.)
            if (storedVersion < 2)
            {
                // v1 → v2: the flat per-condition Conditions list (each entry carrying its own outcome, folded
                // independently) becomes outcome-grouped ConditionRules. Idempotent — EnsureMigrated skips any node
                // whose rules are already populated, and it leaves the legacy list intact — so it's safe to re-run
                // even though LayoutRegistry doesn't force-persist the version bump.
                ConditionModel.EnsureMigratedTree(root);
            }
            // if (storedVersion < 3) { ... }
            return CurrentVersion;
        }
    }
}
