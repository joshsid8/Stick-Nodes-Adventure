namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The base widget skin a <see cref="SasquatchThemeAsset"/> renders with. Serialized on theme
    /// assets (lives in Runtime for that reason); the editor maps it to the matching token
    /// stylesheet and WidgetPalette branch.
    /// </summary>
    public enum SasquatchThemeSkin
    {
        Dark = 0,
        Light = 1,
    }
}
