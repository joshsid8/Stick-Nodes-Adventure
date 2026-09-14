using System;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The shipped recommended defaults for a <see cref="SasquatchLayoutPreset"/> — what a buyer's preset holds
    /// the first time it is created, and what "Reset to Recommended Defaults" restores. Used to seed a fresh
    /// preset and to reset a single row.
    ///
    /// <para>TWO SOURCES, in order:</para>
    /// <list type="number">
    /// <item>The BAKED payload in <see cref="LayoutPresetBakedDefaults"/> — a real preset asset, tuned visually in
    /// the Preset Editor and then captured whole (see <see cref="LayoutPresetBakeCodec"/>). This is what ships
    /// once baked, and it is the only source that can carry a fully configured look: colors, sizes, box model,
    /// collection-context overrides and per-widget styling, none of which survive a hand-written switch.</item>
    /// <item>The in-code table below (<see cref="BuildFallbackTemplate"/>) — widget CHOICE only, no styling. It
    /// stands in when nothing is baked, and it backs any category the payload doesn't cover, so a newly appended
    /// <see cref="PresetCategory"/> always has a sane default even before the next bake.</item>
    /// </list>
    ///
    /// <para>Every default widget must be valid for its category's representative type. The in-code table is
    /// hand-checked; a baked template is validated on decode (<see cref="Validate"/>) because a payload captured
    /// in a full project can name a widget that a module-stripped or older install cannot offer.</para>
    /// </summary>
    public static class LayoutPresetDefaults
    {
        /// <summary>Resets <paramref name="preset"/> to the shipped defaults — all four tiers: per-type templates,
        /// collection-context overrides, per-widget styling, and the two structural container defaults.</summary>
        public static void Populate(SasquatchLayoutPreset preset)
        {
            if (preset == null) return;

            var entries = preset.Entries;
            entries.Clear();
            foreach (PresetCategory c in Enum.GetValues(typeof(PresetCategory)))
            {
                entries.Add(new LayoutPresetEntry
                {
                    category = c,
                    enabled = EnabledByDefault(c),
                    template = BuildTemplate(c)
                });
            }

            // The other three tiers are wholly owned by the payload: with nothing baked they reset to empty /
            // to the model's own initializers.
            preset.ContextEntries.Clear();
            preset.WidgetEntries.Clear();
            preset.DefaultHeaderContainerType = LayoutPresetBakedDefaults.HeaderContainer;
            preset.DefaultRootContainerType = LayoutPresetBakedDefaults.RootContainer;

            foreach (var row in LayoutPresetBakedDefaults.Contexts)
            {
                var template = Validate(LayoutPresetBakeCodec.DecodeBinding(row.Payload), row.Category);
                if (template == null) continue;
                preset.ContextEntries.Add(new ContextPresetEntry
                {
                    category = row.Category,
                    context = row.Context,
                    enabled = row.Enabled,
                    template = template
                });
            }

            foreach (var row in LayoutPresetBakedDefaults.Widgets)
            {
                var template = LayoutPresetBakeCodec.Decode(row.Payload);
                if (template == null) continue;
                preset.WidgetEntries.Add(new WidgetPresetEntry
                {
                    kind = row.Kind,
                    enabled = row.Enabled,
                    template = template
                });
            }
        }

        /// <summary>The shipped default template (widget + full configuration) for a single category — the baked
        /// one when the payload covers it, otherwise the in-code fallback. Always a fresh, independent node.</summary>
        public static BindingNode BuildTemplate(PresetCategory c)
        {
            if (TryFindBakedType(c, out var row))
            {
                var baked = Validate(LayoutPresetBakeCodec.DecodeBinding(row.Payload), c);
                if (baked != null) return baked;
            }
            return BuildFallbackTemplate(c);
        }

        /// <summary>Whether <paramref name="c"/> ships switched ON. Baked rows carry their own state (a category
        /// deliberately turned off in the Preset Editor ships off); anything unbaked defaults to on.</summary>
        public static bool EnabledByDefault(PresetCategory c) =>
            !TryFindBakedType(c, out var row) || row.Enabled;

        /// <summary>
        /// The in-code fallback table: the recommended WIDGET for a category, with no styling. Used when nothing
        /// is baked and as the backstop for a category the payload doesn't cover.
        /// </summary>
        public static BindingNode BuildFallbackTemplate(PresetCategory c)
        {
            var node = new BindingNode();
            switch (c)
            {
                case PresetCategory.String:
                    node.DisplayType = DisplayType.Text;
                    node.RichTextPreview = false; // plain styled text box; rich-text is opt-in per field
                    break;
                case PresetCategory.Bool:
                    node.DisplayType = DisplayType.Toggle;
                    break;
                case PresetCategory.Enum:
                    node.DisplayType = DisplayType.DropDown;
                    node.SearchableDropdown = true;
                    node.DropdownSource = DropdownSource.Auto;
                    break;
                case PresetCategory.FlagsEnum:
                    node.DisplayType = DisplayType.EnumBadgeGrid;
                    node.EnumBadgeShowAllNone = true;
                    break;
                case PresetCategory.LayerMask:
                    node.DisplayType = DisplayType.DropDown;
                    node.DropdownSource = DropdownSource.LayerMask;
                    break;
                case PresetCategory.AnimationCurve:
                    node.DisplayType = DisplayType.AnimationCurveEditor;
                    break;
                case PresetCategory.Gradient:
                    node.DisplayType = DisplayType.Gradient;
                    break;
                case PresetCategory.Dictionary:
                    // DEFAULT, not DisplayType.Dictionary. A SerializedDictionary auto-routes to the styled
                    // table on Default (on every editor), while DisplayType.Dictionary applies only to a
                    // NATIVE Dictionary and is not even offered before 6.6 — shipping it as the recommended
                    // default would seed a preset that pre-6.6 buyers cannot use. What this category exists
                    // to carry is the STYLE pair below, and those ride on the node regardless of kind.
                    node.DisplayType = DisplayType.Default;
                    node.DictionaryStyle = DictionaryTableStyle.Table;
                    node.DictionaryRowSeparator = CollectionRowSeparator.None;
                    break;
                case PresetCategory.AudioClip:
                    node.DisplayType = DisplayType.AudioPreview;
                    break;
                case PresetCategory.Texture:
                case PresetCategory.Sprite:
                case PresetCategory.Mesh:
                case PresetCategory.Material:
                case PresetCategory.GameObject:
                    node.DisplayType = DisplayType.AssetPreview;
                    break;
#if SASQ_PARTICLES
                case PresetCategory.ParticleSystem:
                    node.DisplayType = DisplayType.ParticleSystemPreview;
                    break;
#endif
                case PresetCategory.UnityEvent:
                    node.DisplayType = DisplayType.UnityEvent;
                    break;
                // Int / Float / Double / Color / Vector2-4 / ManagedReference / ObjectReference keep
                // DisplayType.Default: no safe blanket non-default (Slider needs [Range], MinMaxSlider is a range
                // semantic, ColorPalette needs a palette id), and managed refs auto-route to the polymorphic
                // picker at build time anyway.
                default:
                    node.DisplayType = DisplayType.Default;
                    break;
            }
            return Validate(node, c);
        }

        private static bool TryFindBakedType(PresetCategory c, out BakedTypeRow found)
        {
            var rows = LayoutPresetBakedDefaults.Types;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Category != c) continue;
                found = rows[i];
                return true;
            }
            found = default;
            return false;
        }

        /// <summary>
        /// Guarantees a default template is renderable HERE. A payload is baked in a full dev project, but it
        /// ships into installs that may be missing an optional module (no audio ⇒ no AudioClip type at all, no
        /// particles ⇒ no ParticleSystemPreview) or running a different editor line. A widget the catalog won't
        /// offer for the category's own type would render as nothing, so it degrades to the plain Unity field —
        /// the one choice that is always valid. Silent and deterministic: nothing is broken, and the moment the
        /// module is present the baked widget applies again.
        /// </summary>
        private static BindingNode Validate(BindingNode node, PresetCategory c)
        {
            if (node == null) return null;
            if (node.DisplayType == DisplayType.Default) return node;

            var representative = LayoutPresetCategories.RepresentativeType(c);
            if (representative == null || !DisplayTypeCatalog.OfferedForField(node.DisplayType, representative).Contains(node.DisplayType))
                node.DisplayType = DisplayType.Default;

            return node;
        }
    }
}
