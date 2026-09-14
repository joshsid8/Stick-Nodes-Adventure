using System;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The registry behind the Presets editor's "Widgets" section and the widget-drop preset hook: for each
    /// <see cref="WidgetPresetKind"/> it maps a friendly name + palette icon, the palette drop-type string
    /// (e.g. <c>"Container:Card"</c>) used to match a dropped widget, and a factory for a fresh default node.
    /// The widget analog of <see cref="Presets.LayoutPresetCategories"/> for field-type presets. Order here is
    /// the display order in the Widgets list (elements first, then containers). Embedded Editor and C# Property
    /// are intentionally absent — they have nothing meaningful to style.
    /// </summary>
    internal static class WidgetPresetCatalog
    {
        internal struct Entry
        {
            public WidgetPresetKind Kind;
            public string DisplayName;
            public string IconId;
            public string PaletteType;
            public Func<LayoutNode> Factory;
        }

        private static Entry Make(WidgetPresetKind kind, string name, string icon, string paletteType,
            Func<LayoutNode> factory) =>
            new Entry { Kind = kind, DisplayName = name, IconId = icon, PaletteType = paletteType, Factory = factory };

        // Labels + icons mirror WidgetPaletteItems so the preset list reads the same as the drag palette.
        internal static readonly Entry[] All =
        {
            Make(WidgetPresetKind.Button, "Button", SasquatchIcons.Ids.MousePointerClick, "Button",
                () => new ButtonNode()),
            Make(WidgetPresetKind.TextBlock, "Text Block", SasquatchIcons.Ids.AlignLeft, "TextBlock",
                () => new TextBlockNode()),
            Make(WidgetPresetKind.ImageBanner, "Image", SasquatchIcons.Ids.GalleryVertical, "ImageBanner",
                () => new ImageBannerNode()),
            Make(WidgetPresetKind.HelpBox, "Info/Warning", SasquatchIcons.Ids.TriangleAlert, "HelpBox",
                () => new HelpBoxNode()),
            Make(WidgetPresetKind.LabeledDivider, "Divider", SasquatchIcons.Ids.Ruler, "LabeledDivider",
                () => new LabeledDividerNode()),
            Make(WidgetPresetKind.Space, "Space", SasquatchIcons.Ids.ChevronsUpDown, "Space",
                () => new SpaceNode()),
            Make(WidgetPresetKind.ScriptHeader, "Script Header", SasquatchIcons.Ids.FileCode, "ScriptHeader",
                () => new ScriptHeaderNode()),
            Make(WidgetPresetKind.ContainerRows, "Rows", SasquatchIcons.Ids.Rows3, "Container:Rows",
                () => new ContainerNode { Type = ContainerType.Rows }),
            Make(WidgetPresetKind.ContainerColumns, "Columns", SasquatchIcons.Ids.Columns3,
                "Container:Columns", () => new ContainerNode { Type = ContainerType.Columns }),
            Make(WidgetPresetKind.ContainerGrid, "Grid", SasquatchIcons.Ids.LayoutGrid, "Container:Grid",
                () => new ContainerNode { Type = ContainerType.Grid }),
            Make(WidgetPresetKind.ContainerCard, "Card", SasquatchIcons.Ids.RectangleHorizontal,
                "Container:Card", () => new ContainerNode { Type = ContainerType.Card }),
            Make(WidgetPresetKind.ContainerFoldout, "Foldout", SasquatchIcons.Ids.ListCollapse,
                "Container:Foldout", () => new ContainerNode { Type = ContainerType.Foldout }),
            Make(WidgetPresetKind.ContainerTabs, "Tabs", SasquatchIcons.Ids.PanelsTopLeft,
                "Container:Tabs", () => new ContainerNode { Type = ContainerType.Tabs }),
            Make(WidgetPresetKind.ContainerFrame, "Frame", SasquatchIcons.Ids.Frame,
                "Container:Frame", () => new ContainerNode { Type = ContainerType.Frame }),
        };

        /// <summary>The registry entry for <paramref name="kind"/> (default-valued struct if somehow unknown).</summary>
        internal static Entry Get(WidgetPresetKind kind)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Kind == kind) return All[i];
            return default;
        }

        /// <summary>A fresh, unstyled default node for <paramref name="kind"/> (e.g. <c>new ButtonNode()</c>).</summary>
        internal static LayoutNode MakeDefaultNode(WidgetPresetKind kind)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Kind == kind) return All[i].Factory();
            return null;
        }

        /// <summary>Maps a palette drop-type string (e.g. <c>"Container:Card"</c>, <c>"Button"</c>) to its widget
        /// kind. False for palette items with no preset (Embedded Editor, C# Property, Custom Widget).</summary>
        internal static bool TryKindForPaletteType(string paletteType, out WidgetPresetKind kind)
        {
            if (!string.IsNullOrEmpty(paletteType))
                for (int i = 0; i < All.Length; i++)
                    if (All[i].PaletteType == paletteType) { kind = All[i].Kind; return true; }
            kind = default;
            return false;
        }
    }
}
