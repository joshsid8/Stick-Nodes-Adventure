using System;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    // Hand-built layout trees for each Showcase category. Every public method returns a fresh LayoutNode root
    // (a Rows container opening with a "Script Header" bar) which the window wraps in a transient
    // InspectorLayoutAsset and renders via LayoutTreeBuilder.Build. Parent pages compose the same per-feature
    // node builders the focused child pages use, so the two never drift. Property paths must match the matching
    // ShowcaseXTarget field names.
    internal static class ShowcaseLayouts
    {
        // ============================================================ Common Fields ===

        // The all-in-one page: every child page's content, composed from the SAME node builders the children
        // use, so the two can never drift. A child that shows a whole comparison (the affix page, the label
        // page) contributes its own card here rather than being flattened into the big one.
        public static LayoutNode CommonFields() => HeaderRoot(
            // Read-Only sits on its own at the top: it is a property of a ROW rather than a widget, so
            // pairing it with anything reads as "this widget is the read-only one".
            Card("Read-Only", ReadOnlyField()),
            ExpandableCard("Common Fields", true, ProgressBarField(), StepperField(), RoundedToggleField(),
                TextField(), TextFixedHeightField(),
                // The two token drivers sit above the rich-text box here for the same reason they do on the
                // Rich Text child page: without them the {runeCount} / {heroName} tokens in the live preview
                // are text nobody can move, which is the entire point of the feature.
                RuneCountField(), HeroNameField(), RichTextField(),
                MinMaxField(), RatingField(), XYPadField(),
                VectorSlidersField(), AxisPipsField(), FilePathField(), GradientField(), CurveField(),
                // Label Above and the "?" chip are row options, not widgets — one example of each is the
                // whole point; a second only repeats it.
                LabelAboveVectorField(), HelpChipField()),
            ExpandableCard("Prefix & Suffix", true,
                AffixPriceField(), AffixWeightField(), AffixIconField(), AffixPillField(),
                AffixPillPairField(), AffixPillIconField(), AffixTextField(),
                AffixVectorField(), AffixVectorOnceField()),
            Card("Computed Property", ComputedField(), ComputedNote()));

        public static LayoutNode CommonFields_ScriptHeader() => HeaderRoot();
        public static LayoutNode CommonFields_ProgressBar() => Root(ProgressBarField());
        public static LayoutNode CommonFields_Stepper() => Root(StepperField());
        public static LayoutNode CommonFields_RoundedToggle() => Root(RoundedToggleField());
        public static LayoutNode CommonFields_Text() => Root(TextField(), TextFixedHeightField());
        // The driver fields sit ABOVE the rich-text box on purpose: edit either and the {token} in the live
        // preview updates as you type, which is the whole point of the feature.
        public static LayoutNode CommonFields_RichText() => Root(RuneCountField(), HeroNameField(), RichTextField());
        public static LayoutNode CommonFields_FilePath() => Root(FilePathField());
        public static LayoutNode CommonFields_MinMax() => Root(MinMaxField());
        public static LayoutNode CommonFields_Rating() => Root(RatingField());
        public static LayoutNode CommonFields_XYPad() => Root(XYPadField());
        public static LayoutNode CommonFields_VectorSliders() => Root(VectorSlidersField());
        public static LayoutNode CommonFields_AxisPips() => Root(AxisPipsField(), VectorSlidersField());
        // Three compact cards rather than one long run: placement, pills/icons, and the composite / read-out
        // cases. Dense on purpose — the point of the page is that one glance says "these are fully
        // styleable", which a handful of grey "kg"s never manages to say.
        public static LayoutNode CommonFields_UnitSuffix() => Root(
            Card("1 — Where it sits",
                AffixPriceField(), AffixWeightField(), AffixIconField(), AffixPillField()),
            Card("2 — Pills, icons & any field",
                AffixPillPairField(), AffixPillIconField(), AffixTextField()),
            Card("3 — Composites & read-outs",
                AffixVectorField(), AffixVectorOnceField(),
                SuffixSliderField(), SuffixProgressField(), SuffixMinMaxField()));
        public static LayoutNode CommonFields_Gradient() => Root(GradientField());
        public static LayoutNode CommonFields_Curve() => Root(CurveField());
        public static LayoutNode CommonFields_Computed() => Root(ComputedField(), ComputedNote());

        // Where a field's LABEL goes, and whether it is editable at all. Four one-row cards so each option
        // is compared against the standard row directly above it.
        public static LayoutNode CommonFields_LabelPlacement() => Root(
            Card("Standard — the label sits in the inspector's left column", Bind("heroName")),
            Card("Label Above Field — the label takes its own row and the control gets the full width",
                LabelAboveTextField(), LabelAboveVectorField()),
            Card("No label — for a field its container already names", HiddenLabelField()),
            Card("Read-Only — shown, greyed, not editable", ReadOnlyField()));

        // The per-field "?" chip: a markdown help card that stays out of the way until it's wanted.
        public static LayoutNode CommonFields_HelpChip() => Root(
            Card("Help chip", HelpChipField()));

        // The field search lives in the Script Header, so this page opens with one — and carries enough rows
        // that filtering is worth doing. Reuses the same field builders as the parent page.
        public static LayoutNode CommonFields_FieldSearch() => HeaderRoot(
            Card("Type in the magnifier to filter these", ProgressBarField(), RoundedToggleField(),
                MinMaxField(), RatingField(), XYPadField(), VectorSlidersField(), GradientField(), CurveField()));

        private static BindingNode ProgressBarField() => new BindingNode
        {
            PropertyPath = "health", DisplayType = DisplayType.ProgressBar, Header = "Progress Bar",
            ProgressBarTextMode = ProgressBarTextMode.ValueAndMax, UnitSuffix = " HP",
            ProgressBarThickness = 24f, ProgressBarValueFill = true
        };

        // The Numeric Stepper is an option on the DEFAULT display type, not a widget of its own: −/+ buttons
        // wrapped around the value, stepping by Step Size at the field's native precision.
        private static BindingNode StepperField()
        {
            var b = new BindingNode { PropertyPath = "waveSize", DisplayType = DisplayType.Default, Header = "Numeric Stepper" };
            b.ShowStepper = true;
            b.StepSize = 1f;
            b.StepperPlacement = StepperPlacement.Split; // one button each side of the value
            b.StepperButtonStyle = ButtonStyle.Soft;
            b.StepperButtonHoverAnimation = ButtonHoverAnimation.Pop;
            return b;
        }

        private static BindingNode RoundedToggleField()
        {
            var b = new BindingNode { PropertyPath = "invincible", DisplayType = DisplayType.Toggle, Header = "Rounded Toggle" };
            b.DisplayStyleData = new DisplayStyleData { toggleStyle = ToggleStyle.Rounded, toggleShowOnOff = true };
            return b;
        }

        // Resizable + an authored Box Height: the layout seeds the height, and the drag grip in the box's
        // bottom-right writes the same value back. On Fixed, Box Height is the ONLY height control.
        private static BindingNode TextField()
        {
            var b = new BindingNode { PropertyPath = "notes", DisplayType = DisplayType.Text, Header = "Text (multi-line)" };
            b.TextResize = TextResizeMode.Resizable;
            b.TextBoxHeight = 72f;
            return b;
        }

        // The same string with resizing OFF, so the only way to set its height is the Box Height number.
        private static BindingNode TextFixedHeightField()
        {
            var b = new BindingNode
            {
                PropertyPath = "notes", DisplayType = DisplayType.Text, LabelOverride = "Fixed height, no grip"
            };
            b.TextResize = TextResizeMode.Fixed;
            b.TextBoxHeight = 44f;
            return b;
        }

        private static BindingNode RichTextField()
        {
            // Rich text is a Text display-type option: a resizable text box PLUS the live formatted preview +
            // toolbar. The stored string also carries {runeCount} / {heroName} tokens, so the preview shows
            // those fields' LIVE values (type "{" in the box for the autocomplete + code peek).
            var b = new BindingNode
            {
                PropertyPath = "questReward", DisplayType = DisplayType.Text, Header = "Rich Text (live preview)",
                RichTextPreview = true, RichTextToolbar = true
            };
            b.TextResize = TextResizeMode.Resizable;
            return b;
        }

        // The two fields the rich-text tokens read. Plain rows — the point is that editing them moves the preview.
        private static BindingNode RuneCountField() => new BindingNode
        {
            PropertyPath = "runeCount", DisplayType = DisplayType.Default, Header = "Rune Count"
        };
        private static BindingNode HeroNameField() => new BindingNode
        {
            PropertyPath = "heroName", DisplayType = DisplayType.Default, Header = "Hero Name"
        };

        // A plain Vector3 with Axis Color Pips: each component field carries a pip in its handle color
        // (X red, Y green, Z blue) so you can read an axis at a glance without counting boxes.
        private static BindingNode AxisPipsField() => new BindingNode
        {
            PropertyPath = "spawnOffset", DisplayType = DisplayType.Default, Header = "Axis Color Pips",
            VectorAxisPips = true
        };

        // Path input mode: the text box gains a Browse button + a picker filtered to these extensions, and a
        // placeholder greys over the empty field.
        private static BindingNode FilePathField() => new BindingNode
        {
            PropertyPath = "savePath", DisplayType = DisplayType.Text, Header = "File Path",
            TextPathMode = TextPathMode.Path, PathFilter = "json,txt",
            TextPlaceholder = "Browse for a save file…"
        };

        private static BindingNode MinMaxField()
        {
            var b = new BindingNode { PropertyPath = "damageRange", DisplayType = DisplayType.MinMaxSlider, Header = "Min-Max Slider" };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 100f;
            // Shown/editable value read-outs are the defaults (both flags are stored inverted), so only the
            // placement needs stating here.
            b.DisplayStyleData = new DisplayStyleData { sliderFieldPlacement = SliderFieldPlacement.Below };
            return b;
        }

        private static BindingNode RatingField()
        {
            var b = new BindingNode { PropertyPath = "stars", DisplayType = DisplayType.Rating, Header = "Rating" };
            b.RatingStyle = RatingIconStyle.Stars;
            b.RatingCount = 5;
            return b;
        }

        private static BindingNode XYPadField()
        {
            var b = new BindingNode { PropertyPath = "aim", DisplayType = DisplayType.XYPad, Header = "XY Pad" };
            b.XYPadEditableFields = true;
            // Grid + snap + a handle crosshair: the handle lands on clean multiples of Grid Size and the
            // crosshair follows it, so the pad reads as a real coordinate space rather than a blob.
            b.XYPadShowGrid = true;
            b.XYPadSnapToGrid = true;
            b.XYPadGridSize = 0.1f;
            b.XYPadHandleCrosshair = true;
            return b;
        }

        private static BindingNode VectorSlidersField()
        {
            var b = new BindingNode { PropertyPath = "windForce", DisplayType = DisplayType.VectorSliders, Header = "Vector Sliders" };
            b.VectorShowNormalize = true;
            b.VectorNormalizeAlignment = ImageAlignment.Center;
            return b;
        }

        // --- Label placement / read-only / help chip / color modes ------------------------------------

        // Label Above Field: the label lifts onto its own row, so the control gets the whole width. Worth
        // it for anything wide — a long string, or a vector whose three boxes were being squeezed.
        private static BindingNode LabelAboveTextField() => new BindingNode
        {
            PropertyPath = "heroName", LabelOverride = "Hero Name", LabelAbove = true
        };
        private static BindingNode LabelAboveVectorField() => new BindingNode
        {
            PropertyPath = "windForce", LabelOverride = "Wind Force", LabelAbove = true
        };

        // Clearing the Label row in the designer stores the hidden sentinel — the label is suppressed and the
        // control takes the full row. The card's own title is what names it here.
        private static BindingNode HiddenLabelField() => new BindingNode
        {
            PropertyPath = "heroName", LabelOverride = LayoutNode.HiddenSentinel
        };

        // Read-Only draws the real widget and disables it, so the value still reads as a value.
        private static BindingNode ReadOnlyField() => new BindingNode
        {
            PropertyPath = "price", LabelOverride = "Base Price", IsReadOnly = true,
            SuffixAffix = Affix("USD", mount: AffixMount.InsideField)
        };

        // The help chip is two settings: turn it on, and give it text. The card is markdown, like a Help Box.
        private static BindingNode HelpChipField() => new BindingNode
        {
            PropertyPath = "health", DisplayType = DisplayType.Slider, LabelOverride = "Health",
            ShowHelpChip = true,
            HelpText = "**Health** is the pool damage comes out of.\n\n" +
                       "· `0` kills the actor immediately\n" +
                       "· Anything above **100** is clamped on spawn\n\n" +
                       "Set it per difficulty in the {Color|balance table}."
        };

        // Affix demos. Prefixes and suffixes work on EVERY display type, so the page leads with the plain
        // default field (where the feature used to be unavailable entirely) before the three numeric widgets
        // that mount their affixes around their own value read-outs.
        private static FieldAffix Affix(string text = null, string icon = null,
                                        AffixMount mount = AffixMount.Auto, Color? pill = null,
                                        Color? ink = null, FontStyle? fontStyle = null, float? size = null,
                                        float? gap = null)
        {
            var a = new FieldAffix { text = text, iconId = icon, mount = mount };
            if (pill.HasValue) a.pillColor = OverColor(pill.Value);
            if (gap.HasValue) a.gap = OverFloat(gap.Value);
            // The affix carries its OWN TextStyle, independent of the field's label style — so a unit can be
            // loud (a red bold "HP") without dragging the row's label with it.
            if (ink.HasValue) a.style.color = OverColor(ink.Value);
            if (fontStyle.HasValue) a.style.fontStyle = OverEnum(fontStyle.Value);
            if (size.HasValue) a.style.fontSize = OverFloat(size.Value);
            return a;
        }

        // --- 1: where it sits -------------------------------------------------------------------------

        private static BindingNode AffixPriceField() => new BindingNode
        {
            PropertyPath = "price", Header = "Inside the box",
            PrefixAffix = Affix("$", mount: AffixMount.InsideField),
            SuffixAffix = Affix("USD", mount: AffixMount.InsideField),
        };

        private static BindingNode AffixWeightField() => new BindingNode
        {
            PropertyPath = "price", LabelOverride = "Beside the box",
            SuffixAffix = Affix("kg", mount: AffixMount.OutsideField),
        };

        private static BindingNode AffixIconField() => new BindingNode
        {
            PropertyPath = "waveSize", LabelOverride = "Icon, text, or both",
            PrefixAffix = Affix(icon: SasquatchIcons.Ids.Coins),
            SuffixAffix = Affix("each", icon: SasquatchIcons.Ids.Zap),
        };

        private static BindingNode AffixPillField() => new BindingNode
        {
            PropertyPath = "price", LabelOverride = "On a pill",
            SuffixAffix = Affix("USD", mount: AffixMount.OutsideField, pill: new Color(0.2f, 0.5f, 0.9f, 0.35f)),
        };

        // --- 2: pills, icons & any field ----------------------------------------------------------------

        // A pill on BOTH sides, each its own colour — a min/max read that needs no extra label row.
        private static BindingNode AffixPillPairField() => new BindingNode
        {
            PropertyPath = "charge", LabelOverride = "Pill both sides",
            PrefixAffix = Affix("min", mount: AffixMount.InsideField, ink: Color.white,
                                pill: new Color(0.85f, 0.28f, 0.30f, 0.50f)),
            SuffixAffix = Affix("max", mount: AffixMount.InsideField, ink: Color.white,
                                pill: new Color(0.28f, 0.68f, 0.42f, 0.50f)),
        };

        // The pill wraps whatever the affix renders, so an ICON gets one too — not just text.
        private static BindingNode AffixPillIconField() => new BindingNode
        {
            PropertyPath = "price", LabelOverride = "Pill + icon inside",
            PrefixAffix = Affix(icon: SasquatchIcons.Ids.Coins, mount: AffixMount.InsideField,
                                pill: new Color(0.95f, 0.74f, 0.25f, 0.40f)),
        };

        // An affix is NOT a numeric feature — a plain string field takes one too, styled the same way.
        private static BindingNode AffixTextField() => new BindingNode
        {
            PropertyPath = "heroName", LabelOverride = "On a text field",
            PrefixAffix = Affix("@", mount: AffixMount.InsideField, ink: new Color(0.55f, 0.72f, 1f),
                                fontStyle: FontStyle.Bold),
        };

        // --- 3: composites & read-outs ----------------------------------------------------------------

        // The default on a composite (Vector2/3/4, Rect, Bounds): the affix repeats on EVERY component box.
        private static BindingNode AffixVectorField() => new BindingNode
        {
            PropertyPath = "velocity", LabelOverride = "Once per axis",
            SuffixAffix = Affix("m/s"),
        };

        // The same field with Per-Component OFF: one affix at the end of the whole row instead.
        private static BindingNode AffixVectorOnceField() => new BindingNode
        {
            PropertyPath = "velocity", LabelOverride = "Once for the row",
            SuffixAffix = Affix("m/s"), AffixPerComponent = false,
        };

        // The three numeric widgets that carry their affixes around their own value read-outs.
        private static BindingNode SuffixSliderField() => new BindingNode
        {
            PropertyPath = "health", DisplayType = DisplayType.Slider, Header = "Slider", UnitSuffix = " HP"
        };

        private static BindingNode SuffixProgressField() => new BindingNode
        {
            PropertyPath = "health", DisplayType = DisplayType.ProgressBar, Header = "Progress Bar",
            ProgressBarTextMode = ProgressBarTextMode.ValueAndMax, UnitSuffix = " HP"
        };

        private static BindingNode SuffixMinMaxField()
        {
            var b = new BindingNode
            {
                PropertyPath = "damageRange", DisplayType = DisplayType.MinMaxSlider, Header = "Min-Max Slider",
                UnitSuffix = " dmg"
            };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 100f;
            b.DisplayStyleData = new DisplayStyleData { sliderFieldPlacement = SliderFieldPlacement.Below };
            return b;
        }

        private static BindingNode GradientField() =>
            new BindingNode { PropertyPath = "gradient", DisplayType = DisplayType.Gradient, Header = "Gradient", GradientWellHeight = 42f,
                GradientEditorMode = GradientEditorMode.ExpandableWithPreview, WidgetDefaultToOpen = true };

        private static BindingNode CurveField()
        {
            var b = new BindingNode
            {
                PropertyPath = "curve", DisplayType = DisplayType.AnimationCurveEditor, Header = "Animation Curve",
                CurveWellHeight = 112f, CurveEditorMode = CurveEditorMode.ExpandableWithPreview,
                WidgetDefaultToOpen = true
            };
            // Snap-to-grid lands dragged keys on the drawn lines, and the editable Max-Y field over the graph
            // rescales the value axis without leaving the inspector.
            b.CurveSnapToGrid = true;
            b.CurveGridSize = 0.1f;
            b.CurveEditableMaxY = true;
            return b;
        }

        private static PropertyNode ComputedField() =>
            new PropertyNode { PropertyName = "PowerRating", LabelOverride = "Power Rating" };

        // ============================================================ Dropdowns =======

        public static LayoutNode Dropdowns() => Root(
            Card("Dropdowns & Pickers", EnumField(), EnumBadgeFlagsField(), EnumBadgeSingleField(),
                EnumBadgeStyledField(), TagField(), TagChipsField(), LayerField(), LayerMaskField(), MethodField()));

        public static LayoutNode Dropdowns_Enum() => Root(EnumField());
        public static LayoutNode Dropdowns_EnumBadges() => Root(
            EnumBadgeFlagsField(), EnumBadgeSingleField(), EnumBadgeStyledField());
        public static LayoutNode Dropdowns_Tag() => Root(TagField());
        public static LayoutNode Dropdowns_TagChips() => Root(TagChipsField());
        public static LayoutNode Dropdowns_Layer() => Root(LayerField());
        public static LayoutNode Dropdowns_LayerMask() => Root(LayerMaskField());
        public static LayoutNode Dropdowns_Method() => Root(MethodField());

        private static BindingNode EnumField() => new BindingNode
        {
            PropertyPath = "difficulty", DisplayType = DisplayType.DropDown, Header = "Enum (searchable)",
            DropdownSource = DropdownSource.Auto, SearchableDropdown = true
        };
        // [Flags] enum as a multi-select chip grid (None/All quick chips + a compound "MeleeRanged" flag).
        private static BindingNode EnumBadgeFlagsField() => new BindingNode
        {
            PropertyPath = "abilities", DisplayType = DisplayType.EnumBadgeGrid, Header = "Enum Badges (Flags)"
        };
        // Plain enum as a single-select segmented control.
        private static BindingNode EnumBadgeSingleField() => new BindingNode
        {
            PropertyPath = "difficulty", DisplayType = DisplayType.EnumBadgeGrid, Header = "Enum Badges (single-select)"
        };
        // The grid's own styling: a per-value tint AND icon (keyed by the member's underlying value), each
        // label on a soft dark pill (Text Backdrop), in a FIXED two-column grid. Columns are set explicitly
        // rather than left on auto-wrap so the grid keeps its shape when the pane is resized.
        private static BindingNode EnumBadgeStyledField()
        {
            var b = new BindingNode
            {
                PropertyPath = "difficulty", DisplayType = DisplayType.EnumBadgeGrid,
                Header = "Per-value colors + icons",
                EnumBadgeColumns = 2, EnumBadgeHeight = 26f, EnumBadgeTextBackdrop = true
            };
            b.EnumBadgeOverrides = new[]
            {
                Badge(0, new Color(0.35f, 0.72f, 0.42f), SasquatchIcons.Ids.Shield),   // Easy
                Badge(1, new Color(0.30f, 0.58f, 0.95f), SasquatchIcons.Ids.Swords),   // Normal
                Badge(2, new Color(0.95f, 0.66f, 0.22f), SasquatchIcons.Ids.Flame),    // Hard
                Badge(3, new Color(0.88f, 0.28f, 0.32f), SasquatchIcons.Ids.Zap),      // Nightmare
            };
            return b;
        }
        private static EnumBadgeOverride Badge(int value, Color tint, string icon) =>
            new EnumBadgeOverride { value = value, color = OverColor(tint), iconId = icon };
        private static BindingNode TagField() => new BindingNode
        {
            PropertyPath = "targetTag", DisplayType = DisplayType.DropDown, Header = "Tag", DropdownSource = DropdownSource.Tags
        };
        private static BindingNode TagChipsField() => new BindingNode
        {
            PropertyPath = "keywords", DisplayType = DisplayType.TagChips, Header = "Tag Chips"
        };
        private static BindingNode LayerField() => new BindingNode
        {
            PropertyPath = "targetLayer", DisplayType = DisplayType.DropDown, Header = "Layer", DropdownSource = DropdownSource.Layer
        };
        private static BindingNode LayerMaskField() => new BindingNode
        {
            PropertyPath = "collisionMask", DisplayType = DisplayType.DropDown, Header = "LayerMask",
            DropdownSource = DropdownSource.LayerMask
        };
        private static BindingNode MethodField() => new BindingNode
        {
            PropertyPath = "weaponClass", DisplayType = DisplayType.DropDown, Header = "Method-sourced",
            DropdownSource = DropdownSource.Method, ValueSourceMethod = "WeaponClasses", SearchableDropdown = true
        };

        // ==================================================== Previews & References ===
        // Named for both halves: three of the five pages are asset PREVIEWS, and the other two — In-Line Editor
        // and Create Asset Button — are about object REFERENCES. The manual files In-Line Editor under §3.3
        // "Preview display types" as well.

        public static LayoutNode Previews() => Root(
            Card("Previews & References",
                AssetPreviewField(),
#if SASQ_AUDIO
                AudioPreviewField(),
#endif
#if SASQ_PARTICLES
                ParticlePreviewField(),
#endif
                InlineEditorField(), CreateAssetField()));

        public static LayoutNode Previews_AssetPreview() => Root(AssetPreviewField());
#if SASQ_AUDIO
        public static LayoutNode Previews_AudioPreview() => Root(AudioPreviewField());
#endif
        public static LayoutNode Previews_ParticlePreview() => Root(ParticlePreviewField());
        public static LayoutNode Previews_InlineEditor() => Root(InlineEditorField());
        public static LayoutNode Previews_CreateAsset() => Root(CreateAssetField());

        // The custom inline design for the embedded ShowcaseStatBlock (registered as a session override so the
        // In-Line Editor renders it instead of the default fields): a nicer Script Header, the int value as a
        // progress bar, the name as a resizable text box, and the color via a swatch bar.
        public static LayoutNode StatBlock()
        {
            var root = new ContainerNode { Type = ContainerType.Rows };
            root.Children.Add(new ScriptHeaderNode());
            // NO Header on any of these. `Header` is a [Header]-style SECTION title drawn above the row, not
            // a second name for it — setting it to the field's own display name prints "Value" twice, once
            // as a section heading and once as the row's label, on all three rows. It read as an In-Line
            // Editor bug (reported 2026-09-02 against the Media page) and was only ever this layout.
            root.Children.Add(new BindingNode
            {
                PropertyPath = "value", DisplayType = DisplayType.ProgressBar,
                ProgressBarTextMode = ProgressBarTextMode.ValueAndMax
            });
            root.Children.Add(new BindingNode { PropertyPath = "statName", DisplayType = DisplayType.Text });
            root.Children.Add(new BindingNode
            {
                PropertyPath = "barColor", DisplayType = DisplayType.ColorPalette,
                ColorPaletteLayout = ColorPaletteLayout.Bar, ColorBarPlacement = ColorBarPlacement.Below
            });
            return root;
        }

        private static BindingNode AssetPreviewField() =>
            new BindingNode { PropertyPath = "previewMesh", DisplayType = DisplayType.AssetPreview, Header = "Asset Preview",
                AssetPreviewMode = AssetPreviewMode.ThumbnailOnly, PreviewSize = 128f };

#if SASQ_AUDIO
        private static BindingNode AudioPreviewField() => new BindingNode
        {
            PropertyPath = "sfx", DisplayType = DisplayType.AudioPreview, Header = "Audio Preview",
            AudioPreviewMode = AudioPreviewMode.ShowSoundwaves, AudioWaveformPlacement = ElementPlacement.Below
        };
#endif

        private static BindingNode ParticlePreviewField() => new BindingNode
        {
            PropertyPath = "vfxPrefab", DisplayType = DisplayType.ParticleSystemPreview, Header = "Particle Preview",
            ParticlePreviewMode = ParticlePreviewMode.Expandable, ParticleDefaultToOpen = true, ParticleThumbnailSize = 180f
        };

        private static BindingNode InlineEditorField() => new BindingNode
        {
            PropertyPath = "statBlock", DisplayType = DisplayType.InlineEditor, Header = "In-Line Editor",
            Expandable = true, DefaultToOpen = true
        };

        // Bound to the deliberately-unassigned newBlock field so the Create button is visible.
        private static BindingNode CreateAssetField() => new BindingNode
        {
            PropertyPath = "newBlock", DisplayType = DisplayType.InlineEditor, Header = "Create Asset Button",
            Expandable = true, DefaultToOpen = true, CreateAssetEnabled = true
        };

        // ============================================================ Containers =======

        // Parent page: each container sits in its own open-by-default expandable Card so the page reads as a
        // tidy collapsible stack. Foldout / Toggle-Group are already their own collapsible framed sections, so
        // they're left un-double-wrapped; the rest get a card.
        public static LayoutNode Containers() => Root(
            WrapContainerCard(ColumnsContainer(), "Columns"),
            WrapContainerCard(GridContainer(), "Grid"),
            WrapContainerCard(CardContainer(), "Card"),
            FoldoutContainer(),
            ToggleGroupContainer(),
            WrapContainerCard(TabsContainer(), "Tabs"),
            FrameContainer(),
            ExpandableCard("Frame shapes & image fill", false,
                PillFrame(), SplitFrame(), BoundFillFrame(), Bind("portrait")));

        public static LayoutNode Containers_Columns() => Root(ColumnsContainer());
        public static LayoutNode Containers_Grid() => Root(GridContainer());
        public static LayoutNode Containers_Card() => Root(CardContainer());
        public static LayoutNode Containers_Foldout() => Root(FoldoutContainer());
        public static LayoutNode Containers_ToggleGroup() => Root(ToggleGroupContainer());
        public static LayoutNode Containers_Tabs() => Root(TabsContainer());
        public static LayoutNode Containers_Frame() => Root(FrameContainer());

        // The other three Frame shapes plus a fill that rides a FIELD instead of a baked texture. The Circle
        // stays on the Frame page above; these are the ones it can't show at the same time.
        public static LayoutNode Containers_FrameShapes() => Root(
            Card("Pill", PillFrame()),
            Card("Diagonal Split (two panes, angled seam)", SplitFrame()),
            Card("Rounded rect, image fill bound to a field", BoundFillFrame(), Bind("portrait")));

        private static ContainerNode ColumnsContainer()
        {
            var c = Container(ContainerType.Columns, Bind("title"), Bind("capacity"), Bind("spawnRate"));
            c.Title = "Columns (drag the dividers)";
            c.TrackResize = TrackResizeMode.Enabled;
            return c;
        }
        private static ContainerNode GridContainer()
        {
            var c = Container(ContainerType.Grid, Bind("width"), Bind("height"), Bind("capacity"), Bind("dangerLevel"));
            c.Title = "Grid (resizable rows + columns)";
            c.GridConstraint = GridConstraint.FixedColumnCount;
            c.GridColumns = 2;
            c.GridResizableColumns = true;
            c.GridResizableRows = true;
            return c;
        }
        private static ContainerNode CardContainer()
        {
            var c = new ContainerNode { Type = ContainerType.Card, Title = "Card container (color-coded)" };
            // Section Tint: a light-green color-coded section (header + body tint + left accent rail).
            c.SectionTintColor = new OverridableColor { isOverridden = true, value = new Color(0.30f, 0.70f, 0.36f, 1f) };
            c.Children.Add(Bind("themeColor"));
            c.Children.Add(Bind("origin"));
            return c;
        }
        private static ContainerNode FoldoutContainer()
        {
            var c = new ContainerNode { Type = ContainerType.Foldout, Title = "Foldout section (color-coded)", DefaultToOpen = true };
            // Section Tint: a light-red color-coded section.
            c.SectionTintColor = new OverridableColor { isOverridden = true, value = new Color(0.80f, 0.32f, 0.32f, 1f) };
            c.Children.Add(Bind("spawnRate"));
            c.Children.Add(Bind("dangerLevel"));
            return c;
        }
        private static ContainerNode ToggleGroupContainer()
        {
            var c = new ContainerNode
            {
                Type = ContainerType.Foldout, Title = "Toggle-Group Foldout",
                GroupTogglePath = "enableModule", DefaultToOpen = true
            };
            c.Children.Add(Bind("description"));
            c.Children.Add(Bind("active"));
            return c;
        }
        private static ContainerNode TabsContainer()
        {
            var t1 = Container(ContainerType.Rows, Bind("capacity"), Bind("spawnRate"));
            t1.TabLabel = "Stats";
            var t2 = Container(ContainerType.Rows, Bind("active"), Bind("bossRoom"));
            t2.TabLabel = "Flags";
            return Container(ContainerType.Tabs, t1, t2);
        }

        private static ContainerNode FrameContainer()
        {
            var c = new ContainerNode { Type = ContainerType.Frame, Title = "Frame (shaped, clipped panel)" };
            c.Frame.Shape = FrameShape.Circle;
            // Fill / border / radius ride the generic Style — the builder paints them on the clip root, so they
            // follow the shape for free. An overlay wash darkens the bottom edge so overlaid content stays readable.
            c.Style = new LayoutStyle
            {
                width = OverFloat(235f), // Circle enforces 1:1, so this is the diameter
                alignSelf = OverEnum(Align.Center), // center the fixed-width circle in the page
                backgroundColor = OverColor(new Color(0.14f, 0.20f, 0.30f)),
                borderColor = OverColor(new Color(0.40f, 0.70f, 1f)),
                borderWidth = OverFloat(2f), borderRadius = OverFloat(14f)
            };
            c.Frame.OverlayEnabled = true;
            c.Frame.OverlayEdge = FrameOverlayEdge.Bottom;
            var overlayRamp = new Gradient();
            overlayRamp.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) });
            c.Frame.OverlayGradient = overlayRamp;
            c.Children.Add(Bind("title"));
            c.Children.Add(Bind("capacity"));
            return c;
        }

        // Pill: the fully-rounded capsule. Padding comes from the generic Style, same as any container.
        private static ContainerNode PillFrame()
        {
            var c = new ContainerNode { Type = ContainerType.Frame };
            c.Frame.Shape = FrameShape.Pill;
            c.Style = new LayoutStyle
            {
                backgroundColor = OverColor(new Color(0.16f, 0.24f, 0.34f)),
                borderColor = OverColor(new Color(0.45f, 0.72f, 1f)),
                borderWidth = OverFloat(2f),
                paddingLeft = OverFloat(20f), paddingRight = OverFloat(20f),
                paddingTop = OverFloat(10f), paddingBottom = OverFloat(10f)
            };
            c.Children.Add(Bind("title"));
            return c;
        }

        // DiagonalSplit is the one shape with STRUCTURE: Children[0] fills pane A, Children[1] pane B, with an
        // angled seam between them. Each pane gets its own Rows container so the split reads as two regions.
        private static ContainerNode SplitFrame()
        {
            var c = new ContainerNode { Type = ContainerType.Frame };
            c.Frame.Shape = FrameShape.DiagonalSplit;
            c.Frame.SplitPosition = 0.5f;
            c.Frame.SplitAngle = 12f;   // degrees off vertical
            c.Frame.SeamWidth = 2f;
            c.Style = new LayoutStyle
            {
                backgroundColor = OverColor(new Color(0.15f, 0.21f, 0.30f)),
                borderRadius = OverFloat(10f)
            };
            c.Children.Add(Container(ContainerType.Rows, Bind("capacity"), Bind("spawnRate")));
            c.Children.Add(Container(ContainerType.Rows, Bind("active"), Bind("bossRoom")));
            return c;
        }

        // The fill rides a FIELD (Frame.FillBindingPath), not a baked GUID — swap the texture in the object
        // field below the frame and it repaints. The overlay wash keeps the overlaid label readable.
        private static ContainerNode BoundFillFrame()
        {
            var c = new ContainerNode { Type = ContainerType.Frame };
            c.Frame.Shape = FrameShape.RoundedRect;
            c.Frame.FillBindingPath = "portrait";
            c.Frame.FillScaleMode = ImageBannerScaleMode.ScaleAndCrop;
            c.Frame.OverlayEnabled = true;
            c.Frame.OverlayEdge = FrameOverlayEdge.Bottom;
            c.Frame.OverlaySizePercent = 55f;
            var ramp = new Gradient();
            ramp.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            c.Frame.OverlayGradient = ramp;
            c.Style = new LayoutStyle
            {
                height = OverFloat(150f),
                borderRadius = OverFloat(12f),
                justify = OverEnum(Justify.FlexEnd), // push the overlaid label to the bottom edge
                paddingLeft = OverFloat(10f), paddingRight = OverFloat(10f), paddingBottom = OverFloat(8f)
            };
            c.Children.Add(Bind("title"));
            return c;
        }

        // ============================================================ Layout Elements =

        // Decorative, non-bound palette ELEMENTS (parallel to the designer Widgets palette's ELEMENTS section):
        // an image / banner, a rich-text block, spacers + dividers, and an Embedded Editor that re-inlines the
        // target type's OWN [CustomEditor]. None bind a field, so their child demos carry a codeDescription.
        // Parent and child pages share the same element builders so the two never drift.
        public static LayoutNode Elements() => Root(
            ExpandableCard("Image", true, ImageBannerElement()),
            ExpandableCard("Text Block", true, TextBlockElement()),
            ExpandableCard("Spacers & Dividers", true, SpacerDividerElements()),
            ExpandableCard("Embedded Editor", true, EmbeddedEditorElement()));

        public static LayoutNode Elements_ImageBanner() => Root(Card("Image", ImageBannerElement()));
        public static LayoutNode Elements_TextBlock() => Root(Card("Text Block", TextBlockElement()));
        public static LayoutNode Elements_Spacers() => Root(Card("Spacers & Dividers", SpacerDividerElements()));
        public static LayoutNode Elements_EmbeddedEditor() => Root(Card("Embedded Editor", EmbeddedEditorElement()));

        // Asset GUID of the shipped Showcase/Assets/Brick Wall.png (referenced by GUID so it survives moves/renames).
        private const string BrickWallTextureGuid = "71e917249a0ccd24a80b44377dcb374c";

        private static ImageBannerNode ImageBannerElement() => new ImageBannerNode
        {
            TextureGuid = BrickWallTextureGuid, Height = 110f, ScaleMode = ImageBannerScaleMode.ScaleToFit,
            Alignment = ImageBannerAlignment.Center, Zoom = 2.05f
        };

        private static TextBlockNode TextBlockElement()
        {
            var t = new TextBlockNode
            {
                Text =
                    "A **Text Block** drops formatted, {Aurora Red|colorful} prose anywhere in your inspector — no " +
                    "field required. It renders the same lightweight markdown as help boxes: **bold**, *italics*, " +
                    "{#88C0D0|hex} or {Aurora Green|palette} colors, inline `code`, and links like " +
                    "[our YouTube channel](https://www.youtube.com/@sasquatchbgames).\n\n" +
                    "It even renders a full, syntax-highlighted code block:\n" +
                    "```\n" +
                    "// Spawn a wave, scaled to the current difficulty\n" +
                    "for (int i = 0; i < waveSize; i++)\n" +
                    "{\n" +
                    "    var enemy = Instantiate(enemyPrefab, SpawnPoint(i), Quaternion.identity);\n" +
                    "    enemy.Init(difficulty, reward: 10 * i);\n" +
                    "}\n" +
                    "```",
                RenderMarkdown = true, Padding = 10f, CornerRadius = 6f
            };
            t.BackgroundColor = new OverridableColor { isOverridden = true, value = new Color(0.16f, 0.20f, 0.26f) };
            return t;
        }

        private static LayoutNode[] SpacerDividerElements() => new LayoutNode[]
        {
            new TextBlockNode { Text = "Above the divider.", RenderMarkdown = false },
            new LabeledDividerNode { Title = "Labeled Divider", Thickness = 4f },
            new SpaceNode { SpaceAmount = 24f },
            new LabeledDividerNode { Thickness = 1f },
            new TextBlockNode { Text = "Below a 24px Space and a plain rule.", RenderMarkdown = false }
        };

        // Embeds JUST the [SasquatchEmbed("Compass")] slice of ShowcaseElementsTarget's own custom editor.
        private static EmbeddedEditorNode EmbeddedEditorElement() => new EmbeddedEditorNode { SliceMethodName = "Compass" };

        // ============================================================ Validation ======

        public static LayoutNode Validation() => Root(
            Card("Validation Rules", RequiredField(), RangeField(), MethodField_Validate()),
            Card("Conditional Visibility", EnableToggle(), TuningShow(), NoteEnable()),
            HelpBoxesCard(),
            // Its own collapsible (open by default) section so the markdown demo reads as clearly separate.
            ExpandableCard("Markdown", true, MarkdownItems()));

        public static LayoutNode Validation_Required() => Root(RequiredField());
        public static LayoutNode Validation_Range() => Root(RangeField());
        public static LayoutNode Validation_Method() => Root(MethodField_Validate());
        public static LayoutNode Validation_Show() => Root(EnableToggle(), TuningShow());
        public static LayoutNode Validation_Enable() => Root(EnableToggle(), NoteEnable());
        public static LayoutNode Validation_HelpBoxes() => Root(HelpBoxItems());
        public static LayoutNode Validation_Markdown() => Root(MarkdownItems());

        private static BindingNode RequiredField() => new BindingNode
        {
            PropertyPath = "playerName", Header = "Required", Required = true, RequiredMessage = "A player name is required."
        };
        private static BindingNode RangeField() => new BindingNode
        {
            PropertyPath = "level", Header = "Range-validated (1–99)", ValidateRange = true,
            ValidationMin = 1f, ValidationMax = 99f, RangeValidationMessage = "Level must be between 1 and 99."
        };
        private static BindingNode MethodField_Validate() => new BindingNode
        {
            PropertyPath = "teamSize", Header = "Method validation (must be even)",
            ValidationMethod = "ValidateTeamSize", ValidationMessage = "Team size must be an even number."
        };
        private static BindingNode EnableToggle() =>
            new BindingNode { PropertyPath = "enableAdvanced", DisplayType = DisplayType.Toggle, Header = "Enable Advanced" };
        private static BindingNode TuningShow()
        {
            var b = new BindingNode { PropertyPath = "advancedTuning", DisplayType = DisplayType.Slider, Header = "Shown only when enabled" };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 1f;
            b.Conditions.Add(new VisualCondition("enableAdvanced", ConditionOperator.IsTrue, outcome: ConditionOutcome.Show));
            return b;
        }
        private static BindingNode NoteEnable()
        {
            var b = new BindingNode { PropertyPath = "advancedNote", DisplayType = DisplayType.Text, Header = "Enabled only when enabled" };
            b.Conditions.Add(new VisualCondition("enableAdvanced", ConditionOperator.IsTrue, outcome: ConditionOutcome.Enable));
            return b;
        }
        // ============================================ Condition Debugger ======

        // A scenario whose fields START hidden/disabled so the debugger overlays are visible immediately, and that
        // walks the range of the condition system — a simple rule, a mixed-logic GROUP, a computed-bool DRIVER,
        // and a disable rule:
        //   spread      Show when  mode == Ranged                      → HIDDEN   (simple: one condition)
        //   ammoCount   Show when  any of [ (Active AND Ammo), Magic ] → HIDDEN   (a one-level group)
        //   chargeLevel Show when  CanFire is true (a computed bool)   → HIDDEN   (member driver)
        //   reloadNote  Disable when ammoCount == 0                    → DISABLED (amber)
        public static LayoutNode ConditionDebugger() => Root(
            Card("Drivers", CondActiveToggle(), CondAmmoToggle(), CondModeField()),
            Card("Dependent fields", CondSpreadField(), CondAmmoCountField(), CondChargeField(), CondReloadNoteField()));

        private static BindingNode CondActiveToggle() =>
            new BindingNode { PropertyPath = "isActive", DisplayType = DisplayType.Toggle, Header = "Active" };
        private static BindingNode CondAmmoToggle() =>
            new BindingNode { PropertyPath = "hasAmmo", DisplayType = DisplayType.Toggle, Header = "Has Ammo" };
        private static BindingNode CondModeField() =>
            new BindingNode { PropertyPath = "mode", Header = "Weapon Mode" };

        // Simple: a single Show condition — the everyday case.
        private static BindingNode CondSpreadField()
        {
            var b = new BindingNode { PropertyPath = "spread", DisplayType = DisplayType.Slider, Header = "Spread (simple: Ranged only)" };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 1f;
            var rule = new ConditionRule(ConditionOutcome.Show, ConditionMatch.All);
            rule.Items.Add(ConditionGroup.Leaf(new VisualCondition("mode", ConditionOperator.Equals, "Ranged")));
            b.ConditionRules.Add(rule);
            return b;
        }

        // Mixed AND/OR via a one-level GROUP: Show when ANY of [ (Active AND Has Ammo), mode == Magic ].
        private static BindingNode CondAmmoCountField()
        {
            var b = new BindingNode { PropertyPath = "ammoCount", Header = "Ammo Count (group: Active+Ammo, or Magic)" };
            var rule = new ConditionRule(ConditionOutcome.Show, ConditionMatch.Any);
            var group = ConditionGroup.Group(ConditionMatch.All);
            group.Conditions.Clear();
            group.Conditions.Add(new VisualCondition("isActive", ConditionOperator.IsTrue));
            group.Conditions.Add(new VisualCondition("hasAmmo", ConditionOperator.IsTrue));
            rule.Items.Add(group);
            rule.Items.Add(ConditionGroup.Leaf(new VisualCondition("mode", ConditionOperator.Equals, "Magic")));
            b.ConditionRules.Add(rule);
            return b;
        }

        // Computed-bool DRIVER: "CanFire" is a bool property on the target, not a serialized field.
        private static BindingNode CondChargeField()
        {
            var b = new BindingNode { PropertyPath = "chargeLevel", Header = "Charge (shown when CanFire — a computed bool)" };
            var rule = new ConditionRule(ConditionOutcome.Show, ConditionMatch.All);
            rule.Items.Add(ConditionGroup.Leaf(new VisualCondition("CanFire", ConditionOperator.IsTrue)));
            b.ConditionRules.Add(rule);
            return b;
        }

        private static BindingNode CondReloadNoteField()
        {
            var b = new BindingNode { PropertyPath = "reloadNote", DisplayType = DisplayType.Text, Header = "Reload Note (disabled when empty mag)" };
            var rule = new ConditionRule(ConditionOutcome.Disable, ConditionMatch.Any);
            rule.Items.Add(ConditionGroup.Leaf(new VisualCondition("ammoCount", ConditionOperator.Equals, "0")));
            b.ConditionRules.Add(rule);
            return b;
        }

        private static ContainerNode HelpBoxesCard() => Card("Help Boxes (warning labels)", HelpBoxItems());

        private static LayoutNode[] HelpBoxItems() => new LayoutNode[]
        {
            new HelpBoxNode
            {
                Message = "Expandable details: a help box can collapse to a one-line summary and expand on click.",
                MessageType = HelpBoxMessageType.Info, Expandable = true, CollapsedSummary = "Click for details",
                DefaultToOpen = false
            },
            new HelpBoxNode { Message = "Informational note shown right in the inspector.", MessageType = HelpBoxMessageType.Info },
            new HelpBoxNode { Message = "A warning about a risky value.", MessageType = HelpBoxMessageType.Warning },
            new HelpBoxNode { Message = "An error the user must fix.", MessageType = HelpBoxMessageType.Error }
        };

        // One of EVERYTHING the markdown renderer supports, in as few boxes as makes sense. Every line is pure
        // EXAMPLE text (never an instruction): a feature roundup that ends with a link and a real fenced code
        // block, the expandable variant (with colored text), and a markdown-OFF box that shows the raw markers.
        private static LayoutNode[] MarkdownItems() => new LayoutNode[]
        {
            new HelpBoxNode
            {
                MessageType = HelpBoxMessageType.Info,
                Message =
                    "Here is some **bold** text and some *italic* text. Colors come from a palette name like " +
                    "{Aurora Red|this red word} or a hex value like {#88C0D0|this blue word}, and short values " +
                    "use inline `code`. You can also link out: " +
                    "[watch Sasquatch B Studios on YouTube](https://www.youtube.com/@sasquatchbgames).\n\n" +
                    "And a fenced block renders as a real, syntax-highlighted code panel:\n" +
                    "```\n" +
                    "int hp = Mathf.Clamp(value, 0, maxHealth);\n" +
                    "if (hp <= 0) Die();\n" +
                    "transform.position += Vector3.up * speed;\n" +
                    "```"
            },
            // The expandable variant — collapses to a one-line summary; markdown (incl. color) works in both.
            new HelpBoxNode
            {
                MessageType = HelpBoxMessageType.Warning,
                Message = "This is just **example** body text. It {Aurora Green|wraps} to the inspector width " +
                          "and supports the same *markdown* — colors, **bold**, and `code` — as every other box.",
                Expandable = true,
                CollapsedSummary = "{Aurora Yellow|An expandable box} (click to open)",
                DefaultToOpen = true
            },
            // Markdown turned OFF: the very same characters show through verbatim.
            new HelpBoxNode
            {
                MessageType = HelpBoxMessageType.Info, RenderMarkdown = false,
                Message = "With Render Markdown OFF, the raw characters show through: **bold**, *italics*, " +
                          "`code`, and {#88C0D0|color}."
            }
        };

        // ============================================================ Buttons (page) ==

        public static LayoutNode Buttons() => Root(
            Card("State (watch these change)",
                new BindingNode { PropertyPath = "gold", IsReadOnly = true },
                new BindingNode { PropertyPath = "lastAction", IsReadOnly = true }),
            Card("Buttons — one of every style",
                // Modern: hugs content, RIGHT-aligned (Style.alignSelf overrides the button's default left-align).
                new ButtonNode
                {
                    MethodName = "SayHello", ButtonText = "Say Hello", StylePreset = ButtonStyle.Modern,
                    Size = ButtonSize.Large, IconId = SasquatchIcons.Ids.Sparkles,
                    HoverAnimation = ButtonHoverAnimation.Bounce,
                    FullWidth = false, Style = new LayoutStyle { alignSelf = OverEnum(Align.FlexEnd) }
                },
                // Soft: a gentle, warm-accent secondary — full width, gold (thematic for the gold action).
                new ButtonNode
                {
                    MethodName = "AddGold", ButtonText = "Add Gold…", StylePreset = ButtonStyle.Soft,
                    Size = ButtonSize.Medium, IconId = SasquatchIcons.Ids.Coins,
                    HoverAnimation = ButtonHoverAnimation.Lift,
                    AccentColor = OverColor(new Color(0.95f, 0.72f, 0.28f))
                },
                // Outline: hugs content, CENTER-aligned, with the icon trailing (reads like "launch →").
                new ButtonNode
                {
                    MethodName = "RunLongTaskAsync", ButtonText = "Run Task", StylePreset = ButtonStyle.Outline,
                    Size = ButtonSize.Medium, IconId = SasquatchIcons.Ids.Rocket,
                    IconPlacement = ButtonIconPlacement.Right, HoverAnimation = ButtonHoverAnimation.Grow,
                    FullWidth = false, Style = new LayoutStyle { alignSelf = OverEnum(Align.Center) }
                },
                // Danger: hugs content, LEFT-aligned — confirm dialog + a darker red on press.
                new ButtonNode
                {
                    MethodName = "ResetData", ButtonText = "Reset Data", StylePreset = ButtonStyle.Danger,
                    Size = ButtonSize.Medium, IconId = SasquatchIcons.Ids.Trash2,
                    HoverAnimation = ButtonHoverAnimation.Bounce, ConfirmBeforeExecute = true,
                    ConfirmTitle = "Reset data?", ConfirmMessage = "This sets gold back to 0. Continue?",
                    PressedColor = OverColor(new Color(0.55f, 0.14f, 0.14f)),
                    FullWidth = false
                },
                // Celebrate: Ghost, FULL width.
                new ButtonNode
                {
                    MethodName = "Celebrate", ButtonText = "Celebrate", StylePreset = ButtonStyle.Ghost,
                    Size = ButtonSize.Medium, IconId = SasquatchIcons.Ids.Star,
                    HoverAnimation = ButtonHoverAnimation.Pop
                },
                // Save: Classic, FULL width.
                new ButtonNode
                {
                    MethodName = "Save", ButtonText = "Save", StylePreset = ButtonStyle.Classic,
                    Size = ButtonSize.Medium, IconId = SasquatchIcons.Ids.Save,
                    HoverAnimation = ButtonHoverAnimation.None
                }));

        // ============================================================ Collections (page)

        // Parent page: one expandable card per collection feature so the whole area reads as a collapsible
        // stack (the Containers / Elements / Bonuses pattern). Every card opens by default — a collection you
        // have to click open is a collection nobody sees.
        public static LayoutNode Collections() => Root(
            ExpandableCard("Table (drag the column dividers + right-click a row)", true, LootTable()),
            ExpandableCard("Reorderable list", true, TagsList()),
            ExpandableCard("In-Line Editor Rows", true, StatBlockList()),
            ExpandableCard("Tag Chip list", true, TagChipsList()),
            ExpandableCard("Polymorphic list", true, PolymorphicList()),
            // The two style dimensions, same builders the child pages use. Collapsed by default — six more
            // six-row lists open on an already-long page buries the widgets above them, and the Containers
            // page sets the same precedent with "Frame shapes & image fill".
            ExpandableCard("List Styles — Rows / Minimal / Timeline", false,
                StyledWaypoints(CollectionListStyle.Rows),
                StyledWaypoints(CollectionListStyle.Minimal),
                StyledWaypoints(CollectionListStyle.Timeline)),
            ExpandableCard("Row Frames & Zebra — Line / Card / tinted rail / zebra", false,
                FramedWaypoints(CollectionRowSeparator.Line),
                FramedWaypoints(CollectionRowSeparator.Card),
                FramedWaypoints(CollectionRowSeparator.Card, rail: new Color(0.35f, 0.68f, 1f)),
                FramedWaypoints(CollectionRowSeparator.None, zebra: true)),
            ExpandableCard("Large Inventory — search, sort, paginate", true, InventoryTable()));

        public static LayoutNode Collections_Table() => Root(Card("Table", LootTable()));
        public static LayoutNode Collections_List() => Root(Card("Reorderable list", TagsList()));
        public static LayoutNode Collections_InlineRows() => Root(Card("In-Line Editor Rows", StatBlockList()));
        public static LayoutNode Collections_TagChips() => Root(Card("Tag Chip list", TagChipsList()));
        public static LayoutNode Collections_Polymorphic() => Root(Card("Polymorphic list", PolymorphicList()));

        // The list's CONTAINER look. One list drawn three ways, varying exactly one setting, so the
        // difference is the only thing on screen.
        public static LayoutNode Collections_ListStyles() => Root(
            Card("Rows — a bordered shell and an edge down the index gutter (the default)",
                StyledWaypoints(CollectionListStyle.Rows)),
            Card("Minimal — no shell, more air, one faint gutter edge",
                StyledWaypoints(CollectionListStyle.Minimal)),
            Card("Timeline — a recessed well with a continuous rail, every index a node on it",
                StyledWaypoints(CollectionListStyle.Timeline)));

        // The ROW look, which composes with every List Style above. Zebra is the fourth, orthogonal option.
        public static LayoutNode Collections_RowFrames() => Root(
            Card("None — bare rows (the default)", FramedWaypoints(CollectionRowSeparator.None)),
            Card("Line — a hairline between rows, never after the last", FramedWaypoints(CollectionRowSeparator.Line)),
            Card("Card — a tray per row with a full-height left rail", FramedWaypoints(CollectionRowSeparator.Card)),
            Card("Frame Color — the same rail, tinted", FramedWaypoints(CollectionRowSeparator.Card,
                rail: new Color(0.35f, 0.68f, 1f))),
            Card("Zebra Striping — alternating row tint, on or off independently",
                FramedWaypoints(CollectionRowSeparator.None, zebra: true)));

        // The stock Value-Based Fill ramp reads red→yellow→green because it was built for health: full is GOOD.
        // Weight inverts that meaning — a heavy item is the bad one — so both weight columns pin the same three
        // colors in reverse. Assigning the gradient is what pins it; leaving it unset returns the health ramp.
        private static Gradient InvertedWeightRamp()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.35f, 0.75f, 0.38f), 0f),   // light = green
                    new GradientColorKey(new Color(0.95f, 0.79f, 0.25f), 0.5f),
                    new GradientColorKey(new Color(0.85f, 0.22f, 0.22f), 1f),   // heavy = red
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static CollectionNode LootTable()
        {
            // Seed distinct column weights so columns start unequal — a visual cue that they're resizable. Rarity
            // gets extra room so its dropdown shows full enum names (e.g. "Legendary") without clipping.
            // A table CELL is a full binding, so it can be any widget: Weight is a progress bar and Stars a
            // rating row, right inside the grid.
            var item = new BindingNode { PropertyPath = "itemName", ColumnWeight = 2f };
            var weight = new BindingNode
            {
                PropertyPath = "weight", ColumnWeight = 1.5f, DisplayType = DisplayType.ProgressBar,
                ProgressBarTextMode = ProgressBarTextMode.ValueOnly, ProgressBarThickness = 16f,
                ProgressBarValueFill = true
            };
            weight.MinMaxLowLimit = 0f;
            weight.MinMaxHighLimit = 50f;
            weight.ProgressBarFillGradient = InvertedWeightRamp();
            // Affix In Column Header: the unit is folded into the header ("Weight (kg)") and suppressed in
            // every cell — the table-only third place an affix can land, after inside and beside the box.
            weight.SuffixAffix = Affix("kg");
            weight.AffixInColumnHeader = true;
            var rarity = new BindingNode { PropertyPath = "rarity", ColumnWeight = 1.7f };
            var stars = new BindingNode
            {
                PropertyPath = "stars", ColumnWeight = 1.4f, DisplayType = DisplayType.Rating,
                RatingStyle = RatingIconStyle.Stars, RatingCount = 5, RatingPipSize = 13f
            };
            var template = Container(ContainerType.Rows, item, weight, rarity, stars);
            return new CollectionNode
            {
                PropertyPath = "lootTable", DisplayMode = CollectionDisplayMode.Table,
                ReorderStyle = CollectionReorderStyle.DragAndDrop, ItemLayout = template, Searchable = true
            };
        }
        private static CollectionNode TagsList() => new CollectionNode
        {
            PropertyPath = "tags", DisplayMode = CollectionDisplayMode.List,
            ReorderStyle = CollectionReorderStyle.DragAndDrop
        };

        // The same six-row list for both style pages, so the only thing that changes between cards is the
        // one setting each card is named after.
        private static CollectionNode StyledWaypoints(CollectionListStyle style) => new CollectionNode
        {
            PropertyPath = "waypoints", DisplayMode = CollectionDisplayMode.List,
            ReorderStyle = CollectionReorderStyle.DragAndDrop, ListStyle = style
        };

        private static CollectionNode FramedWaypoints(CollectionRowSeparator frame, bool zebra = false,
                                                      Color? rail = null)
        {
            var c = new CollectionNode
            {
                PropertyPath = "waypoints", DisplayMode = CollectionDisplayMode.List,
                ReorderStyle = CollectionReorderStyle.DragAndDrop,
                RowSeparator = frame, ZebraStriping = zebra
            };
            if (rail.HasValue) c.RowSeparatorColor = OverColor(rail.Value);
            return c;
        }

        // Object-reference rows edited IN PLACE: each row is the element's own inspector under a chevron
        // (ShowcaseStatBlock's designed layout, registered as a session override by the window). Needs List
        // mode, UnityEngine.Object elements, and NO item template — a template would decide the row instead.
        // Card separators, because a card's edge rail is the only cue that grows with a tall row.
        private static CollectionNode StatBlockList() => new CollectionNode
        {
            PropertyPath = "statBlocks", DisplayMode = CollectionDisplayMode.List,
            ReorderStyle = CollectionReorderStyle.DragAndDrop,
            ElementInlineEditor = true, ElementDefaultToOpen = true,
            RowSeparator = CollectionRowSeparator.Card
        };

        // The WHOLE string collection as removable chips + an inline add field — the collection-level Display
        // Type, distinct from the Tag Chips display type on a single string[] FIELD (see the Dropdowns page).
        private static CollectionNode TagChipsList() => new CollectionNode
        {
            PropertyPath = "tags", DisplayMode = CollectionDisplayMode.TagChips
        };

        // A [SerializeReference] list: every row is a branded type picker plus the chosen type's own fields.
        private static CollectionNode PolymorphicList() => new CollectionNode
        {
            PropertyPath = "onHitEffects", DisplayMode = CollectionDisplayMode.List,
            ReorderStyle = CollectionReorderStyle.DragAndDrop,
            RowSeparator = CollectionRowSeparator.Card,
            PolymorphicSearchable = true
        };

        // A 120-item Table that shows off the large-collection ergonomics: pagination renders only 25 rows
        // at a time, clicking a header sorts (view-only), and the search box filters before paging.
        public static LayoutNode Inventory() => Root(
            Card("Inventory (120 items — search, sort, paginate)", InventoryTable()));

        private static CollectionNode InventoryTable()
        {
            // Weight is a progress bar cell rather than a number box: the generated data runs 0.1–5.0, so the
            // bar is scaled 0–5 and shows the raw value (no "/ max", which would repeat the same 5 on 120 rows).
            var weight = new BindingNode
            {
                PropertyPath = "weight", ColumnWeight = 1.4f, DisplayType = DisplayType.ProgressBar,
                ProgressBarTextMode = ProgressBarTextMode.ValueOnly, ProgressBarThickness = 16f,
                ProgressBarValueFill = true
            };
            weight.MinMaxLowLimit = 0f;
            weight.MinMaxHighLimit = 5f;
            weight.ProgressBarFillGradient = InvertedWeightRamp();
            var template = Container(ContainerType.Rows,
                new BindingNode { PropertyPath = "name",   ColumnWeight = 2f },
                new BindingNode { PropertyPath = "damage", ColumnWeight = 1f },
                new BindingNode { PropertyPath = "rarity", ColumnWeight = 1.5f },
                new BindingNode { PropertyPath = "value",  ColumnWeight = 1f },
                weight);
            return new CollectionNode
            {
                PropertyPath = "items", DisplayMode = CollectionDisplayMode.Table,
                ReorderStyle = CollectionReorderStyle.DragAndDrop, ItemLayout = template,
                Searchable = true, EnablePagination = true, PageSize = 25, EnableSorting = true,
                // Showcase the clickable page dots (current page enlarged) alongside the prev/next buttons.
                PaginationDisplayStyle = PaginationDisplayStyle.ButtonsAndDots
            };
        }

        // ============================================================ Advanced ========

        public static LayoutNode Advanced() => Root(
            Card("Color Palette", ColorGridField(), ColorBarField()),
            Card("Polymorphic [SerializeReference]", PolymorphicField()),
            Card("Custom UnityEvent", UnityEventField()));

        public static LayoutNode Advanced_ColorGrid() => Root(ColorGridField());
        public static LayoutNode Advanced_ColorBar() => Root(ColorBarField());
        public static LayoutNode Advanced_Polymorphic() => Root(PolymorphicField());
        public static LayoutNode Advanced_UnityEvent() => Root(UnityEventField());

        private static BindingNode ColorGridField() => new BindingNode
        {
            PropertyPath = "accent", DisplayType = DisplayType.ColorPalette, Header = "Grid swatches",
            ColorPaletteLayout = ColorPaletteLayout.Grid
        };
        private static BindingNode ColorBarField() => new BindingNode
        {
            PropertyPath = "background", DisplayType = DisplayType.ColorPalette, Header = "Bar swatches",
            ColorPaletteLayout = ColorPaletteLayout.Bar, ColorBarPlacement = ColorBarPlacement.Below
        };
        private static BindingNode PolymorphicField() => new BindingNode
        {
            PropertyPath = "onHitEffect", DisplayType = DisplayType.SerializeReference,
            Header = "Pick a concrete effect type", PolymorphicSearchable = true
        };
        private static BindingNode UnityEventField() =>
            new BindingNode { PropertyPath = "onTriggered", DisplayType = DisplayType.UnityEvent, Header = "On Triggered" };

        // ============================================================ Styling & Themes =

        public static LayoutNode Styling() => Root(
            Card("Text Styling", TextStyledField()),
            BackgroundsBordersCard(),
            Card("Slider Styling", SliderStyledField()),
            Card("Progress Bar Styling", ValueFillBarField(), FlatFillBarField()),
            Card("Toggle Styles", ToggleStyleFields()),
            SectionTintCard(),
            SectionTintFoldout(),
            ThemedCard());

        public static LayoutNode Styling_Text() => Root(TextStyledField());
        public static LayoutNode Styling_Background() => Root(BackgroundsBordersCard());
        public static LayoutNode Styling_Slider() => Root(SliderStyledField());
        public static LayoutNode Styling_ProgressBar() => Root(Card("Progress Bar Styling", ValueFillBarField(), FlatFillBarField()));
        public static LayoutNode Styling_Toggles() => Root(Card("Toggle Styles", ToggleStyleFields()));
        public static LayoutNode Styling_SectionTint() => Root(SectionTintCard(), SectionTintFoldout());
        public static LayoutNode Styling_Themed() => Root(ThemedCard());

        private static BindingNode TextStyledField()
        {
            var b = new BindingNode { PropertyPath = "heading", DisplayType = DisplayType.Text, Header = "Bold, large, tinted" };
            b.FieldTextStyle = new TextStyle
            {
                fontStyle = OverEnum(FontStyle.BoldAndItalic), fontSize = OverFloat(16f),
                color = OverColor(new Color(0.45f, 0.80f, 1f))
            };
            return b;
        }
        private static ContainerNode BackgroundsBordersCard()
        {
            var c = new ContainerNode { Type = ContainerType.Card, Title = "Backgrounds & Borders" };
            c.Style = new LayoutStyle
            {
                backgroundColor = OverColor(new Color(0.12f, 0.18f, 0.25f)),
                borderColor = OverColor(new Color(0.40f, 0.70f, 1f)),
                borderWidth = OverFloat(2f), borderRadius = OverFloat(8f),
                paddingTop = OverFloat(8f), paddingBottom = OverFloat(8f),
                paddingLeft = OverFloat(10f), paddingRight = OverFloat(10f)
            };
            c.Children.Add(Bind("note", DisplayType.Text));
            c.Children.Add(new BindingNode { PropertyPath = "active", DisplayType = DisplayType.Toggle });
            return c;
        }
        private static BindingNode SliderStyledField()
        {
            var b = new BindingNode { PropertyPath = "power", DisplayType = DisplayType.Slider, Header = "Custom knob, size & colors" };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 100f;
            b.DisplayStyleData = new DisplayStyleData
            {
                sliderKnobShape = SliderKnobShape.Grip, sliderSize = SliderSize.Large,
                sliderBarColor = new Color(0.20f, 0.50f, 0.90f), sliderKnobColor = new Color(0.95f, 0.60f, 0.20f)
            };
            return b;
        }
        // Two bars off the SAME kind of value, styled opposite ways: Value-Based Fill ramps the color by value
        // (the classic health-bar look) while the second pins a flat fill and sits its readout on a dark pill.
        private static BindingNode ValueFillBarField()
        {
            var b = new BindingNode
            {
                PropertyPath = "shields", DisplayType = DisplayType.ProgressBar, Header = "Value-based fill",
                ProgressBarTextMode = ProgressBarTextMode.Percentage, ProgressBarThickness = 26f,
                ProgressBarValueFill = true, UnitSuffix = " SP"
            };
            return b;
        }
        private static BindingNode FlatFillBarField()
        {
            var b = new BindingNode
            {
                PropertyPath = "power", DisplayType = DisplayType.ProgressBar, Header = "Flat fill + text backdrop",
                ProgressBarTextMode = ProgressBarTextMode.ValueAndMax, ProgressBarThickness = 26f,
                ProgressBarTextBackdrop = true
            };
            // Color.clear means "use the theme"; a real color pins the flat fill.
            b.ProgressBarColor = new Color(0.55f, 0.30f, 0.85f);
            return b;
        }

        // All five Toggle Styles at once — these are widget SHAPES, not property tweaks: the same bool reads as a
        // checkbox, a modern checkbox, a switch, a square switch, or a slim switch.
        private static LayoutNode[] ToggleStyleFields() => new LayoutNode[]
        {
            ToggleStyled("checkboxStyle", "Checkbox", ToggleStyle.Checkbox, false),
            ToggleStyled("modernCheckboxStyle", "Modern Checkbox", ToggleStyle.ModernCheckbox, false),
            ToggleStyled("roundedStyle", "Rounded", ToggleStyle.Rounded, true),
            ToggleStyled("squareStyle", "Square", ToggleStyle.Square, true),
            ToggleStyled("slimStyle", "Slim", ToggleStyle.Slim, true),
        };
        private static BindingNode ToggleStyled(string path, string header, ToggleStyle style, bool showOnOff)
        {
            var b = new BindingNode { PropertyPath = path, DisplayType = DisplayType.Toggle, Header = header };
            // ON/OFF overlay text is a SWITCH affordance — the two checkbox looks hide it, so only ask for it
            // on the three switch styles (mirrors the designer's own gating).
            b.DisplayStyleData = new DisplayStyleData { toggleStyle = style, toggleShowOnOff = showOnOff };
            return b;
        }

        // Section Tint: one color drives a section's header, its body wash AND its left accent rail, so a
        // color-coded group takes one field rather than four style overrides. Works on Card and Foldout alike.
        private static ContainerNode SectionTintCard()
        {
            var c = new ContainerNode { Type = ContainerType.Card, Title = "Section Tint — Card (amber)" };
            c.SectionTintColor = new OverridableColor { isOverridden = true, value = new Color(0.92f, 0.66f, 0.20f, 1f) };
            c.Children.Add(Bind("armor"));
            c.Children.Add(Bind("critChance"));
            return c;
        }
        private static ContainerNode SectionTintFoldout()
        {
            var c = new ContainerNode
            {
                Type = ContainerType.Foldout, Title = "Section Tint — Foldout (violet)", DefaultToOpen = true
            };
            c.SectionTintColor = new OverridableColor { isOverridden = true, value = new Color(0.60f, 0.40f, 0.90f, 1f) };
            c.Children.Add(Bind("heading", DisplayType.Text));
            c.Children.Add(Bind("tint"));
            return c;
        }

        private static ContainerNode ThemedCard()
        {
            // The "card" token resolves to the theme the window passes in (see ConfigureCardTheme); without a
            // theme it falls back to a plain Card.
            var c = new ContainerNode { Type = ContainerType.Card, Title = "Themed (token-driven)", StyleToken = "card" };
            c.Children.Add(Bind("heading", DisplayType.Text));
            c.Children.Add(Bind("tint"));
            return c;
        }

        // Fills a transient theme so any node with StyleToken="card" picks up these layout styles.
        public static void ConfigureCardTheme(SasquatchThemeAsset theme)
        {
            if (theme == null) return;
            theme.Themes.Add(new SasquatchThemeAsset.TokenMapping
            {
                token = "card",
                style = new LayoutStyle
                {
                    backgroundColor = OverColor(new Color(0.10f, 0.16f, 0.22f)),
                    borderColor = OverColor(new Color(0.30f, 0.65f, 1f)),
                    borderWidth = OverFloat(2f), borderRadius = OverFloat(10f),
                    paddingTop = OverFloat(8f), paddingBottom = OverFloat(8f),
                    paddingLeft = OverFloat(10f), paddingRight = OverFloat(10f)
                }
            });
        }

        // ============================================================ Animations (page)

        public static LayoutNode Animations() => Root(
            Card("Appear, Hover & Value-Changed", AppearField(), HoverField(), ValueChangedField()),
            Card("Click effects",
                ClickAnim("Click → Fade", AnimEffect.Fade),
                ClickAnim("Click → Scale Pop", AnimEffect.ScalePop),
                ClickAnim("Click → Slide", AnimEffect.Slide),
                ClickAnim("Click → Color Pulse", AnimEffect.ColorPulse),
                ClickAnim("Click → Shake", AnimEffect.Shake),
                ClickAnim("Click → Spring Pop", AnimEffect.SpringPop, 0.6f),
                ClickAnim("Click → Spring Slide", AnimEffect.SpringSlide, 0.5f),
                ClickAnim("Click → Spring Shake", AnimEffect.SpringShake, 0.5f)));

        private static BindingNode AppearField()
        {
            var b = new BindingNode { PropertyPath = "appearValue", DisplayType = DisplayType.Slider, Header = "Appear → Fade in" };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 100f;
            b.Animations.Add(new NodeAnimation { Trigger = AnimTrigger.Appear, Effect = AnimEffect.Fade, DurationSeconds = 0.45f });
            return b;
        }
        // The Hover trigger — the one trigger that needs no edit and no click: pass the cursor over the row.
        // Paired with Color Pulse, the effect it reads most naturally as.
        private static BindingNode HoverField()
        {
            var b = new BindingNode { PropertyPath = "hoverValue", DisplayType = DisplayType.Slider, Header = "Hover → Color Pulse" };
            b.MinMaxLowLimit = 0f;
            b.MinMaxHighLimit = 100f;
            b.Animations.Add(new NodeAnimation { Trigger = AnimTrigger.Hover, Effect = AnimEffect.ColorPulse });
            return b;
        }
        private static BindingNode ValueChangedField()
        {
            // A plain float field (not a slider) so the Value-Changed animation fires when you COMMIT an edit,
            // rather than continuously on every drag tick.
            var b = new BindingNode { PropertyPath = "valueChanged", DisplayType = DisplayType.Default, Header = "Value Changed → Pop" };
            b.Animations.Add(new NodeAnimation { Trigger = AnimTrigger.ValueChanged, Effect = AnimEffect.ScalePop });
            return b;
        }
        private static ButtonNode ClickAnim(string text, AnimEffect effect, float bounce = 0.4f)
        {
            var b = new ButtonNode { MethodName = "Trigger", ButtonText = text };
            var anim = new NodeAnimation { Trigger = AnimTrigger.Click, Effect = effect };
            if (effect == AnimEffect.SpringPop || effect == AnimEffect.SpringSlide || effect == AnimEffect.SpringShake)
                anim.Bounce = bounce;
            b.Animations.Add(anim);
            return b;
        }

        // ============================================================ Quick-Edit (page)

        public static LayoutNode QuickEdit() => Root(
            Card("Right-click any field to edit it inline",
                new BindingNode { PropertyPath = "health" },
                new BindingNode { PropertyPath = "invincible" },
                new BindingNode { PropertyPath = "notes" }));

        // ============================================================ Live Debug (page)

        // Opens with a Script Header, because the bug glyph that arms Live Debug lives IN the header — the
        // switch has to be on screen for the page to mean anything. The recording itself is Play-Mode only,
        // so the fields below are the kind of numbers worth watching rather than a canned recording.
        public static LayoutNode LiveDebug() => HeaderRoot(
            Card("Live Debug", LiveDebugHelp(),
                DebugHealthField(), DebugSpeedField(), DebugWaveField(), DebugFlagField()),
            Card("Driving it from your own code", LiveDebugApiHelp()));

        private static HelpBoxNode LiveDebugHelp() => new HelpBoxNode
        {
            Message = "**Click the bug glyph in the Script Header above.** Every field gains a trace strip: a " +
                      "rolling graph of what the value did, the range it covered, and a pin for every " +
                      "`Debug.Log` this object wrote at that instant. A transport bar pins itself to the top of " +
                      "the inspector — *scrub it, or click any sample* for a moment card showing the exact value, " +
                      "the frame, and the console output around it.\n\n" +
                      "**In your own project, recording runs in Play Mode** — arm the glyph on your script, " +
                      "press Play, and the strips fill. *This sandbox page is the exception:* it drives the four " +
                      "values below from its own demo clock, so you can watch the thing work without leaving the " +
                      "editor. Everything else is the real feature — Record/Stop, the scrubber, the gear, the " +
                      "moment card.\n\n" +
                      "Recording survives the domain reload that entering Play Mode causes, and it switches " +
                      "itself off when you restart the editor — it is a sitting, not a setting.\n\n" +
                      "`Ctrl+Alt+R` starts/stops every armed script, `Ctrl+Alt+M` drops a marker. Both are " +
                      "rebindable in {Color|Edit ▸ Shortcuts}. The gear on the transport bar decides what gets " +
                      "recorded and how long it is kept.",
            MessageType = HelpBoxMessageType.Info
        };

        private static HelpBoxNode LiveDebugApiHelp() => new HelpBoxNode
        {
            Message = "Live Debug also takes orders from your game code, so a recording can start at the moment " +
                      "that actually matters instead of the moment you clicked:\n\n" +
                      "```\n" +
                      "SasquatchDebug.Record(this);      // start recording this object\n" +
                      "SasquatchDebug.Mark(\"died\");       // pin this instant on the timeline\n" +
                      "SasquatchDebug.Stop(this);        // and stop\n" +
                      "```\n\n" +
                      "*The calls compile away outside the editor* — nothing of this ships in a build.",
            MessageType = HelpBoxMessageType.Info
        };

        // Ordinary fields, chosen because a graph of each one would tell you something: a clamped stat, a
        // free float, an integer that steps, and a bool whose trace reads as a square wave.
        private static BindingNode DebugHealthField() => new BindingNode
        {
            PropertyPath = "health", DisplayType = DisplayType.ProgressBar, LabelOverride = "Health",
            ProgressBarTextMode = ProgressBarTextMode.ValueAndMax, ProgressBarValueFill = true,
            ProgressBarThickness = 20f, SuffixAffix = Affix("HP")
        };
        private static BindingNode DebugSpeedField() => new BindingNode
        {
            PropertyPath = "throttle", LabelOverride = "Speed", SuffixAffix = Affix("m/s")
        };
        private static BindingNode DebugWaveField() => new BindingNode
        {
            PropertyPath = "waveSize", LabelOverride = "Wave Size"
        };
        private static BindingNode DebugFlagField()
        {
            var b = new BindingNode { PropertyPath = "invincible", DisplayType = DisplayType.Toggle, LabelOverride = "Invincible" };
            b.DisplayStyleData = new DisplayStyleData { toggleStyle = ToggleStyle.Rounded, toggleShowOnOff = true };
            return b;
        }

        // ============================================================ Bonuses (page) ===

        // Parent page: the three bonuses each in their own open, expandable card so they read as a tidy,
        // separable stack. The focused child pages reuse the same node builders so the two never drift.
        public static LayoutNode Bonuses() => Root(
            ExpandableCard("Scene Reference", true, SceneRefHelp(), SceneRefField()),
            ExpandableCard("SasquatchString", true, SasqStringHelp(), SasqAmmoField(), SasqElementField(), SasqStringField()),
            ExpandableCard("Spring (SpringConfig)", true, SpringHelp(), SpringConfigField()),
            ExpandableCard("Serializable Dictionary", true, DictHelp(), DictWeightsField(), DictColorsField()),
#if UNITY_6000_6_OR_NEWER
            ExpandableCard("Dictionary Table (native)", false, NativeDictHelp(), NativeDictField()),
#endif
            ExpandableCard("Dictionary Styles", false, DictStylesHelp(),
                DictStyleField("styleTable", DictionaryTableStyle.Table, "Table"),
                DictStyleField("styleMinimal", DictionaryTableStyle.Minimal, "Minimal"),
                DictStyleField("styleLedger", DictionaryTableStyle.Ledger, "Ledger"),
                DictStyleField("styleMapping", DictionaryTableStyle.Mapping, "Mapping")),
            ExpandableCard("Spring Physics Tweens", true, SandboxHelp(), SandboxButton()));

        public static LayoutNode Bonuses_Scene() => Root(Card("Scene Reference", SceneRefHelp(), SceneRefField()));
        public static LayoutNode Bonuses_SasqString() => Root(
            Card("SasquatchString", SasqStringHelp(), SasqAmmoField(), SasqElementField(), SasqStringField()));
        public static LayoutNode Bonuses_Spring() => Root(Card("Spring (SpringConfig)", SpringHelp(), SpringConfigField()));
        public static LayoutNode Bonuses_Dictionary() => Root(
            Card("Serializable Dictionary", DictHelp(), DictWeightsField(), DictColorsField()),
            Card("Dictionary Styles", DictStylesHelp(),
                DictStyleField("styleTable", DictionaryTableStyle.Table, "Table"),
                DictStyleField("styleMinimal", DictionaryTableStyle.Minimal, "Minimal"),
                DictStyleField("styleLedger", DictionaryTableStyle.Ledger, "Ledger"),
                DictStyleField("styleMapping", DictionaryTableStyle.Mapping, "Mapping")));
