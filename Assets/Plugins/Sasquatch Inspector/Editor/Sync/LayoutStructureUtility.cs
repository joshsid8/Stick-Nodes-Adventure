using System.Collections.Generic;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Structural node-tree surgery shared by the designer's layout tools and the sync engine:
    /// recursive flatten (the designer's Flatten/Ungroup tool) and single-container dissolve
    /// (the orphaned-header cleanup's "roll the fields up into the parent"). PURE tree mutation —
    /// no undo, persistence, or UI here; callers own that.
    /// </summary>
    internal static class LayoutStructureUtility
    {
        /// <summary>
        /// Replaces <paramref name="target"/> with its own content, spliced into its parent at the
        /// container's index: order preserved, nested structure intact, scoped paths re-based so
        /// they keep resolving, and a toggle-group Foldout's checkbox bool surfaced as an explicit
        /// binding (it is bound implicitly via GroupTogglePath, so dropping the container would
        /// silently lose the field). Returns false when <paramref name="target"/> is the root or
        /// isn't in the tree (collection item templates are not searched — header sections never
        /// live there).
        /// </summary>
        public static bool DissolveContainer(ContainerNode root, ContainerNode target)
        {
            if (root == null || target == null || target == root) return false;
            var parent = FindParent(root, target);
            if (parent == null) return false;
            int idx = parent.Children.IndexOf(target);

            var content = new List<LayoutNode>();

            // GroupTogglePath resolves against the container's OUTER scope (see GatherBoundPaths'
            // derived-path rule) — which is exactly the parent scope we're splicing into.
            // ...unless the group already CONTAINS a binding to that bool. That one is invisible while the
            // group owns it (GroupToggleMirror) and un-hides the instant the container goes, so synthesising a
            // second node here would turn one dissolve into the duplicate pair the mirror rule exists to remove.
            if (target.Type == ContainerType.Foldout && !string.IsNullOrEmpty(target.GroupTogglePath)
                && !GroupToggleMirror.HasExplicitMirror(target))
                content.Add(new BindingNode { PropertyPath = target.GroupTogglePath });

            if (target.Children != null)
            {
                foreach (var child in target.Children)
                {
                    // Children were scoped by the container's own PropertyPath (struct grouping);
                    // moving them up one level means each child absorbs that prefix itself.
                    RebaseNodePath(child, target.PropertyPath);
                    content.Add(child);
                }
            }

            parent.Children.RemoveAt(idx);
            parent.Children.InsertRange(idx, content);
            return true;
        }

        private static ContainerNode FindParent(ContainerNode container, ContainerNode target)
        {
            if (container.Children == null) return null;
            foreach (var child in container.Children)
            {
                if (child == target) return container;
                if (child is ContainerNode c)
                {
                    var found = FindParent(c, target);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Depth-first collect of leaf content from a container, dropping the containers themselves and
        /// re-absolutizing binding paths against the accumulated base path so nothing breaks.
        /// Core of the designer's Flatten/Ungroup tool.
        /// </summary>
        public static void FlattenCollect(ContainerNode container, string basePath, List<LayoutNode> flat, ref int containersRemoved)
        {
            string nextBase = basePath;
            if (!string.IsNullOrEmpty(container.PropertyPath))
                nextBase = string.IsNullOrEmpty(basePath) ? container.PropertyPath : $"{basePath}.{container.PropertyPath}";

            // A toggle-group Foldout's checkbox bool is bound implicitly via GroupTogglePath — it lives on the
            // container, not as a child node. Ungrouping drops the container, so surface that bool as an
            // explicit binding at the head of the flattened content, or the field silently vanishes from the
            // inspector. Resolved against basePath (NOT nextBase), matching GatherBoundPaths' derived-path rule.
            // Skipped when the group already carries an explicit binding to that bool: it is hidden only while
            // the group owns it (GroupToggleMirror) and comes back on its own here, so adding a twin would
            // ungroup one field into two — the dissolve path guards this identically.
            if (container.Type == ContainerType.Foldout && !string.IsNullOrEmpty(container.GroupTogglePath)
                && !GroupToggleMirror.HasExplicitMirror(container))
            {
                string togglePath = string.IsNullOrEmpty(basePath) ? container.GroupTogglePath : $"{basePath}.{container.GroupTogglePath}";
                flat.Add(new BindingNode { PropertyPath = togglePath });
            }

            if (container.Children == null) return;
            foreach (var child in container.Children)
            {
                if (child is ContainerNode cc)
                {
                    containersRemoved++;
                    FlattenCollect(cc, nextBase, flat, ref containersRemoved);
                }
                else
                {
                    RebaseNodePath(child, nextBase);
                    flat.Add(child); // bindings, collections (template intact), buttons, help boxes, script header
                }
            }
        }

        /// <summary>Prefixes a node's scope-relative path with <paramref name="basePath"/> so it keeps
        /// resolving after moving up out of a scoped container. Containers only occur on the dissolve
        /// path (flatten recurses into them instead): a scoped one absorbs the prefix itself; an
        /// unscoped one passes its outer scope straight through, so the prefix pushes down onto its
        /// children — and a Foldout's toggle bool resolves against that same outer scope.</summary>
        public static void RebaseNodePath(LayoutNode node, string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) return;
            if (node is BindingNode bn && !string.IsNullOrEmpty(bn.PropertyPath))
                bn.PropertyPath = $"{basePath}.{bn.PropertyPath}";
            else if (node is CollectionNode col && !string.IsNullOrEmpty(col.PropertyPath))
                col.PropertyPath = $"{basePath}.{col.PropertyPath}";
            else if (node is ContainerNode c)
            {
                if (!string.IsNullOrEmpty(c.PropertyPath))
                {
                    c.PropertyPath = $"{basePath}.{c.PropertyPath}";
                }
                else if (c.Children != null)
                {
                    foreach (var child in c.Children) RebaseNodePath(child, basePath);
                }
                if (c.Type == ContainerType.Foldout && !string.IsNullOrEmpty(c.GroupTogglePath))
                    c.GroupTogglePath = $"{basePath}.{c.GroupTogglePath}";
            }
            // Buttons (method names), help boxes, and script headers carry no rebindable path.
        }
    }
}
