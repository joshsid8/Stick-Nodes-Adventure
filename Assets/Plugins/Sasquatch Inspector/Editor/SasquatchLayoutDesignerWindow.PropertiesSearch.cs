using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The Layout Designer's "Properties" panel field search — the inspector Script Header's search, brought to
    /// the designer. A magnifier sits to the right of the "Properties" title; clicking it expands an inline
    /// field across the header. Type and non-matching rows collapse away while
    /// the section TRAIL stays visible (Appearance ▸ Colors &amp; Borders ▸ Background Color), so a matched row
    /// is never stranded with an ambiguous name. Esc or a second magnifier click cancels; a compact
    /// <c>visible/total</c> count sits in the header, and the whole panel gets the accent
    /// <see cref="SearchActiveOverlay"/> frame so "search is on" is unmistakable.
    ///
    /// Why a bespoke engine (not <see cref="InspectorFieldSearch"/>/<see cref="InspectorSearchIndex"/>): the
    /// designer's rows are raw <see cref="VisualElement"/>s, not <c>LayoutNode</c>-tagged, so the node index
    /// can't walk them. We reuse the pure matcher helpers (<see cref="InspectorSearchIndex.Tokenize"/> /
    /// <see cref="InspectorSearchIndex.SplitWords"/> / <see cref="InspectorSearchIndex.Matches"/>) and mirror
    /// the three-pass Match / AncestorOfMatch / DescendantOfMatch classification over the panel's own tree.
    ///
    /// The header is built ONCE in CreateGUI and persists; only <see cref="_propertiesContainer"/> is torn down
    /// and rebuilt by <see cref="RefreshProperties"/>. So the filter self-heals: every rebuild ends by calling
    /// <see cref="ReapplyPropertiesSearchAfterRebuild"/>, which re-runs the active query against the fresh tree.
    /// The classify + apply core is static (so the tests can drive it on a synthetic panel with no window).
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        // ---- Header chrome (built once in CreateGUI, persists across RefreshProperties rebuilds) ----------
        private VisualElement _propSearchBar;     // the header Row (also the column context-menu target)
        private Label _propSearchTitle;           // "Properties"
        private TextField _propSearchField;       // inline search field, hidden until the magnifier opens it
        private Label _propSearchCount;           // compact "visible/total"
        private Button _propSearchToggle;         // the magnifier

        // Persisted so the query + open state survive a Save (which rebuilds the panel) and a domain reload.
        [SerializeField] private string _propertiesSearchQuery = "";
        [SerializeField] private bool _propertiesSearchOpen;

        // What the current filter application touched, so clearing restores EXACTLY that — never a blanket
        // "show everything" (which would clobber the builder's own conditional hides / fold state).
        private readonly List<(VisualElement el, StyleEnum<DisplayStyle> prev)> _propSearchHidden = new();
        private readonly List<(Foldout f, bool prev)> _propSearchExpanded = new();
        private readonly List<(VisualElement el, StyleFloat w, StyleColor c)> _propSearchStriped = new();

        private const string BaseFieldClass = "unity-base-field";
        private const string BaseFieldLabelClass = "unity-base-field__label";
        private const string FoldoutContentClass = "unity-foldout__content";
        private const string SearchFrameName = "sasq-search-active-frame";

        private enum PropKind { Section, Group, Leaf }
        private enum PropState { Hidden, Match, AncestorOfMatch, DescendantOfMatch }

        private sealed class PropEntry
        {
            public VisualElement Element;
            public PropKind Kind;
            public string Haystack;
            public PropEntry Parent;
            public PropState State;
            public bool Eligible; // leaves only: participates in hide + count (false = builder-gated field)
        }

        // ---- Header construction (called from CreateGUI in place of the bare "Properties" label) ----------

        /// <summary>Builds the Properties header as a row bar with an inline search field + magnifier.</summary>
        private VisualElement BuildPropertiesSearchHeader()
        {
            _propSearchBar = new VisualElement();
            _propSearchBar.style.flexDirection = FlexDirection.Row;
            _propSearchBar.style.alignItems = Align.Center;
            _propSearchBar.style.height = 32;
            _propSearchBar.style.paddingLeft = 10;
            _propSearchBar.style.paddingRight = 6;
            _propSearchBar.style.borderBottomWidth = 1;
            _propSearchBar.style.borderBottomColor = DesignerPalette.DividerLine;
            _propSearchBar.style.backgroundColor = DesignerPalette.PaneHeaderBg;

            _propSearchTitle = new Label("Properties");
            // flexGrow 0 — the title takes only its text width so the magnifier HUGS its right edge instead of
            // being flung to the far side of the bar (the empty remainder to the right stays free for the header
            // chrome that lands there later). Nothing else grows while closed, so the row packs left.
            _propSearchTitle.style.flexGrow = 0;
            _propSearchTitle.style.flexShrink = 0;
            _propSearchTitle.style.fontSize = 13;
            _propSearchTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _propSearchTitle.style.color = DesignerPalette.TextHeader;
            _propSearchTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
            _propSearchBar.Add(_propSearchTitle);

            // Magnifier FIRST, hugging the title. The field opens to its RIGHT and fills only the empty
            // remainder of the bar — it never covers "Properties", so the panel keeps its name while you search.
            _propSearchToggle = MakePropSearchIconButton(SasquatchIcons.Ids.Search, "Search properties", TogglePropertiesSearch);
            _propSearchBar.Add(_propSearchToggle);

            _propSearchField = new TextField { name = "sq-props-search-field" };
            _propSearchField.textEdition.placeholder = "Search…";
            _propSearchField.style.display = DisplayStyle.None; // the magnifier reveals it
            _propSearchField.style.flexGrow = 1;
            _propSearchField.style.flexShrink = 1;
            _propSearchField.style.minWidth = 0;
            _propSearchField.style.height = 18;
            _propSearchField.style.marginLeft = 4;
            _propSearchField.style.marginRight = 4;
            _propSearchField.RegisterValueChangedCallback(evt => ApplyPropertiesFilter(evt.newValue));
            // TrickleDown so the text input can't swallow Escape before we see it.
            _propSearchField.RegisterCallback<KeyDownEvent>(OnPropSearchKeyDown, TrickleDown.TrickleDown);
            // A click in the field must not bubble to the bar's column context menu affordance.
            _propSearchField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            _propSearchBar.Add(_propSearchField);

            _propSearchCount = new Label { name = "sq-props-search-count" };
            _propSearchCount.style.display = DisplayStyle.None;
            _propSearchCount.style.flexShrink = 0;
            _propSearchCount.style.marginLeft = 2;
            _propSearchCount.style.marginRight = 2;
            _propSearchCount.style.fontSize = 10;
            _propSearchCount.style.color = DesignerPalette.TextDisabled;
            _propSearchCount.style.unityTextAlign = TextAnchor.MiddleRight;
            _propSearchCount.pickingMode = PickingMode.Ignore;
            _propSearchBar.Add(_propSearchCount);

            // Keep the column's right-click Focus/Hide menu, now anchored on the bar.
            AddColumnContextMenu(_propSearchBar, 2);

            // A full window rebuild (domain reload / theme flip) reconstructs the header — restore the open look;
            // the first RefreshProperties tail re-applies the actual filter.
            ApplyPropSearchChrome(_propertiesSearchOpen);
            if (_propertiesSearchOpen) _propSearchField.SetValueWithoutNotify(_propertiesSearchQuery ?? "");

            return _propSearchBar;
        }

        /// <summary>
        /// The ONE place that flips the header between resting and searching. Four callers need this exact set
        /// (header build, Open, Close, the post-rebuild self-heal) — keeping it in one method is what stops them
        /// drifting out of lockstep.
        ///
        /// The "Properties" title STAYS PUT: the field opens into the bar's empty right-hand remainder rather
        /// than taking the bar over, so the panel never loses its name mid-search and the magnifier never slides
        /// out from under the cursor that just clicked it.
        /// </summary>
        private void ApplyPropSearchChrome(bool open)
        {
            if (_propSearchField == null) return;
            _propSearchField.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (_propSearchToggle != null) _propSearchToggle.tooltip = open ? "Close search  (Esc)" : "Search properties";
            // Accent blue while open — via the shared painter, so clicking the magnifier you're hovering
            // doesn't repaint a blue glyph onto its blue hover face.
            PaintSearchToggle(_propSearchToggle, open);
        }

        private Button MakePropSearchIconButton(string iconId, string tooltip, Action onClick)
        {
            var btn = new Button(onClick);
            btn.Add(SasquatchIcons.Create(iconId, 12));
            btn.tooltip = tooltip;
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.style.width = 22;
            btn.style.height = 20;
            btn.style.marginLeft = 2;
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            // The house button feel + the magnifier's own hover look: the button fills accent blue and the
            // glyph goes white. Kept in lockstep with the canvas search's twin magnifier.
            EditorSpringFX.ButtonSpring(btn);
            AddChromeHoverRepaint(btn, () => PaintSearchToggle(btn, _propertiesSearchOpen));
            return btn;
        }

        // ---- Open / close -------------------------------------------------------------------------------

        private void TogglePropertiesSearch()
        {
            if (_propertiesSearchOpen) ClosePropertiesSearch();
            else OpenPropertiesSearch();
        }

        private void OpenPropertiesSearch()
        {
            if (_propSearchField == null || _propertiesSearchOpen) return;
            _propertiesSearchOpen = true;
            ApplyPropSearchChrome(true);
            // Deferred: the field just became visible and needs a layout pass before Focus lands.
            _propSearchField.schedule.Execute(() =>
            {
                _propSearchField.Focus();
                _propSearchField.SelectAll();
            });
            ApplyPropertiesFilter(_propSearchField.value);
        }

        private void ClosePropertiesSearch()
        {
            if (_propSearchField == null) return;
            _propertiesSearchOpen = false;
            _propSearchField.SetValueWithoutNotify("");
            ApplyPropertiesFilter(""); // restore everything the filter touched
            ApplyPropSearchChrome(false);
        }

        private void OnPropSearchKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                ClosePropertiesSearch();
                evt.StopPropagation();
            }
        }

        // ---- Filter application -------------------------------------------------------------------------

        private void ApplyPropertiesFilter(string query) => ApplyPropertiesFilter(query, freshTree: false);

        /// <summary>
        /// Applies <paramref name="query"/> to the Properties panel. Restores whatever the previous apply
        /// touched first (skip that when <paramref name="freshTree"/> — those elements were just destroyed by a
        /// rebuild), then classifies the current tree and hides / stripes / expands accordingly. An empty query
        /// is exactly "restore and do nothing more".
        /// </summary>
        private void ApplyPropertiesFilter(string query, bool freshTree)
        {
            _propertiesSearchQuery = query ?? "";
            if (freshTree) ClearPropertiesFilterBookkeeping();
            else RestorePropertiesFilter();

            if (_propertiesContainer == null) return;

            var tokens = InspectorSearchIndex.Tokenize(_propertiesSearchQuery);
            if (tokens.Length == 0)
            {
                SetPropSearchCount(0, 0, show: false);
                SearchActiveOverlay.Hide(_propertiesContainer);
                return;
            }

            ApplyPropFilterCore(_propertiesContainer, tokens, DesignerPalette.Accent,
                _propSearchHidden, _propSearchExpanded, _propSearchStriped, out int visible, out int total);

            SetPropSearchCount(visible, total, show: true);
            // Unmistakable "a filter is hiding fields" cue — the inspector frame's twin, in BRAND GOLD for the
            // same reason: the designer is wall-to-wall accent blue (selected rows, active segments, hovered
            // chrome, selection washes), so a blue frame would vanish into it. Keep this in lockstep with the
            // inspector's WidgetPalette.BrandGold — one signal, one color, both surfaces.
            SearchActiveOverlay.Show(_propertiesContainer, DesignerPalette.BrandGold);
        }

        /// <summary>
        /// The classify + apply core, static so the tests can drive it on a synthetic panel without a live
        /// window. Walks <paramref name="container"/> into Section / Group / Leaf entries, classifies them, then
        /// hides non-matches, stripes matches, and force-expands the trail — recording each mutation into the
        /// passed lists so <see cref="RestorePropFilter"/> can undo EXACTLY what was done.
        /// </summary>
        private static void ApplyPropFilterCore(
            VisualElement container, string[] tokens, Color accent,
            List<(VisualElement el, StyleEnum<DisplayStyle> prev)> hidden,
            List<(Foldout f, bool prev)> expanded,
            List<(VisualElement el, StyleFloat w, StyleColor c)> striped,
            out int visible, out int total)
        {
            visible = 0;
            total = 0;
            if (container == null) return;

            var flat = new List<PropEntry>();
            var boundary = (container as ScrollView)?.contentContainer ?? container;
            WalkPropChildren(container, null, flat, boundary);
            ResolvePropStates(flat, tokens);

            foreach (var e in flat)
            {
                switch (e.Kind)
                {
                    case PropKind.Leaf:
                        if (!e.Eligible) break; // builder-gated field — never surfaced or counted
                        total++;
                        if (e.State == PropState.Hidden)
                        {
                            hidden.Add((e.Element, e.Element.style.display));
                            e.Element.style.display = DisplayStyle.None;
                        }
                        else
                        {
                            visible++;
                            if (e.State == PropState.Match)
                            {
                                striped.Add((e.Element, e.Element.style.borderLeftWidth, e.Element.style.borderLeftColor));
                                e.Element.style.borderLeftWidth = 2;
                                e.Element.style.borderLeftColor = accent;
                            }
                        }
                        break;

                    case PropKind.Section:
                        if (e.State == PropState.Hidden)
                        {
                            hidden.Add((e.Element, e.Element.style.display));
                            e.Element.style.display = DisplayStyle.None;
                        }
                        else if (e.Element is Foldout f && !f.value)
                        {
                            // Force-expand the trail. SetValueWithoutNotify ⇒ no ChangeEvent ⇒ the persisted
                            // _appearanceExpanded/etc. (written by the toggle callback) stay untouched.
                            expanded.Add((f, false));
                            f.SetValueWithoutNotify(true);
                        }
                        break;

                    case PropKind.Group:
                        if (e.State == PropState.Hidden)
                        {
                            hidden.Add((e.Element, e.Element.style.display));
                            e.Element.style.display = DisplayStyle.None;
                        }
                        break;
                }
            }
        }

        /// <summary>Walks a container's children into Section / Group / Leaf entries (skips decorative labels).</summary>
        private static void WalkPropChildren(VisualElement parentEl, PropEntry parentEntry, List<PropEntry> flat, VisualElement boundaryContent)
        {
            foreach (var c in parentEl.Children())
            {
                if (c == null || c.name == SearchFrameName) continue;

                if (c is Foldout f)
                {
                    var e = new PropEntry { Element = c, Kind = PropKind.Section, Haystack = BuildPropHaystack(f.text), Parent = parentEntry };
                    flat.Add(e);
                    WalkPropChildren(f, e, flat, boundaryContent); // f.Children() == the foldout's content
                    continue;
                }

                int fields = TopLevelFieldCount(c);
                if (fields >= 2)
                {
                    // A label-less wrapper holding several field rows (spacing control, range settings, …) —
                    // recurse so each inner field becomes its own leaf.
                    var e = new PropEntry { Element = c, Kind = PropKind.Group, Haystack = string.Empty, Parent = parentEntry };
                    flat.Add(e);
                    WalkPropChildren(c, e, flat, boundaryContent);
                }
                else if (fields == 1)
                {
                    var e = new PropEntry
                    {
                        Element = c,
                        Kind = PropKind.Leaf,
                        Haystack = BuildPropHaystack(PropLeafLabel(c)),
                        Parent = parentEntry,
                        Eligible = IsLeafVisibleOrRevealable(c, boundaryContent),
                    };
                    flat.Add(e);
                }
                else if (c is Button)
                {
                    var e = new PropEntry
                    {
                        Element = c,
                        Kind = PropKind.Leaf,
                        Haystack = BuildPropHaystack(PropLeafLabel(c)),
                        Parent = parentEntry,
                        Eligible = IsLeafVisibleOrRevealable(c, boundaryContent),
                    };
                    flat.Add(e);
                }
                // else: a plain Label / decorative element — no entry, so the filter never hides it.
            }
        }

        /// <summary>Three-pass classification, mirroring <see cref="InspectorSearchIndex.Resolve"/>.</summary>
        private static void ResolvePropStates(List<PropEntry> flat, string[] tokens)
        {
            // A builder-gated leaf can never MATCH. It is already excluded from the hide + the count, but if it
            // were allowed to match it would still drag its ancestors to AncestorOfMatch below — leaving a section
            // standing open and empty over a "0/n" count because the only "hit" is a control the builder hid.
            foreach (var e in flat)
                e.State = (e.Kind == PropKind.Leaf && !e.Eligible) ? PropState.Hidden
                    : InspectorSearchIndex.Matches(e.Haystack, tokens) ? PropState.Match
                    : PropState.Hidden;

            // Ancestors of every match stay visible so the match can render (keeps the section trail).
            foreach (var e in flat)
            {
                if (e.State != PropState.Match) continue;
                for (var a = e.Parent; a != null; a = a.Parent)
                    if (a.State == PropState.Hidden) a.State = PropState.AncestorOfMatch;
            }

            // Matching a section/group TITLE reveals its whole contents.
            foreach (var e in flat)
            {
                if (e.State != PropState.Hidden) continue;
                for (var a = e.Parent; a != null; a = a.Parent)
                {
                    if (a.State == PropState.Match && (a.Kind == PropKind.Section || a.Kind == PropKind.Group))
                    {
                        e.State = PropState.DescendantOfMatch;
                        break;
                    }
                }
            }
        }

        // ---- Restore / bookkeeping ----------------------------------------------------------------------

        private void RestorePropertiesFilter()
        {
            RestorePropFilter(_propSearchHidden, _propSearchExpanded, _propSearchStriped);
            ClearPropertiesFilterBookkeeping();
        }

        private static void RestorePropFilter(
            List<(VisualElement el, StyleEnum<DisplayStyle> prev)> hidden,
            List<(Foldout f, bool prev)> expanded,
            List<(VisualElement el, StyleFloat w, StyleColor c)> striped)
        {
            foreach (var (el, prev) in hidden) el.style.display = prev;
            foreach (var (f, prev) in expanded) f?.SetValueWithoutNotify(prev);
            foreach (var (el, w, c) in striped)
            {
                el.style.borderLeftWidth = w;
                el.style.borderLeftColor = c;
            }
        }

        private void ClearPropertiesFilterBookkeeping()
        {
            _propSearchHidden.Clear();
            _propSearchExpanded.Clear();
            _propSearchStriped.Clear();
        }

        private void SetPropSearchCount(int visible, int total, bool show)
        {
            if (_propSearchCount == null) return;
            if (show)
            {
                _propSearchCount.text = $"{visible}/{total}";
                _propSearchCount.style.display = DisplayStyle.Flex;
            }
            else
            {
                _propSearchCount.text = "";
                _propSearchCount.style.display = DisplayStyle.None;
            }
        }

        // ---- Rebuild self-heal (called at the tail of every RefreshProperties exit) ----------------------

        /// <summary>
        /// RefreshProperties fully rebuilds <see cref="_propertiesContainer"/>, so any active filter must be
        /// re-run against the fresh tree. The old bookkeeping references destroyed elements — drop it (never
        /// restore against it), then re-apply if the search is open and there is something to search.
        /// </summary>
        private void ReapplyPropertiesSearchAfterRebuild()
        {
            if (_propSearchToggle == null) return; // header not built yet
            ClearPropertiesFilterBookkeeping();

            bool hasContent = PropertiesHaveSearchableContent();
            _propSearchToggle.SetEnabled(hasContent);

            bool showField = hasContent && _propertiesSearchOpen;
            ApplyPropSearchChrome(showField);

            if (showField)
            {
                _propSearchField.SetValueWithoutNotify(_propertiesSearchQuery ?? "");
                ApplyPropertiesFilter(_propertiesSearchQuery ?? "", freshTree: true);
            }
            else
            {
                SetPropSearchCount(0, 0, show: false);
                SearchActiveOverlay.Hide(_propertiesContainer);
            }
        }

        private bool PropertiesHaveSearchableContent()
        {
            if (_propertiesContainer == null) return false;
            if (_propertiesContainer.Q<Foldout>() != null) return true;
            if (_propertiesContainer.Q<VisualElement>(className: BaseFieldClass) != null) return true;
            return false;
        }

        // ---- Element classification helpers -------------------------------------------------------------

        /// <summary>Number of top-level Unity fields under <paramref name="c"/> (a composite's sub-fields don't split it).</summary>
        private static int TopLevelFieldCount(VisualElement c)
        {
            if (c.ClassListContains(BaseFieldClass)) return 1; // c IS a field — one leaf, whatever its innards
            int n = 0;
            c.Query<VisualElement>(className: BaseFieldClass).ForEach(fe =>
            {
                for (var p = fe.hierarchy.parent; p != null && p != c; p = p.hierarchy.parent)
                    if (p.ClassListContains(BaseFieldClass)) return; // nested inside another field → don't double-count
                n++;
            });
            return n;
        }

        private static string PropLeafLabel(VisualElement c)
        {
            var lbl = c.Q<Label>(className: BaseFieldLabelClass);
            if (lbl != null && !string.IsNullOrEmpty(lbl.text)) return lbl.text;
            if (c is Button b && !string.IsNullOrEmpty(b.text)) return b.text;
            if (c is Label pl && !string.IsNullOrEmpty(pl.text)) return pl.text;
            return string.Empty;
        }

        private static string BuildPropHaystack(string label)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            var lower = label.ToLowerInvariant();
            var split = InspectorSearchIndex.SplitWords(label);
            var splitLower = split != null ? split.ToLowerInvariant() : lower;
            return splitLower != lower ? lower + " " + splitLower : lower;
        }

        /// <summary>True when this inline style explicitly sets display:none (a concrete value, not "unset").</summary>
        private static bool IsInlineHidden(VisualElement el)
        {
            var d = el.style.display;
            return d.keyword == StyleKeyword.Undefined && d.value == DisplayStyle.None;
        }

        /// <summary>
        /// A leaf is eligible only if it's actually reachable — every hidden ancestor between it and
        /// <paramref name="boundaryContent"/> must be a COLLAPSED FOLDOUT'S CONTENT (which force-expansion can
        /// reveal). Any other hidden ancestor (a builder conditional: validation message, per-side spacing, range
        /// settings) genuinely gates it, so it's excluded from both the filter and the count.
        ///
        /// Note the gate is the content element (<c>unity-foldout__content</c>), NOT the Foldout: collapsing sets
        /// the CONTENT's display, so a Foldout that is itself <c>display:none</c> was hidden by the builder — and
        /// the filter only ever force-EXPANDS a section, never un-hides one, so treating it as revealable would
        /// count (and stripe) rows that stay invisible.
        /// </summary>
        private static bool IsLeafVisibleOrRevealable(VisualElement leaf, VisualElement boundaryContent)
        {
            for (var p = leaf; p != null && p != boundaryContent; p = p.hierarchy.parent)
            {
                if (!IsInlineHidden(p)) continue;
                if (!p.ClassListContains(FoldoutContentClass)) return false;
            }
            return true;
        }

        // ---- Test seams (internal → visible to SasquatchInspector.Tests.Editor via InternalsVisibleTo) -----

        /// <summary>
        /// Test seam: how many rows the CURRENT filter application is hiding that are still LIVE under the panel.
        /// The liveness walk is the point — after a <see cref="RefreshProperties"/> rebuild, bookkeeping left over
        /// from the previous application points at destroyed elements, so a stale filter reads 0 here while a
        /// correctly self-healed one reads the fresh tree's hidden rows.
        /// </summary>
        internal int DebugPropSearchHiddenLiveCount
        {
            get
            {
                int n = 0;
                foreach (var h in _propSearchHidden)
                    for (var p = h.el; p != null; p = p.hierarchy.parent)
                        if (p == _propertiesContainer) { n++; break; }
                return n;
            }
        }

        /// <summary>Records exactly what a filter application touched, so a test can assert + then undo it.</summary>
        internal sealed class PropFilterProbe
        {
            public int Visible;
            public int Total;
            internal readonly List<(VisualElement el, StyleEnum<DisplayStyle> prev)> Hidden = new();
            internal readonly List<(Foldout f, bool prev)> Expanded = new();
            internal readonly List<(VisualElement el, StyleFloat w, StyleColor c)> Striped = new();
            public int HiddenCount => Hidden.Count;
            public int ExpandedCount => Expanded.Count;
            public int StripedCount => Striped.Count;
        }

        /// <summary>Runs the real classify + apply core over <paramref name="container"/> and reports what it did.</summary>
        internal static PropFilterProbe DebugApplyPropertiesFilter(VisualElement container, string query, Color accent)
        {
            var probe = new PropFilterProbe();
            var tokens = InspectorSearchIndex.Tokenize(query);
            if (tokens.Length == 0) return probe;
            ApplyPropFilterCore(container, tokens, accent, probe.Hidden, probe.Expanded, probe.Striped,
                out probe.Visible, out probe.Total);
            return probe;
        }

        /// <summary>Undoes exactly what <see cref="DebugApplyPropertiesFilter"/> recorded.</summary>
        internal static void DebugRestorePropertiesFilter(PropFilterProbe probe)
        {
            if (probe == null) return;
            RestorePropFilter(probe.Hidden, probe.Expanded, probe.Striped);
        }
    }
}
