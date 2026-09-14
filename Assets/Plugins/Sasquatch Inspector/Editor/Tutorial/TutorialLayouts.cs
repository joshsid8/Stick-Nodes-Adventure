using SasquatchInspector.Runtime;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Hand-built layout trees for the Tutorial's live previews of <see cref="SasquatchInspector.Samples.PlayerStats"/>:
    /// a "Before" (flat, default-widget) layout that mimics the stock inspector, and an "After" layout that groups
    /// the fields into color-coded cards with hand-picked widgets — a rich-text description, a Grip-knob slider,
    /// a health bar, enum chips, an expandable acceleration curve, a color-swatch bar, and a toggle that reveals a
    /// conditional field. Mirrors the node-builder idioms in <c>ShowcaseLayouts</c>, kept local so that file's
    /// internal helpers aren't widened. Property paths must match <c>PlayerStats</c>'s field names.
    /// </summary>
    internal static class TutorialLayouts
    {
        // Every PlayerStats field, flat, each with Unity's default widget + a Script Header bar — about as close
        // to the stock inspector as the builder gets. (Stock Unity shows the conditional field unconditionally.)
        public static LayoutNode PlayerStatsBefore()
        {
            var root = new ContainerNode { Type = ContainerType.Rows };
            root.Children.Add(new ScriptHeaderNode());
            root.Children.Add(Bind("displayName"));
            root.Children.Add(Bind("characterDescription"));
            root.Children.Add(Bind("level"));
            root.Children.Add(Bind("health"));
            root.Children.Add(Bind("moveSpeed"));
            root.Children.Add(Bind("characterAcceleration"));
            root.Children.Add(Bind("invincible"));
            root.Children.Add(Bind("invincibilityDuration"));
            root.Children.Add(Bind("characterClass"));
            root.Children.Add(Bind("tint"));
            return root;
        }

        // The same fields, grouped into color-coded cards with hand-picked widgets — the "after the makeover" look.
        // Each card's TITLE is the section header, so the leading field's own code [Header] (displayName's
        // [Header("Identity")], health's [Header("Vitals")], moveSpeed's [Header("Appearance")]) is suppressed by
        // the Card helper — otherwise the section name would render twice (title + inner decorator). This mirrors
        // what Smart Attribute Import does when it promotes a [Header] to a container title. Invincibility Duration
        // is condition-gated so it only appears once Invincible is toggled on — a live demo of the condition system.
        public static LayoutNode PlayerStatsAfter()
        {
            var root = new ContainerNode { Type = ContainerType.Rows };
            root.Children.Add(new ScriptHeaderNode());

            root.Children.Add(Card("Identity", Rgb(0.30f, 0.55f, 0.85f),
                Bind("displayName"),
                Description(),
                LevelSlider(),
                EnumChips("characterClass")));

            root.Children.Add(Card("Vitals", Rgb(0.80f, 0.34f, 0.34f),
                HealthBar(),                      // lead field: its [Header("Vitals")] is blanked by Card
                RoundedToggle("invincible"),
                InvincibilityDuration()));        // revealed only when Invincible is on

            root.Children.Add(Card("Appearance", Rgb(0.36f, 0.70f, 0.42f),
                Bind("moveSpeed"),
                AccelerationCurve(),
                TintSwatch()));

            return root;
        }

        // ---- node builders ----

        private static BindingNode Bind(string path, DisplayType widget = DisplayType.Default) =>
            new BindingNode { PropertyPath = path, DisplayType = widget };

        // A rich-text Text field: the live formatted preview turns the description's bold/italic/color tags into
        // styled text, with the formatting toolbar available.
        private static BindingNode Description() => new BindingNode
        {
            PropertyPath = "characterDescription",
            DisplayType = DisplayType.Text,
            RichTextPreview = true,
            RichTextToolbar = true
        };

        // A fully-styled Grip-knob slider for the [Range] level — the "mixer-fader" look.
        private static BindingNode LevelSlider()
        {
            var b = new BindingNode { PropertyPath = "level", DisplayType = DisplayType.Slider };
            b.DisplayStyleData = new DisplayStyleData
            {
                sliderKnobShape = SliderKnobShape.Grip,
                sliderSize = SliderSize.Large,
                sliderBarColor = new Color(0.30f, 0.55f, 0.85f, 1f),
                sliderKnobColor = new Color(0.82f, 0.88f, 0.97f, 1f)
            };
            return b;
        }

        private static BindingNode EnumChips(string path) =>
            new BindingNode { PropertyPath = path, DisplayType = DisplayType.EnumBadgeGrid };

        private static BindingNode HealthBar() => new BindingNode
        {
            PropertyPath = "health", DisplayType = DisplayType.ProgressBar,
            ProgressBarTextMode = ProgressBarTextMode.ValueAndMax
        };

        private static BindingNode RoundedToggle(string path)
        {
            var b = new BindingNode { PropertyPath = path, DisplayType = DisplayType.Toggle };
            b.DisplayStyleData = new DisplayStyleData { toggleStyle = ToggleStyle.Rounded, toggleShowOnOff = true };
            return b;
        }

        // Conditional: only relevant while the character is invincible, so it stays hidden until the toggle is on.
        private static BindingNode InvincibilityDuration()
        {
            var b = new BindingNode { PropertyPath = "invincibilityDuration", DisplayType = DisplayType.Slider };
            b.DisplayStyleData = new DisplayStyleData
            {
                sliderKnobShape = SliderKnobShape.Capsule,
                sliderBarColor = new Color(0.80f, 0.34f, 0.34f, 1f),
                sliderKnobColor = new Color(0.96f, 0.82f, 0.55f, 1f)
            };
            b.Conditions.Add(new VisualCondition("invincible", ConditionOperator.IsTrue,
                outcome: ConditionOutcome.Show));
            return b;
        }

        // An expandable animation-curve well with an inline preview — collapsed by default (the preview still
        // shows when closed). Labelled by the field name ("Character Acceleration").
        private static BindingNode AccelerationCurve() => new BindingNode
        {
            PropertyPath = "characterAcceleration",
            DisplayType = DisplayType.AnimationCurveEditor,
            CurveEditorMode = CurveEditorMode.ExpandableWithPreview,
            WidgetDefaultToOpen = false
        };

        private static BindingNode TintSwatch() => new BindingNode
        {
            PropertyPath = "tint", DisplayType = DisplayType.ColorPalette,
            ColorPaletteLayout = ColorPaletteLayout.Bar, ColorBarPlacement = ColorBarPlacement.Below
        };

        private static ContainerNode Card(string title, Color tint, params LayoutNode[] children)
        {
            var card = new ContainerNode { Type = ContainerType.Card, Title = title };
            card.SectionTintColor = new OverridableColor { isOverridden = true, value = tint };
            foreach (var c in children) card.Children.Add(c);
            // The card title IS the section header — blank the leading field's code [Header] so the section
            // name isn't rendered twice (harmless no-op for a leading field that carries no [Header]).
            if (card.Children.Count > 0 && card.Children[0] is BindingNode lead)
                lead.Header = LayoutNode.HiddenSentinel;
            return card;
        }

        private static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);
    }
}
