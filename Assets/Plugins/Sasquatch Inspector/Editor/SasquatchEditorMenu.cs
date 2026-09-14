using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>Stores and toggles Sasquatch Inspector's global inspector-override flag (persisted in EditorPrefs).</summary>
    public static class SasquatchEditorMenu
    {
        private const string PrefKey = "SasquatchInspector.EnableOverride";

        // Surfaced as the "Enable Sasquatch Inspector" master toggle in Project Settings → Sasquatch Inspector
        // (SasquatchSettingsPage), which is the only caller.
        /// <summary>Enables/disables the global inspector override, then refreshes live inspectors to show it.</summary>
        public static void SetOverrideEnabled(bool enabled)
        {
            if (enabled == IsOverrideEnabled()) return;

            // The opt-out is a pure runtime flag: our fallback editor is compiled-in and always registered
            // (an attribute can't be un-registered without a domain reload), so turning OFF doesn't remove us —
            // VisualInspectorEditor.RebuildInspectorGUI checks this flag first and draws Unity's exact default
            // inspector for every type instead of any layout. Observably identical to being absent. RefreshInspectors
            // rebuilds the live inspectors so the change shows immediately.
            EditorPrefs.SetBool(PrefKey, enabled);
            SasquatchEditorRegistrar.RefreshInspectors();
        }

        /// <summary>Whether the global inspector override is currently enabled.</summary>
        public static bool IsOverrideEnabled()
        {
            // Default ON. When ON, a type with a saved layout draws it and every other type draws Unity's default
            // (our fallback editor defers to any real [CustomEditor] and otherwise just renders the default), so
            // being enabled is safe. The toggle is a true global opt-out for users who want Sasquatch Inspector silent.
            return EditorPrefs.GetBool(PrefKey, true);
        }
    }
}
