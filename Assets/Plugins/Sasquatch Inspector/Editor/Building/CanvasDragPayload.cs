using System.Collections.Generic;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>Which surface a drag started from — routes the click/drag to the right selection set: the
    /// Canvas/Hierarchy share the main multi-selection, while the Organizer and the Bindings palette each own
    /// a drag-only selection (decoupled from <c>_selectedNode</c>, so selecting a card never navigates the
    /// Properties panel away from the open organizer).</summary>
    public enum DragOrigin { Canvas, Hierarchy, Organizer, Palette }

    /// <summary>Describes what a canvas drag carries — a unified payload so the controller drives node reordering, palette inserts, and collection-item moves through one path.</summary>
    public class CanvasDragPayload
    {
        /// <summary>What kind of drag this is (move a node, add a binding/widget, or move a collection item).</summary>
        public enum DragKind
        {
            MoveNode,        // Reorder/nest an existing LayoutNode on the canvas
            AddBinding,      // Drop a data token from the palette (creates Binding/Collection)
            AddWidget,       // Drop a widget from the palette (Container/Button/HelpBox)
            MoveCollectionItem
        }

        public DragKind Kind { get; private set; }

        // The surface this drag was picked up from (see DragOrigin). Drives selection routing.
        public DragOrigin Origin { get; private set; }

        // MoveNode
        public LayoutNode Node { get; private set; }

                // AddBinding -> token path; AddWidget -> widget type ("Container"/"Button"/"HelpBox")
        public string Path { get; private set; }

        // MoveCollectionItem
        public string CollectionPropPath { get; private set; }
        public int ItemIndex { get; private set; }

        // AddBinding BATCH: when set, this one drag drops MANY nodes at once (the Bindings-tab
        // "C# Properties" card → a PropertyNode for every property). The controller's ResolveAddGroup
        // returns this list so the drop commits through OnAddNodes (HandleMultiPropertyDrop).
        public IReadOnlyList<string> BatchPaths { get; private set; }

        // Chip display
        public string Icon { get; private set; }
        public string Label { get; private set; }

        /// <summary>Payload for reordering/nesting an existing <paramref name="node"/>.</summary>
        public static CanvasDragPayload Move(LayoutNode node, DragOrigin origin = DragOrigin.Canvas)
        {
            return new CanvasDragPayload
            {
                Kind = DragKind.MoveNode,
                Origin = origin,
                Node = node,
                Icon = NodeIcon(node),
                Label = NodeDisplayName(node)
            };
        }

        /// <summary>Payload for dropping a data token from the palette (creates a Binding or Collection node).</summary>
        public static CanvasDragPayload AddBinding(string path, bool isCollection, DragOrigin origin = DragOrigin.Palette)
        {
            return new CanvasDragPayload
            {
                Kind = DragKind.AddBinding,
                Origin = origin,
                Path = path,
                // Vector icon ids (resolved by SasquatchIcons), matching the hierarchy rows the dropped node
                // becomes — Layers for a collection, Link for a binding. Must be real SasquatchIcons ids:
                // SasquatchIcons.Create renders an unrecognized id as a bare dot in the drag ghost.
                Icon = isCollection ? SasquatchIcons.Ids.Layers : SasquatchIcons.Ids.Link,
                Label = path
            };
        }

        // A C# property token dragged from the Bindings tab. Flows through the same AddBinding drop path as a
        // field token, but its "Property:<name>" path makes CreateNodeForPath build a PropertyNode.
        /// <summary>Payload for dropping a single C# property from the palette (creates a <see cref="PropertyNode"/>).</summary>
        public static CanvasDragPayload AddProperty(string propertyName, DragOrigin origin = DragOrigin.Palette)
        {
            return new CanvasDragPayload
            {
                Kind = DragKind.AddBinding,
                Origin = origin,
                Path = "Property:" + propertyName,
                Icon = SasquatchIcons.Ids.Crosshair,
                Label = propertyName
            };
        }

        // The Bindings-tab "C# Properties" card: dragging it drops a PropertyNode for every property the
        // card carries (the ones not in the layout yet) at once. Carries the per-property
        // "Property:<name>" paths as a batch (see BatchPaths).
        /// <summary>Payload for dropping many C# properties at once (one <see cref="PropertyNode"/> each).</summary>
        public static CanvasDragPayload AddProperties(List<string> propertyNames, DragOrigin origin = DragOrigin.Palette)
        {
            var paths = new List<string>(propertyNames != null ? propertyNames.Count : 0);
            if (propertyNames != null)
                foreach (var n in propertyNames) paths.Add("Property:" + n);
            return new CanvasDragPayload
            {
                Kind = DragKind.AddBinding,
                Origin = origin,
                Path = paths.Count > 0 ? paths[0] : null,
                BatchPaths = paths,
                Icon = SasquatchIcons.Ids.Crosshair,
                Label = paths.Count == 1 ? "1 Property" : $"All Properties ({paths.Count})"
            };
        }

        /// <summary>Payload for dropping a widget from the palette (Container / Button / HelpBox).</summary>
        public static CanvasDragPayload AddWidget(string displayType, string label)
        {
            return new CanvasDragPayload
            {
                Kind = DragKind.AddWidget,
                Path = "Widget:" + displayType,
                Icon = WidgetIcon(displayType),
                Label = label
            };
        }

        /// <summary>Payload for moving a collection element at <paramref name="index"/> within <paramref name="collectionPropPath"/>.</summary>
        public static CanvasDragPayload MoveItem(string collectionPropPath, int index)
        {
            return new CanvasDragPayload
            {
                Kind = DragKind.MoveCollectionItem,
                CollectionPropPath = collectionPropPath,
                ItemIndex = index,
                Icon = SasquatchIcons.Ids.Layers,
                Label = $"Item [{index}]"
            };
        }

        // Icon ids resolved by SasquatchIcons; the drag ghost renders them as vector icons.
        // Mappings mirror the hierarchy/widget palette so a dragged thing looks like its source row.
        private static string NodeIcon(LayoutNode node)
        {
            if (node is ContainerNode cn)
            {
                switch (cn.Type)
                {
                    case ContainerType.Rows: return SasquatchIcons.Ids.Rows3;
                    case ContainerType.Columns: return SasquatchIcons.Ids.Columns3;
                    case ContainerType.Grid: return SasquatchIcons.Ids.LayoutGrid;
                    case ContainerType.Foldout: return SasquatchIcons.Ids.ListCollapse;
                    case ContainerType.Tabs: return SasquatchIcons.Ids.PanelsTopLeft;
                    case ContainerType.Card: return SasquatchIcons.Ids.RectangleHorizontal;
                    case ContainerType.Frame: return SasquatchIcons.Ids.Frame;
                }
                return SasquatchIcons.Ids.Folder;
            }
            if (node is ButtonNode) return SasquatchIcons.Ids.Zap;
            if (node is HelpBoxNode) return SasquatchIcons.Ids.TriangleAlert;
            if (node is ScriptHeaderNode) return SasquatchIcons.Ids.FileCode;
            if (node is SpaceNode) return SasquatchIcons.Ids.ChevronsUpDown;
            if (node is LabeledDividerNode) return SasquatchIcons.Ids.Ruler;
            if (node is ImageBannerNode) return SasquatchIcons.Ids.GalleryVertical;
            if (node is TextBlockNode) return SasquatchIcons.Ids.AlignLeft;
            if (node is EmbeddedEditorNode) return SasquatchIcons.Ids.PencilRuler;
#if SASQ_CUSTOM_WIDGET
            if (node is CustomWidgetNode) return SasquatchIcons.Ids.Sparkles;
#endif
            if (node is PropertyNode) return SasquatchIcons.Ids.Crosshair;
            if (node is CollectionNode) return SasquatchIcons.Ids.Layers;
            return SasquatchIcons.Ids.Link;
        }

        private static string WidgetIcon(string type)
        {
            if (type.StartsWith("Container"))
            {
                if (type.Contains("Column")) return SasquatchIcons.Ids.Columns3;
                if (type.Contains("Row")) return SasquatchIcons.Ids.Rows3;
                if (type.Contains("Grid")) return SasquatchIcons.Ids.LayoutGrid;
                if (type.Contains("Foldout")) return SasquatchIcons.Ids.ListCollapse;
                if (type.Contains("Tabs")) return SasquatchIcons.Ids.PanelsTopLeft;
                if (type.Contains("Card")) return SasquatchIcons.Ids.RectangleHorizontal;
                if (type.Contains("Frame")) return SasquatchIcons.Ids.Frame;
                return SasquatchIcons.Ids.Folder;
            }
            switch (type)
            {
                case "Button": return SasquatchIcons.Ids.Zap;
                case "HelpBox": return SasquatchIcons.Ids.TriangleAlert;
                case "Space": return SasquatchIcons.Ids.ChevronsUpDown;
                case "LabeledDivider": return SasquatchIcons.Ids.Ruler;
                case "ImageBanner": return SasquatchIcons.Ids.GalleryVertical;
                case "TextBlock": return SasquatchIcons.Ids.AlignLeft;
                case "EmbeddedEditor": return SasquatchIcons.Ids.PencilRuler;
                default: return SasquatchIcons.Ids.Link;
            }
        }

        private static string NodeDisplayName(LayoutNode node)
        {
            if (node is BindingNode bn) return bn.EffectiveLabelOverride ?? bn.PropertyPath;
            if (node is CollectionNode cn) return cn.PropertyPath;
            if (node is ButtonNode btn) return string.IsNullOrEmpty(btn.ButtonText) ? "Execute Action" : btn.ButtonText;
            if (node is HelpBoxNode) return "Info/Warning";
            if (node is ScriptHeaderNode) return "Script Header";
            if (node is SpaceNode) return "Space";
            if (node is LabeledDividerNode ldn) return string.IsNullOrEmpty(ldn.Title) ? "Divider" : ldn.Title;
            if (node is ImageBannerNode) return "Image";
            if (node is TextBlockNode) return "Text Block";
            if (node is EmbeddedEditorNode) return "Embedded Editor";
#if SASQ_CUSTOM_WIDGET
            if (node is CustomWidgetNode) return "Custom Widget";
#endif
            if (node is PropertyNode pnode) return string.IsNullOrEmpty(pnode.LabelOverride)
                ? (string.IsNullOrEmpty(pnode.PropertyName) ? "C# Property" : pnode.PropertyName)
                : pnode.LabelOverride;
            if (node is ContainerNode c)
            {
                if (!string.IsNullOrEmpty(c.HierarchyName)) return c.HierarchyName; // designer-only cosmetic name
                if (!string.IsNullOrEmpty(c.Title)) return c.Title;
                if (!string.IsNullOrEmpty(c.PropertyPath)) return c.PropertyPath;
                return c.Type.ToString() + " Container";
            }
            return node != null ? node.GetType().Name : "Node";
        }
    }
}
