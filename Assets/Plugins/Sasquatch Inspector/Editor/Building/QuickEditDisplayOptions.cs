using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements; // PopupField<T>
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The mutable context a <see cref="QuickEditDisplayOptions"/> row edits against. The inspector quick-edit
    /// popup owns this: <see cref="Commit"/> wraps a mutation in undo + persistence + refresh, and
    /// <see cref="RequestPopupRelayout"/> asks the popup to rebuild (used after an edit that adds or removes rows,
    /// since these controls emit only the CURRENT state's rows — there are no hidden containers to toggle).
    /// </summary>
    internal sealed class QuickEditOptionsContext
    {
        /// <summary>The binding node whose display options are being edited.</summary>
        public BindingNode Node;

        /// <summary>The bound field's resolved type, or <c>null</c> when it could not be resolved.</summary>
        public System.Type FieldType;

        /// <summary>Runs <paramref name="mutate"/> against <see cref="Node"/>; the popup owns undo/persist/refresh.</summary>
        public System.Action<System.Action> Commit;

        /// <summary>Rebuilds the popup (deferred + spring) after an edit that changes which rows are shown.</summary>
        public System.Action RequestPopupRelayout;
    }

    /// <summary>
    /// Builds the curated Display-Style/Options controls for a <see cref="BindingNode"/>'s
    /// <see cref="DisplayType"/> — the COMPACT, functional subset surfaced in the inspector quick-edit popup
    /// (the full option set, including color/gradient/palette pickers and icon-placement button rows, stays in
    /// the layout designer). Each row commits through <see cref="QuickEditOptionsContext.Commit"/> so persistence
    /// is centralized, mirroring <see cref="OverridableTextRow"/>'s "commit owns persistence" contract.
    ///
    /// Only the current type's rows are emitted; the popup fully rebuilds on a type change, so there are no
    /// hidden containers and no live widget-kind switching (unlike the designer). Types with no curated options
    /// (Spring, TagChips, EulerAngles) and CustomWidget (deferred to the designer) emit nothing.
    /// </summary>
    internal static class QuickEditDisplayOptions
    {
        /// <summary>Builds the curated Display-Style/Options controls for <c>ctx.Node.DisplayType</c> into <paramref name="parent"/>.</summary>
        public static void Build(VisualElement parent, QuickEditOptionsContext ctx)
        {
            if (parent == null || ctx == null || ctx.Node == null) return;
            var bn = ctx.Node;

            switch (bn.DisplayType)
            {
                case DisplayType.Default:              BuildDefault(parent, ctx, bn); break;
                case DisplayType.DropDown:             BuildDropDown(parent, ctx, bn); break;
                case DisplayType.Toggle:               BuildToggle(parent, ctx, bn); break;
                case DisplayType.Slider:               BuildSlider(parent, ctx, bn); break;
                case DisplayType.MinMaxSlider:         BuildMinMax(parent, ctx, bn); break;
                case DisplayType.ProgressBar:          BuildProgressBar(parent, ctx, bn); break;
                case DisplayType.Text:                 BuildText(parent, ctx, bn); break;
                case DisplayType.AssetPreview:         BuildAssetPreview(parent, ctx, bn); break;
                case DisplayType.InlineEditor:         BuildInlineEditor(parent, ctx, bn); break;
                case DisplayType.SerializeReference:   BuildSerializeReference(parent, ctx, bn); break;
                case DisplayType.UnityEvent:           BuildUnityEvent(parent, ctx, bn); break;
                case DisplayType.AudioPreview:         BuildAudioPreview(parent, ctx, bn); break;
                case DisplayType.ParticleSystemPreview: BuildParticle(parent, ctx, bn); break;
                case DisplayType.AnimationCurveEditor: BuildCurve(parent, ctx, bn); break;
                case DisplayType.Gradient:             BuildGradient(parent, ctx, bn); break;
                case DisplayType.EnumBadgeGrid:        BuildEnumBadge(parent, ctx, bn); break;
                case DisplayType.ColorPalette:         BuildColorPalette(parent, ctx, bn); break;
                case DisplayType.Rating:               BuildRating(parent, ctx, bn); break;
                case DisplayType.XYPad:                BuildXYPad(parent, ctx, bn); break;
                case DisplayType.VectorSliders:        BuildVectorSliders(parent, ctx, bn); break;
                case DisplayType.Dictionary:           AddDictionaryRows(parent, ctx, bn); break;
                // Spring / TagChips / EulerAngles have no curated options; CustomWidget is deferred to the
                // designer (asset picker + input mapping). Default arm: emit nothing.
                default: break;
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Per-type builders
        // ---------------------------------------------------------------------------------------------------

        private static void BuildDefault(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Numeric stepper: −/+ buttons that step by Step Size. Toggling it reveals/hides the two sub-rows.
            // Gated on the SAME applicability the designer's own row uses (DisplayTypeCatalog.IsSliderNumeric)
            // — this block had no field-type gate at all, so a string or an object reference was offered a
            // stepper block whose value no renderer reads (BindingFactory:197 requires IsNumericProperty).
            // The neighbouring blocks below were already gated this way; this one was the odd one out.
            if (DisplayTypeCatalog.IsSliderNumeric(ctx.FieldType))
            {
                BoolRow(p, "Numeric Stepper", bn.ShowStepper, ctx, v => bn.ShowStepper = v,
                    "Wrap the value in −/+ buttons that increment/decrement by the Step Size (at native precision).",
                    relayout: true);
                if (bn.ShowStepper)
                {
                    FloatRow(p, "Step Size", bn.StepSize, ctx, v => bn.StepSize = v,
                        "Amount each −/+ press changes the value (fractional steps on integer fields round, with a ±1 floor).");
                    EnumRow(p, "Button Placement", bn.StepperPlacement, ctx, v => bn.StepperPlacement = v);
                    // −/+ button look (enums only; the accent / per-button colors stay in the designer, like the other widgets).
                    EnumRow(p, "Button Style", bn.StepperButtonStyle, ctx, v => bn.StepperButtonStyle = v, relayout: true);
                    if (bn.StepperButtonStyle != ButtonStyle.Classic)
                    {
                        ButtonSizeRow(p, "Button Size", bn.StepperButtonSize, ctx, v => bn.StepperButtonSize = v);
                        EnumRow(p, "Hover Animation", bn.StepperButtonHoverAnimation, ctx, v => bn.StepperButtonHoverAnimation = v);
                    }
                }
            }

            AddAxisPips(p, ctx, bn);

            // Plain Color field: HDR / alpha authoring (mirrors [ColorUsage]). Only for a Color-typed field.
            if (ctx.FieldType == typeof(Color))
            {
                EnumRow(p, "Color Mode", bn.ColorFieldMode, ctx, v => bn.ColorFieldMode = v);
                BoolRow(p, "Show Alpha", bn.ColorShowAlpha, ctx, v => bn.ColorShowAlpha = v,
                    "Whether the alpha channel is editable in the color picker.");
            }

            AddCreateAssetButton(p, ctx, bn);

            // A SerializedDictionary field keeps Display Type = Default (BindingFactory auto-routes it to the
            // Sasquatch table regardless, on the SpringConfig precedent), so its style rows have to be offered
            // from THIS arm as well as from the Dictionary one. The shared gate decides; without this the
            // designer would show the rows for a SerializedDictionary and the popup would not.
            AddDictionaryRows(p, ctx, bn);
        }

        /// <summary>
        /// The Dictionary Style / Row Frame rows. Reached from TWO arms - the explicit Dictionary display type
        /// over a native <c>Dictionary&lt;K,V&gt;</c>, and Default over one of this package own
        /// <c>SerializedDictionary</c> fields - so the gate is
        /// <see cref="DisplayTypeCatalog.ShowsDictionaryTable"/>, the same predicate the designer panel and the
        /// live builder ask. Frame Colour stays designer-only, like every other heavy colour picker here.
        /// </summary>
        private static void AddDictionaryRows(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            if (!DisplayTypeCatalog.ShowsDictionaryTable(bn.DisplayType, ctx.FieldType)) return;

            EnumRow(p, "Dictionary Style", bn.DictionaryStyle, ctx, v => bn.DictionaryStyle = v).tooltip =
                "The overall look of the Key / Value table: Table (bordered shell + column header), " +
                "Minimal (no shell), Ledger (recessed well, zebra rows, one rule down the column split), " +
                "or Mapping (the key as a chip with an arrow to the value). Independent of Row Frame.";

            EnumRow(p, "Row Frame", bn.DictionaryRowSeparator, ctx, v => bn.DictionaryRowSeparator = v).tooltip =
                "The per-row surface, the same enum a List collection uses: None (bare), Line (a hairline " +
                "between rows), or Card (a tray per row with a left edge rail - the one for tall values).";
        }

        private static void BuildDropDown(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Source decides what fills the dropdown; the Method text field appears only for the Method source.
            EnumRow(p, "Source", bn.DropdownSource, ctx, v => bn.DropdownSource = v, relayout: true);
            if (bn.DropdownSource == DropdownSource.Method)
                TextRow(p, "Value Source Method", bn.ValueSourceMethod, ctx, v => bn.ValueSourceMethod = v,
                    "A parameterless method on the target returning an IEnumerable of options (instance or static).");
            BoolRow(p, "Searchable", bn.SearchableDropdown, ctx, v => bn.SearchableDropdown = v,
                "Show a type-to-search box in the dropdown popup.");
        }

        private static void BuildToggle(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Toggle Style gates the Corner Radius row (Checkbox hides it).
            EnumRow(p, "Toggle Style", bn.DisplayStyleData.toggleStyle, ctx,
                v => { var ws = bn.DisplayStyleData; ws.toggleStyle = v; bn.DisplayStyleData = ws; }, relayout: true);
            EnumRow(p, "Size", bn.DisplayStyleData.toggleSize, ctx,
                v => { var ws = bn.DisplayStyleData; ws.toggleSize = v; bn.DisplayStyleData = ws; });
            // ON/OFF overlay text is a SWITCH affordance — both checkbox looks hide it (designer parity).
            if (bn.DisplayStyleData.toggleStyle != ToggleStyle.Checkbox
                && bn.DisplayStyleData.toggleStyle != ToggleStyle.ModernCheckbox)
                BoolRow(p, "Show ON/OFF Text", bn.DisplayStyleData.toggleShowOnOff, ctx,
                    v => { var ws = bn.DisplayStyleData; ws.toggleShowOnOff = v; bn.DisplayStyleData = ws; });

            // Corner Radius is meaningless on the Checkbox style (the designer hides it there too;
            // Modern Checkbox rounds its box with it).
            if (bn.DisplayStyleData.toggleStyle != ToggleStyle.Checkbox)
                AddOverridableFloatRow(p, ctx, bn, "Corner Radius",
                    () => bn.DisplayStyleData.cornerRadius, of => { var ws = bn.DisplayStyleData; ws.cornerRadius = of; bn.DisplayStyleData = ws; },
                    codeDefault: 0f);
        }

        private static void BuildSlider(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // sliderHideField is INVERTED — false = the value field shows. Toggling reveals/hides Field Placement.
            BoolRow(p, "Show Field", !bn.DisplayStyleData.sliderHideField, ctx,
                v => { var ws = bn.DisplayStyleData; ws.sliderHideField = !v; bn.DisplayStyleData = ws; },
                "Show an editable value field alongside the slider.", relayout: true);
            if (!bn.DisplayStyleData.sliderHideField)
                PlacementRow(p, "Field Placement", ctx,
                    () => bn.DisplayStyleData.sliderFieldPlacement,
                    v => { var ws = bn.DisplayStyleData; ws.sliderFieldPlacement = v; bn.DisplayStyleData = ws; },
                    new (string, SliderFieldPlacement, string)[]
                    {
                        (SasquatchIcons.Ids.ArrowLeft,  SliderFieldPlacement.Left,  "Field left of the slider"),
                        (SasquatchIcons.Ids.ArrowUp,    SliderFieldPlacement.Above, "Field above the slider (centered)"),
                        (SasquatchIcons.Ids.ArrowRight, SliderFieldPlacement.Right, "Field right of the slider (default)"),
                        (SasquatchIcons.Ids.ArrowDown,  SliderFieldPlacement.Below, "Field below the slider (centered)"),
                    });

            // Knob Shape gates the Knob Roundness row (only Square / Rectangle knobs have a corner radius).
            EnumRow(p, "Knob Shape", bn.DisplayStyleData.sliderKnobShape, ctx,
                v => { var ws = bn.DisplayStyleData; ws.sliderKnobShape = v; bn.DisplayStyleData = ws; }, relayout: true);
            if (bn.DisplayStyleData.sliderKnobShape == SliderKnobShape.Square ||
                bn.DisplayStyleData.sliderKnobShape == SliderKnobShape.Rectangle)
                FloatRow(p, "Knob Roundness", bn.DisplayStyleData.sliderKnobRoundness, ctx,
                    v => { var ws = bn.DisplayStyleData; ws.sliderKnobRoundness = Mathf.Max(0f, v); bn.DisplayStyleData = ws; },
                    "Corner radius of a Square or Rectangle knob (px).");

            EnumRow(p, "Size", bn.DisplayStyleData.sliderSize, ctx,
                v => { var ws = bn.DisplayStyleData; ws.sliderSize = v; bn.DisplayStyleData = ws; });
        }

        private static void BuildMinMax(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            AddOverridableFloatRow(p, ctx, bn, "Range Min", () => bn.RangeMin, of => bn.RangeMin = of, codeDefault: 0f);
            AddOverridableFloatRow(p, ctx, bn, "Range Max", () => bn.RangeMax, of => bn.RangeMax = of, codeDefault: 100f);

            // Show Min/Max Values gates the Editable Values sub-row (matches the designer's labelOptions gating).
            // Both flags are stored inverted (see DisplayStyleData) — the rows read and write the friendly sense.
            BoolRow(p, "Show Min/Max Values", !bn.DisplayStyleData.minMaxHideValues, ctx,
                v => { var ws = bn.DisplayStyleData; ws.minMaxHideValues = !v; bn.DisplayStyleData = ws; }, relayout: true);
            if (!bn.DisplayStyleData.minMaxHideValues)
                BoolRow(p, "Editable Values", !bn.DisplayStyleData.minMaxReadOnlyValues, ctx,
                    v => { var ws = bn.DisplayStyleData; ws.minMaxReadOnlyValues = !v; bn.DisplayStyleData = ws; },
                    "Render the labels as number fields you can type into (or drag the slider).");

            EnumRow(p, "Size", bn.DisplayStyleData.sliderSize, ctx,
                v => { var ws = bn.DisplayStyleData; ws.sliderSize = v; bn.DisplayStyleData = ws; });
        }

        private static void BuildProgressBar(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            AddOverridableFloatRow(p, ctx, bn, "Range Min", () => bn.RangeMin, of => bn.RangeMin = of, codeDefault: 0f);
            AddOverridableFloatRow(p, ctx, bn, "Range Max", () => bn.RangeMax, of => bn.RangeMax = of, codeDefault: 100f);
            BoolRow(p, "Interactive", bn.ProgressBarInteractive, ctx, v => bn.ProgressBarInteractive = v,
                "Allow clicking/dragging the bar to edit the field value in real time.");
            // Value-Based Fill drives the (designer-only) gradient/flat-color rows, so it gates nothing here.
            BoolRow(p, "Value-Based Fill", bn.ProgressBarValueFill, ctx, v => bn.ProgressBarValueFill = v,
                "Ramp the fill color by value (classic health-bar look), overriding the flat fill color.");
            EnumRow(p, "Text Display Mode", bn.ProgressBarTextMode, ctx, v => bn.ProgressBarTextMode = v);
            BoolRow(p, "Text Backdrop", bn.ProgressBarTextBackdrop, ctx, v => bn.ProgressBarTextBackdrop = v,
                "Sit the readout text on a soft dark pill — guaranteed readability over any bar colors. Off = the text adapts its color to the fill and track automatically.");
            SliderRow(p, "Height (px)", bn.ProgressBarThickness, 0f, 48f, ctx,
                v => bn.ProgressBarThickness = Mathf.Clamp(v, 0f, 48f),
                "Height of the progress bar track in pixels (0-48). 0 = the stylesheet default.");
        }

        private static void BuildText(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Text Resize gates the resize-handle sub-rows (they only matter for a Resizable field).
            EnumRow(p, "Text Resize", bn.TextResize, ctx, v => bn.TextResize = v, relayout: true);
            if (bn.TextResize == TextResizeMode.Resizable)
            {
                // Handle Size gates Grip Alignment (a FullWidth grip spans the box, so alignment is moot).
                EnumRow(p, "Resize Handle Size", bn.TextResizeHandleSize, ctx, v => bn.TextResizeHandleSize = v, relayout: true);
                if (bn.TextResizeHandleSize != TextResizeHandleSize.FullWidth)
                    EnumRow(p, "Grip Alignment", bn.GripAlignment, ctx, v => bn.GripAlignment = v);
            }

            TextRow(p, "Placeholder", bn.TextPlaceholder, ctx, v => bn.TextPlaceholder = v,
                "Greyed hint shown over the field while it's empty. Empty = none.");

            // Input Mode gates the Path sub-rows; Browse Folders in turn gates the file filter.
            EnumRow(p, "Input Mode", bn.TextPathMode, ctx, v => bn.TextPathMode = v, relayout: true);
            if (bn.TextPathMode == TextPathMode.Path)
            {
                BoolRow(p, "Browse Folders", bn.PathIsFolder, ctx, v => bn.PathIsFolder = v,
                    "Browse for a folder instead of a file.", relayout: true);
                if (!bn.PathIsFolder)
                    TextRow(p, "File Filter", bn.PathFilter, ctx, v => bn.PathFilter = v,
                        "Comma-separated extension filter for the file picker, e.g. \"png,jpg\". Empty = any file.");
            }

            // Rich Text preview gates the toolbar toggle.
            BoolRow(p, "Rich Text Preview", bn.RichTextPreview, ctx, v => bn.RichTextPreview = v,
                "Render a live, formatted rich-text preview under the field.", relayout: true);
            if (bn.RichTextPreview)
                BoolRow(p, "Show Formatting Toolbar", bn.RichTextToolbar, ctx, v => bn.RichTextToolbar = v,
                    "Show the Bold / Italic / Underline / Color / Size / More toolbar above the text box.");
        }

        private static void BuildAssetPreview(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            EnumRow(p, "Preview Mode", bn.AssetPreviewMode, ctx, v => bn.AssetPreviewMode = v);
            SliderRow(p, "Thumbnail Size", bn.PreviewSize, 16f, 128f, ctx, v => bn.PreviewSize = Mathf.Clamp(v, 16f, 128f),
                "Preview thumbnail edge length in pixels (16-128).");
            PlacementRow(p, "Preview Placement", ctx,
                () => bn.PreviewPlacement, v => bn.PreviewPlacement = v,
                new (string, ElementPlacement, string)[]
                {
                    (SasquatchIcons.Ids.ArrowLeft,  ElementPlacement.Left,  "Thumbnail on the left, sharing the field's row"),
                    (SasquatchIcons.Ids.ArrowUp,    ElementPlacement.Above, "Thumbnail above the field"),
                    (SasquatchIcons.Ids.ArrowRight, ElementPlacement.Right, "Thumbnail on the right, sharing the field's row (default)"),
                    (SasquatchIcons.Ids.ArrowDown,  ElementPlacement.Below, "Thumbnail below the field"),
                });
            BoolRow(p, "Interactive", bn.AssetPreviewInteractive, ctx, v => bn.AssetPreviewInteractive = v,
                "Drag to orbit, middle-drag to pan, Alt+scroll to zoom a prefab or mesh.");
            AddCreateAssetButton(p, ctx, bn);
        }

        private static void BuildInlineEditor(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            BoolRow(p, "Expandable", bn.Expandable, ctx, v => bn.Expandable = v,
                "Allow collapsing/expanding the embedded editor by clicking its header.", relayout: true);
            if (bn.Expandable)
                BoolRow(p, "Default to Open", bn.DefaultToOpen, ctx, v => bn.DefaultToOpen = v,
                    "If expandable, should the embedded editor open by default?");
            AddCreateAssetButton(p, ctx, bn);
        }

        private static void BuildSerializeReference(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            BoolRow(p, "Searchable", bn.PolymorphicSearchable, ctx, v => bn.PolymorphicSearchable = v,
                "Show a type-to-search box in the polymorphic type picker.");
            BoolRow(p, "Code Peek", bn.PolymorphicCodePeek, ctx, v => bn.PolymorphicCodePeek = v,
                "Hovering a type in the picker previews its declaration.");
        }

        private static void BuildUnityEvent(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            BoolRow(p, "Compact Rows", bn.UnityEventCompact, ctx, v => bn.UnityEventCompact = v,
                "Lay each listener out on one dense line instead of a roomy card.");
            BoolRow(p, "Default to Open", bn.UnityEventDefaultToOpen, ctx, v => bn.UnityEventDefaultToOpen = v,
                "Whether the event opens by default (vs collapsed).");
            BoolRow(p, "Code Peek", bn.UnityEventCodePeek, ctx, v => bn.UnityEventCodePeek = v,
                "Hovering a function in the Select Function popup previews its source code.");
        }

        private static void BuildAudioPreview(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Preview Mode gates the soundwave-only rows (waveform height + interactive scrubbing).
            EnumRow(p, "Preview Mode", bn.AudioPreviewMode, ctx, v => bn.AudioPreviewMode = v, relayout: true);
            if (bn.AudioPreviewMode == AudioPreviewMode.ShowSoundwaves)
            {
                SliderRow(p, "Waveform Height", bn.AudioWaveformHeight, 24f, 128f, ctx,
                    v => bn.AudioWaveformHeight = Mathf.Clamp(v, 24f, 128f), "Waveform row height in pixels (24-128).");
                BoolRow(p, "Interactive (scrub)", bn.AudioInteractive, ctx, v => bn.AudioInteractive = v,
                    "Allow clicking/dragging the waveform to seek and play from that position.");
            }
        }

        private static void BuildParticle(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            EnumRow(p, "Preview Mode", bn.ParticlePreviewMode, ctx, v => bn.ParticlePreviewMode = v);
            BoolRow(p, "Interactive (orbit/pan/zoom)", bn.ParticlePreviewInteractive, ctx,
                v => bn.ParticlePreviewInteractive = v,
                "Drag to orbit · middle-drag to pan · Alt+scroll to zoom, plus Reset-view + 2D overlay buttons.");
            BoolRow(p, "Play On Open", bn.ParticlePlayOnOpen, ctx, v => bn.ParticlePlayOnOpen = v,
                "Expandable mode: start playing automatically when the preview is opened.");
            BoolRow(p, "Default to Open", bn.ParticleDefaultToOpen, ctx, v => bn.ParticleDefaultToOpen = v,
                "Expandable mode: the preview fold opens by default.");
            SliderRow(p, "Thumbnail Size", bn.ParticleThumbnailSize, 16f, 256f, ctx,
                v => bn.ParticleThumbnailSize = Mathf.Clamp(v, 16f, 256f), "Preview thumbnail edge length in pixels (16-256).");
            SliderRow(p, "Preview Zoom", bn.ParticlePreviewZoom, 0.25f, 6f, ctx,
                v => bn.ParticlePreviewZoom = Mathf.Clamp(v, 0.25f, 6f),
                "VFX Graph only: magnifies the preview camera so the effect fills the thumbnail (0.25-6).");
        }

        private static void BuildCurve(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            EnumRow(p, "Editor Mode", bn.CurveEditorMode, ctx, v => bn.CurveEditorMode = v);
            BoolRow(p, "Default to Open", bn.WidgetDefaultToOpen, ctx, v => bn.WidgetDefaultToOpen = v,
                "When Expandable, the fold starts open (until the user collapses it).");

            // Resizable owns the height when on, so the fixed Well Height row hides then.
            BoolRow(p, "Resizable", bn.CurveResizable, ctx, v => bn.CurveResizable = v,
                "Drag a handle at the bottom of the well to resize it.", relayout: true);
            if (!bn.CurveResizable)
                SliderRow(p, "Well Height", bn.CurveWellHeight, 24f, 160f, ctx,
                    v => bn.CurveWellHeight = Mathf.Clamp(v, 24f, 160f), "Inline well height in pixels (24-160).");

            BoolRow(p, "Show Grid", bn.CurveShowGrid, ctx, v => bn.CurveShowGrid = v, "Draw the value/time grid.",
                relayout: true);
            BoolRow(p, "Snap To Grid", bn.CurveSnapToGrid, ctx, v => bn.CurveSnapToGrid = v,
                "Snap dragged keyframes onto the value/time grid — keys land line-to-line.", relayout: true);
            if (bn.CurveShowGrid || bn.CurveSnapToGrid)
                FloatRow(p, "Grid Size", bn.CurveGridSize, ctx,
                    v => bn.CurveGridSize = Mathf.Clamp(v, 0.001f, 1000f),
                    "Value-axis grid spacing in curve units; the time axis picks the closest clean step that keeps cells square. Snap To Grid lands keys on the drawn lines (smaller = finer + tighter snapping).");
            BoolRow(p, "Read-Only Inline", bn.CurveReadOnlyInline, ctx, v => bn.CurveReadOnlyInline = v,
                "Make the inline well view-only; the curve stays editable in the resizable window.");
            BoolRow(p, "Editable Max Y", bn.CurveEditableMaxY, ctx, v => bn.CurveEditableMaxY = v,
                "Show an editable Max-Y field over the top of the graph to rescale the value axis.");
            BoolRow(p, "Editable Max X", bn.CurveEditableMaxX, ctx, v => bn.CurveEditableMaxX = v,
                "Show an editable Max-X field at the right of the graph to rescale the time axis.");

            // "Fixed Y Range" pins the Y axis: it is the isOverridden flag on BOTH CurveRangeMin/Max together.
            bool fixedY = bn.CurveRangeMin.isOverridden && bn.CurveRangeMax.isOverridden;
            BoolRow(p, "Fixed Y Range", fixedY, ctx, v =>
                {
                    var lo = bn.CurveRangeMin; lo.isOverridden = v; bn.CurveRangeMin = lo;
                    var hi = bn.CurveRangeMax; hi.isOverridden = v; bn.CurveRangeMax = hi;
                },
                "Off: the editor auto-frames to the curve's value range. On: pin the Y axis to the Min/Max below.",
                relayout: true);
            if (fixedY)
            {
                FloatRow(p, "Min Y", bn.CurveRangeMin.value, ctx,
                    v => { var lo = bn.CurveRangeMin; lo.value = v; bn.CurveRangeMin = lo; });
                FloatRow(p, "Max Y", bn.CurveRangeMax.value, ctx,
                    v => { var hi = bn.CurveRangeMax; hi.value = v; bn.CurveRangeMax = hi; });
            }
        }

        private static void BuildGradient(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            EnumRow(p, "Editor Mode", bn.GradientEditorMode, ctx, v => bn.GradientEditorMode = v);
            BoolRow(p, "Default to Open", bn.WidgetDefaultToOpen, ctx, v => bn.WidgetDefaultToOpen = v,
                "When Expandable, the fold starts open (until the user collapses it).");
            EnumRow(p, "Pin Layout", bn.GradientPinLayout, ctx, v => bn.GradientPinLayout = v);

            // Resizable owns the height when on, so the fixed Strip Height row hides then.
            BoolRow(p, "Resizable", bn.GradientResizable, ctx, v => bn.GradientResizable = v,
                "Drag a handle at the bottom of the strip to resize it.", relayout: true);
            if (!bn.GradientResizable)
                SliderRow(p, "Strip Height", bn.GradientWellHeight, 18f, 80f, ctx,
                    v => bn.GradientWellHeight = Mathf.Clamp(v, 18f, 80f), "Inline strip height in pixels (18-80).");

            BoolRow(p, "Read-Only Inline", bn.GradientReadOnlyInline, ctx, v => bn.GradientReadOnlyInline = v,
                "Make the inline strip view-only; the gradient stays editable in the resizable window.");
            BoolRow(p, "Palette Match Ring", bn.GradientShowPaletteDot, ctx, v => bn.GradientShowPaletteDot = v,
                "Draw a slim accent ring around any pin whose color matches a swatch in the active palette.");
            BoolRow(p, "HDR", bn.GradientHdr, ctx, v => bn.GradientHdr = v,
                "Author HDR color keys — the pin color picker gains an intensity control so colors can exceed 1.");
        }

        private static void BuildEnumBadge(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            var columnsRow = IntRow(p, "Columns", bn.EnumBadgeColumns, ctx, v => bn.EnumBadgeColumns = Mathf.Max(0, v),
                "Fixed number of grid columns, or 0 to auto-wrap badges to fit the width.");
            // Columns WINS over an explicit width, so the Badge Width row is only offered while Columns is 0
            // (auto-wrap). Relayout only when that gate actually flips — editing 3 → 4 must not rebuild the popup.
            bool hadAutoColumns = bn.EnumBadgeColumns <= 0;
            columnsRow.RegisterValueChangedCallback(e =>
            {
                bool auto = Mathf.Max(0, e.newValue) <= 0;
                if (auto == hadAutoColumns) return;
                hadAutoColumns = auto;
                ctx.RequestPopupRelayout?.Invoke();
            });

            SliderRow(p, "Badge Height", bn.EnumBadgeHeight, 14f, 60f, ctx, v => bn.EnumBadgeHeight = Mathf.Clamp(v, 14f, 60f),
                "Height of each badge in pixels (14-60).");
            if (bn.EnumBadgeColumns <= 0)
                FloatRow(p, "Badge Width", bn.EnumBadgeWidth, ctx, v => bn.EnumBadgeWidth = Mathf.Clamp(v, 0f, 400f),
                    "Fixed width for every badge in pixels. 0 sizes each badge to its label.");
            FloatRow(p, "Badge Spacing", bn.EnumBadgeSpacing, ctx, v => bn.EnumBadgeSpacing = Mathf.Max(0f, v),
                "Gap between badges in pixels. 0 = the stylesheet default.");
            BoolRow(p, "Text Backdrop", bn.EnumBadgeTextBackdrop, ctx, v => bn.EnumBadgeTextBackdrop = v,
                "Sit each badge's label on a soft dark pill, for guaranteed readability over any accent color.");
            // Only meaningful for [Flags] enums (multi-select), but always offered here — the widget ignores it
            // for a single-select enum.
            BoolRow(p, "Show All/None", bn.EnumBadgeShowAllNone, ctx, v => bn.EnumBadgeShowAllNone = v,
                "Show the None / All quick chips ([Flags] enums only).");
            BoolRow(p, "Include Obsolete", bn.EnumBadgeIncludeObsolete, ctx, v => bn.EnumBadgeIncludeObsolete = v,
                "Also surface enum members marked [Obsolete] as badges.");
        }

        private static void BuildColorPalette(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Layout gates the grid-only Columns / Swatch Spacing rows.
            EnumRow(p, "Layout", bn.ColorPaletteLayout, ctx, v => bn.ColorPaletteLayout = v, relayout: true);
            if (bn.ColorPaletteLayout == ColorPaletteLayout.Grid)
            {
                IntRow(p, "Columns", bn.ColorPaletteColumns, ctx, v => bn.ColorPaletteColumns = Mathf.Max(0, v),
                    "Fixed swatches per row. 0 auto-wraps fixed-size squares to the available width.");
                FloatRow(p, "Swatch Spacing", bn.ColorSwatchSpacing, ctx, v => bn.ColorSwatchSpacing = Mathf.Max(0f, v),
                    "Gap around each grid swatch, in pixels. 0 makes them touch — they can never overlap.");
            }
            SliderRow(p, "Swatch Size", bn.ColorSwatchSize, 10f, 80f, ctx, v => bn.ColorSwatchSize = Mathf.Clamp(v, 10f, 80f),
                "Swatch edge length in pixels (grid) or bar height.");
            BoolRow(p, "Copy Hex On Click", bn.ColorCopyOnClick, ctx, v => bn.ColorCopyOnClick = v,
                "Also copy the picked color's hex code to the clipboard when a swatch is clicked.");
            BoolRow(p, "Show Color Bar", bn.ColorShowBar, ctx, v => bn.ColorShowBar = v,
                "Show a standard color bar + picker for the current value alongside the swatches.");

            // HDR / alpha authoring (mirrors [ColorUsage]) — only meaningful for a true float Color, not a Color32.
            if (ctx.FieldType == typeof(Color))
            {
                EnumRow(p, "Color Mode", bn.ColorFieldMode, ctx, v => bn.ColorFieldMode = v);
                BoolRow(p, "Show Alpha", bn.ColorShowAlpha, ctx, v => bn.ColorShowAlpha = v,
                    "Whether the alpha channel is editable in the color picker.");
            }
        }

        private static void BuildRating(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            SliderIntRow(p, "Pips", bn.RatingCount, 1, 10, ctx, v => bn.RatingCount = Mathf.Clamp(v, 1, 10),
                "How many rating pips to show (1-10).");
            EnumRow(p, "Icon", bn.RatingStyle, ctx, v => bn.RatingStyle = v);
            SliderRow(p, "Pip Size", bn.RatingPipSize, 8f, 48f, ctx, v => bn.RatingPipSize = Mathf.Clamp(v, 8f, 48f),
                "Glyph size of each pip in pixels (8-48).");
            BoolRow(p, "Animate", bn.RatingAnimate, ctx, v => bn.RatingAnimate = v,
                "Play the hover/press scale spring on pips.");
        }

        private static void BuildXYPad(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            BoolRow(p, "Editable Fields", bn.XYPadEditableFields, ctx, v => bn.XYPadEditableFields = v,
                "Show editable X/Y number fields under the pad (default off = a compact read-only readout).");
            // Grid + snap (colors stay in the designer, like the other widgets' color pickers).
            BoolRow(p, "Show Grid", bn.XYPadShowGrid, ctx, v => bn.XYPadShowGrid = v,
                "Draw a value grid across the pad, with a center crosshair anchor. Off = a clean pad with just the handle.", relayout: true);
            BoolRow(p, "Snap To Grid", bn.XYPadSnapToGrid, ctx, v => bn.XYPadSnapToGrid = v,
                "Snap a dragged handle onto the grid lines — clean multiples of Grid Size.", relayout: true);
            if (bn.XYPadShowGrid || bn.XYPadSnapToGrid)
                FloatRow(p, "Grid Size", bn.XYPadGridSize, ctx,
                    v => bn.XYPadGridSize = Mathf.Clamp(v, 0.001f, 1000f),
                    "Grid line spacing in value units; Snap To Grid lands the handle on these lines (smaller = finer).");
            BoolRow(p, "Handle Crosshair", bn.XYPadHandleCrosshair, ctx, v => bn.XYPadHandleCrosshair = v,
                "Draw full-width crosshair lines through the handle that follow it as you drag (default off).", relayout: true);
            if (bn.XYPadHandleCrosshair)
                FloatRow(p, "Crosshair Thickness", bn.XYPadCrosshairThickness, ctx,
                    v => bn.XYPadCrosshairThickness = Mathf.Clamp(v, 0.5f, 8f),
                    "Line thickness of the handle crosshair, in pixels (color lives in the designer, with the pad's other colors).");
        }

        private static void BuildVectorSliders(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Per-axis range (defaults -1..1); still honors a [Range] attribute when unset in the widget.
            AddOverridableFloatRow(p, ctx, bn, "Range Min", () => bn.RangeMin, of => bn.RangeMin = of, codeDefault: -1f);
            AddOverridableFloatRow(p, ctx, bn, "Range Max", () => bn.RangeMax, of => bn.RangeMax = of, codeDefault: 1f);

            // Show Normalize reveals the Normalize button's own appearance options.
            BoolRow(p, "Show Normalize", bn.VectorShowNormalize, ctx, v => bn.VectorShowNormalize = v,
                "Show the Normalize button that rescales the vector to unit length.", relayout: true);
            if (bn.VectorShowNormalize)
            {
                EnumRow(p, "Button Style", bn.NormalizeButtonStyle, ctx, v => bn.NormalizeButtonStyle = v);
                ButtonSizeRow(p, "Size", bn.NormalizeButtonSize, ctx, v => bn.NormalizeButtonSize = v);
                EnumRow(p, "Hover Animation", bn.NormalizeButtonHoverAnimation, ctx, v => bn.NormalizeButtonHoverAnimation = v);
            }

            AddAxisPips(p, ctx, bn);
        }

        /// <summary>
        /// The "Axis Color Pips" toggle, shared by the Default and Vector Sliders widgets. Gated on
        /// <see cref="AxisPips.AppliesTo"/> — the same authority the designer panel's row uses, so the
        /// popup can never offer (or hide) an option the designer disagrees about.
        /// </summary>
        private static void AddAxisPips(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            if (!AxisPips.AppliesTo(bn.DisplayType, ctx.FieldType)) return;
            BoolRow(p, "Axis Color Pips", bn.VectorAxisPips, ctx, v => bn.VectorAxisPips = v,
                "Tint a small pip inside each component field — on Vector Sliders, each axis value box — with its axis color (X red, Y green, Z blue; W neutral).");
        }

        // ---------------------------------------------------------------------------------------------------
        // Shared sub-option helpers (used by more than one type)
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The prefix/suffix rows. Affixes apply to EVERY Display Type, so these live in the popup's GENERAL
        /// block (next to Label / Tooltip / Read-Only) rather than in any per-type arm. Only text and placement
        /// are offered here — icon, pill and text styling stay in the designer, like every other heavy picker.
        /// A placement row appears once its side has content; committing text rebuilds the popup, so it shows
        /// up as soon as you type something. Rows the widget cannot honour are HIDDEN, not disabled — see
        /// <see cref="FieldAffixes.CapabilitiesFor"/>, which the designer panel asks the same question of.
        /// </summary>
        internal static void BuildAffixRows(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            // Same capability query the designer asks. Quick edit must not become a back door to a control
            // the designer deliberately hides, or the popup happily stores a setting nothing will honour.
            var caps = FieldAffixes.CapabilitiesFor(bn, ctx.FieldType);
            if (!caps.Any || !caps.Text) return;

            TextRow(p, "Prefix", bn.PrefixAffix.text, ctx,
                v => { var a = bn.PrefixAffix; a.text = v; bn.PrefixAffix = a; },
                "Text shown before the value (\"$\"). Spacing is automatic.", relayoutOnEmptinessFlip: true);
            if (!bn.PrefixAffix.IsEmpty && caps.Placement && caps.InsideMount)
                EnumRow(p, "Prefix Placement", bn.PrefixAffix.mount, ctx,
                    v => { var a = bn.PrefixAffix; a.mount = v; bn.PrefixAffix = a; });

            TextRow(p, "Suffix", bn.SuffixAffix.text, ctx,
                v => { var a = bn.SuffixAffix; a.text = v; bn.SuffixAffix = a; },
                "Text shown after the value (\"kg\", \"m/s\", \"%\"). Spacing is automatic.", relayoutOnEmptinessFlip: true);
            if (!bn.SuffixAffix.IsEmpty && caps.Placement && caps.InsideMount)
                EnumRow(p, "Suffix Placement", bn.SuffixAffix.mount, ctx,
                    v => { var a = bn.SuffixAffix; a.mount = v; bn.SuffixAffix = a; });
        }

        /// <summary>
        /// The "Create Asset Button" toggle shared by the Default / InlineEditor / AssetPreview widgets. Shown
        /// only for a ScriptableObject-reference field (or when the field type is unknown — the widget itself only
        /// surfaces the button at runtime for a null, creatable SO reference, so it stays harmless either way).
        /// </summary>
        private static void AddCreateAssetButton(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn)
        {
            bool applies = ctx.FieldType == null || typeof(ScriptableObject).IsAssignableFrom(ctx.FieldType);
            if (!applies) return;
            BoolRow(p, "Create Asset Button", bn.CreateAssetEnabled, ctx, v => bn.CreateAssetEnabled = v,
                "While this ScriptableObject reference is empty, show a Create button that makes and assigns a new asset of the field's type.");
        }

        /// <summary>
        /// Emits an <see cref="OverridableFloatRow"/> whose backing <see cref="OverridableFloat"/> lives on the
        /// node (directly or on the <see cref="DisplayStyleData"/> struct). The <paramref name="apply"/> delegate
        /// owns the (struct copy-mutate-reassign) write; the whole write runs inside <c>ctx.Commit</c>.
        /// </summary>
        private static void AddOverridableFloatRow(VisualElement p, QuickEditOptionsContext ctx, BindingNode bn,
            string label, Func<OverridableFloat> get, Action<OverridableFloat> apply, float codeDefault)
        {
            var row = OverridableFloatRow.Create(label, get(), codeDefault,
                of => ctx.Commit(() => apply(of)));
            p.Add(row);
        }

        // ---------------------------------------------------------------------------------------------------
        // Compact row primitives — each wired to ctx.Commit ("commit owns persistence") + optional relayout.
        // ---------------------------------------------------------------------------------------------------

        /// <summary>A boolean <see cref="Toggle"/> row. Pass <paramref name="relayout"/> when flipping it adds/removes other rows.</summary>
        private static Toggle BoolRow(VisualElement parent, string label, bool value, QuickEditOptionsContext ctx,
            Action<bool> set, string tip = null, bool relayout = false)
        {
            var t = new Toggle(label) { value = value };
            if (!string.IsNullOrEmpty(tip)) t.tooltip = tip;
            t.RegisterValueChangedCallback(e =>
            {
                ctx.Commit(() => set(e.newValue));
                if (relayout) ctx.RequestPopupRelayout?.Invoke();
            });
            parent.Add(t);
            return t;
        }

        /// <summary>An enum <see cref="PopupField{T}"/> row (all values, nicified labels). Pass <paramref name="relayout"/> when the choice adds/removes other rows.</summary>
        private static PopupField<T> EnumRow<T>(VisualElement parent, string label, T value, QuickEditOptionsContext ctx,
            Action<T> set, bool relayout = false) where T : struct, Enum
        {
            var choices = Enum.GetValues(typeof(T)).Cast<T>().ToList();
            Func<T, string> fmt = v => ObjectNames.NicifyVariableName(v.ToString());
            var f = new PopupField<T>(label, choices, value, fmt, fmt);
            f.RegisterValueChangedCallback(e =>
            {
                ctx.Commit(() => set(e.newValue));
                if (relayout) ctx.RequestPopupRelayout?.Invoke();
            });
            parent.Add(f);
            return f;
        }

        /// <summary>
        /// The <see cref="ButtonSize"/> flavour of <see cref="EnumRow{T}"/>: the sizes in VISUAL order
        /// (Small → Huge), each naming the height it renders at. <c>EnumRow</c> would list them in the enum's
        /// serialization-fixed order — Medium, Small, Large, Huge — which reads as noise on a size picker.
        /// </summary>
        private static PopupField<ButtonSize> ButtonSizeRow(VisualElement parent, string label, ButtonSize value,
            QuickEditOptionsContext ctx, Action<ButtonSize> set)
        {
            var choices = new List<ButtonSize>(LayoutTreeBuilder.ButtonSizeOrder);
            var f = new PopupField<ButtonSize>(label, choices, Mathf.Max(0, choices.IndexOf(value)),
                LayoutTreeBuilder.ButtonSizeLabel, LayoutTreeBuilder.ButtonSizeLabel);
            f.RegisterValueChangedCallback(e => ctx.Commit(() => set(e.newValue)));
            parent.Add(f);
            return f;
        }

        /// <summary>A <see cref="FloatField"/> row (delayed). Apply any clamp inside <paramref name="set"/>.</summary>
        private static FloatField FloatRow(VisualElement parent, string label, float value, QuickEditOptionsContext ctx,
            Action<float> set, string tip = null)
        {
            var f = new FloatField(label) { value = value, isDelayed = true };
            if (!string.IsNullOrEmpty(tip)) f.tooltip = tip;
            f.RegisterValueChangedCallback(e => ctx.Commit(() => set(e.newValue)));
            parent.Add(f);
            return f;
        }

        /// <summary>An <see cref="IntegerField"/> row (delayed). Apply any clamp inside <paramref name="set"/>.</summary>
        private static IntegerField IntRow(VisualElement parent, string label, int value, QuickEditOptionsContext ctx,
            Action<int> set, string tip = null)
        {
            var f = new IntegerField(label) { value = value, isDelayed = true };
            if (!string.IsNullOrEmpty(tip)) f.tooltip = tip;
            f.RegisterValueChangedCallback(e => ctx.Commit(() => set(e.newValue)));
            parent.Add(f);
            return f;
        }

        // A drag-release/typing-pause debounce for the slider rows: well above a frame interval, so a continuous drag
        // never commits mid-stroke (the whole drag stays a single undo step) while a pause/release lands the commit.
        private const long SliderCommitDebounceMs = 120;

        /// <summary>
        /// A <see cref="Slider"/> row for a CLAMPED [<paramref name="min"/>..<paramref name="max"/>] value — the slider
        /// analog of <see cref="FloatRow"/>, used wherever a value has a real upper bound so it reads as a slider (with
        /// a typed input box) instead of a bare number box. Commits ONCE per adjustment via a debounce (drag-release /
        /// typing-pause), so a drag is a single undo step; blur + detach flush any pending edit. Clamp inside <paramref name="set"/>.
        /// </summary>
        private static Slider SliderRow(VisualElement parent, string label, float value, float min, float max,
            QuickEditOptionsContext ctx, Action<float> set, string tip = null)
        {
            var s = new Slider(label, min, max) { value = Mathf.Clamp(value, min, max), showInputField = true };
            if (!string.IsNullOrEmpty(tip)) s.tooltip = tip;

            float committed = Mathf.Clamp(value, min, max);
            IVisualElementScheduledItem debounce = null;
            void CommitNow()
            {
                if (Mathf.Approximately(s.value, committed)) return;
                committed = s.value;
                ctx.Commit(() => set(s.value));
            }
            s.RegisterValueChangedCallback(_ =>
            {
                debounce?.Pause();
                debounce = s.schedule.Execute(CommitNow);
                debounce.ExecuteLater(SliderCommitDebounceMs);
            });
            s.RegisterCallback<FocusOutEvent>(_ => { debounce?.Pause(); CommitNow(); }, TrickleDown.TrickleDown);
            s.RegisterCallback<DetachFromPanelEvent>(_ => { debounce?.Pause(); CommitNow(); });
            parent.Add(s);
            return s;
        }

        /// <summary>A <see cref="SliderInt"/> row for a CLAMPED [<paramref name="min"/>..<paramref name="max"/>] value (typed input; debounced commit) — the slider analog of <see cref="IntRow"/>.</summary>
        private static SliderInt SliderIntRow(VisualElement parent, string label, int value, int min, int max,
            QuickEditOptionsContext ctx, Action<int> set, string tip = null)
        {
            var s = new SliderInt(label, min, max) { value = Mathf.Clamp(value, min, max), showInputField = true };
            if (!string.IsNullOrEmpty(tip)) s.tooltip = tip;

            int committed = Mathf.Clamp(value, min, max);
            IVisualElementScheduledItem debounce = null;
            void CommitNow()
            {
                if (s.value == committed) return;
                committed = s.value;
                ctx.Commit(() => set(s.value));
            }
            s.RegisterValueChangedCallback(_ =>
            {
                debounce?.Pause();
                debounce = s.schedule.Execute(CommitNow);
                debounce.ExecuteLater(SliderCommitDebounceMs);
            });
            s.RegisterCallback<FocusOutEvent>(_ => { debounce?.Pause(); CommitNow(); }, TrickleDown.TrickleDown);
            s.RegisterCallback<DetachFromPanelEvent>(_ => { debounce?.Pause(); CommitNow(); });
            parent.Add(s);
            return s;
        }

        /// <summary>A <see cref="TextField"/> row (delayed — commits on Enter/blur, not per keystroke).</summary>
        private static TextField TextRow(VisualElement parent, string label, string value, QuickEditOptionsContext ctx,
            Action<string> set, string tip = null, bool relayoutOnEmptinessFlip = false)
        {
            var f = new TextField(label) { value = value, isDelayed = true };
            if (!string.IsNullOrEmpty(tip)) f.tooltip = tip;
            f.RegisterValueChangedCallback(e =>
            {
                ctx.Commit(() => set(e.newValue));
                // Only when the row goes empty -> non-empty (or back). ctx.Commit rebuilds the INSPECTOR, not
                // the popup, so a dependent row that appears "once its side has content" never materialised
                // until the popup was closed and reopened. Gated on the flip so a second keystroke in an
                // already-filled field does not re-spring the window under the user's cursor.
                if (relayoutOnEmptinessFlip
                    && string.IsNullOrEmpty(e.previousValue) != string.IsNullOrEmpty(e.newValue))
                {
                    ctx.RequestPopupRelayout?.Invoke();
                }
            });
            parent.Add(f);
            return f;
        }

        /// <summary>
        /// A segmented icon-button placement/alignment picker — the SAME look the designer's Properties panel uses
        /// for placement rows (<c>BuildPlacementButtonRow</c> / <c>BuildImageAlignmentRow</c>): a labelled row of
        /// 24×18 icon buttons where the active option lights up (accent fill, white icon). Nicer than a dropdown for
        /// a small directional choice. Commits through <paramref name="ctx"/>; no ↺ reset (the popup stays compact).
        /// </summary>
        private static VisualElement PlacementRow<TEnum>(VisualElement parent, string label, QuickEditOptionsContext ctx,
            Func<TEnum> get, Action<TEnum> set, (string icon, TEnum value, string tip)[] options) where TEnum : struct, Enum
        {
            var row = new VisualElement();
            row.AddToClassList("unity-base-field");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            var lbl = new Label(label);
            lbl.AddToClassList("unity-base-field__label");
            row.Add(lbl);

            var buttons = new VisualElement();
            buttons.AddToClassList("unity-base-field__input");
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.Center;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.flexGrow = 1;

            var btns = new List<Button>();
            void Sync()
            {
                for (int i = 0; i < options.Length; i++)
                {
                    bool active = EqualityComparer<TEnum>.Default.Equals(get(), options[i].value);
                    btns[i].style.backgroundColor = active ? DesignerPalette.AccentFillSoft : DesignerPalette.ButtonNeutralBg;
                    var icon = btns[i].Q<SasquatchIconElement>();
                    if (icon != null) icon.Tint = active ? Color.white : DesignerPalette.IconIdle;
                }
            }
            foreach (var opt in options)
            {
                var captured = opt.value;
                var btn = new Button();
                btn.Add(SasquatchIcons.Create(opt.icon, 11, DesignerPalette.IconIdle));
                btn.tooltip = opt.tip;
                btn.style.width = 24; btn.style.height = 18;
                btn.style.paddingLeft = 0; btn.style.paddingRight = 0;
                btn.style.marginLeft = 2; btn.style.marginRight = 0;
                btn.style.alignItems = Align.Center;
                btn.style.justifyContent = Justify.Center;
                btn.clicked += () => { ctx.Commit(() => set(captured)); Sync(); };
                SegmentedHover(btn, () => EqualityComparer<TEnum>.Default.Equals(get(), captured), Sync);
                btns.Add(btn);
                buttons.Add(btn);
            }
            row.Add(buttons);
            Sync();
            parent.Add(row);
            return row;
        }

        // The designer's segmented-button hover feel (SasquatchLayoutDesignerWindow.AddSegmentedHover), replicated
        // here (that one is private to the designer): tint on enter by active-state, restore on leave.
        private static void SegmentedHover(Button btn, Func<bool> isActive, Action restore)
        {
            btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.backgroundColor = isActive()
                ? DesignerPalette.SegmentHoverActiveBg : DesignerPalette.SegmentHoverIdleBg);
            btn.RegisterCallback<MouseLeaveEvent>(_ => restore());
        }
    }
}
