using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A layout node bound to a single serialized property. Carries the chosen <see cref="DisplayType"/> plus all
    /// per-widget options — range, text resize, asset/audio preview, color palette, UnityEvent, dropdown source,
    /// validation and text styling. The most configurable node kind.
    /// </summary>
    [Serializable]
    public class BindingNode : LayoutNode, ISerializationCallbackReceiver
    {
        [SerializeField] private string propertyPath;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("widget"), UnityEngine.Serialization.FormerlySerializedAs("widgetType")] private DisplayType displayType = DisplayType.Default;
        [SerializeField] private bool hideLabel;
        [SerializeField] private bool useStandardAlignment = true;
        [SerializeField] private string labelOverride;
        // When true, the field's label sits on its own row above the field instead of the left column (opt-in).
        [SerializeField] private bool labelAbove;
        [SerializeField] private string header;
        [SerializeField] private bool isReadOnly;
        [SerializeField] private bool inlinePreview;
        // Legacy plain-float limits — folded into the overridable rangeMin/rangeMax ONCE on load (see the
        // rangeLimitsMigrated one-shot below). Kept only so layouts written before the overridable pair existed
        // still carry their bounds; new code reads/writes RangeMin/RangeMax (or the MinMax*Limit convenience pair).
        [SerializeField] private float minMaxLowLimit = 0f;
        [SerializeField] private float minMaxHighLimit = 100f;
        [SerializeField] private OverridableFloat rangeMin = new OverridableFloat { isOverridden = false, value = 0f };
        [SerializeField] private OverridableFloat rangeMax = new OverridableFloat { isOverridden = false, value = 100f };
        [SerializeField] private bool rangeLimitsMigrated;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("widgetStyle")] private DisplayStyleData displayStyleData;
        [SerializeField] private string valueSourceMethod;
        [SerializeField] private TextResizeMode textResize = TextResizeMode.Resizable;
        [SerializeField] private float textBoxHeight = 0f;
        [SerializeField] private TextResizeHandleSize textResizeHandleSize = TextResizeHandleSize.Compact;
        // Text widget: greyed hint shown over an empty field. There is no public TextField placeholder API on the
        // supported editor floor, so the widget overlays a muted Label — it stays visible while the field is
        // focused and follows the widget's own multiline/single-line geometry.
        // Empty ⇒ no placeholder. Appended → old layouts clean.
        [SerializeField] private string textPlaceholder = "";
        // Text widget Path/Browse mode: Plain (a normal string field) or Path (adds a browse button that opens a
        // file/folder panel). pathIsFolder picks the panel; pathFilter is a file-extension filter ("png,jpg").
        // Appended → old layouts clean (default Plain = unchanged).
        [SerializeField] private TextPathMode textPathMode = TextPathMode.Plain;
        [SerializeField] private bool pathIsFolder;
        [SerializeField] private string pathFilter = "";
        // Stepper option: for a numeric Default field, wrap the value in −/+ buttons that step by stepSize at
        // native precision (via BindingFactory.StepNumeric). Off by default. Appended → old layouts clean.
        [SerializeField] private bool showStepper;
        [SerializeField] private float stepSize = 1f;
        // Where the −/+ buttons sit relative to the field. Defaults to GroupedRight (both buttons together, right
        // of the field) — the most useful layout for most fields. Appended field, so layouts saved before it get
        // this initializer on load too (they had no stepper-placement concept).
        [SerializeField] private StepperPlacement stepperPlacement = StepperPlacement.GroupedRight;
        // −/+ button styling: full Button-widget chrome (mirrors the Vector Sliders' Normalize button) SHARED by
        // both buttons, plus an enum-badge-style per-button accent override so + and − can differ (e.g. green + /
        // red −). Unset overridables track the shared accent / theme. Classic default keeps existing steppers'
        // stock look until a preset is chosen. All appended → old layouts deserialize clean.
        [SerializeField] private ButtonStyle stepperButtonStyle = ButtonStyle.Classic;
        [SerializeField] private OverridableColor stepperButtonAccent;
        [SerializeField] private OverridableFloat stepperButtonCornerRadius;
        [SerializeField] private ButtonSize stepperButtonSize = ButtonSize.Small;
        [SerializeField] private ButtonHoverAnimation stepperButtonHoverAnimation = ButtonHoverAnimation.Lift;
        [SerializeField] private OverridableColor stepperButtonHoverColor;
        [SerializeField] private OverridableColor stepperButtonPressedColor;
        [SerializeField] private OverridableColor stepperPlusColor;   // per-button accent (+); unset → shared accent
        [SerializeField] private OverridableColor stepperMinusColor;  // per-button accent (−); unset → shared accent
        [SerializeField] private GripAlignment gripAlignment = GripAlignment.Center;
        [SerializeField] private bool required;
        [SerializeField] private string requiredMessage;
        [SerializeField] private bool validateRange;
        [SerializeField] private float validationMin;
        [SerializeField] private float validationMax;
        // Which side(s) of the range are active. Default true/true = the classic two-sided window; a one-sided
        // rule (e.g. an imported [Min]) turns one off so only the other bound is checked/clamped/shown. Default
        // true so layouts serialized before these fields existed keep behaving as a full min↔max window.
        [SerializeField] private bool rangeHasMin = true;
        [SerializeField] private bool rangeHasMax = true;
        [SerializeField] private bool clampToRange;
        [SerializeField] private string rangeValidationMessage;
        [SerializeField] private string validationMethod;
        [SerializeField] private string validationMessage;
        [SerializeField] private float previewSize = 64f;
        [SerializeField] private bool previewBelowField; // legacy — folded into previewPlacement on load
        // AssetPreview Standard: 4-way placement of the thumbnail relative to the object field (supersedes the
        // previewBelowField bool). Right = value 0 = the historical "beside" default.
        [SerializeField] private ElementPlacement previewPlacement = ElementPlacement.Right;
        [SerializeField] private bool previewPlacementMigrated;
        [SerializeField] private AssetPreviewMode assetPreviewMode = AssetPreviewMode.Standard;
        [SerializeField] private bool progressBarInteractive = true;
        [SerializeField] private ProgressBarTextMode progressBarTextMode = ProgressBarTextMode.ValueAndMax;
        [SerializeField] private Color progressBarColor = Color.clear;
        [SerializeField] private Color progressBarBgColor = Color.clear;
        // ProgressBar: ramp the fill red→yellow→green by value (health-bar look); track/fill height in px (0 = default).
        [SerializeField] private bool progressBarValueFill;
        [SerializeField] private float progressBarThickness;
        // ProgressBar value-fill ramp — the user-editable gradient sampled by value when progressBarValueFill is on.
        // Seeded lazily (see ProgressBarFillGradient) so an untouched bar reads as the default health ramp without
        // writing to the asset on mere viewing.
        [SerializeField] private Gradient progressBarFillGradient;
        [SerializeField] private bool progressBarFillGradientSeeded;
        // ProgressBar: draw a soft dark pill behind the readout text (guaranteed contrast on any bar colors).
        [SerializeField] private bool progressBarTextBackdrop;
        [SerializeField] private ImageAlignment previewAlignment = ImageAlignment.Left;
        // AssetPreview: orbit (drag) / pan (middle-drag) / zoom (scroll) a renderable (mesh/prefab/sprite/texture) in
        // a live viewport. Default ON so a freshly-added asset preview is interactive without an extra toggle.
        [SerializeField] private bool assetPreviewInteractive = true;
        // Expandable curve / gradient widgets: open the fold by default (author-time initial state).
        [UnityEngine.Serialization.FormerlySerializedAs("widgetStartExpanded")] [SerializeField] private bool widgetDefaultToOpen;
        // --- AudioPreview widget settings (all appended; default to the Standard mode behavior) ---
        [SerializeField] private AudioPreviewMode audioPreviewMode = AudioPreviewMode.Standard;
        [SerializeField] private ImageAlignment audioPlayAlignment = ImageAlignment.Left;
        // Controls whether a loop TOGGLE BUTTON is shown next to the play button (the button, not this flag,
        // turns looping on/off at preview time). FormerlySerializedAs keeps old layouts' value on rename.
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("audioLoop")] private bool audioShowLoop = true;
        [SerializeField] private bool audioInteractive = true;
        [SerializeField] private float audioWaveformHeight = 50f;
        // When true (Show Soundwaves only), the waveform moves to its own full-width row BELOW the field+buttons
        // (instead of inline to the side), giving the scrubber far more horizontal room.
        [SerializeField] private bool audioWaveformBelow; // legacy — folded into audioWaveformPlacement on load
        // AudioPreview ShowSoundwaves: 4-way placement of the waveform relative to the field+buttons (supersedes
        // the audioWaveformBelow bool). Right = value 0 = the historical inline "beside" default.
        [SerializeField] private ElementPlacement audioWaveformPlacement = ElementPlacement.Right;
        [SerializeField] private bool audioWaveformPlacementMigrated;
        // --- ParticleSystemPreview widget settings (appended; read only by the particle preview widget) ---
        // Poster (idle) frame position as a fraction [0,1] of the effect's "play once" duration.
        [SerializeField] private float particlePosterTime = 0.35f;
        // Solid clear color behind the rendered effect (drives both the panel and the preview camera).
        [SerializeField] private Color particleBackgroundColor = new Color(0.13f, 0.13f, 0.15f, 1f);
        // Layout of the field + live thumbnail. FieldAndThumbnail (value 0) preserves the original look.
        [SerializeField] private ParticlePreviewMode particlePreviewMode = ParticlePreviewMode.FieldAndThumbnail;
        // Thumbnail edge length in pixels (16-256; default 128). Larger so VFX/particle previews can be shown bigger.
        [SerializeField] private float particleThumbnailSize = 128f;
        // VFX-only camera zoom (0.25-6; 1 = the default baseline). A VFX Graph's culling bounds overshoot the visible
        // particles, so this magnifies past them to fill the thumbnail; ignored for ParticleSystems (already tight).
        [SerializeField] private float particlePreviewZoom = 1f;
        // Where the play/stop badge sits over the thumbnail (Center keeps the original centered badge).
        [SerializeField] private PreviewAnchor particlePlayButtonAnchor = PreviewAnchor.Center;
        // Manual pixel nudge applied on top of the anchor.
        [SerializeField] private Vector2 particlePlayButtonOffset = Vector2.zero;
        // Play/stop badge width in pixels (height scales proportionally from the 44x32 base).
        [SerializeField] private float particlePlayButtonWidth = 32f;
        // Expandable mode only: start playback automatically when the preview is opened (stops on close).
        [SerializeField] private bool particlePlayOnOpen = true;
        // Expandable mode only: open the preview fold by default (until the user collapses it).
        [UnityEngine.Serialization.FormerlySerializedAs("particleStartExpanded")] [SerializeField] private bool particleDefaultToOpen;
        // Orbit (drag) / pan (middle-drag) / zoom (scroll) the effect in the preview, plus the Reset-view + 2D
        // overlays. Default ON so the preview is interactive out of the box; turn off for a fixed, click-to-play view.
        [SerializeField] private bool particlePreviewInteractive = true;
        [SerializeField] private bool searchableDropdown;
        // Polymorphic (SerializeReference) type-picker search box. Separate from searchableDropdown (which is
        // shared with the dropdown widgets and defaults off); polymorphic defaults ON.
        [SerializeField] private bool polymorphicSearchable = true;
        // INVERTED (bar-style) so the peek is armed by default — old layouts deserialize false = shown.
        [SerializeField] private bool polymorphicHideCodePeek;
        [SerializeField] private DropdownSource dropdownSource = DropdownSource.Auto;
        // --- InlineEditor widget options (read only by the In-Line Editor widget; ignored by other kinds) ---
        // When true, the embedded editor is wrapped in a collapsible chevron group; DefaultToOpen sets its
        // initial open/closed state. Defaults give a collapsible-but-open editor out of the box.
        [UnityEngine.Serialization.FormerlySerializedAs("minimizable")] [SerializeField] private bool expandable = true;
        [UnityEngine.Serialization.FormerlySerializedAs("startExpanded")] [SerializeField] private bool defaultToOpen = true;
        // --- ColorPalette widget options (read only by the Color Palette widget; ignored by other kinds) ---
        // Which palette (by stable id) inside the single SasquatchColorPaletteLibrary supplies this field's
        // swatches; when blank/unknown, the widget falls back to the library's default palette. Layout/columns/
        // size shape the swatch surface; copyOnClick also writes the picked color's hex to the clipboard. All
        // appended → existing layouts deserialize clean (the retired object reference is simply dropped).
        [SerializeField] private string colorPaletteId = "";
        [SerializeField] private ColorPaletteLayout colorPaletteLayout = ColorPaletteLayout.Bar;
        [SerializeField] private int colorPaletteColumns = 0;   // 0 = auto-wrap square swatches to width
        [SerializeField] private float colorSwatchSize = 22f;
        [SerializeField] private float colorSwatchSpacing = 1f;  // grid: px gap around each swatch (0 = touch, never overlap)
        [SerializeField] private bool colorCopyOnClick = true;
        // INVERTED (slider-style) so the bar shows by default — old layouts deserialize false = shown.
        [SerializeField] private bool colorHideBar;
        [SerializeField] private ColorBarPlacement colorBarPlacement = ColorBarPlacement.Right;
        // --- UnityEvent widget options (read only by the custom UnityEvent drawer) ---
        [SerializeField] private bool unityEventCompact;            // false = roomy card rows (default)
        [UnityEngine.Serialization.FormerlySerializedAs("unityEventStartExpanded")] [SerializeField] private bool unityEventDefaultToOpen = true;
        [SerializeField] private OverridableColor unityEventAccentColor; // header bar tint (isOverridden gates it)
        [SerializeField] private OverridableColor unityEventCardColor;   // per-card background (isOverridden gates it)
        // INVERTED (bar-style) so the peek is armed by default — old layouts deserialize false = shown.
        [SerializeField] private bool unityEventHideCodePeek;
        // --- AnimationCurveEditor widget options (appended; read only by the curve editor widget) ---
        // The AnimationCurve itself is NOT stored here — it lives on the bound SerializedProperty. These are
        // purely the widget's persistent presentation/behaviour options. All appended → old layouts deserialize clean.
        [SerializeField] private CurveEditorMode curveEditorMode = CurveEditorMode.Inline;
        [SerializeField] private Color curveLineColor = new Color(0.45f, 0.78f, 1f, 1f);      // #73C7FF stroke
        [SerializeField] private Color curveFillColor = new Color(0.45f, 0.78f, 1f, 0.14f);   // soft wash under the curve
        [SerializeField] private Color curveBackgroundColor = new Color(0.118f, 0.118f, 0.118f, 1f); // #1e1e1e well
        [SerializeField] private Color curveGridColor = new Color(1f, 1f, 1f, 0.06f);
        [SerializeField] private bool curveShowGrid = true;
        [SerializeField] private float curveWellHeight = 56f;            // inline well height in px (clamp 24-160)
        // Y-axis frame: unset (isOverridden=false) => auto-frame to the curve's value bounds; set => fixed range.
        [SerializeField] private OverridableFloat curveRangeMin = new OverridableFloat { isOverridden = false, value = 0f };
        [SerializeField] private OverridableFloat curveRangeMax = new OverridableFloat { isOverridden = false, value = 1f };
        [SerializeField] private bool curveSnapToGrid;                   // snap dragged (time,value) to the grid
        [SerializeField] private float curveGridSize = 0.25f;            // grid line spacing in curve units (time+value); drives Snap To Grid
        [SerializeField] private bool curveReadOnlyInline;              // inline well view-only (still editable in the window)
        [SerializeField] private bool curveResizable = true;            // drag a handle at the bottom of the well to resize it
        [SerializeField] private bool curveEditableMaxY = true;         // show an editable Max-Y field over the top of the graph
        [SerializeField] private bool curveEditableMaxX;                // show an editable Max-X field at the right of the graph
        // Well border (mirrors the gradient widget's border, kept visually identical to it). Color is
        // Overridable so the default (unset) resolves to the theme border (WidgetPalette.Border) — matching
        // the .sasq-curve-well USS in BOTH skins — instead of a hardcoded gray. Width defaults to 1 (= the USS
        // border), so existing curves render byte-identical; 0 removes the border.
        [SerializeField] private OverridableColor curveBorderColor;    // unset => theme border; set => custom
        [SerializeField] private float curveBorderWidth = 1f;          // well border width px (clamp 0-6)
        // --- Gradient widget options (appended; read only by the gradient editor widget) ---
        // The Gradient itself is NOT stored here — it lives on the bound SerializedProperty (read/written via the
        // public SerializedProperty.boxedValue API). These are purely the widget's persistent presentation/
        // behaviour options. All appended → old layouts deserialize clean.
        [SerializeField] private GradientEditorMode gradientEditorMode = GradientEditorMode.Inline;
        [SerializeField] private GradientPinLayout gradientPinLayout = GradientPinLayout.DualTrack;
        [SerializeField] private float gradientWellHeight = 28f;            // inline strip height in px (clamp 18-80)
        [SerializeField] private bool gradientResizable = true;            // drag a handle at the bottom of the strip to resize it
        [SerializeField] private bool gradientReadOnlyInline;             // inline strip view-only (still editable in the window)
        [SerializeField] private bool gradientShowCheckerboard = true;     // checker backing so alpha/transparency reads
        [SerializeField] private Color gradientBackgroundColor = new Color(0.118f, 0.118f, 0.118f, 1f); // #1e1e1e well (dead; strip covers it)
        [SerializeField] private Color gradientBorderColor = new Color(0.247f, 0.247f, 0.247f, 1f);     // #3f3f3f border
        [SerializeField] private float gradientBorderWidth = 1f;                                        // strip well border width px (clamp 0-6)
        [SerializeField] private Color gradientCheckerColorA = new Color(0.28f, 0.28f, 0.28f, 1f);      // light checker square
        [SerializeField] private Color gradientCheckerColorB = new Color(0.16f, 0.16f, 0.16f, 1f);      // dark checker square
        // Palette integration (mirror colorPaletteId): which palette feeds the swatch row + pin popover.
        [SerializeField] private string gradientColorPaletteId = "";       // blank/unknown => library default palette
        [SerializeField] private bool gradientShowPaletteRow = true;       // swatch row under the editor
        [SerializeField] private bool gradientShowPaletteDot = true;       // "matches a palette color" accent ring around a pin
        [SerializeField] private bool gradientHdr;                         // HDR gradient — the pin color picker gains an intensity control (values > 1)
        // --- Plain Color field (Default display type) options ---
        // A Color field renders through Unity's ColorField (via PropertyField). These two let it opt into an HDR
        // picker and/or a hidden alpha channel WITHOUT a [ColorUsage] attribute — and a [ColorUsage(showAlpha, hdr)]
        // attribute imports straight onto them. Both default to the stock look (Default mode, alpha shown), so a
        // Color field that touches neither renders byte-identical to before (still PropertyField, honoring any real
        // attribute). Appended → old layouts deserialize clean.
        [SerializeField] private ColorFieldMode colorFieldMode = ColorFieldMode.Default;
        [SerializeField] private bool colorShowAlpha = true;
        // --- Enum Badge Grid widget options (appended; read only by the enum badge widget) ---
        // The enum value itself is NOT stored here — it lives on the bound SerializedProperty (read/written via
        // enumValueFlag). These are purely the grid's presentation/behaviour options. All appended → old layouts
        // deserialize clean. Multi-vs-single is auto-resolved from [Flags] at build time (no mode field). The
        // active-badge tint reuses DisplayStyleData.accent (no dedicated color field).
        [SerializeField] private int enumBadgeColumns = 0;                 // 0 = auto-wrap; >0 = fixed grid columns
        [SerializeField] private bool enumBadgeShowAllNone = true;         // None / All quick chips ([Flags] enums only)
        [SerializeField] private bool enumBadgeIncludeObsolete = false;    // also show [Obsolete] enum members
        [SerializeField] private float enumBadgeHeight = 20f;             // per-badge height in px (clamp 14-60)
        [SerializeField] private float enumBadgeWidth = 0f;               // 0 = size to the label; >0 = fixed per-badge width (auto-wrap only)
        [SerializeField] private float enumBadgeSpacing = 0f;             // 0 = stylesheet default; >0 = explicit inter-badge gap (px)
        [SerializeField] private bool enumBadgeTextBackdrop = false;       // label (+ icon) on a dark pill inside the badge
        // Per-value styling: optional custom tint + leading icon for individual enum members (keyed by underlying
        // value). Empty array => every badge uses the shared accent with no icon (original look). Appended → clean.
        [SerializeField] private EnumBadgeOverride[] enumBadgeOverrides = System.Array.Empty<EnumBadgeOverride>();
        // --- Rich Text preview options (appended; read by the Text widget when richTextPreview is on) ---
        // The string itself is NOT stored here — it lives on the bound SerializedProperty. richTextPreview is the
        // master toggle that turns a plain Text field into a split-pane editor (a live formatted preview + an
        // optional formatting toolbar that wraps the highlighted text in rich-text tags). The Color toolbar
        // reuses colorPaletteId; the raw input's resize reuses TextResize / TextBoxHeight. Appended → old layouts
        // deserialize clean.
        [SerializeField] private bool richTextPreview;                             // master: render the live formatted preview
        [SerializeField] private bool richTextToolbar = true;                      // show the B/I/U/Color/Size/More toolbar
        [SerializeField] private OverridableColor richTextPreviewBackground =      // unset => dark-slate default
            new OverridableColor { isOverridden = false, value = new Color(0.145f, 0.145f, 0.16f, 1f) };
        [SerializeField] private OverridableFloat richTextPreviewCornerRadius =    // unset => default bubble radius (5px)
            new OverridableFloat { isOverridden = false, value = 5f };
        // --- Unit suffix (appended; read by the Slider / MinMaxSlider / ProgressBar widgets) ---
        // Rendered VERBATIM after the numeric value — no separator is injected, so include a leading space
        // when you want one (" m/s") and omit it when the unit reads tight ("%"). Appended → old layouts
        // deserialize clean.
        [SerializeField] private string unitSuffix = "";
        // --- Prefix / Suffix affixes (appended; read by EVERY display type via FieldAffixes) ---
        // The universal successor to unitSuffix: text and/or a Sasquatch icon on either side of the field,
        // mountable INSIDE the input box or outside it, each side independently styled. unitSuffix is kept
        // forever (renaming a serialized field breaks the golden corpus) and folds forward in
        // OnAfterDeserialize; the UnitSuffix property is a shim over suffixAffix.text.
        [SerializeField] private FieldAffix prefixAffix;
        [SerializeField] private FieldAffix suffixAffix;
        // Composite fields (Vector2/3/4, Rect, Bounds, euler) repeat the affix on EVERY component box by
        // default ("5 m, 2 m, 9 m") — off puts a single affix at the end of the whole row instead.
        [SerializeField] private bool affixPerComponent = true;
        // Table columns only: show the affix ONCE in the column header ("Weight (kg)") instead of in every
        // cell. Ignored outside a collection's table item layout.
        [SerializeField] private bool affixInColumnHeader;
        // --- Create-asset button (appended; read by the Default / InlineEditor / AssetPreview widgets) ---
        // When on and the bound field is a null ScriptableObject reference, the widget shows a small
        // "Create" button that saves a new asset of the field's type and assigns it. Appended → old
        // layouts deserialize clean (off by default).
        [SerializeField] private bool createAssetEnabled;
        // --- Axis color pips (appended; Default widget on vector-family fields only) ---
        // Small axis-colored pips inside each component field (Unity handle convention: X red,
        // Y green, Z blue; W neutral). Appended → old layouts deserialize clean (off by default).
        [SerializeField] private bool vectorAxisPips;
        // --- Rating widget options (appended; read only by the Rating widget) ---
        // Number of pips shown (1-10) and which glyph to draw. All appended → old layouts deserialize clean.
        [SerializeField] private int ratingCount = 5;
        [SerializeField] private RatingIconStyle ratingStyle = RatingIconStyle.Stars;
        // Pip glyph size in px (default 15) and per-state colors (unset = a sensible default; selected falls back
        // to the accent). ratingAnimate gates the hover/press scale spring (on by default). All appended → clean.
        [SerializeField] private float ratingPipSize = 15f;
        [SerializeField] private OverridableColor ratingSelectedColor;
        [SerializeField] private OverridableColor ratingUnselectedColor;
        [SerializeField] private OverridableColor ratingHoverColor;
        [SerializeField] private bool ratingAnimate = true;
        // --- XY Pad widget options (appended; read only by the XY Pad widget) ---
        // Horizontal placement of the fixed-size pad in its row, and whether to show editable X/Y number fields
        // under the pad (default off = the compact read-only readout). Appended → old layouts deserialize clean.
        [SerializeField] private ImageAlignment xyPadAlignment = ImageAlignment.Left;
        [SerializeField] private bool xyPadEditableFields;
        // Grid + snap (mirrors the curve widget) and per-part colors. The grid draws lines every xyPadGridSize
        // VALUE units on both axes; Snap To Grid lands a dragged handle on those same lines. Colors are
        // OverridableColor so an unset value tracks the theme (bg → well, grid → border, handle → accent) and a
        // Revert stays clean. All appended → old layouts deserialize clean.
        [SerializeField] private bool xyPadShowGrid = true;
        [SerializeField] private bool xyPadSnapToGrid;
        [SerializeField] private bool xyPadHandleCrosshair;                // full-width crosshair lines that follow the handle
        [SerializeField] private float xyPadGridSize = 0.25f;              // grid spacing in value units; drives Snap
        [SerializeField] private OverridableColor xyPadBackgroundColor;    // unset → WidgetPalette.WellBg
        [SerializeField] private OverridableColor xyPadGridColor;          // unset → WidgetPalette.Border
        [SerializeField] private OverridableColor xyPadHandleColor;        // unset → accent
        [SerializeField] private float xyPadCrosshairThickness = 1f;       // handle-crosshair line thickness (px)
        [SerializeField] private OverridableColor xyPadCrosshairColor;     // unset → the grid color
        // --- Vector Sliders widget option (appended; read only by the Vector Sliders widget) ---
        // Show the "Normalize" button that rescales the vector to unit length. OFF by default.
        [SerializeField] private bool vectorShowNormalize = false;
        // Horizontal placement of the Normalize button (default Left = today's alignSelf FlexStart).
        [SerializeField] private ImageAlignment vectorNormalizeAlignment = ImageAlignment.Left;
        // Normalize-button appearance (shown only when Show Normalize is on) — mirrors the Button widget's look,
        // applied via ApplyButtonChrome through a transient ButtonNode. Modern preset + medium size by default.
        [SerializeField] private ButtonStyle normalizeButtonStyle = ButtonStyle.Modern;
        [SerializeField] private OverridableColor normalizeButtonAccent;
        [SerializeField] private OverridableFloat normalizeButtonCornerRadius;
        [SerializeField] private ButtonSize normalizeButtonSize = ButtonSize.Medium;
        [SerializeField] private ButtonHoverAnimation normalizeButtonHoverAnimation = ButtonHoverAnimation.Lift;
        [SerializeField] private OverridableColor normalizeButtonHoverColor;
        [SerializeField] private OverridableColor normalizeButtonPressedColor;
        // --- Dictionary table options (appended; read by BOTH dictionary shapes) ---
        // The container-level look (shell / header / density / how the key presents) and the per-row surface,
        // deliberately orthogonal exactly as CollectionListStyle and CollectionRowSeparator are for a List —
        // so every style works with every frame. Table + None are the values every dictionary rendered before
        // these fields existed, so an old layout deserializes to today's look with no migration.
        //
        // These drive BOTH shapes: a native Unity 6.6 Dictionary<K,V> under DisplayType.Dictionary, and this
        // package's own SerializedDictionary, which auto-routes to the same table (BindingFactory). One pair
        // of fields, one renderer — see DictionaryTableStyle.
        [SerializeField] private DictionaryTableStyle dictionaryStyle = DictionaryTableStyle.Table;
        [SerializeField] private CollectionRowSeparator dictionaryRowSeparator = CollectionRowSeparator.None;
        // Tint for the row frame: the hairline in Line mode, the left edge rail in Card mode. Unset tracks the
        // theme, exactly like CollectionNode.SeparatorColor.
        [SerializeField] private OverridableColor dictionarySeparatorColor;
        // --- Dictionary CELL TEMPLATES (appended; read by BOTH dictionary shapes) ---
        // The dictionary's answer to CollectionNode.ItemLayout: one binding per column that styles the KEY (or
        // the VALUE) of every row. Null means "draw the cell exactly as it was drawn before these existed" —
        // a plain Default field — so an old layout deserializes to today's look with no migration.
        //
        // Both carry an EMPTY PropertyPath, which is the package-wide convention for "the thing the scope
        // already points at" (see LayoutFieldTypeResolver and CollectionNode.ItemLayout's element template).
        // A dictionary's two halves are named differently by the two shapes -- `Key`/`Value` on our own
        // Entry, `key`/`value` on Unity 6.6's native pair -- so a template that STORED a path would be right
        // for exactly one shape. The renderer supplies the real relative name at build time instead, which is
        // what lets one authored template drive both.
        [SerializeReference] private BindingNode dictionaryKeyTemplate;
        [SerializeReference] private BindingNode dictionaryValueTemplate;
        // --- Per-field help chip (appended; any binding) ---
        // Master toggle; when on AND helpText is non-empty a round "?" chip is shown beside the field that reveals
        // a markdown help card below it. helpChipAlignment = Left or Right (Center is not offered); Right default.
        // Appended → old layouts deserialize clean.
        [SerializeField] private bool showHelpChip;
        [SerializeField] private string helpText = "";
        [SerializeField] private ImageAlignment helpChipAlignment = ImageAlignment.Right;
        [SerializeField] private TextStyle helpTextStyle;
        // --- Custom Widget options (appended; read only by the Custom Widget widget) ---
        // Which SasquatchCustomWidget asset (by GUID) renders this field, plus the mapping of its NON-primary
        // inputs to sibling properties. The PRIMARY input is fed by this binding's own PropertyPath (never stored
        // here). A missing/garbage guid or a primary-kind mismatch falls back to a plain PropertyField. Appended →
        // old layouts deserialize clean (empty guid = no custom widget).
