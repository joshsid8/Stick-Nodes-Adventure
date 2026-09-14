using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// The project's single library of color palettes. Instead of one ScriptableObject per palette, this asset
    /// holds a swappable LIST of named <see cref="Palette"/>s plus which one is the project default. The
    /// ColorPalette widget and the Color Palette window draw their swatches from here; a field references a
    /// palette by its stable <see cref="Palette.Id"/> (empty/unknown ⇒ the library default). Resolved as a
    /// project singleton (see SasquatchPaletteLibraryManager) and seeded with three curated default palettes so
    /// the feature works the moment the package is imported — no per-scene "dummy object" needed.
    /// </summary>
    public class SasquatchColorPaletteLibrary : ScriptableObject
    {
        /// <summary>A single named color entry within a <see cref="Palette"/>.</summary>
        [Serializable]
        public struct Swatch
        {
            public string name;
            public Color color;

            /// <summary>Creates a swatch with the given name and color.</summary>
            public Swatch(string name, Color color)
            {
                this.name = name;
                this.color = color;
            }
        }

        /// <summary>One named palette within the library: a stable id, a friendly name, and ordered swatches.</summary>
        [Serializable]
        public class Palette
        {
            [SerializeField] private string id = "";
            [SerializeField] private string name = "";
            [SerializeField] private List<Swatch> swatches = new();

            /// <summary>Parameterless constructor for serialization.</summary>
            public Palette() { }

            /// <summary>Creates a palette with a stable id, friendly name, and ordered swatches.</summary>
            public Palette(string id, string name, List<Swatch> swatches)
            {
                this.id = id;
                this.name = name;
                this.swatches = swatches ?? new List<Swatch>();
            }

            /// <summary>Stable identifier a field stores to reference this palette (survives renames + reorders).</summary>
            public string Id
            {
                get => id;
                set => id = value;
            }

            /// <summary>Friendly display name; falls back to the id when blank.</summary>
            public string Name
            {
                get => name;
                set => name = value;
            }

            /// <summary>The ordered swatches. Editor callers may mutate this directly, then mark the asset dirty.</summary>
            public List<Swatch> Swatches => swatches;

            /// <summary>What UI should show: the explicit <see cref="Name"/>, or the id when unset.</summary>
            public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
        }

        [SerializeField] private List<Palette> palettes = new();
        [SerializeField] private string defaultPaletteId = "";

        /// <summary>All palettes in the library. Editor callers may mutate this directly, then mark the asset dirty.</summary>
        public List<Palette> Palettes => palettes;

        /// <summary>Id of the palette used by ColorPalette fields that don't reference one explicitly.</summary>
        public string DefaultPaletteId
        {
            get => defaultPaletteId;
            set => defaultPaletteId = value;
        }

        /// <summary>The palette with the given id, or null when <paramref name="id"/> is blank/unknown.</summary>
        public Palette GetPalette(string id)
        {
            if (string.IsNullOrEmpty(id) || palettes == null) return null;
            for (int i = 0; i < palettes.Count; i++)
                if (palettes[i] != null && palettes[i].Id == id) return palettes[i];
            return null;
        }

        /// <summary>The default palette (resolves <see cref="DefaultPaletteId"/>), falling back to the first one.</summary>
        public Palette DefaultPalette
        {
            get
            {
                var byId = GetPalette(defaultPaletteId);
                if (byId != null) return byId;
                return (palettes != null && palettes.Count > 0) ? palettes[0] : null;
            }
        }

        // Seeded when the library is first created through the Create/Reset menu (Unity calls Reset on creation)
        // so it's never empty. The manager seeds the same set explicitly for its transient in-memory instance
        // and for the on-disk asset it creates (CreateInstance does not run Reset).
        private void Reset()
        {
            if (palettes != null && palettes.Count > 0) return;
            palettes = DefaultPalettes();
            defaultPaletteId = DefaultPaletteIds.Nord;
        }

        /// <summary>Deterministic ids for the shipped default palettes — stable so saved field references resolve
        /// even against the transient (not-yet-saved) library. User-created palettes get a fresh GUID id.</summary>
        public static class DefaultPaletteIds
        {
            public const string Nord = "nord";
            public const string CatppuccinMocha = "catppuccin-mocha";
            public const string RosePine = "rose-pine";
        }

        /// <summary>The three curated, designer-friendly starter palettes (Nord, Catppuccin Mocha, Rosé Pine).
        /// Each stays within 6–10 colors and uses the palette's own canonical role names.</summary>
        public static List<Palette> DefaultPalettes()
        {
            return new List<Palette>
            {
                // Nord — cool, frosty (Polar Night / Frost / Aurora).
                new Palette(DefaultPaletteIds.Nord, "Nord", new List<Swatch>
                {
                    new Swatch("Polar Night",   Hex("#2E3440")),
                    new Swatch("Snow",          Hex("#ECEFF4")),
                    new Swatch("Frost Teal",    Hex("#8FBCBB")),
                    new Swatch("Frost Sky",     Hex("#88C0D0")),
                    new Swatch("Frost Blue",    Hex("#5E81AC")),
                    new Swatch("Aurora Red",    Hex("#BF616A")),
                    new Swatch("Aurora Orange", Hex("#D08770")),
                    new Swatch("Aurora Yellow", Hex("#EBCB8B")),
                    new Swatch("Aurora Green",  Hex("#A3BE8C")),
                    new Swatch("Aurora Purple", Hex("#B48EAD")),
                }),
                // Catppuccin Mocha — warm pastel dark.
                new Palette(DefaultPaletteIds.CatppuccinMocha, "Catppuccin Mocha", new List<Swatch>
                {
                    new Swatch("Base",   Hex("#1E1E2E")),
                    new Swatch("Text",   Hex("#CDD6F4")),
                    new Swatch("Red",    Hex("#F38BA8")),
                    new Swatch("Peach",  Hex("#FAB387")),
                    new Swatch("Yellow", Hex("#F9E2AF")),
                    new Swatch("Green",  Hex("#A6E3A1")),
                    new Swatch("Sky",    Hex("#89DCEB")),
                    new Swatch("Blue",   Hex("#89B4FA")),
                    new Swatch("Mauve",  Hex("#CBA6F7")),
                }),
                // Rosé Pine — muted, elegant.
                new Palette(DefaultPaletteIds.RosePine, "Rosé Pine", new List<Swatch>
                {
                    new Swatch("Base", Hex("#191724")),
                    new Swatch("Text", Hex("#E0DEF4")),
                    new Swatch("Love", Hex("#EB6F92")),
                    new Swatch("Gold", Hex("#F6C177")),
                    new Swatch("Rose", Hex("#EBBCBA")),
                    new Swatch("Pine", Hex("#31748F")),
                    new Swatch("Foam", Hex("#9CCFD8")),
                    new Swatch("Iris", Hex("#C4A7E7")),
                }),
            };
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }
    }
}
