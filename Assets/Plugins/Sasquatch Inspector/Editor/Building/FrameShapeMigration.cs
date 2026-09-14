using System.Collections.Generic;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// What happens to a Frame's existing direct children when the frame ENTERS DiagonalSplit and those
    /// children already have the shape of a pane pair (exactly two <see cref="ContainerNode"/>s). Consulted
    /// only by <see cref="FrameShapeMigration.ToSplit"/>, and only in that one ambiguous case.
    /// </summary>
    internal enum EnterSplitMode
    {
        /// <summary>Take the two existing containers AS the two panes. Non-destructive and idempotent, and
        /// therefore the correct default for any edit point that cannot reasonably raise a confirmation
        /// dialog: it is what makes the "keep panes as groups" round trip work — leaving a split that way
        /// produces exactly two ContainerNodes, and re-entering must restore those same panes rather than
        /// bury both inside a new Pane A. A split→split re-entry likewise never double-wraps.</summary>
        AdoptExistingPanes,

        /// <summary>Wrap ALL existing children into a new Pane A and add an empty Pane B, exactly as for any
        /// other child shape. Use when the two containers are the user's OWN authored groups (two Cards, say)
        /// rather than panes coming home: adopting those silently turns two movable, deletable groups into
        /// structural panes, which can no longer be moved, are only emptied by Delete, and are destroyed by
        /// "Flatten items in". Only ever pass this in response to an explicit user choice.</summary>
        WrapIntoPaneA,
    }

    /// <summary>
    /// Structural child migration for a Frame container crossing the split / non-split boundary
    /// (the Tabs-conversion precedent). A DiagonalSplit frame's direct children are exactly two pane
    /// ContainerNodes (index 0 = Pane A, 1 = Pane B — identity is positional); a non-split frame holds
    /// its children directly. These are pure model operations: the caller owns Undo recording and any
    /// confirmation dialog, and the BUILDER never calls them (it renders defensively without mutating).
    /// </summary>
    internal static class FrameShapeMigration
    {
        /// <summary>
        /// Entering DiagonalSplit: wrap all existing children into a new Pane A (Rows) and add an empty
        /// Pane B. An empty frame gets two empty panes.
        /// <para>Children that ALREADY look like panes (exactly two ContainerNodes) are ambiguous — they are
        /// either panes coming home from a "keep panes as groups" exit, or two groups the user authored by
        /// hand — so what happens to them is the caller's decision via <paramref name="mode"/>. The default,
        /// <see cref="EnterSplitMode.AdoptExistingPanes"/>, keeps the round trip and the idempotent
        /// re-entry; see <see cref="WouldAdoptExistingPanes"/> for asking the user first.</para>
        /// </summary>
        internal static void ToSplit(ContainerNode frameNode,
                                     EnterSplitMode mode = EnterSplitMode.AdoptExistingPanes)
        {
            if (frameNode == null) return;
            var existing = frameNode.Children ?? new List<LayoutNode>();

            if (mode == EnterSplitMode.AdoptExistingPanes && LooksLikeAPanePair(existing))
                return;

            var paneA = new ContainerNode { Type = ContainerType.Rows };
            if (existing.Count > 0) paneA.Children = new List<LayoutNode>(existing);
            var paneB = new ContainerNode { Type = ContainerType.Rows };

            frameNode.Children = new List<LayoutNode> { paneA, paneB };
        }

        /// <summary>
        /// Leaving DiagonalSplit. Flatten (default): promote each pane's children up one level in
        /// A-then-B order and drop the pane wrappers. Keep-as-groups: leave the panes as child
        /// containers, titled "Pane A"/"Pane B" when untitled so they stay tellable apart.
        /// </summary>
        internal static void FromSplit(ContainerNode frameNode, bool keepPanesAsGroups)
        {
            if (frameNode == null || frameNode.Children == null) return;

            if (keepPanesAsGroups)
            {
                for (int i = 0; i < frameNode.Children.Count; i++)
                {
                    if (frameNode.Children[i] is ContainerNode pane && string.IsNullOrEmpty(pane.Title))
                        pane.Title = i == 1 ? "Pane B" : "Pane A";
                }
                return;
            }

            var flat = new List<LayoutNode>();
            foreach (var child in frameNode.Children)
            {
                if (child is ContainerNode pane && pane.Children != null)
                    flat.AddRange(pane.Children);
                else if (child != null)
                    flat.Add(child);
            }
            frameNode.Children = flat;
        }

        /// <summary>
        /// Would entering DiagonalSplit silently ADOPT the frame's existing children as its two panes — i.e.
        /// is there anything worth asking the user about first? True only in the one ambiguous case: exactly
        /// two ContainerNode children, which are either panes returning from a "keep panes as groups" exit or
        /// two groups the user authored. <paramref name="itemCount"/> is how much content sits inside them,
        /// for the message. An edit point with no way to prompt can ignore this and take the
        /// <see cref="EnterSplitMode.AdoptExistingPanes"/> default, which never destroys anything.
        /// <para>The twin of <see cref="ContainerTypeMigration.WouldRestructureTabs"/>, and asked BEFORE the
        /// shape is assigned — <see cref="ContainerTypeMigration.ApplyFrameShape"/> writes the new shape
        /// first, so a caller that reads <c>Frame.Shape</c> to decide is already too late.</para>
        /// </summary>
        internal static bool WouldAdoptExistingPanes(ContainerNode frameNode, out int itemCount)
        {
            itemCount = 0;
            if (frameNode == null || frameNode.Type != ContainerType.Frame) return false;
            if (frameNode.Frame != null && frameNode.Frame.Shape == FrameShape.DiagonalSplit) return false;
            if (!LooksLikeAPanePair(frameNode.Children)) return false;

            itemCount = CountPaneContent(frameNode);
            return true;
        }

        /// <summary>Exactly two ContainerNodes — the shape a split frame's children have, and the only shape
        /// <see cref="ToSplit"/> cannot tell apart from two groups the user made.</summary>
        private static bool LooksLikeAPanePair(List<LayoutNode> children)
            => children != null && children.Count == 2
               && children[0] is ContainerNode && children[1] is ContainerNode;

        /// <summary>How many items sit inside the panes (drives the leave-split confirmation dialog).</summary>
        internal static int CountPaneContent(ContainerNode frameNode)
        {
            int count = 0;
            if (frameNode?.Children == null) return 0;
            foreach (var child in frameNode.Children)
                if (child is ContainerNode pane)
                    count += pane.Children?.Count ?? 0;
            return count;
        }
    }
}
