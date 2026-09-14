using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The inspector FIELD search: a slim filter over a styled inspector. Type and everything that doesn't
    /// match collapses away; groups containing a match force open (transiently — the user's fold state is
    /// never persisted over, see <see cref="GroupExpanderRegistry"/>); tabs containing matches get a count
    /// badge and the first one auto-activates; Enter drops focus into the first match's control so the flow
    /// ends in an edit, not a scroll. A FILTER, not a find — the same semantics as the shipped List/Table row
    /// search (<see cref="CollectionViewModel"/> filters rows out; it doesn't highlight them). Matches get the
    /// <c>sasq-search-match</c> accent stripe instead of in-text highlighting: labels come from many widget
    /// paths and rich-text injection can't be done uniformly (and <c>&lt;mark&gt;</c> draws on top of glyphs).
    ///
    /// The search UI lives INSIDE the Script Header widget: a hover-revealed magnifier button that opens an
    /// inline search field over the header's name. That header button is the
    /// SOLE entry point — no Ctrl+F, no separate bar, no right-click menu item — so a layout with no Script
    /// Header (or with its <see cref="Runtime.ScriptHeaderNode.ShowSearch"/> turned off) simply has no field
    /// search. This class is the FILTER ENGINE; the header (built in <see cref="LayoutTreeBuilder"/>) owns the
    /// chrome and hands its element handles here via <see cref="RegisterHeaderSearch"/> for <see cref="Attach"/>
    /// to wire.
    ///
    /// Deliberate v1 boundary (documented in the manual): collection ROWS are not searched — collections
    /// already own a row search, so inspector search matches the collection's label, reveals and expands it,
    /// and stops there.
    ///
    /// EXPORTED (baked) editors DO carry the search. The standalone kit ships a twin of this engine
    /// (<c>SasqKit</c>'s Search section), and the exporter bakes each node's haystack by calling
    /// <see cref="InspectorSearchIndex.BuildHaystack(Runtime.LayoutNode)"/> at export time — the same builder
    /// this class's index uses at runtime, so a query cannot mean two different things in the two renderings.
    /// If you change how a node is matched, change it in <see cref="InspectorSearchIndex"/> and both follow.
    /// </summary>
    public sealed class InspectorFieldSearch
    {
        public const string MatchClass = LayoutSearchFilter.MatchClass;

        /// <summary>The Script Header bar's USS class — how <see cref="Attach"/> locates the header that owns the
        /// search UI. Mirrors the literal set in <see cref="LayoutTreeBuilder.BuildScriptHeaderElement"/>.</summary>
        public const string HeaderBarClass = "sasq-script-header";

        /// <summary>Marker class on the header bar while the search field is open — the header's hover chrome
        /// reads it to keep the buttons + active wash visible even when the pointer leaves the bar.</summary>
        public const string SearchingClass = "sasq-script-header--searching";

        public const string FieldName = "sasq-script-header-search-field";
        public const string CountName = "sasq-script-header-search-count";

        /// <summary>
        /// The element handles the Script Header builds for its inline search UI. The header owns construction
        /// and hover chrome (<see cref="RefreshChrome"/> re-applies the reveal/active visuals, keeping the wash
        /// colours private to the builder); the engine owns open/close and the filter.
        /// </summary>
        internal sealed class HeaderSearchHandles
        {
            public VisualElement Bar;
            public Button ToggleButton;   // the magnifier
            public TextField Field;
            public Label Count;
            public Label NameLabel;
            public bool NameHiddenByNode; // the header's own "hide text" state — what Close restores the label to
            public Action RefreshChrome;  // re-apply hover/active button opacity + bar wash
        }

        // header bar → the handles it registered this build. Recreated per rebuild; old entries GC with the bar.
        private static readonly ConditionalWeakTable<VisualElement, HeaderSearchHandles> s_HeaderHandles =
            new ConditionalWeakTable<VisualElement, HeaderSearchHandles>();

        /// <summary>Called by the Script Header builder (live path only) to publish its inline-search handles so
        /// <see cref="Attach"/> can wire them. Idempotent per bar.</summary>
        internal static void RegisterHeaderSearch(VisualElement bar, HeaderSearchHandles handles)
        {
            if (bar == null || handles == null) return;
            s_HeaderHandles.Remove(bar);
            s_HeaderHandles.Add(bar, handles);
        }

        // ---- per-target persistence (query + open state survive same-target rebuilds; cleared on selection change)
        private sealed class SavedState
        {
            public string Query;
            public bool Open;
        }

        private static readonly Dictionary<int, SavedState> s_Saved = new Dictionary<int, SavedState>();

        static InspectorFieldSearch()
        {
            Selection.selectionChanged += s_Saved.Clear;
        }

        private readonly VisualElement _builtRoot;
        private readonly InspectorSearchIndex _index;
        private readonly int _targetId;
        private readonly HeaderSearchHandles _h;
        private bool _open;

        // What the current filter application touched, so clearing restores EXACTLY that — never a blanket
        // "show everything" (which would clobber tab switching and anything else that hides elements).
        // The apply/undo itself is LayoutSearchFilter, shared with the designer's Canvas search.
        private LayoutSearchFilter.Application _app;
        private int _cursor = -1;

        /// <summary>The current matches in top-to-bottom order — the ↓/↑ navigation order.</summary>
        private IReadOnlyList<SearchEntry> Matches =>
            _app != null ? _app.MatchList : System.Array.Empty<SearchEntry>();

        /// <summary>The number of indexed searchable leaves (test seam).</summary>
        public int SearchableLeafCount => _index.SearchableLeafCount;

        /// <summary>Whether the inline search field is currently open.</summary>
        public bool IsOpen => _open;

        /// <summary>
        /// Wires the field search onto one built inspector body by binding the Script Header's inline search UI
        /// to this filter engine. Returns null when the layout carries no search-enabled Script Header (no header,
        /// or its ShowSearch toggle is off) — then there is no field search for this layout at all. Call AFTER the
        /// body has been built; a same-target rebuild re-opens to the saved query.
        /// </summary>
        public static InspectorFieldSearch Attach(VisualElement builtRoot, UnityEngine.Object target)
        {
            if (builtRoot == null || target == null) return null;

            // The header owns the search UI and is the SOLE entry point: no search-enabled Script Header ⇒
            // no field search. Locate the header bar and the handles it registered while building (live path).
            var bar = FindOwnHeaderBar(builtRoot);
            if (bar == null || !s_HeaderHandles.TryGetValue(bar, out var handles)) return null;

            var index = InspectorSearchIndex.Build(builtRoot);
            var search = new InspectorFieldSearch(builtRoot, index, UnityObjectId.Of(target), handles);
            search.WireHeader();

            // Same-target rebuild (typing a value rebuilds the body): restore the query + open state.
            if (s_Saved.TryGetValue(search._targetId, out var saved) && saved.Open)
            {
                search.Open();
                search._h.Field.SetValueWithoutNotify(saved.Query ?? "");
                search.ApplyFilter(saved.Query);
            }

            return search;
        }

        /// <summary>
        /// The Script Header bar that belongs to THIS body: the first <see cref="HeaderBarClass"/> reachable
        /// from <paramref name="root"/> WITHOUT crossing into another editor's content. The boundaries are
        /// character-for-character the ones <see cref="InspectorSearchIndex.Walk"/> stops at, and they have to
        /// stay that way — the index and the header must agree on what "this body" means or the engine ends up
        /// typing into one tree and filtering another.
        ///
        /// <para>A bare <c>Q(className:)</c> was the original lookup, and it takes the first match in
        /// depth-first order with no regard for editor boundaries. A layout whose own root carries no Script
        /// Header (or carries one with ShowSearch off) but which holds an expanded In-Line Editor therefore
        /// resolved to the NESTED layout's header: the outer engine wired itself to the inner magnifier, so
        /// typing in the in-line editor's box filtered the HOST's fields and left the nested ones alone —
        /// the exact inversion of what the box is sitting inside. Now each body finds only its own.</para>
        /// </summary>
        internal static VisualElement FindOwnHeaderBar(VisualElement root)
        {
            if (root == null) return null;
            // Same three stops as the index walk: Unity's own editor host, the embedded-editor container, and
            // the in-line editor's nested scope. Checked on the start element too — harmless at a real build
            // root (never one of these) and correct if a caller ever hands one in.
            if (root is UnityEditor.UIElements.InspectorElement) return null;
            if (root.ClassListContains(EmbeddedEditorHost.ContainerClass)) return null;
            if (root.ClassListContains(SasquatchInspectorHeaderPencil.NestedEditorScopeClass)) return null;

            if (root.ClassListContains(HeaderBarClass)) return root;

            foreach (var child in root.hierarchy.Children())
            {
                var found = FindOwnHeaderBar(child);
                if (found != null) return found;
            }
            return null;
        }

        private InspectorFieldSearch(VisualElement builtRoot, InspectorSearchIndex index, int targetId, HeaderSearchHandles handles)
        {
            _builtRoot = builtRoot;
            _index = index;
            _targetId = targetId;
            _h = handles;
        }

        private void WireHeader()
        {
            _h.ToggleButton.clicked += Toggle;
            _h.Field.RegisterValueChangedCallback(evt => ApplyFilter(evt.newValue));
            // TrickleDown so the text input can't swallow the navigation keys before we see them.
            _h.Field.RegisterCallback<KeyDownEvent>(OnFieldKeyDown, TrickleDown.TrickleDown);
        }

        /// <summary>Opens (or closes) the inline search — the magnifier is a toggle.</summary>
        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        /// <summary>Reveals the inline field over the header name and puts the caret in it (select-all so typing replaces).</summary>
        public void Open()
        {
            if (_h == null || _open) return;
            _open = true;
            _h.Bar?.AddToClassList(SearchingClass);
            if (_h.NameLabel != null) _h.NameLabel.style.display = DisplayStyle.None;
            _h.Field.style.display = DisplayStyle.Flex;
            _h.Count.style.display = DisplayStyle.None; // ApplyFilter reveals it once a query is present
            _h.ToggleButton.tooltip = "Close search  (Esc)";
            _h.RefreshChrome?.Invoke();
            // Deferred: the field may have just become visible and needs a layout pass before Focus lands.
            _h.Field.schedule.Execute(() =>
            {
                _h.Field.Focus();
                _h.Field.SelectAll();
            });
            Save();
        }

        /// <summary>Clears the filter, hides the field and restores the header name.</summary>
        public void Close()
        {
            if (_h == null) return;
            _open = false;
            _h.Field.SetValueWithoutNotify("");
            ApplyFilter(""); // restore everything the filter touched
            _h.Field.style.display = DisplayStyle.None;
            _h.Count.style.display = DisplayStyle.None;
            if (_h.NameLabel != null)
                _h.NameLabel.style.display = _h.NameHiddenByNode ? DisplayStyle.None : DisplayStyle.Flex;
            _h.Bar?.RemoveFromClassList(SearchingClass);
            _h.ToggleButton.tooltip = "Search fields";
            _h.RefreshChrome?.Invoke();
            Save();
        }

        private void Save()
        {
            s_Saved[_targetId] = new SavedState { Query = _h.Field.value, Open = _open };
        }

        private void OnFieldKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    evt.StopPropagation();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    FocusMatch(_cursor >= 0 ? _cursor : 0);
                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                    MoveCursor(+1);
                    evt.StopPropagation();
                    break;
                case KeyCode.UpArrow:
                    MoveCursor(-1);
                    evt.StopPropagation();
                    break;
            }
        }

        /// <summary>
        /// Applies <paramref name="query"/> to the built body: every apply first restores everything the
        /// PREVIOUS apply touched (hides, stripes, transient expansions, badges), then classifies via
        /// <see cref="InspectorSearchIndex.Resolve"/> and applies the new state. An empty query is therefore
        /// exactly "restore and do nothing more".
        /// </summary>
        public void ApplyFilter(string query)
        {
            // ---- restore the previous application ------------------------------------------------------
            LayoutSearchFilter.Restore(_app);
            _app = null;
            _cursor = -1;

            var tokens = InspectorSearchIndex.Tokenize(query);
            if (tokens.Length == 0)
            {
                if (_h?.Count != null)
                {
                    _h.Count.text = "";
                    _h.Count.style.display = DisplayStyle.None;
                }
                // No query ⇒ nothing filtered ⇒ drop the "search is active" frame.
                SearchActiveOverlay.Hide(_builtRoot);
                Save();
                return;
            }

            // ---- classify + apply ----------------------------------------------------------------------
            // NEVER hide an ancestor of the search field. The root build container is one such ancestor and
            // has an empty haystack, so a no-match query resolves it to Hidden — hiding it would collapse the
            // ENTIRE inspector body AND the search box the user is typing into (the box lives in the header,
            // inside this container), leaving no way to even close the search. Also covers the pathological
            // case of a Script Header nested inside a group.
            var searchAncestors = new HashSet<VisualElement>();
            for (var ve = _h?.Bar; ve != null; ve = ve.hierarchy.parent)
                searchAncestors.Add(ve);

            _app = LayoutSearchFilter.Apply(_index, query, _builtRoot, searchAncestors);

            if (_h?.Count != null)
            {
                _h.Count.text = $"{_app.VisibleLeaves}/{_app.TotalLeaves}";
                _h.Count.style.display = DisplayStyle.Flex;
            }
            // An OBVIOUS, purely-visual cue that a filter is on — the discreet count alone is easy to miss.
            // Framed on the outermost styled body, so it survives heavy inner-widget styling.
            // BRAND GOLD, deliberately NOT the accent blue: blue is everywhere in a rendered inspector
            // (progress fills, selected tabs, badges, hover affordances, the match stripe two lines up), so a
            // blue frame reads as more chrome. Gold is spent nowhere else in the body — it reads as a signal.
            SearchActiveOverlay.Show(_builtRoot, WidgetPalette.BrandGold);
            Save();
        }

        /// <summary>Focuses the control of match <paramref name="index"/> (the Enter flow: end in an edit).</summary>
        private void FocusMatch(int index)
        {
            if (Matches.Count == 0) return;
            index = Mathf.Clamp(index, 0, Matches.Count - 1);
            var element = Matches[index].Element;
            ScrollToMatch(element);

            Focusable focus = element.Q<TextField>();
            if (focus == null)
            {
                focus = element.Query<VisualElement>()
                    .Where(v => v.focusable && v.canGrabFocus)
                    .First();
            }
            focus?.Focus();
        }

        /// <summary>↓/↑ from the query field: move the match cursor and ping it without leaving the box.</summary>
        private void MoveCursor(int delta)
        {
            if (Matches.Count == 0) return;
            _cursor = ((_cursor < 0 ? (delta > 0 ? -1 : 0) : _cursor) + delta + Matches.Count) % Matches.Count;
            var element = Matches[_cursor].Element;
            ScrollToMatch(element);
            QuickEditGlow.Ping(element);
        }

        private void ScrollToMatch(VisualElement element)
        {
            var scrollView = _builtRoot.GetFirstAncestorOfType<ScrollView>();
            if (scrollView == null) return;
            try { scrollView.ScrollTo(element); }
            catch (Exception) { /* not (yet) a laid-out descendant — the deferred pass below covers it */ }
            // Filter flips just changed the layout; re-apply once after it settles so the target actually
            // ends up in view (ScrollTo against a mid-settle height under- or over-shoots).
            element.schedule.Execute(() =>
            {
                try { scrollView.ScrollTo(element); }
                catch (Exception) { }
            }).ExecuteLater(50);
        }
    }
}
