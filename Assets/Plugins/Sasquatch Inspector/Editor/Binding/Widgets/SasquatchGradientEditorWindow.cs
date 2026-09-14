using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The freely-resizable editor opened from the inline Gradient widget's expand button. The big strip fills the
    /// top; the controls (blend mode, pin layout, palette row, hint) sit below. Edits the SAME gradient as the
    /// inline strip.
    ///
    /// Lifetime-safe: captures the target object + property PATH and owns its own SerializedObject; closes if the
    /// target dies. Mirrors <see cref="SasquatchCurveEditorWindow"/>.
    /// </summary>
    internal sealed class SasquatchGradientEditorWindow : EditorWindow
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

            var w = GetWindow<SasquatchGradientEditorWindow>(utility: false, title: "Gradient Editor");
            w.minSize = new Vector2(420f, 200f);
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

            if (_node == null || !ResolveProperty() || _prop.propertyType != SerializedPropertyType.Gradient)
            {
                var lost = new Label("This gradient is no longer available.");
                lost.style.opacity = 0.6f;
                root.Add(lost);
                return;
            }

            var control = new SasquatchGradientControl(_prop, _node, editable: true);
            control.PaletteColorsProvider = () => BindingFactory.GetGradientPaletteColors(_node);
            control.OnWarn = msg => { /* silent in the window */ };
            control.OpenPinRequested = (ch, idx) => BindingFactory.OpenGradientPinPopover(control, _node, ch, idx);
            if (_node.GradientPinLayout == GradientPinLayout.Merged) control.NormalizeForMerged();

            var well = new VisualElement();
            well.AddToClassList("sasq-gradient-well");
            BindingFactory.ApplyGradientWellBorder(well, _node);
            well.style.height = 44;
            well.style.marginBottom = 6;
            well.Add(control);
            root.Add(well);

            root.Add(BuildControlsBox(control));
        }

        private VisualElement BuildControlsBox(SasquatchGradientControl control)
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-gradient-controls-box");

            // Blend-mode segmented control (no expand button in the window). The ⋮ preset-library menu is hosted
            // at the top-right here rather than at the bottom of the ribbon.
            var actionRow = BindingFactory.BuildGradientActionRow(control, _node, includeExpand: false, onExpand: null, onLayoutEdited: null);
            actionRow.Add(BindingFactory.BuildGradientPresetMenuButton(control, RebuildContent));
            box.Add(actionRow);

            // Pin Layout toggle (Dual-Track ↔ Merged).
            var layoutRow = new VisualElement();
            layoutRow.AddToClassList("sasq-gradient-wrap-row");
            var layoutField = new EnumField("Pin Layout", _node.GradientPinLayout);
            layoutField.tooltip = "Dual Track: alpha pins on top, color pins on bottom (faithful, up to 8 each). " +
                                  "Merged: split-hemisphere pins editing color + alpha together.";
            layoutField.RegisterValueChangedCallback(evt =>
            {
                _node.GradientPinLayout = (GradientPinLayout)evt.newValue;
                if (_node.GradientPinLayout == GradientPinLayout.Merged) control.NormalizeForMerged();
                RebuildContent();
            });
            layoutRow.Add(layoutField);
            box.Add(layoutRow);

            // Preset ribbon (load / save) — the ⋮ library menu sits in the top action row above instead.
            box.Add(BindingFactory.BuildGradientPresetStrip(control, includeMenu: false));

            var hint = new Label("Double-click to add  •  drag to move  •  drag off to delete  •  click a color pin to recolor  •  click an alpha pin to set its value  •  Ctrl-click to invert alpha");
            hint.AddToClassList("sasq-gradient-hint");
            box.Add(hint);

            return box;
        }
    }
}
