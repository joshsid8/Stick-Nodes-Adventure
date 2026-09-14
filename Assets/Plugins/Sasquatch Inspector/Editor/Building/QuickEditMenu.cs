using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Everything the quick-edit feature needs to act on the REAL inspector it was built for:
    /// the registered layout (whose Root aliases the database entry, so node mutations are the
    /// persisted data), the inspected object's SerializedObject/target, a targeted rebuild of
    /// just this editor (NEVER the selection-nudge — that would steal focus and close the popup),
    /// and the editor's stable root container (survives rebuilds; used to re-resolve a node's
    /// element after the visual tree is rebuilt).
    /// </summary>
    public class QuickEditContext
    {
        public InspectorLayoutAsset LayoutAsset;
        public SerializedObject SerializedObject;
        public UnityEngine.Object Target;
        public Action RebuildInspector;
        public VisualElement StableRoot;

        // Condition Debugger toggle plumbing for the right-click menu. When GetDebugConditions is set (and the
        // layout has conditions) the menu shows a "Debug Conditions" item; SetDebugConditions applies + rebuilds.
        // The host owns the state (live inspector → EditorPrefs; showcase → a window field) so each stays correct.
        public Func<bool> GetDebugConditions;
        public Action<bool> SetDebugConditions;
    }

    /// <summary>
    /// The "Edit …" section of the unified right-click menu: one entry per layout node in the
    /// ancestor chain under the cursor (deepest first), so the user never has to aim at an
    /// invisible headerless container — the menu disambiguates. Picking an entry opens the compact
    /// <see cref="QuickEditPopup"/> anchored to that node's element, with a glow outline marking
    /// exactly what's being edited. Event interception and menu rendering live in
    /// <see cref="QuickMenuController"/> (capture-phase takeover — children can't eat the click);
    /// this class keeps the chain logic and the pure helpers. Wired from both live render paths
    /// (VisualInspectorEditor and SasquatchForceTakeover) plus the Showcase window — never from the
    /// designer canvas, which edits nodes through its own panels.
    /// </summary>
    public static class QuickEditMenu
    {
        /// <summary>One node + its element in the cursor's ancestor chain (a quick-edit menu candidate).</summary>
        public struct ChainEntry
        {
            public LayoutNode Node;
            public VisualElement Element;
        }

        /// <summary>Wires the quick-edit "Edit …" chain menu onto <paramref name="builtRoot"/> for the given inspector context.</summary>
        public static void Attach(VisualElement builtRoot, QuickEditContext ctx)
        {
            QuickMenuController.Attach(builtRoot, ctx);
        }

        /// <summary>
        /// Appends the "Edit …" chain entries for the elements above <paramref name="deepTarget"/>.
        /// The capture-phase controller hands us the true deep element under the cursor directly.
        /// </summary>
        public static void AppendChainEntries(QuickMenuModel model, QuickEditContext ctx,
            VisualElement builtRoot, VisualElement deepTarget)
        {
            var ctxRoot = ctx?.LayoutAsset?.Root;
            if (ctxRoot == null || model == null) return;

            var chain = ComputeAncestorChain(deepTarget, builtRoot);
            foreach (var entry in chain)
            {
                // Only the quick-editable node kinds get entries; synthetic nodes (the primitive
                // value-column binding, template scratch parts) aren't reachable from ANY layout
                // root and are excluded by the null index path — one uniform filter.
                if (!IsQuickEditable(entry.Node)) continue;

                // Resolve the asset that OWNS this entry. A node rendered inside an inline editor lives
                // in a separate (nested) layout, so its path must be computed against THAT asset's root,
                // not the outer ctx root — otherwise FindIndexPath fails and the entry is silently
                // dropped. Each entry then routes its own edit to its own asset (the chain may span the
                // root layout plus one or more nested inline layouts, deepest first).
                var scope = LayoutOwnerRegistry.GetScope(entry.Element) ?? LayoutOwnerRegistry.ResolveScope(entry.Element);
                var owningRoot = scope?.LayoutAsset?.Root ?? ctxRoot;
                var indexPath = FindIndexPath(entry.Node, owningRoot);
                if (indexPath == null) continue;

                var captured = entry;            // capture per-iteration (element + node)
                var capturedPath = indexPath;
                var capturedScope = scope;
                var capturedRoot = owningRoot;
                model.Add(Describe(captured.Node, capturedPath, capturedRoot), () =>
                    QuickEditPopup.Open(ctx, capturedScope, captured.Node, capturedPath, captured.Element));
            }
        }

        /// <summary>
        /// The deepest editable node in the chain whose path resolves within ITS OWN owning layout, plus
        /// that node's owning scope and path. Used by the menu footer's "Open in Layout Designer" so it
        /// deep-links into the nested layout when the cursor is inside an inline editor. <paramref
        /// name="fallbackRoot"/> resolves entries with no stamped scope.
        /// </summary>
        public static LayoutNode DeepestEditable(List<ChainEntry> chain, LayoutNode fallbackRoot,
            out LayoutOwnerScope scope, out List<int> path)
        {
            foreach (var entry in chain)
            {
                if (!IsQuickEditable(entry.Node)) continue;
                var entryScope = LayoutOwnerRegistry.GetScope(entry.Element) ?? LayoutOwnerRegistry.ResolveScope(entry.Element);
                var owningRoot = entryScope?.LayoutAsset?.Root ?? fallbackRoot;
                path = FindIndexPath(entry.Node, owningRoot);
                if (path != null) { scope = entryScope; return entry.Node; }
            }
            scope = null;
            path = null;
            return null;
        }

        /// <summary>True when <paramref name="node"/> has a compact quick-edit panel (so it earns an "Edit …" menu entry).</summary>
        public static bool IsQuickEditable(LayoutNode node) =>
            node is BindingNode || node is ContainerNode || node is CollectionNode || node is ButtonNode
            || node is ScriptHeaderNode || node is HelpBoxNode
            // Decorative / reflected leaves also get a compact panel — a right-click gives every visible widget an
            // "Edit …" entry (parity with the designer's Properties panel), not just the bound-field kinds.
            || node is PropertyNode || node is TextBlockNode || node is ImageBannerNode
            || node is LabeledDividerNode || node is SpaceNode || node is EmbeddedEditorNode;

        /// <summary>
        /// Walks from the clicked element up to (and including) <paramref name="stopAt"/>, collecting
        /// every element that maps to a LayoutNode — deepest first. The LayoutNodeRegistry is the
        /// source of truth (Unity's controls/binding can overwrite userData on LIVE elements, which
        /// used to silently drop binding leaves from this chain); userData remains as a fallback.
        /// Collection rows store an int row index in userData; the type test skips those naturally.
        /// </summary>
        public static List<ChainEntry> ComputeAncestorChain(VisualElement target, VisualElement stopAt)
        {
            var chain = new List<ChainEntry>();
            var seen = new HashSet<LayoutNode>();
            for (var el = target; el != null; el = el.parent)
            {
                var node = LayoutNodeRegistry.GetNode(el) ?? el.userData as LayoutNode;
                if (node != null && seen.Add(node))
                    chain.Add(new ChainEntry { Node = node, Element = el });
                if (el == stopAt) break;
            }
            return chain;
        }

        /// <summary>
        /// Child-index path from <paramref name="root"/> to <paramref name="node"/> (-1 = descend into a
        /// collection's ItemLayout), or null when the node isn't reachable (synthetic). Matches the
        /// designer's index-path scheme, so it doubles as the deep-link payload for SelectNodeByIndexPath.
        /// </summary>
        public static List<int> FindIndexPath(LayoutNode node, LayoutNode root)
        {
            var path = new List<int>();
            return FindIndexPathRecursive(node, root, path) ? path : null;
        }

        private static bool FindIndexPathRecursive(LayoutNode target, LayoutNode current, List<int> path)
        {
            if (current == null) return false;
            if (ReferenceEquals(current, target)) return true;

            if (current is ContainerNode container && container.Children != null)
            {
                for (int i = 0; i < container.Children.Count; i++)
                {
                    path.Add(i);
                    if (FindIndexPathRecursive(target, container.Children[i], path)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
            else if (current is CollectionNode col && col.ItemLayout != null)
            {
                path.Add(-1); // -1 indicates ItemLayout
                if (FindIndexPathRecursive(target, col.ItemLayout, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            // A dictionary's Key / Value cell templates. The live table stamps each cell with its template in
            // LayoutNodeRegistry, so a right-click inside a dictionary row resolves to one of these — and
            // without these two steps FindIndexPath returned null and the menu silently dropped the entry.
            // Must stay in lockstep with the designer's FindNodeIndexPathRecursive, which is the twin of this.
            else if (current is BindingNode dictOwner)
            {
                if (dictOwner.DictionaryKeyTemplate != null)
                {
                    path.Add(LayoutFieldTypeResolver.DictionaryKeyStep);
                    if (FindIndexPathRecursive(target, dictOwner.DictionaryKeyTemplate, path)) return true;
                    path.RemoveAt(path.Count - 1);
                }
                if (dictOwner.DictionaryValueTemplate != null)
                {
                    path.Add(LayoutFieldTypeResolver.DictionaryValueStep);
                    if (FindIndexPathRecursive(target, dictOwner.DictionaryValueTemplate, path)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
            return false;
        }

        /// <summary>
        /// The node at a child-index path (-1 = a collection's ItemLayout), or null. The inverse of
        /// <see cref="FindIndexPath"/>; also used to resolve a node's PARENT (path minus its last step).
        /// </summary>
        public static LayoutNode NodeAtIndexPath(LayoutNode root, List<int> path)
        {
            if (root == null || path == null) return null;
            LayoutNode current = root;
            foreach (int index in path)
            {
                if (index == LayoutFieldTypeResolver.DictionaryKeyStep)
                    current = (current as BindingNode)?.DictionaryKeyTemplate;
                else if (index == LayoutFieldTypeResolver.DictionaryValueStep)
                    current = (current as BindingNode)?.DictionaryValueTemplate;
                else if (index == -1)
                    current = (current as CollectionNode)?.ItemLayout;
                else if (current is ContainerNode container && container.Children != null
                         && index >= 0 && index < container.Children.Count)
                    current = container.Children[index];
                else
                    return null;
                if (current == null) return null;
            }
            return current;
        }

        /// <summary>True when the node is a direct child of a Tabs container — i.e. it IS a tab page.</summary>
        public static bool IsTabChild(LayoutNode root, List<int> indexPath)
        {
            if (indexPath == null || indexPath.Count == 0) return false;
            var parent = NodeAtIndexPath(root, indexPath.GetRange(0, indexPath.Count - 1));
            return parent is ContainerNode pc && pc.Type == ContainerType.Tabs;
        }

        /// <summary>
        /// The menu entry text for a node. Headerless containers get a generated descriptor
        /// ("Rows (3 items)") so invisible grouping boxes are still nameable; a direct child of a Tabs
        /// container reads as a TAB ("Edit Tab …"); nodes inside a collection's ItemLayout say
        /// "(all rows)" because editing the shared template affects every row.
        /// </summary>
        public static string Describe(LayoutNode node, List<int> indexPath, LayoutNode root)
        {
            // -1 is a collection's item template; -2 / -3 are a dictionary's Key / Value cell templates. All
            // three are ONE authored node that draws EVERY row, so all three earn the "(all rows)" suffix —
            // it is the sentence that stops a buyer thinking they are editing the row under the pointer.
            bool allRows = indexPath != null
                           && (indexPath.Contains(-1)
                               || indexPath.Contains(LayoutFieldTypeResolver.DictionaryKeyStep)
                               || indexPath.Contains(LayoutFieldTypeResolver.DictionaryValueStep));
            string suffix = allRows ? " (all rows)" : string.Empty;

            switch (node)
            {
                case BindingNode bn:
                {
                    string name = bn.EffectiveLabelOverride;
                    if (string.IsNullOrEmpty(name)) name = NicifyLastSegment(bn.PropertyPath);
                    return string.IsNullOrEmpty(name)
                        ? $"✏ Edit Field{suffix}…"
                        : $"✏ Edit Field \"{name}\"{suffix}…";
                }
                case CollectionNode col:
                {
                    string kind = col.DisplayMode == CollectionDisplayMode.Table ? "Table"
                                : col.DisplayMode == CollectionDisplayMode.List ? "List" : "Collection";
                    // A retitled collection is named by its Title, exactly as a relabelled field is named by
                    // its Label above — the menu has to say what the user is looking at.
                    string name = col.EffectiveTitleOverride;
                    if (string.IsNullOrEmpty(name)) name = NicifyLastSegment(col.PropertyPath);
                    return string.IsNullOrEmpty(name)
                        ? $"☰ Edit {kind}…"
                        : $"☰ Edit {kind} \"{name}\"…";
                }
                case ButtonNode btn:
                {
                    string name = !string.IsNullOrEmpty(btn.ButtonText) ? btn.ButtonText : btn.MethodName;
                    return string.IsNullOrEmpty(name)
                        ? $"▶ Edit Button{suffix}…"
                        : $"▶ Edit Button \"{name}\"{suffix}…";
                }
                case ScriptHeaderNode _:
                    return $"</> Edit Script Header{suffix}…";
                case PropertyNode pn:
                {
                    string name = !string.IsNullOrEmpty(pn.LabelOverride) ? pn.LabelOverride : NicifyLastSegment(pn.PropertyName);
                    return string.IsNullOrEmpty(name)
                        ? $"◈ Edit Property{suffix}…"
                        : $"◈ Edit Property \"{name}\"{suffix}…";
                }
                case TextBlockNode tb:
                {
                    // Quote a short snippet of the text so multiple blocks stay tellable-apart.
                    string snip = tb.Text;
                    if (!string.IsNullOrEmpty(snip))
                    {
                        snip = snip.Replace('\r', ' ').Replace('\n', ' ');
                        if (snip.Length > 24) snip = snip.Substring(0, 24).TrimEnd() + "…";
                        return $"¶ Edit Text Block \"{snip}\"{suffix}…";
                    }
                    return $"¶ Edit Text Block{suffix}…";
                }
                case ImageBannerNode _:
                    return $"▤ Edit Image{suffix}…";
                case LabeledDividerNode ld:
                    return string.IsNullOrEmpty(ld.Title)
                        ? $"▬ Edit Divider{suffix}…"
                        : $"▬ Edit Divider \"{ld.Title}\"{suffix}…";
                case SpaceNode _:
                    return $"⇳ Edit Space{suffix}…";
                case EmbeddedEditorNode _:
                    return $"⧉ Edit Embedded Editor{suffix}…";
                case HelpBoxNode hb:
                {
                    // Quote a short snippet of the message so multiple boxes stay tellable-apart.
                    string msg = hb.Message;
                    if (!string.IsNullOrEmpty(msg))
                    {
                        msg = msg.Replace('\n', ' ');
                        if (msg.Length > 24) msg = msg.Substring(0, 24).TrimEnd() + "…";
                        return $"⚠ Edit Info/Warning \"{msg}\"{suffix}…";
                    }
                    return $"⚠ Edit Info/Warning{suffix}…";
                }
                case ContainerNode c:
                {
                    if (IsTabChild(root, indexPath))
                    {
                        string tabName = !string.IsNullOrEmpty(c.TabLabel) ? c.TabLabel : c.Title;
                        if (string.IsNullOrEmpty(tabName) && indexPath.Count > 0)
                            tabName = $"Tab {indexPath[indexPath.Count - 1] + 1}";
                        return $"▣ Edit Tab \"{tabName}\"{suffix}…";
                    }
                    // The root container is unique — label it "Root" instead of "Group" so it stands out
                    // from the nested grouping boxes above it in the chain.
                    string groupWord = node == root ? "Root" : "Group";
                    string title = !string.IsNullOrEmpty(c.Title) ? c.Title : c.TabLabel;
                    if (!string.IsNullOrEmpty(title))
                        return $"▢ Edit {groupWord} \"{title}\" ({c.Type}){suffix}…";
                    int count = c.Children?.Count ?? 0;
                    return $"▢ Edit {groupWord} {c.Type} ({count} {(count == 1 ? "item" : "items")}){suffix}…";
                }
                default:
                    return $"Edit {ObjectNames.NicifyVariableName(node.GetType().Name)}{suffix}…";
            }
        }

        /// <summary>
        /// A compact breadcrumb label for a node — just the noun (no glyph, no "Edit …" verb, no trailing
        /// ellipsis), used by the quick-edit popup's ancestor breadcrumb. Mirrors <see cref="Describe"/>'s
        /// per-kind title resolution so the crumb text and the menu text never drift.
        /// </summary>
        public static string CrumbLabel(LayoutNode node, List<int> indexPath, LayoutNode root)
        {
            switch (node)
            {
                case BindingNode bn:
                {
                    string name = bn.EffectiveLabelOverride;
                    if (string.IsNullOrEmpty(name)) name = NicifyLastSegment(bn.PropertyPath);
                    return string.IsNullOrEmpty(name) ? "Field" : name;
                }
                case CollectionNode col:
                {
                    string name = col.EffectiveTitleOverride;
                    if (string.IsNullOrEmpty(name)) name = NicifyLastSegment(col.PropertyPath);
                    if (!string.IsNullOrEmpty(name)) return name;
                    return col.DisplayMode == CollectionDisplayMode.Table ? "Table"
                         : col.DisplayMode == CollectionDisplayMode.List ? "List" : "Collection";
                }
                case ButtonNode btn:
                {
                    string name = !string.IsNullOrEmpty(btn.ButtonText) ? btn.ButtonText : btn.MethodName;
                    return string.IsNullOrEmpty(name) ? "Button" : name;
                }
                case ScriptHeaderNode _:
                    return "Header";
                case PropertyNode pn:
                {
                    string name = !string.IsNullOrEmpty(pn.LabelOverride) ? pn.LabelOverride : NicifyLastSegment(pn.PropertyName);
                    return string.IsNullOrEmpty(name) ? "Property" : name;
                }
                case TextBlockNode _:
                    return "Text";
                case ImageBannerNode _:
                    return "Image";
                case LabeledDividerNode ld:
                    return string.IsNullOrEmpty(ld.Title) ? "Divider" : ld.Title;
                case SpaceNode _:
                    return "Space";
                case EmbeddedEditorNode _:
                    return "Embed";
                case HelpBoxNode _:
                    return "Info";
                case ContainerNode c:
                {
                    if (IsTabChild(root, indexPath))
                    {
                        string tabName = !string.IsNullOrEmpty(c.TabLabel) ? c.TabLabel : c.Title;
                        if (string.IsNullOrEmpty(tabName) && indexPath != null && indexPath.Count > 0)
                            tabName = $"Tab {indexPath[indexPath.Count - 1] + 1}";
                        return string.IsNullOrEmpty(tabName) ? "Tab" : tabName;
                    }
                    if (node == root) return "Root";
                    string title = !string.IsNullOrEmpty(c.Title) ? c.Title : c.TabLabel;
                    return !string.IsNullOrEmpty(title) ? title : "Group";
                }
                default:
                    return ObjectNames.NicifyVariableName(node.GetType().Name);
            }
        }

        private static string NicifyLastSegment(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;
            int dot = propertyPath.LastIndexOf('.');
            string last = dot >= 0 ? propertyPath.Substring(dot + 1) : propertyPath;
            return ObjectNames.NicifyVariableName(last);
        }
    }
}
