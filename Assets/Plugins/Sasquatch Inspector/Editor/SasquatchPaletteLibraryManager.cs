using System.IO;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Loader/accessor for the project's single <see cref="SasquatchColorPaletteLibrary"/> asset.
    ///
    /// Mirrors <see cref="SasquatchSettingsManager"/>: <see cref="Current"/> is the hot-path accessor (called
    /// from every ColorPalette widget build) — it only READS the AssetDatabase, and when no asset exists it
    /// serves a cached in-memory library seeded with the three default palettes. So palettes are available
    /// before the asset is ever created, and the inspector hot path never writes to disk (no import → rebuild
    /// loops). <see cref="GetOrCreate"/> creates/persists the asset and is called when the user first edits or
    /// creates a palette (window, designer, or the widget's gear / "+" tile).
    /// </summary>
    public static class SasquatchPaletteLibraryManager
    {
        private static SasquatchColorPaletteLibrary _cached;
        private static bool _resolved;

        public static SasquatchColorPaletteLibrary Current
        {
            get
            {
                if (_resolved && _cached != null) return _cached;

                var loaded = AssetDatabase.LoadAssetAtPath<SasquatchColorPaletteLibrary>(SasquatchPaths.PaletteLibraryPath);
                if (loaded != null)
                {
                    _cached = loaded;
                }
                else if (_cached == null)
                {
                    // Transient default library stands in until the asset exists (created on first edit/create).
                    _cached = ScriptableObject.CreateInstance<SasquatchColorPaletteLibrary>();
                    _cached.hideFlags = HideFlags.DontSave;
                    SeedDefaults(_cached);
                }
                _resolved = true;
                return _cached;
            }
        }

        /// <summary>Loads the library asset, creating it (and its folder) on disk if missing. Carries over any
        /// edits made through the transient instance this session.</summary>
        public static SasquatchColorPaletteLibrary GetOrCreate()
        {
            string path = SasquatchPaths.PaletteLibraryPath;
            var library = AssetDatabase.LoadAssetAtPath<SasquatchColorPaletteLibrary>(path);
            if (library == null)
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Persist the transient instance in place (preserving any pre-asset edits and any UI already
                // referencing its palettes); otherwise create and seed a fresh one.
                if (_cached != null && !AssetDatabase.Contains(_cached))
                {
                    library = _cached;
                    library.hideFlags = HideFlags.None;
                    SeedDefaults(library);
                }
                else
                {
                    library = ScriptableObject.CreateInstance<SasquatchColorPaletteLibrary>();
                    SeedDefaults(library);
                }
                AssetDatabase.CreateAsset(library, path);
                AssetDatabase.SaveAssets();
            }

            _cached = library;
            _resolved = true;
            return library;
        }

        /// <summary>Drops the cached resolution so the next access re-resolves (asset created/moved/deleted).</summary>
        public static void InvalidateCache()
        {
            // Keep a transient instance alive (it may hold pre-asset edits); a persisted asset re-resolves.
            if (_cached != null && AssetDatabase.Contains(_cached)) _cached = null;
            _resolved = false;
        }

        private static void SeedDefaults(SasquatchColorPaletteLibrary library)
        {
            if (library == null) return;
            if (library.Palettes.Count == 0)
                library.Palettes.AddRange(SasquatchColorPaletteLibrary.DefaultPalettes());
            if (string.IsNullOrEmpty(library.DefaultPaletteId))
                library.DefaultPaletteId = SasquatchColorPaletteLibrary.DefaultPaletteIds.Nord;
        }
    }
}
