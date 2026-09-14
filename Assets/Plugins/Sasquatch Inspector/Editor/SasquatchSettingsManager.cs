using System.IO;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Loader/accessor for the project-wide <see cref="SasquatchSettings"/> asset.
    ///
    /// <see cref="Current"/> is the hot-path accessor (called from every inspector rebuild): it only
    /// ever READS the AssetDatabase, and when no asset exists it serves a cached in-memory instance
    /// carrying the defaults — so default behavior works before the asset is ever created, and the
    /// inspector hot path never writes to disk (no import → rebuild feedback loops).
    /// <see cref="GetOrCreate"/> creates the asset on disk and is called from entry points that must persist a
    /// setting (the settings page, and establishing the active Layout Preset).
    /// </summary>
    public static class SasquatchSettingsManager
    {
        private static SasquatchSettings _cached;
        private static bool _resolved;

        public static SasquatchSettings Current
        {
            get
            {
                if (_resolved && _cached != null) return _cached;

                var loaded = AssetDatabase.LoadAssetAtPath<SasquatchSettings>(SasquatchPaths.SettingsPath);
                if (loaded != null)
                {
                    _cached = loaded;
                }
                else if (_cached == null)
                {
                    // Transient defaults stand in until the asset exists (created from the settings page).
                    _cached = ScriptableObject.CreateInstance<SasquatchSettings>();
                    _cached.hideFlags = HideFlags.DontSave;
                }
                _resolved = true;
                return _cached;
            }
        }

        /// <summary>Loads the settings asset, creating it (and its folder) on disk if missing.</summary>
        public static SasquatchSettings GetOrCreate()
        {
            string path = SasquatchPaths.SettingsPath;
            var settings = AssetDatabase.LoadAssetAtPath<SasquatchSettings>(path);
            if (settings == null)
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                settings = ScriptableObject.CreateInstance<SasquatchSettings>();
                // Carry over any pre-asset tweaks made through the transient defaults this session.
                if (_cached != null && !AssetDatabase.Contains(_cached))
                {
                    settings.UnboundFieldHandling = _cached.UnboundFieldHandling;
                    settings.ApplyScriptAttributesOnSync = _cached.ApplyScriptAttributesOnSync;
                    settings.ApplyPresetsOnSync = _cached.ApplyPresetsOnSync;
                    settings.RouteSyncedFieldsByHeader = _cached.RouteSyncedFieldsByHeader;
                    settings.DefaultTheme = _cached.DefaultTheme;
                    settings.EnableHoverAnimation = _cached.EnableHoverAnimation;
                    settings.MaxParticlePreviews = _cached.MaxParticlePreviews;
                    settings.LayoutPreset = _cached.LayoutPreset;
                    settings.DisableNativeAudioPreview = _cached.DisableNativeAudioPreview;
                    settings.TakeoverFromOtherInspectors = _cached.TakeoverFromOtherInspectors;
                    settings.EnableAutomaticLayoutBackups = _cached.EnableAutomaticLayoutBackups;
                    settings.CompressLayoutAssets = _cached.CompressLayoutAssets;
                    settings.MaxLayoutBackups = _cached.MaxLayoutBackups;
                    settings.BackupFrequency = _cached.BackupFrequency;
                    settings.ClearHoldSeconds = _cached.ClearHoldSeconds;
                }
                AssetDatabase.CreateAsset(settings, path);
            }

            _cached = settings;
            _resolved = true;
            return settings;
        }

        /// <summary>Drops the cached resolution so the next access re-resolves (asset created/moved/deleted).</summary>
        public static void InvalidateCache()
        {
            // Keep a transient instance alive (it may hold pre-asset tweaks); a persisted asset re-resolves.
            if (_cached != null && AssetDatabase.Contains(_cached)) _cached = null;
            _resolved = false;
        }

        // ---- domain-reload teardown ----
        // A HideFlags.DontSave object SURVIVES a domain reload — that is what the flag means natively — but this
        // static does NOT, so the transient defaults instance is orphaned the moment the reload wipes _cached. And
        // because DontSave includes DontUnloadUnusedAsset, UnloadUnusedAssets can never reclaim it either: one
        // permanently stranded ScriptableObject per recompile, for the life of the editor process. The only moment
        // anything can still reach it is BEFORE the reload, while this static is still populated — hence the hook.
        // Deliberately NOT routed through InvalidateCache: that one keeps the transient alive on purpose (it may
        // carry pre-asset tweaks GetOrCreate copies onto the real asset when it is finally created).
        // The static constructor is enough of a trigger: _cached can only ever become non-null through an access
        // to this class, which is exactly what forces the constructor to have run first.
        static SasquatchSettingsManager()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DestroyTransientInstance;
            EditorApplication.quitting += DestroyTransientInstance;
        }

        /// <summary>Destroys the cached TRANSIENT defaults instance — never the real settings asset — and drops the
        /// resolution so the next access re-mints (or re-loads) cleanly. Runs before a domain reload and on quit;
        /// idempotent.</summary>
        internal static void DestroyTransientInstance()
        {
            if (_cached == null) return;                  // nothing cached (or already destroyed elsewhere)
            if (AssetDatabase.Contains(_cached)) return;  // the real asset on disk — never ours to destroy
            UnityEngine.Object.DestroyImmediate(_cached);
            _cached = null;                               // re-mint on next access rather than serve a fake-null
            _resolved = false;
        }
    }
}
