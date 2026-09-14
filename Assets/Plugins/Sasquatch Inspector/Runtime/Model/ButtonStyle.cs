namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The visual preset a <see cref="ButtonNode"/> renders with. Values are serialized by int and
    /// intentionally fixed (same convention as <see cref="ContainerType"/>). Modern = 0 so buttons
    /// saved before the field existed upgrade to the new default look; Classic (1) is pixel parity
    /// with the pre-preset bare Unity button for anyone who wants the stock chrome back.
    /// </summary>
    public enum ButtonStyle
    {
        Modern = 0,
        Classic = 1,
        Outline = 2,
        Ghost = 3,
        Soft = 4,
        Danger = 5
    }

    /// <summary>Height class for a <see cref="ButtonNode"/> (applied as a grow-only min-height,
    /// per the resize convention). Medium = 0 keeps pre-existing buttons unchanged; Huge is appended
    /// (values fixed by serialization, so declaration order is NOT visual order — the dropdowns build
    /// their choices from <c>LayoutTreeBuilder.ButtonSizeOrder</c> instead of <c>Enum.GetValues</c>).
    /// A <see cref="ButtonNode.CustomHeight"/> above 0 overrides the class outright, so the classes
    /// stay coarse presets and the slider does the fine sizing.</summary>
    public enum ButtonSize
    {
        Medium = 0,
        Small = 1,
        Large = 2,
        // 3 is deliberately skipped — it briefly held a "Tiny" class that was cut before release.
        // Huge keeps 4 rather than sliding down into the gap: these values are the serialized form,
        // and renumbering would silently re-read any already-saved Huge button as something else.
        Huge = 4
    }

    /// <summary>Where a <see cref="ButtonNode"/>'s optional icon renders relative to the label. Left/Right
    /// share the row; Top/Bottom stack the icon above/below the label. Left = 0 keeps pre-existing buttons.</summary>
    public enum ButtonIconPlacement
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3
    }

    /// <summary>
    /// The hover/press feedback a <see cref="ButtonNode"/> plays. Serialized by int with intentionally
    /// fixed values (same convention as <see cref="ButtonStyle"/>). Bounce = 0 is the original springy
    /// lift + press-dip; the <see cref="ButtonNode"/> field initializer defaults to Lift, so buttons saved
    /// before it existed deserialize to Lift. Every option is transform-only (never reflows siblings) and
    /// a no-op when "reduce motion" is on.
    /// </summary>
    public enum ButtonHoverAnimation
    {
        /// <summary>The original default: a springy scale lift on hover plus a tactile press-dip.</summary>
        Bounce = 0,
        /// <summary>No transform feedback. (The preset's subtle color-on-hover still applies.)</summary>
        None = 1,
        /// <summary>A calm, low-overshoot rise with a gentle press — understated.</summary>
        Lift = 2,
        /// <summary>A larger, snappier rise — more pronounced than Bounce.</summary>
        Grow = 3,
        /// <summary>A quick scale "pop" each time the cursor enters — playful.</summary>
        Pop = 4
    }
}
