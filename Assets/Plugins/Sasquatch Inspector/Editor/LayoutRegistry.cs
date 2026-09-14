using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Central lookup and save API for per-type inspector layouts. Resolves a type to its layout and handles
    /// saving, undo, and cache invalidation, keyed by the rename-proof script GUID.
    ///
    /// STORAGE (version-control friendly): each styled type's layout is its OWN small text asset — an
    /// <see cref="InspectorLayoutAsset"/> under <see cref="SasquatchPaths.LayoutsDirectory"/>, named
    /// deterministically from the type's keys (see <see cref="SasquatchPaths.GetLayoutAssetPath"/>). Edits to
    /// different types land in different files, so teammates editing different inspectors can never merge-conflict.
    /// The old single central <see cref="SasquatchInspectorDatabase"/> is retained ONLY as a legacy READ path
    /// (covers the brief window before <see cref="LegacyDatabaseMigration"/> converts a pre-existing database to
    /// per-type assets and deletes it); nothing mints a database anymore.
    /// </summary>
    public static class LayoutRegistry
    {
        // Cache keyed by BOTH the MonoScript GUID (primary, rename-proof) and the type-name strings
        // (AssemblyQualifiedName + simple FullName, legacy fallback). GUIDs are hex and type names contain
        // '.'/',', so the two key spaces never collide.
        private static readonly Dictionary<string, InspectorLayoutAsset> _cache = new();
        // MonoScript GUIDs that back TWO OR MORE distinct styled types (two MonoBehaviour/ScriptableObject
        // types declared in one .cs file share one .cs.meta GUID). The single _cache[guid] slot can only hold
        // one of them, so a GUID listed here is AMBIGUOUS and the GUID key is skipped on lookup — the per-type
        // name keys disambiguate. Cleared alongside _cache. (Never populated by renames: a rename keeps one
        // type per GUID, so the rename-proof GUID key stays trustworthy.)
        private static readonly HashSet<string> _ambiguousGuids = new();
        private static bool _hasInitializedInitialScan;

        // ---- SHARED per-drain index walk (item 9) -----------------------------------------------------
        //
        // OrphanedLayoutCleanup and OrphanedHeaderCleanup both hang off [InitializeOnLoadMethod] ->
        // EditorApplication.delayCall, so they drain microseconds apart, and each paid its OWN whole-project
        // FindAssets("t:InspectorLayoutAsset") for the IDENTICAL path set (verified by HashSet.SetEquals).
        // FindOrphanedLayoutAssetPaths measured 10.64 ms to find nothing at all; ResolveLayoutTypes 9.09 ms;
        // 70-82% of each was the bare index walk. They also re-arm on play-mode exit and on every layout-asset
        // import — a designer save, a version-control pull — so it is not only a per-reload cost.
        //
        // Only the GUID ARRAY is shared. Both consumers still do their own GUIDToAssetPath and
        // LoadAssetAtPath, which are cheap once the asset is loaded and — more importantly — keep every
        // existing null check exactly where it was. That is what makes this safe against the sweep deleting
        // files mid-drain: RunPrune removes assets and re-initialises, and a snapshot holding a now-deleted
        // GUID simply loads null, which both consumers already skip.
        //
        // DELIBERATELY NOT sourced from InitializeRegistryCache's enumeration, tempting as that is: that path
        // also runs the schema reconcile, the GUID backfill and SaveAssetIfDirty DISK WRITES, and pulling
        // those earlier in the reload than they happen today would be a behaviour change, not a saving.
        //
        // Lifetime is ONE drain. The clear is queued through delayCall the moment the snapshot is built, and
        // items queued during a drain run on the next one — so it always outlives both sweeps of this drain
        // and never the next. A layout imported later therefore cannot hide from a later sweep: the next
        // drain walks the index again.
        private static string[] _layoutGuidSnapshot;
        private static bool _layoutGuidSnapshotClearQueued;

        /// <summary>
        /// The GUIDs of every <see cref="InspectorLayoutAsset"/>, shared across one delayCall drain so the two
        /// orphan sweeps walk the project index once between them instead of once each.
        /// </summary>
        internal static string[] LayoutAssetGuidsForDrain()
        {
            if (_layoutGuidSnapshot != null) return _layoutGuidSnapshot;

            _layoutGuidSnapshot = AssetDatabase.FindAssets("t:InspectorLayoutAsset");
            if (!_layoutGuidSnapshotClearQueued)
            {
                _layoutGuidSnapshotClearQueued = true;
                EditorApplication.delayCall += () =>
                {
                    _layoutGuidSnapshot = null;
                    _layoutGuidSnapshotClearQueued = false;
                };
            }
            return _layoutGuidSnapshot;
        }

        /// <summary>Drops the shared walk immediately — used when the asset set is known to have changed
        /// mid-drain (a prune deleting files), so nothing downstream reasons from a set that no longer exists.</summary>
        internal static void DropLayoutAssetGuidSnapshot() => _layoutGuidSnapshot = null;

        // Asset paths the cache actually LOADED a layout from — the set InitializeRegistryCache built anyway
        // (its `loadedAssets` list), kept so a DELETED path can be judged without loading it. See MarkChanged:
        // the deleted branch used to invalidate the whole scan for ANY .asset, so removing an unrelated
        // material variant or audio mixer forced the next inspector open to re-scan every layout.
        private static readonly HashSet<string> _cachedLayoutAssetPaths = new(StringComparer.Ordinal);

        // The in-memory wrapper ScriptableObjects we create per database entry (see InitializeRegistryCache).
        // They are kept alive ONLY by _cache, so without HideFlags.DontSave Unity's Resources.UnloadUnusedAssets()
        // (run after a Test Runner pass / play-mode transition) destroys them, leaving _cache full of fake-null
        // entries while _hasInitializedInitialScan stays true — the "no layout exists" / thin-icon stale-cache bug.
        // Tracked separately so we can destroy exactly these on re-scan WITHOUT touching the persistent standalone
        // assets that are also cached.
        private static readonly List<InspectorLayoutAsset> _transientWrappers = new();

        // Session-only, in-memory layout overrides keyed by Type. Checked BEFORE the disk-backed cache and
        // never persisted (no asset DB, no editor re-registration), so they win for the current editor session
        // and vanish on domain reload. Used by the Showcase Sandbox to give an embedded object (e.g.
        // ShowcaseStatBlock) a custom inline layout without polluting the real inspector database.
        private static readonly Dictionary<Type, InspectorLayoutAsset> _sessionOverrides = new();

        // Resolved dynamically (relocation/UPM-safe) — see SasquatchPaths.
        private static string DatabasePath => SasquatchPaths.DatabasePath;
        private static SasquatchInspectorDatabase _database;

        /// <summary>Tries to get the saved layout for <paramref name="targetType"/> (its own, or one inherited
        /// from the nearest ancestor whose layout opted into Apply To Subclasses); returns false when none exists.</summary>
        public static bool TryGet(Type targetType, out InspectorLayoutAsset layout)
            => TryGetCore(targetType, null, allowInherited: true, out layout);

        // instance, when supplied, lets us resolve the script GUID in O(1) via MonoScript (no asset scan) —
        // pass the live inspected object on the hot inspector-draw path.
        /// <summary>Tries to get the saved layout for a type, using a live <paramref name="instance"/> to resolve its script GUID on the hot inspector path.</summary>
        public static bool TryGet(Type targetType, UnityEngine.Object instance, out InspectorLayoutAsset layout)
            => TryGetCore(targetType, instance, allowInherited: true, out layout);

        /// <summary>Exact-type lookup: succeeds only for a layout saved for <paramref name="targetType"/> ITSELF,
        /// never one inherited from an ancestor. Use for own-layout gates (remove menus, designer annotations).</summary>
        public static bool TryGetOwn(Type targetType, out InspectorLayoutAsset layout)
            => TryGetCore(targetType, null, allowInherited: false, out layout);

        /// <summary>True when <paramref name="targetType"/> has a layout of its own (not inherited).</summary>
        public static bool HasOwnLayout(Type targetType)
            => TryGetCore(targetType, null, allowInherited: false, out _);

        private static bool TryGetCore(Type targetType, UnityEngine.Object instance, bool allowInherited, out InspectorLayoutAsset layout)
        {
            layout = null;
            if (targetType == null) return false;

            // Session overrides take priority over anything on disk and survive cache rebuilds (separate store).
            if (_sessionOverrides.TryGetValue(targetType, out var sessionLayout) && sessionLayout != null)
            {
                layout = sessionLayout;
                return true;
            }

            string typeName = targetType.AssemblyQualifiedName;
            string simpleName = targetType.FullName;
            string guid = ResolveGuid(targetType, instance);

            // 1. Check in-memory cache
            if (TryFromCache(guid, typeName, simpleName, out layout) && (allowInherited || !layout.IsInheritedCopy))
                return true;

            // 2. Perform lazy search if not initialized
            if (!_hasInitializedInitialScan)
            {
                InitializeRegistryCache();
                // Migration may have backfilled GUIDs, so re-resolve in case it was empty before.
                guid ??= ResolveGuid(targetType, instance);
            }

            // 3. Recheck cache after populating. Inherited copies are cached under the subclass's own keys
            // (see TryResolveInherited), so an exact-only lookup must treat them as a miss.
            if (TryFromCache(guid, typeName, simpleName, out layout) && (allowInherited || !layout.IsInheritedCopy))
                return true;
            layout = null;

            // 4. Base-class fallback: the nearest ancestor with a layout, when that layout opted in.
            return allowInherited && TryResolveInherited(targetType, guid, typeName, out layout);
        }

        // Walks the ancestor chain looking for the NEAREST type with a cached layout. BARRIER semantics: the
        // first ancestor that HAS a layout decides — if it didn't opt into Apply To Subclasses, farther
        // ancestors don't get a say (the closest layout is the type's visual identity, so silently skipping
        // it for a farther opted-in one would be surprising). A hit is served as a transient DEEP COPY
        // re-keyed to the subclass: the aliased base wrapper must never be shared (auto-sync mutates the
        // served layout in place and would otherwise write subclass fields into the ancestor's saved entry —
        // see the PersistLayoutEdit / BeginLayoutEditUndo guards). The copy is cached under the subclass's
        // own keys so the next lookup is O(1); every cache rebuild discards and lazily re-materializes it.
        private static bool TryResolveInherited(Type targetType, string derivedGuid, string derivedTypeName, out InspectorLayoutAsset layout)
        {
            layout = null;

            // IsTypeEditable doubles as the walk terminator: it rejects MonoBehaviour/ScriptableObject and
            // everything above them (UnityEngine.*), and skips Sasquatch Inspector-internal bases. Generic ancestors
            // simply never hit the cache and are walked past.
            for (var t = targetType.BaseType; t != null && SasquatchContextMenu.IsTypeEditable(t); t = t.BaseType)
            {
                ScriptGuidUtility.TryGetGuid(t, out string ancestorGuid);
                if (!TryFromCache(ancestorGuid, t.AssemblyQualifiedName, t.FullName, out var ancestorLayout))
                    continue;

                if (!ancestorLayout.ApplyToSubclasses) return false; // barrier: nearest layout owner decides

                var copy = UnityEngine.Object.Instantiate(ancestorLayout);
                copy.hideFlags = HideFlags.DontSave; // includes DontUnloadUnusedAsset — same lifetime rules as the wrappers
                copy.IsInheritedCopy = true;
                copy.ForceTakeover = ancestorLayout.ForceTakeover; // a subclass inheriting the layout also inherits its takeover opt-in (Instantiate copies it too; explicit for clarity)
                copy.LayoutHidden = ancestorLayout.LayoutHidden;   // …and its hidden state: hiding a layout hides it everywhere that layout draws
                // When the ancestor's cached layout is itself an inherited copy (mid-chain type with no own
                // layout), credit the ORIGINAL owner so the UX message names the type the user actually edits.
                copy.InheritedFromTypeName = ancestorLayout.IsInheritedCopy
                    ? ancestorLayout.InheritedFromTypeName
                    : t.AssemblyQualifiedName;
                copy.ScriptGuid = derivedGuid;
                copy.TargetTypeName = derivedTypeName;
                _transientWrappers.Add(copy);
                CacheByAllKeys(derivedGuid, derivedTypeName, copy);
                layout = copy;
                return true;
            }
            return false;
        }

        private static bool TryFromCache(string guid, string typeName, string simpleName, out InspectorLayoutAsset layout)
        {
            // GUID key first (rename-proof) — but skip it when the GUID is ambiguous (two styled types in one
            // file), since its slot may hold the OTHER type's layout. The type-name keys below are unique per
            // type and resolve it correctly.
            if (!string.IsNullOrEmpty(guid) && !_ambiguousGuids.Contains(guid)
                && _cache.TryGetValue(guid, out layout) && layout != null)
                return true;
            // Name keys are a FALLBACK for a layout whose GUID no longer matches (a delete-and-recreate, or a
            // file move). They are deliberately NOT ownership-checked against the asset's stored ScriptGuid.
            //
            // A GUID-liveness check here was tried and REVERTED (audit 2026-08-09, finding SG-2): this key is
            // `_cache[<the querying type's own name>]`, and the cache entry was made FROM the asset's stored
            // TargetTypeName — so on a hit the two names are equal by construction. That makes "a new class stole
            // a renamed class's name" and "this class's script simply moved to another .cs" INDISTINGUISHABLE
            // here, and rejecting on GUID liveness punished the second (a routine refactor: the type's own layout
            // silently disappears and a duplicate empty one is minted) to guard the first (rare: requires a rename
            // AND a later new class reusing the exact old name, namespace and assembly).
            //
            // The stale-name hazard is handled upstream instead, by the heal pass in InitializeRegistryCache — it
            // RETIRES a stale TargetTypeName the moment the old name stops resolving, so the hijacker never finds
            // it. Residual window: a rename and the name's reuse within a single editor session, with no cache
            // rebuild in between.
            if (!string.IsNullOrEmpty(typeName) && _cache.TryGetValue(typeName, out layout) && layout != null)
                return true;
            if (!string.IsNullOrEmpty(simpleName) && _cache.TryGetValue(simpleName, out layout) && layout != null)
                return true;
            layout = null;
            return false;
        }

        /// <summary>
        /// True when <paramref name="asset"/>'s stored <c>TargetTypeName</c> is provably stale — its (unambiguous,
        /// still-live) ScriptGuid resolves to a type with a different assembly-qualified name, AND the stored name
        /// resolves to nothing. Outputs the correct name. Conservative by design: it heals only the class-rename
        /// case, where there is exactly one right answer.
        /// </summary>
        private static bool TryHealStaleTargetTypeName(InspectorLayoutAsset asset, out string healedName)
        {
            healedName = null;
            if (asset == null || string.IsNullOrEmpty(asset.ScriptGuid)) return false;

            // Two styled types in one .cs share a GUID, so it cannot say WHICH of them this layout is for.
            if (_ambiguousGuids.Contains(asset.ScriptGuid)) return false;

            var live = ScriptGuidUtility.ResolveType(asset.ScriptGuid);
            string current = live?.AssemblyQualifiedName;
            if (string.IsNullOrEmpty(current)) return false;
            if (string.Equals(current, asset.TargetTypeName, StringComparison.Ordinal)) return false;

            // Only retire a name nothing answers to. If the stored name still resolves, some other live type owns
            // it and rewriting would be a guess — TryFromCache / GetOrCreateLayoutAsset's ownership checks cover
            // that case instead, by refusing to hand this asset over on the name key.
            if (ResolveTypeFromName(asset.TargetTypeName) != null) return false;

            healedName = current;
            return true;
        }

        private static string ResolveGuid(Type targetType, UnityEngine.Object instance)
        {
            // The instance shortcut is only valid when the instance IS the queried type: a
            // TryGet(baseType, derivedInstance) query must not resolve the DERIVED type's GUID — it would
            // answer a base-type query with the derived type's own layout.
            if (instance != null && instance.GetType() == targetType && ScriptGuidUtility.TryGetGuid(instance, out string fromInstance))
                return fromInstance;
            if (targetType != null && ScriptGuidUtility.TryGetGuid(targetType, out string fromType))
                return fromType;
            return null;
        }

        /// <summary>Builds the type→layout lookup cache from the per-type layout assets (primary store) plus any
        /// not-yet-migrated central database entries (legacy read path).</summary>
        public static void InitializeRegistryCache()
        {
            _hasInitializedInitialScan = true;
            DestroyTransientWrappers();
            _cache.Clear();
            _ambiguousGuids.Clear();
            _cachedLayoutAssetPaths.Clear();

            // 1. LEGACY read path: load any pre-existing central database. This covers the brief window before
            //    LegacyDatabaseMigration converts it to per-type assets and deletes it. Per-type assets are loaded
            //    AFTER (step 2) and overwrite these same cache keys, so a migrated type's asset always wins.
            LoadDatabase();
            if (_database != null)
            {
                // Schema reconcile before anything reads the tree: warn once if a newer Sasquatch Inspector wrote it, or
                // run structural migrations for an older stamp (see SasquatchSchema).
                ReconcileDatabaseSchema(_database);

                bool migrated = false;
                foreach (var entry in _database.Entries)
                {
                    if (string.IsNullOrEmpty(entry.TargetTypeName) && string.IsNullOrEmpty(entry.ScriptGuid))
                        continue;

                    // Migration: backfill an empty GUID from the (resolvable) type name. Entries whose
                    // TargetTypeName no longer resolves are left untouched — no recovery is attempted.
                    if (string.IsNullOrEmpty(entry.ScriptGuid) && !string.IsNullOrEmpty(entry.TargetTypeName))
                    {
                        var resolvedType = ResolveTypeFromName(entry.TargetTypeName);
                        if (resolvedType != null && ScriptGuidUtility.TryGetGuid(resolvedType, out string backfilled))
                        {
                            entry.ScriptGuid = backfilled;
                            migrated = true;
                        }
                    }

                    // Create a transient InspectorLayoutAsset wrapper. DontSave keeps it out of the asset DB
                    // AND (via the included DontUnloadUnusedAsset flag) prevents Resources.UnloadUnusedAssets()
                    // from destroying it while _cache still references it — otherwise the cache silently rots
                    // into fake-null entries (the stale-cache "no layout exists" bug). Tracked for cleanup.
                    var asset = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
                    asset.hideFlags = HideFlags.DontSave;
                    _transientWrappers.Add(asset);
                    asset.ScriptGuid = entry.ScriptGuid;
                    asset.TargetTypeName = entry.TargetTypeName;
                    asset.Root = entry.Root;
                    // Alias the sync ledger by reference too (like Root), so in-place ledger mutations
                    // made through the wrapper persist via PersistLayoutEdit's SetDirty(_database).
                    asset.SyncedFieldPaths = entry.SyncedFieldPaths;
                    asset.SyncedFieldTypes = entry.SyncedFieldTypes;
                    asset.ApplyToSubclasses = entry.ApplyToSubclasses; // by value (bool) — nothing to alias
                    asset.ForceTakeover = entry.ForceTakeover;         // by value — mirrors the entry's takeover opt-in

                    CacheByAllKeys(entry.ScriptGuid, entry.TargetTypeName, asset);
                }

                if (migrated)
                {
                    _database.SchemaVersion = SasquatchSchema.CurrentVersion;
                    EditorUtility.SetDirty(_database);
                    AssetDatabase.SaveAssetIfDirty(_database);
                }
            }

            // 2. PRIMARY store: load the per-type InspectorLayoutAsset files. Loaded after the legacy database so
            //    a migrated type's asset overwrites (wins) its stale database wrapper in the cache.
            string dbPath = DatabasePath;
            string[] guids = AssetDatabase.FindAssets("t:InspectorLayoutAsset");
            var loadedAssets = new List<InspectorLayoutAsset>(guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == dbPath) continue;

                var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (asset == null) continue;
                if (string.IsNullOrEmpty(asset.TargetTypeName) && string.IsNullOrEmpty(asset.ScriptGuid)) continue;
                loadedAssets.Add(asset);
                _cachedLayoutAssetPaths.Add(path); // so a later DELETE of this path can be judged — see MarkChanged

                // Hydrate a compressed graph (and self-heal a file whose storage representation doesn't match
                // the Compress Layout Assets setting) BEFORE the schema reconcile below walks the tree.
                LayoutGraphStorage.ReconcileOnLoad(asset);

                ReconcileAssetSchema(asset); // warn-if-newer / migrate-if-older, like the database above

                // Backfill the standalone asset's GUID too, so it survives renames going forward.
                if (string.IsNullOrEmpty(asset.ScriptGuid) && !string.IsNullOrEmpty(asset.TargetTypeName))
                {
                    var resolvedType = ResolveTypeFromName(asset.TargetTypeName);
                    if (resolvedType != null && ScriptGuidUtility.TryGetGuid(resolvedType, out string backfilled))
                    {
                        asset.ScriptGuid = backfilled;
                        // THROUGH THE PERSIST PATH, not a raw SetDirty + SaveAssetIfDirty. ReconcileOnLoad
                        // ran a few lines above and HYDRATED Root from CompressedGraph, so a direct save
                        // writes the full [SerializeReference] node tree next to the blob that already holds
                        // the same graph: both representations, in one file. Measured on three fixtures the
                        // day this was found — 38 -> 1,817 and 154 -> 37,274 lines, the moment their GUID
                        // first resolved. Nothing breaks (Hydrate no-ops when Root != null), which is exactly
                        // why it sat unnoticed: it shows up as an absurd version-control diff, not a failure.
                        // SaveLayoutAsset compresses, nulls Root, writes, and restores the live graph.
                        LayoutGraphStorage.SaveLayoutAsset(asset);
                    }
                }

                CacheByAllKeys(asset.ScriptGuid, asset.TargetTypeName, asset);
            }

            // 3. Heal stale TargetTypeNames. A class rename keeps the .cs GUID, so the layout keeps resolving and
            //    the user sees nothing wrong — but the stored type name is only rewritten by explicit save paths, so
            //    it stays pointing at the OLD name indefinitely. That leaves the layout indexed under a name a
            //    future class could take, which is how a new type ends up adopting (and then destroying) a renamed
            //    type's layout. Healing here retires the stale key on load instead of waiting for the next save.
            //
            //    Deliberately a SECOND pass: _ambiguousGuids is only fully populated once every asset above has been
            //    cached, and a shared GUID (two styled types in one .cs) resolves to an arbitrary one of them — so
            //    healing from it during the first pass could rewrite the key to the WRONG type.
            foreach (var loaded in loadedAssets)
            {
                if (!TryHealStaleTargetTypeName(loaded, out string healedName)) continue;
                loaded.TargetTypeName = healedName;
                QueueLayoutResave(loaded); // persists via the normal save chokepoint, not a raw SetDirty
                CacheByAllKeys(loaded.ScriptGuid, healedName, loaded);
            }
        }

        // Indexes a layout under its GUID (primary) plus the assembly-qualified and simple type names (fallback).
        private static void CacheByAllKeys(string scriptGuid, string targetTypeName, InspectorLayoutAsset asset)
        {
            if (!string.IsNullOrEmpty(scriptGuid))
            {
                // Detect a shared-GUID collision: an existing slot holding a DIFFERENT layout for a DIFFERENT,
                // non-empty type name means two styled types live in one .cs file. Same-type re-cache (legacy DB
                // entry then per-type asset) has matching TargetTypeName and is NOT a collision.
                if (_cache.TryGetValue(scriptGuid, out var existing) && existing != null && existing != asset
                    && !string.IsNullOrEmpty(existing.TargetTypeName) && !string.IsNullOrEmpty(targetTypeName)
                    && !string.Equals(existing.TargetTypeName, targetTypeName, StringComparison.Ordinal))
                {
                    _ambiguousGuids.Add(scriptGuid);
                }
                _cache[scriptGuid] = asset;
            }
            if (!string.IsNullOrEmpty(targetTypeName))
            {
                _cache[targetTypeName] = asset;

                int commaIndex = targetTypeName.IndexOf(',');
                if (commaIndex > 0)
                {
                    string simpleName = targetTypeName.Substring(0, commaIndex).Trim();
                    _cache[simpleName] = asset;
                }
            }
        }

        // Resolves a Type from an AssemblyQualifiedName/FullName string (the legacy key format).
        private static Type ResolveTypeFromName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            Type type = Type.GetType(typeName);
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        private static void LoadDatabase()
        {
            _database = AssetDatabase.LoadAssetAtPath<SasquatchInspectorDatabase>(DatabasePath);
        }

        // ---- Schema reconcile (see SasquatchSchema) ----
        // A file stamped HIGHER than we understand gets one non-blocking warning (loaded as-is). A LOWER stamp runs
        // structural migrations on each layout tree and bumps the in-memory version; the bump is persisted by the
        // next save (the stamp is idempotent, so re-seeing an old on-disk stamp next session is harmless).
        private static bool _warnedNewerSchema;

        private static void WarnNewerSchemaOnce(int storedVersion)
        {
            if (_warnedNewerSchema) return;
            _warnedNewerSchema = true;
            UnityEngine.Debug.LogWarning(
                $"[Sasquatch Inspector] An inspector-layout file was saved by a NEWER version of Sasquatch Inspector " +
                $"(schema v{storedVersion}; this version understands v{SasquatchSchema.CurrentVersion}). Layouts " +
                $"still load, but anything relying on newer schema features may not render correctly — updating " +
                $"Sasquatch Inspector is recommended.");
        }

        private static void ReconcileDatabaseSchema(SasquatchInspectorDatabase db)
        {
            if (db == null) return;
            int stored = db.SchemaVersion;
            if (stored > SasquatchSchema.CurrentVersion) { WarnNewerSchemaOnce(stored); return; }
            if (stored < SasquatchSchema.CurrentVersion)
            {
                foreach (var entry in db.Entries)
                    if (entry != null && entry.Root != null) SasquatchSchema.Migrate(entry.Root, stored);
                db.SchemaVersion = SasquatchSchema.CurrentVersion; // in-memory; persisted by the next save
            }
        }

        private static void ReconcileAssetSchema(InspectorLayoutAsset asset)
        {
            if (asset == null) return;
            int stored = asset.SchemaVersion;
            if (stored > SasquatchSchema.CurrentVersion) { WarnNewerSchemaOnce(stored); return; }
            if (stored < SasquatchSchema.CurrentVersion)
            {
                if (asset.Root != null) SasquatchSchema.Migrate(asset.Root, stored);
                asset.SchemaVersion = SasquatchSchema.CurrentVersion; // in-memory; persisted by the next save
            }
        }

        // NOTE: there is deliberately no LoadOrCreate for the central database, and nothing here may add one.
        // The legacy SasquatchInspectorDatabase is a READ-ONLY compatibility path (see the class summary): it
        // is loaded if a pre-existing one is found, migrated to per-type assets by LegacyDatabaseMigration, and
        // then DELETED. A creator would mint a file the very next editor load throws away, and re-open the
        // duplicate-database edit-loss hazard the per-type storage exists to close. The one that used to live
        // here was verified to have zero callers repo-wide and was removed 2026-08-14;
        // TestNothingInTheShippedPackageCreatesALegacyDatabase keeps it gone.

        // knownFieldPaths: the auto-sync ledger to store with the layout — every top-level bindable path
        // this save has accounted for (bound OR deliberately left out). Null ⇒ all of the type's current
        // code fields, so a plain save marks everything it omitted as a deliberate exclusion.
        /// <summary>Saves <paramref name="root"/> as the layout for <paramref name="targetType"/>, updating the
        /// auto-sync field ledger. <paramref name="applyToSubclasses"/> opts the layout into base-class inheritance.</summary>
        public static void Save(Type targetType, LayoutNode root, IEnumerable<string> knownFieldPaths = null, bool applyToSubclasses = false)
        {
            if (targetType == null) return;
            string typeName = targetType.AssemblyQualifiedName;
            ScriptGuidUtility.TryGetGuid(targetType, out string scriptGuid);

            // Suppress the asset postprocessor's GLOBAL ForceRebuild for the layout write below — we do a precise
            // per-type refresh at the end instead (RefreshInspectorsForType). Save/restore so nesting is safe.
            bool prevSuppress = SasquatchAssetPostprocessor.SuppressLayoutForceRebuild;
            SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = true;
            try
            {

            // Snapshot the current layouts folder before we overwrite it (throttled ~1/hour), so a bad edit is
            // recoverable. Best-effort — never breaks the save.
            BackupLayoutsIfDue();

            var asset = GetOrCreateLayoutAsset(scriptGuid, typeName);

            // Undo of a FIRST-time Save can't delete the just-created file (asset creation isn't
            // undo-recordable) — matches Remove's non-undoable DeleteAsset. Field edits on an existing file
            // stay undoable.
            //
            // RegisterCompleteObjectUndo, NOT RecordObject. MEASURED pathology: RecordObject defers a
            // DIFF of the recorded object to flush time, and diffing this [SerializeReference] graph asset after the
            // Root swap below is catastrophically slow — ~67s on a 73-node layout at an explicit flush, or smeared
            // as ~45ms per VisualElement DETACH while the snapshot stays open (the post-save canvas/inspector
            // rebuilds tear down hundreds of elements → the 15-50s "Hold on…" save stall, and the same cost on a
            // deferred tick → the "Application.Tick" popup). RegisterCompleteObjectUndo snapshots the whole object
            // up front (~3ms) with NO flush-time diff; user-facing undo of the save behaves identically.
            Undo.RegisterCompleteObjectUndo(asset, "Save Sasquatch Layout");

            if (!string.IsNullOrEmpty(scriptGuid)) asset.ScriptGuid = scriptGuid;
            asset.TargetTypeName = typeName;
            asset.Root = root;
            asset.ApplyToSubclasses = applyToSubclasses;
            // ForceTakeover is deliberately NOT touched here — it persists across saves (parity with the old entry).
            // LayoutHidden likewise: hiding a layout is a display toggle, not layout data, so saving from the
            // designer must never silently un-hide (or re-hide) it. Only SetLayoutHidden writes that flag.

            WriteLedger(asset, targetType, knownFieldPaths);

            // Stamps the schema version and writes the graph plain or packed per the Compress Layout Assets setting.
            LayoutGraphStorage.SaveLayoutAsset(asset);

            // Belt-and-suspenders: close any RecordObject snapshot a CALLER may have left open before the refresh
            // below tears down UI (an open RecordObject snapshot makes every element detach pay its diff — see the
            // RegisterCompleteObjectUndo note above). No-op (0ms) when nothing is pending.
            Undo.FlushUndoRecordObjects();

            // Pre-migration only: if a legacy database still holds this type's entry, drop it so no type has two
            // sources of truth (the per-type asset we just wrote now owns the layout). No-op post-migration.
            RemoveLegacyDatabaseEntry(scriptGuid, typeName);

            // Refresh Cache
            InitializeRegistryCache();

            // No table edit needed: our fallback editor is already the resolved inspector for this type (unless it
            // has its own [CustomEditor], in which case Unity defers to that — the deference contract). Refresh only
            // the inspectors that actually render THIS type (both paths), rather than nudging the whole selection —
            // unrelated components/previews don't churn. Displacement (SasquatchForceTakeover) reacts on the header
            // scan the refresh requests — no codegen, no recompile.
            SasquatchEditorRegistrar.RefreshInspectorsForType(targetType);

            }
            finally { SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = prevSuppress; }
        }

        // ---- Per-type layout asset write path (version-control friendly storage) ----

        /// <summary>
        /// Builds the auto-sync ledger for <paramref name="asset"/>: the set of top-level bindable paths this save
        /// accounted for (<paramref name="knownFieldPaths"/>, or all of the type's current code fields when null),
        /// plus the parallel current field types (for rename-pairing heuristics; "" = unknown/unresolved).
        /// Extracted so the designer's standalone-save branch writes an identical ledger.
        /// </summary>
        internal static void WriteLedger(InspectorLayoutAsset asset, Type targetType, IEnumerable<string> knownFieldPaths)
        {
            if (asset == null) return;

            var ledger = new HashSet<string>();
            if (knownFieldPaths != null)
            {
                foreach (var path in knownFieldPaths) ledger.Add(path);
            }
            else
            {
                foreach (var p in SasquatchReflectionUtility.GetTopLevelBindableProperties(targetType))
                    ledger.Add(p.path);
            }
            asset.SyncedFieldPaths = new List<string>(ledger);

            // Parallel type names: reflect each ledgered path's current type while the field is still alive; paths
            // that no longer resolve keep "" = unknown.
            var typeByPath = new Dictionary<string, string>();
            foreach (var p in SasquatchReflectionUtility.GetTopLevelBindableProperties(targetType))
                typeByPath[p.path] = p.type != null ? p.type.FullName : "";
            var types = new List<string>(asset.SyncedFieldPaths.Count);
            foreach (var path in asset.SyncedFieldPaths)
                types.Add(typeByPath.TryGetValue(path, out var t) ? t : "");
            asset.SyncedFieldTypes = types;
        }

        /// <summary>
        /// Resolves the version-control-friendly per-type layout asset for a type, creating one under
        /// <see cref="SasquatchPaths.LayoutsDirectory"/> if none exists yet. Match order mirrors <see cref="Remove"/>:
        /// GUID first (rename-proof), then the assembly-qualified / simple type name. Multiple matches ⇒ the
        /// lexicographically-smallest path (deterministic) + a warning, like <see cref="SasquatchPaths.DatabasePath"/>'s
        /// duplicate handling. A drifted file inside the managed <c>Layouts/</c> folder (type renamed ⇒ short name
        /// changed) is opportunistically renamed back to its deterministic name. On a miss the new asset adopts any
        /// matching legacy database entry's data (pre-migration window), so nothing is lost when that entry is later
        /// dropped. Never returns null.
        /// </summary>
        private static InspectorLayoutAsset GetOrCreateLayoutAsset(string scriptGuid, string typeName)
        {
            string simpleName = SimpleName(typeName);
            string dbPath = DatabasePath;

            // Fast path: the overwhelmingly common case is the file already sitting at its own deterministic path
            // (that path encodes the GUID, so a correctly-homed file's ScriptGuid matches). A single LoadAssetAtPath
            // there — accepted only on a GUID match (the strongest, rename-proof key), or a type-name match when the
            // type has no GUID — skips the registry-wide FindAssets + LoadAssetAtPath-of-EVERY-layout the full scan
            // below runs just to locate ONE file. Any miss (a drifted/renamed file, a legacy-adopt case, a duplicate)
            // falls through to that authoritative scan, which still warns on duplicates and re-homes drifted files.
            string deterministicPath = SasquatchPaths.GetLayoutAssetPath(scriptGuid, typeName);
            if (deterministicPath != dbPath)
            {
                var atDeterministic = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(deterministicPath);
                if (atDeterministic != null)
                {
                    bool guidMatch = !string.IsNullOrEmpty(scriptGuid) && atDeterministic.ScriptGuid == scriptGuid;
                    bool nameMatch = string.IsNullOrEmpty(scriptGuid) && !string.IsNullOrEmpty(atDeterministic.TargetTypeName)
                                     && (atDeterministic.TargetTypeName == typeName || atDeterministic.TargetTypeName == simpleName);
                    if (guidMatch || nameMatch) return atDeterministic;
                }
            }

            var guidMatches = new List<string>();
            var nameMatches = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:InspectorLayoutAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path == dbPath) continue;
                var candidate = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (candidate == null) continue;

                if (!string.IsNullOrEmpty(scriptGuid) && candidate.ScriptGuid == scriptGuid)
                    guidMatches.Add(path);
                // The name fallback is load-bearing for the delete-and-recreate heal AND for a script that simply
                // MOVED file (new GUID, same class). Not ownership-checked, for the reason in TryFromCache:
                // rejecting on GUID liveness here made a routine file move mint a duplicate empty layout.
                else if (!string.IsNullOrEmpty(candidate.TargetTypeName) &&
                         (candidate.TargetTypeName == typeName || candidate.TargetTypeName == simpleName))
                    nameMatches.Add(path);
            }

            var matches = guidMatches.Count > 0 ? guidMatches : nameMatches;
            if (matches.Count > 0)
            {
                matches.Sort(StringComparer.Ordinal);
                if (matches.Count > 1)
                    Debug.LogWarning($"[Sasquatch Inspector] {matches.Count} layout assets exist for '{simpleName}'; using " +
                                     $"'{matches[0]}'. Remove the duplicate(s) so edits don't resolve to different files.");
                var existing = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(matches[0]);
                MaybeRenameToDeterministic(matches[0], scriptGuid, typeName);
                return existing;
            }

            // Miss — create a fresh per-type asset at the deterministic path.
            EnsureAssetFolder(SasquatchPaths.LayoutsDirectory);
            var created = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            created.ScriptGuid = scriptGuid;
            created.TargetTypeName = typeName;
            AdoptLegacyDatabaseEntry(created, scriptGuid, typeName);
            AssetDatabase.CreateAsset(created, SasquatchPaths.GetLayoutAssetPath(scriptGuid, typeName));
            return created;
        }

        // Copies a matching legacy database entry's layout data onto a freshly created per-type asset, so a Save /
        // SetForceTakeover that happens before LegacyDatabaseMigration runs doesn't lose the type's existing layout
        // when RemoveLegacyDatabaseEntry later drops that entry. No-op when no database (post-migration steady state)
        // or no matching entry. The graphs are shared in-memory only; each asset serializes its own copy on save.
        private static void AdoptLegacyDatabaseEntry(InspectorLayoutAsset asset, string scriptGuid, string typeName)
        {
            LoadDatabase();
            if (_database == null) return;
            string simpleName = SimpleName(typeName);

            SasquatchDatabaseEntry entry = null;
            if (!string.IsNullOrEmpty(scriptGuid)) entry = _database.Entries.Find(e => e.ScriptGuid == scriptGuid);
            entry ??= _database.Entries.Find(e => e.TargetTypeName == typeName || e.TargetTypeName == simpleName);
            if (entry == null) return;

            asset.Root = entry.Root;
            asset.SyncedFieldPaths = entry.SyncedFieldPaths != null ? new List<string>(entry.SyncedFieldPaths) : new List<string>();
            asset.SyncedFieldTypes = entry.SyncedFieldTypes != null ? new List<string>(entry.SyncedFieldTypes) : new List<string>();
            asset.ApplyToSubclasses = entry.ApplyToSubclasses;
            asset.ForceTakeover = entry.ForceTakeover;
        }

        // Pre-migration only: drops a type's entry from a still-present legacy database (so no type has two sources
        // of truth once its per-type asset exists) and saves the database. No-op post-migration (no database asset).
        private static void RemoveLegacyDatabaseEntry(string scriptGuid, string typeName)
        {
            LoadDatabase();
            if (_database == null) return;
            string simpleName = SimpleName(typeName);

            bool Matches(SasquatchDatabaseEntry e) =>
                (!string.IsNullOrEmpty(scriptGuid) && e.ScriptGuid == scriptGuid) ||
                e.TargetTypeName == typeName || e.TargetTypeName == simpleName;

            if (!_database.Entries.Exists(Matches)) return;
            _database.Entries.RemoveAll(Matches);
            EditorUtility.SetDirty(_database);
            AssetDatabase.SaveAssetIfDirty(_database);
        }

        // Renames a per-type layout file back to its deterministic name when it has drifted (e.g. the type was
        // renamed, changing the short-name segment). ONLY touches files already inside the managed Layouts/ folder —
        // a user-placed standalone asset elsewhere is left where it is. Skips (with a warning) if the target name is
        // already taken.
        private static void MaybeRenameToDeterministic(string currentPath, string scriptGuid, string typeName)
        {
            if (string.IsNullOrEmpty(currentPath)) return;
            string layoutsDir = SasquatchPaths.LayoutsDirectory;
            if (!currentPath.StartsWith(layoutsDir + "/", StringComparison.Ordinal)) return;

            string desired = SasquatchPaths.GetLayoutAssetPath(scriptGuid, typeName);
            if (currentPath == desired) return;

            if (AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(desired) != null)
            {
                Debug.LogWarning($"[Sasquatch Inspector] Wanted to rename layout '{currentPath}' → '{desired}' but the target " +
                                 "already exists; leaving it in place.");
                return;
            }

            string newName = Path.GetFileNameWithoutExtension(desired);
            string err = AssetDatabase.RenameAsset(currentPath, newName);
            if (!string.IsNullOrEmpty(err))
                Debug.LogWarning($"[Sasquatch Inspector] Could not rename layout '{currentPath}' → '{newName}': {err}");
        }

        // Recursively ensures an Assets-relative folder exists in the AssetDatabase (creating each missing segment
        // via AssetDatabase.CreateFolder), so AssetDatabase.CreateAsset into it succeeds even on first use / under
        // the UPM redirect to "Assets/Sasquatch Inspector User Data".
        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureAssetFolder(parent);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        // The simple type name (before the assembly qualifier) from an AssemblyQualifiedName; mirrors the simple-key
        // extraction in CacheByAllKeys so name-fallback matching is consistent.
        private static string SimpleName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return typeName;
            int comma = typeName.IndexOf(',');
            return comma > 0 ? typeName.Substring(0, comma).Trim() : typeName;
        }

        /// <summary>Ensures the per-type layouts folder exists in the AssetDatabase. Migration must call this BEFORE
        /// its <c>StartAssetEditing</c> batch — <c>AssetDatabase.CreateFolder</c> inside a batch sees a stale
        /// <c>IsValidFolder</c> and would spawn numbered duplicate folders ("Layouts 1", "Layouts 2", …).</summary>
        internal static void EnsureLayoutsFolder() => EnsureAssetFolder(SasquatchPaths.LayoutsDirectory);

        /// <summary>
        /// Migration helper (called by <see cref="LegacyDatabaseMigration"/>): creates a per-type layout asset at
        /// <paramref name="targetPath"/> from a legacy database <paramref name="entry"/>, copying all its layout data
        /// (Root by reference — it is a <c>[SerializeReference]</c> graph — plus fresh copies of the ledger lists and
        /// the flags). Safe because the source database is deleted right after migration. The caller decides whether
        /// to skip an entry already covered by an existing asset (existing wins), and MUST have ensured the layouts
        /// folder exists (<see cref="EnsureLayoutsFolder"/>) before its asset-editing batch — this method does not
        /// create the folder (doing so inside a batch spawns numbered duplicate folders). Stamps the current schema
        /// version and marks the new asset dirty; the caller batches the actual write. Returns the created asset.
        /// </summary>
        internal static InspectorLayoutAsset CreateMigratedLayoutAsset(SasquatchDatabaseEntry entry, string targetPath)
        {
            var asset = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            asset.ScriptGuid = entry.ScriptGuid;
            asset.TargetTypeName = entry.TargetTypeName;
            asset.Root = entry.Root; // [SerializeReference] graph — shared in-memory only; serialized into its own file
            asset.SyncedFieldPaths = entry.SyncedFieldPaths != null ? new List<string>(entry.SyncedFieldPaths) : new List<string>();
            asset.SyncedFieldTypes = entry.SyncedFieldTypes != null ? new List<string>(entry.SyncedFieldTypes) : new List<string>();
            asset.ApplyToSubclasses = entry.ApplyToSubclasses;
            asset.ForceTakeover = entry.ForceTakeover;
            asset.SchemaVersion = SasquatchSchema.CurrentVersion;

            // Deliberately written PLAIN (no LayoutGraphStorage pack) — this runs inside the caller's
            // StartAssetEditing batch, and the registry's next load reconciles the representation anyway
            // (ReconcileOnLoad queues a re-save when Compress Layout Assets is on).
            AssetDatabase.CreateAsset(asset, targetPath);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ---- Force-Takeover (per-type opt-in to draw the layout IN PLACE of the type's own [CustomEditor]) ----
        // The opt-in lives ON the per-type layout asset (co-located with the layout it takes over). The actual
        // display swap is done by SasquatchForceTakeover during the header scan (hide the custom editor's body +
        // render the layout on the header host via public VisualElement API).
        // This is the SAME displacement mechanism the global "Take Over Other Inspectors" setting uses for
        // attribute-less BLANKET editors (e.g. a third-party inspector plugin); this flag is the per-type opt-in
        // that gates it for a type with its OWN genuine [CustomEditor].

        /// <summary>True when <paramref name="targetType"/> should draw its Sasquatch Inspector layout IN PLACE of its own
        /// custom editor: the opt-in is set AND a real layout exists (own or inherited). Inherited-aware; pass the
        /// live <paramref name="instance"/> for the O(1) hot-path GUID resolve.</summary>
        public static bool IsForceTakeover(Type targetType, UnityEngine.Object instance = null)
            => TryGet(targetType, instance, out var layout) && layout != null && layout.ForceTakeover && layout.Root != null;

        /// <summary>True when <paramref name="targetType"/>'s OWN layout (never an inherited one) has the takeover
        /// opt-in set. Used by the designer to render the toggle's current state.</summary>
        public static bool IsForceTakeoverOwn(Type targetType)
            => TryGetOwn(targetType, out var layout) && layout != null && layout.ForceTakeover;

        /// <summary>Sets (or clears) the per-type takeover opt-in for <paramref name="targetType"/>, creating a
        /// per-type layout asset if none exists yet (a null-Root asset — the designer's Save later fills its Root;
        /// the flag persists across saves). Refreshes live inspectors so the change shows immediately.</summary>
        public static void SetForceTakeover(Type targetType, bool enabled)
        {
            if (targetType == null) return;
            string typeName = targetType.AssemblyQualifiedName;
            ScriptGuidUtility.TryGetGuid(targetType, out string scriptGuid);

            // Suppress the postprocessor's global ForceRebuild for the write below (we refresh per-type at the end).
            bool prevSuppress = SasquatchAssetPostprocessor.SuppressLayoutForceRebuild;
            SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = true;
            try
            {

            var asset = GetOrCreateLayoutAsset(scriptGuid, typeName); // null-Root asset if the type has no layout yet
            if (!string.IsNullOrEmpty(scriptGuid)) asset.ScriptGuid = scriptGuid;
            asset.TargetTypeName = typeName;

            // Complete-object undo, not RecordObject: RecordObject's flush-time DIFF of a [SerializeReference]
            // graph asset is pathologically slow and stalls every element detach while pending (see Save).
            Undo.RegisterCompleteObjectUndo(asset, enabled ? "Enable Sasquatch Takeover" : "Disable Sasquatch Takeover");
            asset.ForceTakeover = enabled;

            // Flag-only save: SaveLayoutAsset hydrates first, so a compressed asset's graph survives untouched.
            LayoutGraphStorage.SaveLayoutAsset(asset);

            // Close any caller-left RecordObject snapshot before the refresh below rebuilds inspectors (see Save).
            Undo.FlushUndoRecordObjects();

            // Pre-migration only: drop a still-present legacy database entry for this type (per-type asset now owns it).
            RemoveLegacyDatabaseEntry(scriptGuid, typeName);

            InitializeRegistryCache();
            SasquatchEditorRegistrar.RefreshInspectorsForType(targetType);

            }
            finally { SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = prevSuppress; }
        }

        // ---- Hidden (per-type "switch this layout off in the real Inspector, but keep it") ------------------
        // Same shape as Force-Takeover above and stored on the same per-type asset: a display toggle that the
        // designer's eye button flips and BOTH live render paths honour — VisualInspectorEditor falls back to
        // Unity's default inspector, SasquatchForceTakeover releases the row to the type's own [CustomEditor].
        // DESIGN-TIME surfaces deliberately ignore it (designer canvas, Showcase, tutorial preview, export):
        // hiding a layout must never make it un-editable, or the toggle would be a one-way door.

        /// <summary>True when <paramref name="targetType"/>'s layout (own or inherited) is currently hidden, so the
        /// live inspector must draw what the user would see without Sasquatch Inspector. Inherited-aware; pass the
        /// live <paramref name="instance"/> for the O(1) hot-path GUID resolve.</summary>
        public static bool IsLayoutHidden(Type targetType, UnityEngine.Object instance = null)
            => TryGet(targetType, instance, out var layout) && layout != null && layout.LayoutHidden;

        /// <summary>True when <paramref name="targetType"/>'s OWN layout (never an inherited one) is hidden. Used by
        /// the designer to render the eye button's current state.</summary>
        public static bool IsLayoutHiddenOwn(Type targetType)
            => TryGetOwn(targetType, out var layout) && layout != null && layout.LayoutHidden;

        /// <summary>Hides (or un-hides) <paramref name="targetType"/>'s layout in the real Inspector, creating a
        /// per-type layout asset if none exists yet (a null-Root asset — the designer's Save later fills its Root;
        /// the flag persists across saves). Refreshes live inspectors so the change shows immediately.</summary>
        public static void SetLayoutHidden(Type targetType, bool hidden)
        {
            if (targetType == null) return;
            string typeName = targetType.AssemblyQualifiedName;
            ScriptGuidUtility.TryGetGuid(targetType, out string scriptGuid);

            // Suppress the postprocessor's global ForceRebuild for the write below (we refresh per-type at the end).
            bool prevSuppress = SasquatchAssetPostprocessor.SuppressLayoutForceRebuild;
            SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = true;
            try
            {

            var asset = GetOrCreateLayoutAsset(scriptGuid, typeName); // null-Root asset if the type has no layout yet
            if (!string.IsNullOrEmpty(scriptGuid)) asset.ScriptGuid = scriptGuid;
            asset.TargetTypeName = typeName;

            // Complete-object undo, not RecordObject — same [SerializeReference] diff pathology as SetForceTakeover.
            Undo.RegisterCompleteObjectUndo(asset, hidden ? "Hide Sasquatch Layout" : "Show Sasquatch Layout");
            asset.LayoutHidden = hidden;

            // Flag-only save: SaveLayoutAsset hydrates first, so a compressed asset's graph survives untouched.
            LayoutGraphStorage.SaveLayoutAsset(asset);

            // Close any caller-left RecordObject snapshot before the refresh below rebuilds inspectors (see Save).
            Undo.FlushUndoRecordObjects();

            // Pre-migration only: drop a still-present legacy database entry for this type (per-type asset now owns it).
            RemoveLegacyDatabaseEntry(scriptGuid, typeName);

            InitializeRegistryCache();
            SasquatchEditorRegistrar.RefreshInspectorsForType(targetType);

            }
            finally { SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = prevSuppress; }
        }

        // Persists a live, in-place layout edit made through the rendered inspector (a column/row/text-box
        // resize) so it survives domain reloads and project saves. The node tree was mutated in place, so the
        // change already lives in the serialized graph — all that's left is to mark the ACTUAL on-disk object
        // dirty: the per-type asset itself, or (legacy) the database whose entry.Root the TRANSIENT wrapper
        // TryGet handed out aliases by reference (see InitializeRegistryCache). Marking that transient wrapper
        // is a no-op. Unity then writes it on the next save (Ctrl+S / quit / SaveAssets) — the standard Unity
        // persistence pattern: mutate serialized data → SetDirty the persisted object → let the editor save it.
        /// <summary>Writes an in-place edit of <paramref name="layout"/> back to disk (e.g. a live designer resize).</summary>
        public static void PersistLayoutEdit(InspectorLayoutAsset layout)
        {
            if (layout == null) return;

            // An inherited copy is session-only by design: auto-sync appends the subclass's own fields to
            // it in memory (good — they render), but NOTHING may persist, or the subclass's fields would be
            // written into the ancestor's saved layout. Editing in the designer materializes a real
            // subclass entry via Save() instead.
            if (layout.IsInheritedCopy) return;

            // A per-type InspectorLayoutAsset IS the persisted asset — mark it directly.
            if (AssetDatabase.Contains(layout))
            {
                EditorUtility.SetDirty(layout);
                if (!_pendingStandaloneFlush.Contains(layout)) _pendingStandaloneFlush.Add(layout);
                ScheduleDurableFlush();
                return;
            }

            // Otherwise it's the transient database wrapper: the real persisted object is the database, whose
            // entry.Root the wrapper aliases. Marking the database dirty captures the mutation on save.
            LoadDatabase();
            if (_database != null)
            {
                EditorUtility.SetDirty(_database);
                ScheduleDurableFlush();
            }
        }

        // ---- Durable flush for SetDirty-only edits (auto-sync reconcile + live in-inspector resizes) ----
        // SetDirty alone leaves the change in memory until Unity's next manual Ctrl+S / clean quit, so a crash,
        // force-quit, or a reimport that reloads the asset from disk silently DROPS the edit (a root cause of the
        // "layout reverts on reopen, no pattern" bug). We debounce an AssetDatabase.SaveAssetIfDirty (a flurry of
        // resize-drag edits coalesces into ONE write; SaveAssetIfDirty is a no-op when nothing is dirty) and add
        // quit + before-assembly-reload backstops so the edit is always flushed before any point that discards it.
        private static bool _flushScheduled;
        private static readonly List<InspectorLayoutAsset> _pendingStandaloneFlush = new();

        [InitializeOnLoadMethod]
        private static void RegisterDurableFlushHooks()
        {
            EditorApplication.quitting += FlushPendingLayoutEdits;
            AssemblyReloadEvents.beforeAssemblyReload += FlushPendingLayoutEdits;

            // Same two moments, second job — registered AFTER the flush above so a dirty edit is always written
            // before anything is torn down. The in-memory wrappers are HideFlags.DontSave, so the NATIVE objects
            // survive the domain reload that wipes the statics holding them, and DontSave includes
            // DontUnloadUnusedAsset — nothing can ever reclaim them, so every recompile strands one per database
            // entry / inherited copy for the life of the process. InvalidateCache cannot cover this no matter who
            // calls it: a script import's OnPostprocessAllAssets runs in the NEW domain, where these statics are
            // already empty, so it never sees the pre-reload objects. Only a beforeAssemblyReload hook runs while
            // they are still reachable.
            EditorApplication.quitting += DestroyTransientsForReload;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyTransientsForReload;
        }

        private static void ScheduleDurableFlush()
        {
            if (_flushScheduled) return;
            _flushScheduled = true;
            EditorApplication.delayCall += FlushPendingLayoutEdits;
        }

        /// <summary>Writes the database (and any standalone layout assets) marked dirty by
        /// <see cref="PersistLayoutEdit"/> to disk. Idempotent; runs debounced, on editor quit, and before a
        /// domain reload so SetDirty-only edits are never lost.</summary>
        public static void FlushPendingLayoutEdits()
        {
            _flushScheduled = false;
            // Snapshot the LAST-GOOD on-disk layouts folder (throttled ~1/hour) BEFORE this flush overwrites any file,
            // so a corrupted/bad-merged layout can be rolled back. Plain File IO under Library/ — no AssetDB churn, and
            // Library/ is always gitignored so backups never touch version control.
            BackupLayoutsIfDue();

            // Suppress the asset postprocessor's GLOBAL ActiveEditorTracker.ForceRebuild for OUR OWN durability write
            // below (save/restore so nesting is safe), mirroring what Save() does. This flush is a pure persistence
            // step: every edit that reaches it has ALREADY refreshed its own live UI at edit time — the quick-edit
            // popup via Commit→RebuildAndReglow, live in-inspector resizes via direct style pokes — so a blanket
            // rebuild here is not just wasted churn, it actively BREAKS the quick-edit right-click takeover: the forced
            // rebuild re-runs VisualInspectorEditor.RebuildInspectorGUI while the just-written asset is momentarily
            // mid-reimport, lands in the no-layout fallback branch, and never re-installs the menu takeover (the "the
            // Edit/Open-in-Designer items disappear until I reselect the object" bug). EXTERNAL layout changes (VC
            // pull, revert, a hand-edited asset) don't route through this method, so they still get the global rebuild.
            bool prevSuppress = SasquatchAssetPostprocessor.SuppressLayoutForceRebuild;
            SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = true;
            try
            {
                if (_database != null)
                {
                    _database.SchemaVersion = SasquatchSchema.CurrentVersion;
                    AssetDatabase.SaveAssetIfDirty(_database); // legacy: only a not-yet-migrated database is ever dirty here
                }
                for (int i = 0; i < _pendingStandaloneFlush.Count; i++)
                    if (_pendingStandaloneFlush[i] != null)
                    {
                        // Routes through the storage helper (not a raw SaveAssetIfDirty) so an in-place edit of a
                        // compressed asset re-packs its blob instead of writing the hydrated graph + a stale blob,
                        // and so a representation-mismatch re-save queued by ReconcileOnLoad rewrites the file in
                        // the format the Compress Layout Assets setting asks for. Stamps the schema version too.
                        LayoutGraphStorage.SaveLayoutAsset(_pendingStandaloneFlush[i]);
                    }
                _pendingStandaloneFlush.Clear();
            }
            finally { SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = prevSuppress; }
        }

        /// <summary>Queues a standalone layout asset for the same debounced durable flush
        /// <see cref="PersistLayoutEdit"/> uses. Used by <see cref="LayoutGraphStorage.ReconcileOnLoad"/> to
        /// rewrite a file whose storage representation doesn't match the Compress Layout Assets setting.</summary>
        internal static void QueueLayoutResave(InspectorLayoutAsset asset)
        {
            if (asset == null || !AssetDatabase.Contains(asset)) return;
            if (!_pendingStandaloneFlush.Contains(asset)) _pendingStandaloneFlush.Add(asset);
            ScheduleDurableFlush();
        }

        // ---- Layout backup + recovery ----
        // Rolling snapshots of the whole per-type layouts folder, taken as plain File.Copy directory snapshots under
        // Library/SasquatchInspector/Backups/Layouts_{stamp}/ (Library/ is always gitignored, so backups never touch version
        // control — no AssetDatabase churn). Each snapshot copies every layout .asset AND its .meta, so a restore
        // preserves GUIDs. Recovery is manual, from the settings page. The AUTOMATIC (before-each-save) path is
        // OPT-IN — off unless the user enables it in Project Settings ▸ Sasquatch Inspector (layouts and their snapshots can
        // get large) — and when on, its throttle frequency and how-many-kept cap are the user's settings too. The
        // manual restore's own pre-restore snapshot ignores the opt-in so a restore always stays reversible. The
        // snapshot/prune/throttle mechanics live in the injectable LayoutBackupService (see BackupService); this class
        // just wires it to the real project paths + EditorPrefs and reads the settings.
        private const string LastBackupPrefKeyBase = "SasquatchInspector.LastDbBackupUtcTicks";

        // EditorPrefs are global per-user on the MACHINE, not per project, so the throttle stamp has to carry a
        // stable per-project id — otherwise two projects share one throttle window and whichever saves first each
        // interval starves the other's backups. Same scheme (and same reasoning) as WelcomeState.LastSeenVersionKey
        // and ShowcaseOverrides. FNV-1a over Application.dataPath, not string.GetHashCode() — that one is randomized
        // per process on CoreCLR and would change every launch, re-arming the throttle every time.
        private static string LastBackupPrefKey => LastBackupPrefKeyBase + "." + ProjectId();

        private static string ProjectId()
        {
            string path = Application.dataPath ?? "";
            uint hash = 2166136261u;
            foreach (char c in path)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash.ToString("x8");
        }

        /// <summary>Absolute path to the folder holding layout backups (under the always-gitignored Library/).</summary>
        internal static string BackupsDirectoryAbs => Path.GetFullPath("Library/SasquatchInspector/Backups");

        // The backup engine. All snapshot/prune/throttle mechanics live in LayoutBackupService; this field is
        // swappable so tests can point the whole thing at a temp directory with a fake clock. Null ⇒ lazily built to
        // target the real project paths + the EditorPrefs throttle store.
        private static LayoutBackupService _backupService;

        /// <summary>The backup engine (real project paths + EditorPrefs by default). Tests may assign a temp-directory
        /// instance and restore it with <see cref="ResetBackupService"/>.</summary>
        internal static LayoutBackupService BackupService
        {
            get => _backupService ??= CreateDefaultBackupService();
            set => _backupService = value;
        }

        /// <summary>Restores the backup engine to the real-paths / EditorPrefs default (tests call this in teardown).</summary>
        internal static void ResetBackupService() => _backupService = null;

        private static LayoutBackupService CreateDefaultBackupService() => new LayoutBackupService(
            sourceDirAbs: () => Path.GetFullPath(SasquatchPaths.LayoutsDirectory),
            backupsRootAbs: () => BackupsDirectoryAbs,
            utcNow: () => DateTime.UtcNow,
            readLastBackupUtc: ReadLastBackupUtc,
            writeLastBackupUtc: WriteLastBackupUtc);

        private static DateTime? ReadLastBackupUtc()
        {
            if (!long.TryParse(EditorPrefs.GetString(LastBackupPrefKey, "0"), out long ticks) || ticks <= 0) return null;
            try { return new DateTime(ticks, DateTimeKind.Utc); } catch { return null; }
        }

        private static void WriteLastBackupUtc(DateTime utc) =>
            EditorPrefs.SetString(LastBackupPrefKey, utc.Ticks.ToString());

        /// <summary>Automatic (before-each-save) backup path: no-op unless the user has opted into automatic layout
        /// backups (Project Settings ▸ Sasquatch Inspector); when on, throttled to their chosen frequency and capped to their
        /// chosen number kept.</summary>
        private static void BackupLayoutsIfDue()
        {
            var settings = SasquatchSettingsManager.Current;
            BackupService.BackupIfDue(
                settings.EnableAutomaticLayoutBackups,
                TimeSpan.FromMinutes(settings.BackupFrequencyMinutes),
                settings.MaxLayoutBackups);
        }

        /// <summary>Absolute paths of the current layout snapshot directories, newest first (for the restore UI).</summary>
        internal static List<string> ListLayoutBackups() => BackupService.ListBackups();

        /// <summary>Replaces the current per-type layout assets with the snapshot in <paramref name="backupDir"/>
        /// (the current state is snapshotted first, so this is reversible), reimports, and refreshes the registry +
        /// open inspectors. Destructive — callers should confirm first. Returns true on success.</summary>
        internal static bool RestoreLayoutBackup(string backupDir)
        {
            try
            {
                if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir)) return false;

                string layoutsDir = SasquatchPaths.LayoutsDirectory; // Assets-relative
                EnsureAssetFolder(layoutsDir);
                string layoutsDirAbs = Path.GetFullPath(layoutsDir);

                // Stage the snapshot into a project-Temp folder FIRST. This front-loads the likeliest failure
                // (an unreadable/locked snapshot file) to BEFORE anything is deleted, so a mid-restore throw
                // can't leave the live layouts folder half-populated. Once staging fully succeeds, the
                // delete-then-swap runs against local temp files inside a single batched import scope.
                //
                // Staging also has to happen BEFORE the pre-restore snapshot below: that snapshot prunes, and
                // reading the source into Temp first means even a pathological prune can't strand the restore.
                string staging = FileUtil.GetUniqueTempPathInProject();
                Directory.CreateDirectory(staging);
                try
                {
                    foreach (string f in Directory.GetFiles(backupDir))
                        File.Copy(f, Path.Combine(staging, Path.GetFileName(f)), overwrite: true);

                    // Back up the CURRENT layouts (the dialog promises this is reversible). Snapshot unconditionally,
                    // regardless of the automatic-backup opt-in and throttle — this is a one-time, user-initiated
                    // restore, not the background accumulation the opt-in gates. Honors the user's "keep N" cap, but
                    // EXEMPTS the snapshot being restored from: this new snapshot always sorts newest, so at the
                    // shipped cap of 1 an unprotected prune would delete the user's chosen backup (and at any full
                    // cap N, the oldest — exactly what you pick when rolling further back), leaving them with only a
                    // snapshot of the broken state they were trying to escape.
                    BackupService.WriteSnapshot(SasquatchSettingsManager.Current.MaxLayoutBackups,
                                                protectDirAbs: backupDir);

                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        // Delete every current layout asset (via AssetDatabase so its DB stays consistent).
                        foreach (string guid in AssetDatabase.FindAssets("t:InspectorLayoutAsset"))
                        {
                            string p = AssetDatabase.GUIDToAssetPath(guid);
                            if (string.IsNullOrEmpty(p) || p == DatabasePath) continue;
                            if (p.StartsWith(layoutsDir + "/", StringComparison.Ordinal))
                                AssetDatabase.DeleteAsset(p);
                        }

                        // Swap the staged snapshot into place (assets + .meta ⇒ GUIDs preserved).
                        foreach (string f in Directory.GetFiles(staging))
                            File.Copy(f, Path.Combine(layoutsDirAbs, Path.GetFileName(f)), overwrite: true);
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                }
                finally
                {
                    try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { /* temp cleanup is best-effort */ }
                }

                AssetDatabase.Refresh();
                _database = null;
                InvalidateCache();
                InitializeRegistryCache();
                SasquatchEditorRegistrar.RefreshInspectors();

                // Every layout on disk was just replaced. An open designer still holds its pre-restore clone,
                // so one more edit + Save would write the state the user just restored AWAY from — the restore
                // silently undoing itself. Hand the new truth to any open designer. (Audit 2026-08-09, D1.)
                SasquatchLayoutDesignerWindow.AdoptExternalLayoutChange(null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Sasquatch Inspector] Failed to restore layout backup: " + e);
                return false;
            }
        }

        // Records undo for a live layout edit BEFORE the mutation, against the same persisted object
        // PersistLayoutEdit later dirties (the standalone asset, or the central database whose entry.Root
        // the transient wrapper aliases). Quick-edit-popup callers pair the two around each discrete edit:
        //   BeginLayoutEditUndo → mutate node → PersistLayoutEdit
        // VisualInspectorEditor already rebuilds on Undo.undoRedoPerformed, so the revert redraws for free.
        // (The continuous column/text-box resize drags intentionally skip undo; discrete edits shouldn't.)
        /// <summary>Opens an Undo group named <paramref name="undoName"/> around an edit to <paramref name="layout"/>.</summary>
        public static void BeginLayoutEditUndo(InspectorLayoutAsset layout, string undoName)
        {
            if (layout == null) return;

            // Session-only inherited copy: nothing persisted → nothing to record undo against (recording the
            // database here would create empty undo steps for edits that never reach it).
            if (layout.IsInheritedCopy) return;

            if (AssetDatabase.Contains(layout))
            {
                // Complete-object undo, not RecordObject: quick-edit callers rebuild inspector UI right after the
                // mutation, and RecordObject's still-pending [SerializeReference] diff makes every element detach
                // pay it (~45ms each — see Save). Complete snapshot is ~3ms up front, identical undo UX.
                Undo.RegisterCompleteObjectUndo(layout, undoName);
                return;
            }

            LoadDatabase();
            if (_database != null)
            {
                Undo.RegisterCompleteObjectUndo(_database, undoName);
            }
        }

        // Removes the layout for a type so its inspector falls back to Unity's stock inspector.
        // Deletes every per-type InspectorLayoutAsset bound to the type, and drops a matching entry from a
        // not-yet-migrated legacy database. Returns true if anything was removed.
        /// <summary>Removes the saved layout for <paramref name="targetType"/>; returns true if one existed.</summary>
        public static bool Remove(Type targetType)
        {
            if (targetType == null) return false;
            string typeName = targetType.AssemblyQualifiedName;
            string simpleName = targetType.FullName;
            ScriptGuidUtility.TryGetGuid(targetType, out string scriptGuid);
            bool removed = false;

            // Suppress the postprocessor's global ForceRebuild for the delete/save below (we refresh per-type at the end).
            bool prevSuppress = SasquatchAssetPostprocessor.SuppressLayoutForceRebuild;
            SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = true;
            try
            {

            bool Matches(SasquatchDatabaseEntry e) =>
                (!string.IsNullOrEmpty(scriptGuid) && e.ScriptGuid == scriptGuid) ||
                e.TargetTypeName == typeName || e.TargetTypeName == simpleName;

            bool MatchesAsset(InspectorLayoutAsset a) =>
                (!string.IsNullOrEmpty(scriptGuid) && a.ScriptGuid == scriptGuid) ||
                a.TargetTypeName == typeName || a.TargetTypeName == simpleName;

            // 1. Remove the entry from a still-present legacy central database.
            LoadDatabase();
            if (_database != null && _database.Entries.Exists(Matches))
            {
                // Complete-object undo, not RecordObject — its flush-time diff of [SerializeReference] graphs
                // stalls every element detach while pending (see Save).
                Undo.RegisterCompleteObjectUndo(_database, "Remove Sasquatch Layout");
                _database.Entries.RemoveAll(Matches);
                EditorUtility.SetDirty(_database);
                AssetDatabase.SaveAssetIfDirty(_database);
                Undo.FlushUndoRecordObjects();
                removed = true;
            }

            // 2. Delete the per-type InspectorLayoutAsset file(s) bound to this type (the primary store).
            string dbPath = DatabasePath;
            string[] guids = AssetDatabase.FindAssets("t:InspectorLayoutAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == dbPath) continue;

                var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (asset != null && MatchesAsset(asset))
                {
                    AssetDatabase.DeleteAsset(path);
                    removed = true;
                }
            }

            if (removed)
            {
                AssetDatabase.SaveAssets();
            }

            // 3. Drop cached entries (all key forms) and rescan so live inspectors pick up the change.
            if (!string.IsNullOrEmpty(scriptGuid)) _cache.Remove(scriptGuid);
            _cache.Remove(typeName);
            if (!string.IsNullOrEmpty(simpleName)) _cache.Remove(simpleName);
            InitializeRegistryCache();

            // Removing a layout reverts this type to Unity's default draw. No table edit is involved: our fallback
            // editor stays the resolved inspector (or defers to a real [CustomEditor]); with no layout, TryGet
            // misses and VisualInspectorEditor draws the default. Targeted refresh so only THIS type's inspectors
            // revert (the header scan releases any displaced body when takeover no longer applies), rather than
            // nudging the whole selection.
            SasquatchEditorRegistrar.RefreshInspectorsForType(targetType);

            return removed;

            }
            finally { SasquatchAssetPostprocessor.SuppressLayoutForceRebuild = prevSuppress; }
        }

        // ---- Orphan cleanup (automatic; see OrphanedLayoutCleanup) ----
        // When a script is deleted its layout is pruned automatically so the database never accumulates dead
        // entries. An entry is "dead" only when its MonoScript GUID resolves to no asset AND its stored type
        // name resolves to no loaded type — so a mere compile error (the .cs still exists ⇒ GUID resolves) is
        // never pruned, and a script deleted-and-recreated under the same name before the scan runs resolves
        // again and survives. The core predicate (IsEntryOrphaned) and the list mutation (RemoveEntriesFrom)
        // are factored out so both are unit-testable on in-memory entries/databases without touching the
        // user's on-disk layout database.

        /// <summary>
        /// Returns the central-database entries whose backing script is gone for good — the MonoScript GUID
        /// resolves to no asset AND the stored type name resolves to no loaded type. Returns an empty list
        /// while the editor is compiling/updating (never judge liveness in a transient state). Does not modify
        /// the database; pass the result to <see cref="RemoveEntries"/> to prune.
        /// </summary>
        internal static List<SasquatchDatabaseEntry> FindOrphanedEntries()
        {
            var dead = new List<SasquatchDatabaseEntry>();

            // Types transiently fail to resolve mid-compile/import; pruning then would be destructive.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return dead;

            LoadDatabase();
            if (_database == null) return dead;

            foreach (var entry in _database.Entries)
                if (IsEntryOrphaned(entry)) dead.Add(entry);

            return dead;
        }

        /// <summary>
        /// Per-entry orphan test (no database access, so it is unit-testable on in-memory entries): delegates to
        /// <see cref="IsLayoutOrphaned"/> on the entry's keys. The batch compile-state gate lives in
        /// <see cref="FindOrphanedEntries"/>.
        /// </summary>
        internal static bool IsEntryOrphaned(SasquatchDatabaseEntry entry)
            => entry != null && IsLayoutOrphaned(entry.ScriptGuid, entry.TargetTypeName);

        /// <summary>
        /// Core orphan predicate over a layout's keys (works for both a database entry and a standalone per-type
        /// asset): true only when the MonoScript GUID is present but resolves to no asset, AND the stored type name
        /// resolves to no loaded type, AND the assembly that type named is itself loaded (so "no such type" is an
        /// authoritative answer rather than "that whole assembly is absent right now"). No AssetDatabase
        /// enumeration beyond the GUID lookup, so it is unit-testable on raw keys.
        /// </summary>
        internal static bool IsLayoutOrphaned(string scriptGuid, string targetTypeName)
        {
            // An empty GUID is conservatively never flagged — there is no anchor to distinguish a deleted
            // script from a transient name-resolution failure (a compile error, a not-yet-imported file).
            if (string.IsNullOrEmpty(scriptGuid)) return false;

            // The backing script must be confirmed GONE. AssetDatabase.GUIDToAssetPath can return a STALE
            // "ghost" path for a just-deleted asset (emptiness alone is unreliable — verified live), so
            // confirm no MonoScript actually loads at the resolved path. A script with a mere COMPILE ERROR
            // still has a loadable MonoScript (only GetClass() returns null), so it is never treated as dead.
            string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
            bool guidDead = string.IsNullOrEmpty(scriptPath)
                            || AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath) == null;
            if (!guidDead) return false;

            // Load-bearing guard: a same-name recreation makes the type name resolve again, so the layout
            // re-associates on the next lookup — never treat those as orphans (that would defeat recovery).
            if (ResolveTypeFromName(targetTypeName) != null) return false;

            // Third term — "gone" must be distinguishable from "not here RIGHT NOW". The two tests above are both
            // satisfied by a script that is merely ABSENT rather than deleted: a UPM package removed / re-resolving,
            // a branch switch or stash that drops a tracked .cs, a folder renamed to end in '~' to isolate a compile
            // error. In every one of those the .cs comes back, but the layout would already have been deleted.
            //
            // The discriminator is the ASSEMBLY. A script deleted from a live assembly leaves that assembly loaded
            // (its siblings still compile), so the name genuinely resolved to nothing. A script whose whole assembly
            // is absent tells us nothing — we cannot see the type either way — so we must NOT conclude it is dead.
            // Erring here always errs toward keeping a layout file the user can still use.
            return IsDeclaringAssemblyLoaded(targetTypeName);
        }

        /// <summary>
        /// True when the assembly named in an assembly-qualified <paramref name="targetTypeName"/> is currently
        /// loaded — i.e. we are in a position to say authoritatively that the type is not in it. A bare
        /// <c>FullName</c> with no assembly qualifier (the legacy key format) carries no assembly to check, so it
        /// answers true and leaves the caller on its pre-existing behaviour.
        /// </summary>
        private static bool IsDeclaringAssemblyLoaded(string targetTypeName)
        {
            if (string.IsNullOrEmpty(targetTypeName)) return true;

            // "Ns.Type, AssemblyName, Version=..., Culture=..., PublicKeyToken=..." — the assembly SIMPLE name is
            // the second comma-delimited part. (Generic AQNs nest bracketed arguments, but the layout keys are
            // written from a MonoBehaviour/ScriptableObject type, which is never an open or closed generic here.)
            int firstComma = targetTypeName.IndexOf(',');
            if (firstComma < 0) return true; // bare FullName — no assembly to reason about
            int secondComma = targetTypeName.IndexOf(',', firstComma + 1);
            string assemblyName = secondComma < 0
                ? targetTypeName.Substring(firstComma + 1).Trim()
                : targetTypeName.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();
            if (string.IsNullOrEmpty(assemblyName)) return true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the given orphaned <paramref name="entries"/> from the central database, saves, and rebuilds
        /// the lookup cache so live inspectors refresh. Returns the number actually removed. No undo step: this
        /// is automatic cleanup triggered by a script deletion (itself not undoable), not a user action.
        /// </summary>
        internal static int RemoveEntries(IReadOnlyList<SasquatchDatabaseEntry> entries)
        {
            if (entries == null || entries.Count == 0) return 0;

            LoadDatabase();
            if (_database == null) return 0;

            int removed = RemoveEntriesFrom(_database, entries);
            if (removed == 0) return 0;

            EditorUtility.SetDirty(_database);
            AssetDatabase.SaveAssetIfDirty(_database);

            // Rebuild the type→layout cache (drops the wrappers for the removed entries) and refresh live
            // inspectors, mirroring Remove().
            InitializeRegistryCache();
            SasquatchEditorRegistrar.RefreshInspectors();

            return removed;
        }

        /// <summary>
        /// Removes <paramref name="entries"/> (by reference identity) from <paramref name="database"/> and
        /// returns the count removed. Pure list mutation — no undo/save/cache-rebuild — so it is unit-testable
        /// on an in-memory database. The public <see cref="RemoveEntries"/> wraps it with those side effects.
        /// </summary>
        internal static int RemoveEntriesFrom(SasquatchInspectorDatabase database, IReadOnlyList<SasquatchDatabaseEntry> entries)
        {
            if (database == null || entries == null || entries.Count == 0) return 0;
            var toRemove = new HashSet<SasquatchDatabaseEntry>(entries);
            int before = database.Entries.Count;
            database.Entries.RemoveAll(toRemove.Contains);
            return before - database.Entries.Count;
        }

        /// <summary>
        /// Returns the paths of per-type layout ASSETS whose backing script is gone for good (same predicate as
        /// <see cref="FindOrphanedEntries"/>, applied to standalone assets). Returns an empty list while the editor
        /// is compiling/updating (never judge liveness in a transient state). Pass the result to
        /// <see cref="RemoveLayoutAssets"/> to prune.
        /// </summary>
        internal static List<string> FindOrphanedLayoutAssetPaths()
        {
            var dead = new List<string>();

            // Types transiently fail to resolve mid-compile/import; pruning then would be destructive.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return dead;

            string dbPath = DatabasePath;
            // Shared with the removed-header sweep that drains alongside this one — see
            // LayoutAssetGuidsForDrain. The null check below is what makes a mid-drain deletion harmless.
            foreach (string guid in LayoutAssetGuidsForDrain())
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path == dbPath) continue;
                var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (asset == null) continue;
                if (IsLayoutOrphaned(asset.ScriptGuid, asset.TargetTypeName)) dead.Add(path);
            }
            return dead;
        }

        /// <summary>
        /// Deletes the given orphaned layout asset files and rebuilds the lookup cache so live inspectors refresh.
        /// Returns the number actually deleted. No undo step: this is automatic cleanup triggered by a script
        /// deletion (itself not undoable), mirroring <see cref="RemoveEntries"/> and <see cref="Remove"/>'s DeleteAsset.
        /// <para>Because there is no undo and no prompt, this is the one destructive path the user did not initiate,
        /// so it snapshots first — see the comment on the <see cref="BackupService"/> call below.</para>
        /// </summary>
        internal static int RemoveLayoutAssets(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0) return 0;

            // Snapshot BEFORE deleting, unconditionally — bypassing the automatic-backup opt-in and its throttle the
            // same way the manual restore does. Rationale: this is the only destructive path with no prompt, no undo
            // (see above) and no trash copy (AssetDatabase.DeleteAsset), and it fires automatically on a domain
            // reload. Any false positive of the orphan predicate is otherwise unrecoverable on a default install,
            // where automatic backups are off. Honoring the user's "keep N" cap is correct here: the snapshot written
            // now is the newest, so it is the one that survives the prune, and it holds the files about to be deleted.
            BackupService.WriteSnapshot(SasquatchSettingsManager.Current.MaxLayoutBackups);

            int removed = 0;
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) removed++;
            }
            if (removed == 0) return 0;

            // The shared walk is now describing files that no longer exist. A consumer later in this same
            // drain would still cope (they all null-check the load), but there is no reason to make them:
            // re-walk instead of reasoning from a set that was just invalidated.
            DropLayoutAssetGuidSnapshot();

            InitializeRegistryCache();
            SasquatchEditorRegistrar.RefreshInspectors();
            return removed;
        }

        /// <summary>
        /// Resolves every type that currently has a layout (per-type <see cref="InspectorLayoutAsset"/> files plus
        /// any not-yet-migrated central DB entries) to live <see cref="Type"/>s — the sweep list for the
        /// removed-header scan. Unresolvable GUIDs/names are skipped silently (a renamed-away or not-yet-compiled
        /// type simply isn't scanned this reload).
        /// </summary>
        public static List<Type> ResolveLayoutTypes()
        {
            var result = new List<Type>();
            var seen = new HashSet<Type>();

            void TryAdd(string scriptGuid, string targetTypeName)
            {
                Type t = null;
                if (!string.IsNullOrEmpty(scriptGuid)) t = ScriptGuidUtility.ResolveType(scriptGuid);
                if (t == null && !string.IsNullOrEmpty(targetTypeName)) t = ResolveTypeFromName(targetTypeName);
                if (t != null && seen.Add(t)) result.Add(t);
            }

            string dbPath = DatabasePath;
            var db = AssetDatabase.LoadAssetAtPath<SasquatchInspectorDatabase>(dbPath);
            if (db != null)
                foreach (var entry in db.Entries)
                    TryAdd(entry.ScriptGuid, entry.TargetTypeName);

            // The per-type layout assets (the primary store; same files TryGet resolves against). The index
            // walk is shared with the orphaned-ASSET sweep that drains alongside this one — see
            // LayoutAssetGuidsForDrain. The asset null check below already covers a path that sweep deleted.
            foreach (string guid in LayoutAssetGuidsForDrain())
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == dbPath) continue;
                var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (asset != null) TryAdd(asset.ScriptGuid, asset.TargetTypeName);
            }

            return result;
        }

        /// <summary>Flags the asset at <paramref name="assetPath"/> as changed so the lookup cache refreshes.</summary>
        public static void MarkChanged(string assetPath)
        {
            if (assetPath == DatabasePath)
            {
                _hasInitializedInitialScan = false;
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(assetPath);
            if (asset != null)
            {
                // A reimport reloads serialized state from disk, so a compressed asset arrives with a null Root
                // — hydrate (and self-heal a representation mismatch, e.g. a plain file pulled from VC) here too.
                LayoutGraphStorage.ReconcileOnLoad(asset);

                // Then MIGRATE, exactly as InitializeRegistryCache does after its own ReconcileOnLoad. This is the
                // SECOND load site — an asset that first appears after the initial scan (pulled from version
                // control, imported, or created by another tool) only ever passes through here. Without this it
                // would be cached, used, and then stamped CurrentVersion by SaveLayoutAsset on the next save,
                // having never been migrated: a permanently mis-stamped asset that no later pass will revisit.
                // Harmless while the only migration (v1 -> v2 conditions) also runs at bind time, but the v2 -> v3
                // seam SasquatchSchema invites would land silently. Order matters: hydrate before this walks the
                // tree. (Audit 2026-08-09, finding SR-3 / watch item 1.)
                ReconcileAssetSchema(asset);

                CacheByAllKeys(asset.ScriptGuid, asset.TargetTypeName, asset);
                // This is the SECOND way a path enters the cache (the first is InitializeRegistryCache), and
                // it must be tracked here too or a layout pulled from version control after the initial scan
                // would be invisible to the deleted-path test below.
                _cachedLayoutAssetPaths.Add(assetPath);
            }
            else if (_cachedLayoutAssetPaths.Remove(assetPath))
            {
                // A null load means the file is GONE — the asset postprocessor calls this for every deleted
                // .asset, and a deleted file cannot be type-tested. It used to drop the whole initial-scan
                // flag on any of them, so deleting an unrelated material variant or audio mixer cost the next
                // inspector open a full re-scan of every layout (measured 14.48 ms for 71).
                //
                // Gated on the tracked set instead: a path the registry never cached provably contributed
                // NOTHING to _cache, so dropping it changes no answer TryGet can give. The DatabasePath case
                // is handled by the early return at the top of this method and is unaffected.
                //
                // Deliberately NOT extended to the postprocessor's `changed` flag, or to the settings and
                // palette invalidations next to it. `changed` is what fires ActiveEditorTracker.ForceRebuild,
                // and gating that would leave every inspector rendering a deleted settings asset, palette
                // library or theme. Those memos could never be in a LAYOUT path set anyway, so gating them
                // would not narrow the check — it would stop it running at all.
                _hasInitializedInitialScan = false;
            }
        }

        // Registers an in-memory, session-only layout override for a type — checked by TryGet ahead of the
        // disk cache, never written to the database, and dropped on domain reload. The override owns a
        // transient InspectorLayoutAsset wrapping the supplied root. Re-registering replaces the previous one.
        /// <summary>Sets a session-only layout for <paramref name="targetType"/> (TryGet returns it first; not persisted).</summary>
        public static void RegisterSessionOverride(Type targetType, LayoutNode root)
        {
            if (targetType == null || root == null) return;

            if (_sessionOverrides.TryGetValue(targetType, out var existing) && existing != null)
                UnityEngine.Object.DestroyImmediate(existing);

            var asset = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            asset.hideFlags = HideFlags.DontSave;
            asset.Root = root;
            asset.TargetTypeName = targetType.AssemblyQualifiedName;
            if (ScriptGuidUtility.TryGetGuid(targetType, out string guid)) asset.ScriptGuid = guid;
            _sessionOverrides[targetType] = asset;
        }

        /// <summary>Removes the session-only layout override for <paramref name="targetType"/>, if any.</summary>
        public static void ClearSessionOverride(Type targetType)
        {
            if (targetType == null) return;
            if (_sessionOverrides.TryGetValue(targetType, out var existing) && existing != null)
                UnityEngine.Object.DestroyImmediate(existing);
            _sessionOverrides.Remove(targetType);
        }

        // Drops the in-memory cache so the next lookup rebuilds it. Called when scripts (re)compile so a
        // renamed/moved type is re-resolved through its (unchanged) GUID.
        /// <summary>Drops the cached type→layout lookup so it rebuilds on next access.</summary>
        public static void InvalidateCache()
        {
            _hasInitializedInitialScan = false;
            DestroyTransientWrappers();
            _cache.Clear();
            _ambiguousGuids.Clear();
            _cachedLayoutAssetPaths.Clear();
            DropLayoutAssetGuidSnapshot();
        }

        // Destroys ONLY the in-memory wrappers we created in InitializeRegistryCache (tracked in
        // _transientWrappers), never the persistent standalone InspectorLayoutAsset files that are also
        // cached. Run before clearing _cache so a re-scan starts clean without leaking the old wrappers.
        private static void DestroyTransientWrappers()
        {
            // The Contains guard is belt-and-braces: only CreateInstance / Instantiate results are ever tracked
            // here. But this list is now also drained on the domain-reload path, and destroying a real project
            // layout asset would destroy the user's work, so the guard states the rule rather than assuming it.
            foreach (var wrapper in _transientWrappers)
                if (wrapper != null && !AssetDatabase.Contains(wrapper)) UnityEngine.Object.DestroyImmediate(wrapper);
            _transientWrappers.Clear();
        }

        /// <summary>Destroys every layout object this class minted in memory this session — the database-entry /
        /// inherited-copy wrappers AND the session overrides — and drops the cache along with them, so nothing is
        /// left holding a fake-null. Wired to beforeAssemblyReload + quitting (see RegisterDurableFlushHooks);
        /// idempotent, and safe to call at any time since the next lookup simply re-scans.</summary>
        internal static void DestroyTransientsForReload()
        {
            InvalidateCache(); // destroys the wrappers and empties _cache/_ambiguousGuids in one step

            // Session overrides are the registry's other DontSave minting site (the Showcase Sandbox's in-memory
            // layouts). The dictionary dies with the domain, so without this they orphan exactly like the wrappers.
            foreach (var ov in _sessionOverrides.Values)
                if (ov != null && !AssetDatabase.Contains(ov)) UnityEngine.Object.DestroyImmediate(ov);
            _sessionOverrides.Clear();
        }
    }
}