#if UNITY_6000_6_OR_NEWER
        public static LayoutNode Bonuses_NativeDictionary() => Root(
            Card("Dictionary Table (native)", NativeDictHelp(), NativeDictField()));
#endif
        public static LayoutNode Bonuses_Sandbox() => Root(Card("Spring Physics Tweens", SandboxHelp(), SandboxButton()));

        private static HelpBoxNode SceneRefHelp() => new HelpBoxNode
        {
            Message = "A **SceneReference** field — a safe, serialized way to reference a Scene asset. " +
                      "It survives renames/moves (it stores the GUID) and exposes the path + name at runtime. " +
                      "*Assign a scene that isn't in your Build Settings* and the field warns you, with a one-click " +
                      "**Add to Build Settings** quick-fix.",
            MessageType = HelpBoxMessageType.Info
        };
        private static BindingNode SceneRefField() =>
            new BindingNode { PropertyPath = "targetScene", Header = "Scene Reference" };

        private static HelpBoxNode SasqStringHelp() => new HelpBoxNode
        {
            Message = "A **SasquatchString** — a drop-in replacement for `string` whose `{fieldName}` tokens " +
                      "resolve to *live values* everywhere, not just in this preview. Change the field's type " +
                      "and `label.text = weapon.description;` prints **\"Deals 12 Fire damage.\"** at runtime — " +
                      "TextMeshPro, uGUI, UI Toolkit, `Debug.Log`, plain concatenation, all of it. *No component " +
                      "to add, nothing to install.* Edit **Ammo** or **Element** below and watch the preview move.",
            MessageType = HelpBoxMessageType.Info
        };
        private static BindingNode SasqAmmoField() => new BindingNode { PropertyPath = "ammo", LabelOverride = "Ammo" };
        private static BindingNode SasqElementField() => new BindingNode { PropertyPath = "element", LabelOverride = "Element" };
        // In the designer a SasquatchString IS a Text field — same toolbar, same live preview, same options.
        private static BindingNode SasqStringField()
        {
            var b = new BindingNode
            {
                PropertyPath = "description", DisplayType = DisplayType.Text, LabelOverride = "Description",
                RichTextPreview = true, RichTextToolbar = true
            };
            b.TextResize = TextResizeMode.Resizable;
            return b;
        }

        private static HelpBoxNode SpringHelp() => new HelpBoxNode
        {
            Message = "A **SpringConfig** rendered with the custom **Spring** display type — tweak Duration / Bounce " +
                      "and watch the response graph, then *drag the dot and release* to feel the spring. A SpringConfig " +
                      "field always uses this display type.",
            MessageType = HelpBoxMessageType.Info
        };
        private static BindingNode SpringConfigField() =>
            new BindingNode { PropertyPath = "spring", DisplayType = DisplayType.Spring, Header = "Spring" };

        private static HelpBoxNode DictHelp() => new HelpBoxNode
        {
            Message = "A `SerializedDictionary<K,V>` — a bonus serializable type with a first-class drawer. Unity " +
                      "serializes `Dictionary<,>` itself from **6.6**; this one works back to **6.0**, nests inside a " +
                      "`List<>`, and saves the order you drag rows into. Edit it as a **Key / Value table**: add, delete, " +
                      "*drag a row to reorder*, or right-click a row for more. **Duplicate keys are flagged live** (only the " +
                      "first of each is used at runtime). Keys and values can be *any* serializable type — note the string→int " +
                      "table and the enum→Color table below. At runtime it's a real `IDictionary<K,V>`.\n\n" +
                      "**The values are styled too.** The colour table's *value* column is set to the **Color " +
                      "Palette** display type — pick a swatch, don't hunt in the colour picker. That's a **cell " +
                      "template**: in the Layout Designer, press **+KV** on a dictionary in the layout tree and " +
                      "you get a *Key* and a *Value* binding underneath it, each with the full Display Type list " +
                      "for that half's C# type. Style either, both, or neither.",
            MessageType = HelpBoxMessageType.Info
        };
        // Plain bindings (no Header): the SerializedDictionaryDrawer renders its own titled group, like SceneReference.
        private static BindingNode DictWeightsField() => new BindingNode { PropertyPath = "lootWeights" };

        // THE CELL-TEMPLATE DEMO, and the reason it is on this field rather than a new page: the very first
        // question a dictionary raises is "can I style what is INSIDE it?", and the honest answer is easier
        // shown than written. rarityColors is a SerializedDictionary<ItemRarity, Color>, so its VALUE cell
        // takes a Color Palette exactly as a standalone Color field would - same widget, same options.
        //
        // The KEY half is deliberately left untemplated. Both halves are independent, and seeding one to
        // prove that would only make the row busier; the help card says so in a sentence instead.
        private static BindingNode DictColorsField() => new BindingNode
        {
            PropertyPath = "rarityColors",
            DictionaryValueTemplate = DictColorCellTemplate(),
        };

        // An EMPTY property path is the package-wide "bind the thing the scope already points at" convention
        // - here, the row's value. The Label override is what the DESIGNER'S HIERARCHY names the row; the
        // rendered cell force-hides it, because the column header already says "Value".
        private static BindingNode DictColorCellTemplate() => new BindingNode
        {
            PropertyPath = "",
            LabelOverride = "Value",
            DisplayType = DisplayType.ColorPalette,
        };

