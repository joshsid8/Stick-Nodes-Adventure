namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Whether a <see cref="ContainerType.Rows"/> / <see cref="ContainerType.Columns"/> container's tracks can
    /// be drag-resized.
    /// <para>
    /// <see cref="Auto"/> (the serialization default = 0, so existing layouts need no migration) means
    /// "resizable unless this is the root layout container" — explicitly-created Rows/Columns are resizable
    /// while the root stays clean (no resize handle under every top-level field). <see cref="Enabled"/> and
    /// <see cref="Disabled"/> are explicit user overrides set by the designer's "Resizable" toggle.
    /// </para>
    /// </summary>
    public enum TrackResizeMode
    {
        Auto = 0,
        Enabled = 1,
        Disabled = 2
    }
}
