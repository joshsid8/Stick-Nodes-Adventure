using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Demo.Editor
{
    /// <summary>
    /// A deliberately ordinary hand-written IMGUI inspector for <see cref="DemoLegacyTurret"/> — the kind a
    /// third-party package ships, and exactly the obstacle Force Takeover exists to get past.
    ///
    /// The banner is the exhibit. A buyer selecting this object sees, in the component's OWN inspector, that
    /// it is not being drawn by Sasquatch and what to do about it. Then they flip Force Takeover on and this
    /// entire editor is displaced by the designed layout — no edit to this file, no attribute, nothing they
    /// have to own.
    ///
    /// Public editor API only, per Asset Store guideline 2.5.g. Nothing here reflects into Unity internals.
    /// </summary>
    [CustomEditor(typeof(DemoLegacyTurret))]
    public class DemoLegacyTurretInspector : UnityEditor.Editor
    {
        private static readonly string[] Sections = { "Identity", "Ballistics", "Heat" };
        private int _section;

        public override void OnInspectorGUI()
        {
            DrawBanner();

            serializedObject.Update();
            _section = GUILayout.Toolbar(_section, Sections);
            EditorGUILayout.Space();

            switch (_section)
            {
                case 0:
                    Draw("turretId");
                    Draw("mode");
                    break;
                case 1:
                    Draw("range");
                    Draw("fireRate");
                    Draw("accuracy");
                    Draw("arcMinMax");
                    Draw("muzzleOffset");
                    Draw("beamColour");
                    break;
                default:
                    Draw("health");
                    Draw("maxHealth");
                    Draw("overheats");
                    using (new EditorGUI.DisabledScope(!((DemoLegacyTurret)target).overheats))
                        Draw("coolDownSeconds");
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        // The point of the whole exhibit: say plainly that this is NOT Sasquatch, and what to do about it.
        private void DrawBanner()
        {
            var box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

            var previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.65f, 0.25f);
            EditorGUILayout.BeginVertical(box);
            GUI.backgroundColor = previous;

            EditorGUILayout.LabelField("This inspector is NOT Sasquatch Inspector", title);
            EditorGUILayout.LabelField(
                "This component ships with its own hand-written [CustomEditor], so Unity draws that instead " +
                "of any layout you design. Turn on Force Takeover for this type and Sasquatch displaces it — " +
                "your designed layout renders here instead, and this file is never touched.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(
                "Right-click the component header ▸ Design Inspector Layout, then enable Force Takeover.",
                EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void Draw(string propertyPath)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property != null) EditorGUILayout.PropertyField(property, true);
        }
    }
}
