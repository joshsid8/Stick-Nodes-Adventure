using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Post-compile companion to Smart Header Routing: when a [Header] is DELETED from code, the
    /// section container it once created is left behind with nothing anchoring it to the script.
    /// After a script import settles, this scans every registered layout for containers whose
    /// SourceHeader provenance no longer names any current [Header] on the type and asks — once
    /// per layout — whether to dissolve them (roll their fields up into the surrounding layout,
    /// bindings and styling intact), keep them (clears the provenance, so they become ordinary
    /// user containers and are never asked about again), or decide later (asks again next compile).
    ///
    /// Detection keys on provenance ONLY: user-made containers (empty SourceHeader) are never
    /// touched, and a renamed section is still found (shown under its current title).
    ///
    /// FULLY AUTOMATIC — there is deliberately no menu entry point. Every situation that can gate
    /// or drop a scan re-arms one when it clears: domain reload (the hook below), play-mode exit,
    /// the Layout Designer releasing its type (its clone blocks edits — see CleanupClone), and
    /// layout-asset imports (a version-control pull/revert can introduce orphans with no compile).
    /// </summary>
    internal static class OrphanedHeaderCleanup
    {
        private static bool _scanPending;

        // Types the user answered "Decide Later" for THIS domain. With several re-arm triggers
        // (designer close, play-mode exit, layout imports) a plain rescan would nag the same
        // dialog repeatedly; deferring per-domain preserves the promised contract — asks again
        // after the next compile (statics reset on the reload it causes).
        private static readonly HashSet<string> _deferredTypes = new();

        // Designer-blocked types already warned about this domain, so a designer left open across
        // repeated saves/imports logs the situation once instead of spamming the console.
        private static readonly HashSet<string> _warnedBlockedTypes = new();

        // Every domain reload (== every script compile) schedules a scan, so the feature never
        // depends on OnPostprocessAllAssets' exact invocation timing around a reload. The
        // postprocessor trigger is kept as well (harmless — debounced), and non-compile reloads
        // are cheap no-op scans (headers unchanged ⇒ nothing detected, nothing shown).
        [InitializeOnLoadMethod]
        private static void ScheduleAfterReload()
        {
            // A pending scan is deliberately dropped across a play-mode transition (a modal dialog
            // there is hostile) — and exiting play mode does NOT reload the domain, so without this
            // re-arm an orphan created right before Play would silently wait for the next compile.
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode) ScheduleScan();
            };
            ScheduleScan();
        }

        /// <summary>Debounced entry point (after-reload hook, asset postprocessor, play-mode exit,
        /// designer clone release). Defers past the compile/import pipeline and skips automation,
        /// like WelcomeBootstrap.</summary>
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

            // A modal dialog across a play-mode transition is hostile; the next compile re-asks.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _scanPending = false;
                return;
            }

            // Right after a reload the compile/import pipeline may still be busy — re-defer a frame.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenSettled;
                return;
            }

            _scanPending = false;
            RunScan();
        }

        private static void RunScan()
        {
            foreach (var type in LayoutRegistry.ResolveLayoutTypes())
            {
                try
                {
                    ScanType(type);
                }
                catch (Exception e)
                {
                    // One corrupt layout must never abort the sweep — every remaining type still
                    // gets its scan (a swallowed sweep here would silently leak real orphans).
                    Debug.LogError($"[Sasquatch Inspector] Removed-header scan failed on '{type.Name}' (skipped this pass): {e}");
                }
            }
        }

        private static void ScanType(Type type)
        {
            // Own layouts only — an inherited copy is a session-local view of the ANCESTOR's
            // layout; its orphans are the ancestor's to resolve.
            if (!LayoutRegistry.TryGetOwn(type, out var layout) || !(layout.Root is ContainerNode root)) return;

            // A type reflecting ZERO bindable fields is never legitimate for a layouted type —
            // it's a transient compile/rename state. Misreading it would flag every header as
            // deleted (mirrors ReconcileLayout's no-auto-remove guard).
            if (SasquatchReflectionUtility.GetTopLevelBindableProperties(type).Count == 0) return;

            var currentHeaders = UnboundFieldSync.GetCurrentHeaders(type);

            // The designer edits a CLONE; mutating the saved layout underneath it would be
            // silently clobbered by its next Save. A designer window holds its type even while
            // DOCKED BEHIND another tab, so never skip silently — detect (read-only), say why
            // once, and rely on the CleanupClone re-arm to scan again the moment it lets go.
            if (SasquatchLayoutDesignerWindow.IsEditingType(type))
            {
                if (FindOrphanedSections(root, currentHeaders).Count > 0 && _warnedBlockedTypes.Add(type.Name))
                {
                    Debug.LogWarning($"[Sasquatch Inspector] The layout for '{type.Name}' has section(s) from removed [Header]s, " +
                                     "but the Layout Designer is open on this type (possibly docked behind another tab). " +
                                     "The check re-runs automatically when that Designer closes or switches to another object.");
                }
                return;
            }

            // Layouts imported BEFORE provenance existed have empty SourceHeader everywhere, so
            // a deleted header would never be detected. While its header is still alive we can
            // recover the link: a provenance-less container titled exactly like a CURRENT
            // [Header] gets stamped now (silent migration, like the registry's GUID backfill),
            // so the NEXT deletion of that header is caught.
            if (BackfillProvenance(root, currentHeaders) > 0)
            {
                LayoutRegistry.PersistLayoutEdit(layout);
            }

            var orphans = FindOrphanedSections(root, currentHeaders);
            if (orphans.Count == 0) return;

            // "Decide Later" defers until the next compile — not until the next re-arm trigger.
            if (_deferredTypes.Contains(type.FullName)) return;

            PromptAndApply(type, layout, root, orphans);
        }

        /// <summary>PURE migration for pre-provenance layouts: stamps SourceHeader onto containers
        /// that have none but whose Title matches a CURRENT [Header] on the type (exactly the
        /// title-fallback rule Smart Header Routing uses when inserting). Only live headers are
        /// stamped — a title matching nothing could be a coincidence, never a detection basis.
        /// Returns how many containers were stamped (0 = nothing to persist).</summary>
        internal static int BackfillProvenance(ContainerNode root, List<string> currentHeaders)
        {
            if (root == null || currentHeaders == null || currentHeaders.Count == 0) return 0;
            var live = new HashSet<string>(currentHeaders);
            int stamped = 0;

            void Walk(ContainerNode container)
            {
                if (container.Children == null) return;
                foreach (var child in container.Children)
                {
                    if (child is ContainerNode c)
                    {
                        if (string.IsNullOrEmpty(c.SourceHeader) &&
                            !string.IsNullOrEmpty(c.Title) && live.Contains(c.Title))
                        {
                            c.SourceHeader = c.Title;
                            stamped++;
                        }
                        Walk(c);
                    }
                }
            }
            Walk(root);
            return stamped;
        }

        /// <summary>PURE detection: containers whose non-empty SourceHeader provenance names a
        /// [Header] that no longer exists on the type, in pre-order (outer before inner — the order
        /// dissolving must run in). Collection item templates are out of scope: header sections are
        /// top-level structures and never live there.</summary>
        internal static List<ContainerNode> FindOrphanedSections(ContainerNode root, List<string> currentHeaders)
        {
            var result = new List<ContainerNode>();
            if (root == null) return result;
            var live = new HashSet<string>(currentHeaders ?? new List<string>());

            void Walk(ContainerNode container)
            {
                if (container.Children == null) return;
                foreach (var child in container.Children)
                {
                    if (child is ContainerNode c)
                    {
                        if (!string.IsNullOrEmpty(c.SourceHeader) && !live.Contains(c.SourceHeader))
                            result.Add(c);
                        Walk(c);
                    }
                }
            }
            Walk(root);
            return result;
        }

        private static void PromptAndApply(Type type, InspectorLayoutAsset layout, ContainerNode root, List<ContainerNode> orphans)
        {
            var message = new StringBuilder();
            message.AppendLine($"The layout for '{type.Name}' has {(orphans.Count == 1 ? "a section container" : $"{orphans.Count} section containers")} whose [Header] no longer exists in the script:");
            message.AppendLine();
            foreach (var orphan in orphans)
            {
                string title = string.IsNullOrEmpty(orphan.Title) ? orphan.SourceHeader : orphan.Title;
                message.AppendLine($"    •  {title}");
            }
            message.AppendLine();
            message.AppendLine("Dissolve: remove the container(s) and roll their fields up into the surrounding layout — bindings and field styling are kept.");
            message.AppendLine();
            message.Append("Keep: the container(s) become regular sections you own; you won't be asked again. Both choices support Undo.");

            int choice = EditorUtility.DisplayDialogComplex(
                "Sasquatch Inspector — Removed [Header] Sections",
                message.ToString(),
                "Dissolve Containers", // 0
                "Decide Later",        // 1 (cancel/Esc — asks again after the next compile)
                "Keep Containers");    // 2

            if (choice == 1)
            {
                _deferredTypes.Add(type.FullName);
                return;
            }

            if (choice == 0)
            {
                LayoutRegistry.BeginLayoutEditUndo(layout, "Dissolve Removed Header Sections");
                foreach (var orphan in orphans)
                    LayoutStructureUtility.DissolveContainer(root, orphan);
                LayoutRegistry.PersistLayoutEdit(layout);
            }
            else
            {
                LayoutRegistry.BeginLayoutEditUndo(layout, "Keep Removed Header Sections");
                foreach (var orphan in orphans)
                    orphan.SourceHeader = "";
                LayoutRegistry.PersistLayoutEdit(layout);
            }

            SasquatchEditorRegistrar.RefreshInspectors();
        }
    }
}
