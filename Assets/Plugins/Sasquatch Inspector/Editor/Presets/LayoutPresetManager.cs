using System.IO;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Loader/accessor for the active <see cref="SasquatchLayoutPreset"/>. Mirrors
    /// <see cref="SasquatchSettingsManager"/>: <see cref="Current"/> only READS (serving a transient,
    /// factory-seeded instance until an asset exists, so importers work before anything is created),
    /// <see cref="GetOrCreate"/> materializes the asset on disk.
    /// </summary>
    public static class LayoutPresetManager
    {
        private static SasquatchLayoutPreset _cached;
        private static bool _resolved;

        public static SasquatchLayoutPreset Current
        {
            get
            {
                if (_resolved && _cached != null) return _cached;

                // Prefer the project-wide preset explicitly assigned in Project Settings.
                var settings = SasquatchSettingsManager.Current;
                if (settings != null && settings.LayoutPreset != null)
                {
                    _cached = settings.LayoutPreset;
                    _resolved = true;
                    return _cached;
                }

                var loaded = AssetDatabase.LoadAssetAtPath<SasquatchLayoutPreset>(SasquatchPaths.PresetPath);
                if (loaded != null)
                {
                    _cached = loaded;
                }
                else if (_cached == null)
                {
                    // Transient, factory-seeded defaults stand in until an asset is created — so importers work out
                    // of the box and the read hot path never writes to disk.
                    _cached = ScriptableObject.CreateInstance<SasquatchLayoutPreset>();
                    _cached.hideFlags = HideFlags.DontSave;
                    LayoutPresetDefaults.Populate(_cached);
                }
                _resolved = true;
                return _cached;
            }
        }

        /// <summary>Loads the preset asset, creating it (folder + recommended defaults) on disk if missing.</summary>
        public static SasquatchLayoutPreset GetOrCreate()
        {
            string path = SasquatchPaths.PresetPath;
            var preset = AssetDatabase.LoadAssetAtPath<SasquatchLayoutPreset>(path);
            if (preset == null)
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                preset = ScriptableObject.CreateInstance<SasquatchLayoutPreset>();
                // Carry over any pre-asset edits made through the transient instance this session; else seed
                // defaults. EVERY tier moves across, not just the per-type entries — the shipped defaults seed
                // collection-context overrides, per-widget styling and the structural container choices too, so
                // carrying only Entries would silently downgrade the very first asset a project materializes.
                if (_cached != null && !AssetDatabase.Contains(_cached) && _cached.Entries.Count > 0)
                {
                    preset.Entries = _cached.Entries;
                    preset.ContextEntries = _cached.ContextEntries;
                    preset.WidgetEntries = _cached.WidgetEntries;
                    preset.DefaultHeaderContainerType = _cached.DefaultHeaderContainerType;
                    preset.DefaultRootContainerType = _cached.DefaultRootContainerType;
                }
                else
                {
                    LayoutPresetDefaults.Populate(preset);
                }

                AssetDatabase.CreateAsset(preset, path);
                AssetDatabase.SaveAssets();
            }

            _cached = preset;
            _resolved = true;
            return preset;
        }

        /// <summary>Creates a BRAND-NEW preset asset (recommended defaults) under the User Data folder at a unique
        /// path and returns it — used by the settings-page "+" button. Does not touch the active-preset reference.</summary>
        public static SasquatchLayoutPreset CreateNewAsset()
        {
            string directory = SasquatchPaths.GeneratedDirectory;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/SasquatchLayoutPreset.asset");
            var preset = ScriptableObject.CreateInstance<SasquatchLayoutPreset>();
            LayoutPresetDefaults.Populate(preset);
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            return preset;
        }

        /// <summary>
        /// Ensures a persisted active preset exists AND is referenced by project settings; returns it. Used by
        /// the Tools menu / SO inspector "Open Preset Editor" entry points so editing always targets a real asset.
        /// </summary>
        public static SasquatchLayoutPreset EnsureActivePreset()
        {
            var settings = SasquatchSettingsManager.Current;
            if (settings != null && settings.LayoutPreset != null)
                return BackfillMissingCategories(settings.LayoutPreset);

            var preset = GetOrCreate();
            BackfillMissingCategories(preset);
            var persistedSettings = SasquatchSettingsManager.GetOrCreate();
            if (persistedSettings.LayoutPreset == null)
            {
                persistedSettings.LayoutPreset = preset;
                EditorUtility.SetDirty(persistedSettings);
                AssetDatabase.SaveAssets();
                SasquatchSettingsManager.InvalidateCache();
            }
            InvalidateCache();
            return preset;
        }

        /// <summary>
        /// APPENDS an entry for any <see cref="PresetCategory"/> the asset does not yet carry, using the same
        /// recommended default a freshly created preset would get, and leaves every existing row untouched.
        ///
        /// <para><b>Why this exists.</b> A preset asset stores one entry per category and is written ONCE, at
        /// creation. <see cref="PresetCategory"/> is append-only, so a project that made its preset before a
        /// category was added keeps an asset that is simply short a row — <c>Find</c> returns null for it, the
        /// Preset Editor has no row to show, and that category silently gets no house style. That was the
        /// state every existing project landed in when <c>Dictionary</c> was appended on 2026-09-02.</para>
        ///
        /// <para>Self-healing on resolve rather than through a "repair presets" menu item is the rule here
        /// (hard rule #5), and it is safe because it is strictly additive: it never rewrites, reorders or
        /// re-enables a row the user has tuned, so a tester's existing preset keeps every choice they made
        /// and merely gains the rows that did not exist when they made them.</para>
        /// </summary>
        internal static SasquatchLayoutPreset BackfillMissingCategories(SasquatchLayoutPreset preset)
        {
            if (preset == null) return null;

            bool added = false;
            foreach (PresetCategory c in System.Enum.GetValues(typeof(PresetCategory)))
            {
                if (preset.Find(c) != null) continue;
                preset.Entries.Add(new LayoutPresetEntry
                {
                    category = c,
                    enabled = LayoutPresetDefaults.EnabledByDefault(c),
                    template = LayoutPresetDefaults.BuildTemplate(c),
                });
                added = true;
            }

            // Only touch the asset when something actually changed — an unconditional SetDirty on every
            // resolve would mark the project dirty on load and re-save a file nobody edited.
            if (added && AssetDatabase.Contains(preset))
            {
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssetIfDirty(preset);
            }
            return preset;
        }

        /// <summary>Drops the cached resolution so the next access re-resolves (asset created/moved/deleted).</summary>
        public static void InvalidateCache()
        {
            // Keep a transient instance alive (it may hold pre-asset tweaks); a persisted asset re-resolves.
            if (_cached != null && AssetDatabase.Contains(_cached)) _cached = null;
            _resolved = false;
        }
    }
}
