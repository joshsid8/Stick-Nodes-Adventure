using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Build-time handle onto one Tabs container's switching machinery, so the inspector search can
    /// auto-activate the first tab holding a match and badge each tab button with its match count.
    /// Wraps the builder's local <c>ActivateTab</c> closure — the same in-place switch tab clicks and the
    /// designer's spring-load drop affordance use (and the same precedent as the build auto-selecting the
    /// tab containing <c>selectedNode</c>).
    /// </summary>
    public sealed class TabController
    {
        private readonly Action<int> _activate;
        private readonly Func<int> _getActive;
        private readonly Action<int, int> _setBadge;

        /// <summary>The built tab-child elements, in tab order.</summary>
        public readonly IReadOnlyList<VisualElement> TabChildren;

        public TabController(Action<int> activate, Func<int> getActive, Action<int, int> setBadge,
            IReadOnlyList<VisualElement> tabChildren)
        {
            _activate = activate;
            _getActive = getActive;
            _setBadge = setBadge;
            TabChildren = tabChildren;
        }

        /// <summary>Switches the visible tab in place (no rebuild).</summary>
        public void Activate(int index) => _activate?.Invoke(index);

        /// <summary>The currently active tab index.</summary>
        public int ActiveIndex => _getActive?.Invoke() ?? 0;

        /// <summary>Shows a small count badge on tab <paramref name="index"/>'s button; count ≤ 0 removes it.</summary>
        public void SetBadge(int index, int count) => _setBadge?.Invoke(index, count);
    }

    /// <summary>
    /// Element → <see cref="TabController"/> maps registered by <see cref="LayoutTreeBuilder"/>'s Tabs
    /// branch: the Tabs container root maps to its controller, and each tab-CHILD element maps back to
    /// (controller, index) so a match found inside a tab can be attributed to the tab that shows it.
    /// ConditionalWeakTable keys — rebuilt trees drop their entries with the old elements.
    /// </summary>
    public static class TabControllerRegistry
    {
        private sealed class TabChildRef
        {
            public TabController Controller;
            public int Index;
        }

        private static readonly ConditionalWeakTable<VisualElement, TabController> Roots =
            new ConditionalWeakTable<VisualElement, TabController>();

        private static readonly ConditionalWeakTable<VisualElement, TabChildRef> Children =
            new ConditionalWeakTable<VisualElement, TabChildRef>();

        /// <summary>Registers <paramref name="controller"/> for a Tabs container root and maps each tab child back to it.</summary>
        public static void Register(VisualElement tabsRoot, TabController controller)
        {
            if (tabsRoot == null || controller == null) return;
            Roots.Remove(tabsRoot);
            Roots.Add(tabsRoot, controller);
            for (int i = 0; i < controller.TabChildren.Count; i++)
            {
                var child = controller.TabChildren[i];
                if (child == null) continue;
                Children.Remove(child);
                Children.Add(child, new TabChildRef { Controller = controller, Index = i });
            }
        }

        /// <summary>The controller registered on a Tabs container root, or null.</summary>
        public static TabController Get(VisualElement tabsRoot)
        {
            if (tabsRoot == null) return null;
            return Roots.TryGetValue(tabsRoot, out var c) ? c : null;
        }

        /// <summary>Resolves a tab-CHILD element back to its controller and tab index.</summary>
        public static bool TryGetTabChild(VisualElement element, out TabController controller, out int index)
        {
            controller = null;
            index = -1;
            if (element == null || !Children.TryGetValue(element, out var r)) return false;
            controller = r.Controller;
            index = r.Index;
            return true;
        }

        /// <summary>True when <paramref name="element"/> is a registered tab child (its display belongs to the tab machinery).</summary>
        public static bool IsTabChild(VisualElement element) => TryGetTabChild(element, out _, out _);
    }
}
