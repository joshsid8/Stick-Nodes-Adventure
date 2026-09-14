using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// TRANSIENT open/close control over one collapsible site, registered at build time by
    /// <see cref="LayoutTreeBuilder"/>. <see cref="SetTransient"/> spins the chevron and toggles the content
    /// exactly like a user click — but NEVER writes <see cref="FoldoutStateStore"/>, so the inspector search
    /// can force a collapsed group open to reveal a match without permanently rearranging the user's fold
    /// state. <see cref="Restore"/> re-applies the persisted state (including any toggle the user made while
    /// the transient expansion was active).
    /// </summary>
    public sealed class GroupExpander
    {
        private readonly Action<bool> _setTransient;
        private readonly Action _restore;

        public GroupExpander(Action<bool> setTransient, Action restore)
        {
            _setTransient = setTransient;
            _restore = restore;
        }

        /// <summary>Opens/closes the site visually without persisting anything.</summary>
        public void SetTransient(bool open) => _setTransient?.Invoke(open);

        /// <summary>Re-applies the persisted (user-owned) open state.</summary>
        public void Restore() => _restore?.Invoke();
    }

    /// <summary>
    /// Element → <see cref="GroupExpander"/> map for every collapsible site the builder creates (plain
    /// Foldout, collapsible group shell, toggle-group foldout, expandable List and Table collections — the
    /// five lockstep registration sites in LayoutTreeBuilder). The expand logic lives in per-site build
    /// closures, so this registry is the only way a later feature (search) can reach it. Same
    /// ConditionalWeakTable idiom as <see cref="LayoutNodeRegistry"/>: weak keys die with rebuilt trees.
    /// </summary>
    public static class GroupExpanderRegistry
    {
        private static readonly ConditionalWeakTable<VisualElement, GroupExpander> Map =
            new ConditionalWeakTable<VisualElement, GroupExpander>();

        /// <summary>Maps <paramref name="element"/> (the collapsible site's root) to its expander.</summary>
        public static void Register(VisualElement element, GroupExpander expander)
        {
            if (element == null || expander == null) return;
            Map.Remove(element); // CWT.Add throws on duplicate keys
            Map.Add(element, expander);
        }

        /// <summary>The expander registered exactly on <paramref name="element"/>, or null.</summary>
        public static GroupExpander Get(VisualElement element)
        {
            if (element == null) return null;
            return Map.TryGetValue(element, out var expander) ? expander : null;
        }

        /// <summary>
        /// Resolves the expander for a NODE element: the element itself, else the first registered
        /// descendant reached without crossing into another node's subtree. Needed because
        /// ValidationBinder can wrap a collection in a <c>.sasq-validation-wrapper</c> — the node element is
        /// then the wrapper while the expander was registered on the collection's group root inside it.
        /// </summary>
        public static GroupExpander Find(VisualElement nodeElement)
        {
            if (nodeElement == null) return null;
            var direct = Get(nodeElement);
            if (direct != null) return direct;

            foreach (var child in nodeElement.hierarchy.Children())
            {
                // A child that carries its OWN node is a different entry's subtree — its expander is its own.
                if (LayoutNodeRegistry.GetNode(child) != null || child.userData is Runtime.LayoutNode) continue;
                var found = Find(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
