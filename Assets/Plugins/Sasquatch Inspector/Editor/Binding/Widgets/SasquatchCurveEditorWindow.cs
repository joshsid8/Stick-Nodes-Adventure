using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The freely-resizable editor opened from the inline Animation Curve widget's expand button. The big graph
    /// fills the window; all controls live in a collapsible "Controls" info-box at the bottom (preset swatch
    /// gallery, wrap modes, the selected-key panel, and a hint). Edits the SAME curve as the inline well.
    ///
    /// Lifetime-safe: captures the target object + property PATH and owns its own SerializedObject; closes if
    /// the target dies.
    /// </summary>
    internal sealed class SasquatchCurveEditorWindow : EditorWindow
    {
        private UnityEngine.Object _target;
        private string _propertyPath;
        private BindingNode _node;
        private SerializedObject _so;
        private SerializedProperty _prop;

        public static void Open(SerializedProperty prop, BindingNode node)
        {
            if (prop == null || prop.serializedObject == null) return;
            var target = prop.serializedObject.targetObject;
            if (target == null) return;

            var w = GetWindow<SasquatchCurveEditorWindow>(utility: false, title: "Curve Editor");
            w.minSize = new Vector2(440f, 320f);
            w.Bind(target, prop.propertyPath, node);
            w.Show();
            EditorSpringFX.PopIn(w.rootVisualElement);
        }

        public void Bind(UnityEngine.Object target, string propertyPath, BindingNode node)
        {
            _target = target;
            _propertyPath = propertyPath;
            _node = node;
            _so = null;
            _prop = null;
            RebuildContent();
        }

        private bool ResolveProperty()
        {
            if (_target == null) return false;
            if (_so == null || _so.targetObject == null)
            {
                _so = new SerializedObject(_target);
                _prop = _so.FindProperty(_propertyPath);
            }
            return _prop != null;
        }

        private void CreateGUI() => RebuildContent();

        private void OnDisable()
        {
            _so = null;
            _prop = null;
        }

        private void OnInspectorUpdate()
        {
            if (_target == null) Close();
        }

        private void RebuildContent()
        {
            var root = rootVisualElement;
            if (root == null) return;

            // Widget sheet + skin token sheet (this window's root sits outside any build root, so the
            // --sasq-* variables must be applied here to reach its widget classes).
            SasquatchSkin.ApplyWithWidgetSheet(root);

            root.Clear();
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            if (_node == null || !ResolveProperty() || _prop.propertyType != SerializedPropertyType.AnimationCurve)
            {
                var lost = new Label("This curve is no longer available.");
                lost.style.opacity = 0.6f;
                root.Add(lost);
                return;
            }

            var control = new SasquatchCurveControl(_prop, _node, editable: true) { AllowZoomPan = true };

            var well = new VisualElement();
            well.AddToClassList("sasq-curve-well");
            well.style.flexGrow = 1;
            well.style.minHeight = 120;
            well.style.marginBottom = 6;
            well.Add(control);
            root.Add(well);

            root.Add(BuildControlsBox(control));
        }

        private VisualElement BuildControlsBox(SasquatchCurveControl control)
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-curve-controls-box");

            var header = new VisualElement();
            header.AddToClassList("sasq-curve-controls-header");
            var caret = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 12, SasquatchIcons.FoldoutChevronTint);
            caret.pickingMode = PickingMode.Position;
            caret.style.marginRight = 4;
            header.Add(caret);
            header.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Settings, 12, WidgetPalette.AccentIcon));
            var title = new Label("Controls");
            title.style.marginLeft = 4;
            title.style.flexGrow = 1;
            header.Add(title);
            box.Add(header);

            var fold = new VisualElement();
            fold.AddToClassList("sasq-curve-controls-fold");

            var swatchLabel = new Label("Presets");
            swatchLabel.AddToClassList("sasq-curve-cap");
            swatchLabel.style.marginTop = 2;
            fold.Add(swatchLabel);
            fold.Add(BindingFactory.BuildPresetStrip(control, WidgetPalette.ResolveLegacy(
                _node.CurveLineColor, WidgetPalette.CurveLineDarkDefault, WidgetPalette.CurveLineLight)));

            fold.Add(BuildWrapRow(control));

            // reserveSpace: the tangent controls keep their layout slot even when no key is selected, so the graph
            // above doesn't resize as you click on / off keys.
            fold.Add(BindingFactory.BuildCurveActionRow(control, includeExpand: false, onExpand: null, reserveSpace: true));

            var hint = new Label("Double-click to add  •  drag to move  •  drag the blue handles to bend (Alt-drag to break)  •  Del to remove  •  scroll to zoom  •  drag empty to pan  •  F to frame");
            hint.AddToClassList("sasq-curve-hint");
            fold.Add(hint);

            box.Add(fold);

            bool expanded = true;
            void SetExpanded(bool e, bool animate)
            {
                expanded = e;
                EditorSpringFX.SpringCaret(caret, e, animate);
                if (animate) EditorSpringFX.Fold(fold, e);
                else fold.style.display = e ? DisplayStyle.Flex : DisplayStyle.None;
            }
            SetExpanded(true, false);
            header.RegisterCallback<ClickEvent>(_ => SetExpanded(!expanded, true));

            return box;
        }

        // Pre/Post wrap-mode dropdowns (loop / ping-pong / clamp at the start & end of the graph).
        private VisualElement BuildWrapRow(SasquatchCurveControl control)
        {
            var row = new VisualElement();
            row.AddToClassList("sasq-curve-wrap-row");

            var choices = new List<string> { "Clamp", "Loop", "Ping Pong" };

            var preField = new DropdownField("Pre-Wrap", choices, WrapLabel(control.PreWrap));
            preField.AddToClassList("sasq-curve-wrap-field");
            preField.tooltip = "How the curve behaves BEFORE the first key.";
            var postField = new DropdownField("Post-Wrap", choices, WrapLabel(control.PostWrap));
            postField.AddToClassList("sasq-curve-wrap-field");
            postField.tooltip = "How the curve behaves AFTER the last key.";

            void Apply() => control.SetWrapModes(WrapFromLabel(preField.value), WrapFromLabel(postField.value));
            preField.RegisterValueChangedCallback(_ => Apply());
            postField.RegisterValueChangedCallback(_ => Apply());

            row.Add(preField);
            row.Add(postField);
            return row;
        }

        private static string WrapLabel(WrapMode w)
        {
            switch (w)
            {
                case WrapMode.Loop: return "Loop";
                case WrapMode.PingPong: return "Ping Pong";
                default: return "Clamp";
            }
        }

        private static WrapMode WrapFromLabel(string s)
        {
            switch (s)
            {
                case "Loop": return WrapMode.Loop;
                case "Ping Pong": return WrapMode.PingPong;
                default: return WrapMode.ClampForever;
            }
        }
    }
}
