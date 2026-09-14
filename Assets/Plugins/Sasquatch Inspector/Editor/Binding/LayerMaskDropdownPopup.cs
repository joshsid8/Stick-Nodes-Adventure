using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>A multi-select popup for editing a LayerMask field (the Layer dropdown's mask picker):
    /// Nothing / Everything plus one toggle row per layer, with arrow-key + Enter navigation.</summary>
    public class LayerMaskDropdownPopup : PopupWindowContent
    {
        private readonly Runtime.BindingNode _node;
        private readonly string _title;
        private readonly List<string> _layers;
        private readonly List<int> _values;
        private int _currentMask;
        private readonly Action<int> _onSelect;
        private readonly float _width;

        private ScrollView _scrollView;
        private List<VisualElement> _optionElements;
        private List<Action> _optionActions; // parallel to _optionElements — see ActivateRow
        private VisualElement _highlightedElement;

        private SasquatchIconElement _nothingCheckmark;
        private SasquatchIconElement _everythingCheckmark;
        private List<SasquatchIconElement> _layerCheckmarks;

        /// <summary>Fired from <see cref="OnOpen"/>/<see cref="OnClose"/> — lets the owning field react to the
        /// popup's real lifecycle (e.g. the dropdown button's chevron spin), however the popup ends up closing.</summary>
        public Action Opened;
        /// <inheritdoc cref="Opened"/>
        public Action Closed;

        private readonly VisualElement _anchor;

        // This popup stays open across several clicks, so holding the window behind it still matters more than
        // it does for the single-pick dropdowns.
        private readonly PopupScrollLock _scrollLock = new PopupScrollLock();

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
            Closed?.Invoke();
        }

        /// <summary>Creates the mask popup for <paramref name="node"/> with the given layer names/values and a
        /// selection callback. <paramref name="anchor"/> is the control the popup opens from; it is what lets
        /// the popup hold its host window still while it's open.</summary>
        public LayerMaskDropdownPopup(Runtime.BindingNode node, string title, List<string> layers, List<int> values, int currentMask, Action<int> onSelect, float width = 250f,
            VisualElement anchor = null)
        {
            _anchor = anchor;
            _node = node;
            _title = title;
            _layers = layers ?? new List<string>();
            _values = values ?? new List<int>();
            _currentMask = currentMask;
            _onSelect = onSelect;
            _width = width;
        }

        /// <inheritdoc/>
        public override Vector2 GetWindowSize()
        {
            float height = Mathf.Min(350f, 40f + (_layers.Count + 2) * 22f);
            return new Vector2(_width, height);
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

            _scrollView = new ScrollView();
            _scrollView.style.flexGrow = 1;
            root.Add(_scrollView);

            _optionElements = new List<VisualElement>();
            _optionActions = new List<Action>();
            _layerCheckmarks = new List<SasquatchIconElement>();

            // 1. "Nothing" Row
            Action nothingAction = () =>
            {
                _currentMask = 0;
                NotifyChange();
                EditorSpringFX.PingScale(_nothingCheckmark, 1.35f);
            };
            var nothingRow = CreateRow("Nothing", nothingAction);
            _nothingCheckmark = nothingRow.Q<SasquatchIconElement>("Checkmark");
            _scrollView.Add(nothingRow);
            _optionElements.Add(nothingRow);
            _optionActions.Add(nothingAction);

            // 2. "Everything" Row
            Action everythingAction = () =>
            {
                _currentMask = -1;
                NotifyChange();
                EditorSpringFX.PingScale(_everythingCheckmark, 1.35f);
            };
            var everythingRow = CreateRow("Everything", everythingAction);
            _everythingCheckmark = everythingRow.Q<SasquatchIconElement>("Checkmark");
            _scrollView.Add(everythingRow);
            _optionElements.Add(everythingRow);
            _optionActions.Add(everythingAction);

            // Separator line
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = WidgetPalette.DropPopupSeparator;
            sep.style.marginTop = 3;
            sep.style.marginBottom = 3;
            _scrollView.Add(sep);

            // 3. Layer Rows
            for (int i = 0; i < _layers.Count; i++)
            {
                int index = i;
                int layerBit = _values[index];
                Action layerAction = () =>
                {
                    _currentMask ^= (1 << layerBit);
                    NotifyChange();
                    // A checkmark that just turned ON gets a little attention ping (no-op when motion is off).
                    if ((_currentMask & (1 << layerBit)) != 0)
                        EditorSpringFX.PingScale(_layerCheckmarks[index], 1.35f);
                };
                var layerRow = CreateRow(_layers[index], layerAction);
                var check = layerRow.Q<SasquatchIconElement>("Checkmark");
                _layerCheckmarks.Add(check);
                _scrollView.Add(layerRow);
                _optionElements.Add(layerRow);
                _optionActions.Add(layerAction);
            }

            UpdateCheckmarks();

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
            _scrollView.focusable = true;
            _scrollView.schedule.Execute(() => _scrollView.Focus()).ExecuteLater(50);
        }

        /// <summary>
        /// Invokes the toggle action of the given option row — the SINGLE activation path, shared by pointer
        /// clicks, the Enter key, and the verification tests. Never "activate" a row by synthesizing a
        /// pointer event instead: the panel's
        /// pointer dispatch re-picks the target from the event POSITION, so a synthetic event does nothing at all
        /// unless layout has already resolved and the position genuinely hits the row.
        /// </summary>
        public void ActivateRow(VisualElement row)
        {
            int index = _optionElements != null && row != null ? _optionElements.IndexOf(row) : -1;
            if (index >= 0) _optionActions[index]();
        }

        private VisualElement CreateRow(string name, Action onClick)
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

            row.RegisterCallback<MouseEnterEvent>(_ => SetHighlight(row));
            row.RegisterCallback<MouseLeaveEvent>(_ => {
                if (_highlightedElement == row)
                {
                    SetHighlight(null);
                }
            });

            var checkLabel = SasquatchIcons.Create(SasquatchIcons.Ids.Check, 11);
            checkLabel.name = "Checkmark";
            checkLabel.style.width = 14;
            checkLabel.style.visibility = Visibility.Hidden; // shown by UpdateCheckmarks

            var nameLabel = new Label(name);
            nameLabel.name = "NameLabel";

            // The check is an icon, so color is the only text-style override that maps onto it.
            if (_node != null && _node.DropdownItemTextStyle.color.isOverridden)
            {
                checkLabel.Tint = _node.DropdownItemTextStyle.color.value;
            }
            ApplyTextStyleToLabel(nameLabel);

            row.Add(checkLabel);
            row.Add(nameLabel);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    onClick();
                    evt.StopPropagation();
                }
            });

            return row;
        }

        private void ApplyTextStyleToLabel(Label label)
        {
            if (_node != null && TextStyleApplier.HasAnyOverride(_node.DropdownItemTextStyle))
            {
                TextStyleApplier.Apply(label, _node.DropdownItemTextStyle);
            }

            if (_node != null && _node.DropdownItemTextStyle.color.isOverridden)
            {
                label.style.color = _node.DropdownItemTextStyle.color.value;
            }
            else
            {
                label.style.color = WidgetPalette.DropPopupItemText;
            }

            if (_node == null || !TextStyleApplier.IsUsableFontSize(_node.DropdownItemTextStyle.fontSize))
            {
                label.style.fontSize = 11;
            }
        }

        private void UpdateCheckmarks()
        {
            bool allSelected = true;
            foreach (int val in _values)
            {
                if ((_currentMask & (1 << val)) == 0)
                {
                    allSelected = false;
                    break;
                }
            }
            bool isEverything = _currentMask == -1 || allSelected;
            bool isNothing = _currentMask == 0;

            _nothingCheckmark.style.visibility = isNothing ? Visibility.Visible : Visibility.Hidden;
            _everythingCheckmark.style.visibility = isEverything ? Visibility.Visible : Visibility.Hidden;

            for (int i = 0; i < _layerCheckmarks.Count; i++)
            {
                int val = _values[i];
                bool isChecked = (_currentMask & (1 << val)) != 0;
                _layerCheckmarks[i].style.visibility = isChecked ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void NotifyChange()
        {
            UpdateCheckmarks();
            _onSelect?.Invoke(_currentMask);
        }

        private void SetHighlight(VisualElement target)
        {
            foreach (var el in _optionElements)
            {
                el.style.backgroundColor = Color.clear;

                var labels = el.Query<Label>().ToList();
                foreach (var label in labels)
                {
                    ApplyTextStyleToLabel(label);
                }
            }

            if (target != null)
            {
                if (_node != null && _node.DropdownItemHoverBackground.isOverridden)
                {
                    target.style.backgroundColor = _node.DropdownItemHoverBackground.value;
                }
                else
                {
                    target.style.backgroundColor = WidgetPalette.DropPopupRowHover;
                }

                var labels = target.Query<Label>().ToList();
                foreach (var label in labels)
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
            }

            _highlightedElement = target;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_optionElements.Count == 0) return;

            int currentIndex = -1;
            if (_highlightedElement != null)
            {
                currentIndex = _optionElements.IndexOf(_highlightedElement);
            }

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    int nextIndex = currentIndex + 1;
                    if (nextIndex >= _optionElements.Count) nextIndex = 0;
                    SetHighlight(_optionElements[nextIndex]);
                    evt.StopPropagation();                    break;

                case KeyCode.UpArrow:
                    int prevIndex = currentIndex <= 0 ? _optionElements.Count - 1 : currentIndex - 1;
                    SetHighlight(_optionElements[prevIndex]);
                    evt.StopPropagation();                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_highlightedElement != null)
                    {
                        // Nothing row is index 0, Everything is index 1, separator is not in _optionElements.
                        ActivateRow(_highlightedElement);
                        evt.StopPropagation();                    }
                    break;

                case KeyCode.Escape:
                    editorWindow.Close();
                    evt.StopPropagation();                    break;
            }
        }
    }
}
