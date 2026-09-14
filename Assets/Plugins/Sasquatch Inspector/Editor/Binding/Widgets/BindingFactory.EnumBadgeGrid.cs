using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // Enum Badge Grid widget: renders an enum field as a grid of clickable toggle chips instead of a dropdown.
        // A [System.Flags] enum becomes a multi-select grid; a plain enum becomes a single-select segmented
        // control. The enum value lives on the bound SerializedProperty (read/written via enumValueFlag); the chip
        // build + bit-toggling + refresh lifecycle live in SasquatchEnumBadgeControl. Any non-enum property (the
        // fuzzer throws every widget at every type) falls back to a plain PropertyField so it never throws.
        private static VisualElement CreateEnumBadgeGrid(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.Enum)
                return new PropertyField(prop) { label = resolvedLabel };

            Type enumType = ResolveEnumType(prop);
            if (enumType == null || !enumType.IsEnum)
                return new PropertyField(prop) { label = resolvedLabel };

            // [Flags] enums are multi-select (bitwise); plain enums are single-select (radio).
            bool multi = enumType.IsDefined(typeof(FlagsAttribute), false);
            bool editable = !node.IsReadOnly;

            var control = new SasquatchEnumBadgeControl(prop, node, enumType, multi, editable);

            // Native aligned-field structure (label column + input area) so the badges line up with sibling
            // fields and honor the inspector's `unity-base-field__aligned` label-width sync.
            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-enum-badge-field");
            wrapper.style.flexDirection = FlexDirection.Row;

            var labelEl = new Label(resolvedLabel ?? string.Empty);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel)) labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            var inputContainer = new VisualElement();
            inputContainer.AddToClassList("unity-base-field__input");
            inputContainer.style.flexDirection = FlexDirection.Row;
            inputContainer.style.flexWrap = Wrap.Wrap;
            inputContainer.Add(control);
            wrapper.Add(inputContainer);

            return wrapper;
        }

        /// <summary>Resolves the bound field's enum <see cref="Type"/> (unwrapping array / List&lt;T&gt; element
        /// types), or null when the field isn't an enum — mirrors the resolution in <c>ConditionEvaluator</c>.</summary>
        private static Type ResolveEnumType(SerializedProperty prop)
        {
            var target = prop?.serializedObject?.targetObject;
            if (target == null) return null;

            GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out var resolvedType);
            if (resolvedType == null) return null;
            if (resolvedType.IsEnum) return resolvedType;
            if (resolvedType.IsArray)
            {
                var el = resolvedType.GetElementType();
                if (el != null && el.IsEnum) return el;
            }
            if (resolvedType.IsGenericType && resolvedType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var arg = resolvedType.GetGenericArguments()[0];
                if (arg.IsEnum) return arg;
            }
            return null;
        }
    }
}
