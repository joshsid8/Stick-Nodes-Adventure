using System.Collections.Generic;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The model operations behind <see cref="EmptyCellNode"/> — placing a widget in an arbitrary cell of a
    /// tracked container, and keeping the placeholders that make that possible from ever becoming cruft.
    ///
    /// Pure model work: the caller owns Undo recording and every refresh. The BUILDER never calls this (it
    /// renders defensively without mutating), matching <see cref="ContainerTypeMigration"/>.
    /// </summary>
    internal static class EmptyCellFill
    {
        /// <summary>
        /// True when this container lays its children out as a fixed set of CELLS — the only shape where an
        /// empty cell is a real, visible position rather than just "no child". These are exactly the
        /// containers the builder renders "＋" placeholder cells for.
        /// </summary>
        internal static bool IsTrackedCellContainer(ContainerNode c)
        {
            if (c == null) return false;
            switch (c.Type)
            {
                // An explicit track count is what turns a Columns/Rows stack into cells; without one the
                // container just splits evenly across whatever children it has.
                case ContainerType.Columns:
                case ContainerType.Rows:
                    return c.GridColumns > 0;
                // A Flexible grid derives its track count from the available width, so it has no stable
                // cell to hold; the fixed constraints do.
                case ContainerType.Grid:
                    return c.GridConstraint != GridConstraint.Flexible && c.GridColumns > 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Places <paramref name="node"/> in cell <paramref name="cellIndex"/> of <paramref name="c"/>.
        ///
        /// A placeholder already sitting in that cell is CONSUMED (replaced, not pushed aside) — that is what
        /// makes it "gone the moment the cell is occupied". A cell past the end pads the positions in between
        /// with fresh placeholders, which is what allows an out-of-order drop in the first place. Untracked
        /// containers have no cells, so they fall through to a plain positional insert.
        ///
        /// <paramref name="cellIndex"/> &lt; 0 means append.
        /// </summary>
        internal static void InsertAtCell(ContainerNode c, LayoutNode node, int cellIndex)
        {
            if (c == null || node == null) return;
            if (c.Children == null) c.Children = new List<LayoutNode>();
            var kids = c.Children;

            if (cellIndex < 0 || !IsTrackedCellContainer(c))
            {
                kids.Insert(Mathf.Clamp(cellIndex < 0 ? kids.Count : cellIndex, 0, kids.Count), node);
                return;
            }

            if (cellIndex < kids.Count)
            {
                // Consume the placeholder holding this cell; a real occupant just shifts right as always.
                if (kids[cellIndex] is EmptyCellNode) kids[cellIndex] = node;
                else kids.Insert(cellIndex, node);
                return;
            }

            while (kids.Count < cellIndex) kids.Add(new EmptyCellNode());
            kids.Add(node);
        }

        /// <summary>
        /// Enforces the placeholder invariant across the whole tree: a placeholder survives only while a REAL
        /// sibling follows it. Returns true when anything was removed, so callers can skip a needless dirty
        /// flag. Idempotent, and cheap enough to run on every refresh — which is what makes the cleanup
        /// automatic rather than something the user has to do.
        /// </summary>
        internal static bool Normalize(LayoutNode root)
        {
            bool changed = false;
            Walk(root);
            return changed;

            void Walk(LayoutNode n)
            {
                if (n is ContainerNode c)
                {
                    if (c.Children != null)
                    {
                        // Descend first: children mutate their OWN lists, so c's is untouched here.
                        foreach (var ch in c.Children) Walk(ch);
                        changed |= Trim(c);
                    }
                }
                else if (n is CollectionNode col)
                {
                    if (col.ItemLayout != null) Walk(col.ItemLayout);
                }
            }
        }

        /// <summary>
        /// One container's share of <see cref="Normalize"/>: drop the trailing run of placeholders (the
        /// container already renders identical "＋" cells for absent children, so they hold nothing), and
        /// drop ALL of them once the container stops being a tracked-cell container — a blank cell in a
        /// Card/Foldout, or in a Columns whose track count was cleared, renders as a mysterious empty
        /// column instead of a "＋".
        /// </summary>
        internal static bool Trim(ContainerNode c)
        {
            var kids = c?.Children;
            if (kids == null || kids.Count == 0) return false;
            bool changed = false;

            if (!IsTrackedCellContainer(c))
            {
                for (int i = kids.Count - 1; i >= 0; i--)
                    if (kids[i] is EmptyCellNode) { kids.RemoveAt(i); changed = true; }
                return changed;
            }

            for (int i = kids.Count - 1; i >= 0 && kids[i] is EmptyCellNode; i--)
            {
                kids.RemoveAt(i);
                changed = true;
            }
            return changed;
        }
    }
}