#if SASQ_CUSTOM_WIDGET
        [SerializeField] private string customWidgetGuid = "";
        [SerializeField] private System.Collections.Generic.List<CustomWidgetInputMap> customWidgetInputs =
            new System.Collections.Generic.List<CustomWidgetInputMap>();
#endif

        public string PropertyPath
        {
            get => propertyPath;
            set => propertyPath = value;
        }
 
        public DisplayType DisplayType
        {
            get => displayType;
            set => displayType = value;
        }
 
        public DisplayStyleData DisplayStyleData
        {
            get => displayStyleData;
            set => displayStyleData = value;
        }
 
        public bool HideLabel
        {
            get => hideLabel;
            set => hideLabel = value;
        }
 
        public bool UseStandardAlignment
        {
            get => useStandardAlignment;
            set => useStandardAlignment = value;
        }
 
        public string LabelOverride
        {
            get => labelOverride;
            set
            {
                labelOverride = value;
                // Retire the legacy shadow flag on any write through the modern field. This is the ONLY place
                // it can be cleared: the designer replaced BindingNode's explicit "Hide Label" toggle with this
                // three-state Label row (type = override, clear = HiddenSentinel, reset = back to the code
                // name), so a node still carrying hideLabel:true had no UI that could switch its label back on.
                // Writing here is always an explicit statement ABOUT the label — typing text, or resetting to
                // the nicified name — and the legacy flag must not outlive it and silently veto the result.
                // Same treatment this class already gives its other legacy shadows (minMaxLowLimit → rangeMin,
                // unitSuffix → suffixAffix.text): the superseding member owns the fold.
                //
                // Order matters at the two sites that copy BOTH: LayoutPresetApplier.TransplantIdentity and
                // StyleClipboard both assign LabelOverride BEFORE HideLabel, so an intentional transplant of
                // the flag still lands. Keep it that way.
                hideLabel = false;
            }
        }

        /// <summary>When true, the field's label sits on its own row above the field instead of the inspector's
        /// left label column. Ignored when the label is hidden. Opt-in per binding.</summary>
        public bool LabelAbove
        {
            get => labelAbove;
            set => labelAbove = value;
        }

        /// <summary>
        /// True when the field's label should be suppressed entirely — either an explicitly blanked
        /// <see cref="LabelOverride"/> (the designer stores <see cref="LayoutNode.HiddenSentinel"/> when you
        /// clear the Label field) or the legacy <see cref="HideLabel"/> flag from older layouts.
        /// <para>
        /// The legacy flag only speaks when the modern field is SILENT. A plain <c>||</c> here let a
        /// hideLabel:true node swallow a label the user had typed into the designer's Label row — the text was
        /// accepted, stored on the node, and then discarded at build time, with no toggle anywhere in the
        /// BindingNode panel that could explain or undo it (that toggle was replaced by the Label row itself,
        /// and survives only on <see cref="PropertyNode"/>). A non-empty override can only have come from
        /// someone asking for that text, so it wins; a bare legacy hide still hides, so old layouts are
        /// untouched — and deliberately so, since nothing here rewrites serialized data (the golden corpus
        /// freezes <c>hideLabel</c> on four fixtures, and a fold-on-load would read as data loss).
        /// </para>
        /// </summary>
        public bool IsLabelHidden =>
            labelOverride == HiddenSentinel || (hideLabel && string.IsNullOrEmpty(labelOverride));

        /// <summary>
        /// The custom label text, or <c>null</c> when the label is unset (use the field's display name) or
        /// hidden (callers check <see cref="IsLabelHidden"/>). Never returns the hidden sentinel.
        /// </summary>
        public string EffectiveLabelOverride =>
            (string.IsNullOrEmpty(labelOverride) || labelOverride == HiddenSentinel) ? null : labelOverride;
 
        /// <summary>
        /// An optional section header rendered above the field (like a C# <c>[Header]</c>). When empty, the
        /// builder falls back to any <c>[Header]</c> declared on the member in code; a non-empty value overrides it.
        /// </summary>
        public string Header
        {
            get => header;
            set => header = value;
        }

        /// <summary>True when the <see cref="Header"/> is explicitly blanked (hidden) rather than merely unset.</summary>
        public bool IsHeaderHidden => header == HiddenSentinel;

        /// <summary>
        /// The custom header text, or <c>null</c> when unset (fall back to a code <c>[Header]</c>) or hidden
        /// (callers check <see cref="IsHeaderHidden"/>). Never returns the hidden sentinel.
        /// </summary>
        public string EffectiveHeader =>
            (string.IsNullOrEmpty(header) || header == HiddenSentinel) ? null : header;
 
        public bool IsReadOnly
        {
            get => isReadOnly;
            set => isReadOnly = value;
        }
 
        public bool InlinePreview
        {
            get => inlinePreview;
            set => inlinePreview = value;
        }
 
        public OverridableFloat RangeMin
        {
            get => rangeMin;
            set => rangeMin = value;
        }

        public OverridableFloat RangeMax
        {
            get => rangeMax;
            set => rangeMax = value;
        }

        public float MinMaxLowLimit
        {
            get => rangeMin.isOverridden ? rangeMin.value : minMaxLowLimit;
            set
            {
                rangeMin.isOverridden = true;
                rangeMin.value = value;
                minMaxLowLimit = value;
            }
        }
 
        public float MinMaxHighLimit
        {
            get => rangeMax.isOverridden ? rangeMax.value : minMaxHighLimit;
            set
            {
                rangeMax.isOverridden = true;
                rangeMax.value = value;
                minMaxHighLimit = value;
            }
        }
 
        /// <summary>
        /// For the <see cref="DisplayType.DropDown"/> widget's Method source: the name of a parameterless
        /// method on the target (instance or static) that returns an <c>IEnumerable</c> of options for the
        /// dropdown. Ignored by every other widget kind.
        /// </summary>
        public string ValueSourceMethod
        {
            get => valueSourceMethod;
            set => valueSourceMethod = value;
        }
 
        /// <summary>
        /// For <see cref="DisplayType.Text"/>: whether the field shows a drag-to-resize handle. Defaults to
        /// <see cref="TextResizeMode.Resizable"/>.
        /// </summary>
        public TextResizeMode TextResize
        {
            get => textResize;
            set => textResize = value;
        }

        /// <summary>
        /// The dragged height (px) of a resizable <see cref="DisplayType.Text"/> field. <c>0</c> = auto/unset,
        /// so the field uses its natural height until the user drags the resize handle.
        /// </summary>
        public float TextBoxHeight
        {
            get => textBoxHeight;
            set => textBoxHeight = value;
        }

        /// <summary>
        /// The size of a resizable <see cref="DisplayType.Text"/> field's grip drag handle
        /// (<see cref="TextResizeHandleSize.Compact"/> by default).
        /// </summary>
        public TextResizeHandleSize TextResizeHandleSize
        {
            get => textResizeHandleSize;
            set => textResizeHandleSize = value;
        }

        /// <summary>For <see cref="DisplayType.Text"/>: greyed hint text shown over the field while it is empty
        /// (rendered as a muted overlay Label). Empty ⇒ no placeholder.</summary>
        public string TextPlaceholder
        {
            get => textPlaceholder;
            set => textPlaceholder = value;
        }

        /// <summary>For <see cref="DisplayType.Text"/>: Plain string field, or Path (adds a browse button).</summary>
        public TextPathMode TextPathMode
        {
            get => textPathMode;
            set => textPathMode = value;
        }

        /// <summary>For Path mode: browse for a folder (<c>true</c>) instead of a file.</summary>
        public bool PathIsFolder
        {
            get => pathIsFolder;
            set => pathIsFolder = value;
        }

        /// <summary>For Path mode (files only): comma-separated extension filter, e.g. "png,jpg". Empty ⇒ any.</summary>
        public string PathFilter
        {
            get => pathFilter;
            set => pathFilter = value;
        }

        /// <summary>For a numeric <see cref="DisplayType.Default"/> field: wrap the value in −/+ stepper buttons.</summary>
        public bool ShowStepper
        {
            get => showStepper;
            set => showStepper = value;
        }

        /// <summary>Stepper increment applied by the −/+ buttons (native precision; fractional steps on integer
        /// fields round with a ±1 floor). Defaults to 1.</summary>
        public float StepSize
        {
            get => stepSize;
            set => stepSize = value;
        }

        /// <summary>Where the −/+ stepper buttons sit: Split (− left / + right), or both grouped to the right/left.</summary>
        public StepperPlacement StepperPlacement
        {
            get => stepperPlacement;
            set => stepperPlacement = value;
        }

        /// <summary>Stepper −/+ buttons: visual preset (Classic = stock chrome). Shared by both buttons.</summary>
        public ButtonStyle StepperButtonStyle
        {
            get => stepperButtonStyle;
            set => stepperButtonStyle = value;
        }

        /// <summary>Stepper −/+ buttons: shared accent color (unset = the theme accent); per-button overrides win.</summary>
        public OverridableColor StepperButtonAccent
        {
            get => stepperButtonAccent;
            set => stepperButtonAccent = value;
        }

        /// <summary>Stepper −/+ buttons: shared corner radius (unset = 4px).</summary>
        public OverridableFloat StepperButtonCornerRadius
        {
            get => stepperButtonCornerRadius;
            set => stepperButtonCornerRadius = value;
        }

        /// <summary>Stepper −/+ buttons: height class (Small by default).</summary>
        public ButtonSize StepperButtonSize
        {
            get => stepperButtonSize;
            set => stepperButtonSize = value;
        }

        /// <summary>Stepper −/+ buttons: hover/press feedback animation.</summary>
        public ButtonHoverAnimation StepperButtonHoverAnimation
        {
            get => stepperButtonHoverAnimation;
            set => stepperButtonHoverAnimation = value;
        }

        /// <summary>Stepper −/+ buttons: hover color override (unset = the preset's subtle lighten).</summary>
        public OverridableColor StepperButtonHoverColor
        {
            get => stepperButtonHoverColor;
            set => stepperButtonHoverColor = value;
        }

        /// <summary>Stepper −/+ buttons: pressed color override (unset = no explicit press color).</summary>
        public OverridableColor StepperButtonPressedColor
        {
            get => stepperButtonPressedColor;
            set => stepperButtonPressedColor = value;
        }

        /// <summary>Stepper: per-button accent for the + button (unset = the shared accent).</summary>
        public OverridableColor StepperPlusColor
        {
            get => stepperPlusColor;
            set => stepperPlusColor = value;
        }

        /// <summary>Stepper: per-button accent for the − button (unset = the shared accent).</summary>
        public OverridableColor StepperMinusColor
        {
            get => stepperMinusColor;
            set => stepperMinusColor = value;
        }

        /// <summary>
        /// The horizontal alignment of a resizable <see cref="DisplayType.Text"/> field's grip drag handle
        /// (<see cref="GripAlignment.Center"/> by default).
        /// </summary>
        public GripAlignment GripAlignment
        {
            get => gripAlignment;
            set => gripAlignment = value;
        }
 
        public bool Required
        {
            get => required;
            set => required = value;
        }
 
        public string RequiredMessage
        {
            get => requiredMessage;
            set => requiredMessage = value;
        }
 
        public bool ValidateRange
        {
            get => validateRange;
            set => validateRange = value;
        }
 
        public float ValidationMin
        {
            get => validationMin;
            set => validationMin = value;
        }
 
        public float ValidationMax
        {
            get => validationMax;
            set => validationMax = value;
        }

        /// <summary>
        /// When true (and <see cref="ValidateRange"/> is on), the numeric value is coerced into
        /// [<see cref="ValidationMin"/>, <see cref="ValidationMax"/>] on commit instead of only warning — an
        /// enforced MinValue/MaxValue. Clamping happens on blur so it never fights mid-typing.
        /// </summary>
        public bool ClampToRange
        {
            get => clampToRange;
            set => clampToRange = value;
        }

        /// <summary>When true, the range's lower bound (<see cref="ValidationMin"/>) is active — checked,
        /// clamped and shown in the designer. Turn off for a maximum-only rule. Default true.</summary>
        public bool RangeHasMin
        {
            get => rangeHasMin;
            set => rangeHasMin = value;
        }

        /// <summary>When true, the range's upper bound (<see cref="ValidationMax"/>) is active — checked,
        /// clamped and shown in the designer. Turn off for a minimum-only rule (e.g. an imported
        /// <c>[Min]</c>). Default true.</summary>
        public bool RangeHasMax
        {
            get => rangeHasMax;
            set => rangeHasMax = value;
        }

        public string RangeValidationMessage
        {
            get => rangeValidationMessage;
            set => rangeValidationMessage = value;
        }
 
        public string ValidationMethod
        {
            get => validationMethod;
            set => validationMethod = value;
        }
 
        public string ValidationMessage
        {
            get => validationMessage;
            set => validationMessage = value;
        }
 

 
        public float PreviewSize
        {
            get => previewSize;
            set => previewSize = value;
        }
 
        public bool PreviewBelowField
        {
            get => previewBelowField;
            set => previewBelowField = value;
        }

        /// <summary>4-way placement of the AssetPreview thumbnail relative to the object field (Standard mode);
        /// supersedes the legacy <see cref="PreviewBelowField"/> bool, migrated once on load.</summary>
        public ElementPlacement PreviewPlacement
        {
            get => previewPlacement;
            set => previewPlacement = value;
        }
 
        public bool SearchableDropdown
        {
            get => searchableDropdown;
            set => searchableDropdown = value;
        }

        public bool PolymorphicSearchable
        {
            get => polymorphicSearchable;
            set => polymorphicSearchable = value;
        }

        /// <summary>For <see cref="DisplayType.SerializeReference"/>: hovering a type in the picker previews its
        /// declaration. Defaults on (stored inverted for serialization back-compat).</summary>
        public bool PolymorphicCodePeek
        {
            get => !polymorphicHideCodePeek;
            set => polymorphicHideCodePeek = !value;
        }
 
        public AssetPreviewMode AssetPreviewMode
        {
            get => assetPreviewMode;
            set => assetPreviewMode = value;
        }
 
        public bool ProgressBarInteractive
        {
            get => progressBarInteractive;
            set => progressBarInteractive = value;
        }
 
        public ProgressBarTextMode ProgressBarTextMode
        {
            get => progressBarTextMode;
            set => progressBarTextMode = value;
        }
 
        public Color ProgressBarColor
        {
            get => progressBarColor;
            set => progressBarColor = value;
        }
 
        public Color ProgressBarBgColor
        {
            get => progressBarBgColor;
            set => progressBarBgColor = value;
        }

        /// <summary>When true, the ProgressBar fill ramps red→yellow→green by value (health-bar look),
        /// overriding the flat fill color.</summary>
        public bool ProgressBarValueFill
        {
            get => progressBarValueFill;
            set => progressBarValueFill = value;
        }

        /// <summary>When true, the ProgressBar readout text sits on a soft dark pill, guaranteeing
        /// contrast over any fill/track colors (an accessibility option; off = adaptive split readout).</summary>
        public bool ProgressBarTextBackdrop
        {
            get => progressBarTextBackdrop;
            set => progressBarTextBackdrop = value;
        }

        /// <summary>ProgressBar track/fill height in pixels; 0 uses the stylesheet default.</summary>
        public float ProgressBarThickness
        {
            get => progressBarThickness;
            set => progressBarThickness = value;
        }

        /// <summary>Per-value fill gradient used when <see cref="ProgressBarValueFill"/> is on. Until the user
        /// explicitly sets one, the getter returns a fresh red→yellow→green health ramp WITHOUT storing it, so
        /// simply viewing a progress bar never marks the layout asset dirty. Assigning a value pins it.</summary>
        public Gradient ProgressBarFillGradient
        {
            get => (progressBarFillGradientSeeded && progressBarFillGradient != null)
                ? progressBarFillGradient
                : DefaultProgressFillGradient();
            set
            {
                progressBarFillGradient = value;
                progressBarFillGradientSeeded = true;
            }
        }

        /// <summary>The default red→yellow→green fill ramp (same colors as the legacy hard-coded health bar).</summary>
        public static Gradient DefaultProgressFillGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.85f, 0.22f, 0.22f), 0f),
                    new GradientColorKey(new Color(0.95f, 0.79f, 0.25f), 0.5f),
                    new GradientColorKey(new Color(0.35f, 0.75f, 0.38f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public ImageAlignment PreviewAlignment
        {
            get => previewAlignment;
            set => previewAlignment = value;
        }

        /// <summary>For <see cref="DisplayType.AssetPreview"/>: orbit / pan / zoom a prefab or mesh in a live viewport.</summary>
        public bool AssetPreviewInteractive
        {
            get => assetPreviewInteractive;
            set => assetPreviewInteractive = value;
        }

        /// <summary>For the expandable curve / gradient widgets: open the fold by default.</summary>
        public bool WidgetDefaultToOpen
        {
            get => widgetDefaultToOpen;
            set => widgetDefaultToOpen = value;
        }

        public AudioPreviewMode AudioPreviewMode
        {
            get => audioPreviewMode;
            set => audioPreviewMode = value;
        }

        public ImageAlignment AudioPlayAlignment
        {
            get => audioPlayAlignment;
            set => audioPlayAlignment = value;
        }

        public bool AudioShowLoop
        {
            get => audioShowLoop;
            set => audioShowLoop = value;
        }

        public bool AudioInteractive
        {
            get => audioInteractive;
            set => audioInteractive = value;
        }

        public float AudioWaveformHeight
        {
            get => audioWaveformHeight;
            set => audioWaveformHeight = value;
        }

        public bool AudioWaveformBelow
        {
            get => audioWaveformBelow;
            set => audioWaveformBelow = value;
        }

        /// <summary>4-way placement of the AudioPreview waveform relative to the field+buttons (ShowSoundwaves);
        /// supersedes the legacy <see cref="AudioWaveformBelow"/> bool, migrated once on load.</summary>
        public ElementPlacement AudioWaveformPlacement
        {
            get => audioWaveformPlacement;
            set => audioWaveformPlacement = value;
        }

        public float ParticlePosterTime
        {
            get => particlePosterTime;
            set => particlePosterTime = value;
        }

        /// <summary>Whether the particle preview supports orbit/pan/zoom + the Reset-view/2D overlays. Default ON.</summary>
        public bool ParticlePreviewInteractive
        {
            get => particlePreviewInteractive;
            set => particlePreviewInteractive = value;
        }

        public Color ParticleBackgroundColor
        {
            get => particleBackgroundColor;
            set => particleBackgroundColor = value;
        }

        public ParticlePreviewMode ParticlePreviewMode
        {
            get => particlePreviewMode;
            set => particlePreviewMode = value;
        }

        public float ParticleThumbnailSize
        {
            get => particleThumbnailSize;
            set => particleThumbnailSize = value;
        }

        public float ParticlePreviewZoom
        {
            get => particlePreviewZoom;
            set => particlePreviewZoom = value;
        }

        public PreviewAnchor ParticlePlayButtonAnchor
        {
            get => particlePlayButtonAnchor;
            set => particlePlayButtonAnchor = value;
        }

        public Vector2 ParticlePlayButtonOffset
        {
            get => particlePlayButtonOffset;
            set => particlePlayButtonOffset = value;
        }

        public float ParticlePlayButtonWidth
        {
            get => particlePlayButtonWidth;
            set => particlePlayButtonWidth = value;
        }

        public bool ParticlePlayOnOpen
        {
            get => particlePlayOnOpen;
            set => particlePlayOnOpen = value;
        }

        /// <summary>Expandable ParticleSystemPreview only: open the preview fold by default (until the user collapses it).</summary>
        public bool ParticleDefaultToOpen
        {
            get => particleDefaultToOpen;
            set => particleDefaultToOpen = value;
        }

        public DropdownSource DropdownSource
        {
            get => dropdownSource;
            set => dropdownSource = value;
        }

        /// <summary>
        /// For <see cref="DisplayType.InlineEditor"/>: when true, the embedded editor renders inside a
        /// collapsible chevron group so the dev can minimize/expand it in the inspector. Ignored by other kinds.
        /// </summary>
        public bool Expandable
        {
            get => expandable;
            set => expandable = value;
        }

        /// <summary>
        /// For an expandable <see cref="DisplayType.InlineEditor"/>: whether the collapsible group starts
        /// expanded (true) or collapsed (false).
        /// </summary>
        public bool DefaultToOpen
        {
            get => defaultToOpen;
            set => defaultToOpen = value;
        }

        /// <summary>For <see cref="DisplayType.ColorPalette"/>: the id of the palette (inside the project's
        /// <see cref="SasquatchColorPaletteLibrary"/>) whose swatches are shown. When blank/unknown, the widget
        /// uses the library's default palette.</summary>
        public string ColorPaletteId
        {
            get => colorPaletteId;
            set => colorPaletteId = value;
        }

        /// <summary>For <see cref="DisplayType.ColorPalette"/>: Grid (wrapping squares) or Bar (one rounded strip).</summary>
        public ColorPaletteLayout ColorPaletteLayout
        {
            get => colorPaletteLayout;
            set => colorPaletteLayout = value;
        }

        /// <summary>For a Grid ColorPalette: fixed columns per row, or 0 to auto-wrap fixed-size squares.</summary>
        public int ColorPaletteColumns
        {
            get => colorPaletteColumns;
            set => colorPaletteColumns = value;
        }

        /// <summary>For <see cref="DisplayType.ColorPalette"/>: swatch edge length in pixels (grid) / bar height.</summary>
        public float ColorSwatchSize
        {
            get => colorSwatchSize;
            set => colorSwatchSize = value;
        }

        /// <summary>For a Grid ColorPalette: pixel gap around each swatch (0 = swatches touch, never overlap).</summary>
        public float ColorSwatchSpacing
        {
            get => colorSwatchSpacing;
            set => colorSwatchSpacing = value;
        }

        /// <summary>For <see cref="DisplayType.ColorPalette"/>: also copy the picked color's hex to the clipboard.</summary>
        public bool ColorCopyOnClick
        {
            get => colorCopyOnClick;
            set => colorCopyOnClick = value;
        }

        /// <summary>For <see cref="DisplayType.ColorPalette"/>: show a standard color bar + picker for the current
        /// value (makes the selection unmistakable). Defaults on (stored inverted for serialization back-compat).</summary>
        public bool ColorShowBar
        {
            get => !colorHideBar;
            set => colorHideBar = !value;
        }

        /// <summary>For <see cref="DisplayType.ColorPalette"/>: where the color bar sits (Left/Right share the row).</summary>
        public ColorBarPlacement ColorBarPlacement
        {
            get => colorBarPlacement;
            set => colorBarPlacement = value;
        }

        /// <summary>For <see cref="DisplayType.UnityEvent"/>: lay each listener out on one dense line instead of a card.</summary>
        public bool UnityEventCompact
        {
            get => unityEventCompact;
            set => unityEventCompact = value;
        }

        /// <summary>For <see cref="DisplayType.UnityEvent"/>: whether the event opens by default (vs collapsed).</summary>
        public bool UnityEventDefaultToOpen
        {
            get => unityEventDefaultToOpen;
            set => unityEventDefaultToOpen = value;
        }

        /// <summary>For <see cref="DisplayType.UnityEvent"/>: tints the event header bar when overridden.</summary>
        public OverridableColor UnityEventAccentColor
        {
            get => unityEventAccentColor;
            set => unityEventAccentColor = value;
        }

        /// <summary>For <see cref="DisplayType.UnityEvent"/>: background for every listener card when overridden.</summary>
        public OverridableColor UnityEventCardColor
        {
            get => unityEventCardColor;
            set => unityEventCardColor = value;
        }

        /// <summary>For <see cref="DisplayType.UnityEvent"/>: hovering a function in the picker previews its
        /// source. Defaults on (stored inverted for serialization back-compat).</summary>
        public bool UnityEventCodePeek
        {
            get => !unityEventHideCodePeek;
            set => unityEventHideCodePeek = !value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: Inline well vs Expandable caret panel.</summary>
        public CurveEditorMode CurveEditorMode
        {
            get => curveEditorMode;
            set => curveEditorMode = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the curve stroke color.</summary>
        public Color CurveLineColor
        {
            get => curveLineColor;
            set => curveLineColor = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the soft fill wash under the curve.</summary>
        public Color CurveFillColor
        {
            get => curveFillColor;
            set => curveFillColor = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the graph well background color.</summary>
        public Color CurveBackgroundColor
        {
            get => curveBackgroundColor;
            set => curveBackgroundColor = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the value/time grid line color.</summary>
        public Color CurveGridColor
        {
            get => curveGridColor;
            set => curveGridColor = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: draw the value/time grid.</summary>
        public bool CurveShowGrid
        {
            get => curveShowGrid;
            set => curveShowGrid = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: inline well height in pixels (24-160).</summary>
        public float CurveWellHeight
        {
            get => curveWellHeight;
            set => curveWellHeight = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: Y-axis frame min; unset (isOverridden=false)
        /// auto-frames to the curve's value bounds.</summary>
        public OverridableFloat CurveRangeMin
        {
            get => curveRangeMin;
            set => curveRangeMin = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: Y-axis frame max; unset auto-frames.</summary>
        public OverridableFloat CurveRangeMax
        {
            get => curveRangeMax;
            set => curveRangeMax = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: snap dragged (time,value) to the grid.</summary>
        public bool CurveSnapToGrid
        {
            get => curveSnapToGrid;
            set => curveSnapToGrid = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: grid line spacing in curve units (same on the
        /// time and value axes), anchored at 0. The grid draws lines at every multiple of this, and Snap To Grid
        /// lands keys ON those lines — so a smaller size = a finer grid + tighter snapping. Not a pixel size, so the
        /// grid stays put when the well is resized.</summary>
        public float CurveGridSize
        {
            get => curveGridSize;
            set => curveGridSize = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: inline well is view-only (the resizable
        /// editor window stays fully editable).</summary>
        public bool CurveReadOnlyInline
        {
            get => curveReadOnlyInline;
            set => curveReadOnlyInline = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the inline well has a drag handle at its
        /// bottom edge to resize it (like a resizable text box). When on, the fixed Well Height option is hidden.</summary>
        public bool CurveResizable
        {
            get => curveResizable;
            set => curveResizable = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: show an editable Max-Y field over the top
        /// of the graph (rescales the value axis, like the LineRenderer curve editor).</summary>
        public bool CurveEditableMaxY
        {
            get => curveEditableMaxY;
            set => curveEditableMaxY = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: show an editable Max-X field at the right
        /// of the graph (rescales the time axis).</summary>
        public bool CurveEditableMaxX
        {
            get => curveEditableMaxX;
            set => curveEditableMaxX = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the graph well's border color; unset
        /// (isOverridden=false) resolves to the theme border so the default matches the stylesheet in both skins.</summary>
        public OverridableColor CurveBorderColor
        {
            get => curveBorderColor;
            set => curveBorderColor = value;
        }

        /// <summary>For <see cref="DisplayType.AnimationCurveEditor"/>: the graph well's border width in pixels
        /// (0-6). Defaults to 1 (the stylesheet border); 0 removes the border.</summary>
        public float CurveBorderWidth
        {
            get => curveBorderWidth;
            set => curveBorderWidth = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: Inline strip vs Expandable caret panel vs Expandable + field preview.</summary>
        public GradientEditorMode GradientEditorMode
        {
            get => gradientEditorMode;
            set => gradientEditorMode = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: DualTrack (faithful Unity model) vs Merged split-hemisphere pins.</summary>
        public GradientPinLayout GradientPinLayout
        {
            get => gradientPinLayout;
            set => gradientPinLayout = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: inline strip height in pixels (18-80).</summary>
        public float GradientWellHeight
        {
            get => gradientWellHeight;
            set => gradientWellHeight = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the inline strip has a drag handle at its bottom edge
        /// to resize it. When on, the fixed Strip Height option is hidden.</summary>
        public bool GradientResizable
        {
            get => gradientResizable;
            set => gradientResizable = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: inline strip is view-only (the resizable editor window
        /// stays fully editable).</summary>
        public bool GradientReadOnlyInline
        {
            get => gradientReadOnlyInline;
            set => gradientReadOnlyInline = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: draw a checkerboard behind the strip so transparency reads.</summary>
        public bool GradientShowCheckerboard
        {
            get => gradientShowCheckerboard;
            set => gradientShowCheckerboard = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the strip well background color (behind the checkerboard).</summary>
        public Color GradientBackgroundColor
        {
            get => gradientBackgroundColor;
            set => gradientBackgroundColor = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the strip well border color.</summary>
        public Color GradientBorderColor
        {
            get => gradientBorderColor;
            set => gradientBorderColor = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the strip well border width in pixels (0-6).</summary>
        public float GradientBorderWidth
        {
            get => gradientBorderWidth;
            set => gradientBorderWidth = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the lighter checkerboard square color.</summary>
        public Color GradientCheckerColorA
        {
            get => gradientCheckerColorA;
            set => gradientCheckerColorA = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the darker checkerboard square color.</summary>
        public Color GradientCheckerColorB
        {
            get => gradientCheckerColorB;
            set => gradientCheckerColorB = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: the palette (by id) feeding the swatch row + pin popover;
        /// blank or unknown falls back to the library's default palette.</summary>
        public string GradientColorPaletteId
        {
            get => gradientColorPaletteId;
            set => gradientColorPaletteId = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: show the palette swatch row under the editor.</summary>
        public bool GradientShowPaletteRow
        {
            get => gradientShowPaletteRow;
            set => gradientShowPaletteRow = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: draw a small "matches a palette color" dot under pins.</summary>
        public bool GradientShowPaletteDot
        {
            get => gradientShowPaletteDot;
            set => gradientShowPaletteDot = value;
        }

        /// <summary>For <see cref="DisplayType.Gradient"/>: author HDR color keys — the pin color picker gains an
        /// intensity control so values can exceed 1. Imported from <c>[GradientUsage(hdr)]</c>.</summary>
        public bool GradientHdr
        {
            get => gradientHdr;
            set => gradientHdr = value;
        }

        /// <summary>For a plain Color field (<see cref="DisplayType.Default"/>): a standard 0–1 picker vs an HDR
        /// picker with an intensity control (like Shader Graph). Imported from <c>[ColorUsage(hdr)]</c>; also
        /// settable by hand with no attribute.</summary>
        public ColorFieldMode ColorFieldMode
        {
            get => colorFieldMode;
            set => colorFieldMode = value;
        }

        /// <summary>For a plain Color field (<see cref="DisplayType.Default"/>): whether the alpha channel is
        /// editable. Imported from <c>[ColorUsage(showAlpha)]</c>. Defaults to true (Unity's default).</summary>
        public bool ColorShowAlpha
        {
            get => colorShowAlpha;
            set => colorShowAlpha = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: fixed grid column count, or 0 to auto-wrap badges.</summary>
        public int EnumBadgeColumns
        {
            get => enumBadgeColumns;
            set => enumBadgeColumns = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: show the None / All quick chips ([Flags] enums only).</summary>
        public bool EnumBadgeShowAllNone
        {
            get => enumBadgeShowAllNone;
            set => enumBadgeShowAllNone = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: also surface [Obsolete] enum members as badges.</summary>
        public bool EnumBadgeIncludeObsolete
        {
            get => enumBadgeIncludeObsolete;
            set => enumBadgeIncludeObsolete = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: per-badge height in pixels (14-60).</summary>
        public float EnumBadgeHeight
        {
            get => enumBadgeHeight;
            set => enumBadgeHeight = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: fixed per-badge width in pixels; 0 = size each badge
        /// to its own label. Only applies while <see cref="EnumBadgeColumns"/> is 0 (auto-wrap) — a fixed column
        /// count already dictates the badge width, and it wins.</summary>
        public float EnumBadgeWidth
        {
            get => enumBadgeWidth;
            set => enumBadgeWidth = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: explicit gap between badges in px; 0 = the stylesheet default.</summary>
        public float EnumBadgeSpacing
        {
            get => enumBadgeSpacing;
            set => enumBadgeSpacing = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: sit each badge's label — and its per-value icon —
        /// on a soft dark pill inside the chip, so the text keeps its contrast over any accent or per-value tint.
        /// The pill is only PAINTED on a lit badge: that is the only state whose fill is user-colored (an idle chip
        /// is the theme's own control background under the theme's own body ink, readable by construction). Its
        /// space is reserved either way, so toggling a badge never re-flows the grid. Twin of
        /// <see cref="ProgressBarTextBackdrop"/> and <c>ButtonNode.TextBackdrop</c>.</summary>
        public bool EnumBadgeTextBackdrop
        {
            get => enumBadgeTextBackdrop;
            set => enumBadgeTextBackdrop = value;
        }

        /// <summary>For <see cref="DisplayType.EnumBadgeGrid"/>: per-value tint/icon overrides, keyed by the enum
        /// member's underlying value. Empty ⇒ every badge uses the shared accent with no icon.</summary>
        public EnumBadgeOverride[] EnumBadgeOverrides
        {
            get => enumBadgeOverrides;
            set => enumBadgeOverrides = value;
        }

        /// <summary>For the Text widget: master toggle — render a live, formatted rich-text preview under the field.</summary>
        public bool RichTextPreview
        {
            get => richTextPreview;
            set => richTextPreview = value;
        }

        /// <summary>For the Text widget's rich preview: show the Bold/Italic/Underline/Color/Size/More toolbar.</summary>
        public bool RichTextToolbar
        {
            get => richTextToolbar;
            set => richTextToolbar = value;
        }

        /// <summary>For the Text widget's rich preview: the "result bubble" background (unset ⇒ dark slate).</summary>
        public OverridableColor RichTextPreviewBackground
        {
            get => richTextPreviewBackground;
            set => richTextPreviewBackground = value;
        }

        /// <summary>For the Text widget's rich preview: the preview bubble's corner radius (unset ⇒ 5px).</summary>
        public OverridableFloat RichTextPreviewCornerRadius
        {
            get => richTextPreviewCornerRadius;
            set => richTextPreviewCornerRadius = value;
        }

        /// <summary>
        /// Legacy alias for <see cref="SuffixAffix"/>'s text, kept so older call sites and saved presets keep
        /// working. Reads and writes the suffix affix directly; the serialized <c>unitSuffix</c> field survives
        /// only as the migration source (renaming a serialized field breaks the golden corpus).
        /// </summary>
        public string UnitSuffix
        {
            // Never null: the legacy field was initialised to "" and callers (and tests) rely on that.
            get => suffixAffix.text ?? "";
            set => suffixAffix.text = value;
        }

        /// <summary>
        /// Optional decoration rendered BEFORE the field's control ("$", a lock icon). Applies to every display
        /// type; <see cref="AffixMount.Auto"/> puts it inside the input box when the widget has a text input and
        /// beside it when it does not. Empty (the default) renders nothing.
        /// </summary>
        public FieldAffix PrefixAffix
        {
            get => prefixAffix;
            set => prefixAffix = value;
        }

        /// <summary>
        /// Optional decoration rendered AFTER the field's control ("kg", "m/s", "%"). The universal successor to
        /// the old unit suffix — applies to every display type, not just Slider / MinMaxSlider / ProgressBar.
        /// </summary>
        public FieldAffix SuffixAffix
        {
            get => suffixAffix;
            set => suffixAffix = value;
        }

        /// <summary>
        /// On a composite field (Vector2/3/4, Vector*Int, Rect, Bounds, euler): repeat the affixes on EVERY
        /// component box (the default) instead of showing a single pair at the end of the whole row.
        /// </summary>
        public bool AffixPerComponent
        {
            get => affixPerComponent;
            set => affixPerComponent = value;
        }

        /// <summary>
        /// For a binding used as a TABLE COLUMN: fold the affixes into the column header ("Weight (kg)") and
        /// suppress them in every cell. Ignored outside a collection's table item layout.
        /// </summary>
        public bool AffixInColumnHeader
        {
            get => affixInColumnHeader;
            set => affixInColumnHeader = value;
        }

        /// <summary>
        /// For the Default / InlineEditor / AssetPreview widgets on a ScriptableObject-reference field:
        /// show a small "Create" button while the reference is null that creates a new asset of the field's
        /// type (via a save-file dialog; abstract types offer a picker of concrete subclasses) and assigns it.
        /// </summary>
        public bool CreateAssetEnabled
        {
            get => createAssetEnabled;
            set => createAssetEnabled = value;
        }

        /// <summary>
        /// Default widget on a vector-family field (Vector2/3/4, the Int variants, Quaternion — which
        /// the inspector shows as euler angles): draw a small axis-colored pip inside the right edge of
        /// each component field, following Unity's handle convention (X red, Y green, Z blue; W neutral).
        /// </summary>
        public bool VectorAxisPips
        {
            get => vectorAxisPips;
            set => vectorAxisPips = value;
        }

        /// <summary>Rating widget: how many pips are shown (clamped 1-10 by the widget).</summary>
        public int RatingCount
        {
            get => ratingCount;
            set => ratingCount = value;
        }

        /// <summary>
        /// Dictionary table: the container-level look — shell, column header, density, and how the key
        /// presents itself. Orthogonal to <see cref="DictionaryRowSeparator"/>, which owns the row surface.
        /// Read by BOTH dictionary shapes (native <c>Dictionary&lt;K,V&gt;</c> and <c>SerializedDictionary</c>).
        /// </summary>
        public DictionaryTableStyle DictionaryStyle
        {
            get => dictionaryStyle;
            set => dictionaryStyle = value;
        }

        /// <summary>
        /// Dictionary table: the per-row surface — bare, a hairline between rows, or a tray per row with a
        /// left edge rail. The SAME enum a collection uses, drawn through the same
        /// <c>LayoutTreeBuilder.ApplyRowSeparator</c>, so a dictionary row frame and a list row frame cannot
        /// drift apart. Orthogonal to <see cref="DictionaryStyle"/>.
        /// </summary>
        public CollectionRowSeparator DictionaryRowSeparator
        {
            get => dictionaryRowSeparator;
            set => dictionaryRowSeparator = value;
        }

        /// <summary>Dictionary table: tint of the row frame — the hairline in <c>Line</c> mode, the left edge
        /// rail in <c>Card</c> mode. Unset resolves to the theme.</summary>
        public OverridableColor DictionarySeparatorColor
        {
            get => dictionarySeparatorColor;
            set => dictionarySeparatorColor = value;
        }

        /// <summary>
        /// Dictionary table: the binding that styles the KEY cell of every row — a Display Type and its whole
        /// option set, exactly like any other binding. Null draws the stock key field, which is what every
        /// dictionary drew before templates existed.
        /// <para>
        /// Its <see cref="PropertyPath"/> is EMPTY on purpose: it binds the key the row already points at, the
        /// same convention a <c>CollectionNode.ItemLayout</c> element template uses. The renderer supplies the
        /// shape's own relative name (<c>Key</c> for <c>SerializedDictionary</c>, <c>key</c> for Unity 6.6's
        /// native dictionary), so one authored template drives both shapes.
        /// </para>
        /// </summary>
        public BindingNode DictionaryKeyTemplate
        {
            get => dictionaryKeyTemplate;
            set => dictionaryKeyTemplate = value;
        }

        /// <summary>
        /// Dictionary table: the binding that styles the VALUE cell of every row. The headline half of the
        /// pair — this is what turns a <c>Dictionary&lt;Rarity, Color&gt;</c>'s value column into a Color
        /// Palette, a slider, a rating or any other Display Type. See <see cref="DictionaryKeyTemplate"/> for
        /// the empty-path convention both halves share.
        /// </summary>
        public BindingNode DictionaryValueTemplate
        {
            get => dictionaryValueTemplate;
            set => dictionaryValueTemplate = value;
        }

        /// <summary>Rating widget: which glyph each pip draws (stars / hearts / dots).</summary>
        public RatingIconStyle RatingStyle
        {
            get => ratingStyle;
            set => ratingStyle = value;
        }

        /// <summary>Rating widget: pip glyph size in px (defaults to 15; clamped 8-48 by the widget).</summary>
        public float RatingPipSize
        {
            get => ratingPipSize;
            set => ratingPipSize = value;
        }

        /// <summary>Rating widget: color of "on" pips (unset falls back to the accent, else a warm gold).</summary>
        public OverridableColor RatingSelectedColor
        {
            get => ratingSelectedColor;
            set => ratingSelectedColor = value;
        }

        /// <summary>Rating widget: color of empty / unclicked pips (unset = a muted grey).</summary>
        public OverridableColor RatingUnselectedColor
        {
            get => ratingUnselectedColor;
            set => ratingUnselectedColor = value;
        }

        /// <summary>Rating widget: preview color while hovering (unset = a lightened selected color).</summary>
        public OverridableColor RatingHoverColor
        {
            get => ratingHoverColor;
            set => ratingHoverColor = value;
        }

        /// <summary>Rating widget: play the hover/press scale spring on pips (on by default).</summary>
        public bool RatingAnimate
        {
            get => ratingAnimate;
            set => ratingAnimate = value;
        }

        /// <summary>XY Pad widget: horizontal placement of the pad within its row (Left/Center/Right).</summary>
        public ImageAlignment XYPadAlignment
        {
            get => xyPadAlignment;
            set => xyPadAlignment = value;
        }

        /// <summary>XY Pad widget: show editable X/Y number fields under the pad (default off = read-only readout).</summary>
        public bool XYPadEditableFields
        {
            get => xyPadEditableFields;
            set => xyPadEditableFields = value;
        }

        /// <summary>XY Pad widget: draw the value grid across the pad (default on).</summary>
        public bool XYPadShowGrid
        {
            get => xyPadShowGrid;
            set => xyPadShowGrid = value;
        }

        /// <summary>XY Pad widget: snap a dragged handle onto the grid lines (multiples of <see cref="XYPadGridSize"/>).</summary>
        public bool XYPadSnapToGrid
        {
            get => xyPadSnapToGrid;
            set => xyPadSnapToGrid = value;
        }

        /// <summary>XY Pad widget: draw full-width crosshair lines through the handle that track it as it moves
        /// (default off). Independent of the grid — the grid's static center crosshair only shows with the grid.</summary>
        public bool XYPadHandleCrosshair
        {
            get => xyPadHandleCrosshair;
            set => xyPadHandleCrosshair = value;
        }

        /// <summary>XY Pad widget: grid line spacing in value units; drives both the drawn grid and Snap To Grid.</summary>
        public float XYPadGridSize
        {
            get => xyPadGridSize;
            set => xyPadGridSize = value;
        }

        /// <summary>XY Pad widget: pad background color behind the grid (unset = the theme well color).</summary>
        public OverridableColor XYPadBackgroundColor
        {
            get => xyPadBackgroundColor;
            set => xyPadBackgroundColor = value;
        }

        /// <summary>XY Pad widget: grid line + center crosshair color (unset = the theme border color).</summary>
        public OverridableColor XYPadGridColor
        {
            get => xyPadGridColor;
            set => xyPadGridColor = value;
        }

        /// <summary>XY Pad widget: the aiming handle (dot + glow) color (unset = the accent color).</summary>
        public OverridableColor XYPadHandleColor
        {
            get => xyPadHandleColor;
            set => xyPadHandleColor = value;
        }

        /// <summary>XY Pad widget: handle-crosshair line thickness in pixels (only used when <see cref="XYPadHandleCrosshair"/> is on).</summary>
        public float XYPadCrosshairThickness
        {
            get => xyPadCrosshairThickness;
            set => xyPadCrosshairThickness = value;
        }

        /// <summary>XY Pad widget: handle-crosshair line color (unset = the grid color). Only used when <see cref="XYPadHandleCrosshair"/> is on.</summary>
        public OverridableColor XYPadCrosshairColor
        {
            get => xyPadCrosshairColor;
            set => xyPadCrosshairColor = value;
        }

        /// <summary>Vector Sliders widget: show the "Normalize" button that rescales the vector to unit length.</summary>
        public bool VectorShowNormalize
        {
            get => vectorShowNormalize;
            set => vectorShowNormalize = value;
        }

        /// <summary>Vector Sliders widget: horizontal placement of the Normalize button (Left/Center/Right).</summary>
        public ImageAlignment VectorNormalizeAlignment
        {
            get => vectorNormalizeAlignment;
            set => vectorNormalizeAlignment = value;
        }

        /// <summary>Normalize-button visual preset (Modern / Classic / Outline / Ghost / Soft / Danger).</summary>
        public ButtonStyle NormalizeButtonStyle { get => normalizeButtonStyle; set => normalizeButtonStyle = value; }
        /// <summary>Normalize-button accent override; unset ⇒ the preset default.</summary>
        public OverridableColor NormalizeButtonAccent { get => normalizeButtonAccent; set => normalizeButtonAccent = value; }
        /// <summary>Normalize-button corner-radius override; unset ⇒ the preset default.</summary>
        public OverridableFloat NormalizeButtonCornerRadius { get => normalizeButtonCornerRadius; set => normalizeButtonCornerRadius = value; }
        /// <summary>Normalize-button height class.</summary>
        public ButtonSize NormalizeButtonSize { get => normalizeButtonSize; set => normalizeButtonSize = value; }
        /// <summary>Normalize-button hover/press feedback.</summary>
        public ButtonHoverAnimation NormalizeButtonHoverAnimation { get => normalizeButtonHoverAnimation; set => normalizeButtonHoverAnimation = value; }
        /// <summary>Normalize-button hover background override; unset ⇒ the built-in subtle lighten.</summary>
        public OverridableColor NormalizeButtonHoverColor { get => normalizeButtonHoverColor; set => normalizeButtonHoverColor = value; }
        /// <summary>Normalize-button pressed background override; unset ⇒ spring dip only.</summary>
        public OverridableColor NormalizeButtonPressedColor { get => normalizeButtonPressedColor; set => normalizeButtonPressedColor = value; }

        /// <summary>Master toggle for the per-field help chip: when on AND <see cref="HelpText"/> is non-empty, a
        /// "?" chip beside the field reveals a markdown help card.</summary>
        public bool ShowHelpChip { get => showHelpChip; set => showHelpChip = value; }

        /// <summary>Optional per-field help text; shown (as a markdown card revealed by the "?" chip) only when
        /// <see cref="ShowHelpChip"/> is on.</summary>
        public string HelpText
        {
            get => helpText;
            set => helpText = value;
        }

        /// <summary>Per-field help chip: horizontal placement of the "?" chip (Left or Right; Right by default).</summary>
        public ImageAlignment HelpChipAlignment { get => helpChipAlignment; set => helpChipAlignment = value; }

        /// <summary>Text styling (bold/italic, size, color, alignment, font) for the help card's text.</summary>
        public TextStyle HelpTextStyle { get => helpTextStyle; set => helpTextStyle = value; }

#if SASQ_CUSTOM_WIDGET
        /// <summary>Custom Widget: the GUID of the SasquatchCustomWidget asset that renders this field
        /// (empty ⇒ no widget assigned yet → the field falls back to a plain PropertyField).</summary>
        public string CustomWidgetGuid
        {
            get => customWidgetGuid;
            set => customWidgetGuid = value;
        }

        /// <summary>Custom Widget: the mapping of the widget's NON-primary inputs to sibling property paths
        /// (the primary input is fed by <see cref="PropertyPath"/>). Never null after deserialize.</summary>
        public System.Collections.Generic.List<CustomWidgetInputMap> CustomWidgetInputs
        {
            get => customWidgetInputs;
            set => customWidgetInputs = value;
        }
#endif

        [SerializeField] private TextStyle headerTextStyle;
        [SerializeField] private TextStyle labelTextStyle;
        [SerializeField] private TextStyle fieldTextStyle;
        [SerializeField] private TextStyle toggleOnOffTextStyle;
        [SerializeField] private TextStyle minMaxLabelTextStyle;
        [SerializeField] private OverridableColor fieldBackground;
        [SerializeField] private OverridableColor dropdownPopupBackground;
        [SerializeField] private TextStyle dropdownItemTextStyle;
        [SerializeField] private OverridableColor dropdownItemHoverBackground;
        [SerializeField] private OverridableColor dropdownItemHoverColor;

        /// <summary>Text styling for this field's [Header] text (the bold label rendered above the field).</summary>
        public TextStyle HeaderTextStyle
        {
            get => headerTextStyle;
            set => headerTextStyle = value;
        }

        /// <summary>Text styling for this field's label (its name).</summary>
        public TextStyle LabelTextStyle
        {
            get => labelTextStyle;
            set => labelTextStyle = value;
        }

        /// <summary>Text styling for this field's value text (the editable input).</summary>
        public TextStyle FieldTextStyle
        {
            get => fieldTextStyle;
            set => fieldTextStyle = value;
        }

        /// <summary>Text styling for this toggle's ON/OFF overlay text.</summary>
        public TextStyle ToggleOnOffTextStyle
        {
            get => toggleOnOffTextStyle;
            set => toggleOnOffTextStyle = value;
        }

        /// <summary>Text styling for Min-Max slider labels.</summary>
        public TextStyle MinMaxLabelTextStyle
        {
            get => minMaxLabelTextStyle;
            set => minMaxLabelTextStyle = value;
        }

        /// <summary>Background color of this field's input box (the dark grey area that holds the value).</summary>
        public OverridableColor FieldBackground
        {
            get => fieldBackground;
            set => fieldBackground = value;
        }

        /// <summary>Background color of the dropdown popup menu.</summary>
        public OverridableColor DropdownPopupBackground
        {
            get => dropdownPopupBackground;
            set => dropdownPopupBackground = value;
        }

        /// <summary>Text styling for the dropdown menu items.</summary>
        public TextStyle DropdownItemTextStyle
        {
            get => dropdownItemTextStyle;
            set => dropdownItemTextStyle = value;
        }

        /// <summary>Background color of the hovered/highlighted dropdown item.</summary>
        public OverridableColor DropdownItemHoverBackground
        {
            get => dropdownItemHoverBackground;
            set => dropdownItemHoverBackground = value;
        }

        /// <summary>Text color of the hovered/highlighted dropdown item.</summary>
        public OverridableColor DropdownItemHoverColor
        {
            get => dropdownItemHoverColor;
            set => dropdownItemHoverColor = value;
        }

        /// <summary>Mirrors the suffix affix's text back onto the legacy <c>unitSuffix</c> field so anything
        /// still reading it (older saved presets, external tooling) sees the current value.</summary>
        public void OnBeforeSerialize()
        {
            unitSuffix = suffixAffix.text ?? "";
        }

        /// <summary>Back-compat fixups after load: retires pre-release <see cref="DisplayType"/> values, folds
        /// each legacy field into its replacement (every fold behind a one-shot flag so clearing an override can't
        /// resurrect it), and clamps appended enums/numbers that could hold orphaned or garbage values.</summary>
        public void OnAfterDeserialize()
        {
            // Pre-release widget kinds (retired enum ints — see the note in DisplayType): the old
            // ValueDropdown(5)/Tag(6)/Layer(7) widgets fold into DropDown, whose Auto source renders
            // tags for strings and layers for ints — and whose Method source covers a value-source
            // method, so a migrated ValueDropdown keeps working verbatim. The old string Scene
            // widget (9) reverts to the default field (SceneReference fields have their own drawer).
            int storedKind = (int)displayType;
            if (storedKind == 5 || storedKind == 6 || storedKind == 7)
            {
                displayType = DisplayType.DropDown;
                if (storedKind == 5 && !string.IsNullOrEmpty(valueSourceMethod) && dropdownSource == DropdownSource.Auto)
                    dropdownSource = DropdownSource.Method;
            }
            else if (storedKind == 9)
            {
                displayType = DisplayType.Default;
            }
            else if (storedKind == 26)
            {
                // Retired "Asset Picker" widget (AssetDropdown) — reverts to the default object field (which
                // already carries Unity's native searchable object picker), so any layout saved while it
                // existed keeps a working reference field.
                displayType = DisplayType.Default;
            }

            // Safety clamp: the old AssetPreviewMode.InlineEditor (int value 2) was promoted to its own
            // DisplayType. Any layout still carrying that orphaned value would cast to an undefined enum, so
            // reset it to Standard (the agreed "fall back to a standard preview" behavior — no auto-migration).
            if (!System.Enum.IsDefined(typeof(AssetPreviewMode), assetPreviewMode))
            {
                assetPreviewMode = AssetPreviewMode.Standard;
            }

            // AssetPreview thumbnail placement: fold the legacy previewBelowField bool into the 4-way enum once
            // (below → Below), then guard against garbage values. Only rewrite previewPlacement when the legacy
            // bool is actually set — a code-authored node that sets previewPlacement directly (and never touched
            // the legacy bool) would otherwise be clobbered back to Right on its first deserialize.
            if (!previewPlacementMigrated)
            {
                if (previewBelowField) previewPlacement = ElementPlacement.Below;
                previewPlacementMigrated = true;
            }
            if (!System.Enum.IsDefined(typeof(ElementPlacement), previewPlacement))
                previewPlacement = ElementPlacement.Right;

            // AudioPreview waveform placement: fold the legacy audioWaveformBelow bool into the 4-way enum once
            // (below → Below), then guard against garbage values. Same one-shot rule as previewPlacement above —
            // only rewrite when the legacy bool is set, so a directly-authored placement survives the first load.
            if (!audioWaveformPlacementMigrated)
            {
                if (audioWaveformBelow) audioWaveformPlacement = ElementPlacement.Below;
                audioWaveformPlacementMigrated = true;
            }
            if (!System.Enum.IsDefined(typeof(ElementPlacement), audioWaveformPlacement))
                audioWaveformPlacement = ElementPlacement.Right;

            // Legacy unit suffix → the universal suffix affix. Fold ONLY when the legacy field actually holds
            // something AND the affix is still empty: a naive unconditional copy would wipe a code-authored
            // suffixAffix on a fresh node's FIRST deserialize (the exact bug previewPlacement shipped with).
            // Conditional means no one-shot flag is needed, so serialized JSON equality is untouched.
            if (!string.IsNullOrEmpty(unitSuffix) && string.IsNullOrEmpty(suffixAffix.text))
                suffixAffix.text = unitSuffix;

            // Guard the appended affix mounts against orphaned/garbage int values.
            if (!System.Enum.IsDefined(typeof(AffixMount), prefixAffix.mount))
                prefixAffix.mount = AffixMount.Auto;
            if (!System.Enum.IsDefined(typeof(AffixMount), suffixAffix.mount))
                suffixAffix.mount = AffixMount.Auto;

            // Keep the particle poster frame a valid [0,1] fraction even if an old/edited layout stored garbage.
            particlePosterTime = Mathf.Clamp01(particlePosterTime);

            // Guard the appended particle enums against orphaned/garbage int values, and keep the badge width sane.
            if (!System.Enum.IsDefined(typeof(ParticlePreviewMode), particlePreviewMode))
                particlePreviewMode = ParticlePreviewMode.FieldAndThumbnail;
            if (!System.Enum.IsDefined(typeof(PreviewAnchor), particlePlayButtonAnchor))
                particlePlayButtonAnchor = PreviewAnchor.Center;
            particlePlayButtonWidth = Mathf.Clamp(particlePlayButtonWidth, 24f, 96f);
            particleThumbnailSize = Mathf.Clamp(particleThumbnailSize, 16f, 256f);
            particlePreviewZoom = particlePreviewZoom <= 0f ? 1f : Mathf.Clamp(particlePreviewZoom, 0.25f, 6f);

            // Legacy min/max limits: fold the plain floats into the overridable pair exactly ONCE (same one-shot
            // rule as the placement folds above). Without the flag this re-fires on every load, so CLEARING a
            // range override (the reset arrow in the designer / quick-edit) would silently resurrect it next
            // session from the stale shadow field — the user asked to follow the script's [Range] again and the
            // old pinned bound would come back.
            if (!rangeLimitsMigrated)
            {
                if (!rangeMin.isOverridden && minMaxLowLimit != 0f)
                {
                    rangeMin.isOverridden = true;
                    rangeMin.value = minMaxLowLimit;
                }
                if (!rangeMax.isOverridden && minMaxHighLimit != 100f)
                {
                    rangeMax.isOverridden = true;
                    rangeMax.value = minMaxHighLimit;
                }
                rangeLimitsMigrated = true;
            }

            // Guard the appended curve-editor options against orphaned/garbage values.
            if (!System.Enum.IsDefined(typeof(CurveEditorMode), curveEditorMode))
                curveEditorMode = CurveEditorMode.Inline;
            curveWellHeight = Mathf.Clamp(curveWellHeight, 24f, 160f);
            if (curveGridSize <= 1e-5f || float.IsNaN(curveGridSize)) curveGridSize = 0.25f; // garbage/legacy-zero → default
            if (curveRangeMin.isOverridden && curveRangeMax.isOverridden && curveRangeMax.value <= curveRangeMin.value)
                curveRangeMax.value = curveRangeMin.value + 1f;
            curveBorderWidth = Mathf.Clamp(curveBorderWidth, 0f, 6f);
            if (xyPadGridSize <= 1e-5f || float.IsNaN(xyPadGridSize)) xyPadGridSize = 0.25f; // garbage/legacy-zero → default

            // Guard the appended gradient-editor options against orphaned/garbage values.
            if (!System.Enum.IsDefined(typeof(GradientEditorMode), gradientEditorMode))
                gradientEditorMode = GradientEditorMode.Inline;
            if (!System.Enum.IsDefined(typeof(GradientPinLayout), gradientPinLayout))
                gradientPinLayout = GradientPinLayout.DualTrack;
            if (!System.Enum.IsDefined(typeof(ColorFieldMode), colorFieldMode))
                colorFieldMode = ColorFieldMode.Default;
            // Generous max matches the resizable handle's ceiling so resized strip heights survive reload.
            gradientWellHeight = Mathf.Clamp(gradientWellHeight, 18f, 160f);
            gradientBorderWidth = Mathf.Clamp(gradientBorderWidth, 0f, 6f);

            // Guard the appended dictionary options against orphaned/garbage values. Every other appended
            // enum on this node gets this treatment and these two were simply missed: a layout written by a
            // FUTURE Sasquatch that adds a fifth DictionaryTableStyle would deserialize here as an
            // out-of-range int and render no chrome at all, rather than falling back to the default look.
            if (!System.Enum.IsDefined(typeof(DictionaryTableStyle), dictionaryStyle))
                dictionaryStyle = DictionaryTableStyle.Table;
            if (!System.Enum.IsDefined(typeof(CollectionRowSeparator), dictionaryRowSeparator))
                dictionaryRowSeparator = CollectionRowSeparator.None;

            // Guard the appended enum-badge options against orphaned/garbage values.
            enumBadgeColumns = Mathf.Max(0, enumBadgeColumns);
            enumBadgeHeight = enumBadgeHeight <= 0f ? 20f : Mathf.Clamp(enumBadgeHeight, 14f, 60f);
            // 0 stays 0 (= auto, size to the label); anything else is a real width with a sane ceiling.
            enumBadgeWidth = enumBadgeWidth <= 0f ? 0f : Mathf.Min(enumBadgeWidth, 400f);

            // Migrate the removed standalone Rich Text widget (former DisplayType value 21) to a Text field with
            // the rich-text preview turned on — preserves the look for any layout saved while that value existed.
            if ((int)displayType == 21)
            {
                displayType = DisplayType.Text;
                richTextPreview = true;
            }

            // Retired: the Euler Angles widget (value 27). Unity's Default drawer already renders a Quaternion as
            // three Euler-angle fields, so it earned nothing over Default — migrate any stored node to Default
            // (lossless for the user). The enum value stays reserved; the widget is no longer offered.
            if (displayType == DisplayType.EulerAngles)
                displayType = DisplayType.Default;

            // Rich Text preview options: only the preview corner radius needs a non-negative clamp.
            if (richTextPreviewCornerRadius.isOverridden)
                richTextPreviewCornerRadius.value = Mathf.Max(0f, richTextPreviewCornerRadius.value);

#if SASQ_CUSTOM_WIDGET
            // Custom Widget: null-guard the input-map list (a layout saved before this field existed loads null).
            customWidgetInputs ??= new System.Collections.Generic.List<CustomWidgetInputMap>();
#endif
        }
    }
}
