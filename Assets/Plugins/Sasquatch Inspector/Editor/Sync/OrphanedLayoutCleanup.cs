using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Automatically prunes an inspector layout when its backing script is DELETED, so no dead layouts accumulate.
    /// Deleting a script triggers a domain reload; after it settles this scans for per-type layout ASSET files
    /// (the primary store — see <see cref="LayoutRegistry.FindOrphanedLayoutAssetPaths"/>) AND any not-yet-migrated
    /// central-database entries (legacy read path — see <see cref="LayoutRegistry.FindOrphanedEntries"/>) whose
    /// MonoScript GUID resolves to no asset AND whose type name resolves to no loaded type, and removes them
    /// silently — no menu, no prompt.
    ///
    /// Two safety properties fall out of the predicate: (1) a mere COMPILE ERROR never prunes anything — the
    /// script's .cs still exists, so its GUID still resolves; (2) a script deleted and RE-CREATED under the
    /// same full name before the debounced scan runs is tolerated — its type resolves again, so the entry is
    /// left alone and the type-name fallback re-associates the layout. Only a script that stays deleted loses
    /// its layout.
    ///
    /// Mirrors <see cref="OrphanedHeaderCleanup"/>'s debounce/guard structure. The reliable trigger is the
    /// after-reload hook below (a postprocessor-scheduled delayCall would be wiped by the reload that a script
    /// deletion causes); the postprocessor also pokes <see cref="ScheduleScan"/> for parity.
    /// </summary>
    internal static class OrphanedLayoutCleanup
    {
        private static bool _scanPending;

        // Every domain reload (== every script compile, including a deletion) schedules a scan, so the feature
        // never depends on OnPostprocessAllAssets' delayCall surviving the reload. Non-deletion reloads are
        // cheap no-op scans (nothing is orphaned ⇒ nothing removed).
        [InitializeOnLoadMethod]
        private static void ScheduleAfterReload()
        {
            // Same play-mode re-arm as OrphanedHeaderCleanup: a scan dropped across a play transition
            // would otherwise wait for the next compile (exiting play mode does not reload the domain,
            // e.g. a script deleted during play via "Recompile And Continue Playing").
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode) ScheduleScan();
            };
            ScheduleScan();
        }

        /// <summary>Debounced entry point (after-reload hook + asset postprocessor). Defers past the
        /// compile/import pipeline and skips automation, like OrphanedHeaderCleanup.</summary>
        public static void ScheduleScan()
        {
            if (_scanPending) return;
            _scanPending = true;
            EditorApplication.delayCall += RunWhenSettled;
        }

        private static void RunWhenSettled()
        {
            // Never interrupt automation — CI / -batchmode, the test runner, or a player build.
            if (Application.isBatchMode || BuildPipeline.isBuildingPlayer ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-runTests") >= 0)
            {
                _scanPending = false;
                return;
            }

            // Don't judge liveness across a play-mode transition.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _scanPending = false;
                return;
            }

            // Right after a reload the compile/import pipeline may still be busy — re-defer a frame so GUID and
            // type resolution are trustworthy.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenSettled;
                return;
            }

            _scanPending = false;
            RunPrune();
        }

        private static void RunPrune()
        {
            // Capture names BEFORE removal for one quiet breadcrumb — if a script was deleted by accident, the dev
            // can see the layout went with it.

            // Primary store: per-type layout ASSET files whose script was deleted.
            var orphanedAssetPaths = LayoutRegistry.FindOrphanedLayoutAssetPaths();
            var assetNames = orphanedAssetPaths.Select(PrettyAssetName).ToList();
            int removedAssets = LayoutRegistry.RemoveLayoutAssets(orphanedAssetPaths);

            // Legacy read path: entries still in a not-yet-migrated central database.
            var orphanedEntries = LayoutRegistry.FindOrphanedEntries();
            var entryNames = orphanedEntries.Select(PrettyName).ToList();
            int removedEntries = LayoutRegistry.RemoveEntries(orphanedEntries);

            int removed = removedAssets + removedEntries;
            if (removed <= 0) return;

            var names = assetNames.Concat(entryNames).ToList();
            Debug.Log($"[Sasquatch Inspector] Removed {removed} orphaned inspector layout{(removed == 1 ? "" : "s")} " +
                      $"for deleted script{(removed == 1 ? "" : "s")}: {string.Join(", ", names)}.");
        }

        // Human-readable label: the type's FullName (assembly qualifier stripped), falling back to the GUID.
        private static string PrettyName(SasquatchDatabaseEntry entry)
            => PrettyTypeName(entry.TargetTypeName, entry.ScriptGuid);

        // Same label for a standalone layout asset, loaded (before deletion) to read its stored type name.
        private static string PrettyAssetName(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
            return asset != null
                ? PrettyTypeName(asset.TargetTypeName, asset.ScriptGuid)
                : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        private static string PrettyTypeName(string typeName, string scriptGuid)
        {
            if (!string.IsNullOrEmpty(typeName))
            {
                int comma = typeName.IndexOf(',');
                return comma > 0 ? typeName.Substring(0, comma).Trim() : typeName;
            }
            return string.IsNullOrEmpty(scriptGuid) ? "(unknown)" : $"(script {scriptGuid})";
        }
    }
}
