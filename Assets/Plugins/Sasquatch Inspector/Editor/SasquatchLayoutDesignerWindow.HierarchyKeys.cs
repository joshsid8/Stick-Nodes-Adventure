using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Arrow-key navigation for the Hierarchy panel: ↑/↓ walk the rows, ←/→ close and open a branch and step
    /// out of / into it — the gesture set every tree view in the editor already teaches.
    ///
    /// The walk reads <c>_hierRows</c>, the flat snapshot <see cref="RenderTree"/> stamps as it draws. That is
    /// deliberate: which rows exist is decided by collapse state, the hierarchy search's pruning, its transient
    /// held-open trails and the <c>EmptyCellNode</c> skip, and a second walk of the MODEL would be a second
    /// definition of "visible" — one that drifts silently the first time any of those rules changes.
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        /// <summary>
        /// The arrow-key action, shared by BOTH key routes (see the dual-route note on Delete/F2): OnGUI
        /// fires when nothing has focus — which is exactly the state a hierarchy or canvas row click leaves
        /// behind — and the UITK root handler fires when focus sits on an element. Returns true when the key
        /// was claimed, INCLUDING at the ends of the list: letting an unclaimed arrow through there scrolls
        /// the panel out from under a selection that didn't move, which reads as a glitch.
        /// </summary>
        internal bool TryHandleHierarchyArrowKey(KeyCode key, bool altHeld)
        {
            // Alt+← is the drill-in Back gesture and owns that chord outright.
            if (altHeld) return false;
            if (key != KeyCode.UpArrow && key != KeyCode.DownArrow &&
                key != KeyCode.LeftArrow && key != KeyCode.RightArrow) return false;
            if (_clonedLayoutAsset?.Root == null) return false;
            if (!HierarchyOwnsArrowKeys()) return false;
            if (_hierRows.Count == 0) return false;

            int current = IndexOfHierarchyRow(_selectedNode);

            // The selection is real but its row isn't drawn — it sits inside a branch the user collapsed.
            // Open the trail down to it first, so the walk starts from where the selection actually IS
            // rather than teleporting to the top of the tree.
            if (current < 0 && _selectedNode != null)
            {
                ExpandAncestorsOf(_selectedNode);
                RefreshHierarchy();
                current = IndexOfHierarchyRow(_selectedNode);
            }

            switch (key)
            {
                case KeyCode.DownArrow:
                    return SelectHierarchyRowAt(current < 0 ? 0 : current + 1);

                case KeyCode.UpArrow:
                    return SelectHierarchyRowAt(current < 0 ? _hierRows.Count - 1 : current - 1);

                case KeyCode.RightArrow:
                {
                    if (current < 0) return SelectHierarchyRowAt(0);
                    var node = _hierRows[current].Node;
                    // A shut branch opens; an already-open one steps INTO its first child. Two meanings on
                    // one key is what makes ←/→ a complete walk instead of two half-gestures.
                    if (_hierRows[current].Collapsed && IsExpandableHierarchyNode(node))
                        return SetHierarchyBranchCollapsed(node, collapsed: false);
                    // A deeper next row IS this node's first child (rows are drawn depth-first).
                    if (current + 1 < _hierRows.Count && _hierRows[current + 1].Depth > _hierRows[current].Depth)
                        return SelectHierarchyRowAt(current + 1);
                    return true; // a leaf has nowhere to go in
                }

                case KeyCode.LeftArrow:
                {
                    if (current < 0) return SelectHierarchyRowAt(0);
                    var node = _hierRows[current].Node;
                    if (!_hierRows[current].Collapsed && IsExpandableHierarchyNode(node))
                        return SetHierarchyBranchCollapsed(node, collapsed: true);
                    // Already shut, or a leaf → step OUT to the parent, i.e. the nearest earlier row drawn
                    // one level shallower. Reading it off the row list rather than the model is what keeps
                    // it right for an ItemLayout template, whose HierRow.Parent is deliberately null.
                    for (int i = current - 1; i >= 0; i--)
                        if (_hierRows[i].Depth < _hierRows[current].Depth) return SelectHierarchyRowAt(i);
                    return true; // the root has no parent
                }
            }
            return false;
        }

        /// <summary>
        /// True when the hierarchy may claim an arrow key. Arrows are a first-class editing key inside text
        /// fields, numeric drags, sliders, dropdowns and list views, so the panel only takes them when
        /// NOTHING has focus — clicking a hierarchy or canvas row clears focus, which is why that is the
        /// common case — or when focus is already inside the hierarchy panel itself.
        /// </summary>
        private bool HierarchyOwnsArrowKeys()
        {
            if (IsTypingInTextField()) return false;
            var focused = rootVisualElement?.focusController?.focusedElement as VisualElement;
            if (focused == null) return true;
            if (_hierarchyContainer == null) return false;
            for (var p = focused; p != null; p = p.hierarchy.parent)
                if (p == _hierarchyContainer) return true;
            return false;
        }

        /// <summary>Index of the row drawn for <paramref name="node"/>, or -1 when it isn't drawn.</summary>
        private int IndexOfHierarchyRow(LayoutNode node)
        {
            if (node == null) return -1;
            for (int i = 0; i < _hierRows.Count; i++)
                if (ReferenceEquals(_hierRows[i].Node, node)) return i;
            return -1;
        }

        /// <summary>Moves the primary selection to a drawn row. Out-of-range (the ends of the list) still
        /// CLAIMS the key — see the note on the caller.</summary>
        private bool SelectHierarchyRowAt(int index)
        {
            if (index < 0 || index >= _hierRows.Count) return true;
            var node = _hierRows[index].Node;
            if (node == null) return true;
            SelectSingle(node);
            ScrollHierarchyRowIntoView(node);
            return true;
        }

        /// <summary>Opens or shuts a branch, keeping the node selected (the row you acted on must not move
        /// out from under you).</summary>
        private bool SetHierarchyBranchCollapsed(LayoutNode node, bool collapsed)
        {
            if (collapsed) _collapsedNodes.Add(node);
            else _collapsedNodes.Remove(node);
            RefreshHierarchy();
            ScrollHierarchyRowIntoView(node);
            return true;
        }

        /// <summary>EXACTLY <see cref="RenderTree"/>'s recursion test — a node owns a branch when the
        /// renderer would descend into it.</summary>
        private static bool IsExpandableHierarchyNode(LayoutNode node)
        {
            if (node is ContainerNode container) return container.Children != null && container.Children.Count > 0;
            if (node is CollectionNode collection) return collection.ItemLayout != null;
            // A dictionary binding owns a branch too, once its Key / Value cell templates exist.
            return HasDictionaryCellTemplate(node);
        }

        /// <summary>
        /// Brings the row into view. Deliberately NOT <c>ScrollToAndPing</c>: the glow is a deep-link
        /// affordance, and replaying it on every press while the user holds an arrow down is noise. Deferred
        /// a tick because the row is rebuilt by the refresh above and has no layout yet in this frame.
        /// </summary>
        private void ScrollHierarchyRowIntoView(LayoutNode node)
        {
            if (_hierarchyContainer == null || node == null) return;
            _hierarchyContainer.schedule.Execute(() =>
            {
                int i = IndexOfHierarchyRow(node);
                if (i < 0) return;
                var row = _hierRows[i].Row;
                if (row == null || row.panel == null) return;
                if (float.IsNaN(row.layout.height) || row.layout.height <= 0f) return;
                _hierarchyContainer.ScrollTo(row);
            }).ExecuteLater(0);
        }
    }
}
