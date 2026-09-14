namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// How an expandable widget row (Animation Curve, Gradient, Asset Preview, Particle System, Inline
    /// Editor) draws its frame in the inspector
    /// (Project Settings ▸ Sasquatch Inspector ▸ Expandable Rows).
    /// </summary>
    public enum ExpandableRowStyle
    {
        /// <summary>Each row is its own bordered, rounded box with a gap above and below it.</summary>
        Card = 0,

        /// <summary>Rows sit flush with no box and no gap, parted by a hairline — a run of expandable
        /// widgets reads as one list instead of a stack of separate cards.</summary>
        List = 1
    }
}
