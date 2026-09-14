using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Theme-aware colors for C#-painted WIDGET chrome (the counterpart of the --sasq-* USS tokens;
    /// DesignerPalette stays the palette of the designer tool's own chrome). The dark branch returns
    /// the historical constants verbatim, so dark rendering is pixel-identical by construction.
    ///
    /// Skin resolution: while a LayoutTreeBuilder.Build is in flight, <see cref="BuildScopeIsLight"/>
    /// pins the RESOLVED theme's skin (save/restore per build, recursion-safe — nested inline-editor
    /// builds re-resolve and restore). Outside a build (aux editor windows, popovers) it falls back
    /// to the ambient settings/editor-skin resolution.
    ///
    /// Rules for consumers:
    /// • generateVisualContent painters must CAPTURE colors at construction time (constructors run
    ///   inside the build scope; the deferred paint callback runs after it was popped).
    /// • Hover callbacks read the palette LIVE at fire time, never capture (designer convention).
    /// </summary>
    internal static class WidgetPalette
    {
        /// <summary>Set by LayoutTreeBuilder.Build for the duration of a build (save/restore).</summary>
        internal static bool? BuildScopeIsLight;

        /// <summary>Effective skin: the in-flight build's resolved skin, else the ambient resolution.</summary>
        public static bool IsLight => BuildScopeIsLight ?? SasquatchThemeResolver.AmbientIsLight();

        // ---- Ink & legacy helpers -------------------------------------------------------------

        /// <summary>White-alpha wash on dark, black-alpha on light (hover tints, hairlines, scrims).</summary>
        public static Color Ink(float a) => IsLight ? new Color(0f, 0f, 0f, a) : new Color(1f, 1f, 1f, a);

        /// <summary>
        /// Remap for PER-NODE SERIALIZED colors whose defaults were authored dark (e.g. the curve
        /// well/grid): substitutes the light equivalent only while the stored value still equals the
        /// historical dark default — a user's custom color is always respected.
        /// </summary>
        public static Color ResolveLegacy(Color stored, Color darkDefault, Color lightValue)
            => ResolveLegacy(IsLight, stored, darkDefault, lightValue);

        /// <summary>Capture-friendly overload for generateVisualContent painters (pass the skin captured at construction).</summary>
        public static Color ResolveLegacy(bool isLight, Color stored, Color darkDefault, Color lightValue)
            => isLight && Approximately(stored, darkDefault) ? lightValue : stored;

        private static bool Approximately(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f &&
               Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;

        // ---- Text ladder (dark values = historical constants verbatim) -------------------------

        /// <summary>Bright/near-black primary label (tabs title, group headers painted in C#).</summary>
        public static Color LabelPrimary => IsLight ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.85f, 0.85f, 0.85f);
        public static Color LabelSecondary => IsLight ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.82f, 0.82f, 0.82f);
        public static Color LabelMuted => IsLight ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.70f, 0.70f, 0.70f);
        public static Color LabelDim => IsLight ? new Color(0.36f, 0.36f, 0.36f) : new Color(0.60f, 0.60f, 0.60f);
        /// <summary>Full-strength text: white on dark, near-black on light.</summary>
        public static Color TextStrong => IsLight ? new Color(0.035f, 0.035f, 0.035f) : Color.white;
        /// <summary>Unity's Color.gray (0.5) reads muted on dark; darken on light for contrast.</summary>
        public static Color TextGray => IsLight ? new Color(0.30f, 0.30f, 0.30f) : Color.gray;

        // ---- Tabs (LayoutTreeBuilder tab strip) -------------------------------------------------

        public static Color TabSelectedFallback => IsLight
            ? new Color(0.24f, 0.45f, 0.72f, 0.9f)
            : new Color(0.18f, 0.35f, 0.58f, 0.8f);
        public static Color TabIdle => IsLight
            ? new Color(0.68f, 0.68f, 0.68f, 0.6f)
            : new Color(0.25f, 0.25f, 0.25f, 0.5f);
        public static Color TabHover => IsLight
            ? new Color(0.62f, 0.62f, 0.62f, 0.75f)
            : new Color(0.32f, 0.32f, 0.32f, 0.7f);
        public static Color TabStripBorder => IsLight
            ? new Color(0.55f, 0.55f, 0.55f)
            : new Color(0.3f, 0.3f, 0.3f);
        /// <summary>The 1px seam between two touching tabs. Tabs butt together (marginRight 0), so without
        /// it a run of inactive tabs merges into one grey slab with no visible boundaries. A shade DARKER
        /// than the idle fill on dark, lighter on light — it reads as a groove between segments, not as a
        /// border drawn around each one.</summary>
        public static Color TabDivider => IsLight
            ? new Color(0.78f, 0.78f, 0.78f, 0.9f)
            : new Color(0.20f, 0.20f, 0.20f, 0.9f);
        /// <summary>Fill behind the open tab's content panel. A lift off the inspector background — enough
        /// that the panel reads as one enclosed surface hanging under the strip, quiet enough that a Card
        /// sitting inside it still steps forward. The panel's four EDGES are derived from the tab's accent
        /// (LayoutTreeBuilder.TabPanelEdgeColor), not from here — only the fill is neutral.</summary>
        public static Color TabPanelBg => IsLight
            ? new Color(0f, 0f, 0f, 0.045f)
            : new Color(1f, 1f, 1f, 0.035f);

        // ---- Mixed (multi-object) state — widgets read these, not DesignerPalette --------------

        public static Color Mixed => IsLight ? new Color32(0x4F, 0x4F, 0x4F, 0xFF) : new Color32(0x9E, 0x9E, 0x9E, 0xFF);
        public static Color MixedBorder => IsLight ? new Color32(0x8C, 0x8C, 0x8C, 0xFF) : new Color32(0x6A, 0x6A, 0x6A, 0xFF);

        // ---- Amber warning callout (self-contained boxes like SceneReferenceDrawer's "not in Build Settings").
        // Dark values are the historical constants verbatim (pixel-identical on dark); light values are a soft
        // amber that reads at home in a light editor instead of a heavy dark-on-light block. ------------------
        public static Color WarningCalloutBg => IsLight ? new Color(0.99f, 0.94f, 0.80f) : new Color(0.24f, 0.18f, 0.08f);
        public static Color WarningCalloutBorder => IsLight ? new Color(0.80f, 0.62f, 0.28f) : new Color(0.55f, 0.40f, 0.10f);
        public static Color WarningCalloutText => IsLight ? new Color(0.48f, 0.34f, 0.06f) : new Color(0.95f, 0.75f, 0.35f);
        public static Color WarningCalloutButtonBg => IsLight ? new Color(0.96f, 0.87f, 0.62f) : new Color(0.35f, 0.28f, 0.15f);
        public static Color WarningCalloutButtonBorder => IsLight ? new Color(0.76f, 0.58f, 0.26f) : new Color(0.5f, 0.4f, 0.2f);
        public static Color WarningCalloutButtonText => IsLight ? new Color(0.32f, 0.22f, 0.04f) : new Color(0.95f, 0.85f, 0.65f);

        /// <summary>
        /// --sasq-danger-fg twin, for C#-painted chrome that USS cannot reach. Icon glyphs are the case that
        /// forces it: SasquatchIconElement paints from its Tint, so a `color` rule on the button does nothing.
        /// Reserved for controls that DESTROY something, never for a merely incomplete state.
        /// </summary>
        public static Color DangerText => IsLight ? new Color32(0xC2, 0x2A, 0x2A, 0xFF) : new Color32(0xF8, 0x71, 0x71, 0xFF);

        /// <summary>
        /// The C# side of the <c>--sasq-playhead</c> USS token (<c>#e0b020</c> dark, <c>#B87800</c> light).
        ///
        /// <para>The playhead itself is drawn from USS, but two things draw it with Painter2D and so cannot
        /// read the token: the legend diagram's explanatory playhead and the graph's log pins. Both used to
        /// carry their own literal, both carried only the DARK half, and the legend's own comment promised a
        /// lockstep that had never been kept — so on a light editor the legend taught the notation in the
        /// wrong ink. Keep this in lockstep with the two theme sheets at
        /// <c>SasquatchWidgetTheme{Dark,Light}.uss:131</c>; TestThemeTokenSheetParity pins it.</para>
        /// </summary>
        public static Color Playhead => IsLight ? new Color32(0xB8, 0x78, 0x00, 0xFF) : new Color32(0xE0, 0xB0, 0x20, 0xFF);

        // ---- Shared surfaces/borders for inline-styled chrome (match the USS tokens) ------------

        /// <summary>--sasq-well-bg twin for C#-painted wells.</summary>
        public static Color WellBg => IsLight ? new Color32(0xFA, 0xFA, 0xFA, 0xFF) : new Color32(0x1E, 0x1E, 0x1E, 0xFF);
        /// <summary>--sasq-control-bg twin.</summary>
        public static Color ControlBg => IsLight ? new Color32(0xE9, 0xE9, 0xE9, 0xFF) : new Color32(0x2B, 0x2B, 0x2B, 0xFF);
        /// <summary>--sasq-toggle-on twin: the switch track's ON fill when the node overrides nothing. Needed in
        /// C# (not just USS) because the ON/OFF overlay derives its ink from whatever the track actually is.</summary>
        public static Color ToggleOn => IsLight ? new Color32(0x3D, 0x7D, 0xD6, 0xFF) : new Color32(0x1E, 0x3D, 0x6B, 0xFF);
        /// <summary>--sasq-text-faint twin.</summary>
        public static Color TextFaint => IsLight ? new Color32(0x44, 0x44, 0x44, 0xFF) : new Color32(0x88, 0x88, 0x88, 0xFF);
        /// <summary>--sasq-border twin.</summary>
        public static Color Border => IsLight ? new Color32(0xA3, 0xA3, 0xA3, 0xFF) : new Color32(0x3F, 0x3F, 0x3F, 0xFF);
        /// <summary>--sasq-border-strong twin.</summary>
        public static Color BorderStrong => IsLight ? new Color32(0x8A, 0x8A, 0x8A, 0xFF) : new Color32(0x1E, 0x1E, 0x1E, 0xFF);
        /// <summary>--sasq-accent twin (progress fill default keeps its own member below).</summary>
        public static Color Accent => IsLight ? new Color32(0x22, 0x65, 0xB8, 0xFF) : new Color32(0x2F, 0x7A, 0xD4, 0xFF);
        /// <summary>Default alternating-row (zebra) tint applied to a collection's odd rows; a faint wash that
        /// contrasts the base row background in either skin.</summary>
        public static Color ZebraStripe => IsLight ? new Color(0f, 0f, 0f, 0.05f) : new Color(1f, 1f, 1f, 0.05f);

        // ---- Collection row separators (--sasq-row-* twins) ---------------------------------------
        // Deliberately INK, not a solid: a near-black hairline on the #2b2b2b group-content background is
        // a ~9/255 step and reads as a smudge, while a 10% white rule is ~22/255 and reads as a line. The
        // polarity flips on light for the same reason.

        /// <summary>The hairline drawn between rows in Line mode (--sasq-row-divider).</summary>
        public static Color RowDivider => IsLight ? new Color(0f, 0f, 0f, 0.14f) : new Color(1f, 1f, 1f, 0.1f);
        /// <summary>The left edge rail on a Card-mode row (--sasq-row-rail) — the only separator cue that
        /// scales with row HEIGHT, which is what a tall in-line-editor row needs.</summary>
        public static Color RowRail => IsLight ? new Color(0f, 0f, 0f, 0.26f) : new Color(1f, 1f, 1f, 0.2f);
        /// <summary>Card-mode row fill (--sasq-row-card-bg): a raised tray, one clear step off the content
        /// background — lighter on light (Unity's own light wells go near-white), lifted on dark.</summary>
        public static Color RowCardBg => IsLight ? new Color(1f, 1f, 1f, 0.7f) : new Color(1f, 1f, 1f, 0.045f);
        /// <summary>Hovered Card rail — the row you're pointing at. Accent blue is the package's hover
        /// language everywhere else, so a rail is the one place per-row hover can speak it without noise.</summary>
        public static Color RowRailHover => AccentEdge;

        /// <summary>
        /// Source-over composite of <paramref name="over"/> onto <paramref name="under"/>. Card rows need it:
        /// the card fill and the zebra tint are BOTH washes, and letting zebra simply replace the fill would
        /// erase the card (their alphas are within 0.01 of each other, so the odd rows would flatten out).
        /// </summary>
        public static Color Composite(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0.0001f) return new Color(0f, 0f, 0f, 0f);
            float inv = under.a * (1f - over.a);
            return new Color(
                (over.r * over.a + under.r * inv) / a,
                (over.g * over.a + under.g * inv) / a,
                (over.b * over.a + under.b * inv) / a,
                a);
        }

        // ---- More text / icon chrome -------------------------------------------------------------

        /// <summary>Card titles (brightest inline label).</summary>
        public static Color LabelBright => IsLight ? new Color(0.10f, 0.10f, 0.10f) : new Color(0.9f, 0.9f, 0.9f);
        /// <summary>Chevron/disclosure icon tint used with SasquatchIcons.Create.</summary>
        public static Color ChevronDim => IsLight ? new Color(0.40f, 0.40f, 0.40f, 1f) : new Color(0.365f, 0.365f, 0.365f, 1f);
        /// <summary>"+ tab" affordance text.</summary>
        public static Color TabAddText => IsLight ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.7f, 0.7f, 0.7f);
        /// <summary>Empty drop-slot "＋" glyph.</summary>
        public static Color DropSlotText => IsLight ? new Color(0.35f, 0.35f, 0.35f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.8f);

        // ---- Panels / frames ---------------------------------------------------------------------

        /// <summary>Translucent panel wash: Card container background + the "+ tab" chip.</summary>
        public static Color PanelWash => IsLight ? new Color(0.93f, 0.93f, 0.93f, 0.5f) : new Color(0.2f, 0.2f, 0.2f, 0.4f);
        /// <summary>Default border for user-set border widths with no explicit color; table header underline.</summary>
        public static Color BorderSoft => IsLight ? new Color(0.58f, 0.58f, 0.58f, 0.8f) : new Color(0.35f, 0.35f, 0.35f, 0.8f);
        /// <summary>Collection frame border (quieter than BorderSoft).</summary>
        public static Color BorderSofter => IsLight ? new Color(0.63f, 0.63f, 0.63f, 0.8f) : new Color(0.25f, 0.25f, 0.25f, 0.8f);
        /// <summary>"Empty Container" hint's dashed-look border.</summary>
        public static Color EmptyHintBorder => IsLight ? new Color(0.45f, 0.45f, 0.45f, 0.55f) : new Color(0.4f, 0.4f, 0.4f, 0.5f);

        // ---- Section tint bases (mirror .sasq-group-header / .sasq-group-content per skin) ---------

        public static Color GroupHeaderBase => IsLight ? new Color(0.722f, 0.722f, 0.722f, 1f) : new Color(0.220f, 0.220f, 0.220f, 1f);
        public static Color GroupContentBase => IsLight ? new Color(0.824f, 0.824f, 0.824f, 1f) : new Color(0.169f, 0.169f, 0.169f, 1f);
        /// <summary>Gray base the flat (non-collapsible) section tint lerps from.</summary>
        public static float FlatTintBase => IsLight ? 0.82f : 0.20f;

        // ---- Grips / resize affordances ------------------------------------------------------------

        /// <summary>Interactive column-divider line at rest.</summary>
        public static Color GripLineIdle => IsLight ? new Color(0.33f, 0.33f, 0.33f, 0.5f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
        /// <summary>Non-interactive (static) column separator — quieter than the draggable one.</summary>
        public static Color GripLineStatic => IsLight ? new Color(0.66f, 0.66f, 0.66f, 0.6f) : new Color(0.28f, 0.28f, 0.28f, 0.6f);
        /// <summary>Hovered divider/handle highlight (accent blue).</summary>
        public static Color GripHot => IsLight ? new Color(0.14f, 0.40f, 0.75f, 0.95f) : new Color(0.5f, 0.7f, 1f, 0.95f);
        /// <summary>Row-height divider line at rest (very faint).</summary>
        public static Color RowLineIdle => IsLight ? new Color(0.30f, 0.30f, 0.30f, 0.22f) : new Color(0.5f, 0.5f, 0.5f, 0.18f);
        /// <summary>Space-widget edge pill at rest.</summary>
        public static Color PillIdle => IsLight ? new Color(0.32f, 0.32f, 0.32f, 0.6f) : new Color(0.6f, 0.6f, 0.6f, 0.55f);
        /// <summary>Space-widget edge pill hovered.</summary>
        public static Color PillHot => IsLight ? new Color(0.14f, 0.40f, 0.75f, 0.9f) : new Color(0.5f, 0.7f, 1f, 0.9f);
        /// <summary>Text/curve/gradient edge grip handle at rest.</summary>
        public static Color HandleIdle => IsLight ? new Color(0.32f, 0.32f, 0.32f, 0.45f) : new Color(0.6f, 0.6f, 0.6f, 0.4f);
        /// <summary>Text/curve/gradient edge grip handle hovered.</summary>
        public static Color HandleHot => IsLight ? new Color(0.14f, 0.40f, 0.75f, 0.85f) : new Color(0.5f, 0.7f, 1f, 0.85f);

        // ---- Accent affordances rendered INSIDE the preview (follow the theme skin, not the editor) --

        /// <summary>Unity-blue accent (DesignerPalette.Accent twin for preview-content affordances).</summary>
        public static Color AccentUnity => IsLight ? new Color(0.13f, 0.38f, 0.72f, 1f) : new Color(0.18f, 0.48f, 0.85f, 1f);
        /// <summary>Faint accent wash (DesignerPalette.AccentSoft twin).</summary>
        public static Color AccentSoft => IsLight ? new Color(0.16f, 0.42f, 0.78f, 0.14f) : new Color(0.18f, 0.48f, 0.85f, 0.12f);
        /// <summary>Soft accent edge (DesignerPalette.HoverBorder twin).</summary>
        public static Color AccentEdge => IsLight ? new Color(0.20f, 0.45f, 0.75f, 0.7f) : new Color(0.35f, 0.55f, 0.8f, 0.6f);
        /// <summary>Danger-red button fill fallback (DesignerPalette.Danger twin; a touch more saturated on light).</summary>
        public static Color DangerFill => IsLight ? new Color(0.62f, 0.19f, 0.19f, 1f) : new Color(0.48f, 0.18f, 0.18f, 1f);

        /// <summary>
        /// Brand gold — the palette's SECOND signature color (the brand is blue + gold on charcoal), and the
        /// <see cref="DesignerPalette.BrandGold"/> twin so both surfaces speak one visual language.
        ///
        /// Reserved for the rare states that must NOT blend into the sea of accent blue. Blue is load-bearing all
        /// over an inspector — progress fills, selected tabs, play badges, dropdown selection, every hover
        /// affordance, the curve line, the search MATCH stripe — so anything painted in it reads as ordinary
        /// chrome. Gold is spent nowhere in the body (only the rare amber warning callout is even close), so it
        /// lands as a deliberate signal. Today: the "a filter is hiding fields" frame (SearchActiveOverlay).
        /// </summary>
        public static Color BrandGold => IsLight ? new Color(0.72f, 0.52f, 0.05f, 1f) : new Color(1f, 0.78f, 0.25f, 1f);

        // ---- Widget-specific chrome (dark = historical constants verbatim) -------------------------

        /// <summary>Accent badge fill (audio play button, particle play badge, 2D-toggle ON).</summary>
        public static Color BadgeAccent => IsLight ? new Color(0.15f, 0.36f, 0.63f, 1f) : new Color(0.18f, 0.35f, 0.58f, 1f);
        public static Color BadgeAccentSoft => IsLight ? new Color(0.15f, 0.36f, 0.63f, 0.92f) : new Color(0.18f, 0.35f, 0.58f, 0.92f);
        public static Color BadgeAccentHover => IsLight ? new Color(0.23f, 0.45f, 0.70f, 0.96f) : new Color(0.30f, 0.50f, 0.74f, 0.96f);
        /// <summary>"Drop an AudioClip" hint text over the waveform well.</summary>
        public static Color WaveHint => IsLight ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.72f, 0.72f, 0.72f, 1f);
        /// <summary>Default waveform teal (when the node sets no accent override).</summary>
        public static Color WaveformDefault => IsLight ? new Color(0.09f, 0.45f, 0.47f, 1f) : new Color(0.12f, 0.58f, 0.60f, 1f);
        /// <summary>Custom dropdown button face + border + arrow.</summary>
        public static Color DropdownBg => IsLight ? new Color(0.91f, 0.91f, 0.91f) : new Color(0.24f, 0.24f, 0.24f);
        public static Color DropdownBorder => IsLight ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.15f, 0.15f, 0.15f);
        public static Color ArrowDim => IsLight ? new Color(0.32f, 0.32f, 0.32f) : new Color(0.6f, 0.6f, 0.6f);
        /// <summary>Brighter chevron used by the inline-editor header.</summary>
        public static Color ChevronBright => IsLight ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.7f, 0.7f, 0.7f);
        /// <summary>Inline-editor nested panel wash + border.</summary>
        public static Color InlineSubBg => IsLight ? new Color(0.97f, 0.97f, 0.97f, 0.4f) : new Color(0.18f, 0.18f, 0.18f, 0.2f);
        public static Color InlineSubBorder => IsLight ? new Color(0.55f, 0.55f, 0.55f, 0.3f) : new Color(0.3f, 0.3f, 0.3f, 0.2f);
        /// <summary>Gradient color-popover background.</summary>
        public static Color PopoverBg => IsLight ? new Color(0.97f, 0.97f, 0.97f, 1f) : new Color(0.17f, 0.17f, 0.17f, 1f);
        /// <summary>Rich-text preview bubble default background.</summary>
        public static Color RichPreviewBg => IsLight ? new Color(0.93f, 0.93f, 0.945f, 1f) : new Color(0.145f, 0.145f, 0.16f, 1f);
        /// <summary>Progress-bar fill default. Keep in sync with --sasq-progress-fill in the theme sheets.</summary>
        public static Color ProgressFill => IsLight ? new Color(0.180f, 0.443f, 0.769f, 1f) : new Color(0.18f, 0.48f, 0.85f, 1f);
        /// <summary>Slider knob fallback when the node sets no knob color.</summary>
        public static Color SliderKnobDefault => IsLight ? new Color(0.45f, 0.45f, 0.45f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f);
        /// <summary>
        /// The editor's own inspector surface. Only used as the backdrop a read-only part is composited
        /// onto when its dim is rebuilt in colour instead of subtree opacity (see
        /// <c>BindingFactory.ApplyReadOnlySliderLook</c>) — and only as the FALLBACK: any opaque container
        /// background above the field wins. The dark value is Unity's inspector grey measured off a
        /// 6000.0 dark-skin capture; being a few levels out only shifts the dim slightly, so this never
        /// needs to track the skin exactly.
        /// </summary>
        public static Color ReadOnlyBackdrop => IsLight ? new Color32(0xB8, 0xB8, 0xB8, 0xFF) : new Color32(0x38, 0x38, 0x38, 0xFF);
        /// <summary>Aux-window accent icon tint (curve/gradient editor gear).</summary>
        public static Color AccentIcon => IsLight ? new Color(0.25f, 0.45f, 0.75f) : new Color(0.55f, 0.7f, 0.95f);

        /// <summary>
        /// Amber type tag — the same colour the designer puts on a binding's [type] label
        /// (<c>DesignerPalette.TypeAccent</c>, which is the source of truth for the value). Mirrored here
        /// rather than referenced because inspector-side chrome deliberately draws from WidgetPalette and
        /// designer chrome from DesignerPalette; they are kept apart on purpose.
        /// </summary>
        public static Color TypeAccent => IsLight ? new Color(0.55f, 0.36f, 0.02f, 1f) : new Color(0.95f, 0.75f, 0.3f, 1f);

        // ---- Dropdown popup windows (widget surfaces spawned from inspector dropdowns) --------------

        public static Color DropPopupBg => IsLight ? new Color(0.94f, 0.94f, 0.94f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
        public static Color DropPopupBorder => IsLight ? new Color(0.55f, 0.55f, 0.55f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
        public static Color DropPopupTitle => IsLight ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.75f, 0.75f, 0.75f);
        public static Color DropPopupInputBg => IsLight ? new Color(0.985f, 0.985f, 0.985f) : new Color(0.12f, 0.12f, 0.12f);
        public static Color DropPopupRowHairline => IsLight ? new Color(0.45f, 0.45f, 0.45f, 0.4f) : new Color(0.22f, 0.22f, 0.22f, 0.5f);
        public static Color DropPopupRowHover => IsLight ? new Color(0.55f, 0.55f, 0.55f, 0.35f) : new Color(0.25f, 0.25f, 0.25f, 0.7f);
        public static Color DropPopupSeparator => IsLight ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color(0.3f, 0.3f, 0.3f, 0.5f);
        /// <summary>Dropdown popup item text (carries the <see cref="LabelPrimary"/> value).</summary>
        public static Color DropPopupItemText => IsLight ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.85f, 0.85f, 0.85f);
        /// <summary>Selected/keyboard-highlighted row wash (white item text sits on it — more opaque on light).</summary>
        public static Color DropPopupSelection => IsLight ? new Color(0.15f, 0.36f, 0.63f, 0.75f) : new Color(0.18f, 0.35f, 0.58f, 0.6f);
        public static Color DropPopupSelectionStrong => IsLight ? new Color(0.15f, 0.36f, 0.63f, 0.9f) : new Color(0.18f, 0.35f, 0.58f, 0.9f);

        // ---- Spring graph / tester ------------------------------------------------------------------

        public static Color SpringWellBg => IsLight ? new Color(0.975f, 0.975f, 0.98f, 1f) : new Color(0.13f, 0.13f, 0.14f, 1f);
        public static Color SpringWellBorder => IsLight ? new Color(0.64f, 0.64f, 0.66f, 1f) : new Color(0.05f, 0.05f, 0.06f, 1f);
        public static Color TesterIdleBg => IsLight ? new Color(0.90f, 0.90f, 0.92f, 1f) : new Color(0.085f, 0.085f, 0.095f, 1f);
        public static Color TesterHoverBg => IsLight ? new Color(0.84f, 0.84f, 0.87f, 1f) : new Color(0.16f, 0.16f, 0.175f, 1f);
        public static Color TesterAccent => IsLight ? new Color(0.18f, 0.44f, 0.78f, 1f) : new Color(0.34f, 0.62f, 0.98f, 1f);
        public static Color TesterHandle => IsLight ? new Color(0.22f, 0.28f, 0.36f, 1f) : new Color(0.93f, 0.95f, 0.99f, 1f);

        // ---- Legacy per-node serialized defaults (dark-authored) + their light twins ----------------
        // Used with ResolveLegacy: only a stored value still equal to the dark default gets remapped.

        public static readonly Color CurveWellDarkDefault = new Color(0.118f, 0.118f, 0.118f, 1f); // #1e1e1e
        public static Color CurveWellLight => new Color(0.98f, 0.98f, 0.98f, 1f);
        public static readonly Color CurveGridDarkDefault = new Color(1f, 1f, 1f, 0.06f);
        public static Color CurveGridLight => new Color(0f, 0f, 0f, 0.08f);
        public static readonly Color CurveLineDarkDefault = new Color(0.45f, 0.78f, 1f, 1f); // #73C7FF
        public static Color CurveLineLight => new Color(0.15f, 0.42f, 0.72f, 1f);
        public static readonly Color CurveFillDarkDefault = new Color(0.45f, 0.78f, 1f, 0.14f);
        public static Color CurveFillLight => new Color(0.15f, 0.42f, 0.72f, 0.14f);
    }
}
