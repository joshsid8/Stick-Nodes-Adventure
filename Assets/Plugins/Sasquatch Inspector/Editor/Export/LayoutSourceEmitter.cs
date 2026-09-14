using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// Walks a layout <see cref="LayoutNode"/> tree and emits standalone UI-Toolkit C# that reproduces it, mirroring
    /// the structure decisions of the live <c>LayoutTreeBuilder.BuildNode</c>. Containers become private
    /// <c>Build_*()</c> helper methods (collected on <see cref="EmitContext"/>); leaves are emitted inline into their
    /// parent's method. Per-node <see cref="LayoutStyle"/>/<see cref="TextStyle"/> and condition rules are applied via
    /// <see cref="StyleEmitter"/> / <see cref="ConditionEmitter"/>. The output references stock Unity types plus the
    /// <c>SasqKit</c> helpers baked into the same file, so the generated editor never needs the package installed.
    /// </summary>
    internal sealed class LayoutSourceEmitter
    {
        private readonly EmitContext _ctx;

        // Bindings that duplicate an enclosing toggle-group Foldout's header checkbox. The live builder's twin
        // is LayoutTreeBuilder._toggleMirrors — same GroupToggleMirror.Collect, same derive-per-render rule, so
        // the exported editor drops exactly the fields the package's inspector drops. Null when there are none.
        private HashSet<LayoutNode> _toggleMirrors;

        // Attribute-owned Space widgets whose field the author hid. Twin of LayoutTreeBuilder._orphanedSpacers:
        // hiding a field takes its imported [Space] gap with it, so the exported editor closes up the same way
        // the live one does instead of keeping a blank strip where the field used to be. Null when there are none.
        private HashSet<LayoutNode> _orphanedSpacers;

        public LayoutSourceEmitter(EmitContext ctx) { _ctx = ctx; }

        /// <summary>Emits the root node into the CreateInspectorGUI body, adding it to the local <c>root</c> element.</summary>
        public void EmitRoot(CodeWriter w, LayoutNode root)
        {
            if (root == null) return;
            _toggleMirrors = GroupToggleMirror.Collect(root);
            _orphanedSpacers = HiddenNodeMark.CollectOrphanedSpacers(root);
            string expr = EmitElementExpr(w, root, "");
            if (expr != null) w.Line($"root.Add({expr});");
        }

        /// <summary>
        /// Emits one child with the structural node path pushed for the duration — the live builder's
        /// <c>nodePath + "/" + i</c>. Kept in step so a collapsible site's remembered open-state is per-SITE:
        /// two sections both titled "Advanced" in one layout must not share one memory. Restored in a
        /// <c>finally</c>, since emitting a subtree is re-entrant.
        /// </summary>
        private string EmitChildExpr(CodeWriter w, LayoutNode child, int index, string childBasePath,
                                     bool paneCells = false, bool asCell = false)
        {
            string parent = _ctx.NodePath;
            _ctx.NodePath = parent + "/" + index;
            try { return EmitElementExpr(w, child, childBasePath, paneCells, asCell); }
            finally { _ctx.NodePath = parent; }
        }

        /// <summary>Emits whatever is needed to construct <paramref name="node"/> and returns a C# expression for its element (or null when skipped).
        /// <paramref name="paneCells"/> marks a split-frame pane subtree: the container's direct binding children render as
        /// column cells (the live directChildrenAreCells — a pane is a narrow offset region where the aligned label dies).
        /// <paramref name="asCell"/> says THIS node is one of those cells (the caller wraps it in SasqKit.ColumnCell on the
        /// way out) — the live <c>columnCell</c> flag, and what tells a binding to skip full-row normalization.</summary>
        private string EmitElementExpr(CodeWriter w, LayoutNode node, string basePath, bool paneCells = false,
                                       bool asCell = false)
        {
            string expr = EmitElementExprCore(w, node, basePath, paneCells, asCell);
            expr = EmitSearchable(w, node, expr);

            // Animations hang off LayoutNode, so EVERY node kind can carry them and the live AnimationBinder
            // wires whatever element the node produced. Wiring them on the binding path alone would silently
            // drop Fade / Slide / Colour Pulse from a Card, Button, Help Box, Text Block or Collection.
            // (BindingNode wires its own, after conditions, exactly as live does — don't double up.)
            if (expr != null && !(node is BindingNode) && node.Animations != null && node.Animations.Count > 0)
            {
                string av = _ctx.NewVar("animated", "animated");
                w.Line($"var {av} = {expr};");
                EmitAnimations(w, node, av);
                return av;
            }
            return expr;
        }

        /// <summary>
        /// Publishes one built element to the exported field search, with its searchable text baked as a string
        /// literal by <see cref="InspectorSearchIndex.BuildHaystack(LayoutNode)"/> — the package's OWN haystack
        /// builder, called here at export time. That is the whole parity guarantee: there is no second
        /// implementation to drift, so a query can never mean two different things in the two renderings.
        /// (The live index reads each element's <c>LayoutNode</c> at runtime; the exported file has no nodes,
        /// which is why the text has to travel as a literal instead.)
        /// <para>
        /// Emits nothing at all unless the layout carries a search-enabled Script Header, so an export without
        /// the magnifier is byte-for-byte what it always was.
        /// </para>
        /// </summary>
        private string EmitSearchable(CodeWriter w, LayoutNode node, string expr)
        {
            if (!_ctx.SearchEnabled || expr == null) return expr;

            // The header HOSTS the query box, so it is never itself indexed: it carries no haystack and would
            // resolve to Hidden on any no-match query, collapsing the very bar being typed into. Live's index
            // skips it and its whole subtree for exactly this reason.
            if (node is ScriptHeaderNode) return expr;

            // Nothing inside a collection ROW is indexed. The collection is one leaf (kind 2 below ends the
            // walk there) because its rows are paginated and rebuilt per page, and it already carries a row
            // search of its own — live draws the same boundary.
            if (_ctx.RowBaseExpr != null) return expr;

            // 1 = a grouping container, whose TITLE match reveals its whole contents; 2 = a collection, a leaf
            // that ends the index walk; 0 = an ordinary leaf.
            int kind = node is ContainerNode ? 1 : node is CollectionNode ? 2 : 0;

            _ctx.UseKit(null); // Core — Searchable lives there, so every widget section can write to it
            string v = expr;
            if (!IsPlainIdentifier(expr))
            {
                // A container's expression is a `Build_*(so)` CALL, so it has to be materialized before it can
                // be handed to two statements — otherwise the container would be built twice.
                v = _ctx.NewVar("searchable", "searchable");
                w.Line($"var {v} = {expr};");
            }
            w.Line($"SasqKit.Searchable({v}, {Lit.Str(InspectorSearchIndex.BuildHaystack(node))}, {kind});");
            return v;
        }

        /// <summary>True when <paramref name="expr"/> is a bare local name (so it can be referenced twice for free).</summary>
        private static bool IsPlainIdentifier(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return false;
            foreach (char c in expr)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private string EmitElementExprCore(CodeWriter w, LayoutNode node, string basePath, bool paneCells = false,
                                           bool asCell = false)
        {
            // A binding to the very bool its group's header checkbox already edits is not emitted at all — the
            // live builder's early return in BuildNode, transposed. Returning null routes it through the same
            // "skipped child" path every unsupported node takes, so the section-gap bookkeeping below never
            // counts it and the export can't grow a trailing dead gap the live render doesn't have.
            if (_toggleMirrors != null && _toggleMirrors.Contains(node)) return null;

            // Same treatment for a node the author hid. A hide is a permanent design decision, not the temporary
            // park that InspectorLayoutAsset.LayoutHidden is, so it travels with the layout: the exported editor
            // omits the node exactly as the live inspector skips it, and a hidden container takes its subtree
            // with it because the recursion never descends.
            if (node != null && node.HiddenInInspector) return null;
            if (_orphanedSpacers != null && _orphanedSpacers.Contains(node)) return null;

            switch (node)
            {
                case ContainerNode c:
                {
                    // Inside a list row the container's children bind to paths only known per row, so the
                    // method takes the element base as an argument. The CALL passes whatever expression is
                    // in force HERE; the body reads it back as its own `__base` parameter (see
                    // EmitContainerMethod), which is what lets rows nest containers arbitrarily deep.
                    string rowArg = _ctx.RowBaseExpr;
                    string method = EmitContainerMethod(c, basePath, paneCells);
                    if (method == null) return null;
                    return rowArg == null ? $"{method}(so)" : $"{method}(so, {rowArg})";
                }
                case BindingNode b: return EmitBinding(w, b, basePath, asCell);
                case CollectionNode col:
                    // A collection INSIDE a list row travels like every other node: SasqKit.ListBox / TableBox
                    // / TagChips all take their array path as a RUNTIME string and do their own FindProperty,
                    // so nothing about the collection kit is baked. What the row supplies is the base, and
                    // `_ctx.PathArg` composes it — the same unlock the cell factory uses. See EmitCollection.
                    return EmitCollection(w, col, basePath);
                case ButtonNode btn: return EmitButton(w, btn, basePath);
                case PropertyNode pn: return EmitPropertyNode(w, pn, basePath);
                case ScriptHeaderNode sh: return EmitScriptHeader(w, sh, basePath);
                case SpaceNode sp: return EmitSpace(w, sp, basePath);
                case EmptyCellNode ec: return EmitEmptyCell(w, ec, basePath);
                case LabeledDividerNode d: return EmitDivider(w, d, basePath);
                case TextBlockNode tb: return EmitTextBlock(w, tb, basePath);
                case HelpBoxNode hb: return EmitHelpBox(w, hb, basePath);
                case ImageBannerNode ib: return EmitImageBanner(w, ib, basePath);
                case EmbeddedEditorNode ee: return EmitEmbeddedEditor(w, ee, basePath);
                default:
                    // CustomWidgetNode (define-gated) or any future node kind.
                    _ctx.Note($"A '{node.GetType().Name}' node was skipped (not supported by standalone export).");
                    return null;
            }
        }

        // ---- Containers ------------------------------------------------------------------------------------

        private string EmitContainerMethod(ContainerNode c, string basePath, bool paneCells = false)
        {
            string childBasePath = basePath;
            if (!string.IsNullOrEmpty(c.PropertyPath))
                childBasePath = Combine(basePath, c.PropertyPath);

            string method = _ctx.NewMethod("Build_" + ContainerBaseName(c));
            var w = new CodeWriter();
            // In a list row the method takes the element's path, and the whole body emits against `__base`
            // rather than the caller's expression — `__element` belongs to the factory lambda and is not in
            // scope in here. Restored in the finally at the end, like every other context override.
            string prevRowBase = _ctx.RowBaseExpr;
            if (prevRowBase != null) _ctx.RowBaseExpr = "__base";
            w.Line(prevRowBase == null
                ? $"private VisualElement {method}(SerializedObject so)"
                : $"private VisualElement {method}(SerializedObject so, string __base)");
            w.OpenBrace();

            string el = _ctx.NewVar(ContainerBaseName(c), "container");
            string content = el;
            string splitPaneA = null, splitPaneB = null; // set only for a DiagonalSplit frame
            bool rowFlow = c.Type == ContainerType.Columns || c.Type == ContainerType.Grid;

            // Expandable containers (and toggle-group foldouts) render in the collapsible Sasquatch group shell,
            // exactly like the live builder's .sasq-group frame — the shell owns the title. A plain Foldout that
            // carries a section tint / accent stripe ALSO routes through the shell: the live tinted-foldout wash
            // (header + body + rail) can't be baked onto a raw Unity Foldout, but the group shell reproduces it.
            bool isToggleFoldout = c.Type == ContainerType.Foldout && !string.IsNullOrEmpty(c.GroupTogglePath);
            bool foldoutColorCoded = c.Type == ContainerType.Foldout
                && (c.SectionTintColor.isOverridden || c.AccentStripeColor.isOverridden);
            bool groupShell = (c.Expandable
                    && (c.Type == ContainerType.Rows || c.Type == ContainerType.Columns
                        || c.Type == ContainerType.Grid || c.Type == ContainerType.Card))
                || isToggleFoldout || foldoutColorCoded;

            if (c.Type == ContainerType.Tabs)
            {
                EmitTabs(w, c, el, childBasePath);
                ApplyContainerCommon(w, c, el, basePath);
                w.Line($"return {el};");
                w.CloseBrace();
                _ctx.Methods.Add(w.ToString());
                _ctx.RowBaseExpr = prevRowBase;
                return method;
            }

            if (groupShell)
            {
                _ctx.UseKit("Group");
                content = _ctx.NewVar(ContainerBaseName(c) + "Content", "content");
                // An untitled shell falls back to the container's TYPE name ("Card", "Columns", "Grid"…), which
                // is what the live collapsible shell shows — a flat literal would make every untitled
                // collapsible container read the same regardless of what it actually is.
                // An untitled TOGGLE-group is the one exception: live names it after the bound bool's
                // displayName (LayoutTreeBuilder's toggle-foldout branch), not after the container type, so
                // "Foldout" here would drop the only label the user ever sees. displayName on a plain
                // serialized field is NicifyVariableName of the field name, which is resolvable at emit time.
                string shellTitle = !string.IsNullOrEmpty(c.Title) ? c.Title
                    : isToggleFoldout ? ObjectNames.NicifyVariableName(LastSegment(c.GroupTogglePath))
                    : c.Type.ToString();
                w.Line($"VisualElement {content};");
                var groupArgs = new System.Collections.Generic.List<string>
                {
                    Lit.Str(shellTitle), B(c.DefaultToOpen), $"out {content}"
                };
                if (isToggleFoldout)
                {
                    groupArgs.Add("so");
                    // The toggle's path is resolved against the container's OWN scope (basePath), not the scope
                    // its children get. childBasePath already has this container's PropertyPath appended, so
                    // using it here doubles the segment ("gear.gear.enabled") and the checkbox binds to nothing.
                    groupArgs.Add(Lit.Str(Combine(basePath, c.GroupTogglePath)));
                    if (c.GroupToggleAlignment != GroupToggleAlignment.Left)
                        groupArgs.Add($"align: {(int)c.GroupToggleAlignment}");
                }
                if (c.SectionTintColor.isOverridden) groupArgs.Add($"tint: {Lit.Col(c.SectionTintColor.value)}");
                if (c.AccentStripeColor.isOverridden) groupArgs.Add($"stripe: {Lit.Col(c.AccentStripeColor.value)}");
                // Remembered open-state, keyed per site. A TOGGLE group keys on its bound bool path, exactly as
                // the live toggle-foldout branch does; every other shell keys on its structural node path, as the
                // live collapsible shell does.
                // FoldoutKeyArg, not FoldoutKey: inside a list row the key must carry the ROW's own element
                // path, or every row shares one fold and opening one opens them all.
                string groupStateKey = _ctx.FoldoutKeyArg(
                    isToggleFoldout ? Combine(basePath, c.GroupTogglePath) : _ctx.NodePath, shellTitle);
                if (groupStateKey != null) groupArgs.Add($"stateKey: {groupStateKey}");
                w.Line($"var {el} = SasqKit.Group({string.Join(", ", groupArgs)});");
                if (rowFlow)
                {
                    // A COLLAPSIBLE Columns/Grid still has to be a real columns/grid inside its shell: build the
                    // same shape the non-shell branch builds and let the same SasqKit.LayoutColumns / LayoutGrid
                    // pass run over it. A plain wrapping row here instead would cost an expandable Columns its
                    // equal-share cells, dividers and drag-resize, and an expandable Grid its row regrouping —
                    // the same container rendering completely differently just because Expandable is ticked.
                    string inner = _ctx.NewVar(ContainerBaseName(c) + "Flow", "flow");
                    w.Line($"var {inner} = new VisualElement();");
                    if (c.Type == ContainerType.Columns)
                    {
                        w.Line($"{inner}.style.flexDirection = FlexDirection.Row;");
                        w.Line($"{inner}.style.alignItems = Align.Stretch;");
                    }
                    else
                    {
                        w.Line($"{inner}.style.flexDirection = FlexDirection.Column;");
                    }
                    w.Line($"{content}.Add({inner});");
                    content = inner;
                }
            }
            else switch (c.Type)
            {
                case ContainerType.Rows:
                    w.Line($"var {el} = new VisualElement();");
                    w.Line($"{el}.style.flexDirection = FlexDirection.Column;");
                    EmitContainerTitle(w, c, el, 4f, "SasqTheme.LabelBright");
                    break;

                case ContainerType.Columns:
                    // Cells sit side-by-side in one row; SasqKit.LayoutColumns (after the children) sizes them to
                    // equal flex shares + inserts the dividers, so each cell shrinks to share the row (a raw
                    // Row/wrap of aligned PropertyFields is the "completely messed up" look this replaces).
                    w.Line($"var {el} = new VisualElement();");
                    w.Line($"{el}.style.flexDirection = FlexDirection.Column;");
                    EmitContainerTitle(w, c, el, 4f, "SasqTheme.LabelBright");
                    content = _ctx.NewVar(ContainerBaseName(c) + "Content", "content");
                    w.Line($"var {content} = new VisualElement();");
                    w.Line($"{content}.style.flexDirection = FlexDirection.Row;");
                    w.Line($"{content}.style.alignItems = Align.Stretch;");
                    w.Line($"{el}.Add({content});");
                    break;

                case ContainerType.Grid:
                    // Cells are added flat into a column stack; SasqKit.LayoutGrid (after the children) regroups
                    // them into explicit rows by the grid constraint (equal columns per row).
                    w.Line($"var {el} = new VisualElement();");
                    w.Line($"{el}.style.flexDirection = FlexDirection.Column;");
                    EmitContainerTitle(w, c, el, 4f, "SasqTheme.LabelBright");
                    content = _ctx.NewVar(ContainerBaseName(c) + "Content", "content");
                    w.Line($"var {content} = new VisualElement();");
                    w.Line($"{content}.style.flexDirection = FlexDirection.Column;");
                    w.Line($"{el}.Add({content});");
                    break;

                case ContainerType.Card:
                    w.Line($"var {el} = new VisualElement();");
                    EmitCardChrome(w, el);
                    EmitContainerTitle(w, c, el, 4f, "SasqTheme.LabelBright");
                    content = _ctx.NewVar(ContainerBaseName(c) + "Content", "content");
                    w.Line($"var {content} = new VisualElement();");
                    w.Line($"{el}.Add({content});");
                    break;

                case ContainerType.Foldout:
                    string foldoutTitle = !string.IsNullOrEmpty(c.Title) ? c.Title : "Group";
                    w.Line($"var {el} = new Foldout();");
                    w.Line($"{el}.text = {Lit.Str(foldoutTitle)};");
                    // The live Foldout branch's two title fix-ups, baked: bold + the shared title ink on the
                    // header text (stock is normal-weight grey, the odd one out among the containers), and the
                    // content pulled from Unity's 15px indent onto the 18px gutter the header text already
                    // occupies, so the fields hang under their own title. Nothing here touches a NESTED native
                    // foldout — the styling is applied to this element only, never through a stylesheet.
                    _ctx.UseKit(null); // SasqTheme
                    string foldoutText = _ctx.NewVar("foldoutText", "foldoutText");
                    w.Line($"var {foldoutText} = {el}.Q<Label>(className: \"unity-foldout__text\");");
                    w.Line($"if ({foldoutText} != null)");
                    w.OpenBrace();
                    w.Line($"{foldoutText}.style.unityFontStyleAndWeight = FontStyle.Bold;");
                    w.Line($"{foldoutText}.style.color = SasqTheme.LabelBright;");
                    w.CloseBrace();
                    w.Line($"{el}.contentContainer.style.marginLeft = {Lit.F(SasquatchInspector.Editor.LayoutTreeBuilder.SectionGutter)};");
                    // A UI Toolkit Foldout has NO built-in view-state persistence, so without this the section
                    // springs back open on every rebuild — which includes every selection change. Live wires the
                    // same seed + writeback pair around FoldoutStateStore.
                    // Per-ROW inside a list row (see FoldoutKeyArg) — a shared key would fold every row at once.
                    string foldoutStateKey = _ctx.FoldoutKeyArg(_ctx.NodePath, foldoutTitle);
                    string foldoutPersisted = B(c.DefaultToOpen);
                    if (foldoutStateKey != null)
                    {
                        _ctx.UseKit(null); // Core — FoldoutGet / FoldoutSet
                        string keyVar = _ctx.NewVar("foldKey", "foldKey");
                        w.Line($"var {keyVar} = {foldoutStateKey};");
                        w.Line($"{el}.value = SasqKit.FoldoutGet({keyVar}, {B(c.DefaultToOpen)});");
                        w.Line($"{el}.RegisterValueChangedCallback(__fe => "
                               + $"{{ if (__fe.target == {el}) SasqKit.FoldoutSet({keyVar}, __fe.newValue); }});");
                        foldoutPersisted = $"SasqKit.FoldoutGet({keyVar}, {B(c.DefaultToOpen)})";
                    }
                    else
                    {
                        w.Line($"{el}.value = {B(c.DefaultToOpen)};");
                    }
                    if (_ctx.SearchEnabled)
                    {
                        // The field search's transient expander for a NATIVE Foldout (SasqKit.Group registers
                        // its own). SetValueWithoutNotify, not `value`, on BOTH halves: a plain assignment
                        // would run the writeback above and persist a fold the buyer never asked for — the
                        // whole point of a transient expansion is that the remembered state survives it.
                        _ctx.UseKit(null); // Core — Expander
                        w.Line($"SasqKit.Expander({el}, __o => {el}.SetValueWithoutNotify(__o), "
                               + $"() => {el}.SetValueWithoutNotify({foldoutPersisted}));");
                    }
                    break; // Foldout.Add() routes children into contentContainer

                case ContainerType.Frame:
                {
                    _ctx.UseKit("Frame");
                    var f = c.Frame ?? new FrameSettings();
                    string titleArg = string.IsNullOrEmpty(c.Title) ? "null" : Lit.Str(c.Title);
                    if (f.Shape == FrameShape.DiagonalSplit)
                    {
                        // Children route into the panes below (the live split child loop) — no content host.
                        splitPaneA = _ctx.NewVar(ContainerBaseName(c) + "PaneA", "paneA");
                        splitPaneB = _ctx.NewVar(ContainerBaseName(c) + "PaneB", "paneB");
                        w.Line($"VisualElement {splitPaneA}, {splitPaneB};");
                        var args = new System.Collections.Generic.List<string>
                        {
                            $"out {splitPaneA}", $"out {splitPaneB}", $"title: {titleArg}",
                            $"splitPos: {Lit.F(f.SplitPosition)}", $"splitAngle: {Lit.F(f.SplitAngle)}"
                        };
                        if (f.SeamColor.isOverridden && f.SeamWidth > 0f)
                        {
                            args.Add($"seamColor: {Lit.Col(f.SeamColor.value)}");
                            args.Add($"seamWidth: {Lit.F(f.SeamWidth)}");
                        }
                        AddFrameFillArgs(args, f, split: true);
                        w.Line($"var {el} = SasqKit.SplitFrame({string.Join(", ", args)});");
                        content = null;
                    }
                    else
                    {
                        content = _ctx.NewVar(ContainerBaseName(c) + "Content", "content");
                        w.Line($"VisualElement {content};");
                        var args = new System.Collections.Generic.List<string>
                        {
                            $"out {content}", $"shape: {(int)f.Shape}", $"title: {titleArg}"
                        };
                        if (f.LockAspect && f.Shape != FrameShape.Circle)
                        {
                            args.Add("lockAspect: true");
                            args.Add($"aspectRatio: {Lit.F(f.AspectRatio)}");
                        }
                        // The wash ramp (the gradient's own color+alpha per row, edge/length folded in) is
                        // baked by the LIVE FrameBuilder bake at export time — pixel-identical standalone.
                        if (f.OverlayEnabled)
                            args.Add($"overlayPng: {Lit.Str(BakeOverlayRampPng(f))}");
                        AddFrameFillArgs(args, f, split: false);
                        w.Line($"var {el} = SasqKit.Frame({string.Join(", ", args)});");
                    }
                    // A frame builds its own caption inside the kit, so the node's Title text style has no
                    // element to land on the way every other container's does through EmitContainerTitle —
                    // size, colour, weight and alignment would all be dropped. Re-find the named label and
                    // style it, for both the plain and split shapes.
                    if (!string.IsNullOrEmpty(c.Title) && StyleEmitter.HasTextOverride(c.TitleTextStyle))
                    {
                        string ft = _ctx.NewVar("frameTitle", "frameTitle");
                        w.Line($"var {ft} = {el}.Q<Label>(SasqKit.FrameTitleName);");
                        w.Line($"if ({ft} != null)");
                        w.OpenBrace();
                        StyleEmitter.EmitTextStyle(w, ft, c.TitleTextStyle, _ctx);
                        w.CloseBrace();
                    }
                    break;
                }

                default:
                    w.Line($"var {el} = new VisualElement();");
                    break;
            }

            // Columns / Grid outside a group shell wrap each field child as a "cell" (own label replacing the
            // aligned one that breaks in a narrow flex column) and hand the flat cells to a layout helper below.
            // Cell treatment follows the container TYPE, not whether it happens to be wrapped in a collapsible
            // shell — the shell's content host is built in the same shape above precisely so this can run.
            bool cellFlow = c.Type == ContainerType.Columns || c.Type == ContainerType.Grid;
            // Frame content (and a split pane's subtree) joins the same cell path — the live
            // childIsColumnCell |= Frame rule: a shaped frame's inset content crushes Unity's aligned
            // label to nothing, while a cell's natural-width label + stacking absorbs the squeeze.
            bool frameCells = content != null && c.Type == ContainerType.Frame;
            // Same test, second consequence (live: the ScopeClass tag beside childIsColumnCell): a cell is
            // also its own label-column TRACK. A binding cell brings its own hugging label and never reaches
            // the column at all, but a CONTAINER cell — a Rows stack dropped into a column — carries ordinary
            // field rows, and those must fit the track instead of the whole inspector's width. Includes the
            // over-cap Rows regroup, whose children live in side-by-side stacks for the same reason.
            bool trackCells = cellFlow || frameCells || paneCells
                || (c.Type == ContainerType.Rows && c.GridColumns > 0
                    && EmittableChildCount(c) > c.GridColumns);

            // Section-rhythm bookkeeping for the children loop below (see EmittedSection): which children were
            // containers/collections, and how many children were emitted in total — the last one gets no gap.
            var emittedSections = new System.Collections.Generic.List<EmittedSection>();
            int emittedChildren = 0;

            if (splitPaneA != null)
            {
                // The live split child loop: Children[0] fills Pane A, Children[1] Pane B; extra/legacy
                // children route defensively into Pane A. Each pane subtree is CONTENT-SIZED and never
                // shrinks below its content (the pane box is a fixed absolute layer — shrink = crushed
                // fields; overflow is clipped until the frame's grow pass catches up). It is deliberately
                // NOT stretched: the pane host is an absolute layer sized to the FRAME, so a stretched
                // subtree's height IS the frame's, which made the grow measure self-referential and let
                // pane content ratchet the frame without bound. The background rides the HOST instead.
                if (c.Children != null)
                {
                    for (int ci = 0; ci < c.Children.Count; ci++)
                    {
                        var child = c.Children[ci];
                        string expr = EmitChildExpr(w, child, ci, childBasePath, paneCells: true, asCell: true);
                        if (expr == null) continue;
                        if (child is BindingNode pbn)
                        {
                            _ctx.UseKit("Layout");
                            string cellLabel = CellLabel(pbn, childBasePath);
                            string cellVar = _ctx.NewVar("paneCell", "paneCell");
                            w.Line($"var {cellVar} = SasqKit.ColumnCell({expr}, {(cellLabel == null ? "null" : Lit.Str(cellLabel))});");
                            expr = cellVar;
                        }
                        string paneChild = _ctx.NewVar("paneChild", "paneChild");
                        w.Line($"var {paneChild} = {expr};");
                        // A pane is its own label-column track.
                        _ctx.UseKit(null); // Core (LabelScope)
                        w.Line($"SasqKit.LabelScope({paneChild});");
                        w.Line($"{paneChild}.style.flexShrink = 0f;");
                        // Pane background rides the HOST, not the subtree (see the note above).
                        if (child.Style.backgroundColor.isOverridden)
                        {
                            string host = ci == 1 ? splitPaneB : splitPaneA;
                            w.Line($"{host}.style.backgroundColor = {Lit.Col(child.Style.backgroundColor.value)};");
                            w.Line($"{paneChild}.style.backgroundColor = Color.clear;");
                        }
                        // MODEL index (ci), matching the live builder's `i == 1 ? paneHostB : paneHostA`. A
                        // compacted counter here would slide Pane B's child across the seam into Pane A the
                        // moment Pane A's child is skipped (hidden, or a group-toggle mirror) — the exported
                        // editor would then differ from the live one for the same layout.
                        w.Line($"{(ci == 1 ? splitPaneB : splitPaneA)}.Add({paneChild});");
                    }
                }
            }
            else if (c.Children != null)
            {
                for (int ci = 0; ci < c.Children.Count; ci++)
                {
                    var child = c.Children[ci];
                    // asCell must match the ColumnCell wrap below EXACTLY: a cell is not a full-width row, so a
                    // binding that becomes one skips full-row normalization (the live `!columnCell` guard on
                    // NormalizeFullRowBinding). ColumnCell pins its own margins and flex share anyway, and the
                    // stretch a full row wants would fight the cell's centered alignItems.
                    bool childIsCell = cellFlow || frameCells || paneCells;
                    string expr = EmitChildExpr(w, child, ci, childBasePath, asCell: childIsCell);
                    if (expr == null) continue;
                    // Twin of LayoutTreeBuilder.ApplySectionGaps: a SECTION (container or collection) leaves one
                    // uniform gap below itself, so the space between two neighbours doesn't depend on which
                    // KINDS they are. Only the parent knows sibling order, and the emitted values are written
                    // after the gap/layout block below, so the sections are captured here and stamped there.
                    if (child is ContainerNode || child is CollectionNode)
                    {
                        string sectionVar = _ctx.NewVar("section", "section");
                        w.Line($"var {sectionVar} = {expr};");
                        expr = sectionVar;
                        emittedSections.Add(new EmittedSection(sectionVar, child, emittedChildren));
                    }
                    emittedChildren++;
                    if ((cellFlow || frameCells || paneCells) && child is BindingNode cbn)
                    {
                        _ctx.UseKit("Layout");
                        string cellLabel = CellLabel(cbn, childBasePath);
                        string cellVar = _ctx.NewVar("cell", "cell");
                        w.Line($"var {cellVar} = SasqKit.ColumnCell({expr}, {(cellLabel == null ? "null" : Lit.Str(cellLabel))});");
                        expr = cellVar;
                        // A C# [Header] on a cell's field wraps the CELL, not the field — live's shape, where
                        // the bold line spans the column and the cell's own label sits under it. EmitBinding
                        // deliberately leaves this to us for cells (its `!asCell` arm).
                        string cellHeader = CodeHeaderFor(cbn, Combine(childBasePath, cbn.PropertyPath));
                        if (cellHeader != null)
                            expr = EmitHeaderWrap(w, cbn, expr, cellHeader, "cell");
                    }
                    // A Rows container's authored per-row height, as a min-height FLOOR so taller content still
                    // grows rather than clipping. Gated on the container being track-resizable for live's
                    // reason (LayoutTreeBuilder.ApplyRowsLayout applies it AFTER the `if (!resizable) return`):
                    // with no divider to undo it, a stale height would pin a row to a size the buyer cannot
                    // change. RowHeight is a DIFFERENT field from Style.height, which StyleEmitter already
                    // emits — do not conflate them.
                    // Emitted through SasqKit.RowFloor rather than a raw style.minHeight write: on a slider row
                    // the element IS the slider, whose Restyle re-runs on every GeometryChangedEvent and would
                    // erase this floor on the frame after the editor opens. RowFloor arbitrates the two claims
                    // (largest wins) and returns the element, so it drops straight into the expression.
                    if (c.Type == ContainerType.Rows && c.TrackResize != TrackResizeMode.Disabled
                        && child.RowHeight > 0f)
                    {
                        _ctx.UseKit(null); // Core: RowFloor lives there
                        string rowVar = _ctx.NewVar("row", "row");
                        w.Line($"var {rowVar} = SasqKit.RowFloor({expr}, {Lit.F(child.RowHeight)});");
                        expr = rowVar;
                    }
                    if (trackCells)
                    {
                        _ctx.UseKit(null); // Core (LabelScope)
                        expr = $"SasqKit.LabelScope({expr})";
                    }
                    w.Line($"{content}.Add({expr});");
                }
            }

            // "Columns: N" is a track-count CAP, not a hint: past N children the live builder stops laying one
            // long row and regroups into rows of N (RegroupGridIntoRows), so an over-filled Columns wraps
            // instead of squeezing every cell thinner and thinner. Ignore the cap and a 10-child Columns
            // capped at 3 exports as ten slivers on one line.
            bool columnsOverflow = c.Type == ContainerType.Columns
                && c.GridColumns > 0
                && EmittableChildCount(c) > c.GridColumns;

            if (cellFlow && c.Type == ContainerType.Columns && columnsOverflow)
            {
                _ctx.UseKit("Layout");
                // Equal columns of exactly the cap, which is what the grid regroup produces.
                // A wrapped Columns aligns vertically WITHIN each generated row — the stack itself has to stay
                // stretched or the rows shrink-wrap horizontally (live's wrapped branch). The rows only exist
                // after the regroup, and the regroup re-runs, so the alignment travels as an argument rather
                // than being stamped on once from out here.
                string wrapAlign = c.Style.alignItems.isOverridden
                    ? $", rowAlign: {(int)c.Style.alignItems.value}" : "";
                w.Line($"SasqKit.LayoutGrid({content}, {(int)GridConstraint.FixedColumnCount}, {Mathf.Max(1, c.GridColumns)}, {Lit.F(c.Gap)}, {B(c.TrackResize != TrackResizeMode.Disabled)}, false{wrapAlign});");
            }
            else if (cellFlow && c.Type == ContainerType.Columns)
            {
                _ctx.UseKit("Layout");
                // Columns tracks are drag-resizable unless explicitly disabled — the live TracksResizable rule
                // (Auto ⇒ resizable for any non-root container, and an exported Columns is never the root).
                bool colResizable = c.TrackResize != TrackResizeMode.Disabled;
                // The authored per-column proportions (a divider drag persists ColumnWeight into the layout
                // asset). Omitted entirely when every column is "auto", so an unresized Columns is unchanged.
                string colWeights = TrackWeightsArg(ChildColumnWeights(c));
                w.Line($"SasqKit.LayoutColumns({content}, {Lit.F(c.Gap)}, {B(colResizable)}{(colWeights == null ? "" : ", " + colWeights)});");
            }
            else if (cellFlow && c.Type == ContainerType.Grid)
            {
                _ctx.UseKit("Layout");
                // Grid column weights and row heights only drive the layout when that axis is resizable —
                // live's WeightFor / rowHeights gates — so a non-resizable grid carries neither.
                string gridCols = c.GridResizableColumns ? TrackWeightsArg(c.GridColumnWeights) : null;
                string gridRows = c.GridResizableRows ? TrackWeightsArg(c.GridRowHeights) : null;
                var gridArgs = $"{content}, {(int)c.GridConstraint}, {Mathf.Max(1, c.GridColumns)}, {Lit.F(c.Gap)}, {B(c.GridResizableColumns)}, {B(c.GridResizableRows)}";
                if (gridCols != null) gridArgs += $", colWeightsIn: {gridCols}";
                if (gridRows != null) gridArgs += $", rowHeightsIn: {gridRows}";
                w.Line($"SasqKit.LayoutGrid({gridArgs});");
            }
            // "Rows: N" is the same cap transposed — the (N+1)th child starts a new COLUMN beside the first
            // stack rather than continuing downward. The regroup supplies its own gap.
            else if (c.Type == ContainerType.Rows && c.GridColumns > 0
                     && EmittableChildCount(c) > c.GridColumns && content != null)
            {
                _ctx.UseKit("Layout");
                w.Line($"SasqKit.LayoutRowsCapped({content}, {Mathf.Max(1, c.GridColumns)}, {Lit.F(c.Gap)});");
            }
            // Inter-child gap, for the THREE types the live builder actually applies it to — and only those.
            // LayoutTreeBuilder calls ApplyGap for Rows and Columns alone, and a Grid gets its gap inside the
            // grid layout helper; every other container type ignores Gap entirely, which is why the designer
            // offers the Spacing row for exactly these three. Ungated, this branch was the catch-all for the
            // whole else-chain, so a Card / Foldout / Frame / Tabs carrying a stale or quick-edited Gap
            // spaced its children out in the EXPORT and nowhere else — a live/export parity break with no
            // authoring surface behind it. Columns and Grid cannot reach here today (cellFlow claims both
            // above); they are named anyway so this set mirrors live's rather than depending on that.
            else if (c.Gap > 0f && content != null
                     && (c.Type == ContainerType.Rows || c.Type == ContainerType.Columns
                         || c.Type == ContainerType.Grid))
            {
                string prop = rowFlow ? "marginRight" : "marginBottom";
                w.Line($"for (int __i = 0; __i < {content}.childCount - 1; __i++) {content}[__i].style.{prop} = {Lit.F(c.Gap)};");
            }

            EmitSectionGaps(w, c, emittedSections, emittedChildren);

            // A titled section already shows the header the section was built FROM, so hide only the native
            // [Header] decorator whose text DUPLICATES this section's title/provenance — the one Unity's
            // PropertyField would otherwise draw on top of the section title. A field's own DISTINCT [Header]
            // (e.g. "Grouping" inside a "Plain Card") is a real sub-header and must keep rendering, exactly
            // like the live renderer (which shows every non-consumed [Header]). Import-created layouts also
            // mark the consumed lead field hidden per-field (see EmitBinding); this covers code-built layouts.
            if (!string.IsNullOrEmpty(c.Title))
            {
                // A split frame routes children into panes; sweep the whole frame root for the dup header.
                string dupScope = content ?? el;
                string dupCond = $"__hdr.text == {Lit.Str(c.Title)}";
                if (!string.IsNullOrEmpty(c.SourceHeader) && c.SourceHeader != c.Title)
                    dupCond += $" || __hdr.text == {Lit.Str(c.SourceHeader)}";
                w.Line($"{dupScope}.RegisterCallback<GeometryChangedEvent>(__e => {dupScope}.Query<VisualElement>(className: \"unity-decorator-drawers-container\").ForEach(__d => {{ var __hdr = __d.Q<Label>(); if (__hdr != null && ({dupCond})) __d.style.display = DisplayStyle.None; }}));");
            }

            ApplyContainerCommon(w, c, el, basePath, groupShell);
            EmitColumnsAlignmentTransfer(w, c, el, content, columnsOverflow);
            w.Line($"return {el};");
            w.CloseBrace();
            _ctx.Methods.Add(w.ToString());
            _ctx.RowBaseExpr = prevRowBase;
            return method;
        }

        /// <summary>
        /// Emits an alternating text/code body for markdown containing fenced ``` blocks, and returns its
        /// variable — or NULL when there is no fenced block, in which case callers keep their single-Label shape.
        /// <para>
        /// Done STATICALLY: the markdown is known at export time, so the blocks are split here and the syntax
        /// highlighting is baked with the package's own CodeHighlighter. No runtime markdown parser, no
        /// highlighter and no new kit section travel to the buyer — only the two small builders
        /// (SasqKit.MarkdownText / SasqKit.CodeBlock) the emitted calls need.
        /// </para></summary>
        private string EmitMarkdownBody(CodeWriter w, string markdown)
        {
            if (string.IsNullOrEmpty(markdown) || !SasquatchMarkdown.HasFencedCode(markdown)) return null;
            _ctx.UseKit("HelpBox");
            string body = _ctx.NewVar("mdBody", "mdBody");
            w.Line($"var {body} = new VisualElement();");
            w.Line($"{body}.style.flexDirection = FlexDirection.Column;");
            foreach (var block in SplitFencedBlocks(markdown))
            {
                if (block.Value)
                    w.Line($"{body}.Add(SasqKit.CodeBlock({Lit.Str(CodeHighlighter.Highlight(block.Key))}));");
                else
                    w.Line($"{body}.Add(SasqKit.MarkdownText({Lit.Str(SasquatchMarkdown.ToRichText(block.Key))}));");
            }
            return body;
        }

        /// <summary>Splits markdown into (content, isCode) blocks on ``` fences — the same walk live's
        /// SasquatchMarkdown.BuildMarkdownElement does, including its "unterminated fence renders what we
        /// have" rule. Keep the two in step: a divergence shows up as an exported help box whose fences
        /// render differently from the live one — pinned against live's own element output by
        /// <c>TestExportFencedCodeSplitMatchesTheLiveBuilder</c>.</summary>
        internal static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, bool>>
            SplitFencedBlocks(string markdown)
        {
            var result = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, bool>>();
            if (string.IsNullOrEmpty(markdown)) return result;

            var text = new System.Text.StringBuilder();
            var code = new System.Text.StringBuilder();
            bool inCode = false;

            void FlushText()
            {
                string t = text.ToString().Trim('\n');
                text.Clear();
                if (t.Length > 0)
                    result.Add(new System.Collections.Generic.KeyValuePair<string, bool>(t, false));
            }
            void FlushCode()
            {
                result.Add(new System.Collections.Generic.KeyValuePair<string, bool>(
                    code.ToString().Trim('\n'), true));
                code.Clear();
            }

            foreach (var line in markdown.Replace("\r", "").Split('\n'))
            {
                if (line.TrimStart().StartsWith("```", System.StringComparison.Ordinal))
                {
                    if (inCode) { FlushCode(); inCode = false; }
                    else { FlushText(); inCode = true; }
                    continue;
                }
                var buf = inCode ? code : text;
                if (buf.Length > 0) buf.Append('\n');
                buf.Append(line);
            }
            if (inCode && code.Length > 0) FlushCode();   // unterminated fence → render what we have
            FlushText();
            return result;
        }

        /// <summary>A Columns container's per-child ColumnWeight, in child order (0 = "auto", an equal share).
        /// Only BindingNode/child cells carry a weight; the list is positional, so an unweighted child still
        /// occupies its slot.</summary>
        /// <summary>How many of <paramref name="c"/>'s children this export will actually emit. Twin of the live
        /// builder's <c>RenderableChildCount</c>: every "Columns: N / Rows: N" cap is a count comparison, and a
        /// hidden child is never emitted, so reading <c>Children.Count</c> would keep a container in wrap mode
        /// after it no longer has enough children to wrap. The exported editor has no design mode, so unlike the
        /// live twin there is no build-mode arm here.</summary>
        private int EmittableChildCount(ContainerNode c)
        {
            var kids = c?.Children;
            if (kids == null) return 0;

            int n = 0;
            for (int i = 0; i < kids.Count; i++)
                if (WillEmit(kids[i])) n++;
            return n;
        }

        /// <summary>
        /// Whether <c>EmitElementExpr</c> will emit anything for <paramref name="node"/> — the ONE predicate
        /// every count and every positional array in this emitter must ask. Twin of the live builder's
        /// <c>LayoutTreeBuilder.WillBuild</c>, minus its design-mode arm (an exported editor has no design
        /// surface), and it must list the same skips the emitter's own prologue opens with.
        /// </summary>
        private bool WillEmit(LayoutNode node)
        {
            if (node == null) return false;
            if (node.HiddenInInspector) return false;
            if (_toggleMirrors != null && _toggleMirrors.Contains(node)) return false;
            if (_orphanedSpacers != null && _orphanedSpacers.Contains(node)) return false;
            return true;
        }

        private System.Collections.Generic.List<float> ChildColumnWeights(ContainerNode c)
        {
            if (c.Children == null) return null;
            var list = new System.Collections.Generic.List<float>(c.Children.Count);
            // Positional against the EMITTED cells, not the model — a child that produces no cell must produce
            // no weight either, or every later column reads its left neighbour's share. WillEmit, not a bare
            // HiddenInInspector check: an attribute-owned [Space] orphaned by hiding its field is dropped by
            // the same prologue and used to leave its entry behind. The live path is immune to this class of
            // bug because it applies weights per element BY IDENTITY (ColumnWeightOf on the built child), so
            // the array is an export-only construct and an export-only hazard.
            foreach (var child in c.Children)
            {
                if (!WillEmit(child)) continue;
                list.Add(child.ColumnWeight);
            }
            return list;
        }

        /// <summary>A `new float[] { … }` literal for a track-sizing array, or NULL when every entry is "auto"
        /// — the house idiom: a layout nobody has resized emits no array at all.</summary>
        private static string TrackWeightsArg(System.Collections.Generic.IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0) return null;
            bool any = false;
            foreach (var v in values) if (v > 0f) { any = true; break; }
            if (!any) return null;

            var sb = new System.Text.StringBuilder("new float[] { ");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Lit.F(Mathf.Max(0f, values[i])));
            }
            sb.Append(" }");
            return sb.ToString();
        }

        /// <summary>
        /// Moves a Columns container's "Vertical Alignment" off the wrapper and onto the cell row, then resets
        /// the wrapper — the same transfer the live <c>LayoutTreeBuilder.BuildNode</c> Columns branch does.
        /// <para>
        /// A Columns container is two elements: a flexDirection.Column WRAPPER (title + content) and the
        /// flexDirection.Row cell row inside it. `alignItems` on the wrapper is its CROSS axis, which for a
        /// column is horizontal — so leaving the setting the designer calls "Vertical Alignment" there makes the
        /// whole block shrink-wrap sideways while the cells stay stretched: the wrong axis on the wrong element.
        /// Only on the row is the cross axis vertical.
        /// </para><para>
        /// The wrapped (over-count) case is handled at the LayoutGrid call instead — its rows are built by the
        /// kit at runtime and rebuilt on every regroup, so the alignment has to travel as an argument.
        /// </para>
        /// </summary>
        private void EmitColumnsAlignmentTransfer(CodeWriter w, ContainerNode c, string el, string content,
            bool columnsOverflow)
        {
            if (c.Type != ContainerType.Columns || content == null) return;
            var s = c.Style;
            if (!s.alignItems.isOverridden && !s.justify.isOverridden) return;

            // Wrapped rows take their alignment through LayoutGrid's rowAlign; only the reset applies here.
            if (!columnsOverflow)
            {
                if (s.alignItems.isOverridden) w.Line($"{content}.style.alignItems = Align.{s.alignItems.value};");
                if (s.justify.isOverridden) w.Line($"{content}.style.justifyContent = Justify.{s.justify.value};");
            }

            // Hand the wrapper back its defaults, so the override reads ONLY on the row. Emitted rather than
            // simply skipped on the wrapper because ApplyContainerCommon has already written it there.
            if (s.alignItems.isOverridden) w.Line($"{el}.style.alignItems = Align.Stretch;");
            if (s.justify.isOverridden) w.Line($"{el}.style.justifyContent = Justify.FlexStart;");
        }

        private void EmitTabs(CodeWriter w, ContainerNode c, string el, string childBasePath)
        {
            _ctx.UseKit("Tabs");
            string outer = _ctx.NewVar("tabsOuter", "tabsOuter");
            w.Line($"var {outer} = new VisualElement();");
            w.Line($"{outer}.style.flexDirection = FlexDirection.Column;");
            EmitContainerTitle(w, c, outer, 4f, "SasqTheme.LabelBright");

            var labels = new System.Collections.Generic.List<string>();
            var pageVars = new System.Collections.Generic.List<string>();
            // The MODEL node behind each emitted page, in built order. Kept for exactly one reason:
            // ActiveTabIndex is a MODEL index (see LayoutTreeBuilder's BuiltIndexOfModel), so a hidden tab
            // BEFORE the active one shifts every later built index down and the two stop meaning the same
            // number. Clamping the model index into the built range — what this used to do — does not
            // convert it, it just keeps it in bounds while pointing at the wrong tab.
            var builtTabNodes = new System.Collections.Generic.List<LayoutNode>();
            int i = 0;
            if (c.Children != null)
            {
                for (int ci = 0; ci < c.Children.Count; ci++)
                {
                    var child = c.Children[ci];
                    string expr = EmitChildExpr(w, child, ci, childBasePath);
                    if (expr == null) continue;
                    string pv = _ctx.NewVar("tabPage", "tabPage");
                    w.Line($"var {pv} = {expr};");
                    labels.Add(Lit.Str(TabLabel(child, i)));
                    pageVars.Add(pv);
                    builtTabNodes.Add(child);
                    i++;
                }
            }

            // Nothing built — no strip. Twin of the live builder's `if (builtTabs.Count == 0 && !designMode)
            // ClearTabPanel(contentArea)`: an empty accent box reads as a broken widget rather than an empty
            // one. It also sidesteps a hard COMPILE error: the labels literal is implicitly typed, and
            // `new[] {  }` is CS0826 ("no best type found for implicitly-typed array"), which implicit array
            // creation can never be rescued from by the parameter type it is passed to. An exported inspector
            // that does not compile is the worst failure this emitter has, and it lands in generated code the
            // buyer cannot fix.
            if (i == 0)
            {
                w.Line($"var {el} = {outer};");
                return;
            }

            // Explicitly typed anyway, so the shape above can never silently become a compile error again.
            string labelsLit = "new string[] { " + string.Join(", ", labels) + " }";
            string pagesLit = "new VisualElement[] { " + string.Join(", ", pageVars) + " }";
            // MODEL index → BUILT index, the emitter's twin of BuiltIndexOfModel. -1 (the active tab is the
            // hidden one, or the index is stale) falls back to the first surviving tab, exactly as the live
            // builder's `if (selectedIndex < 0) selectedIndex = 0;` does.
            int active = 0;
            if (c.Children != null && c.ActiveTabIndex >= 0 && c.ActiveTabIndex < c.Children.Count)
            {
                int mapped = builtTabNodes.IndexOf(c.Children[c.ActiveTabIndex]);
                if (mapped >= 0) active = mapped;
            }
            string accentArg = c.TabAccentColor.a > 0f ? $", accent: {Lit.Col(c.TabAccentColor)}" : "";
            w.Line($"{outer}.Add(SasqKit.TabStrip({labelsLit}, {pagesLit}, {active}{accentArg}));");
            w.Line($"var {el} = {outer};");
        }

        private void ApplyContainerCommon(CodeWriter w, ContainerNode c, string el, string basePath, bool groupShell = false)
        {
            // _ctx so a container's tooltip is CLAIMED (anchored to its title) rather than assigned — see
            // StyleEmitter.EmitTooltip. No path is passed: only a bound field can carry a C# [Tooltip].
            StyleEmitter.EmitLayoutStyle(w, el, c, _ctx);
            // Section color-coding runs AFTER the style pass so its wash/rail overrides the plain surface,
            // mirroring the live builder (ApplySectionTint / ApplyCardAccentStripe run after StyleApplier).
            EmitSectionColorCoding(w, c, el, groupShell);
            ConditionEmitter.Emit(w, _ctx, c, el, basePath);
        }

        // Emits a section's optional color-coding — a full-color accent rail down the left edge plus (for a
        // Section Tint) a faint wash over the panel — matching the live flat fallback in ApplySectionTint /
        // ApplyCardAccentStripe.
        private void EmitSectionColorCoding(CodeWriter w, ContainerNode c, string el, bool groupShell)
        {
            bool hasTint = c.SectionTintColor.isOverridden;
            bool hasStripe = c.AccentStripeColor.isOverridden;
            if (!hasTint && !hasStripe) return;

            // The group shell (SasqKit.Group) already baked the tint/stripe wash + rail for a collapsible or
            // color-coded foldout section — nothing to add, and no fidelity note: the shell is full parity.
            if (groupShell) return;

            // A FRAME draws neither, and that is parity, not a gap. Live guards BOTH call sites with
            // `Type != ContainerType.Frame` (LayoutTreeBuilder.cs, the ApplySectionTint / ApplyCardAccentStripe
            // pair): a 3px hard left border would corrupt a circle/pill ring, and the tint's header/body class
            // queries have nothing to match on a frame. Emitting nothing is exactly what the buyer should get,
            // so this must not carry a note either — the old one told them they were missing something they
            // would not have seen with the package installed.
            if (c.Type == ContainerType.Frame) return;

            // TABS belongs in the flat set. Live builds a Tabs root as a bare `new VisualElement()` — no
            // `.sasq-group` class and not a Foldout — so ApplySectionTint falls past both dispatch branches
            // into the SAME flat wash the Card / Rows / Columns / Grid path takes, and ApplyCardAccentStripe
            // finds no inset rail child to recolour and writes the same border rail. There was never a
            // "shaped or tabbed" problem to note here; the export was simply not looking.
            bool flat = c.Type == ContainerType.Card || c.Type == ContainerType.Rows
                || c.Type == ContainerType.Columns || c.Type == ContainerType.Grid
                || c.Type == ContainerType.Tabs;
            if (!flat)
            {
                // Unreachable across the seven container types that exist today: Frame returned above, a
                // colour-coded Foldout always routes through the group shell (see `foldoutColorCoded` in
                // EmitContainerMethod), and every other type is in the set above. Kept as the safe default for
                // a container type added later — a note the buyer can read beats a wash silently painted onto
                // a shape it was never designed for.
                _ctx.Note("A section tint / accent stripe on this container type needs the package (shown plain standalone).");
                return;
            }

            _ctx.UseKit(null); // SasqTheme.IsLight

            // Section Tint first (rail + wash); the Accent Stripe is applied LAST so its rail color wins when
            // both are set — the stripe is the deliberate accent that owns the left edge (live parity).
            if (hasTint)
            {
                Color t = c.SectionTintColor.value;
                string b = _ctx.NewVar("flatBase", "flatBase");
                w.Line($"float {b} = SasqTheme.IsLight ? 0.82f : 0.20f;");
                w.Line($"{el}.style.backgroundColor = new Color(Mathf.Lerp({b}, {Lit.F(t.r)}, 0.25f), Mathf.Lerp({b}, {Lit.F(t.g)}, 0.25f), Mathf.Lerp({b}, {Lit.F(t.b)}, 0.25f), 0.55f);");
                // SectionRailAuto, not a raw border: a Card is built at radius 6, and a 3px left border tapers
                // lumpily into a rounded corner. The kit helper picks the strip or the border off the element's
                // own radius, exactly as the live ApplyFlatSectionRail does — one decision, two renderers.
                w.Line($"SasqKit.SectionRailAuto({el}, new Color({Lit.F(t.r)}, {Lit.F(t.g)}, {Lit.F(t.b)}, 1f));");
            }
            if (hasStripe)
            {
                Color s = c.AccentStripeColor.value;
                w.Line($"SasqKit.SectionRailAuto({el}, new Color({Lit.F(s.r)}, {Lit.F(s.g)}, {Lit.F(s.b)}, 1f));");
            }
        }

        // One ink (SasqTheme.LabelBright) and one 4px gap for every container title, matching the live
        // LayoutTreeBuilder.TitleInk / TitleGap — a standalone editor's headings have to sit on the same ladder
        // the package's do, or the same layout reads differently once it's exported.
        private void EmitContainerTitle(CodeWriter w, ContainerNode c, string el, float marginBottom, string colorExpr)
        {
            if (string.IsNullOrEmpty(c.Title)) return;
            _ctx.UseKit(null); // SasqTheme
            string t = _ctx.NewVar("title", "title");
            // Named so the kit's tooltip claim can anchor the container's tooltip to its heading, the way live
            // anchors to the .sasq-node-title label (SasqKit.SetNodeTooltip → TooltipAnchorRect).
            w.Line($"var {t} = new Label({Lit.Str(c.Title)}) {{ name = \"sasq-kit-node-title\" }};");
            w.Line($"{t}.style.unityFontStyleAndWeight = FontStyle.Bold;");
            w.Line($"{t}.style.marginBottom = {Lit.F(marginBottom)};");
            w.Line($"{t}.style.color = {colorExpr};");
            if (StyleEmitter.HasTextOverride(c.TitleTextStyle))
                StyleEmitter.EmitTextStyle(w, t, c.TitleTextStyle, _ctx);
            w.Line($"{el}.Add({t});");
        }

        /// <summary>One emitted child that is a SECTION (container or collection): the C# variable holding its
        /// element, the node it came from, and its position among the children that actually emitted.</summary>
        private readonly struct EmittedSection
        {
            public readonly string Var;
            public readonly LayoutNode Node;
            public readonly int Ordinal;
            public EmittedSection(string var, LayoutNode node, int ordinal) { Var = var; Node = node; Ordinal = ordinal; }
        }

        /// <summary>
        /// The export twin of <c>LayoutTreeBuilder.ApplySectionGaps</c>: in a VERTICAL stack every section leaves
        /// the same gap below itself (and none at all when it is last), so a Frame above a Card is spaced exactly
        /// like a Card above a Card. Columns / Grid are excluded — their children sit side by side and their own
        /// helper owns the spacing — as are Tabs (one page at a time) and split frames (absolute panes). The
        /// container's own Spacing rides on top for a Rows, matching the live ApplyGap, which ADDS to the margin
        /// rather than replacing it. Emitted last so it wins over the generic gap loop above.
        /// </summary>
        private void EmitSectionGaps(CodeWriter w, ContainerNode c,
                                     System.Collections.Generic.List<EmittedSection> sections, int emittedChildren)
        {
            if (sections == null || sections.Count == 0) return;

            bool rowsOverflow = c.Type == ContainerType.Rows && c.GridColumns > 0
                                && EmittableChildCount(c) > c.GridColumns;
            bool verticalStack = (c.Type == ContainerType.Rows && !rowsOverflow)
                                 || c.Type == ContainerType.Card
                                 || c.Type == ContainerType.Foldout
                                 || (c.Type == ContainerType.Frame && (c.Frame?.Shape ?? FrameShape.RoundedRect) != FrameShape.DiagonalSplit);
            if (!verticalStack) return;

            // Live's ApplyGap runs for Rows (and Columns, which isn't a vertical stack), and ADDS the container's
            // Spacing to whatever margin the child already carries — so a Rows with Spacing gets both.
            float extra = c.Type == ContainerType.Rows && !rowsOverflow && c.Gap > 0f ? c.Gap : 0f;

            foreach (var section in sections)
            {
                // A margin the node owns is never touched — StyleEmitter already wrote it.
                if (!section.Node.Style.marginTop.isOverridden)
                    w.Line($"{section.Var}.style.marginTop = 0f;");
                if (section.Node.Style.marginBottom.isOverridden) continue;
                bool last = section.Ordinal == emittedChildren - 1;
                w.Line($"{section.Var}.style.marginBottom = {Lit.F(last ? 0f : SasquatchInspector.Editor.LayoutTreeBuilder.SectionGap + extra)};");
            }
        }

        private void EmitCardChrome(CodeWriter w, string el)
        {
            _ctx.UseKit(null); // SasqTheme
            w.Line($"{el}.style.backgroundColor = SasqTheme.CardWash;");
            w.Line($"SasqKit.Radius({el}, 6f);");
            w.Line($"SasqKit.Padding({el}, 10f, 10f);");
            // …then the LEFT side alone widens to the shared section gutter, exactly as the live Card branch
            // does: an inset section's title and fields share the edge a Foldout's sit on.
            w.Line($"{el}.style.paddingLeft = {Lit.F(SasquatchInspector.Editor.LayoutTreeBuilder.SectionGutter)};");
            // The Card's own trailing gap, for the contexts EmitSectionGaps deliberately leaves alone (a Card
            // sitting in a Columns/Grid cell). In a vertical stack the parent's pass overwrites this.
            w.Line($"{el}.style.marginBottom = {Lit.F(SasquatchInspector.Editor.LayoutTreeBuilder.SectionGap)};");
        }

        // ---- Bindings & collections ------------------------------------------------------------------------


        /// <summary>
        /// Emits the universal prefix/suffix call for a bound field. Hooked from <see cref="EmitBinding"/> —
        /// the one place that runs for EVERY display type — so this can never drift out of sync with a
        /// per-widget list, exactly like the read-only handling beside it.
        /// <para>
        /// Slider / MinMaxSlider / ProgressBar mount their own affixes around their value read-outs (they have
        /// one or two each), so they are skipped here; their emitters pass the affix TEXT along instead.
        /// </para>
        /// </summary>
        private void EmitAffixes(CodeWriter w, BindingNode node, string field, string path, bool isTableCell = false)
        {
            if (FieldAffixes.OwnsItsAffixes(node.DisplayType)) return;

            // The ONE shared rule, read through the same two accessors the live decorator uses — so header
            // mode cannot mean one thing in the inspector and another in a generated editor. PER SIDE and
            // text-only: an icon-only side has nothing the header string can carry, so it stays in the cell
            // here exactly as it now does live. A whole-node early-out dropped it from BOTH.
            var pre = FieldAffixes.PrefixFor(node, isTableCell);
            var suf = FieldAffixes.SuffixFor(node, isTableCell);
            if (pre.IsEmpty && suf.IsEmpty) return;

            var args = new List<string>();

            void Side(FieldAffix a, string p)
            {
                if (a.IsEmpty) return;
                if (!string.IsNullOrEmpty(a.text)) args.Add($"{p}Text: {Lit.Str(a.text)}");
                if (!string.IsNullOrEmpty(a.iconId))
                {
                    // Baked WHITE and tinted at runtime, the same one-bake-two-skins trick button icons use.
                    var tex = SasquatchIcons.RenderToTexture(a.iconId, 32, Color.white);
                    if (tex != null) args.Add($"{p}Icon: \"{System.Convert.ToBase64String(tex.EncodeToPNG())}\"");
                }
                if (a.mount != AffixMount.Auto) args.Add($"{p}Mount: {(int)a.mount}");
                if (a.style.color.isOverridden) args.Add($"{p}Color: {Lit.Col(a.style.color.value)}");
                if (a.style.fontSize.isOverridden && a.style.fontSize.value > 0f)
                    args.Add($"{p}Size: {Lit.F(a.style.fontSize.value)}");
                if (a.style.fontStyle.isOverridden) args.Add($"{p}Font: {(int)a.style.fontStyle.value}");
                if (a.pillColor.isOverridden) args.Add($"{p}Pill: {Lit.Col(a.pillColor.value)}");
                if (a.gap.isOverridden) args.Add($"{p}Gap: {Lit.F(a.gap.value)}");
                // A custom font asset can't travel into a package-free file — note it and drop it.
                if (a.style.font != null) _ctx.Note("A prefix/suffix custom font is not carried into the export.");
            }

            Side(pre, "pre");
            Side(suf, "suf");
            if (args.Count == 0) return;

            // Per-component only means anything on a field Unity draws as several number boxes on one row.
            //
            // ResolveFieldTYPE, not ResolveField: an ELEMENT-TEMPLATE binding (a List<Vector2>'s own row) has
            // an EMPTY path, and ResolveField returns null for that while ResolveFieldType resolves it to the
            // element type via ResolveRoot. Asking the wrong one reported every element template as
            // non-composite, so per-component fanout — the whole point of "x/y/z" units on a Vector row —
            // was silently dropped from the export while the live path fanned it out correctly.
            var ft = _ctx.ResolveFieldType(path);
            bool composite = ft != null && IsCompositeFieldType(ft);
            if (composite) args.Add("composite: true");
            if (!node.AffixPerComponent) args.Add("perComponent: false");

            _ctx.UseKit(null); // Core (Affixes)
            w.Line($"SasqKit.Affixes({field}, {string.Join(", ", args)});");
        }

        /// <summary>Field types Unity draws as several number boxes on one row — the per-component affix targets.</summary>
        private static bool IsCompositeFieldType(System.Type t)
        {
            return t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)
                || t == typeof(Vector2Int) || t == typeof(Vector3Int)
                || t == typeof(Rect) || t == typeof(RectInt)
                || t == typeof(Bounds) || t == typeof(BoundsInt)
                || t == typeof(Quaternion);
        }

        private string EmitBinding(CodeWriter w, BindingNode node, string basePath, bool asCell = false)
        {
            string path = Combine(basePath, node.PropertyPath);
            // An empty path is genuinely unbound at ordinary scope — but inside a LIST ROW it is the
            // ELEMENT-TEMPLATE binding, i.e. the element itself, whose runtime path comes from the row's
            // base expression rather than from any literal. Skipping it here is what would drop the only
            // binding a List<Vector2> can have.
            if (string.IsNullOrEmpty(path) && _ctx.RowBaseExpr == null)
            {
                _ctx.Note("A binding with no property path was skipped.");
                return null;
            }
            string defaultLabel = ObjectNames.NicifyVariableName(LastSegment(path));

            string field = DisplayTypeEmitter.Emit(w, _ctx, node, path, defaultLabel);
            // The resolved path travels so a C# [Tooltip] on the field can be baked (and a blanked tooltip can
            // overwrite one) — live resolves the same three-way fallback per bound field.
            StyleEmitter.EmitLayoutStyle(w, field, node, _ctx, path);
            DisplayTypeEmitter.EmitFieldStyling(w, _ctx, node, field);

            // Label-above: stack the label on its own row above the input (the live BindingFactory adds the
            // sasq-label-above USS class; the export has no sheet, so the kit reproduces it inline). Mutually
            // exclusive with a hidden label (there's nothing to stack), and with the display types the live
            // renderer makes inert — the kit's fallback branch WOULD stack a card that live leaves alone, so
            // dropping the call here is what keeps the two renderers agreeing.
            //
            // TWO more gates live has and this used to be missing, and they are the same gate live writes them
            // as — `node.UseStandardAlignment && forceHideLabel != true` around the whole stack-or-align
            // choice:
            //  * a COLUMN CELL builds its field with forceHideLabel, so the widget's own label is hidden and
            //    the CELL supplies the label beside it. Live therefore never stacks inside a cell. Emitting
            //    the call anyway turned every label-above widget in a Columns container into a two-row cell —
            //    a progress bar with its name on the line above and the bar full-width beneath, against
            //    live's [name | bar] on one line, and 17px taller for it.
            //  * alignment OFF means the row is not in the label column at all, and live leaves the label
            //    exactly where the widget put it rather than stacking it.
            //  * and the FIELD TYPE, for the third time in this list: a SerializedDictionary field keeps
            //    DisplayType.Default and is auto-routed to the dictionary table, whose header already carries
            //    the field name — live makes the flag inert there, so emitting the call would stack a second
            //    copy of the name that live does not draw. ResolveFieldType, not ResolveField, for the
            //    element-template reason spelled out in EmitAffixes above.
            if (node.LabelAbove && !node.IsLabelHidden && node.UseStandardAlignment && !asCell
                && DisplayTypeCatalog.SupportsLabelAbove(node.DisplayType, _ctx.ResolveFieldType(path)))
            {
                _ctx.UseKit(null); // Core (LabelAbove)
                w.Line($"SasqKit.LabelAbove({field});");
            }

            // A composite's number boxes must take their width from the ROW, never from their own text.
            // Emitted here for the same reason LabelAbove is: the live renderer gets it from one rule in
            // SasquatchWidgets.uss, and an exported editor has no stylesheet. Gated on the field TYPE rather
            // than emitted everywhere, so an inspector full of scalars pays nothing for it.
            // ResolveFieldTYPE, not ResolveField — same trap EmitAffixes documents above: an ELEMENT-TEMPLATE
            // binding (a List<Vector3>'s own row) has an EMPTY path, and ResolveField returns null for it, so
            // every collection row would have reported non-composite and quietly shipped the bug.
            var compositeType = _ctx.ResolveFieldType(path);
            if (compositeType != null && IsCompositeFieldType(compositeType))
            {
                _ctx.UseKit(null); // Core (ClampCompositeBoxes)
                w.Line($"SasqKit.ClampCompositeBoxes({field});");
            }

            // Prefix / suffix. Emitted from EmitBinding for the same reason the live decorator hooks
            // BindingFactory's shared tail: it is the one place that runs for EVERY display type.
            EmitAffixes(w, node, field, path);

            if (node.IsReadOnly)
            {
                w.Line($"{field}.SetEnabled(false);");
                // SetEnabled dims the field but does NOT remove Unity's numeric label drag-scrubber, so a
                // read-only number could still change by dragging its label — swallow pointer-down in the
                // capture phase for numeric fields, exactly like the live read-only path.
                var roField = _ctx.ResolveField(path);
                if (roField != null && IsNumericFieldType(roField.FieldType))
                {
                    w.Line($"// Read-only: block the numeric label drag-scrubber (SetEnabled leaves it alive).");
                    w.Line($"{field}.RegisterCallback<PointerDownEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);");
                }
                // Read-only: also drop the drag-scrubber slide (<>) cursor so the label doesn't imply the value is
                // editable — the "--with-dragger" class survives SetEnabled, and the theme keys the cursor off it.
                // A no-op on fields with no numeric dragger. Mirrors the live BindingFactory.RemoveDraggerCursor.
                w.Line($"{field}.RegisterCallback<GeometryChangedEvent>(__e => {field}.Query<Label>(className: \"unity-base-field__label--with-dragger\").ForEach(__l => __l.RemoveFromClassList(\"unity-base-field__label--with-dragger\")));");
            }

            // The dim SetEnabled(false) buys is subtree opacity, which UI Toolkit composites PER ELEMENT —
            // a slider knob goes see-through and the track reads through it. Rebuild that dim in colour so
            // the knobs stay solid, exactly like the live path does.
            //
            // Emitted for EVERY field, not just read-only ones: a toggle group's bound bool and a condition's
            // Enable/Disable outcome disable the field from ABOVE, and either can flip long after build. The
            // kit costs one class check on an enabled field and no-ops when the field holds no slider — which
            // is also what keeps a [Range] Default field (Unity builds its slider lazily) covered without
            // this having to track the display-type list.
            var dimStyle = node.DisplayStyleData;
            // Colour resolution mirrors the kit's own resting fills: an explicit knob colour wins, and a
            // min-max with only an accent tints its thumbs with that (SasqKit.MinMaxSlider's fallback
            // chain). Everything else falls through to the kit's theme grey.
            string dimKnob = dimStyle.sliderKnobColor.a > 0f ? Lit.Col(dimStyle.sliderKnobColor)
                : node.DisplayType == DisplayType.MinMaxSlider && dimStyle.accent.isOverridden
                    ? Lit.Col(dimStyle.accent.value)
                    : "null";
            string dimCenter = dimStyle.sliderCenterBarColor.a > 0f ? Lit.Col(dimStyle.sliderCenterBarColor) : "null";
            _ctx.UseKit(null); // Core (DisabledSolidKnobs)
            w.Line($"SasqKit.DisabledSolidKnobs({field}, {dimKnob}, {dimCenter});");

            // A native [Space] is always our own SpaceNode (or intentionally dropped) — hide Unity's decorator
            // so it never draws in addition, exactly like the live BindingFactory does for every field.
            EmitSuppressSpaceDecorator(w, field);

            // Header handling mirrors BindingFactory.CreateBindingField: an explicit designer override (or a
            // blanked header) renders our own bold label and suppresses the field's native [Header] decorator;
            // otherwise a Default field's own code [Header] is left to render natively (the "Grouping" case).
            string ownHeader = null;
            if (node.IsHeaderHidden)
            {
                EmitSuppressDecoratorHeader(w, field);
            }
            else if (node.EffectiveHeader != null)
            {
                ownHeader = node.EffectiveHeader;
                EmitSuppressDecoratorHeader(w, field);
            }
            // The THIRD live branch, which the emitter used to be missing. When the widget is not Default,
            // Unity's PropertyField never runs and its native [Header] decorator never draws — so live resolves
            // the C# attribute itself and wraps. Without the twin, an exported panel silently DROPS every
            // `[Header("…")]` written above a slider / badge grid / toggle: measured on SxLiveDebug, where
            // "Recorded channels" and "Demo driver" were simply absent from the export.
            // A COLUMN CELL still gets its header — live renders one there too — but the wrap happens in the
            // PARENT, around the finished SasqKit.ColumnCell, because live's header spans the whole cell
            // rather than sitting beside the cell's own label. Everything else wraps right here.
            else if (!asCell)
            {
                string codeHeader = CodeHeaderFor(node, path);
                if (codeHeader != null)
                {
                    ownHeader = codeHeader;
                    EmitSuppressDecoratorHeader(w, field);
                }
            }

            string result = field;
            if (ownHeader != null)
            {
                result = EmitHeaderWrap(w, node, field, ownHeader, LastSegment(path));
            }
            else if (StyleEmitter.HasTextOverride(node.HeaderTextStyle) && !node.IsHeaderHidden)
            {
                // No override header rendered, but a Header text style is set: it targets the field's native code
                // [Header] decorator label (which has no sasq class), styled deferred as its decorator builds late.
                // Mirrors ApplyBindingHeaderStyle's fallback branch.
                EmitDecoratorHeaderStyle(w, field, node.HeaderTextStyle);
            }

            // Per-field help chip: a "?" beside the field revealing a collapsible markdown help card below it
            // (opt-in via ShowHelpChip + non-empty text). Mirrors the live BindingFactory.WrapWithHelpChip; the
            // markdown is baked to rich text at export (the converter/palette are available now). Right/Center →
            // the field's right edge, Left → just after the label.
            if (node.ShowHelpChip && !string.IsNullOrEmpty(node.HelpText))
            {
                _ctx.UseKit("HelpBox");
                string helpRich = SasquatchMarkdown.ToRichText(node.HelpText);
                bool leftPlacement = node.HelpChipAlignment == ImageAlignment.Left;
                string help = _ctx.NewVar(LastSegment(path) + "Help", "help");
                w.Line($"var {help} = SasqKit.FieldHelpChip({result}, {Lit.Str(helpRich)}, {B(leftPlacement)});");
                // Per-field Help Text Style lands on the card's message label, exactly where live applies it
                // (TextStyleApplier.Apply on the card's first child). Only fetched when something was actually
                // authored, so a default-styled help card emits no extra line.
                if (StyleEmitter.HasTextOverride(node.HelpTextStyle))
                {
                    string helpMsg = _ctx.NewVar(LastSegment(path) + "HelpMsg", "helpMsg");
                    w.Line($"var {helpMsg} = SasqKit.HelpCardLabel({help});");
                    StyleEmitter.EmitTextStyle(w, helpMsg, node.HelpTextStyle, _ctx);
                }
                result = help;
            }

            // Inline validation — Required / numeric range (warn or clamp) / a custom bool method — surfaced as a
            // HelpBox under the field + a red outline, re-evaluated live. Mirrors the live ValidationBinder.Attach,
            // applied AFTER the header/help-chip wrap (the live order: it wraps the whole CreateBindingField result).
            result = EmitFieldValidation(w, node, path, result);

            // The Live Debug graph row, OUTSIDE validation's wrapper so the graph sits below the validation
            // message rather than between the field and its own error text — the live order, node for node
            // (ValidationBinder.Attach, then DebugRowBinder.Attach).
            result = EmitDebugRow(w, node, path, result);

            // Full-width row normalization, in the live order: LayoutTreeBuilder does this to the OUTERMOST
            // element, after the validation and debug wrappers and before conditions — and only when the row
            // is not a cell. Without it the exported panel keeps the editor theme's ~3px horizontal field
            // margin on every row and sits 3px inside both edges the live render reaches, while the label
            // column's right edge (pinned in absolute terms) stays put — which is exactly the "the export
            // spaces things differently" report.
            if (!asCell)
            {
                _ctx.UseKit(null); // Core (NormalizeFullRow)
                w.Line($"SasqKit.NormalizeFullRow({result}, {field});");
            }

            ConditionEmitter.Emit(w, _ctx, node, result, basePath);

            // Node animations (hover / click / value-changed / appear) — wired onto the final element, mirroring the
            // live AnimationBinder.Apply (which runs after conditions on the outermost element).
            EmitAnimations(w, node, result);
            return result;
        }

        // NodeAnimation.Bounce's own field initializer. The kit parameter carries the same default, so a
        // default-bounce spring emits no bounce argument at all — "defaults emit nothing".
        private const float DefaultAnimBounce = 0.35f;

        // Emits SasqKit.WireAnimation calls for each of a node's configured animations. EVERY effect travels as
        // itself, spring-physics ones included: the kit carries a baked closed-form twin of the package's spring
        // solver, the same way it carries a hand-copied twin of the easing table. Bounce rides along for the three
        // spring effects only — Easing is precisely what a spring replaces — and only when it deviates from the
        // model default, which keeps every already-exported non-spring animation byte-identical.
        /// <summary>
        /// The live <c>BindingFactory.WrapWithHeader</c>: a bold secondary-coloured label stacked above
        /// <paramref name="element"/> in its own column. Shared by the two callers that need it — the binding
        /// itself, and the COLUMN-CELL path, where live wraps the finished cell rather than the raw field, so
        /// the header spans the cell instead of landing beside its label.
        /// </summary>
        private string EmitHeaderWrap(CodeWriter w, BindingNode node, string element, string headerText, string nameHint)
        {
            string group = _ctx.NewVar(nameHint + "Group", "group");
            w.Line($"var {group} = new VisualElement();");
            _ctx.UseKit(null); // SasqTheme
            string hl = _ctx.NewVar(nameHint + "Header", "header");
            w.Line($"var {hl} = new Label({Lit.Str(headerText)});");
            w.Line($"{hl}.style.unityFontStyleAndWeight = FontStyle.Bold;");
            w.Line($"{hl}.style.color = SasqTheme.LabelSecondary;");
            w.Line($"{hl}.style.marginTop = 6f;");
            w.Line($"{hl}.style.marginBottom = 2f;");
            // The node's Header text style overrides the defaults above (bold 14 orange, etc.) — mirrors the
            // live ApplyBindingHeaderStyle, which styles our own header label after WrapWithHeader seeds it.
            if (StyleEmitter.HasTextOverride(node.HeaderTextStyle))
                StyleEmitter.EmitTextStyle(w, hl, node.HeaderTextStyle, _ctx);
            w.Line($"{group}.Add({hl});");
            w.Line($"{group}.Add({element});");
            return group;
        }

        /// <summary>
        /// The C# <c>[Header]</c> above a bound field, when the live renderer would draw it ITSELF rather than
        /// leave it to Unity's PropertyField decorator — i.e. any non-Default widget with no authored header.
        /// Null when there is nothing to draw. An ARRAY ELEMENT always answers null: a path inside a list
        /// resolves back to the array FIELD, so one list's header would be stamped above every row of it.
        /// </summary>
        private string CodeHeaderFor(BindingNode node, string path)
        {
            if (node == null || node.DisplayType == DisplayType.Default) return null;
            if (!string.IsNullOrEmpty(node.Header) || node.IsHeaderHidden || node.EffectiveHeader != null) return null;
            if (_ctx.RowBaseExpr != null) return null;
            var f = _ctx.ResolveField(path);
            var attr = f == null ? null : f.GetCustomAttribute<HeaderAttribute>();
            return attr != null && !string.IsNullOrEmpty(attr.header) ? attr.header : null;
        }

        private void EmitAnimations(CodeWriter w, LayoutNode node, string element)
        {
            if (node.Animations == null) return;
            foreach (var anim in node.Animations)
            {
                if (anim == null || anim.Effect == AnimEffect.None) continue;
                _ctx.UseKit("Animation");
                string bounce = anim.IsSpringEffect && !Mathf.Approximately(anim.Bounce, DefaultAnimBounce)
                    ? $", bounce: {Lit.F(anim.Bounce)}"
                    : "";
                w.Line($"SasqKit.WireAnimation({element}, {(int)anim.Trigger}, {(int)anim.Effect}, {Lit.F(anim.DurationSeconds)}, {(int)anim.Easing}, {Lit.Col(anim.PulseColor)}{bounce});");
            }
        }

        // Hides a field's native [Space] decorator element(s) (built lazily on bind). Scoped to Unity's own
        // "unity-space-drawer" class so a [Header] label sharing the container keeps rendering. Mirrors
        // BindingFactory.SuppressSpaceDecorator in the live renderer.
        private static void EmitSuppressSpaceDecorator(CodeWriter w, string field)
        {
            w.Line($"{field}.RegisterCallback<GeometryChangedEvent>(__e => {field}.Query<VisualElement>(className: \"unity-space-drawer\").ForEach(__d => __d.style.display = DisplayStyle.None));");
        }

        // Hides a field's native PropertyField [Header] decorator(s) — used when the designer supplies its own
        // header (rendered as our bold label) or has explicitly blanked it. Mirrors BindingFactory.SuppressDecoratorHeader.
        private static void EmitSuppressDecoratorHeader(CodeWriter w, string field)
        {
            w.Line($"{field}.RegisterCallback<GeometryChangedEvent>(__e => {field}.Query<VisualElement>(className: \"unity-decorator-drawers-container\").ForEach(__d => __d.style.display = DisplayStyle.None));");
        }

        // Styles a field's native [Header] decorator label(s) with a designer Header text style — used when the
        // header is a plain code [Header] (no override wrapper to style directly). Deferred: the decorator builds
        // lazily on bind. Mirrors ApplyBindingHeaderStyle's decorator-fallback branch.
        private static void EmitDecoratorHeaderStyle(CodeWriter w, string field, TextStyle ts)
        {
            var setters = new List<string>();
            if (ts.fontStyle.isOverridden) setters.Add($"__l.style.unityFontStyleAndWeight = FontStyle.{ts.fontStyle.value};");
            if (TextStyleApplier.IsUsableFontSize(ts.fontSize)) setters.Add($"__l.style.fontSize = {Lit.F(ts.fontSize.value)};");
            if (ts.color.isOverridden) setters.Add($"__l.style.color = {Lit.Col(ts.color.value)};");
            if (ts.align.isOverridden) setters.Add($"__l.style.unityTextAlign = TextAnchor.{ts.align.value};");
            if (setters.Count == 0) return;
            w.Line($"{field}.RegisterCallback<GeometryChangedEvent>(__e => {field}.Query<VisualElement>(className: \"unity-decorator-drawers-container\").ForEach(__d => __d.Query<Label>().ForEach(__l => {{ {string.Join(" ", setters)} }})));");
        }

        private string EmitCollection(CodeWriter w, CollectionNode node, string basePath)
        {
            string path = Combine(basePath, node.PropertyPath);
            if (string.IsNullOrEmpty(path))
            {
                _ctx.Note("A collection with no property path was skipped.");
                return null;
            }
            // The header text. A Title override replaces the property's nicified name; an explicitly blanked
            // Title emits "" — which the kit's CollectionShell treats as "no title" (only a NULL label falls
            // back to displayName there), so a hidden title survives the export instead of quietly coming back.
            string label = node.IsTitleHidden
                ? ""
                : (node.EffectiveTitleOverride ?? ObjectNames.NicifyVariableName(LastSegment(path)));
            string v;

            switch (node.DisplayMode)
            {
                case CollectionDisplayMode.TagChips:
                    _ctx.UseKit("TagChips");
                    v = _ctx.NewVar(LastSegment(path) + "Chips", "chips");
                    w.Line($"var {v} = SasqKit.TagChips(so, {_ctx.PathArg(path)}, {Lit.Str(label)});");
                    break;

                case CollectionDisplayMode.List:
                {
                    _ctx.UseKit("Collection");
                    _ctx.UseKit("Group");
                    v = _ctx.NewVar(LastSegment(path) + "List", "list");
                    var args = CollectionArgs(node, path, label);
                    // The List's container-level look (shell / index gutter / density). LIST ONLY — a Table
                    // has its own grid chrome and TableBox takes no such argument, so this cannot move into
                    // the shared CollectionArgs. Only a deviation from the default travels.
                    if (node.ListStyle != CollectionListStyle.Rows)
                        args.Add($"listStyle: {(int)node.ListStyle}");
                    if (node.EnableSorting)
                    {
                        // The "Sort by" dropdown's choices — the same columns the live list offers, gated
                        // by the same "can the comparator actually order this?" test. NO keys is a real
                        // answer (a list of AnimationCurves): omitting the arguments leaves the kit's sort
                        // UI absent, which is exactly what live does when its own gate empties the set.
                        var (sortNames, sortLabels) = ResolveListSortColumns(node, path);
                        if (sortNames.Count > 0)
                        {
                            args.Add("sortNames: new[] { " + string.Join(", ", sortNames.ConvertAll(Lit.Str)) + " }");
                            args.Add("sortLabels: new[] { " + string.Join(", ", sortLabels.ConvertAll(Lit.Str)) + " }");
                        }
                    }
                    // A polymorphic [SerializeReference] list has no useful default drawer: Unity renders the
                    // element's fields with no way to change WHICH implementation it is. The live renderer
                    // gives every element its own type picker (LayoutTreeBuilder's no-ItemLayout managed-
                    // reference branch), so the export has to as well — otherwise the exported editor silently
                    // loses the whole point of the field.
                    // A designed ITEM LAYOUT wins over both, matching live: LayoutTreeBuilder's row branch
                    // tests `node.ItemLayout != null` BEFORE the managed-reference and in-line-editor cases,
                    // so a template decides what a row looks like and the built-in row treatments are the
                    // fallbacks. (InlineEditorElementFactory already self-excludes on ItemLayout; the
                    // managed-reference one did not, which was a real divergence for a polymorphic list that
                    // had been given a template.)
                    // Emitted BEFORE the ListBox call — it declares a local the call then references.
                    string elementFactory = ItemLayoutElementFactory(w, node, path)
                                            ?? ManagedReferenceElementFactory(node, path)
                                            ?? InlineEditorElementFactory(node, path);
                    if (elementFactory != null) args.Add(elementFactory);
                    w.Line($"var {v} = SasqKit.ListBox({string.Join(", ", args)});");
                    break;
                }

                case CollectionDisplayMode.Table:
                {
                    // Twin of the live builder's guard in BuildCollectionElement. CollectLeafBindings drops
                    // hidden columns, and an empty result then falls through to ResolveTableColumnsByReflection
                    // (or, below, to the one-column "Value" table) — both of which put the hidden field back
                    // on screen and editable. Authored-but-all-hidden emits NOTHING for the collection, the
                    // same as the live path returning null for it.
                    if (HasAuthoredColumns(node.ItemLayout) && CollectVisibleLeafCount(node.ItemLayout) == 0)
                        return null;

                    var columns = ResolveTableColumns(node, path);
                    if (columns.names.Count == 0)
                    {
                        // No derivable columns — a list of leaf types (Color, Vector3, string) or of a
                        // custom-drawer type. Live renders these as a ONE-column table whose cell is the element
                        // itself, keeping the search / sort / paging / zebra chrome, so the export must too
                        // rather than dropping to a bare Unity list. "" is the value-column convention the
                        // kit's BuildTableRows understands.
                        columns = (new System.Collections.Generic.List<string> { "" },
                                   new System.Collections.Generic.List<string> { "Value" },
                                   new System.Collections.Generic.List<float> { 1f },
                                   new System.Collections.Generic.List<BindingNode> { null });
                    }
                    _ctx.UseKit("Collection");
                    _ctx.UseKit("Group");
                    v = _ctx.NewVar(LastSegment(path) + "Table", "table");
                    string namesLit = "new[] { " + string.Join(", ", columns.names.ConvertAll(Lit.Str)) + " }";
                    // "Show in Column Header" had NO emitter twin: live moved the unit into the header while the
                    // export left the header bare, so the same layout read "Weight (kg)" live and "Weight" in a
                    // generated editor. One decoration pass here covers the header the same way
                    // LayoutTreeBuilder.ResolveColumnLabel covers it live.
                    for (int __h = 0; __h < columns.headers.Count && __h < columns.nodes.Count; __h++)
                    {
                        var __n = columns.nodes[__h];
                        if (__n != null) columns.headers[__h] = FieldAffixes.DecorateColumnHeader(__n, columns.headers[__h]);
                    }
                    string headersLit = "new[] { " + string.Join(", ", columns.headers.ConvertAll(Lit.Str)) + " }";
                    string weightsLit = "new float[] { " + string.Join(", ", columns.weights.ConvertAll(Lit.F)) + " }";
                    var args = CollectionArgs(node, path, label);
                    args.Insert(3, weightsLit);
                    args.Insert(3, headersLit);
                    args.Insert(3, namesLit);
                    if (node.EnableSorting) args.Add("sorting: true"); // click-to-sort column headers, as live
                    // Emitted BEFORE the TableBox call — it declares a local the call then references, and it
                    // keeps the call itself on one line.
                    string cellFactory = EmitTableCellFactory(w, path, columns.names, columns.headers, columns.nodes);
                    if (cellFactory != null) args.Add(cellFactory);
                    w.Line($"var {v} = SasqKit.TableBox({string.Join(", ", args)});");
                    break;
                }

                default:
                    v = _ctx.NewVar(LastSegment(path) + "List", "list");
                    w.Line($"var {v} = new PropertyField();");
                    w.Line($"{v}.bindingPath = {_ctx.PathArg(path)};");
                    break;
            }

            // The "(N)" header is built inside the shell, so the node's Header text style has no element to land
            // on the way a container title does — size, colour, weight and alignment would all be dropped.
            if (StyleEmitter.HasTextOverride(node.HeaderTextStyle))
            {
                string ht = _ctx.NewVar(LastSegment(path) + "Header", "header");
                w.Line($"var {ht} = {v}.Q<Label>(SasqKit.CollectionTitleName);");
                w.Line($"if ({ht} != null)");
                w.OpenBrace();
                StyleEmitter.EmitTextStyle(w, ht, node.HeaderTextStyle, _ctx);
                w.CloseBrace();
            }

            // A collection carries validation too — Is Required ("X must have at least one item") and a custom
            // validation method — and the live renderer attaches it here (LayoutTreeBuilder ->
            // ValidationBinder.Attach for the CollectionNode branch). The kit's Validation handles collections via
            // its isCollection switch, which tests "cannot be empty" against arraySize rather than for null/blank.
            // Range never applies to a list, so the range arguments go out flat.
            if (ValidationBinder.NeedsValidation(node) && !string.IsNullOrEmpty(path))
            {
                _ctx.UseKit("Validation");
                string cval = _ctx.NewVar(LastSegment(path) + "Valid", "valid");
                var vArgs = new System.Collections.Generic.List<string>
                {
                    v, "so", _ctx.PathArg(path),
                    B(node.Required), Lit.Str(node.RequiredMessage),
                    "false", "false",                 // validateRange, clampToRange — meaningless for a list
                    "false", "false",                 // hasMin, hasMax
                    "0f", "0f", Lit.Str(""),
                    Lit.Str(node.ValidationMethod), Lit.Str(node.ValidationMessage),
                    "isCollection: true",
                };
                w.Line($"var {cval} = SasqKit.Validation({string.Join(", ", vArgs)});");
                v = cval;
            }

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        private System.Collections.Generic.List<string> CollectionArgs(CollectionNode node, string path, string label)
        {
            // PathArg, not a baked literal: inside a list row the array lives at an element-relative path that
            // only exists at runtime, and every collection kit entry point (ListBox / TableBox / TagChips)
            // already takes its path as a plain string it resolves itself. Outside a row PathArg IS
            // Lit.Str(path), so an ordinary collection's emitted call text is unchanged.
            // (A collection can never be a table CELL — columns are BindingNodes only — so PathArg's
            // cell-expression branch is unreachable from here.)
            var args = new System.Collections.Generic.List<string> { "so", _ctx.PathArg(path), Lit.Str(label) };
            if (node.Expandable) { args.Add("expandable: true"); args.Add($"defaultOpen: {B(node.DefaultToOpen)}"); }
            if (node.ZebraStriping)
            {
                args.Add("zebra: true");
                if (node.ZebraColor.isOverridden) args.Add($"zebraColor: {Lit.Col(node.ZebraColor.value)}");
            }
            // Row separator chrome. None is BOTH the node default and the kit parameter default, so only a
            // deviation travels — an unstyled collection keeps the shortest possible emitted call.
            if (node.RowSeparator != CollectionRowSeparator.None)
                args.Add($"rowSeparator: {(int)node.RowSeparator}");
            if (node.RowSeparatorColor.isOverridden)
                args.Add($"rowSeparatorColor: {Lit.Col(node.RowSeparatorColor.value)}");
            if (node.EnablePagination)
            {
                args.Add("pagination: true");
                if (node.PageSize > 0 && node.PageSize != 25) args.Add($"pageSize: {node.PageSize}");
                // Pager presentation: ButtonsOnly (default) / DotsOnly / ButtonsAndDots.
                if (node.PaginationDisplayStyle != PaginationDisplayStyle.ButtonsOnly)
                    args.Add($"paginationStyle: {(int)node.PaginationDisplayStyle}");
            }
            if (node.ReorderStyle != CollectionReorderStyle.DragAndDrop)
                args.Add($"reorderStyle: {(int)node.ReorderStyle}");
            // Live "Search..." filter box (scoped to one element field when SearchProperty is set).
            if (node.Searchable)
            {
                args.Add("searchable: true");
                if (!string.IsNullOrEmpty(node.SearchProperty))
                    args.Add($"searchProperty: {Lit.Str(node.SearchProperty)}");
            }
            return args;
        }

        /// <summary>Table columns from the element type's serialized fields, resolved at export time.</summary>
        /// <summary>
        /// The <c>elementFactory:</c> argument for a polymorphic <c>[SerializeReference]</c> list — a lambda
        /// building the same branded type picker each element gets live — or null when the list isn't one.
        ///
        /// It is emitted as a delegate rather than wired inside the Collection kit section on purpose: that
        /// keeps Collection free of any compile-time reference to the SRef and Dropdowns sections, so an
        /// export with an ordinary list doesn't drag both of them (and the popup chrome behind them) along.
        /// The element's label is blank because the row already carries its <c>[index]</c>, matching live.
        /// </summary>
        private string ManagedReferenceElementFactory(CollectionNode node, string path)
        {
            var element = ResolveManagedReferenceElement(path);
            if (element == null) return null;

            _ctx.UseKit("SRef");
            _ctx.UseKit("Dropdowns"); // StyleDropdownButton + SasqSearchableDropdown live there

            // The lambda parameter is deliberately NOT a short name like "p": this is generated source dropped
            // into a method alongside other emitted locals, and C# rejects a lambda parameter that shadows any
            // name already in that enclosing scope. A prefixed name cannot collide with NewVar's output.
            // RowLambdaParam rather than a literal, because this collection may itself sit inside a list row,
            // where an enclosing element lambda already owns the depth-0 name.
            string param = RowLambdaParam;
            var a = new System.Collections.Generic.List<string>
                { "so", param + ".propertyPath", Lit.Str(""), "typeof(global::" + element.FullName.Replace('+', '.') + ")" };
            // Polymorphic search defaults ON in the kit, like the live picker — only the OFF state travels.
            if (!node.PolymorphicSearchable) a.Add("searchable: false");
            // The collection's popup styling is propagated onto every element picker, exactly as live does.
            if (node.DropdownPopupBackground.isOverridden) a.Add($"popupBg: {Lit.Col(node.DropdownPopupBackground.value)}");
            if (node.DropdownItemTextStyle.color.isOverridden) a.Add($"itemTextColor: {Lit.Col(node.DropdownItemTextStyle.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(node.DropdownItemTextStyle.fontSize)) a.Add($"itemTextSize: {Lit.F(node.DropdownItemTextStyle.fontSize.value)}");
            if (node.DropdownItemHoverBackground.isOverridden) a.Add($"itemHoverBg: {Lit.Col(node.DropdownItemHoverBackground.value)}");
            if (node.DropdownItemHoverColor.isOverridden) a.Add($"itemHoverColor: {Lit.Col(node.DropdownItemHoverColor.value)}");

            return $"elementFactory: {param} => SasqKit.SRefPicker({string.Join(", ", a)})";
        }

        /// <summary>The element type of a <c>[SerializeReference]</c> collection, or null when the field isn't
        /// one. The attribute is the test, not the element type's shape: a plain <c>List&lt;SomeClass&gt;</c>
        /// serializes by value and Unity's own drawer handles it correctly.</summary>
        /// <summary>
        /// The <c>elementFactory:</c> argument for a list whose rows are in-line editors — a lambda building
        /// the same embedded editor each element gets live — or null when the option is off or the elements
        /// aren't <see cref="UnityEngine.Object"/> references. Sibling of
        /// <see cref="ManagedReferenceElementFactory"/>, and emitted as a delegate for the same reason: it
        /// keeps the Collection kit section free of a compile-time reference to the InlineEditor section.
        /// The element's label is blank because the row already carries its <c>[index]</c>, matching live.
        /// </summary>
        private string InlineEditorElementFactory(CollectionNode node, string path)
        {
            if (node == null || !node.ElementInlineEditor || node.ItemLayout != null) return null;
            if (!IsObjectReferenceElement(path)) return null;

            _ctx.UseKit("InlineEditor");
            // See ManagedReferenceElementFactory: the name has to follow the row nesting depth, not be a literal.
            string param = RowLambdaParam;
            var a = new System.Collections.Generic.List<string> { "so", param + ".propertyPath", Lit.Str("") };
            // expandable + defaultOpen both default TRUE in the kit; rows are collapsible and start closed.
            a.Add($"defaultOpen: {B(node.ElementDefaultToOpen)}");
            // Each row remembers its own fold, so a row the user opened survives the next rebuild instead of
            // snapping shut. Unlike every other FoldoutKey call this one CANNOT be fully baked — the row index
            // only exists at runtime — so a baked per-collection prefix is concatenated with the element's
            // property path. Still no hashing or reflection in the buyer's project: just a string concat.
            string rowKeyPrefix = _ctx.FoldoutKey(path, "row");
            // The element's own runtime path is what makes this per-row, and it is FULLY qualified even when
            // this collection is itself nested in a row ("inventory.Array.data[2].links.Array.data[0]"), so
            // the outer row's identity is already carried and the baked prefix does not need it.
            if (rowKeyPrefix != null) a.Add($"stateKey: {Lit.Str(rowKeyPrefix + "/")} + {param}.propertyPath");
            return $"elementFactory: {param} => SasqKit.InlineEditor({string.Join(", ", a)})";
        }

        /// <summary>
        /// The <c>elementFactory:</c> argument for a List whose collection has a designed ITEM LAYOUT — a
        /// lambda that builds that layout, per row, against the element's runtime path — or null when the
        /// collection has no template or its element type can't be resolved.
        /// <para>
        /// This is what makes a designed row survive the export at all. Without it a List-mode item layout
        /// emitted nothing but a fidelity note and the standalone editor fell back to Unity's default row,
        /// so the exact thing the user designed — a styled <c>List&lt;Vector2&gt;</c> element, a row of named
        /// columns — was the one thing that didn't travel.
        /// </para>
        /// <para>
        /// The unlock is <see cref="EmitContext.RowBaseExpr"/>. Every kit widget takes
        /// <c>(SerializedObject so, string path)</c>, and a row's paths are all the element's path plus a
        /// segment, so with the base carried as an EXPRESSION the ordinary emitters run verbatim — including
        /// nested containers, which take that base as a parameter. It is the same trick the table-cell
        /// factory uses, generalised from one path to a whole sub-layout.
        /// </para>
        /// Emitted into a scratch writer and spliced only on success: the lambda's opening lines are useless
        /// without a body, and whether a body exists is only known after the walk.
        /// </summary>
        private string ItemLayoutElementFactory(CodeWriter w, CollectionNode node, string path)
        {
            if (node?.ItemLayout == null) return null;
            FieldInfo fi = _ctx.ResolveField(path);
            if (fi == null) return null;
            if (!SasquatchReflectionUtility.IsCollectionType(fi.FieldType, out var element) || element == null)
                return null;

            string fv = _ctx.NewVar(LastSegment(path) + "Row", "row");
            var body = new CodeWriter();
            // Read BEFORE the depth is pushed: this lambda's own parameter belongs to the enclosing level,
            // and its BODY is one level deeper (that is where a nested collection's lambda would land).
            string param = RowLambdaParam;

            string prevRow = _ctx.RowBaseExpr;
            System.Type prevRoot = _ctx.ResolveRoot;
            string prevNodePath = _ctx.NodePath;
            string expr;
            try
            {
                // Restored in the finally: emitting a row re-enters the ordinary field path, and a leaked
                // base expression would rebind unrelated widgets to a lambda parameter not in their scope.
                _ctx.RowBaseExpr = param + ".propertyPath";
                _ctx.ResolveRoot = element;                    // row paths resolve against the ELEMENT type
                _ctx.NodePath = prevNodePath + "/item";        // distinct fold identity from the collection
                _rowLambdaDepth++;
                expr = EmitElementExpr(body, node.ItemLayout, "");
            }
            finally
            {
                _rowLambdaDepth--;
                _ctx.RowBaseExpr = prevRow;
                _ctx.ResolveRoot = prevRoot;
                _ctx.NodePath = prevNodePath;
            }

            // Nothing emitted (a template of only unsupported nodes) — leave the kit's default row, which is
            // exactly what this collection got before, rather than a lambda that returns nothing.
            if (expr == null) return null;

            w.Line($"System.Func<SerializedProperty, VisualElement> {fv} = {param} =>");
            w.OpenBrace();
            w.AppendBlock(body.ToString());
            w.Line($"return {expr};");
            w.CloseBrace(";");
            return $"elementFactory: {fv}";
        }

        /// <summary>
        /// How many row-factory lambdas the emitter is currently INSIDE. Zero everywhere except while emitting
        /// a collection's item layout, so it costs nothing on the ordinary path.
        /// </summary>
        private int _rowLambdaDepth;

        /// <summary>
        /// The lambda parameter name a row/element factory declares at the current nesting depth.
        /// <para>
        /// A collection nested inside a list row emits an <c>elementFactory:</c> lambda inside another one's
        /// body, so without this the inner lambda would re-declare the outer's parameter. That is <b>legal</b>
        /// on the compiler the buyer actually gets — MEASURED, not assumed: Unity 6's Roslyn at
        /// <c>-langversion:9.0</c> compiles the shadowed form clean, because C# 8 relaxed the enclosing-scope
        /// name rule. (Mono's legacy C# 6 compiler still rejects it with <c>CS0136</c>, which is what made this
        /// look like a hard blocker at first; nothing compiles a generated editor with that compiler.)
        /// </para>
        /// It is uniqued anyway, for the one reason that survives the measurement: the generated file is
        /// something a buyer READS and edits, and an inner <c>__element</c> silently shadowing an outer
        /// <c>__element</c> is the kind of thing that reads as a bug even when it isn't. It also keeps the
        /// emitter from depending on a language-version relaxation it never asked for.
        /// <para>
        /// Depth 0 keeps the historic <c>__element</c>, so every export that has no nested collection — which
        /// is every export produced so far — is byte-identical. SIBLING lambdas at the same depth may share a
        /// name (their scopes do not nest), which is why the counter tracks depth and not a running total.
        /// </para>
        /// </summary>
        private string RowLambdaParam => _rowLambdaDepth == 0 ? "__element" : "__element" + (_rowLambdaDepth + 1);

        /// <summary>True when the collection at <paramref name="path"/> holds UnityEngine.Object references.</summary>
        private bool IsObjectReferenceElement(string path)
        {
            FieldInfo fi = _ctx.ResolveField(path);
            var ft = fi?.FieldType;
            if (ft == null) return false;
            System.Type element = null;
            if (ft.IsArray) element = ft.GetElementType();
            else if (ft.IsGenericType && ft.GetGenericArguments().Length == 1) element = ft.GetGenericArguments()[0];
            return element != null && typeof(UnityEngine.Object).IsAssignableFrom(element);
        }

        private System.Type ResolveManagedReferenceElement(string path)
        {
            FieldInfo fi = _ctx.ResolveField(path);
            if (fi == null || fi.GetCustomAttribute<SerializeReference>() == null) return null;

            var ft = fi.FieldType;
            System.Type element = null;
            if (ft.IsArray) element = ft.GetElementType();
            else if (ft.IsGenericType && ft.GetGenericArguments().Length == 1) element = ft.GetGenericArguments()[0];

            if (element == null || element.IsPrimitive || element == typeof(string)) return null;
            // A UnityEngine.Object reference is an object field, never a managed reference.
            if (typeof(UnityEngine.Object).IsAssignableFrom(element)) return null;
            return element;
        }

        /// <summary>
        /// A table's columns. The live renderer derives them from the ITEM LAYOUT
        /// (<c>LayoutTreeBuilder.GetColumnBindings</c>): the leaf bindings in tree order, each wearing its
        /// designer label override. That is exactly what the designer promises — "Table columns are the
        /// item-layout fields. Drag more fields from the Bindings panel to add columns; reorder or remove them
        /// in the layout tree" — so a deleted column, a reordered one, or a renamed header has to survive the
        /// export. Reflection over the element type is only the FALLBACK, for a collection with no item layout
        /// at all (a code-authored node, or one that never passed through Table mode in the designer).
        /// </summary>
        private (System.Collections.Generic.List<string> names, System.Collections.Generic.List<string> headers,
                 System.Collections.Generic.List<float> weights,
                 System.Collections.Generic.List<BindingNode> nodes) ResolveTableColumns(CollectionNode node, string path)
        {
            var bindings = new System.Collections.Generic.List<BindingNode>();
            CollectLeafBindings(node.ItemLayout, bindings);
            if (bindings.Count > 0)
            {
                var n = new System.Collections.Generic.List<string>();
                var h = new System.Collections.Generic.List<string>();
                var wts = new System.Collections.Generic.List<float>();
                // The authoring node per column, kept so each CELL can be built as the widget the user actually
                // designed rather than a bare field — see EmitTableCellFactory.
                var nodes = new System.Collections.Generic.List<BindingNode>();
                foreach (var b in bindings)
                {
                    string p = b.PropertyPath ?? "";
                    // An ELEMENT-TEMPLATE binding (empty path) IS the one-column "Value" table live renders
                    // for a list of atomic elements — and unlike the synthesized column it carries authored
                    // styling, so it travels WITH its node and gets a real cell factory case. "" is already
                    // the kit's value-column convention in BuildTableRows, so nothing downstream changes.
                    // It can only ever be the sole column (an element has no other field to show beside
                    // itself), so a template mixed with real columns keeps the real ones.
                    if (p.Length == 0)
                    {
                        if (bindings.Count > 1) continue;
                        n.Add("");
                        h.Add(b.EffectiveLabelOverride ?? "Value");
                        wts.Add(b.ColumnWeight);
                        nodes.Add(b);
                        continue;
                    }
                    if (n.Contains(p)) continue;
                    n.Add(p);
                    h.Add(b.EffectiveLabelOverride ?? ObjectNames.NicifyVariableName(LastSegment(p)));
                    // Widths the user dragged persist onto the column node (LayoutTreeBuilder.ColumnWeightOf).
                    // 0 travels as 0 — the kit reads it as "auto", i.e. fit this column to its content the way
                    // live does, rather than as an authored weight of exactly 1.
                    wts.Add(b.ColumnWeight);
                    nodes.Add(b);
                }
                if (n.Count > 0) return (n, h, wts, nodes);
            }
            // Reflection fallback: no authored nodes, so every cell stays a stock field — and no authored
            // widths either, so every column travels as 0 ("auto", fit to content).
            var refl = ResolveTableColumnsByReflection(path);
            return (refl.names, refl.headers, refl.names.ConvertAll(_ => 0f),
                    refl.names.ConvertAll(_ => (BindingNode)null));
        }

        /// <summary>
        /// Emits the table's per-column cell factory and returns the <c>cellFactory:</c> argument, or null when
        /// every column is a plain field (then the kit's stock <c>PropertyField</c> cell is already correct and
        /// nothing travels — the house idiom).
        /// <para>
        /// Live builds every table cell through the binding factory, so a column authored as a Slider, a colour
        /// swatch or a styled toggle renders as that widget — a bare field for all of them is what the export
        /// degrades to without this. The unlock is that kit widgets take <c>(SerializedObject so, string path)</c>
        /// and a cell's path is available at runtime as <c>__cell.propertyPath</c>, so the emitters can be reused
        /// verbatim — <see cref="EmitContext.CellPathExpr"/> swaps the baked literal for that expression and
        /// <see cref="EmitContext.ResolveRoot"/> points reflection at the ELEMENT type.
        /// </para>
        /// </summary>
        private string EmitTableCellFactory(CodeWriter w, string path,
                                            System.Collections.Generic.List<string> names,
                                            System.Collections.Generic.List<string> headers,
                                            System.Collections.Generic.List<BindingNode> nodes)
        {
            if (nodes == null) return null;
            FieldInfo fi = _ctx.ResolveField(path);
            if (fi == null) return null;
            if (!SasquatchReflectionUtility.IsCollectionType(fi.FieldType, out var element) || element == null)
                return null;

            // Only columns that DEVIATE from a plain field need a case; the rest fall through to the kit's
            // PropertyField, which is what they'd have rendered as anyway.
            var custom = new System.Collections.Generic.List<int>();
            for (int c = 0; c < names.Count && c < nodes.Count; c++)
                if (ColumnNeedsWidget(nodes[c])) custom.Add(c);
            if (custom.Count == 0) return null;

            string fv = _ctx.NewVar(LastSegment(path) + "Cells", "cells");
            w.Line($"System.Func<int, SerializedProperty, VisualElement> {fv} = (__col, __cell) =>");
            w.OpenBrace();
            w.Line("switch (__col)");
            w.OpenBrace();

            string prevExpr = _ctx.CellPathExpr;
            System.Type prevRoot = _ctx.ResolveRoot;
            try
            {
                // Restored in the finally: emitting a cell is re-entrant with ordinary field emission, and a
                // leaked expression or resolve root would silently rebind unrelated widgets.
                _ctx.CellPathExpr = "__cell.propertyPath";
                _ctx.ResolveRoot = element;
                foreach (int c in custom)
                {
                    w.Line($"case {c}:");
                    w.OpenBrace();
                    // Labels are force-hidden: the column HEADER already names the value, exactly as live passes
                    // forceHideLabel to the binding factory for a table cell.
                    var cellNode = nodes[c];
                    string cv = DisplayTypeEmitter.Emit(w, _ctx, cellNode, names[c], headers[c]);
                    DisplayTypeEmitter.EmitFieldStyling(w, _ctx, cellNode, cv);
                    // EmitAffixes is reachable only from EmitBinding, and a table cell never goes through it —
                    // so exported cells silently had no affixes at all while live cells did. isTableCell: true
                    // so "Show in Column Header" suppresses the per-cell copy here exactly as it does live.
                    EmitAffixes(w, cellNode, cv, names[c], isTableCell: true);
                    cv = EmitFieldValidation(w, cellNode, names[c], cv);
                    w.Line($"return {cv};");
                    w.CloseBrace();
                }
            }
            finally
            {
                _ctx.CellPathExpr = prevExpr;
                _ctx.ResolveRoot = prevRoot;
            }

            w.CloseBrace();
            w.Line("return null;");
            w.CloseBrace();
            w.Line(";");
            return $"cellFactory: {fv}";
        }

        /// <summary>
        /// Wraps <paramref name="result"/> in the kit's Live Debug graph row, mirroring the live
        /// <c>DebugRowBinder.Attach</c>. Emitted only when the layout's Script Header has the debug toggle on —
        /// the same one question live derives once per build — so an export without it is byte-for-byte what
        /// it always was.
        ///
        /// <para>NOT emitted from the table-cell factory, matching live's <c>isTableCell</c> exclusion: a cell
        /// has no room for a second row, and wrapping one breaks the cell/field identity the column-fit
        /// measurement depends on.</para>
        ///
        /// <para>The path goes through <see cref="EmitContext.PathArg"/>, so a field inside a collection row
        /// registers its RUNTIME path — one channel per row, exactly as live does.</para>
        /// </summary>
        private string EmitDebugRow(CodeWriter w, BindingNode node, string path, string result)
        {
            if (!_ctx.DebugEnabled || string.IsNullOrEmpty(path) || result == null) return result;
            _ctx.UseKit("LiveDebug");

            // The label the moment card's co-change list shows. Live derives it the same way — the authored
            // override, else the nicified leaf of the resolved path.
            string label = node.EffectiveLabelOverride;
            if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(LastSegment(path));

            string v = _ctx.NewVar(LastSegment(path) + "Debug", "debug");
            var args = new List<string> { result, "so", _ctx.PathArg(path), Lit.Str(label) };
            // An authored range is a statement about what this number MEANS, so the graph honours it rather
            // than auto-framing: a health bar that never drops below 40 should still read against 0..100.
            if (node.RangeMin.isOverridden && node.RangeMax.isOverridden)
            {
                args.Add("hasRange: true");
                args.Add($"rangeMin: {Lit.F(node.RangeMin.value)}");
                args.Add($"rangeMax: {Lit.F(node.RangeMax.value)}");
            }
            w.Line($"var {v} = SasqKit.DebugRow({string.Join(", ", args)});");
            return v;
        }

        /// <summary>
        /// Wraps <paramref name="result"/> in the kit's validation shell when the node asks for one (Required /
        /// numeric range, warn or clamp / a custom bool method), returning the element to carry on with. Mirrors
        /// the live ValidationBinder.Attach. Shared with the table-cell factory so an authored column keeps its
        /// validation — the path goes through PathArg, so a cell validates its RUNTIME path.
        /// </summary>
        private string EmitFieldValidation(CodeWriter w, BindingNode node, string path, string result)
        {
            if (!ValidationBinder.NeedsValidation(node)) return result;
            _ctx.UseKit("Validation");
            string val = _ctx.NewVar(LastSegment(path) + "Valid", "valid");
            var vArgs = new List<string>
            {
                result, "so", _ctx.PathArg(path),
                B(node.Required), Lit.Str(node.RequiredMessage),
                B(node.ValidateRange), B(node.ClampToRange),
                B(node.RangeHasMin), B(node.RangeHasMax),
                Lit.F(node.ValidationMin), Lit.F(node.ValidationMax), Lit.Str(node.RangeValidationMessage),
                Lit.Str(node.ValidationMethod), Lit.Str(node.ValidationMessage),
            };
            // In a table cell the kit validates in place (outline + tooltip, no HelpBox), exactly as the live
            // ValidationBinder does — a message box under a cell is clipped by the cell and knocks the field out
            // of line with its row. Named so it skips isCollection, which a cell never is.
            if (_ctx.CellPathExpr != null) vArgs.Add("isTableCell: true");
            w.Line($"var {val} = SasqKit.Validation({string.Join(", ", vArgs)});");
            return val;
        }

        /// <summary>True when a column's authored node asks for anything a stock field wouldn't already give.</summary>
        /// <remarks>
        /// This is the gate on whether a column gets a factory CASE at all, so anything it misses is silently
        /// discarded: the column falls through to the kit's plain <c>new PropertyField(cellProp, "")</c>. It used
        /// to ask only "is this a different WIDGET?", which meant a Default-display column carrying Field
        /// Styling or Required/Range validation exported as a bare grey field — working live, gone in the
        /// generated editor. Keep this in lockstep with what the case body actually emits
        /// (<see cref="DisplayTypeEmitter.EmitFieldStyling"/> + <see cref="EmitFieldValidation"/>).
        /// </remarks>
        private static bool ColumnNeedsWidget(BindingNode b)
        {
            if (b == null) return false;
            if (b.DisplayType != DisplayType.Default) return true;
            if (b.ShowStepper) return true;
            if (ColumnHasFieldStyling(b)) return true;
            if (ValidationBinder.NeedsValidation(b)) return true;
            // A Default-display column whose ONLY deviation is a prefix/suffix still needs a case: without one
            // it falls through to the kit's bare PropertyField and the unit never reaches the exported cell.
            if (!b.PrefixAffix.IsEmpty || !b.SuffixAffix.IsEmpty) return true;
            return false;
        }

        /// <summary>
        /// True when <see cref="DisplayTypeEmitter.EmitFieldStyling"/> would write anything for this node —
        /// mirrors its argument set exactly, so the gate above and the emission below can't drift apart.
        /// </summary>
        private static bool ColumnHasFieldStyling(BindingNode b)
        {
            var l = b.LabelTextStyle;
            var f = b.FieldTextStyle;
            if (l.color.isOverridden || l.fontStyle.isOverridden || l.align.isOverridden) return true;
            if (TextStyleApplier.IsUsableFontSize(l.fontSize)) return true;
            if (f.color.isOverridden || f.fontStyle.isOverridden) return true;
            if (TextStyleApplier.IsUsableFontSize(f.fontSize)) return true;
            if (b.FieldBackground.isOverridden) return true;
            var size = b.DisplayStyleData.size;
            return size == DisplaySize.Compact || size == DisplaySize.Large;
        }

        private (System.Collections.Generic.List<string> names, System.Collections.Generic.List<string> headers) ResolveTableColumnsByReflection(string path)
        {
            var names = new System.Collections.Generic.List<string>();
            var headers = new System.Collections.Generic.List<string>();
            FieldInfo fi = _ctx.ResolveField(path);
            if (fi == null) return (names, headers);

            // The SAME gate the live derivation applies (LayoutTreeBuilder.DeriveColumnBindingsFromElementType),
            // through the same helpers so the two cannot drift apart. An element type that draws as ONE thing per
            // row has no columns to derive: a leaf type (Color, Vector3, a primitive), a UnityEvent, anything
            // carrying its own PropertyDrawer, or a [SerializeReference] list. Reflecting over its fields anyway
            // turns a List<Color> into four columns of raw floats headed R / G / B / A, where the live table
            // draws a single column of colour swatches.
            if (fi.GetCustomAttribute<SerializeReference>() != null) return (names, headers);
            if (!SasquatchReflectionUtility.IsCollectionType(fi.FieldType, out var element)) return (names, headers);
            if (element == null
                || SasquatchReflectionUtility.IsLeafType(element)
                || SasquatchReflectionUtility.IsUnityEventType(element)
                || SasquatchReflectionUtility.TypeHasCustomDrawer(element)
                || !(element.IsClass || (element.IsValueType && !element.IsEnum && !element.IsPrimitive)))
                return (names, headers);

            foreach (var p in SasquatchReflectionUtility.GetSerializableProperties(element))
            {
                // Nested paths and inner collections are not columns — one level only, as live.
                if (p.isCollection || p.path.Contains('.')) continue;
                if (names.Contains(p.path)) continue;
                names.Add(p.path);
                headers.Add(ObjectNames.NicifyVariableName(p.path));
            }
            return (names, headers);
        }

        /// <summary>The List-mode sort dropdown's columns — the emit-time twin of the live renderer's
        /// <c>BuildElementSortKeys</c> + <c>FilterSortableColumns</c> pair, resolved by reflection because
        /// there is no sample element at codegen time.
        ///
        /// <para>Two rules, both of them live's:</para>
        /// <para>1. An ItemLayout that contributes no columns — OR one whose single column IS the element
        /// (an Element template, empty path) — EXPANDS into the element's own parts, so a
        /// <c>List&lt;Vector2&gt;</c> offers X / Y and a <c>List&lt;MyStruct&gt;</c> offers its fields. The
        /// hardcoded single "Value" this used to emit is the fallback live retired: on any compound element
        /// it is also the one key the comparator cannot order, so the dropdown did nothing at all.</para>
        /// <para>2. Every key is gated on <see cref="CollectionViewModel.IsSortableType"/>. A key that
        /// compares equal for every pair is worse than no key — picking it leaves the rows where they were
        /// AND pauses reorder. An empty result is correct and means "offer no sort at all", which is what
        /// live does when its own gate empties the set.</para></summary>
        private (System.Collections.Generic.List<string> names, System.Collections.Generic.List<string> labels) ResolveListSortColumns(CollectionNode node, string path)
        {
            var bindings = new System.Collections.Generic.List<BindingNode>();
            CollectLeafBindings(node.ItemLayout, bindings);
            var names = new System.Collections.Generic.List<string>();
            var labels = new System.Collections.Generic.List<string>();

            System.Type element = ResolveSortElementType(path);

            // No columns at all, or an ItemLayout whose ONLY column is the element itself: both are the same
            // sorting problem — one key standing in for a whole Vector2 — so both expand.
            bool expand = bindings.Count == 0
                          || (bindings.Count == 1 && string.IsNullOrEmpty(bindings[0].PropertyPath));
            if (expand)
            {
                // The element AS A WHOLE. An Element template supplied a label the user wrote for that row,
                // so it wins over "Value" exactly as it does live.
                string wholeLabel = (bindings.Count == 1 ? bindings[0].EffectiveLabelOverride : null) ?? "Value";
                foreach (var part in ElementSortParts(element))
                {
                    names.Add(part.Key);
                    labels.Add(part.Value);
                }
                // NAMED PARTS REPLACE THE WHOLE — they do not sit beside it. "Value" is offered only where it
                // genuinely IS the whole story: a list of ints, strings, enums, colours or object references.
                // A type reflection could not resolve keeps the old fallback rather than silently losing its
                // sort control.
                if (names.Count == 0 && (element == null || IsSortableSortType(element)))
                {
                    names.Add("");
                    labels.Add(wholeLabel);
                }
                return (names, labels);
            }

            foreach (var b in bindings)
            {
                string bp = b.PropertyPath ?? "";
                if (!IsSortableSortKey(element, bp)) continue;
                names.Add(bp);
                labels.Add(b.EffectiveLabelOverride ?? (bp.Length == 0 ? "Value" : ObjectNames.NicifyVariableName(LastSegment(bp))));
            }
            return (names, labels);
        }

        /// <summary>The collection's element type for sort-key purposes. Unlike
        /// <see cref="ResolveTableColumnsByReflection"/> this does NOT bail on a leaf element — a Vector2 IS
        /// the case that needs expanding. A <c>[SerializeReference]</c> list resolves to the DECLARED element
        /// type, whose fields every subclass has; live reads the concrete subclass of element 0 instead, so
        /// this can offer fewer keys, never a wrong one.</summary>
        private System.Type ResolveSortElementType(string path)
        {
            var fi = _ctx.ResolveField(path);
            if (fi == null) return null;
            return SasquatchReflectionUtility.IsCollectionType(fi.FieldType, out var element) ? element : null;
        }

        // The serialized child names Unity's own NextVisible walk yields for the compound built-ins, with the
        // labels it gives them. Hardcoded because reflection reports the BACKING fields, which for several of
        // these do not match what serialization emits: Vector2Int reflects m_X / m_Y and serializes x / y,
        // Rect reflects m_XMin / m_YMin and serializes x / y / width / height, and RectInt has no managed
        // fields at all. Color and Color32 are deliberately ABSENT — a colour property has no visible
        // children, so live offers the whole swatch as one orderable key.
        private static readonly System.Collections.Generic.Dictionary<System.Type, string[]> SortPartsByType =
            new System.Collections.Generic.Dictionary<System.Type, string[]>
            {
                { typeof(Vector2),    new[] { "x", "y" } },
                { typeof(Vector3),    new[] { "x", "y", "z" } },
                { typeof(Vector4),    new[] { "x", "y", "z", "w" } },
                { typeof(Quaternion), new[] { "x", "y", "z", "w" } },
                { typeof(Vector2Int), new[] { "x", "y" } },
                { typeof(Vector3Int), new[] { "x", "y", "z" } },
                { typeof(Rect),       new[] { "x", "y", "width", "height" } },
                { typeof(RectInt),    new[] { "x", "y", "width", "height" } },
                { typeof(Bounds),     new[] { "m_Center", "m_Extent" } },
                { typeof(BoundsInt),  new[] { "m_Position", "m_Size" } },
            };

        /// <summary>The element's own sortable parts as path/label pairs, in serialization order — the
        /// reflection twin of the live walk over a sample element's direct visible children.</summary>
        private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> ElementSortParts(System.Type element)
        {
            var parts = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
            if (element == null) return parts;

            if (SortPartsByType.TryGetValue(element, out var builtIn))
            {
                foreach (var name in builtIn)
                    parts.Add(new System.Collections.Generic.KeyValuePair<string, string>(name, ObjectNames.NicifyVariableName(name)));
                return parts;
            }
            // Every other leaf draws as ONE atomic field and has no visible children to offer.
            if (SasquatchReflectionUtility.IsLeafType(element)) return parts;

            foreach (var rp in SasquatchReflectionUtility.GetSerializableProperties(element))
            {
                // Direct children only, as live: the recursion returns dotted paths for nested structs, and a
                // nested collection is not a sort key.
                if (rp.isCollection || rp.path.Contains(".")) continue;
                if (!IsSortableSortType(rp.type)) continue;
                parts.Add(new System.Collections.Generic.KeyValuePair<string, string>(rp.path, ObjectNames.NicifyVariableName(rp.path)));
            }
            return parts;
        }

        /// <summary>Whether an ItemLayout column can be ordered. A path this cannot resolve is KEPT, matching
        /// live: refusing a key because reflection came up short is the same failure in the other direction.</summary>
        private static bool IsSortableSortKey(System.Type element, string relativePath)
        {
            if (element == null) return true;
            if (string.IsNullOrEmpty(relativePath)) return IsSortableSortType(element);
            foreach (var rp in SasquatchReflectionUtility.GetSerializableProperties(element))
            {
                if (rp.path != relativePath) continue;
                return !rp.isCollection && IsSortableSortType(rp.type);
            }
            return true; // unresolved -> keep, as live keeps a key whose sample type is unknown
        }

        /// <summary>The emit-time stand-in for <see cref="CollectionViewModel.IsSortableType"/>: true when a
        /// value of this C# type reaches the comparator as a <see cref="SerializedPropertyType"/> it can
        /// order. Kept as one list beside the type map above so the two cannot drift.</summary>
        private static bool IsSortableSortType(System.Type t)
        {
            if (t == null) return false;
            if (t.IsEnum) return true;                                    // Enum
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true; // ObjectReference
            if (t == typeof(bool) || t == typeof(char) || t == typeof(string)) return true;
            if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
                || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)) return true;
            if (t == typeof(float) || t == typeof(double)) return true;
            return t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)
                || t == typeof(Quaternion) || t == typeof(Vector2Int) || t == typeof(Vector3Int)
                || t == typeof(Rect) || t == typeof(RectInt)
                || t == typeof(Color) || t == typeof(Color32);
        }

        /// <summary>Whether this item template holds ANY authored column, hidden ones INCLUDED. Twin of
        /// <c>LayoutTreeBuilder.HasAuthoredColumns</c> — see it for why the question cannot be answered from
        /// the filtered walk.</summary>
        private static bool HasAuthoredColumns(LayoutNode node)
        {
            switch (node)
            {
                case null: return false;
                case BindingNode _: return true;
                case ContainerNode c when c.Children != null:
                    foreach (var child in c.Children)
                        if (HasAuthoredColumns(child)) return true;
                    return false;
                default: return false;
            }
        }

        /// <summary>How many columns survive the hidden filter — i.e. what CollectLeafBindings would return.</summary>
        private static int CollectVisibleLeafCount(LayoutNode node)
        {
            var acc = new System.Collections.Generic.List<BindingNode>();
            CollectLeafBindings(node, acc);
            return acc.Count;
        }

        private static void CollectLeafBindings(LayoutNode node, System.Collections.Generic.List<BindingNode> acc)
        {
            // A hidden node is not a table column. Twin of the live LayoutTreeBuilder.CollectColumnBindings skip:
            // these columns are derived from the MODEL, so there is no "skipped child" null to ride, and a hidden
            // container has to take its whole subtree with it (return, not continue).
            if (node != null && node.HiddenInInspector) return;

            if (node is BindingNode binding) { acc.Add(binding); return; }
            if (node is ContainerNode container && container.Children != null)
                foreach (var child in container.Children) CollectLeafBindings(child, acc);
        }

        // ---- Buttons & reflected properties ----------------------------------------------------------------

        private string EmitButton(CodeWriter w, ButtonNode node, string basePath)
        {
            string v = _ctx.NewVar("button", "button");
            w.Line($"var {v} = new Button(() =>");
            w.OpenBrace();

            if (node.ConfirmBeforeExecute)
                w.Line($"if (!EditorUtility.DisplayDialog({Lit.Str(node.ConfirmTitle)}, {Lit.Str(node.ConfirmMessage)}, \"Yes\", \"Cancel\")) return;");

            if (string.IsNullOrEmpty(node.MethodName))
            {
                w.Comment("No method configured for this button.");
            }
            else
            {
                MethodInfo method = _ctx.ResolveParameterlessMethod(node.MethodName);
                bool directCall = method != null && method.IsPublic;

                // A coroutine or async button method must not be called-and-dropped: an iterator method's body
                // does not run at all until something pumps MoveNext, and a Task's failures go unobserved.
                // SasqKit.RunButtonResult sorts that out at runtime (it no-ops on null, i.e. a plain void
                // method), so anything that HANDS SOMETHING BACK gets routed through it.
                bool returnsValue = method != null && method.ReturnType != typeof(void);
                string reflectiveInvoke =
                    $"typeof({_ctx.TargetRef}).GetMethod({Lit.Str(node.MethodName)}, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, System.Type.EmptyTypes, null)?.Invoke(__t, null)";

                w.Line("foreach (var __t in targets)");
                w.OpenBrace();
                w.Line($"Undo.RecordObject(__t, {Lit.Str(node.ButtonText)});");
                if (directCall && returnsValue)
                    w.Line($"SasqKit.RunButtonResult((({_ctx.TargetRef})__t).{node.MethodName}(), serializedObject);");
                else if (directCall)
                    w.Line($"(({_ctx.TargetRef})__t).{node.MethodName}();");
                else
                    // Reflective Invoke already yields object (null for void), so it feeds the helper directly.
                    w.Line($"SasqKit.RunButtonResult({reflectiveInvoke}, serializedObject);");
                w.Line("EditorUtility.SetDirty(__t);");
                w.CloseBrace();
                w.Line("serializedObject.Update();");

                if (method == null)
                    _ctx.Note($"Button method '{node.MethodName}' was not found as parameterless; it is invoked reflectively and may no-op.");
            }

            w.Pop();
            w.Line($"}}) {{ text = {Lit.Str(node.ButtonText)} }};");

            _ctx.UseKit("Button");
            var styleArgs = new System.Collections.Generic.List<string> { v };
            if (node.StylePreset != ButtonStyle.Modern) styleArgs.Add($"style: {(int)node.StylePreset}");
            if (node.AccentColor.isOverridden) styleArgs.Add($"accentOverride: {Lit.Col(node.AccentColor.value)}");
            if (node.CornerRadius.isOverridden) styleArgs.Add($"radius: {Lit.F(node.CornerRadius.value)}");
            int kitSize = StyleEmitter.ButtonKitSize(node.Size);
            if (kitSize != 1) styleArgs.Add($"size: {kitSize}");
            // 0 = follow the size class, so it only needs emitting when it actually overrides one.
            if (node.CustomHeight > 0f) styleArgs.Add($"customHeight: {Lit.F(node.CustomHeight)}");
            if (node.FullWidth) styleArgs.Add("fullWidth: true");
            if (node.HoverColor.isOverridden) styleArgs.Add($"hoverColor: {Lit.Col(node.HoverColor.value)}");
            if (node.PressedColor.isOverridden) styleArgs.Add($"pressedColor: {Lit.Col(node.PressedColor.value)}");
            if (node.HoverAnimation != ButtonHoverAnimation.Lift) styleArgs.Add($"animation: {(int)node.HoverAnimation}");
            if (node.TextBackdrop)
            {
                styleArgs.Add("textBackdrop: true");
                // The pill pins its label light. The generic text-style pass below writes to the BUTTON, and a
                // boxed label's own inline color would shadow that inherited value — so a user label color has
                // to reach the label directly, the way the live StyleApplier reaches it.
                if (node.LabelTextStyle.color.isOverridden)
                    styleArgs.Add($"boxedInk: {Lit.Col(node.LabelTextStyle.color.value)}");
            }
            // A button icon (built-in glyph) has no icon pipeline in the target project — bake it WHITE at export
            // time and let StyleButton tint it to the label ink at runtime, so it matches in both editor skins.
            if (!string.IsNullOrEmpty(node.IconId))
            {
                var iconTex = SasquatchIcons.RenderToTexture(node.IconId, 32, Color.white);
                if (iconTex != null)
                {
                    styleArgs.Add($"iconPng: \"{System.Convert.ToBase64String(iconTex.EncodeToPNG())}\"");
                    if (node.IconPlacement != ButtonIconPlacement.Left)
                        styleArgs.Add($"iconPlacement: {(int)node.IconPlacement}");
                }
            }
            w.Line($"SasqKit.StyleButton({string.Join(", ", styleArgs)});");
            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            if (StyleEmitter.HasTextOverride(node.LabelTextStyle))
                StyleEmitter.EmitTextStyle(w, v, node.LabelTextStyle, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        private string EmitPropertyNode(CodeWriter w, PropertyNode node, string basePath)
        {
            if (string.IsNullOrEmpty(node.PropertyName))
            {
                _ctx.Note("A ShowInInspector property with no name was skipped.");
                return null;
            }

            // A reflected C# property renders as the built-in field for its value type — seeded, polled, and
            // (when writable + a simple type + not forced read-only) editable — exactly like the live
            // BuildPropertyElement. No SerializedProperty exists, so the kit reflects the PRIMARY target.
            _ctx.UseKit("PropertyField");
            string label = node.HideLabel
                ? ""
                : (string.IsNullOrEmpty(node.LabelOverride) ? ObjectNames.NicifyVariableName(node.PropertyName) : node.LabelOverride);

            string v = _ctx.NewVar(node.PropertyName + "Prop", "prop");
            w.Line($"var {v} = SasqKit.ReflectedProperty(target, {Lit.Str(node.PropertyName)}, {Lit.Str(label)}, {(node.ForceReadOnly ? "true" : "false")});");

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);

            // Label + value text styling — mirrors the live PropertyNode styling pass (ApplyTextToFirstClass on
            // the label + ApplyFieldStyle on the input), reusing the same StyleField hook the binding fields use.
            var lts = node.LabelTextStyle;
            var vts = node.ValueTextStyle;
            var styleArgs = new List<string>();
            if (lts.color.isOverridden) styleArgs.Add($"labelColor: {Lit.Col(lts.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(lts.fontSize)) styleArgs.Add($"labelSize: {Lit.F(lts.fontSize.value)}");
            if (lts.fontStyle.isOverridden) styleArgs.Add($"labelFontStyle: {(int)lts.fontStyle.value}");
            if (lts.align.isOverridden) styleArgs.Add($"labelAlign: {(int)lts.align.value}");
            if (vts.color.isOverridden) styleArgs.Add($"textColor: {Lit.Col(vts.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(vts.fontSize)) styleArgs.Add($"textSize: {Lit.F(vts.fontSize.value)}");
            if (vts.fontStyle.isOverridden) styleArgs.Add($"textFontStyle: {(int)vts.fontStyle.value}");
            if (styleArgs.Count > 0)
                w.Line($"SasqKit.StyleField({v}, {string.Join(", ", styleArgs)});");

            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        // ---- Decorative leaves -----------------------------------------------------------------------------

        private string EmitScriptHeader(CodeWriter w, ScriptHeaderNode node, string basePath)
        {
            _ctx.UseKit("ScriptHeader");
            string v = _ctx.NewVar("scriptHeader", "scriptHeader");
            var args = new List<string>
            {
                "so",
                string.IsNullOrEmpty(node.EffectiveTextOverride) ? "null" : Lit.Str(node.EffectiveTextOverride)
            };
            if (node.HideIcon) args.Add("hideIcon: true");
            if (node.IsTextHidden) args.Add("hideText: true");
            var ts = node.TextStyle;
            if (ts.color.isOverridden) args.Add($"textColor: {Lit.Col(ts.color.value)}");
            if (TextStyleApplier.IsUsableFontSize(ts.fontSize)) args.Add($"textSize: {Lit.F(ts.fontSize.value)}");
            if (ts.fontStyle.isOverridden) args.Add($"textFontStyle: {(int)ts.fontStyle.value}");
            var anchor = node.ResolveTextAnchor();
            if (anchor != TextAnchor.MiddleLeft) args.Add($"textAlign: {(int)anchor}");
            // A built-in glyph has no icon pipeline in the target project — bake it as PNGs at export
            // time, one per editor skin (the tints mirror SasquatchIcons.DefaultTint's two branches).
            if (!node.HideIcon && node.IconSource == ScriptHeaderIconSource.BuiltIn && !string.IsNullOrEmpty(node.IconId))
            {
                var dark = SasquatchIcons.RenderToTexture(node.IconId, 32, new Color(0.85f, 0.85f, 0.85f, 1f));
                var light = SasquatchIcons.RenderToTexture(node.IconId, 32, new Color(0.13f, 0.13f, 0.13f, 1f));
                if (dark != null) args.Add($"iconPngDark: \"{System.Convert.ToBase64String(dark.EncodeToPNG())}\"");
                if (light != null) args.Add($"iconPngLight: \"{System.Convert.ToBase64String(light.EncodeToPNG())}\"");
            }
            // A CUSTOM texture icon bakes the same way — read the pixels at export time and carry them as a
            // base64 PNG, so the generated file needs neither the asset nor the package. Skin-invariant: the
            // user's artwork is used as-is rather than tinted, so one payload covers both skins.
            if (!node.HideIcon && node.IconSource == ScriptHeaderIconSource.Custom && node.CustomIcon != null)
            {
                string png = TryEncodeIconPng(node.CustomIcon);
                if (!string.IsNullOrEmpty(png))
                {
                    args.Add($"iconPngDark: \"{png}\"");
                    args.Add($"iconPngLight: \"{png}\"");
                }
                else
                {
                    _ctx.Note("A Script Header custom icon could not be read (mark the texture Read/Write enabled to bake it).");
                }
            }
            // The header's magnifier and the whole field search behind it PORT (SasqKit's Search section, wired
            // by the AttachSearch call StandaloneEditorExporter emits after the build). All this branch owes is
            // the glyph, which has no icon pipeline in the target project: bake it WHITE and let the kit tint
            // it — idle ink in either skin, accent blue while the query box is open.
            if (node.ShowSearch && _ctx.SearchGlyphPng == null)
            {
                var searchTex = SasquatchIcons.RenderToTexture(SasquatchIcons.Ids.Search, 32, Color.white);
                if (searchTex != null)
                    _ctx.SearchGlyphPng = System.Convert.ToBase64String(searchTex.EncodeToPNG());
            }
            w.Line($"var {v} = SasqKit.ScriptHeader({string.Join(", ", args)});");
            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        private string EmitSpace(CodeWriter w, SpaceNode node, string basePath)
        {
            string v = _ctx.NewVar("space", "space");
            w.Line($"var {v} = new VisualElement();");
            w.Line($"{v}.style.flexShrink = 0f;");
            if (node.Flexible)
                w.Line($"{v}.style.flexGrow = 1f;");
            else if (node.Axis == SpaceAxis.Vertical)
                w.Line($"{v}.style.height = {Lit.F(node.SpaceAmount)};");
            else
                w.Line($"{v}.style.width = {Lit.F(node.SpaceAmount)};");
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        /// <summary>Comment the emitter stamps above each held-open cell (see <c>EmitEmptyCell</c>).</summary>
        internal const string EmptyCellMarker = "// Cell deliberately left empty in the designer; it holds its track.";

        // A cell the designer holds open so a LATER cell can carry a widget. It has to be emitted, not
        // skipped: dropping it would slide every following cell one track left and silently export a
        // different layout than the one the user designed. Mirrors LayoutTreeBuilder.MakeEmptyCellElement's
        // box; the Columns/Grid track helpers give it its width like any other cell.
        private string EmitEmptyCell(CodeWriter w, EmptyCellNode node, string basePath)
        {
            string v = _ctx.NewVar("cell", "emptyCell");
            // Named in the output so a reader of the generated file knows the blank element is deliberate
            // rather than leftover scaffolding — and so a test can prove the cell was emitted at all
            // without guessing the generated variable name.
            w.Line(EmptyCellMarker);
            w.Line($"var {v} = new VisualElement();");
            w.Line($"{v}.pickingMode = PickingMode.Ignore;");
            w.Line($"{v}.style.minHeight = 28f;");
            w.Line($"{v}.style.marginTop = 2f;");
            w.Line($"{v}.style.marginBottom = 2f;");
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        private string EmitDivider(CodeWriter w, LabeledDividerNode node, string basePath)
        {
            string v = _ctx.NewVar("divider", "divider");
            w.Line($"var {v} = new VisualElement();");
            w.Line($"{v}.style.flexDirection = FlexDirection.Row;");
            w.Line($"{v}.style.alignItems = Align.Center;");
            w.Line($"{v}.style.flexShrink = 0f;");
            // Match the live divider's runtime footprint (2px margin + 4px padding top/bottom).
            w.Line($"{v}.style.marginTop = 2f;");
            w.Line($"{v}.style.marginBottom = 2f;");
            w.Line($"{v}.style.paddingTop = 4f;");
            w.Line($"{v}.style.paddingBottom = 4f;");

            _ctx.UseKit(null); // SasqTheme (rule color + secondary label color)

            // Emits one full-width rule element (flex-grows to fill), adds it to the divider, and returns its var.
            string EmitRule()
            {
                string r = _ctx.NewVar("dividerRule", "dividerRule");
                w.Line($"var {r} = new VisualElement();");
                w.Line($"{r}.style.flexGrow = 1f;");
                w.Line($"{r}.style.height = {Lit.F(node.Thickness)};");
                string colorExpr = node.LineColor.isOverridden ? Lit.Col(node.LineColor.value) : "SasqTheme.BorderSoft";
                w.Line($"{r}.style.backgroundColor = {colorExpr};");
                if (node.CornerRadius > 0f)
                {
                    string rad = Lit.F(node.CornerRadius);
                    w.Line($"{r}.style.borderTopLeftRadius = {rad};");
                    w.Line($"{r}.style.borderTopRightRadius = {rad};");
                    w.Line($"{r}.style.borderBottomLeftRadius = {rad};");
                    w.Line($"{r}.style.borderBottomRightRadius = {rad};");
                }
                w.Line($"{v}.Add({r});");
                return r;
            }

            if (string.IsNullOrEmpty(node.Title))
            {
                EmitRule();
            }
            else
            {
                // The caption's horizontal alignment decides WHERE it sits along the rule (a shrink-wrapped label
                // can't visibly text-align, so we PLACE it left / center / right) — mirrors the live
                // BuildLabeledDividerElement: center (the default) puts a rule on BOTH sides of the title.
                var anchor = node.TitleTextStyle.align.isOverridden ? node.TitleTextStyle.align.value : TextAnchor.MiddleCenter;
                bool left = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft;
                bool right = anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight;

                string lbl = _ctx.NewVar("dividerLabel", "dividerLabel");
                w.Line($"var {lbl} = new Label({Lit.Str(node.Title)});");
                // Shrink + wrap so a long caption wraps within the row instead of forcing a horizontal scrollbar
                // (matches the live BuildLabeledDividerElement); a short caption has nothing to shrink.
                w.Line($"{lbl}.style.flexShrink = 1f;");
                w.Line($"{lbl}.style.whiteSpace = WhiteSpace.Normal;");
                w.Line($"{lbl}.style.color = SasqTheme.LabelSecondary;");
                w.Line($"{lbl}.style.unityFontStyleAndWeight = FontStyle.Bold;");
                // Line up wrapped lines with the placement (a T-panel align override emits its own below).
                if (!node.TitleTextStyle.align.isOverridden)
                    w.Line($"{lbl}.style.unityTextAlign = TextAnchor.{anchor};");
                if (StyleEmitter.HasTextOverride(node.TitleTextStyle))
                    StyleEmitter.EmitTextStyle(w, lbl, node.TitleTextStyle, _ctx);

                if (left)
                {
                    w.Line($"{lbl}.style.marginLeft = 0f;");
                    w.Line($"{lbl}.style.marginRight = 8f;");
                    w.Line($"{v}.Add({lbl});");
                    EmitRule();
                }
                else if (right)
                {
                    w.Line($"{lbl}.style.marginLeft = 8f;");
                    w.Line($"{lbl}.style.marginRight = 0f;");
                    EmitRule();
                    w.Line($"{v}.Add({lbl});");
                }
                else
                {
                    w.Line($"{lbl}.style.marginLeft = 8f;");
                    w.Line($"{lbl}.style.marginRight = 8f;");
                    EmitRule();
                    w.Line($"{v}.Add({lbl});");
                    EmitRule();
                }
            }

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        private string EmitTextBlock(CodeWriter w, TextBlockNode node, string basePath)
        {
            // Bake the lightweight markdown to Unity rich text at export time using the package's OWN converter,
            // so **bold** / *italics* / `code` / {color|…} palette tokens / [links] all resolve now (the palette
            // library is available at export). enableRichText then renders the baked tags standalone.
            string display = node.Text;
            bool wireLinks = false;
            if (node.RenderMarkdown && !string.IsNullOrEmpty(node.Text))
            {
                display = SasquatchMarkdown.ToRichText(node.Text);
                wireLinks = display.Contains("<link=");
            }

            // A fenced ``` block cannot render inside one Label, so a text block that has one becomes the
            // split body container instead — the same treatment the Help Box branch gives its message. Every
            // styling line below applies to a VisualElement just as well, and the text properties are
            // inherited, so they still reach the paragraphs inside.
            string mdBody = node.RenderMarkdown ? EmitMarkdownBody(w, node.Text) : null;
            string v;
            if (mdBody != null)
            {
                v = mdBody;
            }
            else
            {
                v = _ctx.NewVar("textBlock", "textBlock");
                w.Line($"var {v} = new Label({Lit.Str(display)});");
                w.Line($"{v}.enableRichText = true;");
                w.Line($"{v}.style.whiteSpace = WhiteSpace.Normal;");
            }
            if (StyleEmitter.HasTextOverride(node.TextStyle))
                StyleEmitter.EmitTextStyle(w, v, node.TextStyle, _ctx);
            if (node.BackgroundColor.isOverridden)
            {
                w.Line($"{v}.style.backgroundColor = {Lit.Col(node.BackgroundColor.value)};");
                if (node.Padding > 0f)
                {
                    string p = Lit.F(node.Padding);
                    w.Line($"{v}.style.paddingTop = {p};");
                    w.Line($"{v}.style.paddingBottom = {p};");
                    w.Line($"{v}.style.paddingLeft = {p};");
                    w.Line($"{v}.style.paddingRight = {p};");
                }
                if (node.CornerRadius > 0f)
                {
                    string r = Lit.F(node.CornerRadius);
                    w.Line($"{v}.style.borderTopLeftRadius = {r};");
                    w.Line($"{v}.style.borderTopRightRadius = {r};");
                    w.Line($"{v}.style.borderBottomLeftRadius = {r};");
                    w.Line($"{v}.style.borderBottomRightRadius = {r};");
                }
            }
            if (node.MaxWidth > 0f) w.Line($"{v}.style.maxWidth = {Lit.F(node.MaxWidth)};");
            if (wireLinks) EmitLinkWiring(w, v);

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        /// <summary>
        /// Makes baked <c>[label](url)</c> links clickable on <paramref name="v"/> (or any Label inside it — the
        /// link-tag event bubbles). The text already carries the blue-underlined <c>&lt;link=…&gt;</c> spans from
        /// ToRichText; without this they render as links and do nothing.
        /// <para>
        /// The single emit site for the experimental link-tag events, mirroring why the live package isolates its
        /// own wiring in SasquatchMarkdown.WireLinkEvents: if Unity ever drops these events, one method breaks and
        /// the text still renders — the links just go inert.
        /// </para></summary>
        private static void EmitLinkWiring(CodeWriter w, string v)
        {
            w.Line($"{v}.pickingMode = PickingMode.Position;");
            w.Line($"{v}.RegisterCallback<UnityEngine.UIElements.Experimental.PointerUpLinkTagEvent>(__e => {{ if (!string.IsNullOrEmpty(__e.linkID)) {{ Application.OpenURL(__e.linkID); __e.StopPropagation(); }} }});");
        }

        private string EmitHelpBox(CodeWriter w, HelpBoxNode node, string basePath)
        {
            string v = _ctx.NewVar("helpBox", "helpBox");
            int msgType = node.MessageType == HelpBoxMessageType.Warning ? 1
                : node.MessageType == HelpBoxMessageType.Error ? 2 : 0;

            // Render Markdown is baked at export time, exactly as it already is for a Text Block and for a
            // field's help chip — the converter runs HERE, so the generated file only ever carries finished
            // rich text and needs no package at runtime. Emitting node.Message raw would put the literal
            // asterisks, backticks and [link](...) syntax on screen.
            string message = node.RenderMarkdown && !string.IsNullOrEmpty(node.Message)
                ? SasquatchMarkdown.ToRichText(node.Message)
                : node.Message;
            // Same treatment the Text Block branch gives its links. All three help-box shapes host the message in
            // a Label descendant and the link-tag event bubbles, so one registration on the outer element covers
            // the chip, the expander and the stock box alike.
            bool wireHelpLinks = !string.IsNullOrEmpty(message) && message.Contains("<link=");

            // Fenced ``` blocks cannot live in a single Label, so a message that has them is emitted as a
            // pre-split body of alternating text labels and code panels. Null for everything else.
            string mdBody = node.RenderMarkdown ? EmitMarkdownBody(w, node.Message) : null;
            // With a body the message string is dead weight — the body supersedes it — and emitting it anyway
            // would embed the raw ``` fences in the buyer's file as text nothing ever renders.
            if (mdBody != null) message = string.Empty;

            if (node.Expandable && node.CollapsedStyle == HelpBoxCollapsedStyle.Chip)
            {
                _ctx.UseKit("HelpBox");
                var args = new List<string> { Lit.Str(message) };
                if (msgType != 0) args.Add($"msgType: {msgType}");
                if (node.ChipAlignment != ImageAlignment.Left) args.Add($"align: {(int)node.ChipAlignment}");
                if (mdBody != null) args.Add($"body: {mdBody}");
                w.Line($"var {v} = SasqKit.HelpChip({string.Join(", ", args)});");
            }
            else if (node.Expandable)
            {
                _ctx.UseKit("HelpBox");
                string summary = node.IsSummaryHidden ? "" : node.EffectiveSummaryOverride ?? "";
                string bodyArg = mdBody != null ? $", body: {mdBody}" : "";
                w.Line($"var {v} = SasqKit.HelpExpand({Lit.Str(summary)}, {Lit.Str(message)}, {msgType}, {B(node.DefaultToOpen)}{bodyArg});");
            }
            else
            {
                w.Line($"var {v} = new HelpBox({Lit.Str(message)}, HelpBoxMessageType.{node.MessageType});");
                // A stock HelpBox's inner label does not parse rich text, so markdown would render as raw tags.
                // (The kit's chip/expand labels are plain Labels, which already do.)
                if (node.RenderMarkdown && !string.IsNullOrEmpty(node.Message))
                {
                    string lbl = _ctx.NewVar("helpLabel", "helpLabel");
                    w.Line($"var {lbl} = {v}.Q<Label>();");
                    w.Line($"if ({lbl} != null) {lbl}.enableRichText = true;");
                    // The stock box's label must be hit-testable for the link event to originate at all.
                    if (wireHelpLinks) w.Line($"if ({lbl} != null) {lbl}.pickingMode = PickingMode.Position;");
                }
            }
            if (wireHelpLinks) EmitLinkWiring(w, v);
            // Message / summary text styles. All three shapes build their own labels, so like the frame caption
            // and the collection header these need re-finding rather than a style pass on the outer element.
            // The message label is the LAST one in every shape (the expandable summary sits before it).
            if (StyleEmitter.HasTextOverride(node.MessageTextStyle) || StyleEmitter.HasTextOverride(node.SummaryTextStyle))
            {
                string labels = _ctx.NewVar("helpLabels", "helpLabels");
                w.Line($"var {labels} = {v}.Query<Label>().ToList();");
                if (StyleEmitter.HasTextOverride(node.SummaryTextStyle) && node.Expandable)
                {
                    string s = _ctx.NewVar("helpSummary", "helpSummary");
                    w.Line($"var {s} = {labels}.Count > 0 ? {labels}[0] : null;");
                    w.Line($"if ({s} != null)");
                    w.OpenBrace();
                    StyleEmitter.EmitTextStyle(w, s, node.SummaryTextStyle, _ctx);
                    w.CloseBrace();
                }
                if (StyleEmitter.HasTextOverride(node.MessageTextStyle))
                {
                    string m = _ctx.NewVar("helpMessage", "helpMessage");
                    w.Line($"var {m} = {labels}.Count > 0 ? {labels}[{labels}.Count - 1] : null;");
                    w.Line($"if ({m} != null)");
                    w.OpenBrace();
                    StyleEmitter.EmitTextStyle(w, m, node.MessageTextStyle, _ctx);
                    w.CloseBrace();
                }
            }

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        // Twin of LayoutTreeBuilder's `EmbeddedEditorHost.Build(node, serializedObject, designMode)` leaf.
        //
        // This used to be skipped outright as "a Sasquatch-only feature". It is not one. Every part live uses is
        // public API and the kit already proved most of it package-free for the In-Line Editor: the genuine
        // editor is found through TypeCache plus the CustomEditor attribute's own constructor argument (no
        // reflection into editor internals — §2.5.g is not in play), it is instantiated with
        // Editor.CreateEditor(targets, type), and it is hosted in an InspectorElement. The one piece that does
        // NOT travel is the [SasquatchEmbed] attribute — it lives in the Sasquatch Runtime assembly — so the kit
        // matches the slice by NAME instead, which resolves the same method (measured on the fixture's two
        // slices, VisualElement and void).
        //
        // No baked editor-type literal: the kit re-resolves from the target type at build time, exactly as live
        // does, so the export self-heals across an editor-class rename the same way live's does.
        //
        // The note is an ASSET-DEPENDENCY ADVISORY, the same class as the image banner's texture-by-GUID line —
        // not a degradation. The widget renders in full; it just needs the type's own editor script to travel
        // with the generated file, and a buyer who does not ship it gets the kit's HelpBox saying exactly that.
        private string EmitEmbeddedEditor(CodeWriter w, EmbeddedEditorNode node, string basePath)
        {
            _ctx.UseKit("EmbeddedEditor");

            string what = string.IsNullOrEmpty(node.SliceMethodName)
                ? "An embedded editor re-inlines"
                : $"An embedded editor re-inlines the '{node.SliceMethodName}' slice of";
            string targetName = _ctx.TargetType != null ? _ctx.TargetType.Name : "the inspected type";
            _ctx.Note($"{what} {targetName}'s own [CustomEditor] at runtime — include that editor script in the shared project.");

            string v = _ctx.NewVar("embed", "embeddedEditor");
            // Defaults emit nothing: an empty slice name IS the kit's default (embed the whole editor).
            string slice = string.IsNullOrEmpty(node.SliceMethodName) ? "" : $", {Lit.Str(node.SliceMethodName)}";
            w.Line($"var {v} = SasqKit.EmbeddedEditor(so{slice});");

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        // Twin of LayoutTreeBuilder.BuildImageBannerElement — the node renders as an alignment ROW hosting the
        // image, never as a bare Image (which has nowhere to honour Alignment, Zoom or Crop Alpha).
        // The row is what clips, unconditionally: zoom is a center-origin transform, invisible to layout, so
        // without the clip a zoomed image paints over its neighbours. See the live builder for the full why.
        private string EmitImageBanner(CodeWriter w, ImageBannerNode node, string basePath)
        {
            float height = Mathf.Clamp(node.Height, ImageBannerNode.MinHeight, ImageBannerNode.MaxHeight);
            float zoom = Mathf.Clamp(node.Zoom, ImageBannerNode.MinZoom, ImageBannerNode.MaxZoom);
            bool fit = node.ScaleMode == ImageBannerScaleMode.ScaleToFit;

            string v = _ctx.NewVar("banner", "banner");
            w.Line($"var {v} = new VisualElement();");
            w.Line($"{v}.style.flexDirection = FlexDirection.Row;");
            w.Line($"{v}.style.flexShrink = 0f;");
            w.Line($"{v}.style.marginTop = 4f;");
            w.Line($"{v}.style.marginBottom = 4f;");
            w.Line($"{v}.style.overflow = Overflow.Hidden; // zoom is a transform — the row is what crops it");
            string justify = node.Alignment == ImageBannerAlignment.Left ? "FlexStart"
                           : node.Alignment == ImageBannerAlignment.Right ? "FlexEnd"
                           : "Center";
            w.Line($"{v}.style.justifyContent = Justify.{justify};");

            if (string.IsNullOrEmpty(node.TextureGuid))
            {
                // No texture picked: the live builder reserves a washed block of the banner's height (the
                // "pick a texture" hint is design-mode only, and an exported editor is never in design mode).
                _ctx.UseKit(null); // SasqTheme (the wash)
                string ph = _ctx.NewVar("bannerPlaceholder", "bannerPlaceholder");
                w.Line($"var {ph} = new Label(string.Empty);");
                w.Line($"{ph}.style.flexGrow = 1f;");
                w.Line($"{ph}.style.height = {Lit.F(height)};");
                w.Line($"{ph}.style.unityTextAlign = TextAnchor.MiddleCenter;");
                w.Line($"{ph}.style.backgroundColor = SasqTheme.CardWash;");
                w.Line($"{ph}.style.borderTopLeftRadius = 4f;");
                w.Line($"{ph}.style.borderTopRightRadius = 4f;");
                w.Line($"{ph}.style.borderBottomLeftRadius = 4f;");
                w.Line($"{ph}.style.borderBottomRightRadius = 4f;");
                w.Line($"{ph}.pickingMode = PickingMode.Ignore;");
                w.Line($"{v}.Add({ph});");
            }
            else
            {
                _ctx.Note("An image banner references a texture by GUID — include that texture asset in the shared project.");

                // GUID-referenced (survives asset moves/renames), resolved through the same Texture2D-then-Sprite
                // chain the live builder uses. A texture missing from the target project leaves the row as a
                // correctly-sized empty block rather than throwing.
                string path = _ctx.NewVar("bannerPath", "bannerPath");
                string tex = _ctx.NewVar("bannerTex", "bannerTex");
                string sprite = _ctx.NewVar("bannerSprite", "bannerSprite");
                w.Line($"var {path} = AssetDatabase.GUIDToAssetPath({Lit.Str(node.TextureGuid)});");
                w.Line($"Texture {tex} = AssetDatabase.LoadAssetAtPath<Texture2D>({path});");
                w.Line($"if ({tex} == null)");
                w.OpenBrace();
                w.Line($"var {sprite} = AssetDatabase.LoadAssetAtPath<Sprite>({path});");
                w.Line($"if ({sprite} != null) {tex} = {sprite}.texture;");
                w.CloseBrace();

                string img = _ctx.NewVar("bannerImage", "bannerImage");
                string scaleMode = node.ScaleMode == ImageBannerScaleMode.ScaleAndCrop ? "ScaleAndCrop"
                                 : node.ScaleMode == ImageBannerScaleMode.StretchToFill ? "StretchToFill"
                                 : "ScaleToFit";
                w.Line($"var {img} = new Image {{ image = {tex} }};");
                w.Line($"{img}.scaleMode = ScaleMode.{scaleMode};");
                w.Line($"{img}.style.height = {Lit.F(height)};");

                // Crop Alpha tightens the fit to the texture's opaque bounding box. That needs a pixel read, so
                // it is BAKED here at export time (the generated file is package-free and reads no pixels) —
                // both the sub-rect and the intrinsic width it implies. Never write a zero sourceRect: UITK
                // renders a flat gray for one. Re-export if the texture's opaque bounds change.
                bool bakedCrop = false;
                if (fit && node.CropAlpha)
                {
                    var resolved = ResolveBannerTexture(node.TextureGuid);
                    if (resolved != null
                        && LayoutTreeBuilder.TryGetOpaqueSourceRect(resolved, node.TextureGuid, out var srcPx)
                        && srcPx.width > 0f && srcPx.height > 0f)
                    {
                        w.Line($"{img}.sourceRect = new Rect({Lit.F(srcPx.x)}, {Lit.F(srcPx.y)}, " +
                               $"{Lit.F(srcPx.width)}, {Lit.F(srcPx.height)}); // baked opaque bounds (Crop Alpha)");
                        w.Line($"{img}.style.width = {Lit.F(height * (srcPx.width / srcPx.height))};");
                        w.Line($"{img}.style.maxWidth = Length.Percent(100);");
                        bakedCrop = true;
                    }
                }

                if (fit && !bakedCrop)
                {
                    // Intrinsic width at this height so Alignment (Left/Center/Right) is meaningful. Computed at
                    // runtime from the resolved texture, so swapping the asset re-fits instead of keeping a
                    // stale baked width.
                    w.Line($"if ({tex} != null && {tex}.height > 0)");
                    w.OpenBrace();
                    w.Line($"{img}.style.width = {Lit.F(height)} * ((float){tex}.width / {tex}.height);");
                    w.Line($"{img}.style.maxWidth = Length.Percent(100);");
                    w.CloseBrace();
                    w.Line("else");
                    w.OpenBrace();
                    w.Line($"{img}.style.flexGrow = 1f;");
                    w.CloseBrace();
                }
                else if (!fit)
                {
                    w.Line($"{img}.style.flexGrow = 1f; // Crop / Stretch fill the full width (alignment is moot)");
                }

                if (!Mathf.Approximately(zoom, 1f))
                    w.Line($"{img}.style.scale = new Scale(new Vector2({Lit.F(zoom)}, {Lit.F(zoom)}));");

                w.Line($"{img}.pickingMode = PickingMode.Ignore;");
                w.Line($"{v}.Add({img});");
            }

            StyleEmitter.EmitLayoutStyle(w, v, node, _ctx);
            ConditionEmitter.Emit(w, _ctx, node, v, basePath);
            return v;
        }

        /// <summary>The banner's texture as the live builder resolves it (Texture2D, else a Sprite's atlas page),
        /// for export-time baking. Null when the GUID doesn't resolve in THIS project.</summary>
        private static Texture ResolveBannerTexture(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            Texture tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) tex = sprite.texture;
            }
            return tex;
        }

        // ---- Helpers ---------------------------------------------------------------------------------------

        private static string Combine(string basePath, string path)
        {
            if (string.IsNullOrEmpty(basePath)) return path;
            if (string.IsNullOrEmpty(path)) return basePath;
            return basePath + "." + path;
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "field";
            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path.Substring(dot + 1) : path;
        }

        // Bakes the frame's overlay wash with the LIVE FrameBuilder ramp bake (gradient color+alpha per row,
        // edge/length folded into the alpha) and returns it as a base64 PNG for the generated file — the
        // standalone wash is pixel-identical to the package's. The bake target is a throwaway texture.
        /// <summary>
        /// A user texture as a base64 PNG, for baking into the generated file. Most imported textures are NOT
        /// Read/Write enabled, so <c>EncodeToPNG</c> throws on them — blit through a temporary RenderTexture to
        /// read the pixels back regardless of import settings. Returns null when even that fails (a crunched or
        /// otherwise unreadable format), so the caller can record an honest note instead of emitting nothing.
        /// </summary>
        private static string TryEncodeIconPng(Texture2D source)
        {
            if (source == null) return null;
            try { return System.Convert.ToBase64String(source.EncodeToPNG()); }
            catch { /* not readable — fall through to the blit path */ }

            RenderTexture rt = null;
            RenderTexture prev = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return System.Convert.ToBase64String(copy.EncodeToPNG());
            }
            catch { return null; }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        private static string BakeOverlayRampPng(FrameSettings f)
        {
            var ramp = FrameBuilder.BakeOverlayRamp(f.OverlayGradient, f.OverlayEdge, f.OverlaySizePercent, null);
            try { return System.Convert.ToBase64String(ramp.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(ramp); }
        }

        /// <summary>
        /// Appends a frame's image-fill arguments to a <c>SasqKit.Frame</c> / <c>SasqKit.SplitFrame</c> call.
        /// The fill is PORTED, not skipped: live's <c>FrameBuilder.AddFillLayer</c> is public API end to end
        /// (AssetDatabase GUID resolve, a background image, background-size/-position for scale × zoom ×
        /// focus), so the only thing that cannot travel is the texture's pixels — and those go the way the
        /// image banner's do, by GUID.
        /// <para>Two deliberate choices. The bound path is baked ABSOLUTE, because live reads the fill field
        /// off the root <c>serializedObject</c>, so inside a list row a frame fill binds the inspected object
        /// and not the row element — same behaviour, same limitation. And per-pane fills only ride the split
        /// branch, because live only builds them inside a pane clipper.</para>
        /// Defaults emit nothing: every framing knob travels only when it deviates from the model default.
        /// </summary>
        private void AddFrameFillArgs(System.Collections.Generic.List<string> args, FrameSettings f, bool split)
        {
            bool hasBinding = !string.IsNullOrEmpty(f.FillBindingPath);
            bool hasStatic = !string.IsNullOrEmpty(f.FillTextureGuid);
            bool paneA = split && !string.IsNullOrEmpty(f.PaneAFillGuid);
            bool paneB = split && !string.IsNullOrEmpty(f.PaneBFillGuid);
            if (!hasBinding && !hasStatic && !paneA && !paneB) return;

            // An ASSET-DEPENDENCY advisory, not a degradation — the same class as the image banner's, and
            // deliberately carrying its wording so the two read as one rule. A bound fill needs no advisory:
            // the image comes off the inspected object, so nothing has to travel beside the script.
            if (hasStatic || paneA || paneB)
                _ctx.Note("A Frame image fill references a texture by GUID — include that texture asset in the shared project.");

            if (hasBinding)
            {
                args.Add("fillSo: so");
                args.Add($"fillPath: {Lit.Str(f.FillBindingPath)}");
            }
            if (hasStatic) args.Add($"fillGuid: {Lit.Str(f.FillTextureGuid)}");
            // Framing applies to the pane fills too (live: paneFill.scaleMode = ToScaleMode(FillScaleMode)),
            // so these travel whenever ANY fill source is configured.
            if (f.FillScaleMode != ImageBannerScaleMode.StretchToFill)
                args.Add($"fillScaleMode: {(int)f.FillScaleMode}");
            if (!Mathf.Approximately(f.FillZoom, 1f)) args.Add($"fillZoom: {Lit.F(f.FillZoom)}");
            if (!Mathf.Approximately(f.FillFocus.x, 0.5f)) args.Add($"fillFocusX: {Lit.F(f.FillFocus.x)}");
            if (!Mathf.Approximately(f.FillFocus.y, 0.5f)) args.Add($"fillFocusY: {Lit.F(f.FillFocus.y)}");
            if (paneA) args.Add($"paneAFillGuid: {Lit.Str(f.PaneAFillGuid)}");
            if (paneB) args.Add($"paneBFillGuid: {Lit.Str(f.PaneBFillGuid)}");
        }

        // The label a Columns/Grid cell renders itself (in place of the field's aligned inline label, which is
        // hidden at runtime when present). A label-hidden field passes null; the resolved text otherwise mirrors
        // EmitBinding's default-label derivation. SasqKit.ColumnCell keeps this hidden for a kit widget that
        // already draws its own label, so passing it for every binding is safe.
        private string CellLabel(BindingNode node, string basePath)
        {
            if (node.IsLabelHidden) return null;
            if (node.EffectiveLabelOverride != null) return node.EffectiveLabelOverride;
            string path = Combine(basePath, node.PropertyPath);
            return ObjectNames.NicifyVariableName(LastSegment(path));
        }

        private static string ContainerBaseName(ContainerNode c)
        {
            if (!string.IsNullOrEmpty(c.Title)) return c.Title;
            if (!string.IsNullOrEmpty(c.TabLabel)) return c.TabLabel;
            return c.Type.ToString();
        }

        private static string TabLabel(LayoutNode child, int index)
        {
            if (child is ContainerNode cc)
            {
                if (!string.IsNullOrEmpty(cc.TabLabel)) return cc.TabLabel;
                if (!string.IsNullOrEmpty(cc.Title)) return cc.Title;
            }
            return "Tab " + (index + 1);
        }

        private static string B(bool b) => b ? "true" : "false";

        // Mirrors the live IsNumericProperty gate (SerializedPropertyType Integer/Float) at the field-type level.
        private static bool IsNumericFieldType(System.Type t) =>
            t == typeof(int) || t == typeof(float) || t == typeof(long) || t == typeof(double)
            || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort)
            || t == typeof(uint) || t == typeof(ulong);
    }
}
