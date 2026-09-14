using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared accent / border / highlight colors for the DESIGNER TOOL'S OWN chrome (windows, panels,
    /// popups). Skin-aware since the light-theme work: every member branches on the EDITOR skin —
    /// designer chrome follows Unity's skin, while rendered inspector CONTENT follows the assigned
    /// theme via <see cref="WidgetPalette"/> (whose build-scope skin can differ from the editor's).
    ///
    /// Rules for members:
    /// • The dark branch is the historical constant VERBATIM — dark rendering must stay pixel-identical.
    /// • Properties, not static readonly fields, so a live skin flip is picked up on the next rebuild.
    /// • Hover callbacks read these LIVE at fire time, never capture (designer style-row convention).
    /// </summary>
    public static class DesignerPalette
    {
        /// <summary>The editor-skin branch every member keys off.</summary>
        public static bool IsLight => !EditorGUIUtility.isProSkin;

        public static Color Accent => IsLight ? new Color(0.13f, 0.38f, 0.72f, 1f) : new Color(0.18f, 0.48f, 0.85f, 1f); // Unity Blue
        public static Color AccentSoft => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.14f) : new Color(0.18f, 0.48f, 0.85f, 0.12f);
        public static Color NestHighlight => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.28f) : new Color(0.18f, 0.48f, 0.85f, 0.25f);
        public static Color HoverBorder => IsLight ? new Color(0.22f, 0.45f, 0.72f, 0.7f) : new Color(0.35f, 0.55f, 0.8f, 0.6f);
        public static Color NormalBorder => IsLight ? new Color(0.42f, 0.42f, 0.42f, 0.4f) : new Color(0.35f, 0.35f, 0.35f, 0.3f);
        public static Color SelectedBorder => IsLight ? new Color(0.13f, 0.38f, 0.72f, 1f) : new Color(0.18f, 0.48f, 0.85f, 1f);
        public static Color Danger => IsLight ? new Color(0.68f, 0.20f, 0.20f, 1f) : new Color(0.48f, 0.18f, 0.18f, 1f);
        // Muted grays for the "mixed value" state on multi-object selection (match the enum-badge --mixed USS).
        // The dark values run lighter than that USS, whose #8a8a8a / #555555 grays sit below comfortable contrast
        // on the dark canvas.
        public static Color Mixed => IsLight ? new Color(0.353f, 0.353f, 0.353f, 1f) : new Color(0.620f, 0.620f, 0.620f, 1f);       // #9e9e9e dark
        public static Color MixedBorder => IsLight ? new Color(0.549f, 0.549f, 0.549f, 1f) : new Color(0.416f, 0.416f, 0.416f, 1f); // #6a6a6a dark

        /// <summary>White-alpha wash on dark chrome, black-alpha on light (hover tints, hairlines, scrims).</summary>
        public static Color Ink(float a) => IsLight ? new Color(0f, 0f, 0f, a) : new Color(1f, 1f, 1f, a);

        // ---- Designer-window chrome (light values keep light-skin text on the #090909→#4A4A4A ladder) ----

        /// <summary>Brightest chrome text (selected tab labels, toast messages, tool-card titles).</summary>
        public static Color TextBright => IsLight ? new Color(0.035f, 0.035f, 0.035f, 1f) : new Color(0.9f, 0.9f, 0.9f, 1f);
        /// <summary>Primary chrome text (row/label text throughout the designer).</summary>
        public static Color TextPrimary => IsLight ? new Color(0.10f, 0.10f, 0.10f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
        /// <summary>Pane header / toggle-label text.</summary>
        public static Color TextHeader => IsLight ? new Color(0.165f, 0.165f, 0.165f, 1f) : new Color(0.8f, 0.8f, 0.8f, 1f);
        /// <summary>Text on neutral chrome buttons (e.g. the "T" text-style toggle at rest).</summary>
        public static Color TextButton => IsLight ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
        /// <summary>Muted hint text.</summary>
        public static Color TextMuted => IsLight ? new Color(0.29f, 0.29f, 0.29f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f);
        /// <summary>Faint text (collapsed-section titles, "+ Add Tab" at rest).</summary>
        public static Color TextFaint => IsLight ? new Color(0.32f, 0.32f, 0.32f, 1f) : new Color(0.55f, 0.55f, 0.55f, 1f);
        /// <summary>Dimmest text (empty-state / placeholder messages).</summary>
        public static Color TextDisabled => IsLight ? new Color(0.35f, 0.35f, 0.35f, 1f) : Color.gray;
        /// <summary>Tab Organizer card name text.</summary>
        public static Color OrganizerCardText => IsLight ? new Color(0.08f, 0.08f, 0.08f, 1f) : new Color(0.88f, 0.88f, 0.88f, 1f);
        /// <summary>Strong glyph/text on NEUTRAL chrome (icon hover states, glyphs on neutral buttons). White on dark ⇄ near-black on light — never use on accent fills (those keep Color.white).</summary>
        public static Color IconStrong => IsLight ? new Color(0.06f, 0.06f, 0.06f, 1f) : Color.white;
        /// <summary>Idle icon tint / glyph text on neutral option buttons.</summary>
        public static Color IconIdle => IsLight ? new Color(0.13f, 0.13f, 0.13f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
        /// <summary>Muted icon tint (gear/caret icons at rest).</summary>
        public static Color IconMuted => IsLight ? new Color(0.247f, 0.247f, 0.247f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f);

        // Surfaces (Unity light chrome window ≈ #C8C8C8; headers sit slightly darker than their panel).
        /// <summary>Designer window root background.</summary>
        public static Color WindowBg => IsLight ? new Color(0.784f, 0.784f, 0.784f, 1f) : new Color(0.12f, 0.12f, 0.12f, 1f);
        /// <summary>Center canvas background (darker than the chrome so rendered cards pop).</summary>
        public static Color CanvasBg => IsLight ? new Color(0.67f, 0.67f, 0.67f, 1f) : new Color(0.13f, 0.13f, 0.13f, 1f);
        /// <summary>Left (hierarchy/token) panel background.</summary>
        public static Color PanelBg => IsLight ? new Color(0.769f, 0.769f, 0.769f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f);
        /// <summary>Right (properties) panel background.</summary>
        public static Color PanelBgAlt => IsLight ? new Color(0.78f, 0.78f, 0.78f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);
        /// <summary>Pane header strips + unselected left-tab background.</summary>
        public static Color PaneHeaderBg => IsLight ? new Color(0.71f, 0.71f, 0.71f, 1f) : new Color(0.16f, 0.16f, 0.16f, 1f);
        /// <summary>Top toolbar background.</summary>
        public static Color ToolbarBg => IsLight ? new Color(0.73f, 0.73f, 0.73f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
        /// <summary>Raised card background (animation rows) on a panel.</summary>
        public static Color CardBg => IsLight ? new Color(0.82f, 0.82f, 0.82f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
        /// <summary>Toast background.</summary>
        public static Color ToastBg => IsLight ? new Color(0.92f, 0.92f, 0.92f, 0.98f) : new Color(0.17f, 0.17f, 0.17f, 0.98f);
        /// <summary>Token/preset row background at rest.</summary>
        public static Color RowBg => IsLight ? new Color(0.84f, 0.84f, 0.84f, 1f) : new Color(0.22f, 0.22f, 0.22f, 1f);
        /// <summary>Token/preset row hover (lightens on dark ⇄ darkens on light).</summary>
        public static Color RowHoverBg => IsLight ? new Color(0.79f, 0.79f, 0.79f, 1f) : new Color(0.30f, 0.30f, 0.30f, 1f);
        /// <summary>Selected token row / organizer card background (dark text stays readable on light).</summary>
        public static Color RowSelectedBg => IsLight ? new Color(0.60f, 0.71f, 0.86f, 1f) : new Color(0.20f, 0.40f, 0.62f, 1f);
        /// <summary>Tab Organizer card background at rest.</summary>
        public static Color OrganizerCardBg => IsLight ? new Color(0.81f, 0.81f, 0.83f, 1f) : new Color(0.28f, 0.28f, 0.30f, 1f);
        /// <summary>Tab Organizer card hover background.</summary>
        public static Color OrganizerCardHoverBg => IsLight ? new Color(0.76f, 0.76f, 0.80f, 1f) : new Color(0.34f, 0.34f, 0.38f, 1f);
        /// <summary>Tools-tab action card background.</summary>
        public static Color ToolCardBg => IsLight ? new Color(0.82f, 0.82f, 0.82f, 1f) : new Color(0.20f, 0.20f, 0.20f, 1f);
        /// <summary>Tools-tab action card hover background.</summary>
        public static Color ToolCardHoverBg => IsLight ? new Color(0.78f, 0.78f, 0.78f, 1f) : new Color(0.28f, 0.28f, 0.28f, 1f);
        /// <summary>Tools-tab action card border.</summary>
        public static Color ToolCardBorder => IsLight ? new Color(0.62f, 0.62f, 0.62f, 1f) : new Color(0.26f, 0.26f, 0.26f, 1f);
        /// <summary>Left-tab hover background (inactive tabs).</summary>
        public static Color TabHoverBg => IsLight ? new Color(0.68f, 0.68f, 0.68f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);
        /// <summary>
        /// Recessed track behind the left tab bar — the surface the idle folder tabs sit in, and what shows
        /// through around the active one. It has to read DARKER than <see cref="PanelBg"/> in BOTH skins,
        /// which is exactly why it is its own member rather than a reuse: <see cref="WindowBg"/> is darker
        /// than the panel on dark (0.12 vs 0.14) but LIGHTER on light (0.784 vs 0.769), so borrowing it would
        /// silently INVERT the recess the moment the editor skin flips.
        /// </summary>
        public static Color TabTrackBg => IsLight ? new Color(0.66f, 0.66f, 0.66f, 1f) : new Color(0.10f, 0.10f, 0.10f, 1f);
        /// <summary>Neutral chrome button face (mode buttons, mini up/down, segmented idle).</summary>
        public static Color ButtonNeutralBg => IsLight ? new Color(0.894f, 0.894f, 0.894f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
        /// <summary>Segmented-button hover while idle (lightens on dark ⇄ darkens on light).</summary>
        public static Color SegmentHoverIdleBg => IsLight ? new Color(0.84f, 0.84f, 0.84f, 1f) : new Color(0.33f, 0.33f, 0.33f, 1f);
        /// <summary>Segmented-button hover while active (brightened accent).</summary>
        public static Color SegmentHoverActiveBg => IsLight ? new Color(0.20f, 0.46f, 0.80f, 0.9f) : new Color(0.24f, 0.44f, 0.70f, 0.9f);

        /// <summary>
        /// The hover tint for a chrome button whose face is painted INLINE (Save, the Design/Live-Preview
        /// pair, Revert, the column toggles). An inline background beats Unity's stock <c>:hover</c> USS, so
        /// those buttons show no color feedback at all unless a callback supplies it — this is the one rule
        /// they share. Saturated faces (accent blue, danger red, dirty-Save amber) BRIGHTEN in both skins,
        /// matching <see cref="SegmentHoverActiveBg"/>; near-neutral faces follow <see cref="SegmentHoverIdleBg"/>
        /// and lighten on dark / darken on light, because lightening an already-pale light-skin face just
        /// washes it into the toolbar behind it.
        /// </summary>
        public static Color Hovered(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            bool saturated = max - min > 0.12f;
            float toward = (saturated || !IsLight) ? 1f : 0f;
            const float t = 0.13f;
            return new Color(Mathf.Lerp(c.r, toward, t), Mathf.Lerp(c.g, toward, t), Mathf.Lerp(c.b, toward, t), c.a);
        }

        // Accent fills (saturated fills keep white glyphs in both skins).
        /// <summary>Solid accent button fill (Save, active mode button, open "T" panel).</summary>
        public static Color AccentFill => IsLight ? new Color(0.13f, 0.38f, 0.72f, 1f) : new Color(0.18f, 0.35f, 0.58f, 1f);
        /// <summary>Accent fill for active segmented/alignment option buttons.</summary>
        public static Color AccentFillSoft => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.8f) : new Color(0.18f, 0.35f, 0.58f, 0.8f);
        /// <summary>Selected left-tab background wash.</summary>
        public static Color AccentTabBg => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.4f) : new Color(0.18f, 0.35f, 0.58f, 0.4f);
        /// <summary>Primary-selection row wash (hierarchy).</summary>
        public static Color AccentWashStrong => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.5f) : new Color(0.18f, 0.35f, 0.58f, 0.5f);
        /// <summary>Secondary multi-selection row wash (hierarchy).</summary>
        public static Color AccentWash => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.3f) : new Color(0.18f, 0.35f, 0.58f, 0.3f);
        // ---- Save: a two-state button, so the PAIR is designed, not each color on its own ----------------
        //
        // The tuning rule here is LUMINANCE, not hue. An earlier brand-gold Save sat at ~0.62 relative
        // luminance against a ~0.045 charcoal — a 13x jump, which reads as a raw alert state rather than a
        // deliberate accent, and left white text at an unreadable 1.6:1. Both faces below sit near ~0.09, so
        // the button is calm against the chrome AND clears WCAG AA against white text.
        //
        // They also sit at nearly the SAME luminance as EACH OTHER, so going dirty reads as a hue change
        // rather than a brightness flash — the state change is obvious without the button ever shouting.
        //
        // Why these run deeper than the "obvious" pick of each hue: the hover tint (Hovered, +13% toward
        // white) is a big relative jump on a deep saturated fill. Tuned so the FACE clears AA, the HOVERED face
        // came in at 4.27:1 — a real painted state you read a label on, failing quietly. Pick from the hovered
        // value, not the rest value. TestSaveButtonFacesStayReadableAndPaired pins exactly this.

        /// <summary>
        /// Save at rest — the designer's deep blue, the same RGB the autocomplete highlight wears (that row
        /// runs it as <see cref="AccentTabBg"/>, i.e. this color at 40% alpha; Save needs the opaque twin,
        /// which is <see cref="AccentFill"/>). Keeping rest COOL is what reserves the entire warm range to mean
        /// "unsaved", so nothing competes with <see cref="SaveDirtyBg"/>.
        /// </summary>
        public static Color SaveBg => AccentFill;
        /// <summary>
        /// Save while the layout is dirty — dark gold. Same family as the Bindings tab's <c>[type]</c> tags, but
        /// re-weighted for a FILL: TypeAccent is a TEXT color (thin bright glyphs on dark chrome, where bright is
        /// correct), and flooding that same value across a button puts it back at ~0.57 luminance — the glare
        /// this whole exercise was removing, with white text at 1.7:1. This is that gold at fill weight.
        /// </summary>
        public static Color SaveDirtyBg => IsLight ? new Color(0.42f, 0.27f, 0.01f, 1f) : new Color(0.50f, 0.33f, 0.02f, 1f);
        /// <summary>Brief blue "landed" pulse on a dropped canvas card (alpha is animated by the caller).</summary>
        public static Color DropPulse => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.18f) : new Color(0.184f, 0.478f, 0.831f, 0.18f);
        /// <summary>Brief canvas flash locating the text a "T" style panel edits.</summary>
        public static Color FlashWash => IsLight ? new Color(0.13f, 0.38f, 0.72f, 0.45f) : new Color(0.30f, 0.55f, 0.90f, 0.45f);
        /// <summary>Link-blue text (the accented "T" when a text style has overrides).</summary>
        public static Color LinkText => IsLight ? new Color(0.14f, 0.40f, 0.75f, 1f) : new Color(0.5f, 0.75f, 1f, 1f);
        /// <summary>Blue-tinted border around the inline text-style panel.</summary>
        public static Color TextPanelEdge => IsLight ? new Color(0.28f, 0.38f, 0.55f, 0.7f) : new Color(0.32f, 0.40f, 0.52f, 0.7f);

        // Danger / status.
        /// <summary>Strong destructive button fill (Revert to Default, preset reset). White text in both skins.</summary>
        public static Color DangerStrong => IsLight ? new Color(0.72f, 0.20f, 0.20f, 1f) : new Color(0.62f, 0.18f, 0.18f, 1f);
        /// <summary>Red delete-condition ✕ icon tint.</summary>
        public static Color DangerIcon => IsLight ? new Color(0.72f, 0.13f, 0.13f, 1f) : new Color(0.9f, 0.3f, 0.3f, 1f);
        /// <summary>Preview build error text.</summary>
        public static Color ErrorText => IsLight ? new Color(0.72f, 0.05f, 0.05f, 1f) : Color.red;
        /// <summary>Green "already bound" check icon.</summary>
        public static Color SuccessIcon => IsLight ? new Color(0.05f, 0.50f, 0.10f, 1f) : Color.green;
        /// <summary>Muted green "duplicate allowed / placed" check icon.</summary>
        public static Color CheckMuted => IsLight ? new Color(0.20f, 0.45f, 0.20f, 1f) : new Color(0.5f, 0.7f, 0.5f, 1f);
        /// <summary>Soft blue "used as group toggle" check icon.</summary>
        public static Color DerivedCheck => IsLight ? new Color(0.15f, 0.40f, 0.70f, 1f) : new Color(0.45f, 0.7f, 0.95f, 1f);
        /// <summary>Amber type-name tag next to binding tokens.</summary>
        public static Color TypeAccent => IsLight ? new Color(0.55f, 0.36f, 0.02f, 1f) : new Color(0.95f, 0.75f, 0.3f, 1f);
        /// <summary>Amber button fill (e.g. Leave a Review CTA in Tutorial).</summary>
        public static Color ButtonAmber => IsLight ? new Color(0.75f, 0.48f, 0.08f, 1f) : new Color(0.85f, 0.55f, 0.12f, 1f);

        // Borders / dividers.
        /// <summary>Standard header-strip divider line.</summary>
        public static Color DividerLine => IsLight ? new Color(0.58f, 0.58f, 0.58f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
        /// <summary>Darker separation line above the Animations header.</summary>
        public static Color DividerShadow => IsLight ? new Color(0.52f, 0.52f, 0.52f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f);
        /// <summary>Toolbar bottom border.</summary>
        public static Color ToolbarBorder => IsLight ? new Color(0.56f, 0.56f, 0.56f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
        /// <summary>Thin vertical separator between the left tabs.</summary>
        public static Color TabDivider => IsLight ? new Color(0.60f, 0.60f, 0.60f, 1f) : new Color(0.28f, 0.28f, 0.28f, 1f);
        /// <summary>Mid-strength border (token rows, toast edges).</summary>
        public static Color BorderMid => IsLight ? new Color(0.60f, 0.60f, 0.60f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
        /// <summary>Column-visibility box border in the toolbar.</summary>
        public static Color BoxBorder => IsLight ? new Color(0.54f, 0.54f, 0.54f, 1f) : new Color(0.32f, 0.32f, 0.32f, 1f);
        /// <summary>Hairline between hierarchy rows — deliberately QUIETER than TreeGuide in both skins.</summary>
        public static Color RowHairline => IsLight ? new Color(0.55f, 0.55f, 0.55f, 0.35f) : new Color(0.18f, 0.18f, 0.18f, 0.5f);
        /// <summary>Hierarchy indent/branch guide lines — the conspicuous structural cue (clearly stronger
        /// than RowHairline: white-ink 14% reads crisply on dark, so light gets a solid black-ink 45%).</summary>
        public static Color TreeGuide => IsLight ? new Color(0f, 0f, 0f, 0.45f) : new Color(1f, 1f, 1f, 0.14f);
        /// <summary>Faint separator under condition rows.</summary>
        public static Color SeparatorFaint => IsLight ? new Color(0.45f, 0.45f, 0.45f, 0.4f) : new Color(0.25f, 0.25f, 0.25f, 0.4f);
        /// <summary>Collapsed tool-section chevron at rest.</summary>
        public static Color SectionArrow => IsLight ? new Color(0.45f, 0.45f, 0.45f, 1f) : new Color(0.365f, 0.365f, 0.365f, 1f);

        // Washes / wells (translucent).
        /// <summary>Unselected hierarchy row hover wash (lighten on dark ⇄ ink on light).</summary>
        public static Color RowHoverWash => IsLight ? new Color(0f, 0f, 0f, 0.08f) : new Color(0.25f, 0.25f, 0.25f, 0.3f);
        /// <summary>Tab Organizer column well behind the card stack.</summary>
        public static Color KanbanColumnBg => IsLight ? new Color(0.55f, 0.55f, 0.55f, 0.35f) : new Color(0.20f, 0.20f, 0.20f, 0.5f);
        /// <summary>"+ Add Tab" placeholder well at rest.</summary>
        public static Color WellBg => IsLight ? new Color(0.55f, 0.55f, 0.55f, 0.20f) : new Color(0.20f, 0.20f, 0.20f, 0.25f);
        /// <summary>"+ Add Tab" placeholder well on hover.</summary>
        public static Color WellHoverBg => IsLight ? new Color(0.45f, 0.45f, 0.45f, 0.30f) : new Color(0.25f, 0.25f, 0.25f, 0.35f);
        /// <summary>Dashed placeholder border ("drop items here").</summary>
        public static Color DashEdge => IsLight ? new Color(0.42f, 0.42f, 0.42f, 0.6f) : new Color(0.4f, 0.4f, 0.4f, 0.6f);
        /// <summary>Fainter placeholder border ("+ Add Tab" at rest).</summary>
        public static Color DashEdgeFaint => IsLight ? new Color(0.42f, 0.42f, 0.42f, 0.4f) : new Color(0.4f, 0.4f, 0.4f, 0.4f);
        /// <summary>Placeholder border on hover.</summary>
        public static Color DashEdgeHover => IsLight ? new Color(0.30f, 0.30f, 0.30f, 0.8f) : new Color(0.6f, 0.6f, 0.6f, 0.8f);

        // Toast accents (border + icon; darkened on light for contrast against the pale toast).
        public static Color ToastSuccess => IsLight ? new Color(0.13f, 0.48f, 0.20f, 1f) : new Color(0.40f, 0.75f, 0.45f, 1f);
        public static Color ToastWarning => IsLight ? new Color(0.66f, 0.40f, 0.04f, 1f) : new Color(0.92f, 0.62f, 0.25f, 1f);
        public static Color ToastInfo => IsLight ? new Color(0.15f, 0.38f, 0.68f, 1f) : new Color(0.40f, 0.62f, 0.90f, 1f);

        // Tools-tab hover-hint borders / icon colors (pale hues darkened for light surfaces).
        /// <summary>Gold "recommended" hover border (Smart Attribute Import).</summary>
        public static Color ToolAmber => IsLight ? new Color(0.62f, 0.42f, 0.04f, 0.8f) : new Color(0.95f, 0.75f, 0.3f, 0.8f);
        /// <summary>Blue hover border (Apply Presets).</summary>
        public static Color ToolBlue => IsLight ? new Color(0.15f, 0.40f, 0.70f, 0.8f) : new Color(0.4f, 0.7f, 0.95f, 0.8f);
        /// <summary>Orange destructive-ish hover border (Override with Presets).</summary>
        public static Color ToolOrange => IsLight ? new Color(0.78f, 0.40f, 0.02f, 0.85f) : new Color(1f, 0.55f, 0.1f, 0.85f);
        /// <summary>Orange icon (Override with Presets).</summary>
        public static Color ToolOrangeIcon => IsLight ? new Color(0.78f, 0.40f, 0.02f, 1f) : new Color(1f, 0.55f, 0.1f, 1f);
        /// <summary>"This removes things" hover border (Remove Orphaned Bindings).</summary>
        public static Color ToolCareful => IsLight ? new Color(0.70f, 0.42f, 0.12f, 0.85f) : new Color(0.85f, 0.55f, 0.25f, 0.85f);
        /// <summary>Red destructive hover border (Clear Layout).</summary>
        public static Color ToolDanger => IsLight ? new Color(0.72f, 0.15f, 0.15f, 0.8f) : new Color(0.85f, 0.25f, 0.25f, 0.8f);
        /// <summary>Red destructive icon (Clear Layout).</summary>
        public static Color ToolDangerIcon => IsLight ? new Color(0.72f, 0.15f, 0.15f, 1f) : new Color(0.85f, 0.25f, 0.25f, 1f);

        // Competing-editor warning banner (pale yellow + dark text on light; dark amber + white on dark).
        public static Color WarningBg => IsLight ? new Color(0.93f, 0.86f, 0.60f, 1f) : new Color(0.32f, 0.26f, 0.12f, 1f);
        public static Color WarningBorder => IsLight ? new Color(0.70f, 0.60f, 0.30f, 1f) : new Color(0.22f, 0.18f, 0.08f, 1f);
        public static Color WarningText => IsLight ? new Color(0.13f, 0.10f, 0.02f, 1f) : Color.white;

        // Branded windows (Welcome / Tutorial / Showcase / Spring Sandbox) — same rule: dark verbatim,
        // light values on the standard ink ladder. Saturated fills (gold buttons, category chips,
        // accent frames) keep white glyphs in both skins.
        /// <summary>Brand gold (Welcome/Tutorial primary action) — darkened on light so it keeps enough depth for white text.</summary>
        public static Color BrandGold => IsLight ? new Color(0.72f, 0.52f, 0.05f, 1f) : new Color(1f, 0.78f, 0.25f, 1f);
        /// <summary>Body copy in the branded windows (notes, demo descriptions, error-card body).</summary>
        public static Color TextBody => IsLight ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.82f, 0.82f, 0.82f);
        /// <summary>Code-snippet block background. DELIBERATELY skin-invariant: the block reads as a
        /// dark code editor "screenshot" in both skins.</summary>
        public static Color CodeBlockBg => new Color(0.12f, 0.12f, 0.13f);
        /// <summary>Code-snippet text — skin-invariant, pairs with the always-dark CodeBlockBg.</summary>
        public static Color CodeText => new Color(0.83f, 0.83f, 0.83f);
        /// <summary>Spring Sandbox section labels (Feel/Duration/etc.).</summary>
        public static Color SandboxLabel => IsLight ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.84f, 0.84f, 0.84f);
        /// <summary>Spring Sandbox dim info line.</summary>
        public static Color InfoDim => IsLight ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.72f, 0.72f, 0.72f);
        /// <summary>Spring Sandbox active option glow (pale blue on dark → deep blue on light).</summary>
        public static Color SandboxGlow => IsLight ? new Color(0.16f, 0.42f, 0.75f) : new Color(0.55f, 0.78f, 1f);
        /// <summary>Showcase error-card background (dark red-tinted panel ⇄ pale red panel).</summary>
        public static Color ErrorCardBg => IsLight ? new Color(0.97f, 0.90f, 0.90f, 1f) : new Color(0.16f, 0.13f, 0.13f, 1f);
        /// <summary>Showcase error-card title text.</summary>
        public static Color ErrorCardTitle => IsLight ? new Color(0.62f, 0.13f, 0.13f, 1f) : new Color(0.96f, 0.7f, 0.7f, 1f);
        /// <summary>Showcase sidebar selected-demo fill (Unity's adaptive button text sits on it).</summary>
        public static Color SidebarSelected => IsLight ? new Color(0.55f, 0.67f, 0.84f) : new Color(0.24f, 0.38f, 0.58f);
        /// <summary>Welcome window big title.</summary>
        public static Color WelcomeTitle => IsLight ? new Color(0.08f, 0.08f, 0.08f) : new Color(0.92f, 0.92f, 0.92f);
        /// <summary>Welcome button subtitle on a primary (accent) button — pale-on-accent in dark, deep slate on light.</summary>
        public static Color WelcomeSubPrimary => IsLight ? new Color(0.86f, 0.90f, 0.96f) : new Color(0.85f, 0.88f, 0.93f);
        /// <summary>Welcome button subtitle on a neutral button.</summary>
        public static Color WelcomeSubSecondary => IsLight ? new Color(0.32f, 0.32f, 0.34f) : new Color(0.68f, 0.68f, 0.7f);
        /// <summary>Welcome secondary-button icon tint.</summary>
        public static Color WelcomeIconDim => IsLight ? new Color(0.28f, 0.28f, 0.30f) : new Color(0.7f, 0.7f, 0.72f);
        /// <summary>Welcome list chevron.</summary>
        public static Color WelcomeChevron => IsLight ? new Color(0.35f, 0.35f, 0.37f) : new Color(0.6f, 0.6f, 0.62f);
        /// <summary>Welcome footer link blue.</summary>
        public static Color WelcomeLink => IsLight ? new Color(0.15f, 0.40f, 0.72f) : new Color(0.55f, 0.72f, 0.95f);
        /// <summary>Welcome footer separator dot.</summary>
        public static Color WelcomeDot => IsLight ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.5f, 0.5f, 0.5f);
        /// <summary>Tutorial live-preview frame (the preview inside follows the theme and paints its own bg).</summary>
        public static Color TutorialFrameBg => IsLight ? new Color(0.86f, 0.86f, 0.87f, 1f) : new Color(0.16f, 0.16f, 0.17f, 1f);

        // Designer-owned popups (quick edit, autocomplete).
        /// <summary>Floating popup background (near-opaque).</summary>
        public static Color PopupBg => IsLight ? new Color(0.95f, 0.95f, 0.95f, 0.97f) : new Color(0.18f, 0.18f, 0.18f, 0.95f);
        /// <summary>Quick-edit window background (opaque).</summary>
        public static Color PopupBgOpaque => IsLight ? new Color(0.94f, 0.94f, 0.94f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
        /// <summary>Popup frame border.</summary>
        public static Color PopupBorder => IsLight ? new Color(0.55f, 0.55f, 0.55f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
        /// <summary>Popup title text.</summary>
        public static Color PopupTitleText => IsLight ? new Color(0.25f, 0.25f, 0.25f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f);
        /// <summary>Pale link/icon blue inside popups (darkened on light).</summary>
        public static Color PopupLink => IsLight ? new Color(0.16f, 0.42f, 0.72f, 1f) : new Color(0.55f, 0.7f, 0.95f, 1f);

        // Column-visibility toggle bevel (raised when the column shows, pushed-in when hidden).
        public static Color BevelFaceRaised => IsLight ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(0.30f, 0.30f, 0.30f, 1f);
        public static Color BevelFaceSunken => IsLight ? new Color(0.66f, 0.66f, 0.66f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f);
        public static Color BevelHighlight => IsLight ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.45f, 0.45f, 0.45f, 1f);
        public static Color BevelHighlightSoft => IsLight ? new Color(0.90f, 0.90f, 0.90f, 1f) : new Color(0.30f, 0.30f, 0.30f, 1f);
        public static Color BevelShadow => IsLight ? new Color(0.55f, 0.55f, 0.55f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
        public static Color BevelShadowDeep => IsLight ? new Color(0.48f, 0.48f, 0.48f, 1f) : new Color(0.08f, 0.08f, 0.08f, 1f);

        public const float DragThreshold = 5f;
    }
}
