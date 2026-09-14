using UnityEditor;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Refreshes Sasquatch Inspector's caches (layout registry, settings, palette library, script-GUID memo, package
    /// paths) when relevant assets are imported, moved, or deleted — then rebuilds open inspectors.
    /// </summary>
    public class SasquatchAssetPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Set (save/restore, re-entrancy-safe) by <see cref="LayoutRegistry"/>'s own save/remove paths around the
        /// asset write. When true, the global <see cref="ActiveEditorTracker.ForceRebuild"/> below is skipped: an
        /// in-process save already does a PRECISE per-type refresh (<see cref="SasquatchEditorRegistrar.RefreshInspectorsForType"/>),
        /// so the blanket rebuild that recreates EVERY editor on the selection (the ~2s post-save churn — it also
        /// double-builds a displaced body) is pure waste there. EXTERNAL layout changes (version-control pull, a
        /// revert, a hand-edited asset) leave this false and still get the global rebuild. The layout .asset write
        /// fires this postprocessor synchronously, so the flag is reliably observed.
        /// </summary>
        internal static bool SuppressLayoutForceRebuild;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool changed = false;

            // Any .asset in this batch could BE the central database — created, deleted or relocated — and
            // that changes where SasquatchPaths.DatabasePath resolves without touching a single script. Drop
            // the memo up front, once, rather than per path: the loops below call LayoutRegistry.MarkChanged
            // per touched file and MarkChanged's first act is to read DatabasePath, so invalidating here is
            // also what keeps a batch delete of N files to ONE project-wide index walk instead of N.
            // Only the located-asset memo is dropped — the package root and stylesheet caches are expensive to
            // refill and no .asset can move them.
            if (TouchesAsset(importedAssets) || TouchesAsset(deletedAssets)
                || TouchesAsset(movedAssets) || TouchesAsset(movedFromAssetPaths))
            {
                SasquatchPaths.InvalidateLocatedAssetCache();

                // Same reasoning, same gate: the preset-exists latch says "this project already has a Layout
                // Preset", and any .asset appearing, vanishing or moving can make that answer wrong. Dropped
                // unconditionally rather than type-tested, because deciding whether a DELETED path was a
                // preset costs a load that cannot succeed — and a dropped latch only ever costs one query.
                SessionState.EraseBool(LayoutPresetBootstrap.PresetsExistKey);
            }

            // A script import/move/delete may be a rename that retired the old Type and introduced a new one
            // under the same MonoScript GUID. Drop the Type↔GUID memo and the registry cache so layouts
            // re-resolve through their (unchanged) GUID under the new type.
            if (TouchesScript(importedAssets) || TouchesScript(deletedAssets) || TouchesScript(movedAssets) || TouchesScript(movedFromAssetPaths))
            {
                ScriptGuidUtility.InvalidateCache();
                LayoutRegistry.InvalidateCache();
                SasquatchPaths.InvalidateCache(); // package may have moved → re-resolve root/stylesheets
                changed = true;

                // A script change may have deleted a [Header] whose section container is now
                // orphaned — offer cleanup once the import pipeline settles.
                OrphanedHeaderCleanup.ScheduleScan();

                // A DELETED script leaves its inspector layout orphaned in the database — remove it
                // automatically once the pipeline settles (debounced/guarded in OrphanedLayoutCleanup; the
                // reliable trigger is its after-reload hook, since this delayCall is wiped by the reload).
                if (TouchesScript(deletedAssets))
                    OrphanedLayoutCleanup.ScheduleScan();
            }

            foreach (string str in importedAssets)
            {
                if (str.EndsWith(".asset") && IsLayoutAsset(str))
                {
                    LayoutRegistry.MarkChanged(str);
                    changed = true;

                    // A layout asset can change with NO compile involved (version-control pull or
                    // revert, a designer save) and (re)introduce section containers whose [Header]
                    // is already gone from code — poke the removed-header scan so those orphans
                    // never sit waiting for the next compile. Debounced; no-op on clean layouts.
                    OrphanedHeaderCleanup.ScheduleScan();
                }
                if (str.EndsWith(".asset") && IsSettingsAsset(str))
                {
                    SasquatchSettingsManager.InvalidateCache();
                }
                if (str.EndsWith(".asset") && IsPaletteLibraryAsset(str))
                {
                    SasquatchPaletteLibraryManager.InvalidateCache();
                }
            }

            foreach (string str in deletedAssets)
            {
                if (str.EndsWith(".asset"))
                {
                    // Since it is deleted, we can't load it to check type, so we invalidate the cache on any deleted asset
                    LayoutRegistry.MarkChanged(str);
                    SasquatchSettingsManager.InvalidateCache();
                    SasquatchPaletteLibraryManager.InvalidateCache();
                    changed = true;
                }
            }

            foreach (string str in movedAssets)
            {
                if (str.EndsWith(".asset") && IsLayoutAsset(str))
                {
                    LayoutRegistry.MarkChanged(str);
                    changed = true;
                }
                if (str.EndsWith(".asset") && IsSettingsAsset(str))
                {
                    SasquatchSettingsManager.InvalidateCache();
                }
                if (str.EndsWith(".asset") && IsPaletteLibraryAsset(str))
                {
                    SasquatchPaletteLibraryManager.InvalidateCache();
                }
            }

            // Our own in-process layout save/remove already does a precise per-type inspector refresh
            // (SasquatchEditorRegistrar.RefreshInspectorsForType); skip the global rebuild that would recreate
            // every editor on the selection — that blanket churn is the post-save lag. EXTERNAL changes (VC pull,
            // revert, a hand-edited asset) leave the flag clear and still get the global rebuild.
            if (changed && !SuppressLayoutForceRebuild)
            {
                ActiveEditorTracker.sharedTracker.ForceRebuild();

                // The real Inspector now shows the external change — but an open Layout Designer is still
                // holding the clone it took BEFORE it, and its next Save writes that clone wholesale. Hand the
                // change to any open designer so the two cannot silently disagree. Same suppression gate as the
                // rebuild above: our own in-process saves are already the designer's own work, so they skip this.
                // (Audit 2026-08-09, finding D1.)
                SasquatchLayoutDesignerWindow.AdoptExternalLayoutChange(null);
            }
        }

        private static bool TouchesScript(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.EndsWith(".cs")) return true;
            }
            return false;
        }

        private static bool TouchesAsset(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.EndsWith(".asset")) return true;
            }
            return false;
        }

        // ---- Type-testing an imported .asset WITHOUT deserialising it -------------------------------------
        //
        // These three answer "is this one of ours?" for every .asset in the import batch. They used to answer
        // it with AssetDatabase.LoadAssetAtPath, which DESERIALISES the asset — so on a first project open, a
        // Library rebuild or a large version-control pull, the package deserialised every ScriptableObject in
        // the BUYER's project just to discover that almost none of them were its own.
        //
        // GetMainAssetTypeAtPath answers the identical question from the asset database's own type metadata,
        // with no object load. It is exact rather than merely close: it reports the same concrete type the
        // loaded object would have had, so `IsAssignableFrom` accepts exactly the set `is` accepted, including
        // subclasses. (Verified across all 186 .asset files in this project: 186/186 agreement, and
        // TestImportedAssetTypeTestMatchesLoading re-checks that over the whole project on every run.)
        //
        // A PATH SCOPE was the other candidate and is deliberately NOT used: DatabasePath and the layout
        // assets are documented to survive relocation anywhere in the project, so scoping to the package's own
        // folder would silently stop a relocated layout from refreshing inspectors on an external change.
        internal static bool IsLayoutAsset(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null
                && (typeof(InspectorLayoutAsset).IsAssignableFrom(type)
                    || typeof(SasquatchInspectorDatabase).IsAssignableFrom(type));
        }

        internal static bool IsSettingsAsset(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null && typeof(SasquatchSettings).IsAssignableFrom(type);
        }

        internal static bool IsPaletteLibraryAsset(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null && typeof(SasquatchColorPaletteLibrary).IsAssignableFrom(type);
        }
    }
}
