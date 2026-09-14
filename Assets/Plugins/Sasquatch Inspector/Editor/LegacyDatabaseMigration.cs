using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// One-time, silent migration from the OLD single central <see cref="SasquatchInspectorDatabase"/> asset to the
    /// version-control-friendly per-type layout assets under <see cref="SasquatchPaths.LayoutsDirectory"/>. Runs on
    /// editor load (like <see cref="OrphanedLayoutCleanup"/>): if any database asset exists, every entry is converted
    /// to its own <see cref="InspectorLayoutAsset"/> and the database is deleted — no dialog, no user action, no
    /// recompile (assets only). Idempotent: with no database present it is an instant no-op, so it costs nothing on
    /// every subsequent load.
    ///
    /// Loss-proof by design: entries are migrated VERBATIM, orphans included — liveness is never judged here
    /// (<see cref="OrphanedLayoutCleanup"/> prunes dead layouts afterward). An entry whose type is already covered by
    /// an existing standalone asset is skipped (existing wins); across multiple databases the first (ordinal-sorted)
    /// wins per type. A read-only database (e.g. under <c>Packages/</c>) that can't be deleted has its entries copied
    /// out and cleared in memory — the per-type assets take precedence for lookups regardless.
    /// </summary>
    internal static class LegacyDatabaseMigration
    {
        [InitializeOnLoadMethod]
        private static void Schedule() => EditorApplication.delayCall += RunWhenSettled;

        private static void RunWhenSettled()
        {
            // Never judge liveness / mutate assets across a play-mode transition or while the compile/import pipeline
            // is still busy — re-defer a frame until the editor settles. (Unlike OrphanedLayoutCleanup this does NOT
            // skip -batchmode: a CI/import run should still migrate a stale database it happens to find.)
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenSettled;
                return;
            }

            MigrateAllDatabases();
        }

        /// <summary>
        /// Converts every entry in every existing central database to a per-type layout asset, then deletes the
        /// database(s). Returns the number of per-type assets created (0 when there is nothing to migrate).
        /// </summary>
        internal static int MigrateAllDatabases()
        {
            // Shares SasquatchPaths' memo of this exact search rather than running its own. This hook fires on
            // EVERY domain reload — every script compile, every play-mode enter — and long after the one-time
            // migration completed it was still paying a full project-wide index walk to learn there was
            // nothing to migrate. The memo is dropped by any .asset import/delete/move, so a database that
            // turns up later (a version-control pull, a package re-import) is still seen and still migrated.
            string[] dbGuids = SasquatchPaths.DatabaseAssetGuids;
            if (dbGuids.Length == 0) return 0; // nothing to migrate — the common steady-state fast path

            var dbPaths = new List<string>();
            foreach (var g in dbGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!string.IsNullOrEmpty(p)) dbPaths.Add(p);
            }
            if (dbPaths.Count == 0) return 0;
            dbPaths.Sort(StringComparer.Ordinal); // deterministic first-DB-wins order

            // Pre-scan the standalone per-type assets that ALREADY exist (before the batch, so FindAssets is current).
            // A type covered by one of these is skipped — the existing asset wins over the database entry.
            var coveredKeys = SnapshotExistingLayoutKeys(dbPaths);
            // Deterministic target paths claimed THIS run — batch-safe dedup (FindAssets can't see same-batch
            // creations while StartAssetEditing holds), giving first-DB-wins across multiple databases.
            var claimedPaths = new HashSet<string>(StringComparer.Ordinal);

            int migrated = 0;
            int skippedExisting = 0;
            var migratedDbs = new List<SasquatchInspectorDatabase>();

            // Ensure the destination folder BEFORE the batch: AssetDatabase.CreateFolder inside StartAssetEditing
            // sees a stale IsValidFolder and would spawn numbered duplicate folders ("Layouts 1", "Layouts 2", …).
            LayoutRegistry.EnsureLayoutsFolder();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var dbPath in dbPaths)
                {
                    var db = AssetDatabase.LoadAssetAtPath<SasquatchInspectorDatabase>(dbPath);
                    if (db == null) continue;

                    foreach (var entry in db.Entries)
                    {
                        if (entry == null) continue;
                        // An entry with neither key is inert junk — nothing to migrate.
                        if (string.IsNullOrEmpty(entry.TargetTypeName) && string.IsNullOrEmpty(entry.ScriptGuid))
                            continue;

                        if (IsCovered(coveredKeys, entry.ScriptGuid, entry.TargetTypeName)) { skippedExisting++; continue; }

                        string targetPath = SasquatchPaths.GetLayoutAssetPath(entry.ScriptGuid, entry.TargetTypeName);
                        if (!claimedPaths.Add(targetPath)) { skippedExisting++; continue; } // first DB wins this type

                        LayoutRegistry.CreateMigratedLayoutAsset(entry, targetPath);
                        migrated++;
                    }

                    migratedDbs.Add(db);
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Delete the migrated database(s). A read-only one (e.g. under Packages/) can't be deleted — clear its
            // entries in memory so it stops contributing this session (per-type assets win lookups anyway) and warn.
            foreach (var db in migratedDbs)
            {
                if (db == null) continue;
                string p = AssetDatabase.GetAssetPath(db);
                bool deleted = false;
                try { deleted = !string.IsNullOrEmpty(p) && AssetDatabase.DeleteAsset(p); }
                catch { deleted = false; }

                if (!deleted)
                {
                    db.Entries.Clear();
                    EditorUtility.SetDirty(db);
                    Debug.LogWarning($"[Sasquatch Inspector] Could not delete the legacy layout database '{p}' (read-only?). " +
                                     "Its entries were migrated to per-type assets, which now take precedence — delete " +
                                     "the file manually if you can.");
                }
            }

            LayoutRegistry.InvalidateCache();
            SasquatchEditorRegistrar.RefreshInspectors();

            if (migrated > 0)
                Debug.Log($"[Sasquatch Inspector] Migrated {migrated} inspector layout{(migrated == 1 ? "" : "s")} to per-type " +
                          $"assets under {SasquatchPaths.LayoutsDirectory} (version-control friendly)." +
                          (skippedExisting > 0 ? $" ({skippedExisting} already had a per-type asset — left as-is.)" : ""));

            return migrated;
        }

        // Builds the set of keys (GUID + assembly-qualified name + simple name) already covered by a standalone
        // per-type layout asset, so migration never duplicates a type that already has one. Skips the database
        // assets' own paths defensively.
        private static HashSet<string> SnapshotExistingLayoutKeys(List<string> dbPaths)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var dbPathSet = new HashSet<string>(dbPaths, StringComparer.Ordinal);

            foreach (string guid in AssetDatabase.FindAssets("t:InspectorLayoutAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || dbPathSet.Contains(path)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (asset == null) continue;
                AddKeys(keys, asset.ScriptGuid, asset.TargetTypeName);
            }
            return keys;
        }

        private static bool IsCovered(HashSet<string> keys, string scriptGuid, string typeName)
        {
            if (!string.IsNullOrEmpty(scriptGuid) && keys.Contains("g:" + scriptGuid)) return true;
            if (!string.IsNullOrEmpty(typeName))
            {
                if (keys.Contains("t:" + typeName)) return true;
                int comma = typeName.IndexOf(',');
                if (comma > 0 && keys.Contains("t:" + typeName.Substring(0, comma).Trim())) return true;
            }
            return false;
        }

        private static void AddKeys(HashSet<string> keys, string scriptGuid, string typeName)
        {
            if (!string.IsNullOrEmpty(scriptGuid)) keys.Add("g:" + scriptGuid);
            if (!string.IsNullOrEmpty(typeName))
            {
                keys.Add("t:" + typeName);
                int comma = typeName.IndexOf(',');
                if (comma > 0) keys.Add("t:" + typeName.Substring(0, comma).Trim());
            }
        }
    }
}