#if UNITY_6000_6_OR_NEWER
        private static HelpBoxNode NativeDictHelp() => new HelpBoxNode
        {
            Message = "This one is **Unity's own** `Dictionary<ItemRarity, Color>` — not the bonus type — with " +
                      "its **Display Type** set to **Dictionary Table**. Unity 6.6 serializes it (note the " +
                      "`[SerializeField]`, which its backend requires *even on a public field*), and *Default* " +
                      "would draw Unity's own two-column view. Dictionary Table is the opt-in upgrade on top of " +
                      "that: **drag a row to reorder** and the order is saved, a **right-click row menu**, and " +
                      "**real value editors** — the colour swatches below are the same ones a Sasquatch Color " +
                      "field draws. Duplicate and empty keys are flagged from Unity's own " +
                      "`GetDictionaryIgnoredEntries()`, so a warning here is exactly a row Unity will discard. " +
                      "*Dictionary Style* and *Row Frame* shape this table too — and so do **cell templates**: " +
                      "this field's *value* column carries the same **Color Palette** template as the bonus " +
                      "`SerializedDictionary` above, because both shapes read one pair of model fields through " +
                      "one renderer.",
            MessageType = HelpBoxMessageType.Info
        };
        // The whole point of the demo: an explicit DisplayType.Dictionary on a NATIVE Dictionary field. A bare
        // node here would render Unity's stock DictionaryView instead and demonstrate nothing.
        private static BindingNode NativeDictField() =>
            new BindingNode
            {
                PropertyPath = "nativeRarityColors",
                DisplayType = DisplayType.Dictionary,
                // The SAME cell template the bonus type above gets. One pair of model fields drives both
                // shapes through one renderer, and showing it on both is what makes that claim checkable
                // rather than something the manual merely asserts.
                DictionaryValueTemplate = DictColorCellTemplate(),
            };
