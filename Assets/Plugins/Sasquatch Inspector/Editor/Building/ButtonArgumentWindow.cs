using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Small UI Toolkit form shown when an action button bound to a method WITH parameters is clicked. It collects
    /// one value per parameter, then hands them back to the caller to invoke. Editor-only and transient — the
    /// values are remembered for the session via <see cref="ButtonInvoker.RememberArgs"/>.
    ///
    /// UITK rather than IMGUI deliberately: this is the only window an end user meets on an ordinary action
    /// (clicking a parameterized button), and an IMGUI form draws its text through the legacy GUIStyle pipeline,
    /// reading visibly softer than the inspector it popped out of — worst on the bold "Enter arguments" heading.
    /// </summary>
    internal class ButtonArgumentWindow : EditorWindow
    {
        private ButtonNode _node;
        private ParameterInfo[] _params;
        private object[] _values;
        private Action<object[]> _onRun;

        /// <summary>Opens the argument-entry window for a parameterized action button; invokes <paramref name="onRun"/> with the collected values.</summary>
        public static void Show(string title, ButtonNode node, ParameterInfo[] parameters,
            object[] initialValues, Action<object[]> onRun)
        {
            if (parameters == null) return;

            var window = CreateInstance<ButtonArgumentWindow>();
            window.titleContent = new GUIContent(string.IsNullOrEmpty(title) ? "Arguments" : title);
            window._node = node;
            window._params = parameters;
            window._values = initialValues != null && initialValues.Length == parameters.Length
                ? initialValues
                : new object[parameters.Length];
            window._onRun = onRun;

            float height = 74f + parameters.Length * 22f + 34f;
            SasquatchDialogChrome.CenterOnFocused(window, SasquatchDialogChrome.DialogWidth, height);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            if (root == null) return;

            // A domain reload can revive the window with its non-serializable state (ParameterInfo[], the run
            // callback) gone — there is nothing left to collect, so close rather than draw a dead form.
            if (_params == null || _onRun == null)
            {
                EditorApplication.delayCall += Close;
                return;
            }

            SasquatchDialogChrome.PrepareRoot(root);
            root.Add(SasquatchDialogChrome.Heading("Enter arguments"));

            var fields = new ScrollView(ScrollViewMode.Vertical);
            fields.style.flexGrow = 1;
            fields.style.minHeight = 0;
            for (int i = 0; i < _params.Length; i++)
            {
                int index = i; // captured per row — the callbacks below outlive this loop
                string label = ObjectNames.NicifyVariableName(_params[i].Name);
                var row = BuildField(label, _params[i].ParameterType, _values[i], v => _values[index] = v);
                if (row != null) fields.Add(row);
            }
            root.Add(fields);

            root.Add(SasquatchDialogChrome.ButtonRow("Cancel", Cancel, "Run", Submit));
            SasquatchDialogChrome.WireKeys(root, Submit, Cancel);

            // Focus the first input so the form is type-ready.
            root.schedule.Execute(() =>
            {
                var first = fields.Q<VisualElement>(className: "unity-base-field");
                first?.Q<VisualElement>(className: "unity-base-field__input")?.Focus();
            });

            EditorSpringFX.PopIn(root);
        }

        private void Cancel() => Close();

        private void Submit()
        {
            var run = _onRun;
            var values = _values;
            if (run == null) { Close(); return; }
            ButtonInvoker.RememberArgs(_node, values);
            // Registered BEFORE Close() for the same reason as ButtonConfirmWindow.Confirm — queue the
            // continuation while this instance is still alive. Deferred either way: the run may pop the confirm
            // dialog next, which must not happen inside this window's teardown.
            EditorApplication.delayCall += () => run(values);
            Close();
        }

        /// <summary>
        /// One labelled input per supported parameter type, reporting edits through <paramref name="commit"/>.
        /// Mirrors <see cref="ButtonInvoker.IsSupportedParameterType"/> — anything it accepts must build a row
        /// here, or the button would prompt for a value it can never collect.
        /// </summary>
        private static VisualElement BuildField(string label, Type t, object value, Action<object> commit)
        {
            if (t == typeof(int)) return Wire(new IntegerField(label) { value = value is int i ? i : 0 }, commit);
            if (t == typeof(long)) return Wire(new LongField(label) { value = value is long l ? l : 0L }, commit);
            if (t == typeof(float)) return Wire(new FloatField(label) { value = value is float f ? f : 0f }, commit);
            if (t == typeof(double)) return Wire(new DoubleField(label) { value = value is double d ? d : 0d }, commit);
            if (t == typeof(bool)) return Wire(new Toggle(label) { value = value is bool b && b }, commit);
            if (t == typeof(string)) return Wire(new TextField(label) { value = value as string ?? string.Empty }, commit);
            if (t == typeof(Vector2)) return Wire(new Vector2Field(label) { value = value is Vector2 v2 ? v2 : Vector2.zero }, commit);
            if (t == typeof(Vector3)) return Wire(new Vector3Field(label) { value = value is Vector3 v3 ? v3 : Vector3.zero }, commit);
            if (t == typeof(Vector4)) return Wire(new Vector4Field(label) { value = value is Vector4 v4 ? v4 : Vector4.zero }, commit);
            if (t == typeof(Color)) return Wire(new ColorField(label) { value = value is Color c ? c : Color.white }, commit);
            if (t == typeof(Rect)) return Wire(new RectField(label) { value = value is Rect r ? r : default(Rect) }, commit);
            if (t == typeof(Bounds)) return Wire(new BoundsField(label) { value = value is Bounds bo ? bo : default(Bounds) }, commit);
            if (t.IsEnum)
            {
                var current = value is Enum en ? en : (Enum)Enum.GetValues(t).GetValue(0);
                commit(current); // seed: the popup shows this before any edit, so _values must agree
                // A [Flags] parameter needs the MASK field. A plain EnumField is single-select, so a
                // combined default (seeded correctly above) collapsed to one member on the first
                // interaction with no UI path back to the combination — and the session arg cache then
                // remembered the collapsed value for every later click.
                if (Attribute.IsDefined(t, typeof(FlagsAttribute)))
                    return Wire(new EnumFlagsField(label, current), commit);
                return Wire(new EnumField(label, current), commit);
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                var of = new ObjectField(label) { objectType = t, allowSceneObjects = true, value = value as UnityEngine.Object };
                return Wire(of, commit);
            }

            // Unsupported: show it read-only rather than silently dropping the parameter, so the mismatch with
            // IsSupportedParameterType is visible instead of mysterious.
            var unsupported = new TextField(label) { value = $"(unsupported type: {t.Name})" };
            unsupported.SetEnabled(false);
            return unsupported;
        }

        private static VisualElement Wire<T>(BaseField<T> field, Action<object> commit)
        {
            field.RegisterValueChangedCallback(evt => commit(evt.newValue));
            return field;
        }
    }
}
