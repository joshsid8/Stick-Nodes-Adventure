using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Makes a live editor-skin flip (Preferences → General → Editor Theme) deterministic for
    /// Sasquatch inspectors: Unity repaints its own chrome, but inspectors built earlier keep the
    /// skin they were built with until something rebuilds them. Poll isProSkin and force a refresh
    /// on change; open tool windows restyle off <see cref="SkinChanged"/>. (The theme setting itself
    /// already refreshes via the settings page's TrackSerializedObjectValue.)
    /// </summary>
    [InitializeOnLoad]
    internal static class SasquatchSkinWatcher
    {
        private static bool _lastProSkin;

        /// <summary>Raised once per editor-skin flip, after inspectors were refreshed. Open tool
        /// windows subscribe to restyle their chrome without waiting for a reopen.</summary>
        internal static event System.Action SkinChanged;

        static SasquatchSkinWatcher()
        {
            _lastProSkin = EditorGUIUtility.isProSkin;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (pro == _lastProSkin) return;
            _lastProSkin = pro;
            SasquatchEditorRegistrar.RefreshInspectors();
            SkinChanged?.Invoke();
        }
    }
}
