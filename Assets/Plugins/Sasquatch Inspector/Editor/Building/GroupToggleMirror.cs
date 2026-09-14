using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A toggle-group Foldout's header checkbox IS the bound bool (see <see cref="ContainerNode.GroupTogglePath"/>) —
    /// there is no separate flag behind it. So a <see cref="BindingNode"/> to that SAME bool sitting inside the group
    /// is a second, redundant editor of one value: two checkboxes that move together, which reads as a bug. It is an
    /// easy state to reach without doing anything wrong — import every field, wrap the lot in a Foldout, then pick one
    /// of those fields as the Toggle Property, and the field you promoted is still sitting there in the body.
    ///
    /// This is the single source of truth for "that binding is a MIRROR of its group's header checkbox". The live
    /// builder skips those nodes, the standalone export emitter never emits them, and the designer canvas keeps them
    /// visible (dimmed, with a corner pip) so they stay selectable, movable and deletable.
    ///
    /// Nothing is stored on the model: the relation is DERIVED from the tree on every build. Clear the Toggle
    /// Property, switch the Foldout to another container type, ungroup it, or drag the field out of the group, and
    /// the field comes straight back with its styling and conditions intact — no un-hide step to remember, and no
    /// "hidden" flag that can outlive the group that caused it.
    ///
    /// Scope is the owning Foldout's own subtree, resolved exactly the way
    /// <c>LayoutTreeBuilder.BuildToggleGroup</c> resolves the checkbox: the toggle path against the container's
    /// OUTER scope, children against its inner one. A field bound to the same bool somewhere ELSE in the layout is
    /// left alone — that is a deliberate second placement, not the accident this fixes.
    /// </summary>
    internal static class GroupToggleMirror
    {
        internal const string PipName = "sasq-group-toggle-mirror-pip";

        private const string PipText = "IN HEADER";

        private const string PipTooltip =
            "Hidden in the real Inspector: this bool is the group's Toggle Property, so the Foldout's header " +
            "checkbox already edits it. Clear the Toggle Property, or move this field out of the group, and it " +
            "comes back.";

        // Dimmed like the collection item-template row (0.6) — this card is scaffolding on the canvas, not
        // something the buyer will ever see rendered.
        private const float DimmedOpacity = 0.55f;

        /// <summary>
        /// Every node under <paramref name="root"/> that mirrors an enclosing toggle-group Foldout's header
        /// checkbox. Returns <c>null</c> — never an empty set — when there are none, so the shipping inspector's
        /// common path allocates nothing and callers can null-check instead of probing a set.
        /// </summary>
        internal static HashSet<LayoutNode> Collect(LayoutNode root)
        {
            if (root == null) return null;
            HashSet<LayoutNode> found = null;
            Walk(root, "", ref found);
            return found;
        }

        /// <summary>
        /// True when <paramref name="foldout"/> is a toggle group whose own subtree ALREADY carries an explicit
        /// binding to its checkbox bool. The structural tools (ungroup / dissolve) ask this before surfacing the
        /// implicit bool as a new <see cref="BindingNode"/>: without the check, dropping the container would
        /// un-hide the mirror AND add a synthetic twin next to it — the very duplicate this file removes.
        /// </summary>
        internal static bool HasExplicitMirror(ContainerNode foldout) =>
            HasExplicitMirror(foldout, foldout?.GroupTogglePath);

        /// <summary>
        /// <see cref="HasExplicitMirror(ContainerNode)"/> against a togglePath the node does not carry yet — what
        /// the designer's Toggle Property field asks per keystroke, where the typed text leads the model by a
        /// debounce and answering from the node would lag the note it drives by one edit.
        /// </summary>
        internal static bool HasExplicitMirror(ContainerNode foldout, string togglePath)
        {
            if (foldout == null || foldout.Type != ContainerType.Foldout) return false;
            if (string.IsNullOrEmpty(togglePath) || foldout.Children == null) return false;

            HashSet<LayoutNode> found = null;
            // Resolved against an empty outer scope: the toggle path and the container's own PropertyPath are
            // both relative to whatever encloses it, so the comparison holds at any depth.
            foreach (var child in foldout.Children)
                MarkMirrors(child, foldout.PropertyPath ?? "", togglePath, ref found);
            return found != null;
        }

        /// <summary>
        /// The design-canvas treatment: dim the card and pin a corner "IN HEADER" pip on it. The node stays fully
        /// present and selectable — the author has to be able to find it, move it or delete it — but it reads as
        /// something that will not be there in the real Inspector. Idempotent. (Live Preview and the real
        /// inspector both drop the node entirely; this surface is the only one that shows it.)
        /// </summary>
        internal static void ApplyDesignPip(VisualElement host)
        {
            if (host == null) return;
            host.style.opacity = DimmedOpacity;
            host.tooltip = PipTooltip;

            if (host.Q<Label>(PipName) != null) return;

            var pip = new Label(PipText) { name = PipName, pickingMode = PickingMode.Ignore };
            pip.style.position = Position.Absolute;
            pip.style.top = 2;
            pip.style.right = 2;
            pip.style.paddingLeft = 5;
            pip.style.paddingRight = 5;
            pip.style.paddingTop = 1;
            pip.style.paddingBottom = 1;
            pip.style.fontSize = 9;
            pip.style.unityFontStyleAndWeight = FontStyle.Bold;
            pip.style.color = new Color(1f, 1f, 1f, 0.92f);
            pip.style.borderTopLeftRadius = 3;
            pip.style.borderTopRightRadius = 3;
            pip.style.borderBottomLeftRadius = 3;
            pip.style.borderBottomRightRadius = 3;
            // Slate, deliberately NOT the condition pips' red/amber: nothing here is wrong or conditional.
            pip.style.backgroundColor = new Color(0.33f, 0.38f, 0.46f, 0.92f);
            host.Add(pip);
            pip.BringToFront();
        }

        // ── Tree walk ────────────────────────────────────────────────────────────────────────────────────────
        // Scoping mirrors UnboundFieldSync.GatherBoundPaths exactly (which is itself pinned to BuildToggleGroup),
        // so "the path this group's checkbox binds" means the same string in all three places.

        private static void Walk(LayoutNode node, string basePath, ref HashSet<LayoutNode> found)
        {
            if (node == null) return;

            if (node is CollectionNode col)
            {
                // An ItemLayout is a per-ELEMENT scope; carry it so a toggle group INSIDE the template is still
                // found, resolved against the collection path as GatherBoundPaths does.
                if (col.ItemLayout != null)
                    Walk(col.ItemLayout, Resolve(basePath, col.PropertyPath), ref found);
                return;
            }

            if (!(node is ContainerNode container)) return;

            string childBasePath = Resolve(basePath, container.PropertyPath);

            if (container.Type == ContainerType.Foldout && !string.IsNullOrEmpty(container.GroupTogglePath)
                && container.Children != null)
            {
                string togglePath = Resolve(basePath, container.GroupTogglePath);
                foreach (var child in container.Children)
                    MarkMirrors(child, childBasePath, togglePath, ref found);
            }

            if (container.Children == null) return;
            foreach (var child in container.Children)
                Walk(child, childBasePath, ref found);
        }

        private static void MarkMirrors(LayoutNode node, string basePath, string togglePath, ref HashSet<LayoutNode> found)
        {
            if (node == null) return;

            if (node is BindingNode binding)
            {
                if (string.IsNullOrEmpty(binding.PropertyPath)) return;
                if (!string.Equals(Resolve(basePath, binding.PropertyPath), togglePath, StringComparison.Ordinal)) return;
                (found ??= new HashSet<LayoutNode>()).Add(node);
                return;
            }

            // A collection binds an array and its template is a per-element scope, so nothing under it can ever
            // resolve to the group's own bool — the scan stops here rather than walking a subtree that cannot match.
            if (!(node is ContainerNode container) || container.Children == null) return;

            string childBasePath = Resolve(basePath, container.PropertyPath);
            foreach (var child in container.Children)
                MarkMirrors(child, childBasePath, togglePath, ref found);
        }

        private static string Resolve(string basePath, string path)
        {
            if (string.IsNullOrEmpty(path)) return basePath ?? "";
            if (string.IsNullOrEmpty(basePath)) return path;
            return basePath + "." + path;
        }
    }
}
