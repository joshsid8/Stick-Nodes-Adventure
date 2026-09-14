using System.IO;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Resolves which theme asset + effective skin a build renders with.
    /// Precedence: explicit theme arg → Project Settings default → the bundled theme matching the
    /// editor skin (DefaultDarkTheme / DefaultLightTheme) → any theme asset in the project → none.
    /// The effective skin always follows the RESOLVED asset's BaseSkin so the USS token sheet and
    /// the C# WidgetPalette can never disagree; only when no theme asset exists at all does the
    /// editor skin decide directly.
    /// </summary>
    internal static class SasquatchThemeResolver
    {
        // Bundled-theme lookups cache only hits (a late import is picked up on the next miss);
        // domain reload clears both.
        private static SasquatchThemeAsset _cachedDark;
        private static SasquatchThemeAsset _cachedLight;

        internal static (SasquatchThemeAsset theme, bool isLight) Resolve(SasquatchThemeAsset explicitTheme)
        {
            var theme = explicitTheme != null ? explicitTheme : SasquatchSettingsManager.Current.DefaultTheme;
            if (theme == null) theme = LoadBundledTheme(!EditorGUIUtility.isProSkin);
            if (theme != null) return (theme, theme.BaseSkin == SasquatchThemeSkin.Light);
            return (null, !EditorGUIUtility.isProSkin);
        }

        /// <summary>
        /// The effective skin outside a build scope (settings/editor-skin driven) — what
        /// WidgetPalette falls back to for aux windows built after the inspector build returned.
        /// </summary>
        internal static bool AmbientIsLight()
        {
            return Resolve(null).isLight;
        }

        /// <summary>
        /// Loads the bundled default theme for a skin, falling back to the opposite bundled theme
        /// and finally to any theme asset in the project. Null only when the project has no theme
        /// assets at all.
        /// </summary>
        internal static SasquatchThemeAsset LoadBundledTheme(bool light)
        {
            var cached = light ? _cachedLight : _cachedDark;
            if (cached != null) return cached;

            var theme = FindByName(light ? "DefaultLightTheme" : "DefaultDarkTheme")
                        ?? FindByName(light ? "DefaultDarkTheme" : "DefaultLightTheme")
                        ?? FindAny();
            if (theme != null)
            {
                if (light) _cachedLight = theme;
                else _cachedDark = theme;
            }
            return theme;
        }

        // SessionState key prefix for a resolved theme GUID — same lifetime argument as
        // SasquatchPaths.StyleSheetPinPrefix: survives domain reloads, dies with the editor session.
        private const string ThemePinPrefix = "Sasquatch.Theme.Pin.";

        private static SasquatchThemeAsset FindByName(string name)
        {
            // Session-pinned GUID ahead of the walk. The statics above only cache within one domain, so
            // this unscoped FindAssets ran again on EVERY reload at a measured 10.7-11.4 ms — and its cost
            // is result-independent, so finding the theme instantly is worth the same as finding nothing.
            //
            // Accepted ONLY on an exact name match, which is the same preference the search below applies.
            // That matters twice: a pinned GUID survives a RENAME, and the fallback this method can return
            // (`first`, a contains-match) is not pinnable at all — its identity depends on the search order,
            // so pinning it could answer a later call differently from how the search would have.
            string pinKey = ThemePinPrefix + name;
            string pinnedGuid = SessionState.GetString(pinKey, string.Empty);
            if (!string.IsNullOrEmpty(pinnedGuid))
            {
                string pinnedPath = AssetDatabase.GUIDToAssetPath(pinnedGuid);
                if (!string.IsNullOrEmpty(pinnedPath))
                {
                    // Non-null asset, not merely a non-empty path: GUIDToAssetPath can return a ghost path
                    // for an asset deleted a moment ago.
                    var pinned = AssetDatabase.LoadAssetAtPath<SasquatchThemeAsset>(pinnedPath);
                    if (pinned != null && pinned.name == name) return pinned;
                }
            }

            // FindAssets name matching is a contains-search; prefer the exact name so a user's
            // "MyDefaultDarkThemeCopy" never shadows the bundled asset.
            string[] guids = AssetDatabase.FindAssets(name + " t:SasquatchThemeAsset");
            SasquatchThemeAsset first = null;
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SasquatchThemeAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                if (asset.name == name) { SessionState.SetString(pinKey, guid); return asset; }
                if (first == null) first = asset;
            }
            SessionState.EraseString(pinKey);
            return first;
        }

        private static SasquatchThemeAsset FindAny()
        {
            string[] guids = AssetDatabase.FindAssets("t:SasquatchThemeAsset");
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<SasquatchThemeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        /// <summary>
        /// Creates a BRAND-NEW theme asset in the user-data folder, seeded from the bundled theme
        /// matching the current editor skin — used by the settings-page "+" button (mirrors
        /// LayoutPresetManager.CreateNewAsset). Does not touch the active-theme setting.
        /// </summary>
        internal static SasquatchThemeAsset CreateNewAsset()
        {
            string directory = SasquatchPaths.GeneratedDirectory;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var source = LoadBundledTheme(!EditorGUIUtility.isProSkin);
            var theme = source != null
                ? Object.Instantiate(source)
                : ScriptableObject.CreateInstance<SasquatchThemeAsset>();
            theme.name = "SasquatchTheme";

            string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/SasquatchTheme.asset");
            AssetDatabase.CreateAsset(theme, path);
            AssetDatabase.SaveAssets();
            return theme;
        }
    }
}
