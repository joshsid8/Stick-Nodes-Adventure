using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // DropDown widget: the unified "pick from a list" widget that consolidates Tag + Layer +
        // Enum + ValueDropdown + searchable. It dispatches by the bound field's type and the node's
        // DropdownSource:
        //   • enum field        → an enum dropdown (searchable when SearchableDropdown is on)
        //   • Source.Auto       → string ⇒ Tags, int ⇒ Layer, LayerMask ⇒ LayerMask, anything else ⇒ Method
        //   • Source.Tags       → Unity TagField (string)
        //   • Source.Layers     → Unity LayerField (int)
        //   • Source.Method     → values from the node's ValueSourceMethod (the old ValueDropdown path)
        // Each underlying creator already falls back to the default field for the wrong property type, so a
        // mis-sourced DropDown is never stuck.
        private static VisualElement CreateDropDown(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            // Enum fields are inherently their own value list; the Source selector only applies to non-enums.
            if (prop.propertyType == SerializedPropertyType.Enum)
            {
                return CreateSearchableEnumDropdown(node, prop, resolvedLabel);
            }

            var source = node.DropdownSource;
            if (source == DropdownSource.Auto)
            {
                if (prop.propertyType == SerializedPropertyType.String) source = DropdownSource.Tags;
                else if (prop.propertyType == SerializedPropertyType.Integer) source = DropdownSource.Layer;
                else if (prop.propertyType == SerializedPropertyType.LayerMask) source = DropdownSource.LayerMask;
                else source = DropdownSource.Method;
            }

            switch (source)
            {
                case DropdownSource.Tags: return CreateTagField(node, prop, resolvedLabel);
                case DropdownSource.Layer:
                    if (prop.propertyType == SerializedPropertyType.LayerMask)
                        return CreateLayerMaskField(node, prop, resolvedLabel);
                    return CreateLayerField(node, prop, resolvedLabel);
                case DropdownSource.LayerMask:
                    return CreateLayerMaskField(node, prop, resolvedLabel);
                case DropdownSource.Method: return CreateValueDropdown(node, prop, resolvedLabel);
                default: return new PropertyField(prop) { label = resolvedLabel };
            }
        }

        private static VisualElement CreateTagField(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.String)
                return new PropertyField(prop) { label = resolvedLabel };

            string Current() => string.IsNullOrEmpty(prop.stringValue) ? "Untagged" : prop.stringValue;

            var tags = new List<string>(UnityEditorInternal.InternalEditorUtility.tags);
            if (!tags.Contains("Untagged"))
            {
                tags.Insert(0, "Untagged");
            }

            var field = CreateSearchableDropdown(node, resolvedLabel, tags, Current(), selected =>
            {
                prop.stringValue = selected == "Untagged" ? "" : selected;
                prop.serializedObject.ApplyModifiedProperties();
            }, out var valueLabel);

            void Sync() => MixedValueUI.SetText(valueLabel, MixedValueUI.IsMixed(prop), Current());
            Sync();
            field.TrackPropertyValueWithRefresh(prop, _ => Sync());

            return field;
        }

        // DropdownSource.Layer: render an int property as Unity's single-layer dropdown. Non-int properties
        // (incl. a LayerMask, which already gets the multi-select mask field via Auto) fall back to default.
        private static VisualElement CreateLayerField(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.Integer)
                return new PropertyField(prop) { label = resolvedLabel };

            var layers = new List<string>();
            var values = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layers.Add(name);
                    values.Add(i);
                }
            }

            string Current()
            {
                int val = prop.intValue;
                int idx = values.IndexOf(val);
                return idx >= 0 ? layers[idx] : val.ToString();
            }

            var field = CreateSearchableDropdown(node, resolvedLabel, layers, Current(), selected =>
            {
                int idx = layers.IndexOf(selected);
                if (idx >= 0)
                {
                    prop.intValue = values[idx];
                    prop.serializedObject.ApplyModifiedProperties();
                }
            }, out var valueLabel);

            void Sync() => MixedValueUI.SetText(valueLabel, MixedValueUI.IsMixed(prop), Current());
            Sync();
            field.TrackPropertyValueWithRefresh(prop, _ => Sync());

            return field;
        }

        // The dropdown button paints its background inline (palette or the node's Field Background override),
        // which suppresses Unity's :hover pseudo-state — so hover feedback is wired manually. The rest color is
        // re-resolved at fire time, never captured, so leaving hover restores exactly what ApplyFieldStyle last
        // stamped (hover callbacks read live state — the designer-wide convention).
        private static void AttachDropdownButtonChrome(Button btn, SasquatchIconElement arrow, BindingNode node)
        {
            if (btn == null) return;

            // SHRINK POLICY. Both dropdown buttons set flexGrow 1 and nothing else, which is a one-way
            // street: grow only ever hands out SPARE width, so a button whose text is longer than its lane
            // sizes itself to that text and refuses to give any of it back. Measured 2026-09-01 at a 300pt
            // inspector — a 120px button in a 110px input container, painting 10px past it with the chevron
            // itself sliced. Attached here rather than at the two call sites because this is the one place
            // both of them already funnel through, so they cannot drift apart again.
            //
            // The three parts each want something different: the BUTTON gives way, the TEXT truncates
            // (a half-visible name still reads; a name that pushes the chevron off the row does not), and
            // the CHEVRON never shrinks — it is the affordance that says this thing opens.
            btn.style.flexShrink = 1;
            btn.style.minWidth = 0;
            foreach (var child in btn.Children())
            {
                if (child == arrow) continue;
                if (!(child is Label text)) continue;
                text.style.flexShrink = 1;
                text.style.minWidth = 0;
                text.style.overflow = Overflow.Hidden;
                text.style.textOverflow = TextOverflow.Ellipsis;
                text.style.whiteSpace = WhiteSpace.NoWrap;
            }
            if (arrow != null) arrow.style.flexShrink = 0;
            Color RestBg() => node != null && node.FieldBackground.isOverridden
                ? node.FieldBackground.value
                : WidgetPalette.DropdownBg;
            btn.RegisterCallback<PointerEnterEvent>(_ =>
            {
                var rest = RestBg();
                btn.style.backgroundColor = WidgetPalette.IsLight
                    ? Color.Lerp(rest, Color.black, 0.07f)
                    : Color.Lerp(rest, Color.white, 0.07f);
                if (arrow != null) arrow.Tint = WidgetPalette.LabelPrimary;
            });
            btn.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                btn.style.backgroundColor = RestBg();
                if (arrow != null) arrow.Tint = WidgetPalette.ArrowDim;
            });
        }

        // Spins the button's chevron between closed (▼, 0°) and open (▲, 180°) with the house spring — the same
        // caret language as the expandable group boxes. Snaps instantly when Reduce Motion is on.
        private static void SetDropdownArrowOpen(VisualElement arrow, bool open)
        {
            if (arrow == null) return;
            arrow.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f));
            float target = open ? 180f : 0f;
            if (!EditorSpringFX.MotionEnabled)
            {
                arrow.CancelSprings();
                arrow.style.rotate = new Rotate(Angle.Degrees(target));
                return;
            }
            arrow.SpringRotate(target, EditorSpringFX.Tune(SpringConfig.Bouncy));
        }

        // DropdownSource.LayerMask: render an int-backed property as the multi-select mask field. The
        // property-type guard is NOT optional and NOT symmetry for its own sake: `prop.intValue` on, say, a
        // string property reads 0 AND makes Unity log "type is not a supported int value" on every read —
        // twice per build — while a selection writes back into intValue and is silently discarded. Without
        // it the user gets an inert picker that reads "Nothing", accepts clicks, changes nothing, and spams
        // the console. Integer is allowed alongside LayerMask because a raw int used as a bitmask is coherent
        // here and `intValue` serves both. The mis-sourced node is reachable when a field TYPE changes under
        // an existing layout: the designer rewrites an out-of-range DropdownSource only while its properties
        // panel is open on that node, never on load and never in either render path.
        private static VisualElement CreateLayerMaskField(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.LayerMask
                && prop.propertyType != SerializedPropertyType.Integer)
                return new PropertyField(prop) { label = resolvedLabel };

            var layers = new List<string>();
            var values = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layers.Add(name);
                    values.Add(i);
                }
            }

            string CurrentText()
            {
                return GetLayerMaskText(prop.intValue, layers, values);
            }

            var field = CreateSearchableLayerMaskDropdown(node, resolvedLabel, layers, values, () => prop.intValue, selectedMask =>
            {
                prop.intValue = selectedMask;
                prop.serializedObject.ApplyModifiedProperties();
            }, out var valueLabel);

            void Sync() => MixedValueUI.SetText(valueLabel, MixedValueUI.IsMixed(prop), CurrentText());
            Sync();
            field.TrackPropertyValueWithRefresh(prop, _ => Sync());

            return field;
        }

        private static string GetLayerMaskText(int mask, List<string> layers, List<int> values)
        {
            if (mask == 0) return "Nothing";
            
            bool allSelected = true;
            foreach (int val in values)
            {
                if ((mask & (1 << val)) == 0)
                {
                    allSelected = false;
                    break;
                }
            }
            if (mask == -1 || allSelected) return "Everything";

            var selectedNames = new List<string>();
            for (int i = 0; i < layers.Count; i++)
            {
                int val = values[i];
                if ((mask & (1 << val)) != 0)
                {
                    selectedNames.Add(layers[i]);
                }
            }

            if (selectedNames.Count == 0) return "Nothing";
            if (selectedNames.Count == 1) return selectedNames[0];
            
            return "Mixed...";
        }

        private static VisualElement CreateSearchableLayerMaskDropdown(BindingNode node, string labelText, List<string> layers, List<int> values, Func<int> getMask, Action<int> onSelect, out Label valueLabel)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-searchable-dropdown-field");

            var labelEl = new Label(labelText);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(labelText))
            {
                labelEl.style.display = DisplayStyle.None;
            }
            wrapper.Add(labelEl);

            var inputContainer = new VisualElement();
            inputContainer.AddToClassList("unity-base-field__input");
            inputContainer.style.flexDirection = FlexDirection.Row;

            var dropdownBtn = new Button();
            dropdownBtn.style.flexGrow = 1;
            dropdownBtn.style.flexDirection = FlexDirection.Row;
            dropdownBtn.style.justifyContent = Justify.SpaceBetween;
            dropdownBtn.style.alignItems = Align.Center;
            dropdownBtn.style.backgroundColor = WidgetPalette.DropdownBg;
            dropdownBtn.style.borderTopWidth = 1;
            dropdownBtn.style.borderBottomWidth = 1;
            dropdownBtn.style.borderLeftWidth = 1;
            dropdownBtn.style.borderRightWidth = 1;
            dropdownBtn.style.borderTopColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderBottomColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderLeftColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderRightColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderTopLeftRadius = 3;
            dropdownBtn.style.borderTopRightRadius = 3;
            dropdownBtn.style.borderBottomLeftRadius = 3;
            dropdownBtn.style.borderBottomRightRadius = 3;
            dropdownBtn.style.paddingLeft = 6;
            dropdownBtn.style.paddingRight = 6;
            dropdownBtn.style.paddingTop = 2;
            dropdownBtn.style.paddingBottom = 2;
            dropdownBtn.style.marginLeft = 0;
            dropdownBtn.style.marginRight = 0;
            dropdownBtn.style.marginTop = 0;
            dropdownBtn.style.marginBottom = 0;

            var localLabel = new Label(GetLayerMaskText(getMask(), layers, values));
            localLabel.style.color = WidgetPalette.LabelPrimary;
            dropdownBtn.Add(localLabel);
            valueLabel = localLabel;

            var arrowLabel = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronDown, 10, WidgetPalette.ArrowDim);
            dropdownBtn.Add(arrowLabel);

            inputContainer.Add(dropdownBtn);
            wrapper.Add(inputContainer);

            AttachDropdownButtonChrome(dropdownBtn, arrowLabel, node);

            dropdownBtn.clicked += () =>
            {
                var rect = dropdownBtn.worldBound;
                var popup = new LayerMaskDropdownPopup(node, labelText, layers, values, getMask(), selected =>
                {
                    onSelect?.Invoke(selected);
                    localLabel.text = GetLayerMaskText(selected, layers, values);
                }, rect.width, anchor: dropdownBtn);
                // Tie the chevron to the popup's real lifecycle (Opened/Closed fire from OnOpen/OnClose), so it
                // points up while the popup is showing and springs back however the popup ends up closing.
                popup.Opened = () => SetDropdownArrowOpen(arrowLabel, true);
                popup.Closed = () => SetDropdownArrowOpen(arrowLabel, false);
                UnityEditor.PopupWindow.Show(rect, popup);
            };

            // Stashed on the INPUT row, not the wrapper: LayoutTreeBuilder stamps the source LayoutNode
            // into the built element's userData (the quick-edit menu's element→node link), which would
            // clobber a wrapper-level stash. Tests read the option list from here.
            inputContainer.userData = layers;
            return wrapper;
        }

        // ValueDropdown: render the field as a dropdown whose options come from a named, parameterless
        // method on the target (instance or static) returning an IEnumerable. Supports string / int / float /
        // object-reference fields; any other type — or a missing/invalid/throwing method — falls back to the
        // normal field so the value is never stuck. Options are fetched once per build (a rebuild refetches).
        private static VisualElement CreateValueDropdown(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            bool supported = prop.propertyType == SerializedPropertyType.String
                || prop.propertyType == SerializedPropertyType.Integer
                || prop.propertyType == SerializedPropertyType.Float
                || prop.propertyType == SerializedPropertyType.ObjectReference;
            if (!supported || string.IsNullOrEmpty(node.ValueSourceMethod))
                return ValueDropdownFallback(prop, resolvedLabel);

            System.Collections.IEnumerable options = null;
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj != null)
            {
                try
                {
                    var method = targetObj.GetType().GetMethod(
                        node.ValueSourceMethod,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
                    if (method != null && method.GetParameters().Length == 0)
                    {
                        var result = method.IsStatic ? method.Invoke(null, null) : method.Invoke(targetObj, null);
                        options = result as System.Collections.IEnumerable;
                    }
                }
                catch { /* fall back below */ }
            }

            if (options == null)
                return ValueDropdownFallback(prop, resolvedLabel);

            var values = new System.Collections.Generic.List<object>();
            var labels = new System.Collections.Generic.List<string>();
            foreach (var o in options)
            {
                values.Add(o);
                labels.Add(ValueDropdownLabel(o));
            }

            void SyncSelection(Label valLabel)
            {
                object current = CurrentPropertyValue(prop);
                int idx = -1;
                for (int i = 0; i < values.Count; i++)
                {
                    if (ValueDropdownValuesEqual(current, values[i], prop.propertyType)) { idx = i; break; }
                }
                string text = idx >= 0 ? labels[idx] : ValueDropdownLabel(current);
                MixedValueUI.SetText(valLabel, MixedValueUI.IsMixed(prop), text);
            }

            var valLabel = new Label();
            var searchDropdown = CreateSearchableDropdown(node, resolvedLabel, labels, "", selected =>
            {
                int idx = labels.IndexOf(selected);
                if (idx >= 0)
                {
                    ApplyValueDropdownValue(prop, values[idx]);
                    prop.serializedObject.ApplyModifiedProperties();
                }
            }, out valLabel);

            SyncSelection(valLabel);
            searchDropdown.TrackPropertyValueWithRefresh(prop, _ => SyncSelection(valLabel));
            return searchDropdown;
        }

        /// <summary><paramref name="codePeek"/>, when given, maps a choice to a highlighted source snippet and
        /// arms the popup's hover code peek — only the polymorphic type picker passes one, since it is the one
        /// dropdown here whose choices are code rather than data.</summary>
        private static VisualElement CreateSearchableDropdown(BindingNode node, string labelText, List<string> choices, string currentSelection, Action<string> onSelect, out Label valueLabel,
            Func<string, string> codePeek = null)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-searchable-dropdown-field");

            var labelEl = new Label(labelText);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(labelText))
            {
                labelEl.style.display = DisplayStyle.None;
            }
            wrapper.Add(labelEl);

            var inputContainer = new VisualElement();
            inputContainer.AddToClassList("unity-base-field__input");
            inputContainer.style.flexDirection = FlexDirection.Row;

            var dropdownBtn = new Button();
            dropdownBtn.style.flexGrow = 1;
            dropdownBtn.style.flexDirection = FlexDirection.Row;
            dropdownBtn.style.justifyContent = Justify.SpaceBetween;
            dropdownBtn.style.alignItems = Align.Center;
            dropdownBtn.style.backgroundColor = WidgetPalette.DropdownBg;
            dropdownBtn.style.borderTopWidth = 1;
            dropdownBtn.style.borderBottomWidth = 1;
            dropdownBtn.style.borderLeftWidth = 1;
            dropdownBtn.style.borderRightWidth = 1;
            dropdownBtn.style.borderTopColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderBottomColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderLeftColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderRightColor = WidgetPalette.DropdownBorder;
            dropdownBtn.style.borderTopLeftRadius = 3;
            dropdownBtn.style.borderTopRightRadius = 3;
            dropdownBtn.style.borderBottomLeftRadius = 3;
            dropdownBtn.style.borderBottomRightRadius = 3;
            dropdownBtn.style.paddingLeft = 6;
            dropdownBtn.style.paddingRight = 6;
            dropdownBtn.style.paddingTop = 2;
            dropdownBtn.style.paddingBottom = 2;
            dropdownBtn.style.marginLeft = 0;
            dropdownBtn.style.marginRight = 0;
            dropdownBtn.style.marginTop = 0;
            dropdownBtn.style.marginBottom = 0;

            valueLabel = new Label(currentSelection);
            valueLabel.style.color = WidgetPalette.LabelPrimary;
            dropdownBtn.Add(valueLabel);

            var arrowLabel = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronDown, 10, WidgetPalette.ArrowDim);
            dropdownBtn.Add(arrowLabel);

            inputContainer.Add(dropdownBtn);
            wrapper.Add(inputContainer);

            AttachDropdownButtonChrome(dropdownBtn, arrowLabel, node);

            var valLabel = valueLabel;
            dropdownBtn.clicked += () =>
            {
                var rect = dropdownBtn.worldBound;
                var popup = new SearchableDropdownPopup(node, labelText, choices, valLabel.text, selected =>
                {
                    onSelect?.Invoke(selected);
                    valLabel.text = selected;
                }, rect.width, itemIcon: null, codePeek: codePeek, anchor: dropdownBtn);
                // Tie the chevron to the popup's real lifecycle (Opened/Closed fire from OnOpen/OnClose), so it
                // points up while the popup is showing and springs back however the popup ends up closing.
                popup.Opened = () => SetDropdownArrowOpen(arrowLabel, true);
                popup.Closed = () => SetDropdownArrowOpen(arrowLabel, false);
                UnityEditor.PopupWindow.Show(rect, popup);
            };

            // Stashed on the INPUT row, not the wrapper: LayoutTreeBuilder stamps the source LayoutNode
            // into the built element's userData (the quick-edit menu's element→node link), which would
            // clobber a wrapper-level stash. Tests read the option list from here.
            inputContainer.userData = choices;
            return wrapper;
        }

        private static VisualElement CreateSearchableEnumDropdown(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            var enumNames = new List<string>(prop.enumDisplayNames);
            
            void SyncSelection(Label valLabel)
            {
                int valIdx = prop.enumValueIndex;
                string text = (valIdx >= 0 && valIdx < enumNames.Count)
                    ? enumNames[valIdx]
                    : prop.enumValueIndex.ToString();
                MixedValueUI.SetText(valLabel, MixedValueUI.IsMixed(prop), text);
            }

            var searchDropdown = CreateSearchableDropdown(node, resolvedLabel, enumNames, "", selected =>
            {
                int idx = enumNames.IndexOf(selected);
                if (idx >= 0)
                {
                    prop.enumValueIndex = idx;
                    prop.serializedObject.ApplyModifiedProperties();
                }
            }, out var valLabel);

            SyncSelection(valLabel);
            searchDropdown.TrackPropertyValueWithRefresh(prop, _ => SyncSelection(valLabel));
            return searchDropdown;
        }

        private static VisualElement ValueDropdownFallback(SerializedProperty prop, string resolvedLabel)
        {
            return new PropertyField(prop) { label = resolvedLabel };
        }

        private static string ValueDropdownLabel(object value)
        {
            if (value == null) return "(None)";
            if (value is UnityEngine.Object uo) return uo != null ? uo.name : "(None)";
            return value.ToString();
        }

        private static object CurrentPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.ObjectReference: return prop.objectReferenceValue;
                default: return null;
            }
        }

        // The options come from the USER's member, so they are whatever they authored — including a
        // string[] against a numeric field. Convert.To* on a string reads the thread's culture, so a
        // comma-decimal editor turns the option "1.5" into 15 (or throws into the catch and silently
        // selects nothing). Invariant matches how the values were authored in C# source.
        private static bool ValueDropdownValuesEqual(object current, object option, SerializedPropertyType type)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                switch (type)
                {
                    case SerializedPropertyType.ObjectReference:
                        return (current as UnityEngine.Object) == (option as UnityEngine.Object);
                    case SerializedPropertyType.String:
                        return (current as string ?? "") == (option?.ToString() ?? "");
                    case SerializedPropertyType.Integer:
                        return option != null && Convert.ToInt32(option, inv) == Convert.ToInt32(current, inv);
                    case SerializedPropertyType.Float:
                        return option != null && Mathf.Approximately(Convert.ToSingle(option, inv), Convert.ToSingle(current, inv));
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        private static void ApplyValueDropdownValue(SerializedProperty prop, object value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.String: prop.stringValue = value?.ToString() ?? ""; break;
                    // Invariant for the same reason as ValueDropdownValuesEqual above — a string option
                    // must not be re-read through the editor's decimal separator on the way to the field.
                    case SerializedPropertyType.Integer: prop.intValue = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Float: prop.floatValue = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.ObjectReference: prop.objectReferenceValue = value as UnityEngine.Object; break;
                }
            }
            catch { /* option type incompatible with this field — ignore */ }
        }

    }
}
