using System.IO;
using SasquatchInspector.Runtime;
using SasquatchInspector.Samples;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Owns the shippable sample asset the Tutorial "graduates" the user onto. Creates a real, selectable
    /// <see cref="PlayerStats"/> asset on demand (the <c>LayoutPresetManager.GetOrCreate</c> pattern) so the final
    /// step can open the REAL Layout Designer on a real object — the authentic "select it, click the pencil"
    /// flow — without the user needing a scene or a script of their own yet.
    /// </summary>
    internal static class TutorialSample
    {
        /// <summary>Finds the shipped sample asset, creating it under <c>User Data/Samples/</c> if absent.</summary>
        public static PlayerStats GetOrCreateSampleAsset()
        {
            // An existing instance anywhere in the project wins (survives relocation), like SasquatchPaths does.
            foreach (var guid in AssetDatabase.FindAssets("t:PlayerStats"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var existing = AssetDatabase.LoadAssetAtPath<PlayerStats>(path);
                if (existing != null) return existing;
            }

            string generated = SasquatchPaths.GeneratedDirectory;     // <PackageRoot>/User Data
            string samples = generated + "/Samples";
            if (!AssetDatabase.IsValidFolder(generated))
                AssetDatabase.CreateFolder(
                    Path.GetDirectoryName(generated).Replace('\\', '/'), Path.GetFileName(generated));
            if (!AssetDatabase.IsValidFolder(samples))
                AssetDatabase.CreateFolder(generated, "Samples");

            var asset = ScriptableObject.CreateInstance<PlayerStats>();
            AssetDatabase.CreateAsset(asset, samples + "/PlayerStats.asset");
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Pings (and selects) the sample asset in the Project window so the user can see the real file
        /// the Tutorial is teaching against — creating it on demand if it doesn't exist yet.</summary>
        public static void PingSampleAsset()
        {
            var asset = GetOrCreateSampleAsset();
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>Pings (and selects) the Demo.unity scene asset in the Project window.</summary>
        public static void PingDemoScene()
        {
            foreach (var guid in AssetDatabase.FindAssets("Demo t:Scene"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Demo.unity", System.StringComparison.OrdinalIgnoreCase))
                {
                    var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                    if (sceneAsset != null)
                    {
                        Selection.activeObject = sceneAsset;
                        EditorGUIUtility.PingObject(sceneAsset);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Selects the sample asset and opens the real Layout Designer seeded with the Tutorial's finished design
        /// (the same "After" tree the final page previews) — the Tutorial's graduation. The layout is handed off
        /// in-memory and cloned by the designer, so nothing is written to disk unless the user Saves; a Save then
        /// persists a normal PlayerStats layout (no showcase-override key), exactly like doing it on their own
        /// script. Mirrors the Showcase's "Open in Designer": build → hand off → drop our copy.
        /// </summary>
        public static void OpenInRealDesigner()
        {
            var asset = GetOrCreateSampleAsset();
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            var layout = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            layout.Root = TutorialLayouts.PlayerStatsAfter();
            SasquatchLayoutDesignerWindow.OpenWindowWithLayout(layout, asset, typeof(PlayerStats));
            Object.DestroyImmediate(layout);   // the designer holds its own clone; drop ours
        }
    }
}
