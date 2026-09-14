using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The UnityEvent card's "Select Function" popup — a SECTIONED, collapsible, searchable picker.
    ///
    /// Replaces a flat list of "Group/Member (arg)" strings that ran to several hundred visually-identical
    /// rows on any real GameObject. Functions are grouped by the object that owns them; the user's own scripts
    /// get their own section at the top, expanded, bold and accented, while the GameObject and the built-in
    /// components sit below, collapsed. The default expand state is the point of the whole thing: what you
    /// reach for 99% of the time is on screen, and Unity's surface is one click — not one long scroll — away.
    ///
    /// Deliberately NOT an extension of <see cref="SearchableDropdownPopup"/>. That class has six other callers
    /// (icon pickers, the script picker, the polymorphic type picker, the dropdown widgets) and none of them
    /// want sections, foldouts, per-row secondary text, or header-skipping keyboard nav. It is borrowed from
    /// structurally — rows, highlight, single trickle-down key handler, entrance motion — not extended.
    ///
    /// Styled inline rather than through USS, matching its sibling popups: a popup owns its whole visual and
    /// the export kit carries a package-free twin that cannot reach the package stylesheet anyway.
    /// </summary>
    internal sealed class UnityEventMethodPicker : PopupWindowContent
    {
        // Sentinel selection: "No Function" clears the listener rather than pointing it at an entry.
        internal const int NoFunctionIndex = -1;

        private const float RowHeight = 19f;
        private const float HeaderHeight = 20f;
        private const float MinHeight = 220f;
        private const float MaxHeight = 460f;
        private const float MinWidth = 360f;

        private const string SessionPrefix = "sasq.uepick.";

        // A row can hold more than one Label (the No Function row leads with a ✕ glyph, member rows trail with
        // a muted argument type), so the one that carries the row's NAME is tagged. A bare Q<Label>() would pick
        // the first child, recolouring the glyph and leaving the text alone.
        private const string RowTextClass = "sasq-uepick-rowtext";

        // ---- model -----------------------------------------------------------------------------------

        private sealed class Group
        {
            public UnityEventReflection.GroupKind Kind;
            public int GroupIndex;
            public bool Dynamic;
            public string Name;
            public UnityEngine.Object Owner;
            public Type OwnerType;

            public readonly List<int> Declared = new List<int>();
            public readonly List<int> Inherited = new List<int>();
            public readonly List<VisualElement> DeclaredRows = new List<VisualElement>();
            public readonly List<VisualElement> InheritedRows = new List<VisualElement>();

            public VisualElement Root;
            public Label Chevron;
            public VisualElement Content;
            public VisualElement InheritedRoot;
            public Label InheritedChevron;
            public VisualElement InheritedContent;

            public bool Expanded;
            public bool InheritedExpanded;
            public bool VisibleNow = true;

            // Session key: per owning TYPE, so collapsing "Transform" stays collapsed for the next card too.
            public string Key => (Dynamic ? "dyn." : "grp.") + (OwnerType != null ? OwnerType.FullName : "?");
        }

        private sealed class Section
        {
            public string Title;
            public VisualElement Header;
            public readonly List<Group> Groups = new List<Group>();
        }

        /// <summary>What a focusable HEADER row points at. Rows carry either an int (a selectable function,
        /// or <see cref="NoFunctionIndex"/>) or one of these.</summary>
        private sealed class HeaderRef
        {
            public Group Group;
            public bool Inherited;
        }

        // ---- state -----------------------------------------------------------------------------------

        private readonly List<UnityEventReflection.MethodEntry> _entries;
        private readonly int _currentIndex;
        private readonly float _width;
        private readonly string _dynamicLabel;
        private readonly Action<int> _onSelect;

        private readonly List<Group> _groups = new List<Group>();
        private readonly List<Section> _sections = new List<Section>();
        // Every NAVIGABLE row in visual order — function rows AND group/inherited headers. Section rules are
        // not in here: they are decorative, and the rule is that anything you can land on is actionable.
        private readonly List<VisualElement> _rows = new List<VisualElement>();

        private TextField _searchField;
        private Label _countLabel;
        private ScrollView _scroll;
        private VisualElement _noFunctionRow;
        private VisualElement _highlighted;
        private string[] _tokens = Array.Empty<string>();
        private bool _closing;

        // ---- code peek -------------------------------------------------------------------------------
        private readonly bool _codePeek;         // the layout's Code Peek option — off means never build one
        private readonly VisualElement _anchor;  // the function button; how the peek finds the inspector's panel
        private PopupCodePeek _peek;             // shows a source card for the selected/hovered function

        // Holds the inspector behind us still while we're open, the way a native menu would.
        private readonly PopupScrollLock _scrollLock = new PopupScrollLock();

        /// <summary>
        /// Opens the picker under <paramref name="anchor"/>. <paramref name="currentIndex"/> is the entry the
        /// listener currently points at (or <see cref="NoFunctionIndex"/>); it is highlighted, scrolled to, and
        /// its group force-expanded, so reopening a configured listener lands ON its function.
        /// <paramref name="codePeek"/> arms the hover source preview.
        /// </summary>
        internal static void Show(Button anchor, List<UnityEventReflection.MethodEntry> entries,
            int currentIndex, string dynamicLabel, Action<int> onSelect, bool codePeek = true)
        {
            if (anchor == null) return;
            float width = Mathf.Max(MinWidth, anchor.worldBound.width);
            UnityEditor.PopupWindow.Show(anchor.worldBound,
                new UnityEventMethodPicker(entries, currentIndex, width, dynamicLabel, onSelect, codePeek, anchor));
        }

        private UnityEventMethodPicker(List<UnityEventReflection.MethodEntry> entries, int currentIndex,
            float width, string dynamicLabel, Action<int> onSelect, bool codePeek, VisualElement anchor = null)
        {
            _entries = entries ?? new List<UnityEventReflection.MethodEntry>();
            _currentIndex = currentIndex;
            _width = width;
            _dynamicLabel = dynamicLabel;
            _onSelect = onSelect;
            _codePeek = codePeek;
            _anchor = anchor;

            // The model is built HERE, not in OnGUI, because GetWindowSize() runs before the tree exists and
            // has to measure the default-expanded row count from something.
            BuildModel();
        }

        // ---- model assembly --------------------------------------------------------------------------

        private void BuildModel()
        {
            // GetMethodCandidates already returns picker order (Kind, then GroupIndex), so find-or-create in
            // iteration order yields groups in the right order within each section for free.
            var byKey = new Dictionary<long, Group>();

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                // Dynamic entries live in their own section, so a dynamic and a static call on the same object
                // must not land in the same group.
                long key = ((long)e.GroupIndex << 1) | (e.IsDynamic ? 1L : 0L);
                if (!byKey.TryGetValue(key, out var g))
                {
                    g = new Group
                    {
                        Kind = e.Kind,
                        GroupIndex = e.GroupIndex,
                        Dynamic = e.IsDynamic,
                        Name = e.Group,
                        Owner = e.Target,
                        OwnerType = e.Target != null ? e.Target.GetType() : null,
                    };
                    byKey[key] = g;
                    _groups.Add(g);
                }

                // Nothing folds inside the Dynamic section — it is already short, and a dynamic match on an
                // inherited Unity method is the rare case that would be hidden for no gain.
                if (e.IsInherited && !e.IsDynamic) g.Inherited.Add(i);
                else g.Declared.Add(i);
            }

            foreach (var g in _groups)
            {
                g.Expanded = SessionState.GetBool(SessionPrefix + g.Key, g.Dynamic || g.Kind == UnityEventReflection.GroupKind.ProjectScript);
                g.InheritedExpanded = SessionState.GetBool(SessionPrefix + "inh." + g.Key, false);
            }

            // The assigned function must be reachable the moment the popup opens.
            if (_currentIndex >= 0 && _currentIndex < _entries.Count)
            {
                foreach (var g in _groups)
                {
                    if (g.Declared.Contains(_currentIndex)) { g.Expanded = true; break; }
                    if (g.Inherited.Contains(_currentIndex)) { g.Expanded = true; g.InheritedExpanded = true; break; }
                }
            }

            AddSection("Dynamic" + (string.IsNullOrEmpty(_dynamicLabel) ? "" : "  " + _dynamicLabel),
                g => g.Dynamic);
            AddSection("Your Scripts",
                g => !g.Dynamic && g.Kind == UnityEventReflection.GroupKind.ProjectScript);
            AddSection("This GameObject",
                g => !g.Dynamic && g.Kind == UnityEventReflection.GroupKind.GameObject);
            AddSection("Components",
                g => !g.Dynamic && g.Kind == UnityEventReflection.GroupKind.Component);
        }

        private void AddSection(string title, Func<Group, bool> predicate)
        {
            var section = new Section { Title = title };
            foreach (var g in _groups)
                if (predicate(g)) section.Groups.Add(g);
            if (section.Groups.Count > 0) _sections.Add(section);
        }

        /// <inheritdoc/>
        public override Vector2 GetWindowSize()
        {
            // Measured from the MODEL — the tree does not exist yet when Unity asks.
            float rows = 1f;                                  // "No Function"
            float headers = 0f;
            foreach (var s in _sections)
            {
                headers += 1f;                                // section rule
                foreach (var g in s.Groups)
                {
                    headers += 1f;                            // group header
                    if (!g.Expanded) continue;
                    rows += g.Declared.Count;
                    if (g.Inherited.Count > 0)
                    {
                        headers += 1f;                        // the collapsed "Inherited from …" row
                        if (g.InheritedExpanded) rows += g.Inherited.Count;
                    }
                }
            }

            float h = 12f + 18f + 26f + rows * RowHeight + headers * HeaderHeight + 8f;
            return new Vector2(MeasuredWidth(), Mathf.Clamp(h, MinHeight, MaxHeight));
        }

        /// <summary>
        /// Wide enough for its own content. A popup cannot resize once open, so if the list needs more room
        /// than the button that opened it, the answer is to OPEN wider — never to scroll sideways.
        ///
        /// Measures every row, including the ones inside collapsed groups: expanding a group must not be able
        /// to introduce an overflow the window can no longer grow to fix. The estimate is character-count
        /// based (there is no tree yet to measure against) and generous; anything past
        /// <see cref="PopupListChrome.MaxAutoWidth"/> ellipsizes instead.
        /// </summary>
        private float MeasuredWidth()
        {
            float widest = 0f;
            foreach (var s in _sections)
            {
                foreach (var g in s.Groups)
                {
                    // Group header: 4 pad + 12 chevron + 18 icon + name + 6 gap + count + 6 pad.
                    widest = Mathf.Max(widest, 46f
                        + PopupListChrome.EstimateTextWidth(g.Name, 11f)
                        + PopupListChrome.EstimateTextWidth($"({g.Declared.Count + g.Inherited.Count})", 10f));

                    foreach (int i in g.Declared) widest = Mathf.Max(widest, EntryRowWidth(i, 22f));

                    if (g.Inherited.Count > 0)
                    {
                        // Inherited header: 22 indent + 12 chevron + label + count + 6 pad.
                        widest = Mathf.Max(widest, 40f
                            + PopupListChrome.EstimateTextWidth($"Inherited from {NearestUnityBaseName(g.OwnerType)}", 10f)
                            + PopupListChrome.EstimateTextWidth($"({g.Inherited.Count})", 10f));
                        foreach (int i in g.Inherited) widest = Mathf.Max(widest, EntryRowWidth(i, 34f));
                    }
                }
            }

            widest += PopupListChrome.ScrollbarAllowance;
            return Mathf.Clamp(Mathf.Max(_width, widest), _width, Mathf.Max(_width, PopupListChrome.MaxAutoWidth));
        }

        // A member row: indent + the member name + the muted argument type, right-aligned after an 8px gap.
        private float EntryRowWidth(int entryIndex, float indent)
        {
            if (entryIndex < 0 || entryIndex >= _entries.Count) return 0f;
            var e = _entries[entryIndex];
            float w = indent + 6f + PopupListChrome.EstimateTextWidth(e.MemberDisplay, 11f);
            if (!string.IsNullOrEmpty(e.ArgLabel)) w += 8f + PopupListChrome.EstimateTextWidth(e.ArgLabel, 10f);
            return w;
        }

        /// <inheritdoc/>
        public override void OnGUI(Rect rect)
        {
            var root = editorWindow.rootVisualElement;
            if (root.childCount == 0) BuildUI(root);
        }

        // ---- UI --------------------------------------------------------------------------------------

        private void BuildUI(VisualElement root)
        {
            root.style.backgroundColor = WidgetPalette.DropPopupBg;
            root.style.borderTopWidth = 1;
            root.style.borderBottomWidth = 1;
            root.style.borderLeftWidth = 1;
            root.style.borderRightWidth = 1;
            root.style.borderTopColor = WidgetPalette.DropPopupBorder;
            root.style.borderBottomColor = WidgetPalette.DropPopupBorder;
            root.style.borderLeftColor = WidgetPalette.DropPopupBorder;
            root.style.borderRightColor = WidgetPalette.DropPopupBorder;
            root.style.paddingTop = 6;
            root.style.paddingBottom = 6;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;

            var title = new Label("Select Function");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 11;
            title.style.color = WidgetPalette.DropPopupTitle;
            title.style.marginBottom = 4;
            root.Add(title);

            BuildSearchRow(root);

            // The peek prefers to hang below this popup in the INSPECTOR's panel, where it can be wider than the
            // popup. The list still gets its own area so the fallback placement — docked inside, when there's
            // no room in that panel — lands on the list's edges and never covers the title or the search row.
            var listArea = PopupCodePeek.CreateListArea();
            _peek = new PopupCodePeek(listArea, _anchor, PopupScreenRect);
            root.Add(listArea);

            _scroll = new ScrollView();
            _scroll.style.flexGrow = 1;
            PopupListChrome.SuppressHorizontalScrolling(_scroll);
            listArea.Add(_scroll);

            _noFunctionRow = BuildNoFunctionRow();
            _scroll.Add(_noFunctionRow);

            foreach (var section in _sections)
            {
                section.Header = BuildSectionHeader(section.Title);
                _scroll.Add(section.Header);
                foreach (var g in section.Groups)
                    _scroll.Add(BuildGroup(g));
            }

            if (_rows.Count <= 1)
            {
                var empty = new Label("No functions available on this target.");
                empty.style.fontSize = 11;
                empty.style.color = WidgetPalette.DropPopupTitle;
                empty.style.paddingLeft = 6;
                empty.style.paddingTop = 6;
                _scroll.Add(empty);
            }

            // Land on the assigned function (or the first row) and scroll it into view.
            var initial = FindRowForEntry(_currentIndex) ?? (_rows.Count > 0 ? _rows[0] : null);
            SetHighlight(initial, scroll: true);
            ScrollToAfterLayout(initial);

            if (EditorSpringFX.MotionEnabled)
            {
                EditorSpringFX.SlideInY(root, -6f);
                var lead = new List<VisualElement>();
                for (int i = 0; i < _rows.Count && i < 14; i++) lead.Add(_rows[i]);
                EditorSpringFX.StaggerIn(lead, startScale: 0.97f, gapSeconds: 0.018f, maxWindowSeconds: 0.24f);
            }

            // ONE trickle-down handler on the root — it is an ancestor of the search field, so it sees the
            // field's keys first. A second registration on the field would double-step the arrows.
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.focusable = true;

            _searchField.schedule.Execute(() => _searchField.Focus()).ExecuteLater(50);
        }

        private void BuildSearchRow(VisualElement root)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;

            var magnifier = SasquatchIcons.Create(SasquatchIcons.Ids.Search, 12f, WidgetPalette.DropPopupTitle);
            magnifier.style.flexShrink = 0;
            magnifier.style.marginRight = 4;
            row.Add(magnifier);

            _searchField = new TextField();
            _searchField.style.flexGrow = 1;
            _searchField.style.marginLeft = 0;
            _searchField.style.marginRight = 0;
            var input = _searchField.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = WidgetPalette.DropPopupInputBg;
                input.style.borderTopLeftRadius = 3;
                input.style.borderTopRightRadius = 3;
                input.style.borderBottomLeftRadius = 3;
                input.style.borderBottomRightRadius = 3;
                input.style.paddingLeft = 4;
            }
            _searchField.RegisterValueChangedCallback(evt => ApplyFilter(evt.newValue));
            row.Add(_searchField);

            // House rule across every Sasquatch search surface: the count is visible exactly when a query is.
            _countLabel = new Label(string.Empty);
            _countLabel.style.fontSize = 10;
            _countLabel.style.color = WidgetPalette.DropPopupTitle;
            _countLabel.style.marginLeft = 6;
            _countLabel.style.flexShrink = 0;
            _countLabel.style.display = DisplayStyle.None;
            row.Add(_countLabel);

            root.Add(row);
        }

        private VisualElement BuildNoFunctionRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight;
            row.style.flexShrink = 0;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = WidgetPalette.DropPopupSeparator;
            row.style.marginBottom = 2;

            var glyph = new Label("✕");
            glyph.style.fontSize = 9;
            glyph.style.width = 12;
            glyph.style.flexShrink = 0;
            glyph.style.color = WidgetPalette.DropPopupTitle;
            row.Add(glyph);

            var label = new Label("No Function");
            label.AddToClassList(RowTextClass);
            label.style.fontSize = 11;
            label.style.flexGrow = 1;
            PopupListChrome.ClipLongText(label);
            label.style.color = WidgetPalette.DropPopupItemText;
            row.Add(label);

            row.userData = NoFunctionIndex;
            row.RegisterCallback<MouseEnterEvent>(_ => SetHighlight(row, scroll: false));
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                Select(NoFunctionIndex);
                evt.StopPropagation();
            });
            _rows.Add(row);
            return row;
        }

        private VisualElement BuildSectionHeader(string titleText)
        {
            var el = new VisualElement();
            el.style.flexDirection = FlexDirection.Row;
            el.style.alignItems = Align.Center;
            el.style.height = HeaderHeight;
            el.style.flexShrink = 0;
            el.style.paddingLeft = 4;
            el.style.paddingRight = 4;

            var label = new Label(titleText.ToUpperInvariant());
            label.style.fontSize = 9;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = WidgetPalette.DropPopupTitle;
            label.style.marginRight = 6;
            label.style.flexShrink = 0;
            el.Add(label);

            var rule = new VisualElement();
            rule.style.flexGrow = 1;
            rule.style.height = 1;
            rule.style.backgroundColor = WidgetPalette.DropPopupSeparator;
            el.Add(rule);

            return el;
        }

        private VisualElement BuildGroup(Group g)
        {
            g.Root = new VisualElement();
            g.Root.style.flexShrink = 0;

            g.Root.Add(BuildGroupHeader(g));

            g.Content = new VisualElement();
            g.Content.style.flexShrink = 0;
            g.Root.Add(g.Content);

            foreach (int idx in g.Declared)
            {
                var row = BuildMemberRow(idx, indent: 22f);
                g.DeclaredRows.Add(row);
                g.Content.Add(row);
            }

            if (g.Inherited.Count > 0)
            {
                g.InheritedRoot = new VisualElement();
                g.InheritedRoot.style.flexShrink = 0;
                g.InheritedRoot.Add(BuildInheritedHeader(g));

                g.InheritedContent = new VisualElement();
                g.InheritedContent.style.flexShrink = 0;
                g.InheritedRoot.Add(g.InheritedContent);

                foreach (int idx in g.Inherited)
                {
                    var row = BuildMemberRow(idx, indent: 34f);
                    g.InheritedRows.Add(row);
                    g.InheritedContent.Add(row);
                }
                g.Content.Add(g.InheritedRoot);
            }

            ApplyGroupChrome(g, g.Expanded);
            ApplyInheritedChrome(g, g.InheritedExpanded);
            return g.Root;
        }

        private VisualElement BuildGroupHeader(Group g)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = HeaderHeight;
            header.style.flexShrink = 0;
            header.style.paddingLeft = 4;
            header.style.paddingRight = 6;

            g.Chevron = new Label(g.Expanded ? "▾" : "▸");
            g.Chevron.style.fontSize = 9;
            g.Chevron.style.width = 12;
            g.Chevron.style.flexShrink = 0;
            g.Chevron.style.color = WidgetPalette.DropPopupTitle;
            header.Add(g.Chevron);

            var iconTexture = ResolveIcon(g);
            if (iconTexture != null)
            {
                var icon = new Image { image = iconTexture };
                icon.style.width = 14;
                icon.style.height = 14;
                icon.style.flexShrink = 0;
                icon.style.marginRight = 4;
                header.Add(icon);
            }

            var name = new Label(g.Name);
            name.AddToClassList(RowTextClass);
            name.style.fontSize = 11;
            name.style.flexGrow = 1;
            PopupListChrome.ClipLongText(name);
            // Your own scripts read louder than Unity's surface.
            bool mine = g.Kind == UnityEventReflection.GroupKind.ProjectScript;
            name.style.unityFontStyleAndWeight = mine ? FontStyle.Bold : FontStyle.Normal;
            name.userData = mine ? WidgetPalette.AccentIcon : WidgetPalette.DropPopupItemText; // resting colour
            name.style.color = (Color)name.userData;
            header.Add(name);

            var count = new Label($"({g.Declared.Count + g.Inherited.Count})");
            count.style.fontSize = 10;
            count.style.flexShrink = 0;
            count.style.marginLeft = 6;
            count.style.color = WidgetPalette.DropPopupTitle;
            header.Add(count);

            RegisterNavigableHeader(header, new HeaderRef { Group = g, Inherited = false });
            return header;
        }

        private VisualElement BuildInheritedHeader(Group g)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = HeaderHeight;
            header.style.flexShrink = 0;
            header.style.paddingLeft = 22;
            header.style.paddingRight = 6;

            g.InheritedChevron = new Label(g.InheritedExpanded ? "▾" : "▸");
            g.InheritedChevron.style.fontSize = 9;
            g.InheritedChevron.style.width = 12;
            g.InheritedChevron.style.flexShrink = 0;
            g.InheritedChevron.style.color = WidgetPalette.DropPopupTitle;
            header.Add(g.InheritedChevron);

            var label = new Label($"Inherited from {NearestUnityBaseName(g.OwnerType)}");
            label.AddToClassList(RowTextClass);
            label.style.fontSize = 10;
            label.style.flexGrow = 1;
            PopupListChrome.ClipLongText(label);
            label.userData = WidgetPalette.DropPopupTitle; // resting colour
            label.style.color = WidgetPalette.DropPopupTitle;
            label.tooltip = "Members this script inherits from Unity — kept, just folded out of the way.";
            header.Add(label);

            var count = new Label($"({g.Inherited.Count})");
            count.style.fontSize = 10;
            count.style.flexShrink = 0;
            count.style.color = WidgetPalette.DropPopupTitle;
            header.Add(count);

            RegisterNavigableHeader(header, new HeaderRef { Group = g, Inherited = true });
            return header;
        }

        /// <summary>
        /// Makes a header a first-class navigable row: hover and arrow-key focus share ONE highlight, Enter
        /// and click both toggle it. Section rules ("── YOUR SCRIPTS ──") deliberately do NOT get this — the
        /// rule is that everything you can land on is something you can act on, and a divider isn't.
        /// </summary>
        private void RegisterNavigableHeader(VisualElement header, HeaderRef reference)
        {
            header.userData = reference;
            header.RegisterCallback<MouseEnterEvent>(_ => SetHighlight(header, scroll: false));
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                ToggleHeader(reference);
                evt.StopPropagation();
            });
            _rows.Add(header);
            PaintRow(header, highlighted: false);
        }

        private void ToggleHeader(HeaderRef reference)
        {
            if (reference.Inherited) SetInheritedExpanded(reference.Group, !reference.Group.InheritedExpanded);
            else SetGroupExpanded(reference.Group, !reference.Group.Expanded);
        }

        private VisualElement BuildMemberRow(int entryIndex, float indent)
        {
            var e = _entries[entryIndex];

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight;
            row.style.flexShrink = 0;
            row.style.paddingLeft = indent;
            row.style.paddingRight = 6;

            var name = new Label(e.MemberDisplay);
            name.AddToClassList(RowTextClass);
            name.style.fontSize = 11;
            name.style.flexGrow = 1;
            PopupListChrome.ClipLongText(name);
            name.style.color = WidgetPalette.DropPopupItemText;
            row.Add(name);

            // The argument sits right-aligned and muted so the member names form a clean scannable column —
            // baking a "(bool)" suffix into the label instead would make every row a different length.
            if (!string.IsNullOrEmpty(e.ArgLabel))
            {
                var arg = new Label(e.ArgLabel);
                arg.style.fontSize = 10;
                arg.style.flexShrink = 0;
                arg.style.marginLeft = 8;
                // The colour carries meaning: AMBER = a value YOU supply (and it is the same amber the
                // designer tags binding types with), BLUE = the event supplies it.
                arg.style.color = e.IsDynamic ? WidgetPalette.AccentIcon : WidgetPalette.TypeAccent;
                if (e.IsDynamic) arg.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(arg);
            }

            row.userData = entryIndex;
            // Hover only moves the highlight; the peek hangs off that (SetHighlight), so the mouse and the
            // arrow keys drive it identically. No MouseLeave teardown either: leaving a row doesn't unselect
            // it, so retiring the card there would kill a peek the keyboard is still standing on.
            row.RegisterCallback<MouseEnterEvent>(_ => SetHighlight(row, scroll: false));
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                Select(entryIndex);
                evt.StopPropagation();
            });

            _rows.Add(row);
            PaintRow(row, highlighted: false);
            return row;
        }

        private static Texture ResolveIcon(Group g)
        {
            if (g.Owner == null) return null;
            try { return EditorGUIUtility.ObjectContent(g.Owner, g.OwnerType).image; }
            catch { return null; }   // a broken/missing script icon must never take the picker down
        }

        /// <summary>The nearest Unity base class of <paramref name="t"/> — names the fold row honestly.</summary>
        private static string NearestUnityBaseName(Type t)
        {
            for (var cur = t; cur != null; cur = cur.BaseType)
                if (UnityEventReflection.IsUnityDeclaredType(cur)) return cur.Name;
            return "Unity";
        }

        // ---- expansion -------------------------------------------------------------------------------

        private void SetGroupExpanded(Group g, bool expanded)
        {
            g.Expanded = expanded;
            SessionState.SetBool(SessionPrefix + g.Key, expanded);
            ApplyGroupChrome(g, expanded);
            EnsureHighlightVisible();
        }

        private void SetInheritedExpanded(Group g, bool expanded)
        {
            g.InheritedExpanded = expanded;
            SessionState.SetBool(SessionPrefix + "inh." + g.Key, expanded);
            ApplyInheritedChrome(g, expanded);
            EnsureHighlightVisible();
        }

        // Chrome only — never touches SessionState, so a search-driven force-expand cannot overwrite the
        // user's own collapse state when the query clears.
        private static void ApplyGroupChrome(Group g, bool expanded)
        {
            if (g.Chevron != null) g.Chevron.text = expanded ? "▾" : "▸";
            if (g.Content != null) g.Content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void ApplyInheritedChrome(Group g, bool expanded)
        {
            if (g.InheritedChevron != null) g.InheritedChevron.text = expanded ? "▾" : "▸";
            if (g.InheritedContent != null) g.InheritedContent.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- search ----------------------------------------------------------------------------------

        private void ApplyFilter(string query)
        {
            _tokens = InspectorSearchIndex.Tokenize(query);
            bool active = _tokens.Length > 0;
            int matched = 0, total = 0;

            foreach (var g in _groups)
            {
                int decl = FilterRows(g.Declared, g.DeclaredRows, active);
                int inh = FilterRows(g.Inherited, g.InheritedRows, active);
                matched += decl + inh;
                total += g.Declared.Count + g.Inherited.Count;

                g.VisibleNow = !active || decl + inh > 0;
                g.Root.style.display = g.VisibleNow ? DisplayStyle.Flex : DisplayStyle.None;

                if (g.InheritedRoot != null)
                    g.InheritedRoot.style.display = (!active || inh > 0) ? DisplayStyle.Flex : DisplayStyle.None;

                // A query force-expands anything holding a hit; clearing it restores what the user chose.
                ApplyGroupChrome(g, active ? g.VisibleNow : g.Expanded);
                ApplyInheritedChrome(g, active ? inh > 0 : g.InheritedExpanded);
            }

            // Sections keep their order while filtering — "your stuff first" is exactly what a search is for.
            foreach (var s in _sections)
            {
                bool any = false;
                foreach (var g in s.Groups) if (g.VisibleNow) { any = true; break; }
                s.Header.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _countLabel.text = active ? $"{matched}/{total}" : string.Empty;
            _countLabel.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;

            EnsureHighlightVisible();
        }

        private int FilterRows(List<int> indices, List<VisualElement> rows, bool active)
        {
            int visible = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                bool show = !active || InspectorSearchIndex.Matches(_entries[indices[i]].Haystack, _tokens);
                rows[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) visible++;
            }
            return visible;
        }

        // ---- highlight + keyboard --------------------------------------------------------------------

        private void PaintRow(VisualElement row, bool highlighted)
        {
            bool selected = row.userData is int i && i == _currentIndex && i != NoFunctionIndex;

            if (highlighted)
                row.style.backgroundColor = selected ? WidgetPalette.DropPopupSelectionStrong : WidgetPalette.DropPopupRowHover;
            else if (selected)
                row.style.backgroundColor = WidgetPalette.DropPopupSelection;
            else
                row.style.backgroundColor = Color.clear;

            var label = row.Q<Label>(className: RowTextClass);
            if (label == null) return;
            // Un-highlighting restores the row's OWN resting colour (stashed on the label), not a blanket
            // default — otherwise painting a project-script header back would strip its accent, and the
            // inherited fold row would lose its muted tone.
            label.style.color = (highlighted || selected)
                ? Color.white
                : (label.userData is Color resting ? resting : WidgetPalette.DropPopupItemText);
        }

        private void SetHighlight(VisualElement row, bool scroll)
        {
            if (_highlighted != null) PaintRow(_highlighted, false);
            _highlighted = row;
            // The peek follows the HIGHLIGHT, so arrowing onto a function previews it exactly as pointing at it
            // does — same dwell, same card. Landing on a header (userData is a HeaderRef, not an index) retires
            // the card, since a group name has no source.
            if (row?.userData is int entryIndex) SchedulePeek(entryIndex, row);
            else HidePeek();
            if (row == null) return;
            PaintRow(row, true);
            // ScrollTo needs real layout. The live popup builds into editorWindow.rootVisualElement, which is
            // always in a panel, so this only ever short-circuits for the detached tree the tests build.
            if (scroll && _scroll.panel != null) _scroll.ScrollTo(row);
        }

        /// <summary>
        /// Scrolls to <paramref name="row"/> once layout actually exists. The ScrollTo inside BuildUI is a
        /// NO-OP: at that moment the ScrollView has no resolved layout, so the scroll offset computes to
        /// nothing and reopening a configured listener never actually landed on its row. Re-applied across
        /// the settle window (the popup sizes itself, then the groups lay out), and abandoned the moment the
        /// user moves the highlight themselves.
        /// </summary>
        private void ScrollToAfterLayout(VisualElement row)
        {
            if (row == null) return;
            foreach (int delay in ScrollSettleMs)
            {
                _scroll.schedule.Execute(() =>
                {
                    if (_highlighted == row && row.panel != null) _scroll.ScrollTo(row);
                }).ExecuteLater(delay);
            }
        }

        private static readonly int[] ScrollSettleMs = { 1, 32, 96 };

        private bool IsRowVisible(VisualElement row)
        {
            for (var el = row; el != null && el != _scroll.contentContainer; el = el.parent)
                if (el.style.display.value == DisplayStyle.None) return false;
            return true;
        }

        private List<VisualElement> VisibleRows()
        {
            var list = new List<VisualElement>();
            foreach (var r in _rows)
                if (IsRowVisible(r)) list.Add(r);
            return list;
        }

        private void EnsureHighlightVisible()
        {
            if (_highlighted != null && IsRowVisible(_highlighted)) return;
            var visible = VisibleRows();
            SetHighlight(visible.Count > 0 ? visible[0] : null, scroll: true);
        }

        private VisualElement FindRowForEntry(int entryIndex)
        {
            if (entryIndex < 0) return null;
            foreach (var r in _rows)
                if (r.userData is int i && i == entryIndex) return r;
            return null;
        }

        private void MoveHighlight(int delta)
        {
            var visible = VisibleRows();
            if (visible.Count == 0) return;
            int current = _highlighted != null ? visible.IndexOf(_highlighted) : -1;
            int next = current + delta;
            if (next < 0) next = visible.Count - 1;
            if (next >= visible.Count) next = 0;
            SetHighlight(visible[next], scroll: true);
        }

        /// <summary>
        /// ←/→ act on the HEADER you are standing on, and only then. While the search box has text the arrows
        /// belong to the caret, and on a function row there is nothing to collapse — so in both cases the key
        /// is left unhandled rather than doing something invisible to a group you can't see you're "in".
        /// </summary>
        private bool SetHighlightedHeaderExpanded(bool expand)
        {
            if (!string.IsNullOrEmpty(_searchField.value)) return false;
            if (_highlighted?.userData is not HeaderRef reference) return false;

            bool current = reference.Inherited ? reference.Group.InheritedExpanded : reference.Group.Expanded;
            if (current == expand) return false;

            if (reference.Inherited) SetInheritedExpanded(reference.Group, expand);
            else SetGroupExpanded(reference.Group, expand);
            return true;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // NOTE: no HidePeek here. The arrows MOVE the highlight, and the peek follows it — SetHighlight
            // re-arms the card for the row you just landed on. It hangs below the popup rather than over the
            // list, so there is nothing for it to cover.
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveHighlight(1);
                    evt.StopPropagation();
                    break;

                case KeyCode.UpArrow:
                    MoveHighlight(-1);
                    evt.StopPropagation();
                    break;

                case KeyCode.RightArrow:
                    if (SetHighlightedHeaderExpanded(true)) evt.StopPropagation();
                    break;

                case KeyCode.LeftArrow:
                    if (SetHighlightedHeaderExpanded(false)) evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                    if (_highlighted?.userData is int idx)
                    {
                        Select(idx);
                        evt.StopPropagation();
                    }
                    else if (_highlighted?.userData is HeaderRef header)
                    {
                        ToggleHeader(header);   // Enter on a header opens/closes it
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.Escape:
                    // Dismissing retires the peek explicitly: OnClose would do it, but a picker built without
                    // a window (headless / under test) never gets one, and a queued card must not outlive it.
                    HidePeek();
                    if (editorWindow != null) editorWindow.Close();
                    evt.StopPropagation();
                    break;
            }
        }

        // ---- hover code peek -------------------------------------------------------------------------

        /// <summary>
        /// The source snippet for an entry, or null. Only PROJECT SCRIPTS have readable source — a built-in
        /// component resolves to no MonoScript, and a member a project script inherited from Unity lives in
        /// Unity's own assembly, so both are skipped rather than paying for a file lookup that cannot succeed.
        /// </summary>
        private string PeekSnippetFor(int entryIndex)
        {
            if (entryIndex < 0 || entryIndex >= _entries.Count) return null;
            var e = _entries[entryIndex];
            if (!e.IsProjectScript || e.IsInherited || e.DeclaringType == null) return null;

            try
            {
                // Property setters are stored as "set_Health" but declared as a property.
                return e.MethodName.StartsWith("set_", StringComparison.Ordinal)
                    ? MethodSourcePreview.GetPropertySnippet(e.DeclaringType, e.MethodName.Substring(4))
                    : MethodSourcePreview.GetSnippet(e.DeclaringType, e.MethodName);
            }
            catch { return null; }   // best-effort: an unreadable file just shows no peek
        }

        private void SchedulePeek(int entryIndex, VisualElement row)
        {
            if (_peek == null) return;
            if (!_codePeek) { _peek.Hide(); return; }
            _peek.Schedule(() => PeekSnippetFor(entryIndex), row);
        }

        private void HidePeek() => _peek?.Hide();

        /// <summary>This popup window's live screen rect — the edge an overlay peek hangs off. Empty before the
        /// window exists (headless / under test), which the peek reads as "no overlay, dock instead".</summary>
        private Rect PopupScreenRect() => editorWindow != null ? editorWindow.position : default(Rect);

        /// <inheritdoc/>
        public override void OnOpen() => _scrollLock.Engage(_anchor);

        /// <inheritdoc/>
        public override void OnClose()
        {
            _scrollLock.Release();
            HidePeek();
        }

        // ---- commit ----------------------------------------------------------------------------------

        private void Select(int entryIndex)
        {
            if (_closing) return;
            HidePeek();                 // stops a queued peek from firing into a window that is closing
            _onSelect?.Invoke(entryIndex);

            var win = editorWindow;
            if (win == null) return;
            if (!EditorSpringFX.MotionEnabled)
            {
                win.Close();
                return;
            }

            // Menu-style acknowledgement, matching the sibling popup: flash the chosen row, then close a beat
            // later, so the pick reads as registered instead of the popup just vanishing.
            _closing = true;
            var row = FindRowForEntry(entryIndex) ?? (entryIndex == NoFunctionIndex ? _noFunctionRow : null);
            if (row != null)
            {
                row.style.backgroundColor = WidgetPalette.DropPopupSelectionStrong;
                EditorSpringFX.PressBounce(row, 0.95f);
            }
            win.rootVisualElement.schedule.Execute(() =>
            {
                var w = editorWindow;
                if (w != null) w.Close();
            }).ExecuteLater(110);
        }

        // ---- test seams ------------------------------------------------------------------------------
        // The picker is a PopupWindowContent, so there is no headless way to open it for real. These build
        // the same tree into a detached root and expose what the UX actually promises: which groups open by
        // default, what a query does to them, and that keyboard nav skips headers.

        internal static UnityEventMethodPicker CreateForTests(
            List<UnityEventReflection.MethodEntry> entries, int currentIndex, string dynamicLabel,
            bool codePeek = true, VisualElement anchor = null)
            => new UnityEventMethodPicker(entries, currentIndex, MinWidth, dynamicLabel, null, codePeek, anchor);

        /// <summary>Builds the peek for an entry the way selecting it would, and returns the live card (null =
        /// no peek: the option is off, or the entry has no readable source).</summary>
        internal VisualElement DebugShowPeek(int entryIndex, VisualElement row)
        {
            if (!_codePeek || _peek == null) return null;
            _peek.Show(PeekSnippetFor(entryIndex), row);
            return _peek.Pane;
        }

        /// <summary>The list area a docked peek falls back into — its top or bottom edge, never the popup's
        /// chrome.</summary>
        internal VisualElement DebugPeekHost => _peek?.Host;

        /// <summary>Whether the current highlight has a peek waiting on its dwell timer.</summary>
        internal bool DebugPeekArmed => _peek != null && _peek.Armed;

        internal VisualElement BuildForTests()
        {
            var root = new VisualElement();
            BuildUI(root);
            return root;
        }

        internal void DebugSearch(string query) => ApplyFilter(query);

        /// <summary>The rich-text snippet the hover peek would show for an entry (null = no peek).</summary>
        internal string DebugPeekSnippet(int entryIndex) => PeekSnippetFor(entryIndex);

        internal void DebugMoveHighlight(int delta) => MoveHighlight(delta);

        /// <summary>Stands in for ←/→ on the currently highlighted row.</summary>
        internal bool DebugArrowExpand(bool expand) => SetHighlightedHeaderExpanded(expand);

        internal int DebugVisibleRowCount => VisibleRows().Count;

        internal List<string> DebugVisibleRowLabels()
        {
            var labels = new List<string>();
            foreach (var r in VisibleRows())
            {
                var l = r.Q<Label>(className: RowTextClass);
                if (l != null) labels.Add(l.text);
            }
            return labels;
        }

        internal bool DebugSectionVisible(string title)
        {
            foreach (var s in _sections)
                if (s.Title.StartsWith(title, StringComparison.Ordinal))
                    return s.Header != null && s.Header.style.display.value != DisplayStyle.None;
            return false;
        }

        internal bool DebugGroupExpanded(string groupName)
        {
            foreach (var g in _groups)
                if (!g.Dynamic && g.Name == groupName)
                    return g.Content != null && g.Content.style.display.value != DisplayStyle.None;
            return false;
        }

        internal bool DebugGroupVisible(string groupName)
        {
            foreach (var g in _groups)
                if (!g.Dynamic && g.Name == groupName)
                    return g.Root != null && g.Root.style.display.value != DisplayStyle.None;
            return false;
        }

        internal string DebugHighlightedLabel()
        {
            var l = _highlighted?.Q<Label>(className: RowTextClass);
            return l?.text;
        }

        /// <summary>Clears any session-persisted expand state so a test starts from the DEFAULT rule.</summary>
        internal static void DebugResetSessionState(List<UnityEventReflection.MethodEntry> entries)
        {
            foreach (var e in entries)
            {
                string type = e.Target != null ? e.Target.GetType().FullName : "?";
                foreach (string prefix in new[] { "grp.", "dyn." })
                {
                    SessionState.EraseBool(SessionPrefix + prefix + type);
                    SessionState.EraseBool(SessionPrefix + "inh." + prefix + type);
                }
            }
        }
    }
}
