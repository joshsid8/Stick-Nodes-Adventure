using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A project-shared library of named Gradient presets for the Gradient editor widget — the gradient analog of
    /// <see cref="SasquatchCurvePresetLibrary"/>. Factory presets are available in-memory immediately; the backing
    /// asset is only written once the user saves/edits a preset, so merely opening a gradient never creates a file.
    /// </summary>
    internal sealed class SasquatchGradientPresetLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string name;
            public Gradient gradient = new Gradient();
        }

        [SerializeField] private List<Entry> presets = new List<Entry>();
        public List<Entry> Presets => presets;

        private const string AssetFileName = "SasquatchGradientPresets.asset";

        /// <summary>
        /// The USER's gradient library, so it lives under <see cref="SasquatchPaths.GeneratedDirectory"/>
        /// ("User Data") like every other user-owned store. It must NOT live inside the shipping tree: at the
        /// old in-package path the file exported with the package and carried a fixed GUID, so a package UPDATE
        /// re-imported over it and destroyed every gradient the buyer had authored. Mirrors
        /// <see cref="SasquatchCurvePresetLibrary"/>; the move helpers are shared with it.
        /// </summary>
        private static string AssetPath => SasquatchPaths.GeneratedDirectory + "/" + AssetFileName;

        /// <summary>Where the library used to be written — inside the shipped package folder.</summary>
        private const string LegacyAssetPath = "Assets/Sasquatch Inspector/Editor/" + AssetFileName;

        private static SasquatchGradientPresetLibrary _instance;

        public static SasquatchGradientPresetLibrary Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = AssetDatabase.LoadAssetAtPath<SasquatchGradientPresetLibrary>(AssetPath);
                if (_instance == null)
                    _instance = SasquatchCurvePresetLibrary.MigrateFromLegacyLocation(AssetPath, LegacyAssetPath)
                                as SasquatchGradientPresetLibrary;
                if (_instance == null)
                {
                    _instance = CreateInstance<SasquatchGradientPresetLibrary>();
                    SeedFactory(_instance);
                    // In-memory only until the first Save() — opening a gradient must not write an asset.
                }
                return _instance;
            }
        }

        public void Add(string presetName, Gradient gradient)
        {
            presets.Add(new Entry { name = string.IsNullOrEmpty(presetName) ? "Preset" : presetName, gradient = Clone(gradient) });
            Save();
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= presets.Count) return;
            presets.RemoveAt(index);
            Save();
        }

        /// <summary>Reorder a preset from one position to another (drag-to-reorder in the strip).</summary>
        public void Move(int from, int to)
        {
            if (from < 0 || from >= presets.Count) return;
            to = Mathf.Clamp(to, 0, presets.Count - 1);
            if (from == to) return;
            var e = presets[from];
            presets.RemoveAt(from);
            presets.Insert(to, e);
            Save();
        }

        public void Rename(int index, string newName)
        {
            if (index < 0 || index >= presets.Count || string.IsNullOrEmpty(newName)) return;
            presets[index].name = newName;
            Save();
        }

        public void AddFactoryPresets()
        {
            var temp = CreateInstance<SasquatchGradientPresetLibrary>();
            SeedFactory(temp);
            presets.AddRange(temp.presets);
            DestroyImmediate(temp);
            Save();
        }

        /// <summary>A stable, name-independent fingerprint of a gradient (mode + rounded color/alpha keys), used to
        /// skip duplicate imports.</summary>
        public static string Signature(Gradient g)
        {
            if (g == null) return "null";
            var sb = new System.Text.StringBuilder();
            sb.Append((int)g.mode).Append(';');
            foreach (var ck in g.colorKeys)
                sb.Append(F(ck.color.r)).Append(',').Append(F(ck.color.g)).Append(',').Append(F(ck.color.b)).Append('@').Append(F(ck.time)).Append(';');
            sb.Append('|');
            foreach (var ak in g.alphaKeys)
                sb.Append(F(ak.alpha)).Append('@').Append(F(ak.time)).Append(';');
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Add the given presets, skipping any whose shape already exists. Returns (added, skipped). Saves once.</summary>
        public (int added, int skipped) AddRangeUnique(IEnumerable<(string name, Gradient gradient)> items)
        {
            if (items == null) return (0, 0);
            var seen = new HashSet<string>();
            foreach (var e in presets) if (e?.gradient != null) seen.Add(Signature(e.gradient));

            int added = 0, skipped = 0;
            foreach (var (name, gradient) in items)
            {
                if (gradient == null) { skipped++; continue; }
                if (!seen.Add(Signature(gradient))) { skipped++; continue; }
                presets.Add(new Entry { name = string.IsNullOrEmpty(name) ? "Imported" : name, gradient = Clone(gradient) });
                added++;
            }
            if (added > 0) Save();
            return (added, skipped);
        }

        public void ResetToFactory()
        {
            presets.Clear();
            SeedFactory(this);
            Save();
        }

        public void Reveal()
        {
            EnsureAsset();
            EditorGUIUtility.PingObject(this);
            Selection.activeObject = this;
        }

        private void Save()
        {
            EnsureAsset();
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        private void EnsureAsset()
        {
            if (AssetDatabase.Contains(this)) return;
            var dir = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            SasquatchCurvePresetLibrary.EnsureFolder(dir);       // User Data may not exist yet on a fresh install
            if (!AssetDatabase.IsValidFolder(dir)) dir = "Assets"; // last-ditch fallback if that somehow failed
            var path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + AssetFileName);
            AssetDatabase.CreateAsset(this, path);
        }

        public static Gradient Clone(Gradient g)
        {
            var ng = new Gradient();
            if (g != null) { ng.SetKeys(g.colorKeys, g.alphaKeys); ng.mode = g.mode; }
            return ng;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        private static Gradient Make(GradientColorKey[] ck, GradientAlphaKey[] ak, GradientMode mode = GradientMode.Blend)
        {
            var g = new Gradient();
            g.SetKeys(ck, ak);
            g.mode = mode;
            return g;
        }

        private static GradientAlphaKey[] Opaque => new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

        // A curated starter set covering the common gradient shapes.
        private static void SeedFactory(SasquatchGradientPresetLibrary lib)
        {
            lib.presets.Add(new Entry
            {
                name = "Grayscale",
                gradient = Make(new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) }, Opaque)
            });
            lib.presets.Add(new Entry
            {
                name = "Fire",
                gradient = Make(new[]
                {
                    new GradientColorKey(Hex("#000000"), 0f), new GradientColorKey(Hex("#7A0000"), 0.25f),
                    new GradientColorKey(Hex("#FF3B00"), 0.5f), new GradientColorKey(Hex("#FFC400"), 0.75f),
                    new GradientColorKey(Hex("#FFFBE6"), 1f)
                }, Opaque)
            });
            lib.presets.Add(new Entry
            {
                name = "Rainbow",
                gradient = Make(new[]
                {
                    new GradientColorKey(Hex("#FF3B30"), 0f), new GradientColorKey(Hex("#FFCC00"), 0.2f),
                    new GradientColorKey(Hex("#34C759"), 0.4f), new GradientColorKey(Hex("#32ADE6"), 0.6f),
                    new GradientColorKey(Hex("#5856D6"), 0.8f), new GradientColorKey(Hex("#AF52DE"), 1f)
                }, Opaque)
            });
            lib.presets.Add(new Entry
            {
                name = "Sunset",
                gradient = Make(new[]
                {
                    new GradientColorKey(Hex("#2A2A72"), 0f), new GradientColorKey(Hex("#7B2FF7"), 0.35f),
                    new GradientColorKey(Hex("#F72C5B"), 0.7f), new GradientColorKey(Hex("#FFC371"), 1f)
                }, Opaque)
            });
            lib.presets.Add(new Entry
            {
                name = "Ocean",
                gradient = Make(new[]
                {
                    new GradientColorKey(Hex("#0B2447"), 0f), new GradientColorKey(Hex("#19376D"), 0.4f),
                    new GradientColorKey(Hex("#576CBC"), 0.75f), new GradientColorKey(Hex("#A5D7E8"), 1f)
                }, Opaque)
            });
            lib.presets.Add(new Entry
            {
                name = "Cool → Warm",
                gradient = Make(new[]
                {
                    new GradientColorKey(Hex("#3B4CC0"), 0f), new GradientColorKey(Hex("#DDDDDD"), 0.5f),
                    new GradientColorKey(Hex("#B40426"), 1f)
                }, Opaque)
            });
            lib.presets.Add(new Entry
            {
                name = "Fade Out",
                gradient = Make(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) })
            });
            lib.presets.Add(new Entry
            {
                name = "Stepped",
                gradient = Make(new[]
                {
                    new GradientColorKey(Hex("#264653"), 0f), new GradientColorKey(Hex("#2A9D8F"), 0.33f),
                    new GradientColorKey(Hex("#E9C46A"), 0.66f), new GradientColorKey(Hex("#E76F51"), 1f)
                }, Opaque, GradientMode.Fixed)
            });
        }
    }
}
