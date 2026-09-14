using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The shared "overridable text" row: a TextField that shows the code default as real (normal-weight)
    /// text when unset, bold text when overridden, a "(hidden)" placeholder when cleared (stored value =
    /// LayoutNode.HiddenSentinel), and a ↺ reset button that restores the code default. Used by the
    /// designer's Properties panel (Label/Tooltip/Header) and the inspector quick-edit popup, so the
    /// "untouched vs. set vs. hidden" convention reads identically everywhere.
    ///
    /// The <c>commit</c> delegate receives the new STORED value (override text, empty = unset, or
    /// HiddenSentinel) and owns undo recording, persistence, and any refresh — this row only manages its
    /// own display state.
    /// </summary>
    public static class OverridableTextRow
    {
        /// <summary>
        /// The one set of metrics for every small square button that sits at the right edge of a
        /// Properties-panel field (the ↺ reset and the "T" text-style toggle), so they always read as
        /// the same size when they appear side by side. Explicit height because the two have different
        /// content (an 11px icon vs. a text glyph) and would otherwise size themselves differently.
        /// </summary>
        public static void StyleAsFieldButton(Button b)
        {
            b.style.width = 20;
            b.style.height = 18;
            b.style.marginLeft = 2;
            b.style.marginTop = 0;
            b.style.marginBottom = 0;
            b.style.paddingLeft = 0;
            b.style.paddingRight = 0;
            b.style.paddingTop = 0;
            b.style.paddingBottom = 0;
            b.style.flexShrink = 0;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
        }

        /// <summary>The shared ↺ reset button (Lucide rotate-ccw), sized to match the "T" button.</summary>
        public static Button CreateResetButton(string tooltip)
        {
            var reset = new Button();
            reset.Add(SasquatchIcons.Create(SasquatchIcons.Ids.RotateCcw, 11));
            reset.tooltip = tooltip;
            StyleAsFieldButton(reset);
            return reset;
        }

        /// <summary>Builds an overridable text row: a field pre-filled with <paramref name="current"/>, a code-default placeholder, and a reset button that calls <paramref name="commit"/>.</summary>
        public static VisualElement Create(string label, string current, string codeDefault,
            Action<string> commit, string fieldTooltip = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var field = new TextField(label);
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            if (!string.IsNullOrEmpty(fieldTooltip)) field.tooltip = fieldTooltip;

            var reset = CreateResetButton("Reset to the script default");

            string stored = current;

            // The text the field should DISPLAY for a given stored state: the override when set, otherwise the
            // code default shown as a real (editable, normal-weight) value, otherwise empty (hidden / no default).
            string DisplayTextFor(string s)
            {
                if (s == LayoutNode.HiddenSentinel) return string.Empty;
                if (string.IsNullOrEmpty(s)) return codeDefault ?? string.Empty;
                return s;
            }

            // Sync the bold (override) state, the "(hidden)" placeholder, and the reset-arrow visibility to the
            // stored state. Never touches the field's text here (that would jump the caret mid-typing).
            void UpdateChrome()
            {
                bool isHidden = stored == LayoutNode.HiddenSentinel;
                bool isUnset = !isHidden && string.IsNullOrEmpty(stored);
                bool isOverride = !isHidden && !isUnset;
                // Default = white/normal, overwritten = bold (inherited down to the label + value text).
                field.style.unityFontStyleAndWeight = isOverride ? FontStyle.Bold : FontStyle.Normal;
                // The placeholder only labels the empty hidden row (the code default is shown as real text).
                field.textEdition.placeholder = isHidden ? "(hidden)" : string.Empty;
                field.textEdition.hidePlaceholderOnFocus = false;
                reset.style.display = isUnset ? DisplayStyle.None : DisplayStyle.Flex;
            }

            void Commit(string newStored, bool syncText)
            {
                stored = newStored;
                commit(stored);
                if (syncText)
                    field.SetValueWithoutNotify(DisplayTextFor(stored));
                UpdateChrome();
            }

            field.RegisterValueChangedCallback(evt =>
            {
                string text = evt.newValue;
                string newStored;
                if (string.IsNullOrEmpty(text))
                    newStored = LayoutNode.HiddenSentinel;                          // cleared → hide (no fallback)
                else if (!string.IsNullOrEmpty(codeDefault) && text == codeDefault)
                    newStored = string.Empty;                                       // typed back to default → unset
                else
                    newStored = text;                                               // anything else → override
                Commit(newStored, syncText: false);
            });

            // When there's no code default the unset field is empty, so deleting raises no change event —
            // Backspace/Delete on the empty field is still the "hide it" gesture. Caught in the trickle-down phase
            // (the text input consumes the key before it would bubble to this field-level handler). When a code
            // default IS shown as real text, clearing it fires the value-changed handler above instead.
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                bool isDeleteKey = evt.keyCode == KeyCode.Backspace || evt.keyCode == KeyCode.Delete
                                   || evt.character == '\b' || evt.character == (char)127;
                if (isDeleteKey
                    && string.IsNullOrEmpty(field.value)
                    && stored != LayoutNode.HiddenSentinel)
                {
                    Commit(LayoutNode.HiddenSentinel, syncText: false);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            reset.clicked += () => Commit(string.Empty, syncText: true);

            field.SetValueWithoutNotify(DisplayTextFor(stored));
            UpdateChrome();

            row.Add(field);
            row.Add(reset);
            return row;
        }

        // ---- Code-default resolvers (shared by the designer panel + the quick-edit popup) ----

        /// <summary>The [Tooltip] text declared in C# on the member a binding points at, or null.</summary>
        public static string ResolveCodeTooltip(LayoutNode node, Type targetType)
        {
            var fieldInfo = ResolveFieldInfo(node, targetType);
            if (fieldInfo == null) return null;
            var attr = Attribute.GetCustomAttribute(fieldInfo, typeof(TooltipAttribute)) as TooltipAttribute;
            return attr?.tooltip;
        }

        /// <summary>The default label Unity would show for the member a binding points at (the nicified
        /// field name), or null.</summary>
        public static string ResolveCodeLabel(LayoutNode node, Type targetType)
        {
            var fieldInfo = ResolveFieldInfo(node, targetType);
            return fieldInfo == null ? null : ObjectNames.NicifyVariableName(fieldInfo.Name);
        }

        /// <summary>
        /// The title a collection's header shows when its Title is unset — the nicified last segment of its
        /// property path, which is what Unity's own <c>displayName</c> resolves to (and exactly what the
        /// standalone export emits). Takes the node rather than a target type because a collection's default
        /// comes from the path alone: no reflection, so it is right even mid-compile.
        /// </summary>
        public static string ResolveCollectionTitle(CollectionNode node)
        {
            string path = node?.PropertyPath;
            if (string.IsNullOrEmpty(path)) return null;
            int dot = path.LastIndexOf('.');
            string leaf = dot >= 0 ? path.Substring(dot + 1) : path;
            return string.IsNullOrEmpty(leaf) ? null : ObjectNames.NicifyVariableName(leaf);
        }

        /// <summary>The [Header] text declared in C# on the member a binding points at, or null.</summary>
        public static string ResolveCodeHeader(LayoutNode node, Type targetType)
        {
            var fieldInfo = ResolveFieldInfo(node, targetType);
            if (fieldInfo == null) return null;
            var attr = Attribute.GetCustomAttribute(fieldInfo, typeof(HeaderAttribute)) as HeaderAttribute;
            return attr?.header;
        }

        private static System.Reflection.FieldInfo ResolveFieldInfo(LayoutNode node, Type targetType)
        {
            if (!(node is BindingNode bn) || string.IsNullOrEmpty(bn.PropertyPath) || targetType == null)
                return null;
            return BindingFactory.GetFieldInfoFromPropertyPath(targetType, bn.PropertyPath, out _);
        }
    }
}
