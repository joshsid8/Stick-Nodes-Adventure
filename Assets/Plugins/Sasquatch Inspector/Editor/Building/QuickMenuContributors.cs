using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Everything a contribution callback gets to see about the right-click being serviced.
    /// ContributorElement is the element the contribution was registered on (set per hit while
    /// Collect walks the chain); DeepTarget is the element actually under the cursor.
    /// </summary>
    public sealed class QuickMenuRequest
    {
        public QuickEditContext Ctx;
        public VisualElement Root;
        public VisualElement DeepTarget;
        public VisualElement ContributorElement;
        public Vector2 PanelPosition;
    }

    /// <summary>A callback that adds entries to the unified right-click menu for a registered element.</summary>
    public delegate void QuickMenuContribution(QuickMenuRequest request, QuickMenuModel menu);

    /// <summary>
    /// Per-element entry registry for the unified right-click menu. Anything inside a LIVE layout
    /// that wants extra menu entries (collection row actions, script-header actions, resize reset)
    /// registers here at build time — NEVER via a ContextualMenuManipulator, which the capture-phase
    /// takeover in QuickMenuController suppresses by design. Keyed by a ConditionalWeakTable because
    /// userData is already occupied (LayoutNode on node roots, row index ints on collection rows)
    /// and weak keys mean rebuilt trees drop their registrations together with the old elements.
    /// </summary>
    public static class QuickMenuContributors
    {
        private static readonly ConditionalWeakTable<VisualElement, List<QuickMenuContribution>> Table =
            new ConditionalWeakTable<VisualElement, List<QuickMenuContribution>>();

        /// <summary>Registers a menu <paramref name="contribution"/> for <paramref name="element"/> (additive; weakly keyed so it drops on rebuild).</summary>
        public static void Register(VisualElement element, QuickMenuContribution contribution)
        {
            if (element == null || contribution == null) return;
            Table.GetOrCreateValue(element).Add(contribution);
        }

        /// <summary>
        /// Walks from <paramref name="deepTarget"/> up to (and including) <paramref name="stopAt"/>,
        /// deepest-first, invoking every registered contribution, with a separator between distinct
        /// contributor elements.
        /// </summary>
        public static void Collect(VisualElement deepTarget, VisualElement stopAt,
                                   QuickMenuRequest request, QuickMenuModel menu)
        {
            if (deepTarget == null || menu == null) return;
            for (var el = deepTarget; el != null; el = el.parent)
            {
                if (Table.TryGetValue(el, out var list) && list.Count > 0)
                {
                    menu.AddSeparator();
                    if (request != null) request.ContributorElement = el;
                    foreach (var contribution in list) contribution(request, menu);
                }
                if (el == stopAt) break;
            }
        }
    }
}
