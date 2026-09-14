using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The design-canvas treatment for a node the author has hidden
    /// (<see cref="Runtime.LayoutNode.HiddenInInspector"/>): dim the card and pin a corner "HIDDEN" pip on it.
    ///
    /// The live half of the rule is the early return at the top of <c>LayoutTreeBuilder.BuildNode</c>, which skips
    /// the node entirely so its siblings close up. This is the other half — the design surfaces deliberately keep
    /// BUILDING a hidden node, because the canvas is where the author selects it, styles it and un-hides it. A
    /// node that vanished from the canvas too could only be reached from the hierarchy row, and a hidden container
    /// would swallow every child with it.
    ///
    /// Deliberately a separate file from <see cref="GroupToggleMirror"/> even though the two treatments rhyme: a
    /// mirror is DERIVED from the tree and evaporates on its own, while this is an explicit, persisted choice the
    /// author made. Same shape, different cause — and the pips must not read as the same thing.
    /// </summary>
    internal static class HiddenNodeMark
    {
        internal const string PipName = "sasq-hidden-node-pip";

        private const string PipText = "HIDDEN";

        private const string PipTooltip =
            "Hidden in the real Inspector: this element (and anything inside it) is not drawn at all, and " +
            "everything around it closes up as if it were deleted. Click the eye on its Hierarchy row — or " +
            "turn \"Visible in Inspector\" back on — to bring it back.";

        // The same dim as the toggle-group mirror pip: both cards are scaffolding on the canvas, not something
        // the buyer will ever see rendered.
        private const float DimmedOpacity = 0.55f;

        /// <summary>
        /// Dim <paramref name="host"/> and pin the corner pip on it. Idempotent — the design canvas rebuilds
        /// constantly, and a second call must not stack a second label.
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
            // Left, not right: the toggle-group mirror's pip owns the top-RIGHT corner, and a node can be both a
            // mirror and hidden. Opposite corners keep the two readable instead of one covering the other.
            pip.style.left = 2;
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
            // Violet — distinct from the mirror pip's slate (that one is "the header already edits this") and from
            // the conditions overlay's red/amber (those mean a rule is failing). Nothing here is wrong; it is off.
            pip.style.backgroundColor = new Color(0.45f, 0.33f, 0.58f, 0.94f);
            host.Add(pip);
            pip.BringToFront();
        }

        /// <summary>
        /// Every attribute-owned <see cref="SpaceNode"/> under <paramref name="root"/> whose field has been
        /// hidden — the gap a code <c>[Space]</c> imported for a field that is no longer drawn.
        ///
        /// <para>Without this the promise breaks in the one place it is most visible: hide a field and the
        /// blank strip its <c>[Space]</c> reserved stays behind, so the layout does NOT close up the way a
        /// delete would. The spacer cannot be dealt with by the plain hidden check because it is a separate
        /// node that is not itself hidden.</para>
        ///
        /// <para>Paired LOCALLY — the owner is looked for beside the spacer, never by a global path search -
        /// but the KEY is the RESOLVED path, because that is the string the sync engine actually wrote into
        /// <see cref="SpaceNode.OwnerFieldPath"/> (<c>UnboundFieldSync.MakeSpaceNode</c> is handed a
        /// root-resolved path). A binding inside a property-scoped container carries only the scope-relative
        /// remainder, so comparing the two raw would never match there and the gap would survive its field.
        /// The walk therefore composes <c>ContainerNode.PropertyPath</c> / <c>CollectionNode.PropertyPath</c>
        /// exactly as <see cref="GroupToggleMirror"/> and the builder do.</para>
        ///
        /// <para>TWO owner shapes, matching <c>UnboundFieldSync.FindSpaceWidgetAbove</c> — the method that
        /// decides where a spacer BELONGS, so this one has to read the same shapes or the two disagree:</para>
        /// <list type="number">
        /// <item>a plain sibling in the same list, and</item>
        /// <item>the LEADING descendant of the sibling container directly below the spacer. Smart Attribute
        /// Import parks a <c>[Header]</c>+<c>[Space]</c> field's gap at root, just before the section
        /// container the header opened — so for that (very common) shape the owner is one level DOWN, and
        /// pairing only within the list would leave the gap behind.</item>
        /// </list>
        ///
        /// <para>A spacer is orphaned only when EVERY node bound to its owner path is hidden. Duplicating a
        /// binding (Ctrl+D) leaves two siblings on one path, and hiding one copy must not strip the gap the
        /// visible copy still needs — so the scan tallies hidden-vs-visible over the whole list instead of
        /// stopping at the first hidden match. A spacer whose owner is somewhere else entirely finds nothing
        /// and stays: never a wrong removal.</para>
        ///
        /// <para>Returns <c>null</c> — never an empty set — when there are none, so the shipping inspector's
        /// common path allocates nothing, matching <c>GroupToggleMirror.Collect</c>.</para>
        /// </summary>
        internal static HashSet<LayoutNode> CollectOrphanedSpacers(LayoutNode root)
        {
            if (root == null) return null;
            HashSet<LayoutNode> found = null;
            Walk(root, "", ref found);
            return found;
        }

        private static void Walk(LayoutNode node, string basePath, ref HashSet<LayoutNode> found)
        {
            if (node == null) return;

            if (node is CollectionNode col)
            {
                // An ItemLayout is a per-ELEMENT scope, carried the same way GroupToggleMirror.Walk carries it.
                if (col.ItemLayout != null)
                    Walk(col.ItemLayout, Resolve(basePath, col.PropertyPath), ref found);
                return;
            }

            if (!(node is ContainerNode c) || c.Children == null) return;

            string childBasePath = Resolve(basePath, c.PropertyPath);
            CollectInSiblingList(c.Children, childBasePath, ref found);
            foreach (var child in c.Children) Walk(child, childBasePath, ref found);
        }

        private static void CollectInSiblingList(List<LayoutNode> siblings, string basePath, ref HashSet<LayoutNode> found)
        {
            for (int i = 0; i < siblings.Count; i++)
            {
                if (!(siblings[i] is SpaceNode sp) || !sp.IsAttributeOwned) continue;

                // Tallied, not short-circuited: see the summary's duplicate-binding note. anyVisible VETOES.
                bool anyHidden = false, anyVisible = false;

                for (int j = 0; j < siblings.Count; j++)
                {
                    if (j == i) continue;
                    TallyOwner(siblings[j], basePath, sp.OwnerFieldPath, ref anyHidden, ref anyVisible);
                }

                // Shape 2: the [Header]+[Space] field whose gap Smart Attribute Import parked one level up.
                if (i + 1 < siblings.Count && siblings[i + 1] is ContainerNode below)
                    TallyLeadingOwner(below, basePath, sp.OwnerFieldPath, ref anyHidden, ref anyVisible);

                if (anyHidden && !anyVisible)
                    (found ??= new HashSet<LayoutNode>()).Add(sp);
            }
        }

        /// <summary>Counts <paramref name="sibling"/> toward the hidden/visible tally when it binds
        /// <paramref name="ownerPath"/>. Anything that is not a field-bound node cannot own a gap.</summary>
        private static void TallyOwner(LayoutNode sibling, string basePath, string ownerPath,
                                       ref bool anyHidden, ref bool anyVisible)
        {
            string path = sibling is BindingNode b ? b.PropertyPath
                        : sibling is CollectionNode cn ? cn.PropertyPath
                        : null;
            if (string.IsNullOrEmpty(path)) return;
            if (!string.Equals(Resolve(basePath, path), ownerPath, StringComparison.Ordinal)) return;

            if (sibling.HiddenInInspector) anyHidden = true;
            else anyVisible = true;
        }

        /// <summary>
        /// The LEADING-descendant arm of the pairing: descend <paramref name="container"/>'s first-child chain
        /// to the field it opens with, and tally that against <paramref name="ownerPath"/>. A container hidden
        /// anywhere on the way down takes the leaf with it, so it counts as hidden — the gap above a section
        /// whose whole body is gone must go too.
        /// <para>Strictly the LEADING chain, never a general subtree search: a spacer belongs to the field
        /// directly below it, and matching any descendant would strip gaps that belong to something else.
        /// Depth-bounded because the layout graph is user data — the caller's cycle guard has already run in
        /// both live paths, but a walker that cannot terminate alone is a hang waiting for the one layout that
        /// reaches it by another door.</para>
        /// </summary>
        private static void TallyLeadingOwner(ContainerNode container, string basePath, string ownerPath,
                                              ref bool anyHidden, ref bool anyVisible)
        {
            string scope = basePath;
            LayoutNode node = container;
            bool hiddenOnTheWay = false;

            for (int depth = 0; depth < 64; depth++)
            {
                if (node == null) return;
                if (node.HiddenInInspector) hiddenOnTheWay = true;

                if (node is ContainerNode c)
                {
                    scope = Resolve(scope, c.PropertyPath);
                    if (c.Children == null || c.Children.Count == 0) return;
                    node = c.Children[0];
                    continue;
                }

                string path = node is BindingNode b ? b.PropertyPath
                            : node is CollectionNode cn ? cn.PropertyPath
                            : null;
                if (string.IsNullOrEmpty(path)) return;
                if (!string.Equals(Resolve(scope, path), ownerPath, StringComparison.Ordinal)) return;

                if (hiddenOnTheWay) anyHidden = true;
                else anyVisible = true;
                return;
            }
        }

        /// <summary>Scope composition, identical to <c>GroupToggleMirror.Resolve</c> and to the builder's own
        /// <c>childBasePath</c> — the three must agree or a scoped path means three different strings.</summary>
        private static string Resolve(string basePath, string path)
        {
            if (string.IsNullOrEmpty(path)) return basePath ?? "";
            if (string.IsNullOrEmpty(basePath)) return path;
            return basePath + "." + path;
        }
    }
}
