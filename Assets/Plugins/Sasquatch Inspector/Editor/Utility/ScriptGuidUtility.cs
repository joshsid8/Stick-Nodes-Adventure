using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Resolves between a <see cref="System.Type"/> and the GUID of its backing MonoScript asset
    /// (the <c>.cs.meta</c> GUID). That GUID is stable across class renames, namespace moves, and file
    /// moves, so it is the rename-proof key Sasquatch Inspector uses to store layouts (see LayoutRegistry).
    ///
    /// Two resolution directions, both memoized:
    ///  - Type/instance → GUID: instance overload is O(1) via MonoScript.FromMonoBehaviour/FromScriptableObject;
    ///    the type-only path lazily scans all MonoScripts once and caches a Type→GUID map.
    ///  - GUID → Type: via AssetDatabase.GUIDToAssetPath → MonoScript.GetClass().
    ///
    /// Target types are always top-level MonoBehaviour/ScriptableObject classes whose filename matches the
    /// class name, so MonoScript.GetClass() resolves reliably for them.
    ///
    /// Caveat: deleting and re-creating a script mints a new GUID, so the GUID key no longer matches. When a
    /// script is DELETED, OrphanedLayoutCleanup automatically prunes its layout entry (see that class), so a
    /// deleted script leaves no dead layout behind. A very fast delete+recreate under the same full name is
    /// tolerated: the debounced scan sees the type resolve again, and LayoutRegistry's type-name fallback plus
    /// Save()'s GUID-heal re-associate the layout.
    /// </summary>
    public static class ScriptGuidUtility
    {
        // Memoized Type → GUID (per-type, populated lazily / from the full scan below).
        private static readonly Dictionary<Type, string> _typeToGuid = new();
        // Memoized GUID → Type.
        private static readonly Dictionary<string, Type> _guidToType = new();
        // Full Type → GUID map built once by scanning every MonoScript; null until first needed.
        private static Dictionary<Type, string> _scannedTypeMap;

        /// <summary>
        /// O(1) GUID lookup for a live UnityEngine.Object (MonoBehaviour / ScriptableObject) using its
        /// backing MonoScript. Prefer this overload whenever an instance is available.
        /// </summary>
        public static bool TryGetGuid(UnityEngine.Object instance, out string guid)
        {
            guid = null;
            if (instance == null) return false;

            // Repeat inspector draws are common — serve from the per-type memo before touching MonoScript.
            if (_typeToGuid.TryGetValue(instance.GetType(), out guid) && !string.IsNullOrEmpty(guid))
                return true;

            MonoScript monoScript = instance switch
            {
                MonoBehaviour mb => MonoScript.FromMonoBehaviour(mb),
                ScriptableObject so => MonoScript.FromScriptableObject(so),
                _ => null
            };
            if (monoScript == null) return false;

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(monoScript, out string resolved, out long _)
                && !string.IsNullOrEmpty(resolved))
            {
                guid = resolved;
                Type cls = monoScript.GetClass();
                if (cls != null)
                {
                    _typeToGuid[cls] = resolved;
                    _guidToType[resolved] = cls;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the MonoScript GUID for a type without needing an instance. Lazily scans all MonoScripts
        /// the first time and caches the result.
        /// </summary>
        public static bool TryGetGuid(Type type, out string guid)
        {
            guid = null;
            if (type == null) return false;

            if (_typeToGuid.TryGetValue(type, out guid) && !string.IsNullOrEmpty(guid))
                return true;

            // ---------------------------------------------------------------------------------------------
            // DO NOT "optimise" this into a name-scoped AssetDatabase search. It was tried (2026-08-13) and
            // REVERTED, and the reason is worth more than the 60ms it would have saved.
            //
            // The shortcut looked provably exact: search for scripts whose FILE NAME matches the class name,
            // load only those, and accept one only when GetClass() returns this exact type — reasoning that
            // at most one MonoScript in a project can report a given Type, since two files cannot declare the
            // same type in one assembly. TestScriptGuidLookupMatchesAFullScan disproved it against this very
            // project: 2 of 203 sampled types resolved to a DIFFERENT GUID than the full scan gives (first
            // seen on Unity.InferenceEngine.Tokenization.Mappers.UnigramMapper). More than one MonoScript can
            // and does report the same Type here — a package shipped as both source and a precompiled
            // assembly is the likely shape — so "find one that matches" is not the same question as "which
            // one does the full scan settle on".
            //
            // That distinction is not academic: this GUID is the rename-proof key every layout is stored
            // under, so picking the other one is a layout that silently stops being found for its type. The
            // scan below is a one-time-per-domain cost on a specific trigger; a wrong key is forever.
            // ---------------------------------------------------------------------------------------------
            EnsureScannedMap();
            if (_scannedTypeMap.TryGetValue(type, out guid) && !string.IsNullOrEmpty(guid))
            {
                _typeToGuid[type] = guid;
                _guidToType[guid] = type;
                return true;
            }

            guid = null;
            return false;
        }

        /// <summary>
        /// Resolves the type backing a MonoScript GUID, or null if the GUID no longer maps to a script/class.
        /// </summary>
        public static Type ResolveType(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            if (_guidToType.TryGetValue(guid, out Type cached) && cached != null)
                return cached;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Type type = monoScript != null ? monoScript.GetClass() : null;
            if (type != null)
            {
                _guidToType[guid] = type;
                _typeToGuid[type] = guid;
            }
            return type;
        }

        /// <summary>
        /// Clears all cached mappings. Called when scripts are imported/moved/deleted, since a class rename
        /// retires the old Type and introduces a new one under the same GUID.
        /// </summary>
        public static void InvalidateCache()
        {
            _typeToGuid.Clear();
            _guidToType.Clear();
            _scannedTypeMap = null;
        }

        private static void EnsureScannedMap()
        {
            if (_scannedTypeMap != null) return;

            _scannedTypeMap = new Dictionary<Type, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Type cls = monoScript != null ? monoScript.GetClass() : null;
                if (cls == null) continue; // null for generics / non-behaviour scripts — not a layout target

                // First writer wins; duplicate class names across files are pathological and rare.
                _scannedTypeMap[cls] = guid;
            }
        }
    }
}
