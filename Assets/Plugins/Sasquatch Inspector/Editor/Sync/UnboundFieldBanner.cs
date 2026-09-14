using System;
using System.Collections.Generic;
using SasquatchSpring;
using UnityEditor;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// NotificationBanner mode of the unbound-field handling setting: instead of mutating the layout,
    /// the real inspector shows a slim dismissible row reporting how the layout has drifted from the
    /// script (added / removed / renamed fields) with a one-click Sync. Dismissal lasts the editor
    /// session (SessionState) and is keyed to the SET of changes, so a later, different batch re-shows
    /// a previously dismissed banner.
    /// </summary>
    public static class UnboundFieldBanner
    {
        /// <summary>
        /// Returns the banner element, or null when there's nothing to offer (layout in sync, dismissed,
        /// or this layout still needed its ledger initialized — which this call performs).
        /// </summary>
        public static VisualElement TryCreate(Type targetType, InspectorLayoutAsset layout,
            bool applyAttributes, Action rebuildInspector)
        {
            if (targetType == null || layout == null) return null;

            var diff = UnboundFieldSync.ComputeDiff(
                targetType, layout.Root, layout.SyncedFieldPaths, layout.SyncedFieldTypes);
            if (diff.NeedsLedgerInit)
            {
                UnboundFieldSync.InitializeLedger(targetType, layout);
                return null;
            }

            // [Space] ops are attribute imports — gated by the same setting as insert-time attribute mapping.
            if (!applyAttributes)
            {
                diff.SpaceAdds.Clear();
                diff.SpaceMarks.Clear();
                diff.SpaceClears.Clear();
            }
            if (diff.IsEmpty) return null;

            // A diff of only SpaceMarks/SpaceClears is invisible flag bookkeeping — nothing to review, so
            // don't nag: apply it silently and show no banner.
            if (!diff.HasVisibleChanges)
            {
                UnboundFieldSync.ReconcileLayout(targetType, layout, applyAttributes);
                return null;
            }

            string dismissKey = BuildDismissKey(targetType, layout, diff);
            if (SessionState.GetBool(dismissKey, false)) return null;

            var banner = new VisualElement();
            banner.AddToClassList("sasq-sync-banner");
            // The banner sits on the editor's root container, OUTSIDE the layout view that gets the
            // package stylesheet attached in LayoutTreeBuilder.Build — so it loads the sheet (and the
            // skin token sheet, or the --sasq-* variables never reach it) itself.
            SasquatchSkin.ApplyWithWidgetSheet(banner);

            var label = new Label(ComposeMessage(diff));
            label.AddToClassList("sasq-sync-banner__label");
            banner.Add(label);

            var syncButton = new Button(() =>
            {
                UnboundFieldSync.ReconcileLayout(targetType, layout, applyAttributes);
                rebuildInspector?.Invoke();
            }) { text = "Sync now" };
            syncButton.AddToClassList("sasq-sync-banner__btn");
            banner.Add(syncButton);

            var dismissButton = new Button(() =>
            {
                SessionState.SetBool(dismissKey, true);
                if (EditorSpringFX.MotionEnabled)
                {
                    // Fold shut + fade, then leave the tree for real.
                    banner.SpringOpacity(0f, EditorSpringFX.Tune(SpringConfig.Snappy));
                    SpringFold.SetExpanded(banner, false, EditorSpringFX.Tune(EditorSpringFX.FoldConfig),
                        _ => banner.RemoveFromHierarchy());
                }
                else
                {
                    banner.RemoveFromHierarchy();
                }
            }) { text = "×", tooltip = "Dismiss for this editor session" };
            dismissButton.AddToClassList("sasq-sync-banner__dismiss");
            banner.Add(dismissButton);

            // Spring entrance — it's a notification, it should arrive like one. Guarded so a
            // re-attach (dock/undock) doesn't replay it; rebuilds create fresh instances anyway.
            bool entered = false;
            banner.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (entered) return;
                entered = true;
                EditorSpringFX.SlideInY(banner, -8f);
            });

            return banner;
        }

        private static string ComposeMessage(LayoutSyncDiff diff)
        {
            var parts = new List<string>(4);
            if (diff.Added.Count > 0) parts.Add($"{diff.Added.Count} added");
            if (diff.Removed.Count > 0) parts.Add($"{diff.Removed.Count} removed");
            int renamed = diff.Renamed.Count + diff.ScopeRenames.Count;
            if (renamed > 0) parts.Add($"{renamed} renamed");
            if (diff.SpaceAdds.Count > 0) parts.Add($"{diff.SpaceAdds.Count} new [Space]");
            return $"Layout out of sync with script: {string.Join(", ", parts)}.";
        }

        private static string BuildDismissKey(Type targetType, InspectorLayoutAsset layout, LayoutSyncDiff diff)
        {
            string typeKey = !string.IsNullOrEmpty(layout.ScriptGuid) ? layout.ScriptGuid : targetType.FullName;

            // Canonical, order-independent change-set lines hashed with FNV-1a (string.GetHashCode isn't
            // guaranteed stable across domain reloads/runtimes) — a different batch re-shows the banner.
            var lines = new List<string>();
            foreach (var a in diff.Added) lines.Add("add:" + a.path);
            foreach (var r in diff.Removed) lines.Add("del:" + r.ResolvedPath);
            foreach (var r in diff.Renamed) lines.Add("ren:" + r.OldResolvedPath + ">" + r.NewResolvedPath);
            foreach (var r in diff.ScopeRenames) lines.Add("ren:" + r.OldResolvedPath + ">" + r.NewResolvedPath);
            foreach (var s in diff.SpaceAdds) lines.Add("spc:" + s);
            lines.Sort(StringComparer.Ordinal);

            uint hash = 2166136261;
            foreach (var line in lines)
            {
                foreach (char c in line)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                hash ^= '\n';
                hash *= 16777619;
            }

            return $"SasquatchInspector.SyncBanner.{typeKey}.{hash:x8}";
        }
    }
}
