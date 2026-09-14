using System;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Per-page persistence for the Showcase Sandbox. When a demo page is opened in the Designer and saved, its
    /// layout is stored here (keyed by the page's stable key) INSTEAD of the shared per-type inspector registry —
    /// so each sandbox page remembers its OWN edits, they survive Unity restarts, and real inspectors are never
    /// touched. Backed by EditorPrefs as JSON via <see cref="EditorJsonUtility"/>, which (unlike JsonUtility)
    /// preserves the polymorphic [SerializeReference] LayoutNode graph.
    /// </summary>
    internal static class ShowcaseOverrides
    {
        // Per-project (not machine-global) so a sandbox edited in one project can't leak into another. Mirrors
        // WelcomeState's FNV-1a-of-dataPath project id (string.GetHashCode is per-process randomized on CoreCLR,
        // so it can't be used for a restart-stable key).
        private static readonly string Prefix = $"SasquatchInspector.Showcase.Override.{ProjectId()}/";

        private static string ProjectId()
        {
            string path = Application.dataPath ?? "";
            uint hash = 2166136261u;
            foreach (char c in path) { hash ^= c; hash *= 16777619u; }
            return hash.ToString("x8");
        }

        /// <summary>Raised (with the page key) right after a page's override is saved, so an open sandbox can
        /// refresh that page immediately rather than waiting to regain focus.</summary>
        public static event Action<string> OverrideSaved;

        private static string PrefKey(string key) => Prefix + key;

        /// <summary>True if a saved override exists for page <paramref name="key"/>.</summary>
        public static bool Has(string key) =>
            !string.IsNullOrEmpty(key) && EditorPrefs.HasKey(PrefKey(key));

        /// <summary>Persists <paramref name="root"/> as the saved layout for page <paramref name="key"/>.</summary>
        public static void Save(string key, LayoutNode root)
        {
            if (string.IsNullOrEmpty(key) || root == null) return;

            // A throwaway asset is just a serialization vehicle: ToJson captures its Root graph, then it's
            // destroyed. The managed [SerializeReference] nodes are plain CLR objects, so nothing is lost.
            var asset = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            asset.hideFlags = HideFlags.DontSave;
            asset.Root = root;
            string json = EditorJsonUtility.ToJson(asset);
            UnityEngine.Object.DestroyImmediate(asset);

            EditorPrefs.SetString(PrefKey(key), json);
            OverrideSaved?.Invoke(key);
        }

        /// <summary>Loads the saved layout root for page <paramref name="key"/>; false when none exists.</summary>
        public static bool TryLoad(string key, out LayoutNode root)
        {
            root = null;
            if (string.IsNullOrEmpty(key) || !EditorPrefs.HasKey(PrefKey(key))) return false;

            string json = EditorPrefs.GetString(PrefKey(key));
            if (string.IsNullOrEmpty(json)) return false;

            var asset = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            asset.hideFlags = HideFlags.DontSave;
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, asset);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(asset);
                return false;
            }

            root = asset.Root;
            UnityEngine.Object.DestroyImmediate(asset);
            return root != null;
        }

        /// <summary>Clears the saved override for page <paramref name="key"/> (revert to the code-built default).</summary>
        public static void Clear(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            EditorPrefs.DeleteKey(PrefKey(key));
        }
    }
}
