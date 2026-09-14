using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The Layout Designer's HIERARCHY search — the third and last of the designer's search bars. A magnifier
    /// hugs the "Hierarchy" title; click it and an inline field slides over the header's buttons, and the tree
    /// prunes down to the rows you're hunting for, each match wearing an accent stripe with its parent trail
    /// still above it. Esc or a second click clears it. Search by what a row SAYS — a binding's name or label,
    /// a container's title or its F2 rename, a divider's caption, a tab's name — or by what a row IS
    /// ("divider", "slider", "space"), the same widget-type vocabulary the Canvas search answers to.
    ///
    /// Why this one filters differently from its two siblings: the inspector's and canvas's searches HIDE
    /// elements in a built tree and record every mutation so clearing can put it back exactly. The hierarchy
    /// has no such tree to preserve — <see cref="RefreshHierarchy"/> re-renders it from the node model on every
    /// click already, so the filter simply decides which rows get BUILT. There is nothing to restore, which
    /// means there is no bookkeeping to go stale and no self-heal hook to forget at a new rebuild exit: a
    /// rebuild IS the re-apply. <see cref="BeginHierarchySearchPass"/> resolves the query before the render and
    /// <see cref="EndHierarchySearchPass"/> reports it after, and <see cref="RenderTree"/> consults the result.
    ///
    /// Classification itself is the shipped engine's, not a third copy: entries are handed to
    /// <see cref="InspectorSearchIndex.Resolve"/>, so Match / AncestorOfMatch / DescendantOfMatch mean exactly
    /// what they mean in the other two surfaces.
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        // ---- Header chrome (built once in CreateGUI, persists across RefreshHierarchy rebuilds) -----------
        private Label _hierSearchTitle;          // "Hierarchy"
        private Button _hierSearchToggle;        // the magnifier
        private TextField _hierSearchField;      // inline search field, hidden until the magnifier opens it
        private Label _hierSearchCount;          // compact "visible/total"

        /// <summary>The header's flexible middle — the run holding the add-container and gear buttons. The
        /// search bar OVERLAYS this region, covering both rather than pushing them off the edge.</summary>
        private VisualElement _hierHeaderMiddle;
        private VisualElement _hierSearchOverlay;

        // Persisted so the query + open state survive a Save (which rebuilds) and a domain reload.
        [SerializeField] private string _hierarchySearchQuery = "";
        [SerializeField] private bool _hierarchySearchOpen;

        // Resolved once per RefreshHierarchy, consumed by RenderTree. Null = no filter is running, which is
        // the signal every consumer checks — never an empty dictionary, so "not filtering" can't be confused
        // with "filtering, nothing matched".
        private Dictionary<LayoutNode, SearchEntryState> _hierSearchStates;
        private HashSet<LayoutNode> _hierSearchPinnedOpen;
        private int _hierSearchTotalRows;
        private int _hierSearchVisibleRows;

        // ---- Header construction (called from CreateGUI, in place of the bare "Hierarchy" label) ----------

        /// <summary>
        /// Builds the "Hierarchy" title, its magnifier, and the header's flexible middle region. The magnifier
        /// sits immediately right of the title (the title does NOT flex-grow), matching the Properties and
        /// Canvas headers.
        ///
        /// The search bar itself is an ABSOLUTE overlay pinned across that middle region, not a flex sibling.
        /// The header is only 32px of a narrow panel and already carries two icon buttons; opening a field
        /// inline would either shove them out of the panel or squeeze the field to nothing. Instead it simply
        /// covers them — they are "add a root container" and a display-options gear, neither of which anyone
        /// reaches for mid-search — and lifts off again on close, with nothing in the header having moved.
        /// CreateGUI fills the middle region with the spacer + both buttons, which the overlay sits on top of.
        /// </summary>
        private void BuildHierarchySearchHeader(VisualElement headerRow)
        {
            _hierSearchTitle = new Label("Hierarchy");
            _hierSearchTitle.style.paddingLeft = 10;
            _hierSearchTitle.style.fontSize = 13;
            _hierSearchTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hierSearchTitle.style.color = DesignerPalette.TextHeader;
            _hierSearchTitle.style.flexGrow = 0;
            _hierSearchTitle.style.flexShrink = 0;
            headerRow.Add(_hierSearchTitle);

            _hierSearchToggle = MakeHierSearchIconButton(SasquatchIcons.Ids.Search,
                "Search the hierarchy  (widgets + bindings)", ToggleHierarchySearch);
            headerRow.Add(_hierSearchToggle);

            // The flexible middle. CreateGUI fills it with the spacer + add/gear buttons; the overlay covers it.
            _hierHeaderMiddle = new VisualElement();
            _hierHeaderMiddle.style.flexGrow = 1;
            _hierHeaderMiddle.style.flexShrink = 1;
            _hierHeaderMiddle.style.minWidth = 0;
            _hierHeaderMiddle.style.flexDirection = FlexDirection.Row;
            _hierHeaderMiddle.style.alignItems = Align.Center;
            headerRow.Add(_hierHeaderMiddle);

            // Absolute + inset, so it takes NO layout space: the header behind it never reflows when the search
            // opens. Opaque (it paints the header's own background) because it must genuinely COVER the buttons
            // underneath rather than let them show through.
            _hierSearchOverlay = new VisualElement { name = "sq-hier-search-overlay" };
            _hierSearchOverlay.style.display = DisplayStyle.None; // the magnifier reveals it
            _hierSearchOverlay.style.position = Position.Absolute;
            _hierSearchOverlay.style.left = 0;
            _hierSearchOverlay.style.right = 4;
            _hierSearchOverlay.style.top = 2;
            _hierSearchOverlay.style.bottom = 2;
            _hierSearchOverlay.style.flexDirection = FlexDirection.Row;
            _hierSearchOverlay.style.alignItems = Align.Center;
            _hierSearchOverlay.style.backgroundColor = DesignerPalette.PaneHeaderBg;
            _hierHeaderMiddle.Add(_hierSearchOverlay);

            _hierSearchField = new TextField { name = "sq-hier-search-field" };
            _hierSearchField.textEdition.placeholder = "Filter widgets & bindings…";
            _hierSearchField.style.flexGrow = 1;
            _hierSearchField.style.flexShrink = 1;
            _hierSearchField.style.minWidth = 0;
            _hierSearchField.style.height = 18;
            _hierSearchField.style.marginLeft = 4;
            _hierSearchField.style.marginRight = 4;
            _hierSearchField.style.fontSize = 11;
            _hierSearchField.RegisterValueChangedCallback(evt => ApplyHierarchyFilter(evt.newValue));
            // TrickleDown so the text input can't swallow Escape before we see it.
            _hierSearchField.RegisterCallback<KeyDownEvent>(OnHierSearchKeyDown, TrickleDown.TrickleDown);
            _hierSearchField.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            _hierSearchOverlay.Add(_hierSearchField);

            _hierSearchCount = new Label { name = "sq-hier-search-count" };
            _hierSearchCount.style.display = DisplayStyle.None;
            _hierSearchCount.style.flexShrink = 0;
            _hierSearchCount.style.marginRight = 4;
            _hierSearchCount.style.fontSize = 10;
            _hierSearchCount.style.color = DesignerPalette.TextDisabled;
            _hierSearchCount.style.unityTextAlign = TextAnchor.MiddleRight;
            _hierSearchCount.pickingMode = PickingMode.Ignore;
            _hierSearchOverlay.Add(_hierSearchCount);

            // A full window rebuild (domain reload / theme flip) reconstructs the header — restore the open look;
            // the first RefreshHierarchy re-applies the actual filter.
            ApplyHierSearchChrome(_hierarchySearchOpen);
            if (_hierarchySearchOpen) _hierSearchField.SetValueWithoutNotify(_hierarchySearchQuery ?? "");
        }

        private Button MakeHierSearchIconButton(string iconId, string tooltip, Action onClick)
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
            // Lockstep with the Properties and Canvas magnifiers: house spring + the shared four-state paint.
            EditorSpringFX.ButtonSpring(btn);
            AddChromeHoverRepaint(btn, () => PaintSearchToggle(btn, _hierarchySearchOpen));
            return btn;
        }

        /// <summary>
        /// The ONE place that flips the hierarchy header between closed and searching. The "Hierarchy" title
        /// stays put — the bar is an absolute OVERLAY over the header's middle run, so opening it covers the
        /// add-container and gear buttons rather than pushing anything sideways.
        /// </summary>
        private void ApplyHierSearchChrome(bool open)
        {
            if (_hierSearchOverlay == null) return;
            _hierSearchOverlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            // Draw order IS child order, and the overlay is built before CreateGUI fills the region behind it —
            // so without this the buttons would paint straight through it. Re-asserted on every open, so a
            // control added to the region later can't sneak on top. (Layout-safe: the overlay is absolute, so
            // its sibling index doesn't affect where anything sits.)
            if (open) _hierSearchOverlay.BringToFront();
            if (_hierSearchToggle != null)
                _hierSearchToggle.tooltip = open ? "Close search  (Esc)" : "Search the hierarchy  (widgets + bindings)";
            // Accent blue while open — via the shared painter, so hover (a lighter blue) still wins.
            PaintSearchToggle(_hierSearchToggle, open);
        }

        // ---- Open / close --------------------------------------------------------------------------------

        private void ToggleHierarchySearch()
        {
            if (_hierarchySearchOpen) CloseHierarchySearch();
            else OpenHierarchySearch();
        }

        private void OpenHierarchySearch()
        {
            if (_hierSearchField == null || _hierarchySearchOpen) return;
            _hierarchySearchOpen = true;
            ApplyHierSearchChrome(true);
            // Deferred: the field just became visible and needs a layout pass before Focus lands.
            _hierSearchField.schedule.Execute(() =>
            {
                _hierSearchField.Focus();
                _hierSearchField.SelectAll();
            });
            ApplyHierarchyFilter(_hierSearchField.value);
        }

        private void CloseHierarchySearch()
        {
            if (_hierSearchField == null) return;
            _hierarchySearchOpen = false;
            _hierSearchField.SetValueWithoutNotify("");
            ApplyHierarchyFilter(""); // re-renders the full tree
            ApplyHierSearchChrome(false);
        }

        private void OnHierSearchKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                CloseHierarchySearch();
                evt.StopPropagation();
            }
        }

        // ---- Filter application --------------------------------------------------------------------------

        /// <summary>
        /// Applies <paramref name="query"/> to the tree. There is no incremental hide/restore to run: the
        /// hierarchy is rendered from the node model, so re-rendering it IS the filter. Cheap for the same
        /// reason — every selection click already does exactly this.
        /// </summary>
        private void ApplyHierarchyFilter(string query)
        {
            _hierarchySearchQuery = query ?? "";
            RefreshHierarchy();
        }

        /// <summary>
        /// Resolves the active query before <see cref="RenderTree"/> runs. Leaves every field null/zero when
        /// nothing is being filtered — that null is the signal RenderTree checks, so an unfiltered render pays
        /// nothing beyond a tokenize.
        /// </summary>
        private void BeginHierarchySearchPass()
        {
            _hierSearchStates = null;
            _hierSearchPinnedOpen = null;
            _hierSearchTotalRows = 0;
            _hierSearchVisibleRows = 0;

            var root = _clonedLayoutAsset != null ? _clonedLayoutAsset.Root : null;
            if (root == null) return;
            if (InspectorSearchIndex.Tokenize(_hierarchySearchQuery).Length == 0) return;

            _hierSearchStates = ResolveHierarchySearchStates(
                root, _hierarchySearchQuery, out _hierSearchPinnedOpen, out _hierSearchTotalRows);
        }

        /// <summary>
        /// Reports the pass that just rendered: the magnifier's enabled state, the compact count, and the gold
        /// "a filter is hiding rows" frame. Called after <see cref="RefreshHierarchy"/>'s PreserveScroll block,
        /// which covers every one of its exits (they return out of the lambda, and PreserveScroll invokes it
        /// synchronously).
        /// </summary>
        private void EndHierarchySearchPass()
        {
            if (_hierSearchToggle == null) return; // header not built yet

            bool hasContent = HierarchyHasSearchableContent();
            _hierSearchToggle.SetEnabled(hasContent);
            ApplyHierSearchChrome(hasContent && _hierarchySearchOpen);

            if (_hierSearchStates != null)
            {
                SetHierSearchCount(_hierSearchVisibleRows, _hierSearchTotalRows, show: true);
                // Unmistakable "a filter is hiding rows" cue, on the scroll VIEWPORT so it frames the panel
                // rather than the (taller/shorter) tree. Brand gold — the same signal as the other two.
                SearchActiveOverlay.Show(_hierarchyContainer, DesignerPalette.BrandGold);
            }
            else
            {
                SetHierSearchCount(0, 0, show: false);
                SearchActiveOverlay.Hide(_hierarchyContainer);
            }
        }

        private void SetHierSearchCount(int visible, int total, bool show)
        {
            if (_hierSearchCount == null) return;
            if (show)
            {
                _hierSearchCount.text = $"{visible}/{total}";
                _hierSearchCount.style.display = DisplayStyle.Flex;
            }
            else
            {
                _hierSearchCount.text = "";
                _hierSearchCount.style.display = DisplayStyle.None;
            }
        }

        private bool HierarchyHasSearchableContent()
        {
            return _clonedLayoutAsset != null && _clonedLayoutAsset.Root != null;
        }

        /// <summary>
        /// Loading a different type replaces the layout entirely, so a query aimed at the old one is
        /// meaningless. Clears it (the bar stays open) — mirrors the Canvas and Properties searches.
        /// </summary>
        private void ResetHierarchySearchForNewLayout()
        {
            _hierarchySearchQuery = "";
            _hierSearchStates = null;
            _hierSearchPinnedOpen = null;
            _hierSearchField?.SetValueWithoutNotify("");
        }

        // ---- Classification (static: needs no window instance) --------------------------------------------

        /// <summary>
        /// Classifies every node in <paramref name="root"/>'s tree against <paramref name="query"/> and returns
        /// the map <see cref="RenderTree"/> renders from.
        ///
        /// The passes themselves are <see cref="InspectorSearchIndex.Resolve"/>'s — the entries are built here
        /// (from the node MODEL, since the hierarchy has no built element tree to walk) but resolved there, so
        /// "matching a container reveals its contents" and "a match keeps its parent trail" mean exactly what
        /// they mean in the inspector and on the canvas.
        /// </summary>
        /// <param name="pinnedOpen">Every strict ancestor of a match — the rows the filter holds open so the
        /// match it found is actually on screen. Collapsed state itself is never written (see RenderTree).</param>
        /// <param name="total">How many rows the unfiltered tree would show, excluding the always-present root.</param>
        internal static Dictionary<LayoutNode, SearchEntryState> ResolveHierarchySearchStates(
            LayoutNode root, string query, out HashSet<LayoutNode> pinnedOpen, out int total)
        {
            pinnedOpen = new HashSet<LayoutNode>();
            total = 0;
            var states = new Dictionary<LayoutNode, SearchEntryState>();
            if (root == null) return states;

            var index = new InspectorSearchIndex();
            var parents = new Dictionary<LayoutNode, LayoutNode>();

            void Walk(LayoutNode node, ContainerNode modelParent, SearchEntry parentEntry)
            {
                if (node == null) return;
                var entry = new SearchEntry
                {
                    Node = node,
                    Parent = parentEntry,
                    Haystack = BuildHierarchyHaystack(node, modelParent),
                    IsContainer = node is ContainerNode,
                };
                index.Entries.Add(entry);
                if (parentEntry != null) parents[node] = parentEntry.Node;

                if (node is ContainerNode container && container.Children != null)
                {
                    foreach (var child in container.Children) Walk(child, container, entry);
                }
                else if (node is CollectionNode collection && collection.ItemLayout != null)
                {
                    // The item template renders as a child row, so it searches as one. Its model parent is
                    // null (it isn't in any Children list) — matching how RenderTree passes it through.
                    Walk(collection.ItemLayout, null, entry);
                }
                else if (node is BindingNode dictOwner)
                {
                    // A dictionary's Key / Value cell templates. Same rule as the item template above: they
                    // render as child rows, so they search as child rows, with a null model parent.
                    if (dictOwner.DictionaryKeyTemplate != null) Walk(dictOwner.DictionaryKeyTemplate, null, entry);
                    if (dictOwner.DictionaryValueTemplate != null) Walk(dictOwner.DictionaryValueTemplate, null, entry);
                }
            }

            Walk(root, null, null);
            index.Resolve(query);

            foreach (var entry in index.Entries)
            {
                states[entry.Node] = entry.State;
                if (entry.Node != root) total++;
                if (entry.State != SearchEntryState.Match) continue;
                // Hold the whole trail open. Note this is driven by MATCH, not by AncestorOfMatch: a container
                // that matches on its own title AND holds a match inside it is state Match, and gating on
                // AncestorOfMatch would leave that one shut over the very row the query found.
                for (var a = entry.Node; parents.TryGetValue(a, out var p); a = p) pinnedOpen.Add(p);
            }

            return states;
        }

        /// <summary>True when <paramref name="node"/> survives the active filter (always, when none is running).</summary>
        private bool RendersUnderHierarchySearch(LayoutNode node)
        {
            if (_hierSearchStates == null) return true;
            if (node == null) return false;
            return _hierSearchStates.TryGetValue(node, out var state) && state != SearchEntryState.Hidden;
        }

        /// <summary>
        /// The text a hierarchy row displays for <paramref name="node"/> — "[Binding] Move Speed", "Tab:
        /// Combat", "[Divider] Loadout", "Pane B".
        ///
        /// Lifted out of <see cref="RenderTree"/> so the search builds its haystack from exactly what the row
        /// SHOWS. A second copy of this chain would drift the first time a label gained a prefix, and the
        /// failure would be silent and specific: a row plainly reading "Tab: Combat" that "Combat" can't find.
        ///
        /// Every name appended to a kind tag is USER CONTENT of unbounded length, so it goes through
        /// <see cref="Snip"/> — see the note there for why, and why <paramref name="snip"/> exists.
        /// </summary>
        private static string BuildHierarchyRowLabel(LayoutNode node, ContainerNode parent, bool snip = true)
        {
            if (node is ContainerNode cn)
            {
                // Cosmetic hierarchy name: an explicit rename (F2 / right-click) wins, else the
                // container inherits its Title. Display-only — never touches Title or the canvas.
                string cosmetic = !string.IsNullOrEmpty(cn.HierarchyName) ? Snip(cn.HierarchyName, snip)
                                : !string.IsNullOrEmpty(cn.Title) ? Snip(cn.Title, snip) : null;

                if (cn.Type == ContainerType.Tabs)
                {
                    // The Tabs container itself — distinct from a generic container so it's findable.
                    return cosmetic != null ? $"[Tabs] {cosmetic}" : "[Tabs]";
                }
                if (parent != null && parent.Type == ContainerType.Tabs)
                {
                    // This container IS a tab — show its tab name so tabs are tellable apart (which is
                    // what makes the existing hierarchy drag actually usable for cross-tab moves).
                    string tabName = !string.IsNullOrEmpty(cn.HierarchyName) ? Snip(cn.HierarchyName, snip)
                                   : !string.IsNullOrEmpty(cn.TabLabel) ? Snip(cn.TabLabel, snip)
                                   : !string.IsNullOrEmpty(cn.Title) ? Snip(cn.Title, snip)
                                   : $"Tab {parent.Children.IndexOf(cn) + 1}";
                    return $"Tab: {tabName}";
                }
                if (cn.Type == ContainerType.Frame)
                {
                    return cosmetic != null ? $"[Frame] {cosmetic}" : "[Frame]";
                }
                if (parent != null && parent.Type == ContainerType.Frame
                    && parent.Frame.Shape == FrameShape.DiagonalSplit)
                {
                    // This container IS a split-frame pane; identity is positional (0 = A, 1 = B).
                    string pane = parent.Children.IndexOf(cn) == 1 ? "Pane B" : "Pane A";
                    return cosmetic != null ? $"{pane}: {cosmetic}" : pane;
                }

                // The style-token suffix is optional, so build it only when there IS one — interpolating an
                // empty string left the row reading "[Container] " with a trailing space.
                string label = cosmetic != null
                    ? $"[Container] {cosmetic}"
                    : (!string.IsNullOrEmpty(cn.StyleToken) ? $"[Container] .{Snip(cn.StyleToken, snip)}" : "[Container]");
                if (!string.IsNullOrEmpty(cn.PropertyPath)) label += $" (Scope: {Snip(cn.PropertyPath, snip)})";
                return label;
            }

            switch (node)
            {
                case BindingNode bn:      return $"[Binding] {Snip(GetNodeDisplayName(bn), snip)}";
                case CollectionNode col:  return $"[Collection] {Snip(GetNodeDisplayName(col), snip)}";
                case PropertyNode pnode:  return $"[Property] {Snip(GetNodeDisplayName(pnode), snip)}";
                case ButtonNode btn:      return $"[Button] {Snip(GetNodeDisplayName(btn), snip)}";
                case HelpBoxNode hb:      return $"[Info/Warning] {Snip(GetNodeDisplayName(hb), snip)}";
                // Missing this arm fell through to the "" at the bottom, so a Text Block's hierarchy row had
                // NO name at all — unreadable and unsearchable. Its twin lives in DragGhost.KindLabel.
                // The kind tag is the palette's own words ("Text Block"), not a clipped "[Text]": a block whose
                // text is still empty has nothing else to show, and a row that only reads "[Text]" beside a
                // paragraph icon does not tell you which widget you dropped.
                case TextBlockNode tb:    return string.IsNullOrEmpty(tb.Text)
                                              ? "[Text Block]" : $"[Text Block] {Snip(tb.Text, snip)}";
                case ScriptHeaderNode _:  return "[Script Header]";
                case SpaceNode sp:        return sp.IsAttributeOwned ? "[Space] (from code)" : "[Space]";
                case LabeledDividerNode ld: return string.IsNullOrEmpty(ld.Title) ? "[Divider]" : $"[Divider] {Snip(ld.Title, snip)}";
                case ImageBannerNode _:   return "[Image]";
                case EmbeddedEditorNode _: return "[Embedded Editor]";
                // A held-open cell is skipped by RenderTree (it is plumbing, not a widget), so this arm is for
                // the other readers of this method — the search haystack and the tests that enumerate kinds.
                case EmptyCellNode _:     return "[Empty Cell]";
#if SASQ_CUSTOM_WIDGET
                case CustomWidgetNode _:  return "[Custom Widget]";
#endif
            }
            // Never "" — a row with no text is a blank line the user can neither read nor search for, and the
            // ONLY way to reach it is a node kind nobody added an arm for. Falling back to the prettified type
            // name means a new kind is merely under-described here instead of invisible.
            return $"[{ObjectNames.NicifyVariableName(StripNodeSuffix(node.GetType().Name))}]";
        }

        /// <summary>"TextBlockNode" → "TextBlock". The suffix every layout-node type carries adds nothing to a
        /// name the user reads.</summary>
        private static string StripNodeSuffix(string typeName) =>
            typeName.EndsWith("Node") && typeName.Length > 4
                ? typeName.Substring(0, typeName.Length - 4)
                : typeName;

        /// <summary>
        /// How much of an appended name a hierarchy row shows before it clips.
        ///
        /// A row is ONE line in a narrow panel, and every name appended to a kind tag is user content of
        /// unbounded length — a Text Block's body is a paragraph, a Help Box's message is a sentence, an F2
        /// rename or a collection's title override can be anything. Appended whole, a long one pushes its own
        /// kind tag out of view and the row stops identifying itself, which is the opposite of what the tag is
        /// for. Sized so the longest tag ("[Info/Warning] ") plus a snippet still reads at the panel's default
        /// width.
        /// </summary>
        internal const int HierarchyNameMaxChars = 28;

        /// <summary>
        /// Flattens a name onto one line and clips it to <see cref="HierarchyNameMaxChars"/>.
        ///
        /// <paramref name="snip"/> is false for exactly one caller — the search haystack. The hierarchy row is
        /// the ONLY place some of these strings appear at all (an F2 cosmetic rename, a tab name, a collection's
        /// title override, a container's scope path are none of them in
        /// <see cref="InspectorSearchIndex.BuildHaystack(LayoutNode, bool)"/>), so indexing the clipped label
        /// would quietly make the tail of every long name unfindable — a search bug caused by a display change,
        /// which is the hardest kind to trace back.
        /// </summary>
        private static string Snip(string text, bool snip = true)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string flat = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (!snip || flat.Length <= HierarchyNameMaxChars) return flat;
            return flat.Substring(0, HierarchyNameMaxChars).TrimEnd() + "…";
        }

        /// <summary>
        /// The searchable text for a hierarchy row: what the row SAYS plus what the node IS.
        ///
        /// The row's own label is the first half because the hierarchy shows things nothing else does — an F2
        /// cosmetic rename, a tab's name, "Pane A" — and a user searching the tree is searching for the words
        /// in front of them. The second half is the shipped <see cref="InspectorSearchIndex.BuildHaystack"/>
        /// with type names on, which adds the raw member name behind a label override (so <c>moveSpeed</c> is
        /// findable when the row reads "Speed") and the widget-kind vocabulary — "divider" also answering to
        /// "separator", "space" to "gap" — exactly as the Canvas search does.
        /// </summary>
        private static string BuildHierarchyHaystack(LayoutNode node, ContainerNode modelParent)
        {
            var sb = new StringBuilder();
            // BuildPropHaystack is the shared "lowercase + camelCase-split" helper (named for the panel that
            // needed it first); the row label is free text, so it gets the same treatment as any other label.
            // snip: false — the DISPLAYED row clips long names at HierarchyNameMaxChars, but the index must
            // hold them whole or a query for a word past the clip point silently matches nothing (see Snip).
            sb.Append(BuildPropHaystack(BuildHierarchyRowLabel(node, modelParent, snip: false)));
            var nodeText = InspectorSearchIndex.BuildHaystack(node, includeTypeNames: true);
            if (!string.IsNullOrEmpty(nodeText))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(nodeText);
            }
            return sb.ToString();
        }

        // ---- Test seams (internal → visible to SasquatchInspector.Tests.Editor via InternalsVisibleTo) -----

        /// <summary>Test seam: how many rows the tree is currently showing (root excluded), and of how many.</summary>
        internal int DebugHierSearchVisibleRows => _hierSearchVisibleRows;
        internal int DebugHierSearchTotalRows => _hierSearchTotalRows;

        /// <summary>Test seam: true while a hierarchy query is actually filtering the tree.</summary>
        internal bool DebugHierSearchFiltering => _hierSearchStates != null;
    }
}
