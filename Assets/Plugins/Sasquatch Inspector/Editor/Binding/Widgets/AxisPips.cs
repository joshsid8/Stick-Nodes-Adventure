using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Axis color pips for the Default widget's vector-family fields: a small rounded bar tucked
    /// inside the RIGHT edge of each component's input (field text is left-aligned, so the right
    /// edge is the quiet corner), colored by Unity's own handle convention — X red, Y green, Z blue
    /// (read live from <see cref="Handles"/>, so a user's customized gizmo colors carry over); W has
    /// no axis, so it gets a neutral pip that keeps the row rhythm. Applies to Vector2/3/4, the Int
    /// variants, and Quaternion (shown as euler by the inspector).
    /// </summary>
    internal static class AxisPips
    {
        // internal: FieldAffixes has to know this lane exists so an inside SUFFIX can sit left of it instead
        // of writing over the same paddingRight and drawing on top of the pip.
        internal const string PipClass = "sasq-axis-pip";
        // 3 inset + 3 pip + 3 gap. The single source of truth for the reservation below AND for the affix
        // offset in FieldAffixes.ReservedTrailingLane — two literals here is how they drift apart.
        internal const float LaneWidth = 9f;

        /// <summary>True when <paramref name="prop"/> is a vector-family property the pips apply to.</summary>
        public static bool IsEligible(SerializedProperty prop)
        {
            if (prop == null) return false;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Quaternion:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Type-level mirror of <see cref="IsEligible(SerializedProperty)"/> for the designer's applicability gates.</summary>
        public static bool IsEligible(Type fieldType)
            => fieldType == typeof(Vector2) || fieldType == typeof(Vector3) || fieldType == typeof(Vector4)
            || fieldType == typeof(Vector2Int) || fieldType == typeof(Vector3Int) || fieldType == typeof(Quaternion);

        /// <summary>
        /// Whether a node's <paramref name="displayType"/> + field type gets the "Axis Color Pips" option at
        /// all — the SOLE authority, shared by the designer panel and the quick-edit popup so the two can't
        /// drift. Default covers the whole vector family; Vector Sliders renders only Vector3/Vector4 (every
        /// other type falls back to a plain field, which has no per-axis boxes to dress).
        /// </summary>
        public static bool AppliesTo(DisplayType displayType, Type fieldType)
        {
            switch (displayType)
            {
                case DisplayType.Default: return IsEligible(fieldType);
                case DisplayType.VectorSliders: return fieldType == typeof(Vector3) || fieldType == typeof(Vector4);
                default: return false;
            }
        }

        /// <summary>
        /// Dresses the composite field's X/Y/Z/W inputs with their axis pips. The inner component
        /// fields build LAZILY during binding, so this sweeps on every geometry change — idempotent:
        /// an input that already carries its pip is skipped.
        /// </summary>
        public static void Attach(VisualElement fieldRoot, SerializedProperty prop)
        {
            if (fieldRoot == null || !IsEligible(prop)) return;
            fieldRoot.RegisterCallback<GeometryChangedEvent>(_ => Apply(fieldRoot));
            Apply(fieldRoot);
        }

        /// <summary>
        /// Dresses ONE already-known field with the pip for <paramref name="axisLabel"/> ("X"/"Y"/"Z"/"W").
        /// For layouts that carry the axis name somewhere the label sweep can't see it — the Vector Sliders
        /// widget puts a bare Label at the head of each row and leaves its value field label-less, so
        /// <see cref="Attach(VisualElement, SerializedProperty)"/> would find nothing to match on.
        /// Idempotent, and re-armed on geometry because a field can build its input after this call.
        /// </summary>
        public static void Decorate(VisualElement field, string axisLabel)
        {
            if (field == null || !TryColorFor(axisLabel, out var color)) return;
            field.RegisterCallback<GeometryChangedEvent>(_ => DressField(field, color));
            DressField(field, color);
        }

        private static void Apply(VisualElement root)
        {
            // Label-driven, not composite-driven: Vector2/3 (and Ints) are composite fields with
            // X/Y/Z labels on their sub-fields, but Vector4's drawer is a FOLDOUT of stacked
            // FloatField rows, and the pips-on Quaternion route is a bare euler Vector3Field.
            // Every shape resolves the same way — any base-field label reading X/Y/Z/W dresses
            // its owning field's text input. The sweep is scoped to THIS property's element, so
            // unrelated fields can never match.
            root.Query<Label>(className: "unity-base-field__label").ForEach(label =>
            {
                if (!TryColorFor(label.text, out var color)) return;
                DressField(label.parent, color);
            });
        }

        /// <summary>Puts the pip inside <paramref name="field"/>'s text input and reserves its lane. A no-op
        /// when the field has no input yet, or already carries a pip.</summary>
        private static void DressField(VisualElement field, Color color)
        {
            var input = field?.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input == null || field.Q<VisualElement>(className: PipClass) != null) return;

            var pip = new VisualElement { pickingMode = PickingMode.Ignore };
            pip.AddToClassList(PipClass);
            pip.style.position = Position.Absolute;
            pip.style.right = 3f;
            pip.style.top = 3f;
            pip.style.bottom = 3f;
            pip.style.width = 3f;
            pip.style.borderTopLeftRadius = 1.5f; pip.style.borderTopRightRadius = 1.5f;
            pip.style.borderBottomLeftRadius = 1.5f; pip.style.borderBottomRightRadius = 1.5f;
            pip.style.backgroundColor = color;

            // Reserve the pip's lane so a LONG value never paints underneath it. Padding alone
            // isn't enough — UITK clips overflow at the PADDING box, so glyphs would still run
            // to the input's edge. Clip at the CONTENT box instead (padding then genuinely ends
            // the text 9px early: 3 inset + 3 pip + 3 gap)… which is also why the pip lives on
            // the FIELD root, not inside the input — inside, the content-box clip would erase it.
            // The input spans to the field's right edge, so the anchor point is the same.
            input.style.paddingRight = LaneWidth;
            input.style.overflow = Overflow.Hidden;
            input.style.unityOverflowClipBox = OverflowClipBox.ContentBox;
            // hierarchy.Add, NOT Add: a text field's contentContainer IS its input, so plain
            // Add() would redirect the pip right back inside the clip that erases it.
            field.hierarchy.Add(pip);
        }

        private static bool TryColorFor(string label, out Color color)
        {
            switch (label?.Trim())
            {
                case "X": color = Handles.xAxisColor; return true;
                case "Y": color = Handles.yAxisColor; return true;
                case "Z": color = Handles.zAxisColor; return true;
                // W has no axis — a neutral pip keeps the four-field rhythm without inventing one.
                case "W": color = WidgetPalette.Ink(0.45f); return true;
                default: color = default; return false;
            }
        }
    }
}
