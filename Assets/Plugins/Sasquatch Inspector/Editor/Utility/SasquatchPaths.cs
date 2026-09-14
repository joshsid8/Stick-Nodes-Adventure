using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Resolves Sasquatch Inspector's own asset locations dynamically instead of relying on hardcoded
    /// "Assets/Sasquatch Inspector/..." literals. This keeps the package working when its folder is moved
    /// inside the project, renamed, or installed read-only under <c>Packages/</c> (UPM).
    ///
    /// Everything is resolved via the AssetDatabase (GUID/FindAssets), so a literal path is never assumed.
    /// The package root is anchored on the editor asmdef, which always lives at <c>&lt;root&gt;/Editor/</c>.
    /// Results are cached; the cache is invalidated when scripts/assets reimport (see
    /// <see cref="SasquatchAssetPostprocessor"/>).
    /// </summary>
    public static class SasquatchPaths
    {
        private const string EditorAsmdefFileName = "SasquatchInspector.Editor.asmdef";
        private const string FallbackPackageRoot = "Assets/Sasquatch Inspector";

        // The user-data folder ("User Data" — deliberately NOT "Generated", which reads as machine output:
        // it holds the layouts/settings/palettes the user authors). Lives inside the package folder, which is
        // why this one folder must be excluded when exporting the package — otherwise the authoring project's
        // own layouts and settings ship to buyers.
        private const string UserDataFolderName = "User Data";

        // Fallback for read-only Packages/ installs (registry/Git/tarball), where the package folder can't be
        // written — user data then generates under Assets/ instead.
        private const string ExternalUserDataRoot = "Assets/Sasquatch Inspector User Data";

        // Where the OLDEST installs generated editor stubs (outside the package). LEGACY-CLEANUP-ONLY.
        private const string LegacyExternalGeneratedRoot = "Assets/Sasquatch Inspector Generated";

        private static string _packageRoot;
        private static string _generatedDirectory;
        private static readonly Dictionary<string, StyleSheet> _styleSheetCache = new();

        // SessionState key prefix for the resolved GUID of a bundled stylesheet. SessionState (not
        // EditorPrefs) on purpose: it survives domain reloads, which is the whole point, and dies with the
        // editor session, so a sheet moved or deleted while Unity was CLOSED can never resurrect a stale
        // answer. See LoadStyleSheet for the verification the pin still goes through.
        private const string StyleSheetPinPrefix = "Sasquatch.Paths.Sheet.";

        /// <summary>
        /// The package root folder (e.g. "Assets/Sasquatch Inspector" or "Packages/com.sasquatch.ui"), wherever
        /// it currently lives. Resolved once by locating the editor asmdef at &lt;root&gt;/Editor/ and walking up.
        /// </summary>
        public static string PackageRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_packageRoot)) return _packageRoot;

                foreach (string guid in AssetDatabase.FindAssets("SasquatchInspector.Editor t:AssemblyDefinitionAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith("/" + EditorAsmdefFileName)) continue;

                    // path == <root>/Editor/SasquatchInspector.Editor.asmdef → root is two directories up.
                    string editorDir = Path.GetDirectoryName(path);     // <root>/Editor
                    string root = Path.GetDirectoryName(editorDir);     // <root>
                    _packageRoot = Normalize(root);
                    return _packageRoot;
                }

                // Last resort: the historical install location.
                _packageRoot = FallbackPackageRoot;
                return _packageRoot;
            }
        }

        /// <summary>
        /// Folder holding the user's Sasquatch Inspector data (per-type layouts, settings, palette library,
        /// preset, samples). Normally <c>&lt;PackageRoot&gt;/User Data</c> — inside the package folder, which is
        /// why exactly this one folder must be excluded when exporting the package (<c>TestShippedFolderIsPure</c>
        /// documents that contract).
        /// When the package is installed read-only under
        /// <c>Packages/</c> (registry, Git, tarball — i.e. NOT an Embedded/Local install), that folder can't be
        /// written, so the DEFAULT CREATION path is redirected to <c>Assets/Sasquatch Inspector User Data</c>.
        /// Only the creation default lives here: every consumer resolves an existing asset by GUID/FindAssets
        /// first (see <see cref="DatabasePath"/> et al.), so data that already lives anywhere in the project —
        /// including a legacy <c>Generated/</c> location — still loads. Cached; cleared by
        /// <see cref="InvalidateCache"/>.
        /// </summary>
        public static string GeneratedDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_generatedDirectory)) return _generatedDirectory;

                string root = PackageRoot;
                if (root.StartsWith("Packages/"))
                {
                    // Embedded (source copied under this project's Packages/) and Local (file: reference) installs
                    // are writable, so they keep generating in-place. Everything else under Packages/ (Registry,
                    // Git, LocalTarball, Builtin) is read-only → generate under Assets/ instead. A null PackageInfo
                    // is treated as read-only (the safe default for a Packages/ path). PackageManager is fully
                    // qualified because its PackageInfo collides with UnityEditor.PackageInfo.
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(root);
                    bool writable = info != null &&
                                    (info.source == UnityEditor.PackageManager.PackageSource.Embedded ||
                                     info.source == UnityEditor.PackageManager.PackageSource.Local);
                    _generatedDirectory = writable ? root + "/" + UserDataFolderName : ExternalUserDataRoot;
                }
                else
                {
                    _generatedDirectory = root + "/" + UserDataFolderName;
                }
                return _generatedDirectory;
            }
        }

        /// <summary>Legacy folder for the per-type <c>*_Editor.cs</c> stubs the oldest installs generated. Nothing writes here anymore (code generation was replaced by panel displacement); kept only so the path resolves for cleaning up any leftover folder. Tracks <see cref="GeneratedDirectory"/> so it points at the writable location.</summary>
        public static string GeneratedEditorsDirectory => GeneratedDirectory + "/Editors";

        /// <summary>
        /// LEGACY-CLEANUP-ONLY. Folder that older installs used for the auto-generated exact-type inspector stubs.
        /// Nothing writes here anymore — inspector takeover is now done by panel displacement
        /// (<see cref="SasquatchForceTakeover"/>), which needs no generated scripts. Kept so the one-time cleanup in
        /// <see cref="SasquatchGeneratedEditors"/> can locate and remove the leftover file/folder. It sat outside
        /// the package (under <c>Assets/</c>, in a folder literally named "Editor") so the stubs compiled into the
        /// predefined <c>Assembly-CSharp-Editor</c>; the cleanup removes this folder only when empty and never
        /// touches the parent <see cref="GeneratedDirectory"/> root.
        /// </summary>
        public static string GeneratedInspectorsDirectory => LegacyExternalGeneratedRoot + "/Editor";

        /// <summary>LEGACY-CLEANUP-ONLY path to the single stubs file older installs generated (see <see cref="GeneratedInspectorsDirectory"/>).</summary>
        public static string GeneratedInspectorsFilePath => GeneratedInspectorsDirectory + "/SasquatchGeneratedInspectors.cs";

        /// <summary>
        /// Path to the central <see cref="Runtime.SasquatchInspectorDatabase"/> asset. Prefers an existing
        /// database located anywhere in the project (so it survives relocation); otherwise returns the
        /// default creation path under <see cref="GeneratedDirectory"/>.
        /// </summary>
        // Memo for DatabasePath. The resolution below is a project-wide AssetDatabase index walk, and the
        // property is read from ~10 sites — several of them on the save path, and one (LayoutRegistry.MarkChanged)
        // that the asset postprocessor calls once per touched .asset, turning a batch delete of N files into N
        // whole-project walks inside a single callback. Post-migration there is usually NO database at all, so
        // every one of those walks ran to completion only to return the default creation path.
        //
        // Invalidation is the whole correctness story, and it has two halves, both required:
        //   • InvalidateCache() (script import/move/delete) — the fallback path is derived from
        //     GeneratedDirectory, so a package move must re-resolve it.
        //   • InvalidateLocatedAssetCache(), called by SasquatchAssetPostprocessor for ANY .asset import,
        //     delete or move — the database is an .asset, and creating, deleting or relocating it changes this
        //     answer without touching a single script. Deliberately unconditional there: deciding whether a
        //     given path IS the database costs a deserialisation, which is the very thing being avoided.
        private static string _databasePath;
        private static string[] _databaseGuids;

        // Same memo, same lifetime, same invalidation channel as _databasePath — see
        // InvalidateLocatedAssetCache. These three resolvers were completely uncached at a measured
        // 6.7-8.7 ms PER CALL, each one an unscoped project-wide index walk. Harmless only by luck: their
        // managers happen to cache downstream, so nothing calls them in a loop today. That is a landmine
        // rather than a design — any future caller on a per-rebuild path would cost 8.7 ms a call, and would
        // look completely innocent at the call site.
        //
        // The MISS is memoised too (the default creation path under GeneratedDirectory). Creating the asset
        // later is an .asset import, which drops this memo, so the next read re-resolves and finds it.
        private static string _settingsPath;
        private static string _presetPath;
        private static string _paletteLibraryPath;

        public static string DatabasePath
        {
            get
            {
                if (_databasePath != null) return _databasePath;
                _databasePath = ResolveDatabasePath();
                return _databasePath;
            }
        }

        /// <summary>
        /// The GUIDs of every <see cref="Runtime.SasquatchInspectorDatabase"/> in the project, memoised on the
        /// same lifetime as <see cref="DatabasePath"/> — which resolves from this very query, so the two share
        /// one project-wide index walk instead of running the same search twice.
        ///
        /// Exists for <c>LegacyDatabaseMigration</c>, whose after-reload hook ran this search on EVERY domain
        /// reload forever — long after the one-time migration completed — purely to discover there was nothing
        /// left to migrate. Sharing the walk is the safe half of that fix: unlike a persisted "already
        /// migrated" flag it cannot go stale, because a database appearing later is an <c>.asset</c> import and
        /// so drops this memo (see <see cref="InvalidateLocatedAssetCache"/>).
        /// </summary>
        internal static string[] DatabaseAssetGuids
        {
            get
            {
                if (_databaseGuids != null) return _databaseGuids;
                _databaseGuids = AssetDatabase.FindAssets("t:SasquatchInspectorDatabase");
                return _databaseGuids;
            }
        }

        private static string ResolveDatabasePath()
        {
            {
                string[] guids = DatabaseAssetGuids;
                if (guids.Length > 0)
                {
                    // Deterministic pick if more than one database somehow exists — FindAssets order is NOT
                    // stable across sessions/reimports, so picking [0] could resolve saves and loads to DIFFERENT
                    // files between calls, making edits "disappear" with no pattern. Prefer one under the package's
                    // Generated folder, otherwise the lexicographically-smallest path, so the choice is stable.
                    var paths = new List<string>();
                    foreach (var guid in guids)
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(p)) paths.Add(p);
                    }
                    if (paths.Count > 0)
                    {
                        if (paths.Count > 1)
                            UnityEngine.Debug.LogWarning($"[Sasquatch Inspector] {paths.Count} SasquatchInspectorDatabase assets exist; using a deterministic one. Remove the duplicate(s) so layout edits don't resolve to different files.");
                        string genDir = GeneratedDirectory;
                        string best = null;
                        foreach (var p in paths)
                        {
                            bool pGen = p.StartsWith(genDir);
                            bool bGen = best != null && best.StartsWith(genDir);
                            if (best == null || (pGen && !bGen) || (pGen == bGen && string.CompareOrdinal(p, best) < 0))
                                best = p;
                        }
                        return best;
                    }
                }
                return GeneratedDirectory + "/SasquatchInspectorDatabase.asset";
            }
        }

        /// <summary>
        /// Folder holding the version-control-friendly per-type layout assets (one small
        /// <see cref="Runtime.InspectorLayoutAsset"/> text file per styled type). Nested under
        /// <see cref="GeneratedDirectory"/> so it auto-inherits the writable/UPM-redirect location. Edits to
        /// different types land in different files, so teammates editing different inspectors never merge-conflict.
        /// </summary>
        public static string LayoutsDirectory => GeneratedDirectory + "/Layouts";

        /// <summary>
        /// Path to the project-wide <see cref="Runtime.SasquatchSettings"/> asset. Prefers an existing
        /// settings asset located anywhere in the project; otherwise returns the default creation path
        /// under <see cref="GeneratedDirectory"/>.
        /// </summary>
        public static string SettingsPath
        {
            get
            {
                if (_settingsPath != null) return _settingsPath;
                string[] guids = AssetDatabase.FindAssets("t:SasquatchSettings");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    if (!string.IsNullOrEmpty(path)) return _settingsPath = path;
                }
                return _settingsPath = GeneratedDirectory + "/SasquatchSettings.asset";
            }
        }

        /// <summary>
        /// Path to the project-wide <see cref="Runtime.SasquatchLayoutPreset"/> asset (default widget + config per
        /// field type). Prefers an existing preset located anywhere in the project; otherwise returns the default
        /// creation path under <see cref="GeneratedDirectory"/>.
        /// </summary>
        public static string PresetPath
        {
            get
            {
                if (_presetPath != null) return _presetPath;
                string[] guids = AssetDatabase.FindAssets("t:SasquatchLayoutPreset");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    if (!string.IsNullOrEmpty(path)) return _presetPath = path;
                }
                return _presetPath = GeneratedDirectory + "/SasquatchLayoutPreset.asset";
            }
        }

        /// <summary>
        /// Path to the project's single <see cref="Runtime.SasquatchColorPaletteLibrary"/> asset. Prefers an
        /// existing library located anywhere in the project; otherwise returns the default creation path under
        /// <see cref="GeneratedDirectory"/>.
        /// </summary>
        public static string PaletteLibraryPath
        {
            get
            {
                if (_paletteLibraryPath != null) return _paletteLibraryPath;
                string[] guids = AssetDatabase.FindAssets("t:SasquatchColorPaletteLibrary");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    if (!string.IsNullOrEmpty(path)) return _paletteLibraryPath = path;
                }
                return _paletteLibraryPath = GeneratedDirectory + "/SasquatchColorPaletteLibrary.asset";
            }
        }

        /// <summary>
        /// Path to the bundled user-manual PDF, located by asset name so it resolves wherever the package folder
        /// lives (moved or renamed). Null if the manual can't be found.
        /// </summary>
        public static string UserManualPath
        {
            get
            {
                foreach (string guid in AssetDatabase.FindAssets("SasquatchInspector_User_Manual"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".pdf")) return path;
                }
                return null;
            }
        }

        /// <summary>
        /// Loads one of the package's bundled stylesheets by file name (without extension), e.g.
        /// "SasquatchWidgets" or "DesignerCanvas". Located by GUID so it works regardless of folder layout.
        /// Cached across calls.
        /// </summary>
        public static StyleSheet LoadStyleSheet(string fileNameNoExtension)
        {
            if (string.IsNullOrEmpty(fileNameNoExtension)) return null;
            if (_styleSheetCache.TryGetValue(fileNameNoExtension, out var cached) && cached != null)
                return cached;

            // ---- Session-pinned GUID, ahead of the index walk ------------------------------------------
            // The memo above is a plain static, so a DOMAIN RELOAD empties it and the FindAssets below runs
            // again from scratch. Its cost is result-INDEPENDENT — the walk over every asset in the project
            // IS the cost — and it was measured at 10.4-11.0 ms per sheet on this project, four sheets deep
            // on the displacement path. SessionState survives domain reloads and is cleared when the editor
            // exits, which is exactly the lifetime a resolved asset location is valid for.
            //
            // The pin is VERIFIED, never trusted:
            //   - LoadAssetAtPath must return non-null. A non-empty path is not enough: GUIDToAssetPath can
            //     still hand back a ghost path for an asset deleted moments ago (the same hazard documented
            //     at LayoutRegistry.cs:1257-1263).
            //   - The path must still end in "/<name>.uss" — today's accept predicate, unchanged. A pinned
            //     GUID survives a RENAME, and without this re-check the pin would return a sheet the search
            //     below would have rejected. A rename correctly falls through and re-stamps.
            // A MOVE keeps the GUID and the file name, so it stays a hit, which is right — same asset.
            //
            // Where a project has two same-named sheets the search's answer already depends on FindAssets
            // order, which is not stable across sessions; pinning holds one of those same candidates for the
            // session rather than adding a new possibility.
            string pinKey = StyleSheetPinPrefix + fileNameNoExtension;
            string pinnedGuid = SessionState.GetString(pinKey, string.Empty);
            if (!string.IsNullOrEmpty(pinnedGuid))
            {
                string pinnedPath = AssetDatabase.GUIDToAssetPath(pinnedGuid);
                if (!string.IsNullOrEmpty(pinnedPath) && pinnedPath.EndsWith("/" + fileNameNoExtension + ".uss"))
                {
                    var pinnedSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(pinnedPath);
                    if (pinnedSheet != null)
                    {
                        _styleSheetCache[fileNameNoExtension] = pinnedSheet;
                        return pinnedSheet;
                    }
                }
            }

            foreach (string guid in AssetDatabase.FindAssets($"{fileNameNoExtension} t:StyleSheet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith("/" + fileNameNoExtension + ".uss")) continue;

                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet != null)
                {
                    _styleSheetCache[fileNameNoExtension] = sheet;
                    SessionState.SetString(pinKey, guid);
                    return sheet;
                }
            }
            // Resolved nothing: drop any pin so the next call is not preceded by a load that cannot succeed.
            SessionState.EraseString(pinKey);
            return null;
        }

        // ---- Deterministic per-type layout asset naming (version-control friendly storage) ----
        // A styled type's layout is stored at <LayoutsDirectory>/{ShortTypeName}_{keySuffix}.asset. The name is
        // computed purely from the type's keys so it is IDENTICAL on every machine — two teammates who style the
        // SAME type land on the same file (an inherent add/add conflict, accepted), and DIFFERENT types can never
        // share a file (so their edits never conflict). keySuffix disambiguates same-short-name types across
        // namespaces (Foo.Player vs Bar.Player): the GUID's first 8 chars when known, else a stable FNV-1a hash of
        // the assembly-qualified name (NEVER string.GetHashCode — that is not stable across runtimes/runs).
        // Pure string math (no AssetDatabase), so it is unit-testable.

        /// <summary>Deterministic file name (incl. ".asset") for a type's per-type layout asset. See <see cref="GetLayoutAssetPath"/>.</summary>
        internal static string GetLayoutAssetFileName(string scriptGuid, string targetTypeName)
        {
            string shortName = ShortTypeName(targetTypeName);
            string suffix = KeySuffix(scriptGuid, targetTypeName);
            return shortName + "_" + suffix + ".asset";
        }

        /// <summary>Deterministic full asset path under <see cref="LayoutsDirectory"/> for a type's per-type layout asset.</summary>
        internal static string GetLayoutAssetPath(string scriptGuid, string targetTypeName)
            => LayoutsDirectory + "/" + GetLayoutAssetFileName(scriptGuid, targetTypeName);

        // Segment after the last '.' of the type's full name (before the ',' that begins the assembly qualifier),
        // sanitized to a legal, cross-platform-stable file-name token: every char that is not [A-Za-z0-9_] (so '+'
        // from nested types and '`' from generics too) becomes '_'. Empty input falls back to "Layout".
        private static string ShortTypeName(string targetTypeName)
        {
            if (string.IsNullOrEmpty(targetTypeName)) return "Layout";
            string fullName = targetTypeName;
            int comma = fullName.IndexOf(',');
            if (comma >= 0) fullName = fullName.Substring(0, comma);
            fullName = fullName.Trim();
            int dot = fullName.LastIndexOf('.');
            string segment = dot >= 0 ? fullName.Substring(dot + 1) : fullName;

            var sb = new System.Text.StringBuilder(segment.Length);
            foreach (char c in segment)
                sb.Append((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
            string cleaned = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(cleaned) ? "Layout" : cleaned;
        }

        // First 8 chars of the (32-char hex) MonoScript GUID when present; otherwise 8 hex digits of an FNV-1a hash
        // of the assembly-qualified name — deterministic across machines and runtimes (unlike string.GetHashCode).
        private static string KeySuffix(string scriptGuid, string targetTypeName)
        {
            if (!string.IsNullOrEmpty(scriptGuid))
                return scriptGuid.Length >= 8 ? scriptGuid.Substring(0, 8) : scriptGuid;
            return Fnv1a(targetTypeName ?? string.Empty).ToString("x8");
        }

        // 32-bit FNV-1a over the string's UTF-16 code units (low byte then high byte) — a small, well-defined,
        // runtime-independent hash. Used only to disambiguate the rare no-GUID layout file name.
        private static uint Fnv1a(string s)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offset;
            foreach (char c in s)
            {
                hash = (hash ^ (byte)(c & 0xFF)) * prime;
                hash = (hash ^ (byte)((c >> 8) & 0xFF)) * prime;
            }
            return hash;
        }

        /// <summary>Drops cached resolutions so the next access re-resolves (called on asset/script reimport).</summary>
        public static void InvalidateCache()
        {
            _packageRoot = null;
            _generatedDirectory = null;
            _styleSheetCache.Clear();
            InvalidateLocatedAssetCache();
        }

        /// <summary>
        /// Drops only the memos for assets LOCATED by a project-wide search (currently <see cref="DatabasePath"/>).
        /// Called for any <c>.asset</c> import/delete/move, because creating, deleting or relocating one of those
        /// assets changes where it resolves without touching a script — and unlike <see cref="InvalidateCache"/>
        /// this costs nothing to over-call, since the package root and stylesheet memos are left intact.
        /// </summary>
        public static void InvalidateLocatedAssetCache()
        {
            _databasePath = null;
            _databaseGuids = null;
            _settingsPath = null;
            _presetPath = null;
            _paletteLibraryPath = null;
        }

        // Unity asset paths use forward slashes; Path.GetDirectoryName yields backslashes on Windows.
        private static string Normalize(string path) => string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
    }
}
