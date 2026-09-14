using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Per-user toggle (EditorPrefs): whether the designer's import tools apply the active Layout Preset's
    /// widget + config to newly-imported fields. On by default — presets are meant to be the path of least
    /// resistance. Explicit actions (the "Apply Presets to Layout" button, "Set as default") ignore this and
    /// always use the preset.
    /// </summary>
    public static class LayoutPresetPrefs
    {
        private const string Key = "SasquatchInspector.UseLayoutPresets";

        public static bool UseLayoutPresets
        {
            get => EditorPrefs.GetBool(Key, true);
            set => EditorPrefs.SetBool(Key, value);
        }
    }
}
