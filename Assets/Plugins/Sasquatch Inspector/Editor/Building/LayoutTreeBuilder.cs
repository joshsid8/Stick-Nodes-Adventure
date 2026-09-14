using System;
using System.Collections.Generic;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds the UI Toolkit element tree for a layout — the shared renderer used by both the live inspector
    /// and the designer's preview/canvas.
    /// </summary>
    public static partial class LayoutTreeBuilder
    {
        // Hard safety cap on rows built in a single pass when a collection's Pagination is OFF. Building every
        // row of a multi-thousand-item list eagerly freezes the editor; above this we build the first N and show
        // a note pointing at the Pagination toggle. (With pagination ON — the default — PageSize governs instead.)
        private const int HardMaxUnpagedRows = 500;

        // ── Container title alignment ─────────────────────────────────────────────────────────────────────
        // Every container title lands on ONE of two left edges, and its OWN content lands on the same one, so a
        // stack of mixed containers reads as two deliberate columns instead of five ragged ones:
        //
        //   FLUSH  — Rows / Columns / Grid / Tabs. Title and fields both sit at the container's left edge.
        //   INSET  — Card / Foldout / the collapsible .sasq-group shell. Both sit SectionGutter px in.
        //
        // SectionGutter is not arbitrary: it is exactly the gutter Unity's own Foldout arrow already occupies
        // (a stock foldout's text box lands at 19px = this 18px box edge + the 1px padding every bare Label
        // carries), so the disclosure containers keep their native look and everything else lines up with them.
        // Change it and BOTH .sasq-group-header/-content in SasquatchWidgets.uss and SasqKit.Group in
        // ExportedWidgetKit.cs have to move with it — pinned by TestContainerTitlesShareTwoLeftEdges.
        //
        // Frames are deliberately EXCLUDED: a shaped clip's horizontal inset is computed per shape by the chord
        // fit, so a fixed gutter would either crop the title or float it. A frame title tracks its own content
        // instead (FrameBuilder.MakeTitleLabel).
        internal const float SectionGutter = 18f;

        // One ink for every container title, whatever renders it — the flow header, the Card label, the group
        // shell's header and Unity's Foldout text. (.sasq-group-title reads the matching --sasq-text-node-title
        // token, so the USS-driven titles land on the same value.)
        private static Color TitleInk => WidgetPalette.LabelBright;

        // Gap under a title that sits directly above its own content.
        private const float TitleGap = 4f;

        // ── Tab content panel ─────────────────────────────────────────────────────────────────────────────
        // A Tabs container used to end at its strip: the open tab's children were dropped into a bare, unstyled
        // VisualElement directly below. Nothing said they belonged to the lit tab, so a stack of Cards under
        // "Stats" read exactly like siblings that merely came AFTER the tabs — the reader had no way to tell
        // where the tab's content started or, worse, where it STOPPED.
        //
        // The fix is the folder-tab reading the designer's own left tab bar already uses (ApplyLeftTabVisual):
        // make the tab the top of the panel instead of a control pointing at it. Here the active tab is an
        // ACCENT-FILLED button rather than a panel-coloured one, so the join is made in the accent: the panel's
        // top edge is a 2px rail in the SAME colour the active tab is filled with, the strip sits flush on it
        // (no hairline, no gap), and the two become one continuous shape. A bottom edge in the same hue at
        // lower volume closes it, which is what gives the content a visible END.
        //
        // The panel is bounded TOP AND BOTTOM ONLY — deliberately, after seeing it on screen. Side edges were
        // tried and removed: a Card carries its own 3px section rail on its left, so the panel's left edge
        // landed flush against it with ZERO gap — measured #385880 at x7-8 then #2E7AD9 at x9-11, five pixels
        // of blue in two shades reading as one smudged double-line, and worse on a teal-tinted section where
        // muted blue butted straight into cyan. Nothing could separate them: the two-left-edges rule (see
        // TabPanelPadH) forbids insetting the content, and widening the negative margin would have pushed the
        // frame off the inspector's right edge. Losing the sides costs little — the rail is what ties content
        // to the lit tab, the bottom edge is what ends it — and it buys back the negative horizontal margins,
        // which were the one part of this that could clip inside a Frame's shaped overflow.
        //
        // TWIN: SasqKit.TabStrip in ExportedWidgetKit.cs bakes these same numbers. Change both, or an exported
        // inspector quietly loses the containment the live one has — pinned by TestTabContentPanelIsContained
        // and TestExportedTabPanelMatchesLive.
        private const float TabPanelRail = 2f;      // top edge, painted in the active tab's colour
        // The bottom edge matches the rail's thickness. At 1px in a neutral grey it measured RGB 77 on a 56
        // background — technically present, too discreet to end anything. Same width as the rail is what makes
        // the two read as one frame rather than a rail with a hairline somewhere below it.
        private const float TabPanelEdge = 2f;
        /// <summary>Alpha the rail's colour is dropped to for the other three sides. The box is the SAME
        /// colour all the way round — the sides are simply the rail continuing at lower volume, which is what
        /// keeps the frame tied to the lit tab instead of being a neutral rectangle drawn near it. Deriving
        /// it from the accent (rather than a fixed grey) also means a custom TabAccentColor tints the whole
        /// frame for free.</summary>
        private const float TabPanelEdgeAlpha = 0.42f;
        // SQUARE, all four. This went round twice, so the reasoning is worth keeping.
        //
        // A rounded top was tried and it frayed the one join this whole feature exists to make. With no side
        // borders, a corner radius has nothing to curve INTO — it just eats the ends of the rail. Measured at
        // 4px: the active tab's fill started at x=9 while the rail only reached full strength at x=12, so a
        // 3px notch of bare inspector background sat under the tab's bottom-left corner. The rail visibly
        // stopped short of the tab it is supposed to be continuous with.
        //
        // Rounding only the BOTTOM would fix that and was the first version, but two round corners under two
        // hard ones reads as a box someone forgot to finish — and the bottom edge frays at its own ends for
        // exactly the same reason, just where nothing draws attention to it.
        //
        // So: no radius anywhere. The rail and the bottom edge each run the full width, flush, and the panel
        // is symmetric. A radius only ever made sense while this thing was a closed box with sides.
        private const float TabPanelRadius = 0f;
        private const float TabPanelPadV = 8f;      // breathing room under the rail
        private const float TabPanelPadVBottom = 6f;
        // The panel costs ZERO horizontal space, and that is a hard constraint, not a taste call. Tabs is in
        // the FLUSH tier of the two-left-edges rule (see TestContainerTitlesShareTwoLeftEdges): its title and
        // its fields both sit on the container's own left edge. The title lives ABOVE the strip, outside this
        // panel, so any padding or border here moves the FIELDS and nothing else — 5px of padding plus a 2px
        // border put them 7px off their own heading, which the alignment test caught immediately.
        //
        // So: no horizontal padding, and the side borders are pulled back out with a negative margin of their
        // own width. The panel's CONTENT box then lines up exactly with the container's, and the frame is
        // drawn just outside it, in space the parent's own gutter already owns.
        private const float TabPanelPadH = 0f;

        // ── The size cue on the tab buttons themselves ──────────────────────────────────────────────────
        //
        // The active tab is TALLER than its neighbours, and it grows UPWARD only. That direction is the whole
        // trick, and it is why this is safe where the old 0.86 scale was not:
        //
        //   * The strip is `align-items: flex-end`, so every tab's BOTTOM edge stays pinned on the panel's
        //     rail no matter how tall it is. The join the panel exists to make is untouched.
        //   * Nothing changes WIDTH. A wider active tab — or a bold swap, or a bigger font — re-measures the
        //     button, and with tabs butting together that shoves every neighbour sideways on each click. Only
        //     the vertical paddings move here, so the strip's horizontal geometry is frozen.
        //   * It is real LAYOUT, not a transform. The old cue scaled the button, which both re-opened the 1px
        //     seams that butt the tabs together and offset `worldBound` mid-flight, skewing the drop-slot
        //     geometry the designer's spring-load-while-dragging path measures. Padding does neither: the
        //     button's box genuinely is that size, so everything that measures it measures the truth.
        //
        // TWIN: SasqKit.TabStrip in ExportedWidgetKit.cs bakes these same numbers — pinned by
        // TestExportedTabPanelMatchesLive and TestTabsShowcaseScaleTreatment.
        private const float TabPadH = 10f;
        private const float TabPadTop = 4f;         // a resting, inactive tab
        private const float TabPadBottom = 4f;
        /// <summary>How much taller the active tab is. Split evenly across its two vertical paddings, so the
        /// label re-centres in the taller box and rides up with it — a taller box whose label stayed put
        /// reads as a misaligned tab rather than a raised one.</summary>
        private const float TabActiveGrow = 4f;
        /// <summary>An inactive tab meets you halfway on hover. Added to the TOP padding only, so the fill
        /// grows up toward the active tab's height while the label itself does not budge — the tab reaches
        /// for you, the text stays still.
        ///
        /// Deliberately LESS than <see cref="TabActiveGrow"/>, and not only so hover cannot impersonate
        /// selection: the strip's height is set by its tallest tab, which is always the active one, so a
        /// hover lift that never reaches that height cannot change the strip's height at all. Nothing below
        /// the strip moves — not the panel, not a single field, not the drop slots the designer measured at
        /// the start of a drag. The same arithmetic makes a tab SWITCH free: the incoming tab gains exactly
        /// what the outgoing one gives up, so the maximum never moves either.</summary>
        private const float TabHoverGrow = 2f;

        /// <summary>The vertical size cue, applied wherever a tab's state changes: build, activate, hover.
        /// One function so the three sites cannot drift — the hover restore in particular has to land on the
        /// exact resting value or a tab that has been moused over stays permanently 2px tall.</summary>
        internal static void ApplyTabButtonSize(VisualElement tabButton, bool selected, bool hovered)
        {
            tabButton.style.paddingLeft = TabPadH;
            tabButton.style.paddingRight = TabPadH;
            if (selected)
            {
                tabButton.style.paddingTop = TabPadTop + TabActiveGrow * 0.5f;
                tabButton.style.paddingBottom = TabPadBottom + TabActiveGrow * 0.5f;
                return;
            }
            tabButton.style.paddingTop = TabPadTop + (hovered ? TabHoverGrow : 0f);
            tabButton.style.paddingBottom = TabPadBottom;
        }

        /// <summary>Eases the fill, the ink and the height between tab states instead of snapping them. UI
        /// Toolkit transitions rather than a spring on purpose: a spring drives `transform`, and a transform
        /// on a tab flush against its neighbours is exactly what the size cue above had to avoid. Transitions
        /// animate the real padding, so the button's box is honest at every frame of the fade.
        ///
        /// Duration 0 when the user has turned spring animations off — the properties still change, they just
        /// arrive instantly, which is what "no motion" has to mean.</summary>
        internal static void ApplyTabButtonMotion(VisualElement tabButton, bool motionEnabled)
        {
            tabButton.style.transitionProperty = new List<StylePropertyName>
            {
                new StylePropertyName("background-color"),
                new StylePropertyName("color"),
                new StylePropertyName("padding-top"),
                new StylePropertyName("padding-bottom"),
                new StylePropertyName("border-right-color"),
            };
            float fade = motionEnabled ? 0.12f : 0f;
            float lift = motionEnabled ? 0.16f : 0f;
            tabButton.style.transitionDuration = new List<TimeValue>
            {
                new TimeValue(fade, TimeUnit.Second),
                new TimeValue(fade, TimeUnit.Second),
                new TimeValue(lift, TimeUnit.Second),
                new TimeValue(lift, TimeUnit.Second),
                new TimeValue(fade, TimeUnit.Second),
            };
            // Ease-OUT everywhere: the cue should arrive fast and settle, never creep in. EaseOutCubic on the
            // height in particular is what keeps the lift from reading as a slow stretch.
            tabButton.style.transitionTimingFunction = new List<EasingFunction>
            {
                new EasingFunction(EasingMode.EaseOutSine),
                new EasingFunction(EasingMode.EaseOutSine),
                new EasingFunction(EasingMode.EaseOutCubic),
                new EasingFunction(EasingMode.EaseOutCubic),
                new EasingFunction(EasingMode.EaseOutSine),
            };
        }

        /// <summary>The colour a Tabs container lights its active tab with. Alpha 0 means the layout predates
        /// the per-container setting, so it falls back to the palette blue. Read by BOTH the tab buttons and
        /// the content panel's rail — they must never resolve it differently or the join comes apart.</summary>
        internal static Color ResolveTabAccent(ContainerNode containerNode)
        {
            return containerNode.TabAccentColor.a > 0f
                ? containerNode.TabAccentColor
                : WidgetPalette.TabSelectedFallback;
        }

        /// <summary>The rail colour dropped to <see cref="TabPanelEdgeAlpha"/> for the panel's bottom edge.
        /// Alpha only — the hue has to stay the rail's, or the two stop reading as one frame.</summary>
        internal static Color TabPanelEdgeColor(Color accent)
        {
            return new Color(accent.r, accent.g, accent.b, accent.a * TabPanelEdgeAlpha);
        }

        /// <summary>Dresses the open tab's content host as a bounded panel hanging off the strip. See the
        /// block comment above for why each edge is what it is.</summary>
        internal static void StyleTabPanel(VisualElement panel, Color accent)
        {
            // Tuck the rail UP under the strip by its own thickness. Tab buttons carry marginBottom 2 (row
            // spacing for when the strip wraps), which left a 2px sliver of inspector background between the
            // active tab and the rail — small, but it is the ONE seam that has to be invisible: a gap there
            // reads as "tab" and "bar", not as one shape. Pulling the panel up closes it for every tab at
            // once, where a negative margin on the active BUTTON would have to be added and removed on every
            // ActivateTab. The house precedent is ApplyLeftTabVisual, which
            // overhangs the active designer tab by -1 for exactly this reason.
            panel.style.marginTop = -TabPanelRail;
            // NO side frames, and therefore no horizontal margins to compensate for. The panel is bounded
            // top and bottom only. See the block comment above for why the sides had to go.
            panel.style.marginLeft = 0;
            panel.style.marginRight = 0;
            panel.style.backgroundColor = WidgetPalette.TabPanelBg;
            panel.style.borderTopWidth = TabPanelRail;
            panel.style.borderTopColor = accent;
            panel.style.borderLeftWidth = 0;
            panel.style.borderRightWidth = 0;
            panel.style.borderBottomWidth = TabPanelEdge;
            panel.style.borderBottomColor = TabPanelEdgeColor(accent);
            panel.style.borderTopLeftRadius = TabPanelRadius;
            panel.style.borderTopRightRadius = TabPanelRadius;
            panel.style.borderBottomLeftRadius = TabPanelRadius;
            panel.style.borderBottomRightRadius = TabPanelRadius;
            panel.style.paddingTop = TabPanelPadV;
            panel.style.paddingBottom = TabPanelPadVBottom;
            panel.style.paddingLeft = TabPanelPadH;
            panel.style.paddingRight = TabPanelPadH;
        }

        /// <summary>Takes <see cref="StyleTabPanel"/> back off. A Tabs container with nothing built has no
        /// content to enclose, and an empty box with a 2px accent bar across it is worse than no panel at
        /// all — it reads as a broken widget rather than an empty one.</summary>
        internal static void ClearTabPanel(VisualElement panel)
        {
            panel.style.marginTop = 0;
            panel.style.marginLeft = 0;
            panel.style.marginRight = 0;
            panel.style.backgroundColor = Color.clear;
            panel.style.borderTopWidth = 0;
            panel.style.borderLeftWidth = 0;
            panel.style.borderRightWidth = 0;
            panel.style.borderBottomWidth = 0;
            panel.style.borderTopLeftRadius = 0;
            panel.style.borderTopRightRadius = 0;
            panel.style.borderBottomLeftRadius = 0;
            panel.style.borderBottomRightRadius = 0;
            panel.style.paddingTop = 0;
            panel.style.paddingBottom = 0;
            panel.style.paddingLeft = 0;
            panel.style.paddingRight = 0;
        }

        // ── Section rhythm ────────────────────────────────────────────────────────────────────────────────
        // The ONE gap a SECTION — a container or a collection — leaves below itself when another child follows
        // it in a vertical stack. Before this the gap depended entirely on which KIND of section it was:
        // Card 6, the collapsible .sasq-group shell 8 (plus 4 ABOVE it), Frame / Rows / Columns / Grid /
        // Foldout / Tabs 0. A Frame sitting above a Card therefore butted straight into it while two Cards in
        // the very same stack were spaced — and only in the live inspector, because the designer canvas gives
        // every card one uniform margin (see the `element.style.marginBottom = 8` in the design-card block),
        // which is exactly why the break was invisible while authoring.
        //
        // Applied by the PARENT (ApplySectionGaps) rather than by each section on itself: only the parent knows
        // sibling order, so the LAST section in a stack can leave no trailing dead space inside its container —
        // a self-margin compounds one dead gap per nesting level at the bottom of every Card. It runs BEFORE
        // ApplyGap so a container's own "Spacing" still adds on top, exactly how the Card's hard-coded 6 did.
        // The export emitter carries a twin (LayoutSourceEmitter's children loop) — keep the two in lockstep.
        internal const float SectionGap = 6f;

        // Extra selected nodes (multi-selection) highlighted on the canvas alongside the primary
        // selectedNode. Set for the duration of a Build; the editor runs builds synchronously.
        private static System.Collections.Generic.HashSet<LayoutNode> _selectionSet;

        // When true, an empty collection with an ItemLayout renders a dimmed scaffolding row so the
        // item template stays visible/selectable/designable on the canvas (design mode only).
        // Set for the duration of a Build, mirroring _selectionSet.
        private static bool _showItemTemplates = true;

        // When true, Appear-trigger animations play this build; callers pass false on rebuilds so the
        // appear tween doesn't replay/flicker on every undo/value-change. Set for the duration of a Build.
        private static bool _playAppearAnimations = true;

        // Invoked when the user edits the LAYOUT itself through the rendered UI (a table column resize, a
        // text-box resize), so the host can persist it: the designer marks dirty + re-renders, the real
        // inspector SetDirty's the shared layout asset. Set for the duration of a Build (save/restore).
        private static Action _onLayoutEdited;

        // Read by BindingFactory at BUILD time (while a Build is in progress) to capture the persist callback
        // into resize-handle closures — the static itself is restored once Build returns.
        internal static Action CurrentOnLayoutEdited => _onLayoutEdited;

        // When true, the Condition Debugger overlay mode is active: nodes that conditions would hide/disable are
        // revealed in place with a ConditionDebugOverlay instead of being collapsed. Set for the duration of a
        // Build (save/restore), consumed only at the single ConditionBinder.Bind call site.
        private static bool _debugConditions;

        // True while building the DESIGNER's authoring preview (vs the real inspector). Set for the duration of a
        // Build (save/restore). Read by BindingFactory to skip work that's pointless/expensive in the constantly-
        // rebuilt authoring pane — e.g. the Asset Preview's render-and-capture warm-up (which otherwise re-renders
        // a PreviewRenderUtility on every keystroke as the preview rebuilds).
        private static bool _designMode;

        /// <summary>
        /// True while building a REAL Inspector (the fallback or the displacement path), as opposed to a
        /// design canvas, a Showcase pane, a tutorial preview or an export. The gate on Live Debug — see
        /// where <c>EnabledForBuild</c> is derived in <see cref="Build"/> for why it is an opt-in.
        /// </summary>
        private static bool _liveInspector;
        internal static bool CurrentDesignMode => _designMode;

        // The bindings that duplicate an enclosing toggle-group Foldout's header checkbox (see GroupToggleMirror).
        // Derived from the tree once per Build and set for its duration (save/restore, mirroring _selectionSet);
        // null — not empty — whenever the layout has none, which is nearly every layout. Live builds SKIP these
        // nodes outright; the designer canvas dims them and pips them instead.
        private static System.Collections.Generic.HashSet<LayoutNode> _toggleMirrors;

        // Attribute-owned Space widgets whose field has been hidden (see HiddenNodeMark.CollectOrphanedSpacers).
        // Same lifecycle as _toggleMirrors above — derived once per Build, saved/restored, null when there are
        // none. Live builds skip them so hiding a field takes its imported [Space] gap with it; the designer
        // canvas keeps them, because the spacer itself is a real node the author can still select.
        private static System.Collections.Generic.HashSet<LayoutNode> _orphanedSpacers;

        // ── Single-node live rebuild ──────────────────────────────────────────────────────────────────────
        // The designer's "live poke" pattern shows instant canvas feedback while a continuous control is dragged,
        // without the full-tree RefreshPreview teardown. Most controls map to a cheap in-place style poke, but the
        // long tail (values baked into captured build locals, painted widgets, structural/multi-element changes)
        // has no clean poke. For those, RebuildNodeInPlace rebuilds JUST the dragged node's element: cheaper than
        // the whole tree (~1/N), and it reproduces binding/conditions/drag/design-card exactly because it re-runs
        // the SAME BuildNode. Rebuilding one node needs the exact args its original build used — trickiest being
        // basePath (collection item indices / column-cell scoping can't be re-derived from the model alone), so we
        // CAPTURE them at build time, keyed weakly by the built element. Only the designer opts in (via Build's
        // captureNodeContexts) — the shipping inspector never pays for the capture.
        private static bool _captureNodeContexts;

        private sealed class NodeRebuildContext
        {
            public LayoutNode node;
            public string basePath;
            public string nodePath;
            public SerializedObject serializedObject;
            public Action onRebuildRequired;
            public SasquatchThemeAsset theme;
            public bool designMode;
            public LayoutNode selectedNode;
            public CanvasDragController dragController;
            public InspectorLayoutAsset layoutAsset;
            public bool designInteract;
            public bool columnCell;
            public bool parentColumnsHasAnyHeader;
            public bool directChildrenAreCells;
            public bool tableCell;
            // Per-build statics that BuildNode reads, snapshotted so a later rebuild lands on identical context.
            public System.Collections.Generic.HashSet<LayoutNode> selectionSet;
            public bool showItemTemplates;
            public Action onLayoutEdited;
            public bool debugConditions;
            public bool? buildScopeIsLight;
            public System.Collections.Generic.HashSet<LayoutNode> toggleMirrors;
            public System.Collections.Generic.HashSet<LayoutNode> orphanedSpacers;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, NodeRebuildContext> _rebuildContexts =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, NodeRebuildContext>();

        /// <summary>
        /// Builds the inspector/preview element tree for <paramref name="layoutAsset"/> against
        /// <paramref name="serializedObject"/>, wiring rebuild and layout-edit callbacks, optional design-mode
        /// interactivity, and appear animations. The shared entry point for the inspector and the designer.
        /// </summary>
        public static VisualElement Build(
            InspectorLayoutAsset layoutAsset,
            SerializedObject serializedObject,
            Action onRebuildRequired,
            SasquatchThemeAsset theme = null,
            bool designMode = false,
            LayoutNode selectedNode = null,
            CanvasDragController dragController = null,
            InspectorLayoutAsset rootAssetRef = null,
            bool designInteract = false,
            System.Collections.Generic.HashSet<LayoutNode> selectedNodes = null,
            bool showItemTemplates = true,
            bool playAppearAnimations = true,
            Action onLayoutEdited = null,
            bool debugConditions = false,
            bool captureNodeContexts = false,
            bool liveInspector = false)
        {
            // Build can recurse (BindingFactory builds inline sub-layouts for expanded object refs mid-build),
            // so save/restore these per-build statics rather than just overwriting — otherwise a nested build
            // leaks its context onto sibling nodes the parent builds afterward.
            var prevSelectionSet = _selectionSet;
            var prevShowItemTemplates = _showItemTemplates;
            var prevPlayAppear = _playAppearAnimations;
            var prevOnLayoutEdited = _onLayoutEdited;
            var prevDebugConditions = _debugConditions;
            var prevDesignMode = _designMode;
            var prevCaptureNodeContexts = _captureNodeContexts;
            var prevBuildSkin = WidgetPalette.BuildScopeIsLight;
            var prevToggleMirrors = _toggleMirrors;
            var prevOrphanedSpacers = _orphanedSpacers;
            var prevDebugEnabled = LiveDebug.DebugRowBinder.EnabledForBuild;
            var prevLiveInspector = _liveInspector;
            // BuiltGraphs is a process-wide static that the walk below ACCUMULATES into and the block at the
            // end of the try CONSUMES. It was the one per-build static not saved here, and Build re-enters
            // itself synchronously mid-walk — BindingFactory.InlineEditor's RebuildInlineFields and
            // RebuildLegacy both call it — so the nested build's `debugGraphs.Clear()` destroyed the outer
            // build's accumulation. Every graph built BEFORE an in-line-editor row was orphaned: never handed
            // to DebugRunner, so never shown, never synced, never torn down. With the inline row first the
            // count reached zero and the inspector got no transport bar and no runner at all, while the bug
            // glyph still lit as if it were recording.
            // Only allocates for a nested build inside a debug-enabled outer one; the outermost build sees an
            // empty list and copies nothing.
            var prevDebugGraphs = LiveDebug.DebugRowBinder.BuiltGraphs.Count > 0
                ? new List<LiveDebug.DebugGraphElement>(LiveDebug.DebugRowBinder.BuiltGraphs)
                : null;
            // Cleared, not just saved: a nested build (in-line editor, embedded editor) must answer this from
            // its OWN layout's header, never inherit the outer one's. Same reasoning as _toggleMirrors above.
            LiveDebug.DebugRowBinder.EnabledForBuild = false;
            // Cleared up front, not just replaced: the Root null-check below returns before the collect runs, and
            // a nested build must never inherit its parent asset's mirror set.
            _toggleMirrors = null;
            _orphanedSpacers = null;
            _selectionSet = selectedNodes;
            _showItemTemplates = showItemTemplates;
            _playAppearAnimations = playAppearAnimations;
            _onLayoutEdited = onLayoutEdited;
            _debugConditions = debugConditions;
            _designMode = designMode;
            _captureNodeContexts = captureNodeContexts;
            // INHERITED by a nested build, unlike EnabledForBuild beside it. "Am I inside a real Inspector?" is
            // a fact about the SURFACE, and an in-line editor drawn inside one is still inside one — so an
            // expanded object reference whose own layout carries the debug toggle keeps its graph rows. What
            // the nested build must not inherit is the OUTER LAYOUT's answer about its own Script Header, which
            // is the separate flag cleared two lines below.
            _liveInspector = liveInspector || _liveInspector;

            try
            {
                if (layoutAsset == null || layoutAsset.Root == null)
                {
                    return new Label("Empty or null Inspector Layout.");
                }

                // CYCLE GUARD — must stay the FIRST thing that touches the graph. Everything below walks the
                // node tree recursively (GroupToggleMirror.Collect on the very next line, then BuildNode, then
                // the per-node condition pass), and a cyclic graph sends all of them into unbounded recursion.
                // See HasCycle for why that is not survivable without this check.
                if (HasCycle(layoutAsset.Root, out var cyclicNode))
                {
                    WarnCyclicLayoutOnce(layoutAsset, cyclicNode);
                    return BuildCyclicLayoutNotice();
                }

                // A toggle-group Foldout's header checkbox IS its bound bool, so a binding to that same bool
                // inside the group is a second editor of one value. Derived per build (never stored on the
                // model) so it un-does itself the moment the group or its Toggle Property changes.
                _toggleMirrors = GroupToggleMirror.Collect(layoutAsset.Root);
                // Derived the same way and for the same reason: nothing is stored on the model, so
                // un-hiding the field brings its [Space] gap straight back.
                _orphanedSpacers = HiddenNodeMark.CollectOrphanedSpacers(layoutAsset.Root);

                // Live Debug is opt-in per layout via the Script Header's toggle, so the answer is derived once
                // here and read by every BindingNode below.
                //
                // OPT IN, NOT OPT OUT. `!designMode` was the only gate, and it is not the same question: the
                // Layout Designer's PREVIEW button sets designMode = false precisely so the canvas behaves like
                // a real inspector, and the preset editor is hard-wired to false. Both therefore mounted the
                // whole feature onto a design surface — a transport bar, a graph row under every field, a
                // recorder for the throwaway preview target, and DebugRunner.Install hooking the console. The
                // designer measures node-card geometry off that canvas, so the extra rows moved the thing it
                // was measuring. Of the 13 callers of Build only THREE pass liveInspector — the two real
                // inspectors, VisualInspectorEditor (fallback) and SasquatchForceTakeover (displacement), plus
                // the Showcase Sandbox, which asks on purpose so its Live Debug page can demonstrate the
                // feature (see ShowcaseLiveDebugDriver). A new caller gets no Live Debug until it asks, which
                // is the safe default for a feature that installs global hooks.
                var debugGraphs = LiveDebug.DebugRowBinder.BuiltGraphs;
                debugGraphs.Clear();
                LiveDebug.DebugRowBinder.EnabledForBuild =
                    _liveInspector && !designMode && LiveDebug.DebugRowBinder.LayoutWantsDebug(layoutAsset.Root);

                // Theme + effective skin: explicit arg → Project Settings → bundled theme matching the
                // editor skin (empty setting = follow the editor). The skin follows the RESOLVED asset's
                // BaseSkin and picks both the --sasq-* token sheet and the WidgetPalette branch; pinning
                // it for the whole build keeps every widget constructor on the same palette.
                bool isLightSkin;
                (theme, isLightSkin) = SasquatchThemeResolver.Resolve(theme);
                WidgetPalette.BuildScopeIsLight = isLightSkin;

                var rootElement = BuildNode(
                    layoutAsset.Root,
                    "",
                    serializedObject,
                    onRebuildRequired,
                    theme,
                    designMode,
                    selectedNode,
                    dragController,
                    layoutAsset,
                    designInteract,
                    nodePath: "0"
                );

                if (rootElement != null)
                {
                    var widgetsUss = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
                    if (widgetsUss != null)
                    {
                        rootElement.styleSheets.Add(widgetsUss);
                    }
                    // Tag the build root so ConditionDebugOverlay can find an always-on-top, rebuild-scoped host
                    // for its floating breakdown card (it dies with this root on the next rebuild).
                    rootElement.AddToClassList("sasq-build-root");
                    // Skin class + per-skin --sasq-* token sheet. Hosts that only load the widget
                    // sheet (no skin applied) resolve the var() fallbacks — the historical dark look.
                    SasquatchSkin.Apply(rootElement, isLightSkin);
                    rootElement.Bind(serializedObject);
                    // Conditions are bound per-node inside BuildNode (each node needs its own scoped base
                    // path), so there is no whole-tree condition pass here.

                    // ONE sampling tick for the whole inspector — installed here rather than per row so every
                    // channel is written on the same pass and their sample indices keep meaning one instant.
                    if (debugGraphs.Count > 0)
                    {
                        var debugRecorder = LiveDebug.DebugRecorder.For(serializedObject);
                        var debugType = FirstTargetType(serializedObject);
                        // The transport owns the shared playhead, so there is exactly one for the whole
                        // inspector. It sits directly under the header, above the first field, because that is
                        // where the eye already is after clicking the record button.
                        var transport = new LiveDebug.DebugTransportBar(debugRecorder,
                            () => LiveDebug.DebugMomentCard.Hide(rootElement));
                        // InstallInto, not Insert: the bar reserves its gap at the TOP of the flow but has to
                        // be the LAST child to paint over the rows it pins itself above. UI Toolkit has no
                        // z-index, so paint order is hierarchy order and nothing else.
                        transport.InstallInto(rootElement);

                        LiveDebug.DebugRunner.Install(
                            rootElement,
                            debugType,
                            debugRecorder,
                            debugGraphs,
                            (graph, absolute) => LiveDebug.DebugMomentCard.Show(rootElement, graph, absolute,
                                debugRecorder, debugType),
                            transport);
                    }
                    else if (LiveDebug.DebugRowBinder.EnabledForBuild)
                    {
                        // A DEBUG BUILD THAT PRODUCED NOTHING still minted a recorder: DebugRowBinder.Attach
                        // calls For() on the first bound row, before it can know whether any Register will
                        // succeed. When they all fail — a layout whose binding paths stopped resolving, which
                        // is what a script refactored without re-syncing its layout looks like — no graph is
                        // built, DebugRunner is never reached, and nothing ever marks the recorder as SEEN.
                        // PruneDead's residue rule requires that mark, so the entry became un-evictable for the
                        // rest of the domain and collected a fresh un-evictable twin on every scene reload
                        // (the object id changes and nothing looks the old one up again). Marking rather than
                        // evicting here is deliberate: it is the same signal a normal build gives, so the one
                        // rule in PruneDead stays the only rule.
                        LiveDebug.DebugRecorder.MarkBuiltFor(serializedObject);
                    }
                }

                return rootElement ?? new VisualElement();
            }
            finally
            {
                _selectionSet = prevSelectionSet;
                _showItemTemplates = prevShowItemTemplates;
                _playAppearAnimations = prevPlayAppear;
                _onLayoutEdited = prevOnLayoutEdited;
                _debugConditions = prevDebugConditions;
                _designMode = prevDesignMode;
                _captureNodeContexts = prevCaptureNodeContexts;
                WidgetPalette.BuildScopeIsLight = prevBuildSkin;
                _toggleMirrors = prevToggleMirrors;
                _orphanedSpacers = prevOrphanedSpacers;
                LiveDebug.DebugRowBinder.EnabledForBuild = prevDebugEnabled;
                _liveInspector = prevLiveInspector;
                // Hand the outer build back exactly the accumulation it had. Unconditional, so an early
                // return above (null root, cyclic graph) restores the same contents rather than leaving a
                // half-filled list behind.
                LiveDebug.DebugRowBinder.BuiltGraphs.Clear();
                if (prevDebugGraphs != null) LiveDebug.DebugRowBinder.BuiltGraphs.AddRange(prevDebugGraphs);
            }
        }

        // ---- Cyclic-graph guard ----------------------------------------------------------------------------
        // A LayoutNode graph is a TREE by construction: the designer's re-parenting refuses to drop a node
        // under its own descendant, and nothing in the product can author a cycle. A hand-edited, badly-merged
        // or otherwise corrupted .asset can still hold one, and when it does EVERY recursive walker over the
        // graph — GroupToggleMirror.Collect, BuildNode, the condition pass, the export emitter — recurses
        // forever. What comes back is StackOverflowException, which .NET CANNOT catch: it kills the editor
        // process outright, so the build-failure net in VisualInspectorEditor cannot degrade it either and the
        // user loses their session with no diagnostic at all.
        //
        // So the graph is validated ONCE, up front, and the check itself is ITERATIVE (an explicit stack) —
        // a recursive cycle detector would be the very thing it exists to prevent. Cost is O(nodes) with one
        // HashSet and one Stack, against a build that goes on to construct hundreds of VisualElements.
        //
        // The two sets do DIFFERENT jobs, and both are needed:
        //   onStack — the current ANCESTOR chain. Re-entering it IS the cycle. This is the detector.
        //   done    — nodes proven acyclic. A graph that merely ALIASES one node under two parents is legal
        //             here (finite; BuildNode simply builds it twice), and without `done` each alias is
        //             re-walked once per path — exponential on a deep graph, i.e. the same editor hang this
        //             guard exists to prevent, arriving by a different door.
        // They overlap: `done` is consulted first, so removing `onStack.Remove` alone changes no OUTCOME (it
        // only lets onStack grow from tree-DEPTH to tree-SIZE). Measured 2026-08-14 — the alias assertion in
        // TestCyclicLayoutGraphIsDetectedIteratively needs BOTH neutered to go red, and its control does that.

        /// <summary>
        /// How many of <paramref name="container"/>'s children this build will actually PRODUCE an element for.
        /// <para>Every "Columns: N / Rows: N" cap decision is a count comparison, and in a live render a hidden
        /// node is skipped outright (the early return at the top of <see cref="BuildNode"/>), so reading
        /// <c>Children.Count</c> there would keep a container wrapping after it no longer has enough children to
        /// wrap — a 4-child "Columns: 3" would stay in Grid-regroup mode showing three columns and one orphan
        /// row. Design mode builds everything, so the two counts agree there and the canvas keeps showing the
        /// author the shape they actually authored.</para>
        /// </summary>
        private static int RenderableChildCount(ContainerNode container, bool designMode)
        {
            var kids = container?.Children;
            if (kids == null) return 0;
            if (designMode) return kids.Count;

            int n = 0;
            for (int i = 0; i < kids.Count; i++)
                if (WillBuild(kids[i], designMode)) n++;
            return n;
        }

        /// <summary>
        /// Whether <see cref="BuildNode"/> will produce an element for <paramref name="node"/> — the ONE
        /// predicate every "how many children are really there?" question must ask, so a new skip can never
        /// be added to the builder's prologue and forgotten by the counters.
        /// <para>THREE live skips, and they must stay the same three BuildNode opens with: the author's own
        /// hide, a toggle-group mirror (the header checkbox already edits that bool), and an attribute-owned
        /// [Space] orphaned by its field being hidden. Counting only the FIRST is what shipped: a Columns
        /// container whose hidden field owned an imported gap stayed one over its cap and kept regrouping
        /// with nothing left to regroup — the exact failure RenderableChildCount was written to prevent,
        /// arriving through the door next to the one it was watching.</para>
        /// <para>Reads the per-build statics rather than taking them as parameters because both are already
        /// saved/restored around nested builds, so they are accurate for the build in flight — the same
        /// reason CollectColumnBindings reads _designMode directly.</para>
        /// <para>TWIN: LayoutSourceEmitter.WillEmit. The standalone export is a third renderer of this model
        /// and asks the identical question; change one, change the other.</para>
        /// </summary>
        internal static bool WillBuild(LayoutNode node, bool designMode)
        {
            if (node == null) return false;
            // The design canvas builds everything — it is the only surface the author can un-hide from.
            if (designMode) return true;
            if (node.HiddenInInspector) return false;
            if (_toggleMirrors != null && _toggleMirrors.Contains(node)) return false;
            if (_orphanedSpacers != null && _orphanedSpacers.Contains(node)) return false;
            return true;
        }

        /// <summary>
        /// True when the graph under <paramref name="root"/> contains a cycle — some node reachable from
        /// itself — and outputs the node the walk re-entered. Iterative, so it can never overflow. Follows
        /// exactly the child links every other walker follows: <see cref="ContainerNode.Children"/>,
        /// <see cref="CollectionNode.ItemLayout"/>, and a binding's two dictionary CELL TEMPLATES.
        /// <para>
        /// The templates are here for the same reason the ItemLayout is: they are node references stored on a
        /// node, so a hand-edited or badly-merged file can point one at an ancestor, and the resulting
        /// stack-overflow is the UNCATCHABLE crash this guard exists to turn into a help box. Nothing in the
        /// designer can author it — which is exactly what was said about the ItemLayout too.
        /// </para>
        /// </summary>
        internal static bool HasCycle(LayoutNode root, out LayoutNode offender)
        {
            offender = null;
            if (root == null) return false;

            // (node, next child index). -1 is not used: the ItemLayout slot is simply index 0 of a collection.
            var stack = new Stack<KeyValuePair<LayoutNode, int>>();
            var onStack = new HashSet<LayoutNode>();   // the current ANCESTOR chain — re-entry here IS the cycle
            var done = new HashSet<LayoutNode>();      // fully explored, provably acyclic

            stack.Push(new KeyValuePair<LayoutNode, int>(root, 0));
            onStack.Add(root);

            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                LayoutNode child = null;

                if (frame.Key is ContainerNode container && container.Children != null
                    && frame.Value < container.Children.Count)
                {
                    child = container.Children[frame.Value];
                    stack.Push(new KeyValuePair<LayoutNode, int>(frame.Key, frame.Value + 1));
                }
                else if (frame.Key is CollectionNode collection && frame.Value == 0)
                {
                    child = collection.ItemLayout;
                    stack.Push(new KeyValuePair<LayoutNode, int>(frame.Key, 1));
                }
                // A binding's dictionary Key / Value cell templates occupy slots 0 and 1.
                else if (frame.Key is BindingNode dictOwner && frame.Value < 2)
                {
                    child = frame.Value == 0 ? dictOwner.DictionaryKeyTemplate : dictOwner.DictionaryValueTemplate;
                    stack.Push(new KeyValuePair<LayoutNode, int>(frame.Key, frame.Value + 1));
                }
                else
                {
                    // Every child explored — this node leaves the ancestor chain.
                    onStack.Remove(frame.Key);
                    done.Add(frame.Key);
                    continue;
                }

                if (child == null || done.Contains(child)) continue;
                if (!onStack.Add(child)) { offender = child; return true; }
                stack.Push(new KeyValuePair<LayoutNode, int>(child, 0));
            }

            return false;
        }

        // One line per (asset, node kind), not one per rebuild: a corrupted layout rebuilds on every repaint,
        // and the diagnostic is only useful the first time. Same warn-once shape as the fallback inspector's
        // build-failure net.
        private static readonly HashSet<string> _warnedCyclicLayouts = new HashSet<string>();

        private static void WarnCyclicLayoutOnce(InspectorLayoutAsset layoutAsset, LayoutNode offender)
        {
            string assetName = layoutAsset != null ? layoutAsset.name : "<unnamed layout>";
            string key = assetName + "|" + (offender != null ? offender.GetType().Name : "<null>");
            if (!_warnedCyclicLayouts.Add(key)) return;

            Debug.LogWarning(
                $"[Sasquatch Inspector] The layout '{assetName}' could not be drawn: its node graph contains a " +
                $"CYCLE (a {(offender != null ? offender.GetType().Name : "node")} that is nested inside itself). " +
                "Nothing in Sasquatch Inspector can author this, so the file has been hand-edited or badly " +
                "merged. Restore a snapshot from Project Settings ▸ Sasquatch Inspector, or delete the layout " +
                "asset to fall back to the default inspector.", layoutAsset);
        }

        // Shown IN PLACE of the layout. Deliberately not a bare empty element: a blank inspector reads as
        // "Sasquatch Inspector is broken", where this says what happened and what to do about it.
        private static VisualElement BuildCyclicLayoutNotice() =>
            new HelpBox(
                "This inspector layout could not be drawn — its saved node graph contains a cycle (a group " +
                "nested inside itself), which only a corrupted or hand-edited layout file can hold.\n\n" +
                "Restore a snapshot from Project Settings ▸ Sasquatch Inspector, or delete this type's layout " +
                "asset to fall back to Unity's default inspector.",
                HelpBoxMessageType.Error);

        private static VisualElement BuildNode(
            LayoutNode node,
            string basePath,
            SerializedObject serializedObject,
            Action onRebuildRequired,
            SasquatchThemeAsset theme,
            bool designMode,
            LayoutNode selectedNode,
            CanvasDragController dragController,
            InspectorLayoutAsset layoutAsset,
            bool designInteract = false,
            bool columnCell = false,
            bool parentColumnsHasAnyHeader = false,
            string nodePath = "",
            bool directChildrenAreCells = false,
            // A LIVE table cell. The widget is built with its label force-hidden (the column header already
            // names the value) and stays a bare cell — no full-row normalization, no validation wrapper.
            // Everything AFTER the widget is built is deliberately NOT special-cased, because skipping it is
            // precisely what left a table column's Appearance + Field Styling alive on the designer canvas
            // (whose representative row routes through here) and dead in the real Inspector. Appended at the
            // end of the parameter list on purpose: two call sites pass columnCell/parentColumnsHasAnyHeader
            // positionally, so a middle insertion would silently re-map them.
            bool tableCell = false)
        {
            if (node == null) return null;

            // A binding that duplicates its group's header checkbox is not built at all in a LIVE render — not
            // built-then-hidden, because the sibling rhythm (ApplySectionGaps / ApplyGap) is computed from the
            // elements that exist, and a hidden last child would leave its gap behind as a dead strip. Skipping
            // it here also matches the standalone export, which never emits the node either. The DESIGN canvas
            // keeps building it (dimmed + pipped further down) — the author still has to find and edit it.
            if (!designMode && _toggleMirrors != null && _toggleMirrors.Contains(node)) return null;

            // The author hid this node (the hierarchy row's eye, General Settings ▸ Visible in Inspector, or the
            // live quick-edit menu). Skipped for exactly the reason spelled out above: the sibling rhythm is
            // computed from the elements that EXIST, so building-then-hiding would leave the gap behind. A hidden
            // container takes its whole subtree with it, because the recursion never reaches the children.
            // The design canvas keeps building it — dimmed and pipped further down — since that is the only
            // surface the author can un-hide it from.
            if (!designMode && node.HiddenInInspector) return null;

            // ...and the gap that field's code [Space] imported goes with it. Otherwise hiding a field leaves a
            // blank strip exactly where it used to be — the one place "everything closes up" would visibly fail.
            // The spacer is a separate node and is not itself hidden, so the check above cannot catch it.
            if (!designMode && _orphanedSpacers != null && _orphanedSpacers.Contains(node)) return null;

            VisualElement element = null;
            // Set by the BindingNode branch; consumed by the DrawerVisibilityBridge wiring at the bottom,
            // which needs the RESOLVED path but must run on the final element. Null for every other node kind.
            string boundFieldPath = null;

            if (node is ContainerNode containerNode)
            {
                VisualElement containerElement = null;
                VisualElement contentTarget = null;

                // Append this container's scope if configured
                string childBasePath = basePath;
                if (!string.IsNullOrEmpty(containerNode.PropertyPath))
                {
                    childBasePath = string.IsNullOrEmpty(basePath) 
                        ? containerNode.PropertyPath 
                        : $"{basePath}.{containerNode.PropertyPath}";
                }

                // Eligible containers (Rows/Columns/Grid/Card) render inside a collapsible
                // .sasq-group shell when Expandable. Foldout and Tabs are excluded. The group becomes the
                // container's root element (so it flows through StyleApplier/userData/drag exactly like the
                // expandable Collection widget); the inner content-target is left unchanged.
                bool collapsible = containerNode.Expandable
                    && (containerNode.Type == ContainerType.Rows
                     || containerNode.Type == ContainerType.Columns
                     || containerNode.Type == ContainerType.Grid
                     || containerNode.Type == ContainerType.Card);

                switch (containerNode.Type)
                {
                    case ContainerType.Rows:
                        // "Rows" stacks children top-to-bottom (each its own row). In flexbox that's
                        // flex-direction: column — the name describes the visual result, not the CSS axis.
                        containerElement = new VisualElement();
                        containerElement.style.flexDirection = FlexDirection.Column;
                        contentTarget = containerElement;
                        break;
                    case ContainerType.Columns:
                        containerElement = new VisualElement();
                        containerElement.style.flexDirection = FlexDirection.Column;

                        if (!collapsible && !string.IsNullOrEmpty(containerNode.Title))
                        {
                            var flowHeader = new Label(containerNode.Title);
                            flowHeader.AddToClassList("sasq-flow-header");
                            flowHeader.AddToClassList("sasq-node-title");
                            flowHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                            flowHeader.style.color = TitleInk;
                            flowHeader.style.marginBottom = TitleGap;
                            flowHeader.style.flexShrink = 0;

                            if (containerNode.Style.justify.isOverridden)
                            {
                                var j = containerNode.Style.justify.value;
                                if (j == Justify.Center)
                                    flowHeader.style.unityTextAlign = TextAnchor.MiddleCenter;
                                else if (j == Justify.FlexEnd)
                                    flowHeader.style.unityTextAlign = TextAnchor.MiddleRight;
                                else
                                    flowHeader.style.unityTextAlign = TextAnchor.MiddleLeft;
                            }
                            else
                            {
                                flowHeader.style.unityTextAlign = TextAnchor.MiddleLeft;
                            }

                            containerElement.Add(flowHeader);
                        }

                        contentTarget = new VisualElement();
                        contentTarget.AddToClassList("sasq-columns-content-target");
                        contentTarget.style.flexDirection = FlexDirection.Row;
                        contentTarget.style.flexWrap = Wrap.Wrap;
                        // Default STRETCH: cells share the row's height, so design-mode selection
                        // outlines tile uniformly instead of floating at each cell's own center
                        // (matches the wrapped/grid rows). "Vertical Alignment" still overrides.
                        contentTarget.style.alignItems = Align.Stretch;

                        containerElement.Add(contentTarget);
                        break;
                    case ContainerType.Grid:
                        // A grid is a COLUMN stack of explicit Row containers (built by ApplyGridLayout below).
                        // The shared children loop first builds the cells flat into contentTarget; ApplyGridLayout
                        // then regroups them into per-row containers. Explicit rows mean each row is self-contained
                        // (no flex-wrap to overflow), cell heights synchronize per row, and dividers sit in-flow
                        // (no absolute handles to drift) — while the per-cell build (design cards, drag
                        // manipulators, conditions) is left untouched by the regroup (it only re-parents cells).
                        containerElement = new VisualElement();
                        containerElement.style.flexDirection = FlexDirection.Column;

                        contentTarget = new VisualElement();
                        contentTarget.AddToClassList("sasq-grid-content-target");
                        contentTarget.style.flexDirection = FlexDirection.Column;

                        containerElement.Add(contentTarget);
                        break;
                    case ContainerType.Card:
                        containerElement = new VisualElement();
                        // Decorative background/radius/padding is suppressed when collapsible so the .sasq-group
                        // shell provides the only frame (no double frame inside the group).
                        if (!collapsible)
                        {
                            containerElement.style.backgroundColor = WidgetPalette.PanelWash;
                            containerElement.style.borderTopLeftRadius = 6;
                            containerElement.style.borderTopRightRadius = 6;
                            containerElement.style.borderBottomLeftRadius = 6;
                            containerElement.style.borderBottomRightRadius = 6;
                            containerElement.style.paddingTop = 10;
                            containerElement.style.paddingBottom = 10;
                            // LEFT is the shared section gutter, not the box's 10px inset: a Card is an INSET
                            // section, so its title and its fields sit on the same edge a Foldout's do. The other
                            // three sides stay at 10 — widening them too would only cost field width, and a
                            // full-width row's input already runs to the right inset, so the asymmetry doesn't read.
                            containerElement.style.paddingLeft = SectionGutter;
                            containerElement.style.paddingRight = 10;
                            containerElement.style.marginBottom = 6;
                        }

                        if (!collapsible && !string.IsNullOrEmpty(containerNode.Title))
                        {
                            var cardLabel = new Label(containerNode.Title);
                            cardLabel.AddToClassList("sasq-node-title");
                            cardLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            cardLabel.style.marginBottom = TitleGap;
                            cardLabel.style.color = TitleInk;
                            containerElement.Add(cardLabel);
                        }
                        contentTarget = new VisualElement();
                        containerElement.Add(contentTarget);
                        break;
                    case ContainerType.Frame:
                        // A shaped clipping container (FrameBuilder): the root clips to the shape; for a
                        // DiagonalSplit contentTarget is null and the two pane hosts are filled by the
                        // split branch below (the Tabs precedent) instead of the shared children loop.
                        containerElement = FrameBuilder.BuildShell(containerNode, serializedObject, designMode, out var frameContent);
                        contentTarget = frameContent;
                        break;
                    case ContainerType.Foldout:
                        // A toggle group iff a bool path is set (the bound bool IS the header checkbox). Otherwise
                        // a plain Unity Foldout, unchanged.
                        if (string.IsNullOrEmpty(containerNode.GroupTogglePath))
                        {
                            var foldout = new Foldout();
                            string foldoutTitle = !string.IsNullOrEmpty(containerNode.Title) ? containerNode.Title : "Group";
                            foldout.text = foldoutTitle;
                            // Remember the open/closed state per type (like Unity's native foldouts). Seed BEFORE
                            // registering the writeback so seeding doesn't persist itself.
                            string foldoutKey = designMode ? null : FoldoutStateStore.Key(serializedObject.targetObject, nodePath, foldoutTitle);
                            foldout.value = FoldoutStateStore.Get(foldoutKey, containerNode.DefaultToOpen);
                            SasquatchIcons.SkinFoldout(foldout);
                            // A Foldout is the INSET section that already sits on the shared gutter: its arrow
                            // defines it. Two adjustments make it match the rest.
                            //  1. The header text is stock-weight grey — every other container title is bold and
                            //     one ink. Styled INLINE (never via USS on .unity-foldout__text) because a plain
                            //     struct/class field inside this layout renders its own native Foldout, and those
                            //     must stay stock. The marker class joins so the Title "T" panel keeps finding it
                            //     FIRST — otherwise ApplyTextToFirstClass's subtree query hits a titled Card
                            //     nested inside this foldout and styles the child's title instead.
                            //  2. Unity indents the content 15px, 4px short of where it puts the header text, so
                            //     the fields never quite hung under their own title.
                            var foldoutText = foldout.Q<Label>(className: "unity-foldout__text");
                            if (foldoutText != null)
                            {
                                foldoutText.AddToClassList("sasq-node-title");
                                foldoutText.style.unityFontStyleAndWeight = FontStyle.Bold;
                                foldoutText.style.color = TitleInk;
                            }
                            foldout.contentContainer.style.marginLeft = SectionGutter;
                            // Design select-only: stop the disclosure toggle from expanding/collapsing on click (a
                            // Foldout is a container, so the leaf picking-disable never reaches it). The header
                            // Toggle and the content container are SIBLINGS, so disabling just the toggle leaves
                            // the child cards inside the foldout fully selectable.
                            if (designMode && !designInteract)
                            {
                                var headerToggle = foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
                                if (headerToggle != null) DisableInteractivityRecursively(headerToggle);
                            }
                            // Guard on evt.target: a NESTED foldout's value change also bubbles to here, and must
                            // not overwrite THIS foldout's stored state.
                            foldout.RegisterValueChangedCallback(evt =>
                            {
                                if (evt.target == foldout) FoldoutStateStore.Set(foldoutKey, evt.newValue);
                            });
                            // Search expander (site 1 of 5 — keep in lockstep): SetValueWithoutNotify toggles the
                            // content without firing the persist callback above; Restore re-reads the store so a
                            // toggle the user made DURING the search survives the restore.
                            {
                                var searchFoldout = foldout;
                                var searchKey = foldoutKey;
                                var searchDefault = containerNode.DefaultToOpen;
                                GroupExpanderRegistry.Register(searchFoldout, new GroupExpander(
                                    open => searchFoldout.SetValueWithoutNotify(open),
                                    () => searchFoldout.SetValueWithoutNotify(
                                        FoldoutStateStore.Get(searchKey, searchDefault))));
                            }
                            containerElement = foldout;
                            contentTarget = foldout.contentContainer;
                        }
                        else
                        {
                            containerElement = BuildToggleGroup(containerNode, basePath, serializedObject, designMode, designInteract, out contentTarget);
                        }
                        break;
                    case ContainerType.Tabs:
                        containerElement = new VisualElement();
                        containerElement.style.flexDirection = FlexDirection.Column;

                        // Optional Title/Label rendered as a header ABOVE the tab strip. The designer offers a
                        // Title on every container type (its "T" text panel styles it via the sasq-flow-header /
                        // sasq-node-title classes — see ApplyNodeTextStyles). Distinct from the per-tab strip
                        // buttons, which come from each child's TabLabel. Mirrors the Columns header (bold,
                        // justify-aware). Added at index 0; the tab strip below is fetched by name (not by
                        // child index) so this header doesn't shift it.
                        if (!string.IsNullOrEmpty(containerNode.Title))
                        {
                            var tabsHeader = new Label(containerNode.Title);
                            tabsHeader.AddToClassList("sasq-flow-header");
                            tabsHeader.AddToClassList("sasq-node-title");
                            tabsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                            tabsHeader.style.color = TitleInk;
                            tabsHeader.style.marginBottom = TitleGap;
                            tabsHeader.style.flexShrink = 0;

                            if (containerNode.Style.justify.isOverridden)
                            {
                                var j = containerNode.Style.justify.value;
                                if (j == Justify.Center)
                                    tabsHeader.style.unityTextAlign = TextAnchor.MiddleCenter;
                                else if (j == Justify.FlexEnd)
                                    tabsHeader.style.unityTextAlign = TextAnchor.MiddleRight;
                                else
                                    tabsHeader.style.unityTextAlign = TextAnchor.MiddleLeft;
                            }
                            else
                            {
                                tabsHeader.style.unityTextAlign = TextAnchor.MiddleLeft;
                            }

                            containerElement.Add(tabsHeader);
                        }

                        var tabStrip = new VisualElement();
                        // Named so the Tabs-building block below fetches it by name — an optional Title header
                        // can sit at index 0, so a fixed child index would be wrong.
                        tabStrip.name = "sasq-tab-strip";
                        tabStrip.style.flexDirection = FlexDirection.Row;
                        // Wrap tab buttons onto new rows once they exceed the inspector width, instead of
                        // overflowing into a long horizontal scroll. Tabs should never push past the edge.
                        tabStrip.style.flexWrap = Wrap.Wrap;
                        // BOTTOM-ALIGNED, and that is load-bearing. The active tab is taller than the others
                        // (see ApplyTabButtonSize); the default `stretch` would hand that extra height to
                        // every tab in the row and erase the cue entirely. Flex-end instead pins every tab's
                        // bottom edge on the panel's rail and lets the tall one grow UPWARD, which is both
                        // the cue and the reason the join underneath survives it.
                        tabStrip.style.alignItems = Align.FlexEnd;
                        // NO baseline hairline here, and NO gap below it. The panel underneath draws its own
                        // top rail in the ACTIVE TAB'S colour and the strip sits flush on it, so the lit tab
                        // and its content are one continuous accent surface. A neutral line plus a 6px gap —
                        // what used to be here — did the exact opposite: it fenced the tab off from the
                        // content it owns, and the content read as a sibling that merely came afterwards.
                        tabStrip.style.borderBottomWidth = 0;
                        tabStrip.style.marginBottom = 0;
                        containerElement.Add(tabStrip);

                        var tabsContentTarget = new VisualElement();
                        // Named for the same reason the strip is: tests and the search pass fetch the open
                        // tab's panel without counting past an optional Title header.
                        tabsContentTarget.name = "sasq-tab-panel";
                        StyleTabPanel(tabsContentTarget, ResolveTabAccent(containerNode));
                        containerElement.Add(tabsContentTarget);
                        contentTarget = tabsContentTarget;
                        break;
                }

                // Flow containers (Rows/Grid) render an optional Title as a header above their
                // content. Card/Foldout/Tabs/Columns handle their own title in the switch above. The header
                // carries no userData, so the drag controller's CountLayoutChildren ignores it (drag-safe).
                if (!collapsible &&
                    (containerNode.Type == ContainerType.Rows ||
                     containerNode.Type == ContainerType.Grid) &&
                    !string.IsNullOrEmpty(containerNode.Title))
                {
                    var flowHeader = new Label(containerNode.Title);
                    flowHeader.AddToClassList("sasq-flow-header");
                    // Joins Columns/Tabs in carrying the marker class. Without it ApplyTextToFirstClass's
                    // subtree query walks past this header to a titled Card nested inside, and styling a
                    // Rows/Grid title silently restyled the child's instead.
                    flowHeader.AddToClassList("sasq-node-title");
                    flowHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                    flowHeader.style.color = TitleInk;
                    flowHeader.style.marginBottom = TitleGap;
                    flowHeader.style.flexShrink = 0;
                    // alignSelf:Stretch, NOT flex-basis:100%. Both hosts are COLUMN stacks, where flex-basis
                    // is the MAIN axis (height) — 100% there makes the title eat the whole container height
                    // and collapses the fields (they shrink to ~0 and overlap). A column child is already
                    // full-width via the default cross-axis stretch, and Stretch keeps it full-width even
                    // when the container's field alignment (alignItems) is Center/Left/Right.
                    if (containerNode.Type == ContainerType.Grid)
                    {
                        flowHeader.style.alignSelf = Align.Stretch;
                    }
                    else
                    {
                        flowHeader.style.alignSelf = Align.Stretch;
                    }

                    // Title alignment from the container's `justify` override (the L/C/R buttons), mirroring
                    // Columns. For Rows the container's justifyContent is reset post-build so `justify` only
                    // drives this title text-align, not the vertical distribution of the fields.
                    var titleAnchor = TextAnchor.MiddleLeft;
                    if (containerNode.Style.justify.isOverridden)
                    {
                        var jv = containerNode.Style.justify.value;
                        if (jv == Justify.Center) titleAnchor = TextAnchor.MiddleCenter;
                        else if (jv == Justify.FlexEnd) titleAnchor = TextAnchor.MiddleRight;
                    }
                    flowHeader.style.unityTextAlign = titleAnchor;

                    if (containerNode.Type == ContainerType.Grid)
                    {
                        containerElement.Insert(0, flowHeader);
                    }
                    else
                    {
                        contentTarget.Add(flowHeader);
                    }
                }

                // Collapsible wrap: place the built container inside a .sasq-group shell. This happens
                // AFTER the switch + (suppressed) title block but BEFORE the children loop, so contentTarget is
                // unchanged and now simply lives inside groupContent. groupRoot becomes containerElement, so the
                // downstream shared style/userData/design-card/drag/condition/animation logic treats the group
                // as THE container element — identical to the expandable Collection widget (whose .sasq-group
                // root is also its element, which is required for CanvasDragController.CountLayoutChildren to see
                // this container as a userData-bearing child). Children are still added into the original
                // contentTarget, which now lives inside groupContent.
                if (collapsible)
                {
                    var groupRoot = new VisualElement();
                    groupRoot.AddToClassList("sasq-group");

                    var header = new VisualElement();
                    header.AddToClassList("sasq-group-header");
                    // Lay the chevron and title side-by-side on a single row (chevron to the LEFT of the
                    // title), matching the collection header. The .sasq-group-header class alone doesn't force
                    // a row direction, so a VisualElement would otherwise stack them vertically.
                    header.style.flexDirection = FlexDirection.Row;
                    header.style.alignItems = Align.Center;

                    var chevron = SasquatchIcons.Create(
                        SasquatchIcons.Ids.ChevronRight, // single glyph; ApplyExpanded spins it 0°↔90°
                        10, WidgetPalette.ChevronDim);
                    chevron.AddToClassList("sasq-group-chevron");
                    header.Add(chevron);

                    string headerText = string.IsNullOrEmpty(containerNode.Title)
                        ? containerNode.Type.ToString()
                        : containerNode.Title;
                    var groupTitle = new Label(headerText);
                    groupTitle.AddToClassList("sasq-group-title");
                    // Marker class, same reason as the flow header: the shell WRAPS the container, so a titled
                    // Card inside it is a descendant and would otherwise win ApplyTextToFirstClass's query.
                    // Document order puts this header first, so the shell's own title is found.
                    groupTitle.AddToClassList("sasq-node-title");
                    groupTitle.pickingMode = PickingMode.Ignore;
                    groupTitle.style.flexGrow = 1;
                    header.Add(groupTitle);

                    var groupContent = new VisualElement();
                    groupContent.AddToClassList("sasq-group-content");
                    groupContent.Add(containerElement);

                    groupRoot.Add(header);
                    groupRoot.Add(groupContent);

                    // Open/closed state is REMEMBERED per type (FoldoutStateStore, EditorPrefs-backed) so a
                    // foldout stays how the user left it across selection changes, domain reloads and restarts —
                    // like Unity's native foldouts. DefaultToOpen is only the author-time default used until the
                    // user first toggles it; nothing is written to the layout model/database.
                    // null key in design mode → the designer shows the author-time DefaultToOpen default and
                    // never writes runtime view-state (the mock target shares the real type's GUID).
                    string groupKey = designMode ? null : FoldoutStateStore.Key(serializedObject.targetObject, nodePath, headerText);
                    bool expanded = FoldoutStateStore.Get(groupKey, containerNode.DefaultToOpen);
                    void ApplyExpanded(bool animate)
                    {
                        EditorSpringFX.SpringCaret(chevron, expanded, animate);
                        header.style.borderBottomLeftRadius = expanded ? 0 : 4;
                        header.style.borderBottomRightRadius = expanded ? 0 : 4;
                        // Spring only on live clicks; design mode stays instant so animated heights
                        // never fight canvas drag geometry (DropSlotMap snapshots, gap animation).
                        // animate==true marks a real user toggle (the initial seed uses animate==false), so
                        // that is exactly when the new open-state is persisted.
                        if (animate) { EditorSpringFX.Fold(groupContent, expanded); FoldoutStateStore.Set(groupKey, expanded); }
                        else groupContent.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    ApplyExpanded(false);
                    header.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        // Mirror the Collection guard so the chevron doesn't fight canvas selection/drag in design mode.
                        if (designMode && !designInteract) return;
                        expanded = !expanded;
                        ApplyExpanded(!designMode);
                        if (!designMode || designInteract) evt.StopPropagation();
                    });

                    // Search expander (site 2 of 5 — keep in lockstep): the transient path mirrors
                    // ApplyExpanded(false) minus persistence and WITHOUT touching `expanded` (the user's
                    // logical state); Restore replays ApplyExpanded from `expanded`, which still tracks any
                    // real click made during the search.
                    GroupExpanderRegistry.Register(groupRoot, new GroupExpander(
                        open =>
                        {
                            EditorSpringFX.SpringCaret(chevron, open, false);
                            header.style.borderBottomLeftRadius = open ? 0 : 4;
                            header.style.borderBottomRightRadius = open ? 0 : 4;
                            groupContent.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                        },
                        () => ApplyExpanded(false)));

                    containerElement = groupRoot; // BuildNode continues with the group as the container's root
                }

                if (containerNode.Type == ContainerType.Tabs)
                {
                    // By name, not containerElement[0]: an optional Title header may occupy index 0 now.
                    var tabStrip = containerElement.Q<VisualElement>("sasq-tab-strip");
                    var contentArea = contentTarget;
                    List<VisualElement> builtTabs = new List<VisualElement>();
                    // The MODEL node behind each entry of builtTabs, in the same order. builtTabs and tabStrip are
                    // index-parallel with EACH OTHER, but a hidden tab is never built, so neither is parallel with
                    // containerNode.Children any more. Every place that used to read Children[builtIndex] reads
                    // this instead; ActiveTabIndex stays a MODEL index, converted at the two seams below.
                    List<LayoutNode> builtTabNodes = new List<LayoutNode>();

                    if (containerNode.Children != null)
                    {
                        for (int i = 0; i < containerNode.Children.Count; i++)
                        {
                            var childNode = containerNode.Children[i];
                            var childElement = BuildNode(
                                childNode,
                                childBasePath,
                                serializedObject,
                                onRebuildRequired,
                                theme,
                                designMode,
                                selectedNode,
                                dragController,
                                layoutAsset,
                                designInteract,
                                nodePath: nodePath + "/" + i
                            );
                            if (childElement != null)
                            {
                                contentArea.Add(childElement);
                                builtTabs.Add(childElement);
                                builtTabNodes.Add(childNode);
                            }
                        }
                    }

                    // Model index → built index. Returns -1 when that tab was not built (it is hidden), which the
                    // clamp below turns into "show the first surviving tab".
                    int BuiltIndexOfModel(int modelIndex)
                    {
                        if (containerNode.Children == null) return -1;
                        if (modelIndex < 0 || modelIndex >= containerNode.Children.Count) return -1;
                        return builtTabNodes.IndexOf(containerNode.Children[modelIndex]);
                    }

                    // ...and back, for the writes to ActiveTabIndex.
                    int ModelIndexOfBuilt(int builtIndex)
                    {
                        if (containerNode.Children == null) return builtIndex;
                        if (builtIndex < 0 || builtIndex >= builtTabNodes.Count) return builtIndex;
                        return containerNode.Children.IndexOf(builtTabNodes[builtIndex]);
                    }

                    int selectedIndex = BuiltIndexOfModel(containerNode.ActiveTabIndex);
                    for (int i = 0; i < builtTabs.Count; i++)
                    {
                        var tabChild = builtTabNodes[i];
                        if (tabChild == selectedNode || IsDescendant(tabChild, selectedNode))
                        {
                            selectedIndex = i;
                            containerNode.ActiveTabIndex = ModelIndexOfBuilt(i);
                            break;
                        }
                        if (_selectionSet != null)
                        {
                            bool found = false;
                            foreach (var selNode in _selectionSet)
                            {
                                if (tabChild == selNode || IsDescendant(tabChild, selNode))
                                {
                                    selectedIndex = i;
                                    containerNode.ActiveTabIndex = ModelIndexOfBuilt(i);
                                    found = true;
                                    break;
                                }
                            }
                            if (found) break;
                        }
                    }
                    if (builtTabs.Count > 0)
                    {
                        if (selectedIndex < 0 || selectedIndex >= builtTabs.Count)
                        {
                            selectedIndex = 0;
                        }
                    }
                    else
                    {
                        selectedIndex = 0;
                    }

                    // Nothing built ⇒ nothing to enclose. Design mode keeps the box: the empty body is a real
                    // drop zone there (see MakeEmptyContainerHint below) and the panel is what shows where a
                    // dropped token lands. A LIVE inspector would just show an orphan accent bar, so strip it.
                    if (builtTabs.Count == 0 && !designMode)
                        ClearTabPanel(contentArea);

                    int currentTab = selectedIndex; // shared so hover styling can skip the active tab
                    // Active-tab tint is configurable per Tabs container; alpha 0 (old layouts) ⇒ default blue.
                    // SHARED with the content panel's top rail (StyleTabPanel, applied in the switch above) —
                    // that shared value is the whole join, so both sites resolve it through ONE helper.
                    var tabSelectedColor = ResolveTabAccent(containerNode);
                    var tabIdleColor = WidgetPalette.TabIdle;
                    var tabHoverColor = WidgetPalette.TabHover;
                    // Captured at build time so post-build callbacks (clicks) stay on the built skin.
                    var tabTextOn = WidgetPalette.TextStrong;
                    var tabTextOff = WidgetPalette.TextGray;

                    // The 1px seam between neighbouring tabs. Tabs BUTT TOGETHER now (marginRight 0), so
                    // without this every inactive tab's fill merges into one undifferentiated grey slab and
                    // you cannot see where one ends. Reserved on EVERY tab, transparent under the active one
                    // — the accent fill already separates that tab from its neighbours, and giving the seam
                    // to some tabs and not others would make them different widths.
                    Color TabSeam(bool selected) => selected ? Color.clear : WidgetPalette.TabDivider;

                    // Switches the visible tab in place (no rebuild) — shared by tab-button clicks and the
                    // spring-load drop affordance (hovering a tab mid-drag auto-opens it).
                    void ActivateTab(int activeIndex)
                    {
                        int previousTab = currentTab;
                        currentTab = activeIndex;
                        containerNode.ActiveTabIndex = ModelIndexOfBuilt(activeIndex);
                        for (int j = 0; j < builtTabs.Count; j++)
                        {
                            builtTabs[j].style.display = (j == activeIndex) ? DisplayStyle.Flex : DisplayStyle.None;
                            var btn = tabStrip[j] as Button;
                            if (btn != null)
                            {
                                btn.style.backgroundColor = (j == activeIndex) ? tabSelectedColor : tabIdleColor;
                                btn.style.color = (j == activeIndex) ? tabTextOn : tabTextOff;
                                btn.style.borderRightColor = TabSeam(j == activeIndex);
                                // The size cue moves with the fill. Hovered is FALSE for the incoming tab
                                // even though the pointer is sitting on it — it is about to be the tallest
                                // thing in the strip on its own account, and the outgoing one has to drop
                                // back to resting whether or not it is still under the cursor.
                                ApplyTabButtonSize(btn, j == activeIndex, false);
                            }
                        }

                        // Live inspector only: the incoming tab's fields cascade-punch in (each row springs
                        // up with a bouncy pop, staggered top-to-bottom). The button itself does NOT spring —
                        // its own rise to the taller active height is a padding transition, and a LandPunch
                        // or a scale is deliberately not put back on top of it: a transform on a tab that is
                        // flush against its neighbours reopens the seams it just closed, and a mid-flight
                        // transform offsets worldBound, which skews the drop-slot geometry of the
                        // spring-load-while-dragging path. The transition has neither problem.
                        if (!designMode && activeIndex != previousTab && EditorSpringFX.MotionEnabled
                            && activeIndex >= 0 && activeIndex < builtTabs.Count)
                        {
                            EditorSpringFX.CascadeFieldsIn(builtTabs[activeIndex]);
                        }
                    }

                    for (int i = 0; i < builtTabs.Count; i++)
                    {
                        int index = i;
                        var tabNode = builtTabNodes[index] as ContainerNode;
                        // The tab label lives on the child's TabLabel (separate from Title, so naming a tab never
                        // forces an in-content header). Falls back to Title (back-compat) then "Tab N".
                        string tabTitle =
                            (tabNode != null && !string.IsNullOrEmpty(tabNode.TabLabel)) ? tabNode.TabLabel :
                            (tabNode != null && !string.IsNullOrEmpty(tabNode.Title)) ? tabNode.Title :
                            $"Tab {index + 1}";

                        var tabButton = new Button();
                        tabButton.text = tabTitle;
                        // Hover feedback so tabs feel interactive (only when not the active tab — the lit one
                        // is already at the top of the strip and has nowhere to go). Fill AND height both
                        // move, and both ease: see ApplyTabButtonMotion for why this is a transition rather
                        // than one of the springs used everywhere else.
                        tabButton.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            if (index == currentTab) return;
                            tabButton.style.backgroundColor = tabHoverColor;
                            tabButton.style.color = tabTextOn;
                            ApplyTabButtonSize(tabButton, false, true);
                        });
                        tabButton.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            if (index == currentTab) return;
                            tabButton.style.backgroundColor = tabIdleColor;
                            tabButton.style.color = tabTextOff;
                            ApplyTabButtonSize(tabButton, false, false);
                        });
                        ApplyTabButtonMotion(tabButton, EditorSpringFX.MotionEnabled);
                        tabButton.style.borderTopWidth = 0;
                        tabButton.style.borderBottomWidth = 0;
                        tabButton.style.borderLeftWidth = 0;
                        // The seam, reserved on every tab so they all measure the same — see TabSeam.
                        tabButton.style.borderRightWidth = 1;
                        tabButton.style.borderTopLeftRadius = 3;
                        tabButton.style.borderTopRightRadius = 3;
                        tabButton.style.borderBottomLeftRadius = 0;
                        tabButton.style.borderBottomRightRadius = 0;
                        // Paddings come from ApplyTabButtonSize below, once the selected state is known — it
                        // is the horizontal pair AND the vertical size cue in one place.
                        // Tabs BUTT TOGETHER. The 2px that used to sit here looked like ~15px on screen,
                        // because the old 0.86 shrink on an inactive tab also ate ~6.5px off each of its
                        // sides — so a row of unselected tabs read as separate floating chips rather than one
                        // bar. The shrink is gone and the margin with it; the 1px seam above is what keeps
                        // neighbours legible, and the active cue is now fill, ink, and HEIGHT (see
                        // ApplyTabButtonSize) — height being the one size cue that costs no width.
                        tabButton.style.marginRight = 0;
                        // marginLeft/Top are NOT redundant: Unity's stock `.unity-button` USS carries its own
                        // 3px side margins, and zeroing only the right one leaves that 3px sitting between
                        // every pair of tabs — measured, after marginRight was already 0. It also pushed the
                        // FIRST tab 3px right of the strip's own left edge, so the bar started out of line
                        // with the panel beneath it. The export kit already zeroed all four; live never did.
                        tabButton.style.marginLeft = 0;
                        tabButton.style.marginTop = 0;
                        tabButton.style.marginBottom = 2; // breathing room when tabs wrap to multiple rows

                        builtTabs[index].style.display = (index == selectedIndex) ? DisplayStyle.Flex : DisplayStyle.None;
                        tabButton.style.backgroundColor = (index == selectedIndex) ? tabSelectedColor : tabIdleColor;
                        tabButton.style.color = (index == selectedIndex) ? tabTextOn : tabTextOff;
                        tabButton.style.borderRightColor = TabSeam(index == selectedIndex);
                        ApplyTabButtonSize(tabButton, index == selectedIndex, false);
                        // Pin the other three border colours inline even though they are ZERO width. They
                        // look impossible to see, but a rounded corner is tessellated from BOTH edges that
                        // meet there, and the top corners are rounded — so the top-right corner samples the
                        // TOP colour, which, left unset, falls through to the editor theme's
                        // `.unity-button:hover/:active/:focus` rules and gets repainted with the blue focus
                        // accent the instant the mouse goes down. That is the stray blue pixel in the corner
                        // of a tab you press. Only an INLINE value outranks a USS pseudo-state.
                        // Exact twin of the cure in ApplyLeftTabVisual — see
                        // TestLeftTabBordersCannotBeRepaintedByTheEditorTheme, which is where this was
                        // diagnosed the first time. The seam only made the tabs eligible for it.
                        tabButton.style.borderTopColor = Color.clear;
                        tabButton.style.borderLeftColor = Color.clear;
                        tabButton.style.borderBottomColor = Color.clear;

                        tabButton.clicked += () =>
                        {
                            ActivateTab(index);
                            // In the designer, clicking a tab also SELECTS its container so you can edit it or
                            // press Delete to remove it. Deferred a tick: selecting rebuilds the canvas (which
                            // destroys this button), so we let the click finish first.
                            if (designMode && dragController != null && tabNode != null)
                            {
                                var sel = tabNode;
                                tabButton.schedule.Execute(() => dragController.OnSelectNode?.Invoke(sel, false, false));
                            }
                        };

                        // Cross-tab drag affordance (design mode): the tab button is a drop target — drop a
                        // node on it to move it into this tab — and hovering it mid-drag spring-loads the tab
                        // so you can then drop precisely inside. userData = the tab container lets the shared
                        // controller resolve the drop to AsChild (append into this tab).
                        if (designMode && dragController != null && tabNode != null)
                        {
                            tabButton.userData = tabNode;
                            dragController.RegisterSpringTab(tabButton, () => ActivateTab(index));
                        }

                        // Rename a tab in place (design mode): double-click or F2 swaps the button for a text
                        // field; Enter/blur commits to the child's TabLabel, Escape cancels. The rebuild restores
                        // the button with the new label. (Only ContainerNode tabs carry a TabLabel.)
                        if (designMode && tabNode != null)
                        {
                            var renameTabNode = tabNode;
                            var persistEdit = _onLayoutEdited; // capture at build time — the static is restored when Build returns
                            void BeginTabRename()
                            {
                                if (tabButton.parent == null || tabButton.style.display == DisplayStyle.None) return;
                                int btnIndex = tabStrip.IndexOf(tabButton);
                                var renameField = new TextField { value = tabButton.text };
                                renameField.style.marginRight = 2;
                                renameField.style.minWidth = 60;
                                tabStrip.Insert(btnIndex, renameField);
                                tabButton.style.display = DisplayStyle.None;
                                bool done = false;
                                void Commit(bool cancel)
                                {
                                    if (done) return;
                                    done = true;
                                    if (!cancel)
                                    {
                                        string v = renameField.value?.Trim();
                                        // Same undo record as the Properties-panel twin
                                        // (SasquatchLayoutDesignerWindow's "Rename Tab"): without it Ctrl+Z
                                        // skips straight past this edit and pops the preceding unrelated one.
                                        // RecordObject (not RegisterCompleteObjectUndo) matches every other
                                        // designer edit op — a small in-place mutation diffs cheaply; the
                                        // complete-snapshot rule is for the SAVE/persist paths.
                                        if (layoutAsset != null)
                                            Undo.RecordObject(layoutAsset, "Rename Tab");
                                        renameTabNode.TabLabel = string.IsNullOrEmpty(v) ? $"Tab {index + 1}" : v;
                                    }
                                    // Full refresh so the rename also reflects in the organizer + hierarchy.
                                    if (dragController?.OnStructuralChange != null) dragController.OnStructuralChange();
                                    else (persistEdit ?? onRebuildRequired)?.Invoke();
                                }
                                renameField.RegisterCallback<KeyDownEvent>(e =>
                                {
                                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Commit(false); e.StopPropagation(); }
                                    else if (e.keyCode == KeyCode.Escape) { Commit(true); e.StopPropagation(); }
                                });
                                renameField.RegisterCallback<FocusOutEvent>(_ => Commit(false));
                                renameField.schedule.Execute(() => { renameField.Focus(); renameField.SelectAll(); });
                            }
                            tabButton.RegisterCallback<MouseDownEvent>(e => 
                            { 
                                if (e.clickCount == 2 && e.button == 0) 
                                { 
                                    BeginTabRename(); 
                                    e.StopPropagation(); 
                                } 
                            }, TrickleDown.TrickleDown);
                            
                            tabButton.RegisterCallback<PointerDownEvent>(e => 
                            { 
                                if (e.clickCount == 2 && e.button == 0) 
                                { 
                                    BeginTabRename(); 
                                    e.StopPropagation(); 
                                } 
                            }, TrickleDown.TrickleDown);

                            tabButton.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.F2) BeginTabRename(); });

                            // Right-click a tab header for Rename / Delete. Delete routes through the window's
                            // DeleteNode (handles selection + Undo + refreshes); falls back to an inline remove
                            // if no controller is wired (e.g. a design context without the drag controller).
                            // DESIGNER-ONLY by design (this whole block is gated on designMode): the live
                            // inspector's unified takeover never runs in the designer, so a manipulator is
                            // correct here — and in the live inspector tab pages get "Edit Tab …" via the
                            // quick-edit chain instead (rename lives in QuickEditPopup's tab-label row).
                            tabButton.AddManipulator(new ContextualMenuManipulator(evt =>
                            {
                                evt.menu.AppendAction("Rename Tab", _ => BeginTabRename());
                                evt.menu.AppendAction("Delete Tab", _ =>
                                {
                                    if (dragController?.OnDeleteNode != null)
                                    {
                                        dragController.OnDeleteNode(renameTabNode);
                                    }
                                    else
                                    {
                                        // The no-controller fallback (OnDeleteNode handles undo itself) —
                                        // third sibling of the Add/Rename records above, same reason.
                                        if (layoutAsset != null)
                                            Undo.RecordObject(layoutAsset, "Delete Tab");
                                        containerNode.Children?.Remove(renameTabNode);
                                        if (containerNode.ActiveTabIndex >= (containerNode.Children?.Count ?? 0))
                                            containerNode.ActiveTabIndex = Mathf.Max(0, (containerNode.Children?.Count ?? 1) - 1);
                                        (persistEdit ?? onRebuildRequired)?.Invoke();
                                    }
                                });
                            }));
                        }

                        tabStrip.Add(tabButton);
                    }

                    // Excel-style "+" tab (design mode only): appends a new tab, then persists + rebuilds.
                    // Also a DROP ZONE — dropping an item here is the ONLY way to create a new tab via drag,
                    // so you can't accidentally spawn tabs by dropping between existing ones.
                    if (designMode)
                    {
                        var persistAdd = _onLayoutEdited; // capture now — the static is restored when Build returns
                        var addTabButton = new Button(() =>
                        {
                            // Same undo record as the Properties-panel twin (SasquatchLayoutDesignerWindow's
                            // "Add Tab"). Without it the append into the [SerializeReference] graph is
                            // invisible to undo, and with Auto-Save off (the default) the next Ctrl+Z rolls
                            // the clone back to a state that predates the new tab AND whatever came before it.
                            if (layoutAsset != null)
                                Undo.RecordObject(layoutAsset, "Add Tab");
                            containerNode.Children ??= new List<LayoutNode>();
                            containerNode.Children.Add(new ContainerNode
                            {
                                Type = ContainerType.Rows,
                                TabLabel = $"Tab {containerNode.Children.Count + 1}" // names the tab WITHOUT forcing a header
                            });
                            // Full refresh so the new tab shows in the Properties organizer + hierarchy, not
                            // just the canvas re-bind that OnLayoutResizedFromPreview would do.
                            if (dragController?.OnStructuralChange != null) dragController.OnStructuralChange();
                            else (persistAdd ?? onRebuildRequired)?.Invoke();
                        }) { text = "+", tooltip = "Add a new tab (or drop an item here to put it in a new tab)" };
                        if (dragController != null) dragController.RegisterDropZone(addTabButton, containerNode);
                        addTabButton.style.borderTopWidth = 0;
                        addTabButton.style.borderBottomWidth = 0;
                        addTabButton.style.borderLeftWidth = 0;
                        addTabButton.style.borderRightWidth = 0;
                        addTabButton.style.borderTopLeftRadius = 3;
                        addTabButton.style.borderTopRightRadius = 3;
                        addTabButton.style.borderBottomLeftRadius = 0;
                        addTabButton.style.borderBottomRightRadius = 0;
                        addTabButton.style.paddingLeft = 9;
                        addTabButton.style.paddingRight = 9;
                        addTabButton.style.paddingTop = 4;
                        addTabButton.style.paddingBottom = 4;
                        addTabButton.style.marginLeft = 2;
                        // Match the tabs' own vertical margins. The strip is bottom-aligned now (so the
                        // active tab can be taller without dragging the rest up with it), which means the "+"
                        // sits on whatever margin it is given — and Unity's stock `.unity-button` USS hands
                        // it 3px where the tabs use 2, so left alone it floats 1px above the row it belongs to.
                        addTabButton.style.marginTop = 0;
                        addTabButton.style.marginBottom = 2;
                        addTabButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                        var addTabIdleBg = WidgetPalette.PanelWash;   // captured: callbacks stay on the built skin
                        var addTabIdleText = WidgetPalette.TabAddText;
                        addTabButton.style.backgroundColor = addTabIdleBg;
                        addTabButton.style.color = addTabIdleText;
                        addTabButton.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            addTabButton.style.backgroundColor = new Color(0.27f, 0.45f, 0.3f, 0.8f);
                            addTabButton.style.color = Color.white;
                        });
                        addTabButton.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            addTabButton.style.backgroundColor = addTabIdleBg;
                            addTabButton.style.color = addTabIdleText;
                        });
                        tabStrip.Add(addTabButton);
                    }

                    // Registered for the inspector search: lets the filter auto-activate the first tab
                    // holding a match and badge each tab button with its match count (same in-place
                    // ActivateTab the tab clicks use). containerElement is the node element here whether or
                    // not the Tabs container was wrapped in a collapsible group shell above.
                    TabControllerRegistry.Register(containerElement, new TabController(
                        ActivateTab,
                        () => currentTab,
                        (tabIndex, matchCount) =>
                        {
                            if (tabIndex < 0 || tabIndex >= builtTabs.Count) return;
                            var badgeBtn = tabStrip[tabIndex] as Button;
                            if (badgeBtn == null) return;
                            var badge = badgeBtn.Q<Label>(className: "sasq-tab-badge");
                            if (matchCount <= 0)
                            {
                                badge?.RemoveFromHierarchy();
                                return;
                            }
                            if (badge == null)
                            {
                                badge = new Label { pickingMode = PickingMode.Ignore };
                                badge.AddToClassList("sasq-tab-badge");
                                // Small count chip pinned to the button's top-right corner; absolute so the
                                // button's own text layout (and its 0.86 inactive scale) is untouched.
                                badge.style.position = Position.Absolute;
                                badge.style.top = -5;
                                badge.style.right = -3;
                                badge.style.fontSize = 9;
                                badge.style.paddingLeft = 4;
                                badge.style.paddingRight = 4;
                                badge.style.paddingTop = 1;
                                badge.style.paddingBottom = 1;
                                badge.style.borderTopLeftRadius = 7;
                                badge.style.borderTopRightRadius = 7;
                                badge.style.borderBottomLeftRadius = 7;
                                badge.style.borderBottomRightRadius = 7;
                                badge.style.backgroundColor = WidgetPalette.Accent;
                                badge.style.color = Color.white;
                                badgeBtn.Add(badge);
                            }
                            badge.text = matchCount.ToString();
                        },
                        builtTabs));
                }
                else if (containerNode.Type == ContainerType.Frame && containerNode.Frame.Shape == FrameShape.DiagonalSplit)
                {
                    // Split frame (the Tabs precedent): Children[0] fills Pane A, Children[1] Pane B. The
                    // builder never normalizes the model — extra/legacy children route defensively into
                    // Pane A; structure is only rewritten at edit points (FrameShapeMigration) with Undo.
                    var paneHostA = containerElement.Q<VisualElement>(FrameBuilder.PaneAName);
                    var paneHostB = containerElement.Q<VisualElement>(FrameBuilder.PaneBName);
                    if (containerNode.Children != null && paneHostA != null)
                    {
                        for (int i = 0; i < containerNode.Children.Count; i++)
                        {
                            var childNode = containerNode.Children[i];
                            var childElement = BuildNode(
                                childNode,
                                childBasePath,
                                serializedObject,
                                onRebuildRequired,
                                theme,
                                designMode,
                                selectedNode,
                                dragController,
                                layoutAsset,
                                designInteract,
                                nodePath: nodePath + "/" + i,
                                // Pane bindings render as column cells (see childIsColumnCell in the shared
                                // loop): a pane is a narrow offset region where the aligned label dies.
                                directChildrenAreCells: true
                            );
                            if (childElement == null) continue;
                            // A pane is its own label-column track (see FieldLabelColumn.ScopeClass) —
                            // the same reason its bindings render as column cells.
                            childElement.AddToClassList(FieldLabelColumn.ScopeClass);
                            // The pane subtree is CONTENT-SIZED, never stretched. It used to take
                            // flexGrow:1 for one reason — so its background reached the seam — and that
                            // one line is what made the whole split-grow measure ill-posed: the pane host
                            // is an absolute layer sized to the FRAME, so a stretched subtree's layout
                            // height IS the frame's height. GrowSplitToContent then measured the frame's
                            // own height back as "content", and pane content could ratchet the frame
                            // without bound (measured: 4,211,400px and still climbing). The background
                            // moves to the pane HOST below, which is already full-pane and clipped at the
                            // seam — same picture, none of the feedback.
                            //
                            // Still never SHRINKS below its content: the pane box is a fixed absolute
                            // layer, so shrink = crushed fields. Natural height in both directions is what
                            // lets GrowSplitToContent measure the true need in one pass (overflow is
                            // clipped by the pane clipper until the frame catches up).
                            childElement.style.flexShrink = 0f;
                            // MODEL index, deliberately — `i` is the loop over Children, not over what was
                            // built. Hiding Pane A's child therefore leaves Pane A EMPTY rather than sliding
                            // Pane B's content across the seam: the two panes are named places in the frame,
                            // and a split frame that silently reflowed into one pane would read as a bug.
                            var host = (i == 1 && paneHostB != null) ? paneHostB : paneHostA;
                            // Pane background rides the HOST (see the flexShrink note above). Cleared off
                            // the subtree so a content-sized subtree doesn't paint a short block of colour
                            // where the full-pane wash belongs.
                            if (childNode != null && childNode.Style.backgroundColor.isOverridden)
                            {
                                host.style.backgroundColor = childNode.Style.backgroundColor.value;
                                childElement.style.backgroundColor = Color.clear;
                            }
                            host.Add(childElement);
                            // Panes are structural (never dragged out) — hide the pane's own grip;
                            // containers only start drags from the grip, so this disables the drag.
                            // Direct children only: nested containers inside the pane keep theirs.
                            if (designMode)
                            {
                                foreach (var ce in childElement.Children())
                                    if (ce.ClassListContains("designer-grip")) ce.style.display = DisplayStyle.None;
                            }
                        }
                    }
                }
                else
                {
                    if (containerNode.Children != null)
                    {
                        // Frame content joins Columns/Grid on the column-cell field path: Unity's aligned
                        // label derives its width from the whole INSPECTOR width minus the field's x offset,
                        // so a shaped frame's inset/offset content crushes the label to nothing in the live
                        // inspector (and the designer canvas — no InspectorElement, aligned dead — shows a
                        // stale fixed-width label instead). The cell field (natural-width ellipsized label,
                        // field absorbs the squeeze, auto-stacks when narrow) fits shaped content the same
                        // way it fits a narrow column. directChildrenAreCells extends this to a split
                        // frame's pane containers (their Rows wrapper builds the actual bindings).
                        bool childIsColumnCell = containerNode.Type == ContainerType.Columns || containerNode.Type == ContainerType.Grid
                            || containerNode.Type == ContainerType.Frame
                            || directChildrenAreCells
                            // A Rows container OVER its track cap regroups into side-by-side column
                            // stacks — its children live in narrow columns where Unity's aligned
                            // label dies (label eats the width, value crushed), exactly the Columns
                            // situation. Under the cap they keep the classic aligned fields.
                            || (containerNode.Type == ContainerType.Rows
                                && containerNode.GridColumns > 0
                                && RenderableChildCount(containerNode, designMode) > containerNode.GridColumns);
                        // The cross-cell "blank spacer header" (eye-aligning a headerless cell with header'd
                        // siblings) is a single-ROW concept and only valid for Columns. A Grid has MANY rows, so
                        // computing it container-wide gives every headerless cell in a header-FREE row a phantom
                        // spacer — e.g. a Float row under a header'd Vector2/String row gets ~a header's worth of
                        // dead space above it, reading as a mysterious gap. So only Columns opts in; a Grid cell
                        // renders its own header (if any) and no phantom spacer.
                        bool columnsHasAnyHeader = containerNode.Type == ContainerType.Columns
                            && ColumnsContainerHasAnyHeader(containerNode, serializedObject, childBasePath, designMode);

                        for (int ci = 0; ci < containerNode.Children.Count; ci++)
                        {
                            var childNode = containerNode.Children[ci];
                            // A held-open cell renders as the very same "＋" placeholder an ABSENT child
                            // renders as — that indistinguishability is the whole point (see EmptyCellNode).
                            // It deliberately bypasses BuildNode, which would give it node styling and the
                            // full design-card chrome (border, selection badge, drag grip, resize handles) —
                            // none of which a placeholder may have. It IS stamped like any other child
                            // though: it occupies a real cell, so the userData-keyed passes that regroup
                            // tracks (CollectGridCells, RegroupRowsIntoColumns) must see it or its cell is
                            // stranded outside the regrouped rows. The drop snapshot skips it explicitly.
                            if (childNode is EmptyCellNode)
                            {
                                var cellEl = MakeEmptyCellElement(containerNode.Type, designMode);
                                cellEl.userData = childNode;
                                LayoutNodeRegistry.SetNode(cellEl, childNode);
                                contentTarget.Add(cellEl);
                                continue;
                            }
                            var childElement = BuildNode(
                                childNode,
                                childBasePath,
                                serializedObject,
                                onRebuildRequired,
                                theme,
                                designMode,
                                selectedNode,
                                dragController,
                                layoutAsset,
                                designInteract,
                                childIsColumnCell,
                                columnsHasAnyHeader,
                                nodePath: nodePath + "/" + ci
                            );
                            if (childElement != null)
                            {
                                // Same test, second consequence: a cell is ALSO its own label-column track.
                                // A binding cell builds its own hugging label (BuildColumnCellField) and never
                                // reaches the column engine, but a CONTAINER cell — a Rows stack dropped into
                                // a column, the usual way anyone fills one — carries ordinary field rows, and
                                // those must fit this track rather than the whole inspector's width.
                                if (childIsColumnCell)
                                {
                                    childElement.AddToClassList(FieldLabelColumn.ScopeClass);
                                    // ...and third: a cell in a STACKING parent must not flex-GROW.
                                    // BuildColumnCellField's no-label branch returns the bare field carrying
                                    // flexGrow:1 (see "the field simply fills the cell") — correct inside a
                                    // Columns/Grid ROW, where grow hands out spare WIDTH. But a Frame's content
                                    // host, a split frame's pane subtree and an over-cap Rows stack are all
                                    // COLUMNS, and there the very same declaration hands out spare HEIGHT: the
                                    // labelless cell swallows the whole frame's slack and its box stops
                                    // describing its content. That is fatal specifically inside a split frame,
                                    // because FrameBuilder.GrowSplitToContent derives the frame's height from
                                    // the lowest in-flow yMax in the pane — a stretched cell makes that measure
                                    // the IDENTITY (need == current height at ANY height), so the frame has no
                                    // fixed point, never converges, and any 1px of noise circulates forever:
                                    // MEASURED as a live 96↔97px flutter, 18 flips per 40 ticks, propagating up
                                    // the whole subtree from the cell to the InspectorElement. The oscillation
                                    // pin cannot save it either — a pure identity measure produces no signal to
                                    // detect. This is the third route into the circular-measure trap that
                                    // GrowSplitToContent's own comments document (sub.layout.height, then
                                    // absolute overlays, now flex-grown cells); the cure is the same each time,
                                    // which is to keep the measured box equal to real content.
                                    //
                                    // The paired flexShrink:0 on the pane subtree already asserts exactly this
                                    // contract ("never SHRINKS below its content ... lets GrowSplitToContent
                                    // measure the true need in one pass") — natural height needs BOTH halves,
                                    // and only shrink had been pinned. An AUTHORED flexGrow still wins: the user
                                    // asking a pane child to fill is a real thing to want, and the same
                                    // isOverridden guard the pane wiring uses keeps that intact.
                                    if (containerNode.Type != ContainerType.Columns
                                        && containerNode.Type != ContainerType.Grid
                                        && childNode != null
                                        && !childNode.Style.flexGrow.isOverridden)
                                    {
                                        childElement.style.flexGrow = 0f;
                                    }
                                }
                                contentTarget.Add(childElement);
                            }
                        }
                    }

                    // "Columns: N" is a real cap — the (N+1)th child starts a new wrap row (exactly
                    // what the drop preview's phantom row promises) instead of squeezing in as an
                    // ever-narrower extra column. Overflowing columns reuse the Grid row machinery
                    // (equal columns, "+" pad cells in the under-filled last row); the common ≤N
                    // case keeps the classic single row with weights + resize dividers.
                    bool columnsOverflow = containerNode.Type == ContainerType.Columns
                        && containerNode.GridColumns > 0
                        && RenderableChildCount(containerNode, designMode) > containerNode.GridColumns;

                    // "Rows: N" is the same kind of cap, transposed — the (N+1)th child starts a
                    // new COLUMN beside the first stack (children fill top-to-bottom, then wrap
                    // right), exactly what the drop preview's new-column cells promise.
                    bool rowsOverflow = containerNode.Type == ContainerType.Rows
                        && containerNode.GridColumns > 0
                        && RenderableChildCount(containerNode, designMode) > containerNode.GridColumns;

                    // One rhythm for every section in a VERTICAL stack — Rows, a Card's body, a Foldout's content
                    // and a (non-split) Frame's content host. Columns/Grid are excluded because their children
                    // sit side by side: a vertical margin there is not the gap between them, and both already
                    // own their spacing (weights + dividers, or the row regroup). An over-cap Rows regroups into
                    // side-by-side stacks for the same reason, so it opts out too. Must precede ApplyGap.
                    if ((containerNode.Type == ContainerType.Rows && !rowsOverflow)
                        || containerNode.Type == ContainerType.Card
                        || containerNode.Type == ContainerType.Foldout
                        || containerNode.Type == ContainerType.Frame)
                    {
                        ApplySectionGaps(contentTarget, theme, designMode);
                    }

                    if ((containerNode.Type == ContainerType.Rows || containerNode.Type == ContainerType.Columns)
                        && !columnsOverflow && !rowsOverflow) // the regroups supply their own gap
                    {
                        ApplyGap(contentTarget, containerNode.Type, containerNode.Gap);
                    }

                    // Columns: weight-based widths (default equal) + drag-to-resize dividers between them.
                    if (containerNode.Type == ContainerType.Columns)
                    {
                        if (columnsOverflow)
                        {
                            bool isRoot = layoutAsset != null && containerNode == layoutAsset.Root;
                            contentTarget.style.flexDirection = FlexDirection.Column;
                            contentTarget.style.flexWrap = Wrap.NoWrap;
                            contentTarget.style.alignItems = Align.Stretch; // rows span the full width
                            // Resizable follows the same setting as the single-row case; the shared
                            // GridColumnWeights keep the columns aligned across the wrapped rows.
                            RegroupGridIntoRows(contentTarget, containerNode, CollectGridCells(contentTarget),
                                containerNode.GridColumns, TracksResizable(containerNode, isRoot), false,
                                _onLayoutEdited, designMode);
                            // Breathing room between wrapped rows even at Gap 0 — butted rows read
                            // as one mangled row (a Vec3 crammed under a Float).
                            // Direct children only: a Query would grab a nested grid's rows too.
                            if (containerNode.Gap <= 0f)
                            {
                                var wrapRows = new List<VisualElement>();
                                foreach (var rowCh in contentTarget.Children())
                                    if (rowCh.ClassListContains("sasq-grid-row")) wrapRows.Add(rowCh);
                                for (int wr = 0; wr < wrapRows.Count - 1; wr++)
                                    wrapRows[wr].style.marginBottom = 6f;
                            }
                        }
                        else
                        {
                            bool isRoot = layoutAsset != null && containerNode == layoutAsset.Root;
                            ApplyColumnsLayout(contentTarget, _onLayoutEdited, TracksResizable(containerNode, isRoot));
                        }
                    }
                    // Rows: per-row explicit heights (default auto) + a drag-to-resize divider under each row.
                    else if (containerNode.Type == ContainerType.Rows)
                    {
                        if (rowsOverflow)
                        {
                            // Over the cap: regroup into side-by-side column stacks. Row resizing
                            // survives the regroup — same per-node RowHeight + divider-under-each-
                            // row treatment as the single stack, just inside each column.
                            bool isRoot = layoutAsset != null && containerNode == layoutAsset.Root;
                            RegroupRowsIntoColumns(contentTarget, containerNode, designMode,
                                TracksResizable(containerNode, isRoot), _onLayoutEdited);
                        }
                        else
                        {
                            bool isRoot = layoutAsset != null && containerNode == layoutAsset.Root;
                            ApplyRowsLayout(contentTarget, _onLayoutEdited, TracksResizable(containerNode, isRoot), designMode);
                        }
                    }
                    // Grid: regroup the flat cells into a column-stack of explicit Row containers, sized by flex
                    // weight, with in-flow column/row dividers that resize when the grid is resizable.
                    else if (containerNode.Type == ContainerType.Grid)
                    {
                        ApplyGridLayout(contentTarget, containerNode, _onLayoutEdited, designMode);
                    }
                }

                // Design-mode empty / under-filled state. When a Columns or Rows container has an explicit
                // track count (GridColumns) larger than its child count, render that many *empty drop slots*
                // so you can lay out an empty container first and drag fields into the visible cells. Slots
                // are visual-only (no userData), so the drag controller still targets the parent container
                // and fills them in order. With no track count, fall back to the single "Empty Container" hint.
                if (designMode && contentTarget != null) // null contentTarget = split Frame (panes host their own hints)
                {
                    int childCount = containerNode.Children?.Count ?? 0;
                    int slotCount = (containerNode.Type == ContainerType.Columns || containerNode.Type == ContainerType.Rows)
                        ? containerNode.GridColumns
                        : 0;

                    if (slotCount > childCount)
                    {
                        for (int s = 0; s < slotCount - childCount; s++)
                            contentTarget.Add(MakeEmptyDropSlot(containerNode.Type));
                    }
                    else if (containerNode.Type == ContainerType.Grid
                             && containerNode.GridConstraint != GridConstraint.Flexible
                             && childCount == 0)
                    {
                        // Empty fixed-constraint grid: visible "+" cells for the declared tracks so
                        // the grid reads as a grid before the first drop — a fixed COLUMN count
                        // shows one row of N cells; a fixed ROW count shows N stacked cells (its
                        // reserved row tracks). Flexible has no stable track count when empty, so
                        // it keeps the hint below. Every batch is wrapped in .sasq-grid-row so any
                        // later regroup discards it like other row chrome.
                        bool fixedRowTracks = containerNode.GridConstraint == GridConstraint.FixedRowCount;
                        int trackCount = Mathf.Max(1, containerNode.GridColumns);
                        if (fixedRowTracks)
                        {
                            for (int s = 0; s < trackCount; s++)
                            {
                                var slotBand = new VisualElement();
                                slotBand.AddToClassList("sasq-grid-row");
                                slotBand.style.flexDirection = FlexDirection.Row;
                                slotBand.Add(MakeEmptyDropSlot(ContainerType.Grid));
                                contentTarget.Add(slotBand);
                            }
                        }
                        else
                        {
                            var slotRow = new VisualElement();
                            slotRow.AddToClassList("sasq-grid-row");
                            slotRow.style.flexDirection = FlexDirection.Row;
                            for (int s = 0; s < trackCount; s++)
                                slotRow.Add(MakeEmptyDropSlot(ContainerType.Grid));
                            contentTarget.Add(slotRow);
                        }
                    }
                    else if (childCount == 0)
                    {
                        var emptyHint = MakeEmptyContainerHint();
                        // An empty Tabs body is a real drop target, not just a caption: dropping on
                        // the hint creates a tab holding everything in hand — the same zone the "+"
                        // button registers, so single AND multi-token drops share its commit path
                        // (a batch becomes ONE new tab).
                        if (containerNode.Type == ContainerType.Tabs && dragController != null)
                            dragController.RegisterDropZone(emptyHint, containerNode);
                        contentTarget.Add(emptyHint);
                    }
                }

                element = containerElement;
            }
            else if (node is BindingNode bindingNode)
            {
                // An EMPTY PropertyPath inside a scope binds the thing the scope ALREADY points at, rather
                // than a field under it — for a collection's item layout that is the ELEMENT itself. It is
                // the only way to bind (and therefore style) a list whose elements are atomic: a
                // List<Vector2> has no sub-field to hang a widget on, so without this the elements are
                // stuck at whatever Unity draws by default. basePath is already the element's own property
                // path, so the binding simply IS it — the concatenation below would produce a trailing dot
                // and a dead lookup, which is exactly why the Table's "Value" column had to be synthesized
                // outside this method and could never carry authored styling.
                // At ROOT scope an empty path still means nothing is bound, and keeps reading as dead.
                bool elementSelf = string.IsNullOrEmpty(bindingNode.PropertyPath) && !string.IsNullOrEmpty(basePath);
                string resolvedPath = elementSelf
                    ? basePath
                    : string.IsNullOrEmpty(basePath)
                        ? bindingNode.PropertyPath
                        : $"{basePath}.{bindingNode.PropertyPath}";

                // Kept for the DrawerVisibilityBridge wiring further down, which must run on the FINAL
                // (possibly wrapped) element — the same one ConditionBinder binds — and so cannot live here.
                boundFieldPath = resolvedPath;

                // Dead binding (field deleted/renamed in code while auto-sync hasn't run): Unity-like,
                // the field simply doesn't render live; the designer shows a red-tinted placeholder so
                // the stale node stays discoverable (and selectable/deletable via the common block below).
                // Exception: a field that EXISTS in code but is a plain unserializable BCL collection
                // (Dictionary/SortedDictionary/HashSet/SortedSet — Unity can't serialize it, so FindProperty
                // stays null forever) gets a conversion guidance card in BOTH modes.
                // Tested on the RESOLVED path, not the node's own: an element-self binding has an empty
                // PropertyPath but a real path to check, and a root-scope empty path resolves to "" and so
                // still skips this branch exactly as before.
                if (!string.IsNullOrEmpty(resolvedPath) && serializedObject.FindProperty(resolvedPath) == null)
                {
                    var bindTarget = serializedObject.targetObject;
                    var deadField = bindTarget != null
                        ? BindingFactory.GetFieldInfoFromPropertyPath(bindTarget.GetType(), resolvedPath, out _)
                        : null;
                    if (deadField != null && SasquatchReflectionUtility.IsPlainUnserializableCollection(deadField.FieldType))
                    {
                        // Both modes honor the per-user "don't show this again" dismissal (devs add plain
                        // Dictionaries for access all the time, knowing they won't draw — the tip must not nag
                        // on every one). The designer canvas is WYSIWYG for the inspector, so it hides the card
                        // too; the invisible node stays in the layout data and reappears if tips are re-enabled
                        // (Project Settings ▸ Sasquatch Inspector).
                        element = ShowCollectionConversionTips
                            ? MakeUnserializableCollectionCard(deadField)
                            : null; // dismissed: the field simply doesn't render, exactly like stock Unity
                    }
                    else
                    {
                        element = designMode ? MakeMissingBindingPlaceholder($"Missing property: '{resolvedPath}'") : null;
                    }
                }
                else
                {
                    // An ELEMENT TEMPLATE bound to an array element takes the row's own label rule: the row
                    // already carries its [index], and the property's display name for an element is the
                    // word "Element" plus that same index — so an unlabelled template rendered
                    // "Element 0" directly beside "[0]". Same duplication the in-line editor rows hit, and
                    // the same fix, except an explicit Label override still wins (naming the value
                    // "Waypoint" is a real thing to want). A trailing ']' IS Unity's array-element path
                    // grammar, so this asks exactly the right question and stays false for a scoped
                    // container's self-binding, whose label is its struct's name and worth keeping.
                    bool? elementRowLabel = elementSelf && resolvedPath.EndsWith("]")
                        ? (bool?)(bindingNode.EffectiveLabelOverride == null)
                        : null;
                    element = tableCell
                        // A table cell hides its label unconditionally — the column header carries it — and
                        // never grows its own label column. designMode/designInteract still travel through:
                        // the designer's representative row is a table cell too, and its widgets must build
                        // in their design-mode form (inert inputs, template previews) like every other card.
                        ? BindingFactory.CreateBindingField(bindingNode, resolvedPath, serializedObject,
                            forceHideLabel: true, designMode: designMode, designInteract: designInteract,
                            // The ONLY site that may pass this true. Affix suppression ("Show in Column
                            // Header") keys off it, and this is the one cell shape that actually HAS a header.
                            isTableCell: true)
                        : columnCell
                            ? BuildColumnCellField(bindingNode, resolvedPath, serializedObject, parentColumnsHasAnyHeader, designMode)
                            : BindingFactory.CreateBindingField(bindingNode, resolvedPath, serializedObject,
                                forceHideLabel: elementRowLabel, designMode: designMode, designInteract: designInteract);
                    // In a table cell validation paints the red outline in place: there is no room under a row
                    // for a HelpBox, and a wrapper would break the cell/field identity the column fit measures.
                    element = ValidationBinder.Attach(element, bindingNode, resolvedPath, serializedObject, designMode, isTableCell: tableCell);
                    // Outside validation's wrapper, so the graph sits below the validation message rather than
                    // between the field and its own error text.
                    element = LiveDebug.DebugRowBinder.Attach(element, bindingNode, resolvedPath, serializedObject, designMode, isTableCell: tableCell);
                    if (!columnCell && !tableCell)
                    {
                        NormalizeFullRowBinding(element);
                    }
                }
            }
            else if (node is CollectionNode collectionNode)
            {
                string resolvedPath = string.IsNullOrEmpty(basePath)
                    ? collectionNode.PropertyPath
                    : $"{basePath}.{collectionNode.PropertyPath}";

                element = BuildCollectionElement(
                    collectionNode,
                    resolvedPath,
                    serializedObject,
                    onRebuildRequired,
                    theme,
                    designMode,
                    selectedNode,
                    dragController,
                    layoutAsset,
                    designInteract
                );
                // Null = dead collection hidden in live mode (see BuildCollectionElement) — skip wrapping.
                if (element != null)
                {
                    element = ValidationBinder.Attach(element, collectionNode, resolvedPath, serializedObject, designMode);
                }
            }
            else if (node is ButtonNode buttonNode)
            {
                element = BuildButtonElement(buttonNode, serializedObject, layoutAsset, onRebuildRequired);
            }
            else if (node is PropertyNode propertyNode)
            {
                element = BuildPropertyElement(propertyNode, serializedObject);
            }
            else if (node is HelpBoxNode helpBoxNode)
            {
                element = BuildHelpBoxElement(helpBoxNode, designMode ? null : FoldoutStateStore.Key(
                    serializedObject.targetObject, nodePath,
                    helpBoxNode.EffectiveSummaryOverride ?? helpBoxNode.MessageType.ToString()));
            }
            else if (node is ScriptHeaderNode scriptHeaderNode)
            {
                element = BuildScriptHeaderElement(scriptHeaderNode, serializedObject, designMode, onRebuildRequired);
            }
            else if (node is SpaceNode spaceNode)
            {
                element = BuildSpaceElement(spaceNode, designMode);
            }
            else if (node is LabeledDividerNode dividerNode)
            {
                element = BuildLabeledDividerElement(dividerNode, designMode);
            }
            else if (node is ImageBannerNode bannerNode)
            {
                element = BuildImageBannerElement(bannerNode, designMode);
            }
            else if (node is TextBlockNode textBlockNode)
            {
                element = BuildTextBlockElement(textBlockNode, designMode);
            }
            else if (node is EmbeddedEditorNode embeddedNode)
            {
                element = EmbeddedEditorHost.Build(embeddedNode, serializedObject, designMode);
            }
#if SASQ_CUSTOM_WIDGET
            else if (node is CustomWidgetNode customWidgetNode)
            {
                element = CustomWidgetNodeHost.Build(customWidgetNode, serializedObject, designMode, designInteract);
            }
#endif

            if (element != null)
            {
                // userData for the designer's drag/drop + grid systems; the registry for the live
                // quick menu — Unity's controls/binding can overwrite userData on live elements,
                // the registry they cannot touch (see LayoutNodeRegistry).
                element.userData = node;
                LayoutNodeRegistry.SetNode(element, node);
                // The asset + SerializedObject this node belongs to, so the quick-edit menu can route an
                // edit to its OWNING layout (nested inline editors build through here with their own
                // nestedLayout/subSO; collection ItemLayouts keep the root's asset/SO). See LayoutOwnerRegistry.
                LayoutOwnerRegistry.SetScope(element, new LayoutOwnerScope(layoutAsset, serializedObject));
                StyleApplier.Apply(element, node, theme);
                // Section color-coding runs AFTER StyleApplier so the tinted header/body/rail can't be clobbered.
                // Section Tint paints the header/body wash (plus a rail); the Accent Stripe is applied LAST so its
                // rail color wins when BOTH are set — the stripe is the deliberate accent, so it owns the left edge.
                // hasRail marks the 3px colored left border as intentional, so the design-card border logic
                // below must leave it alone — otherwise the stripe is invisible in Design mode.
                bool hasRail = false;
                // Frames opt out of Section Tint / Accent Stripe: the rail is a hard left border that would
                // corrupt a circle/pill ring, and the tint's header/body class queries don't exist on a frame.
                if (node is ContainerNode tintNode && tintNode.SectionTintColor.isOverridden
                    && tintNode.Type != ContainerType.Frame)
                {
                    ApplySectionTint(element, tintNode);
                    hasRail = true;
                }
                if (node is ContainerNode stripeNode && stripeNode.AccentStripeColor.isOverridden
                    && stripeNode.Type != ContainerType.Frame)
                {
                    ApplyCardAccentStripe(element, stripeNode);
                    hasRail = true;
                }
                ApplyNodeTextStyles(element, node);
                SuppressDuplicateContainerHeaderDecorator(element, node);
                AttachNodeResizeHandles(element, node, designMode);

                if (node is ContainerNode cn && cn.Type == ContainerType.Columns)
                {
                    var columnsRow = element.Q<VisualElement>(className: "sasq-columns-content-target");
                    if (columnsRow != null)
                    {
                        // Over-count columns wrapped into .sasq-grid-row rows: the content target is
                        // then a COLUMN stack, so vertical alignment applies WITHIN each row (the
                        // stack itself stays stretched, or rows would shrink-wrap horizontally).
                        bool wrapped = false;
                        foreach (var rowCh in columnsRow.Children())
                        {
                            if (!rowCh.ClassListContains("sasq-grid-row")) continue;
                            wrapped = true;
                            if (cn.Style.alignItems.isOverridden)
                                rowCh.style.alignItems = element.style.alignItems;
                        }
                        if (!wrapped)
                        {
                            if (cn.Style.alignItems.isOverridden)
                            {
                                columnsRow.style.alignItems = element.style.alignItems;
                            }
                            if (cn.Style.justify.isOverridden)
                            {
                                columnsRow.style.justifyContent = element.style.justifyContent;
                            }
                        }

                        // Revert wrapper element's alignment styles to default so they only affect the inner content row
                        element.style.alignItems = Align.Stretch;
                        element.style.justifyContent = Justify.FlexStart;
                    }
                }
                else if (node is ContainerNode rn && rn.Type == ContainerType.Rows)
                {
                    // Rows is a single COLUMN container. `alignItems` (set by StyleApplier from the node's
                    // "Horizontal Alignment") aligns the fields horizontally on the cross axis — keep it.
                    // `justify` is repurposed for the TITLE's text-align (applied above), so reset the
                    // container's main-axis (vertical) distribution that StyleApplier set from it, otherwise an
                    // overridden justify would shove all the fields down/centered.
                    element.style.justifyContent = Justify.FlexStart;
                }
                else if (node is ContainerNode gn && gn.Type == ContainerType.Grid)
                {
                    // Reset Grid's justifyContent post-build so `justify` only drives the title text alignment,
                    // not the horizontal distribution of the grid cells.
                    element.style.justifyContent = Justify.FlexStart;
                }
                else if (node is ContainerNode fn && fn.Type == ContainerType.Frame)
                {
                    // Runs after StyleApplier by design: moves padding onto the content host (the absolute
                    // fill/scrim layers must stay edge-to-edge), transfers alignItems/justify onto the content
                    // host (the Columns precedent above), and registers the shape geometry pass. The seed key
                    // lets a rebuilt split frame start at its previously converged height (rebuild-jerk fix).
                    FrameBuilder.PostStyleFixup(element, fn, FrameBuilder.SplitSeedKey(serializedObject, nodePath));
                }

                if (designMode)
                {
                    // Wrap leaf elements (Bindings, Buttons, HelpBoxes) in a designer card wrapper
                    // to prevent PropertyField binding clearing children, and pointer capture conflicts.
                    if (!(node is ContainerNode) && !(node is CollectionNode))
                    {
                        var wrapper = new VisualElement();
                        wrapper.userData = node;
                        LayoutNodeRegistry.SetNode(wrapper, node);
                        wrapper.style.flexGrow = 1f;

                        // Transfer layout properties to wrapper so it positions correctly
                        wrapper.style.marginTop = element.style.marginTop;
                        wrapper.style.marginBottom = element.style.marginBottom;
                        wrapper.style.marginLeft = element.style.marginLeft;
                        wrapper.style.marginRight = element.style.marginRight;
                        wrapper.style.flexGrow = element.style.flexGrow;
                        wrapper.style.flexShrink = element.style.flexShrink;
                        wrapper.style.width = element.style.width;
                        // HEIGHT is deliberately NOT copied. The element keeps its own height, and an
                        // auto-height wrapper hugs it — so a height written to the element mid-drag (the
                        // resizable text box's bottom grip, which writes textField.style.height on every
                        // PointerMove) is visible IMMEDIATELY. Copying it pinned the card to the BUILD-TIME
                        // height: the field could not grow past the card, the canvas never reflowed, and the
                        // whole resize only appeared when the release-rebuild remade the card ("dragging the
                        // text box does nothing until you let go"). MakeHeightResizable nodes were already
                        // correct for exactly this reason — StyleApplier routes their height to min-height,
                        // so there was never a fixed height here to copy. Width still needs the copy: without
                        // it the selection card stretches full-width around a width-constrained widget.
                        wrapper.style.alignSelf = element.style.alignSelf;

                        // A ButtonNode's entire look is inline chrome (background/border/radius from
                        // ApplyButtonChrome). Transferring it to the wrapper and clearing it off the button would
                        // revert the button to Unity's stock gray look in Design mode. So for a button we KEEP the
                        // chrome on the element and let it sit inside its (transparent) selection card; every other
                        // leaf still transfers its box styling to the wrapper to avoid double-drawing.
                        bool keepInlineChrome = node is ButtonNode;

                        if (!keepInlineChrome)
                        {
                            // Transfer borders, background, and radius to wrapper so they render on the card container
                            wrapper.style.borderTopWidth = element.style.borderTopWidth;
                            wrapper.style.borderBottomWidth = element.style.borderBottomWidth;
                            wrapper.style.borderLeftWidth = element.style.borderLeftWidth;
                            wrapper.style.borderRightWidth = element.style.borderRightWidth;

                            wrapper.style.borderTopColor = element.style.borderTopColor;
                            wrapper.style.borderBottomColor = element.style.borderBottomColor;
                            wrapper.style.borderLeftColor = element.style.borderLeftColor;
                            wrapper.style.borderRightColor = element.style.borderRightColor;

                            wrapper.style.borderTopLeftRadius = element.style.borderTopLeftRadius;
                            wrapper.style.borderTopRightRadius = element.style.borderTopRightRadius;
                            wrapper.style.borderBottomLeftRadius = element.style.borderBottomLeftRadius;
                            wrapper.style.borderBottomRightRadius = element.style.borderBottomRightRadius;

                            wrapper.style.backgroundColor = element.style.backgroundColor;
                        }

                        // Clear layout props from element to avoid double positioning (all leaves).
                        element.style.marginTop = 0;
                        element.style.marginBottom = 0;
                        element.style.marginLeft = 0;
                        element.style.marginRight = 0;
                        element.style.flexGrow = 1f;

                        if (!keepInlineChrome)
                        {
                            // Clear the box decoration off the element (now on the wrapper).
                            element.style.borderTopWidth = 0;
                            element.style.borderBottomWidth = 0;
                            element.style.borderLeftWidth = 0;
                            element.style.borderRightWidth = 0;
                            element.style.borderTopLeftRadius = 0;
                            element.style.borderTopRightRadius = 0;
                            element.style.borderBottomLeftRadius = 0;
                            element.style.borderBottomRightRadius = 0;
                            element.style.backgroundColor = StyleKeyword.Null;
                        }

                        element.userData = null; // Prevent double binding of conditions or callbacks
                        LayoutNodeRegistry.SetNode(element, null); // keep registry in lockstep

                        // Disable interactive fields/inputs in Design Mode so a click only SELECTS the card.
                        // Skipped in Interact mode so the fields can be edited (e.g. to test conditions).
                        // Persistent (not one-shot): numeric inner fields build lazily during bind, and the
                        // In-Line Editor / [SerializeReference] widgets embed a nested inspector that builds and
                        // REBUILDS over many frames — re-applying on every layout change catches them all.
                        if (!designInteract)
                        {
                            SuppressInteractionPersistently(element);
                        }

                        wrapper.Add(element);
                        element = wrapper;
                    }

                    element.AddToClassList("designer-card");
                    // Captured for the frame-interior chrome trim below: true = the 1px border being
                    // added exists ONLY to host the selection/hover outline (the node authored none).
                    bool hairlineSelectionBorder = element.style.borderTopWidth.keyword == StyleKeyword.Null;
                    if (element.style.borderTopWidth.keyword == StyleKeyword.Null)
                    {
                        element.style.borderTopWidth = 1;
                        element.style.borderBottomWidth = 1;
                        element.style.borderRightWidth = 1;
                        // This 1px border exists ONLY to host the card's selection/hover outline. The node
                        // set no border width, so it has NO border in the live inspector — drop any inline
                        // border color (a color override applied by StyleApplier) so we don't paint a phantom
                        // colored border the inspector never shows. Clearing to Null (not an inline transparent
                        // color) lets USS drive it: .designer-card keeps it transparent at rest, while
                        // :hover / .designer-card-selected still light it blue.
                        element.style.borderTopColor = StyleKeyword.Null;
                        element.style.borderBottomColor = StyleKeyword.Null;
                        element.style.borderRightColor = StyleKeyword.Null;
                        // Keep the Section Tint / Accent Stripe rail (3px colored left border) — it's intentional,
                        // so leave the left width AND color alone; otherwise the stripe vanishes in Design mode.
                        if (!hasRail)
                        {
                            element.style.borderLeftWidth = 1;
                            element.style.borderLeftColor = StyleKeyword.Null;
                        }
                    }
                    else if (element.style.borderTopWidth.value > 0 && element.style.borderTopColor.keyword == StyleKeyword.Null)
                    {
                        // Default visible border color for custom border widths if color is unset
                        element.style.borderTopColor = WidgetPalette.BorderSoft;
                        element.style.borderBottomColor = WidgetPalette.BorderSoft;
                        element.style.borderRightColor = WidgetPalette.BorderSoft;
                        // Don't overwrite the accent rail's color (see above).
                        if (!hasRail) element.style.borderLeftColor = WidgetPalette.BorderSoft;
                    }

                    if (element.style.borderTopLeftRadius.keyword == StyleKeyword.Null)
                    {
                        element.style.borderTopLeftRadius = 4;
                        element.style.borderTopRightRadius = 4;
                        element.style.borderBottomLeftRadius = 4;
                        element.style.borderBottomRightRadius = 4;
                    }
                    element.style.marginBottom = 8;

                    // A canvas card must NEVER flex-grow in its stack. The drag-preview gap opens by
                    // spring-growing the receiving container's MIN-HEIGHT (GapAnimator), and vertical-flex
                    // slack lands on whichever child has flex-grow — that card silently inflates mid-drag
                    // (Tag Chips and the Embedded Editor ship flexGrow:1 on their widget roots, and the
                    // leaf wrap copies a widget's inline flexGrow onto the card), every card below shifts
                    // off the frozen drop snapshot, and the insertion line cuts through cards. Zeroed HERE,
                    // the one point every card passes; parents that own their children's flex re-apply it
                    // AFTER this (Columns/Grid weights, split-frame pane fill), so only vertical stack-slack
                    // absorption is removed.
                    element.style.flexGrow = 0f;

                    // WYSIWYG inside frames: a frame is a FIXED-width clipped shape, so the card
                    // chrome's 20px grip gutter (+1px selection borders) eats real interior width —
                    // cells rendered ~22px narrower on the canvas than in the live inspector, clipping
                    // strings the inspector shows fine. Inside a frame the gutter is dropped (only when
                    // it came from USS — an inline value is node-authored padding that renders live
                    // too) and the hairline selection borders are zeroed: hover/selection feedback
                    // comes from the overlay ring, which is absolute and costs no width, and the grip —
                    // already absolutely positioned — simply overlays the content's left edge, exactly
                    // like the frame root's own grip does. Node-authored borders stay (they consume
                    // width in the live inspector too, so they're already WYSIWYG). Attach-time: the
                    // ancestor chain doesn't exist yet at build time; idempotent on re-attach.
                    var chromeCard = element;
                    bool trimHairline = hairlineSelectionBorder;
                    bool keepRail = hasRail;
                    chromeCard.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        for (var p = chromeCard.parent; p != null; p = p.parent)
                        {
                            if (!p.ClassListContains(FrameBuilder.FrameClass)) continue;
                            if (chromeCard.style.paddingLeft.keyword != StyleKeyword.Undefined)
                                chromeCard.style.paddingLeft = 0f; // kill the USS gutter, keep node padding
                            if (trimHairline)
                            {
                                chromeCard.style.borderTopWidth = 0f;
                                chromeCard.style.borderBottomWidth = 0f;
                                chromeCard.style.borderRightWidth = 0f;
                                if (!keepRail) chromeCard.style.borderLeftWidth = 0f;
                            }
                            return;
                        }
                    });

                    // A TABLE CELL is the frame case again, and worse: a column is only as wide as its header,
                    // and the card chrome's 20px grip gutter + 1px hairlines are width the live Inspector never
                    // spends — four columns lost ~84px of the row to chrome, which is most of why the canvas
                    // table read as squeezed while the real one was roomy. Trim exactly what the frame interior
                    // trims: the grip is absolutely positioned, so it simply overlays the field's left edge, and
                    // hover/selection feedback still comes from the overlay ring (absolute, costs no width). The
                    // card's stack margin goes too — a table row is a ROW, and 8px under every cell only made
                    // the representative row taller than the one it is previewing. Applied here rather than at
                    // attach: a cell knows what it is at build time, and the hairline/rail facts are in scope.
                    if (tableCell)
                    {
                        if (element.style.paddingLeft.keyword != StyleKeyword.Undefined)
                            element.style.paddingLeft = 0f;
                        element.style.marginBottom = 0f;
                        if (hairlineSelectionBorder)
                        {
                            element.style.borderTopWidth = 0f;
                            element.style.borderBottomWidth = 0f;
                            element.style.borderRightWidth = 0f;
                            if (!hasRail) element.style.borderLeftWidth = 0f;
                        }
                    }

                    // Hover/selection feedback is LAYERED so it's always visible — even when a binding carries
                    // its own outline (a custom inline border colour, which beats a :hover USS rule):
                    //   1. The card border keeps the classic blue hover/selection outline (DesignerCanvas.uss).
                    //   2. A separate inset RING draws on top in a BOLD colour baked inline here. The colour
                    //      adapts to the card's background — RingHighlightColor contrasts against it — so the
                    //      ring is never hidden behind the field; it's the always-visible layer for outlined
                    //      cards. USS toggles only the ring's VISIBILITY (opacity) on hover/selected, never its
                    //      colour, so the colour is free to be per-card.
                    //   3. A custom outline colour itself lightens while hovered (below) for extra feedback.
                    // The ring is PickingMode.Ignore (out of drag/click hit-testing) and dies with the card on
                    // rebuild. Added before the badge/grip so those draw on top of it.
                    var cardBg = element.style.backgroundColor;
                    Color? ringBg = (cardBg.keyword != StyleKeyword.Null && cardBg.value.a > 0.01f)
                        ? cardBg.value
                        : (Color?)null;

                    // CONTAINERS get a fourth layer and a different ring rule, because a container is the one
                    // card whose own box styling routinely swallows the mark: a black background + black border
                    // beats the USS hover/selected border (an inline colour wins), which left only the adaptive
                    // ring — and RingHighlightColor derives that ring FROM the background, so a black container's
                    // ring came out grey-white. That reads as "something is glowing here", not "this is selected".
                    //   4. A barely-opaque WASH over the entire container (below), which no card colour can hide.
                    // The ring joins the wash's hue instead of the background's, so the two never disagree, and
                    // SelectionAccentFor moves BOTH off blue when the container itself is blue. Leaves (bindings,
                    // buttons, help boxes …) are untouched — they keep the adaptive ring exactly as before.
                    var cardBorder = element.style.borderTopColor;
                    Color? ringBorder = (cardBorder.keyword != StyleKeyword.Null && cardBorder.value.a > 0.01f)
                        ? cardBorder.value
                        : (Color?)null;
                    // The root container is excluded: its card wraps the whole canvas, so a full-surface tint is
                    // a lot of blue for a selection that is already unmistakable (nothing else is outlined).
                    bool washesOnSelect = node is ContainerNode && node != layoutAsset?.Root;
                    Color selectionAccent = SelectionAccentFor(ringBg, ringBorder);
                    Color ringColor = washesOnSelect ? selectionAccent : RingHighlightColor(ringBg);

                    if (washesOnSelect)
                    {
                        // Added AFTER the content (a container's children are already in the tree by now) so the
                        // wash lies over the whole box, and BEFORE the ring/grip/badge so those stay crisp on top
                        // of it. Absolute + Ignore: costs no layout and never enters drag/click hit-testing.
                        // USS owns only its VISIBILITY; the colour is inline per-card, like the ring's.
                        var wash = new VisualElement { name = "designer-selection-wash", pickingMode = PickingMode.Ignore };
                        wash.AddToClassList("designer-selection-wash");
                        wash.style.backgroundColor = SelectionWashFor(selectionAccent);
                        wash.style.borderTopLeftRadius = element.style.borderTopLeftRadius;
                        wash.style.borderTopRightRadius = element.style.borderTopRightRadius;
                        wash.style.borderBottomLeftRadius = element.style.borderBottomLeftRadius;
                        wash.style.borderBottomRightRadius = element.style.borderBottomRightRadius;
                        // The badge reads the accent back off here (AttachSelectionBadge) so a fallback-hued
                        // container isn't labelled in the blue its wash just moved away from.
                        wash.userData = selectionAccent;
                        element.Add(wash);
                    }

                    var hoverRing = new VisualElement { name = "designer-hover-ring", pickingMode = PickingMode.Ignore };
                    hoverRing.AddToClassList("designer-hover-ring");
                    hoverRing.style.borderTopColor = ringColor;
                    hoverRing.style.borderBottomColor = ringColor;
                    hoverRing.style.borderLeftColor = ringColor;
                    hoverRing.style.borderRightColor = ringColor;
                    // Follow the card's corner radius (custom radii are supported) so the ring's corners line up.
                    hoverRing.style.borderTopLeftRadius = element.style.borderTopLeftRadius;
                    hoverRing.style.borderTopRightRadius = element.style.borderTopRightRadius;
                    hoverRing.style.borderBottomLeftRadius = element.style.borderBottomLeftRadius;
                    hoverRing.style.borderBottomRightRadius = element.style.borderBottomRightRadius;
                    element.Add(hoverRing);

                    // A binding's own outline lightens while the card is hovered, so the field under the cursor
                    // reads as "active" without losing its custom hue. Only cards that actually paint a border
                    // qualify — the 1px transparent selection-host border on plain cards has a Null colour and
                    // is skipped (USS drives their blue hover border instead).
                    var restingBorder = element.style.borderTopColor;
                    if (restingBorder.keyword != StyleKeyword.Null && restingBorder.value.a > 0.01f
                        && element.style.borderTopWidth.value > 0f)
                    {
                        Color resting = restingBorder.value;
                        Color hovered = LightenOutlineForHover(resting);
                        var card = element;
                        card.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            card.style.borderTopColor = hovered;
                            card.style.borderBottomColor = hovered;
                            card.style.borderLeftColor = hovered;
                            card.style.borderRightColor = hovered;
                        });
                        card.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            card.style.borderTopColor = resting;
                            card.style.borderBottomColor = resting;
                            card.style.borderLeftColor = resting;
                            card.style.borderRightColor = resting;
                        });
                    }

                    bool isPrimarySelection = node == selectedNode;
                    bool isSelected = isPrimarySelection || (_selectionSet != null && _selectionSet.Contains(node));
                    if (isSelected)
                    {
                        element.AddToClassList("designer-card-selected");

                        // Badge only on the primary so a multi-selection doesn't get noisy.
                        if (isPrimarySelection)
                        {
                            AttachSelectionBadge(element, node, positionNow: false);
                        }
                    }
                    // Non-selected cards stay outline-free until hovered (see DesignerCanvas.uss).
                    // This keeps the canvas clean instead of drawing a grid of nested boxes.

                    // Add grip handle (unless root)
                    if (node != layoutAsset.Root)
                    {
                        var grip = new VisualElement();
                        grip.AddToClassList("designer-grip");
                        // The drag grip must survive the design-mode Ignore sweeps: a self-carded widget
                        // (Tag Chips — the built widget IS the card) suppresses its own children
                        // persistently, and an unexempted grip gets swept to Ignore — then ComputeCanDrag
                        // never sees a grip press and the card can be selected but not dragged.
                        grip.AddToClassList(DesignKeepInteractiveClass);
                        var glyph = new GripGlyph();
                        grip.Add(glyph);
                        element.Add(grip);
                        element.AddToClassList("grip-host");

                        var gripLine = new VisualElement { pickingMode = PickingMode.Ignore };
                        gripLine.AddToClassList("designer-grip-line");
                        element.Add(gripLine);
                        grip.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            gripLine.style.display = DisplayStyle.Flex;
                            glyph.Tint = GripGlyph.HoverTint;
                        });
                        grip.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            gripLine.style.display = DisplayStyle.None;
                            glyph.Tint = GripGlyph.DefaultTint;
                        });
                    }

                    // Pointer-based drag + click-to-select are handled entirely by the central
                    // CanvasDragController via this manipulator. Root is attached too (selection
                    // only — it has no grip, so ComputeCanDrag never lets it start a drag).
                    if (dragController != null)
                    {
                        if (node != layoutAsset.Root)
                        {
                            element.AddToClassList("designer-draggable");
                        }
                        // In Interact mode, leaf field cards get no drag manipulator at all so the
                        // field stays fully interactive (toggles, sliders, typing). Containers keep
                        // it for grip-based reordering.
                        bool leaf = !(node is ContainerNode) && !(node is CollectionNode);
                        if (!(designInteract && leaf))
                        {
                            // Tag Chips renders leaf-like (no frame, no draggable item rows) and its body
                            // is fully inert in design mode, so it drags from anywhere like a leaf card.
                            // Interact mode keeps the grip-only rule — the chips/add-field are editable there.
                            bool dragAnywhere = !designInteract && node is CollectionNode chipsNode &&
                                                chipsNode.DisplayMode == CollectionDisplayMode.TagChips;
                            element.AddManipulator(new CanvasDragManipulator(dragController, CanvasDragPayload.Move(node), dragAnywhere));
                        }

                        // Right-click → the window's node context menu (Copy/Paste Style, Duplicate, Ping in
                        // Hierarchy). A separate right-button event, so the manipulator's button-0 drag
                        // handshake is untouched.
                        var contextNode = node;
                        element.RegisterCallback<ContextClickEvent>(evt =>
                        {
                            dragController.OnNodeContextMenu?.Invoke(contextNode);
                            evt.StopPropagation();
                        });

                        // Double-click → jump to this card's row in the hierarchy: scroll it into view and
                        // flash it. The exact mirror of double-clicking a hierarchy row to find the card, so
                        // the two panels navigate to each other with the same gesture.
                        //
                        // PointerDOWN, not up: at press time nothing has moved, so this can never be confused
                        // with the tail of a drag (the manipulator refuses to drag a double-click press for
                        // the same reason). Gated on the SAME condition as the drag manipulator above — where
                        // a leaf card yields the pointer to its live field in Interact mode, the field owns
                        // the double-click too, and the right-click entry covers that case instead.
                        if (!(designInteract && leaf))
                        {
                            element.RegisterCallback<PointerDownEvent>(evt =>
                            {
                                if (evt.button != 0 || evt.clickCount < 2) return;
                                // Controls that own their own double-click keep it (a tab's inline rename must
                                // still word-select; grips and mini buttons still act).
                                if (evt.target is VisualElement t &&
                                    (t is Button || t.GetFirstAncestorOfType<Button>() != null ||
                                     t is TextField || t.GetFirstAncestorOfType<TextField>() != null)) return;
                                dragController.OnNodeDoubleClick?.Invoke(contextNode);
                                // Stop here so only the DEEPEST card under the cursor pings — every ancestor
                                // container carries this same handler.
                                evt.StopPropagation();
                            });
                        }
                    }
                }
            }

            // Bind this node's conditions against its scoped base path so conditions inside a collection
            // ItemLayout resolve against the indexed element rather than the root object. Applied to
            // the final (possibly design-mode-wrapped) element, which carries userData == node.
            if (element != null &&
                ((node.ConditionRules != null && node.ConditionRules.Count > 0) ||
                 (node.Conditions != null && node.Conditions.Count > 0)))
            {
                ConditionBinder.Bind(element, node, serializedObject, basePath, _debugConditions, designMode);
            }

            // The design-canvas half of the group-toggle mirror rule (the live half is the early return at the
            // top of BuildNode). Same element the conditions land on — the design-card wrapper, carrying
            // userData == node — so the pip sits on the card the author actually clicks.
            if (designMode && element != null && _toggleMirrors != null && _toggleMirrors.Contains(node))
            {
                GroupToggleMirror.ApplyDesignPip(element);
            }

            // The design-canvas half of the hidden-node rule (the live half is the second early return at the top
            // of BuildNode). Same element, same reasoning as the mirror pip above — the author still has to be
            // able to find, select and un-hide it. Opposite corner, so a node that is BOTH stays readable.
            if (designMode && element != null && node.HiddenInInspector)
            {
                HiddenNodeMark.ApplyDesignPip(element);
            }

            // A third-party [ShowIf]-style drawer collapses its own content but cannot remove OUR row, which
            // would leave a dead strip of margin/background behind. Bound to the SAME element as the
            // conditions above and routed through the same visibility arbiter, so an attribute-hidden field
            // disappears exactly like a condition-hidden one. Self-gating: a no-op unless this really is a
            // Default field owned by a non-Unity drawer.
            if (element != null && boundFieldPath != null && node is BindingNode drawerBoundNode)
            {
                DrawerVisibilityBridge.Bind(element, drawerBoundNode, boundFieldPath, serializedObject, designMode);
            }

            // Animations play in live preview and real inspectors, never in design mode (where they'd
            // fight dragging/selection).
            if (!designMode && element != null)
            {
                AnimationBinder.Apply(element, node, _playAppearAnimations);
            }

            // Subtle hover "lift" on leaf field/widget cards — opt-in via Project Settings ▸ Sasquatch Inspector
            // ▸ Enable Hover Animation (off by default: it's cosmetic and can make small fields fiddlier to
            // hover). In design mode `element` is the designer-card wrapper; in the live inspector it's the
            // field element itself — both read as a card, so one attach point covers both surfaces. Skipped
            // for nodes that define their own Hover animation so the two springs don't fight; transform-only.
            if (element != null
                && SasquatchSettingsManager.Current.EnableHoverAnimation
                && !(node is ContainerNode) && !(node is CollectionNode)
                && !NodeHasHoverAnimation(node))
            {
                EditorSpringFX.HoverScale(element);
            }

            // Live-rebuild support: stash the exact build context for this node's element so the designer can
            // rebuild JUST this node in place during a continuous-control drag (see RebuildNodeInPlace). Keyed by
            // the final element, which carries userData == node (the design-card wrapper in design mode, the
            // element itself in live preview) — the same element LivePokeCanvasElement's lookup resolves. Only the
            // designer opts in (captureNodeContexts); the shipping inspector never allocates a context.
            if (_captureNodeContexts && element != null)
            {
                _rebuildContexts.AddOrUpdate(element, new NodeRebuildContext
                {
                    node = node,
                    basePath = basePath,
                    nodePath = nodePath,
                    serializedObject = serializedObject,
                    onRebuildRequired = onRebuildRequired,
                    theme = theme,
                    designMode = designMode,
                    selectedNode = selectedNode,
                    dragController = dragController,
                    layoutAsset = layoutAsset,
                    designInteract = designInteract,
                    columnCell = columnCell,
                    parentColumnsHasAnyHeader = parentColumnsHasAnyHeader,
                    directChildrenAreCells = directChildrenAreCells,
                    // Without this a live poke of a table column rebuilt it as a plain field — label back,
                    // card chrome back — i.e. the designer's own edit undid the cell's cell-ness.
                    tableCell = tableCell,
                    selectionSet = _selectionSet,
                    showItemTemplates = _showItemTemplates,
                    onLayoutEdited = _onLayoutEdited,
                    debugConditions = _debugConditions,
                    buildScopeIsLight = WidgetPalette.BuildScopeIsLight,
                    // Derived from the WHOLE tree, which a single-node rebuild never sees — without carrying it
                    // over, a live poke on a mirrored binding would rebuild the card without its dim + pip.
                    toggleMirrors = _toggleMirrors,
                    orphanedSpacers = _orphanedSpacers,
                });
            }

            return element;
        }

        /// <summary>
        /// Rebuilds a single node's element in place — the designer's "live poke" for continuous controls that
        /// have no cheap in-place style poke (painted widgets, values baked into captured build locals,
        /// structural/multi-element changes). Re-runs the SAME <see cref="BuildNode"/> against the model as it has
        /// just been mutated, then swaps the fresh element into the old one's slot, so binding, conditions, drag
        /// wiring and the design-card wrapper are reproduced exactly at ~1/N the cost of a whole-tree rebuild.
        /// Requires the node to have been built with captureNodeContexts (the designer sets it). Returns false —
        /// the caller then relies on the debounced full rebuild — when no context is stored, the element is
        /// detached, the node is the layout root (rebuilding it would orphan the drag controller's canvas root),
        /// or the rebuild throws. A true return IS authoritative: it re-ran the real BuildNode, so the designer's
        /// drag commit skips its full-canvas rebuild when every tick of a burst reconciled this way (avoiding the
        /// release-time jolt from widgets whose geometry settles over several frames).
        /// </summary>
        /// <summary>
        /// Copies the inline layout styles a PARENT container stamped onto a child after building it, from the
        /// element being replaced onto its rebuilt replacement.
        /// </summary>
        /// <remarks>
        /// A container sizes its children in a pass of its own AFTER they are built —
        /// <see cref="ApplyColumnsLayout"/> writes <c>flexGrow</c> / <c>flexBasis</c> / <c>minWidth</c> on each
        /// column, and the Rows and Grid passes do the equivalent. Those live as INLINE styles on the child, so
        /// a freshly built replacement does not have them: it sizes to its own content instead.
        /// <para>
        /// Visible symptom: typing into any live-rebuilding field (a prefix, a suffix, a placeholder) on a node
        /// inside a Columns container made that node COLLAPSE horizontally on every keystroke, then snap back
        /// to full width on save — because saving runs the full RefreshPreview, which re-runs the parent pass.
        /// </para>
        /// <para>
        /// Only properties a parent container is known to write are carried, and only when the OLD element
        /// really had them set. Note the keyword test: <c>StyleKeyword.Undefined</c> means a REAL value is
        /// present, not "unset".
        /// </para>
        /// </remarks>
        private static void CarryParentAppliedLayout(VisualElement oldElement, VisualElement newElement)
        {
            if (oldElement == null || newElement == null) return;

            var flexGrow = oldElement.style.flexGrow;
            if (flexGrow.keyword == StyleKeyword.Undefined) newElement.style.flexGrow = flexGrow.value;

            var flexShrink = oldElement.style.flexShrink;
            if (flexShrink.keyword == StyleKeyword.Undefined) newElement.style.flexShrink = flexShrink.value;

            var flexBasis = oldElement.style.flexBasis;
            if (flexBasis.keyword == StyleKeyword.Undefined) newElement.style.flexBasis = flexBasis.value;

            var minWidth = oldElement.style.minWidth;
            if (minWidth.keyword == StyleKeyword.Undefined) newElement.style.minWidth = minWidth.value;

            var width = oldElement.style.width;
            if (width.keyword == StyleKeyword.Undefined) newElement.style.width = width.value;

            var height = oldElement.style.height;
            if (height.keyword == StyleKeyword.Undefined) newElement.style.height = height.value;

            // Wrapped Columns rows get breathing room stamped on them at Gap 0; a rebuilt row loses it and the
            // rows butt together until the next full refresh.
            var marginBottom = oldElement.style.marginBottom;
            if (marginBottom.keyword == StyleKeyword.Undefined) newElement.style.marginBottom = marginBottom.value;
        }

        internal static bool RebuildNodeInPlace(VisualElement oldElement)
        {
            if (oldElement == null) return false;
            if (!_rebuildContexts.TryGetValue(oldElement, out var ctx) || ctx == null) return false;
            // Rebuilding the root would swap the whole tree out from under the drag controller's canvas root
            // (only RefreshPreview re-points SetCanvasRoot) — leave the root to the debounced rebuild.
            if (ctx.layoutAsset != null && ReferenceEquals(ctx.node, ctx.layoutAsset.Root)) return false;
            var parent = oldElement.parent;
            if (parent == null) return false;
            int index = parent.IndexOf(oldElement);
            if (index < 0) return false;

            var prevSelectionSet = _selectionSet;
            var prevShowItemTemplates = _showItemTemplates;
            var prevPlayAppear = _playAppearAnimations;
            var prevOnLayoutEdited = _onLayoutEdited;
            var prevDebugConditions = _debugConditions;
            var prevDesignMode = _designMode;
            var prevCapture = _captureNodeContexts;
            var prevBuildSkin = WidgetPalette.BuildScopeIsLight;
            var prevSuppressCondWarn = ConditionEvaluator.SuppressWarnings;
            var prevToggleMirrors = _toggleMirrors;
            var prevOrphanedSpacers = _orphanedSpacers;

            _toggleMirrors = ctx.toggleMirrors;
            _orphanedSpacers = ctx.orphanedSpacers;
            _selectionSet = ctx.selectionSet;
            _showItemTemplates = ctx.showItemTemplates;
            _playAppearAnimations = false; // never replay appear-in animations on a live poke rebuild
            _onLayoutEdited = ctx.onLayoutEdited;
            _debugConditions = ctx.debugConditions;
            _designMode = ctx.designMode;
            _captureNodeContexts = true; // the fresh element must be re-registered for the NEXT poke of this node
            WidgetPalette.BuildScopeIsLight = ctx.buildScopeIsLight;
            // This IS a designer rebuild (it requires captureNodeContexts, which only the designer sets), so it
            // inherits the designer's condition-warning silence — the same scope RefreshPreview wraps its full
            // build in. Without it, the in-place path re-bound conditions OUTSIDE that scope and every debounced
            // keystroke in a condition-driver field logged the half-typed path. Live Preview mode builds with
            // designMode:false, so the binder's own designMode check can't cover this on its own.
            ConditionEvaluator.SuppressWarnings = true;

            try
            {
                var newElement = BuildNode(
                    ctx.node, ctx.basePath, ctx.serializedObject, ctx.onRebuildRequired, ctx.theme,
                    ctx.designMode, ctx.selectedNode, ctx.dragController, ctx.layoutAsset, ctx.designInteract,
                    ctx.columnCell, ctx.parentColumnsHasAnyHeader, ctx.nodePath, ctx.directChildrenAreCells,
                    ctx.tableCell);
                if (newElement == null) return false;
                CarryParentAppliedLayout(oldElement, newElement);
                parent.Insert(index, newElement);
                oldElement.RemoveFromHierarchy();
                // The primary selection's badge is hosted on the build ROOT (not as a child of the card, so a
                // clipping ancestor can't clip its protruding tab) and only repositioned on the card's
                // GeometryChanged. The rebuilt card creates its OWN fresh badge, so the removed card's badge is now
                // orphaned on the root — and would pile up one-per-tick through a drag (the edited node is always
                // the primary selection). Clear stale badges; the new card re-adds its own on its next layout pass.
                // Only the primary-selected node has a badge, so scope the sweep to a primary rebuild — a non-primary
                // rebuild neither makes a badge nor should clear the primary's.
                if (ReferenceEquals(ctx.node, ctx.selectedNode))
                {
                    SweepSelectionBadges(FindBuildRoot(newElement));
                }
                newElement.Bind(ctx.serializedObject);
                return true;
            }
            catch (Exception ex)
            {
                // A live rebuild is best-effort feedback; never let a widget's rebuild failure break the drag.
                // The debounced full rebuild still reconciles this node on release.
                Debug.LogWarning($"[Sasquatch Inspector] Live node rebuild failed; falling back to debounced refresh. {ex.Message}");
                return false;
            }
            finally
            {
                _selectionSet = prevSelectionSet;
                _showItemTemplates = prevShowItemTemplates;
                _playAppearAnimations = prevPlayAppear;
                _onLayoutEdited = prevOnLayoutEdited;
                _debugConditions = prevDebugConditions;
                _designMode = prevDesignMode;
                _captureNodeContexts = prevCapture;
                WidgetPalette.BuildScopeIsLight = prevBuildSkin;
                ConditionEvaluator.SuppressWarnings = prevSuppressCondWarn;
                _toggleMirrors = prevToggleMirrors;
                _orphanedSpacers = prevOrphanedSpacers;
            }
        }

        // True when the node carries an explicit Hover-triggered animation (AnimationBinder will wire
        // it), so the generic hover lift above should stand down and not compound with it.
        private static bool NodeHasHoverAnimation(LayoutNode node)
        {
            if (node?.Animations == null) return false;
            foreach (var a in node.Animations)
                if (a != null && a.Effect != AnimEffect.None && a.Trigger == AnimTrigger.Hover) return true;
            return false;
        }

        /// <summary>
        /// The collection's title text: its <see cref="CollectionNode.TitleOverride"/> when set, the bound
        /// property's own display name when unset, and empty when the title is explicitly blanked. The single
        /// resolver for every header shape (List expandable/flat, Table expandable/flat) plus the whole-array
        /// widget modes, so a retitled collection reads the same everywhere.
        /// </summary>
        internal static string ResolveCollectionTitle(CollectionNode node, SerializedProperty prop)
        {
            if (node != null && node.IsTitleHidden) return string.Empty;
            string over = node?.EffectiveTitleOverride;
            if (!string.IsNullOrEmpty(over)) return over;
            return prop != null ? prop.displayName : string.Empty;
        }

        // "Waypoints (3)" — the header line. A hidden title takes the count with it (the count is part of the
        // title, not separate chrome), leaving the label empty; the label ELEMENT still exists, because it is
        // also the flexGrow spacer that holds the "+" button out at the right edge.
        private static string CollectionHeaderText(CollectionNode node, SerializedProperty prop, bool templateNote = false)
        {
            string title = ResolveCollectionTitle(node, prop);
            string tail = templateNote ? "(template preview)" : $"({prop.arraySize})";
            if (string.IsNullOrEmpty(title)) return templateNote ? tail : string.Empty;
            return $"{title} {tail}";
        }

        private static VisualElement BuildCollectionElement(
            CollectionNode node,
            string resolvedPath,
            SerializedObject serializedObject,
            Action onRebuildRequired,
            SasquatchThemeAsset theme,
            bool designMode,
            LayoutNode selectedNode,
            CanvasDragController dragController,
            InspectorLayoutAsset layoutAsset,
            bool designInteract = false)
        {
            // Table display mode renders the same collection data as a column-per-field table instead of
            // stacked rows.
            if (node.DisplayMode == CollectionDisplayMode.Table)
            {
                var columns = GetColumnBindings(node.ItemLayout);
                // A Table node with NO ItemLayout bindings is the shape code-authored (in-memory API)
                // layouts arrive in — only the designer's mode-switch runs AutoFillTableColumnsIfEmpty.
                // Derive the columns from the element type so those layouts get the same table the
                // designer and the standalone export produce, instead of the single-"Value" fallback.
                if (columns.Count == 0)
                {
                    // "Nothing was ever authored" and "everything authored is now HIDDEN" are different
                    // questions that used to get the same answer — and the answer was the worst one
                    // available. DeriveColumnBindingsFromElementType reflects the element type and mints a
                    // column for EVERY serialized leaf, so hiding a table's last visible column ADDED
                    // columns, the hidden field among them and still fully editable. Hiding must never
                    // resurrect the thing it hid.
                    if (HasAuthoredColumns(node.ItemLayout))
                    {
                        // Every column the author designed is hidden, so there is no content left to draw.
                        // Same answer the stale-path prune inside BuildCollectionTableElement already gives
                        // ("if (columns.Count == 0) return null;"): a table with no columns is not drawn,
                        // exactly as deleting its last column would leave nothing to draw. Falling through
                        // to the single "Value" column instead would be WORSE than the reflection fallback
                        // for a struct element — that column binds the element itself, which draws every
                        // one of its fields, hidden ones included.
                        //
                        // Unreachable in design mode: CollectColumnBindings keeps hidden columns there, so
                        // columns.Count is never 0 for an authored template and the author can always find
                        // the dimmed column and un-hide it.
                        return null;
                    }
                    columns = DeriveColumnBindingsFromElementType(serializedObject, resolvedPath);
                }
                if (columns.Count > 0)
                {
                    return BuildCollectionTableElement(
                        node, resolvedPath, columns, serializedObject, onRebuildRequired,
                        theme, designMode, selectedNode, dragController, layoutAsset, designInteract);
                }
                // No item-layout fields (e.g. a primitive List<int>/List<string>) — render the element
                // itself as a single "Value" column instead of silently falling back to List.
                var valueColumn = new List<BindingNode> { new BindingNode { PropertyPath = "", LabelOverride = "Value" } };
                return BuildCollectionTableElement(
                    node, resolvedPath, valueColumn, serializedObject, onRebuildRequired,
                    theme, designMode, selectedNode, dragController, layoutAsset, designInteract, isValueColumn: true);
            }

            var prop = serializedObject.FindProperty(resolvedPath);
            if (!IsRenderableCollectionProperty(prop))
            {
                // A field that still EXISTS in code but is now an unserializable BCL collection is not a
                // dead binding -- it gets the conversion card, exactly as the BindingNode branch does.
                if (TryDeadCollectionConversionCard(serializedObject, resolvedPath, out var conversionCard))
                    return conversionCard;

                // Dead collection: hidden live (Unity-like), red-tinted in the designer.
                if (!designMode) return null;
                var fallbackContainer = CreateCollectionFrame();
                fallbackContainer.AddToClassList("sasq-missing-binding");
                fallbackContainer.Add(new Label(DeadCollectionMessage(prop, resolvedPath)));
                return fallbackContainer;
            }

            if (node.DisplayMode == CollectionDisplayMode.Default)
            {
                var propField = new PropertyField(prop);
                propField.style.flexGrow = 1f;
                // A code [Space] is ALWAYS our own Space widget, never the stock list's native SpaceDrawer
                // decorator — unconditionally hide it (a cheap no-op when the field has no [Space]). Otherwise
                // the native gap draws in addition to our widget, or as an untouchable gap on an un-imported one.
                BindingFactory.SuppressSpaceDecorator(propField);
                // Persistent: Unity's stock list draws its rows lazily during bind and RECYCLES them as the
                // view scrolls, so every fresh row would come back at the default PickingMode.Position and a
                // string element stayed clickable/typeable with Interact OFF.
                if (designMode && !designInteract)
                {
                    SuppressInteractionPersistently(propField);
                }
                return propField;
            }

#if SASQ_CUSTOM_WIDGET
            if (node.DisplayMode == CollectionDisplayMode.CustomWidget)
            {
                // The whole array is drawn by one Custom Widget (its primary list input = this array). Read-only
                // in the non-interactive designer so a design-time preview never writes to the real target.
                bool ro = designMode && !designInteract;
                var custom = BindingFactory.TryBuildCustomWidget(
                    node.CustomWidgetGuid, node.CustomWidgetInputs, prop, ResolveCollectionTitle(node, prop),
                    resolvedPath, node.PropertyPath, serializedObject, ro, WidgetPalette.Accent);
                if (custom != null)
                {
                    custom.style.flexGrow = 1f;
                    return custom;
                }
                // Asset missing / primary-kind mismatch → fall back to the stock Unity list (never an error),
                // flagged in the designer so the author knows to assign or rebuild the widget asset.
                var fallback = new PropertyField(prop) { style = { flexGrow = 1f } };
                if (!designMode) return fallback;
                var frame = CreateCollectionFrame();
                frame.AddToClassList("sasq-missing-binding");
                frame.Add(new Label("Custom Widget: assign a compatible widget asset (showing the default list)."));
                frame.Add(fallback);
                // Persistent — the fallback is a stock Unity list, whose rows build lazily (see the Default
                // branch above).
                if (!designInteract) SuppressInteractionPersistently(frame);
                return frame;
            }
#endif

            if (node.DisplayMode == CollectionDisplayMode.TagChips)
            {
                // Render the whole string collection as the Tag Chips widget (chips + inline add field), reusing
                // the field-level builder. It self-guards to string arrays (falls back to a plain field otherwise),
                // so an accidental non-string TagChips mode degrades gracefully. Read-only in the non-interactive
                // designer so a preview never mutates the real target.
                var chips = BindingFactory.CreateTagChips(null, prop, ResolveCollectionTitle(node, prop));
                chips.style.flexGrow = 1f;
                // Collection-mode Tag Chips renders as a label+input row but never passes through the
                // field factory's alignment post-process — join the shared label column here.
                FieldLabelColumn.AttachToRow(chips, node);
                // The Tag Chips widget rebuilds its chip children whenever the array changes (TrackPropertyValue
                // re-fires on bind/attach), and every rebuild spawns fresh chips at the default PickingMode.Position
                // whose Labels StopPropagation on PointerDown — so a one-shot disable leaks them back and they eat
                // the canvas selection click, leaving the card selectable only from its left/label gutter. Disable
                // the chip subtree PERSISTENTLY, but keep the card ROOT pickable: a CollectionNode gets no leaf-card
                // wrapper, so `chips` IS the card that carries userData + the selection manipulator — disabling it
                // too would drop the click through to the parent container (unselectable). Skipped in Interact mode
                // so the chips stay editable.
                if (designMode && !designInteract) SuppressChildrenInteractionPersistently(chips);
                return chips;
            }

            // Item-template scaffolding: an empty list with an ItemLayout would otherwise show no
            // rows, leaving the template invisible/undesignable. In design mode we render one sample row
            // built from a *separate throwaway scratch object* (see GetTemplateScratch) so the template
            // binds to real default data and is editable — WITHOUT ever mutating the real inspected
            // object. Turning off the Templates toggle or leaving Design Mode hides it; live preview never
            // shows it.
            bool showTemplate = designMode && _showItemTemplates && node.ItemLayout != null && prop.arraySize == 0;

            // The List's container-level look (shell / gutter / density / ordinal). Orthogonal to
            // node.RowSeparator, which owns the ROW surface — see LayoutTreeBuilder.ListStyles.cs.
            var listStyle = node.ListStyle;

            VisualElement container;
            VisualElement contentTarget;
            // The header bar, whichever branch built it — the skin pass needs it after both.
            VisualElement listHeaderRow = null;
            SasquatchIconElement chevron = null;

            // Count label (hoisted so the in-place rebuild can refresh "(N)") + the structural-op callback.
            // In LIVE mode, +/-/insert/duplicate/delete rebuild ONLY this collection's rows in place
            // (localized, exactly like the inline editor's RebuildInlineFields) instead of tearing down the
            // whole inspector via onRebuildRequired — that full teardown was the list sluggishness AND the
            // "jump up then snap back" scroll jank. Design mode keeps onRebuildRequired (its structural ops
            // re-evaluate item templates / canvas selection, which a local rebuild can't). liveRebuild is
            // assigned once the row container + helpers exist; the wrapper lambda defers to it.
            Label titleLabel = null;
            // EXPERIMENT: skinned headers move the "(N)" out of the title STRING and into a pill, so the
            // in-place rebuild has a second element to retext. Null on Classic (and on a blanked title).
            Label countPill = null;
            // Hold-to-clear, hoisted for the same reason as countPill: the in-place rows rebuild never
            // rebuilds the HEADER, so the one thing that changes about this button — whether the list has
            // anything left to clear — has to be re-synced from there. See CollectionClearAll.Sync.
            Button clearBtn = null;
            Action liveRebuild = null;
            Action structuralRebuild = designMode ? onRebuildRequired : new Action(() => liveRebuild?.Invoke());

            // --- Making an APPEND actually visible (drop / eyedropper). Assigned below once the header and the
            // view state exist; the wrapper handed to CollectionObjectDrop.Attach defers to them, exactly like
            // liveRebuild. An append lands at the END of the array, and three separate pieces of view state can
            // park it off-screen — a collapsed foldout, an active search filter, and a page that is not the last
            // one (pagination defaults to ON at 25 rows, so a 26-item list hides the whole drop). The user then
            // sees the count go up and NOTHING else, which reads as "dragging multiple items in doesn't work".
            // Keep in lockstep with the Table twin in BuildCollectionTableElement. ---
            Action ensureExpanded = null;
            Action revealAppended = null;
            // The row menu's Insert twin: same filter/sort clear, no page move. See CollectionRowMenu.Attach.
            Action revealInserted = null;

            // --- Transient (never serialized) page + sort state for the live view. Captured by the row
            // population + toolbar closures; reset to page 0 whenever the search/sort changes. ---
            int currentPage = 0;
            int sortColumnIndex = -1;       // -1 = natural order
            bool sortDescending = false;
            int lastVisibleCount = 0, lastPageStart = 0, lastPageCount = 0;
            // The sortable columns = the item-layout leaf bindings (same as Table columns); a primitive
            // element list (List<int>/<string>) gets a single "Value" key. Labels are precomputed so the
            // sort note never touches a sample element that a rebuild may have invalidated.
            //
            // A FUNCTION, not a one-shot, for two reasons that turned out to be the same reason:
            //   * every key is derived by INSPECTING ELEMENT 0, and an empty list has no element 0. A list
            //     that was empty when the inspector opened got the honest-but-useless fallback ("Value")
            //     and then kept it forever: '+' routes through RebuildListRowsInPlace, which re-syncs the
            //     search box and the rows and never re-asked for the keys. UpdateListSortUi only ever
            //     re-synced the dropdown's VALUE, never its `choices`.
            //   * the same sample is what says whether a key can be ORDERED at all. Offering one that
            //     cannot is worse than offering nothing — see FilterSortableColumns.
            // Declared before the derive so the closures below capture the variables, not a snapshot.
            List<BindingNode> sortColumns = new List<BindingNode>();
            var sortColumnLabels = new List<string>();
            // Column labels are USER text, and DropdownField resolves a picked value back to an index by
            // FIRST string match — so two columns with the same header both sorted the first one, and a
            // column literally labelled "None" matched the leading entry and silently sorted nothing.
            // Disambiguate so every entry is unique and choice index maps 1:1 onto sortColumns. (The sort
            // NOTE still shows the raw label, which is what the user actually wrote.)
            var sortChoices = new List<string> { "None" };
            // True while the keys above were derived with nothing to sample. The ONLY condition that makes
            // a re-derive worth doing: a list that already had a row cannot change what its keys are.
            bool sortKeysAwaitingSample = true;
            DropdownField sortField = null;
            VisualElement sortRowHost = null;

            void DeriveSortKeys()
            {
                var sample = prop.arraySize > 0 ? prop.GetArrayElementAtIndex(0) : null;
                var cols = GetColumnBindings(node.ItemLayout);
                // No columns at all, OR an ItemLayout whose only column is the ELEMENT itself (an empty
                // PropertyPath — the Element template): both are the same sorting problem, one key standing
                // in for a whole Vector2. Expand either into the element's components, so adding an Element
                // template to a list never costs it the X / Y keys it had before.
                if (cols.Count == 0 || (cols.Count == 1 && string.IsNullOrEmpty(cols[0].PropertyPath)))
                    cols = BuildElementSortKeys(sample, cols.Count == 1 ? cols[0] : null);
                // BOTH branches, not just the designed-ItemLayout one: BuildElementSortKeys filters the
                // element's CHILDREN but still falls back to the whole element when none of them survive,
                // and that whole element is exactly the Generic-typed key that compares 0 for every pair.
                cols = FilterSortableColumns(cols, sample);

                sortColumns = cols;
                sortKeysAwaitingSample = sample == null;

                sortColumnLabels.Clear();
                foreach (var sc in sortColumns) sortColumnLabels.Add(ResolveColumnLabel(sc, sample));

                sortChoices.Clear();
                sortChoices.Add("None");
                var used = new HashSet<string>(StringComparer.Ordinal) { "None" };
                foreach (var label in sortColumnLabels)
                {
                    string baseLabel = string.IsNullOrEmpty(label) ? "Value" : label;
                    string unique = baseLabel;
                    int suffix = 2;
                    while (!used.Add(unique)) unique = $"{baseLabel} ({suffix++})";
                    sortChoices.Add(unique);
                }

                // A key that no longer exists must not stay selected — it would keep reorder paused for a
                // sort nothing can perform.
                if (sortColumnIndex >= sortColumns.Count) { sortColumnIndex = -1; sortDescending = false; }

                if (sortField != null) sortField.choices = new List<string>(sortChoices);
                // Nothing orderable at all ⇒ no control. A "Sort: None" dropdown with no second entry is a
                // control whose only setting is off.
                if (sortRowHost != null)
                    sortRowHost.style.display = sortColumns.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            DeriveSortKeys();

            // Re-ask ONLY when the first derive had nothing to look at and the list has since grown. Cheap,
            // and it cannot churn a list that was never empty.
            void RefreshSortKeysIfSampleArrived()
            {
                if (sortKeysAwaitingSample && prop.arraySize > 0) DeriveSortKeys();
            }

            string SortPath() => (sortColumnIndex >= 0 && sortColumnIndex < sortColumns.Count)
                ? sortColumns[sortColumnIndex].PropertyPath : null;

            if (node.Expandable)
            {
                container = new VisualElement();
                container.AddToClassList("sasq-group");
                container.style.marginBottom = 6;

                // Single continuous outline around the WHOLE group (header + content). The header and content
                // below keep their backgrounds but drop their individual side/corner borders so this one box
                // is the only visible outline. overflow:Hidden clips their square corners to the rounded box.
                var groupOutline = WidgetPalette.BorderStrong; // #1e1e1e on dark
                container.style.borderTopWidth = 1;
                container.style.borderBottomWidth = 1;
                container.style.borderLeftWidth = 1;
                container.style.borderRightWidth = 1;
                container.style.borderTopColor = groupOutline;
                container.style.borderBottomColor = groupOutline;
                container.style.borderLeftColor = groupOutline;
                container.style.borderRightColor = groupOutline;
                container.style.borderTopLeftRadius = 4;
                container.style.borderTopRightRadius = 4;
                container.style.borderBottomLeftRadius = 4;
                container.style.borderBottomRightRadius = 4;
                container.style.overflow = Overflow.Hidden;

                // Header bar
                var headerRow = new VisualElement();
                headerRow.AddToClassList("sasq-group-header");
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.Center;
                // Drop the header's own box border (the container outline handles it); keep only a bottom
                // separator line between the header and the content when expanded (set in ApplyExpanded).
                headerRow.style.borderTopWidth = 0;
                headerRow.style.borderLeftWidth = 0;
                headerRow.style.borderRightWidth = 0;
                headerRow.style.borderBottomColor = groupOutline;
                headerRow.style.borderTopLeftRadius = 0;
                headerRow.style.borderTopRightRadius = 0;

                // Chevron
                chevron = SasquatchIcons.Create(
                    SasquatchIcons.Ids.ChevronRight, // single glyph; ApplyExpanded spins it 0°↔90°
                    10, WidgetPalette.ChevronDim);
                chevron.AddToClassList("sasq-group-chevron");
                headerRow.Add(chevron);

                // Title
                titleLabel = new Label(CollectionHeaderText(node, prop, showTemplate));
                titleLabel.AddToClassList("sasq-collection-header");
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.flexGrow = 1;
                titleLabel.style.color = WidgetPalette.LabelPrimary;
                headerRow.Add(titleLabel);

                // TRIAL: eyedropper — pick objects straight out of the Project/Hierarchy window. Null for
                // value collections, in design mode, and — the opt-in half — anywhere that is not one of the
                // two REAL Inspector paths: the designer's Live Preview, the preset editor and the Showcase
                // all pass designMode:false, and would otherwise arm a global picking mode whose picks land
                // in a throwaway preview target. Same gate, and the same reasoning, as Live Debug's above.
                // Added BEFORE the "+" so ApplyListHeaderStyle's spacer insert leaves the header reading
                // title · pill · spacer · eyedropper · "+". The reveal goes in SEPARATELY from the rebuild so
                // an ALT-strip — which appends nothing — never resets the user's search/sort/page.
                var pickBtn = CollectionEyedropper.CreateHeaderButton(container, prop, serializedObject,
                    structuralRebuild,
                    () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); },
                    designMode, _liveInspector);

                // Hold-to-clear, FURTHEST FROM THE "+": a destructive control sitting shoulder to shoulder
                // with the one people click most is a mis-click waiting to happen, so it takes the leading
                // slot of the button group and the "+" keeps the outside edge. Header reads
                // title · pill · spacer · clear · eyedropper · "+". Site 1 of 4 — keep the List plain twin
                // below and BOTH Table branches in lockstep; see collection-header-has-four-branches.
                clearBtn = CollectionClearAll.CreateHeaderButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract);
                if (clearBtn != null) headerRow.Add(clearBtn);
                if (pickBtn != null) headerRow.Add(pickBtn);

                // Add button. Reveal site 1 of 4 — the same pair the eyedropper above and the drop below
                // pass; see CreateCollectionAddButton. (ensureExpanded is also wired as a ClickEvent further
                // down for the collapsed case; it is idempotent, so passing it here costs nothing and keeps
                // all six append paths reading identically.)
                var addBtn = CreateCollectionAddButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract,
                    () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); });
                headerRow.Add(addBtn);
                // EXPERIMENT: count out of the title string and into a pill; "+" becomes a quiet icon.
                countPill = ApplyListHeaderStyle(node, prop, headerRow, titleLabel, addBtn, showTemplate);
                listHeaderRow = headerRow;

                container.Add(headerRow);

                // Content area (holds items + search box)
                contentTarget = new VisualElement();
                contentTarget.AddToClassList("sasq-group-content");
                contentTarget.style.flexDirection = FlexDirection.Column;
                // The container's own inline outline (set above) already frames all four sides, so drop the
                // .sasq-group-content USS side/bottom borders — otherwise they stack on top of the container's
                // border and the left/right/bottom edges render at DOUBLE thickness (the top stays single because
                // the content's top border is already 0). The Table path avoids this by having no container border.
                contentTarget.style.borderLeftWidth = 0;
                contentTarget.style.borderRightWidth = 0;
                contentTarget.style.borderBottomWidth = 0;
                container.Add(contentTarget);

                // Toggle logic. Open-state remembered per type, keyed by the list's property path; persisted on
                // a real user toggle (animate==true; the initial seed uses animate==false).
                string collKey = designMode ? null : FoldoutStateStore.Key(serializedObject.targetObject, prop.propertyPath, prop.displayName);
                bool expanded = FoldoutStateStore.Get(collKey, node.DefaultToOpen);
                void ApplyExpanded(bool animate)
                {
                    EditorSpringFX.SpringCaret(chevron, expanded, animate);
                    SyncHeaderSeam(headerRow, expanded);
                    if (animate) { EditorSpringFX.Fold(contentTarget, expanded); FoldoutStateStore.Set(collKey, expanded); }
                    else contentTarget.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                }
                ApplyExpanded(false);

                headerRow.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    if (designMode && !designInteract) return;
                    expanded = !expanded;
                    ApplyExpanded(!designMode);
                    if (!designMode || designInteract) evt.StopPropagation();
                });

                // Search expander (site 4 of 5 — keep in lockstep): transient open/close without
                // persistence; Restore replays the user-owned `expanded` state. Locals are pinned because
                // chevron/contentTarget are method-level variables the closure must not track past here.
                {
                    var searchChevron = chevron;
                    var searchContent = contentTarget;
                    GroupExpanderRegistry.Register(container, new GroupExpander(
                        open =>
                        {
                            EditorSpringFX.SpringCaret(searchChevron, open, false);
                            SyncHeaderSeam(headerRow, open);
                            searchContent.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                        },
                        () => ApplyExpanded(false)));
                }

                // Hoisted so an APPEND from anywhere gets the same courtesy the "+" button gives: a collapsed
                // list that silently swallows a drop is the worst of the three invisibility bugs, because on a
                // blanked title there is no count pill either — literally zero on-screen feedback.
                ensureExpanded = () =>
                {
                    if (expanded) return;
                    expanded = true;
                    ApplyExpanded(!designMode);
                };

                addBtn.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) evt.StopPropagation(); });
                addBtn.RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0) evt.StopPropagation(); });
                addBtn.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    ensureExpanded();
                });
            }
            else
            {
                container = CreateCollectionFrame();

                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.Center;
                headerRow.style.marginBottom = 6;

                titleLabel = new Label(CollectionHeaderText(node, prop, showTemplate));
                titleLabel.AddToClassList("sasq-collection-header");
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.flexGrow = 1;
                titleLabel.style.color = WidgetPalette.LabelPrimary;
                headerRow.Add(titleLabel);

                // TRIAL: eyedropper — the plain-header twin of the expandable branch above. Keep in lockstep.
                // (ensureExpanded is never assigned on this branch — there is no foldout — and the
                // null-conditional is what makes the shared reveal legal from both.)
                var pickBtn = CollectionEyedropper.CreateHeaderButton(container, prop, serializedObject,
                    structuralRebuild,
                    () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); },
                    designMode, _liveInspector);

                // Hold-to-clear — site 2 of 4, the plain-header twin of the branch above. Keep in lockstep.
                clearBtn = CollectionClearAll.CreateHeaderButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract);
                if (clearBtn != null) headerRow.Add(clearBtn);
                if (pickBtn != null) headerRow.Add(pickBtn);

                // Reveal site 2 of 4 — the List plain-header twin. Keep in lockstep.
                var addBtn = CreateCollectionAddButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract,
                    () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); });
                headerRow.Add(addBtn);
                // EXPERIMENT: count out of the title string and into a pill; "+" becomes a quiet icon.
                countPill = ApplyListHeaderStyle(node, prop, headerRow, titleLabel, addBtn, showTemplate);
                listHeaderRow = headerRow;

                container.Add(headerRow);

                contentTarget = container;
            }

            // EXPERIMENT: restyle the outer box + header bar for the skin. After BOTH branches so it sees
            // the finished shell, and it clears inline widths the expandable branch set (which no
            // stylesheet could override).
            ApplyListShellStyle(listStyle, container, listHeaderRow, contentTarget);

            // Object drag-and-drop onto the WHOLE list, so a multi-object drag lands every object and an empty
            // list can be filled by dragging at all (a per-row field can only ever take one). Live only: the
            // designer canvas is a preview, and its own card drag-drop owns these events there.
            //
            // The callback REVEALS before it rebuilds — see the revealAppended note above. A drop is an explicit
            // "put these in the list" gesture, so it outranks a stale view filter; honouring the filter over it
            // is what made a perfectly correct append look like a no-op.
            if (!designMode)
            {
                CollectionObjectDrop.Attach(container, prop, serializedObject, () =>
                {
                    ensureExpanded?.Invoke();
                    revealAppended?.Invoke();
                    structuralRebuild();
                });
            }

            // Toolbar zone (search + sort dropdown + sort-note + pager) sits above the rows. Keeping it in
            // its own container — separate from itemsContainer — means itemsContainer.Clear() on an in-place
            // rebuild never destroys the search box (so typing focus survives) or the pager.
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Column;

            // EXPERIMENT: skinned lists put search + sort on ONE row. Three stacked full-width stock
            // controls is what makes the Classic toolbar read like a debug panel. Null on Classic, and
            // `searchHost` then falls back to the toolbar column — so SyncListSearchField's re-insert
            // lands in the right place either way (put it back in the column under a skin and the toolbar
            // silently reverts to the stacked layout the moment a list crosses the 1↔2 element boundary).
            var toolbarLine = MakeListToolbarLine(toolbar);
            var searchHost = toolbarLine ?? toolbar;

            // Search box is created (cosmetically) in design mode too so the designer preview shows it; its
            // change callback is only wired live (below), and SyncListSearchField only runs on live rebuilds.
            TextField searchField = null;
            if (node.Searchable && prop.arraySize >= 2)
            {
                searchField = MakeCollectionSearchField(designMode && !designInteract);
                if (toolbarLine != null)
                {
                    searchField.style.flexGrow = 1;
                    // Grow WITHOUT shrink is a one-way street (the same trap called out on sortField
                    // below): Unity ships a BaseField as flexShrink 0 with a content-derived basis, so
                    // this box took its share of the row and never gave any back. In a narrow row that
                    // STARVED the sort dropdown beside it to zero width, and a zero-width dropdown
                    // measures its text one character per line — MEASURED with no frame involved: the
                    // toolbar row went 20px -> 282px the moment its width crossed under ~90px, and
                    // hiding EITHER control brought it straight back to 20 (it takes both: one to hog
                    // the width, one to wrap in what is left).
                    searchField.style.flexShrink = 1;
                    searchField.style.minWidth = 0;
                }
                searchHost.Add(searchField);
            }

            // List-mode sort control: a "Sort by [field] ▲▼" dropdown (Table sorts via header clicks). Built
            // in BOTH modes so the designer previews it; in design it's shown but disabled (a preview of the
            // inspector chrome — see UpdateListSortUi).
            Button sortDirBtn = null;
            if (node.EnableSorting)
            {
                var sortRow = new VisualElement();
                sortRow.AddToClassList("sasq-collection-sortrow");
                sortRow.style.flexDirection = FlexDirection.Row;
                sortRow.style.alignItems = Align.Center;
                sortRow.style.flexShrink = 1;
                sortRow.style.minWidth = 0;

                sortField = new DropdownField("Sort", new List<string>(sortChoices), 0);
                sortField.AddToClassList("sasq-collection-sort");
                sortField.style.flexGrow = 1;
                // Grow WITHOUT shrink is a one-way street. Unity ships a BaseField as flexShrink 0 with a
                // content-derived basis, so this dropdown sized itself to the longest column name and refused
                // to give any of it back — the sort row painted 21px past the inspector on a 300pt pane. The
                // column names are user text, so there is no width at which "it will always fit" is true.
                sortField.style.flexShrink = 1;
                sortField.style.minWidth = 0;
                // (Its text is kept unwrapped by MakeListToolbarLine, which covers the whole strip.)
                sortField.RegisterValueChangedCallback(evt =>
                {
                    // The dropdown's own INDEX, not a label lookup. Column labels are user text: two columns
                    // titled the same both resolved to the first one, and a column literally labelled "None"
                    // matched the leading "None" entry and silently mapped to -1 (no sort at all).
                    sortColumnIndex = sortField.index - 1; // -1 = the leading "None" entry
                    currentPage = 0;
                    liveRebuild?.Invoke();
                });

                sortDirBtn = new Button(() =>
                {
                    sortDescending = !sortDescending;
                    currentPage = 0;
                    liveRebuild?.Invoke();
                });
                sortDirBtn.AddToClassList("sasq-pager-btn");
                // An icon button, hidden until a sort is actually active — see UpdateSortDirectionButton.
                StyleSortDirectionButton(sortDirBtn);
                EditorSpringFX.ButtonSpring(sortDirBtn);

                sortRow.Add(sortField);
                sortRow.Add(sortDirBtn);
                // Onto the shared toolbar line — and the dropdown stops growing there, because the search
                // box is what should eat the spare width, not the sort control.
                sortField.style.flexGrow = 0;
                // GROW and SHRINK are independent, and only grow was ever meant here: `flexGrow = 0` on the
                // field already says "don't take the spare width". Pinning flexShrink to 0 as well said
                // something quite different — never give width BACK either — so the control stayed frozen at
                // whatever its longest column name measured and pushed the toolbar 21px past the inspector on
                // a 300pt pane (measured 2026-09-02: the row ran to x=302 inside a parent ending at 262).
                // Column names are user text, so no width makes "it will always fit" true. The floor below is
                // what keeps the dropdown clickable rather than letting it collapse to a sliver.
                sortRow.style.flexShrink = 1;
                sortRow.style.minWidth = 0;
                sortField.style.minWidth = 72;
                // ONE LEVEL DEEPER TOO. Making the field shrink is only half of it: Unity builds a popup
                // field's own input container as flexShrink 0, so the outer field obediently came down to
                // 258px while the input inside it stayed at its content width and hung 40px out — the same
                // shear, just moved down a level. A BaseField's visualInput exists from construction, so it
                // can be reached here rather than waiting for an attach callback.
                var sortInput = sortField.Q<VisualElement>(className: "unity-base-popup-field__input");
                if (sortInput != null)
                {
                    sortInput.style.flexShrink = 1;
                    sortInput.style.minWidth = 0;
                }
                searchHost.Add(sortRow);

                // The row DeriveSortKeys hides when nothing about this element can be ordered, and the
                // control it pushes refreshed choices into. Assigned after the build, then re-run once so a
                // collection that already knows it has no orderable key never flashes the control at all.
                sortRowHost = sortRow;
                if (sortColumns.Count == 0) sortRow.style.display = DisplayStyle.None;
            }

            // Sort note ("Sorted by X ▲ · reorder paused [clear]") — the clear path back to natural order.
            Label sortNoteLabel = null;
            var sortNote = MakeSortNote(out sortNoteLabel, out var sortNoteDirIcon, () =>
            {
                sortColumnIndex = -1;
                currentPage = 0;
                liveRebuild?.Invoke();
            });
            toolbar.Add(sortNote);

            // Pagination toolbar (shown in design too as a preview; buttons disabled there).
            Label pageLabel = null, countLabel = null;
            Button prevBtn = null, nextBtn = null;
            VisualElement pagerDots = null;
            var pager = MakeCollectionPager(out pageLabel, out countLabel, out prevBtn, out nextBtn, out pagerDots,
                onPrev: () => { currentPage = Mathf.Max(0, currentPage - 1); liveRebuild?.Invoke(); },
                onNext: () => { currentPage += 1; liveRebuild?.Invoke(); });
            toolbar.Add(pager);

            contentTarget.Add(toolbar);

            void UpdateListSortUi()
            {
                bool sorted = sortColumnIndex >= 0 && sortColumnIndex < sortColumnLabels.Count;
                if (sortField != null)
                {
                    sortField.SetValueWithoutNotify(sorted ? sortChoices[sortColumnIndex + 1] : "None");
                    // Disabled in design (preview only); live: the direction toggle is usable once a column
                    // is chosen. The toggle also COLLAPSES entirely while nothing is sorted — a permanently
                    // disabled control is noise, and unlike the row actions there is nothing to reserve
                    // space for, so it is display (which reflows) rather than opacity (which does not).
                    sortField.SetEnabled(!designMode);
                    UpdateSortDirectionButton(sortDirBtn, sorted, sortDescending, interactive: !designMode);
                }
                sortNote.style.display = sorted ? DisplayStyle.Flex : DisplayStyle.None;
                if (sorted)
                    { sortNoteLabel.text = $"Sorted by {sortColumnLabels[sortColumnIndex]}"; SetSortNoteDirection(sortNoteDirIcon, sortDescending); }
            }

            void UpdateListPager() => UpdatePagerUi(pager, pageLabel, countLabel, prevBtn, nextBtn, pagerDots,
                node.PaginationDisplayStyle, p => { currentPage = Mathf.Max(0, p); liveRebuild?.Invoke(); },
                node.EnablePagination, !designMode, node.PageSize, lastVisibleCount, currentPage, lastPageStart, lastPageCount);

            var itemsContainer = new VisualElement();
            itemsContainer.style.flexDirection = FlexDirection.Column;

            // Reorder affordances follow the node's ReorderStyle in BOTH live and design mode, so Design
            // Mode previews the chosen style. Live rows wire real drag/▲▼; design rows show the matching
            // affordances but reorder through the CanvasDragController grip (drag) — and they're gated so
            // a drag-less collection can't be drag-reordered, matching the live behavior.
            // ResolveReorderStyle collapses Arrows/Both to DragAndDrop (they are no longer offered), so
            // showArrows is currently always false and the ▲▼ paths below are unreachable — kept, not
            // deleted, so re-offering arrows is a one-line change rather than a rewrite.
            var effectiveReorder = ResolveReorderStyle(node.ReorderStyle);
            bool showDrag = effectiveReorder == CollectionReorderStyle.DragAndDrop || effectiveReorder == CollectionReorderStyle.Both;
            bool showArrows = effectiveReorder == CollectionReorderStyle.Arrows || effectiveReorder == CollectionReorderStyle.Both;
            bool liveShowGrip = !designMode && showDrag;
            bool liveShowArrows = !designMode && showArrows;
            bool designShowGrip = designMode && showDrag;
            bool designShowArrows = designMode && showArrows;

            // The element count these rows were last drawn from — the external-size sync compares against it
            // so the widget's OWN +/- (which already rebuilt in place) never triggers a second rebuild.
            int renderedArraySize = -1;

            // The per-element row construction, factored so the LIVE in-place rebuild can re-run it without
            // touching the rest of the inspector. Local function: shares every local declared above by ref.
            void PopulateListRows()
            {
            // A repopulate is a page hop, a sort, a search, an add or a delete — in every one of those the
            // held multi-selection either points somewhere the user can no longer see or at elements that
            // have shifted. One clear covers the lot; a committed REORDER does not come through here (it
            // reflows values into the existing rows), so a dragged block stays lit where it landed.
            CollectionRowSelection.For(itemsContainer)?.Clear();
            renderedArraySize = prop.arraySize;
            // Which of the rows about to be built are NEW — an add hands the range over one call before it
            // triggers this rebuild. Taken ONCE, up here, because a rebuild recreates EVERY row and an
            // entrance played per-row without a range turns each add into a wave down the whole list. Empty
            // for every other reason a rebuild happens (a page hop, a sort, a search, a delete). Keep in
            // lockstep with the Table twin in BuildTableLiveRows.
            var rowEntrance = CollectionRowEntrance.Consume(prop, designMode);
            // Filter (search) -> sort (view-only) -> page window. Only the current page's rows are built,
            // which is the real perf win for large lists (BuildNode runs <= PageSize times, not arraySize).
            var visible = CollectionViewModel.BuildVisibleIndices(prop, searchField?.value, node.SearchProperty, SortPath(), sortDescending);
            // Paginate in design too so the pager preview is accurate (design then shows page 1's rows).
            int pageSizeEff = node.EnablePagination ? Mathf.Max(1, node.PageSize) : Mathf.Max(1, visible.Count);
            // Hard safety cap (see HardMaxUnpagedRows): even with pagination OFF, never build more than that many
            // rows in one pass — a multi-thousand-row list would otherwise freeze the editor building every row
            // eagerly. Above the cap we build the first N (currentPage stays 0, pager hidden) + a note below.
            if (!node.EnablePagination && pageSizeEff > HardMaxUnpagedRows) pageSizeEff = HardMaxUnpagedRows;
            currentPage = CollectionViewModel.ClampPage(currentPage, visible.Count, pageSizeEff);
            var (pageStart, pageCount) = CollectionViewModel.PageSlice(visible.Count, pageSizeEff, currentPage);
            lastVisibleCount = visible.Count; lastPageStart = pageStart; lastPageCount = pageCount;
            // Reorder (drag grip + ▲▼) is only offered in natural order — a sorted/filtered view's display
            // order doesn't map to array indices. Cleared via the sort dropdown's "None" or the sort note.
            bool reorderAllowed = sortColumnIndex < 0 && string.IsNullOrEmpty(searchField?.value);

            for (int k = pageStart; k < pageStart + pageCount; k++)
            {
                int index = visible[k];
                var itemProp = prop.GetArrayElementAtIndex(index);
                VisualElement liveGrip = null;

                var itemRow = new VisualElement();
                itemRow.userData = index;
                itemRow.style.flexDirection = FlexDirection.Row;
                // TOP, like the table row. The body is always the tallest thing here, so centering only ever
                // moved the row's CHROME — on a tall row (In-Line Editor Rows, a Card separator) the [index]
                // and the ✕ ended up floating halfway down a body whose header is at the top.
                itemRow.style.alignItems = Align.FlexStart;
                itemRow.AddToClassList("sasq-list-row");
                itemRow.style.borderTopLeftRadius = 3;
                itemRow.style.borderTopRightRadius = 3;
                itemRow.style.borderBottomLeftRadius = 3;
                itemRow.style.borderBottomRightRadius = 3;
                itemRow.AddToClassList(ListRowStyleClass(listStyle));

                // Separator chrome + zebra in one pass (spacing, rule, card tray) — see ApplyRowSeparator.
                // Zebra tints odd VISIBLE rows so the stripe pattern is stable across pages and filters.
                // suppressRail: Timeline owns the row's left edge, so a Card frame there gives up the tray's
                // own 3px rail rather than drawing two rails a few pixels apart.
                ApplyRowSeparator(itemRow, node.RowSeparator, node.RowSeparatorColor,
                    isLast: k == pageStart + pageCount - 1,
                    isTable: false,
                    zebraTint: node.ZebraStriping && (k & 1) == 1
                        ? node.ZebraColor.Resolve(WidgetPalette.ZebraStripe)
                        : (Color?)null,
                    hoverable: !designMode,
                    suppressRail: ListStyleSuppressesRowRail(listStyle));
                // The padding a STYLE owns but ApplyRowSeparator writes inline (Minimal's air).
                ApplyListRowStyleGeometry(listStyle, itemRow);

                // Ordinal + drag handle in one fixed-width lane (see MakeListGutter). The grip is built LIVE
                // ONLY, and only while the view is in natural order — exactly the condition the live grip
                // below is wired under. Design mode keeps the designer's own `collection-item-grip`
                // affordance instead, so a row never grows two handles.
                VisualElement gutterGripTarget;
                Label indexLabel;
                var gutterLane = MakeListGutter(
                    listStyle, index,
                    withGrip: liveShowGrip && reorderAllowed,
                    out gutterGripTarget, out indexLabel);
                itemRow.Add(gutterLane);

                VisualElement itemBody = null;
                if (node.ItemLayout != null)
                {
                    itemBody = BuildNode(
                        node.ItemLayout,
                        itemProp.propertyPath,
                        serializedObject,
                        structuralRebuild,
                        theme,
                        designMode,
                        selectedNode,
                        dragController,
                        layoutAsset,
                        designInteract,
                        nodePath: itemProp.propertyPath
                    );
                }
                else if (itemProp.propertyType == SerializedPropertyType.ManagedReference)
                {
                    // Polymorphic [SerializeReference] element (no item template) — render the branded type
                    // picker + inline fields rather than Unity's default managed-reference chrome. The label
                    // is hidden because the row already carries the [index] label. DisplayType is set to
                    // SerializeReference (so the picker honors the Searchable flag) and the collection's
                    // polymorphic menu options are propagated onto each element so every picker shares the
                    // configured search + menu styling.
                    itemBody = BindingFactory.CreateBindingField(
                        new BindingNode
                        {
                            PropertyPath = itemProp.propertyPath,
                            DisplayType = DisplayType.SerializeReference,
                            PolymorphicSearchable = node.PolymorphicSearchable,
                            DropdownPopupBackground = node.DropdownPopupBackground,
                            DropdownItemHoverBackground = node.DropdownItemHoverBackground,
                            DropdownItemHoverColor = node.DropdownItemHoverColor,
                            DropdownItemTextStyle = node.DropdownItemTextStyle,
                        },
                        itemProp.propertyPath, serializedObject, forceHideLabel: true);
                }
                else if (node.ElementInlineEditor && itemProp.propertyType == SerializedPropertyType.ObjectReference)
                {
                    // In-line editor PER ROW: the element's object field plus that object's own inspector,
                    // under a chevron. Without this a list of ScriptableObjects/components can only be
                    // reassigned from the inspector, which is why layouts grow a "focused item" helper field
                    // + Up/Down buttons just to edit one element at a time. The row already carries the
                    // [index] label, so the field's own label is hidden.
                    itemBody = BindingFactory.CreateBindingField(
                        new BindingNode
                        {
                            PropertyPath = itemProp.propertyPath,
                            DisplayType = DisplayType.InlineEditor,
                            Expandable = true,
                            DefaultToOpen = node.ElementDefaultToOpen,
                        },
                        itemProp.propertyPath, serializedObject, forceHideLabel: true);
                }
                else
                {
                    var pf = new PropertyField(itemProp, "");
                    pf.style.flexGrow = 1;
                    itemBody = pf;
                }

                if (itemBody != null)
                {
                    // NEVER grow inside a CardStack row: that row is a COLUMN, so flexGrow is the VERTICAL
                    // axis there — the body would inflate to the tray's cross-size and overflow it. The
                    // column's default align-items: stretch is what gives it full width, not grow. (Same
                    // trap documented on the table cell / column cell paths.)
                    // CLEARED, not merely skipped: several body branches set grow on THEMSELVES (the plain
                    // PropertyField does), so not assigning it here leaves the vertical claim in place.
                    itemBody.style.flexGrow = 1f;
                    itemRow.Add(itemBody);
                }

                if (designMode)
                {
                    itemRow.AddToClassList("collection-item-row");
                    // Drag grip — only for styles that include drag-and-drop, mirroring the live affordance.
                    if (designShowGrip)
                    {
                        var itemGrip = new VisualElement();
                        itemGrip.AddToClassList("collection-item-grip");
                        var itemGlyph = new GripGlyph();
                        itemGrip.Add(itemGlyph);
                        itemRow.Insert(0, itemGrip);
                        itemRow.AddToClassList("grip-host");

                        var itemGripLine = new VisualElement { pickingMode = PickingMode.Ignore };
                        itemGripLine.AddToClassList("designer-grip-line");
                        itemRow.Add(itemGripLine);
                        itemGrip.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            itemGripLine.style.display = DisplayStyle.Flex;
                            itemGlyph.Tint = GripGlyph.HoverTint;
                        });
                        itemGrip.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            itemGripLine.style.display = DisplayStyle.None;
                            itemGlyph.Tint = GripGlyph.DefaultTint;
                        });
                    }

                    // Disable interactivity on item fields in Design Mode so dragging is prioritized.
                    // PERSISTENT, exactly like the leaf-card path: this used to be a one-shot sweep plus a
                    // single self-unregistering GeometryChanged re-apply, and a row's PropertyField builds its
                    // inner TextField/TextInput/TextElement DURING bind — after that one re-apply had already
                    // fired and torn itself down. The result was a string row you could click into and TYPE in
                    // with Interact OFF (rows 0 won the race, rows 1+ lost it). An in-line-editor row is worse
                    // still: its nested inspector builds and REBUILDS over many frames.
                    // Safe to sweep the whole subtree here because this branch is ItemLayout-LESS — the body is
                    // a plain PropertyField / in-line editor / type picker, never a stack of selectable child
                    // cards (an ItemLayout's cards each suppress their own interior and stay pickable).
                    if (itemBody != null && node.ItemLayout == null && !designInteract)
                    {
                        SuppressInteractionPersistently(itemBody);
                    }

                    // Manual ▲▼ reorder — only for styles that include arrows, mirroring the live affordance.
                    // Design-mode reorder rebuilds the canvas (re-evaluates templates/selection), so these
                    // keep onRebuildRequired rather than the live in-place refresh.
                    if (designShowArrows)
                    {
                        var moveUpBtn = new Button(() =>
                        {
                            if (index > 0)
                            {
                                prop.MoveArrayElement(index, index - 1);
                                serializedObject.ApplyModifiedProperties();
                                onRebuildRequired?.Invoke();
                            }
                        });
                        moveUpBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronUp, 9));
                        moveUpBtn.style.alignItems = Align.Center;
                        moveUpBtn.style.justifyContent = Justify.Center;
                        moveUpBtn.style.alignSelf = Align.Center; // row chrome centres (live twin: StyleTableRowButton)
                        moveUpBtn.style.paddingLeft = 4;
                        moveUpBtn.style.paddingRight = 4;
                        moveUpBtn.style.marginLeft = 2;
                        moveUpBtn.style.marginRight = 2;
                        if (index == 0) moveUpBtn.SetEnabled(false);
                        if (!designInteract) DisableInteractivityRecursively(moveUpBtn); // design select-only (designShowArrows ⇒ designMode)
                        itemRow.Add(moveUpBtn);

                        var moveDownBtn = new Button(() =>
                        {
                            if (index < prop.arraySize - 1)
                            {
                                prop.MoveArrayElement(index, index + 1);
                                serializedObject.ApplyModifiedProperties();
                                onRebuildRequired?.Invoke();
                            }
                        });
                        moveDownBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronDown, 9));
                        moveDownBtn.style.alignItems = Align.Center;
                        moveDownBtn.style.justifyContent = Justify.Center;
                        moveDownBtn.style.alignSelf = Align.Center; // row chrome centres (live twin: StyleTableRowButton)
                        moveDownBtn.style.paddingLeft = 4;
                        moveDownBtn.style.paddingRight = 4;
                        moveDownBtn.style.marginLeft = 2;
                        moveDownBtn.style.marginRight = 2;
                        if (index == prop.arraySize - 1) moveDownBtn.SetEnabled(false);
                        if (!designInteract) DisableInteractivityRecursively(moveDownBtn); // design select-only
                        itemRow.Add(moveDownBtn);
                    }

                    // Pointer-based reorder is handled by the controller; only enable it for styles that
                    // include drag-and-drop (so an Arrows-only collection can't be drag-reordered, matching
                    // the live behavior). Tag the row with its array index so the controller can map a drop
                    // slot back to a MoveArrayElement.
                    if (dragController != null && designShowGrip)
                    {
                        itemRow.userData = index;
                        itemRow.AddManipulator(new CanvasDragManipulator(dragController, CanvasDragPayload.MoveItem(prop.propertyPath, index)));
                    }
                }
                else
                {
                    // Live preview / real inspector: reorder via grip drag and/or ▲▼ per ReorderStyle —
                    // but only while the view is in natural order (no sort/filter remaps the rows).
                    itemRow.userData = index;
                    if (liveShowGrip && reorderAllowed)
                    {
                        // EXPERIMENT: the skinned gutter already CONTAINS the handle (stacked under the
                        // ordinal), so the drag target is the whole lane rather than a 14x7 nub bolted to
                        // the left of the row. Classic keeps the standalone grip.
                        liveGrip = gutterGripTarget ?? MakeReorderGrip();
                        if (gutterGripTarget == null) itemRow.Insert(0, liveGrip);
                    }
                    if (liveShowArrows && reorderAllowed)
                    {
                        // hoverReveal on a skinned row: the ▲▼ fade in with the rest of the row chrome
                        // instead of parking a permanent pair of buttons on every row.
                        AddReorderArrows(itemRow, prop, serializedObject, index, itemsContainer, hoverReveal: true);
                    }
                    // Right-click row menu: Duplicate / Insert Above / Insert Below / Delete. Always
                    // available — it operates on the raw array index, order-agnostic.
                    CollectionRowMenu.Attach(itemRow, prop, serializedObject, index, structuralRebuild,
                        () => revealInserted?.Invoke());
                }

                var delBtn = new Button(() =>
                {
                    int oldSize = prop.arraySize;
                    prop.DeleteArrayElementAtIndex(index);
                    if (prop.arraySize == oldSize)
                    {
                        prop.DeleteArrayElementAtIndex(index);
                    }
                    serializedObject.ApplyModifiedProperties();
                    structuralRebuild?.Invoke();
                });
                // A hover-revealed ✕, matching the Table row and the UnityEvent card. Design mode opts OUT
                // of the reveal — there, hover already means "this is the node you'd select", and chrome
                // that vanishes is chrome you cannot design against. StyleListDeleteButton springs it.
                StyleListDeleteButton(delBtn, hoverReveal: !designMode);
                if (designMode && !designInteract) DisableInteractivityRecursively(delBtn); // design select-only
                itemRow.Add(delBtn);

                itemsContainer.Add(itemRow);
                // AFTER the Add: the seed has to be on the element before its first paint, and the spring
                // needs the row on a panel to be ticked at all.
                rowEntrance.Play(itemRow, index);

                if (liveGrip != null)
                {
                    // Paginated reorder is within-page; indexOffset maps the page-relative drop gap back to
                    // an absolute array index (userData/fromIndex are already absolute).
                    // allowMultiSelect: Ctrl/Shift-click the gutter to pick several rows and drag them as
                    // one block. Opt-in per call site — the UnityEvent, dictionary and palette lists keep
                    // the plain single-row gesture.
                    // onStructuralChange: the rows-only rebuild that Delete/Backspace needs to redraw the
                    // collection after removing the picked rows.
                    CollectionRowReorder.Attach(liveGrip, itemRow, itemsContainer, prop, serializedObject,
                        indexOffset: pageStart, allowMultiSelect: true,
                        onStructuralChange: structuralRebuild);
                }
            }

            // Live-only "nothing matched" note.
            if (!designMode && pageCount == 0 && prop.arraySize > 0)
            {
                var none = new Label("No items match your search.");
                none.style.color = WidgetPalette.TextGray;
                none.style.unityFontStyleAndWeight = FontStyle.Italic;
                none.style.fontSize = 11;
                none.style.paddingTop = 4;
                none.style.paddingBottom = 4;
                itemsContainer.Add(SkinListEmptyNote(none));
            }
            // …and the empty-list note, in PARITY with the Table renderer (which always had one). A List used
            // to lean on the "(0)" in its title alone, so an empty list — with a cleared or hidden title — was
            // a bare box with no affordance at all. That is what reads as "this list is broken".
            else if (!designMode && prop.arraySize == 0)
            {
                itemsContainer.Add(SkinListEmptyNote(MakeCollectionEmptyNote(prop)));
            }

            // Hard-cap overflow note: pagination is off but the list exceeds HardMaxUnpagedRows, so only the
            // first N rows were built to keep the editor responsive. Point the user at the Pagination toggle.
            if (!node.EnablePagination && lastVisibleCount > HardMaxUnpagedRows)
            {
                var capped = new Label($"Showing the first {HardMaxUnpagedRows} of {lastVisibleCount} items. " +
                    "Turn on Pagination for this collection (in the Layout Designer) to page through all of them.");
                capped.style.color = WidgetPalette.TextGray;
                capped.style.unityFontStyleAndWeight = FontStyle.Italic;
                capped.style.fontSize = 11;
                capped.style.whiteSpace = WhiteSpace.Normal;
                capped.style.paddingTop = 4;
                capped.style.paddingBottom = 4;
                itemsContainer.Add(capped);
            }

            // Timeline's continuous rail, threaded behind the node discs. Built HERE, inside the row
            // population pass, because it belongs to the ROWS: itemsContainer is Cleared on every in-place
            // rebuild, so anything that lives with the rows has to be rebuilt with them. No-op for every
            // other style.
            AttachTimelineRail(listStyle, itemsContainer, pageCount);

            UpdateListSortUi();
            UpdateListPager();
            }

            PopulateListRows();

            // Dimmed, editable template row backed by a throwaway scratch object (never the real target).
            if (showTemplate)
            {
                var scratchSO = GetTemplateScratch(serializedObject.targetObject?.GetType(), resolvedPath);
                if (scratchSO != null)
                {
                    var scratchProp = scratchSO.FindProperty(resolvedPath);
                    var scratchItem = scratchProp.GetArrayElementAtIndex(0);

                    var templateRow = new VisualElement();
                    templateRow.style.flexDirection = FlexDirection.Row;
                    templateRow.style.alignItems = Align.Center;
                    templateRow.AddToClassList("sasq-list-row");
                    // The template is the last thing in the container, so it separates like a last row: it
                    // takes the node's chrome (a None collection mustn't leave one stray rule behind on the
                    // scaffolding row) but never a trailing rule of its own.
                    templateRow.style.borderTopLeftRadius = 3;
                    templateRow.style.borderTopRightRadius = 3;
                    templateRow.style.borderBottomLeftRadius = 3;
                    templateRow.style.borderBottomRightRadius = 3;
                    ApplyRowSeparator(templateRow, node.RowSeparator, node.RowSeparatorColor,
                        isLast: true, isTable: false, zebraTint: null, hoverable: false);
                    templateRow.style.opacity = 0.6f; // reads as scaffolding, not real data

                    var tmplLabel = new Label("tmpl");
                    tmplLabel.style.width = 32;
                    tmplLabel.style.color = WidgetPalette.TextGray;
                    tmplLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                    tmplLabel.style.marginLeft = 4;
                    templateRow.Add(tmplLabel);

                    var templateBody = BuildNode(
                        node.ItemLayout,
                        scratchItem.propertyPath,
                        scratchSO,
                        onRebuildRequired,
                        theme,
                        designMode,
                        selectedNode,
                        dragController,
                        layoutAsset,
                        designInteract,
                        nodePath: scratchItem.propertyPath
                    );
                    if (templateBody != null)
                    {
                        templateBody.style.flexGrow = 1;
                        templateRow.Add(templateBody);
                        // Bind the template subtree to the scratch object so its fields show real default
                        // values. This is a nested bind to a different SerializedObject (same pattern as
                        // BindingFactory's inline-editor wrapper), so the outer Bind won't clobber it.
                        templateBody.Bind(scratchSO);
                    }

                    itemsContainer.Add(templateRow);
                }
            }

            contentTarget.Add(itemsContainer);

            // EXPERIMENT: the "＋ Add Item" footer strip. OUTSIDE itemsContainer on purpose — an in-place
            // rebuild Clears that container, and a footer built inside it would be destroyed on every add.
            var addFooter = MakeListAddFooter(prop, serializedObject, structuralRebuild, designMode && !designInteract,
                () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); });
            if (addFooter != null) contentTarget.Add(addFooter);

            // Search drives a FILTER-THEN-PAGE rebuild (reset to page 0): the rebuild recomputes the visible
            // set + page and builds only that page — so search composes with sort + pagination, instead of
            // just display-toggling already-built rows.
            if (searchField != null && !designMode)
            {
                searchField.RegisterValueChangedCallback(_ => { currentPage = 0; liveRebuild?.Invoke(); });
            }

            // LIVE in-place row refresh: rebuild ONLY this collection's rows + "(N)" count + search box —
            // never the whole inspector. structuralRebuild routes +/-/insert/duplicate/delete here in live
            // mode (see the top of this method). The outer Build/Bind owns the initial rows; on rebuild we
            // re-read the array and re-bind just this subtree, leaving every sibling field untouched.
            void RebuildListRowsInPlace()
            {
                serializedObject.Update();
                itemsContainer.Clear();
                // BEFORE the rows: the sort keys were derived from element 0, and on a list that opened
                // EMPTY there was no element 0 to derive them from. This is the only moment that can notice
                // the first row arriving — a no-op on every list that already had one.
                RefreshSortKeysIfSampleArrived();
                // Search box FIRST, rows second. PopulateListRows filters by searchField?.value, and
                // SyncListSearchField removes the box below 2 elements — so in the other order, shrinking a
                // searchable collection to one element filtered the survivor out and THEN took away the box:
                // "No items match your search." over a non-empty array, with no control left to clear it.
                // Retiring the box first makes the query null for this rebuild, which is what the user sees.
                SyncListSearchField();
                PopulateListRows(); // recomputes filter/sort/page; refreshes the sort note + pager
                // The header keeps its count in a PILL, so retexting the title with CollectionHeaderText
                // would paste the "(N)" back in beside it. A blanked title has no pill and needs neither.
                if (countPill != null) countPill.text = prop.arraySize.ToString();
                // A list that just went empty has nothing left to clear, and one that just got its first
                // row does. The header is not rebuilt here, so this is the only place that can say so.
                CollectionClearAll.Sync(clearBtn, prop);
                itemsContainer.Bind(serializedObject);
            }

            // The search box only exists at 2+ elements; keep it in sync across an in-place rebuild without
            // a full teardown (create/remove on the 1↔2 boundary). The box lives in `toolbar` (not
            // itemsContainer), so it survives the Clear above; recreation reuses the filter-then-page callback.
            void SyncListSearchField()
            {
                bool wantSearch = node.Searchable && !designMode && prop.arraySize >= 2;
                if (wantSearch && searchField == null)
                {
                    searchField = MakeCollectionSearchField();
                    searchField.RegisterValueChangedCallback(_ => { currentPage = 0; liveRebuild?.Invoke(); });
                    // Classic: top of the toolbar COLUMN. Skinned: first cell of the toolbar LINE, ahead of
                    // the sort control and growing to eat the spare width — the same shape the initial
                    // build produces, so crossing the 1↔2 element boundary is invisible.
                    if (toolbarLine != null) searchField.style.flexGrow = 1;
                    searchHost.Insert(0, searchField);
                }
                else if (!wantSearch && searchField != null)
                {
                    searchField.RemoveFromHierarchy();
                    searchField = null;
                }
            }

            if (!designMode) liveRebuild = RebuildListRowsInPlace;

            // Clear the three pieces of view state that can hide a freshly appended element, so the rebuild that
            // follows draws it. Natural order + no filter + the last page = the new rows are on screen, at the
            // bottom, where the user expects them. Each clear is one the user could already make by hand (the
            // sort note's "clear", emptying the search box, the pager) — nothing here is a new concept.
            //   * search: an append is exempt from a filter the user typed to find something ELSE.
            //   * sort:   a view-only sort scatters the appended run and also disables reorder, so the user
            //             cannot even drag them back into place.
            //   * page:   int.MaxValue is deliberate — ClampPage pins it to the LAST page without this scope
            //             needing to know the effective page size (which depends on the search result count).
            //             Only when pagination is ON: with it off the pager is hidden, so moving the window
            //             past HardMaxUnpagedRows would strand the user with no way back.
            // Keep in lockstep with the Table twin.
            revealAppended = () =>
            {
                if (searchField != null && !string.IsNullOrEmpty(searchField.value))
                    searchField.SetValueWithoutNotify(string.Empty);
                sortColumnIndex = -1;
                sortDescending = false;
                currentPage = node.EnablePagination ? int.MaxValue : 0;
            };

            // Insert Above / Insert Below: the first two clears, and NOT the page one. An insert lands at a
            // named index next to a row the user is looking at, so the page is already right — jumping to the
            // last page would carry them away from the row they just made. Keep in lockstep with the Table twin.
            revealInserted = () =>
            {
                if (searchField != null && !string.IsNullOrEmpty(searchField.value))
                    searchField.SetValueWithoutNotify(string.Empty);
                sortColumnIndex = -1;
                sortDescending = false;
            };

            AttachExternalArraySizeSync(container, prop, designMode, () => renderedArraySize, RebuildListRowsInPlace);

            return container;
        }

        // A collection maintains its rows through its OWN +/- controls. When something OUTSIDE the widget
        // resizes the array — a ButtonNode method appending to the list (the reported case), an Undo, another
        // inspector, a running script — nothing in here runs, so the rows sat at the stale count until the
        // whole inspector was rebuilt by hand. Watching the array's own size property closes that gap by
        // re-running the SAME in-place row rebuild the +/- buttons use: the refresh stays localized to this
        // widget and never becomes a whole-inspector teardown. Design mode has no live data to track.
        //
        // INTERNAL, not private: any widget that renders one element per array entry has this exact gap, and
        // the mechanism is fiddlier than it looks — a tracker on the PARENT property does NOT see a child
        // array resize, and undo restores data outside the tracker's change-vs-baseline comparison entirely,
        // so it needs its own event. UnityEventWidget's listener cards are the second caller.
        internal static void AttachExternalArraySizeSync(VisualElement host, SerializedProperty prop,
            bool designMode, Func<int> renderedCount, Action rebuildRowsInPlace)
        {
            if (designMode || host == null || prop == null || rebuildRowsInPlace == null) return;
            // `isArray` is true for strings too — a string's "size" is its character count, not a row count.
            if (!prop.isArray || prop.propertyType == SerializedPropertyType.String) return;

            var so = prop.serializedObject;
            if (so == null) return;
            var sizeProp = so.FindProperty(prop.propertyPath + ".Array.size");
            if (sizeProp == null) return;

            // Compares the array's CURRENT size against what these rows were last drawn from, so the
            // widget's own +/- (which already rebuilt in place) never triggers a second rebuild.
            void SyncNow(SerializedProperty size)
            {
                if (size == null) return;
                int now;
                try { now = size.intValue; }
                catch { return; } // the property died with its inspector mid-teardown
                if (renderedCount != null && renderedCount() == now) return;
                rebuildRowsInPlace();
            }

            host.TrackPropertyValueWithRefresh(sizeProp, SyncNow);

            // Undo/redo needs its own trigger. Unity's trackers fire on change-vs-baseline at the next
            // BINDING tick, and an undo restores the data outside that comparison — so a deleted row could
            // come back in the data while the inspector still showed the shortened list. The event is
            // authoritative and immediate; it is dropped when the widget leaves the panel so a rebuilt
            // inspector never accumulates handlers against dead SerializedObjects.
            Undo.UndoRedoCallback onUndoRedo = null;
            onUndoRedo = () =>
            {
                // Panel-less: either torn down (the real-detach release below drops us shortly) or momentarily
                // re-parented — either way there is nothing to sync, and unsubscribing HERE would kill the
                // sync of a widget that is about to come back.
                if (host.panel == null) return;
                try
                {
                    so.Update();
                    SyncNow(so.FindProperty(prop.propertyPath + ".Array.size"));
                }
                catch { Undo.undoRedoPerformed -= onUndoRedo; } // target destroyed under the undo
            };
            Undo.undoRedoPerformed += onUndoRedo;
            // Dropped on a REAL teardown only: a re-parent (window tab drag, row reorder) re-attaches this
            // same host, and unsubscribing on any detach left undo/redo unable to re-sync the row count.
            PanelLifetime.OnRealDetach(host, () => Undo.undoRedoPerformed -= onUndoRedo);
        }

        /// <summary>
        /// True when <paramref name="prop"/> is an array a collection widget can render one row per element
        /// of. <c>isArray</c> alone is NOT that question — it answers "yes" for two shapes that must never
        /// reach the List/Table renderers:
        /// <list type="bullet">
        /// <item>a <c>string</c>, whose "size" is its character count, not a row count;</item>
        /// <item>on Unity 6.6+, a native <c>Dictionary&lt;K,V&gt;</c>, which reports <c>isArray == true</c>
        /// with <c>arraySize</c> == entry count and <c>Array.data[i].key</c>/<c>.value</c> children.</item>
        /// </list>
        /// <para>
        /// A <c>CollectionNode</c> pointed at a dictionary therefore sailed straight through the
        /// dead-collection gate and drew the LIST widget over it: rows of raw pair structs, a drag grip
        /// writing an order Unity's dictionary discards, and an add button doing <c>arraySize++</c> on a
        /// dictionary. Discovery cannot produce that node (<c>IsCollectionType</c> rejects Dictionary, so
        /// every automatic producer emits a BindingNode instead) but a code-authored layout, a hand-typed
        /// Property Path and a <c>List</c>→<c>Dictionary</c> refactor all reach it.
        /// </para>
        /// <para>
        /// ONE predicate for BOTH gates on purpose: <c>BuildCollectionElement</c> dispatches to the Table
        /// builder before reaching its own copy, so patching one site would leave the other open.
        /// </para>
        /// </summary>
        private static bool IsRenderableCollectionProperty(SerializedProperty prop) =>
            prop != null
            && prop.isArray
            && prop.propertyType != SerializedPropertyType.String
            && !BindingFactory.IsNativeDictionaryProperty(prop);

        /// <summary>
        /// What the designer's red placeholder says for a collection it cannot render. A path that resolves
        /// to a DICTIONARY is a different mistake from one that resolves to nothing, and calling it "Missing"
        /// sends the user hunting for a typo in a field name they can plainly see in their script.
        /// </summary>
        private static string DeadCollectionMessage(SerializedProperty prop, string resolvedPath)
        {
            if (prop != null && BindingFactory.IsNativeDictionaryProperty(prop))
                return $"'{resolvedPath}' is a Dictionary, not a list — bind it as a field and set its " +
                       "Display Type to Dictionary Table.";
            return $"Missing collection: '{resolvedPath}'";
        }

        // The framed box shared by the List and Table collection renderers.
        private static VisualElement CreateCollectionFrame()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            container.style.borderTopWidth = 1;
            container.style.borderBottomWidth = 1;
            container.style.borderLeftWidth = 1;
            container.style.borderRightWidth = 1;

            container.style.borderTopColor = WidgetPalette.BorderSofter;
            container.style.borderBottomColor = WidgetPalette.BorderSofter;
            container.style.borderLeftColor = WidgetPalette.BorderSofter;
            container.style.borderRightColor = WidgetPalette.BorderSofter;

            container.style.borderTopLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderBottomRightRadius = 4;

            container.style.paddingTop = 6;
            container.style.paddingBottom = 6;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            container.style.marginBottom = 6;
            return container;
        }

        // The "+" append-element button shared by the List and Table collection renderers.
        //
        // onAppended is the ensureExpanded/revealAppended pair — the SAME one the object drop and the
        // eyedropper pass. It was written for those two and never wired here, which made the "+" the one
        // append path that could still land off-screen: a 26th row behind a page boundary, a fresh blank
        // element behind a live search query, or a scattered row under an active sort. The count went up and
        // nothing else moved, so the button read as broken and got clicked again. Runs AFTER the grow (the
        // page clamp needs the new size) and BEFORE the rebuild (the rebuild is what draws the row).
        private static Button CreateCollectionAddButton(SerializedProperty prop, SerializedObject serializedObject, Action onRebuildRequired, bool suppress = false, Action onAppended = null)
        {
            var addBtn = new Button(() =>
            {
                // The new element lands at the END, so the array's size BEFORE the grow IS its index. Armed
                // ahead of the rebuild below, because that rebuild is what actually draws the row — see
                // CollectionRowEntrance for why the adder is the only place that can name the new index.
                CollectionRowEntrance.Arm(prop, prop.arraySize);
                prop.arraySize++;
                serializedObject.ApplyModifiedProperties();
                onAppended?.Invoke();
                onRebuildRequired?.Invoke();
            }) { text = "+" };
            addBtn.style.paddingLeft = 8;
            addBtn.style.paddingRight = 8;
            EditorSpringFX.ButtonSpring(addBtn);
            // Design select-only (Interact OFF): make the button inert so a click falls through to SELECT the
            // collection card instead of adding a row + rebuilding the canvas (which would also lose selection).
            if (suppress) DisableInteractivityRecursively(addBtn);
            return addBtn;
        }

        // The italic "nothing here yet" aside shared by the List and Table collection renderers.
        //
        // It names BOTH ways to fill the collection, because on an empty one neither is obvious: there are no
        // rows, so the only visible control is a lone "+", and the object-drop surface (CollectionObjectDrop)
        // is invisible until a drag is already over it. A tester who tried dragging into a bare box and got
        // nothing is exactly how the missing drop support was reported in the first place.
        private static Label MakeCollectionEmptyNote(SerializedProperty prop)
        {
            bool takesObjects = CollectionObjectDrop.ResolveObjectElementType(prop) != null;
            var empty = new Label(takesObjects
                ? "List is empty — click + to add a row, or drag objects here."
                : "List is empty — click + to add a row.");
            empty.style.color = WidgetPalette.TextGray;
            empty.style.unityFontStyleAndWeight = FontStyle.Italic;
            empty.style.fontSize = 11;
            empty.style.whiteSpace = WhiteSpace.Normal;
            empty.style.paddingTop = 4;
            empty.style.paddingBottom = 4;
            return empty;
        }

        // The "Search..." filter box shared by the List and Table collection renderers.
        private static TextField MakeCollectionSearchField(bool suppress = false)
        {
            var searchField = new TextField();
            searchField.AddToClassList("sasq-collection-search");
            searchField.textEdition.placeholder = "Search...";
            // Design select-only: make it non-focusable/typeable so a click falls through to select.
            // Persistent — a TextField's inner text element is the one part of it that can (re)appear after
            // the initial sweep, and this box is the most obviously typeable thing on a collection card.
            if (suppress) SuppressInteractionPersistently(searchField);
            return searchField;
        }

        // The Prev/Next pagination toolbar shared by the List and Table collection renderers. Hidden until
        // UpdatePagerUi reveals it (only when the visible count exceeds the page size).
        private static VisualElement MakeCollectionPager(out Label pageLabel, out Label countLabel,
            out Button prevBtn, out Button nextBtn, out VisualElement dotsContainer, Action onPrev, Action onNext)
        {
            var pager = new VisualElement();
            pager.AddToClassList("sasq-collection-pager");
            pager.style.flexDirection = FlexDirection.Row;
            pager.style.alignItems = Align.Center;

            prevBtn = new Button(onPrev) { text = "‹ Prev" };
            prevBtn.AddToClassList("sasq-pager-btn");
            EditorSpringFX.ButtonSpring(prevBtn);
            pageLabel = new Label { name = "sasq-pager-label" };
            pageLabel.AddToClassList("sasq-pager-label");
            nextBtn = new Button(onNext) { text = "Next ›" };
            nextBtn.AddToClassList("sasq-pager-btn");
            EditorSpringFX.ButtonSpring(nextBtn);
            // iOS-style clickable page dots — populated per rebuild by UpdatePagerUi → RebuildPagerDots.
            dotsContainer = new VisualElement();
            dotsContainer.AddToClassList("sasq-pager-dots");
            dotsContainer.style.flexDirection = FlexDirection.Row;
            dotsContainer.style.alignItems = Align.Center;
            countLabel = new Label { name = "sasq-pager-count" };
            countLabel.AddToClassList("sasq-pager-count");

            pager.Add(prevBtn);
            pager.Add(pageLabel);
            pager.Add(nextBtn);
            pager.Add(dotsContainer);
            pager.Add(countLabel);
            pager.style.display = DisplayStyle.None;
            return pager;
        }

        // Refreshes the pager labels/buttons + visibility for the current page. show = pagination on (live)
        // AND there's more than one page's worth of visible rows, so small/filtered-small lists hide it.
        // interactive=false (design mode) shows the pager as a faithful PREVIEW of the inspector chrome with
        // its buttons disabled — the designer is for authoring, the live inspector is where paging works.
        private static void UpdatePagerUi(VisualElement pager, Label pageLabel, Label countLabel,
            Button prevBtn, Button nextBtn, VisualElement dotsContainer, PaginationDisplayStyle style,
            Action<int> onGoToPage, bool enabled, bool interactive, int pageSize, int visibleCount,
            int currentPage, int pageStart, int pageCount)
        {
            int pageSizeEff = Mathf.Max(1, pageSize);
            int pages = CollectionViewModel.PageCount(visibleCount, pageSizeEff);
            bool show = enabled && visibleCount > pageSizeEff;
            pager.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            bool showButtons = style == PaginationDisplayStyle.ButtonsOnly || style == PaginationDisplayStyle.ButtonsAndDots;
            bool showDots = style == PaginationDisplayStyle.DotsOnly || style == PaginationDisplayStyle.ButtonsAndDots;

            // Prev/Next buttons + "Page X of Y" label.
            prevBtn.style.display = showButtons ? DisplayStyle.Flex : DisplayStyle.None;
            nextBtn.style.display = showButtons ? DisplayStyle.Flex : DisplayStyle.None;
            pageLabel.style.display = showButtons ? DisplayStyle.Flex : DisplayStyle.None;
            if (showButtons)
            {
                pageLabel.text = $"Page {currentPage + 1} of {pages}";
                prevBtn.SetEnabled(interactive && currentPage > 0);
                nextBtn.SetEnabled(interactive && currentPage < pages - 1);
            }

            // Clickable page dots (current dot enlarged; windowed when there are many pages).
            dotsContainer.style.display = showDots ? DisplayStyle.Flex : DisplayStyle.None;
            if (showDots) RebuildPagerDots(dotsContainer, pages, currentPage, interactive, onGoToPage);

            countLabel.text = pageCount == 0
                ? "showing 0 of 0"
                : $"showing {pageStart + 1}–{pageStart + pageCount} of {visibleCount}";
        }

        // Page-dot count cap before switching from "a dot per page" to a windowed set (first + last + a
        // window around the current page, with "…" gap markers that jump into the skipped span). Keeps the
        // dot strip from overflowing the collection card on huge lists.
        private const int MaxPagerDots = 11;
        private const int PagerDotWindowRadius = 2;

        // The page indices to render as dots: every page when there are few, otherwise first + a window
        // around the current page + last (callers insert "…" gap markers between non-adjacent indices).
        // Pure + internal so it can be unit-tested without a panel.
        internal static List<int> ComputePagerDotIndices(int pages, int currentPage)
        {
            var indices = new List<int>();
            if (pages <= 0) return indices;
            if (pages <= MaxPagerDots)
            {
                for (int i = 0; i < pages; i++) indices.Add(i);
                return indices;
            }
            void AddIdx(int i) { if (i >= 0 && i < pages && !indices.Contains(i)) indices.Add(i); }
            AddIdx(0);
            for (int i = currentPage - PagerDotWindowRadius; i <= currentPage + PagerDotWindowRadius; i++) AddIdx(i);
            AddIdx(pages - 1);
            indices.Sort();
            return indices;
        }

        // (Re)builds the page-dot strip for the current page count + selection. Each dot click jumps straight
        // to that page via onGoToPage; the current page's dot carries the .active class (rendered larger).
        private static void RebuildPagerDots(VisualElement container, int pages, int currentPage, bool interactive, Action<int> onGoToPage)
        {
            // Remember the page we last rendered as active so we only "pop" the active dot when the page actually
            // CHANGES (a real navigation) — not on every search/sort rebuild, which would pulse it constantly.
            int lastActive = container.userData is int prevActive ? prevActive : -1;
            container.Clear();
            if (pages <= 1) { container.userData = currentPage; return; }

            var indices = ComputePagerDotIndices(pages, currentPage);

            int prev = -1;
            foreach (int idx in indices)
            {
                if (prev >= 0 && idx - prev > 1)
                {
                    int jumpTo = (prev + idx) / 2; // ellipsis jumps to the middle of the skipped span
                    var gap = new Label("…");
                    gap.AddToClassList("sasq-pager-dot-gap");
                    if (interactive)
                    {
                        gap.AddToClassList("sasq-clickable");
                        EditorSpringFX.HoverScale(gap, 1.25f);
                        gap.RegisterCallback<PointerDownEvent>(e => { onGoToPage?.Invoke(jumpTo); e.StopPropagation(); });
                    }
                    container.Add(gap);
                }

                int page = idx; // capture for the click closure
                var dot = new VisualElement();
                dot.AddToClassList("sasq-pager-dot");
                if (page == currentPage) dot.AddToClassList("active");
                dot.tooltip = $"Page {page + 1}";
                if (interactive)
                {
                    dot.AddToClassList("sasq-clickable");
                    EditorSpringFX.HoverScale(dot, 1.4f); // gentle grow on hover (matches the Tutorial's step dots)
                    dot.RegisterCallback<PointerDownEvent>(e => { onGoToPage?.Invoke(page); e.StopPropagation(); });
                    // Pop the dot of the page you just landed on (the click/jump feedback), once per change.
                    if (page == currentPage && currentPage != lastActive) EditorSpringFX.PingScale(dot);
                }
                container.Add(dot);
                prev = idx;
            }
            container.userData = currentPage;
        }

        // The "Sorted by X ▲ · reorder paused [clear]" note shared by the List and Table renderers. Shown
        // (via UpdateSortNote) only while a view-only sort is active, so the user knows reorder is paused
        // and has a one-click way back to natural order. Hidden by default.
        private static VisualElement MakeSortNote(out Label noteLabel, out VisualElement dirIconHost, Action onClear)
        {
            var note = new VisualElement();
            note.AddToClassList("sasq-collection-sortnote");
            note.style.flexDirection = FlexDirection.Row;
            note.style.alignItems = Align.Center;

            // Split into "Sorted by X" + an ICON + the tail, instead of one string with a "▲" in the middle:
            // a text glyph renders at the font's own weight and never matches the icon set everything else
            // in the package draws with. SetSortNoteDirection fills the host.
            noteLabel = new Label { name = "sasq-sortnote-label" };
            noteLabel.AddToClassList("sasq-sortnote-label");

            dirIconHost = new VisualElement { pickingMode = PickingMode.Ignore };
            dirIconHost.AddToClassList("sasq-sortnote-dir");
            dirIconHost.style.flexDirection = FlexDirection.Row;
            dirIconHost.style.alignItems = Align.Center;

            var tail = new Label("· reorder paused");
            tail.AddToClassList("sasq-sortnote-label");

            var clearBtn = new Button(onClear) { text = "clear" };
            clearBtn.AddToClassList("sasq-sortnote-clear");
            note.Add(noteLabel);
            note.Add(dirIconHost);
            note.Add(tail);
            note.Add(clearBtn);
            note.style.display = DisplayStyle.None;
            return note;
        }

        // The columns of a Table-mode collection are the leaf BindingNodes of its ItemLayout, in tree
        // order — the same fields the user designs for List mode, so flipping modes preserves them.
        // Nested CollectionNode/Button/HelpBox nodes are not columns (v1).
        private static List<BindingNode> GetColumnBindings(LayoutNode itemLayout)
        {
            var result = new List<BindingNode>();
            CollectColumnBindings(itemLayout, result);
            return result;
        }

        /// <summary>
        /// Whether this item template holds ANY authored column at all — hidden ones INCLUDED. The one
        /// question that separates "this layout never designed a table" (a code-authored, in-memory layout,
        /// which legitimately wants columns reflected off the element type) from "every designed column is
        /// hidden" (which wants no columns at all). <see cref="GetColumnBindings"/> cannot answer it: the
        /// hidden filter lives inside its walk, so both cases come back as an empty list.
        /// <para>Deliberately does NOT reuse CollectColumnBindings with a flag — that walk reads the
        /// _designMode static, and this question has the same answer on every surface.</para>
        /// </summary>
        private static bool HasAuthoredColumns(LayoutNode itemLayout)
        {
            switch (itemLayout)
            {
                case null:
                    return false;
                case BindingNode _:
                    return true;
                case ContainerNode container when container.Children != null:
                    foreach (var child in container.Children)
                        if (HasAuthoredColumns(child)) return true;
                    return false;
                default:
                    return false;
            }
        }

        private static void CollectColumnBindings(LayoutNode node, List<BindingNode> acc)
        {
            // A hidden node is not a column. Unlike every other child walk this one has no null-return path to
            // ride — the columns are derived from the MODEL before a single cell is built — so the skip has to
            // live here, and it has to take a hidden container's whole subtree with it (the `return` below, not
            // a `break`). Reads the per-build static rather than a threaded parameter because both callers sit
            // deep inside collection building; _designMode is saved/restored around nested builds, so it is
            // accurate for the build in flight. Design mode keeps every column, dimmed, so the author can find
            // and un-hide one.
            if (!_designMode && node != null && node.HiddenInInspector) return;

            switch (node)
            {
                case null:
                    return;
                case BindingNode binding:
                    acc.Add(binding);
                    break;
                case ContainerNode container:
                    if (container.Children != null)
                    {
                        foreach (var child in container.Children)
                        {
                            CollectColumnBindings(child, acc);
                        }
                    }
                    break;
            }
        }

        // Fallback columns for a Table-mode collection whose ItemLayout has no bindings: one column per
        // DIRECT serialized element leaf field, in declaration order. Mirrors the designer's
        // AutoFillTableColumnsIfEmpty (nested-struct sub-fields and sub-collections stay manual-add, v1)
        // and the standalone export's ResolveTableColumns, so a code-authored layout renders the same
        // table in the bridge, the designer, and the exported editor. Element types that render as ONE
        // atomic field (primitives, leaf built-ins, object references, custom-drawer types, UnityEvents,
        // managed references) return empty — the caller falls back to the single "Value" column.
        private static List<BindingNode> DeriveColumnBindingsFromElementType(SerializedObject serializedObject, string resolvedPath)
        {
            var result = new List<BindingNode>();
            var target = serializedObject?.targetObject;
            if (target == null) return result;

            var fieldInfo = BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), resolvedPath, out _);
            if (fieldInfo == null) return result;
            if (Attribute.GetCustomAttribute(fieldInfo, typeof(SerializeReference)) != null) return result;
            if (!SasquatchReflectionUtility.IsCollectionType(fieldInfo.FieldType, out var elementType)) return result;
            // An ATOMIC element (Vector2, float, enum, object reference, custom-drawer type, …) has no
            // sub-field to make a column of — the caller falls back to the single "Value" column, which is
            // the element itself. Shared gate, so "no columns derivable" and "offer an element template"
            // can never disagree.
            if (SasquatchReflectionUtility.IsAtomicElementType(elementType)) return result;

            foreach (var p in SasquatchReflectionUtility.GetSerializableProperties(elementType))
            {
                if (p.isCollection || p.path.Contains('.')) continue;
                result.Add(new BindingNode { PropertyPath = p.path });
            }
            return result;
        }

        // Header text for a Table column: a designer label override wins; otherwise the element field's
        // serialized display name (resolved from a sample element when available); otherwise a nicified
        // last path segment.
        /// <summary>
        /// Sort keys for a List whose row IS the element — either an ItemLayout that contributes no columns,
        /// or one whose single column is the element itself. A `List&lt;int&gt;` has exactly one key, the
        /// element ("Value"). A `List&lt;Vector2&gt;` is atomic to the WIDGET but not to a person sorting it,
        /// so its components become the keys (X, Y); the same expansion gives a `List&lt;MyStruct&gt;` its
        /// fields. Keys the comparator cannot order are never offered — see
        /// <see cref="CollectionViewModel.IsSortableType"/>.
        /// </summary>
        /// <summary>
        /// The <see cref="SerializedPropertyType"/> a sort key would actually hand to
        /// <see cref="CollectionViewModel.CompareProperty"/>, resolved from a sample element — or
        /// <c>null</c> when the sample cannot answer: there is no sample (an empty collection), or the
        /// path is not on this particular element (a polymorphic list where only some subclasses have it).
        /// </summary>
        internal static SerializedPropertyType? ResolveSortKeyType(SerializedProperty sampleItem, BindingNode column)
        {
            if (sampleItem == null || column == null) return null;
            var p = string.IsNullOrEmpty(column.PropertyPath)
                ? sampleItem
                : sampleItem.FindPropertyRelative(column.PropertyPath);
            return p == null ? (SerializedPropertyType?)null : p.propertyType;
        }

        /// <summary>
        /// Drops the sort keys <see cref="CollectionViewModel.CompareProperty"/> cannot order.
        ///
        /// <para>A key that compares 0 for every pair is not "no sort" — it is strictly WORSE than not
        /// offering the key at all, because selecting it leaves the rows exactly where they were AND sets
        /// the sort index, which pauses reorder and takes every drag grip off the list until the user finds
        /// the sort note's "clear" link. <see cref="CollectionViewModel.IsSortableType"/> exists to prevent
        /// precisely that, but for a long time it gated only ONE of the three places a key is offered
        /// (<see cref="BuildElementSortKeys"/>); a List with a designed ItemLayout went straight to
        /// GetColumnBindings, and a Table marked every header sortable.</para>
        ///
        /// <para>A key whose type will not resolve is KEPT, deliberately. Refusing to offer a key because
        /// the list happens to be empty right now is the other half of the same bug — the caller re-derives
        /// once a sample exists.</para>
        /// </summary>
        internal static List<BindingNode> FilterSortableColumns(List<BindingNode> columns, SerializedProperty sampleItem)
        {
            if (columns == null || sampleItem == null) return columns;
            var kept = new List<BindingNode>();
            foreach (var c in columns)
            {
                var t = ResolveSortKeyType(sampleItem, c);
                if (t == null || CollectionViewModel.IsSortableType(t.Value)) kept.Add(c);
            }
            return kept;
        }

        internal static List<BindingNode> BuildElementSortKeys(SerializedProperty sampleItem, BindingNode elementColumn = null)
        {
            // The element AS A WHOLE. When an Element template supplied the column, its own label is the name
            // the user gave that row, so keep it rather than overwriting it with "Value".
            var whole = new BindingNode
            {
                PropertyPath = "",
                LabelOverride = elementColumn?.EffectiveLabelOverride ?? "Value",
            };
            // No sample to inspect (an empty list): the element key is all we can honestly offer.
            if (sampleItem == null) return new List<BindingNode> { whole };

            var keys = new List<BindingNode>();
            if (sampleItem.hasVisibleChildren)
            {
                var it = sampleItem.Copy();
                var end = sampleItem.GetEndProperty();
                bool enterChildren = true;
                while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
                {
                    enterChildren = false; // direct children only
                    if (!CollectionViewModel.IsSortableType(it.propertyType)) continue;
                    keys.Add(new BindingNode { PropertyPath = it.name, LabelOverride = it.displayName });
                }
            }

            // NAMED PARTS REPLACE THE WHOLE — they don't sit beside it. On a List<Vector2>, "X" and "Y"
            // answer a question a person actually asked; "Value" there means lexicographic x-then-y, which
            // nobody would predict from the word and nobody asked for. Offering all three invites exactly
            // one question — "what would Value even be?" — and there is no good answer. So the element key
            // is offered ONLY when the element has no named parts to offer instead, i.e. only where "Value"
            // genuinely IS the whole story (a List<int>, <string>, <enum>, object references).
            //
            // The comparator still orders a whole vector, and must: a TABLE's single value column binds the
            // element with an empty path and sorts through exactly that route.
            if (keys.Count == 0) keys.Add(whole);
            return keys;
        }

        internal static string ResolveColumnLabel(BindingNode column, SerializedProperty sampleItem)
        {
            // Affix column-header mode folds the unit into the header ("Weight (kg)") and suppresses it in
            // every cell — 40 rows of "kg" is noise. Decorating HERE covers the header row and the sort
            // dropdown in one place, so the two can never disagree about what a column is called.
            return FieldAffixes.DecorateColumnHeader(column, ResolveColumnLabelBase(column, sampleItem));
        }

        private static string ResolveColumnLabelBase(BindingNode column, SerializedProperty sampleItem)
        {
            if (column == null) return "Value";
            if (column.EffectiveLabelOverride != null) return column.EffectiveLabelOverride;

            // An EMPTY path means the column IS the element — and a SerializedProperty for an array element
            // display-names itself "Element 0", the index of whichever sample we happened to grab. Reported
            // as "why am I sorting by 'Element 0'? What does that even mean?". It is the same rule the row
            // labels and the in-line editor rows already follow: an element binding NEVER surfaces Unity's
            // "Element N". Only a real sub-field may take its name from the sample; the element itself falls
            // through to "Value" below.
            if (sampleItem != null && !string.IsNullOrEmpty(column.PropertyPath))
            {
                var cellProp = sampleItem.FindPropertyRelative(column.PropertyPath);
                if (cellProp != null) return cellProp.displayName;
            }

            string path = column.PropertyPath ?? "";
            int dot = path.LastIndexOf('.');
            if (dot >= 0) path = path.Substring(dot + 1);
            return string.IsNullOrEmpty(path) ? "Value" : ObjectNames.NicifyVariableName(path);
        }

        private const float TableIndexWidth = 28f;
        // Per-column floor. Kept LOW on purpose: divider drags cascade across every column (not just the
        // adjacent pair), so the floor doesn't have to reserve drag slack — it only stops a column from
        // collapsing to an unclickable sliver. A bool/toggle column auto-fits down to about this width.
        private const float TableColumnMinWidth = 32f;

        /// <summary>One data cell of a Table-mode collection — the unit the column sizer drives. Public so
        /// tests (and dev probes) can find a table's real columns without guessing at its structure.</summary>
        internal const string TableCellClass = "sasq-table-cell";

        /// <summary>One header cell of a Table-mode collection; there is exactly one per column.</summary>
        internal const string TableHeaderCellClass = "sasq-table-header-cell";

        /// <summary>
        /// The designer's representative table row, when it stands in for the collection's item-layout
        /// CONTAINER — i.e. when its cells are exactly that container's direct children, laid out side by
        /// side. Carries no USS: it exists so the drop map can treat the row as that container's element and
        /// offer horizontal gaps between the columns (which is what makes columns drag-reorderable).
        /// </summary>
        internal const string TableColumnHostClass = "sasq-table-columns-host";

        // True when every rendered column is a DIRECT child of the item-layout container — the shape the
        // designer authors (a Panel container of BindingNodes). A column nested inside a sub-container is
        // not a sibling of the others, so a slot index computed against this parent would move it somewhere
        // it never previewed; such a table simply keeps the old non-reorderable row. Non-binding children
        // (a stray HelpBox, an EmptyCellNode) are fine — they render no column, and the slot indices come
        // from the model, not from the column ordinal.
        private static bool ColumnsAreDirectChildrenOf(List<BindingNode> columns, LayoutNode itemLayout)
        {
            if (!(itemLayout is ContainerNode container) || container.Children == null) return false;
            if (columns == null || columns.Count == 0) return false;
            foreach (var col in columns)
                if (col == null || !container.Children.Contains(col)) return false;
            return true;
        }
        // Auto-fit (divider double-click) scans at most this many elements — a huge array would otherwise
        // stall the click while every row's text is measured. Matches typical visible-data expectations.
        private const int TableAutoFitSampleRows = 500;
        // ── Row-action buttons (✕ / ▲ / ▼) ────────────────────────────────────────────────────────────────
        // ONE button's box, and the slot it occupies once its 1px side margins are counted. The actions lane
        // is DERIVED from these rather than hand-tuned, because the two used to drift: the lane was a flat
        // 34px holding a single 19px-wide button right-aligned, so every row of every table carried ~13px of
        // dead pixels between the last column and the ✕ — on top of the 7px trailing divider lane. Table
        // space is the scarcest thing a table has; sizing the lane to its contents hands that back to the
        // columns (they are flex-grow, so the slack is redistributed with no other change).
        //
        // WIDTH IS THE GRIP'S WIDTH, deliberately: `.sasq-reorder-grip` is 14w x 7h, and the grip and the ✕ are
        // the row's two pieces of chrome, one at each end. Matching widths is what makes them read as a pair
        // rather than as two unrelated controls. SQUARE rather than the grip's literal 7px height, because the
        // ✕ glyph is 9px (a 7px box would clip it) and because the original complaint was that these were a
        // sliver to hit — 14x14 keeps a real target while still looking like the grip's sibling.
        // Both are also the unit the actions lane is sized from, so neither may be tuned alone.
        private const float TableRowButtonWidth = 14f;
        private const float TableRowButtonHeight = 14f;
        private const float TableRowButtonSlot = TableRowButtonWidth + 2f; // + marginLeft/marginRight of 1

        private const float TableActionsWidth = TableRowButtonSlot * 3f;   // ▲ ▼ ✕
        private const float TableActionsWidthCompact = TableRowButtonSlot; // ✕ only (drag-grip or no reorder)
        private const float TableDividerWidth = 7f; // inter-column gap; doubles as the drag-to-resize handle

        // Breathing room between the last action button and the row's trailing edge. Sizing the lane to its
        // buttons (see above) left the ✕ hard against that edge while the grip at the other end sat clearly
        // inset — the row read as lopsided. This is the trailing counterpart to the grip's leading margin, and
        // it is deliberately much smaller than the ~20px of container padding on the left: the point of the
        // lane change was to hand space back to the COLUMNS, so this buys the ✕ an edge to breathe against
        // without giving that back. Applied as padding INSIDE the reserved width so `ActionsWidthFor` stays
        // the single number the header spacer and the row cluster both read.
        private const float TableActionsTrailingInset = 6f;

        /// <summary>USS class on every table row-action button (✕ / ▲ / ▼) — the hook the hover reveal
        /// hangs off. Internal so the verification suite can find them without guessing at the row shape.</summary>
        internal const string TableRowActionClass = "sasq-table-row-action";

        // The actions-column width for a table: arrows need three slots; a delete-only column (drag-grip or
        // no reorder) gets one, so the data columns keep the rest. Includes the trailing inset, because the
        // header reserves the lane from this SAME call — split them and every column header slides off.
        private static float ActionsWidthFor(CollectionReorderStyle reorderStyle)
        {
            bool showArrows = reorderStyle == CollectionReorderStyle.Arrows || reorderStyle == CollectionReorderStyle.Both;
            return (showArrows ? TableActionsWidth : TableActionsWidthCompact) + TableActionsTrailingInset;
        }
        // Reserved in the header (and in a row whose grip is suppressed by an active sort/filter) to align
        // with a row's real reorder grip. It MUST equal the lane `.sasq-reorder-grip` actually occupies:
        // width 14 + margin-left 2 + margin-right 4. It sat at 18 and put every table column 2px right of
        // its own header — invisible until TestCollectionRowSeparatorTableCardKeepsColumnsInRegister started
        // measuring header-to-cell x. Change the USS rule, change this.
        private const float TableGripWidth = 20f;

        // Table render mode for a CollectionNode: a header row of column names plus one row per element,
        // each row laying the element's bound fields into columns via the existing BindingFactory (so
        // custom property drawers and widget kinds still apply). Honors the hard rules: cells bind to the
        // same SerializedObject + element paths the List builder uses (the throwaway mock in live preview /
        // the real object in the inspector), and value edits never rebuild the table — only explicit
        // add/remove/reorder do (via onRebuildRequired).
        private static VisualElement BuildCollectionTableElement(
            CollectionNode node,
            string resolvedPath,
            List<BindingNode> columns,
            SerializedObject serializedObject,
            Action onRebuildRequired,
            SasquatchThemeAsset theme,
            bool designMode,
            LayoutNode selectedNode,
            CanvasDragController dragController,
            InspectorLayoutAsset layoutAsset,
            bool designInteract = false,
            bool isValueColumn = false)
        {
            var prop = serializedObject.FindProperty(resolvedPath);
            if (!IsRenderableCollectionProperty(prop))
            {
                // A field that still EXISTS in code but is now an unserializable BCL collection is not a
                // dead binding -- it gets the conversion card, exactly as the BindingNode branch does.
                if (TryDeadCollectionConversionCard(serializedObject, resolvedPath, out var conversionCard))
                    return conversionCard;

                // Dead collection: hidden live (Unity-like), red-tinted in the designer.
                if (!designMode) return null;
                var fallbackContainer = CreateCollectionFrame();
                fallbackContainer.AddToClassList("sasq-missing-binding");
                fallbackContainer.Add(new Label(DeadCollectionMessage(prop, resolvedPath)));
                return fallbackContainer;
            }

            // Drop table columns whose element field no longer exists (deleted/renamed while sync hasn't
            // run) — IsLivePath rather than FindProperty so it works on an empty list (and resolves
            // unindexed element paths, which segment reflection alone can't). Live only: the designer
            // keeps the column visible (its cells/error reveal the stale path for cleanup).
            if (!designMode && !isValueColumn && serializedObject.targetObject != null)
            {
                var targetType = serializedObject.targetObject.GetType();
                columns = columns.FindAll(c => string.IsNullOrEmpty(c.PropertyPath) ||
                    UnboundFieldSync.IsLivePath(targetType, resolvedPath + "." + c.PropertyPath));
                if (columns.Count == 0) return null;
            }

            VisualElement container;
            VisualElement contentTarget;
            SasquatchIconElement chevron = null;

            // Same localized-rebuild contract as the List renderer: LIVE +/-/insert/duplicate/delete rebuild
            // ONLY this table's rows + "(N)" count + search box in place, never the whole inspector. Design
            // mode keeps onRebuildRequired. liveRebuild is wired once the row container + helpers exist.
            Label titleLabel = null;
            // Hold-to-clear — hoisted for the List twin's reason: the in-place rebuild never rebuilds the
            // header, so only it can re-sync whether there is anything left to clear.
            Button clearBtn = null;
            Action liveRebuild = null;
            Action structuralRebuild = designMode ? onRebuildRequired : new Action(() => liveRebuild?.Invoke());
            // Making an APPEND visible — the List twin's revealAppended/ensureExpanded pair. Read its note
            // there; the two must stay in lockstep.
            Action ensureExpanded = null;
            Action revealAppended = null;
            // The row menu's Insert twin — filter/sort clear, no page move. See the List twin.
            Action revealInserted = null;
            // Element count these rows were last drawn from — see AttachExternalArraySizeSync (List twin).
            int renderedArraySize = -1;
            // Set only while a VIEW-ONLY trigger (search / pager / sort) drives the rebuild — see
            // ViewOnlyRebuild. Everything else leaves it false and re-measures the column fit. Declared up here
            // rather than beside the fit because the sort/pager lambdas below capture it, and a lambda cannot
            // capture a local declared later in the method (CS0165).
            bool viewOnlyRebuild = false;

            // --- Transient (never serialized) page + sort state. Table sorts via clickable column headers
            // (sortColumnIndex indexes `columns`); -1 = natural order. ---
            int currentPage = 0;
            int sortColumnIndex = -1;
            bool sortDescending = false;
            int lastVisibleCount = 0, lastPageStart = 0, lastPageCount = 0;
            string SortPath() => (sortColumnIndex >= 0 && sortColumnIndex < columns.Count)
                ? columns[sortColumnIndex].PropertyPath : null;

            if (node.Expandable)
            {
                container = new VisualElement();
                container.AddToClassList("sasq-group");
                container.style.marginBottom = 6;

                // Header bar (title row)
                var titleRow = new VisualElement();
                titleRow.AddToClassList("sasq-group-header");
                titleRow.style.flexDirection = FlexDirection.Row;
                titleRow.style.alignItems = Align.Center;

                // Chevron
                chevron = SasquatchIcons.Create(
                    SasquatchIcons.Ids.ChevronRight, // single glyph; ApplyExpanded spins it 0°↔90°
                    10, WidgetPalette.ChevronDim);
                chevron.AddToClassList("sasq-group-chevron");
                titleRow.Add(chevron);

                // Title label
                titleLabel = new Label(CollectionHeaderText(node, prop));
                titleLabel.AddToClassList("sasq-collection-header");
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.flexGrow = 1;
                titleLabel.style.color = WidgetPalette.LabelPrimary;
                titleRow.Add(titleLabel);

                // Hold-to-clear — site 3 of 4. The Table pair lives a thousand lines from the List pair and
                // carries no cross-reference of its own, which is exactly how a header button has shipped
                // List-only before; see collection-header-has-four-branches. (The eyedropper's absence here
                // is deliberate and unrelated — a Table is only offered for multi-field element types and an
                // eyedropper only for object-reference ones, so the two sets never overlap. Clearing a list
                // has no such restriction.)
                clearBtn = CollectionClearAll.CreateHeaderButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract);
                if (clearBtn != null) titleRow.Add(clearBtn);

                // Add button. Reveal site 3 of 4 — the Table expandable twin of the List pair a thousand
                // lines up. Keep in lockstep; see collection-header-has-four-branches.
                var addBtn = CreateCollectionAddButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract,
                    () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); });
                titleRow.Add(addBtn);

                container.Add(titleRow);

                // Content area (holds search box + headers + items)
                contentTarget = new VisualElement();
                contentTarget.AddToClassList("sasq-group-content");
                contentTarget.style.flexDirection = FlexDirection.Column;
                container.Add(contentTarget);

                // Toggle logic. Open-state remembered per type, keyed by the list's property path; persisted on
                // a real user toggle (animate==true; the initial seed uses animate==false).
                string collKey = designMode ? null : FoldoutStateStore.Key(serializedObject.targetObject, prop.propertyPath, prop.displayName);
                bool expanded = FoldoutStateStore.Get(collKey, node.DefaultToOpen);
                void ApplyExpanded(bool animate)
                {
                    EditorSpringFX.SpringCaret(chevron, expanded, animate);
                    titleRow.style.borderBottomLeftRadius = expanded ? 0 : 4;
                    titleRow.style.borderBottomRightRadius = expanded ? 0 : 4;
                    if (animate) { EditorSpringFX.Fold(contentTarget, expanded); FoldoutStateStore.Set(collKey, expanded); }
                    else contentTarget.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                }
                ApplyExpanded(false);

                titleRow.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    if (designMode && !designInteract) return;
                    expanded = !expanded;
                    ApplyExpanded(!designMode);
                    if (!designMode || designInteract) evt.StopPropagation();
                });

                // Search expander (site 5 of 5 — keep in lockstep): transient open/close without
                // persistence; Restore replays the user-owned `expanded` state. Locals are pinned because
                // chevron/contentTarget are method-level variables the closure must not track past here.
                {
                    var searchChevron = chevron;
                    var searchContent = contentTarget;
                    GroupExpanderRegistry.Register(container, new GroupExpander(
                        open =>
                        {
                            EditorSpringFX.SpringCaret(searchChevron, open, false);
                            titleRow.style.borderBottomLeftRadius = open ? 0 : 4;
                            titleRow.style.borderBottomRightRadius = open ? 0 : 4;
                            searchContent.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                        },
                        () => ApplyExpanded(false)));
                }

                // Hoisted for the drop — the List twin's ensureExpanded. Keep in lockstep.
                ensureExpanded = () =>
                {
                    if (expanded) return;
                    expanded = true;
                    ApplyExpanded(!designMode);
                };

                addBtn.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) evt.StopPropagation(); });
                addBtn.RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0) evt.StopPropagation(); });
                addBtn.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    ensureExpanded();
                });
            }
            else
            {
                container = CreateCollectionFrame();

                var titleRow = new VisualElement();
                titleRow.style.flexDirection = FlexDirection.Row;
                titleRow.style.alignItems = Align.Center;
                titleRow.style.marginBottom = 6;

                titleLabel = new Label(CollectionHeaderText(node, prop));
                titleLabel.AddToClassList("sasq-collection-header");
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.flexGrow = 1;
                titleLabel.style.color = WidgetPalette.LabelPrimary;
                titleRow.Add(titleLabel);
                // Hold-to-clear — site 4 of 4, the Table plain-header twin. Keep in lockstep.
                clearBtn = CollectionClearAll.CreateHeaderButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract);
                if (clearBtn != null) titleRow.Add(clearBtn);
                // Reveal site 4 of 4 — the Table plain-header twin. Keep in lockstep.
                titleRow.Add(CreateCollectionAddButton(prop, serializedObject, structuralRebuild,
                    designMode && !designInteract,
                    () => { ensureExpanded?.Invoke(); revealAppended?.Invoke(); }));
                container.Add(titleRow);

                contentTarget = container;
            }

            // Object drag-and-drop onto the whole table — the List renderer's twin; keep the two in lockstep,
            // including the reveal-then-rebuild callback (see revealAppended's note over there).
            if (!designMode)
            {
                CollectionObjectDrop.Attach(container, prop, serializedObject, () =>
                {
                    ensureExpanded?.Invoke();
                    revealAppended?.Invoke();
                    structuralRebuild();
                });
            }

            // Toolbar zone (search + sort-note + pager) sits above the header row. Separate from
            // itemsContainer so an in-place rebuild's Clear() never destroys the search box or pager.
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Column;

            // Search box is created (cosmetically) in design mode too so the designer preview shows it; its
            // change callback is only wired live (below), and SyncTableSearchField only runs on live rebuilds.
            TextField searchField = null;
            if (node.Searchable && prop.arraySize >= 2)
            {
                searchField = MakeCollectionSearchField(designMode && !designInteract);
                toolbar.Add(searchField);
            }

            Label sortNoteLabel = null;
            var sortNote = MakeSortNote(out sortNoteLabel, out var sortNoteDirIcon, () =>
            {
                sortColumnIndex = -1;
                currentPage = 0;
                ViewOnlyRebuild(); // sorting is view-only: BuildVisibleIndices remaps, the array is untouched
            });
            toolbar.Add(sortNote);

            // Pagination toolbar — shown in design too as a faithful preview (buttons disabled there).
            Label pageLabel = null, countLabel = null;
            Button prevBtn = null, nextBtn = null;
            VisualElement pagerDots = null;
            var pager = MakeCollectionPager(out pageLabel, out countLabel, out prevBtn, out nextBtn, out pagerDots,
                onPrev: () => { currentPage = Mathf.Max(0, currentPage - 1); ViewOnlyRebuild(); },
                onNext: () => { currentPage += 1; ViewOnlyRebuild(); });
            toolbar.Add(pager);

            contentTarget.Add(toolbar);

            SerializedProperty sampleItem = prop.arraySize > 0 ? prop.GetArrayElementAtIndex(0) : null;
            // Column labels precomputed once (the sort note must not touch a sample element a rebuild may
            // have invalidated). headerSortGlyphs hold the ▲/▼ marks, updated in place on each sort change.
            var columnLabels = new List<string>();
            for (int c = 0; c < columns.Count; c++) columnLabels.Add(ResolveColumnLabel(columns[c], sampleItem));
            var headerSortGlyphs = new Label[columns.Count];
            // Build the clickable header CELL in both modes (so design previews the sortable header's look +
            // link cursor); the click handler that actually sorts is wired live only.
            bool headerSortable = node.EnableSorting;
            // …but PER COLUMN, because CompareProperty cannot order every type. A header that offers a sort
            // it cannot perform is worse than a plain one: the click leaves the rows exactly where they
            // were AND sets sortColumnIndex, which pauses reorder and strips every drag grip off the table
            // until the user finds the sort note's "clear". The List twin filters its dropdown the same way;
            // CollectionViewModel.IsSortableType is the single source both ask.
            //
            // Resolved from a sample element, so an EMPTY table cannot answer — it keeps every header
            // sortable and re-asks on the first in-place rebuild that has a row (SyncColumnSortability).
            // The transition is therefore only ever unknown → known-unsortable, which is why the glyph can
            // simply be hidden rather than built late.
            var columnSortable = new bool[columns.Count];
            void ResolveColumnSortability()
            {
                var s = prop.arraySize > 0 ? prop.GetArrayElementAtIndex(0) : null;
                for (int c = 0; c < columns.Count; c++)
                {
                    var t = ResolveSortKeyType(s, columns[c]);
                    columnSortable[c] = t == null || CollectionViewModel.IsSortableType(t.Value);
                }
            }
            ResolveColumnSortability();
            // Whether a column can be dragged to a new position at all — see TableColumnGrip's gate. A
            // nested item template or one whose columns were DERIVED from the element type has no single
            // model list to move a column within, so those tables simply get no grips.
            bool columnsAreArrangeable = TableColumnGrip.ColumnsAreArrangeable(node, columns);
            // Whether a grip will ACTUALLY be built — the exact condition the Attach below uses. The
            // auto-fit has to reserve the grip's lane on precisely the columns that will get one and no
            // others: reserving it on a design canvas (where no grip is ever offered) widened every fitted
            // column by a lane the user could not see, which moved the very geometry the designer measures.
            bool columnGripsOffered = _liveInspector && !designMode && columnsAreArrangeable;

            // A reorder grip column appears (live + design) when the style includes drag-and-drop; reserve
            // matching width in the header so column headers line up with the cells below.
            bool gripShown = node.ReorderStyle == CollectionReorderStyle.DragAndDrop || node.ReorderStyle == CollectionReorderStyle.Both;

            // Per-column element lists (header cell + every row cell) so a divider drag can re-flow the whole
            // column at once; captured by ApplyColumnWeight + the resizer closures below.
            var columnCells = new List<VisualElement>[columns.Count];
            for (int c = 0; c < columns.Count; c++) columnCells[c] = new List<VisualElement>();

            void ApplyColumnWeight(int c)
            {
                float w = ColumnWeightOf(columns[c]);
                foreach (var el in columnCells[c])
                {
                    // Leaving content-fit mode: the cascade/auto-fit math shares ONE pool of width between
                    // grow factors, so a leftover flex-basis from the initial fit would silently offset every
                    // column the drag computes.
                    el.style.flexBasis = 0;
                    el.style.flexShrink = 1f;
                    el.style.flexGrow = w;
                }
            }

            // Header row: [grip spacer?] index spacer + bold column headers (drag dividers between them) +
            // actions spacer (live only, so the headers sit over the cells rather than the row buttons).
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;
            headerRow.style.paddingBottom = 2;
            headerRow.style.borderBottomWidth = 1;
            headerRow.style.borderBottomColor = WidgetPalette.BorderSoft;
            // Card rows inset their content by the rail + tray padding; the header takes the SAME inset or
            // every grow column drifts out from under its name. See ApplyTableCardHeaderInset for why.
            ApplyTableCardHeaderInset(headerRow, node.RowSeparator);

            if (gripShown) headerRow.Add(MakeTableSpacer(TableGripWidth));
            headerRow.Add(MakeTableSpacer(TableIndexWidth));

            // Auto-fit measurement (divider double-click): each column measures through its own header label
            // (attached to the panel, so fonts resolve) against the header text + the sampled cell values.
            var headerTextLabels = new Label[columns.Count];
            Func<int, float> measureColumn = c =>
                MeasureTableColumnFitWidth(prop, columns[c], headerTextLabels[c], columnLabels[c],
                    headerSortable && columnSortable[c],
                    columnGripsOffered);

            // ---- initial column sizing ------------------------------------------------------------------
            // Until a divider is dragged, a table has no authored widths — and every cell used to be
            // flex-basis:0 with an equal grow, so a checkbox column and a name column got the same pixels and
            // both were wrong (the checkbox floated in dead space while the name ellipsized to "Ashen Sv…").
            // Instead: each column's measured need — header text + sampled values + a floor for the widgets
            // whose width has nothing to do with their value — becomes its flex BASIS, and only the text-ish
            // columns grow into whatever slack is left. When the table is too narrow for the total, flex-shrink
            // takes it out of the columns in proportion to what they asked for, so the wide column pays most.
            // A drag authors weights, which returns the table to weight mode (see ApplyColumnWeight).
            bool ColumnWidthsAreAuthored()
            {
                for (int c = 0; c < columns.Count; c++)
                    if (columns[c].ColumnWeight > 0f) return true;
                return false;
            }

            // The last measurement, kept so a VIEW-ONLY rebuild can re-apply it to fresh cells instead of
            // re-deriving it. See ApplyContentFit and ViewOnlyRebuild below.
            float[] fitBasis = null;
            bool[] fitGreedy = null;
            bool fitAnyGreedy = false;
            bool fitMeasurementStale = true;

            void ApplyContentFit()
            {
                if (ColumnWidthsAreAuthored()) return;

                // MEASURE only when the measurement could actually have moved.
                //
                // The fit samples `prop` — up to TableAutoFitSampleRows elements of the WHOLE array — and never
                // reads the search term, the sort order or the current page. So a rebuild driven by any of
                // those recomputes a provably identical answer, and skipping it is an input-equality skip, not
                // a debounce: the widths on screen are the same widths either way, applied at the same moment.
                // That distinction matters because the two obvious speedups here are both forbidden — debouncing
                // the keystroke makes results visibly lag, and sampling only the rendered page would change the
                // derived widths. Neither is needed.
                //
                // Measured on a 500-row, 6-column table: ~92ms per keystroke, against a 16.7ms frame.
                //
                // Everything that is NOT view-only leaves the measurement stale (see RebuildTableRowsInPlace),
                // so a structural change, an external array-size change, or the post-attach re-fit all still
                // measure exactly as before.
                if (fitMeasurementStale || fitBasis == null || fitBasis.Length != columns.Count)
                {
                    fitBasis = new float[columns.Count];
                    fitGreedy = new bool[columns.Count];
                    fitAnyGreedy = false;
                    for (int c = 0; c < columns.Count; c++)
                    {
                        fitBasis[c] = Mathf.Max(measureColumn(c), TableColumnMinWidth);
                        fitGreedy[c] = IsGreedyTableColumn(prop, columns[c]);
                        fitAnyGreedy |= fitGreedy[c];
                    }
                    fitMeasurementStale = false;
                }

                var basis = fitBasis;
                var greedy = fitGreedy;
                bool anyGreedy = fitAnyGreedy;

                // The APPLY always runs: a rebuild hands us brand-new cell elements with no sizing on them.
                for (int c = 0; c < columns.Count; c++)
                {
                    // No text-ish column to absorb the slack (an all-numeric table) — the table still fills its
                    // container rather than hanging short of the right edge, but the slack is shared in
                    // PROPORTION to what each column asked for. Splitting it equally instead would hand the
                    // same 100px to a 2-digit int as to a long enum, which is the equal-share look this whole
                    // pass exists to get rid of.
                    float grow = anyGreedy ? (greedy[c] ? 1f : 0f) : basis[c];
                    foreach (var el in columnCells[c])
                    {
                        el.style.flexBasis = basis[c];
                        el.style.flexShrink = 1f;
                        el.style.flexGrow = grow;
                    }
                }
            }

            for (int c = 0; c < columns.Count; c++)
            {
                int colIndex = c;
                var headerLabel = new Label(columnLabels[c]);
                headerTextLabels[c] = headerLabel;
                headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                headerLabel.style.color = WidgetPalette.LabelSecondary;
                headerLabel.style.whiteSpace = WhiteSpace.NoWrap;
                headerLabel.style.overflow = Overflow.Hidden;
                headerLabel.style.textOverflow = TextOverflow.Ellipsis;

                // ALWAYS a wrapper: [grip | label | ▲/▼ glyph]. The header cell used to change SHAPE with
                // EnableSorting — sortable was this wrapper, unsortable WAS the bare label — which is why so
                // much of the surrounding USS carries both `.x .unity-label` and `.x.unity-label` selector
                // forms. One shape now, because the reorder grip needs an absolute slot to live in that the
                // label's own text-shifting padding cannot drag along with it. `sasq-table-col-header` stays
                // the SORTABLE marker rather than becoming the cell marker, so it keeps meaning one thing.
                var headerCell = new VisualElement();
                headerCell.style.flexDirection = FlexDirection.Row;
                headerCell.style.alignItems = Align.Center;
                headerLabel.style.flexGrow = 1;
                headerLabel.style.flexShrink = 1;
                headerCell.Add(headerLabel);

                if (headerSortable && columnSortable[c])
                {
                    headerCell.AddToClassList("sasq-table-col-header");

                    // SORTING IS THE GLYPH, not the whole cell. It used to be the cell — with the glyph
                    // picking-disabled — which left no room for any other gesture on a header. The grip
                    // attached below wants the same surface, and "click the header" versus "drag the header"
                    // is exactly the ambiguity that makes a reorder feel unreliable. So the click target is
                    // now the arrows the user is already aiming at, widened to a comfortable size.
                    var glyph = new Label();
                    glyph.AddToClassList("sasq-col-sort-glyph");
                    glyph.style.flexShrink = 0;
                    glyph.style.width = 16;
                    glyph.style.unityTextAlign = TextAnchor.MiddleCenter;
                    glyph.tooltip = "Sort by this column";
                    headerSortGlyphs[c] = glyph;
                    headerCell.Add(glyph);

                    // Live only: clicking sorts. In design the header is a non-interactive preview (it still
                    // shows the header look + link cursor so the designer sees the column is sortable).
                    if (!designMode)
                    {
                        glyph.RegisterCallback<PointerDownEvent>(evt =>
                        {
                            if (evt.button != 0) return;
                            ToggleSort(colIndex);
                            evt.StopPropagation();
                        });
                    }
                }

                headerCell.style.flexGrow = ColumnWeightOf(columns[c]);
                headerCell.style.flexBasis = 0;
                headerCell.style.minWidth = TableColumnMinWidth;
                headerCell.style.overflow = Overflow.Hidden; // truncate like the cells below it
                headerCell.AddToClassList(TableHeaderCellClass);
                headerRow.Add(headerCell);
                columnCells[c].Add(headerCell);

                // Hover a header and a grip springs in on its left; drag it to move the column. onRebuildRequired,
                // NOT structuralRebuild — a column move changes the header row and every cell, which
                // repopulating the rows alone cannot express.
                //
                // GATED ON _liveInspector, NOT on !designMode. They are not the same question: the Layout
                // Designer's Preview button sets designMode = false on purpose, and so do the preset editor
                // and the tutorial preview — only 3 of the 13 Build callers pass liveInspector (the two real
                // Inspectors, plus the Showcase Sandbox, which opts in deliberately).
                // With the weaker gate the grip mounted on the designer's own canvas preview, where its
                // Commit writes through to the WORKING CLONE behind the designer's back and then hands the
                // same edit back via AdoptLiveLayoutEdit — which, depending on the dirty flag, either threw
                // the user's column move away or overwrote unsaved designer work, with the canvas never
                // redrawing either way. Same trap, same fix, as Live Debug's own gate.
                if (columnGripsOffered)
                {
                    TableColumnGrip.Attach(headerCell, headerLabel, node, colIndex, columnCells, onRebuildRequired);
                }

                if (c < columns.Count - 1)
                {
                    var divider = MakeColumnDivider(tooltip: "Drag to resize · double-click to fit the column");
                    headerRow.Add(divider);
                    // Capture the persist callback's VALUE now (build time) — _onLayoutEdited is a per-build
                    // static that gets restored once Build returns, but the resizer fires later (on drag).
                    AttachColumnResizer(divider, columns, columnCells, c, ApplyColumnWeight, _onLayoutEdited, measureColumn);
                }
                else if (columns.Count > 1)
                {
                    // Trailing lane: same width as a divider (keeps the last column's right gap consistent),
                    // no drag (nothing on its right to trade with), but double-click still fits the column.
                    var trailing = MakeColumnDivider(dragCursor: false, tooltip: "Double-click to fit the column");
                    headerRow.Add(trailing);
                    AttachColumnAutoFitOnly(trailing, columns, columnCells, c, ApplyColumnWeight, _onLayoutEdited, measureColumn);
                }
                else
                {
                    headerRow.Add(MakeTableSpacer(TableDividerWidth)); // single column: plain gap, nothing to fit
                }
            }

            // Reserve the actions column whenever the rows have one: always live, and for a value column in
            // design mode too (it now renders live-style rows with the +/− and reorder buttons).
            if (!designMode || isValueColumn) headerRow.Add(MakeTableSpacer(ActionsWidthFor(node.ReorderStyle)));
            contentTarget.Add(headerRow);

            var itemsContainer = new VisualElement();
            itemsContainer.style.flexDirection = FlexDirection.Column;

            // The Table twin of the List's RefreshSortKeysIfSampleArrived. A table built over an EMPTY
            // collection has no element to resolve a column's type from, so every header keeps its sort
            // glyph on trust; the first row is what finally answers. The transition is therefore only ever
            // unknown → known-unsortable, which is why hiding the glyph and dropping the marker class is the
            // whole operation — nothing has to be built late. Runs once, then never again.
            bool columnSortabilityResolved = prop.arraySize > 0;
            void SyncColumnSortability()
            {
                if (columnSortabilityResolved || prop.arraySize == 0) return;
                columnSortabilityResolved = true;
                ResolveColumnSortability();
                for (int c = 0; c < columns.Count; c++)
                {
                    if (columnSortable[c]) continue;
                    var glyph = headerSortGlyphs[c];
                    if (glyph == null) continue;
                    glyph.style.display = DisplayStyle.None;
                    glyph.parent?.RemoveFromClassList("sasq-table-col-header");
                    // Null, not merely hidden: UpdateHeaderSortGlyphs skips nulls, so this also stops the
                    // retired column re-texting itself to "↕" on the next sort change.
                    headerSortGlyphs[c] = null;
                    // A column that turns out to be unorderable must not stay SELECTED — that would hold
                    // reorder paused for a sort nothing can perform.
                    if (sortColumnIndex == c) { sortColumnIndex = -1; sortDescending = false; }
                }
            }

            // --- Live sort/pager helpers. ToggleSort (called by the clickable headers above) cycles a
            // column asc -> desc -> clear; ComputeTablePageIndices runs filter -> view-only sort -> page. ---
            void UpdateHeaderSortGlyphs()
            {
                for (int c = 0; c < headerSortGlyphs.Length; c++)
                {
                    var g = headerSortGlyphs[c];
                    if (g == null) continue;
                    if (c == sortColumnIndex)
                    {
                        g.text = sortDescending ? "▼" : "▲"; // active column: bright direction arrow
                        g.style.opacity = 1f;
                    }
                    else
                    {
                        g.text = "↕"; // faint "this column is sortable" hint on EVERY header (discoverability)
                        g.style.opacity = 0.35f;
                    }
                }
            }
            void ToggleSort(int c)
            {
                if (sortColumnIndex != c) { sortColumnIndex = c; sortDescending = false; }
                else if (!sortDescending) sortDescending = true;
                else sortColumnIndex = -1; // 3rd click clears -> natural order (reorder re-enabled)
                currentPage = 0;
                ViewOnlyRebuild(); // sorting never mutates the array, so the column fit still holds
            }
            void UpdateTableSortUi()
            {
                bool sorted = sortColumnIndex >= 0 && sortColumnIndex < columnLabels.Count;
                sortNote.style.display = sorted ? DisplayStyle.Flex : DisplayStyle.None;
                if (sorted)
                    { sortNoteLabel.text = $"Sorted by {columnLabels[sortColumnIndex]}"; SetSortNoteDirection(sortNoteDirIcon, sortDescending); }
                UpdateHeaderSortGlyphs();
            }
            void UpdateTablePager() => UpdatePagerUi(pager, pageLabel, countLabel, prevBtn, nextBtn, pagerDots,
                node.PaginationDisplayStyle, p => { currentPage = Mathf.Max(0, p); ViewOnlyRebuild(); },
                node.EnablePagination, !designMode, node.PageSize, lastVisibleCount, currentPage, lastPageStart, lastPageCount);

            List<int> ComputeTablePageIndices(out bool reorderAllowed, out int pageStartOffset)
            {
                var visible = CollectionViewModel.BuildVisibleIndices(prop, searchField?.value, node.SearchProperty, SortPath(), sortDescending);
                // Paginate in design too (value-column tables) so the pager preview matches the inspector.
                int pageSizeEff = node.EnablePagination ? Mathf.Max(1, node.PageSize) : Mathf.Max(1, visible.Count);
                // Hard safety cap (mirrors the List renderer, see HardMaxUnpagedRows): a huge unpaged table would
                // otherwise build every row and freeze the editor. Build the first N + a note below.
                if (!node.EnablePagination && pageSizeEff > HardMaxUnpagedRows) pageSizeEff = HardMaxUnpagedRows;
                currentPage = CollectionViewModel.ClampPage(currentPage, visible.Count, pageSizeEff);
                var (start, count) = CollectionViewModel.PageSlice(visible.Count, pageSizeEff, currentPage);
                lastVisibleCount = visible.Count; lastPageStart = start; lastPageCount = count;
                reorderAllowed = sortColumnIndex < 0 && string.IsNullOrEmpty(searchField?.value);
                pageStartOffset = start;
                return visible.GetRange(start, count);
            }

            // Hard-cap overflow note (mirrors the List renderer): pagination is off but the table exceeds
            // HardMaxUnpagedRows, so ComputeTablePageIndices built only the first N rows. Point the user at the
            // Pagination toggle. Appended after the live rows in both the initial build and each in-place rebuild.
            void AddTableOverflowNote()
            {
                if (node.EnablePagination || lastVisibleCount <= HardMaxUnpagedRows) return;
                var capped = new Label($"Showing the first {HardMaxUnpagedRows} of {lastVisibleCount} items. " +
                    "Turn on Pagination for this collection (in the Layout Designer) to page through all of them.");
                capped.style.color = WidgetPalette.TextGray;
                capped.style.unityFontStyleAndWeight = FontStyle.Italic;
                capped.style.fontSize = 11;
                capped.style.whiteSpace = WhiteSpace.Normal;
                capped.style.paddingTop = 4;
                capped.style.paddingBottom = 4;
                itemsContainer.Add(capped);
            }

            // A value column (a primitive List<int>/List<string>/etc.) has no columns to design, so render
            // all rows like the live inspector even in design mode — otherwise design mode shows a single
            // representative row and "add" looks like it does nothing. Struct tables keep the
            // one-representative-row design view (where you select/drag the column cards).
            if (designMode && !isValueColumn)
            {
                BuildTableDesignRow(
                    node, resolvedPath, columns, prop, serializedObject, onRebuildRequired,
                    theme, selectedNode, dragController, layoutAsset, designInteract, itemsContainer, columnCells);
                // The pager above the single representative design row is a PREVIEW of the inspector — base
                // its counts on the real array so it reads "Page 1 of N · showing 1–PageSize of total".
                int psz = Mathf.Max(1, node.PageSize);
                lastVisibleCount = prop.arraySize;
                lastPageStart = 0;
                lastPageCount = Mathf.Min(psz, prop.arraySize);
            }
            else
            {
                var pageIndices = ComputeTablePageIndices(out bool tReorder, out int tOffset);
                renderedArraySize = prop.arraySize;
                BuildTableLiveRows(columns, prop, serializedObject, structuralRebuild, itemsContainer,
                    node.ReorderStyle, columnCells, designMode, pageIndices, tReorder, tOffset,
                    node.ZebraStriping, node.ZebraColor.Resolve(WidgetPalette.ZebraStripe),
                    node.RowSeparator, node.RowSeparatorColor, theme, layoutAsset,
                    () => revealInserted?.Invoke());

                // A value-column table renders full LIVE rows even in design mode (there's no column to design).
                // In design select-only mode, make those rows inert (cells + delete + ▲▼ + grip + right-click
                // menu) so a click SELECTS the table card instead of editing/deleting/reordering data. Persistent
                // so an in-place row rebuild re-disables too. (Struct tables use the selectable BuildTableDesignRow
                // above, so this only touches value columns.)
                if (designMode && !designInteract && isValueColumn)
                    SuppressInteractionPersistently(itemsContainer);
                AddTableOverflowNote();
            }
            UpdateTableSortUi();
            UpdateTablePager();
            // Rows exist now, so every column's cells can be sized together. Re-run once the header row is on a
            // panel: the fit measures text through the header label, and an unattached label resolves no font
            // (the estimate it falls back to is close, but not the real thing).
            ApplyContentFit();
            // The post-attach re-fit MUST re-measure, not reuse: the first pass above ran with the header label
            // unattached, where MeasureTextSize resolves no font and MeasureHeaderText falls back to a
            // characters-x-7px estimate. Measured on the label column: 184 (estimate) -> 172 (real font).
            headerRow.RegisterCallback<AttachToPanelEvent>(_ => headerRow.schedule.Execute(() =>
            {
                fitMeasurementStale = true;
                ApplyContentFit();
            }).StartingIn(0));

            contentTarget.Add(itemsContainer);

            // Search drives a FILTER-THEN-PAGE rebuild (reset to page 0) — composes with sort + pagination.
            if (searchField != null && !designMode)
            {
                searchField.RegisterValueChangedCallback(_ => { currentPage = 0; ViewOnlyRebuild(); });
            }

            // LIVE in-place row refresh (mirror of the List renderer): rebuild ONLY this table's rows +
            // "(N)" count + search box, never the whole inspector. structuralRebuild routes the live
            // +/-/insert/duplicate/delete here (the add button + BuildTableLiveRows both got it above).
            // A rebuild driven purely by the VIEW — the search box, the pager, a sort toggle. None of them can
            // change what the column fit samples (the array itself), so the fit's measurement survives them;
            // every other route into RebuildTableRowsInPlace marks it stale and re-measures. Explicit opt-IN,
            // so a new trigger added later measures by default rather than silently inheriting the skip.
            void ViewOnlyRebuild()
            {
                bool was = viewOnlyRebuild;
                viewOnlyRebuild = true;
                try { liveRebuild?.Invoke(); }
                finally { viewOnlyRebuild = was; }
            }

            void RebuildTableRowsInPlace()
            {
                if (!viewOnlyRebuild) fitMeasurementStale = true;
                serializedObject.Update();
                renderedArraySize = prop.arraySize;
                // BEFORE the page is computed: a table that opened EMPTY offered every column as sortable
                // because it had nothing to check against. The first row is the only moment that can retire
                // the ones that cannot be ordered — and it may clear the active sort, which the page
                // computation below has to see.
                SyncColumnSortability();
                itemsContainer.Clear();
                // Drop the stale per-column ROW cells (keep each column's header cell at index 0) so a
                // divider drag keeps re-flowing the live cells, not detached ones.
                for (int c = 0; c < columnCells.Length; c++)
                    if (columnCells[c].Count > 1) columnCells[c].RemoveRange(1, columnCells[c].Count - 1);
                // Search box BEFORE the page is computed (parity with the List renderer). Below 2 elements
                // SyncTableSearchField retires the box; computing the filtered page first would filter by a
                // query the user is then left with no control to clear — "No items match your search." over
                // a non-empty array.
                SyncTableSearchField();
                var pageIndices = ComputeTablePageIndices(out bool tReorder, out int tOffset);
                BuildTableLiveRows(columns, prop, serializedObject, structuralRebuild, itemsContainer,
                    node.ReorderStyle, columnCells, designMode, pageIndices, tReorder, tOffset,
                    node.ZebraStriping, node.ZebraColor.Resolve(WidgetPalette.ZebraStripe),
                    node.RowSeparator, node.RowSeparatorColor, theme, layoutAsset,
                    () => revealInserted?.Invoke());
                AddTableOverflowNote();
                if (titleLabel != null) titleLabel.text = CollectionHeaderText(node, prop);
                // Nothing left to clear ⇒ nothing to offer (List twin: the countPill retext beside it).
                CollectionClearAll.Sync(clearBtn, prop);
                UpdateTableSortUi();
                UpdateTablePager();
                // The rebuilt cells are fresh elements with no sizing on them yet — and the values they show
                // may be new, so the fit is re-measured rather than replayed.
                ApplyContentFit();
                itemsContainer.Bind(serializedObject);
            }

            // The search box lives in `toolbar` (not itemsContainer), so it survives the Clear above;
            // recreation on the 1↔2-row boundary reuses the filter-then-page callback.
            void SyncTableSearchField()
            {
                bool wantSearch = node.Searchable && !designMode && prop.arraySize >= 2;
                if (wantSearch && searchField == null)
                {
                    searchField = MakeCollectionSearchField();
                    searchField.RegisterValueChangedCallback(_ => { currentPage = 0; ViewOnlyRebuild(); });
                    toolbar.Insert(0, searchField); // top of the toolbar zone
                }
                else if (!wantSearch && searchField != null)
                {
                    searchField.RemoveFromHierarchy();
                    searchField = null;
                }
            }

            if (!designMode) liveRebuild = RebuildTableRowsInPlace;

            // The List twin's revealAppended, verbatim in intent — read the full note there. A table sorts by
            // clicking a column header rather than a dropdown, but the state (and the way an append is hidden
            // by it) is identical.
            revealAppended = () =>
            {
                if (searchField != null && !string.IsNullOrEmpty(searchField.value))
                    searchField.SetValueWithoutNotify(string.Empty);
                sortColumnIndex = -1;
                sortDescending = false;
                currentPage = node.EnablePagination ? int.MaxValue : 0;
            };

            // The List twin's revealInserted — filter and sort, never the page. Read the note there.
            revealInserted = () =>
            {
                if (searchField != null && !string.IsNullOrEmpty(searchField.value))
                    searchField.SetValueWithoutNotify(string.Empty);
                sortColumnIndex = -1;
                sortDescending = false;
            };

            // Parity with the List renderer above — the twin renderers stay pinned to the same behavior.
            AttachExternalArraySizeSync(container, prop, designMode, () => renderedArraySize, RebuildTableRowsInPlace);

            return container;
        }

        /// <summary>USS class marking a Card-mode collection row (carries the tray fill + rail color).</summary>
        internal const string RowCardClass = "sasq-list-row--card";
        /// <summary>Width of the Card-mode left edge rail.</summary>
        internal const float RowRailWidth = 3f;
        /// <summary>Right padding a Card-mode row reserves inside its tray.</summary>
        internal const float RowCardPadRight = 2f;

        /// <summary>
        /// Mirrors onto a Table's HEADER row the horizontal inset a Card row takes out of its own content
        /// box (the rail on the left, the tray's padding on the right).
        ///
        /// This is what makes Card legal in a table at all. A column is two elements in two different rows,
        /// and they are kept the same width by sharing ONE flex spec — the header cell and every row cell
        /// live in the same <c>columnCells[c]</c> list and are written identical basis/grow/shrink values.
        /// That only holds the columns in register while the header and the rows have the same INNER width:
        /// take 5px out of each row and not the header, and every growing column absorbs a share of the
        /// difference, so the cells drift left of their names — worse the further right you go. Insetting
        /// the header by the same 5px restores it exactly, and it is a single decision point so the live
        /// renderer and the standalone export cannot disagree about the number.
        /// </summary>
        internal static void ApplyTableCardHeaderInset(VisualElement headerRow, CollectionRowSeparator mode)
        {
            if (headerRow == null || mode != CollectionRowSeparator.Card) return;
            headerRow.style.paddingLeft = RowRailWidth;
            headerRow.style.paddingRight = RowCardPadRight;
        }

        /// <summary>
        /// Collapses a stored <see cref="CollectionReorderStyle"/> to one the UI still offers. Arrows and
        /// Both were retired from the designer (one drag affordance keeps every List style stylable), but
        /// the ENUM MEMBERS cannot be deleted: <c>None</c> was appended LAST to preserve serialized
        /// integers, so removing them would silently turn every stored <c>None</c> — including everything
        /// imported from <c>[NonReorderable]</c> — into a different value, and the export emitter writes the
        /// raw int into generated C#. Degrading here instead means an older layout keeps a working reorder
        /// affordance, and it is the SINGLE decision point so no renderer can forget it.
        /// </summary>
        internal static CollectionReorderStyle ResolveReorderStyle(CollectionReorderStyle stored)
            => stored == CollectionReorderStyle.None
                ? CollectionReorderStyle.None
                : CollectionReorderStyle.DragAndDrop;

        /// <summary>
        /// Paints one collection row's separator chrome — the SINGLE place the Line / Card / None decision
        /// is made, so List rows and Table rows can't drift apart (and the standalone export has one shape
        /// to mirror, see ExportedWidgetKit.ApplyRowSeparator).
        ///
        /// Why this exists at all: zebra striping separates rows by TINT, which works right up until a row
        /// gets tall. An in-line-editor row is a whole nested inspector; halfway down it the eye has lost
        /// which stripe it started in. A separator marks the boundary itself instead of the interior.
        ///
        /// <paramref name="zebraTint"/> is non-null only for rows that should be striped. It is applied
        /// here rather than at the call site because Card mode has to COMPOSITE it over the tray fill —
        /// letting it replace the fill would erase the card (the two alphas are within 0.01 of each other).
        /// </summary>
        internal static void ApplyRowSeparator(
            VisualElement row,
            CollectionRowSeparator mode,
            OverridableColor colorOverride,
            bool isLast,
            bool isTable,
            Color? zebraTint,
            bool hoverable,
            bool suppressRail = false)
        {
            if (row == null) return;

            switch (mode)
            {
                case CollectionRowSeparator.None:
                    row.style.paddingTop = isTable ? 1f : 0f;
                    row.style.paddingBottom = isTable ? 1f : 0f;
                    row.style.marginBottom = 2f;
                    row.style.borderBottomWidth = 0f;
                    if (zebraTint.HasValue) row.style.backgroundColor = zebraTint.Value;
                    break;

                case CollectionRowSeparator.Card:
                {
                    // A tray per row: filled surface, rounded corners, real spacing, and a left rail. The
                    // rail is the only cue that scales with row HEIGHT — a bottom hairline is a fixed 1px
                    // of signal whether the rows are 18px or 400px apart, which is exactly the case that
                    // sent people looking for this.
                    row.AddToClassList(RowCardClass);
                    row.style.borderBottomWidth = 0f;
                    // suppressRail: a List style that already owns the row's left edge (Timeline's rail)
                    // takes the tray WITHOUT its own rail — two rails a few pixels apart reads as a bug.
                    row.style.borderLeftWidth = suppressRail ? 0f : RowRailWidth;
                    // A TABLE's tray is tighter than a List's. A list row is one item you read on its own,
                    // so it can afford air; a table is many rows scanned at once, and the list's 5/5 + 6px
                    // would add ~16px to EVERY row — a 20-row table grows by a third of a screen for chrome.
                    // At 3/3 + 4px a table card costs 3px a row over Line and still reads as a tray.
                    // The HORIZONTAL numbers are shared, because the header mirrors them to hold register
                    // (ApplyTableCardHeaderInset) — only the vertical rhythm differs.
                    row.style.paddingTop = isTable ? 3f : 5f;
                    row.style.paddingBottom = isTable ? 3f : 5f;
                    row.style.paddingRight = RowCardPadRight;
                    row.style.marginBottom = isLast ? 0f : (isTable ? 4f : 6f);
                    row.style.borderTopLeftRadius = 4f;
                    row.style.borderTopRightRadius = 4f;
                    row.style.borderBottomLeftRadius = 4f;
                    row.style.borderBottomRightRadius = 4f;

                    // Captured at build time: constructors run inside the build's skin scope, so this is the
                    // RESOLVED theme's rail. (The hover color below is read live, per the palette convention.)
                    var rail = colorOverride.Resolve(WidgetPalette.RowRail);
                    row.style.borderLeftColor = rail;

                    row.style.backgroundColor = zebraTint.HasValue
                        ? WidgetPalette.Composite(WidgetPalette.RowCardBg, zebraTint.Value)
                        : WidgetPalette.RowCardBg;

                    // Only when the rail is actually drawn: with suppressRail the border is 0-wide, so these
                    // would repaint an invisible edge on every hover for nothing.
                    if (hoverable && !suppressRail)
                    {
                        // Accent blue is this package's hover language everywhere else; a rail is the one
                        // per-row surface that can speak it without adding noise to the body. Design mode
                        // opts out — there hover already means "this is the node you'd select".
                        row.RegisterCallback<MouseEnterEvent>(_ => row.style.borderLeftColor = WidgetPalette.RowRailHover);
                        row.RegisterCallback<MouseLeaveEvent>(_ => row.style.borderLeftColor = rail);
                    }
                    break;
                }

                default: // CollectionRowSeparator.Line
                {
                    // A rule with no gutter reads as an underline glued to the content above it. Symmetric
                    // padding above / margin below floats it in the middle of the space between two rows.
                    float pad = isTable ? 2f : 3f;
                    row.style.paddingTop = pad;
                    row.style.paddingBottom = pad;
                    // Never a rule after the last row — a trailing rule reads as "there's more below".
                    row.style.marginBottom = isLast ? 0f : (isTable ? 2f : 4f);
                    row.style.borderBottomWidth = isLast ? 0f : 1f;
                    if (!isLast) row.style.borderBottomColor = colorOverride.Resolve(WidgetPalette.RowDivider);
                    if (zebraTint.HasValue) row.style.backgroundColor = zebraTint.Value;
                    break;
                }
            }
        }

        // Live preview / real inspector: the current page's elements as editable rows of cells.
        // pageIndices are RAW array indices (filtered/sorted/paged by the caller); each row still carries
        // its raw index in userData so reorder/delete/menu map back correctly. reorderAllowed is false while
        // a sort/filter is active — then the drag grip / ▲▼ are suppressed (a spacer keeps columns aligned).
        private static void BuildTableLiveRows(
            List<BindingNode> columns,
            SerializedProperty prop,
            SerializedObject serializedObject,
            Action onRebuildRequired,
            VisualElement itemsContainer,
            CollectionReorderStyle reorderStyle,
            List<VisualElement>[] columnCells,
            bool designMode,
            IReadOnlyList<int> pageIndices,
            bool reorderAllowed,
            int pageStartOffset,
            bool zebra,
            Color zebraColor,
            CollectionRowSeparator separator,
            OverridableColor separatorColor,
            // Both needed only so a cell can go through BuildNode like every other leaf — see the cell loop.
            SasquatchThemeAsset theme,
            InspectorLayoutAsset layoutAsset,
            // Handed to the row menu's Insert so a blank element is not swallowed by a live filter or sort.
            // Optional: the design-time value-column path has no view state to clear. See CollectionRowMenu.
            Action onRevealInserted = null)
        {
            bool showGrip = reorderStyle == CollectionReorderStyle.DragAndDrop || reorderStyle == CollectionReorderStyle.Both;
            bool showArrows = reorderStyle == CollectionReorderStyle.Arrows || reorderStyle == CollectionReorderStyle.Both;
            bool gripActive = showGrip && reorderAllowed;
            bool arrowsActive = showArrows && reorderAllowed;

            // Same reasoning as the List's PopulateListRows: this runs for a page hop, a sort, a search, an
            // add or a delete, and a selection held across any of those is stale.
            CollectionRowSelection.For(itemsContainer)?.Clear();

            // The List twin's row-entrance window, verbatim in intent — read the note over there. A table
            // rebuilds through the same localized path and recreates every row on an add, so it needs the
            // same one-shot range or the whole table waves.
            var rowEntrance = CollectionRowEntrance.Consume(prop, designMode);

            int pos = pageStartOffset; // absolute visible position, for consistent zebra parity across pages
            int built = 0;             // rows emitted so far, for "is this the last one" (no trailing rule)
            foreach (int index in pageIndices)
            {
                var itemProp = prop.GetArrayElementAtIndex(index);

                var row = new VisualElement();
                row.userData = index; // reorder target (see CollectionRowReorder) — always the RAW index
                row.style.flexDirection = FlexDirection.Row;
                // TOP, not center (matching BuildTableDesignRow). The row is only ever as tall as its
                // TALLEST cell, so centering pushed every short cell down by half the difference — one
                // column given a taller widget (a 36px progress bar) sank the name and the rating pips into
                // the middle of the row. Text has to stay on the row's first eye-line to be scannable.
                row.style.alignItems = Align.FlexStart;
                row.AddToClassList(TableRowClass);
                // Separator chrome + zebra in one pass. isTable only tunes the rhythm now — a Card tray is
                // real here, held in register by the matching header inset applied above.
                ApplyRowSeparator(row, separator, separatorColor,
                    isLast: ++built == pageIndices.Count,
                    isTable: true,
                    zebraTint: zebra && (pos++ & 1) == 1 ? zebraColor : (Color?)null,
                    // Design mode opts out of the rail's hover, exactly as a List row does: there a hover
                    // already means "this is the node you'd select", and two hover languages fight.
                    hoverable: !designMode);

                VisualElement grip = null;
                if (gripActive)
                {
                    grip = MakeReorderGrip();
                    row.Add(grip);
                }
                else if (showGrip)
                {
                    // Reorder paused (sorted/filtered) but the style reserves a grip column in the header —
                    // keep a spacer so the cells stay aligned under the header.
                    row.Add(MakeTableSpacer(TableGripWidth));
                }

                var indexLabel = new Label($"[{index}]");
                indexLabel.AddToClassList(CollectionRowReorder.IndexLabelClass);
                indexLabel.style.width = TableIndexWidth;
                indexLabel.style.flexShrink = 0;
                indexLabel.style.fontSize = 10;
                indexLabel.style.color = WidgetPalette.TextGray;
                // CENTRED, like the design row and like the row's other two pieces of chrome (the grip pins
                // its own align-self: center in USS; the actions lane centres itself). This was a 4px top
                // margin, which reads identically on a one-line row but leaves the [index] stranded at the
                // top the moment a column carries a taller widget.
                indexLabel.style.alignSelf = Align.Center;
                row.Add(indexLabel);

                for (int c = 0; c < columns.Count; c++)
                {
                    var column = columns[c];

                    var cell = new VisualElement();
                    cell.style.flexGrow = ColumnWeightOf(column);
                    cell.style.flexBasis = 0;
                    cell.style.minWidth = TableColumnMinWidth;
                    // A squeezed cell must TRUNCATE, never paint over its neighbours. Without this a table
                    // narrower than its columns' minimums renders as overlapping controls and stray text —
                    // and the overflow rescue prunes at an inline Hidden, so a clipped cell can't summon a
                    // phantom pan scroller either.
                    cell.style.overflow = Overflow.Hidden;
                    cell.AddToClassList(TableCellClass);
                    // ...and a table cell IS a narrow track (see FieldLabelColumn.ScopeClass): a stock
                    // row nested inside it -- a compound column's foldout children -- must fit THIS
                    // cell's gutter. Measured against the InspectorElement instead, the stock formula
                    // subtracts the cell's own x-offset and writes `width: 0` on every label, which
                    // paints as a truncation dot. Lockstep with the design-row twin below and with
                    // SerializedDictionaryDrawer.MakeCell.
                    cell.AddToClassList(FieldLabelColumn.ScopeClass);

                    // The cell's widget goes through BuildNode — the same door a List row's item body and the
                    // designer's representative table row already use. That is what carries the column node's
                    // Appearance styling, Field Styling, conditions, drawer bridge and quick-menu identity into
                    // the real Inspector. Building the field straight off BindingFactory (what this did) ran the
                    // widget constructor and nothing after it, so a styled column looked right on the canvas —
                    // which DOES route through BuildNode — and rendered plain in the Inspector. Both live render
                    // paths (fallback + displacement) come through here, so this one call site covers both.
                    //
                    // designMode is pinned FALSE rather than forwarded: a design-mode table renders its columns
                    // through BuildTableDesignRow, and the only rows that reach here in design mode belong to a
                    // value column, which must stay plain live rows rather than becoming designer cards.
                    // The "Value" column of a primitive list takes this same door. Its path IS the element's,
                    // which BuildNode expresses as an EMPTY PropertyPath against the element base path — so a
                    // value column authored in the designer carries its styling here like any other column,
                    // rather than being rebuilt as a bare field on every build.
                    VisualElement field = BuildNode(
                        column,
                        itemProp.propertyPath,
                        serializedObject,
                        onRebuildRequired,
                        theme,
                        false,
                        null,
                        null,
                        layoutAsset,
                        nodePath: itemProp.propertyPath + "/" + c,
                        tableCell: true);
                    if (field != null)
                    {
                        // NOT flexGrow. A cell is a COLUMN container, so grow here is the VERTICAL axis — the
                        // width already comes from the cell's default align-items: stretch. With grow on, every
                        // cell's widget inflated to the row's tallest cell (a 36px progress bar stretched the
                        // name TextField to 36px) while the cell itself stayed at its own 20px content height,
                        // so the widget overflowed and the cell's overflow:Hidden sliced its text in half.
                        // Measured: grow 1 → field h=36 in a 20px cell; grow 0 → h=18, width unchanged at 125.
                        field.style.flexGrow = 0;
                        // The Inspector panel styles .unity-base-field with a NEGATIVE right margin, so a
                        // cell-filling field's right border sits 2px outside its cell — invisible until the
                        // cell started clipping, at which point every field in the table lost its right edge
                        // and read as sliced off. The cell owns its spacing (the divider lane), so pin the
                        // field's horizontal margins to 0 inline, which beats any panel's stylesheet.
                        PinCellFieldMargins(field);
                        // Margins alone only stop the field from starting outside the cell — it can still be
                        // WIDER than one. A cell clips, so anything that refuses to shrink loses its right
                        // border and rounded corner to the clip rect (a slider's fixed 50px value box, a
                        // dropdown's content-sized button). Let the content give instead.
                        FitCellContent(field);
                        // No ValidationBinder.Attach here: both branches above already ran it (BuildNode does it
                        // for the authored columns). Attaching twice would double-wire the tracker and paint the
                        // outline over itself.
                        cell.Add(field);
                    }
                    row.Add(cell);
                    columnCells[c].Add(cell);
                    row.Add(MakeTableSpacer(TableDividerWidth)); // aligns cells under the header dividers
                }

                var actions = new VisualElement();
                actions.style.flexDirection = FlexDirection.Row;
                actions.style.width = ActionsWidthFor(reorderStyle);
                actions.style.flexShrink = 0;
                actions.style.justifyContent = Justify.FlexEnd;
                // Inside the reserved width (UITK boxes are border-box), so the buttons end this far short of
                // the row's trailing edge instead of hard against it.
                actions.style.paddingRight = TableActionsTrailingInset;
                // The row is top-aligned (short cells must not sink — see the row's Align.FlexStart), but the
                // two pieces of ROW CHROME are not cells: the grip already pins `align-self: center` in USS, so
                // a top-aligned ✕ sat level with the first eye-line while the grip floated at the row's middle.
                // On a tall row (a 36px progress bar) they were half a row apart. Centre this to match the grip
                // — the two live at opposite ends of the same row and should read as a pair.
                actions.style.alignSelf = Align.Center;

                if (arrowsActive)
                {
                    AddReorderArrows(actions, prop, serializedObject, index, itemsContainer, hoverReveal: true);
                }

                var delBtn = new Button(() =>
                {
                    int oldSize = prop.arraySize;
                    prop.DeleteArrayElementAtIndex(index);
                    if (prop.arraySize == oldSize)
                    {
                        prop.DeleteArrayElementAtIndex(index);
                    }
                    serializedObject.ApplyModifiedProperties();
                    onRebuildRequired?.Invoke();
                });
                delBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.X, 9));
                delBtn.tooltip = "Remove this row";
                StyleTableRowButton(delBtn, hoverReveal: true);
                actions.Add(delBtn);

                row.Add(actions);
                // Right-click row menu: Duplicate / Insert Above / Insert Below / Delete (raw-index based).
                CollectionRowMenu.Attach(row, prop, serializedObject, index, onRebuildRequired, onRevealInserted);
                itemsContainer.Add(row);
                // AFTER the Add, for the same two reasons as the List twin: seed before the first paint, and
                // a spring on a panel-less element is never ticked.
                rowEntrance.Play(row, index);

                if (grip != null)
                {
                    // Paginated reorder is within-page; pageStartOffset maps the page-relative drop gap to
                    // an absolute array index. allowMultiSelect matches the List path — see there.
                    CollectionRowReorder.Attach(grip, row, itemsContainer, prop, serializedObject,
                        indexOffset: pageStartOffset, allowMultiSelect: true,
                        onStructuralChange: onRebuildRequired);
                }
            }

            if (prop.arraySize > 0 && pageIndices.Count == 0)
            {
                // The array has rows but none match the active search.
                var noMatch = new Label("No items match your search.");
                noMatch.style.color = WidgetPalette.TextGray;
                noMatch.style.unityFontStyleAndWeight = FontStyle.Italic;
                noMatch.style.fontSize = 11;
                noMatch.style.paddingTop = 4;
                noMatch.style.paddingBottom = 4;
                itemsContainer.Add(noMatch);
            }
            else if (prop.arraySize == 0)
            {
                var empty = MakeCollectionEmptyNote(prop);
                itemsContainer.Add(empty);
            }
        }

        // Design mode: a single representative row so columns stay selectable/draggable cards (built via
        // BuildNode, design-time parity with List) without cluttering the canvas with every element.
        // Uses element[0] when present, else the throwaway scratch object (dimmed) — never mutating the
        // real target. Showing one row is enough to design the columns; the count is in the title.
        private static void BuildTableDesignRow(
            CollectionNode node,
            string resolvedPath,
            List<BindingNode> columns,
            SerializedProperty prop,
            SerializedObject serializedObject,
            Action onRebuildRequired,
            SasquatchThemeAsset theme,
            LayoutNode selectedNode,
            CanvasDragController dragController,
            InspectorLayoutAsset layoutAsset,
            bool designInteract,
            VisualElement itemsContainer,
            List<VisualElement>[] columnCells)
        {
            SerializedObject rowSO = serializedObject;
            SerializedProperty rowItem = null;
            bool isTemplate = false;

            if (prop.arraySize > 0)
            {
                rowItem = prop.GetArrayElementAtIndex(0);
            }
            else if (_showItemTemplates)
            {
                var scratchSO = GetTemplateScratch(serializedObject.targetObject?.GetType(), resolvedPath);
                if (scratchSO != null)
                {
                    var scratchArray = scratchSO.FindProperty(resolvedPath);
                    if (scratchArray != null && scratchArray.isArray && scratchArray.arraySize > 0)
                    {
                        rowSO = scratchSO;
                        rowItem = scratchArray.GetArrayElementAtIndex(0);
                        isTemplate = true;
                    }
                }
            }

            if (rowItem == null)
            {
                var hint = new Label("No rows yet — columns above are the table fields.");
                hint.style.color = WidgetPalette.TextGray;
                hint.style.unityFontStyleAndWeight = FontStyle.Italic;
                hint.style.fontSize = 11;
                hint.style.paddingTop = 4;
                hint.style.paddingBottom = 4;
                itemsContainer.Add(hint);
                return;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            // The canvas's representative row previews the chosen Row Frame, same as it previews the chosen
            // ReorderStyle below. It went without one for as long as tables have had frames, so the only
            // place you could see the choice was the real Inspector.
            //
            // isLast: FALSE deliberately, and it is the whole point of the call. Line never rules after the
            // last row, and this row IS the last (only) one — passing true would leave the canvas exactly as
            // blank as it was. This row stands in for "a row", not for "the final row", so it is drawn the
            // way a row with more beneath it is drawn. No `sasq-table-row` class: that would hand the design
            // row the live resting wash it has never carried, which is a look change, not a preview.
            ApplyRowSeparator(row, node.RowSeparator, node.RowSeparatorColor,
                isLast: false, isTable: true, zebraTint: null, hoverable: false);
            if (isTemplate) row.style.opacity = 0.6f;
            // Table mode never BUILDS the item-layout container — this one representative row is the only
            // thing standing in for it — so without a marker the column cards are cards with no parent, and a
            // drag over them resolves to nothing. Tagged here, the drop map treats the row AS that container's
            // element and lays its slots out horizontally: dropping a column between two others reorders the
            // model children, and the table's columns follow. See DropSnapshotBuilder.
            if (ColumnsAreDirectChildrenOf(columns, node.ItemLayout))
                row.AddToClassList(TableColumnHostClass);

            var indexLabel = new Label(isTemplate ? "tmpl" : "[0]");
            indexLabel.style.width = TableIndexWidth;
            indexLabel.style.flexShrink = 0;
            indexLabel.style.fontSize = 10;
            indexLabel.style.color = WidgetPalette.TextGray;
            indexLabel.style.alignSelf = Align.Center; // live twin: row chrome centres, only CELLS top-align
            if (isTemplate) indexLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            row.Add(indexLabel);

            for (int c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var cell = new VisualElement();
                cell.style.flexGrow = ColumnWeightOf(column);
                cell.style.flexBasis = 0;
                cell.style.minWidth = TableColumnMinWidth;
                // Marker only (the class carries no USS) so tests and probes can find the design row's columns
                // the same way they find live ones. NOT the live cell's overflow:Hidden — a design cell stays
                // unclipped so the designer's selection badges and drop chrome can spill past its edge.
                cell.AddToClassList(TableCellClass);
                // Same narrow-track scope the live row's cell takes -- see there. The canvas must
                // fit its gutter the same way or the design row and the Inspector row disagree.
                cell.AddToClassList(FieldLabelColumn.ScopeClass);

                // A column is TWO elements in two different rows — the header that names it and the cell that
                // shows its value — and nothing in the tree says they are the same thing. Record the pairing
                // here, the one place that holds both: selection lights the header, hover lights the header,
                // and a drag slides the header along with the cell. Without it every consumer would re-pair
                // them by index, which is precisely how the two halves drift apart.
                var headerCell = (columnCells != null && c < columnCells.Length && columnCells[c].Count > 0)
                    ? columnCells[c][0]
                    : null;
                if (headerCell != null)
                {
                    TableColumnLink.Set(cell, headerCell);
                    bool columnSelected = column == selectedNode
                                          || (_selectionSet != null && _selectionSet.Contains(column));
                    headerCell.EnableInClassList(TableColumnLink.HeaderSelectedClass, columnSelected);
                    // Hover the value, light the name (and vice versa is not needed — the header is the label
                    // for the cell, so the answer people want is always "which name is this value under").
                    var hoverHeader = headerCell;
                    cell.RegisterCallback<MouseEnterEvent>(_ => hoverHeader.AddToClassList(TableColumnLink.HeaderHotClass));
                    cell.RegisterCallback<MouseLeaveEvent>(_ => hoverHeader.RemoveFromClassList(TableColumnLink.HeaderHotClass));
                }

                var built = BuildNode(
                    column,
                    rowItem.propertyPath,
                    rowSO,
                    onRebuildRequired,
                    theme,
                    true,
                    selectedNode,
                    dragController,
                    layoutAsset,
                    designInteract,
                    nodePath: rowItem.propertyPath + "/" + c,
                    // The canvas row is a preview of the Inspector's row, so its cells are TABLE CELLS: the
                    // column header above already names the field, and repeating that name inside every cell
                    // is what left "Ra… Qu… Co…" truncations where the values should be. Live/design twins.
                    tableCell: true);
                if (built != null)
                {
                    // Grow OFF for the same reason as the live cell: the cell is a column, so grow is the
                    // vertical axis and the widget would inflate to the tallest column in the row. Width comes
                    // from the cell's align-items: stretch. (Live/design twins — keep in lockstep.)
                    built.style.flexGrow = 0;
                    // Same margin pin as the live row, so the designer's representative row sits at exactly
                    // the width the inspector will give it (the panels disagree by 5px otherwise).
                    PinCellFieldMargins(built);
                    // Live/design twin: the canvas cell is deliberately UNCLIPPED, so a non-shrinking widget
                    // does not visibly shear here — it silently overhangs into the next column instead, which
                    // is a worse lie (you author a column that looks fine and ships sliced). Fit both, so what
                    // the canvas shows at a given column width is what the Inspector will show.
                    FitCellContent(built);
                    cell.Add(built);
                    // Scratch template cells bind to their own SerializedObject (same nested-bind pattern
                    // the List template uses), so the outer Bind to the real object won't clobber them.
                    if (isTemplate)
                    {
                        built.Bind(rowSO);
                    }
                }
                row.Add(cell);
                columnCells[c].Add(cell);
                row.Add(MakeTableSpacer(TableDividerWidth)); // aligns cells under the header dividers
            }

            // Mirror the live reorder affordances so Design Mode previews the chosen ReorderStyle.
            // This is one representative row, so the controls are non-functional previews — identical to
            // what a single-element live table shows (its ▲▼ are both disabled at index 0 of 1).
            bool designShowGrip = node.ReorderStyle == CollectionReorderStyle.DragAndDrop || node.ReorderStyle == CollectionReorderStyle.Both;
            bool designShowArrows = node.ReorderStyle == CollectionReorderStyle.Arrows || node.ReorderStyle == CollectionReorderStyle.Both;
            if (designShowGrip)
            {
                row.Insert(0, MakeReorderGrip());
            }
            if (designShowArrows)
            {
                var actions = new VisualElement();
                actions.style.flexDirection = FlexDirection.Row;
                actions.style.flexShrink = 0;
                actions.style.justifyContent = Justify.FlexEnd;
                actions.style.alignSelf = Align.Center; // live twin: sits level with the grip, not the first eye-line
                actions.style.paddingRight = TableActionsTrailingInset; // …and the same trailing breathing room
                var up = new Button();
                up.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronUp, 9));
                StyleTableRowButton(up);
                up.SetEnabled(false);
                var down = new Button();
                down.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronDown, 9));
                StyleTableRowButton(down);
                down.SetEnabled(false);
                actions.Add(up);
                actions.Add(down);
                row.Add(actions);
            }

            itemsContainer.Add(row);
        }

        // A fixed-width, non-interactive spacer that reserves the grip / index / divider / actions columns in
        // the table header so the headers line up with the cells.
        private static VisualElement MakeTableSpacer(float width)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.style.width = width;
            s.style.flexGrow = 0;
            s.style.flexShrink = 0;
            return s;
        }

        // The proportional flex weight for a table column: the binding's stored ColumnWeight, or 1 ("auto",
        // an equal share) when unset — so layouts authored before resizable columns render equal-width.
        private static float ColumnWeightOf(LayoutNode col) => (col != null && col.ColumnWeight > 0f) ? col.ColumnWeight : 1f;

        // True while a designer drag is in flight (the window root carries .designer-dragging).
        // Divider hover affordances stand down during a drag: the drop preview is the only thing
        // that may highlight, and a resizer lighting up mid-drag reads as a bogus drop target.
        private static bool DragInProgress(VisualElement el)
        {
            for (var cur = el; cur != null; cur = cur.parent)
                if (cur.ClassListContains("designer-dragging")) return true;
            return false;
        }

        // The drag handle between two column headers (doubles as the inter-column gap). A faint vertical line
        // brightens on hover; the resize-horizontal cursor comes from USS (.sasq-col-resizer in
        // SasquatchWidgets.uss). Wire the drag via AttachColumnResizer.
        private static VisualElement MakeColumnDivider(bool interactive = true, string tooltip = null, bool dragCursor = true)
        {
            var divider = new VisualElement();
            // dragCursor false = the auto-fit-only trailing lane: hover + tooltip, but no resize cursor
            // (a drag promise it can't keep).
            if (interactive && dragCursor) divider.AddToClassList("sasq-col-resizer"); // resize-horizontal cursor (USS)
            divider.style.width = TableDividerWidth;
            divider.style.flexGrow = 0;
            divider.style.flexShrink = 0;
            divider.style.alignSelf = Align.Stretch;
            divider.style.justifyContent = Justify.Center;
            divider.style.alignItems = Align.Center;
            if (interactive) divider.tooltip = tooltip ?? "Drag to resize columns";

            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.width = 1;
            line.style.height = Length.Percent(70);
            // Interactive (draggable) dividers read as a normal mid-grey; a static separator (resize off) is a
            // quieter grey so it clearly isn't a drag handle. Captured so callbacks stay on the built skin.
            var gripIdle = WidgetPalette.GripLineIdle;
            var gripHot = WidgetPalette.GripHot;
            line.style.backgroundColor = interactive ? gripIdle : WidgetPalette.GripLineStatic;
            divider.Add(line);

            // The hover "grab" affordance only makes sense when it's actually draggable; a non-interactive
            // divider stays a plain static separator.
            if (interactive)
            {
                divider.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    if (DragInProgress(divider)) return; // mid-drag: never read as a drop target
                    line.style.width = 2;
                    line.style.backgroundColor = gripHot;
                });
                divider.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    line.style.width = 1;
                    line.style.backgroundColor = gripIdle;
                });
            }
            return divider;
        }

        // Pure column-resize math (unit-tested via SasquatchVerificationTests): given the two adjacent
        // columns' current pixel widths + flex weights, a horizontal drag delta, and a per-column minimum
        // width, return their new weights. The pair's total width AND total weight are conserved, so the
        // OTHER columns are unaffected.
        /// <summary>Converts two dragged pixel widths into normalized flex weights for a resizable column pair.</summary>
        public static (float left, float right) ComputeResizedWeights(float leftWidth, float rightWidth, float dx, float leftWeight, float rightWeight, float minWidth)
        {
            float total = leftWidth + rightWidth;
            float weightSum = leftWeight + rightWeight;
            if (total <= 0f || weightSum <= 0f || total < minWidth * 2f)
                return (leftWeight, rightWeight); // degenerate / no room to resize
            float newLeftWidth = Mathf.Clamp(leftWidth + dx, minWidth, total - minWidth);
            float newLeft = weightSum * (newLeftWidth / total);
            return (newLeft, weightSum - newLeft);
        }

        // Pure whole-table resize math (unit-tested via SasquatchVerificationTests): the table analog of
        // ComputeResizedWeights, but the drag CASCADES. Dragging right grows the column left of the divider
        // and crushes the right-side columns nearest-first down to minWidth (then the next, and so on);
        // dragging left mirrors — the left-side columns crush nearest-first and the right neighbor takes
        // the space. The divider therefore follows the pointer across nearly the whole table instead of
        // clamping the moment its immediate neighbor bottoms out. Total width and total weight conserved.
        /// <summary>Cascade drag: new flex weights for every column from a divider drag of <paramref name="dx"/> px.</summary>
        public static float[] ComputeCascadeResizedWeights(float[] widths, float[] weights, int leftCol, float dx, float minWidth)
        {
            int n = widths?.Length ?? 0;
            if (n < 2 || weights == null || weights.Length != n || leftCol < 0 || leftCol >= n - 1)
                return weights != null ? (float[])weights.Clone() : new float[0];
            float total = 0f, weightSum = 0f;
            for (int i = 0; i < n; i++) { total += widths[i]; weightSum += weights[i]; }
            if (total <= 0f || weightSum <= 0f) return (float[])weights.Clone();

            var w = (float[])widths.Clone();
            if (dx > 0f)
            {
                float remaining = dx;
                for (int i = leftCol + 1; i < n && remaining > 0f; i++)
                {
                    float give = Mathf.Min(remaining, Mathf.Max(0f, w[i] - minWidth));
                    w[i] -= give; w[leftCol] += give; remaining -= give;
                }
            }
            else if (dx < 0f)
            {
                float remaining = -dx;
                for (int i = leftCol; i >= 0 && remaining > 0f; i--)
                {
                    float give = Mathf.Min(remaining, Mathf.Max(0f, w[i] - minWidth));
                    w[i] -= give; w[leftCol + 1] += give; remaining -= give;
                }
            }

            var result = new float[n];
            for (int i = 0; i < n; i++) result[i] = weightSum * (w[i] / total);
            return result;
        }

        // Pure auto-fit math (unit-tested via SasquatchVerificationTests): give column `col` exactly
        // `desiredWidth` px of the current total and scale the remaining columns proportionally into what's
        // left, conserving total width and total weight. The fitted column may not push the others below
        // minWidth collectively (each cell's style min-width is the per-column backstop).
        /// <summary>Auto-fit: new flex weights that size <paramref name="col"/> to <paramref name="desiredWidth"/> px.</summary>
        public static float[] ComputeAutoFitWeights(float[] widths, float[] weights, int col, float desiredWidth, float minWidth)
        {
            int n = widths?.Length ?? 0;
            if (n < 2 || weights == null || weights.Length != n || col < 0 || col >= n)
                return weights != null ? (float[])weights.Clone() : new float[0]; // 1 column always fills
            float total = 0f, weightSum = 0f;
            for (int i = 0; i < n; i++) { total += widths[i]; weightSum += weights[i]; }
            if (total <= 0f || weightSum <= 0f) return (float[])weights.Clone();

            float desired = Mathf.Clamp(desiredWidth, minWidth, Mathf.Max(minWidth, total - (n - 1) * minWidth));
            float othersOld = total - widths[col];
            float othersNew = total - desired;
            var result = new float[n];
            for (int i = 0; i < n; i++)
            {
                float w = i == col ? desired
                    : othersOld > 0f ? widths[i] * (othersNew / othersOld)
                    : othersNew / (n - 1); // fitted column previously owned everything: share the rest evenly
                result[i] = weightSum * (w / total);
            }
            return result;
        }

        /// <summary>
        /// The bold accent colour the designer's hover/selection RING draws on top of a card. With no custom
        /// background it's a strong, fully-opaque blue (the classic accent, brighter than the card border's
        /// 0.6-alpha hover); with one, it derives a high-contrast shade FROM that background — lightening dark
        /// backgrounds, darkening light ones — so the ring can never be hidden behind the field. Public +
        /// static so the verification suite can assert it directly.
        /// </summary>
        public static Color RingHighlightColor(Color? customBackground)
        {
            var accent = new Color(0.353f, 0.549f, 0.8f, 1f);
            if (customBackground == null) return accent;

            Color bg = customBackground.Value;
            float luma = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            // Push away from the background's luminance so the ring always contrasts with the field behind it.
            Color contrast = luma < 0.5f ? Color.Lerp(bg, Color.white, 0.6f) : Color.Lerp(bg, Color.black, 0.5f);
            contrast.a = 1f;
            return contrast;
        }

        // A colour under this alpha is not on screen, so it cannot hide anything drawn on top of it.
        private const float SelectionMinVisibleAlpha = 0.15f;
        // How far (in hue degrees) a selection hue must stay from the card's own colours to be legible on it.
        private const float SelectionHueClearance = 45f;
        // "Barely opaque" — enough that the whole container reads as picked, little enough that the fields
        // inside it keep their own colours.
        private const float SelectionWashAlpha = 0.16f;

        /// <summary>
        /// The hue the designer marks a SELECTED CONTAINER with — its wash, its ring and its badge all share it.
        /// Normally the package's accent blue. The exception is the case that motivated this: a container whose
        /// OWN box styling is blue, where a blue mark disappears into the very thing it is marking. Candidates
        /// are tried in order (accent blue → amber → magenta) and the first one clear of both the card's
        /// background and its border wins; if a card somehow collides with all three we keep the hue that is
        /// furthest from them. Fully deterministic, so a card's chrome is identical across rebuilds.
        /// Public + static so the verification suite can assert the table directly.
        /// </summary>
        public static Color SelectionAccentFor(Color? background, Color? border)
        {
            var candidates = new[]
            {
                WidgetPalette.Accent,                 // the package's selection blue (#2F7AD4 dark / #2265B8 light)
                new Color(0.91f, 0.64f, 0.24f, 1f),   // amber — most of a wheel away from the accent
                new Color(0.83f, 0.36f, 0.78f, 1f),   // magenta — for a card that is blue AND amber
            };

            var own = new List<Color>();
            if (background.HasValue && background.Value.a >= SelectionMinVisibleAlpha) own.Add(background.Value);
            if (border.HasValue && border.Value.a >= SelectionMinVisibleAlpha) own.Add(border.Value);
            if (own.Count == 0) return candidates[0];

            Color best = candidates[0];
            float bestClearance = -1f;
            foreach (var candidate in candidates)
            {
                float clearance = float.MaxValue;
                foreach (var c in own) clearance = Mathf.Min(clearance, SelectionHueClearanceBetween(candidate, c));
                if (clearance >= SelectionHueClearance) return candidate;
                if (clearance > bestClearance) { bestClearance = clearance; best = candidate; }
            }
            return best;
        }

        /// <summary>
        /// The translucent fill painted over a whole selected container. Public + static for verification.
        /// </summary>
        public static Color SelectionWashFor(Color accent)
        {
            accent.a = SelectionWashAlpha;
            return accent;
        }

        // Hue clearance in degrees (0 = the same hue, 180 = opposite). A grey, a near-black or a near-white has
        // NO hue to collide with, so it scores maximum clearance — the black-bordered container that started
        // this keeps the plain blue mark, which is the whole point: the fallback is for BLUE cards, not dark ones.
        private static float SelectionHueClearanceBetween(Color a, Color b)
        {
            Color.RGBToHSV(a, out float ha, out float sa, out float va);
            Color.RGBToHSV(b, out float hb, out float sb, out float vb);
            if (sa < 0.18f || va < 0.10f || sb < 0.18f || vb < 0.10f) return 180f;
            return Mathf.Abs(Mathf.DeltaAngle(ha * 360f, hb * 360f));
        }

        /// <summary>
        /// Lightens a binding's own outline colour for its hovered state — a subtle "active" lift that keeps
        /// the user's chosen hue while signalling the card under the cursor. Public + static for direct
        /// verification.
        /// </summary>
        public static Color LightenOutlineForHover(Color outline)
        {
            Color lifted = Color.Lerp(outline, Color.white, 0.3f);
            lifted.a = outline.a; // preserve the outline's own opacity
            return lifted;
        }

        // Samples every column's current pixel width (header cell) + stored weight — the shared input to
        // the cascade-drag and auto-fit math.
        private static void SampleTableColumns(List<BindingNode> columns, List<VisualElement>[] columnCells, out float[] widths, out float[] weights)
        {
            int n = columns.Count;
            widths = new float[n];
            weights = new float[n];
            for (int i = 0; i < n; i++)
            {
                widths[i] = columnCells[i].Count > 0 ? columnCells[i][0].worldBound.width : 0f;
                weights[i] = ColumnWeightOf(columns[i]);
            }
        }

        // Excel's double-click-the-boundary: size column `col` to its widest content (header text + the
        // sampled rows' values, via measureColumn), reflow the others proportionally, persist once.
        private static void AutoFitTableColumn(List<BindingNode> columns, List<VisualElement>[] columnCells, int col, Action<int> applyColumnWeight, Action onLayoutEdited, Func<int, float> measureColumn)
        {
            if (columns.Count < 2) return; // a single column always fills the table
            SampleTableColumns(columns, columnCells, out var widths, out var weights);
            var fitted = ComputeAutoFitWeights(widths, weights, col, measureColumn(col), TableColumnMinWidth);
            for (int i = 0; i < columns.Count; i++)
            {
                columns[i].ColumnWeight = fitted[i];
                applyColumnWeight(i);
            }
            onLayoutEdited?.Invoke();
        }

        // Wires drag-to-resize onto a header column divider. A drag CASCADES: the column left of the divider
        // grows/shrinks against ALL columns on the far side (nearest crushed first, each to its minimum), so
        // the divider tracks the pointer across the whole table. Double-click auto-fits the left column to
        // its content (Excel style, via measureColumn). Live updates go through applyColumnWeight; the model
        // edit persists once on release via _onLayoutEdited. Start values are sampled on press so the math
        // is stateless per-move and dragging back to the start restores it.
        private static void AttachColumnResizer(VisualElement divider, List<BindingNode> columns, List<VisualElement>[] columnCells, int leftCol, Action<int> applyColumnWeight, Action onLayoutEdited, Func<int, float> measureColumn)
        {
            bool resizing = false;
            float startX = 0f;
            float[] widths0 = null, weights0 = null;

            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.clickCount == 2)
                {
                    // The first press of the pair already ran a no-op drag; this one fits instead.
                    resizing = false;
                    AutoFitTableColumn(columns, columnCells, leftCol, applyColumnWeight, onLayoutEdited, measureColumn);
                    evt.StopPropagation();
                    return;
                }
                resizing = true;
                startX = evt.position.x;
                SampleTableColumns(columns, columnCells, out widths0, out weights0);
                divider.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                var next = ComputeCascadeResizedWeights(widths0, weights0, leftCol, evt.position.x - startX, TableColumnMinWidth);
                for (int i = 0; i < columns.Count; i++)
                {
                    columns[i].ColumnWeight = next[i];
                    applyColumnWeight(i);
                }
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (divider.HasPointerCapture(evt.pointerId)) divider.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke(); // persist: designer marks dirty + re-renders; inspector SetDirty's the asset
                evt.StopPropagation();
            });

            // Capture-loss (window blur / focus steal / Esc) without a PointerUp must still persist the
            // live model edit, or the resize silently reverts on the next rebuild-from-disk.
            divider.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        // The last column has no drag divider (there is nothing on its right to trade width with), but it
        // still deserves Excel's double-click-to-fit — wired onto the trailing lane after its header.
        private static void AttachColumnAutoFitOnly(VisualElement divider, List<BindingNode> columns, List<VisualElement>[] columnCells, int col, Action<int> applyColumnWeight, Action onLayoutEdited, Func<int, float> measureColumn)
        {
            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                AutoFitTableColumn(columns, columnCells, col, applyColumnWeight, onLayoutEdited, measureColumn);
                evt.StopPropagation();
            });
        }

        // ---- Auto-fit content measurement ------------------------------------------------------------
        // The desired pixel width for a whole column: header text, then the widest sampled cell value,
        // then the widget floor for content-independent widgets (a slider doesn't care how long "0.5" is).
        private static float MeasureTableColumnFitWidth(SerializedProperty arrayProp, BindingNode column, Label measurer,
            string headerText, bool sortable, bool arrangeable)
        {
            const float headerPad = 8f;
            // The sort glyph is 16px wide (it grew from 12 when sorting moved off the whole cell onto the
            // arrows), and a hover grip springs the label right by a whole GripLane. A fitted column sits
            // exactly at its measured basis with flexGrow 0, so anything not reserved here is taken out of
            // the header's own text: without the grip lane, hovering a fitted column ellipsized its title
            // every single time the user reached for it.
            float headerWidth = MeasureHeaderText(measurer, headerText)
                + (sortable ? 16f : 0f)
                + (arrangeable ? TableColumnGrip.GripLane : 0f)
                + headerPad;

            // The VALUE side, measured separately so the affix can be added to it exactly once. The cells
            // render the column's prefix/suffix beside their value and the fit never accounted for them: a
            // column carrying a unit was born exactly as narrow as its bare numbers, so the affix immediately
            // had nowhere to sit and tripped its own narrow-box demote — the unit hopped outside the cell the
            // moment the table laid out. headerText is already the DECORATED label, so the header half of
            // "Weight (kg)" is covered above; this is the per-cell half.
            float valueWidth = AutoFitWidgetFloor(column.DisplayType);
            if (arrayProp != null && arrayProp.isArray)
            {
                int count = Mathf.Min(arrayProp.arraySize, TableAutoFitSampleRows);
                for (int i = 0; i < count; i++)
                {
                    var element = arrayProp.GetArrayElementAtIndex(i);
                    var cellProp = string.IsNullOrEmpty(column.PropertyPath)
                        ? element
                        : element.FindPropertyRelative(column.PropertyPath);
                    if (cellProp == null) continue;
                    float w = MeasureTableCellWidth(cellProp, measurer);
                    if (w > valueWidth) valueWidth = w;
                }
            }
            // Added to the value side whether or not any row was sampled: a column with no rows yet still
            // renders its affix the moment one is added.
            valueWidth += MeasureColumnAffixWidth(column, measurer);

            return Mathf.Max(headerWidth, valueWidth);
        }

        /// <summary>
        /// Extra width a column needs for the affix its CELLS will render. Zero when the column has no affix,
        /// and zero for a side column-header mode lifted out of the cells — that side needs no room down here.
        /// Measured through the same (bold, header-sized) label as everything else here, which slightly
        /// over-measures; erring wide is what keeps the value readable.
        /// <para>
        /// PrefixFor/SuffixFor, not the raw node: header mode is a PER-SIDE, text-only rule, so an icon-only
        /// side still rides the cells and still needs its width booked. Asking the whole-node question here
        /// would starve exactly the affix the cell is about to draw.
        /// </para>
        /// </summary>
        private static float MeasureColumnAffixWidth(BindingNode column, Label measurer)
        {
            if (column == null) return 0f;
            return MeasureOneAffixSide(FieldAffixes.PrefixFor(column, true), measurer)
                 + MeasureOneAffixSide(FieldAffixes.SuffixFor(column, true), measurer);
        }

        private static float MeasureOneAffixSide(FieldAffix affix, Label measurer)
        {
            if (affix.IsEmpty) return 0f;
            float w = 0f;
            if (!string.IsNullOrEmpty(affix.text)) w += MeasureHeaderText(measurer, affix.text);
            if (!string.IsNullOrEmpty(affix.iconId)) w += 12f;
            return w + 8f; // the gap sits on both sides of the affix
        }

        // Text measurement through the header label (same font family/size as the cells; its bold weight
        // slightly over-measures, which errs on the readable side). Falls back to a per-character estimate
        // when the label can't resolve a font yet (fresh panel).
        //
        // NEVER ASK AN OFF-PANEL ELEMENT. MeasureTextSize reads the element's scaledPixelsPerPoint, and from
        // Unity 6.6 that logs "Trying to access the DPI setting of a visual element that is not on a panel."
        // -- once per CALL. This is reached from ApplyContentFit, which runs once per column per sampled
        // cell on every table build, and BOTH live render paths hand Unity a DETACHED tree
        // (CreateInspectorGUI returns one; Unity attaches it afterwards). Measured on a real 6000.6.0f1: ONE
        // content-fit table logged 96 of them in a single build, and a full suite run logged 612 -- every
        // one from this line, and nowhere else in the package.
        //
        // The guard changes NO measurement. Measured on both editors: a detached label returns NaN from
        // MeasureTextSize, so the per-character estimate below was already the answer in exactly this case --
        // the call was only ever asking a question the element cannot answer, and paying a console warning
        // for the privilege. Keep the NaN test as well: an ATTACHED label whose font has not resolved yet
        // (a fresh panel) returns NaN too, which is the case the original fallback was written for.
        private static float MeasureHeaderText(Label measurer, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            if (measurer == null || measurer.panel == null) return text.Length * 7f;
            float w = measurer.MeasureTextSize(text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
            return float.IsNaN(w) || w <= 0f ? text.Length * 7f : w;
        }

        // Which columns should absorb a table's leftover width. A string or an object reference reads better
        // the wider it is; a checkbox, a number, an enum or a slider is finished at its natural size and extra
        // width only makes the row look padded. Read off a sampled element, because the DisplayType alone
        // can't tell a Default int from a Default string.
        private static bool IsGreedyTableColumn(SerializedProperty arrayProp, BindingNode column)
        {
            if (column.DisplayType == DisplayType.Text) return true;
            if (arrayProp == null || !arrayProp.isArray || arrayProp.arraySize == 0) return false;
            var element = arrayProp.GetArrayElementAtIndex(0);
            var cell = string.IsNullOrEmpty(column.PropertyPath)
                ? element
                : element.FindPropertyRelative(column.PropertyPath);
            if (cell == null) return false;
            switch (cell.propertyType)
            {
                case SerializedPropertyType.String:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.ManagedReference:
                case SerializedPropertyType.ExposedReference:
                    return true;
                default:
                    return false;
            }
        }

        // Content-independent widgets render a control whose usable width has nothing to do with the value's
        // text — fitting those to text would crush them. Plain fields (Default/Text/Toggle/DropDown) measure
        // their content instead (0 = no floor).
        private static float AutoFitWidgetFloor(DisplayType displayType)
        {
            switch (displayType)
            {
                case DisplayType.Default:
                case DisplayType.Text:
                case DisplayType.Toggle:
                case DisplayType.DropDown:
                    return 0f;
                case DisplayType.Rating:
                case DisplayType.AnimationCurveEditor:
                case DisplayType.Gradient:
                    return 90f;
                default:
                    return 110f; // sliders, previews, pads, badge grids, …
            }
        }

        // The pixel width one cell's CURRENT value wants (Excel measures rendered content, not capacity).
        // Composite/native-drawer types get sensible fixed widths; unknown shapes return 0 (no opinion).
        private static float MeasureTableCellWidth(SerializedProperty p, Label measurer)
        {
            const float fieldChrome = 16f;    // text-input padding + borders
            const float dropdownChrome = 26f; // popup arrow + padding
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return 24f;
                case SerializedPropertyType.Integer:
                    return MeasureHeaderText(measurer, p.longValue.ToString()) + fieldChrome;
                case SerializedPropertyType.Float:
                    return MeasureHeaderText(measurer, p.doubleValue.ToString("G7")) + fieldChrome;
                case SerializedPropertyType.String:
                    return Mathf.Min(MeasureHeaderText(measurer, p.stringValue) + fieldChrome, 360f);
                case SerializedPropertyType.Character:
                    return 30f;
                case SerializedPropertyType.Enum:
                {
                    var names = p.enumDisplayNames;
                    string current = (p.enumValueIndex >= 0 && p.enumValueIndex < names.Length) ? names[p.enumValueIndex] : "Mixed";
                    return MeasureHeaderText(measurer, current) + dropdownChrome;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    string label = p.objectReferenceValue != null
                        ? p.objectReferenceValue.name
                        : $"None ({ObjectReferenceTypeName(p)})";
                    return MeasureHeaderText(measurer, label) + 40f; // type icon + picker button
                }
                case SerializedPropertyType.Color:
                    return 70f;
                case SerializedPropertyType.LayerMask:
                    return 110f;
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector2Int:
                    return 110f;
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector3Int:
                    return 160f;
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Quaternion:
                    return 210f;
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                    return 220f;
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Gradient:
                    return 90f;
                default:
                    return 0f;
            }
        }

        // "PPtr<$Texture2D>" -> "Texture2D" for the ObjectField's "None (Texture2D)" placeholder text.
        private static string ObjectReferenceTypeName(SerializedProperty p)
        {
            string t = p.type ?? "";
            int start = t.IndexOf('$');
            return start >= 0 && t.EndsWith(">") ? t.Substring(start + 1, t.Length - start - 2) : "Object";
        }

        // hoverReveal marks the button as LIVE TABLE row chrome, which fades in on row hover (the USS rule
        // keys off `.sasq-table-row:hover`). It is opt-IN rather than automatic because this styler is shared:
        // a LIST row's ▲▼ come through here too (see the AddReorderArrows call in the list builder) and a list
        // row is `.sasq-list-row`, so the reveal rule would never match and the arrows would sit at opacity 0
        // forever. Same trap for the designer's representative row, which is a card host, not a table row.
        private static void StyleTableRowButton(Button button, bool hoverReveal = false)
        {
            button.style.fontSize = 10;
            // Center icon children (text-only buttons are unaffected).
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            // No horizontal padding: the box is pinned to 14px and the 9px glyph is centred by the flex rules
            // above, so padding here would only squeeze (and clip) the icon it is supposed to frame.
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;
            button.style.marginLeft = 1;
            button.style.marginRight = 1;
            // Pinned, not content-derived: the actions lane reserves exactly TableRowButtonSlot per button, so
            // a button that sized itself off its glyph would drift out of that reservation the moment an icon
            // changed. Also the fix for "hard to click" — the icon is 9px, which left a 13px-tall target.
            button.style.width = TableRowButtonWidth;
            button.style.minWidth = TableRowButtonWidth;
            button.style.height = TableRowButtonHeight;
            button.style.flexShrink = 0f;
            // Vertically CENTRED on whatever row it lands in. A collection row is `align-items: FlexStart`
            // (short cells must not sink — see TestCollectionRowsTopAlignTheirCells), but a row action is
            // CHROME, not a cell: top-aligned, a 14px button sat hard against the row's top edge while the
            // drag grip at the other end centred itself, so on any row taller than one line the two ends
            // were half a row apart. Harmless where it is already handled — a table's actions LANE centres
            // itself, and inside that lane this resolves to the same place.
            button.style.alignSelf = Align.Center;
            if (hoverReveal) button.AddToClassList(TableRowActionClass);
            EditorSpringFX.ButtonSpring(button); // springy hover/press for every table-row action button (X / ▲ / ▼)
        }

        // A draggable reorder handle (Unity-style 3-line grip) for a live collection row. The caller wires it
        // up via CollectionRowReorder.Attach.
        /// <summary>
        /// The CLASSIC standalone reorder grip's USS class — the skinned twin is
        /// <see cref="ListGutterGripClass"/>. Named because keyboard handlers have to tell "the pointer is
        /// on a drag handle" from "the pointer is in a field", and a row carries both.
        /// </summary>
        internal const string ReorderGripClass = "sasq-reorder-grip";

        internal static VisualElement MakeReorderGrip()
        {
            var grip = new VisualElement();
            grip.AddToClassList(ReorderGripClass);
            grip.tooltip = "Drag to reorder";

            // Ink alphas (white on dark, black on light): the grip line's rest and hover tints.
            var restTint = WidgetPalette.Ink(0.25f);
            var hoverTint = WidgetPalette.Ink(0.5f);
            var glyph = new GripGlyph { Tint = restTint };
            grip.Add(glyph);
            grip.RegisterCallback<MouseEnterEvent>(_ => glyph.Tint = hoverTint);
            grip.RegisterCallback<MouseLeaveEvent>(_ => glyph.Tint = restTint);

            return grip;
        }

        // ▲▼ move-up/down buttons appended to a live collection row's action area. Reorders in place (no
        // full rebuild) so the values reflow without the one-frame blank flash a rebuild causes — the rows
        // are bound by array index, so syncing the SerializedObject shows the new order. The
        // buttons' enabled state is positional (top row's ▲, bottom row's ▼), so it stays correct too.
        // The two swapped rows' change-driven widgets are refreshed explicitly, in the same frame: their
        // trackers would otherwise repaint a binding tick late (and glide, not snap — see CommitMove).
        private static void AddReorderArrows(VisualElement parent, SerializedProperty prop, SerializedObject serializedObject, int index, VisualElement rowsContainer, bool hoverReveal = false)
        {
            var moveUpBtn = new Button(() =>
            {
                if (index > 0)
                {
                    prop.MoveArrayElement(index, index - 1);
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    CollectionRowReorder.RefreshRowsInRange(rowsContainer, index, index - 1);
                }
            });
            moveUpBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronUp, 9));
            StyleTableRowButton(moveUpBtn, hoverReveal); // includes the spring hover/press
            if (index == 0) moveUpBtn.SetEnabled(false);
            parent.Add(moveUpBtn);

            var moveDownBtn = new Button(() =>
            {
                if (index < prop.arraySize - 1)
                {
                    prop.MoveArrayElement(index, index + 1);
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    CollectionRowReorder.RefreshRowsInRange(rowsContainer, index, index + 1);
                }
            });
            moveDownBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronDown, 9));
            StyleTableRowButton(moveDownBtn, hoverReveal); // includes the spring hover/press
            if (index == prop.arraySize - 1) moveDownBtn.SetEnabled(false);
            parent.Add(moveDownBtn);
        }

        // A cached, throwaway instance per target type whose matching array is grown to one element, used
        // only to render an item-template preview for an empty list — so the real inspected object is
        // never mutated. Editor-only and tiny (one hidden instance per previewed type).
        private static readonly System.Collections.Generic.Dictionary<Type, UnityEngine.Object> _templateScratch
            = new System.Collections.Generic.Dictionary<Type, UnityEngine.Object>();

        // The MonoBehaviour branch parks a real GameObject in the ACTIVE SCENE, and HideFlags.DontSave keeps
        // it alive across scene loads AND domain reloads — but this dictionary does NOT survive a reload, so
        // without a teardown each reload strands the previous set and mints a fresh one. Invisible
        // (HideInHierarchy) but real, and they accumulate for the whole editor session. Every other cache in
        // the package that owns native objects hooks the same two events (AudioWaveformRenderer,
        // ParticlePreviewRenderer); this one was the exception.
        static LayoutTreeBuilder()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearTemplateScratch;
            EditorApplication.quitting += ClearTemplateScratch;
        }

        /// <summary>Destroys every cached template-scratch instance (and the GameObject hosting a component one).</summary>
        internal static void ClearTemplateScratch()
        {
            foreach (var kv in _templateScratch)
            {
                var obj = kv.Value;
                if (obj == null) continue;
                // Destroy the HOST for a component: destroying the component alone would leave the
                // GameObject behind, which is the thing actually sitting in the scene.
                var host = (obj as Component)?.gameObject;
                try { UnityEngine.Object.DestroyImmediate(host != null ? host : obj); }
                catch (Exception) { /* already gone with its scene */ }
            }
            _templateScratch.Clear();
        }

        private static SerializedObject GetTemplateScratch(Type type, string arrayPath)
        {
            if (type == null || string.IsNullOrEmpty(arrayPath)) return null;

            if (!_templateScratch.TryGetValue(type, out var obj) || obj == null)
            {
                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    obj = ScriptableObject.CreateInstance(type);
                }
                else if (typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var go = new GameObject("[Sasquatch Inspector Template Scratch]") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
                    obj = go.AddComponent(type);
                }
                else
                {
                    return null;
                }
                // HideInHierarchy|DontSave (not HideAndDontSave): the latter includes HideFlags.NotEditable,
                // which would render the template-row fields disabled/greyed. Keep them editable.
                if (obj != null) obj.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                _templateScratch[type] = obj;
            }

            var so = new SerializedObject(obj);
            var prop = so.FindProperty(arrayPath);
            if (prop == null || !prop.isArray) return null;
            if (prop.arraySize < 1)
            {
                prop.arraySize = 1;
                so.ApplyModifiedProperties();
            }
            return so;
        }

        private static string GetNodeBadgeName(LayoutNode node)
        {
            if (node is ContainerNode) return "Container";
            if (node is BindingNode) return "Binding";
            if (node is CollectionNode) return "Collection";
            if (node is ButtonNode) return "Button";
            if (node is HelpBoxNode) return "Info/Warning";
            if (node is ScriptHeaderNode) return "Script Header";
            if (node is SpaceNode) return "Space";
            if (node is LabeledDividerNode) return "Divider";
            if (node is ImageBannerNode) return "Image";
            if (node is TextBlockNode) return "Text Block";
            if (node is EmbeddedEditorNode) return "Editor";
#if SASQ_CUSTOM_WIDGET
            if (node is CustomWidgetNode) return "Custom Widget";
#endif
            return "Node";
        }

        // Nearest ancestor tagged as a build root — the non-clipping top-level host for a build. The selection badge
        // is floated here (rather than parented to its card) so a nested overflow:Hidden card can't clip it.
        private static VisualElement FindBuildRoot(VisualElement from)
        {
            for (var p = from?.parent; p != null; p = p.parent)
                if (p.ClassListContains("sasq-build-root")) return p;
            return null;
        }

        /// <summary>
        /// One floating selection badge's teardown handle: the card its geometry hook is registered on, that
        /// hook, and its follow tick. The hook HAS to live on the long-lived card — the badge's own
        /// <c>.Every(16)</c> tick cannot run while the badge is still detached, so the card's GeometryChanged is
        /// the only thing that can perform the badge's FIRST add — which means nothing tears the hook down when
        /// the badge is retired. Parked on the badge's own <c>userData</c> so every badge sweep can unregister
        /// exactly the closure it is about to orphan.
        /// </summary>
        private sealed class SelectionBadgeHook
        {
            public VisualElement Card;
            public EventCallback<GeometryChangedEvent> OnCardGeometry;
            public IVisualElementScheduledItem Follow;
        }

        /// <summary>
        /// Diagnostics + tests: selection-badge geometry hooks currently registered on LIVE designer cards. Only
        /// the primary selection carries a badge, so repeated in-place selection changes must leave this AT ONE.
        /// It climbing once per click is the leak <see cref="SweepSelectionBadges"/> exists to stop — every stale
        /// closure retained a dead badge Label for the whole life of the canvas.
        /// </summary>
        internal static int s_SelectionBadgeHooks;

        /// <summary>
        /// Diagnostics + tests: how many times a badge has actually repositioned. This is the counter that
        /// proves the UNREGISTER really happened, which <see cref="s_SelectionBadgeHooks"/> alone cannot —
        /// that one is decremented by the sweep's own bookkeeping line, so it stays correct even if the
        /// <c>UnregisterCallback</c> beside it is deleted. A swept badge's card must no longer be able to
        /// drive its closure: fire geometry at the old card and this must not move.
        /// </summary>
        internal static int s_SelectionBadgeRepositions;

        /// <summary>
        /// Retires every floating selection badge under <paramref name="buildRoot"/> — removing the element AND
        /// unregistering the card hook it owns. BOTH badge sweeps (the in-place selection-chrome pass and the
        /// live-poke node rebuild) go through here, so the hook count can never drift from the badge count.
        /// </summary>
        private static void SweepSelectionBadges(VisualElement buildRoot)
        {
            if (buildRoot == null) return;
            var badges = buildRoot.Query<VisualElement>(className: "selection-badge").ToList();
            foreach (var badge in badges)
            {
                if (badge.userData is SelectionBadgeHook hook)
                {
                    hook.Card?.UnregisterCallback(hook.OnCardGeometry);
                    hook.Follow?.Pause();
                    badge.userData = null;
                    s_SelectionBadgeHooks--;
                }
                badge.RemoveFromHierarchy();
            }
        }

        /// <summary>
        /// Creates the primary selection's floating badge for <paramref name="badgeCard"/> and keeps it glued to
        /// the card's top-left. Hosted on the NON-CLIPPING build root (not parented to the card) so a card that
        /// clips its overflow — or the first card inside such a container — can't clip the badge's protruding
        /// top:-10 tab. The card's own GeometryChanged only fires when ITS rect changes; an ANCESTOR resizing
        /// after the card settled (e.g. a circle frame's deferred aspect-lock height write one layout pass later)
        /// shifts the card without re-firing it, which would strand the badge at the card's pre-shift position
        /// — so a per-frame follow tick on the badge re-anchors it whenever the card actually moved (compare-only
        /// no-op otherwise, and the scheduler pauses automatically while the badge is detached).
        /// <paramref name="positionNow"/> is for attaching to an ALREADY-laid-out card (the in-place selection
        /// chrome pass), where no GeometryChanged is coming to do the initial placement.
        /// </summary>
        private static void AttachSelectionBadge(VisualElement badgeCard, LayoutNode node, bool positionNow)
        {
            var badge = new Label(GetNodeBadgeName(node));
            badge.AddToClassList("selection-badge");
            badge.pickingMode = PickingMode.Ignore;

            // A container whose selection hue fell back off blue (SelectionAccentFor — it is itself blue) must
            // not then be labelled in that same blue. The wash stashed the chosen accent, so both the build-time
            // path and the in-place TryUpdateSelectionChrome pass pick it up without recomputing anything.
            foreach (var child in badgeCard.Children())
            {
                if (!child.ClassListContains("designer-selection-wash") || !(child.userData is Color accent)) continue;
                badge.style.backgroundColor = accent;
                // Amber needs dark text where blue needs white — pick by the accent's own luminance.
                float accentLuma = 0.299f * accent.r + 0.587f * accent.g + 0.114f * accent.b;
                badge.style.color = accentLuma > 0.6f ? new Color(0.09f, 0.09f, 0.09f, 1f) : Color.white;
                break;
            }

            bool added = false;
            float lastX = float.NaN, lastY = float.NaN;
            void Reposition()
            {
                s_SelectionBadgeRepositions++;
                // A swept badge (selection moved on, node rebuilt in place) must never resurrect itself
                // from a stale geometry callback still registered on a live card.
                if (added && badge.parent == null) return;
                var host = FindBuildRoot(badgeCard);
                if (host == null) return;
                if (badge.parent != host)
                {
                    host.Add(badge);
                    // Count the hook only once the badge is actually IN the tree, which is the only state a
                    // sweep can ever see (and therefore the only state it can decrement).
                    if (!added) s_SelectionBadgeHooks++;
                    added = true;
                }
                var p = badgeCard.ChangeCoordinatesTo(host, new Vector2(8f, -10f));
                if (p.x == lastX && p.y == lastY) return;
                lastX = p.x;
                lastY = p.y;
                badge.style.left = p.x;
                badge.style.top = p.y;
            }

            // Kept as a NAMED delegate (not an inline lambda) so the sweep can unregister this exact closure.
            EventCallback<GeometryChangedEvent> onCardGeometry = _ => Reposition();
            badgeCard.RegisterCallback(onCardGeometry);
            var follow = badge.schedule.Execute(Reposition).Every(16);
            badge.userData = new SelectionBadgeHook
            {
                Card = badgeCard,
                OnCardGeometry = onCardGeometry,
                Follow = follow,
            };
            if (positionNow) Reposition();
        }

        /// <summary>
        /// Re-applies selection chrome (card highlight classes + the floating primary badge) IN PLACE on an
        /// already-built design-mode canvas — the cheap path for a selection-only change. A full RefreshPreview
        /// for a mere click tears down and relayouts the whole canvas, which visibly jolted it (transient scroll
        /// clamp + late multi-pass widget geometry) on EVERY click. Also re-points each card's captured rebuild
        /// context at the new selection so a later live-poke rebuild reproduces the right chrome. Returns false
        /// when only a full rebuild can represent the change — no build root yet, or a newly selected node's card
        /// is missing or display:none (e.g. inside an inactive tab, which the rebuild's tab auto-switch reveals).
        /// </summary>
        internal static bool TryUpdateSelectionChrome(
            VisualElement canvasRoot,
            LayoutNode primary,
            System.Collections.Generic.HashSet<LayoutNode> selectionSet)
        {
            if (canvasRoot == null) return false;
            var buildRoot = canvasRoot.ClassListContains("sasq-build-root")
                ? canvasRoot
                : canvasRoot.Q<VisualElement>(className: "sasq-build-root");
            if (buildRoot == null) return false;

            // Every design-card wrapper carries userData == its LayoutNode (BuildNode keeps that invariant).
            var cards = buildRoot.Query<VisualElement>(className: "designer-card").ToList();

            VisualElement primaryCard = null;
            var unresolvedSelected = selectionSet != null
                ? new System.Collections.Generic.HashSet<LayoutNode>(selectionSet)
                : null;
            var selectedCards = new System.Collections.Generic.List<VisualElement>();

            foreach (var card in cards)
            {
                if (!(card.userData is LayoutNode node)) continue;
                bool isPrimary = node == primary;
                bool isSelected = isPrimary || (selectionSet != null && selectionSet.Contains(node));
                card.EnableInClassList("designer-card-selected", isSelected);
                // A table column's other half. This pass is the CHEAP selection path (no rebuild), so the
                // header must be re-lit here too or clicking between columns leaves the previous column's
                // name highlighted — the link would then be actively misleading rather than merely absent.
                TableColumnLink.HeaderForCard(card)
                    ?.EnableInClassList(TableColumnLink.HeaderSelectedClass, isSelected);
                if (isPrimary) primaryCard = card;
                if (isSelected) selectedCards.Add(card);
                unresolvedSelected?.Remove(node);
                if (_rebuildContexts.TryGetValue(card, out var ctx) && ctx != null)
                {
                    ctx.selectedNode = primary;
                    ctx.selectionSet = selectionSet;
                }
            }

            // A selected node with no visible card needs the full rebuild (fallback is harmless: the rebuild
            // re-derives ALL chrome from scratch, so a half-applied pass here is simply overwritten).
            if (primary != null && primaryCard == null) return false;
            if (unresolvedSelected != null && unresolvedSelected.Count > 0) return false;
            foreach (var card in selectedCards)
            {
                if (IsEffectivelyHidden(card, buildRoot)) return false;
            }

            // Retire the previous badge(s) and float a fresh one over the new primary. The old badge's card is
            // still alive, so the sweep must UNREGISTER its geometry hook as well as remove the element —
            // removing the element alone left one live closure per click on a card the user keeps clicking
            // between, each retaining a dead badge Label. (The resurrect-guard keeps a not-yet-swept hook from
            // re-adding a removed badge; it was never a teardown.)
            SweepSelectionBadges(buildRoot);
            if (primary != null && primaryCard != null)
            {
                AttachSelectionBadge(primaryCard, primary, positionNow: true);
            }
            return true;
        }

        // True when the element or any ancestor up to (exclusive) stopAt is display:none — e.g. content of an
        // inactive tab, whose cards exist but aren't shown.
        private static bool IsEffectivelyHidden(VisualElement ve, VisualElement stopAt)
        {
            for (var p = ve; p != null && p != stopAt; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return true;
            return false;
        }

        private static bool IsInteractiveElement(VisualElement element)
        {
            if (element == null) return false;

            // Allow dragging from any static text elements / labels (even if nested inside fields)
            if (element is Label || element is TextElement)
            {
                return false;
            }

            // Buttons in design mode are treated as static/draggable layout preview elements
            if (element is Toggle || element is TextField || element is FloatField || 
                element is IntegerField || element is DoubleField || element is EnumField || 
                element is ObjectField || element is Slider || element is MinMaxSlider || element is SliderInt)
            {
                return true;
            }
            string name = element.GetType().Name;
            if (name.Contains("Input") || name.Contains("Field") || name.Contains("Toggle") || name.Contains("Slider"))
            {
                return true;
            }
            return IsInteractiveElement(element.parent);
        }

        // Full-width inspector rows should not inherit each widget's native horizontal margins or minimum
        // content width. Apply this before StyleApplier so explicit designer width/margin/alignment overrides
        // still win.
        private static void NormalizeFullRowBinding(VisualElement element)
        {
            if (element == null) return;

            element.AddToClassList("sasq-binding-row");
            element.style.alignSelf = Align.Stretch;
            element.style.minWidth = 0;
            element.style.flexShrink = 1f;
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
        }

        /// <summary>
        /// Loads the bundled default <see cref="SasquatchThemeAsset"/> matching the current editor
        /// skin (DefaultDarkTheme / DefaultLightTheme), or null if the project has no theme assets.
        /// Kept as a public shim over <see cref="SasquatchThemeResolver"/>.
        /// </summary>
        public static SasquatchThemeAsset LoadDefaultTheme()
        {
            return SasquatchThemeResolver.LoadBundledTheme(!EditorGUIUtility.isProSkin);
        }

        // A nicer "Script" header (replaces Unity's greyed-out Script field): a tinted bar with the target's
        // script icon + name. Single click pings the script, double click opens it — the same gesture pair
        // Unity's own read-only Script field answers to, which is why the header carries NO dedicated
        // ping/open buttons: they duplicated the bar's own click and crowded the cluster out (the right-click
        // menu still lists both, for anyone who wants them named). Right-click offers the full action menu;
        // the tooltip shows the asset path.
        // In design mode it's display-only (no ping/open is wired) so a click just selects the widget like any
        // other node. The script is resolved from the hidden m_Script property, so it covers both
        // MonoBehaviours and ScriptableObjects with no property path.
        private static VisualElement BuildScriptHeaderElement(ScriptHeaderNode node, SerializedObject serializedObject,
            bool designMode, Action onRebuildRequired = null)
        {
            var target = serializedObject != null ? serializedObject.targetObject : null;
            MonoScript script = serializedObject?.FindProperty("m_Script")?.objectReferenceValue as MonoScript;

            string scriptName = script != null ? script.GetClass()?.Name : null;
            if (string.IsNullOrEmpty(scriptName)) scriptName = target != null ? target.GetType().Name : "Script";
            string assetPath = script != null ? AssetDatabase.GetAssetPath(script) : null;

            var bar = new VisualElement();
            bar.AddToClassList("sasq-script-header");
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.height = 24;
            // A little top headroom so the header's top edge / rounded corner isn't clipped where the layout body
            // sits tight under the host's native header — worst on a genuine [CustomEditor] force-takeover, where our
            // body is injected inside the competing editor's InspectorElement. (In the designer this margin is moved
            // to the card wrapper and zeroed here, so the canvas is unaffected.)
            bar.style.marginTop = 6;
            bar.style.marginBottom = 6;
            bar.style.paddingLeft = 6;
            bar.style.paddingRight = 4;
            var barIdleWash = WidgetPalette.Ink(0.05f);   // captured: hover callbacks stay on the built skin
            var barHoverWash = WidgetPalette.Ink(0.09f);
            bar.style.backgroundColor = barIdleWash;
            bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius =
                bar.style.borderBottomLeftRadius = bar.style.borderBottomRightRadius = 4;
            bar.style.borderLeftWidth = 2;
            bar.style.borderLeftColor = WidgetPalette.AccentUnity;

            if (!node.HideIcon)
            {
                if (node.IconSource == ScriptHeaderIconSource.BuiltIn && !string.IsNullOrEmpty(node.IconId))
                {
                    // A built-in glyph chosen from the dropdown (same shared icon pipeline as Action Buttons).
                    var glyph = SasquatchIcons.Create(node.IconId, 16f);
                    glyph.style.flexShrink = 0;
                    glyph.style.marginRight = 6;
                    bar.Add(glyph);
                }
                else
                {
                    // Custom-texture source (or Built In with no glyph chosen yet): the assigned texture when in
                    // Custom mode, otherwise the script's own thumbnail. The Icon Source enum keeps the two exclusive.
                    var icon = new Image { scaleMode = ScaleMode.ScaleToFit };
                    icon.style.width = 16;
                    icon.style.height = 16;
                    icon.style.marginRight = 6;
                    icon.image = (node.IconSource == ScriptHeaderIconSource.Custom && node.CustomIcon != null)
                        ? node.CustomIcon
                        : (script != null ? AssetPreview.GetMiniThumbnail(script)
                                          : EditorGUIUtility.IconContent("cs Script Icon").image);
                    bar.Add(icon);
                }
            }

            string resolvedText = node.EffectiveTextOverride ?? scriptName;
            var nameLabel = new Label(resolvedText);
            nameLabel.AddToClassList("sasq-script-header-title");
            nameLabel.style.flexGrow = 1;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = node.ResolveTextAnchor();

            if (node.IsTextHidden)
            {
                nameLabel.style.display = DisplayStyle.None;
            }
            bar.Add(nameLabel);

            if (!string.IsNullOrEmpty(assetPath)) bar.tooltip = assetPath;

            // Inline field-search UI. The magnifier is the SOLE way into the inspector field search (see
            // InspectorFieldSearch) — off (ShowSearch=false) means no button and no search for this layout.
            // Built for both the design-canvas preview and the live inspector but WIRED only when live; the
            // field opens over the name (from just after the icon to the button cluster) and the compact
            // count sits just before the buttons. Ordered here so the cluster reads [🔍] [🐞].
            TextField searchField = null;
            Label searchCount = null;
            Button searchBtn = null;
            if (node.ShowSearch)
            {
                searchField = new TextField { name = InspectorFieldSearch.FieldName };
                searchField.textEdition.placeholder = "Search…";
                searchField.style.display = DisplayStyle.None; // the magnifier reveals it
                searchField.style.flexGrow = 1;
                searchField.style.flexShrink = 1;
                searchField.style.minWidth = 0;
                searchField.style.height = 18;
                searchField.style.marginTop = 0;
                searchField.style.marginBottom = 0;
                searchField.style.marginLeft = 0;
                searchField.style.marginRight = 4;
                searchField.style.fontSize = 11;
                // A click in the field must not bubble to the bar's ping/open click handler below.
                searchField.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                bar.Add(searchField);

                searchCount = new Label { name = InspectorFieldSearch.CountName };
                searchCount.style.display = DisplayStyle.None; // filled in while a query is active
                searchCount.style.flexShrink = 0;
                searchCount.style.marginLeft = 2;
                searchCount.style.marginRight = 2;
                searchCount.style.fontSize = 10;
                searchCount.style.color = WidgetPalette.TextGray;
                searchCount.style.unityTextAlign = TextAnchor.MiddleRight;
                searchCount.pickingMode = PickingMode.Ignore;
                bar.Add(searchCount);

                searchBtn = MakeScriptHeaderIconButton(SasquatchIcons.Ids.Search, "Search fields", null);
                bar.Add(searchBtn);
            }

            // Live Debug toggle. Opt-in per layout (right-click the header ▸ "Show Sasquatch Debug Toggle"), so
            // it is absent from every inspector that hasn't asked for it. With ping/open gone the cluster is
            // just the two MODE buttons, [🔍] [🐞].
            // Unlike its neighbour it does NOT hide when the bar is unhovered while recording: an inspector that
            // is capturing data has to look like one, or the strips read as a rendering bug.
            Button debugBtn = null;
            Type debugTargetType = FirstTargetType(serializedObject);
            if (node.ShowDebugToggle)
            {
                debugBtn = MakeScriptHeaderIconButton(SasquatchIcons.Ids.Bug, "Live Debug — record this script's values in Play Mode", null);
                bar.Add(debugBtn);
            }

            void Ping() { if (script != null) EditorGUIUtility.PingObject(script); }
            void Open()
            {
                if (script == null) return;
                // Scripts open straight in the external script editor rather than through
                // AssetDatabase.OpenAsset: that pipeline validates every [OnOpenAsset] handler in
                // the project on each call, so a mismatched third-party package (e.g. a Timeline
                // version newer than the editor patch) logs ITS signature error under OUR stack
                // trace. Going direct sidesteps other packages' registrations entirely.
                if (!string.IsNullOrEmpty(assetPath))
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(assetPath, 1);
                else
                    AssetDatabase.OpenAsset(script);
            }

            // NOTE: there are deliberately no ping/open icon-buttons here. The bar itself answers to
            // click-to-ping and double-click-to-open — exactly what Unity's read-only Script field does — so
            // dedicated buttons only restated a gesture the user already has, and they crowded the cluster.
            // Both actions remain NAMED in the right-click menu below for discoverability.

            if (designMode || script == null)
            {
                // Display-only: don't wire ping/open (in the designer a click selects the node; with no script
                // there's nothing to act on). Show the mode buttons faintly so the widget still reads the same.
                if (searchBtn != null)
                {
                    searchBtn.SetEnabled(false);
                    searchBtn.style.opacity = 0.35f;
                }
                if (debugBtn != null)
                {
                    debugBtn.SetEnabled(false);
                    debugBtn.style.opacity = 0.35f;
                }
                return bar;
            }

            // Live inspector: hover reveals the buttons and brightens the bar; click pings, double-click opens.
            // While the search field is open the bar carries InspectorFieldSearch.SearchingClass, and the reveal
            // stays on regardless of hover so the field never sits in a "dead"-looking header.
            if (searchBtn != null) searchBtn.style.opacity = 0f;
            if (debugBtn != null) debugBtn.style.opacity = 0f;

            bool barHovered = false;
            void ApplyHeaderChrome()
            {
                bool searching = bar.ClassListContains(InspectorFieldSearch.SearchingClass);
                bool recording = debugBtn != null && LiveDebug.DebugModeState.IsOn(debugTargetType);
                bool active = barHovered || searching || recording;
                float op = active ? 1f : 0f;
                if (searchBtn != null) searchBtn.style.opacity = op;
                // The bug glyph is the one button that stays lit on its own: while recording it is a STATUS
                // light, not an affordance, so it must survive the pointer leaving the bar.
                if (debugBtn != null) debugBtn.style.opacity = recording ? 1f : op;
                bar.style.backgroundColor = active ? barHoverWash : barIdleWash;
                // The magnifier goes accent blue while its field is open — the shared "search is on" tell,
                // matched on the designer's Properties + Canvas magnifiers.
                SearchActiveOverlay.TintSearchToggle(searchBtn, searching,
                    WidgetPalette.Accent, SasquatchIcons.DefaultTint);
                // Same tell, same helper, for "debug is recording".
                SearchActiveOverlay.TintSearchToggle(debugBtn, recording,
                    WidgetPalette.Accent, SasquatchIcons.DefaultTint);
            }

            if (debugBtn != null)
            {
                debugBtn.clicked += () =>
                {
                    LiveDebug.DebugModeState.Toggle(debugTargetType);
                    ApplyHeaderChrome();
                };
                // Debug can also be flipped from another inspector showing the same type, so follow the state
                // rather than only writing it. Unhooked through PanelLifetime — a plain DetachFromPanelEvent
                // would drop the handler on a dock-tab drag and leave the chrome frozen (see detach-is-not-teardown).
                Action onDebugChanged = ApplyHeaderChrome;
                LiveDebug.DebugModeState.Changed += onDebugChanged;
                PanelLifetime.OnRealDetach(bar, () => LiveDebug.DebugModeState.Changed -= onDebugChanged);
            }
            bar.RegisterCallback<MouseEnterEvent>(_ =>
            {
                barHovered = true;
                // BEFORE the chrome, and before any click can land. The debug key lives in SessionState,
                // which an EXPORTED [CustomEditor]'s baked Live Debug twin writes under the same name on
                // purpose — but it raises its own event, not ours, so DebugRunner's cached arm flag drifts
                // and the next click on this glyph is swallowed whole ("nothing happened", then it works on
                // the second press). Hover is the one thing that always precedes a click on a glyph that is
                // only revealed BY hover, so it is where the drift gets corrected. No-op when in sync.
                if (debugBtn != null) LiveDebug.DebugModeState.Resync(debugTargetType);
                ApplyHeaderChrome();
            });
            bar.RegisterCallback<MouseLeaveEvent>(_ => { barHovered = false; ApplyHeaderChrome(); });

            bar.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2) Open();
                else Ping();
            });

            // Publish the inline-search handles so InspectorFieldSearch.Attach can bind them to the filter
            // engine (RefreshChrome re-applies the reveal above, keeping the wash colours private here).
            if (searchBtn != null)
            {
                InspectorFieldSearch.RegisterHeaderSearch(bar, new InspectorFieldSearch.HeaderSearchHandles
                {
                    Bar = bar,
                    ToggleButton = searchBtn,
                    Field = searchField,
                    Count = searchCount,
                    NameLabel = nameLabel,
                    NameHiddenByNode = node.IsTextHidden,
                    RefreshChrome = ApplyHeaderChrome,
                });
            }

            // Live-only (design mode returned above). Entries surface in the unified right-click
            // menu via the contributor registry — see QuickMenuController.
            QuickMenuContributors.Register(bar, (request, menu) =>
            {
                menu.Add("Open Script", Open);
                menu.Add("Ping in Project", Ping);
                menu.AddSeparator();
                menu.Add("Copy Type Name", () => EditorGUIUtility.systemCopyBuffer = scriptName);
                menu.Add("Copy Asset Path", () => EditorGUIUtility.systemCopyBuffer = assetPath ?? "",
                    enabled: !string.IsNullOrEmpty(assetPath));
                // NOTE: Live Debug is enabled from the Layout Designer's Script Header panel and the quick-edit
                // popup, NOT from here. A menu entry was tried and pulled: it landed last in a menu that already
                // stacks Unity's native items and the chain entries above it, so it was effectively unfindable.
            });

            return bar;
        }

        // A pure spacer node — blank space in the layout. Live inspector: an invisible, non-interactive gap
        // (a fixed px size on the chosen axis, or flex-grow when Flexible). Design mode: a subtle, selectable
        // placeholder with a size label and an edge grip so it can be dragged to resize. Kept a plain leaf so
        // its size is applied here as a literal fixed height/width — StyleApplier only routes ContainerNode /
        // MakeHeightResizable nodes to a grow-only min-height, which a fixed gap must not be.
        private static VisualElement BuildSpaceElement(SpaceNode node, bool designMode)
        {
            bool horizontal = node.Axis == SpaceAxis.Horizontal;
            float amount = Mathf.Clamp(node.SpaceAmount, SpaceNode.MinSpace, SpaceNode.MaxSpace);

            if (!designMode)
            {
                // Live inspector: just empty space, but PICKABLE (not Ignore) so a right-click over the gap resolves
                // to this Space node and the unified menu can offer "Edit Space…" — the only way to tweak the height
                // on the fly. A left-click does nothing in a gap (there's no field there), so a pickable gap can't
                // steal a real field's click; and flow siblings never overlap, so it can't cover them either.
                // (Mirrors the Image Banner leaf, whose root is likewise pickable for the same right-click reason.)
                var live = new VisualElement();
                live.pickingMode = PickingMode.Position;
                if (node.Flexible)
                {
                    // Grow to fill the parent's leftover main-axis room (vertical in a Rows container,
                    // horizontal in a Columns row), pushing siblings apart.
                    live.style.flexGrow = 1f;
                }
                else
                {
                    live.style.flexShrink = 0f; // a fixed gap must never be squished away when space is tight
                    if (horizontal) live.style.width = amount;
                    else live.style.height = amount;
                }
                return live;
            }

            // ---- Design-mode placeholder: visible + selectable so it can be picked, moved and resized ----
            // The resizable area is a NESTED element (fill) inside the node root, mirroring the Curve/Gradient
            // widgets — dragging a nested element updates live on the design canvas, whereas resizing the node
            // root's own height/width doesn't reflect until the next rebuild. box auto-sizes to fill.
            var box = new VisualElement();
            box.style.position = Position.Relative; // containing block for the absolutely-positioned grip
            box.style.flexShrink = 0f;

            var fill = new VisualElement();
            fill.style.alignItems = Align.Center;
            fill.style.justifyContent = Justify.Center;
            fill.style.backgroundColor = WidgetPalette.AccentSoft;
            fill.style.borderTopLeftRadius = fill.style.borderTopRightRadius =
                fill.style.borderBottomLeftRadius = fill.style.borderBottomRightRadius = 3;
            fill.style.borderTopWidth = fill.style.borderBottomWidth =
                fill.style.borderLeftWidth = fill.style.borderRightWidth = 1;
            var edge = WidgetPalette.AccentEdge;
            fill.style.borderTopColor = fill.style.borderBottomColor =
                fill.style.borderLeftColor = fill.style.borderRightColor = edge;
            box.Add(fill);

            var label = new Label();
            label.pickingMode = PickingMode.Ignore;
            label.style.fontSize = 10;
            label.style.color = WidgetPalette.AccentUnity;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            fill.Add(label);

            if (node.Flexible)
            {
                // Don't actually flex-grow in the (vertical) design canvas — it would balloon to fill the whole
                // window and bury everything below. Show a modest fixed footprint that reads as "flexible".
                box.tooltip = "Flexible space — fills leftover room in the live inspector.";
                label.text = "Flexible";
                if (horizontal) { fill.style.width = 90f; fill.style.height = 22f; }
                else { fill.style.height = 22f; }
                return box;
            }

            // Fixed size: render on the chosen axis, but keep a minimum grabbable footprint so a tiny gap stays
            // selectable. The label always shows the true value.
            const float minVisible = 14f;
            box.tooltip = (horizontal ? "Horizontal" : "Vertical") + " space — drag the grip or set the pixels in the panel.";
            label.text = Mathf.RoundToInt(amount) + " px";
            if (horizontal)
            {
                fill.style.width = Mathf.Max(amount, minVisible);
                fill.style.height = 22f;
                fill.style.paddingRight = 8;
            }
            else
            {
                fill.style.height = Mathf.Max(amount, minVisible);
                fill.style.paddingBottom = 8;
            }

            // Edge grip. The full-edge strip is click-THROUGH (pickingMode Ignore); only the small centered
            // GRAB zone is interactive, so clicking anywhere else along the edge SELECTS the Space widget
            // instead of starting a resize. Still draggable in design select-only mode (grab is keep-interactive).
            var strip = new VisualElement();
            strip.pickingMode = PickingMode.Ignore; // positioning container only — never eats a selection click
            strip.style.position = Position.Absolute;
            strip.style.alignItems = Align.Center;
            strip.style.justifyContent = Justify.Center;

            var grab = new VisualElement();
            grab.AddToClassList(DesignKeepInteractiveClass);
            grab.AddToClassList(horizontal ? "sasq-col-resizer" : "sasq-row-resizer");
            grab.style.alignItems = Align.Center;
            grab.style.justifyContent = Justify.Center;

            var pill = new VisualElement();
            pill.pickingMode = PickingMode.Ignore;
            pill.style.borderTopLeftRadius = pill.style.borderTopRightRadius =
                pill.style.borderBottomLeftRadius = pill.style.borderBottomRightRadius = 2;
            var pillIdle = WidgetPalette.PillIdle;
            var pillHot = WidgetPalette.PillHot;
            pill.style.backgroundColor = pillIdle;

            if (horizontal)
            {
                strip.style.right = 0; strip.style.top = 0; strip.style.bottom = 0;
                strip.style.width = 12;
                grab.style.width = 12; grab.style.height = 40; // comfortable hit target around the pill
                pill.style.width = 3; pill.style.height = 20;
            }
            else
            {
                strip.style.bottom = 0; strip.style.left = 0; strip.style.right = 0;
                strip.style.height = 12;
                grab.style.height = 12; grab.style.width = 40; // comfortable hit target around the pill
                pill.style.height = 3; pill.style.width = 22;
            }
            grab.Add(pill);
            strip.Add(grab);
            grab.RegisterCallback<PointerEnterEvent>(_ => pill.style.backgroundColor = pillHot);
            grab.RegisterCallback<PointerLeaveEvent>(_ => pill.style.backgroundColor = pillIdle);
            AttachSpaceResize(grab, fill, node, horizontal, label);
            box.Add(strip);
            return box;
        }

        // Drag-to-resize for a Space placeholder, mirroring AttachCurveResize but axis-aware: it writes a real
        // clamped px value to node.SpaceAmount (a literal fixed size, never a grow-only floor) and commits once
        // on release through CurrentOnLayoutEdited — no inspector rebuild.
        private static void AttachSpaceResize(VisualElement handle, VisualElement target, SpaceNode node,
            bool horizontal, Label label)
        {
            var onEdited = CurrentOnLayoutEdited;
            const float minVisible = 14f;
            bool resizing = false;
            float start = 0f, startSize = 0f;

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                start = horizontal ? evt.position.x : evt.position.y;
                startSize = node.SpaceAmount;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                float cur = horizontal ? evt.position.x : evt.position.y;
                float raw = Mathf.Clamp(startSize + (cur - start), 2f, SpaceNode.MaxSpace);
                node.SpaceAmount = raw;
                if (horizontal) target.style.width = Mathf.Max(raw, minVisible);
                else target.style.height = Mathf.Max(raw, minVisible);
                if (label != null) label.text = Mathf.RoundToInt(raw) + " px";
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (handle.HasPointerCapture(evt.pointerId)) handle.ReleasePointer(evt.pointerId);
                onEdited?.Invoke();
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!resizing) return;
                resizing = false;
                onEdited?.Invoke(); // capture-loss safety net so a mid-drag value is never lost
            });
        }

        // ----- Decorative leaf: LabeledDivider (a thin rule with an optional centered caption) -----
        private static VisualElement BuildLabeledDividerElement(LabeledDividerNode node, bool designMode)
        {
            float thickness = Mathf.Clamp(node.Thickness, LabeledDividerNode.MinThickness, LabeledDividerNode.MaxThickness);
            float radius = Mathf.Clamp(node.CornerRadius, 0f, LabeledDividerNode.MaxCornerRadius);
            Color line = node.LineColor.Resolve(WidgetPalette.BorderSoft);

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.flexShrink = 0f;
            // The root stays PICKABLE in BOTH modes. The rule + caption children below are pickingMode-Ignore, so a
            // right-click on the visible line/label falls THROUGH them to this stamped root, letting the unified menu
            // offer "Edit Divider…". (Make the root Ignore and every pixel of the divider falls through to the
            // parent container instead, leaving only "Edit Root" in the menu.) A bare rule is only ~1-2px
            // tall — a near-impossible right-click target — so in the live inspector we swap 4px of the 6px top/bottom
            // MARGIN for equal PADDING: the total footprint and the rule's centered position stay pixel-identical, but
            // the pickable border-box grows from ~2px to ~10px (and, sitting inside the divider's own reserved height,
            // it still can't eat a neighbouring field's click). Design mode keeps the plain 6px margins — its selection
            // card is built from the transferred margins by the common stamping block.
            if (!designMode)
            {
                root.style.marginTop = 2;
                root.style.marginBottom = 2;
                root.style.paddingTop = 4;
                root.style.paddingBottom = 4;
            }
            else
            {
                root.style.marginTop = 6;
                root.style.marginBottom = 6;
            }

            VisualElement MakeRule()
            {
                var r = new VisualElement { pickingMode = PickingMode.Ignore };
                r.style.flexGrow = 1f;
                r.style.height = thickness;
                r.style.backgroundColor = line;
                if (radius > 0f)
                {
                    r.style.borderTopLeftRadius = radius;
                    r.style.borderTopRightRadius = radius;
                    r.style.borderBottomLeftRadius = radius;
                    r.style.borderBottomRightRadius = radius;
                }
                return r;
            }

            if (string.IsNullOrEmpty(node.Title))
            {
                root.Add(MakeRule());
            }
            else
            {
                var label = new Label(node.Title) { pickingMode = PickingMode.Ignore };
                // Shrink + wrap: a long caption must WRAP within the row, never expand it (an unshrinkable label
                // pushed the row past the inspector width → a horizontal scrollbar on the whole inspector). A
                // short caption has nothing to shrink, so it stays content-sized and its placement is unchanged.
                label.style.flexShrink = 1f;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = WidgetPalette.LabelSecondary;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                // The "T" caption styling (bold/italic, size, color, align) overrides the defaults above.
                TextStyleApplier.Apply(label, node.TitleTextStyle);

                // The caption's horizontal alignment (from the T panel) decides WHERE it sits along the rule:
                // a single-line caption is content-sized so it can't visibly text-align — we PLACE it left /
                // center / right instead — while a WRAPPED caption aligns its own lines to match that placement.
                var anchor = node.TitleTextStyle.align.isOverridden ? node.TitleTextStyle.align.value : TextAnchor.MiddleCenter;
                bool left = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft;
                bool right = anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight;
                // Line up wrapped lines with the placement (a T-panel align already set unityTextAlign above).
                if (!node.TitleTextStyle.align.isOverridden) label.style.unityTextAlign = anchor;

                if (left)
                {
                    label.style.marginLeft = 0;
                    label.style.marginRight = 8;
                    root.Add(label);
                    root.Add(MakeRule());
                }
                else if (right)
                {
                    label.style.marginLeft = 8;
                    label.style.marginRight = 0;
                    root.Add(MakeRule());
                    root.Add(label);
                }
                else
                {
                    label.style.marginLeft = 8;
                    label.style.marginRight = 8;
                    root.Add(MakeRule());
                    root.Add(label);
                    root.Add(MakeRule());
                }
            }
            return root;
        }

        // ----- Decorative leaf: ImageBanner (a texture rendered at a fixed height, GUID-referenced) -----
        private static VisualElement BuildImageBannerElement(ImageBannerNode node, bool designMode)
        {
            float height = Mathf.Clamp(node.Height, ImageBannerNode.MinHeight, ImageBannerNode.MaxHeight);

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.flexShrink = 0f;
            root.style.marginTop = 4;
            root.style.marginBottom = 4;
            // The banner box ALWAYS clips, whatever the current zoom. Zoom is a center-origin transform on the
            // image, and a transform is invisible to layout — an unclipped root lets a zoomed image paint clean
            // over the widgets above and below it. Never gate this on `zoom != 1`: a banner built at zoom 1
            // would have no clip for the designer's zoom slider to poke into, and LivePokeCanvasElement writes
            // `img.style.scale` on the ALREADY-BUILT element — so the very FIRST zoom drag after assigning a
            // texture would escape the card entirely until the debounced commit's rebuild reeled it back in.
            // Free at zoom 1 — nothing overflows there (Fit caps at maxWidth 100%, Stretch/Crop flex to fit).
            root.style.overflow = Overflow.Hidden;
            switch (node.Alignment)
            {
                case ImageBannerAlignment.Left: root.style.justifyContent = Justify.FlexStart; break;
                case ImageBannerAlignment.Right: root.style.justifyContent = Justify.FlexEnd; break;
                default: root.style.justifyContent = Justify.Center; break;
            }

            Texture tex = null;
            if (!string.IsNullOrEmpty(node.TextureGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(node.TextureGuid);
                if (!string.IsNullOrEmpty(path))
                {
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex == null)
                    {
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (sprite != null) tex = sprite.texture;
                    }
                }
            }

            if (tex != null)
            {
                var img = new Image { image = tex };
                img.scaleMode = node.ScaleMode == ImageBannerScaleMode.ScaleAndCrop ? ScaleMode.ScaleAndCrop
                              : node.ScaleMode == ImageBannerScaleMode.StretchToFill ? ScaleMode.StretchToFill
                              : ScaleMode.ScaleToFit;
                img.style.height = height;

                // Aspect for the ScaleToFit intrinsic width — tightened to the opaque bounding box when Crop
                // Alpha is on, so transparent margins don't pad the banner.
                float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1f;
                if (node.ScaleMode == ImageBannerScaleMode.ScaleToFit && node.CropAlpha
                    && TryGetOpaqueSourceRect(tex, node.TextureGuid, out var srcPx) && srcPx.height > 0f)
                {
                    img.sourceRect = srcPx;
                    aspect = srcPx.width / srcPx.height;
                }

                if (node.ScaleMode == ImageBannerScaleMode.ScaleToFit && tex.height > 0)
                {
                    // Intrinsic width at this height so Alignment (Left/Center/Right) is meaningful.
                    img.style.width = height * aspect;
                    img.style.maxWidth = Length.Percent(100);
                }
                else
                {
                    img.style.flexGrow = 1f; // Crop / Stretch fill the full width (alignment is moot)
                }

                // Uniform zoom on top of the scale mode: a center-origin transform, with the row (clipped above,
                // unconditionally) cropping the overflow so zoom > 1 reads as enlarge-and-crop within the box.
                float zoom = Mathf.Clamp(node.Zoom, ImageBannerNode.MinZoom, ImageBannerNode.MaxZoom);
                if (!Mathf.Approximately(zoom, 1f))
                    img.style.scale = new Scale(new Vector2(zoom, zoom));

                if (!designMode) img.pickingMode = PickingMode.Ignore;
                root.Add(img);
            }
            else
            {
                // Empty placeholder — a hint in design mode, a blank reserved block live.
                var ph = new Label(designMode ? "Image — pick a texture" : "");
                ph.style.flexGrow = 1f;
                ph.style.height = height;
                ph.style.unityTextAlign = TextAnchor.MiddleCenter;
                ph.style.color = WidgetPalette.DropSlotText;
                ph.style.backgroundColor = WidgetPalette.PanelWash;
                ph.style.borderTopLeftRadius = ph.style.borderTopRightRadius =
                    ph.style.borderBottomLeftRadius = ph.style.borderBottomRightRadius = 4;
                if (!designMode) ph.pickingMode = PickingMode.Ignore;
                root.Add(ph);
            }
            return root;
        }

        // Cache of a texture's opaque (alpha > threshold) bounding box in SOURCE PIXELS (Image.sourceRect space,
        // top-left origin), keyed by asset GUID. Static → reset on domain reload. Lets Crop Alpha avoid re-reading
        // pixels on every rebuild. A re-imported texture keeps its GUID; the rare stale bound is harmless for a
        // decorative banner and clears on the next reload. An empty (width 0) entry means "fully transparent".
        private static readonly Dictionary<string, Rect> _imageBannerAlphaBounds = new Dictionary<string, Rect>();

        /// <summary>
        /// Computes the opaque bounding box of <paramref name="tex"/> as an <c>Image.sourceRect</c> (source pixels,
        /// top-left origin). Blits through a temp RenderTexture so NON-readable textures work too. Returns false
        /// (and leaves <paramref name="sourceRectPx"/> empty) when the texture is fully transparent or unreadable.
        /// Internal so the standalone export can BAKE the same rect into the generated file (the exported editor
        /// is package-free and can't rerun this) — the BakeOverlayRamp precedent.
        /// </summary>
        internal static bool TryGetOpaqueSourceRect(Texture tex, string guid, out Rect sourceRectPx)
        {
            sourceRectPx = default;
            if (tex == null) return false;
            if (!string.IsNullOrEmpty(guid) && _imageBannerAlphaBounds.TryGetValue(guid, out sourceRectPx))
                return sourceRectPx.width > 0f;

            int w = tex.width, h = tex.height;
            if (w <= 0 || h <= 0) return false;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply(false);
                var px = readable.GetPixels32();

                const byte thresh = 8;
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[rowBase + x].a > thresh)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    if (!string.IsNullOrEmpty(guid)) _imageBannerAlphaBounds[guid] = default; // fully transparent
                    return false;
                }

                float bw = maxX - minX + 1;
                float bh = maxY - minY + 1;
                // GetPixels32 rows are BOTTOM-up (row 0 = bottom); Image.sourceRect uses a TOP-left origin, so
                // flip Y when converting the pixel bounds.
                float topLeftY = h - (maxY + 1);
                sourceRectPx = new Rect(minX, topLeftY, bw, bh);
                if (!string.IsNullOrEmpty(guid)) _imageBannerAlphaBounds[guid] = sourceRectPx;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        private static VisualElement BuildTextBlockElement(TextBlockNode node, bool designMode)
        {
            var root = new VisualElement();
            root.style.flexShrink = 0f;
            root.style.marginTop = 4;
            root.style.marginBottom = 4;
            // Decorative in the live inspector — the root ignores picking so empty margins never eat a field
            // click; the inner text label stays pickable so markdown links still work.
            if (!designMode) root.pickingMode = PickingMode.Ignore;

            // Optional card background + padding + corner rounding (only when a background color is set).
            if (node.BackgroundColor.isOverridden)
            {
                root.style.backgroundColor = node.BackgroundColor.value;
                float pad = Mathf.Max(0f, node.Padding);
                root.style.paddingLeft = pad; root.style.paddingRight = pad;
                root.style.paddingTop = pad; root.style.paddingBottom = pad;
                float r = Mathf.Clamp(node.CornerRadius, 0f, TextBlockNode.MaxCornerRadius);
                if (r > 0f)
                {
                    root.style.borderTopLeftRadius = r; root.style.borderTopRightRadius = r;
                    root.style.borderBottomLeftRadius = r; root.style.borderBottomRightRadius = r;
                }
            }
            if (node.MaxWidth > 0f) root.style.maxWidth = node.MaxWidth;

            string text = node.Text ?? "";
            VisualElement content;
            if (node.RenderMarkdown && SasquatchMarkdown.HasFencedCode(text))
            {
                // Fenced ``` code needs the block builder (text labels + monospace code panels).
                content = SasquatchMarkdown.BuildMarkdownElement(text);
            }
            else
            {
                var label = new Label(text);
                label.style.whiteSpace = WhiteSpace.Normal; // wrap
                TextStyleApplier.Apply(label, node.TextStyle);
                if (node.RenderMarkdown && !string.IsNullOrEmpty(text))
                    SasquatchMarkdown.ApplyMarkdownToLabel(label, text);
                content = label;
            }
            root.Add(content);
            return root;
        }

        // A compact glyph icon-button for the script header (the search magnifier and the Live Debug bug).
        // Stops its own click from bubbling to the bar so clicking one doesn't also fire the bar's
        // single-click ping.
        private static Button MakeScriptHeaderIconButton(string iconId, string tooltip, Action onClick)
        {
            var btn = new Button(onClick);
            btn.Add(SasquatchIcons.Create(iconId, 11));
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.tooltip = tooltip;
            btn.style.width = 20;
            btn.style.height = 18;
            btn.style.marginLeft = 2;
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            return btn;
        }

        // The type button methods resolve against — the first live target (matches ButtonInvoker's own
        // SerializedObject overload). Null when there is nothing to verify a method name against.
        private static Type FirstTargetType(SerializedObject serializedObject)
        {
            if (serializedObject == null) return null;
            foreach (var t in serializedObject.targetObjects)
                if (t != null) return t.GetType();
            return null;
        }

        private static VisualElement BuildButtonElement(ButtonNode node, SerializedObject serializedObject,
            InspectorLayoutAsset layoutAsset, Action onRebuildRequired)
        {
            var button = new Button();
            // A marker on EVERY button a ButtonNode renders, Classic included (which carries none of the
            // preset classes) — it's how a designer live poke finds the button inside a design card without
            // guessing at the first Button in the subtree. Purely a handle: no stylesheet rule reads it.
            button.AddToClassList(ButtonWidgetClass);
            string baseText = string.IsNullOrEmpty(node.ButtonText) ? "Execute Action" : node.ButtonText;

            // An inner icon+label row exists only when an icon is set or the Text Backdrop needs content to
            // sit on its pill — a bare button keeps Unity's own text rendering, so Classic with neither stays
            // pixel-identical to the pre-preset button. SetText targets whichever holds the text (the
            // Running… contract below).
            Label innerLabel = null;
            bool hasIcon = !string.IsNullOrEmpty(node.IconId);
            if (hasIcon || node.TextBackdrop)
            {
                button.text = "";
                var row = new VisualElement();
                row.AddToClassList("sasq-button__row");
                row.pickingMode = PickingMode.Ignore;

                innerLabel = new Label(baseText);
                innerLabel.AddToClassList("sasq-button__label");
                innerLabel.pickingMode = PickingMode.Ignore;

                // Text Backdrop: the row stays the full-width centering host and a hugging pill rides inside
                // it, holding BOTH the label and the icon — the same host+pill split the progress bar's
                // readout uses. The pill takes over as the content parent, so icon placement (including the
                // Top/Bottom column flip) applies inside it.
                var content = row;
                if (node.TextBackdrop)
                {
                    var pill = new VisualElement();
                    pill.AddToClassList("sasq-button__backdrop");
                    pill.pickingMode = PickingMode.Ignore;
                    row.Add(pill);
                    content = pill;
                    innerLabel.AddToClassList("sasq-button__label--boxed");
                }

                if (hasIcon)
                {
                    var icon = SasquatchIcons.Create(node.IconId, ButtonIconSize(node), ResolveButtonInkColor(node));
                    icon.AddToClassList("sasq-button__icon");
                    switch (node.IconPlacement)
                    {
                        case ButtonIconPlacement.Right:
                            icon.AddToClassList("sasq-button__icon--right");
                            content.Add(innerLabel);
                            content.Add(icon);
                            break;
                        case ButtonIconPlacement.Top:
                            content.style.flexDirection = FlexDirection.Column;
                            icon.style.marginBottom = 2;
                            content.Add(icon);
                            content.Add(innerLabel);
                            break;
                        case ButtonIconPlacement.Bottom:
                            content.style.flexDirection = FlexDirection.Column;
                            icon.style.marginTop = 2;
                            content.Add(innerLabel);
                            content.Add(icon);
                            break;
                        default: // Left
                            content.Add(icon);
                            content.Add(innerLabel);
                            break;
                    }
                }
                else
                {
                    content.Add(innerLabel);
                }
                button.Add(row);
            }
            else
            {
                button.text = baseText;
            }
            void SetText(string t)
            {
                if (innerLabel != null) innerLabel.text = t;
                else button.text = t;
            }

            ApplyButtonChrome(button, node);

            // Missing-method affordance: the bound method no longer resolves (deleted, renamed past the
            // signature self-heal, or it gained parameters the prompt can't draw — the heal already ran
            // this rebuild, so anything still missing is genuinely unresolvable). Surfaced at build time so
            // the user learns BEFORE the click-time console error. Quotes, not angle brackets, around the
            // name — editor tooltips parse rich text and would eat a bracketed token.
            var warnType = FirstTargetType(serializedObject);
            bool methodMissing = warnType != null && !string.IsNullOrEmpty(node.MethodName) &&
                !EditorApplication.isCompiling &&
                ButtonInvoker.ResolveMethod(warnType, node.MethodName) == null;
            if (methodMissing)
            {
                button.tooltip = ButtonInvoker.HasMethodNamed(warnType, node.MethodName)
                    ? $"Method '{node.MethodName}' exists on {warnType.Name} but has parameters the button can't prompt for."
                    : $"Method '{node.MethodName}' was not found on {warnType.Name} — it may have been renamed or removed.";
                var warnIcon = SasquatchIcons.Create(SasquatchIcons.Ids.TriangleAlert, 11, DesignerPalette.ToolOrangeIcon);
                warnIcon.AddToClassList("sasq-button__warn");
                warnIcon.style.position = Position.Absolute;
                warnIcon.style.right = 4;
                warnIcon.style.top = Length.Percent(50);
                warnIcon.style.translate = new Translate(0, Length.Percent(-50));
                button.Add(warnIcon);
            }

            button.clicked += () =>
            {
                if (string.IsNullOrEmpty(node.MethodName))
                {
                    Debug.LogWarning("[Sasquatch Inspector] Button clicked, but Method Name is empty.");
                    return;
                }

                // Final step: optional confirm gate, then invoke with the collected args and a running state.
                Action<object[]> runWithArgs = args =>
                {
                    Action invoke = () => ButtonInvoker.InvokeButtonMethod(node, serializedObject, args,
                        onStarted: () => { button.SetEnabled(false); SetText("Running…"); },
                        onFinished: () => { button.SetEnabled(true); SetText(baseText); },
                        // The method mutated the object behind the inspector's back: drive every
                        // change-driven widget in this build (collections' external-size sync, conditions,
                        // validation badges, readouts) off the re-read data. Localized to what this build
                        // rendered — never a whole-inspector teardown.
                        onMutated: () => TrackedRefresh.RefreshSubtree(FindBuildRoot(button) ?? button.panel?.visualTree));

                    if (node.ConfirmBeforeExecute)
                    {
                        // Callback-based, not a blocking modal: the confirm gate is a house UITK dialog matching
                        // the argument prompt (both can fire from this one click), and it runs `invoke` itself.
                        ButtonConfirmWindow.Show(
                            string.IsNullOrEmpty(node.ConfirmTitle) ? "Confirm" : node.ConfirmTitle,
                            string.IsNullOrEmpty(node.ConfirmMessage)
                                ? "Are you sure you want to do this?"
                                : node.ConfirmMessage,
                            invoke);
                        return;
                    }

                    invoke();
                };

                // If the method takes parameters, prompt for them first; otherwise run straight away.
                var method = ButtonInvoker.ResolveMethod(serializedObject, node.MethodName);
                var parameters = method != null ? method.GetParameters() : null;

                if (parameters != null && parameters.Length > 0)
                {
                    var initial = ButtonInvoker.GetInitialArgs(node, parameters);
                    ButtonArgumentWindow.Show(baseText, node, parameters, initial, runWithArgs);
                }
                else
                {
                    runWithArgs(null);
                }
            };

            // Inline fix row — LIVE inspector only (the designer's own Method Name field covers authoring,
            // and while a designer holds this type its stale clone would clobber an inspector-side edit on
            // Save, the same reason the heal suppresses itself there). An unresolvable button gets a relink
            // picker right under it — the same autocomplete list every method picker uses — plus a remove
            // control. The inspector is where buttons get USED, so the repair affordance lives where the
            // breakage is discovered; a relink stamps a fresh heal fingerprint, persists through the host's
            // layout-edit callback, and rebuilds in place (which retires the row).
            if (methodMissing && !_designMode && !SasquatchLayoutDesignerWindow.IsEditingType(warnType))
            {
                var persistEdit = _onLayoutEdited; // capture at build time — the static is restored when Build returns
                var wrap = new VisualElement();
                wrap.AddToClassList("sasq-button__fix-wrap");
                wrap.Add(button);

                var fixRow = new VisualElement();
                fixRow.AddToClassList("sasq-button__fix-row");
                fixRow.style.flexDirection = FlexDirection.Row;
                fixRow.style.alignItems = Align.Center;
                fixRow.style.marginTop = 2;

                var fixIcon = SasquatchIcons.Create(SasquatchIcons.Ids.TriangleAlert, 11, DesignerPalette.ToolOrangeIcon);
                fixIcon.style.marginRight = 4;
                fixIcon.style.flexShrink = 0;
                fixRow.Add(fixIcon);

                var relinkField = new TextField { value = node.MethodName };
                relinkField.tooltip = "Type or pick the method this button should run.";
                relinkField.style.flexGrow = 1;
                relinkField.style.flexShrink = 1;
                relinkField.style.flexBasis = 0;
                relinkField.style.minWidth = 0;

                void TryRelink(string methodName)
                {
                    if (string.IsNullOrEmpty(methodName) || methodName == node.MethodName) return;
                    if (ButtonInvoker.ResolveMethod(warnType, methodName) == null) return;
                    LayoutRegistry.BeginLayoutEditUndo(layoutAsset, "Relink Button Method");
                    node.MethodName = methodName;
                    node.MethodSignature = ButtonMethodSync.ComputeSignature(warnType, methodName);
                    persistEdit?.Invoke();
                    onRebuildRequired?.Invoke();
                }

                // Commit on Enter and on blur — NOT per keystroke: typing toward "SaveAll" passes through
                // the complete name "Save", and a keystroke-commit would relink to the wrong method mid-word.
                // Clicking an autocomplete suggestion blurs the field, so picks land through the blur commit.
                relinkField.RegisterCallback<FocusOutEvent>(_ => TryRelink(relinkField.value));
                relinkField.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) TryRelink(relinkField.value);
                });
                // The hover code peek (also on the designer/quick-edit member pickers): dwelling on a
                // suggestion pops a highlighted source snippet, so "which one did I mean?" answers itself.
                AutocompleteTextField.Attach(relinkField, () => ButtonInvoker.GetMethodSuggestions(warnType),
                    s => MethodSourcePreview.GetSnippet(warnType, s.Value));
                fixRow.Add(relinkField);

                var removeBtn = new Button(() =>
                {
                    // Resolve the parent BEFORE prompting. If this node is already out of the tree (a stale row
                    // that outlived its rebuild), asking "Remove?" and then doing nothing reads as a broken
                    // button; retiring the row instead says what actually happened.
                    var parent = FindParentContainer(layoutAsset != null ? layoutAsset.Root : null, node);
                    if (parent == null) { wrap.RemoveFromHierarchy(); return; }
                    if (!EditorUtility.DisplayDialog("Remove Button",
                            $"Remove the '{baseText}' button from this layout?", "Remove", "Cancel")) return;
                    LayoutRegistry.BeginLayoutEditUndo(layoutAsset, "Remove Sasquatch Button");
                    parent.Children.Remove(node);
                    persistEdit?.Invoke();
                    onRebuildRequired?.Invoke();
                });
                removeBtn.AddToClassList("sasq-button__fix-remove");
                removeBtn.tooltip = "Remove this button from the layout (undoable).";
                removeBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Trash2, 11, DesignerPalette.DangerIcon));
                removeBtn.style.alignItems = Align.Center;
                removeBtn.style.justifyContent = Justify.Center;
                removeBtn.style.width = 22;
                removeBtn.style.height = 18;
                removeBtn.style.flexShrink = 0;
                removeBtn.style.marginLeft = 2;
                removeBtn.style.paddingLeft = 0;
                removeBtn.style.paddingRight = 0;
                removeBtn.style.paddingTop = 0;
                removeBtn.style.paddingBottom = 0;
                fixRow.Add(removeBtn);

                wrap.Add(fixRow);
                return wrap;
            }

            return button;
        }

        // Depth-first parent lookup across the node graph (container children + collection item templates) —
        // the inline remove control needs the owning Children list. Null when the node isn't in this tree
        // (a code-authored in-memory layout, or the root itself).
        private static ContainerNode FindParentContainer(LayoutNode current, LayoutNode child)
        {
            if (current == null) return null;
            if (current is ContainerNode container)
            {
                if (container.Children.Contains(child)) return container;
                foreach (var sub in container.Children)
                {
                    var found = FindParentContainer(sub, child);
                    if (found != null) return found;
                }
            }
            if (current is CollectionNode collection)
                return FindParentContainer(collection.ItemLayout, child);
            return null;
        }

        /// <summary>The effective accent for a button preset: the node's override, else the house
        /// accent (Danger presets default to the danger red instead). Testable precedence seam.</summary>
        internal static Color ResolveButtonAccent(ButtonNode node)
        {
            Color fallback = node.StylePreset == ButtonStyle.Danger ? WidgetPalette.DangerFill : WidgetPalette.AccentUnity;
            return node.AccentColor.Resolve(fallback);
        }

        // Label/icon color per preset: filled presets use near-white; accent-on-dark presets use a
        // lightened accent so it stays readable; Classic keeps the standard editor label gray.
        internal static Color ResolveButtonTextColor(ButtonNode node)
        {
            switch (node.StylePreset)
            {
                case ButtonStyle.Classic:
                    return SasquatchIcons.DefaultTint;
                case ButtonStyle.Modern:
                case ButtonStyle.Danger:
                    return new Color(0.96f, 0.96f, 0.96f, 1f);
                default:
                {
                    var accent = ResolveButtonAccent(node);
                    return Color.Lerp(accent, Color.white, 0.4f);
                }
            }
        }

        // The ink the label + icon actually draw in. A Text Backdrop pins near-white: the pill is dark on
        // every preset and in either skin, so the preset's own ink (Classic's editor gray, or the lightened
        // accent Outline/Ghost/Soft use) would be contrasting against the wrong backdrop. The label reads
        // this via .sasq-button__label--boxed, so a user LabelTextStyle color (applied inline, after build)
        // still wins; the icon has no stylesheet, so it takes the color here.
        internal static Color ResolveButtonInkColor(ButtonNode node)
        {
            return node.TextBackdrop ? Color.white : ResolveButtonTextColor(node);
        }

        // The ButtonSize members in VISUAL order (smallest → largest). Enum.GetValues would order by
        // the serialization-fixed int values instead — Medium(0), Small(1), Large(2), Huge(4) — so every
        // size dropdown builds its choices from here.
        internal static readonly ButtonSize[] ButtonSizeOrder =
        {
            ButtonSize.Small, ButtonSize.Medium, ButtonSize.Large, ButtonSize.Huge
        };

        // Grow-only height per size class (min-height per the resize convention; Classic-Medium keeps
        // the legacy fixed 24px for pixel parity). The SOLE class → px table — the dropdown labels and
        // the export emitter both read it, so a size is described exactly as it renders.
        internal static float ButtonSizeHeight(ButtonSize size)
        {
            switch (size)
            {
                case ButtonSize.Small: return 14f;
                case ButtonSize.Large: return 40f;
                case ButtonSize.Huge: return 84f;
                default: return 26f; // Medium, and any stale value (the cut "Tiny" 3) that no longer maps
            }
        }

        /// <summary>"Small (14 px)" — the size class plus the height it actually renders at.</summary>
        internal static string ButtonSizeLabel(ButtonSize size) => $"{size} ({ButtonSizeHeight(size):0} px)";

        // The height a button renders at: a Custom Height above 0 overrides the class outright.
        // Takes the height as a parameter rather than only reading the node so the designer's live
        // poke can preview a drag value BEFORE it is written to the model (the undo record is taken
        // at the start of the drag burst, so the model must not move ahead of it).
        internal static float ResolveButtonHeight(ButtonSize size, float customHeight)
        {
            return customHeight > 0f
                ? Mathf.Clamp(customHeight, ButtonNode.MinCustomHeight, ButtonNode.MaxCustomHeight)
                : ButtonSizeHeight(size);
        }

        internal static float ButtonMinHeight(ButtonNode node) => ResolveButtonHeight(node.Size, node.CustomHeight);

        // Hand-tuned per class rather than a flat fraction of the height: the glyph-to-height ratio has to
        // DECAY as the button grows (0.57 at Small down to 0.36 at Huge), or an 84px button reads as an icon
        // with a caption under it. Small lands on 8 — the same floor the custom-height path clamps to.
        private static float ButtonIconSize(ButtonSize size)
        {
            switch (size)
            {
                case ButtonSize.Small: return 8f;
                case ButtonSize.Large: return 18f;
                case ButtonSize.Huge: return 30f;
                default: return 13f;
            }
        }

        // The glyph scales with a custom height too, else a 150px button keeps a 13px icon. Half the height
        // is roughly what the class table works out to mid-range (Medium 26→13), clamped so the extremes
        // stay legible rather than vanishing or swallowing the label. The ceiling is what
        // keeps a banner-tall button from becoming an icon with a caption — half of 192 would be exactly
        // that — and it tracks ButtonNode.MaxCustomHeight, so raise it if that ceiling moves again.
        internal static float ResolveButtonIconSize(ButtonSize size, float customHeight)
        {
            return customHeight > 0f
                ? Mathf.Clamp(ResolveButtonHeight(size, customHeight) * 0.5f, 8f, 32f)
                : ButtonIconSize(size);
        }

        internal static float ButtonIconSize(ButtonNode node) => ResolveButtonIconSize(node.Size, node.CustomHeight);

        /// <summary>
        /// Pushes a height onto an ALREADY-BUILT button in place — the Custom Height slider's live poke.
        /// Cheaper and smoother than a node rebuild (no card swap under the held pointer), and it reaches
        /// the icon too: the glyph's size is inline, set at build time, so USS can't carry it.
        /// <paramref name="customHeight"/> is passed explicitly (0 = follow the class) because during a
        /// drag the slider is ahead of the model.
        /// </summary>
        internal static void ApplyButtonHeight(Button button, ButtonNode node, float customHeight)
        {
            if (button == null || node == null) return;

            // Classic + Medium + no custom height is the one case that keeps the legacy FIXED height;
            // everything else is the grow-only min-height. Reconcile both ways — this runs repeatedly on
            // the same element, so the branch not taken has to clear what the other one wrote.
            if (node.StylePreset == ButtonStyle.Classic && customHeight <= 0f && node.Size == ButtonSize.Medium)
            {
                button.style.height = 24;
                button.style.minHeight = StyleKeyword.Null;
            }
            else
            {
                button.style.height = StyleKeyword.Null;
                button.style.minHeight = ResolveButtonHeight(node.Size, customHeight);
            }

            var icon = button.Q(className: "sasq-button__icon");
            if (icon == null) return;
            float iconSize = ResolveButtonIconSize(node.Size, customHeight);
            icon.style.width = iconSize;
            icon.style.height = iconSize;
        }

        /// <summary>Stamped on every <see cref="ButtonNode"/> button so a live poke can find it in either
        /// canvas mode and under any preset (Classic gets no <c>sasq-button</c> class).</summary>
        internal const string ButtonWidgetClass = "sasq-button-widget";

        internal static string ButtonPresetClass(ButtonStyle preset)
        {
            switch (preset)
            {
                case ButtonStyle.Outline: return "sasq-button--outline";
                case ButtonStyle.Ghost: return "sasq-button--ghost";
                case ButtonStyle.Soft: return "sasq-button--soft";
                case ButtonStyle.Danger: return "sasq-button--danger";
                default: return "sasq-button--modern";
            }
        }

        // Preset chrome, applied inline (accent + radius are per-node, so USS can't carry them; USS
        // holds the structural classes only). StyleApplier runs AFTER build, so any user LayoutStyle
        // override still wins — and the hover feedback samples the button's RESOLVED background on
        // entry rather than restoring a captured preset color, so it composes with user overrides too.
        internal static void ApplyButtonChrome(Button button, ButtonNode node)
        {
            button.style.marginTop = 4;
            button.style.marginBottom = 4;
            if (!node.FullWidth) button.style.alignSelf = Align.FlexStart;

            var preset = node.StylePreset;
            // Height (and the icon that scales with it) is ApplyButtonHeight's job for BOTH presets — one
            // authority, so the slider's live poke and a real build can never disagree. Stock Unity chrome
            // stays the pre-preset button byte for byte: Classic + Medium keeps the exact legacy fixed 24.
            ApplyButtonHeight(button, node, node.CustomHeight);
            if (preset == ButtonStyle.Classic) return;

            button.AddToClassList("sasq-button");
            button.AddToClassList(ButtonPresetClass(preset));

            float radius = node.CornerRadius.Resolve(4f);
            button.style.borderTopLeftRadius = radius;
            button.style.borderTopRightRadius = radius;
            button.style.borderBottomLeftRadius = radius;
            button.style.borderBottomRightRadius = radius;

            Color accent = ResolveButtonAccent(node);
            Color bg;
            switch (preset)
            {
                case ButtonStyle.Outline:
                    bg = Color.clear;
                    button.style.borderTopWidth = 1;
                    button.style.borderBottomWidth = 1;
                    button.style.borderLeftWidth = 1;
                    button.style.borderRightWidth = 1;
                    button.style.borderTopColor = accent;
                    button.style.borderBottomColor = accent;
                    button.style.borderLeftColor = accent;
                    button.style.borderRightColor = accent;
                    break;
                case ButtonStyle.Ghost:
                    bg = Color.clear;
                    button.style.borderTopWidth = 0;
                    button.style.borderBottomWidth = 0;
                    button.style.borderLeftWidth = 0;
                    button.style.borderRightWidth = 0;
                    break;
                case ButtonStyle.Soft:
                    bg = new Color(accent.r, accent.g, accent.b, 0.16f);
                    button.style.borderTopWidth = 0;
                    button.style.borderBottomWidth = 0;
                    button.style.borderLeftWidth = 0;
                    button.style.borderRightWidth = 0;
                    break;
                default: // Modern / Danger — filled accent with a subtle top highlight (matches the progress fill).
                    bg = accent;
                    button.style.borderTopWidth = 1;
                    button.style.borderTopColor = new Color(1f, 1f, 1f, 0.14f);
                    button.style.unityFontStyleAndWeight = FontStyle.Bold;
                    break;
            }
            button.style.backgroundColor = bg;
            button.style.color = ResolveButtonTextColor(node); // inherited → the inner label picks it up too

            // Hover feedback in code: inline colors always beat USS pseudo-states. The resting color
            // is sampled on entry so a user background override (applied by StyleApplier after this)
            // is what gets restored, not the preset color. An explicit HoverColor/PressedColor override
            // takes over the respective state; otherwise the built-in subtle lighten is used.
            Color resting = bg;
            bool hovering = false;
            bool pressed = false;
            Color HoverBg() =>
                node.HoverColor.isOverridden ? node.HoverColor.value
                : resting.a < 0.05f
                    ? new Color(accent.r, accent.g, accent.b, 0.12f)  // ghost/outline: faint accent wash
                    : Color.Lerp(resting, Color.white, 0.07f);
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!button.enabledSelf || hovering) return;
                hovering = true;
                resting = button.resolvedStyle.backgroundColor;
                if (!pressed) button.style.backgroundColor = HoverBg();
            });
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!hovering) return;
                hovering = false;
                if (!pressed) button.style.backgroundColor = resting;
            });
            // PressedColor is wired in the CAPTURE phase (TrickleDown). The Button's built-in Clickable manipulator
            // handles PointerDown in the bubble phase and calls StopImmediatePropagation() while starting a click,
            // which would swallow a bubble-phase handler registered here (that's why PressedColor never showed).
            // Capture runs before the bubble phase, so the press color lands; the matching PointerUp restores it.
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !button.enabledSelf || !node.PressedColor.isOverridden) return;
                pressed = true;
                button.style.backgroundColor = node.PressedColor.value;
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (!pressed) return;
                pressed = false;
                button.style.backgroundColor = hovering ? HoverBg() : resting;
            }, TrickleDown.TrickleDown);

            // Tactile feedback on every styled preset — the flavor is per-button (Bounce is the original,
            // subtle-because-these-are-often-full-width-bars default; None/Lift/Grow/Pop are opt-in). Classic
            // returns above, so — as before — the stock Unity button stays animation-free. FullWidth is passed so
            // Grow can use its gentler, no-overshoot rise on edge-to-edge buttons (no room to grow → no clipped corner).
            EditorSpringFX.ApplyButtonHover(button, node.HoverAnimation, node.FullWidth);
        }

        // How often a PropertyNode re-reads its getter so computed values stay live (esp. in play mode).
        private const long PropertyPollIntervalMs = 200;

        // Renders a PropertyNode: a reflected, NON-serialized C# property (computed/derived getters). There
        // is no SerializedProperty, so the value is read via reflection and polled. Get-only / complex-typed /
        // force-read-only properties display disabled; a writable simple-typed property is editable and writes
        // back through the setter. Sibling of BuildButtonElement (which reflects a method instead of a property).
        private static VisualElement BuildPropertyElement(PropertyNode node, SerializedObject serializedObject)
        {
            var target = serializedObject != null ? serializedObject.targetObject : null;
            var prop = target != null ? PropertyInvoker.ResolveProperty(target.GetType(), node.PropertyName) : null;

            string labelText = node.HideLabel ? ""
                : (string.IsNullOrEmpty(node.LabelOverride)
                    ? ObjectNames.NicifyVariableName(node.PropertyName ?? "")
                    : node.LabelOverride);

            if (prop == null)
            {
                // Designer + runtime feedback: the configured property doesn't resolve on the target type.
                return new HelpBox(
                    string.IsNullOrEmpty(node.PropertyName)
                        ? "C# Property: no property selected."
                        : $"C# Property '{node.PropertyName}' not found (or not readable) on " +
                          $"'{(target != null ? target.GetType().Name : "target")}'.",
                    HelpBoxMessageType.Warning);
            }

            Type t = prop.PropertyType;
            bool editable = prop.CanWrite && !node.ForceReadOnly && PropertyInvoker.IsSimpleEditableType(t);

            VisualElement field;
            if (t == typeof(bool))
                field = ConfigureValueField(new Toggle(labelText), prop, target, serializedObject, editable, v => v is bool b && b);
            else if (t == typeof(int))
                field = ConfigureValueField(new IntegerField(labelText), prop, target, serializedObject, editable, v => v == null ? 0 : Convert.ToInt32(v));
            else if (t == typeof(long))
                field = ConfigureValueField(new LongField(labelText), prop, target, serializedObject, editable, v => v == null ? 0L : Convert.ToInt64(v));
            else if (t == typeof(float))
                field = ConfigureValueField(new FloatField(labelText), prop, target, serializedObject, editable, v => v == null ? 0f : Convert.ToSingle(v));
            else if (t == typeof(double))
                field = ConfigureValueField(new DoubleField(labelText), prop, target, serializedObject, editable, v => v == null ? 0d : Convert.ToDouble(v));
            else if (t == typeof(string))
                field = ConfigureValueField(new TextField(labelText), prop, target, serializedObject, editable, v => v as string ?? "");
            else if (t == typeof(Vector2))
                field = ConfigureValueField(new Vector2Field(labelText), prop, target, serializedObject, editable, v => v is Vector2 vec ? vec : Vector2.zero);
            else if (t == typeof(Vector3))
                field = ConfigureValueField(new Vector3Field(labelText), prop, target, serializedObject, editable, v => v is Vector3 vec ? vec : Vector3.zero);
            else if (t == typeof(Vector4))
                field = ConfigureValueField(new Vector4Field(labelText), prop, target, serializedObject, editable, v => v is Vector4 vec ? vec : Vector4.zero);
            else if (t == typeof(Vector2Int))
                field = ConfigureValueField(new Vector2IntField(labelText), prop, target, serializedObject, editable, v => v is Vector2Int vec ? vec : Vector2Int.zero);
            else if (t == typeof(Vector3Int))
                field = ConfigureValueField(new Vector3IntField(labelText), prop, target, serializedObject, editable, v => v is Vector3Int vec ? vec : Vector3Int.zero);
            else if (t == typeof(Color))
                field = ConfigureValueField(new ColorField(labelText), prop, target, serializedObject, editable, v => v is Color c ? c : Color.clear);
            else if (t.IsEnum)
            {
                Enum initial = (Enum)Enum.GetValues(t).GetValue(0);
                if (PropertyInvoker.TryGetValue(target, prop, out var raw0) && raw0 is Enum e0) initial = e0;
                field = ConfigureValueField(new EnumField(labelText, initial), prop, target, serializedObject, editable, v => v as Enum ?? initial);
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                var of = new ObjectField(labelText) { objectType = t, allowSceneObjects = true };
                field = ConfigureValueField(of, prop, target, serializedObject, editable, v => v as UnityEngine.Object);
            }
            else
            {
                // Fallback for complex/custom types: a read-only text view of the value (ToString()).
                var tf = new TextField(labelText);
                field = ConfigureValueField(tf, prop, target, serializedObject, editable: false,
                    coerce: v => v?.ToString() ?? "null");
            }

            // Read-only: disable interaction (the established read-only pattern); editable controls stay enabled.
            if (!editable) field.SetEnabled(false);

            // Join the shared label column so property rows line up with the surrounding fields
            // (honors the node's Align in Parent override the same way binding rows do).
            FieldLabelColumn.AttachToRow(field, node);
            return field;
        }

        // Seeds a typed value field from a property getter, tracks focus, wires the setter (when editable),
        // and polls the getter on an interval so computed values stay live. Used only by BuildPropertyElement.
        private static BaseField<TVal> ConfigureValueField<TVal>(
            BaseField<TVal> field, System.Reflection.PropertyInfo prop, UnityEngine.Object target,
            SerializedObject serializedObject, bool editable, Func<object, TVal> coerce)
        {
            if (PropertyInvoker.TryGetValue(target, prop, out var seed))
                field.SetValueWithoutNotify(coerce(seed));

            // Don't let the poll clobber the field while the user is actively editing it.
            bool focused = false;
            field.RegisterCallback<FocusInEvent>(_ => focused = true);
            field.RegisterCallback<FocusOutEvent>(_ => focused = false);

            if (editable)
            {
                field.RegisterValueChangedCallback(e =>
                {
                    if (target == null) return;
                    Undo.RecordObject(target, $"Set {prop.Name}");
                    PropertyInvoker.SetValue(target, prop, e.newValue);
                    EditorUtility.SetDirty(target);
                    // The setter may have mutated serialized fields (e.g. a debug setter writing a backing
                    // field) — refresh the SerializedObject so bound widgets reflect it.
                    serializedObject?.Update();
                });
            }

            // No detach Pause: UI Toolkit pauses a scheduled item when its element leaves the panel and
            // RESUMES it on re-attach, while an explicit Pause() is permanent — which stopped every
            // [ShowInInspector] property row from ever updating again after a re-parent (a window tab drag
            // re-parents the whole tree). See PanelLifetime.
            field.schedule.Execute(() =>
            {
                if (focused) return;
                if (target == null) return;
                if (PropertyInvoker.TryGetValue(target, prop, out var v))
                    field.SetValueWithoutNotify(coerce(v));
            }).Every(PropertyPollIntervalMs);

            return field;
        }

        /// <summary>
        /// Gives every SECTION (container or collection) in a vertical stack the same <see cref="SectionGap"/>
        /// below it, so the space between two neighbours no longer depends on which KINDS they happen to be —
        /// the "Frame above a Card butts into it while Card above Card is spaced" report. Run once per parent,
        /// from the shared children loop, BEFORE <see cref="ApplyGap"/> so the container's own Spacing still adds
        /// on top of it.
        ///
        /// Three things are deliberate:
        ///  • The LAST section gets 0, not the gap. Only the parent knows sibling order, which is the whole
        ///    reason the rule lives here — a section that margins ITSELF leaves one dead gap per nesting level
        ///    pooled at the bottom of the enclosing Card (Rows in Rows in a Card = 12px of nothing under 10px
        ///    of padding).
        ///  • marginTOP is zeroed too. The other half of the non-uniformity was the .sasq-group shell's USS
        ///    `margin-top: 4px`, which made every collapsible section sit 4px lower than a Card in the same
        ///    slot. Inline beats USS, and the shell keeps its own margins everywhere else it is used (the
        ///    Welcome window, the palette editor, the widget shells) because those are not built through here.
        ///  • An explicit margin override always wins — this is the DEFAULT rhythm, not a cap. Tightening two
        ///    sections up with a negative margin still works exactly as before.
        /// </summary>
        private static void ApplySectionGaps(VisualElement host, SasquatchThemeAsset theme, bool designMode)
        {
            // The designer canvas has its own uniform rhythm — every design card, bindings included, carries the
            // same 8px margin — so normalising containers to 6 there would make the CANVAS the ragged surface.
            if (host == null || designMode) return;

            var stackChildren = new List<VisualElement>();
            foreach (var child in host.Children())
            {
                // A container's own title header is chrome, not a sibling section (ApplyGap skips it too).
                if (child.ClassListContains("sasq-flow-header")) continue;
                stackChildren.Add(child);
            }

            for (int i = 0; i < stackChildren.Count; i++)
            {
                var child = stackChildren[i];
                // The registry, not userData: Unity's binding can overwrite userData on a live element.
                var node = LayoutNodeRegistry.GetNode(child);
                if (!(node is ContainerNode) && !(node is CollectionNode)) continue;

                // A margin the node (or its style token) owns is never touched — StyleApplier already resolved
                // it, and this pass runs after that.
                if (!StyleApplier.OwnsVerticalMargin(node, theme, top: true)) child.style.marginTop = 0f;
                if (!StyleApplier.OwnsVerticalMargin(node, theme, top: false))
                    child.style.marginBottom = i < stackChildren.Count - 1 ? SectionGap : 0f;
            }
        }

        private static void ApplyGap(VisualElement parent, ContainerType kind, float gap)
        {
            if (gap <= 0f) return;
            var children = parent.Children();
            List<VisualElement> actualChildren = new List<VisualElement>();
            foreach (var child in children)
            {
                if (child.ClassListContains("selection-badge") ||
                    child.ClassListContains("designer-grip") ||
                    child.ClassListContains("designer-draggable-placeholder") ||
                    child.ClassListContains("sasq-flow-header"))
                    continue;
                if (child is Label label && label.text.Contains("Empty Container"))
                    continue;
                actualChildren.Add(child);
            }

            for (int i = 0; i < actualChildren.Count - 1; i++)
            {
                var child = actualChildren[i];
                if (kind == ContainerType.Rows)
                {
                    float currentMargin = child.style.marginBottom.keyword == StyleKeyword.Null || child.style.marginBottom.keyword == StyleKeyword.Undefined
                        ? 0f
                        : child.style.marginBottom.value.value;
                    child.style.marginBottom = currentMargin + gap;
                }
                else if (kind == ContainerType.Columns)
                {
                    float currentMargin = child.style.marginRight.keyword == StyleKeyword.Null || child.style.marginRight.keyword == StyleKeyword.Undefined
                        ? 0f
                        : child.style.marginRight.value.value;
                    child.style.marginRight = currentMargin + gap;
                }
            }
        }

        private const float ColumnContainerMinWidth = 40f; // smallest a Columns cell can be dragged to

        // Effective resizability for a Rows/Columns container: explicit Enabled/Disabled win; Auto (the
        // default) is resizable for any container EXCEPT the root layout — so explicitly-created Rows/Columns
        // get resize handles by default while the root stays clean (no handle under every top-level field).
        internal static bool TracksResizable(ContainerNode node, bool isRoot)
        {
            if (node == null) return false;
            switch (node.TrackResize)
            {
                case TrackResizeMode.Enabled: return true;
                case TrackResizeMode.Disabled: return false;
                default: return !isRoot; // Auto
            }
        }

        // Lays out a Columns container's children side-by-side: each gets a flex share from its node's
        // ColumnWeight (0 ⇒ equal), shrinks to share the row, and drops the inspector label-alignment that
        // breaks in narrow columns. Then inserts a drag-to-resize divider between adjacent columns (reusing
        // the table's divider + weight math). Skips the designer chrome, the title header, slots, and hint.
        private static void ApplyColumnsLayout(VisualElement parent, System.Action onLayoutEdited, bool resizable)
        {
            var cols = new List<VisualElement>();
            foreach (var child in parent.Children())
            {
                if (child.ClassListContains("selection-badge") ||
                    child.ClassListContains("designer-grip") ||
                    child.ClassListContains("designer-draggable-placeholder") ||
                    child.ClassListContains("sasq-flow-header") ||
                    child.ClassListContains("sasq-drop-slot") ||
                    // A held-open cell sizes itself exactly like a "＋" cell and owns no weight of its
                    // own, so it's chrome here too — it keeps its track without joining the divider pairs.
                    child.ClassListContains(EmptyCellClass))
                    continue;
                if (child is Label label && label.text.Contains("Empty Container"))
                    continue;
                cols.Add(child);
            }

            foreach (var child in cols)
            {
                // Width from the stored weight (0 ⇒ equal share of the row).
                child.style.flexGrow = ColumnWeightOf(child.userData as LayoutNode);
                child.style.flexBasis = 0f;
                // Flex items default to min-width:auto (their content width), so without this they refuse to
                // shrink and overflow/truncate in a narrow inspector. Allow them to shrink to share the row.
                child.style.minWidth = 0;
                // Field children carry no inspector label-column alignment at all: BuildColumnCellField
                // renders the label itself (auto beside/above), so there's nothing to strip and no inline
                // label width to fight. Don't reach for a class-strip or a .sasq-col-cell USS rule instead —
                // USS never overrides inline styles in UITK, so it can't beat the alignment system's width.
            }

            // A divider between each adjacent pair of columns (the visual gap/separator). It doubles as the
            // drag-to-resize handle only when the container is resizable — otherwise it's a static line, so a
            // non-resizable Columns container keeps the same look minus the drag affordance.
            for (int i = 1; i < cols.Count; i++)
            {
                var leftEl = cols[i - 1];
                var rightEl = cols[i];
                if (!(leftEl.userData is LayoutNode leftNode) || !(rightEl.userData is LayoutNode rightNode))
                    continue; // need a node on each side to persist the weight
                var divider = MakeColumnDivider(interactive: resizable);
                parent.Insert(parent.IndexOf(rightEl), divider);
                if (resizable)
                    AttachColumnContainerResizer(divider, leftEl, leftNode, rightEl, rightNode, onLayoutEdited);
            }
        }

        // Smallest field width (px) we'll allow beside a label before flipping a column cell's label to sit
        // ABOVE the field. Tuned so a label + a usable field fit, else the label gets its own line.
        private const float ColumnCellMinFieldWidth = 90f;

        // Extra room required to flip a stacked label BACK beside its field — see the hysteresis note in
        // BuildColumnCellField. Sized above the two known width modulators (scrollbar ~13px, split seam
        // pad wobble) so neither can re-toggle a decision the decision itself caused.
        private const float StackUnflipHysteresisPx = 16f;

        // Builds a BindingNode that is a DIRECT field child of a Columns container. Unity's inspector
        // label-column alignment can't fit a narrow side-by-side column: it sets a wide, FIXED inline label
        // width on the field's (lazily-built) inner label, and inline styles always beat USS in UITK — so
        // neither a class-strip nor `!important` USS can claw the width back (the long-standing "column label
        // width" bug). The robust cure is to never let the native label participate: build the field with its
        // label suppressed (forceHideLabel also skips the aligned class — the same path table cells use) and
        // render OUR OWN label, auto-flipping it from beside the field to above it when the column is too
        // narrow to fit both. The placement is decided from the label's natural text width (independent of the
        // chosen direction) so it's stable — no flip-flop — and re-evaluated on resize so dragging a divider
        // narrow stacks the label live.
        // WHY EVERY CELL FIELD IS PINNED flex-shrink:1 (both write sites below point here).
        // The editor sheet styles `.unity-base-field` with **flex-shrink: 0**, and a field's
        // flex-basis stays `auto` — which for a multiline TextField resolves to the FULL width of its parent.
        // Beside a hugging label that leaves the field wider than the slot it was given, and flex-grow cannot
        // pull it back: grow only distributes POSITIVE free space, and minWidth:0 only permits a shrink that
        // flex-shrink:0 forbids outright. The field simply overhangs the cell's right edge (measured 42.5px in
        // a 244px circle frame). Inside a shaped Frame that is the visible bug: FrameBuilder's chord fit insets
        // the ROW to the curve, so an overhanging child walks straight back out through the shape — the text box
        // hanging past a circle — while the designer canvas, being narrower, stacked the same cell and looked
        // fine. The Path/Browse row in BindingFactory.Text hit this first and fixed it locally; this is the same
        // cure for every cell. Shrink (not flex-basis:0) because a stacked cell flips to flexDirection.Column,
        // where basis would size the field's HEIGHT — and the stacked branch sets flexGrow 0, so a 0 basis there
        // would collapse the field to nothing. flex-shrink is inert in the stacked direction (the cell's height
        // is auto ⇒ never negative free space), so one unconditional write is correct in both states.
        private static VisualElement BuildColumnCellField(BindingNode node, string resolvedPath, SerializedObject serializedObject, bool parentColumnsHasAnyHeader = false, bool designMode = false)
        {
            var built = BindingFactory.CreateBindingField(node, resolvedPath, serializedObject, forceHideLabel: true);
            if (built == null) return null;

            // A header'd field comes back wrapped as a column [header, field]. Pull the header out so it
            // always sits ABOVE the label+field cell (header > label > field) even when the cell stacks
            // narrow — otherwise stacking would interleave it as label > header > field.
            VisualElement header = null;
            VisualElement field = built;
            if (node.EffectiveHeader != null && built.childCount == 2)
            {
                header = built[0];
                field = built[1];
            }
            else if (!node.IsHeaderHidden)
            {
                var prop = serializedObject.FindProperty(resolvedPath);
                if (prop != null)
                {
                    var targetObj = prop.serializedObject.targetObject;
                    if (targetObj != null)
                    {
                        var fieldInfo = BindingFactory.GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out _);
                        if (fieldInfo != null &&
                            Attribute.GetCustomAttribute(fieldInfo, typeof(HeaderAttribute)) is HeaderAttribute ha)
                        {
                            var leafField = field;
                            leafField.RegisterCallback<GeometryChangedEvent>(_ =>
                            {
                                leafField.Query<VisualElement>(className: "unity-decorator-drawers-container")
                                         .ForEach(d => d.style.display = DisplayStyle.None);
                            });

                            var headerLabel = new Label(ha.header);
                            headerLabel.AddToClassList("sasq-field-header");
                            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                            headerLabel.style.color = WidgetPalette.LabelSecondary;
                            headerLabel.style.marginTop = 6;
                            headerLabel.style.marginBottom = 2;
                            header = headerLabel;
                        }
                    }
                }
            }

            // If this node does not have a header, but some other column in the same row does,
            // render a blank spacer to keep the layout aligned at the same eye-level.
            if (header == null && parentColumnsHasAnyHeader)
            {
                var spacerLabel = new Label(" ");
                spacerLabel.AddToClassList("sasq-field-header");
                spacerLabel.style.marginTop = 6;
                spacerLabel.style.marginBottom = 2;
                header = spacerLabel;
            }

            // Apply no-wrap, overflow hidden and ellipsis to the header label so it doesn't bleed into other columns
            if (header is Label hl)
            {
                hl.style.whiteSpace = WhiteSpace.NoWrap;
                hl.style.overflow = Overflow.Hidden;
                hl.style.textOverflow = TextOverflow.Ellipsis;
            }

            // Resolve the label text the field would otherwise have shown.
            string labelText = "";
            if (!node.IsLabelHidden)
            {
                if (node.EffectiveLabelOverride != null)
                {
                    labelText = node.EffectiveLabelOverride;
                }
                else
                {
                    var prop = serializedObject.FindProperty(resolvedPath);
                    labelText = prop != null
                        ? prop.displayName
                        : ObjectNames.NicifyVariableName(LastPathSegment(resolvedPath));
                }
            }

            // No label → the field simply fills the cell (header above it if present).
            if (string.IsNullOrEmpty(labelText))
            {
                field.style.flexGrow = 1f;
                field.style.minWidth = 0;
                field.style.flexShrink = 1f; // .unity-base-field ships shrink:0 — see the note above this method
                PinCellFieldMargins(field);
                return WithHeaderAbove(header, field);
            }

            var cell = new VisualElement();
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.alignItems = Align.Center;

            var label = new Label(labelText);
            label.AddToClassList("sasq-col-field-label");
            label.style.flexShrink = 0;          // keep the label's content width when beside the field
            label.style.marginRight = 6;
            label.style.color = WidgetPalette.LabelSecondary;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;

            field.style.minWidth = 0;
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f; // .unity-base-field ships shrink:0 — see the note above this method
            PinCellFieldMargins(field);

            cell.Add(label);
            cell.Add(field);

            // Re-decide beside/above on every geometry pass. The cell's own measurement produces its
            // WANT; the applied state comes from a per-ROW consensus (any cell needs stacking → the
            // whole row stacks) so one row never mixes beside- and above-label cells — the mixed row
            // was what made multi-column layouts read as a jumble (a vector cell always stacks, its
            // short neighbors didn't). Cells outside a columns/grid row (frame column-cells) keep
            // the classic per-cell decision. Writes only happen on a decision change, so the flip's
            // own GeometryChanged can't loop.
            var placement = new ColumnCellPlacement { Cell = cell, Label = label, Field = field };
            _columnCellPlacements.Add(cell, placement);
            cell.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float cellW = evt.newRect.width;
                if (designMode)
                {
                    // Canvas/live parity: in Design mode the cell sits inside its designer-card wrapper,
                    // whose grip reserve + selection border (~22px: DesignerCanvas.uss padding-left 20px
                    // + 1px side borders) exist ONLY on the canvas. Deciding from the cell's own
                    // (narrower) width made the canvas stack labels the live inspector renders beside —
                    // a WYSIWYG break, worst inside fixed-width Frames where both surfaces are otherwise
                    // pixel-identical.
                    //
                    // EVERY card, not just the nearest one — the exact shape FieldAffixes.DesignCardReserve
                    // uses, and for the same measured reason. The canvas cards every NODE, so a cell one
                    // container deep already sits inside TWO of them. This walked to the first card and
                    // stopped, which closes exactly HALF the gap: measured at ppp=1, the canvas stacked a
                    // label the inspector kept beside across 5 swept widths at depth 1 and 16 at depth 3.
                    //
                    // ADD the reserve rather than adopting a card's width outright: a Columns row gives each
                    // cell a fraction of the slot, and taking the outermost card's width would hand every
                    // cell in the row the WHOLE row. The outermost card is the CAP, not the answer — it
                    // occupies exactly the slot the bare cell fills live, so no live-equivalent width can
                    // exceed it, and without that ceiling a cell pinned at its own minWidth keeps climbing
                    // past width the card does not have (which is what stopped the affix demote firing at
                    // all until its reserve was capped the same way).
                    float cardReserve = 0f, outermostCardWidth = 0f;
                    for (var p = cell.parent; p != null; p = p.parent)
                    {
                        if (p.ClassListContains("designer-card"))
                        {
                            var cs = p.resolvedStyle;
                            cardReserve += cs.paddingLeft + cs.paddingRight
                                           + cs.borderLeftWidth + cs.borderRightWidth;
                            outermostCardWidth = cs.width;   // keeps overwriting, so it ends OUTERMOST
                        }
                        // Stop AFTER counting: the outermost card can carry the build-root class itself,
                        // and its reserve is as real as any other's. Anything above a nested build root is
                        // another field's slot, not ours.
                        if (p.ClassListContains("sasq-build-root")) break;
                    }
                    if (cardReserve > 0f && outermostCardWidth > 0f)
                        cellW = Mathf.Min(cellW + cardReserve, outermostCardWidth);
                }
                if (float.IsNaN(cellW) || cellW <= 0f) return;
                float labelWidth = MeasuredCellLabelWidth(placement, label, labelText);
                if (labelWidth <= 0f) return; // font not resolved yet — re-decides on the next geometry pass
                // Stored as a BORDER-BOX width, because that is what an inline `width` writes in UI Toolkit
                // and what the field beside it is pushed by — text alone would clip the widest label in the
                // run by whatever padding the label carries.
                placement.NaturalLabelWidth = Mathf.Ceil(labelWidth) + CellLabelBoxExtra(label);
                // Beside-or-above is decided from the gutter this cell will actually SHOW, which for a cell
                // with stack-mates is the SHARED one (see ColumnLabelGroup) — deciding from the cell's own
                // short word would leave its field beside a gutter widened by the cell below it, with less
                // room than the measurement promised.
                float gutter = ColumnLabelGutter(placement, labelWidth);
                // HYSTERESIS (the split-frame "vibration" fix): once stacked, only go back beside with
                // 16px of extra room. The cell's width can DEPEND on the layout's height — a split
                // frame's seam pad carries tan(angle)·h/2, and a scroll view's scrollbar costs ~13px —
                // so a flip that changes the cell's height (~15px) can re-change its own width and
                // flip straight back, re-firing every frame (state A measures B, state B measures A).
                // The dead zone makes any ≤16px width wobble unable to toggle the decision.
                float need = gutter + 6f + ColumnCellMinFieldWidth;
                placement.Want = placement.Applied == true
                    ? need + StackUnflipHysteresisPx > cellW
                    : need > cellW;
                ApplyRowPlacementConsensus(cell, placement);
                // Widths come AFTER the beside/above consensus, because the answer depends on it: a
                // stacked cell hugs again (its label owns the whole cell width), a beside cell takes the
                // run's shared gutter. Every member is re-settled, not just this one — this cell's label
                // may be the one that just widened the run.
                SettleColumnLabelGutter(placement);
            });

            return WithHeaderAbove(header, cell);
        }

        // One column cell's label-placement state, keyed off the cell element (GC-tied). Want = the
        // cell's own measurement; Applied = what's currently styled (row consensus, or Want when the
        // cell isn't in a columns/grid row).
        private sealed class ColumnCellPlacement
        {
            public VisualElement Cell;
            public Label Label;
            public VisualElement Field;
            public bool Want;
            public bool? Applied;

            // The vertical stack of cells this one shares a gutter with (see ColumnLabelGroup), or null
            // for a cell that has no cell BELOW it — a Columns row, a Frame cell, a one-row grid.
            public ColumnLabelGroup Group;
            // This cell's label at its NATURAL (hugging) width — what the group's gutter is the max of.
            // -1 = never measured (a cell hidden by a condition never fires geometry, and must not
            // narrow the gutter its visible stack-mates agreed on).
            public float NaturalLabelWidth = -1f;
            // The inline width currently written on the label. NaN = none (hugging), which is also the
            // state a STACKED cell returns to, since a label above its field spans the cell.
            public float AppliedLabelWidth = float.NaN;

            // Memo for the per-geometry-pass label measurement (see MeasuredCellLabelWidth).
            public string MeasuredText;
            public float MeasuredWidth = -1f;
            public float MeasuredFontSize;
            public FontStyle MeasuredFontStyle;
            public UnityEngine.Object MeasuredFont;
        }

        /// <summary>
        /// A VERTICAL run of column cells — one grid column, or one stack of a Rows-cap regroup — that share
        /// a single label gutter, so their fields all start at the same x.
        ///
        /// <para>WHY THIS EXISTS. A binding dropped straight into a cell never reaches
        /// <see cref="FieldLabelColumn"/>: <see cref="BuildColumnCellField"/> hides the native label and
        /// renders its own at its natural text width, because Unity's aligned column writes a wide FIXED
        /// inline width that no USS can claw back in a narrow flex cell. Hugging is the right cure for that,
        /// but it was only ever exercised by a Columns container — ONE row of cells side by side, where
        /// nothing sits below a label to disagree with it. A Grid stacks cells vertically, and there two
        /// labels in the same column ("Weather" over "Wildlife") each hugged their own text, so the two
        /// toggles beside them started ~7px apart. Reported as a visual glitch; it is, and the cause is that
        /// the hugging rule simply had no opinion about the axis the Grid added.</para>
        ///
        /// <para>The gutter is the widest natural label in the run, so nothing is ever truncated — this
        /// widens labels, never narrows them. It is deliberately NOT capped by
        /// <c>SasquatchSettings.MaxLabelColumnWidth</c>: that cap governs the inspector-wide column, whose
        /// rows can ellipsize into it, while a cell label's own width IS its content and clipping it would
        /// be a new defect rather than the fix for this one. A run too wide to fit a field beside the shared
        /// gutter stacks instead, through the row consensus that already existed.</para>
        /// </summary>
        private sealed class ColumnLabelGroup
        {
            public readonly List<ColumnCellPlacement> Members = new List<ColumnCellPlacement>();
        }

        /// <summary>
        /// The cell label's measured width, memoised on everything the measurement can depend on. A verbatim
        /// mirror of <see cref="FieldLabelColumn"/>'s <c>MeasuredLabelWidth</c>, which already carries this memo
        /// — the two are the same defect and deserve the same fix, and keeping the key list identical is what
        /// stops one of them drifting into staleness the other doesn't have.
        ///
        /// <para><c>MeasureTextSize</c> is one of UI Toolkit's most expensive primitives (measured at ~10.7µs a
        /// call, roughly four times a whole clean layout pass), and this ran once per column cell on EVERY
        /// geometry pass to re-derive a number that only moves when the label's text, size, weight or face
        /// moves. Nothing in the handler around it can change any of those, so the memo returns exactly the
        /// number the call would have returned: an input-equality skip, not a throttle, a debounce or a
        /// deferral. The beside/above decision still runs in full, at the same rate, on every pass.</para>
        ///
        /// <para>THE FACE is resolved definition-first (asset, then font) and only then the legacy
        /// <c>unityFont</c> — the same precedence the rest of the package uses. Getting that order wrong
        /// silently defeats the memo rather than breaking it: a user font override is written as
        /// <c>FontDefinition.FromFont(...)</c>, which fills <c>.font</c> and leaves <c>.fontAsset</c> null.</para>
        ///
        /// <para>A zero measurement is NEVER memoised — the caller reads 0 as "font not resolved yet, re-decide
        /// on the next geometry pass", so caching it would freeze the cell's placement on a phantom width.</para>
        /// </summary>
        internal static float MeasuredCellLabelWidth(object placementState, Label label, string labelText)
        {
            var p = placementState as ColumnCellPlacement;
            if (p == null || label == null) return 0f;

            var rs = label.resolvedStyle;
            float fontSize = rs.fontSize;
            var fontStyle = rs.unityFontStyleAndWeight;
            var fontDef = rs.unityFontDefinition;
            UnityEngine.Object font = (UnityEngine.Object)fontDef.fontAsset ?? (UnityEngine.Object)fontDef.font ?? rs.unityFont;

            if (p.MeasuredWidth >= 0f
                && p.MeasuredFontSize == fontSize
                && p.MeasuredFontStyle == fontStyle
                && ReferenceEquals(p.MeasuredFont, font)
                && string.Equals(p.MeasuredText, labelText, System.StringComparison.Ordinal))
                return p.MeasuredWidth;

            float width = label.MeasureTextSize(labelText, 0, VisualElement.MeasureMode.Undefined,
                                                0, VisualElement.MeasureMode.Undefined).x;
            if (width > 0f)
            {
                p.MeasuredText = labelText;
                p.MeasuredFontSize = fontSize;
                p.MeasuredFontStyle = fontStyle;
                p.MeasuredFont = font;
                p.MeasuredWidth = width;
            }
            return width;
        }

        /// <summary>Test seam: the live placement state for a built column cell, or null.</summary>
        internal static object ColumnCellPlacementFor(VisualElement cell)
            => cell != null && _columnCellPlacements.TryGetValue(cell, out var p) ? p : null;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, ColumnCellPlacement>
            _columnCellPlacements = new();

        // Applies the row-uniform placement: every registered cell in the same visual row stacks iff
        // ANY of them needs to. Idempotent per cell (writes only when Applied flips), so the extra
        // geometry passes the flip causes converge instead of looping.
        private static void ApplyRowPlacementConsensus(VisualElement cell, ColumnCellPlacement own)
        {
            VisualElement row = null;
            for (var p = cell.parent; p != null; p = p.parent)
            {
                if (p.ClassListContains("sasq-columns-content-target") || p.ClassListContains("sasq-grid-row"))
                {
                    row = p;
                    break;
                }
            }

            if (row == null)
            {
                // Not in a columns/grid row (frame column-cells, split panes): classic per-cell decision.
                if (own.Applied == own.Want) return;
                own.Applied = own.Want;
                ApplyColumnCellLabelPlacement(own, own.Want);
                return;
            }

            var rowCells = new List<ColumnCellPlacement>();
            CollectRowCellPlacements(row, rowCells);
            // A label-column TRACK sharing this row (a Rows stack dropped into a column, whose fields run the
            // same decision on the fitted column instead of a hugging cell label) gets a vote too — otherwise
            // one row can end up half beside-label and half above-label depending on which kind of cell it is.
            bool stack = RowScopeStackDemand(row);
            foreach (var c in rowCells) stack |= c.Want;
            foreach (var c in rowCells)
            {
                if (c.Applied == stack) continue;
                c.Applied = stack;
                ApplyColumnCellLabelPlacement(c, stack);
            }
        }

        // The stacking demand FieldLabelColumn's tracks have placed on a row (see ApplyRowStackConsensus).
        // Kept here rather than passed in because a cell's own geometry callback has to see it too.
        private sealed class RowStackDemand { public bool Value; }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, RowStackDemand>
            _rowScopeStackDemand = new();

        private static bool RowScopeStackDemand(VisualElement row)
        {
            RowStackDemand demand;
            return _rowScopeStackDemand.TryGetValue(row, out demand) && demand.Value;
        }

        /// <summary>True when any column CELL in this visual row has decided it must stack. Read by
        /// <c>FieldLabelColumn</c> so a track follows its cell row-mates.</summary>
        internal static bool RowCellsWantStacking(VisualElement row)
        {
            if (row == null) return false;
            var cells = new List<ColumnCellPlacement>();
            CollectRowCellPlacements(row, cells);
            foreach (var c in cells)
                if (c.Want) return true;
            return false;
        }

        /// <summary>Records what the label-column TRACKS in this row concluded and re-settles the row's cells
        /// against it. Writes only on a real change, so the extra geometry pass a flip causes converges.</summary>
        internal static void SetRowScopeStackDemand(VisualElement row, bool demand)
        {
            if (row == null) return;
            var box = _rowScopeStackDemand.GetValue(row, _ => new RowStackDemand());
            if (box.Value == demand) return;
            box.Value = demand;

            var cells = new List<ColumnCellPlacement>();
            CollectRowCellPlacements(row, cells);
            bool stack = demand;
            foreach (var c in cells) stack |= c.Want;
            foreach (var c in cells)
            {
                if (c.Applied == stack) continue;
                c.Applied = stack;
                ApplyColumnCellLabelPlacement(c, stack);
            }
        }

        // The label's own box, in the units an inline `width` is written in. UI Toolkit's `width` is a
        // BORDER-BOX width, so a gutter computed from text alone would clip the run's widest label by
        // exactly the padding it carries. Nothing styles .sasq-col-field-label today, so this is 0 in
        // practice — it is here so a future USS rule cannot silently start truncating labels.
        private static float CellLabelBoxExtra(Label label)
        {
            if (label == null) return 0f;
            var rs = label.resolvedStyle;
            float extra = rs.paddingLeft + rs.paddingRight + rs.borderLeftWidth + rs.borderRightWidth;
            return float.IsNaN(extra) ? 0f : extra;
        }

        /// <summary>
        /// The width this cell's label should occupy: the widest natural label in its vertical run when the
        /// cell has one, otherwise <paramref name="ownRaw"/> — the cell's own hugging measurement, byte for
        /// byte the pre-<see cref="ColumnLabelGroup"/> behaviour. A cell with no cell below it (a Columns
        /// row, a Frame cell, a one-row grid) is never grouped, so that arm is the only thing those layouts
        /// can take: this feature cannot move a cell that has nothing to align WITH.
        /// </summary>
        private static float ColumnLabelGutter(ColumnCellPlacement p, float ownRaw)
        {
            var group = p?.Group;
            if (group == null) return ownRaw;
            float widest = p.NaturalLabelWidth;
            foreach (var m in group.Members)
                if (m.NaturalLabelWidth > widest) widest = m.NaturalLabelWidth;
            return widest;
        }

        /// <summary>
        /// Re-writes the run's shared gutter onto EVERY member (this cell included), each honouring its own
        /// beside/above state. All of them, not just the caller, because the label that just measured may be
        /// the one that widened the run — and a stack-mate whose own geometry is settled will not fire again
        /// on its own to notice.
        /// </summary>
        private static void SettleColumnLabelGutter(ColumnCellPlacement p)
        {
            if (p == null) return;
            if (p.Group == null)
            {
                // Ungrouped — including a cell a regroup just moved out of a run: back to hugging.
                ApplyCellLabelWidth(p, float.NaN);
                return;
            }
            float gutter = ColumnLabelGutter(p, p.NaturalLabelWidth);
            foreach (var m in p.Group.Members)
                ApplyCellLabelWidth(m, m.Applied == true ? float.NaN : gutter);
        }

        // Writes (or clears, with NaN) the label's inline width. Guarded on a real change because a width
        // write re-enters layout: an unconditional one would re-fire the very geometry pass that produced
        // it. Same ≥0.5px dead zone FieldLabelColumn uses for the same reason.
        private static void ApplyCellLabelWidth(ColumnCellPlacement p, float width)
        {
            if (p?.Label == null) return;
            if (float.IsNaN(width) || width <= 0f)
            {
                if (float.IsNaN(p.AppliedLabelWidth)) return;
                p.AppliedLabelWidth = float.NaN;
                p.Label.style.width = StyleKeyword.Null;
                return;
            }
            if (!float.IsNaN(p.AppliedLabelWidth) && Mathf.Abs(p.AppliedLabelWidth - width) < 0.5f) return;
            p.AppliedLabelWidth = width;
            p.Label.style.width = width;
        }

        /// <summary>
        /// Binds one VERTICAL run of cells — a grid column, or one stack of a Rows-cap regroup — into a
        /// shared label gutter. Called from the regroup that ASSIGNS cells to their run, so a Flexible grid
        /// re-flowing on a width change re-forms the runs with it instead of aligning to a stale shape.
        ///
        /// <para>A run of fewer than two labelled cells is left UNGROUPED rather than given a group of one:
        /// that keeps every single-row layout (a Columns container, a Frame's cells) on the exact hugging
        /// path it has always had, so this can only ever change a layout that actually stacks cells.</para>
        /// </summary>
        private static void RegisterCellLabelRun(List<VisualElement> runCells)
        {
            if (runCells == null || runCells.Count == 0) return;

            var members = new List<ColumnCellPlacement>();
            foreach (var cellEl in runCells)
            {
                var p = FirstColumnCellPlacementIn(cellEl);
                if (p != null) members.Add(p);
            }

            if (members.Count < 2)
            {
                // Also the UNBIND path: a cell regrouped out of a run must drop the width it was given.
                foreach (var m in members)
                {
                    m.Group = null;
                    ApplyCellLabelWidth(m, float.NaN);
                }
                return;
            }

            var group = new ColumnLabelGroup();
            group.Members.AddRange(members);
            foreach (var m in members) m.Group = group;
            // Settle now rather than waiting on geometry: a regroup re-parents cells that may already have
            // measured and be otherwise at rest, and one that fires no further pass would keep the width its
            // OLD run agreed on. Members that have not measured yet read as -1 and simply hug until they do.
            SettleColumnLabelGutter(members[0]);
        }

        // The cell placement a grid slot holds, or null. The slot element is not always the registered cell
        // itself: a header'd binding comes back wrapped [header, cell], and the designer canvas wraps every
        // cell in its .designer-card. Search stops at the first registered cell so a nested container's own
        // cells (which form their own runs) can never be adopted by the outer grid's column.
        private static ColumnCellPlacement FirstColumnCellPlacementIn(VisualElement slot)
        {
            if (slot == null) return null;
            if (_columnCellPlacements.TryGetValue(slot, out var own)) return own;
            var found = new List<ColumnCellPlacement>();
            CollectRowCellPlacements(slot, found);
            return found.Count > 0 ? found[0] : null;
        }

        // Registered cells under this row, stopping at any NESTED columns/grid row — an inner
        // container's cells form their own row and decide for themselves.
        private static void CollectRowCellPlacements(VisualElement root, List<ColumnCellPlacement> result)
        {
            foreach (var ch in root.Children())
            {
                if (_columnCellPlacements.TryGetValue(ch, out var st))
                {
                    result.Add(st);
                    continue;
                }
                if (ch.ClassListContains("sasq-columns-content-target") || ch.ClassListContains("sasq-grid-row"))
                    continue;
                CollectRowCellPlacements(ch, result);
            }
        }

        // MEASURED cross-surface drift: Unity's Inspector panel styles .unity-base-field with a -2px
        // right margin, while the default editor sheet uses +3px — so the IDENTICAL cell rendered 5px
        // wider in a real inspector than in the designer's canvas/live preview, enough to clip the tail
        // of a string on one surface but not the other. Cells own their spacing (the 6px label gap), so
        // the outermost field's horizontal margins are pinned to 0 INLINE, which beats any panel's USS
        // on every surface. Re-applied on geometry changes because PropertyField materializes its inner
        // base-field lazily during bind, after the cell is built (same-value inline writes don't
        // re-dirty styling, so the callback can't loop).
        private static void PinCellFieldMargins(VisualElement field)
        {
            void Apply()
            {
                var baseField = field.ClassListContains("unity-base-field")
                    ? field
                    : field.Q(className: "unity-base-field");
                if (baseField == null) return;
                // Inside a shaped frame the chord-fit pass owns this element's horizontal margins (it
                // zeroes the same USS drift via a floor of 0 on base-fields, then adds the curve inset) —
                // re-pinning to 0 here would fight it every geometry pass, alternating writes forever.
                if (baseField.ClassListContains(FrameBuilder.FlowHookedClass)) return;
                baseField.style.marginLeft = 0f;
                baseField.style.marginRight = 0f;
            }
            Apply();
            field.RegisterCallback<GeometryChangedEvent>(_ => Apply());
        }

        /// <summary>
        /// Makes a table cell's widget able to SHRINK, so a column narrower than the widget truncates instead
        /// of being sheared off at the cell's clip edge.
        /// </summary>
        /// <remarks>
        /// MEASURED (real Inspector, 6000.0): a table cell is <c>overflow: Hidden</c>, and several stock
        /// widgets refuse to shrink at all — Unity pins a slider's value box to <c>width: 50; flex-shrink: 0</c>
        /// and the searchable dropdown's button sizes to its content with a non-shrinking text label. Put one
        /// in a 60px column and the widget still lays out at its full width; the cell then clips whatever hangs
        /// past its right edge. What that removes is precisely the widget's right BORDER and its rounded
        /// corner — so the field reads as sliced off flat while its left edge keeps a tidy radius, and a
        /// dropdown loses its chevron entirely. Numbers from the repro: slider input 74px in a 60px cell
        /// (+14), dropdown button 78px in a 68px cell (+10), its chevron +13.
        ///
        /// The fix is not padding or a wider floor — it is telling the content it is allowed to give. Only
        /// CONTAINERS and TEXT are made shrinkable; leaf chrome (the chevron, a slider dragger, a knob) keeps
        /// <c>flex-shrink: 0</c> so it is the label that gives way, not the affordance. Text that is not an
        /// editable input also gets ellipsis, so "Legendary" becomes "Lege…" beside an intact chevron rather
        /// than pushing it out of the cell.
        ///
        /// Runs again on geometry because a PropertyField materializes its inner field lazily during bind,
        /// after the cell is built. Self-limiting: the guard only fires on elements still at shrink 0, so once
        /// a subtree is fitted the callback stops writing and cannot loop.
        /// </remarks>
        private static void FitCellContent(VisualElement field)
        {
            // ONE working stack per cell, allocated at build and reused by every later geometry pass. The walk
            // below runs per CELL on EVERY geometry pass — rows x columns times for every frame of a width
            // drag — and a fresh Stack per call was that many short-lived allocations per frame, for a buffer
            // whose contents never outlive the walk. Captured per cell rather than shared statically, so there
            // is no re-entrancy question to get wrong: a cell's own walk cannot overlap itself (it only writes
            // styles, and UI Toolkit defers the resulting layout work to the next pass).
            var stack = new Stack<VisualElement>();

            void Apply()
            {
                stack.Clear(); // a walk that threw part-way through must not leak into the next one
                stack.Push(field);
                while (stack.Count > 0)
                {
                    var e = stack.Pop();
                    bool isText = e is TextElement;
                    // A container passes the squeeze down; a TextElement is the thing that should absorb it.
                    // Everything else is fixed-size chrome and is deliberately left alone.
                    if (e.childCount > 0 || isText)
                    {
                        if (e.resolvedStyle.flexShrink == 0f) e.style.flexShrink = 1f;
                    }
                    // Ellipsis only on TEXT LEAVES, and never inside a text input: an editable field scrolls its
                    // own content to follow the caret, and Unity already handles that — overriding it here would
                    // fight the editing experience for no gain.
                    if (isText && e.childCount == 0 && !e.ClassListContains(InnerInputTextClass))
                    {
                        e.style.overflow = Overflow.Hidden;
                        e.style.textOverflow = TextOverflow.Ellipsis;
                    }
                    for (int i = 0; i < e.childCount; i++) stack.Push(e[i]);
                }
            }
            Apply();
            field.RegisterCallback<GeometryChangedEvent>(_ => Apply());
        }

        // Unity's marker on the TextElement that a text INPUT scrolls internally (the caret-following one).
        private const string InnerInputTextClass = "unity-text-element--inner-input-field-component";

        // Stacks an optional header above content in a column; returns content unchanged when no header.
        // Adding header/content here re-parents them out of any temporary wrapper they came from.
        private static VisualElement WithHeaderAbove(VisualElement header, VisualElement content)
        {
            if (header == null) return content;
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            col.Add(header);
            col.Add(content);
            return col;
        }

        // Toggles a column cell between label-beside-field (Row) and label-above-field (stacked Column).
        // In the stacked case the field's width comes from the cross-axis stretch (so flexGrow is cleared to
        // avoid it growing VERTICALLY in a column flex container).
        private static void ApplyColumnCellLabelPlacement(ColumnCellPlacement p, bool stack)
        {
            var cell = p.Cell;
            var label = p.Label;
            var field = p.Field;
            if (stack)
            {
                cell.style.flexDirection = FlexDirection.Column;
                cell.style.alignItems = Align.Stretch;
                label.style.marginRight = 0;
                label.style.marginBottom = 1;
                field.style.flexGrow = 0f;
            }
            else
            {
                cell.style.flexDirection = FlexDirection.Row;
                cell.style.alignItems = Align.Center;
                label.style.marginRight = 6;
                label.style.marginBottom = 0;
                field.style.flexGrow = 1f;
            }
            // A shared gutter is a BESIDE-only idea — stacked, the label owns the cell's width and a fixed
            // one would leave it short of the field under it. Settled here as well as in the cell's own
            // geometry pass, because the cell the ROW consensus just flipped is not always the cell that
            // fired: it can be a stack-mate that is otherwise settled and would keep a stale width.
            ApplyCellLabelWidth(p, stack ? float.NaN : ColumnLabelGutter(p, p.NaturalLabelWidth));
        }

        private static string LastPathSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path.Substring(dot + 1) : path;
        }

        // Drag-to-resize for a Columns container: dragging the divider redistributes flex weight between the
        // two adjacent column children (live, no rebuild) and persists once on release via onLayoutEdited.
        // Mirrors the table resizer (AttachColumnResizer) but for single-cell columns whose weights live on
        // the child nodes' ColumnWeight. Reuses the pure, unit-tested ComputeResizedWeights.
        private static void AttachColumnContainerResizer(VisualElement divider, VisualElement leftEl, LayoutNode leftNode, VisualElement rightEl, LayoutNode rightNode, System.Action onLayoutEdited)
        {
            bool resizing = false;
            float startX = 0f, leftWidth0 = 0f, rightWidth0 = 0f, leftWeight0 = 1f, rightWeight0 = 1f;

            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                startX = evt.position.x;
                leftWidth0 = leftEl.worldBound.width;
                rightWidth0 = rightEl.worldBound.width;
                leftWeight0 = ColumnWeightOf(leftNode);
                rightWeight0 = ColumnWeightOf(rightNode);
                divider.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                var (gL, gR) = ComputeResizedWeights(leftWidth0, rightWidth0, evt.position.x - startX, leftWeight0, rightWeight0, ColumnContainerMinWidth);
                leftNode.ColumnWeight = gL;
                rightNode.ColumnWeight = gR;
                leftEl.style.flexGrow = gL;
                rightEl.style.flexGrow = gR;
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (divider.HasPointerCapture(evt.pointerId)) divider.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke(); // persist: designer marks dirty + re-renders; inspector SetDirty's the asset
                evt.StopPropagation();
            });

            // Capture-loss (window blur / focus steal / Esc) without a PointerUp must still persist the
            // live model edit, or the resize silently reverts on the next rebuild-from-disk.
            divider.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        private const float RowResizeMinHeight = 20f;  // drag below this ⇒ revert the row to auto (natural) height
        private const float RowDividerHeight = 5f;      // thickness of the draggable handle strip under a row
        // Smallest change a row-resize drag bothers to apply. A row's height drives a FULL inspector relayout
        // plus a GeometryChangedEvent cascade over everything below it (measured at ~9ms/frame on a large
        // inspector against ~2ms idle), so spending one on sub-pixel pointer jitter is pure chop. Mirrors the
        // ≥0.5px write guard FieldLabelColumn already uses for the same reason.
        private const float RowResizeEpsilon = 0.5f;

        // Which rows need a RowFloorHost to carry their reserved height. Design mode already wraps every leaf
        // in a card (that wrapper is exactly why the designer rendered tall rows correctly all along), and our
        // own container elements are plain boxes with no Unity internals to deform — only a LIVE leaf row is
        // a raw Unity widget that would misalign if stretched. See RowFloorHost.
        private static bool NeedsFloorHost(LayoutNode node, bool designMode) =>
            !designMode && !(node is ContainerNode) && !(node is CollectionNode);

        /// <summary>
        /// Reserves <paramref name="height"/> for one row/cell, putting the floor on a <see cref="RowFloorHost"/>
        /// whenever the element is a live leaf widget that stretching would deform. Pads and other chrome carry
        /// no node and are plain boxes, so they take the floor directly. The single place a row floor is applied.
        /// </summary>
        private static void ApplyRowFloor(VisualElement element, float height, bool designMode)
        {
            var node = element?.userData as LayoutNode;
            MinHeightFloors.SetRow(RowFloorHost.Ensure(element, node != null && NeedsFloorHost(node, designMode)), height);
        }

        // Lays out a Rows container's children top-to-bottom: applies each row's stored explicit height (0 ⇒
        // auto) as a min-height (so taller content never clips), then inserts a horizontal drag-to-resize
        // divider UNDER each row. The Rows analog of ApplyColumnsLayout — height instead of width, since a
        // vertical stack in an auto-sized inspector has no fixed total to split proportionally. Skips designer
        // chrome, the title header, slots, and the hint.
        private static void ApplyRowsLayout(VisualElement parent, System.Action onLayoutEdited, bool resizable, bool designMode)
        {
            var rows = new List<VisualElement>();
            foreach (var child in parent.Children())
            {
                if (child.ClassListContains("selection-badge") ||
                    child.ClassListContains("designer-grip") ||
                    child.ClassListContains("designer-draggable-placeholder") ||
                    child.ClassListContains("sasq-flow-header") ||
                    child.ClassListContains("sasq-row-resizer") ||
                    child.ClassListContains("sasq-drop-slot") ||
                    // See ApplyColumnsLayout: a held-open cell reserves its track by itself and takes no
                    // stored RowHeight, so it never earns a resize divider.
                    child.ClassListContains(EmptyCellClass))
                    continue;
                if (child is Label label && label.text.Contains("Empty Container"))
                    continue;
                rows.Add(child);
            }

            // The row divider exists ONLY as a resize handle (rows need no visual separator), so when the
            // container isn't resizable we add none at all — this is what keeps the root layout clean. The stored
            // per-row min-height floor is applied here too (deliberately AFTER this guard, not before): a
            // non-resizable container has no divider to undo a height, so a stale RowHeight — e.g. one left on a
            // child from an earlier Rows arrangement, like a Grid that was once a resized row — would otherwise
            // pin that child to a height the user CAN'T change (it shows up as dead space the inner resizer can't
            // reclaim). Keeping the floor and the handle-to-change-it together means a non-resizable container
            // (the root included) never imposes an unchangeable height.
            if (!resizable) return;

            foreach (var row in rows)
            {
                // Apply the stored height (as a min-height floor). Dividers go in a second pass so inserting
                // them doesn't shift the rows we're still iterating.
                if (row.userData is LayoutNode node && node.RowHeight > 0f)
                {
                    // The floor goes on the row's HOST, never on the widget: stretching a widget past its
                    // content deforms it (slider knob, unit suffix, badges … all drift to the middle). The
                    // host is the row itself for design/container rows, which are already safe to stretch.
                    // Through the arbiter, never a raw write — a styled slider re-asserts its own knob floor
                    // from a GeometryChangedEvent and would win any bare write. See MinHeightFloors.
                    ApplyRowFloor(row, node.RowHeight, designMode);
                }
            }

            foreach (var row in rows)
            {
                if (!(row.userData is LayoutNode rowNode)) continue;
                var divider = MakeRowDivider();
                // The divider belongs BELOW the reserved space, so it anchors to the host when there is one.
                var anchor = RowFloorHost.Of(row) ?? row;
                parent.Insert(parent.IndexOf(anchor) + 1, divider);
                AttachRowContainerResizer(divider, row, rowNode, onLayoutEdited, designMode);
            }
        }

        // Applies each node's per-role text styles to the SPECIFIC element that holds that text (no cascade):
        // container title, collection header, button label, help-box message, and a binding's header / label /
        // value (+ value background). Unity-internal labels/inputs build lazily, so they're applied deferred.
        // INTERNAL, not private: SerializedDictionaryDrawer calls it for a key/value cell template. That
        // drawer builds its cells through BindingFactory.CreateBindingField (the widget constructor alone)
        // rather than through BuildNode, so without this the Field Styling box on a dictionary cell template
        // would be a dead control -- the exact shape of the table-cell hole recorded in
        // table-cells-bypassed-buildnode.
        internal static void ApplyNodeTextStyles(VisualElement element, LayoutNode node)
        {
            switch (node)
            {
                case ContainerNode c:
                    ApplyTextToFirstClass(element, c.TitleTextStyle, "sasq-node-title", "sasq-flow-header", "sasq-group-title", "unity-foldout__text");
                    break;
                case CollectionNode col:
                    ApplyTextToFirstClass(element, col.HeaderTextStyle, "sasq-collection-header");
                    break;
                case ButtonNode b:
                    // A button with an icon renders its text in an inner label — style that directly
                    // (non-inherited text properties like alignment don't cascade); otherwise the
                    // button element itself holds the only text.
                    var buttonLabel = element.Q<Label>(className: "sasq-button__label");
                    TextStyleApplier.Apply(buttonLabel != null ? (VisualElement)buttonLabel : element, b.LabelTextStyle);
                    break;
                case PropertyNode pn:
                    ApplyTextToFirstClass(element, pn.LabelTextStyle, "unity-base-field__label");
                    ApplyFieldStyle(element, pn.ValueTextStyle, default);
                    break;
                case HelpBoxNode hb:
                    ApplyTextToFirstClass(element, hb.MessageTextStyle, "sasq-helpbox-message", "unity-help-box__label");
                    ApplyTextToFirstClass(element, hb.SummaryTextStyle, "sasq-help-expand-summary");
                    break;
                case BindingNode bn:
                    ApplyBindingHeaderStyle(element, bn.HeaderTextStyle);
                    ApplyTextToFirstClass(element, bn.LabelTextStyle, "unity-base-field__label");
                    // UnityEvent renders one input per listener card — fan the field styling across all of them.
                    ApplyFieldStyle(element, bn.FieldTextStyle, bn.FieldBackground, allInputs: bn.DisplayType == DisplayType.UnityEvent);
                    ApplyTextToFirstClass(element, bn.ToggleOnOffTextStyle, "sq-toggle-onoff-label");
                    break;
                case ScriptHeaderNode sh:
                    ApplyTextToFirstClass(element, sh.TextStyle, "sasq-script-header-title");
                    break;
            }
        }

        // A titled section already shows its title as a header; a Default field inside it whose native code
        // [Header] decorator repeats that SAME text would draw the header twice (section title + decorator). Hide
        // the duplicate — a field's DISTINCT [Header] (a real sub-header) survives. This is the code-built-layout
        // safety net: an IMPORT-built layout instead marks the consumed lead field header-hidden per-field (so its
        // decorator is already suppressed and this is a no-op). Mirrors the export's title-based dedup so the live
        // inspector and a standalone-exported editor agree.
        private static void SuppressDuplicateContainerHeaderDecorator(VisualElement element, LayoutNode node)
        {
            if (element == null || !(node is ContainerNode c)) return;
            string title = c.Title;
            string source = c.SourceHeader;
            bool hasTitle = !string.IsNullOrEmpty(title);
            bool hasSource = !string.IsNullOrEmpty(source) && source != title;
            if (!hasTitle && !hasSource) return;

            element.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                element.Query<VisualElement>(className: "unity-decorator-drawers-container").ForEach(d =>
                {
                    var hdr = d.Q<Label>();
                    if (hdr == null) return;
                    if ((hasTitle && hdr.text == title) || (hasSource && hdr.text == source))
                        d.style.display = DisplayStyle.None;
                });
            });
        }

        // ~half a second at 16ms cadence — long enough for a field's late internals (PropertyField input,
        // decorator labels) to build after this node does.
        private const int DeferredStyleReapplyTicks = 30;

        // Runs <paramref name="apply"/> on every geometry change (so per-node text/field styling survives a
        // field's later INTERNAL rebuilds) AND on a short bounded poll right after build. The poll is what
        // makes styling reliable after an IN-PLACE node rebuild (LiveRebuildSelectedNode → RebuildNodeInPlace,
        // triggered by Save's CommitPendingTextEdit and by an alignment-button click): the fresh element is
        // inserted into an already-settled parent, so its root resolves geometry exactly ONCE — before a
        // PropertyField's input has built — and since applying a larger font is itself what would move
        // geometry, the GeometryChanged handler alone never gets a second chance to catch the late-built
        // input. Without the poll, field/label/header styling silently drops on the CANVAS after Save or an
        // alignment click (the model, and so the real inspector, stay correct). The poll re-applies for a
        // moment, catching the internals whenever they appear, then stops.
        private static void ScheduleStyleApply(VisualElement root, Action apply)
        {
            if (root == null || apply == null) return;
            root.RegisterCallback<GeometryChangedEvent>(_ => apply());
            int ticks = 0;
            IVisualElementScheduledItem item = null;
            item = root.schedule.Execute(() =>
            {
                apply();
                if (++ticks >= DeferredStyleReapplyTicks) item.Pause(); // the bounded poll stops itself
            }).Every(16);
            // No detach Pause: UI Toolkit pauses the item on detach and resumes it on re-attach, so a
            // re-parent mid-poll (window tab drag) resumes its remaining ticks instead of dropping the
            // late-built styling for good. See PanelLifetime.
        }

        // Applies a text style to the first descendant matching any of the given classes. Deferred (see
        // ScheduleStyleApply) because some targets (esp. PropertyField internals) aren't built at this point;
        // re-applying on later layouts is intentional, so styling survives a field's internal rebuilds.
        private static void ApplyTextToFirstClass(VisualElement root, TextStyle ts, params string[] classNames)
        {
            if (root == null || !TextStyleApplier.HasAnyOverride(ts)) return;
            ScheduleStyleApply(root, () =>
            {
                foreach (var cls in classNames)
                {
                    var el = root.Q<VisualElement>(className: cls);
                    if (el != null) { TextStyleApplier.Apply(el, ts); break; }
                }
            });
        }

        // Styles a binding's header text, whichever form it takes: our own override label (sasq-field-header)
        // OR — for a plain C# [Header] with no override — Unity's native PropertyField decorator label, which
        // has no sasq class (so the generic class query missed it). Deferred: the decorator builds lazily.
        private static void ApplyBindingHeaderStyle(VisualElement root, TextStyle ts)
        {
            if (root == null || !TextStyleApplier.HasAnyOverride(ts)) return;
            ScheduleStyleApply(root, () =>
            {
                var own = root.Q<VisualElement>(className: "sasq-field-header");
                if (own != null) { TextStyleApplier.Apply(own, ts); return; }

                // Code [Header] → style every label inside the decorator container (Space decorators have none).
                var deco = root.Q<VisualElement>(className: "unity-decorator-drawers-container");
                if (deco != null) deco.Query<Label>().ForEach(l => TextStyleApplier.Apply(l, ts));
            });
        }

        // Applies a binding's value-text style + input background to the field's input box (deferred).
        private static void ApplyFieldStyle(VisualElement root, TextStyle ts, OverridableColor background, bool allInputs = false)
        {
            if (root == null || (!TextStyleApplier.HasAnyOverride(ts) && !background.isOverridden)) return;

            void ApplyToInput(VisualElement input)
            {
                if (input == null) return;

                // Support custom searchable dropdown buttons inside input container
                var button = input.Q<Button>();
                if (button != null)
                {
                    TextStyleApplier.Apply(button, ts);
                    if (background.isOverridden)
                    {
                        button.style.backgroundColor = background.value;
                        button.style.backgroundImage = StyleKeyword.Null;
                    }
                }
                else
                {
                    TextStyleApplier.Apply(input, ts);
                    if (background.isOverridden)
                    {
                        input.style.backgroundColor = background.value;
                        // Clear background image (e.g. for PopupField/DropdownField bevel textures)
                        // so the solid background color actually shows
                        input.style.backgroundImage = StyleKeyword.Null;
                    }
                }

                if (TextStyleApplier.HasAnyOverride(ts))
                {
                    // Unit-suffix labels keep their fixed dim styling — the value-text style must not
                    // recolor the unit into looking like part of the value.
                    input.Query<TextElement>().ForEach(te =>
                    {
                        if (te.ClassListContains(BindingFactory.UnitSuffixClass)) return;
                        TextStyleApplier.Apply(te, ts);
                    });
                }
            }

            ScheduleStyleApply(root, () =>
            {
                // Composite widgets (e.g. the UnityEvent card list) hold one input PER card — style them all,
                // not just the first. Single-field widgets keep the original first-match behavior.
                if (allInputs)
                    root.Query<VisualElement>(className: "unity-base-field__input").ForEach(ApplyToInput);
                else
                    ApplyToInput(root.Q<VisualElement>(className: "unity-base-field__input"));
            });
        }

        private const float NodeResizeMinSize = 20f;

        private enum ResizeEdge { Right, Left, Bottom }

        // Adds opt-in drag grips to a node element: a width grip on its free horizontal edge when
        // node.MakeWidthResizable, and a bottom-edge height grip when node.MakeHeightResizable. The width grip
        // sits on the LEFT when the node is right-aligned (its free edge), otherwise on the RIGHT; center- and
        // stretch-aligned nodes re-center as they resize, so a single grip resizes them symmetrically. Dragging
        // writes the size into Style.width / Style.height (applied by StyleApplier) and persists on release via
        // the captured _onLayoutEdited — same model as the text-box / row / column resizers.
        /// <summary>
        /// Puts a resize grip back after the host element rebuilds its children out from under it.
        /// <para>
        /// A <see cref="PropertyField"/> REBUILDS its children when the inspector binds — and again on every
        /// re-bind — which clears an absolutely-positioned grip straight back out of the hierarchy. So a grip
        /// added with a plain <c>hierarchy.Add</c> is destroyed one binding tick later and can never be
        /// dragged. <see cref="DisplayType.Default"/> IS a raw <c>PropertyField</c>, so the most ordinary
        /// resizable field was exactly the broken one; a Text or Slider node builds its own widget and was
        /// never affected, which is why the canvas drag tests never caught it.
        /// </para>
        /// <para>
        /// DEFERRED through <c>schedule</c>, not immediate: re-parenting from inside the detach notification
        /// itself is the case UI Toolkit rejects outright, logging <i>"Modifying the parent of a VisualElement
        /// while it is already being modified"</i>. The scheduled tick lands after the rebuild has finished,
        /// which is the supported way to change a hierarchy in response to one.
        /// </para>
        /// The parent test keeps a genuine teardown out of this path: when the whole inspector detaches, the
        /// grip goes with it but nothing removed it, so <c>parent</c> is still the host and this does nothing.
        /// A null parent means something took it out on purpose.
        /// <para>
        /// Twin of <c>SasqKit.AttachNodeGrip</c>'s self-heal in the export kit, where this was found first.
        /// Keep the two in step.
        /// </para>
        /// </summary>
        private static void RehomeGripOnRebuild(VisualElement host, VisualElement handle)
        {
            if (host == null || handle == null) return;
            bool rehomePending = false;
            handle.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (handle.parent != null || host.panel == null || rehomePending) return;
                rehomePending = true;
                host.schedule.Execute(() =>
                {
                    rehomePending = false;
                    if (handle.parent == null && host.panel != null) host.hierarchy.Add(handle);
                });
            });
        }

        private static void AttachNodeResizeHandles(VisualElement element, LayoutNode node, bool designMode)
        {
            if (node == null || element == null) return;
            if (!node.MakeWidthResizable && !node.MakeHeightResizable) return;

            var persist = _onLayoutEdited; // capture at build time — the static is restored when Build returns

            if (node.MakeWidthResizable)
            {
                bool rightAligned = node.Style.alignSelf.isOverridden && node.Style.alignSelf.value == Align.FlexEnd;

                // Force space inside the container on the resizable side so content does not overlap the grip
                if (rightAligned)
                {
                    float currentPaddingLeft = 0f;
                    if (element.style.paddingLeft.keyword != StyleKeyword.Null && element.style.paddingLeft.value.unit == LengthUnit.Pixel)
                    {
                        currentPaddingLeft = element.style.paddingLeft.value.value;
                    }
                    if (currentPaddingLeft < 8f)
                    {
                        element.style.paddingLeft = 8f;
                    }
                }
                else
                {
                    float currentPaddingRight = 0f;
                    if (element.style.paddingRight.keyword != StyleKeyword.Null && element.style.paddingRight.value.unit == LengthUnit.Pixel)
                    {
                        currentPaddingRight = element.style.paddingRight.value.value;
                    }
                    if (currentPaddingRight < 8f)
                    {
                        element.style.paddingRight = 8f;
                    }
                }

                var edge = rightAligned ? ResizeEdge.Left : ResizeEdge.Right;
                var handle = MakeEdgeGripHandle(edge);
                element.hierarchy.Add(handle);
                RehomeGripOnRebuild(element, handle);
                // Left grip grows the node as the cursor moves left (free edge moves outward), so flip the sign.
                AttachNodeResizeDrag(handle, element, node, persist, horizontal: true, growSign: rightAligned ? -1f : 1f);
            }
            // An aspect-locked frame derives its height from its width, so a height grip is inert: the drag
            // writes Style.height and the frame's own aspect pass overwrites it on the next geometry pass.
            // The designer HIDES the "Make Height Resizable" toggle for such a frame, which on its own left a
            // one-way trap — turn it on as a plain Rounded Rect, switch to Circle, and the grip stayed on
            // screen doing nothing with no toggle left to switch it back off. Gated here rather than cleared
            // at the edit points because the flag also arrives by paste, preset, multi-edit and legacy data.
            if (node.MakeHeightResizable && !FrameBuilder.AspectLocked(node))
            {
                // Force space inside the container at the bottom so content does not overlap the grip
                float currentPaddingBottom = 0f;
                if (element.style.paddingBottom.keyword != StyleKeyword.Null && element.style.paddingBottom.value.unit == LengthUnit.Pixel)
                {
                    currentPaddingBottom = element.style.paddingBottom.value.value;
                }
                if (currentPaddingBottom < 8f)
                {
                    element.style.paddingBottom = 8f;
                }

                var handle = MakeEdgeGripHandle(ResizeEdge.Bottom);
                element.hierarchy.Add(handle);
                RehomeGripOnRebuild(element, handle);
                AttachNodeResizeDrag(handle, element, node, persist, horizontal: false, growSign: 1f);
            }

            // Right-click the element → "Return to Default Size" (clears the dragged width/height override),
            // shown only once it's actually been resized. The hasW/hasH checks run inside the callbacks so
            // visibility stays dynamic across resizes without a rebuild.
            void ResetSize(bool hasW, bool hasH)
            {
                var s = node.Style;
                // The design card mirrors the width (see DesignLeafCardOf) — release it too, or the card keeps
                // the old fixed width and "Return to Default Size" looks like it did nothing in the canvas.
                if (hasW)
                {
                    var w = s.width; w.isOverridden = false; w.value = 0f; s.width = w;
                    element.style.width = StyleKeyword.Null;
                    var card = DesignLeafCardOf(element, node);
                    if (card != null) card.style.width = StyleKeyword.Null;
                }
                // Height resize lives on min-height (grow-only floor); clear that. Also clear any legacy fixed
                // height so older layouts that pinned style.height return to natural too.
                // Releasing THIS node's floor must not take a Rows container's row floor down with it, so the
                // min-height clear goes through the arbiter; the legacy fixed height is ours alone to clear.
                if (hasH) { var h = s.height; h.isOverridden = false; h.value = 0f; s.height = h; MinHeightFloors.SetNode(element, 0f); element.style.height = StyleKeyword.Null; }
                node.Style = s;
                persist?.Invoke();
            }

            if (designMode)
            {
                // The designer has no unified takeover; its canvas/node menus merge with this manipulator.
                element.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    bool hasW = node.MakeWidthResizable && node.Style.width.isOverridden;
                    bool hasH = node.MakeHeightResizable && node.Style.height.isOverridden;
                    if (!hasW && !hasH) return;
                    evt.menu.AppendAction("Return to Default Size", _ => ResetSize(hasW, hasH));
                }));
            }

            // Live inspector: same entry through the unified right-click menu's contributor registry.
            QuickMenuContributors.Register(element, (request, menu) =>
            {
                bool hasW = node.MakeWidthResizable && node.Style.width.isOverridden;
                bool hasH = node.MakeHeightResizable && node.Style.height.isOverridden;
                if (!hasW && !hasH) return;
                menu.Add("Return to Default Size", () => ResetSize(hasW, hasH));
            });
        }

        // A discreet centered "grabber" pill on an invisible edge hit-strip — the same look as the resizable
        // text box's grip (MakeGripResizeHandle), oriented to the edge it sits on. Brightens on hover.
        private static VisualElement MakeEdgeGripHandle(ResizeEdge edge)
        {
            bool bottom = edge == ResizeEdge.Bottom;

            var handle = new VisualElement();
            handle.AddToClassList(bottom ? "sasq-row-resizer" : "sasq-col-resizer"); // resize cursor
            handle.AddToClassList(DesignKeepInteractiveClass); // node width/height adjust works in design mode too (leaf widgets included)
            handle.style.position = Position.Absolute;
            handle.style.justifyContent = Justify.Center;
            handle.style.alignItems = Align.Center;

            var idle = WidgetPalette.HandleIdle;
            var hot = WidgetPalette.HandleHot;
            var pill = new VisualElement { pickingMode = PickingMode.Ignore };
            pill.style.backgroundColor = idle;
            pill.style.borderTopLeftRadius = 2;
            pill.style.borderTopRightRadius = 2;
            pill.style.borderBottomLeftRadius = 2;
            pill.style.borderBottomRightRadius = 2;

            if (bottom)
            {
                handle.style.left = 0;
                handle.style.right = 0;
                handle.style.bottom = 0;
                handle.style.height = 8;
                handle.style.flexDirection = FlexDirection.Row;
                pill.style.width = 28;
                pill.style.height = 3;
                pill.style.marginBottom = 1;
            }
            else
            {
                handle.style.top = 0;
                handle.style.bottom = 0;
                handle.style.width = 8;
                if (edge == ResizeEdge.Right) handle.style.right = 0; else handle.style.left = 0;
                handle.style.flexDirection = FlexDirection.Column;
                pill.style.width = 3;
                pill.style.height = 28;
                if (edge == ResizeEdge.Right) pill.style.marginRight = 1; else pill.style.marginLeft = 1;
            }

            handle.Add(pill);
            handle.RegisterCallback<MouseEnterEvent>(_ => pill.style.backgroundColor = hot);
            handle.RegisterCallback<MouseLeaveEvent>(_ => pill.style.backgroundColor = idle);
            return handle;
        }

        /// <summary>
        /// The design-mode selection CARD wrapping <paramref name="target"/>, or null (live inspector, or a
        /// container — only leaves get wrapped). The card copies the element's width at build time and is what
        /// the canvas actually lays out, so a width write that skips it is invisible. Identified by node
        /// identity: the wrap transfers the node to the card and NULLS the registry entry on the element.
        /// </summary>
        private static VisualElement DesignLeafCardOf(VisualElement target, LayoutNode node)
        {
            var parent = target != null ? target.parent : null;
            if (parent == null || node == null || !parent.ClassListContains("designer-card")) return null;
            return LayoutNodeRegistry.GetNode(parent) == node ? parent : null;
        }

        private static void AttachNodeResizeDrag(VisualElement handle, VisualElement target, LayoutNode node, System.Action onLayoutEdited, bool horizontal, float growSign)
        {
            bool resizing = false;
            float start = 0f, startSize = 0f;
            VisualElement sizeCard = null; // design-mode selection card mirroring the width — see DesignLeafCardOf

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                // Resolved per gesture, not at build time: the card is created AFTER the handles are wired.
                sizeCard = horizontal ? DesignLeafCardOf(target, node) : null;
                start = horizontal ? evt.position.x : evt.position.y;
                // An Asset Preview's intended size lives in node.PreviewSize (a square thumbnail), NOT in
                // node.Style — so if the element was built on an inactive tab (worldBound == 0) the Style
                // fallbacks below would start a resize from the 40/100 default. Seed from PreviewSize first.
                float previewFallback = node is BindingNode pbn && pbn.DisplayType == DisplayType.AssetPreview && pbn.PreviewSize > 1f
                    ? pbn.PreviewSize : 0f;
                if (horizontal)
                    startSize = target.worldBound.width > 1f ? target.worldBound.width
                        : node.Style.width.isOverridden ? node.Style.width.value
                        : previewFallback > 1f ? previewFallback : 100f;
                else
                    startSize = target.worldBound.height > 1f ? target.worldBound.height
                        : node.Style.height.isOverridden ? node.Style.height.value
                        : previewFallback > 1f ? previewFallback : 40f;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                float cur = horizontal ? evt.position.x : evt.position.y;
                float raw = startSize + growSign * (cur - start);
                var s = node.Style;
                if (horizontal)
                {
                    // Width stays a fixed size: clamp so you can't drag past the inspector edge. The bound is
                    // the CONTAINER's width — in design mode that is the card's parent, never the element's,
                    // because the card sits between them carrying a copy of this node's own width. Measuring
                    // target.parent there returned the node's current width as its own maximum, so the grip
                    // could only ever shrink a widget in the canvas and never grow it.
                    var widthParent = (sizeCard ?? target).parent;
                    float max = widthParent != null && widthParent.worldBound.width > 1f ? widthParent.worldBound.width : 100000f;
                    raw = Mathf.Clamp(raw, NodeResizeMinSize, max);
                    var w = s.width; w.isOverridden = true; w.value = raw; s.width = w;
                    target.style.width = raw;
                    if (sizeCard != null) sizeCard.style.width = raw; // the box the canvas lays out
                }
                else
                {
                    // Height is a GROW-ONLY floor (min-height), mirroring AttachRowContainerResizer: the grip
                    // adds room but can never squish the node below its natural content height. Dragging back
                    // up past NodeResizeMinSize clears it (return to auto/natural). The stored height drives a
                    // min-height in StyleApplier (gated on MakeHeightResizable), so this persists correctly.
                    if (raw <= NodeResizeMinSize)
                    {
                        var h = s.height; h.isOverridden = false; h.value = 0f; s.height = h;
                        MinHeightFloors.SetNode(target, 0f); // arbitrated: a row floor on this element survives
                    }
                    else
                    {
                        float max = target.panel != null && target.panel.visualTree.worldBound.height > 1f ? target.panel.visualTree.worldBound.height : 100000f;
                        raw = Mathf.Min(raw, max);
                        var h = s.height; h.isOverridden = true; h.value = raw; s.height = h;
                        MinHeightFloors.SetNode(target, raw);
                    }
                }
                node.Style = s;
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (handle.HasPointerCapture(evt.pointerId)) handle.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke();
                evt.StopPropagation();
            });
            // Capture-loss without a PointerUp must still persist the live edit (see other resizers).
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        // The drag handle under a row (the Rows analog of MakeColumnDivider). A faint full-width horizontal
        // line brightens on hover; the resize-vertical cursor comes from USS (.sasq-row-resizer). alignSelf:
        // Stretch keeps it full-width even when the container aligns its fields (alignItems). Wire via
        // AttachRowContainerResizer.
        private static VisualElement MakeRowDivider()
        {
            var divider = new VisualElement();
            divider.AddToClassList("sasq-row-resizer");
            divider.style.height = RowDividerHeight;
            divider.style.flexGrow = 0;
            divider.style.flexShrink = 0;
            divider.style.alignSelf = Align.Stretch;
            divider.style.flexDirection = FlexDirection.Row;
            divider.style.alignItems = Align.Center;
            divider.style.justifyContent = Justify.Center;
            divider.tooltip = "Drag to resize row height";

            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.height = 1;
            line.style.width = Length.Percent(97);
            var rowIdle = WidgetPalette.RowLineIdle;   // captured: callbacks stay on the built skin
            var rowHot = WidgetPalette.GripHot;
            line.style.backgroundColor = rowIdle;
            divider.Add(line);

            divider.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (DragInProgress(divider)) return; // mid-drag: never read as a drop target
                line.style.height = 2;
                line.style.backgroundColor = rowHot;
            });
            divider.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                line.style.height = 1;
                line.style.backgroundColor = rowIdle;
            });
            return divider;
        }

        // Drag-to-resize for a Rows container: dragging the divider sets the row above it to an explicit
        // min-height (live, no rebuild), stored on the node's RowHeight and persisted once on release via
        // onLayoutEdited. Dragging back up past RowResizeMinHeight clears it (auto/natural). Mirrors the
        // text-box bottom-edge resize (AttachTextResizeDrag).
        private static void AttachRowContainerResizer(VisualElement divider, VisualElement rowEl, LayoutNode rowNode,
            System.Action onLayoutEdited, bool designMode)
        {
            bool resizing = false;
            float startY = 0f, startHeight = 0f;
            float lastApplied = float.NaN; // last height actually written, for the sub-pixel guard below

            // The element the reserved height lives on. Created on first drag for a row that had no stored
            // height (so an untouched layout carries no extra elements), and stable for the whole gesture.
            VisualElement FloorTarget() => RowFloorHost.Ensure(rowEl, NeedsFloorHost(rowNode, designMode));


            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                lastApplied = float.NaN;
                var parent = divider.parent;
                startY = parent != null ? parent.WorldToLocal(evt.position).y : evt.position.y;
                // Measure the HOST: it is what occupies the row's slot once a height is reserved.
                var target = FloorTarget();
                startHeight = target != null && target.layout.height > 1f
                    ? target.layout.height
                    : (rowNode.RowHeight > 0f ? rowNode.RowHeight : 24f);
                divider.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                var parent = divider.parent;
                float currentY = parent != null ? parent.WorldToLocal(evt.position).y : evt.position.y;
                float raw = startHeight + (currentY - startY);
                // Below the minimum the row returns to auto (natural), which the model stores as 0.
                float target = raw <= RowResizeMinHeight ? 0f : raw;
                // Sub-pixel pointer jitter costs a whole-inspector relayout for no visible change — skip it.
                if (!float.IsNaN(lastApplied) && Mathf.Abs(target - lastApplied) < RowResizeEpsilon)
                {
                    evt.StopPropagation();
                    return;
                }
                lastApplied = target;
                MinHeightFloors.SetRow(FloorTarget(), target);
                rowNode.RowHeight = target;
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (divider.HasPointerCapture(evt.pointerId)) divider.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke(); // persist: designer marks dirty + re-renders; inspector SetDirty's the asset
                evt.StopPropagation();
            });

            // Capture-loss (window blur / focus steal / Esc) without a PointerUp must still persist the
            // live model edit, or the resize silently reverts on the next rebuild-from-disk.
            divider.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        // Lays a Grid out as a COLUMN stack of explicit Row containers. Cells are sized by flex weight
        // (responsive — no pixel math), each row's height is the row element's own (optional) min-height, and
        // dividers sit IN-FLOW between cells / rows. So there is no flex-wrap to overflow, no absolute handles to
        // drift, and cell heights synchronize per row. The cells were already built (flat) into contentTarget by
        // the shared children loop; this only regroups them, leaving their design cards / drag manipulators /
        // bindings intact.
        //
        // Fixed constraints have a stable column COUNT, so they group once and let flex handle width changes.
        // Flexible derives its column count from the available width, so it re-groups on width change (only when
        // the count actually changes) and gets no drag-resize handles (a cell's column index isn't stable).
        private static void ApplyGridLayout(VisualElement contentTarget, ContainerNode node, System.Action onLayoutEdited, bool designMode)
        {
            if (node == null) return;

            if (node.GridConstraint == GridConstraint.Flexible)
            {
                int lastCols = -1;
                contentTarget.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    float w = evt.newRect.width;
                    if (w <= 0f) return;
                    if (CountGridCells(contentTarget) == 0) return;
                    const float minWidth = 150f;
                    int cols = Mathf.Max(1, Mathf.FloorToInt((w + node.Gap) / (minWidth + node.Gap)));
                    if (cols == lastCols) return; // column count unchanged — flex already reflows the widths
                    lastCols = cols;
                    RegroupGridIntoRows(contentTarget, node, CollectGridCells(contentTarget), cols, false, false, onLayoutEdited, designMode);
                });
                return;
            }

            var cells = CollectGridCells(contentTarget);
            if (cells.Count == 0) return;
            int fixedCols = node.GridConstraint == GridConstraint.FixedColumnCount
                ? Mathf.Max(1, node.GridColumns)
                : Mathf.Max(1, Mathf.CeilToInt(cells.Count / (float)Mathf.Max(1, node.GridColumns))); // FixedRowCount
            // A fixed ROW count reserves its declared tracks in design mode, exactly like a fixed
            // column count does: an under-filled grid still shows every row, the open tracks as
            // "+" pads, so the shape you configured is the shape you see (and drop into). Live
            // mode never pads — an empty reserved row would just be dead vertical space.
            int minRows = designMode && node.GridConstraint == GridConstraint.FixedRowCount
                ? Mathf.Max(1, node.GridColumns) : 1;
            RegroupGridIntoRows(contentTarget, node, cells, fixedCols,
                node.GridResizableColumns, node.GridResizableRows, onLayoutEdited, designMode, minRows);
        }

        // Pulls the grid's field cells out of contentTarget in visual order, discarding any prior row wrappers,
        // dividers and fillers, and detaches the cells so RegroupGridIntoRows can re-parent them cleanly. Handles
        // both the initial flat state (cells are direct children) and an already-grouped state (cells live inside
        // .sasq-grid-row wrappers) — the latter happens when a Flexible grid re-groups on a width change.
        private static List<VisualElement> CollectGridCells(VisualElement contentTarget)
        {
            var cells = new List<VisualElement>();
            foreach (var child in new List<VisualElement>(contentTarget.Children()))
            {
                if (child.ClassListContains("sasq-grid-row"))
                {
                    foreach (var rc in new List<VisualElement>(child.Children()))
                        if (rc.userData is LayoutNode) cells.Add(rc);
                    contentTarget.Remove(child);
                }
                else if (child.userData is LayoutNode)
                {
                    cells.Add(child);
                }
                else if (child.ClassListContains("sasq-grid-row-resizer"))
                {
                    contentTarget.Remove(child); // stray row divider from a previous grouping
                }
                // Anything else (the flow header lives on the wrapper, not here; badges; the hint) is left alone.
            }
            foreach (var cell in cells) cell.RemoveFromHierarchy();
            return cells;
        }

        // Counts the grid's field cells without mutating the tree (used by the Flexible re-group guard).
        private static int CountGridCells(VisualElement contentTarget)
        {
            int n = 0;
            foreach (var child in contentTarget.Children())
            {
                if (child.ClassListContains("sasq-grid-row"))
                {
                    foreach (var rc in child.Children())
                        if (rc.userData is LayoutNode) n++;
                }
                else if (child.userData is LayoutNode) n++;
            }
            return n;
        }

        // Builds `rows` Row containers under contentTarget from the flat `cells`, `cols` per row. Cells are sized
        // by column flex weight (shared across rows via GridColumnWeights); the last, underfilled row is padded
        // with weightless fillers so its columns line up with the rows above. When resizable, an in-flow divider
        // between columns / rows carries the drag-to-resize handle; a row's height comes from the row element's
        // own min-height (GridRowHeights), so a row can never overflow its container or leave dead space.
        private static void RegroupGridIntoRows(VisualElement contentTarget, ContainerNode node,
            List<VisualElement> cells, int cols, bool resizableCols, bool resizableRows, System.Action onLayoutEdited,
            bool designMode, int minRows = 1)
        {
            int count = cells.Count;
            if (count == 0) return;
            cols = Mathf.Max(1, cols);
            int rows = Mathf.Max(Mathf.Max(1, minRows), Mathf.CeilToInt(count / (float)cols));
            float gap = node.Gap;

            var weights = node.GridColumnWeights;
            // A stored weight only drives width when the grid is resizable-columns; otherwise columns are equal.
            // A missing/zero entry counts as weight 1, so an empty list ⇒ equal columns.
            float WeightFor(int c) => (resizableCols && weights != null && c < weights.Count && weights[c] > 0f) ? weights[c] : 1f;

            var rowHeights = node.GridRowHeights;
            // Per-column cell lists so a column resize can update every row's cell in that column live.
            var cellsByCol = new List<VisualElement>[cols];
            for (int c = 0; c < cols; c++) cellsByCol[c] = new List<VisualElement>();

            // One CLEAN line per column boundary: every row hosts its own drag handle
            // (each drives the shared weight), but the default centered 70%-height lines read as a
            // stack of broken dashes. Each segment stretches to its full row height and bridges
            // down across the inter-row space, fusing into a single continuous line; hovering any
            // handle lights the WHOLE boundary. Joins overlap by design — same x, same colour.
            float rowBridge = resizableRows ? RowDividerHeight : Mathf.Max(gap, 6f);
            float fusedDivW = gap > TableDividerWidth ? gap : TableDividerWidth;
            var boundaryDivs = new List<VisualElement>[cols];
            for (int c = 1; c < cols; c++) boundaryDivs[c] = new List<VisualElement>();

            for (int r = 0; r < rows; r++)
            {
                var rowEl = new VisualElement();
                rowEl.AddToClassList("sasq-grid-row");
                rowEl.style.flexDirection = FlexDirection.Row;
                rowEl.style.alignItems = Align.Stretch; // cells match the row's height

                if (resizableRows && rowHeights != null && r < rowHeights.Count && rowHeights[r] > 0f)
                    rowEl.style.minHeight = rowHeights[r];
                // Inter-row spacing comes from the resize divider when resizable; otherwise from a gap margin.
                if (!resizableRows && gap > 0f && r < rows - 1)
                    rowEl.style.marginBottom = gap;

                for (int c = 0; c < cols; c++)
                {
                    if (c > 0)
                    {
                        var colDiv = MakeColumnDivider(interactive: resizableCols);
                        colDiv.AddToClassList("sasq-grid-col-resizer");
                        if (gap > TableDividerWidth) colDiv.style.width = gap;
                        rowEl.Add(colDiv);
                        if (resizableCols)
                            AttachGridColumnDivider(colDiv, node, c - 1, cellsByCol, onLayoutEdited);
                        if (colDiv.childCount > 0)
                        {
                            // Fuse this row's segment into the boundary's continuous line.
                            var seg = colDiv[0];
                            seg.style.position = Position.Absolute;
                            seg.style.top = 0;
                            seg.style.bottom = r == rows - 1 ? 0f : -rowBridge;
                            seg.style.height = StyleKeyword.Null;
                            seg.style.left = (fusedDivW - 1f) * 0.5f;
                            boundaryDivs[c].Add(colDiv);
                        }
                    }

                    int idx = r * cols + c;
                    if (idx < count)
                    {
                        var cell = cells[idx];
                        cell.style.flexGrow = WeightFor(c);
                        cell.style.flexBasis = 0f;
                        cell.style.flexShrink = 1f;           // pin: EVERY row member shrinks identically
                        cell.style.minWidth = 0;              // allow shrinking to share the row (flex default is auto)
                        cell.style.width = StyleKeyword.Null; // drop any stale pixel width from the old grid model
                        rowEl.Add(cell);
                        cellsByCol[c].Add(cell);
                    }
                    else
                    {
                        // Design mode renders the pad cell as a visible "+" drop slot (the drop engine
                        // highlights it as the append target); preview keeps an invisible filler.
                        // The flex profile is pinned IDENTICALLY to the real cells — a pad whose
                        // shrink OR basis floor differs makes its row's column boundary drift off
                        // the rows above (a Vec2 cell ending further right than the Float above it).
                        // Yoga floors a flex item's base size at its own padding+border EVEN with an
                        // explicit basis of 0 — and that floor is NOT a constant: designer cards keep
                        // a 20px grip gutter (a 10px boundary drift per pad), frame-hosted cards
                        // strip it, and the live inspector's raw fields have none. So mirror a real
                        // cell's RESOLVED floor onto the pad once layout has computed it, split
                        // symmetrically so the "+" stays centered. Idempotent — the corrective
                        // pass converges.
                        var filler = designMode ? MakeEmptyDropSlot(ContainerType.Grid) : new VisualElement();
                        filler.style.flexGrow = WeightFor(c);
                        filler.style.flexBasis = 0f;
                        filler.style.flexShrink = 1f;
                        filler.style.minWidth = 0;
                        filler.style.paddingLeft = designMode ? 10 : 0;  // pre-seed the designer-card case
                        filler.style.paddingRight = designMode ? 10 : 0; // (20px gutter + 2px hairlines − pad borders)
                        var floorRef = cells[0]; // col-0 of row 0 is always a real cell
                        var pad = filler;
                        pad.RegisterCallback<GeometryChangedEvent>(_ =>
                        {
                            var cs = floorRef.resolvedStyle;
                            var ps = pad.resolvedStyle;
                            float cellFloor = cs.paddingLeft + cs.paddingRight
                                              + cs.borderLeftWidth + cs.borderRightWidth;
                            float want = Mathf.Max(0f,
                                (cellFloor - ps.borderLeftWidth - ps.borderRightWidth) * 0.5f);
                            if (Mathf.Abs(ps.paddingLeft - want) > 0.5f ||
                                Mathf.Abs(ps.paddingRight - want) > 0.5f)
                            {
                                pad.style.paddingLeft = want;
                                pad.style.paddingRight = want;
                            }
                        });
                        rowEl.Add(filler);
                        // The filler is a real column member for WIDTH purposes: register it so a column resize
                        // rewrites its flexGrow too. Omitting it leaves an underfilled row's column on the stale
                        // weight, so that row's divider tracks the drag sub-linearly (the "lags at ~50%" bug).
                        cellsByCol[c].Add(filler);
                    }
                }

                contentTarget.Add(rowEl);

                if (resizableRows && r < rows - 1)
                {
                    var rowDiv = MakeRowDivider();
                    rowDiv.AddToClassList("sasq-grid-row-resizer");
                    contentTarget.Add(rowDiv);
                    AttachGridRowDivider(rowDiv, rowEl, node, r, onLayoutEdited);
                }
            }

            // Cells stacked in the same COLUMN share ONE label gutter, so their fields start at the same x
            // (see ColumnLabelGroup — a grid was the first layout to put two hugging labels above one
            // another, and they each hugged their own word). Registered here because this is the moment a
            // cell learns which column it is in, and it re-runs on every regroup, so a Flexible grid
            // re-flowing on a width change re-forms its runs instead of aligning to the old shape. The
            // pad fillers in `cellsByCol` carry no label and are skipped.
            for (int c = 0; c < cols; c++) RegisterCellLabelRun(cellsByCol[c]);

            // Whole-boundary hover: the fused line is ONE affordance — hovering any row's handle
            // lights every segment of that boundary (they all drive the same shared weight).
            // Runs beside MakeColumnDivider's own per-segment hover, writing the same colours.
            if (resizableCols)
            {
                for (int c = 1; c < cols; c++)
                {
                    var segs = boundaryDivs[c];
                    if (segs.Count < 2) continue; // single row: the built-in hover already covers it
                    foreach (var d in segs)
                    {
                        var self = d;
                        d.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            if (DragInProgress(self)) return;
                            foreach (var o in segs)
                                if (o.childCount > 0)
                                {
                                    o[0].style.width = 2;
                                    o[0].style.backgroundColor = WidgetPalette.GripHot;
                                }
                        });
                        d.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            foreach (var o in segs)
                                if (o.childCount > 0)
                                {
                                    o[0].style.width = 1;
                                    o[0].style.backgroundColor = WidgetPalette.GripLineIdle;
                                }
                        });
                    }
                }
            }
        }

        // "Rows: N" cap overflow — regroups the flat cells into SIDE-BY-SIDE column stacks (children
        // fill top-to-bottom, then wrap right: the vertical transpose of the Columns cap's grid
        // regroup). The stacks live inside an inner band row because a Rows container's contentTarget
        // is the container root itself (it also hosts the title header and design chrome) — flipping
        // its flex direction would drag the title into the row. The under-filled last column is
        // closed with "+" pad slots in design mode so the reserved tracks stay visible. Handles the
        // flat state and a previously-banded state (in-place rebuilds always build flat, but stay
        // defensive like CollectGridCells).
        private static void RegroupRowsIntoColumns(VisualElement contentTarget, ContainerNode node, bool designMode,
            bool resizable, System.Action onLayoutEdited)
        {
            var cells = new List<VisualElement>();
            foreach (var child in new List<VisualElement>(contentTarget.Children()))
            {
                if (child.ClassListContains("sasq-rows-colband"))
                {
                    foreach (var col in new List<VisualElement>(child.Children()))
                        foreach (var rc in new List<VisualElement>(col.Children()))
                            if (rc.userData is LayoutNode) cells.Add(rc);
                    contentTarget.Remove(child);
                }
                else if (child.userData is LayoutNode)
                {
                    cells.Add(child);
                }
                else if (child.ClassListContains("sasq-row-resizer"))
                {
                    contentTarget.Remove(child); // stray divider from a previous under-cap build
                }
            }
            foreach (var cell in cells) cell.RemoveFromHierarchy();
            if (cells.Count == 0) return;

            int rowsPer = Mathf.Max(1, node.GridColumns);
            int colCount = Mathf.Max(1, Mathf.CeilToInt(cells.Count / (float)rowsPer));
            float gap = Mathf.Max(0f, node.Gap);

            var band = new VisualElement();
            band.AddToClassList("sasq-rows-colband");
            band.style.flexDirection = FlexDirection.Row;
            band.style.flexWrap = Wrap.NoWrap;

            // Track r spans EVERY column (cells and "+" pads alike): resizing is per-TRACK, not
            // per-cell — dragging any column's divider for track r resizes the whole track so the
            // columns stay uniform (the AttachGridColumnDivider shared-weight pattern, transposed).
            // Captured by reference: the resizers read them at drag time, when all columns exist.
            var trackEls = new List<VisualElement>[rowsPer];
            var trackNodes = new List<LayoutNode>[rowsPer];
            var trackDivs = new List<VisualElement>[rowsPer]; // per-track handles, for the fused-line hover
            for (int r = 0; r < rowsPer; r++)
            {
                trackEls[r] = new List<VisualElement>();
                trackNodes[r] = new List<LayoutNode>();
                trackDivs[r] = new List<VisualElement>();
            }

            for (int col = 0; col < colCount; col++)
            {
                var colEl = new VisualElement();
                colEl.AddToClassList("sasq-rows-col");
                colEl.style.flexDirection = FlexDirection.Column;
                colEl.style.flexGrow = 1f;
                colEl.style.flexBasis = 0f;
                colEl.style.minWidth = 0;
                // Breathing room between the stacks even at Gap 0 — butted columns read as one pile.
                if (col > 0) colEl.style.marginLeft = gap > 0f ? gap : 6f;

                // Design mode always renders the full track count ("+" pads close the last column);
                // live mode stops at the real children.
                int colItems = designMode ? rowsPer : Mathf.Min(rowsPer, cells.Count - col * rowsPer);
                // This stack's members, for the shared label gutter below — the transpose of the grid's
                // cellsByCol. A Rows container over its cap puts hugging cells above one another exactly
                // the way a grid column does, so it has the same ragged-label problem and the same fix.
                var stackCells = new List<VisualElement>();
                for (int r = 0; r < colItems; r++)
                {
                    int idx = col * rowsPer + r;
                    var cell = idx < cells.Count ? cells[idx] : MakeEmptyDropSlot(ContainerType.Rows);
                    if (gap > 0f && r < colItems - 1)
                    {
                        // Mirror ApplyGap: add to any node-authored inline margin instead of replacing it.
                        float current = cell.style.marginBottom.keyword == StyleKeyword.Null ||
                                        cell.style.marginBottom.keyword == StyleKeyword.Undefined
                            ? 0f : cell.style.marginBottom.value.value;
                        cell.style.marginBottom = current + gap;
                    }
                    colEl.Add(cell);
                    stackCells.Add(cell);
                    trackEls[r].Add(cell);
                    if (idx < cells.Count && cell.userData is LayoutNode cellNode) trackNodes[r].Add(cellNode);

                    // A divider under each real cell; each drives the SHARED track height. Pads get
                    // no divider (nothing of their own to resize) but ride the track's floor.
                    // Gated on `resizable` like ApplyRowsLayout: no handle, no unchangeable height.
                    if (resizable && idx < cells.Count && cell.userData is LayoutNode)
                    {
                        var divider = MakeRowDivider();
                        colEl.Add(divider);
                        AttachRowsTrackResizer(divider, trackEls[r], trackNodes[r], onLayoutEdited, designMode);
                        if (divider.childCount > 0)
                        {
                            // Fuse this column's segment into the track's continuous line (the
                            // column-boundary treatment in RegroupGridIntoRows, transposed): full
                            // column width, bridged right across the inter-column margin whenever
                            // the next column carries this track.
                            var seg = divider[0];
                            bool bridgeRight = col < colCount - 1
                                && (designMode || (col + 1) * rowsPer + r < cells.Count);
                            seg.style.position = Position.Absolute;
                            seg.style.left = 0;
                            seg.style.right = bridgeRight ? -(gap > 0f ? gap : 6f) : 0f;
                            seg.style.width = StyleKeyword.Null;
                            seg.style.top = (RowDividerHeight - 1f) * 0.5f;
                            trackDivs[r].Add(divider);
                        }
                    }
                }
                RegisterCellLabelRun(stackCells);
                band.Add(colEl);
            }

            // Build-time floors, per TRACK: the tallest stored RowHeight in a track floors every
            // cell AND pad in it, so a saved resize renders uniform columns from the first frame.
            if (resizable)
            {
                for (int r = 0; r < rowsPer; r++)
                {
                    float floor = 0f;
                    foreach (var n in trackNodes[r]) floor = Mathf.Max(floor, n.RowHeight);
                    if (floor > 0f)
                        foreach (var el in trackEls[r]) ApplyRowFloor(el, floor, designMode); // hosts live leaf cells — see ApplyRowsLayout
                }

                // Whole-track hover: the fused line is ONE affordance — hovering any column's
                // handle lights every segment of that track (they all drive the shared height).
                for (int r = 0; r < rowsPer; r++)
                {
                    var segs = trackDivs[r];
                    if (segs.Count < 2) continue; // single-column track: built-in hover covers it
                    foreach (var d in segs)
                    {
                        var self = d;
                        d.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            if (DragInProgress(self)) return;
                            foreach (var o in segs)
                                if (o.childCount > 0)
                                {
                                    o[0].style.height = 2;
                                    o[0].style.backgroundColor = WidgetPalette.GripHot;
                                }
                        });
                        d.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            foreach (var o in segs)
                                if (o.childCount > 0)
                                {
                                    o[0].style.height = 1;
                                    o[0].style.backgroundColor = WidgetPalette.GripLineIdle;
                                }
                        });
                    }
                }
            }
            contentTarget.Add(band);
        }

        // Shared-track row resize for a capped Rows container's column stacks: dragging the divider
        // under ANY column's cell in track r sets the min-height of EVERY cell/pad in that track and
        // persists RowHeight on every node in it (so the track re-floors uniformly on rebuild, and a
        // later cap change leaves each child with a sane individual height). Mirrors
        // AttachRowContainerResizer's feel: drag up past RowResizeMinHeight returns the track to auto.
        private static void AttachRowsTrackResizer(VisualElement divider, List<VisualElement> trackEls,
            List<LayoutNode> trackNodes, System.Action onLayoutEdited, bool designMode)
        {
            bool resizing = false;
            float startY = 0f, startHeight = 0f;
            float lastApplied = float.NaN; // last height actually written, for the sub-pixel guard below

            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                lastApplied = float.NaN;
                var parent = divider.parent;
                startY = parent != null ? parent.WorldToLocal(evt.position).y : evt.position.y;
                // The track's effective height is its tallest member (alignItems keeps the rest visually level).
                startHeight = 0f;
                // Measure the HOST where one exists — it is what occupies the cell's slot once floored.
                foreach (var el in trackEls)
                    startHeight = Mathf.Max(startHeight, (RowFloorHost.Of(el) ?? el).layout.height);
                if (startHeight <= 1f)
                {
                    foreach (var n in trackNodes) startHeight = Mathf.Max(startHeight, n.RowHeight);
                    if (startHeight <= 1f) startHeight = 24f;
                }
                divider.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                var parent = divider.parent;
                float currentY = parent != null ? parent.WorldToLocal(evt.position).y : evt.position.y;
                float raw = startHeight + (currentY - startY);
                // Below the minimum the whole track returns to auto (natural), which the model stores as 0.
                float target = raw <= RowResizeMinHeight ? 0f : raw;
                // A track drives EVERY cell in it, so the jitter guard matters even more here than on a row.
                if (!float.IsNaN(lastApplied) && Mathf.Abs(target - lastApplied) < RowResizeEpsilon)
                {
                    evt.StopPropagation();
                    return;
                }
                lastApplied = target;
                foreach (var el in trackEls) ApplyRowFloor(el, target, designMode); // hosts live leaf cells — see ApplyRowsLayout
                foreach (var n in trackNodes) n.RowHeight = target;
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (divider.HasPointerCapture(evt.pointerId)) divider.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke();
                evt.StopPropagation();
            });

            // Capture-loss (window blur / focus steal / Esc) without a PointerUp must still persist the
            // live model edit, or the resize silently reverts on the next rebuild-from-disk.
            divider.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        // Column resize for a Grid: redistributes weight between column `leftCol` and `leftCol + 1` (conserving
        // the pair's total width + weight, so other columns hold still) and applies it to EVERY row's cell in
        // those two columns, live; persists on release. There's one divider per row for the same boundary — each
        // drives the same shared weight, so dragging any of them resizes the whole column. cellsByCol is captured
        // by reference and read at drag time, by when every row has been added.
        private static void AttachGridColumnDivider(VisualElement divider, ContainerNode node, int leftCol,
            List<VisualElement>[] cellsByCol, System.Action onLayoutEdited)
        {
            int rightCol = leftCol + 1;
            bool resizing = false;
            float startX = 0f, wL0 = 0f, wR0 = 0f, gL0 = 1f, gR0 = 1f;

            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (node.GridColumnWeights == null) node.GridColumnWeights = new List<float>();
                while (node.GridColumnWeights.Count <= rightCol) node.GridColumnWeights.Add(1f);
                resizing = true;
                startX = evt.position.x;
                wL0 = cellsByCol[leftCol].Count > 0 ? cellsByCol[leftCol][0].worldBound.width : 0f;
                wR0 = cellsByCol[rightCol].Count > 0 ? cellsByCol[rightCol][0].worldBound.width : 0f;
                gL0 = node.GridColumnWeights[leftCol] > 0f ? node.GridColumnWeights[leftCol] : 1f;
                gR0 = node.GridColumnWeights[rightCol] > 0f ? node.GridColumnWeights[rightCol] : 1f;
                divider.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                var (gL, gR) = ComputeResizedWeights(wL0, wR0, evt.position.x - startX, gL0, gR0, ColumnContainerMinWidth);
                node.GridColumnWeights[leftCol] = gL;
                node.GridColumnWeights[rightCol] = gR;
                foreach (var cell in cellsByCol[leftCol]) cell.style.flexGrow = gL;
                foreach (var cell in cellsByCol[rightCol]) cell.style.flexGrow = gR;
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (divider.HasPointerCapture(evt.pointerId)) divider.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke();
                evt.StopPropagation();
            });

            // Capture-loss (window blur / focus steal / Esc) without a PointerUp must still persist the
            // live model edit, or the resize silently reverts on the next rebuild-from-disk.
            divider.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        // Row resize for a Grid: dragging the divider under row `row` sets THAT row element's min-height (drag
        // back up past RowResizeMinHeight ⇒ auto), live, persisting on release. Because the height lives on the
        // row container (which wraps the cells), the row sizes to max(content, min-height) — it can never overflow
        // and never leaves dead space. Mirrors AttachRowContainerResizer.
        private static void AttachGridRowDivider(VisualElement divider, VisualElement rowEl, ContainerNode node,
            int row, System.Action onLayoutEdited)
        {
            bool resizing = false;
            float startY = 0f, startHeight = 0f;

            divider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (node.GridRowHeights == null) node.GridRowHeights = new List<float>();
                while (node.GridRowHeights.Count <= row) node.GridRowHeights.Add(0f);
                resizing = true;
                var parent = divider.parent;
                startY = parent != null ? parent.WorldToLocal(evt.position).y : evt.position.y;
                startHeight = rowEl.layout.height > 1f
                    ? rowEl.layout.height
                    : (node.GridRowHeights[row] > 0f ? node.GridRowHeights[row] : 24f);
                divider.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                var parent = divider.parent;
                float currentY = parent != null ? parent.WorldToLocal(evt.position).y : evt.position.y;
                float raw = startHeight + (currentY - startY);
                if (raw <= RowResizeMinHeight)
                {
                    rowEl.style.minHeight = StyleKeyword.Null; // back to auto/natural
                    node.GridRowHeights[row] = 0f;
                }
                else
                {
                    rowEl.style.minHeight = raw;
                    node.GridRowHeights[row] = raw;
                }
                evt.StopPropagation();
            });

            divider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (divider.HasPointerCapture(evt.pointerId)) divider.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke();
                evt.StopPropagation();
            });

            // Capture-loss (window blur / focus steal / Esc) without a PointerUp must still persist the
            // live model edit, or the resize silently reverts on the next rebuild-from-disk.
            divider.RegisterCallback<PointerCaptureOutEvent>(_ => { if (resizing) { resizing = false; onLayoutEdited?.Invoke(); } });
        }

        // Builds a "toggle group" Foldout: a self-contained, Particle-System-style header bar (a left-pinned
        // expand chevron + a bound checkbox and name aligned left/center/right) over a content area that greys
        // out (disables) when the bound bool is false. The whole bar expands/collapses; clicking the checkbox
        // toggles the bool without expanding. The bound bool IS the group's enable state — no separate field
        // (see ContainerNode.GroupTogglePath). Returns the group root; outputs the content container so the
        // shared children loop fills it like any other container.
        private static VisualElement BuildToggleGroup(
            ContainerNode node,
            string basePath,
            SerializedObject serializedObject,
            bool designMode,
            bool designInteract,
            out VisualElement contentTarget)
        {
            var group = new VisualElement();
            group.AddToClassList("sasq-group");

            // Header bar — the whole bar is the expand/collapse surface.
            var header = new VisualElement();
            header.AddToClassList("sasq-group-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, WidgetPalette.ChevronDim);
            chevron.AddToClassList("sasq-group-chevron");
            header.Add(chevron);

            // Align region holds the checkbox + name and shifts them left / center / right.
            var alignRegion = new VisualElement();
            alignRegion.AddToClassList("sasq-group-align");
            alignRegion.style.flexDirection = FlexDirection.Row;
            alignRegion.style.alignItems = Align.Center;
            alignRegion.style.flexGrow = 1;
            alignRegion.pickingMode = PickingMode.Ignore; // empty space falls through to the header (expand)
            switch (node.GroupToggleAlignment)
            {
                case GroupToggleAlignment.Center: alignRegion.style.justifyContent = Justify.Center; break;
                case GroupToggleAlignment.Right: alignRegion.style.justifyContent = Justify.FlexEnd; break;
                default: alignRegion.style.justifyContent = Justify.FlexStart; break;
            }

            var toggle = new Toggle();
            toggle.AddToClassList("sasq-group-toggle");
            // Clicking the checkbox toggles the bool only — its events never reach the header's expand
            // handler. Left button only: right-clicks belong to the unified quick menu / designer menus.
            toggle.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) evt.StopPropagation(); });
            toggle.RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0) evt.StopPropagation(); });
            toggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            // Design select-only: make the checkbox inert (recursively, so its checkmark child can't catch the
            // press either) so a click neither flips the bound bool nor swallows the press — it falls through
            // (alignRegion + header are pickingMode-Ignore/guarded) to SELECT the group.
            if (designMode && !designInteract) DisableInteractivityRecursively(toggle);
            alignRegion.Add(toggle);

            // Resolve + bind the bool; the name falls back to the bool's display name when there's no Title.
            string resolvedTogglePath = string.IsNullOrEmpty(basePath)
                ? node.GroupTogglePath
                : $"{basePath}.{node.GroupTogglePath}";
            var toggleProp = serializedObject.FindProperty(resolvedTogglePath);

            string titleText = !string.IsNullOrEmpty(node.Title)
                ? node.Title
                : (toggleProp != null ? toggleProp.displayName : "Group");
            var titleLabel = new Label(titleText);
            titleLabel.AddToClassList("sasq-group-title");
            titleLabel.AddToClassList("sasq-node-title"); // marker, as on every other container title
            titleLabel.pickingMode = PickingMode.Ignore;
            alignRegion.Add(titleLabel);

            header.Add(alignRegion);
            group.Add(header);

            var content = new VisualElement();
            content.AddToClassList("sasq-group-content");
            group.Add(content);

            if (toggleProp != null && toggleProp.propertyType == SerializedPropertyType.Boolean)
            {
                toggle.BindProperty(toggleProp);
                // SetEnabled hands the content the theme's `opacity: .5`, which UI Toolkit composites PER
                // ELEMENT — so any slider inside the group goes see-through (the track reads through the
                // knob). Re-home that dim in colour. It has to be called explicitly: SetEnabled changes no
                // layout, so no GeometryChangedEvent fires, and UI Toolkit exposes no enabled-changed event
                // — nothing downstream can notice the flip on its own. A no-op when the group holds no slider.
                SetGroupContentEnabled(content, toggleProp.boolValue);
                toggle.RegisterValueChangedCallback(evt => SetGroupContentEnabled(content, evt.newValue));
            }
            else if (!designMode)
            {
                // The toggle bool no longer exists in code (deleted/renamed while auto-sync hasn't run):
                // render as a plain foldout instead of a dead, unbound checkbox. The designer keeps the
                // checkbox visible so the stale GroupTogglePath stays discoverable.
                toggle.style.display = DisplayStyle.None;
            }

            // Whole-bar expand/collapse. In design mode the press must still bubble so the canvas can
            // select/drag the node; in live preview we stop it so a nested group's bar isn't also toggled.
            // Open-state is remembered per type, keyed by the bound bool path (stable). animate==true marks a
            // real user toggle (the initial seed uses animate==false), so that is when it persists.
            string togglePath = string.IsNullOrEmpty(node.GroupTogglePath)
                ? null
                : (string.IsNullOrEmpty(basePath) ? node.GroupTogglePath : basePath + "." + node.GroupTogglePath);
            string groupKey = designMode ? null : FoldoutStateStore.Key(serializedObject.targetObject, togglePath, node.Title);
            bool expanded = FoldoutStateStore.Get(groupKey, node.DefaultToOpen);
            void ApplyExpanded(bool animate)
            {
                EditorSpringFX.SpringCaret(chevron, expanded, animate);
                if (animate) { EditorSpringFX.Fold(content, expanded); FoldoutStateStore.Set(groupKey, expanded); }
                else content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ApplyExpanded(false);
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (designMode && !designInteract) return; // design select-only: don't expand/collapse, let it select
                expanded = !expanded;
                ApplyExpanded(!designMode);
                if (!designMode || designInteract) evt.StopPropagation();
            });

            // Search expander (site 3 of 5 — keep in lockstep): transient open/close without persistence;
            // Restore replays the user-owned `expanded` state.
            GroupExpanderRegistry.Register(group, new GroupExpander(
                open =>
                {
                    EditorSpringFX.SpringCaret(chevron, open, false);
                    content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                },
                () => ApplyExpanded(false)));

            contentTarget = content;
            return group;
        }

        /// <summary>
        /// True when this node's ROOT element is our collapsible group shell (<c>.sasq-group</c>). The shell's
        /// two children — <c>.sasq-group-header</c> and <c>.sasq-group-content</c> — carry opaque USS fills and
        /// cover the root edge to edge, so a <c>Style.backgroundColor</c> written onto the root by
        /// <see cref="StyleApplier"/> can never be seen. The designer uses this to hide the Background Color row
        /// rather than ship a control that does nothing (Section Tint is the one that works on a group — it
        /// paints the header/content, see <see cref="ApplySectionTint"/>).
        /// <para>
        /// Deliberately narrow: a container that is NOT collapsible, and a collection with Expandable off
        /// (which renders into <see cref="CreateCollectionFrame"/>, a frame that paints nothing), both still
        /// show their background — so the row stays for them.
        /// </para>
        /// Mirrors the three sites that build the shell: the <c>collapsible</c> gate in the ContainerNode
        /// branch, <see cref="BuildToggleGroup"/>, and the <c>node.Expandable</c> branch of the List and Table
        /// collection renderers. Keep in lockstep with all three.
        /// </summary>
        internal static bool RendersAsOpaqueGroupShell(LayoutNode node)
        {
            if (node is ContainerNode container)
            {
                if (container.Expandable
                    && (container.Type == ContainerType.Rows
                     || container.Type == ContainerType.Columns
                     || container.Type == ContainerType.Grid
                     || container.Type == ContainerType.Card))
                    return true;
                // A Foldout is a toggle GROUP (shell) iff a bool path is set; without one it stays a plain
                // Unity Foldout, which has no fill of its own and does show the node's background.
                return container.Type == ContainerType.Foldout
                    && !string.IsNullOrEmpty(container.GroupTogglePath);
            }

            if (node is CollectionNode collection)
                return collection.Expandable
                    && (collection.DisplayMode == CollectionDisplayMode.List
                     || collection.DisplayMode == CollectionDisplayMode.Table);

            return false;
        }

        // Base colors of the collapsible group frame — mirror --sasq-header-bg / --sasq-group-content-bg per
        // skin (WidgetPalette twins the USS tokens). The section tint is composited OVER these to an OPAQUE
        // result so a tinted group still reads as a solid panel (an inline translucent backgroundColor would
        // instead replace the USS color and let the inspector background bleed through).
        private static Color s_groupHeaderBase => WidgetPalette.GroupHeaderBase;
        private static Color s_groupContentBase => WidgetPalette.GroupContentBase;

        /// <summary>
        /// Color-codes a Foldout / group / card section: tints its header bar + body and draws a full-color
        /// accent rail down the left edge (the chosen "Header + body tint + accent rail" look). Called AFTER
        /// <see cref="StyleApplier.Apply"/> so nothing clobbers it. Dispatches by the root's actual shape so all
        /// four container render paths share one implementation without matching a NESTED section's frame:
        /// a <c>.sasq-group</c> shell (collapsible Rows/Columns/Grid/Card + toggle-group Foldout) tints its
        /// header/content opaquely over the panel base; a plain <see cref="Foldout"/> is dressed as that same
        /// rounded panel (opaque tinted header bar over a soft body wash — the group-shell recipe, so both
        /// collapsible looks read as ONE feature); anything else (a non-collapsible Card or bare container)
        /// tints itself.
        /// </summary>
        private static void ApplySectionTint(VisualElement root, ContainerNode node)
        {
            Color tint = node.SectionTintColor.value;

            // THE RAIL IS AN INSET STRIP ON EVERY ROUNDED ROOT, A BORDER ONLY ON A SQUARE ONE. It used to be
            // a plain left border set right here, for every path, with the plain Foldout below overwriting it
            // — which left the other two rounded shapes drawing a square-ended 3px bar beside a rounded card.
            // Each branch now owns its own rail; see EnsureInsetSectionRail for the measurements.

            if (root.ClassListContains("sasq-group"))
            {
                // Collapsible group shell OR toggle-group Foldout: header + content are this root's own children
                // (the first matches in pre-order, so a nested group's frame is never picked up here).
                var header = root.Q<VisualElement>(className: "sasq-group-header");
                var content = root.Q<VisualElement>(className: "sasq-group-content");
                if (header != null) header.style.backgroundColor = Opaque(Color.Lerp(s_groupHeaderBase, tint, 0.38f));
                if (content != null) content.style.backgroundColor = Opaque(Color.Lerp(s_groupContentBase, tint, 0.16f));
                // The shell's root carries NO radius — the visible 4px corners are on those two children — so
                // a border rail here is a straight bar standing beside a rounded card. Strip instead.
                EnsureInsetSectionRail(root, tint, GroupShellRadius);
                return;
            }

            if (root is Foldout foldout)
            {
                // Plain Unity Foldout — same recipe as the tinted group shell above (opaque header + body over
                // the skin's panel bases). The wash lives on the ROOT so it also spans the content's native
                // 15px indent; a wash on the content container alone leaves the header reading as a floating
                // strip disconnected from its body.
                var toggle = foldout.Q<Toggle>(className: "unity-foldout__toggle");
                Color headerBg = Opaque(Color.Lerp(s_groupHeaderBase, tint, 0.38f));
                Color bodyBg = Opaque(Color.Lerp(s_groupContentBase, tint, 0.16f));

                root.style.backgroundColor = bodyBg;
                root.style.borderTopLeftRadius = 4;
                root.style.borderTopRightRadius = 4;
                root.style.borderBottomLeftRadius = 4;
                root.style.borderBottomRightRadius = 4;
                // Uniform hairline ring, shaded toward the tint so the outline reads as part of the accent
                // instead of a hard near-black break against the washes. The rail is deliberately NOT the
                // left border here: border width interpolates around a rounded corner, so 3px→1px tapers
                // into a visible lump at both left corners — the inset strip added below keeps the ring
                // geometry clean.
                Color ring = Opaque(Color.Lerp(WidgetPalette.BorderStrong, tint, 0.35f));
                root.style.borderLeftWidth = 1;
                root.style.borderTopWidth = 1;
                root.style.borderRightWidth = 1;
                root.style.borderBottomWidth = 1;
                root.style.borderLeftColor = ring;
                root.style.borderTopColor = ring;
                root.style.borderRightColor = ring;
                root.style.borderBottomColor = ring;
                // .sasq-group's 4/8 separation so the panel doesn't sit flush on its neighbors; StyleApplier
                // already ran, so only the sides the user left unset are filled.
                if (!node.Style.marginTop.isOverridden) root.style.marginTop = 4;
                if (!node.Style.marginBottom.isOverridden) root.style.marginBottom = 8;

                if (toggle != null)
                {
                    toggle.style.backgroundColor = headerBg;
                    toggle.style.marginTop = 0;
                    toggle.style.marginBottom = 0;
                    toggle.style.marginLeft = 0;
                    toggle.style.marginRight = 0;
                    toggle.style.paddingTop = 3;
                    toggle.style.paddingBottom = 3;
                    toggle.style.paddingLeft = 5;
                    toggle.style.paddingRight = 6;
                    // Header corners tuck inside the root's 4px rounding. Children overpaint a parent's rounded
                    // corners (no clip), so the bottom pair rounds only while COLLAPSED — square corners would
                    // poke past the panel's curve when the bar is the whole panel; open, the bar sits flush on
                    // the body it reveals.
                    toggle.style.borderTopLeftRadius = 3;
                    toggle.style.borderTopRightRadius = 3;
                    void SyncHeaderBottomCorners(bool open)
                    {
                        float r = open ? 0f : 3f;
                        toggle.style.borderBottomLeftRadius = r;
                        toggle.style.borderBottomRightRadius = r;
                    }
                    SyncHeaderBottomCorners(foldout.value);
                    // Guard on evt.target: a NESTED foldout's value change also bubbles through here.
                    foldout.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.target == foldout) SyncHeaderBottomCorners(evt.newValue);
                    });

                    // Whole-bar hover at the theme's header-hover step — the inline background replaces any
                    // USS :hover, so it's hand-rolled.
                    Color hoverBg = Opaque(Color.Lerp(headerBg, WidgetPalette.IsLight ? Color.black : Color.white, 0.06f));
                    toggle.RegisterCallback<MouseEnterEvent>(_ => toggle.style.backgroundColor = hoverBg);
                    toggle.RegisterCallback<MouseLeaveEvent>(_ => toggle.style.backgroundColor = headerBg);

                    // The chevron carries the full-strength accent, tying the rail through the header.
                    var chevronIcon = toggle.Q<SasquatchIconElement>();
                    if (chevronIcon != null) chevronIcon.Tint = new Color(tint.r, tint.g, tint.b, 1f);
                }

                if (foldout.contentContainer != null)
                {
                    // Breathing room so the last field doesn't sit on the panel's bottom curve; display:None
                    // while collapsed, so a closed panel is just the header bar.
                    foldout.contentContainer.style.paddingTop = 4;
                    foldout.contentContainer.style.paddingBottom = 5;
                    foldout.contentContainer.style.paddingRight = 4;
                }

                // The accent rail: an inset strip whose left corners hug the ring's inner curve (4px outer
                // − 1px ring), painted LAST so it rides over both washes; absolute + Ignore so it touches
                // neither layout nor picking. Direct-child scan (never Q) — a nested tinted foldout carries
                // its own rail. Reused when the same element is re-styled in place.
                VisualElement rail = null;
                foreach (var child in foldout.hierarchy.Children())
                    if (child.name == SectionRailName) { rail = child; break; }
                if (rail == null)
                {
                    rail = new VisualElement { name = SectionRailName, pickingMode = PickingMode.Ignore };
                    foldout.hierarchy.Add(rail);
                }
                rail.style.position = Position.Absolute;
                rail.style.left = 0;
                rail.style.top = 0;
                rail.style.bottom = 0;
                rail.style.width = 3;
                rail.style.borderTopLeftRadius = 3;
                rail.style.borderBottomLeftRadius = 3;
                rail.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 1f);
                return;
            }

            // Flat fallback (a non-collapsible Card, or a bare Rows/Columns/Grid): tint the element itself, kept
            // near the skin's panel gray + mostly opaque so a Card still reads as a panel rather than a faint
            // transparent wash.
            float flatBase = WidgetPalette.FlatTintBase;
            root.style.backgroundColor = new Color(
                Mathf.Lerp(flatBase, tint.r, 0.25f),
                Mathf.Lerp(flatBase, tint.g, 0.25f),
                Mathf.Lerp(flatBase, tint.b, 0.25f),
                0.55f);
            ApplyFlatSectionRail(root, tint);
        }

        /// <summary>
        /// The rail for the flat shapes — a non-collapsible Card or a bare Rows/Columns/Grid.
        ///
        /// <para>Rounded (a Card is built at radius 6, measured) gets the inset strip, for the same reason the
        /// Foldout and the group shell do. SQUARE roots keep the BORDER, deliberately: a border indents the
        /// container's content by its own width, and swapping a square root to an overlaying strip would shift
        /// every child 3px left in layouts that look right today. Nothing is gained there — with no corner to
        /// round, the two forms are pixel-identical.</para>
        /// </summary>
        private static void ApplyFlatSectionRail(VisualElement root, Color color)
        {
            float radius = InlineLeftRadiusPx(root);
            if (radius > 0f)
            {
                EnsureInsetSectionRail(root, color, radius);
                return;
            }
            root.style.borderLeftWidth = SectionRailWidth;
            root.style.borderLeftColor = new Color(color.r, color.g, color.b, 1f);
        }

        /// <summary>The root's INLINE top-left radius in px, or 0 when it carries none. Read off the inline
        /// style rather than <c>resolvedStyle</c> on purpose: this runs at BUILD time, before the element has
        /// a panel to resolve against, and every radius that matters here (the Card's 6, a user's Corner
        /// Radius override) was written inline moments earlier by the builder or StyleApplier. A radius that
        /// arrives from USS instead reads as 0 and keeps the old border rail — the pre-existing behaviour,
        /// so an unknown shape degrades to what it already did.</summary>
        private static float InlineLeftRadiusPx(VisualElement el)
        {
            var r = el.style.borderTopLeftRadius;
            if (r.keyword != StyleKeyword.Undefined) return 0f; // Null/Auto/Initial — nothing inline to read
            return r.value.unit == LengthUnit.Pixel ? Mathf.Max(0f, r.value.value) : 0f;
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        /// <summary>Name of the inset accent-rail strip a tinted section carries in place of a border rail.</summary>
        internal const string SectionRailName = "sasq-section-rail";

        /// <summary>
        /// THE SEAM UNDER A GROUP HEADER EXISTS ONLY WHILE THERE IS SOMETHING UNDER IT.
        ///
        /// <para>Used by the shells that draw ONE outline around the whole group (the expandable List
        /// collection): the header's own box border is dropped and its BOTTOM edge is kept as the
        /// separator between header and body. Collapsed, there is no body to separate — and the seam then
        /// lands directly on top of the container's own bottom border, so the bottom edge paints 2px of
        /// <c>#1e1e1e</c> while every other edge paints 1. Measured collapsed: group 26px around a 24px
        /// header. That doubled edge is what reads as a drop shadow hanging under a closed list.</para>
        ///
        /// <para>NOT for the Table shell or the collapsible-container shell: there the group root draws no
        /// border of its own, so the header's bottom edge IS the box's bottom edge and must stay.</para>
        ///
        /// <para>And not for a shell that paints straight through its header: a Timeline list's well runs
        /// unbroken behind it, so <see cref="NoHeaderSeamClass"/> (stamped by ApplyListShellStyle) holds the
        /// seam at 0 forever. That marker is why this takes no parameter — it is read off the element, so
        /// both toggle call sites, and any added later, honour it without plumbing.</para>
        /// </summary>
        private static void SyncHeaderSeam(VisualElement headerRow, bool expanded)
        {
            if (headerRow == null) return;
            bool wantsSeam = !headerRow.ClassListContains(NoHeaderSeamClass);
            headerRow.style.borderBottomWidth = (expanded && wantsSeam) ? 1 : 0;
            // Collapsed, the header IS the whole panel, so it owns the panel's bottom curve; open, it sits
            // flush on the body it reveals.
            headerRow.style.borderBottomLeftRadius = expanded ? 0 : 4;
            headerRow.style.borderBottomRightRadius = expanded ? 0 : 4;
        }

        /// <summary>
        /// Draws the section accent rail as an INSET, round-cornered strip instead of a thick left BORDER.
        ///
        /// <para>A border rail only looks right on a square-cornered root. UI Toolkit interpolates border
        /// WIDTH around a rounded corner, so a 3px left edge tapers into the 1px top edge and leaves a lump
        /// at both left corners — which is why the plain-Foldout path has always built a strip instead. The
        /// same argument applies to any rounded root, and two of them were still on the border: a
        /// <c>.sasq-group</c> shell (an Expandable container — its root has NO radius, the visible 4px corners
        /// live on its header and content CHILDREN, so the rail rendered as a straight bar standing beside a
        /// rounded card) and a non-collapsible Card (root radius 6, measured).</para>
        ///
        /// <para>Absolute + <see cref="PickingMode.Ignore"/>, so it costs neither layout nor hit-testing, and
        /// it spans the root top-to-bottom — including when the group collapses to just its header. Reused in
        /// place when the same element is re-styled, and found by a DIRECT-child scan (never <c>Q</c>) so a
        /// nested tinted section's own rail is never mistaken for this one.</para>
        /// </summary>
        private static void EnsureInsetSectionRail(VisualElement root, Color color, float radius)
        {
            if (root == null) return;

            VisualElement rail = null;
            foreach (var child in root.hierarchy.Children())
                if (child.name == SectionRailName) { rail = child; break; }
            if (rail == null)
            {
                rail = new VisualElement { name = SectionRailName, pickingMode = PickingMode.Ignore };
                root.hierarchy.Add(rail);
            }

            rail.style.position = Position.Absolute;
            rail.style.left = 0;
            rail.style.top = 0;
            rail.style.bottom = 0;
            rail.style.width = SectionRailWidth;
            rail.style.borderTopLeftRadius = radius;
            rail.style.borderBottomLeftRadius = radius;
            rail.style.backgroundColor = new Color(color.r, color.g, color.b, 1f);
        }

        /// <summary>Width of the accent rail, border and inset-strip forms alike.</summary>
        private const float SectionRailWidth = 3f;

        /// <summary>Corner radius of a <c>.sasq-group</c> shell — the header's top pair and the content's
        /// bottom pair both declare 4px in SasquatchWidgets.uss, and the collapsed header's inline bottom
        /// pair matches. The rail hugs that curve.</summary>
        private const float GroupShellRadius = 4f;

        /// <summary>Draws a full-color accent rail down the section's left edge ONLY — the lighter-weight sibling
        /// of <see cref="ApplySectionTint"/> (no header/body wash). Called AFTER StyleApplier AND after
        /// <see cref="ApplySectionTint"/>, so when both are set the stripe's rail color overrides the tint's rail
        /// while the tint keeps its header/body wash.</summary>
        private static void ApplyCardAccentStripe(VisualElement root, ContainerNode node)
        {
            Color c = node.AccentStripeColor.value;

            // A section that ALREADY carries an inset strip (the tint ran first) just gets recolored — drawing
            // a border rail on top would leave a second, taper-cornered rail beside it. Direct-child scan: a
            // nested tinted section owns its own strip.
            foreach (var child in root.hierarchy.Children())
            {
                if (child.name == SectionRailName)
                {
                    child.style.backgroundColor = new Color(c.r, c.g, c.b, 1f);
                    return;
                }
            }

            // No tint ran, so this is the ONLY rail — and it has to pick its own form the same way
            // ApplySectionTint does. THIS is the path the bug report came in on: Accent Stripe set with no
            // Section Tint, on an Expandable container, drew a square-ended 3px bar down the side of a
            // rounded card. Same three shapes, same rule.
            if (root.ClassListContains("sasq-group"))
            {
                EnsureInsetSectionRail(root, c, GroupShellRadius);
                return;
            }
            if (root is Foldout)
            {
                // An UNTINTED plain Foldout is not dressed as a rounded panel, so it has no corner to fight;
                // but it is the one shape whose rail a later tint would expect to find as a strip. Match the
                // tinted Foldout's geometry (4px panel − 1px ring) so both orders of the two settings agree.
                EnsureInsetSectionRail(root, c, 3f);
                return;
            }
            ApplyFlatSectionRail(root, c);
        }

        // Design-mode stand-in for a binding whose property no longer exists in code (deleted/renamed
        // while auto-sync hasn't run). Lives in the canvas like a normal node row — it falls through
        // BuildNode's common stamping block, so it stays selectable, draggable and deletable.
        private static VisualElement MakeMissingBindingPlaceholder(string message)
        {
            var placeholder = new VisualElement();
            placeholder.AddToClassList("sasq-missing-binding");
            placeholder.Add(new Label(message));
            return placeholder;
        }

        /// <summary>
        /// Per-user switch for the plain-collection conversion tips (the Dictionary/HashSet guidance cards) in
        /// the inspector AND the designer canvas (WYSIWYG — what the canvas shows is what the inspector shows).
        /// Set false by the card's "Don't show this again" button; surfaced as a re-enable toggle in
        /// Project Settings ▸ Sasquatch Inspector.
        /// </summary>
        internal static bool ShowCollectionConversionTips
        {
            get => EditorPrefs.GetBool(CollectionTipsPrefKey, true);
            set => EditorPrefs.SetBool(CollectionTipsPrefKey, value);
        }

        private const string CollectionTipsPrefKey = "SasquatchInspector.ShowCollectionConversionTips";

        // Guidance card for a binding whose field EXISTS in code but is a plain Dictionary<,> — Unity can't
        // serialize it, so there is no SerializedProperty to draw or save, ever. Shown in BOTH live and design
        // modes (unlike the red missing-binding placeholder, this is information, not an error) with a
        // click-to-copy SerializedDictionary declaration for that exact field. Falls through BuildNode's common
        // stamping block, so in the designer it stays selectable, draggable and deletable. The card carries a
        // "Don't show this again" dismissal (see ShowCollectionConversionTips) that hides these tips in both
        // modes — the canvas mirrors the inspector.
        private static VisualElement MakeUnserializableCollectionCard(System.Reflection.FieldInfo field)
        {
            var card = new VisualElement();
            card.AddToClassList("sasq-unserializable-dict");

            string friendly = SasquatchReflectionUtility.GetFriendlyTypeName(field.FieldType);
            string niceName = ObjectNames.NicifyVariableName(field.Name);
            // A key/value type (Dictionary/SortedDictionary/SortedList) can be swapped 1:1 for SerializedDictionary
            // and gets the copyable declaration. A set (HashSet/SortedSet) has no such drop-in, so it gets a
            // generic "make it serializable" message without the misleading K/V swap snippet.
            bool isDict = SasquatchReflectionUtility.IsPlainUnserializableDictionary(field.FieldType);

            // Unity 6.6 serializes Dictionary<K, V> natively — but ONLY with an explicit [SerializeField],
            // even on a public field (analyzer UAC1015; it is the one place Unity breaks its own "public
            // fields serialize" rule). So on 6.6 a bare Dictionary field still lands here with no
            // SerializedProperty, and the honest fix is the attribute, not a type swap. SortedDictionary /
            // SortedList / HashSet / SortedSet are still unserializable on every version and keep the old
            // message. Saying "Unity can't serialize it" about a Dictionary on 6.6 is simply false.
            // AND THE ATTRIBUTE MUST ACTUALLY BE MISSING. IsNativelySerializableDictionaryType is a pure
            // TYPE + version test; "no SerializedProperty" (which is what got us here) and "no
            // [SerializeField]" are different conditions. Without this read, a Dictionary field that ALREADY
            // carries the attribute -- but still has no property, because a key or value type is not
            // serializable, or because it sits inside a List<> (analyzer UAC1009) -- was told as fact that it
            // had no [SerializeField], and handed a Copy button reproducing the exact line already in its
            // own source. Paste it, recompile, and the same card comes back.
            bool missingSerializeField =
                System.Attribute.GetCustomAttribute(field, typeof(SerializeField)) == null;
            bool isNativeDict = SasquatchReflectionUtility.IsNativelySerializableDictionaryType(field.FieldType)
                                && missingSerializeField;
            // The honest third arm: the type IS one Unity 6.6 serializes, the attribute IS present, and there
            // is still no property. Naming a cause we have not established would be the same mistake again,
            // so this states what is true and points at the two things that actually cause it.
            //
            // UNREACHABLE FROM SOURCE ON 6.6, measured against a real 6000.6.0f1 -- Unity's serialization
            // ANALYZERS refuse every route into this state before the code compiles: an interface, abstract
            // or System.Type key/value is UAC1012, a dictionary inside a List<>/array is UAC1009, and a
            // Dictionary<string, object> is simply accepted and serializes. Kept anyway, because it is the
            // right message if a future editor relaxes one of those rules or a precompiled assembly arrives
            // with the analyzers off, and because the guard it rides on (missingSerializeField) is correct
            // regardless. Deliberately NOT unit-tested: see the note in
            // SasquatchVerificationTests.DictionaryPolish.cs for why a test here would pin nothing.
            bool attributedButStillDead =
                SasquatchReflectionUtility.IsNativelySerializableDictionaryType(field.FieldType)
                && !missingSerializeField;

            var help = new HelpBox(
                isNativeDict
                    ? $"'{niceName}' is a {friendly} with no [SerializeField]. Unity serializes dictionaries " +
                      "from 6.6, but only when the field carries [SerializeField] — a public one is not enough. " +
                      "Add it and this draws as Unity's Key/Value table."
                    : attributedButStillDead
                    ? $"'{niceName}' is a {friendly} with [SerializeField], but Unity still has no serialized " +
                      "data for it. From 6.6 it serializes a dictionary only when BOTH the key and the value " +
                      "are serializable types, and never inside a List<> or an array — " +
                      "SerializedDictionary<K, V> has neither limit."
                    : isDict
                        ? $"'{niceName}' is a plain {friendly} — Unity can't serialize it, so there's nothing to save " +
                          "or draw. Swap it for SerializedDictionary<K, V> (SasquatchInspector.Runtime): same runtime " +
                          "API, fully serializable, drawn as a Key/Value table."
                        : $"'{niceName}' is a plain {friendly} — Unity can't serialize it, so there's nothing to save " +
                          "or draw. Back it with a serializable collection (e.g. a List<T>, or a [Serializable] wrapper) " +
                          "if you need it in the inspector.",
                HelpBoxMessageType.Info);
            card.Add(help);

            if (isDict) // sets: message only, no copyable snippet (there is no drop-in for a set)
            {
                // On 6.6 the primary offer is the attribute that makes the field work as written; the
                // SerializedDictionary swap becomes the SECOND offer, and has to earn it by naming what it
                // buys (Unity's own version has none of these — verified against 6000.6.0f1).
                string decl = isNativeDict
                    ? GetSerializeFieldConversion(field)
                    : GetSerializedDictionaryConversion(field);

                if (isNativeDict)
                {
                    // NARROWED, and it had to be. This used to offer "drag-to-reorder with the order actually
                    // saved" as a reason to change TYPE -- the exact feature the Dictionary Table display type
                    // already gives this very field, on the dictionary they already have. Two shipped strings
                    // contradicting each other, and the card is the one a buyer reads first. What is left is
                    // what SerializedDictionary alone can do (verified against 6000.6.0f1).
                    var alt = new HelpBox(
                        "Want the Sasquatch table on it? Keep the Dictionary, add [SerializeField], then set " +
                        "this field's Display Type to Dictionary Table — that adds drag-to-reorder, a per-row " +
                        "menu and full Sasquatch value editors on top of Unity's own serialization. " +
                        "Prefer SerializedDictionary<K, V> only for what Unity's dictionaries structurally " +
                        "cannot do: nesting inside a List<>, multi-object editing, and support back to Unity 6.0.",
                        HelpBoxMessageType.None);
                    card.Add(alt);
                }

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;

                var code = new Label(decl);
                code.AddToClassList("sasq-unserializable-dict__code");
                code.selection.isSelectable = true;
                code.style.flexGrow = 1;
                row.Add(code);

                var copy = new Button { text = "Copy" };
                copy.style.flexShrink = 0;
                EditorSpringFX.ButtonSpring(copy);
                copy.clicked += () =>
                {
                    EditorGUIUtility.systemCopyBuffer = decl;
                    copy.text = "Copied!";
                    copy.schedule.Execute(() => copy.text = "Copy").StartingIn(1200);
                };
                row.Add(copy);

                card.Add(row);
            }

            // One-click opt-out, in BOTH modes (the designer canvas is WYSIWYG for the inspector). Devs add
            // plain Dictionaries for ACCESS all the time, fully aware they won't draw — after the first read
            // this card is a nag, so it can be dismissed for good (per user; re-enable in Project Settings ▸
            // Sasquatch Inspector).
            var dismissRow = new VisualElement();
            dismissRow.style.flexDirection = FlexDirection.Row;
            dismissRow.style.justifyContent = Justify.FlexEnd;

            var dismiss = new Button { text = "Don't show this again" };
            dismiss.AddToClassList("sasq-unserializable-dict__dismiss");
            dismiss.AddToClassList(DesignKeepInteractiveClass); // dismissable straight from the design canvas too
            dismiss.tooltip = "Hide these conversion tips — for every such field, in the inspector and the " +
                              "Layout Designer, on this machine. Re-enable any time in " +
                              "Project Settings ▸ Sasquatch Inspector.";
            dismiss.clicked += () =>
            {
                ShowCollectionConversionTips = false;
                // Fold away every conversion card in EVERY open window — the inspector and the designer canvas
                // show the same card, so one dismiss must clear both at once (not leave the other window stale
                // until its next rebuild). Sweeping all EditorWindows covers both plus any extra inspectors.
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    if (w == null) continue;
                    w.rootVisualElement.Query<VisualElement>(className: "sasq-unserializable-dict")
                        .ForEach(c => EditorSpringFX.Fold(c, expanded: false));
                }
            };
            dismissRow.Add(dismiss);
            card.Add(dismissRow);

            return card;
        }

        // The one-line declaration offered by the guidance card (internal so tests can pin it exactly).
        internal static string GetSerializedDictionaryConversion(System.Reflection.FieldInfo field)
        {
            // THE RESOLVED BASE, not the field's own type. `class LootTable : Dictionary<string,int> {}` is
            // itself non-generic, so GetGenericArguments() returns an EMPTY array and args[0] threw straight
            // out of the card build. Same resolver the card's gate uses, so the two cannot disagree about
            // what counts as a dictionary.
            var closed = SasquatchReflectionUtility.ResolveUnserializableDictionaryBase(field.FieldType);
            if (closed == null) return $"SerializedDictionary<K, V> {field.Name} = new();";
            var args = closed.GetGenericArguments();
            string k = SasquatchReflectionUtility.GetFriendlyTypeName(args[0]);
            string v = SasquatchReflectionUtility.GetFriendlyTypeName(args[1]);
            return $"SerializedDictionary<{k}, {v}> {field.Name} = new();";
        }

        // The 6.6+ offer: keep the plain Dictionary, add the attribute Unity's dictionary backend demands.
        // Emitted with the field's OWN accessibility so pasting it over the original line is a true
        // replacement — [SerializeField] on a public field is legal and is exactly what UAC1015 asks for,
        // but a dev who wrote `private` should not silently get `public` back.
        internal static string GetSerializeFieldConversion(System.Reflection.FieldInfo field)
        {
            // Only ever reached for an exact Dictionary<,> (IsNativelySerializableDictionaryType gates it),
            // but resolved through the same helper as its sibling above so neither can index an empty array.
            var closed = SasquatchReflectionUtility.ResolveUnserializableDictionaryBase(field.FieldType);
            string access = field.IsPublic ? "public" : "private";
            if (closed == null) return $"[SerializeField] {access} {field.FieldType.Name} {field.Name} = new();";
            var args = closed.GetGenericArguments();
            string k = SasquatchReflectionUtility.GetFriendlyTypeName(args[0]);
            string v = SasquatchReflectionUtility.GetFriendlyTypeName(args[1]);
            return $"[SerializeField] {access} Dictionary<{k}, {v}> {field.Name} = new();";
        }

        /// <summary>
        /// The conversion-card exception to a dead COLLECTION, shared by the two dead-collection gates
        /// (<c>BuildCollectionElement</c>'s own, and <c>BuildCollectionTableElement</c>'s -- a Table-mode
        /// node returns into the second one before it ever reaches the first, so fixing only one would have
        /// left List and Table disagreeing).
        /// <para>
        /// A field whose type changed to a plain unserializable BCL collection (<c>List&lt;string&gt;</c> to
        /// <c>HashSet&lt;string&gt;</c>, or <c>List&lt;Foo&gt;</c> to <c>Dictionary&lt;string,Foo&gt;</c> on
        /// 6.0-6.5) still exists in code; it is exactly the shape
        /// <see cref="MakeUnserializableCollectionCard"/> was written to explain, and the card hands over the
        /// one-line fix. Without this the CollectionNode route gave nothing live and a bare red
        /// missing-collection placeholder in the designer, while the identical field bound as a BindingNode
        /// got the card: two nodes, one field, two different answers.
        /// </para>
        /// <para>
        /// Returns TRUE when it has taken responsibility for the field -- including when the per-user
        /// dismissal is off and the honest answer is to draw NOTHING (a null <paramref name="card"/>), which
        /// is the same both-modes behaviour the binding branch has.
        /// </para>
        /// </summary>
        private static bool TryDeadCollectionConversionCard(
            SerializedObject serializedObject, string resolvedPath, out VisualElement card)
        {
            card = null;
            var target = serializedObject != null ? serializedObject.targetObject : null;
            if (target == null || string.IsNullOrEmpty(resolvedPath)) return false;

            var field = BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), resolvedPath, out _);
            if (field == null || !SasquatchReflectionUtility.IsPlainUnserializableCollection(field.FieldType))
                return false;

            card = ShowCollectionConversionTips ? MakeUnserializableCollectionCard(field) : null;
            return true;
        }

        // The dashed "Empty Container (Drag properties here)" hint shown for an empty container that has no
        // explicit slot count.
        private static VisualElement MakeEmptyContainerHint()
        {
            var placeholder = new Label("Empty Container (Drag properties here)");
            // The drop snapshot anchors an empty container's insertion line to this hint (a
            // full-height pane's container bottom can be a whole frame away from the hint).
            placeholder.AddToClassList("sasq-empty-hint");
            placeholder.style.color = WidgetPalette.TextGray;
            placeholder.style.fontSize = 10;
            placeholder.style.unityFontStyleAndWeight = FontStyle.Italic;
            placeholder.style.paddingTop = 6;
            placeholder.style.paddingBottom = 6;
            placeholder.style.paddingLeft = 8;
            placeholder.style.paddingRight = 8;
            placeholder.style.borderTopWidth = 1;
            placeholder.style.borderBottomWidth = 1;
            placeholder.style.borderLeftWidth = 1;
            placeholder.style.borderRightWidth = 1;
            var c = WidgetPalette.EmptyHintBorder;
            placeholder.style.borderTopColor = c;
            placeholder.style.borderBottomColor = c;
            placeholder.style.borderLeftColor = c;
            placeholder.style.borderRightColor = c;
            placeholder.style.borderTopLeftRadius = 4;
            placeholder.style.borderTopRightRadius = 4;
            placeholder.style.borderBottomLeftRadius = 4;
            placeholder.style.borderBottomRightRadius = 4;
            return placeholder;
        }

        // A single empty "drop slot" for a Columns/Rows container whose track count exceeds its child count,
        // so an empty 3-column container visibly shows 3 droppable cells. Visual-only (no userData, ignores
        // picking) so it never interferes with the drag controller, which still targets the parent container.
        // Internal: CanvasDragController also inserts these as TEMPORARY track fillers while a tracked
        // container's own child is lifted mid-drag, so the container never collapses below its track count.
        internal static VisualElement MakeEmptyDropSlot(ContainerType kind)
        {
            var slot = new Label("＋");
            slot.AddToClassList("sasq-drop-slot");
            slot.pickingMode = PickingMode.Ignore;
            slot.style.unityTextAlign = TextAnchor.MiddleCenter;
            slot.style.color = WidgetPalette.DropSlotText;
            slot.style.minHeight = 28;
            slot.style.marginTop = 2;
            slot.style.marginBottom = 2;
            if (kind == ContainerType.Columns || kind == ContainerType.Grid)
            {
                // Equal-width cell, matching ApplyColumnsLayout so filled + empty cells line up.
                slot.style.flexGrow = 1f;
                slot.style.flexBasis = 0f;
                slot.style.minWidth = 0;
                if (kind == ContainerType.Columns)
                {
                    // Grid cells carry no horizontal margins (columns must align with real cells
                    // across rows); Columns cells do.
                    slot.style.marginLeft = 2;
                    slot.style.marginRight = 2;
                }
            }
            return slot;
        }

        // The element for an EmptyCellNode — a cell the user has deliberately left open so a later cell could
        // hold a widget. In DESIGN mode it is byte-for-byte the visual-only "＋" placeholder (same class, same
        // styling, no userData), so a held cell and an unheld one are indistinguishable and the drop snapshot
        // treats them through one code path. In LIVE inspectors and exported editors it becomes an inert blank
        // cell: dropping it there would slide the real widgets left and break the designed layout.
        internal static VisualElement MakeEmptyCellElement(ContainerType kind, bool designMode)
        {
            if (designMode) return MakeEmptyDropSlot(kind);

            var cell = new VisualElement();
            cell.AddToClassList(EmptyCellClass);
            cell.pickingMode = PickingMode.Ignore; // nothing to click; never steal a neighbour's press
            // Mirror MakeEmptyDropSlot's BOX exactly (minus the glyph and border): the live inspector must
            // reserve the same room the designer showed, or a held-open Rows track collapses to nothing and
            // the widgets below it slide up — the layout the user designed, silently not the one they get.
            cell.style.minHeight = 28;
            cell.style.marginTop = 2;
            cell.style.marginBottom = 2;
            if (kind == ContainerType.Columns || kind == ContainerType.Grid)
            {
                cell.style.flexGrow = 1f;
                cell.style.flexBasis = 0f;
                cell.style.minWidth = 0;
                if (kind == ContainerType.Columns)
                {
                    cell.style.marginLeft = 2;
                    cell.style.marginRight = 2;
                }
            }
            return cell;
        }

        /// <summary>Marks a live/exported blank cell held open by an <see cref="EmptyCellNode"/>.</summary>
        internal const string EmptyCellClass = "sasq-empty-cell";

        // Subtrees tagged with this class stay INTERACTIVE even in design select-only mode. Used for widget
        // resize grips (e.g. a text box's drag-to-resize handle): resizing a widget is a layout-DESIGN action,
        // so it should keep working regardless of the Interact toggle, matching the container/column resize grips.
        internal const string DesignKeepInteractiveClass = "sasq-design-keep-interactive";

        /// <summary>Gates a Toggle Group's content on its bound bool. Pairs the <c>SetEnabled</c> with the
        /// disabled-slider repaint, so a group holding a slider can never leave a see-through knob behind —
        /// the two must move together, because nothing observes an enabled-state change on its own.</summary>
        private static void SetGroupContentEnabled(VisualElement content, bool enabled)
        {
            content.SetEnabled(enabled);
            BindingFactory.RefreshDisabledKnobsUnder(content);
        }

        private static void DisableInteractivityRecursively(VisualElement ve)
        {
            if (ve == null) return;
            if (ve.ClassListContains(DesignKeepInteractiveClass)) return; // leave exempt subtrees (resize grips) pickable
            ve.pickingMode = PickingMode.Ignore;
            // PICKING IS NOT THE ONLY WAY IN — clearing focus is half the job. An unpickable control that is
            // still `focusable` can be reached two ways, and both let the KEYBOARD edit a value the canvas is
            // supposed to be select-only:
            //   1. An exempt descendant. The resizable text box's drag grip carries DesignKeepInteractiveClass
            //      and is a child of `unity-text-input`, i.e. INSIDE the field. A press on it runs UI Toolkit's
            //      focus default action, which walks UP to the nearest focusable ancestor — the TextField —
            //      whose delegatesFocus then hands focus to the very text input we just made unpickable. Typing
            //      then edited the value with Interact OFF, with every element already at PickingMode.Ignore.
            //   2. Tab. Focus navigation never consults pickingMode at all, so every preview field in the canvas
            //      was a tab stop.
            // Design mode rebuilds the canvas when Interact is toggled, so nothing has to restore this.
            ve.focusable = false;
            foreach (var child in ve.Children())
            {
                DisableInteractivityRecursively(child);
            }
        }

        // Design-mode select-only: make `el` + all descendants non-interactive (pickingMode=Ignore) so a click
        // falls through to SELECT the card instead of operating the control, and KEEP re-applying on every layout
        // change so controls that build LAZILY during bind, or are rebuilt later (numeric inner inputs, the
        // In-Line Editor's nested inspector, collection value-column cells), are caught too. Setting pickingMode
        // does not change geometry, so the re-apply can't loop. The handler dies with the card on the next canvas
        // rebuild (and the canvas rebuilds when Interact is toggled on), so it never fights Interact mode.
        /// <summary>
        /// The design-canvas select-only sweep, for a widget that lives OUTSIDE this file and therefore cannot
        /// reach the private twin below. Today's caller is <c>SerializedDictionaryDrawer</c>'s search box:
        /// the drawer is a plain <c>CustomPropertyDrawer</c>, so the canvas's own blanket suppression does not
        /// reach a control it builds, and a search box is the most obviously typeable thing on the card.
        /// </summary>
        internal static void SuppressInteractionForDesign(VisualElement el) => SuppressInteractionPersistently(el);

        private static void SuppressInteractionPersistently(VisualElement el)
        {
            if (el == null) return;
            DisableInteractivityRecursively(el);
            el.RegisterCallback<GeometryChangedEvent>(_ => DisableInteractivityRecursively(el));
            // Lazily-built inner controls attach AFTER the initial sweep — most importantly a numeric field's
            // label drag-SCRUBBER (Unity's FieldMouseDragger on IntegerField/FloatField.labelElement), which
            // can stay pickable in the gap before the next GeometryChanged and hijack a select-drag into a
            // value change (the "Interact off but the number still scrubs" leak). A press is always preceded
            // by the pointer moving over the card, so re-running the Ignore sweep on pointer enter/move
            // guarantees the label (and any other late-built input) is neutralized before pointer-down picks
            // a target. TrickleDown so it runs before the inner control; setting pickingMode changes no
            // geometry, so this can't loop, and the DesignKeepInteractiveClass exemption still protects grips.
            el.RegisterCallback<PointerEnterEvent>(_ => DisableInteractivityRecursively(el), TrickleDown.TrickleDown);
            el.RegisterCallback<PointerMoveEvent>(_ => DisableInteractivityRecursively(el), TrickleDown.TrickleDown);
            AttachFocusBarrier(el, () => DisableInteractivityRecursively(el));
        }

        /// <summary>
        /// Closes the KEYBOARD route into a design select-only subtree. The pointer triggers above cannot: a
        /// child built during BIND (a numeric field's inner text input, a TextArea's ScrollView/TextElement)
        /// appears without resizing its parent, so no <see cref="GeometryChangedEvent"/> reaches the swept root,
        /// and Tab navigation never dispatches a pointer event to re-arm the sweep either. Two passes close it:
        /// <list type="number">
        /// <item>A deferred re-sweep once the panel has run its binding pass, so the tree is genuinely clean
        /// rather than cleaned reactively on hover.</item>
        /// <item>A trickle-down <c>FocusInEvent</c> barrier — if anything inside ever does take focus, re-sweep
        /// and blur it. This is the backstop that does not care WHEN or HOW a control appeared.</item>
        /// </list>
        /// The blur is deferred by a tick: blurring inside the focus controller's own dispatch re-enters it.
        /// </summary>
        private static void AttachFocusBarrier(VisualElement el, Action resweep)
        {
            el.RegisterCallback<AttachToPanelEvent>(_ => el.schedule.Execute(() => resweep()).StartingIn(0));
            el.RegisterCallback<FocusInEvent>(evt =>
            {
                resweep();
                var focused = evt.target as VisualElement;
                if (focused == null) return;
                el.schedule.Execute(() =>
                {
                    // Only blur what is still inside this subtree — by the next tick the user may legitimately
                    // have moved focus to the designer's own chrome (the Properties panel, a search box).
                    var live = el.panel != null ? el.panel.focusController.focusedElement as VisualElement : null;
                    if (live != null && (live == el || live.FindCommonAncestor(el) == el)) live.Blur();
                }).StartingIn(0);
            }, TrickleDown.TrickleDown);
        }

        // Like SuppressInteractionPersistently, but leaves `el` ITSELF pickable and disables only its descendants.
        // Use when `el` is the selection card itself (a CollectionNode gets no leaf-card wrapper, so the built
        // widget IS the card that carries userData + the CanvasDragManipulator). Disabling the root too — as the
        // plain variant does — leaves nothing to catch the click, so it falls through to the parent container and
        // the widget can't be selected at all. The re-apply handlers live on `el` (which stays pickable, so its
        // pointer handlers fire on hover) and on GeometryChanged (fires even on the Ignore'd children after a
        // rebuild), catching widgets that rebuild their own children — Tag Chips clears+recreates its chips on
        // every array change, and each fresh chip returns at the default PickingMode.Position.
        private static void SuppressChildrenInteractionPersistently(VisualElement el)
        {
            if (el == null) return;
            DisableChildrenInteractivity(el);
            el.RegisterCallback<GeometryChangedEvent>(_ => DisableChildrenInteractivity(el));
            el.RegisterCallback<PointerEnterEvent>(_ => DisableChildrenInteractivity(el), TrickleDown.TrickleDown);
            el.RegisterCallback<PointerMoveEvent>(_ => DisableChildrenInteractivity(el), TrickleDown.TrickleDown);
            AttachFocusBarrier(el, () => DisableChildrenInteractivity(el));
        }

        private static void DisableChildrenInteractivity(VisualElement el)
        {
            if (el == null) return;
            foreach (var child in el.Children())
            {
                DisableInteractivityRecursively(child);
            }
        }

        // Renders a non-expandable HelpBox's native label as markdown when enabled, preserving the box's themed
        // icon/background. The internal label is built in the HelpBox ctor (queryable now); a one-shot
        // GeometryChanged re-apply is a safety net in case a Unity version builds it lazily. The apply is
        // idempotent (SasquatchMarkdown re-registers a static link handler), so a double call is harmless.
        private static void ApplyHelpBoxMarkdown(HelpBox helpBox, HelpBoxNode node)
        {
            if (helpBox == null || !node.RenderMarkdown || string.IsNullOrEmpty(node.Message)) return;

            // A fenced ``` code block needs a real monospace panel — the native HelpBox has a single Label, so
            // hide it and host a block container (text Labels + code panels) next to the box's icon/background.
            if (SasquatchMarkdown.HasFencedCode(node.Message))
            {
                var label = helpBox.Q<Label>(className: "unity-help-box__label");
                if (label != null) label.style.display = DisplayStyle.None;
                var content = SasquatchMarkdown.BuildMarkdownElement(node.Message);
                content.style.flexGrow = 1;
                content.style.flexShrink = 1;
                helpBox.Add(content);
                return;
            }

            void Apply()
            {
                var label = helpBox.Q<Label>(className: "unity-help-box__label");
                if (label != null) SasquatchMarkdown.ApplyMarkdownToLabel(label, node.Message);
            }

            Apply();
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                Apply();
                helpBox.UnregisterCallback(once);
            };
            helpBox.RegisterCallback(once);
        }

        private static VisualElement BuildHelpBoxElement(HelpBoxNode node, string stateKey = null)
        {
            if (!node.Expandable)
            {
                var helpBox = new HelpBox(
                    string.IsNullOrEmpty(node.Message) ? "" : node.Message,
                    node.MessageType
                );
                helpBox.style.marginTop = 4;
                helpBox.style.marginBottom = 4;
                ApplyHelpBoxMarkdown(helpBox, node);
                return helpBox;
            }

            // Chip collapsed style: a compact round "?" reveal chip (optionally with a summary label beside it) in
            // place of the summary-line + chevron header. Clicking the chip spring-reveals a card holding the
            // message (markdown when enabled); Warning/Error tint the card amber. Reveal state is remembered
            // per-type in the same FoldoutStateStore as the summary-line variant.
            if (node.CollapsedStyle == HelpBoxCollapsedStyle.Chip)
            {
                var chipContainer = new VisualElement();
                chipContainer.style.marginTop = 4;
                chipContainer.style.marginBottom = 4;

                bool warn = node.MessageType != HelpBoxMessageType.Info;
                var parts = SasquatchHelpChip.Build(node.Message, warn, node.RenderMarkdown,
                    startOpen: FoldoutStateStore.Get(stateKey, node.DefaultToOpen));

                var chipRow = new VisualElement();
                chipRow.style.flexDirection = FlexDirection.Row;
                chipRow.style.alignItems = Align.Center;
                // A pure "?" chip (no summary text) placed left / center / right per ChipAlignment.
                chipRow.style.justifyContent = node.ChipAlignment == ImageAlignment.Center ? Justify.Center
                                             : node.ChipAlignment == ImageAlignment.Right ? Justify.FlexEnd
                                             : Justify.FlexStart;
                parts.Chip.style.marginLeft = 0;
                chipRow.Add(parts.Chip);

                // Persist the reveal state (runs after the chip's own toggle handler, so it reads the new state).
                parts.Chip.clicked += () =>
                    FoldoutStateStore.Set(stateKey, parts.Card.style.display.value == DisplayStyle.Flex);

                chipContainer.Add(chipRow);
                chipContainer.Add(parts.Card);
                return chipContainer;
            }

            // Expandable help box: remember open/closed per type (FoldoutStateStore); DefaultToOpen is the default.
            bool startOpen = FoldoutStateStore.Get(stateKey, node.DefaultToOpen);

            var container = new VisualElement();
            container.style.marginTop = 4;
            container.style.marginBottom = 4;

            var header = new VisualElement();
            header.AddToClassList("sasq-help-expand-header");

            Color themeColor;
            switch (node.MessageType)
            {
                case HelpBoxMessageType.Warning:
                    themeColor = new Color(0.96f, 0.62f, 0.04f, 0.8f);
                    break;
                case HelpBoxMessageType.Error:
                    themeColor = new Color(0.93f, 0.27f, 0.27f, 0.8f);
                    break;
                case HelpBoxMessageType.Info:
                default:
                    themeColor = new Color(0.18f, 0.35f, 0.58f, 0.8f);
                    break;
            }
            header.style.borderLeftColor = themeColor;
            header.style.borderLeftWidth = 3;

            var chevron = SasquatchIcons.Create(
                SasquatchIcons.Ids.ChevronRight, // single glyph; spun 0°↔90° below
                10, WidgetPalette.ChevronDim);
            chevron.AddToClassList("sasq-help-expand-chevron");
            header.Add(chevron);
            EditorSpringFX.SpringCaret(chevron, startOpen, animate: false);

            string iconId = SasquatchIcons.Ids.Info;
            if (node.MessageType == HelpBoxMessageType.Warning) iconId = SasquatchIcons.Ids.TriangleAlert;
            else if (node.MessageType == HelpBoxMessageType.Error) iconId = SasquatchIcons.Ids.OctagonAlert;
            // 16px + full alpha, NOT the border accent verbatim: the alert glyphs are outline + interior
            // "!" — at 12px every stroke is ~1px and the marks fuse into a blob, and the border tint's
            // 0.8 alpha muddies the thin strokes further. 16 matches Unity's native HelpBox icon size.
            var iconTint = new Color(themeColor.r, themeColor.g, themeColor.b, 1f);
            var icon = SasquatchIcons.Create(iconId, 16, iconTint);
            icon.style.marginRight = 6;
            header.Add(icon);

            // Three-state summary (same convention as label/header overrides): unset → the
            // "{MessageType} Details" default, custom text → that text, HiddenSentinel → no text
            // (the chevron + icon still mark the collapsed row).
            string summaryText = node.IsSummaryHidden
                ? string.Empty
                : node.EffectiveSummaryOverride ?? (node.MessageType.ToString() + " Details");
            var summary = new Label(summaryText);
            summary.AddToClassList("sasq-help-expand-summary");
            summary.style.flexGrow = 1;
            summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            summary.style.color = WidgetPalette.LabelPrimary;
            // Markdown in the collapsed summary is formatting-only (bold/color); a link here would also fire the
            // header's pointer-down toggle, so links belong in the details body, not the summary.
            if (node.RenderMarkdown && !string.IsNullOrEmpty(summaryText))
                SasquatchMarkdown.ApplyMarkdownToLabel(summary, summaryText);
            header.Add(summary);

            container.Add(header);

            // With a fenced ``` code block, the body is a stacked container (text + monospace code panels);
            // otherwise it's a single wrapping Label. Both share the expand-details framing + fold animation.
            VisualElement details;
            if (node.RenderMarkdown && SasquatchMarkdown.HasFencedCode(node.Message))
            {
                details = SasquatchMarkdown.BuildMarkdownElement(node.Message);
            }
            else
            {
                var detailsLabel = new Label(string.IsNullOrEmpty(node.Message) ? "" : node.Message);
                detailsLabel.AddToClassList("sasq-helpbox-message");
                detailsLabel.style.whiteSpace = WhiteSpace.Normal; // wrap long messages
                if (node.RenderMarkdown && !string.IsNullOrEmpty(node.Message))
                    SasquatchMarkdown.ApplyMarkdownToLabel(detailsLabel, node.Message);
                details = detailsLabel;
            }
            details.AddToClassList("sasq-help-expand-details");
            details.style.borderLeftColor = themeColor;
            details.style.borderLeftWidth = 3;
            details.style.display = startOpen ? DisplayStyle.Flex : DisplayStyle.None;
            container.Add(details);

            // Tracked in a bool (not read back from style.display) because the spring fold
            // keeps display:Flex until the collapse animation settles.
            bool helpExpanded = startOpen;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                helpExpanded = !helpExpanded;
                EditorSpringFX.SpringCaret(chevron, helpExpanded);
                EditorSpringFX.Fold(details, helpExpanded);
                FoldoutStateStore.Set(stateKey, helpExpanded);
                evt.StopPropagation();
            });

            return container;
        }

        /// <summary>
        /// Whether any child of this Columns container carries a header, which is what decides that the
        /// headerLESS cells get a blank spacer so every column's field starts on one line.
        /// <para>Asks <see cref="WillBuild"/> first, deliberately. Reading the raw Children list means a
        /// header'd child that is SKIPPED in a live render still answers "yes", and every surviving cell
        /// then reserves a header-height strip for a header nobody will see — a dead band exactly where
        /// the hidden field used to be, which is the one failure the skip-don't-hide design exists to
        /// prevent. Same question RenderableChildCount asks, about headers instead of counts.</para>
        /// </summary>
        private static bool ColumnsContainerHasAnyHeader(ContainerNode container, SerializedObject serializedObject,
                                                        string basePath, bool designMode)
        {
            if (container.Children == null) return false;
            foreach (var child in container.Children)
            {
                if (!WillBuild(child, designMode)) continue;

                if (child is BindingNode bn)
                {
                    if (bn.EffectiveHeader != null) return true;
                    string resolvedPath = string.IsNullOrEmpty(basePath)
                        ? bn.PropertyPath
                        : $"{basePath}.{bn.PropertyPath}";
                    var prop = serializedObject.FindProperty(resolvedPath);
                    if (prop != null)
                    {
                        var targetObj = prop.serializedObject.targetObject;
                        if (targetObj != null)
                        {
                            var fieldInfo = BindingFactory.GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out _);
                            if (fieldInfo != null && Attribute.IsDefined(fieldInfo, typeof(HeaderAttribute)))
                            {
                                return true;
                            }
                        }
                    }
                }
                else if (child is ContainerNode cn && !string.IsNullOrEmpty(cn.Title))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsDescendant(LayoutNode parent, LayoutNode target)
        {
            if (parent == null || target == null) return false;
            if (parent == target) return true;
            if (parent is ContainerNode containerNode && containerNode.Children != null)
            {
                foreach (var child in containerNode.Children)
                {
                    if (IsDescendant(child, target)) return true;
                }
            }
            else if (parent is CollectionNode collectionNode)
            {
                if (IsDescendant(collectionNode.ItemLayout, target)) return true;
            }
            return false;
        }

        // Collection search/sort filtering lives in CollectionViewModel (RowMatchesQuery /
        // RowMatchesProperty / PropertyContainsText) so the renderer and verification tests share one impl.
    }
}
