using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The Layout Designer's CANVAS search — filter the layout you're building down to the widgets and bindings
    /// you're looking for. A magnifier hugs the "Canvas" title; click it, type, and every node whose label,
    /// member name, tooltip or group title doesn't match collapses away, while the section trail to each match
    /// stays open. Esc or a second click clears it. On a hundred-field component this is the difference between
    /// scrolling for the Damage row and landing on it.
    ///
    /// Unlike the Properties panel — whose rows are raw <see cref="VisualElement"/>s and need a bespoke walk
    /// (see SasquatchLayoutDesignerWindow.PropertiesSearch.cs) — the canvas IS a real
    /// <see cref="LayoutTreeBuilder"/> tree with <c>LayoutNode</c>-tagged elements. So this reuses the shipped
    /// engine outright: <see cref="InspectorSearchIndex"/> to index + classify, and
    /// <see cref="LayoutSearchFilter"/> to apply and undo. Same code the live inspector's Script Header search
    /// runs, so the two behave identically by construction rather than by discipline.
    ///
    /// The header is built ONCE in CreateGUI and persists; <see cref="RefreshPreview"/> tears down and rebuilds
    /// the canvas constantly (every edit, every drag), so the filter self-heals: every rebuild exit calls
    /// <see cref="ReapplyCanvasSearchAfterRebuild"/>, which re-indexes the fresh tree and re-runs the query.
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        // ---- Header chrome (built once in CreateGUI, persists across RefreshPreview rebuilds) -------------
        private Label _canvasSearchTitle;         // "Canvas"
        private TextField _canvasSearchField;     // inline search field, hidden until the magnifier opens it
        private Label _canvasSearchCount;         // compact "visible/total"
        private Button _canvasSearchToggle;       // the magnifier

        // Persisted so the query + open state survive a Save (which rebuilds) and a domain reload.
        [SerializeField] private string _canvasSearchQuery = "";
        [SerializeField] private bool _canvasSearchOpen;

        // What the current application touched, so clearing restores EXACTLY that.
        private LayoutSearchFilter.Application _canvasSearchApp;

        /// <summary>The header's flexible middle — the empty run between the magnifier and the mode toggles,
        /// which also hosts the "Layout" menu button. The search bar OVERLAYS this region.</summary>
        private VisualElement _canvasHeaderMiddle;
        private VisualElement _canvasSearchOverlay;   // absolutely-positioned bar holding the field + count

        // ---- Header construction (called from CreateGUI, in place of the bare "Canvas" label) -------------

        /// <summary>
        /// Builds the "Canvas" title, its magnifier, and the header's flexible middle region. The magnifier sits
        /// immediately right of the title (the title does NOT flex-grow), matching the Properties header.
        ///
        /// The search bar itself is an ABSOLUTE overlay pinned across that middle region, not a flex sibling:
        /// opening it must not shove the Layout menu and the Design/Live-Preview toggles rightward. It simply
        /// covers the empty run (and the Layout button sitting in it) and lifts off again on close — nothing in
        /// the header ever moves. CreateGUI fills the middle region with the spacer + Layout menu, which the
        /// overlay then sits on top of.
        /// </summary>
        private void BuildCanvasSearchHeader(VisualElement headerRow)
        {
            _canvasSearchTitle = new Label("Canvas");
            _canvasSearchTitle.style.fontSize = 13;
            _canvasSearchTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _canvasSearchTitle.style.color = DesignerPalette.TextHeader;
            _canvasSearchTitle.style.flexGrow = 0;
            _canvasSearchTitle.style.flexShrink = 0;
            headerRow.Add(_canvasSearchTitle);

            _canvasSearchToggle = MakeCanvasSearchIconButton(SasquatchIcons.Ids.Search,
                "Search the canvas  (widgets + bindings)", ToggleCanvasSearch);
            headerRow.Add(_canvasSearchToggle);

            // The flexible middle. CreateGUI fills it with the spacer + Layout menu; the overlay covers it.
            _canvasHeaderMiddle = new VisualElement();
            _canvasHeaderMiddle.style.flexGrow = 1;
            _canvasHeaderMiddle.style.flexShrink = 1;
            _canvasHeaderMiddle.style.minWidth = 0;
            _canvasHeaderMiddle.style.flexDirection = FlexDirection.Row;
            _canvasHeaderMiddle.style.alignItems = Align.Center;
            headerRow.Add(_canvasHeaderMiddle);

            // Absolute + inset, so it takes NO layout space: the header behind it never reflows when the search
            // opens. Opaque (it paints the header's own background) because it must genuinely COVER the Layout
            // button underneath rather than let it show through.
            _canvasSearchOverlay = new VisualElement { name = "sq-canvas-search-overlay" };
            _canvasSearchOverlay.style.display = DisplayStyle.None; // the magnifier reveals it
            _canvasSearchOverlay.style.position = Position.Absolute;
            _canvasSearchOverlay.style.left = 0;
            _canvasSearchOverlay.style.right = 4;
            _canvasSearchOverlay.style.top = 2;
            _canvasSearchOverlay.style.bottom = 2;
            _canvasSearchOverlay.style.flexDirection = FlexDirection.Row;
            _canvasSearchOverlay.style.alignItems = Align.Center;
            _canvasSearchOverlay.style.backgroundColor = DesignerPalette.PaneHeaderBg;
            _canvasHeaderMiddle.Add(_canvasSearchOverlay);

            _canvasSearchField = new TextField { name = "sq-canvas-search-field" };
            _canvasSearchField.textEdition.placeholder = "Filter widgets & bindings…";
            _canvasSearchField.style.flexGrow = 1;
            _canvasSearchField.style.flexShrink = 1;
            _canvasSearchField.style.minWidth = 0;
            _canvasSearchField.style.height = 18;
            _canvasSearchField.style.marginLeft = 4;
            _canvasSearchField.style.marginRight = 4;
            _canvasSearchField.style.fontSize = 11;
            _canvasSearchField.RegisterValueChangedCallback(evt => ApplyCanvasFilter(evt.newValue));
            // TrickleDown so the text input can't swallow Escape before we see it.
            _canvasSearchField.RegisterCallback<KeyDownEvent>(OnCanvasSearchKeyDown, TrickleDown.TrickleDown);
            _canvasSearchField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            _canvasSearchOverlay.Add(_canvasSearchField);

            _canvasSearchCount = new Label { name = "sq-canvas-search-count" };
            _canvasSearchCount.style.display = DisplayStyle.None;
            _canvasSearchCount.style.flexShrink = 0;
            _canvasSearchCount.style.marginRight = 4;
            _canvasSearchCount.style.fontSize = 10;
            _canvasSearchCount.style.color = DesignerPalette.TextDisabled;
            _canvasSearchCount.style.unityTextAlign = TextAnchor.MiddleRight;
            _canvasSearchCount.pickingMode = PickingMode.Ignore;
            _canvasSearchOverlay.Add(_canvasSearchCount);

            // A full window rebuild (domain reload / theme flip) reconstructs the header — restore the open look;
            // the first RefreshPreview tail re-applies the actual filter.
            ApplyCanvasSearchChrome(_canvasSearchOpen);
            if (_canvasSearchOpen) _canvasSearchField.SetValueWithoutNotify(_canvasSearchQuery ?? "");
        }

        private Button MakeCanvasSearchIconButton(string iconId, string tooltip, Action onClick)
        {
            var btn = new Button(onClick);
            btn.Add(SasquatchIcons.Create(iconId, 12));
            btn.tooltip = tooltip;
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.style.width = 22;
            btn.style.height = 20;
            btn.style.marginLeft = 2; // hugs the title
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            // Lockstep with the Properties bar's twin magnifier: house spring + fill-blue/glyph-white on hover.
            EditorSpringFX.ButtonSpring(btn);
            AddChromeHoverRepaint(btn, () => PaintSearchToggle(btn, _canvasSearchOpen));
            return btn;
        }

        /// <summary>
        /// The ONE place that flips the canvas header between closed and searching. The "Canvas" title stays put
        /// — the bar is an absolute OVERLAY over the header's middle run, so opening it covers the Layout menu
        /// and the empty space rather than pushing anything sideways.
        /// </summary>
        private void ApplyCanvasSearchChrome(bool open)
        {
            if (_canvasSearchField == null) return;
            _canvasSearchOverlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            // Draw order IS child order, and the overlay is built before CreateGUI fills the region behind it —
            // so without this the Layout button would paint straight through it. Re-asserted on every open, so a
            // control added to the region later can't sneak on top. (Layout-safe: the overlay is absolute, so its
            // sibling index doesn't affect where anything sits.)
            if (open) _canvasSearchOverlay.BringToFront();
            if (_canvasSearchToggle != null)
                _canvasSearchToggle.tooltip = open ? "Close search  (Esc)" : "Search the canvas  (widgets + bindings)";
            // Accent blue while open — via the shared painter, so hover (blue face, white glyph) still wins.
            PaintSearchToggle(_canvasSearchToggle, open);
        }

        // ---- Open / close --------------------------------------------------------------------------------

        private void ToggleCanvasSearch()
        {
            if (_canvasSearchOpen) CloseCanvasSearch();
            else OpenCanvasSearch();
        }

        private void OpenCanvasSearch()
        {
            if (_canvasSearchField == null || _canvasSearchOpen) return;
            _canvasSearchOpen = true;
            ApplyCanvasSearchChrome(true);
            // Deferred: the field just became visible and needs a layout pass before Focus lands.
            _canvasSearchField.schedule.Execute(() =>
            {
                _canvasSearchField.Focus();
                _canvasSearchField.SelectAll();
            });
            ApplyCanvasFilter(_canvasSearchField.value);
        }

        private void CloseCanvasSearch()
        {
            if (_canvasSearchField == null) return;
            _canvasSearchOpen = false;
            _canvasSearchField.SetValueWithoutNotify("");
            ApplyCanvasFilter(""); // restore everything the filter touched
            ApplyCanvasSearchChrome(false);
        }

        private void OnCanvasSearchKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                CloseCanvasSearch();
                evt.StopPropagation();
            }
        }

        // ---- Filter application --------------------------------------------------------------------------

        private void ApplyCanvasFilter(string query) => ApplyCanvasFilter(query, freshTree: false);

        /// <summary>
        /// Applies <paramref name="query"/> to the canvas. Restores whatever the previous apply touched first —
        /// skipped when <paramref name="freshTree"/>, because a rebuild just destroyed those elements and the
        /// record points at corpses. An empty query is exactly "restore and do nothing more".
        /// </summary>
        private void ApplyCanvasFilter(string query, bool freshTree)
        {
            _canvasSearchQuery = query ?? "";
            if (!freshTree) LayoutSearchFilter.Restore(_canvasSearchApp);
            _canvasSearchApp = null;

            if (_canvasContent == null) return;

            if (InspectorSearchIndex.Tokenize(_canvasSearchQuery).Length == 0)
            {
                SetCanvasSearchCount(0, 0, show: false);
                SearchActiveOverlay.Hide(_previewContainer);
                return;
            }

            // includeTypeNames: on the CANVAS you hunt by structure — "divider", "slider", "image" — so the
            // index also carries what each node IS. Off in the live inspector, where you hunt by data.
            var index = InspectorSearchIndex.Build(_canvasContent, includeTypeNames: true);

            // NEVER hide a root entry. The canvas's outermost build container carries the layout's root
            // ContainerNode, whose haystack is empty — so a no-match query resolves it to Hidden and hiding it
            // would blank the entire canvas (including its drop zones) rather than just emptying it.
            var keepVisible = new HashSet<VisualElement>();
            foreach (var e in index.Entries)
                if (e.Parent == null) keepVisible.Add(e.Element);

            _canvasSearchApp = LayoutSearchFilter.Apply(index, _canvasSearchQuery, _canvasContent, keepVisible);

            SetCanvasSearchCount(_canvasSearchApp.VisibleLeaves, _canvasSearchApp.TotalLeaves, show: true);
            // Unmistakable "a filter is hiding widgets" cue, on the scroll VIEWPORT so it frames the canvas
            // rather than the (taller/shorter) content. Brand gold — the same signal as the inspector's.
            SearchActiveOverlay.Show(_previewContainer, DesignerPalette.BrandGold);
        }

        private void SetCanvasSearchCount(int visible, int total, bool show)
        {
            if (_canvasSearchCount == null) return;
            if (show)
            {
                _canvasSearchCount.text = $"{visible}/{total}";
                _canvasSearchCount.style.display = DisplayStyle.Flex;
            }
            else
            {
                _canvasSearchCount.text = "";
                _canvasSearchCount.style.display = DisplayStyle.None;
            }
        }

        // ---- Rebuild self-heal (called at every exit of RefreshPreview) ----------------------------------

        /// <summary>
        /// RefreshPreview rebuilds the whole canvas, so an active filter must be re-indexed and re-run against
        /// the fresh tree. The old record references destroyed elements — drop it (never restore against it),
        /// then re-apply if the search is open and there is anything to search.
        /// </summary>
        private void ReapplyCanvasSearchAfterRebuild()
        {
            if (_canvasSearchToggle == null) return; // header not built yet
            _canvasSearchApp = null;

            bool hasContent = CanvasHasSearchableContent();
            _canvasSearchToggle.SetEnabled(hasContent);

            bool showField = hasContent && _canvasSearchOpen;
            ApplyCanvasSearchChrome(showField);

            if (showField)
            {
                _canvasSearchField.SetValueWithoutNotify(_canvasSearchQuery ?? "");
                ApplyCanvasFilter(_canvasSearchQuery ?? "", freshTree: true);
            }
            else
            {
                SetCanvasSearchCount(0, 0, show: false);
                SearchActiveOverlay.Hide(_previewContainer);
            }
        }

        /// <summary>
        /// False for the blank-state picker / an error card (nothing node-tagged to filter) and for the preset
        /// editor, whose centre pane is a SINGLE live widget preview — there is nothing to filter down to, so
        /// its header drops the search entirely (see the _presetMode branch in CreateGUI).
        /// </summary>
        private bool CanvasHasSearchableContent()
        {
            if (_presetMode || _canvasContent == null || _clonedLayoutAsset == null) return false;
            return InspectorSearchIndex.Build(_canvasContent, includeTypeNames: true).Entries.Count > 0;
        }

        /// <summary>
        /// Loading a different type replaces the layout entirely, so a query aimed at the old one is meaningless.
        /// Clears it (the bar stays open) — mirrors the Properties search's per-node reset.
        /// </summary>
        private void ResetCanvasSearchForNewLayout()
        {
            _canvasSearchQuery = "";
            _canvasSearchApp = null;
            _canvasSearchField?.SetValueWithoutNotify("");
        }

        // ---- Test seam --------------------------------------------------------------------------------

        /// <summary>
        /// Test seam: how many canvas elements the CURRENT filter application is hiding that are still LIVE under
        /// the canvas. The liveness walk is the point — after a RefreshPreview rebuild, a record left over from
        /// the previous application points at destroyed elements, so a stale filter reads 0 here while a
        /// correctly self-healed one reads the fresh tree's hidden nodes.
        /// </summary>
        internal int DebugCanvasSearchHiddenLiveCount
        {
            get
            {
                if (_canvasSearchApp == null || _canvasContent == null) return 0;
                int n = 0;
                foreach (var e in _canvasSearchApp.Hidden)
                    for (var p = e.Element; p != null; p = p.hierarchy.parent)
                        if (p == _canvasContent) { n++; break; }
                return n;
            }
        }
    }
}
