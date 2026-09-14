using System;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A positional placeholder that reserves one empty cell in a TRACKED container (a Columns/Rows
    /// container with an explicit track count, or a fixed-constraint Grid).
    ///
    /// Children are a flat ordered list, so position IS index: without a placeholder there is no way to
    /// put a widget in the third cell of a three-track container while the first two stay empty. Dropping
    /// onto the third "＋" cell therefore materialises two of these ahead of it.
    ///
    /// It is deliberately INVISIBLE PLUMBING, not a widget the user manages:
    /// <list type="bullet">
    /// <item>It renders exactly like the visual-only "＋" placeholder cells, so a held cell is
    /// indistinguishable from an unheld one.</item>
    /// <item>A drop onto its cell CONSUMES it (replace, not insert) — it is gone the instant the cell is
    /// genuinely occupied.</item>
    /// <item>It never appears in the hierarchy, is never selectable, draggable, duplicable, styleable or
    /// quick-editable, and carries no settings of its own.</item>
    /// <item><see cref="SasquatchInspector.Editor.EmptyCellFill"/> trims it automatically the moment it
    /// stops holding a position — see the invariant below.</item>
    /// </list>
    ///
    /// THE INVARIANT: a placeholder only ever exists when a real (non-placeholder) sibling follows it.
    /// A trailing run of them is meaningless — the container renders identical "＋" cells for absent
    /// children anyway — so it is trimmed on every refresh, which is what keeps the model free of cruft
    /// with no manual cleanup. Likewise every placeholder is purged when its container stops being a
    /// tracked-cell container (track count cleared, or re-typed to Card/Foldout/…), where a blank cell
    /// would render as a mysterious empty column instead of a "＋".
    ///
    /// Live inspectors and exported editors DO render it (as an empty, non-interactive cell) — dropping
    /// the cell there would slide the real widgets left and break the layout the user designed.
    /// </summary>
    [Serializable]
    public class EmptyCellNode : LayoutNode
    {
    }
}
