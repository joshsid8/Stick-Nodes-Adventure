using System.Collections.Generic;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The apply/undo half of the layout field search, shared by every surface that filters a real
    /// <see cref="LayoutTreeBuilder"/> tree: given a resolved <see cref="InspectorSearchIndex"/> it hides the
    /// non-matches, stripes the matches, forces the trail open and badges the tabs — recording every mutation
    /// into an <see cref="Application"/> so <see cref="Restore"/> undoes EXACTLY that and nothing else. Never a
    /// blanket "show everything", which would clobber tab switching and the condition rules.
    ///
    /// TWO callers today: the live inspector's Script Header search (<see cref="InspectorFieldSearch"/>) and the
    /// Layout Designer's Canvas search. They filter the same kind of tree, so they must behave identically —
    /// and the subtleties here are exactly the sort that drift apart the moment they're copied: a tab CHILD's
    /// display belongs to the tab machinery (touching it fights ActivateTab), a matched container reveals its
    /// whole contents, and expansion goes through <see cref="GroupExpander.SetTransient"/> so the user's
    /// persisted fold state is never written. One core, two callers — add a third the same way.
    ///
    /// Note the split: classification is <see cref="InspectorSearchIndex.Resolve"/>'s job (pure logic over the
    /// node model); this type only ever writes to elements.
    /// </summary>
    public static class LayoutSearchFilter
    {
        /// <summary>Accent stripe applied to a matching row (defined in SasquatchWidgets.uss).</summary>
        public const string MatchClass = "sasq-search-match";

        /// <summary>Everything one <see cref="Apply"/> touched, so <see cref="Restore"/> can undo precisely that.</summary>
        public sealed class Application
        {
            internal readonly List<SearchEntry> Hidden = new List<SearchEntry>();
            internal readonly List<SearchEntry> Striped = new List<SearchEntry>();
            internal readonly List<GroupExpander> Expanders = new List<GroupExpander>();
            internal readonly List<TabController> Tabs = new List<TabController>();
            internal readonly List<SearchEntry> Matches = new List<SearchEntry>();

            /// <summary>Leaves still visible under the filter (the numerator of the header's count).</summary>
            public int VisibleLeaves;
            /// <summary>Every leaf the index knows about (the denominator).</summary>
            public int TotalLeaves;
            /// <summary>The matches in index order — i.e. top-to-bottom, which is the ↓/↑ navigation order.</summary>
            public IReadOnlyList<SearchEntry> MatchList => Matches;
        }

        /// <summary>
        /// Resolves <paramref name="query"/> against <paramref name="index"/> and applies it to the elements.
        /// </summary>
        /// <param name="root">The built root; bounds the tab-attribution walk.</param>
        /// <param name="keepVisible">
        /// Elements that must NEVER be hidden however they resolve. The caller passes the ancestor chain of its
        /// own search field here: the root build container has an empty haystack, so a no-match query resolves it
        /// to Hidden — hiding it would collapse the entire body AND, when the field lives inside that body (the
        /// Script Header case), the very box being typed into, leaving no way to even cancel the search.
        /// </param>
        public static Application Apply(InspectorSearchIndex index, string query, VisualElement root,
                                        ICollection<VisualElement> keepVisible)
        {
            var app = new Application();
            if (index == null) return app;

            index.Resolve(query);

            foreach (var entry in index.Entries)
            {
                if (!entry.IsContainer)
                {
                    app.TotalLeaves++;
                    if (entry.State != SearchEntryState.Hidden) app.VisibleLeaves++;
                }

                if (entry.State == SearchEntryState.Hidden)
                {
                    // A tab child's display belongs to the tab machinery — hiding/unhiding it here would
                    // fight ActivateTab. Its CONTENTS are entries of their own and filter normally.
                    if (!TabControllerRegistry.IsTabChild(entry.Element) &&
                        (keepVisible == null || !keepVisible.Contains(entry.Element)))
                    {
                        ElementVisibility.SetHidden(entry.Element, ElementVisibility.Reason.SearchFilter, true);
                        app.Hidden.Add(entry);
                    }
                }
                else if (entry.State == SearchEntryState.Match)
                {
                    entry.Element.AddToClassList(MatchClass);
                    app.Striped.Add(entry);
                    app.Matches.Add(entry);
                }
            }

            // Transient expansion: every match forces itself (a matched group/collection) and its whole
            // ancestor chain open, WITHOUT touching FoldoutStateStore.
            foreach (var match in app.Matches)
            {
                for (var e = match; e != null; e = e.Parent)
                {
                    var expander = GroupExpanderRegistry.Find(e.Element);
                    if (expander == null) continue;
                    if (!app.Expanders.Contains(expander)) app.Expanders.Add(expander);
                    expander.SetTransient(true);
                }
            }

            // Tabs: count matches per tab child (walking each match's element ancestry attributes it to
            // every tab level it sits under — nested Tabs badge correctly), then badge and auto-activate.
            var tabCounts = new Dictionary<TabController, Dictionary<int, int>>();
            foreach (var match in app.Matches)
            {
                for (var ve = match.Element; ve != null && ve != root; ve = ve.hierarchy.parent)
                {
                    if (!TabControllerRegistry.TryGetTabChild(ve, out var controller, out int tabIndex)) continue;
                    if (!tabCounts.TryGetValue(controller, out var counts))
                        tabCounts[controller] = counts = new Dictionary<int, int>();
                    counts.TryGetValue(tabIndex, out int n);
                    counts[tabIndex] = n + 1;
                }
            }
            foreach (var kv in tabCounts)
            {
                var controller = kv.Key;
                var counts = kv.Value;
                if (!app.Tabs.Contains(controller)) app.Tabs.Add(controller);
                int firstWithMatch = -1;
                for (int i = 0; i < controller.TabChildren.Count; i++)
                {
                    counts.TryGetValue(i, out int n);
                    controller.SetBadge(i, n);
                    if (n > 0 && firstWithMatch < 0) firstWithMatch = i;
                }
                counts.TryGetValue(controller.ActiveIndex, out int activeCount);
                if (activeCount == 0 && firstWithMatch >= 0)
                    controller.Activate(firstWithMatch);
            }

            return app;
        }

        /// <summary>Undoes exactly what <paramref name="app"/> recorded. Safe on a null / never-applied record.</summary>
        public static void Restore(Application app)
        {
            if (app == null) return;
            foreach (var e in app.Hidden)
                ElementVisibility.SetHidden(e.Element, ElementVisibility.Reason.SearchFilter, false);
            foreach (var e in app.Striped)
                e.Element.RemoveFromClassList(MatchClass);
            foreach (var ex in app.Expanders)
                ex.Restore();
            foreach (var tc in app.Tabs)
                for (int i = 0; i < tc.TabChildren.Count; i++)
                    tc.SetBadge(i, 0);
        }
    }
}
