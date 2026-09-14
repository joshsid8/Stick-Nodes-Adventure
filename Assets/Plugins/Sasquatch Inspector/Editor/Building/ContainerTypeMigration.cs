using System.Collections.Generic;
using System.Linq;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// What happens to a Tabs container's tab sub-containers when the container is re-typed to something
    /// else. Consulted only when LEAVING Tabs.
    /// </summary>
    internal enum LeaveTabsMode
    {
        /// <summary>Leave each tab in place as an ordinary child group, surfacing its <c>TabLabel</c> as the
        /// group <c>Title</c> so the former tabs stay tellable apart. Non-destructive, and therefore the
        /// correct default for any edit point that cannot reasonably raise a confirmation dialog.</summary>
        KeepAsGroups,

        /// <summary>Promote each tab's direct children up one level and drop the tab wrappers. DESTRUCTIVE —
        /// the wrappers, their titles and their styling are gone and undo is the only way back. Only ever
        /// pass this in response to an explicit user choice.</summary>
        Flatten,
    }

    /// <summary>
    /// Structural child migration for a <see cref="ContainerNode"/> changing <see cref="ContainerNode.Type"/>
    /// — the ContainerType twin of <see cref="FrameShapeMigration"/>, and for the same reason: for two
    /// container kinds the direct children ARE the structure, not merely content.
    ///
    /// A Tabs container's children are its tab PAGES (the builder renders child N as tab N), and a
    /// DiagonalSplit Frame's children are its two panes (identity is positional). So assigning
    /// <c>Type</c> raw leaves the children in a shape the new type reads completely differently: a Rows
    /// container holding eight widgets re-typed to Tabs becomes eight inert one-item tabs, and seven of
    /// every eight widgets disappear from the live inspector — in both render paths and in exported
    /// standalone editors, which reproduce the model faithfully.
    ///
    /// Pure model operations: the caller owns Undo recording, any confirmation dialog and every refresh.
    /// The BUILDER never calls this (it renders defensively without mutating).
    /// </summary>
    internal static class ContainerTypeMigration
    {
        /// <summary>
        /// Would re-typing this container away from Tabs restructure real content — i.e. is there anything
        /// worth asking the user about first? Only <see cref="LeaveTabsMode.Flatten"/> destroys anything, so
        /// an edit point with no way to prompt can ignore this and take the KeepAsGroups default.
        /// </summary>
        internal static bool WouldRestructureTabs(ContainerNode node, ContainerType newKind,
                                                  out int tabCount, out int contentCount)
        {
            tabCount = 0;
            contentCount = 0;
            if (node == null || node.Type != ContainerType.Tabs || newKind == ContainerType.Tabs) return false;

            if (node.Children != null)
                foreach (var child in node.Children)
                    if (child is ContainerNode tab) { tabCount++; contentCount += tab.Children?.Count ?? 0; }

            return tabCount > 0 && contentCount > 0;
        }

        /// <summary>
        /// Migrate the container's children for the type change, then assign the new type. No-op when the
        /// type is unchanged. <paramref name="leaveTabs"/> is consulted only when leaving Tabs.
        /// </summary>
        internal static void Apply(ContainerNode node, ContainerType newKind,
                                   LeaveTabsMode leaveTabs = LeaveTabsMode.KeepAsGroups)
        {
            if (node == null) return;
            var oldKind = node.Type;
            if (oldKind == newKind) return;

            // The three arms are mutually exclusive by construction: oldKind != newKind, so "entering Tabs"
            // implies we were not Tabs, and either Frame-arm implies we are not staying a Frame.
            if (newKind == ContainerType.Tabs)
            {
                EnterTabs(node, oldKind);
            }
            else if (oldKind == ContainerType.Frame && node.Frame.Shape == FrameShape.DiagonalSplit)
            {
                // Leaving a split Frame for a non-Tabs kind: the two panes persist as plain child
                // containers ("keep as groups"), titled so they stay tellable apart.
                FrameShapeMigration.FromSplit(node, keepPanesAsGroups: true);
            }
            else if (oldKind == ContainerType.Tabs)
            {
                LeaveTabs(node, leaveTabs);
            }

            node.Type = newKind;

            // Cells only exist for tracked Columns/Rows/Grid. Re-typing to anything else leaves any
            // EmptyCellNode holding a cell open as a blank child that renders as dead space instead of a
            // "＋" — so the type change is exactly the moment they stop meaning anything. The designer's
            // RefreshAll normalizes too, but the live quick-edit and Paste Style reach this without it.
            EmptyCellFill.Trim(node);
        }

        /// <summary>
        /// The Frame-shape twin of <see cref="Apply"/>: migrates the children across the split / non-split
        /// boundary, then assigns the shape. No-op when the shape is unchanged or the node is not a Frame.
        /// Both are needed when one edit changes the type AND the shape — Paste Style does exactly that, and
        /// they must run in this order, because the type decides whether this is a Frame at all.
        /// <para><paramref name="enterSplit"/> is consulted only when ENTERING a split whose existing children
        /// already look like a pane pair, and <paramref name="keepPanesAsGroups"/> only when leaving one. Both
        /// default to the non-destructive answer, because most callers here (a paste, a multi-edit fan-out)
        /// cannot raise a modal. Note the shape is assigned FIRST, so a caller deciding between the two modes
        /// must ask <see cref="FrameShapeMigration.WouldAdoptExistingPanes"/> BEFORE calling this.</para>
        /// </summary>
        internal static void ApplyFrameShape(ContainerNode node, FrameShape newShape,
                                             bool keepPanesAsGroups = true,
                                             EnterSplitMode enterSplit = EnterSplitMode.AdoptExistingPanes)
        {
            if (node == null) return;
            bool isFrame = node.Type == ContainerType.Frame;
            bool wasSplit = isFrame && node.Frame.Shape == FrameShape.DiagonalSplit;
            bool isSplit = isFrame && newShape == FrameShape.DiagonalSplit;

            node.Frame.Shape = newShape;

            if (!wasSplit && isSplit) FrameShapeMigration.ToSplit(node, enterSplit);
            else if (wasSplit && !isSplit) FrameShapeMigration.FromSplit(node, keepPanesAsGroups);
        }

        // Entering Tabs: the direct children become tab pages, so they all have to be containers.
        // A split Frame ALREADY has exactly two child containers — keep those as the two tabs rather than
        // burying both inside one wrapper. Everything else is wrapped into a single "Tab 1", which preserves
        // the existing layout exactly and leaves the user one drag from splitting it up.
        private static void EnterTabs(ContainerNode node, ContainerType oldKind)
        {
            bool fromSplitFrame = oldKind == ContainerType.Frame
                                  && node.Frame.Shape == FrameShape.DiagonalSplit
                                  && node.Children != null && node.Children.Count > 0
                                  && node.Children.All(c => c is ContainerNode);

            if (fromSplitFrame)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    if (node.Children[i] is ContainerNode pane && string.IsNullOrEmpty(pane.TabLabel))
                        pane.TabLabel = i == 1 ? "Pane B" : "Pane A";
            }
            else
            {
                var existing = node.Children;
                if (existing != null && existing.Count > 0)
                {
                    var tab = new ContainerNode
                    {
                        Type = ContainerType.Rows,
                        TabLabel = "Tab 1",
                        Children = new List<LayoutNode>(existing)
                    };
                    node.Children = new List<LayoutNode> { tab };
                }
            }

            // A stale index left over from an earlier stint as Tabs would open a page that no longer exists.
            node.ActiveTabIndex = 0;
        }

        private static void LeaveTabs(ContainerNode node, LeaveTabsMode mode)
        {
            if (node.Children == null) return;

            if (mode == LeaveTabsMode.Flatten)
            {
                // Promote each tab's direct children up one level and drop the tab wrappers. A nested Tabs
                // container is left whole — flattening it would destroy a second level of structure the
                // user never asked about.
                var flat = new List<LayoutNode>();
                foreach (var child in node.Children)
                {
                    if (child is ContainerNode tab && tab.Type != ContainerType.Tabs && tab.Children != null)
                        flat.AddRange(tab.Children);
                    else
                        flat.Add(child);
                }
                node.Children = flat;
                return;
            }

            // Keep as groups: the tabs stay as child containers, so surface each tab's name as a group title
            // or they arrive as a stack of anonymous boxes.
            foreach (var child in node.Children)
                if (child is ContainerNode tab && string.IsNullOrEmpty(tab.Title) && !string.IsNullOrEmpty(tab.TabLabel))
                    tab.Title = tab.TabLabel;
        }
    }
}
