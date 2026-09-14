using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Ensures the project always has a default Layout Preset asset. On first editor load, if NO
    /// <see cref="SasquatchInspector.Runtime.SasquatchLayoutPreset"/> exists anywhere in the project, one is
    /// created (with the recommended defaults) under the package's "User Data" folder
    /// (<see cref="SasquatchPaths.GeneratedDirectory"/> — a legacy property name that resolves to the writable
    /// "User Data" location, never a literal "Generated" folder).
    ///
    /// Because this self-heals on first load, the preset asset does NOT need to ship: a project that imports
    /// the package without one gets an identical preset generated automatically the first time the editor loads.
    /// </summary>
    [InitializeOnLoad]
    public static class LayoutPresetBootstrap
    {
        static LayoutPresetBootstrap()
        {
            // Defer until the AssetDatabase is ready (avoids creating assets mid domain-reload import).
            EditorApplication.delayCall += EnsureDefaultPresetAsset;
        }

        /// <summary>
        /// SessionState key for "the project already has a preset". SessionState is the only store with the
        /// right lifetime here, and both alternatives are wrong in opposite directions:
        /// <list type="bullet">
        /// <item>a plain static is wiped by the domain reload this very method runs after, so it would never
        /// latch anything and the query below would run on every reload regardless;</item>
        /// <item>EditorPrefs would outlive the editor, so a preset deleted while Unity was CLOSED would come
        /// back to a latched "yes" and the project would sit with no default preset at all.</item>
        /// </list>
        /// </summary>
        internal const string PresetsExistKey = "Sasquatch.Presets.Exist";

        private static void EnsureDefaultPresetAsset()
        {
            // The query below is an UNSCOPED project-wide index walk (measured ~7-10 ms), and its cost is
            // result-independent — it walks the whole index whether it finds one preset or none. It ran on
            // every domain reload to re-learn a fact that has been true since the first load of the project.
            //
            // A stale-FALSE is free: one extra query that then latches. A stale-TRUE would mean skipping the
            // regeneration, so every route to one is closed: deleting a preset with the editor OPEN drops the
            // latch through SasquatchAssetPostprocessor's .asset gate, and deleting it with the editor CLOSED
            // cannot latch at all because SessionState starts empty. Behaviour is unchanged either way — this
            // method only ever runs on a domain reload, so today's code also regenerates no sooner than that.
            if (SessionState.GetBool(PresetsExistKey, false)) return;

            if (AssetDatabase.FindAssets("t:SasquatchLayoutPreset").Length > 0)
            {
                SessionState.SetBool(PresetsExistKey, true);
                return;
            }

            LayoutPresetManager.GetOrCreate(); // creates + populates at SasquatchPaths.GeneratedDirectory
            SessionState.SetBool(PresetsExistKey, true);
        }
    }
}
