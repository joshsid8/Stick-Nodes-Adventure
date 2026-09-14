using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// How alike a multi-selection is — and therefore how much of the Properties panel it can be offered.
    /// The panel does NOT have one multi-edit mode; it has three, and the rule is simply "the more the
    /// selected nodes have in common, the more of the real panel you get".
    /// </summary>
    internal enum MultiEditTier
    {
        /// <summary>One node (or none) — the ordinary single-node panel.</summary>
        Single,

        /// <summary>Different node KINDS. Only the box model every <see cref="LayoutNode"/> carries can apply.</summary>
        MixedKinds,

        /// <summary>Same kind, different widget/container type. The kind's shared rows apply; its per-type options can't.</summary>
        SameKind,

        /// <summary>Same kind AND same type — five progress bars. Everything applies, so the REAL panel is used.</summary>
        Identical
    }

    // ===== Multi-edit =====
    // A multi-selection is styled through the tier above. The top tier deliberately does NOT re-implement the
    // Properties panel: it builds the ordinary single-node panel against the last-clicked node and lets
    // MultiEditMirror fan each edit out to the rest. That is what makes "select two progress bars and get ALL
    // the progress bar options" true by construction rather than by a list somebody has to remember to extend.
    //
    // Identity — paths, labels, headers, tooltips, method names, prose — never fans out and is hidden here, so
    // no row in this mode can be read as applying to all when it only ever meant one node.
    public partial class SasquatchLayoutDesignerWindow
    {
        // Selection signature so multi-edit tweaks preserve the panel scroll position (the
        // single-node check compares only _selectedNode, which doesn't change between multi edits).
        private HashSet<LayoutNode> _lastPropertiesSelection;

        /// <summary>Marker on a row that can only ever mean ONE node; hidden while a multi-selection is up.</summary>
        internal const string IdentityRowClass = "sasq-identity-row";

        /// <summary>Marker on anything the multi-edit sweep hid, so the sweep can read its own work back.</summary>
        internal const string MultiHiddenClass = "sasq-multi-hidden";

        /// <summary>
        /// Tags <paramref name="row"/> as identity — per-node text or a per-target-type code hook. Returns it,
        /// so a build site reads <c>section.Add(Identity(pathField))</c>. The tag is the SOLE authority the
        /// multi-edit sweep uses; its data twin is <c>MultiEditMirror.IdentityFields</c>, which stops the same
        /// values travelling even if a row is ever missed here.
        /// </summary>
        private static T Identity<T>(T row) where T : VisualElement
        {
            row?.AddToClassList(IdentityRowClass);
            return row;
        }

        // The armed delta mirror. Non-null only while the top tier's panel is up.
        private MultiEditMirror _multiEditMirror;

        // ---- Tier ---------------------------------------------------------------------------------------------

        // The shared runtime type of the whole selection, or null when kinds are mixed.
        private Type GetSharedSelectedNodeType()
        {
            Type shared = null;
            foreach (var n in _selectedNodes)
            {
                if (n == null) return null;
                var t = n.GetType();
                if (shared == null) shared = t;
                else if (shared != t) return null;
            }
            return shared;
        }

        /// <summary>
        /// The node's TYPE within its kind — what decides whether the per-type option rows mean the same thing
        /// on two nodes. A Frame carries its shape as well, because the shape is what re-reads the children.
        /// </summary>
        private static object GetMultiEditSubKey(LayoutNode node)
        {
            switch (node)
            {
                case BindingNode b: return b.DisplayType;
                case ContainerNode c:
                    return c.Type == ContainerType.Frame
                        ? (object)(((int)c.Type << 8) | (int)c.Frame.Shape)
                        : c.Type;
                case CollectionNode col: return col.DisplayMode;
                // Every other kind has exactly one shape, so same-kind IS identical.
                default: return 0;
            }
        }

        private MultiEditTier GetMultiEditTier()
        {
            if (!HasMultiSelection) return MultiEditTier.Single;
            if (GetSharedSelectedNodeType() == null) return MultiEditTier.MixedKinds;

            object shared = null;
            bool first = true;
            foreach (var n in _selectedNodes)
            {
                var key = GetMultiEditSubKey(n);
                if (first) { shared = key; first = false; }
                else if (!Equals(shared, key)) return MultiEditTier.SameKind;
            }
            return MultiEditTier.Identical;
        }

        // ---- The mirror ---------------------------------------------------------------------------------------

        /// <summary>
        /// Arms (or stands down) the delta mirror for the panel about to be built. Called on every properties
        /// rebuild, which is also every selection change — so the mirror can never outlive the panel it belongs to.
        /// </summary>
        private void ArmMultiEditMirror(MultiEditTier tier)
        {
            if (tier == MultiEditTier.Single || _selectedNode == null)
            {
                _multiEditMirror = null;
                return;
            }

            // ---- Before the snapshot: make the selection's condition state self-consistent -------------------
            //
            // A node can hold an empty ConditionRules list AND a live LEGACY `conditions` list — that is simply
            // what a pre-rules saved layout looks like until something migrates it. Under a fan-out that pairing
            // is a grenade, and the mirror cannot defuse it, because the mirror is DELTA-based: the PRIMARY's
            // legacy list never changes, so it never travels, however carefully `conditions` is listed in the
            // scope set. Delete the primary's last rule, watch the empty list fan onto a follower, and the next
            // EnsureMigrated rebuilds that follower's rules straight back out of the legacy list it kept.
            // Silently, and only on the follower.
            //
            // So strip it here — migrate, then clear, exactly as SaveLayoutAsset does at the persist chokepoint.
            // Lossless (the migration runs first) and idempotent. It MUST happen before the mirror snapshots, or
            // the migration itself reads as a delta and the primary's rules stamp the selection with no gesture
            // aimed at conditions at all.
            foreach (var n in _selectedNodes) ConditionModel.StripLegacyConditions(n);

            // The ROOT is never a follower — the same guard Paste Style takes. It is the layout's own container
            // rather than a widget in it, so a fanned-out container-type change would restructure the whole tree
            // through a gesture aimed at two ordinary sections.
            var followers = new List<LayoutNode>();
            var root = _clonedLayoutAsset?.Root;
            foreach (var n in _selectedNodes)
                if (n != null && !ReferenceEquals(n, root)) followers.Add(n);

            // Identical = the full opt-OUT sweep. The selection has proved every row means the same thing on
            // every node, so every field may travel.
            //
            // The lower tiers have proved no such thing, and the sweep FAILS UNSAFE — an unclassified field is
            // written over each follower's value. So they arm a SCOPE: the union of the field sets backing the
            // few sections those tiers actually show. An opt-IN allow-list, which fails safe. Keep this union in
            // step with what BuildMultiEditPanel appends, or a section is on screen writing to one node only.
            ICollection<string> scope = null;
            if (tier != MultiEditTier.Identical)
            {
                var union = new HashSet<string>();
                union.UnionWith(MultiEditMirror.ConditionFields);
                union.UnionWith(MultiEditMirror.AnimationFields);
                if (SelectionSupportsAffixSection()) union.UnionWith(MultiEditMirror.AffixFields);
                if (SelectionSupportsValidationSection()) union.UnionWith(MultiEditMirror.ValidationFields);
                scope = union;
            }

            _multiEditMirror = new MultiEditMirror(_selectedNode, followers, ResolveSelectedNodeFieldType, scope);
        }

        /// <summary>
        /// Fans the primary's just-made edit out to the rest of the selection, returning how many fields
        /// travelled. Called from two places, and it needs BOTH:
        /// <list type="bullet">
        /// <item><c>OnModification</c> — after the edit and after its <c>Undo.RecordObject</c>, so the
        /// followers' writes land inside the SAME undo step, and before auto-save, so they are in the file.</item>
        /// <item>every TICK of a debounced drag burst — because a burst defers <c>OnModification</c> until the
        /// pointer is released. Fanning out only at commit left the followers' models untouched for the whole
        /// drag, so a colour dragged across two selected knobs animated one of them and the other snapped on
        /// release. The burst's undo snapshot is taken before its first tick, so per-tick follower writes are
        /// covered by it and one Ctrl+Z still reverts the whole drag.</item>
        /// </list>
        /// <para>A modification that never touched the primary (every structural op — add, delete, reorder)
        /// produces an empty delta and costs one field sweep.</para>
        /// </summary>
        private int MirrorMultiEditDelta()
        {
            if (_multiEditMirror == null || _clonedLayoutAsset == null) return 0;

            // Stale mirror: the selection moved on without a panel rebuild, or an undo re-deserialized the
            // layout so every LayoutNode is a NEW instance and the mirror is holding orphans. Stand down rather
            // than write to nodes that are no longer in the tree.
            if (!HasMultiSelection || !ReferenceEquals(_multiEditMirror.Primary, _selectedNode))
            {
                _multiEditMirror = null;
                return 0;
            }

            return _multiEditMirror.ApplyDelta();
        }

        /// <summary>
        /// Hands the mirror the answer the user gave in a structural dialog, for the ONE fan-out that
        /// immediately follows (the mirror resets it afterwards). A no-op when no multi-selection is armed.
        /// The panel is the only place that can raise such a dialog — a fan-out cannot sensibly ask once per
        /// follower — so the choice has to travel this way or the followers silently take the default.
        /// </summary>
        private void MirrorNextEnterSplit(EnterSplitMode mode)
        {
            if (_multiEditMirror != null) _multiEditMirror.NextEnterSplit = mode;
        }

        // ---- Shared helpers -----------------------------------------------------------------------------------

        // One undo record covering an edit fanned out to every selected node.
        private void FanOutEdit(string undoName, Action<LayoutNode> edit)
        {
            if (_clonedLayoutAsset == null) return;
            Undo.RecordObject(_clonedLayoutAsset, undoName);
            foreach (var n in _selectedNodes)
            {
                if (n != null) edit(n);
            }
            OnModification();
            RefreshPreview();
        }

        private bool SelectionValuesDiffer<T>(Func<LayoutNode, T> read)
        {
            var comparer = EqualityComparer<T>.Default;
            bool first = true;
            T reference = default;
            foreach (var n in _selectedNodes)
            {
                if (n == null) continue;
                var v = read(n);
                if (first) { reference = v; first = false; }
                else if (!comparer.Equals(reference, v)) return true;
            }
            return false;
        }

        // Muted label + tooltip on a row whose values disagree — the honest "mixed" treatment
        // (mirrors MixedValueUI's muted gray). The row stays editable; editing unifies.
        private static void MarkRowMixed(VisualElement row, bool mixed)
        {
            if (!mixed || row == null) return;
            var label = row.Q<Label>();
            if (label != null) label.style.color = DesignerPalette.Mixed;
            row.tooltip = "Values differ across the selection — editing here sets ALL selected nodes.";
        }

        // ---- The type-independent sections every tier can offer ------------------------------------------------
        //
        // Four sections mean exactly the same thing whatever a node's Display Type is — and Conditionals and
        // Animations mean the same thing whatever its KIND is, because both are declared on LayoutNode itself.
        // Gating them on "same widget" was never anything but a side effect of where the tier dispatch returned,
        // and it read to the user as "conditions are single-select only".
        //
        // They are reused, not re-authored: the same builders the single-node panel calls, with the writes fanned
        // out by the scoped mirror. That is the same bargain the Identical tier already makes, and it is what
        // stops these sections drifting the way the old hand-written multi-edit panel did.

        /// <summary>Runs <paramref name="test"/> over the whole selection and answers only if every node passes.</summary>
        private bool EverySelected<T>(Func<T, bool> test) where T : LayoutNode
        {
            if (_selectedNodes.Count == 0) return false;
            foreach (var n in _selectedNodes)
            {
                if (!(n is T typed)) return false;
                if (!test(typed)) return false;
            }
            return true;
        }

        /// <summary>Every selected node is a binding whose widget can carry SOME affix.</summary>
        private bool SelectionSupportsAffixSection()
            => EverySelected<BindingNode>(_ => true) && SelectionAffixCapabilities().Any;

        /// <summary>
        /// What the whole selection can honour — the intersection, so a Progress Bar in the selection quietly
        /// narrows the section to Text and Gap rather than offering icon rows that would do nothing on it.
        /// </summary>
        private FieldAffixes.AffixCapabilities SelectionAffixCapabilities()
        {
            var bindings = new List<BindingNode>();
            foreach (var n in _selectedNodes)
                if (n is BindingNode b) bindings.Add(b);
            // The field-type resolver goes WITH the nodes: a dictionary table carries no affix, and the
            // SerializedDictionary route to it is invisible from the DisplayType alone.
            return FieldAffixes.Intersect(bindings, ResolveSelectedNodeFieldType);
        }

        /// <summary>
        /// Every selected node carries validation fields. Bindings and collections both do, and the builder
        /// already handles either — so a mixed bag of the two still gets the section, which is the one place a
        /// MixedKinds selection earns more than the box model.
        /// </summary>
        private bool SelectionSupportsValidationSection()
        {
            if (_selectedNodes.Count == 0) return false;
            foreach (var n in _selectedNodes)
                if (!(n is BindingNode) && !(n is CollectionNode)) return false;
            return true;
        }

        /// <summary>
        /// Appends the shared sections to a lower tier's panel. Ordered to match the single-node panel, so a
        /// user who moves between one node and several finds them in the same place.
        /// </summary>
        private void AppendMultiEditSharedSections()
        {
            if (SelectionSupportsAffixSection())
            {
                var primaryBinding = MultiEditPrimary() as BindingNode ?? FirstSelected<BindingNode>();
                if (primaryBinding != null)
                {
                    var affixSection = BuildAffixSection(primaryBinding, SelectionAffixCapabilities(),
                        test => EverySelected<BindingNode>(test));
                    if (affixSection != null) _propertiesContainer.Add(affixSection);
                }
            }

            if (SelectionSupportsValidationSection())
            {
                var primary = MultiEditPrimary();
                // The applicability of each rule is ANDed across the selection: a range row offered because the
                // PRIMARY is numeric would fan a min/max onto a string field that can never satisfy it.
                var section = BuildValidationSection(primary as BindingNode, primary as CollectionNode,
                    requiredAppliesOverride: EverySelected<LayoutNode>(ValidationRequiredApplies),
                    rangeAppliesOverride: EverySelected<LayoutNode>(ValidationRangeApplies));
                if (section != null) _propertiesContainer.Add(section);
            }

            // Conditionals last, and unconditional on kind — every LayoutNode has them. Still absent from the
            // Preset Editor for the reason the single-node call site gives: a preset has no target type, so a
            // condition has no field to point at.
            if (!_presetMode) _propertiesContainer.Add(BuildConditionalsSection());
        }

        /// <summary>Per-node twin of the "Is Required" gate in <c>BuildValidationSection</c>.</summary>
        private bool ValidationRequiredApplies(LayoutNode node)
        {
            if (node is CollectionNode) return true;          // a collection can always be empty
            if (!(node is BindingNode b)) return false;
            var t = ResolveSelectedNodeFieldType(b);
            return t == null || typeof(UnityEngine.Object).IsAssignableFrom(t) || t == typeof(string) || b.Required;
        }

        /// <summary>Per-node twin of the "Validate Range" gate in <c>BuildValidationSection</c>.</summary>
        private bool ValidationRangeApplies(LayoutNode node)
        {
            if (!(node is BindingNode b)) return false;        // collections have no range rule at all
            var t = ResolveSelectedNodeFieldType(b);
            return t == null || DisplayTypeCatalog.IsSliderNumeric(t) || b.ValidateRange;
        }

        /// <summary>
        /// The warning a LIST-valued section owes a multi-selection. A list is ONE field to the mirror, so the
        /// first edit does not unify a row — it replaces each follower's entire list. Every other row in the
        /// panel can be undone by simply not touching it; this one destroys work the user cannot see from here,
        /// so it is stated up front with a count rather than discovered.
        /// <para>Silent on a single selection, and silent when the lists already agree.</para>
        /// </summary>
        private void AppendMultiEditListBanner(VisualElement host, ICollection<string> scope, string what)
        {
            if (host == null || !HasMultiSelection) return;
            if (MultiEditMirror.CountDifferingSettings(_selectedNodes, scope) == 0) return;

            int others = _selectedNodes.Count - 1;
            var box = new VisualElement();
            box.style.marginBottom = 6;
            box.style.paddingTop = 4;
            box.style.paddingBottom = 4;
            box.style.paddingLeft = 7;
            box.style.paddingRight = 6;
            box.style.borderLeftWidth = 2;
            box.style.borderLeftColor = DesignerPalette.ToastWarning;

            var text = new Label(
                $"These {_selectedNodes.Count} elements do not all have the same {what}. "
                + $"Editing here replaces the {what} on all of them — the other {others} "
                + $"{(others == 1 ? "element loses its own" : "elements lose their own")}.");
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.fontSize = 10f;
            text.style.color = DesignerPalette.ToastWarning;
            box.Add(text);

            host.Insert(0, box);
        }

        private static string MultiKindLabel(LayoutNode node)
        {
            switch (node)
            {
                case ContainerNode _: return "containers";
                case CollectionNode _: return "collections";
                case BindingNode _: return "fields";
                case ButtonNode _: return "buttons";
                case HelpBoxNode _: return "help boxes";
                case SpaceNode _: return "spacers";
                case LabeledDividerNode _: return "dividers";
                case ImageBannerNode _: return "images";
                case EmbeddedEditorNode _: return "embedded editors";
#if SASQ_CUSTOM_WIDGET
                case CustomWidgetNode _: return "custom widgets";
#endif
                case PropertyNode _: return "properties";
                default: return "nodes";
            }
        }

        /// <summary>The selection's shared type, phrased for a header ("Progress Bar", "Tabs"), or null.</summary>
        private static string MultiSubKindLabel(LayoutNode node)
        {
            switch (node)
            {
                case BindingNode b: return DisplayTypeCatalog.Format(b.DisplayType);
                case ContainerNode c: return ObjectNames.NicifyVariableName(c.Type.ToString());
                case CollectionNode col: return ObjectNames.NicifyVariableName(col.DisplayMode.ToString());
                default: return null;
            }
        }

        private LayoutNode MultiEditPrimary()
        {
            if (_selectedNode != null && _selectedNodes.Contains(_selectedNode)) return _selectedNode;
            return _selectedNodes.Count > 0 ? _selectedNodes[0] : null;
        }

        // ---- The two hand-built tiers -------------------------------------------------------------------------

        /// <summary>
        /// The panel for a selection that is NOT uniform: the rows that provably mean the same thing on every
        /// node in it. Mixed kinds still get the box model — it is the one thing every LayoutNode carries, and
        /// withholding it (which is what the old "select nodes of the SAME kind" placeholder did) turned a
        /// perfectly meaningful "pad all six of these" into an error message.
        /// </summary>
        private void BuildMultiEditPanel(MultiEditTier tier)
        {
            var primary = MultiEditPrimary();
            if (primary == null) return;

            string what = tier == MultiEditTier.MixedKinds ? "nodes of different kinds" : MultiKindLabel(primary);
            string scope = tier == MultiEditTier.MixedKinds
                ? "They share the box model below — spacing, background, borders — plus conditions and animations, which every element carries. Select nodes of the same kind for their own styling too."
                : "They share the rows below, including conditions, animations, prefix & suffix and validation. Select ones of the SAME type for every option that type offers.";

            _propertiesContainer.Add(BuildMultiEditHeader(
                $"Editing {_selectedNodes.Count} {what} together.", scope));

            // Paste Style to All — the Copy Style clipboard fanned across the selection (one undo step).
            _propertiesContainer.Add(BuildPasteStyleToAllButton(primary));

            if (tier == MultiEditTier.SameKind)
            {
                if (primary is BindingNode) BuildMultiEditBindingSection();
                else if (primary is ContainerNode) BuildMultiEditContainerSection();
                else if (primary is ButtonNode) BuildMultiEditButtonSection();
            }

            BuildMultiEditLayoutStyleSection(primary);

            // The sections that do not care what type the nodes are. Appended AFTER the box model so the panel
            // reads in the same order as the single-node one. These are the REAL single-node builders, so they
            // can still emit an Identity(...) row — the caller runs HideMultiEditIdentityRows last, after the
            // property search, for the same reason the top tier does.
            AppendMultiEditSharedSections();
        }

        private Button BuildPasteStyleToAllButton(LayoutNode primary)
        {
            var pasteBtn = new Button(() => PasteStyleToSelection(primary))
            {
                text = StyleClipboard.HasStyle
                    ? $"Paste Style to All ({StyleClipboard.SourceKindLabel})"
                    : "Paste Style to All",
                tooltip = StyleClipboard.HasStyle
                    ? "Apply the copied style to every selected node."
                    : "Copy a node's style first (right-click ▸ Copy Style)."
            };
            pasteBtn.SetEnabled(StyleClipboard.HasStyle);
            pasteBtn.style.height = 24;
            pasteBtn.style.marginBottom = 6;
            return pasteBtn;
        }

        private static VisualElement BuildMultiEditHeader(string headline, string detail)
        {
            var box = new VisualElement();
            box.style.paddingTop = 10;
            box.style.paddingBottom = 6;

            var title = new Label(headline);
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = DesignerPalette.TextHeader;
            box.Add(title);

            var sub = new Label(detail);
            sub.style.whiteSpace = WhiteSpace.Normal;
            sub.style.marginTop = 2;
            sub.style.fontSize = 10f;
            sub.style.color = DesignerPalette.TextDisabled;
            box.Add(sub);

            return box;
        }

        // ---- The top tier: the REAL panel, mirrored ------------------------------------------------------------

        /// <summary>
        /// Finishes the ordinary single-node panel for a top-tier multi-selection: states what is being edited,
        /// then strips every row that can only mean one node. Runs LAST — after the property search has had its
        /// pass — so identity stays hidden even while a search is filtering the panel.
        /// </summary>
        private void FinishMultiEditFullPanel()
        {
            if (_propertiesContainer == null) return;
            var primary = MultiEditPrimary();
            if (primary == null) return;

            string kind = MultiKindLabel(primary);
            string sub = MultiSubKindLabel(primary);
            int differing = MultiEditMirror.CountDifferingSettings(_selectedNodes);

            string detail = differing > 0
                ? $"Every row below sets all {_selectedNodes.Count}. Values shown are the last-clicked node's — {differing} setting{(differing == 1 ? "" : "s")} currently differ across the selection."
                : $"Every row below sets all {_selectedNodes.Count}. Their current values all match.";

            _propertiesContainer.Insert(0, BuildMultiEditHeader(
                sub == null
                    ? $"Editing {_selectedNodes.Count} {kind} together."
                    : $"Editing {_selectedNodes.Count} {kind} together — all {sub}.",
                detail + "\nIdentity (paths, labels, tooltips, methods) is per-node, so it's hidden here."));

            _propertiesContainer.Insert(1, BuildPasteStyleToAllButton(primary));

            HideMultiEditIdentityRows();
        }

        /// <summary>
        /// Strips every row that can only ever mean one node, then any section those rows emptied. Split out of
        /// <see cref="FinishMultiEditFullPanel"/> because the LOWER tiers now build real single-node sections
        /// too (Conditionals, Prefix &amp; Suffix, Validation) and owe the identity sweep for the same reason.
        /// Idempotent — a row already hidden is simply hidden again.
        /// </summary>
        private void HideMultiEditIdentityRows()
        {
            if (_propertiesContainer == null) return;

            foreach (var row in _propertiesContainer.Query<VisualElement>(className: IdentityRowClass).ToList())
                HideForMultiEdit(row);

            // A section whose every row was identity is now an empty titled box. Deepest-last first, so a
            // nested section that has just emptied counts against its parent in the same pass.
            var sections = _propertiesContainer.Query<Foldout>().ToList();
            for (int i = sections.Count - 1; i >= 0; i--)
            {
                var section = sections[i];
                if (section.ClassListContains(MultiHiddenClass)) continue;

                bool anyVisible = false;
                foreach (var child in section.contentContainer.Children())
                    if (!child.ClassListContains(MultiHiddenClass)) { anyVisible = true; break; }

                if (!anyVisible) HideForMultiEdit(section);
            }
        }

        private static void HideForMultiEdit(VisualElement element)
        {
            if (element == null) return;
            element.AddToClassList(MultiHiddenClass);
            element.style.display = DisplayStyle.None;
        }

        // ---- Shared LayoutStyle box-model rows (every node kind carries these) ---------------------------------

        private void BuildMultiEditLayoutStyleSection(LayoutNode primary)
        {
            var section = CreateFoldoutHeader("Style (All)");

            VisualElement ColorRow(string label, Func<LayoutStyle, OverridableColor> read, Func<LayoutStyle, OverridableColor, LayoutStyle> write)
            {
                var row = CreateOverridableColorElement(label,
                    () => read(primary.Style),
                    val => { foreach (var n in _selectedNodes) { if (n == null) continue; n.Style = write(n.Style, val); } },
                    RefreshPreview);
                MarkRowMixed(row, SelectionValuesDiffer(n => read(n.Style)));
                return row;
            }
            VisualElement FloatRow(string label, Func<LayoutStyle, OverridableFloat> read, Func<LayoutStyle, OverridableFloat, LayoutStyle> write)
            {
                var row = CreateOverridableFloatElement(label,
                    () => read(primary.Style),
                    val => { foreach (var n in _selectedNodes) { if (n == null) continue; n.Style = write(n.Style, val); } },
                    RefreshPreview);
                MarkRowMixed(row, SelectionValuesDiffer(n => read(n.Style)));
                return row;
            }

            // Twin of the single-edit Appearance gate: Background Color is invisible on a node that renders as
            // the opaque .sasq-group shell. Dropped here only when EVERY selected node is one — if even one can
            // still show it, the row does something and stays. No re-sync hook is needed: this panel is rebuilt
            // whenever the selection changes, and none of the toggles that flip the shell live in it.
            bool allSelectedSwallowBackground = _selectedNodes.Count > 0;
            foreach (var n in _selectedNodes)
                if (n == null || !LayoutTreeBuilder.RendersAsOpaqueGroupShell(n)) { allSelectedSwallowBackground = false; break; }
            if (!allSelectedSwallowBackground)
                section.Add(ColorRow("Background Color", s => s.backgroundColor, (s, v) => { s.backgroundColor = v; return s; }));
            section.Add(ColorRow("Border Color", s => s.borderColor, (s, v) => { s.borderColor = v; return s; }));
            section.Add(FloatRow("Border Width", s => s.borderWidth, (s, v) => { s.borderWidth = v; return s; }));
            // "Box Corner Radius" — the outer box radius (Style.borderRadius), named to match the single-edit
            // Appearance row and to stay distinct from the Display (All) widget radius + Button (All) radius below.
            section.Add(FloatRow("Box Corner Radius", s => s.borderRadius, (s, v) => { s.borderRadius = v; return s; }));

            (string, Func<OverridableFloat>, Action<OverridableFloat>)[] Sides(
                Func<LayoutStyle, OverridableFloat>[] reads,
                Func<LayoutStyle, OverridableFloat, LayoutStyle>[] writes)
            {
                var names = new[] { "Top", "Right", "Bottom", "Left" };
                var sides = new (string, Func<OverridableFloat>, Action<OverridableFloat>)[4];
                for (int i = 0; i < 4; i++)
                {
                    var read = reads[i];
                    var write = writes[i];
                    sides[i] = (names[i],
                        () => read(primary.Style),
                        val => { foreach (var n in _selectedNodes) { if (n == null) continue; n.Style = write(n.Style, val); } });
                }
                return sides;
            }

            section.Add(CreateSpacingControl("Margin", Sides(
                new Func<LayoutStyle, OverridableFloat>[] { s => s.marginTop, s => s.marginRight, s => s.marginBottom, s => s.marginLeft },
                new Func<LayoutStyle, OverridableFloat, LayoutStyle>[]
                {
                    (s, v) => { s.marginTop = v; return s; },
                    (s, v) => { s.marginRight = v; return s; },
                    (s, v) => { s.marginBottom = v; return s; },
                    (s, v) => { s.marginLeft = v; return s; }
                }), RefreshPreview));

            section.Add(CreateSpacingControl("Padding", Sides(
                new Func<LayoutStyle, OverridableFloat>[] { s => s.paddingTop, s => s.paddingRight, s => s.paddingBottom, s => s.paddingLeft },
                new Func<LayoutStyle, OverridableFloat, LayoutStyle>[]
                {
                    (s, v) => { s.paddingTop = v; return s; },
                    (s, v) => { s.paddingRight = v; return s; },
                    (s, v) => { s.paddingBottom = v; return s; },
                    (s, v) => { s.paddingLeft = v; return s; }
                }), RefreshPreview));

            _propertiesContainer.Add(section);
        }

        // Bindings of the same kind but DIFFERENT display types: the shared Display Type popup (honest
        // intersection of what every field accepts) plus the shared DisplayStyleData rows. Per-type option
        // sections can't apply here — unify the type and the panel promotes itself to the full option set.
        private void BuildMultiEditBindingSection()
        {
            var section = CreateFoldoutHeader("Display (All)");
            var primary = (BindingNode)((_selectedNode is BindingNode) ? _selectedNode : null) ?? FirstSelected<BindingNode>();
            if (primary == null) return;

            // Display Type — offered only when every selected field accepts more than Default.
            var pairs = new List<(DisplayType current, Type fieldType)>();
            foreach (var n in _selectedNodes)
            {
                if (n is BindingNode b) pairs.Add((b.DisplayType, ResolveSelectedNodeFieldType(b)));
            }
            var offered = DisplayTypeCatalog.OfferedIntersection(pairs);
            if (offered.Count > 1)
            {
                bool widgetMixed = SelectionValuesDiffer(n => ((BindingNode)n).DisplayType);

                // A mixed selection gets an em-dash entry rather than one node's kind pulled forward as if it
                // spoke for all of them. It is a display state, not a value: re-picking it is a no-op, and the
                // first real pick unifies the selection and drops it.
                var labels = new List<string>();
                if (widgetMixed) labels.Add("—");
                foreach (var d in offered) labels.Add(FormatDisplayType(d));

                int startIndex = widgetMixed
                    ? 0
                    : Mathf.Max(0, offered.IndexOf(primary.DisplayType));

                var widgetField = new PopupField<string>("Display Type", labels, startIndex);
                widgetField.tooltip = widgetMixed
                    ? "The selected fields use different widgets. Pick one to give them all the same widget — and the full option set for it."
                    : null;
                widgetField.RegisterValueChangedCallback(evt =>
                {
                    int index = widgetField.index - (widgetMixed ? 1 : 0);
                    if (index < 0 || index >= offered.Count) return; // the "—" placeholder
                    var chosen = offered[index];
                    FanOutEdit("Change Display Type", n =>
                    {
                        if (!(n is BindingNode b)) return;
                        b.DisplayType = chosen;
                        // Mirror the single-edit side effect: a fresh progress bar starts interactive.
                        if (chosen == DisplayType.ProgressBar) b.ProgressBarInteractive = true;
                    });
                    RefreshProperties(); // unifying the kind promotes the panel to the full option set
                });
                MarkRowMixed(widgetField, widgetMixed);
                section.Add(widgetField);
            }

            var accentRow = CreateOverridableColorElement("Accent Color",
                () => primary.DisplayStyleData.accent,
                val => { foreach (var n in _selectedNodes) { if (n is BindingNode b) { var ws = b.DisplayStyleData; ws.accent = val; b.DisplayStyleData = ws; } } },
                RefreshPreview);
            MarkRowMixed(accentRow, SelectionValuesDiffer(n => ((BindingNode)n).DisplayStyleData.accent));
            section.Add(accentRow);

            var radiusRow = CreateOverridableFloatElement("Corner Radius",
                () => primary.DisplayStyleData.cornerRadius,
                val => { foreach (var n in _selectedNodes) { if (n is BindingNode b) { var ws = b.DisplayStyleData; ws.cornerRadius = val; b.DisplayStyleData = ws; } } },
                RefreshPreview);
            MarkRowMixed(radiusRow, SelectionValuesDiffer(n => ((BindingNode)n).DisplayStyleData.cornerRadius));
            section.Add(radiusRow);

            // Dictionary table style + row frame. Offered when ANY selected binding renders the dictionary
            // table (the same ShowsDictionaryTable predicate the single-select panel and the quick-edit popup
            // ask), and hidden when NOTHING in the selection would be affected - the case the rows are pure
            // noise in. The fan-out below is harmless on a non-dictionary follower: its renderer never reads
            // the fields, exactly like the other mixed-selection rows here.
            if (_selectedNodes.Any(n => n is BindingNode b &&
                    DisplayTypeCatalog.ShowsDictionaryTable(b.DisplayType, ResolveSelectedNodeFieldType(b))))
            {
                var dictStyleRow = new EnumField("Dictionary Style", primary.DictionaryStyle);
                dictStyleRow.tooltip =
                    "The overall look of the Key / Value table (Table / Minimal / Ledger / Mapping). " +
                    "Independent of Row Frame.";
                dictStyleRow.RegisterValueChangedCallback(evt => FanOutEdit("Change Dictionary Style", n =>
                {
                    if (n is BindingNode b) b.DictionaryStyle = (DictionaryTableStyle)evt.newValue;
                }));
                MarkRowMixed(dictStyleRow, SelectionValuesDiffer(n => ((BindingNode)n).DictionaryStyle));
                section.Add(dictStyleRow);

                var dictFrameRow = new EnumField("Row Frame", primary.DictionaryRowSeparator);
                dictFrameRow.tooltip =
                    "The per-row surface, the same enum a List collection uses: None, Line (a hairline " +
                    "between rows) or Card (a tray per row with a left edge rail).";
                dictFrameRow.RegisterValueChangedCallback(evt => FanOutEdit("Change Dictionary Row Frame", n =>
                {
                    if (n is BindingNode b) b.DictionaryRowSeparator = (CollectionRowSeparator)evt.newValue;
                }));
                MarkRowMixed(dictFrameRow, SelectionValuesDiffer(n => ((BindingNode)n).DictionaryRowSeparator));
                section.Add(dictFrameRow);
            }

            // NOTE: DisplayStyleData.size is deliberately NOT exposed here — it is a no-op kept only for
            // serialization back-compat, and a "Widget Size" row collides with the Slider's real "Size" control.

            var fieldBgRow = CreateOverridableColorElement("Field Background",
                () => primary.FieldBackground,
                val => { foreach (var n in _selectedNodes) { if (n is BindingNode b) b.FieldBackground = val; } },
                RefreshPreview);
            MarkRowMixed(fieldBgRow, SelectionValuesDiffer(n => ((BindingNode)n).FieldBackground));
            section.Add(fieldBgRow);

            // Offered when ANY selected field can use it — a mixed selection still gets the row, and the fan-out
            // below is harmless on the ones that cannot (the renderer makes the flag inert for them). Hidden
            // only when NOTHING in the selection would be affected, which is the case the row is noise in.
            bool anySupportsLabelAbove = false;
            foreach (var n in _selectedNodes)
                if (n is BindingNode b && DisplayTypeCatalog.SupportsLabelAbove(b.DisplayType, ResolveSelectedNodeFieldType(b)))
                { anySupportsLabelAbove = true; break; }
            if (anySupportsLabelAbove)
            {
                var labelAboveField = new Toggle("Label Above Field");
                labelAboveField.tooltip = "Put the field's label on its own row above the field instead of in the inspector's left label column.";
                labelAboveField.value = primary.LabelAbove;
                labelAboveField.RegisterValueChangedCallback(evt => FanOutEdit("Toggle Label Above Field", n =>
                {
                    if (n is BindingNode b) b.LabelAbove = evt.newValue;
                }));
                MarkRowMixed(labelAboveField, SelectionValuesDiffer(n => ((BindingNode)n).LabelAbove));
                section.Add(labelAboveField);
            }

            _propertiesContainer.Add(section);
        }

        private void BuildMultiEditContainerSection()
        {
            var section = CreateFoldoutHeader("Container (All)");
            var primary = (ContainerNode)((_selectedNode is ContainerNode) ? _selectedNode : null) ?? FirstSelected<ContainerNode>();
            if (primary == null) return;

            var typeField = new EnumField("Container Type", primary.Type);
            typeField.RegisterValueChangedCallback(evt =>
            {
                // Through the shared migration, never a bare `c.Type =`. A raw fan-out left every container's
                // children in a shape the NEW type reads differently — sharpest going into Tabs, where the
                // builder renders child N as tab N, so a container of eight widgets becomes eight inert
                // one-item tabs and seven of every eight widgets vanish from the live inspector (and from
                // exported editors, which reproduce the model faithfully). A fan-out cannot sensibly raise
                // one dialog per node, so it takes the non-destructive KeepAsGroups default.
                var newKind = (ContainerType)evt.newValue;
                FanOutEdit("Change Container Type",
                    n => { if (n is ContainerNode c) ContainerTypeMigration.Apply(c, newKind); });
                RefreshHierarchy();
                RefreshProperties(); // unifying the type promotes the panel to the full option set
            });
            MarkRowMixed(typeField, SelectionValuesDiffer(n => ((ContainerNode)n).Type));
            section.Add(typeField);

            var tintRow = CreateOverridableColorElement("Section Tint",
                () => primary.SectionTintColor,
                val => { foreach (var n in _selectedNodes) { if (n is ContainerNode c) c.SectionTintColor = val; } },
                RefreshPreview);
            MarkRowMixed(tintRow, SelectionValuesDiffer(n => ((ContainerNode)n).SectionTintColor));
            section.Add(tintRow);

            var stripeRow = CreateOverridableColorElement("Accent Stripe",
                () => primary.AccentStripeColor,
                val => { foreach (var n in _selectedNodes) { if (n is ContainerNode c) c.AccentStripeColor = val; } },
                RefreshPreview);
            stripeRow.tooltip = "A color rail down the section's left edge only (no tinted header/body). If Section Tint is also set, Section Tint takes over.";
            MarkRowMixed(stripeRow, SelectionValuesDiffer(n => ((ContainerNode)n).AccentStripeColor));
            section.Add(stripeRow);

            // Tab accent applies only when the WHOLE selection is Tabs containers.
            bool allTabs = true;
            foreach (var n in _selectedNodes)
            {
                if (!(n is ContainerNode c) || c.Type != ContainerType.Tabs) { allTabs = false; break; }
            }
            if (allTabs)
            {
                var tabAccentField = new ColorField("Active Tab Color");
                tabAccentField.tooltip = "Header tint for the selected tab. The other tabs stay neutral grey.";
                tabAccentField.value = primary.TabAccentColor;
                tabAccentField.RegisterValueChangedCallback(evt => FanOutEdit("Change Active Tab Color", n =>
                {
                    if (n is ContainerNode c) c.TabAccentColor = evt.newValue;
                }));
                MarkRowMixed(tabAccentField, SelectionValuesDiffer(n => ((ContainerNode)n).TabAccentColor));
                section.Add(tabAccentField);
            }

            // Default to Open / Expandable — mirror single-edit's gating so we don't expose a no-op control for
            // Tabs/Box: Foldout always has Default to Open; Rows/Columns/Grid/Card get an Expandable toggle that
            // reveals Default to Open; other (or mixed) container types get neither.
            bool allFoldout = true, allMinimizable = true;
            foreach (var n in _selectedNodes)
            {
                if (!(n is ContainerNode c)) { allFoldout = allMinimizable = false; break; }
                if (c.Type != ContainerType.Foldout) allFoldout = false;
                bool canMin = c.Type == ContainerType.Rows || c.Type == ContainerType.Columns
                           || c.Type == ContainerType.Grid || c.Type == ContainerType.Card;
                if (!canMin) allMinimizable = false;
            }

            if (allFoldout)
            {
                var startExpandedField = new Toggle("Default to Open");
                startExpandedField.value = primary.DefaultToOpen;
                startExpandedField.RegisterValueChangedCallback(evt => FanOutEdit("Change Default to Open", n =>
                {
                    if (n is ContainerNode c) c.DefaultToOpen = evt.newValue;
                }));
                MarkRowMixed(startExpandedField, SelectionValuesDiffer(n => ((ContainerNode)n).DefaultToOpen));
                section.Add(startExpandedField);
            }
            else if (allMinimizable)
            {
                var minimizableField = new Toggle("Expandable");
                minimizableField.tooltip = "Allow collapsing/expanding this container by clicking its header.";
                minimizableField.value = primary.Expandable;
                minimizableField.RegisterValueChangedCallback(evt =>
                {
                    FanOutEdit("Toggle Expandable Container", n => { if (n is ContainerNode c) c.Expandable = evt.newValue; });
                    RefreshProperties(); // reveal/hide the indented Default to Open row
                });
                MarkRowMixed(minimizableField, SelectionValuesDiffer(n => ((ContainerNode)n).Expandable));
                section.Add(minimizableField);

                if (primary.Expandable)
                {
                    var startExpandedField = new Toggle("Default to Open");
                    startExpandedField.tooltip = "If expandable, should this container open by default?";
                    startExpandedField.style.marginLeft = 15;
                    startExpandedField.value = primary.DefaultToOpen;
                    startExpandedField.RegisterValueChangedCallback(evt => FanOutEdit("Change Container Default to Open", n =>
                    {
                        if (n is ContainerNode c) c.DefaultToOpen = evt.newValue;
                    }));
                    MarkRowMixed(startExpandedField, SelectionValuesDiffer(n => ((ContainerNode)n).DefaultToOpen));
                    section.Add(startExpandedField);
                }
            }

            // Title text style — the shared "T" editor, fanned out.
            var titleStyleLabel = new Label("Title Text");
            titleStyleLabel.style.flexGrow = 1;
            section.Add(WithTextButton(titleStyleLabel,
                () => primary.TitleTextStyle,
                v => { foreach (var n in _selectedNodes) { if (n is ContainerNode c) c.TitleTextStyle = v; } },
                "title"));

            _propertiesContainer.Add(section);
        }

        private void BuildMultiEditButtonSection()
        {
            var section = CreateFoldoutHeader("Button (All)");
            var primary = (ButtonNode)((_selectedNode is ButtonNode) ? _selectedNode : null) ?? FirstSelected<ButtonNode>();
            if (primary == null) return;

            var presetField = new EnumField("Button Style", primary.StylePreset);
            presetField.tooltip = "Visual preset. Classic is Unity's stock button chrome; the other presets draw from the accent color.";
            presetField.RegisterValueChangedCallback(evt => FanOutEdit("Change Button Style", n =>
            {
                if (n is ButtonNode b) b.StylePreset = (ButtonStyle)evt.newValue;
            }));
            MarkRowMixed(presetField, SelectionValuesDiffer(n => ((ButtonNode)n).StylePreset));
            section.Add(presetField);

            var accentRow = CreateOverridableColorElement("Accent Color",
                () => primary.AccentColor,
                val => { foreach (var n in _selectedNodes) { if (n is ButtonNode b) b.AccentColor = val; } },
                RefreshPreview);
            MarkRowMixed(accentRow, SelectionValuesDiffer(n => ((ButtonNode)n).AccentColor));
            section.Add(accentRow);

            var radiusRow = CreateOverridableFloatElement("Corner Radius",
                () => primary.CornerRadius,
                val => { foreach (var n in _selectedNodes) { if (n is ButtonNode b) b.CornerRadius = val; } },
                RefreshPreview);
            MarkRowMixed(radiusRow, SelectionValuesDiffer(n => ((ButtonNode)n).CornerRadius));
            section.Add(radiusRow);

            var sizeField = MakeButtonSizeField("Size", primary.Size);
            sizeField.RegisterValueChangedCallback(evt => FanOutEdit("Change Button Size", n =>
            {
                if (n is ButtonNode b) b.Size = evt.newValue;
            }));
            MarkRowMixed(sizeField, SelectionValuesDiffer(n => ((ButtonNode)n).Size));
            section.Add(sizeField);

            // Custom Height — exact pixels, overriding the preset above. 0 = follow the preset (the ↺).
            // No live poke here: the fan-out writes every selected node, and the burst's per-tick
            // LiveRebuildSelectedNodes already repaints the whole selection.
            var heightSlider = new Slider("Custom Height", 0f, ButtonNode.MaxCustomHeight) { showInputField = true };
            heightSlider.tooltip = "Exact button height in pixels, overriding the Size preset. Drag it below " +
                $"{ButtonNode.MinCustomHeight:0} to hand the height back to the preset (0 = follow the preset).";
            var heightRow = CreateResettableNumberField<float>(
                heightSlider, 0f,
                () => primary.CustomHeight,
                val => { foreach (var n in _selectedNodes) { if (n is ButtonNode b) b.CustomHeight = val; } },
                v => v < ButtonNode.MinCustomHeight ? 0f : Mathf.Min(v, ButtonNode.MaxCustomHeight),
                "Change Button Height");
            MarkRowMixed(heightRow, SelectionValuesDiffer(n => ((ButtonNode)n).CustomHeight));
            section.Add(heightRow);

            var hoverAnimField = new EnumField("Hover Animation", primary.HoverAnimation);
            hoverAnimField.tooltip = "The hover/press motion this button plays. Bounce is the default; None disables the motion; Lift/Grow/Pop are alternative feels. Hover the button in the live preview (Interact mode) to try it.";
            hoverAnimField.RegisterValueChangedCallback(evt => FanOutEdit("Change Button Hover Animation", n =>
            {
                if (n is ButtonNode b) b.HoverAnimation = (ButtonHoverAnimation)evt.newValue;
            }));
            MarkRowMixed(hoverAnimField, SelectionValuesDiffer(n => ((ButtonNode)n).HoverAnimation));
            section.Add(hoverAnimField);

            var hoverColorRow = CreateOverridableColorElement("Hover Color",
                () => primary.HoverColor,
                val => { foreach (var n in _selectedNodes) { if (n is ButtonNode b) b.HoverColor = val; } },
                RefreshPreview);
            hoverColorRow.tooltip = "Background color while hovered; unset keeps the built-in subtle lighten.";
            MarkRowMixed(hoverColorRow, SelectionValuesDiffer(n => ((ButtonNode)n).HoverColor));
            section.Add(hoverColorRow);

            var pressedColorRow = CreateOverridableColorElement("Pressed Color",
                () => primary.PressedColor,
                val => { foreach (var n in _selectedNodes) { if (n is ButtonNode b) b.PressedColor = val; } },
                RefreshPreview);
            pressedColorRow.tooltip = "Background color while pressed (pointer down); unset keeps the spring dip only.";
            MarkRowMixed(pressedColorRow, SelectionValuesDiffer(n => ((ButtonNode)n).PressedColor));
            section.Add(pressedColorRow);

            var textBackdropField = new Toggle("Text Backdrop");
            textBackdropField.tooltip = "Sit the label — and the icon — on a soft dark pill inside the button, for guaranteed readability over any accent color.";
            textBackdropField.value = primary.TextBackdrop;
            textBackdropField.RegisterValueChangedCallback(evt => FanOutEdit("Toggle Button Text Backdrop", n =>
            {
                if (n is ButtonNode b) b.TextBackdrop = evt.newValue;
            }));
            MarkRowMixed(textBackdropField, SelectionValuesDiffer(n => ((ButtonNode)n).TextBackdrop));
            section.Add(textBackdropField);

            var fullWidthField = new Toggle("Full Width");
            fullWidthField.tooltip = "Stretch across the row (the default) or hug the button's content.";
            fullWidthField.value = primary.FullWidth;
            fullWidthField.RegisterValueChangedCallback(evt => FanOutEdit("Toggle Button Full Width", n =>
            {
                if (n is ButtonNode b) b.FullWidth = evt.newValue;
            }));
            MarkRowMixed(fullWidthField, SelectionValuesDiffer(n => ((ButtonNode)n).FullWidth));
            section.Add(fullWidthField);

            _propertiesContainer.Add(section);
        }

        private T FirstSelected<T>() where T : LayoutNode
        {
            foreach (var n in _selectedNodes)
            {
                if (n is T t) return t;
            }
            return null;
        }
    }
}
