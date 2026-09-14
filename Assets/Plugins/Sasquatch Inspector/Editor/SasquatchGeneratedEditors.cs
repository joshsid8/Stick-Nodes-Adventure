using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Classifier for the ONE question the inspector-takeover system asks about a type: does it have a genuine
    /// per-type <c>[CustomEditor]</c> of its OWN? That answer decides DEFER-vs-DISPLACE:
    ///   • has a genuine per-type editor → Sasquatch Inspector defers by default; it draws the layout in place only on an
    ///     explicit per-type <c>Force Takeover</c> opt-in;
    ///   • claimed only by a BLANKET framework editor (e.g. a third-party inspector plugin, which injects with
    ///     NO <c>[CustomEditor]</c> attribute) → taken over under the global "Take Over Other Inspectors" setting;
    ///   • nothing competing → drawn by our own fallback (<see cref="VisualInspectorEditor"/>).
    ///
    /// <para>Both takeovers are performed by PANEL DISPLACEMENT (<see cref="SasquatchForceTakeover"/>): the
    /// competitor's inspector body is hidden and the layout is drawn in its place via the
    /// <see cref="UnityEngine.UIElements.VisualElement"/> API. There is NO code generation and NO recompile —
    /// saving/removing a layout or toggling the setting is picked up live by the header scan. (Earlier versions
    /// generated exact-type <c>[CustomEditor]</c> stub SCRIPTS to out-resolve blanket editors; that mechanism was
    /// replaced by displacement so "zero recompiles" holds even with a third-party inspector framework installed.
    /// The one-time cleanup below removes the leftover generated file from installs that predate the rewrite.)</para>
    ///
    /// <para>The classifier reads the <c>CustomEditor</c> constructor arguments via
    /// <see cref="TypeCache"/> + <c>CustomAttributeData</c> (the same pattern used for CustomPropertyDrawer).</para>
    /// </summary>
    public static class SasquatchGeneratedEditors
    {
        // Memoized classifier result (null values cached too, so a type with no genuine editor isn't re-scanned).
        // The classifier now runs on the HOT scan path — every styled row, every scan tick — so the full
        // TypeCache walk must not repeat per call. The [CustomEditor] set only changes across a domain reload,
        // which resets this static, so the memo is always coherent.
        private static readonly Dictionary<Type, Type> s_GenuineEditorMemo = new Dictionary<Type, Type>();

        // One-time migration cleanup: delete the legacy auto-generated stub file (and its now-empty "Editor"
        // folder) left by installs that predate the displacement rewrite. Runs after every reload but is a no-op
        // once the file/folder are gone. Deleting the .cs costs exactly ONE recompile, then never again.
        [DidReloadScripts]
        private static void CleanupLegacyGeneratedFile()
        {
            // Never touch scripts mid compile/import — reschedule for the next idle tick.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CleanupLegacyGeneratedFile;
                return;
            }

            try
            {
                string filePath = SasquatchPaths.GeneratedInspectorsFilePath;
                if (File.Exists(Path.GetFullPath(filePath)))
                    AssetDatabase.DeleteAsset(filePath); // one migration recompile, then gone for good

                // Remove the now-empty legacy "Editor" folder.
                string dir = SasquatchPaths.GeneratedInspectorsDirectory;
                string dirAbs = Path.GetFullPath(dir);
                if (Directory.Exists(dirAbs) && Directory.GetFileSystemEntries(dirAbs).Length == 0)
                    AssetDatabase.DeleteAsset(dir);

                // Also prune the parent "Assets/Sasquatch Inspector Generated" root IFF it is now empty. For an Assets/
                // install (this is the common case) nothing ever writes there, so it lingers as a confusing empty
                // folder. The empty-check is the safety net: a read-only Packages/ install redirects the live
                // database/settings/preset assets here, but in that case the folder is NOT empty and is preserved.
                int slash = dir.LastIndexOf('/');
                if (slash > 0)
                {
                    string parentDir = dir.Substring(0, slash); // "Assets/Sasquatch Inspector Generated"
                    string parentAbs = Path.GetFullPath(parentDir);
                    if (Directory.Exists(parentAbs) && Directory.GetFileSystemEntries(parentAbs).Length == 0)
                        AssetDatabase.DeleteAsset(parentDir);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sasquatch Inspector] Legacy generated-inspector cleanup skipped: " + e.Message);
            }
        }

        /// <summary>
        /// True when a REAL per-type editor (the developer's own or a third party's) targets <paramref name="t"/>
        /// by its EXACT type — the DEFER case (a per-type Force Takeover opt-in still displaces it). Reads only
        /// Unity's <c>CustomEditor</c> ctor arguments; our own fallback/displacement editors are excluded,
        /// and a blanket editor injected WITHOUT a <c>[CustomEditor]</c> attribute is correctly not seen here —
        /// exactly the types we take over.
        /// </summary>
        public static bool HasGenuinePerTypeEditor(Type t) => GetGenuinePerTypeEditor(t) != null;

        /// <summary>The exact-type competing editor for <paramref name="t"/> (see <see cref="HasGenuinePerTypeEditor"/>), or null. Memoized per type (coherent until the next domain reload).</summary>
        public static Type GetGenuinePerTypeEditor(Type t)
        {
            if (t == null) return null;
            if (s_GenuineEditorMemo.TryGetValue(t, out var cached)) return cached;

            Type found = null;
            foreach (var editorType in TypeCache.GetTypesWithAttribute<CustomEditor>())
            {
                if (editorType == null) continue;
                if (typeof(VisualInspectorEditor).IsAssignableFrom(editorType)) continue; // our own fallbacks/displacement never count
                foreach (var ad in editorType.GetCustomAttributesData())
                {
                    if (ad.AttributeType != typeof(CustomEditor)) continue;
                    if (ad.ConstructorArguments.Count == 0) continue;
                    if (ad.ConstructorArguments[0].Value as Type == t) { found = editorType; break; } // names the EXACT type → genuine
                }
                if (found != null) break;
            }

            s_GenuineEditorMemo[t] = found;
            return found;
        }
    }
}
