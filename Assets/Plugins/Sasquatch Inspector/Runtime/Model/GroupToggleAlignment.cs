namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Horizontal placement of a toggle-group Foldout's header checkbox + name within its header bar.
    /// The expand chevron is always pinned to the left edge; this only shifts the checkbox+name group.
    /// <see cref="Left"/> is the serialization default (= 0), matching the original always-left behavior.
    /// </summary>
    public enum GroupToggleAlignment
    {
        Left = 0,
        Center = 1,
        Right = 2
    }
}
