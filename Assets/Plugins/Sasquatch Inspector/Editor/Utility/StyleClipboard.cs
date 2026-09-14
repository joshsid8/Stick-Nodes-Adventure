using System;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// How much of a node a transfer carries. The distinction exists because "styling" quietly grew a
    /// THIRD meaning that nobody named, and the format painter inherited it by accident.
    /// <para><b>Appearance</b> — how the node LOOKS: colors, sizes, text styles, placements, the box model,
    /// the widget kind. This is the format painter's whole contract, and the promise its name makes.</para>
    /// <para><b>Full</b> — appearance PLUS the value contract (what the shipped inspector will accept:
    /// slider bounds, step size, path filters, per-widget interactivity) and per-field content (unit
    /// suffixes, placeholders, dropdown sources). What an intentionally authored preset TEMPLATE carries,
    /// because a "0–100 percent bar" preset is meaningless without its bounds.</para>
    /// <para>The painter used to take everything. Painting a slider's colors onto a second slider also
    /// re-based its track, which moves the knob without changing the value — a change you cannot see at
    /// the moment you make it, and eight deep in sticky mode by the time you do. If it changes what a
    /// user of the SHIPPED inspector can type, it is not format.</para>
    /// </summary>
    internal enum StyleScope
    {
        /// <summary>Look only — Copy/Paste Style and the format painter.</summary>
        Appearance,
        /// <summary>Look + value contract + per-field content — authored preset templates.</summary>
        Full
    }

    /// <summary>
    /// In-memory style clipboard behind the designer's Copy/Paste Style menu items and the format
    /// painter, and the carry-set authority for widget presets. Copies a node's visual identity — the
    /// base LayoutStyle box model plus, for a same-category paste, the widget/appearance/field-styling
    /// options — but never what a node IS: paths, labels, header text, tooltips, methods, validation,
    /// conditions, or a button's icon. Snapshots are held on detached template nodes populated through
    /// the same transfer lists that apply them, so copy and paste can never drift apart. For bindings,
    /// the copied DisplayType is applied only when the TARGET's bound field type accepts it (per
    /// DisplayTypeCatalog); everything else still transfers, so a float→Color paste carries all the
    /// rest of the styling. Cross-CATEGORY pastes (container → field, …) carry the shared LayoutStyle
    /// only. Plain statics — the clipboard lives for the editor session and clears on domain reload.
    /// <para><b><see cref="Copy"/> always snapshots wide; <see cref="ApplyTo"/> decides how much lands.</b>
    /// A snapshot is lossless so one copy can serve both a paste (<see cref="StyleScope.Appearance"/>) and a
    /// preset capture (<see cref="StyleScope.Full"/>). Only BindingNode has a meaningful split — container,
    /// button and leaf-widget options are appearance end to end.</para>
    /// </summary>
    internal static class StyleClipboard
    {
        private enum Category { None, Container, Binding, Button, Leaf, Other }

        private static Category _category = Category.None;

        // Base box model — every LayoutNode carries this.
        private static LayoutStyle _style;

        // Snapshot templates: fresh nodes populated via the transfer lists below. Never attached to
        // any layout — they exist only to hold copied values.
        private static ContainerNode _containerSnapshot;
        private static BindingNode _bindingSnapshot;
        private static ButtonNode _buttonSnapshot;
        // A same-type snapshot for the styleable leaf widgets (Text Block / Image / Info box / Divider /
        // Space / Script Header) — held polymorphically because there's one field for all six kinds.
        private static LayoutNode _leafSnapshot;

        // The copied widget kind, applied separately because it's conditional on the target's type.
        private static DisplayType _bindingDisplayType;

        // ---- Transfer lists: (source, destination) copies used for BOTH snapshotting (real node →
        // template) and applying (template → real node). One list per category = zero drift. ----

        private static readonly Action<ContainerNode, ContainerNode>[] ContainerTransfers =
        {
            (s, d) => d.Type = s.Type,
            (s, d) => d.Expandable = s.Expandable,
            (s, d) => d.DefaultToOpen = s.DefaultToOpen,
            (s, d) => d.SectionTintColor = s.SectionTintColor,
            (s, d) => d.AccentStripeColor = s.AccentStripeColor,
            (s, d) => d.TabAccentColor = s.TabAccentColor,
            (s, d) => d.TitleTextStyle = s.TitleTextStyle,

            // Track geometry — a Rows/Columns/Grid container's visual shape (track count, gutter, grid
            // constraint, divider-resize affordances). gridColumnWeights / gridRowHeights are deliberately
            // ABSENT: those hold the RESULT of dragging a specific container's dividers — the container-level
            // twin of LayoutNode.ColumnWeight — so they're per-instance sizing, not authored styling.
            (s, d) => d.GridColumns = s.GridColumns,
            (s, d) => d.Gap = s.Gap,
            (s, d) => d.GridConstraint = s.GridConstraint,
            (s, d) => d.GridResizableColumns = s.GridResizableColumns,
            (s, d) => d.GridResizableRows = s.GridResizableRows,
            (s, d) => d.TrackResize = s.TrackResize,

            // A Frame's whole look (shape, aspect lock, overlay wash, split seam, fill fit/zoom/focus) lives
            // in one FrameSettings object — copied by VALUE with its image references stripped, see CloneFrame.
            (s, d) => d.Frame = CloneFrame(s.Frame)
        };

        /// <summary>
        /// An independent copy of <paramref name="src"/> carrying its STYLING only. <see cref="FrameSettings"/>
        /// is a plain [Serializable] class (and holds a <see cref="Gradient"/>, another class), so a bare
        /// assignment would alias: later edits to one frame would silently rewrite the other, and a captured
        /// widget-preset template would track the live preview node it was captured from. Round-tripping
        /// through Unity's serializer copies every field — including the overlay gradient — and stays
        /// drift-free as new frame settings are appended.
        /// <para>The four image references are then cleared, because they are CONTENT, not styling: a bound
        /// fill path names a field on one specific target type, and the static fill / per-pane GUIDs name
        /// specific texture assets (the same reason <c>ImageBannerNode.TextureGuid</c> is absent from
        /// <see cref="ImageBannerTransfers"/>). Everything about how a fill is FITTED — scale mode, zoom,
        /// focus — does travel, so a frame preset can be authored to suit images supplied later.</para>
        /// </summary>
        private static FrameSettings CloneFrame(FrameSettings src)
        {
            if (src == null) return new FrameSettings();
            var clone = JsonUtility.FromJson<FrameSettings>(JsonUtility.ToJson(src)) ?? new FrameSettings();
            clone.FillBindingPath = "";
            clone.FillTextureGuid = "";
            clone.PaneAFillGuid = "";
            clone.PaneBFillGuid = "";
            return clone;
        }

        /// <summary>
        /// An independent copy of a <see cref="Gradient"/>. Gradient is a CLASS, so assigning one into a
        /// snapshot template would alias it to the live node — the template would track later edits, and a
        /// paste would hand every destination the same instance.
        /// </summary>
        private static Gradient CloneGradient(Gradient src)
        {
            var g = new Gradient();
            if (src != null) { g.mode = src.mode; g.SetKeys(src.colorKeys, src.alphaKeys); }
            return g;
        }

        // ---- APPEARANCE: how the field LOOKS. Everything in the designer's Display Style/Options +
        // Appearance + Field Styling sections that is purely visual. Identity (path/label/header/tooltip),
        // behavior (read-only, required, validation) and conditions/animations are deliberately absent, and
        // so — since the format painter reads this list alone — is anything that changes what the shipped
        // inspector will ACCEPT (see BindingContractTransfers) or what it SAYS (see BindingContentTransfers).
        private static readonly Action<BindingNode, BindingNode>[] BindingAppearanceTransfers =
        {
            // Appearance / layout of the field row.
            // NOT HideLabel. Whether a field HAS a label is identity, not appearance — the same bucket as
            // LabelOverride, which this list has always excluded, and which is the modern spelling of the very
            // same state. Painting it made the destination's label vanish, and because BindingNode's explicit
            // "Hide Label" toggle no longer exists, nothing in the designer could put it back: typing into the
            // Label row was silently vetoed by the pasted flag. See BindingNode.IsLabelHidden.
            (s, d) => d.LabelAbove = s.LabelAbove,
            (s, d) => d.UseStandardAlignment = s.UseStandardAlignment,
            (s, d) => d.InlinePreview = s.InlinePreview,
            (s, d) => d.Expandable = s.Expandable,
            (s, d) => d.DefaultToOpen = s.DefaultToOpen,
            (s, d) => d.WidgetDefaultToOpen = s.WidgetDefaultToOpen,
            (s, d) => d.CreateAssetEnabled = s.CreateAssetEnabled,

            // Shared widget style.
            (s, d) => d.DisplayStyleData = s.DisplayStyleData,

            // Text widget.
            (s, d) => d.TextResize = s.TextResize,
            (s, d) => d.TextBoxHeight = s.TextBoxHeight,

            // Numeric stepper.
            (s, d) => d.ShowStepper = s.ShowStepper,
            (s, d) => d.StepperPlacement = s.StepperPlacement,
            (s, d) => d.StepperButtonStyle = s.StepperButtonStyle,
            (s, d) => d.StepperButtonAccent = s.StepperButtonAccent,
            (s, d) => d.StepperButtonCornerRadius = s.StepperButtonCornerRadius,
            (s, d) => d.StepperButtonSize = s.StepperButtonSize,
            (s, d) => d.StepperButtonHoverAnimation = s.StepperButtonHoverAnimation,
            (s, d) => d.StepperButtonHoverColor = s.StepperButtonHoverColor,
            (s, d) => d.StepperButtonPressedColor = s.StepperButtonPressedColor,
            (s, d) => d.StepperPlusColor = s.StepperPlusColor,
            (s, d) => d.StepperMinusColor = s.StepperMinusColor,
            (s, d) => d.TextResizeHandleSize = s.TextResizeHandleSize,
            (s, d) => d.GripAlignment = s.GripAlignment,
            (s, d) => d.RichTextPreview = s.RichTextPreview,
            (s, d) => d.RichTextToolbar = s.RichTextToolbar,
            (s, d) => d.RichTextPreviewBackground = s.RichTextPreviewBackground,
            (s, d) => d.RichTextPreviewCornerRadius = s.RichTextPreviewCornerRadius,

            // Asset preview.
            (s, d) => d.PreviewSize = s.PreviewSize,
            (s, d) => d.PreviewPlacement = s.PreviewPlacement,
            (s, d) => d.AssetPreviewMode = s.AssetPreviewMode,
            (s, d) => d.PreviewAlignment = s.PreviewAlignment,

            // Progress bar.
            (s, d) => d.ProgressBarTextMode = s.ProgressBarTextMode,
            (s, d) => d.ProgressBarTextBackdrop = s.ProgressBarTextBackdrop,
            (s, d) => d.ProgressBarColor = s.ProgressBarColor,
            (s, d) => d.ProgressBarBgColor = s.ProgressBarBgColor,
            (s, d) => d.ProgressBarValueFill = s.ProgressBarValueFill,
            // The gradient the flag above ENABLES. Copying the flag alone made a value-fill paste show the
            // destination's old gradient — or, on a node that never had one, nothing at all. Gradient is a
            // CLASS, so it is cloned rather than assigned: a bare assignment would alias the snapshot to the
            // live node, and later edits to one bar would silently rewrite the other (same hazard CloneFrame
            // guards for FrameSettings).
            (s, d) => d.ProgressBarFillGradient = CloneGradient(s.ProgressBarFillGradient),
            (s, d) => d.ProgressBarThickness = s.ProgressBarThickness,

            // Audio preview.
            (s, d) => d.AudioPreviewMode = s.AudioPreviewMode,
            (s, d) => d.AudioPlayAlignment = s.AudioPlayAlignment,
            (s, d) => d.AudioShowLoop = s.AudioShowLoop,
            (s, d) => d.AudioWaveformHeight = s.AudioWaveformHeight,
            (s, d) => d.AudioWaveformPlacement = s.AudioWaveformPlacement,

            // Particle / VFX preview.
            (s, d) => d.ParticlePosterTime = s.ParticlePosterTime,
            (s, d) => d.ParticleBackgroundColor = s.ParticleBackgroundColor,
            (s, d) => d.ParticlePreviewMode = s.ParticlePreviewMode,
            (s, d) => d.ParticleThumbnailSize = s.ParticleThumbnailSize,
            (s, d) => d.ParticlePreviewZoom = s.ParticlePreviewZoom,
            (s, d) => d.ParticlePlayButtonAnchor = s.ParticlePlayButtonAnchor,
            (s, d) => d.ParticlePlayButtonOffset = s.ParticlePlayButtonOffset,
            (s, d) => d.ParticlePlayButtonWidth = s.ParticlePlayButtonWidth,
            (s, d) => d.ParticlePlayOnOpen = s.ParticlePlayOnOpen,
            (s, d) => d.ParticleDefaultToOpen = s.ParticleDefaultToOpen,

            // Dropdowns.
            (s, d) => d.SearchableDropdown = s.SearchableDropdown,
            (s, d) => d.PolymorphicSearchable = s.PolymorphicSearchable,
            (s, d) => d.PolymorphicCodePeek = s.PolymorphicCodePeek,

            // Color palette widget.
            (s, d) => d.ColorPaletteId = s.ColorPaletteId,
            (s, d) => d.ColorPaletteLayout = s.ColorPaletteLayout,
            (s, d) => d.ColorPaletteColumns = s.ColorPaletteColumns,
            (s, d) => d.ColorSwatchSize = s.ColorSwatchSize,
            (s, d) => d.ColorSwatchSpacing = s.ColorSwatchSpacing,
            (s, d) => d.ColorCopyOnClick = s.ColorCopyOnClick,
            (s, d) => d.ColorShowBar = s.ColorShowBar,
            (s, d) => d.ColorBarPlacement = s.ColorBarPlacement,

            // UnityEvent widget.
            (s, d) => d.UnityEventCompact = s.UnityEventCompact,
            (s, d) => d.UnityEventDefaultToOpen = s.UnityEventDefaultToOpen,
            (s, d) => d.UnityEventAccentColor = s.UnityEventAccentColor,
            (s, d) => d.UnityEventCardColor = s.UnityEventCardColor,
            (s, d) => d.UnityEventCodePeek = s.UnityEventCodePeek,

            // Curve widget.
            (s, d) => d.CurveEditorMode = s.CurveEditorMode,
            (s, d) => d.CurveLineColor = s.CurveLineColor,
            (s, d) => d.CurveFillColor = s.CurveFillColor,
            (s, d) => d.CurveBackgroundColor = s.CurveBackgroundColor,
            (s, d) => d.CurveGridColor = s.CurveGridColor,
            (s, d) => d.CurveShowGrid = s.CurveShowGrid,
            (s, d) => d.CurveWellHeight = s.CurveWellHeight,
            // Snap/grid size are the visible GRID the curve is drawn on, so they ride with the look; the
            // curve's DOMAIN (CurveRangeMin/Max, CurveEditableMaxX/Y) is contract and does not.
            (s, d) => d.CurveSnapToGrid = s.CurveSnapToGrid,
            (s, d) => d.CurveGridSize = s.CurveGridSize,
            (s, d) => d.CurveResizable = s.CurveResizable,
            (s, d) => d.CurveBorderColor = s.CurveBorderColor,
            (s, d) => d.CurveBorderWidth = s.CurveBorderWidth,

            // Gradient widget.
            (s, d) => d.GradientEditorMode = s.GradientEditorMode,
            (s, d) => d.GradientPinLayout = s.GradientPinLayout,
            (s, d) => d.GradientWellHeight = s.GradientWellHeight,
            (s, d) => d.GradientResizable = s.GradientResizable,
            (s, d) => d.GradientShowCheckerboard = s.GradientShowCheckerboard,
            (s, d) => d.GradientBackgroundColor = s.GradientBackgroundColor,
            (s, d) => d.GradientBorderColor = s.GradientBorderColor,
            (s, d) => d.GradientBorderWidth = s.GradientBorderWidth,
            (s, d) => d.GradientCheckerColorA = s.GradientCheckerColorA,
            (s, d) => d.GradientCheckerColorB = s.GradientCheckerColorB,
            (s, d) => d.GradientColorPaletteId = s.GradientColorPaletteId,
            (s, d) => d.GradientShowPaletteRow = s.GradientShowPaletteRow,
            (s, d) => d.GradientShowPaletteDot = s.GradientShowPaletteDot,
            (s, d) => d.GradientHdr = s.GradientHdr,

            // Color widget. ColorFieldMode/ColorShowAlpha are read by BindingFactory.Color, the palette
            // widget's colour bar AND the export emitter, so leaving them behind made an HDR-with-alpha
            // field paste as a plain LDR one.
            (s, d) => d.ColorFieldMode = s.ColorFieldMode,
            (s, d) => d.ColorShowAlpha = s.ColorShowAlpha,

            // Vector axis pips (the X/Y/Z colour rails).
            (s, d) => d.VectorAxisPips = s.VectorAxisPips,

            // Help-chip STYLING. ShowHelpChip is deliberately not here: it enables a chip whose text is
            // per-field CONTENT (HelpText), and content never travels — copying the flag alone would arm a
            // chip that renders nothing. The styling does travel, so a chip switched on later inherits it.
            (s, d) => d.HelpChipAlignment = s.HelpChipAlignment,
            (s, d) => d.HelpTextStyle = s.HelpTextStyle,

            // Rating / Vector Sliders widgets.
            (s, d) => d.RatingCount = s.RatingCount,
            (s, d) => d.RatingStyle = s.RatingStyle,
            (s, d) => d.RatingPipSize = s.RatingPipSize,
            (s, d) => d.RatingSelectedColor = s.RatingSelectedColor,
            (s, d) => d.RatingUnselectedColor = s.RatingUnselectedColor,
            (s, d) => d.RatingHoverColor = s.RatingHoverColor,
            (s, d) => d.RatingAnimate = s.RatingAnimate,
            (s, d) => d.XYPadAlignment = s.XYPadAlignment,
            (s, d) => d.XYPadShowGrid = s.XYPadShowGrid,
            (s, d) => d.XYPadSnapToGrid = s.XYPadSnapToGrid,
            (s, d) => d.XYPadHandleCrosshair = s.XYPadHandleCrosshair,
            (s, d) => d.XYPadGridSize = s.XYPadGridSize,
            (s, d) => d.XYPadBackgroundColor = s.XYPadBackgroundColor,
            (s, d) => d.XYPadGridColor = s.XYPadGridColor,
            (s, d) => d.XYPadHandleColor = s.XYPadHandleColor,
            (s, d) => d.XYPadCrosshairThickness = s.XYPadCrosshairThickness,
            (s, d) => d.XYPadCrosshairColor = s.XYPadCrosshairColor,
            (s, d) => d.VectorShowNormalize = s.VectorShowNormalize,
            (s, d) => d.VectorNormalizeAlignment = s.VectorNormalizeAlignment,
            (s, d) => d.NormalizeButtonStyle = s.NormalizeButtonStyle,
            (s, d) => d.NormalizeButtonAccent = s.NormalizeButtonAccent,
            (s, d) => d.NormalizeButtonCornerRadius = s.NormalizeButtonCornerRadius,
            (s, d) => d.NormalizeButtonSize = s.NormalizeButtonSize,
            (s, d) => d.NormalizeButtonHoverAnimation = s.NormalizeButtonHoverAnimation,
            (s, d) => d.NormalizeButtonHoverColor = s.NormalizeButtonHoverColor,
            (s, d) => d.NormalizeButtonPressedColor = s.NormalizeButtonPressedColor,

            // Enum badge grid — the grid's SHAPE. Which members it offers (IncludeObsolete) is contract, and
            // the per-member overrides are content keyed to one specific enum's member names.
            (s, d) => d.EnumBadgeColumns = s.EnumBadgeColumns,
            (s, d) => d.EnumBadgeShowAllNone = s.EnumBadgeShowAllNone,
            (s, d) => d.EnumBadgeHeight = s.EnumBadgeHeight,
            (s, d) => d.EnumBadgeWidth = s.EnumBadgeWidth,
            (s, d) => d.EnumBadgeSpacing = s.EnumBadgeSpacing,
            (s, d) => d.EnumBadgeTextBackdrop = s.EnumBadgeTextBackdrop,

            // Dictionary table. Pure look, and the exact analogue of a collection's ListStyle /
            // RowSeparator / SeparatorColor trio: the shell and the row surface are orthogonal, and
            // neither one changes what the field accepts or what it means. Both dictionary shapes
            // (a native Dictionary and this package's SerializedDictionary) read the same two fields
            // through the same renderer, so one paste covers both.
            (s, d) => d.DictionaryStyle = s.DictionaryStyle,
            (s, d) => d.DictionaryRowSeparator = s.DictionaryRowSeparator,
            (s, d) => d.DictionarySeparatorColor = s.DictionarySeparatorColor,

#if SASQ_CUSTOM_WIDGET
            // WHICH custom widget renders the field is the widget kind's other half — the direct analogue of
            // DisplayType, and appearance for the same reason. The input MAP is not: it names property paths
            // on one specific target type (see BindingContentTransfers).
            (s, d) => d.CustomWidgetGuid = s.CustomWidgetGuid,
#endif

            // Field styling (text + backgrounds).
            (s, d) => d.HeaderTextStyle = s.HeaderTextStyle,
            (s, d) => d.LabelTextStyle = s.LabelTextStyle,
            (s, d) => d.FieldTextStyle = s.FieldTextStyle,
            (s, d) => d.ToggleOnOffTextStyle = s.ToggleOnOffTextStyle,
            (s, d) => d.MinMaxLabelTextStyle = s.MinMaxLabelTextStyle,
            (s, d) => d.FieldBackground = s.FieldBackground,
            (s, d) => d.DropdownPopupBackground = s.DropdownPopupBackground,
            (s, d) => d.DropdownItemTextStyle = s.DropdownItemTextStyle,
            (s, d) => d.DropdownItemHoverBackground = s.DropdownItemHoverBackground,
            (s, d) => d.DropdownItemHoverColor = s.DropdownItemHoverColor
        };

        // ---- CONTRACT: what the SHIPPED inspector will accept. Bounds, steps, filters, and per-widget
        // interactivity are field-specific decisions about the DATA, not about the look — the same category
        // as read-only/required/validation, which have always been excluded. A preset template carries them
        // (a "0–100 percent bar" preset is meaningless without its bounds); the format painter must not, or
        // painting a slider's colors silently re-bases the destination's track. That moves the knob without
        // changing the value, which reads as a bug rather than as an edit.
        private static readonly Action<BindingNode, BindingNode>[] BindingContractTransfers =
        {
            // RangeMin/RangeMax carry the WHOLE state (value + isOverridden), so they are the only range
            // transfer. Copying MinMaxLowLimit/MinMaxHighLimit as well would undo that: those setters force
            // isOverridden = true, so pasting from a node with no range override used to pin one on the
            // destination — wiping a [Range] attribute's bounds with the source's 0..100 fallback.
            (s, d) => d.RangeMin = s.RangeMin,
            (s, d) => d.RangeMax = s.RangeMax,
            (s, d) => d.StepSize = s.StepSize,

            // What a path picker will accept.
            (s, d) => d.TextPathMode = s.TextPathMode,
            (s, d) => d.PathIsFolder = s.PathIsFolder,
            (s, d) => d.PathFilter = s.PathFilter,

            // The curve's editable DOMAIN (its grid and well size are appearance and travel separately).
            (s, d) => d.CurveRangeMin = s.CurveRangeMin,
            (s, d) => d.CurveRangeMax = s.CurveRangeMax,
            (s, d) => d.CurveEditableMaxY = s.CurveEditableMaxY,
            (s, d) => d.CurveEditableMaxX = s.CurveEditableMaxX,

            // Which enum members the badge grid offers at all.
            (s, d) => d.EnumBadgeIncludeObsolete = s.EnumBadgeIncludeObsolete,

            // Per-widget interactivity — read-only-ness wearing a widget's name. BindingNode.ReadOnly itself
            // has never travelled; these are the same decision made per widget, so they follow it.
            (s, d) => d.AssetPreviewInteractive = s.AssetPreviewInteractive,
            (s, d) => d.ProgressBarInteractive = s.ProgressBarInteractive,
            (s, d) => d.AudioInteractive = s.AudioInteractive,
            (s, d) => d.ParticlePreviewInteractive = s.ParticlePreviewInteractive,
            (s, d) => d.XYPadEditableFields = s.XYPadEditableFields,
            (s, d) => d.CurveReadOnlyInline = s.CurveReadOnlyInline,
            (s, d) => d.GradientReadOnlyInline = s.GradientReadOnlyInline
        };

        // ---- CONTENT: per-field text and per-target-type bindings. These were only ever in the transfer
        // list by oversight — the class has always claimed content never travels, and a method name or a
        // property path is content by exactly the rule that keeps PropertyPath out. A preset template still
        // carries them, because a template is authored deliberately for a shape of field ("a percent field"
        // legitimately owns its "%"), whereas a paint is a gesture aimed at a look.
        private static readonly Action<BindingNode, BindingNode>[] BindingContentTransfers =
        {
            (s, d) => d.UnitSuffix = s.UnitSuffix,            // " m/s" is about the FIELD, not the styling
            // Prefix/suffix affixes travel with the FIELD for the same reason: painting "kg" onto a price is
            // exactly the invisible side effect this three-way split exists to prevent. Their styling rides
            // inside the same object deliberately — splitting look from text here would let a paint change
            // what an affix SAYS on half the rows and how it LOOKS on the other half.
            (s, d) => d.PrefixAffix = s.PrefixAffix,
            (s, d) => d.SuffixAffix = s.SuffixAffix,
            (s, d) => d.AffixPerComponent = s.AffixPerComponent,
            (s, d) => d.AffixInColumnHeader = s.AffixInColumnHeader,
            (s, d) => d.TextPlaceholder = s.TextPlaceholder,  // literal per-field copy
            (s, d) => d.ValueSourceMethod = s.ValueSourceMethod, // a method on one specific target type
            (s, d) => d.DropdownSource = s.DropdownSource,    // where the options come from

            // Keyed to one specific enum's member NAMES, so on any other enum they are inert noise.
            // Clone the array (struct elements) so the snapshot never aliases the live node's array.
            (s, d) => d.EnumBadgeOverrides = s.EnumBadgeOverrides == null
                ? null
                : (EnumBadgeOverride[])s.EnumBadgeOverrides.Clone(),

#if SASQ_CUSTOM_WIDGET
            // The input map names property paths on one specific target type — PropertyPath by another name.
            // A DEEP COPY of the list (struct elements) so a paste never aliases the source node's list.
            (s, d) => d.CustomWidgetInputs = s.CustomWidgetInputs == null
                ? new System.Collections.Generic.List<CustomWidgetInputMap>()
                : new System.Collections.Generic.List<CustomWidgetInputMap>(s.CustomWidgetInputs),
#endif
        };

        /// <summary>
        /// Runs the binding transfer lists <paramref name="scope"/> asks for. The single place the three
        /// buckets are composed, so a paste and a preset capture can never disagree about what is in which.
        /// </summary>
        private static void RunBindingTransfers(BindingNode s, BindingNode d, StyleScope scope)
        {
            foreach (var t in BindingAppearanceTransfers) t(s, d);
            if (scope != StyleScope.Full) return;
            foreach (var t in BindingContractTransfers) t(s, d);
            foreach (var t in BindingContentTransfers) t(s, d);
        }

        // Button styling (icon excluded — the icon is content, not styling).
        private static readonly Action<ButtonNode, ButtonNode>[] ButtonTransfers =
        {
            (s, d) => d.LabelTextStyle = s.LabelTextStyle,
            (s, d) => d.StylePreset = s.StylePreset,
            (s, d) => d.AccentColor = s.AccentColor,
            (s, d) => d.CornerRadius = s.CornerRadius,
            (s, d) => d.Size = s.Size,
            (s, d) => d.CustomHeight = s.CustomHeight,
            (s, d) => d.FullWidth = s.FullWidth,
            (s, d) => d.HoverAnimation = s.HoverAnimation,
            (s, d) => d.HoverColor = s.HoverColor,
            (s, d) => d.PressedColor = s.PressedColor,
            (s, d) => d.TextBackdrop = s.TextBackdrop
        };

        // ---- Styleable leaf widgets. Each list is STYLING only; content is excluded (Text Block's text,
        // Image's texture GUID, Info box's message/summary, Divider's title, Script Header's custom icon /
        // text override). TextStyle / OverridableColor are value structs, so these copies never alias. ----

        private static readonly Action<TextBlockNode, TextBlockNode>[] TextBlockTransfers =
        {
            (s, d) => d.TextStyle = s.TextStyle,
            (s, d) => d.RenderMarkdown = s.RenderMarkdown,
            (s, d) => d.BackgroundColor = s.BackgroundColor,
            (s, d) => d.Padding = s.Padding,
            (s, d) => d.CornerRadius = s.CornerRadius,
            (s, d) => d.MaxWidth = s.MaxWidth
        };

        private static readonly Action<ImageBannerNode, ImageBannerNode>[] ImageBannerTransfers =
        {
            (s, d) => d.Alignment = s.Alignment,
            (s, d) => d.ScaleMode = s.ScaleMode,
            (s, d) => d.Height = s.Height,
            (s, d) => d.Zoom = s.Zoom,
            (s, d) => d.CropAlpha = s.CropAlpha
        };

        private static readonly Action<HelpBoxNode, HelpBoxNode>[] HelpBoxTransfers =
        {
            (s, d) => d.MessageType = s.MessageType,
            (s, d) => d.Expandable = s.Expandable,
            (s, d) => d.DefaultToOpen = s.DefaultToOpen,
            (s, d) => d.RenderMarkdown = s.RenderMarkdown,
            (s, d) => d.CollapsedStyle = s.CollapsedStyle,
            (s, d) => d.ChipAlignment = s.ChipAlignment,
            (s, d) => d.MessageTextStyle = s.MessageTextStyle,
            (s, d) => d.SummaryTextStyle = s.SummaryTextStyle
        };

        private static readonly Action<LabeledDividerNode, LabeledDividerNode>[] LabeledDividerTransfers =
        {
            (s, d) => d.Thickness = s.Thickness,
            (s, d) => d.CornerRadius = s.CornerRadius,
            (s, d) => d.LineColor = s.LineColor,
            (s, d) => d.TitleTextStyle = s.TitleTextStyle
        };

        // Axis is deliberately excluded — it's context (Vertical gap in Rows vs Horizontal in Columns), not style.
        private static readonly Action<SpaceNode, SpaceNode>[] SpaceTransfers =
        {
            (s, d) => d.SpaceAmount = s.SpaceAmount,
            (s, d) => d.Flexible = s.Flexible
        };

        private static readonly Action<ScriptHeaderNode, ScriptHeaderNode>[] ScriptHeaderTransfers =
        {
            (s, d) => d.HideIcon = s.HideIcon,
            (s, d) => d.TextStyle = s.TextStyle
        };

        /// <summary>True once a style has been copied this session.</summary>
        public static bool HasStyle => _category != Category.None;

        /// <summary>Short kind label for menu text ("Field", "Container", "Button", "Node").</summary>
        public static string SourceKindLabel
        {
            get
            {
                switch (_category)
                {
                    case Category.Container: return "Container";
                    case Category.Binding: return "Field";
                    case Category.Button: return "Button";
                    case Category.Leaf: return "Widget";
                    case Category.Other: return "Node";
                    default: return "";
                }
            }
        }

        /// <summary>Snapshots <paramref name="node"/>'s styling by value onto detached template
        /// nodes. Safe to keep across later edits to the source (nothing aliases it).
        /// <para>Always snapshots <see cref="StyleScope.Full"/> — a lossless snapshot lets one copy serve both
        /// a paste and a preset capture. <see cref="ApplyTo"/> is where the scope is decided.</para></summary>
        public static void Copy(LayoutNode node)
        {
            if (node == null) return;
            _style = node.Style;

            switch (node)
            {
                case ContainerNode c:
                    _category = Category.Container;
                    _containerSnapshot = new ContainerNode();
                    foreach (var t in ContainerTransfers) t(c, _containerSnapshot);
                    break;

                case BindingNode b:
                    _category = Category.Binding;
                    _bindingSnapshot = new BindingNode();
                    RunBindingTransfers(b, _bindingSnapshot, StyleScope.Full);
                    _bindingDisplayType = b.DisplayType;
                    break;

                case ButtonNode btn:
                    _category = Category.Button;
                    _buttonSnapshot = new ButtonNode();
                    foreach (var t in ButtonTransfers) t(btn, _buttonSnapshot);
                    break;

                default:
                    // Styleable leaf widgets snapshot their full styling onto a fresh same-type node via the
                    // shared transfer; anything else carries the base LayoutStyle only (Category.Other).
                    var leafSnap = MakeStyleableLeaf(node);
                    if (leafSnap != null)
                    {
                        _category = Category.Leaf;
                        _leafSnapshot = leafSnap;
                        TransferStyling(node, _leafSnapshot, StyleScope.Full);
                    }
                    else
                    {
                        _category = Category.Other;
                    }
                    break;
            }
        }

        // A fresh instance of node's type when it is one of the styleable leaf widgets, else null.
        private static LayoutNode MakeStyleableLeaf(LayoutNode node)
        {
            if (node is TextBlockNode) return new TextBlockNode();
            if (node is ImageBannerNode) return new ImageBannerNode();
            if (node is HelpBoxNode) return new HelpBoxNode();
            if (node is LabeledDividerNode) return new LabeledDividerNode();
            if (node is SpaceNode) return new SpaceNode();
            if (node is ScriptHeaderNode) return new ScriptHeaderNode();
            return null;
        }

        /// <summary>
        /// Copies <paramref name="source"/>'s STYLING onto <paramref name="dest"/> node-to-node (no session
        /// clipboard): the base <see cref="LayoutStyle"/> box model always, plus the same-category widget/appearance
        /// options when the two nodes are the same kind (Container, Button, Binding, or a styleable leaf). Never
        /// copies identity (paths, labels, text, header, tooltips, methods, a button's icon, an image's texture).
        /// This is the single definition of "what is styling" shared by Copy/Paste-Style and the widget-drop
        /// presets. Cross-kind pairs carry the base <see cref="LayoutStyle"/> only.
        /// <para>Defaults to <see cref="StyleScope.Full"/> because its callers are the preset-template paths —
        /// an authored template is meant to carry a field's bounds and its unit suffix. The paste paths go
        /// through <see cref="ApplyTo"/> instead and ask for <see cref="StyleScope.Appearance"/>.</para>
        /// </summary>
        internal static void TransferStyling(LayoutNode source, LayoutNode dest, StyleScope scope = StyleScope.Full)
        {
            if (source == null || dest == null) return;
            dest.Style = source.Style;

            if (source is ContainerNode sc && dest is ContainerNode dc)
                foreach (var t in ContainerTransfers) t(sc, dc);
            else if (source is BindingNode sb && dest is BindingNode db)
            {
                RunBindingTransfers(sb, db, scope);
                db.DisplayType = sb.DisplayType; // unconditional here; ApplyTo gates it on the target field type
            }
            else if (source is ButtonNode sbn && dest is ButtonNode dbn)
                foreach (var t in ButtonTransfers) t(sbn, dbn);
            else if (source is TextBlockNode stb && dest is TextBlockNode dtb)
                foreach (var t in TextBlockTransfers) t(stb, dtb);
            else if (source is ImageBannerNode sib && dest is ImageBannerNode dib)
                foreach (var t in ImageBannerTransfers) t(sib, dib);
            else if (source is HelpBoxNode shb && dest is HelpBoxNode dhb)
                foreach (var t in HelpBoxTransfers) t(shb, dhb);
            else if (source is LabeledDividerNode sld && dest is LabeledDividerNode dld)
                foreach (var t in LabeledDividerTransfers) t(sld, dld);
            else if (source is SpaceNode ssp && dest is SpaceNode dsp)
                foreach (var t in SpaceTransfers) t(ssp, dsp);
            else if (source is ScriptHeaderNode ssh && dest is ScriptHeaderNode dsh)
                foreach (var t in ScriptHeaderTransfers) t(ssh, dsh);
        }

        /// <summary>
        /// Applies the copied styling to <paramref name="node"/> (no undo/persistence — the caller
        /// owns that). Base LayoutStyle always applies; category extras apply same-category. For a
        /// binding paste, <paramref name="boundFieldType"/> (the TARGET's resolved code type) gates
        /// the widget kind: it transfers only when that type accepts it, and stays put otherwise —
        /// so a float→Color paste still carries every other option. Null/unresolvable type (e.g.
        /// mid-compile) never changes the kind. Returns false when nothing was ever copied.
        /// <para><paramref name="scope"/> defaults to <see cref="StyleScope.Appearance"/> — the paste
        /// contract. Ask for <see cref="StyleScope.Full"/> only when authoring a preset template.</para>
        /// </summary>
        public static bool ApplyTo(LayoutNode node, Type boundFieldType = null,
                                   StyleScope scope = StyleScope.Appearance)
        {
            if (node == null || _category == Category.None) return false;
            node.Style = _style;

            if (_category == Category.Container && node is ContainerNode c && _containerSnapshot != null)
            {
                // Type and Frame.Shape are STRUCTURAL, not decorative: a Tabs container's direct children ARE
                // its tab pages and a DiagonalSplit frame's ARE its two panes. Assigning either as a plain
                // field value leaves the destination's existing children re-read as a shape they were never
                // built as — pasting a split frame onto a Card produced a "split" frame with no panes, which
                // the builder routes by index and no gesture can repair.
                //
                // Copying them at all IS deliberate (test-pinned): Paste Style re-types the destination. So
                // let the transfer list assign everything, then rewind just these two and re-apply them
                // through the migrations that own the children. Order matters — the type decides whether
                // this is a Frame at all, and only then does the shape decide whether its children are panes.
                var oldType = c.Type;
                var oldShape = c.Frame.Shape;

                foreach (var t in ContainerTransfers) t(_containerSnapshot, c);

                var newType = c.Type;
                var newShape = c.Frame.Shape;
                c.Type = oldType;
                c.Frame.Shape = oldShape;

                // Both are no-ops when the value is unchanged. Both migrations take their non-destructive
                // default here (KeepAsGroups leaving Tabs, AdoptExistingPanes entering a split) — a paste must
                // never raise a modal, and must never destroy wrappers the user cannot see it deciding about.
                // Adopting is the safe half of the split question: it restructures nothing, where wrapping
                // would insert a pane level the paste was never asked for.
                ContainerTypeMigration.Apply(c, newType);
                ContainerTypeMigration.ApplyFrameShape(c, newShape);
            }
            else if (_category == Category.Binding && node is BindingNode b && _bindingSnapshot != null)
            {
                RunBindingTransfers(_bindingSnapshot, b, scope);

                // Widget kind transfers only when the target's field type genuinely offers it (the
                // same applicability rules as the Display Type popup, current kind retained).
                if (boundFieldType != null &&
                    DisplayTypeCatalog.OfferedForField(b.DisplayType, boundFieldType).Contains(_bindingDisplayType))
                {
                    b.DisplayType = _bindingDisplayType;
                }
            }
            else if (_category == Category.Button && node is ButtonNode btn && _buttonSnapshot != null)
            {
                foreach (var t in ButtonTransfers) t(_buttonSnapshot, btn);
            }
            else if (_category == Category.Leaf && _leafSnapshot != null && node.GetType() == _leafSnapshot.GetType())
            {
                // Same-kind styleable leaf (Text Block / Image / Info box / Divider / Space / Script Header).
                // No leaf list has a contract/content half, so every scope lands the same values here.
                TransferStyling(_leafSnapshot, node, scope);
            }
            return true;
        }
    }
}
