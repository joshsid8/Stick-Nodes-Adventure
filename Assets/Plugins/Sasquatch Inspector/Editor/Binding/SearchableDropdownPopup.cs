using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>A searchable single-select dropdown popup (type-to-filter) used by the searchable dropdown widgets.</summary>
    public class SearchableDropdownPopup : PopupWindowContent
    {
        private readonly Runtime.BindingNode _node;
        private readonly string _title;
        private readonly List<string> _choices;
        private readonly string _currentValue;
        private readonly Action<string> _onSelect;
        private readonly float _width;
        private readonly Func<string, VisualElement> _itemIcon;
        private readonly Func<string, string> _codePeek;
        private readonly VisualElement _anchor;

        // Shows a source card for the selected/hovered choice — only built when _codePeek is given.
        private PopupCodePeek _peek;

        // Holds the window behind us still while we're open, the way a native menu would.
        private readonly PopupScrollLock _scrollLock = new PopupScrollLock();

        private TextField _searchField;
        private ScrollView _scrollView;
        private List<VisualElement> _optionElements;
        private VisualElement _highlightedElement;
        private bool _closing;

        /// <summary>Fired from <see cref="OnOpen"/>/<see cref="OnClose"/> — lets the owning field react to the
        /// popup's real lifecycle (e.g. the dropdown button's chevron spin), however the popup ends up closing.</summary>
        public Action Opened;
        /// <inheritdoc cref="Opened"/>
        public Action Closed;

        /// <inheritdoc/>
        public override void OnOpen()
        {
            _scrollLock.Engage(_anchor);
            Opened?.Invoke();
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            _scrollLock.Release();
            _peek?.Hide();   // stops a queued peek from firing into a window that is going away
            Closed?.Invoke();
        }

        /// <summary>Creates the dropdown popup for <paramref name="node"/> with the given choices and a selection
        /// callback. <paramref name="itemIcon"/>, when supplied, builds a small glyph rendered left of each
        /// choice's text (used by the button icon picker to preview the actual icons).
        /// <paramref name="codePeek"/>, when supplied, maps a choice to a highlighted source snippet and turns
        /// on the code peek — for the pickers whose choices ARE code (a script to design, a
        /// [SerializeReference] implementation). Choices that resolve to nothing simply show no card.
        /// <paramref name="anchor"/> is the control the popup opens from. It is what lets the popup hold its
        /// host window still while it's open (<see cref="PopupScrollLock"/>) and lets a code peek hang in that
        /// window's panel instead of being trapped inside the popup — pass it wherever you have it.</summary>
        public SearchableDropdownPopup(Runtime.BindingNode node, string title, List<string> choices, string currentValue, Action<string> onSelect, float width = 250f,
            Func<string, VisualElement> itemIcon = null, Func<string, string> codePeek = null, VisualElement anchor = null)
        {
            _node = node;
            _title = title;
            _choices = choices ?? new List<string>();
            _currentValue = currentValue;
            _onSelect = onSelect;
            _width = width;
            _itemIcon = itemIcon;
            _codePeek = codePeek;
            _anchor = anchor;
        }

        /// <inheritdoc/>
        public override Vector2 GetWindowSize()
        {
            return new Vector2(MeasuredWidth(), 280f);
        }

        /// <summary>
        /// Wide enough for its own choices. A popup cannot resize once open, so if the longest choice needs
        /// more room than the control that opened it, the answer is to OPEN wider — never to scroll sideways.
        /// The estimate is character-count based (there is no tree yet to measure against) and generous;
        /// anything past <see cref="PopupListChrome.MaxAutoWidth"/> ellipsizes instead.
        /// </summary>
        private float MeasuredWidth()
        {
            float widest = 0f;
            foreach (var choice in _choices)
                widest = Mathf.Max(widest, PopupListChrome.EstimateTextWidth(choice, 11f));

            // 12 row padding + 12 popup padding + 2 border, plus the glyph column when one is drawn.
            widest += 26f + (_itemIcon != null ? 20f : 0f) + PopupListChrome.ScrollbarAllowance;
            return Mathf.Clamp(Mathf.Max(_width, widest), _width, Mathf.Max(_width, PopupListChrome.MaxAutoWidth));
        }

        /// <inheritdoc/>
        public override void OnGUI(Rect rect)
        {
            var root = editorWindow.rootVisualElement;
            if (root.childCount == 0)
            {
                BuildUI(root);
            }
        }

        private void BuildUI(VisualElement root)
        {
            if (_node != null && _node.DropdownPopupBackground.isOverridden)
            {
                root.style.backgroundColor = _node.DropdownPopupBackground.value;
            }
            else
            {
                root.style.backgroundColor = WidgetPalette.DropPopupBg;
            }
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

            if (!string.IsNullOrEmpty(_title))
            {
                var titleLabel = new Label(_title);
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.fontSize = 11;
                titleLabel.style.color = WidgetPalette.DropPopupTitle;
                titleLabel.style.marginBottom = 4;
                root.Add(titleLabel);
            }

            bool searchable = _node == null
                || (_node.DisplayType == Runtime.DisplayType.SerializeReference
                        ? _node.PolymorphicSearchable
                        : _node.SearchableDropdown);

            _searchField = new TextField();
            _searchField.style.marginBottom = searchable ? 6 : 0;
            _searchField.style.display = searchable ? DisplayStyle.Flex : DisplayStyle.None;
            
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

            _searchField.RegisterValueChangedCallback(evt => FilterOptions(evt.newValue));
            // NOTE: keyboard nav is handled by ONE trickle-down registration on the popup root (added
            // at the end of BuildUI) — root is an ancestor of this field, so it sees the field's key
            // events first. A second registration here would make OnKeyDown run twice per keypress
            // (double-stepping the arrows).
            root.Add(_searchField);

            // With a peek armed the list moves into its own area, so a DOCKED card lands on the list's edges
            // and never covers the title or the search field. (The preferred placement is an overlay in the
            // host window's panel, which needs neither.) Without a peek the ScrollView stays where it was.
            if (_codePeek != null)
            {
                var listArea = PopupCodePeek.CreateListArea();
                _peek = new PopupCodePeek(listArea, _anchor, PopupScreenRect);
                root.Add(listArea);
                _scrollView = new ScrollView();
                _scrollView.style.flexGrow = 1;
                PopupListChrome.SuppressHorizontalScrolling(_scrollView);
                listArea.Add(_scrollView);
            }
            else
            {
                _scrollView = new ScrollView();
                _scrollView.style.flexGrow = 1;
                PopupListChrome.SuppressHorizontalScrolling(_scrollView);
                root.Add(_scrollView);
            }

            _optionElements = new List<VisualElement>();

            foreach (var choice in _choices)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = WidgetPalette.DropPopupRowHairline;
                row.style.borderTopLeftRadius = 2;
                row.style.borderTopRightRadius = 2;
                row.style.borderBottomLeftRadius = 2;
                row.style.borderBottomRightRadius = 2;

                bool isSelected = choice == _currentValue;
                if (isSelected)
                {
                    if (_node != null && _node.DropdownItemHoverBackground.isOverridden)
                    {
                        var c = _node.DropdownItemHoverBackground.value;
                        row.style.backgroundColor = new Color(c.r, c.g, c.b, c.a * 0.66f);
                    }
                    else
                    {
                        row.style.backgroundColor = WidgetPalette.DropPopupSelection;
                    }
                }

                // Hover and the keyboard share one highlight, and the peek follows the HIGHLIGHT (see
                // SetHighlight) — so arrowing onto a choice previews it exactly as pointing at it does.
                //
                // There is deliberately no MouseLeave teardown (the sibling UnityEvent picker has never had
                // one). Leaving a row doesn't UNSELECT it, and clearing the highlight there did two bad
                // things: a mouse twitch threw away where the keyboard had got to, and — now that the code
                // peek hangs BELOW the popup — moving the pointer down towards the card you are reading
                // retired the very card you were reaching for.
                row.RegisterCallback<MouseEnterEvent>(_ => SetHighlight(row));

                // Optional per-choice glyph (e.g. the icon picker shows each icon beside its id).
                if (_itemIcon != null)
                {
                    var glyph = _itemIcon(choice);
                    if (glyph != null)
                    {
                        glyph.style.marginRight = 6;
                        glyph.style.flexShrink = 0;
                        row.Add(glyph);
                    }
                }

                var label = new Label(choice);
                // Shrinkable + ellipsized: a choice longer than the popup must clip, never widen the content
                // box past the viewport (which is what grows a horizontal scrollbar).
                PopupListChrome.ClipLongText(label);
                if (_node != null && TextStyleApplier.HasAnyOverride(_node.DropdownItemTextStyle))
                {
                    TextStyleApplier.Apply(label, _node.DropdownItemTextStyle);
                }

                if (isSelected)
                {
                    if (_node != null && _node.DropdownItemHoverColor.isOverridden)
                    {
                        label.style.color = _node.DropdownItemHoverColor.value;
                    }
                    else
                    {
                        label.style.color = Color.white;
                    }
                }
                else
                {
                    if (_node != null && _node.DropdownItemTextStyle.color.isOverridden)
                    {
                        label.style.color = _node.DropdownItemTextStyle.color.value;
                    }
                    else
                    {
                        label.style.color = WidgetPalette.DropPopupItemText;
                    }
                }

                if (_node == null || !TextStyleApplier.IsUsableFontSize(_node.DropdownItemTextStyle.fontSize))
                {
                    label.style.fontSize = 11;
                }

                row.Add(label);

                row.userData = choice;
                row.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        SelectChoice(choice);
                        evt.StopPropagation();
                    }
                });

                _scrollView.Add(row);
                _optionElements.Add(row);
            }

            InitializeHighlight();

            // Entrance: the popup body slides/fades in and the first screenful of rows cascades after it —
            // the same house motion as the expandable group widgets. No-op when Reduce Motion is on.
            if (EditorSpringFX.MotionEnabled)
            {
                EditorSpringFX.SlideInY(root, -6f);
                var lead = new List<VisualElement>();
                for (int i = 0; i < _optionElements.Count && i < 14; i++) lead.Add(_optionElements[i]);
                EditorSpringFX.StaggerIn(lead, startScale: 0.97f, gapSeconds: 0.018f, maxWindowSeconds: 0.24f);
            }

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.focusable = true;

            if (searchable)
            {
                _searchField.schedule.Execute(() => _searchField.Focus()).ExecuteLater(50);
            }
            else
            {
                _scrollView.focusable = true;
                _scrollView.schedule.Execute(() => _scrollView.Focus()).ExecuteLater(50);
            }
        }

        /// <summary>The snippet for a choice, or null. Best-effort by design: a provider that can't resolve
        /// the choice — or throws reaching for the file — just shows no card.</summary>
        private string SafePeek(string choice)
        {
            try { return _codePeek != null ? _codePeek(choice) : null; }
            catch { return null; }
        }

        /// <summary>This popup window's live screen rect — the edge an overlay peek hangs off. Empty before the
        /// window exists (headless / under test), which the peek reads as "no overlay, dock instead".</summary>
        private Rect PopupScreenRect() => editorWindow != null ? editorWindow.position : default(Rect);

        /// <summary>Arms the peek for whatever is currently selected. Driven by the HIGHLIGHT rather than by
        /// the cursor, because these pickers are as usable from the arrow keys as from the mouse and a keyboard
        /// selection deserves the same preview — same dwell, same card, either way.</summary>
        private void ArmPeek(VisualElement target)
        {
            if (_peek == null) return;
            var choice = target?.userData as string;
            if (choice == null) { _peek.Hide(); return; }
            _peek.Schedule(() => SafePeek(choice), target);
        }

        // ---- test seams ------------------------------------------------------------------------------

        /// <summary>Builds the popup's tree into <paramref name="root"/> without opening a window.</summary>
        internal void BuildForTests(VisualElement root) => BuildUI(root);

        /// <summary>Shows the peek for a choice the way a hover would; null when the popup has none armed or
        /// the choice resolves to no source.</summary>
        internal VisualElement DebugShowPeek(string choice, VisualElement row)
        {
            if (_peek == null) return null;
            _peek.Show(SafePeek(choice), row);
            return _peek.Pane;
        }

        /// <summary>The list area a docked peek falls back into, or null when the popup has no peek.</summary>
        internal VisualElement DebugPeekHost => _peek?.Host;

        /// <summary>Whether the current highlight has a peek waiting on its dwell timer.</summary>
        internal bool DebugPeekArmed => _peek != null && _peek.Armed;

        /// <summary>Drives the popup's keyboard handler the way a real keypress would.</summary>
        internal void DebugKeyDown(KeyCode key)
        {
            using (var evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None))
                OnKeyDown(evt);
        }

        /// <summary>The row elements, in list order (a peek test needs one to select).</summary>
        internal List<VisualElement> DebugRows => _optionElements;

        /// <summary>Runs the search filter the way typing in the search box would. Drives the real
        /// FilterOptions rather than the TextField, because the value-changed callback needs a live panel
        /// and the filter itself does not.</summary>
        internal void DebugFilter(string query) => FilterOptions(query);

        /// <summary>The currently highlighted row's choice, or null.</summary>
        internal string DebugHighlightedChoice => _highlightedElement?.userData as string;

        private void SetHighlight(VisualElement target)
        {
            // Clear previous highlight styles
            foreach (var el in _optionElements)
            {
                bool isSelected = (el.userData as string) == _currentValue;
                if (isSelected)
                {
                    if (_node != null && _node.DropdownItemHoverBackground.isOverridden)
                    {
                        var c = _node.DropdownItemHoverBackground.value;
                        el.style.backgroundColor = new Color(c.r, c.g, c.b, c.a * 0.66f);
                    }
                    else
                    {
                        el.style.backgroundColor = WidgetPalette.DropPopupSelection;
                    }
                }
                else
                {
                    el.style.backgroundColor = Color.clear;
                }

                var label = el.Q<Label>();
                if (label != null)
                {
                    if (_node != null && TextStyleApplier.HasAnyOverride(_node.DropdownItemTextStyle))
                    {
                        TextStyleApplier.Apply(label, _node.DropdownItemTextStyle);
                    }

                    if (isSelected)
                    {
                        if (_node != null && _node.DropdownItemHoverColor.isOverridden)
                        {
                            label.style.color = _node.DropdownItemHoverColor.value;
                        }
                        else
                        {
                            label.style.color = Color.white;
                        }
                    }
                    else
                    {
                        if (_node != null && _node.DropdownItemTextStyle.color.isOverridden)
                        {
                            label.style.color = _node.DropdownItemTextStyle.color.value;
                        }
                        else
                        {
                            label.style.color = WidgetPalette.DropPopupItemText;
                        }
                    }
                }
            }

            // Apply new highlight styles
            if (target != null)
            {
                bool isSelected = (target.userData as string) == _currentValue;
                if (_node != null && _node.DropdownItemHoverBackground.isOverridden)
                {
                    target.style.backgroundColor = _node.DropdownItemHoverBackground.value;
                }
                else
                {
                    if (isSelected)
                    {
                        target.style.backgroundColor = WidgetPalette.DropPopupSelectionStrong;
                    }
                    else
                    {
                        target.style.backgroundColor = WidgetPalette.DropPopupRowHover;
                    }
                }

                var label = target.Q<Label>();
                if (label != null)
                {
                    if (_node != null && _node.DropdownItemHoverColor.isOverridden)
                    {
                        label.style.color = _node.DropdownItemHoverColor.value;
                    }
                    else
                    {
                        label.style.color = Color.white;
                    }
                }

                // PANEL-GUARDED. On Unity 6.6 ScrollTo throws NullReferenceException from inside Unity's own
                // ShouldDeferScrollTo() when the ScrollView is not attached to a panel; on 6.0 the same call
                // was a silent no-op. The real popup path is safe either way — OnGUI builds into
                // editorWindow.rootVisualElement, which is already attached — but scrolling to a row before
                // there is a viewport to scroll within is meaningless in every case, so the guard costs
                // nothing and it keeps the BuildForTests seam (which passes a detached root) honest rather
                // than needing a test exemption. See unity-66-is-stricter-about-panel-less-elements.
                if (_scrollView.panel != null) _scrollView.ScrollTo(target);
            }

            _highlightedElement = target;
            ArmPeek(target);
        }

        private List<VisualElement> GetVisibleOptions()
        {
            var list = new List<VisualElement>();
            foreach (var el in _optionElements)
            {
                if (el.style.display != DisplayStyle.None)
                {
                    list.Add(el);
                }
            }
            return list;
        }

        private void InitializeHighlight()
        {
            var visible = GetVisibleOptions();
            if (visible.Count == 0)
            {
                SetHighlight(null);
                return;
            }

            // Try to find the one matching _currentValue
            VisualElement toHighlight = null;
            foreach (var el in visible)
            {
                if ((el.userData as string) == _currentValue)
                {
                    toHighlight = el;
                    break;
                }
            }

            // Otherwise, highlight the first visible one
            if (toHighlight == null)
            {
                toHighlight = visible[0];
            }

            SetHighlight(toHighlight);
        }

        private void FilterOptions(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                foreach (var el in _optionElements)
                {
                    el.style.display = DisplayStyle.Flex;
                }
                InitializeHighlight();
                return;
            }

            var q = query.ToLowerInvariant();
            foreach (var el in _optionElements)
            {
                var choice = el.userData as string;
                if (choice != null && choice.ToLowerInvariant().Contains(q))
                {
                    el.style.display = DisplayStyle.Flex;
                }
                else
                {
                    el.style.display = DisplayStyle.None;
                }
            }
            InitializeHighlight();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // NOTE: no HidePeek here. The peek follows the highlight, and the arrows MOVE the highlight — so
            // SetHighlight re-arms it for the row you just landed on. It sits below the popup rather than over
            // the list, so there is nothing for it to cover.
            var visible = GetVisibleOptions();
            if (visible.Count == 0) return;

            int currentIndex = -1;
            if (_highlightedElement != null)
            {
                currentIndex = visible.IndexOf(_highlightedElement);
            }

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    int nextIndex = currentIndex + 1;
                    if (nextIndex >= visible.Count) nextIndex = 0;
                    SetHighlight(visible[nextIndex]);
                    evt.StopPropagation();                    break;

                case KeyCode.UpArrow:
                    int prevIndex = currentIndex <= 0 ? visible.Count - 1 : currentIndex - 1;
                    SetHighlight(visible[prevIndex]);
                    evt.StopPropagation();                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                    if (_highlightedElement != null)
                    {
                        var choice = _highlightedElement.userData as string;
                        if (choice != null)
                        {
                            SelectChoice(choice);
                            evt.StopPropagation();                        }
                    }
                    break;

                case KeyCode.Escape:
                    // Dismissing retires the peek explicitly: OnClose would do it, but a popup built without a
                    // window (headless / under test) never gets one, and a queued card must not outlive it.
                    _peek?.Hide();
                    if (editorWindow != null) editorWindow.Close();
                    evt.StopPropagation();                    break;
            }
        }

        private void SelectChoice(string choice)
        {
            if (_closing) return;
            _peek?.Hide();
            _onSelect?.Invoke(choice);

            var win = editorWindow;
            if (win == null) return;
            if (!EditorSpringFX.MotionEnabled)
            {
                win.Close();
                return;
            }

            // Menu-style acknowledgement: flash the chosen row at full selection strength with a tiny press
            // bounce, then close a beat later — so the pick reads as registered instead of the popup just
            // vanishing. Escape / click-outside still close instantly.
            _closing = true;
            VisualElement row = null;
            if (_optionElements != null)
            {
                foreach (var el in _optionElements)
                {
                    if ((el.userData as string) == choice) { row = el; break; }
                }
            }
            if (row != null)
            {
                row.style.backgroundColor = _node != null && _node.DropdownItemHoverBackground.isOverridden
                    ? _node.DropdownItemHoverBackground.value
                    : WidgetPalette.DropPopupSelectionStrong;
                EditorSpringFX.PressBounce(row, 0.95f);
            }
            win.rootVisualElement.schedule.Execute(() =>
            {
                var w = editorWindow;
                if (w != null) w.Close();
            }).ExecuteLater(110);
        }
    }
}