#endif

        private static HelpBoxNode DictStylesHelp() => new HelpBoxNode
        {
            Message = "**Dictionary Style** shapes the table, exactly the way a `List<>`'s **List Style** shapes a " +
                      "collection — so a dictionary and a list in the same inspector read as siblings. Four styles, " +
                      "shown below on the same kind of data. **Row Frame** (None / Line / Card) is a *separate* " +
                      "setting for the row surface, and every style works with every frame. Both live in " +
                      "*Display Style & Options*, and both apply to a native Unity 6.6 `Dictionary` on the " +
                      "**Dictionary Table** display type as well as to the bonus `SerializedDictionary` shown here.",
            MessageType = HelpBoxMessageType.Info
        };

        // One field per style: Dictionary Style is a per-binding setting, so four looks need four bindings.
        // Row Frame is left at None throughout on purpose — the point of this card is the STYLE axis alone,
        // and mixing both axes in one card would make it impossible to tell which setting did what.
        private static BindingNode DictStyleField(string path, DictionaryTableStyle style, string label) =>
            new BindingNode
            {
                PropertyPath = path,
                LabelOverride = label,
                DictionaryStyle = style,
            };

        private static HelpBoxNode SandboxHelp() => new HelpBoxNode
        {
            Message = "**SasquatchSpring** is a bonus, *spring-physics driven* tween library bundled with this " +
                      "tool — fluid, mashable `MoveTo` / `ScaleTo` / `RotateTo` animations with no coroutines. " +
                      "Open the sandbox to feel the springs and copy the exact code.",
            MessageType = HelpBoxMessageType.Info
        };
        private static ButtonNode SandboxButton() =>
            new ButtonNode { MethodName = "OpenSpringSandbox", ButtonText = "Open Spring Physics Sandbox" };

        // ============================================================ Helpers ==========

        // The default page root: a plain Rows container holding its children directly, with NO Script Header bar.
        // The Script Header only appears on the Common Fields pages that need it (see HeaderRoot).
        private static ContainerNode Root(params LayoutNode[] children)
        {
            var root = new ContainerNode { Type = ContainerType.Rows };
            foreach (var child in children) root.Children.Add(child);
            return root;
        }

        // A page root that DOES open with the Script Header bar — the Common Fields page, its dedicated Script
        // Header child, and the Field Search child (whose search box lives IN the header).
        private static ContainerNode HeaderRoot(params LayoutNode[] children)
        {
            var root = new ContainerNode { Type = ContainerType.Rows };
            root.Children.Add(new ScriptHeaderNode { TextOverride = "Script Header" });
            foreach (var child in children) root.Children.Add(child);
            return root;
        }

        private static ContainerNode Card(string title, params LayoutNode[] children)
        {
            var card = new ContainerNode { Type = ContainerType.Card, Title = title };
            foreach (var child in children) card.Children.Add(child);
            return card;
        }

        // A Card that renders inside the collapsible .sasq-group frame (header + chevron) so it can be
        // minimized. expanded seeds the initial open/closed state.
        private static ContainerNode ExpandableCard(string title, bool expanded, params LayoutNode[] children)
        {
            var card = new ContainerNode { Type = ContainerType.Card, Title = title, Expandable = true, DefaultToOpen = expanded };
            foreach (var child in children) card.Children.Add(child);
            return card;
        }

        // Wraps a container in its own open-by-default expandable Card so the Containers parent page reads as a
        // tidy stack of collapsible sections. Plain containers (Rows/Columns/Grid/Card) hand their Title to the
        // card to avoid a duplicate heading; Foldout/Tabs keep their own header (it's part of what they show).
        private static ContainerNode WrapContainerCard(ContainerNode inner, string fallbackTitle)
        {
            string title = !string.IsNullOrEmpty(inner.Title) ? inner.Title : fallbackTitle;
            bool plain = inner.Type == ContainerType.Rows || inner.Type == ContainerType.Columns
                         || inner.Type == ContainerType.Grid || inner.Type == ContainerType.Card;
            if (plain) inner.Title = null;
            return ExpandableCard(title, true, inner);
        }

        // The note for the computed property: it has no backing field and recomputes from health/invincible.
        private static HelpBoxNode ComputedNote() => new HelpBoxNode
        {
            Message = "Power Rating is computed in C# (no stored field). It updates live as you change Health or toggle Invincible (x2 while invincible).",
            MessageType = HelpBoxMessageType.Info
        };

        private static ContainerNode Container(ContainerType type, params LayoutNode[] children)
        {
            var container = new ContainerNode { Type = type };
            foreach (var child in children) container.Children.Add(child);
            return container;
        }

        private static BindingNode Bind(string path) => new BindingNode { PropertyPath = path };
        private static BindingNode Bind(string path, DisplayType widget) => new BindingNode { PropertyPath = path, DisplayType = widget };

        private static OverridableFloat OverFloat(float v) => new OverridableFloat { isOverridden = true, value = v };
        private static OverridableColor OverColor(Color v) => new OverridableColor { isOverridden = true, value = v };
        private static OverridableEnum<T> OverEnum<T>(T v) where T : struct, Enum =>
            new OverridableEnum<T> { isOverridden = true, value = v };
    }
}
