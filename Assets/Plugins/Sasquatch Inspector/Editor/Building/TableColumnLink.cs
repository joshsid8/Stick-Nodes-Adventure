using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Ties a Table column's two halves together on the designer canvas: the HEADER cell that names the
    /// column, and the DATA cell of the representative row that holds its binding card.
    ///
    /// They are built in different passes of <see cref="LayoutTreeBuilder"/> and live in different rows, so
    /// nothing in the tree structure says they are the same column — yet every good interaction here needs
    /// exactly that: selecting a binding should light its header, dragging a binding must slide its header
    /// with it, and hovering either should say which value belongs to which name. Rebuilding that pairing by
    /// index at each call site is how the two halves drift apart, so it is recorded once, at the one place
    /// that has both in hand.
    ///
    /// A <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed on the elements: entries die with the built
    /// tree exactly like <see cref="LayoutNodeRegistry"/>'s, so a rebuilt canvas can never resolve a stale
    /// header.
    /// </summary>
    internal static class TableColumnLink
    {
        private static readonly ConditionalWeakTable<VisualElement, VisualElement> _headerOfCell = new();

        /// <summary>USS marker: this header's column is selected on the canvas.</summary>
        internal const string HeaderSelectedClass = "sasq-table-header-cell--linked";
        /// <summary>USS marker: this header's column is under the pointer.</summary>
        internal const string HeaderHotClass = "sasq-table-header-cell--hot";

        /// <summary>Records that <paramref name="header"/> is the column header above <paramref name="cell"/>.</summary>
        internal static void Set(VisualElement cell, VisualElement header)
        {
            if (cell == null || header == null) return;
            _headerOfCell.Remove(cell);
            _headerOfCell.Add(cell, header);
        }

        /// <summary>The column header above <paramref name="cell"/>, or null.</summary>
        internal static VisualElement Header(VisualElement cell)
        {
            if (cell == null) return null;
            return _headerOfCell.TryGetValue(cell, out var header) ? header : null;
        }

        /// <summary>
        /// The column header for a design CARD (the card sits inside the data cell), or null when the card is
        /// not a table column. The one lookup every consumer outside the builder wants.
        /// </summary>
        internal static VisualElement HeaderForCard(VisualElement card) => Header(card?.parent);
    }
}
