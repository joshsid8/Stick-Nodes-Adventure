using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A project-shared library of named AnimationCurve presets for the Animation Curve editor widget. This is
    /// Sasquatch Inspector's own store, since Unity's built-in curve preset library isn't public API. Factory
    /// presets are available in-memory immediately; the backing asset is only written once the user saves/edits a
    /// preset, so merely opening a curve never creates a file.
    /// </summary>
    internal sealed class SasquatchCurvePresetLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string name;
            public AnimationCurve curve = new AnimationCurve();
        }

        [SerializeField] private List<Entry> presets = new List<Entry>();
        public List<Entry> Presets => presets;

        private const string AssetFileName = "SasquatchCurvePresets.asset";

        /// <summary>
        /// This is the USER's preset library, so it lives under <see cref="SasquatchPaths.GeneratedDirectory"/>
        /// (the "User Data" folder) like every other user-owned store — settings, palettes, layouts.
        /// <para>It must NOT live inside the shipping tree. It used to sit at
        /// <c>&lt;package&gt;/Editor/SasquatchCurvePresets.asset</c>, which meant the file was exported with the
        /// package and carried a fixed GUID: a package UPDATE re-imported over that GUID and destroyed every
        /// preset the buyer had authored, with no prompt, no backup and no undo. See
        /// <see cref="LegacyAssetPath"/> and the migration in <see cref="Instance"/>.</para>
        /// </summary>
        private static string AssetPath => SasquatchPaths.GeneratedDirectory + "/" + AssetFileName;

        /// <summary>Where the library used to be written — inside the shipped package folder. Read once, on
        /// first access, so an existing library MOVES to User Data instead of being silently abandoned.</summary>
        private const string LegacyAssetPath = "Assets/Sasquatch Inspector/Editor/" + AssetFileName;

        private static SasquatchCurvePresetLibrary _instance;

        public static SasquatchCurvePresetLibrary Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = AssetDatabase.LoadAssetAtPath<SasquatchCurvePresetLibrary>(AssetPath);
                if (_instance == null) _instance = MigrateFromLegacyLocation(AssetPath, LegacyAssetPath) as SasquatchCurvePresetLibrary;
                if (_instance == null)
                {
                    _instance = CreateInstance<SasquatchCurvePresetLibrary>();
                    SeedFactory(_instance);
                    // In-memory only until the first Save() — opening a curve must not write an asset.
                }
                return _instance;
            }
        }

        /// <summary>
        /// Moves a preset library left at the old in-package path to <paramref name="newPath"/> under User Data,
        /// preserving its GUID (so nothing referencing it breaks) and its contents. Self-healing: it runs on the
        /// first access after an update and needs no menu item. Returns the moved asset, or null when there was
        /// nothing to move. Shared by the curve and gradient libraries.
        /// </summary>
        internal static ScriptableObject MigrateFromLegacyLocation(string newPath, string legacyPath)
        {
            var legacy = AssetDatabase.LoadAssetAtPath<ScriptableObject>(legacyPath);
            if (legacy == null) return null;

            EnsureFolder(Path.GetDirectoryName(newPath).Replace('\\', '/'));
            // MoveAsset keeps the .meta (and therefore the GUID), which matters: a plain copy+delete would
            // change identity and any future reference to the library would dangle.
            string error = AssetDatabase.MoveAsset(legacyPath, newPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[Sasquatch Inspector] Could not move the preset library out of the package " +
                                 $"folder ({error}). It will keep working, but a package update may overwrite it. " +
                                 $"Move '{legacyPath}' to '{newPath}' by hand to be safe.");
                return legacy; // still usable where it is
            }
            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(newPath);
        }

        // Creates every missing folder on an Assets-relative path. Deliberately NOT inside an
        // AssetDatabase batch scope — CreateFolder inside StartAssetEditing duplicates folders.
        internal static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string running = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = running + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(running, parts[i]);
                running = next;
            }
        }

        public void Add(string presetName, AnimationCurve curve)
        {
            presets.Add(new Entry { name = string.IsNullOrEmpty(presetName) ? "Preset" : presetName, curve = Clone(curve) });
            Save();
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= presets.Count) return;
            presets.RemoveAt(index);
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
            var temp = CreateInstance<SasquatchCurvePresetLibrary>();
            SeedFactory(temp);
            presets.AddRange(temp.presets);
            DestroyImmediate(temp);
            Save();
        }

        /// <summary>A stable, name-independent fingerprint of a curve's shape (rounded), used to skip duplicate
        /// imports. Two curves with the same keyframes + wrap modes share a signature regardless of name.</summary>
        public static string Signature(AnimationCurve c)
        {
            if (c == null) return "null";
            var sb = new System.Text.StringBuilder();
            sb.Append((int)c.preWrapMode).Append('|').Append((int)c.postWrapMode).Append(';');
            foreach (var k in c.keys)
            {
                sb.Append(F(k.time)).Append(',').Append(F(k.value)).Append(',')
                  .Append(F(k.inTangent)).Append(',').Append(F(k.outTangent)).Append(',')
                  .Append((int)k.weightedMode).Append(',').Append(F(k.inWeight)).Append(',').Append(F(k.outWeight)).Append(';');
            }
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Add the given presets, skipping any whose shape already exists in the library. Returns the count
        /// added and skipped. Saves once at the end (not per-add).</summary>
        public (int added, int skipped) AddRangeUnique(IEnumerable<(string name, AnimationCurve curve)> items)
        {
            if (items == null) return (0, 0);
            var seen = new HashSet<string>();
            foreach (var e in presets) if (e?.curve != null) seen.Add(Signature(e.curve));

            int added = 0, skipped = 0;
            foreach (var (name, curve) in items)
            {
                if (curve == null) { skipped++; continue; }
                if (!seen.Add(Signature(curve))) { skipped++; continue; }
                presets.Add(new Entry { name = string.IsNullOrEmpty(name) ? "Imported" : name, curve = Clone(curve) });
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

        public string AssetLocation => AssetDatabase.Contains(this) ? AssetDatabase.GetAssetPath(this) : "(not saved yet)";

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
            EnsureFolder(dir);                                   // User Data may not exist yet on a fresh install
            if (!AssetDatabase.IsValidFolder(dir)) dir = "Assets"; // last-ditch fallback if that somehow failed
            var path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + AssetFileName);
            AssetDatabase.CreateAsset(this, path);
        }

        private static AnimationCurve Clone(AnimationCurve c)
        {
            var nc = new AnimationCurve(c != null ? c.keys : Array.Empty<Keyframe>());
            if (c != null) { nc.preWrapMode = c.preWrapMode; nc.postWrapMode = c.postWrapMode; }
            return nc;
        }

        // The built-in starter set (mirrors the common shapes Unity ships, authored over 0..1 x 0..1).
        private static void SeedFactory(SasquatchCurvePresetLibrary lib)
        {
            lib.presets.Add(new Entry { name = "Linear", curve = AnimationCurve.Linear(0f, 0f, 1f, 1f) });
            lib.presets.Add(new Entry { name = "Ease In-Out", curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f) });
            lib.presets.Add(new Entry { name = "Ease In", curve = Ease(0f, 2f) });
            lib.presets.Add(new Entry { name = "Ease Out", curve = Ease(2f, 0f) });
            lib.presets.Add(new Entry { name = "Constant 0", curve = AnimationCurve.Constant(0f, 1f, 0f) });
            lib.presets.Add(new Entry { name = "Constant 1", curve = AnimationCurve.Constant(0f, 1f, 1f) });
            lib.presets.Add(new Entry { name = "Flat", curve = AnimationCurve.Linear(0f, 0.5f, 1f, 0.5f) });
            lib.presets.Add(new Entry { name = "Step", curve = Step() });
            lib.presets.Add(new Entry { name = "Bell", curve = Bell() });
        }

        private static AnimationCurve Ease(float outMul, float inMul)
        {
            var k0 = new Keyframe(0f, 0f) { outTangent = 1f * outMul };
            var k1 = new Keyframe(1f, 1f) { inTangent = 1f * inMul };
            return new AnimationCurve(k0, k1);
        }

        private static AnimationCurve Step()
        {
            var c = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Constant);
            }
            return c;
        }

        private static AnimationCurve Bell()
        {
            var c = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            return c;
        }
    }
}
