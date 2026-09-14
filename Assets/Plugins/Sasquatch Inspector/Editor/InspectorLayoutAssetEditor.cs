using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A clean, themed inspector for the per-type layout assets under <c>User Data/Layouts/</c>. Those files are
    /// normally driven entirely by the Layout Designer, and with no editor of their own they would show a
    /// developer the raw serialized graph in the Project window. So the layout asset gets one of its own: a
    /// friendly at-a-glance summary (target type, storage mode, sync ledger, options) plus a
    /// collapsible tree of the layout structure and a one-click "Open in Layout Designer" button. It renders with
    /// the tool's own skin/USS (<see cref="SasquatchSkin"/>) so it looks like everything else Sasquatch Inspector
    /// draws. Purely presentational — nothing here mutates or dirties the asset.
    /// </summary>
    [CustomEditor(typeof(InspectorLayoutAsset))]
    public class InspectorLayoutAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var asset = (InspectorLayoutAsset)target;

            var root = new VisualElement();
            root.style.marginTop = 4;
            root.style.marginBottom = 2;
            SasquatchSkin.ApplyWithWidgetSheet(root); // pull in SasquatchWidgets.uss + the --sasq-* token sheet

            // A compressed asset serializes Root as null; hydrate in memory (never dirties the file) so the
            // structure tree has a graph to walk.
            try { LayoutGraphStorage.Hydrate(asset); }
            catch (Exception e) { Debug.LogWarning("[Sasquatch Inspector] Couldn't hydrate layout for preview: " + e.Message); }

            var resolvedType = ResolveTargetType(asset);

            root.Add(BuildHeaderCard(asset, resolvedType));
            root.Add(BuildFactsCard(asset, resolvedType));
            root.Add(BuildOpenButton(resolvedType));
            root.Add(BuildStructureSection(asset));
            root.Add(BuildFooterNote());

            return root;
        }

        // ---- Header ------------------------------------------------------------------------------------------

        private static VisualElement BuildHeaderCard(InspectorLayoutAsset asset, Type resolvedType)
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            StyleAsCard(card, WidgetPalette.PanelWash);

            var icon = SasquatchIcons.Create(SasquatchIcons.Ids.PencilRuler, 22f, WidgetPalette.Accent);
            icon.style.marginRight = 10;
            icon.style.flexShrink = 0;
            card.Add(icon);

            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;

            var title = new Label(resolvedType != null ? resolvedType.Name : "Inspector Layout");
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = WidgetPalette.LabelBright;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            text.Add(title);

            var subtitle = new Label("Sasquatch Inspector · custom inspector layout");
            subtitle.style.fontSize = 11;
            subtitle.style.color = WidgetPalette.LabelMuted;
            subtitle.style.marginTop = 1;
            text.Add(subtitle);

            card.Add(text);
            return card;
        }

        // ---- Facts -------------------------------------------------------------------------------------------

        private static VisualElement BuildFactsCard(InspectorLayoutAsset asset, Type resolvedType)
        {
            var card = new VisualElement();
            StyleAsCard(card, WidgetPalette.WellBg);
            card.style.marginTop = 8;

            string typeDisplay = resolvedType != null
                ? (string.IsNullOrEmpty(resolvedType.Namespace) ? resolvedType.Name : resolvedType.Namespace + "." + resolvedType.Name)
                : (string.IsNullOrEmpty(asset.TargetTypeName) ? "Unresolved (script may be missing)" : ShortTypeName(asset.TargetTypeName));

            bool compressed = !string.IsNullOrEmpty(asset.CompressedGraph);
            string storage = compressed
                ? $"Compressed (~{Mathf.Max(1, Mathf.CeilToInt(asset.CompressedGraph.Length / 1024f))} KB blob)"
                : "Human-readable (plain graph)";

            int nodeCount = CountNodes(asset.Root);
            int synced = asset.SyncedFieldPaths != null ? asset.SyncedFieldPaths.Count : 0;

            card.Add(MakeFactRow("Target Type", typeDisplay, resolvedType == null ? WidgetPalette.LabelDim : (Color?)null));
            card.Add(MakeFactRow("Storage", storage));
            card.Add(MakeFactRow("Schema", "v" + asset.SchemaVersion));
            card.Add(MakeFactRow("Elements", nodeCount == 0 ? "—" : nodeCount + (nodeCount == 1 ? " node" : " nodes")));
            card.Add(MakeFactRow("Auto-synced", synced == 0 ? "None" : synced + (synced == 1 ? " field" : " fields")));
            card.Add(MakeFactRow("Subclasses", asset.ApplyToSubclasses ? "Inherit this layout" : "Not inherited"));
            card.Add(MakeFactRow("Takeover", asset.ForceTakeover ? "Replaces the type's own editor" : "Off"));
            // Hidden is worth a row of its own: a switched-off layout looks identical to a broken one from the
            // Inspector, and this asset is where someone lands when they go looking for why nothing is drawing.
            var hiddenRow = MakeFactRow("Inspector", asset.LayoutHidden ? "Hidden (layout switched off)" : "Showing",
                asset.LayoutHidden ? WidgetPalette.LabelDim : (Color?)null);
            hiddenRow.style.marginBottom = 0; // last row
            card.Add(hiddenRow);

            return card;
        }

        // ---- Open button -------------------------------------------------------------------------------------

        private static VisualElement BuildOpenButton(Type resolvedType)
        {
            var btn = new Button(() =>
            {
                if (resolvedType != null)
                    SasquatchLayoutDesignerWindow.OpenWindowForType(resolvedType, null);
                else
                    EditorUtility.DisplayDialog("Sasquatch Inspector",
                        "Couldn't resolve the target type for this layout — its script may have been renamed or removed.",
                        "OK");
            })
            { text = resolvedType != null ? "Open in Layout Designer" : "Target type not found" };

            btn.style.height = 28;
            btn.style.marginTop = 10;
            btn.style.marginLeft = 5;   // small inset so the (light) hover rise can't overflow the inspector
            btn.style.marginRight = 5;  // width and pop a horizontal scrollbar
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = Color.white;
            btn.style.backgroundColor = resolvedType != null ? WidgetPalette.BadgeAccent : WidgetPalette.ControlBg;
            btn.SetEnabled(resolvedType != null);
            // Calm, low-overshoot "Lift" instead of the default bouncy ButtonSpring (hoverScale 1.12): the big
            // bounce overshot the full-width button and popped a horizontal scrollbar into the inspector.
            if (resolvedType != null) EditorSpringFX.ApplyButtonHover(btn, ButtonHoverAnimation.Lift);
            return btn;
        }

        // ---- Structure tree ----------------------------------------------------------------------------------

        private static VisualElement BuildStructureSection(InspectorLayoutAsset asset)
        {
            var content = new VisualElement();
            content.style.paddingTop = 2;
            content.style.paddingBottom = 2;

            if (asset.Root == null)
            {
                var empty = new Label("No layout graph stored.");
                empty.style.color = WidgetPalette.LabelDim;
                empty.style.fontSize = 11;
                content.Add(empty);
            }
            else
            {
                int budget = 500; // guard against a pathologically large graph swamping the inspector
                AppendNode(content, asset.Root, 0, ref budget);
                if (budget <= 0)
                {
                    var more = new Label("… (structure truncated)");
                    more.style.color = WidgetPalette.LabelDim;
                    more.style.fontSize = 10;
                    more.style.marginTop = 2;
                    content.Add(more);
                }
            }

            string key = FoldoutStateStore.Key(asset, "layout-asset-structure", "Layout Structure");
            // A standalone card in this asset's own inspector, not a row in a styled type's run of widgets.
            var group = BindingFactory.BuildExpandableGroup("Layout Structure", null, content, key,
                fallbackExpanded: true, listRowEligible: false);
            group.style.marginTop = 8;
            return group;
        }

        private static void AppendNode(VisualElement column, LayoutNode node, int depth, ref int budget)
        {
            if (node == null || budget <= 0) return;
            budget--;
            column.Add(BuildNodeRow(node, depth));

            if (node is ContainerNode container && container.Children != null)
            {
                foreach (var child in container.Children)
                {
                    if (budget <= 0) break;
                    AppendNode(column, child, depth + 1, ref budget);
                }
            }
        }

        private static VisualElement BuildNodeRow(LayoutNode node, int depth)
        {
            Describe(node, out string iconId, out string label, out string detail);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginLeft = 2 + depth * 14;
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;

            var icon = SasquatchIcons.Create(iconId, 12f); // default tint + size 12, matching the Hierarchy rows
            icon.style.marginRight = 6;
            icon.style.flexShrink = 0;
            row.Add(icon);

            var name = new Label(label);
            name.style.color = WidgetPalette.LabelBright;
            name.style.fontSize = 11;
            name.style.flexShrink = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(name);

            if (!string.IsNullOrEmpty(detail))
            {
                var det = new Label("[" + detail + "]");
                det.style.color = DesignerPalette.TypeAccent; // the amber the Bindings tab uses for its [type] tags
                det.style.fontSize = 10;
                det.style.marginLeft = 6;
                det.style.flexShrink = 0;
                row.Add(det);
            }
            return row;
        }

        // Describe a node as (icon, primary label, secondary detail) for the structure tree. Only the node kinds
        // that ALWAYS ship are strongly typed; anything else (optional / define-gated widgets) falls back to a
        // prettified type name so this editor compiles and renders regardless of which features are enabled.
        private static void Describe(LayoutNode node, out string icon, out string label, out string detail)
        {
            // Reuse the designer Hierarchy's icon mapping (bindings → Link, containers → Folder/Frame/Tabs, …) so
            // this structure tree reads consistently with the Hierarchy panel.
            icon = SasquatchLayoutDesignerWindow.GetNodeIcon(node);
            detail = null;
            switch (node)
            {
                case ContainerNode c:
                    string title = !string.IsNullOrEmpty(c.Title) ? c.Title : c.TabLabel;
                    label = string.IsNullOrEmpty(title) ? c.Type.ToString() : c.Type + " · " + title;
                    int kids = c.Children != null ? c.Children.Count : 0;
                    detail = kids + (kids == 1 ? " item" : " items");
                    return;
                case BindingNode b:
                    label = string.IsNullOrEmpty(b.PropertyPath) ? "(field)" : b.PropertyPath;
                    detail = Prettify(b.DisplayType.ToString());
                    return;
                case ButtonNode btn:
                    label = Quote(btn.ButtonText, "Button");
                    detail = "Button";
                    return;
                case LabeledDividerNode d:
                    label = string.IsNullOrEmpty(d.Title) ? "Divider" : d.Title;
                    detail = "Divider";
                    return;
                case TextBlockNode t:
                    label = Snippet(t.Text, "Text Block");
                    detail = "Text";
                    return;
                case SpaceNode s:
                    label = s.Flexible ? "Space (flexible)" : $"Space ({Mathf.RoundToInt(s.SpaceAmount)}px)";
                    return;
                case HelpBoxNode h:
                    label = Snippet(h.Message, "Help Box");
                    detail = "Help Box";
                    return;
                case CollectionNode col:
                    label = string.IsNullOrEmpty(col.PropertyPath) ? "Collection" : col.PropertyPath;
                    detail = "Collection";
                    return;
                default:
                    label = Prettify(StripNodeSuffix(node.GetType().Name));
                    return;
            }
        }

        // ---- Footer ------------------------------------------------------------------------------------------

        private static VisualElement BuildFooterNote()
        {
            var note = new Label("Auto-generated by Sasquatch Inspector — edit it in the Layout Designer rather than by hand.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.color = WidgetPalette.LabelDim;
            note.style.fontSize = 10;
            note.style.marginTop = 10;
            note.style.marginBottom = 2;
            return note;
        }

        // ---- Shared bits -------------------------------------------------------------------------------------

        private static void StyleAsCard(VisualElement card, Color bg)
        {
            card.style.backgroundColor = bg;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            var border = WidgetPalette.Border;
            card.style.borderTopColor = border;
            card.style.borderBottomColor = border;
            card.style.borderLeftColor = border;
            card.style.borderRightColor = border;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
        }

        private static VisualElement MakeFactRow(string label, string value, Color? valueColor = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 3;

            var l = new Label(label);
            l.style.width = 96;
            l.style.flexShrink = 0;
            l.style.color = WidgetPalette.LabelMuted;
            l.style.fontSize = 11;
            row.Add(l);

            var v = new Label(value);
            v.style.flexGrow = 1;
            v.style.flexShrink = 1;
            v.style.whiteSpace = WhiteSpace.Normal;
            v.style.color = valueColor ?? WidgetPalette.LabelBright;
            v.style.fontSize = 11;
            row.Add(v);
            return row;
        }

        // ---- Type resolution + small text helpers ------------------------------------------------------------

        private static Type ResolveTargetType(InspectorLayoutAsset asset)
        {
            if (asset == null) return null;

            if (!string.IsNullOrEmpty(asset.TargetTypeName))
            {
                var t = Type.GetType(asset.TargetTypeName);
                if (t != null) return t;
            }

            // Fallback via the rename-proof script GUID → its MonoScript → class.
            if (!string.IsNullOrEmpty(asset.ScriptGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(asset.ScriptGuid);
                if (!string.IsNullOrEmpty(path))
                {
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (ms != null)
                    {
                        var c = ms.GetClass();
                        if (c != null) return c;
                    }
                }
            }
            return null;
        }

        private static int CountNodes(LayoutNode node)
        {
            if (node == null) return 0;
            int count = 1;
            if (node is ContainerNode c && c.Children != null)
                foreach (var child in c.Children)
                    count += CountNodes(child);
            return count;
        }

        // "Namespace.Type, Assembly, Version=…" → "Namespace.Type".
        private static string ShortTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return assemblyQualifiedName;
            int comma = assemblyQualifiedName.IndexOf(',');
            return comma > 0 ? assemblyQualifiedName.Substring(0, comma).Trim() : assemblyQualifiedName;
        }

        private static string StripNodeSuffix(string typeName)
            => (!string.IsNullOrEmpty(typeName) && typeName.EndsWith("Node"))
                ? typeName.Substring(0, typeName.Length - 4)
                : typeName;

        // "MinMaxSlider" → "Min Max Slider" (insert a space before an uppercase that follows a non-uppercase char).
        private static string Prettify(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return pascal;
            var sb = new StringBuilder(pascal.Length + 6);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascal[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Quote(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : "“" + s + "”";

        private static string Snippet(string s, string fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length == 0) return fallback;
            if (s.Length > 42) s = s.Substring(0, 42) + "…";
            return "“" + s + "”";
        }
    }
}
