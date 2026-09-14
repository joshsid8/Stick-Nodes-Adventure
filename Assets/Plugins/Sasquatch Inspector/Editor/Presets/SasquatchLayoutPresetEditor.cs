using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Inspector for <see cref="SasquatchLayoutPreset"/>. The asset is NOT edited inline — it's a non-editable card
    /// pointing to the dedicated Preset Editor window (where the widget type AND all deep per-widget knobs are set
    /// per type, with a live preview).
    /// </summary>
    [CustomEditor(typeof(SasquatchLayoutPreset))]
    public class SasquatchLayoutPresetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var preset = (SasquatchLayoutPreset)target;
            var root = new VisualElement();
            root.style.marginTop = 6;

            var title = new Label("Layout Presets");
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            root.Add(title);

            var msg = new Label(
                "Defines the default widget + configuration for each field type when importing a script into a " +
                "layout. Edit it in the Preset Editor window — not here.");
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.fontSize = 11;
            msg.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            msg.style.marginBottom = 10;
            root.Add(msg);

            var openBtn = new Button(() => SasquatchLayoutDesignerWindow.OpenForPreset(preset))
            {
                text = "Open Preset Editor"
            };
            openBtn.style.height = 28;
            openBtn.style.backgroundColor = new Color(0.18f, 0.35f, 0.58f, 1f);
            openBtn.style.color = Color.white;
            openBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(openBtn);
            EditorSpringFX.ButtonSpring(openBtn);

            var count = new Label($"{preset.Entries.Count} type categories configured.");
            count.style.fontSize = 10;
            count.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            count.style.marginTop = 8;
            root.Add(count);

            return root;
        }
    }
}
