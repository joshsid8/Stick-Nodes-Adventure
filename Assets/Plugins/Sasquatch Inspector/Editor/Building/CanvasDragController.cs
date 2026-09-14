using System;
using System.Collections.Generic;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using DropRelation = SasquatchInspector.Editor.SasquatchLayoutDesignerWindow.DropRelation;

namespace SasquatchInspector.Editor
{
    // Single owner of the designer drag lifecycle, built on pointer capture (driven by
    // CanvasDragManipulator). The core principle: one drag = one frozen geometry snapshot
    // (DragSnapshot) + pure-math resolution (DropResolver) + transform-only feedback (GapAnimator).
    // panel.Pick is never called during a drag and nothing that affects layout is inserted, removed
    // or hidden, so the indicator can't oscillate — the layout literally cannot change under the
    // pointer. Commits go out exactly once, always as AsChild + an explicit slot index.
    /// <summary>
    /// Single owner of the designer's drag lifecycle (pointer-capture based): one frozen geometry snapshot
    /// (<see cref="DragSnapshot"/>) + pure-math resolution (<see cref="DropResolver"/>) + transform-only feedback
    /// (<see cref="GapAnimator"/>), so the drop indicator can never oscillate. Commits exactly once as an
    /// explicit parent + slot insert.
    /// </summary>
    public class CanvasDragController
    {
        // Commit hooks — wired by the window to its existing model-mutation methods.
        public Action<LayoutNode, LayoutNode, DropRelation, int> OnMoveNode;
        public Action<LayoutNode, string, DropRelation, int> OnAddNode;
        // Multi-binding add: create one node per path under target (used by the Bindings tab's
        // multi-selection drag). target, paths, relation, slot.
        public Action<LayoutNode, List<string>, DropRelation, int> OnAddNodes;
        // Organizer multi-move: relocate the organizer's own multi-selection as a block. target,
        // relation, slot (the moved set is the window's _organizerSelection).
        public Action<LayoutNode, DropRelation, int> OnMoveOrganizerGroup;
        public Action<string, int, int> OnMoveCollectionItem;
        public Action<LayoutNode> OnDeleteNode;
        // Group remove (multi-drag onto the delete zone / Bindings panel): remove every dragged node.
        public Action<List<LayoutNode>> OnDeleteNodes;
        // node, additive (ctrl/cmd), range (shift)
        public Action<LayoutNode, bool, bool> OnSelectNode;
        // Right-click on a canvas card — the window shows its node context menu (style clipboard).
        // Routed like OnSelectNode so the static tree builder never reaches into the window.
        public Action<LayoutNode> OnNodeContextMenu;
        // Double-click on a canvas card — the window pings the node in the HIERARCHY (the mirror of
        // double-clicking a hierarchy row to ping it here). Same routing rule: the tree builder raises the
        // gesture, the window decides what it means.
        public Action<LayoutNode> OnNodeDoubleClick;
        // Organizer card click — routed separately so it selects WITHOUT navigating the Properties
        // panel (which hosts the organizer). clickCount lets a double-click open the card's properties.
        public Action<LayoutNode, bool, bool, int> OnSelectOrganizerNode;
        // Bindings token click — selects the token for multi-drag (path, additive, range).
        public Action<string, bool, bool> OnSelectBindingToken;
        // Click on a BATCH add card (the "C# Properties" card): it has no single token to select, so the
        // window toggles the card open/closed instead. Drag still adds every property at once.
        public Action<CanvasDragPayload> OnBatchAddClick;

        // Fired by structural edits made inside the static tree builder (tab "+", rename) so the
        // window can run a full refresh (hierarchy + organizer + preview), not just a canvas re-bind.
        public Action OnStructuralChange;

        // Given the grabbed payload, returns the full set being dragged (the surface-appropriate
        // multi-selection) or null for a normal single drag.
        public Func<CanvasDragPayload, List<LayoutNode>> ResolveDragGroup;

        // Given the grabbed AddBinding payload, returns the full set of token paths being dragged
        // (the Bindings tab multi-selection) or null for a single-token drag.
        public Func<CanvasDragPayload, List<string>> ResolveAddGroup;

        // When true (Interact mode / Alt held), leaf field cards yield the pointer to their fields.
        public bool FieldsInteractive;

        // The window supplies its flat hierarchy-row metadata (rebuilt each RefreshHierarchy) so the
        // engine can snapshot the tree for indent-based reparenting.
        public Func<List<HierRowInfo>> HierarchyRowsProvider;

        private const float AutoScrollMargin = 28f;
        private const float AutoScrollSpeed = 8f;

        private readonly ScrollView _scroll;
        private readonly VisualElement _windowRoot;
        private readonly VisualElement _overlay;
        private readonly VisualElement _deleteZone;

        private readonly DropIndicatorLayer _indicators;
        private readonly DragGhost _ghost;
        private readonly GapAnimator _gap;

        private VisualElement _canvasRoot;

        // Extra drop regions beyond the canvas (hierarchy tree, tab-organizer kanban).
        private readonly List<(ScrollView scroll, DropRegionKind kind)> _dropRegions = new();

        // Drag state
        private bool _dragging;
        private CanvasDragPayload _payload;
        private VisualElement _source;
        private Vector2 _sourceWorldPos;
        private Vector2 _grabOffset;
        private Vector2 _lastPanelPos;
        private IVisualElementScheduledItem _autoScroll;
        private readonly List<VisualElement> _lifted = new();   // dimmed in place (hierarchy rows, palette tokens)
        private readonly List<VisualElement> _hidden = new();   // removed from layout (canvas/organizer cards) — the layout closes up, Unity-style
        private readonly List<VisualElement> _tempSlots = new(); // temporary "+" track fillers keeping tracked Rows/Columns at full track count mid-drag

        private DragSnapshot _snapshot;
        private ResolvedDrop _resolved;
        private bool _overDelete;

        private readonly HashSet<LayoutNode> _dragged = new();        // top-level dragged nodes
        private readonly HashSet<LayoutNode> _draggedClosure = new(); // dragged + all descendants
        private CollectionNode _draggedTemplateOwner;
        private float _gapMainV = 36f;

        // Multi-drag stack: the topmost held nodes in visual (drop) order with their TRUE heights —
        // a selected child of a selected parent rides along inside it and contributes no height.
        // Drives the stacked ghost, the N preview lines, and the summed gap/expansion.
        private readonly List<LayoutNode> _dragStack = new();
        private readonly List<float> _dragHeights = new();
        private float _fallbackSpacingV = 4f;
        private DragGapProfile _gapProfile = new DragGapProfile(null, 4f);

        // Region-aware ghost: over the organizer/hierarchy the stack morphs to that region's row
        // WIDTH + HEIGHT (touching), over the canvas it shows true card width + heights + spacing.
        private bool _ghostHier;
        private float _hierRowH = 18f;       // current compact ROW HEIGHT (organizer card / hier row)
        private float _compactRowW = 180f;   // current compact ROW WIDTH
        private float _grabLocalY = 10f; // clamped grab Y within the grabbed card, kept to re-anchor per mode
        private float _grabLocalX = 12f; // raw grab X within the grabbed card, kept to re-anchor per ghost width

        // Per-card heights the drop GAP opens on the CANVAS (move = true card heights, add = uniform
        // AddGap). Over the organizer/hierarchy the gap is rebuilt from the compact row height instead,
        // so a tall canvas card still only opens one compact row's worth of space there.
        private float[] _canvasGapHeights = { DragTuning.AddGapPx };

        // Remove target: dragging an existing node onto the Bindings panel (or the delete zone) unbinds
        // it. Tinted red; commits via OnDeleteNode(s). Gated by _removeActive (Bindings tab visible).
        private VisualElement _removeZone;
        private Func<bool> _removeActive;
        private bool _overRemove;

        // Set by the window (NotifyCreatedNode) during the OnAddNode dispatch so the settle
        // animation can find the freshly created card after the rebuild.
        private LayoutNode _createdNode;
        // Multi-binding add: every node created by the OnAddNodes dispatch, so each fresh card can
        // settle from its slot in the stack ghost (parallel to _createdNode for single adds).
        private readonly List<LayoutNode> _createdNodes = new();

        // The token paths a Bindings-tab drag is carrying (one entry for a single token, the whole
        // multi-selection otherwise). Drives the stacked add ghost and the multi-add commit.
        private readonly List<string> _addPaths = new();

        // Spring-loaded tabs: hovering a registered tab button mid-drag auto-switches to it after a
        // dwell. GC-keyed so stale registrations from old canvas rebuilds clean themselves up.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Action> _springTabs = new();
        private VisualElement _springEl;
        private IVisualElementScheduledItem _springTimer;

        // "Create a new tab here" drop zones (the Tabs "+"): element → the Tabs container.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, LayoutNode> _dropZones = new();

        public bool IsDragging => _dragging;

        // The DragOrigin of the drag currently being committed — set by CommitDrag right before it
        // dispatches OnMoveNode/OnAddNode, so those callbacks can tell an Organizer move (pure
        // reorganization, must NOT navigate the Properties panel) from a canvas/hierarchy one.
        /// <summary>The <see cref="DragOrigin"/> of the drag being committed — valid while OnMoveNode/OnAddNode run.</summary>
        public DragOrigin CommittingOrigin { get; private set; }

        // Whether the drop being committed landed on a CELL of a tracked container. Published the same
        // way as CommittingOrigin (right before dispatch) so the move handlers can tell a TRACK ORDINAL
        // from a model index: a lifted card's track is held by a temp pad, so the tracks never renumber
        // and the slot must NOT be re-based against the dragged node's old index. See ResolvedDrop.CellTrack.
        /// <summary>True when the drop being committed targets a tracked container's cell, so its slot is a
        /// track ordinal (already post-removal) rather than a model index — valid while OnMoveNode runs.</summary>
        public bool CommittingCellTrack { get; private set; }

        /// <summary>Creates a controller that draws its ghost/indicators on <paramref name="overlayHost"/> and auto-scrolls <paramref name="canvasScroll"/> during a drag.</summary>
        public CanvasDragController(VisualElement overlayHost, ScrollView canvasScroll)
        {
            _scroll = canvasScroll;
            _windowRoot = overlayHost;

            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;

            _indicators = new DropIndicatorLayer(_overlay);
            _ghost = new DragGhost(_overlay);
            _gap = new GapAnimator();

            _deleteZone = new VisualElement { pickingMode = PickingMode.Ignore };
            _deleteZone.AddToClassList("drop-delete-zone");
            var trashLabel = new Label("Drag here to remove") { pickingMode = PickingMode.Ignore };
            trashLabel.AddToClassList("drop-delete-label");
            _deleteZone.Add(trashLabel);
            _deleteZone.style.display = DisplayStyle.None;
            _scroll.Add(_deleteZone);

            overlayHost.Add(_overlay);

            // Window resize mid-drag shifts every rect — rebuild the snapshot when it settles.
            _windowRoot.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_dragging) InvalidateSnapshot();
            });
        }

        // Called by the window after each canvas rebuild. A rebuild mid-drag would orphan the whole
        // snapshot, so it defensively cancels (spring tabs switch in place and never come through here).
        /// <summary>Sets the canvas root element the drag operates within.</summary>
        public void SetCanvasRoot(VisualElement rootElement)
        {
            if (_dragging) CancelDrag();
            _canvasRoot = rootElement;
        }

        /// <summary>Registers a scrollable drop region (canvas / hierarchy / organizer) with auto-scroll near its edges.</summary>
        public void AddDropRegion(ScrollView scroll, DropRegionKind kind)
        {
            if (scroll == null) return;
            _dropRegions.Add((scroll, kind));
        }

        // Compatibility overload kept so older call sites still compile. `reflow` is not consulted —
        // every viewport registered this way becomes an organizer-style "indicator only" pane.
        /// <summary>Registers a plain (non-scrolling) viewport as a drop region.</summary>
        public void AddDropRegion(VisualElement viewport, bool reflow)
        {
            var scroll = viewport?.GetFirstAncestorOfType<ScrollView>();
            if (scroll != null) AddDropRegion(scroll, DropRegionKind.Organizer);
        }

        // Register a tab button so hovering it during a drag auto-switches to that tab (spring-load).
        // `activate` flips the tab's content visibility in place (no rebuild).
        /// <summary>Registers a tab button that springs open when a drag hovers it, so a node can be dropped into a collapsed tab.</summary>
        public void RegisterSpringTab(VisualElement tabButton, Action activate)
        {
            if (tabButton == null || activate == null) return;
            _springTabs.Remove(tabButton);
            _springTabs.Add(tabButton, activate);
        }

        // Register a "create a new tab here" drop zone (the Tabs "+").
        /// <summary>Registers an explicit drop-zone element that targets <paramref name="target"/>.</summary>
        public void RegisterDropZone(VisualElement zone, LayoutNode target)
        {
            if (zone == null || target == null) return;
            _dropZones.Remove(zone);
            _dropZones.Add(zone, target);
        }

        // Register the panel that, when an existing node is dropped on it, UNBINDS that node (the
        // Bindings token bank). `active` gates it (only when the Bindings tab is actually showing).
        /// <summary>Designates the "remove" zone (active while <paramref name="active"/> returns true) — dropping there deletes the dragged node(s).</summary>
        public void SetRemoveZone(VisualElement zone, Func<bool> active)
        {
            _removeZone = zone;
            _removeActive = active;
        }

        // Keep the ghost + indicator overlay above the rest of the window.
        /// <summary>Raises the drag overlay above the rest of the UI.</summary>
        public void BringToFront()
        {
            _overlay.BringToFront();
        }

        // The window calls this from HandlePropertyDrop (synchronously inside the OnAddNode dispatch)
        // so the settle animation can target the node that was just created.
        /// <summary>Tells the controller a node was just created so it can select and track it.</summary>
        public void NotifyCreatedNode(LayoutNode node)
        {
            _createdNode = node;
        }

        // The window calls this from HandleMultiPropertyDrop (synchronously inside the OnAddNodes
        // dispatch) with every node it created, so each fresh card can glide in from the stack ghost.
        /// <summary>Tells the controller several nodes were just created (multi-select them).</summary>
        public void NotifyCreatedNodes(IEnumerable<LayoutNode> nodes)
        {
            _createdNodes.Clear();
            if (nodes != null) foreach (var n in nodes) if (n != null) _createdNodes.Add(n);
        }

        // A layout-affecting change happened mid-drag (spring-tab switch, window resize). Freeze the
        // current target, snap the gap shut so the re-read sees NATURAL geometry (worldBounds include
        // our translates/expansion), then re-snapshot once flex layout has settled.
        /// <summary>Discards the cached geometry snapshot so the next drag re-measures the canvas.</summary>
        public void InvalidateSnapshot()
        {
            if (!_dragging || _snapshot == null) return;
            _snapshot.Stale = true;
            _gap.ClearGap(instant: true);
            _overlay.schedule.Execute(() =>
            {
                if (!_dragging) return;
                BuildSnapshot();
                ResolveAndApply(_lastPanelPos);
            }).StartingIn(DragTuning.SnapshotRebuildDelayMs);
        }

        /// <summary>Routes a click on a draggable to selection (additive / range / double-click open), per the payload's <see cref="DragOrigin"/>.</summary>
        public void HandleClick(CanvasDragPayload payload, bool additive, bool range, int clickCount = 1)
        {
            if (payload == null) return;
            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode)
            {
                // Organizer cards select WITHOUT navigating the Properties panel; everything else
                // (canvas/hierarchy) selects through the main selection.
                if (payload.Origin == DragOrigin.Organizer)
                    OnSelectOrganizerNode?.Invoke(payload.Node, additive, range, clickCount);
                else
                    OnSelectNode?.Invoke(payload.Node, additive, range);
            }
            else if (payload.Kind == CanvasDragPayload.DragKind.AddBinding)
            {
                if (payload.BatchPaths != null && payload.BatchPaths.Count > 0)
                    // The "C# Properties" card: click opens/closes it (drag adds them all).
                    OnBatchAddClick?.Invoke(payload);
                else
                    // Bindings-tab token: click selects it for multi-drag (does not add to the layout).
                    OnSelectBindingToken?.Invoke(payload.Path, additive, range);
            }
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Begins a drag for <paramref name="payload"/> from <paramref name="source"/>, capturing the frozen geometry snapshot.</summary>
        public void BeginDrag(CanvasDragPayload payload, VisualElement source, Vector2 panelPos)
        {
            if (_dragging) return;

            // Finish any in-flight cancel animation so its cleanup can't undim THIS drag's sources.
            _ghost.CancelFlight();
            _ghost.Remove();

            _dragging = true;
            _payload = payload;
            _source = source;
            _resolved = null;
            _overDelete = false;
            _overRemove = false;
            _createdNode = null;
            _lastPanelPos = panelPos;

            _windowRoot.AddToClassList("designer-dragging");

            // Dragged set (multi-selection aware) + its full closure, used everywhere a dragged
            // node must be invisible to targeting.
            _dragged.Clear();
            _draggedClosure.Clear();
            _addPaths.Clear();
            _createdNodes.Clear();
            _draggedTemplateOwner = null;
            int groupCount = 0;
            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode && payload.Node != null)
            {
                var group = ResolveDragGroup?.Invoke(payload);
                if (group != null && group.Count > 1 && group.Contains(payload.Node))
                {
                    foreach (var n in group) if (n != null) _dragged.Add(n);
                    groupCount = _dragged.Count;
                }
                else
                {
                    _dragged.Add(payload.Node);
                }
                foreach (var n in _dragged) AddClosure(n);
                _draggedTemplateOwner = FindTemplateOwner(payload.Node, RootModelNode());
            }
            else if (payload.Kind == CanvasDragPayload.DragKind.AddBinding && !string.IsNullOrEmpty(payload.Path))
            {
                // Bindings-tab multi-drag: carry every selected token path (or just this one).
                var paths = ResolveAddGroup?.Invoke(payload);
                if (paths != null && paths.Count > 1 && paths.Contains(payload.Path))
                    _addPaths.AddRange(paths);
                else
                    _addPaths.Add(payload.Path);
                groupCount = _addPaths.Count;
            }

            // Gap size: the source's TRUE height, so the preview gap is exactly the space the card
            // will occupy — the released layout then matches the preview pixel-for-pixel. Only a
            // sanity cap, no aggressive clamp (the source leaves the layout, so size is conserved).
            float srcH = source != null && source.worldBound.height > 1f ? source.worldBound.height : 0f;
            bool isMove = payload.Kind == CanvasDragPayload.DragKind.MoveNode ||
                          payload.Kind == CanvasDragPayload.DragKind.MoveCollectionItem;
            _gapMainV = (isMove && srcH > 0f)
                ? Mathf.Clamp(srcH, DragTuning.MinGapPx, DragTuning.MaxGapSanityPx)
                : DragTuning.AddGapPx;

            // Stack + gap profile: every held card's TRUE geometry, captured BEFORE anything leaves
            // the layout. The profile carries one height per held card so the preview gap, the
            // expansion and the insertion lines are exactly the space the drop will occupy.
            int grabbedStackIndex = BuildDragStack(payload, srcH);
            // Canvas gap heights: move = each held card's true height; add = uniform AddGap per token.
            // ResolveAndApply rebuilds the gap from the compact row height when over the organizer.
            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode && _dragHeights.Count > 0)
                _canvasGapHeights = _dragHeights.ToArray();
            else if (payload.Kind == CanvasDragPayload.DragKind.AddBinding && _addPaths.Count > 0)
                _canvasGapHeights = UniformHeights(_addPaths.Count, DragTuning.AddGapPx);
            else
                _canvasGapHeights = new[] { _gapMainV };
            _gapProfile = new DragGapProfile(_canvasGapHeights, _fallbackSpacingV);

            // A drag picked up from a hierarchy row OR an organizer card STARTS in compact presentation
            // — the ghost is built at row height directly, so it can never flash canvas-sized and
            // shrink. (The organizer follows the hierarchy's compact feel; size only "expands" when the
            // card is dragged out over the canvas.)
            bool fromHier = false;
            if (source != null)
            {
                foreach (var (scroll, kind) in _dropRegions)
                {
                    if ((kind != DropRegionKind.Hierarchy && kind != DropRegionKind.Organizer) ||
                        scroll?.contentContainer == null) continue;
                    if (!DropSnapshotBuilder.IsWithin(source, scroll.contentContainer)) continue;
                    if (source.worldBound.height > 1f) _hierRowH = source.worldBound.height;
                    if (source.worldBound.width > 1f) _compactRowW = Mathf.Clamp(source.worldBound.width, 80f, 320f);
                    fromHier = true;
                    break;
                }
            }

            // Capture ghost geometry BEFORE anything is removed from the layout. Every MoveNode drag
            // (single included) uses the stack ghost so one code path owns the region morph.
            _sourceWorldPos = source != null ? (Vector2)source.worldBound.position : panelPos;
            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode && _dragStack.Count >= 1)
            {
                var infos = new List<GhostCardInfo>(_dragStack.Count);
                for (int i = 0; i < _dragStack.Count; i++)
                {
                    var cardPayload = CanvasDragPayload.Move(_dragStack[i]);
                    infos.Add(new GhostCardInfo
                    {
                        Icon = cardPayload.Icon,
                        Label = cardPayload.Label,
                        Kind = DragGhost.KindLabel(cardPayload),
                        Height = _dragHeights[i]
                    });
                }
                var grabbedEl = FindCanvasElement(payload.Node);
                // When the grabbed node lives on an INACTIVE tab its canvas element is display:none and
                // never laid out (worldBound == 0). Falling straight to the compact source row width
                // would pin the "canvas" width to the tab-organizer size, so the ghost never expands
                // over the canvas. Prefer the canvas content width (what a flex-grow card fills) first.
                float canvasW = _canvasRoot != null && _canvasRoot.worldBound.width > 1f ? _canvasRoot.worldBound.width : 0f;
                float w = grabbedEl != null && grabbedEl.worldBound.width > 1f ? grabbedEl.worldBound.width
                        : canvasW > 1f ? canvasW
                        : source != null && source.worldBound.width > 1f ? source.worldBound.width : 200f;
                // w = the full canvas card width (used over the canvas); _compactRowW = the narrow row
                // width (used over the organizer/hierarchy). The ghost morphs between them.
                _ghost.Show(infos, grabbedStackIndex, w, _fallbackSpacingV, fromHier, _compactRowW, _hierRowH);
                _ghostHier = fromHier;
            }
            else if (payload.Kind == CanvasDragPayload.DragKind.AddBinding && _addPaths.Count > 1)
            {
                // Multi-token Bindings drag: a compact stack of chips (no source size to preserve),
                // one per selected token, matching the canvas multi-drag's stacked look.
                float rowH = source != null && source.worldBound.height > 1f ? source.worldBound.height : 24f;
                var infos = new List<GhostCardInfo>(_addPaths.Count);
                foreach (var p in _addPaths)
                {
                    infos.Add(new GhostCardInfo
                    {
                        Icon = SasquatchIcons.Ids.Link,
                        Label = p,
                        Kind = "BINDING",
                        Height = rowH
                    });
                }
                int grabbedIndex = Mathf.Max(0, _addPaths.IndexOf(payload.Path));
                float w = source != null && source.worldBound.width > 1f ? source.worldBound.width : 200f;
                // New tokens have no canvas card, so the chips stay compact everywhere (canvas width
                // == compact width == the token row's width).
                _compactRowW = Mathf.Clamp(w, 80f, 320f);
                _hierRowH = rowH;
                _ghost.Show(infos, grabbedIndex, w, _fallbackSpacingV, startCompact: true, compactRowW: w, compactRowH: rowH);
                _ghostHier = true;
            }
            else
            {
                _ghost.Show(payload, source, groupCount);
                _ghostHier = false;
            }
            Vector2 grabLocal = source != null ? panelPos - (Vector2)source.worldBound.position : new Vector2(12f, 10f);
            _grabLocalY = Mathf.Clamp(grabLocal.y, 8f, 24f);
            _grabLocalX = grabLocal.x;
            _grabOffset = new Vector2(
                Mathf.Clamp(_grabLocalX, 8f, _ghost.Width - 16f),
                _ghost.GrabbedOffsetY + GrabLocalYForMode(_ghostHier));

            // The picked-up item LEAVES the canvas/organizer (the layout closes up, Unity-style —
            // no dimmed leftover for the preview to overlap). Hierarchy rows and palette tokens dim
            // in place instead. The snapshot is built after the closed-up layout settles.
            LiftAndHideSources();
            InsertTempTrackSlots();

            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode)
                _deleteZone.style.display = DisplayStyle.Flex;

            _snapshot = null;
            _overlay.schedule.Execute(() =>
            {
                if (!_dragging) return;
                BuildSnapshot();
                ResolveAndApply(_lastPanelPos);
            }).StartingIn(DragTuning.SnapshotRebuildDelayMs);

            _autoScroll = _overlay.schedule.Execute(AutoScrollTick).Every(16);
            UpdateDrag(panelPos);
        }

        /// <summary>Updates the in-flight drag to a new pointer position — resolves the drop target and moves the ghost/indicators.</summary>
        public void UpdateDrag(Vector2 panelPos)
        {
            if (!_dragging) return;
            _lastPanelPos = panelPos;
            _ghost.Follow(_overlay.WorldToLocal(panelPos) - _grabOffset);
            ResolveAndApply(panelPos);
        }

        /// <summary>Commits the current drag — inserts/moves the dragged node(s) at the resolved parent + slot (or deletes over the remove zone).</summary>
        public void CommitDrag()
        {
            if (!_dragging) return;

            var payload = _payload;
            // Publish the origin BEFORE dispatch so the move/add callbacks can read it (an Organizer
            // move keeps the Properties panel on the Tabs container instead of jumping to the moved card).
            CommittingOrigin = payload?.Origin ?? DragOrigin.Canvas;
            CommittingCellTrack = _resolved != null && _resolved.CellTrack;
            var resolved = _resolved;
            bool overDelete = _overDelete;
            bool overRemove = _overRemove;
            // The held nodes, captured before Cleanup clears _dragged — so a group remove can take all.
            var removeSet = _dragged.Count > 0 ? new List<LayoutNode>(_dragged) : null;
            Vector2 ghostWorld = _ghost.CurrentWorldPosition;

            // Hierarchy reorders: the ghost parks in the (horizontally-offset) hierarchy panel, so
            // gliding the rebuilt canvas card from there sweeps mostly SIDEWAYS. Capture each moved
            // card's true pre-rebuild canvas position instead, so the settle delta is the real
            // on-canvas displacement — vertical for normal stacks, horizontal only for columns/rows.
            Dictionary<LayoutNode, Vector2> canvasOriginByNode = null;
            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode && payload.Origin == DragOrigin.Hierarchy)
            {
                canvasOriginByNode = new Dictionary<LayoutNode, Vector2>();
                void CaptureCanvasOrigin(LayoutNode n)
                {
                    if (n == null || canvasOriginByNode.ContainsKey(n)) return;
                    var el = FindCanvasElement(n);
                    if (el != null && el.panel != null && el.worldBound.width > 0f && !float.IsNaN(el.worldBound.x))
                        canvasOriginByNode[n] = el.worldBound.position;
                }
                if (_dragStack.Count > 1)
                    foreach (var n in _dragStack) CaptureCanvasOrigin(n);
                else
                    CaptureCanvasOrigin(payload.Node);
            }

            // Multi-drag: capture the stack (Cleanup clears it) and each card's CURRENT Y offset
            // within the ghost (mode-aware — hierarchy rows sit tighter than canvas cards) so EVERY
            // moved card can glide from where its facsimile actually was, not just the grabbed one.
            List<(LayoutNode node, float offsetY)> settleStack = null;
            if (payload.Kind == CanvasDragPayload.DragKind.MoveNode && _dragStack.Count > 1)
            {
                var offsets = _ghost.GetCardOffsets();
                settleStack = new List<(LayoutNode, float)>(_dragStack.Count);
                for (int i = 0; i < _dragStack.Count; i++)
                    settleStack.Add((_dragStack[i], offsets != null && i < offsets.Length ? offsets[i] : 0f));
            }

            // Multi-token Bindings add: snapshot the held paths BEFORE Cleanup (which clears _addPaths)
            // so the dispatch below still sees the whole selection — otherwise the multi branch is
            // skipped and only the grabbed token lands. Mirrors the settleStack/removeSet pre-capture.
            List<string> addPaths = (payload.Kind == CanvasDragPayload.DragKind.AddBinding && _addPaths.Count > 1)
                ? new List<string>(_addPaths) : null;

            // The ghost survives the commit: it parks at the drop point until the rebuilt card is
            // found, then hands off to the settle glide — no dead frames where nothing is visible.
            Cleanup(instantGap: true, keepGhost: true);

            LayoutNode settleNode = null;
            bool dispatched = false;
            switch (payload.Kind)
            {
                case CanvasDragPayload.DragKind.MoveNode:
                    if (overDelete || overRemove)
                    {
                        // Delete zone OR the Bindings panel (unbind): remove the whole held set.
                        if (removeSet != null && removeSet.Count > 1) OnDeleteNodes?.Invoke(removeSet);
                        else OnDeleteNode?.Invoke(payload.Node);
                        dispatched = true;
                    }
                    else if (resolved != null && resolved.IsCommittable && resolved.TargetParent != null)
                    {
                        // An organizer multi-drag moves the organizer's own selection as a block
                        // (keeping the Tabs container selected); everything else uses the normal path.
                        if (payload.Origin == DragOrigin.Organizer && _dragged.Count > 1)
                            OnMoveOrganizerGroup?.Invoke(resolved.TargetParent, DropRelation.AsChild, resolved.Slot);
                        else
                            OnMoveNode?.Invoke(payload.Node, resolved.TargetParent, DropRelation.AsChild, resolved.Slot);
                        settleNode = payload.Node;
                        dispatched = true;
                    }
                    break;

                case CanvasDragPayload.DragKind.AddBinding:
                case CanvasDragPayload.DragKind.AddWidget:
                    if (resolved != null && resolved.IsCommittable && resolved.TargetParent != null)
                    {
                        if (payload.Kind == CanvasDragPayload.DragKind.AddBinding && addPaths != null)
                        {
                            // Multi-token add: create one node per path, then glide each fresh card
                            // in from its slot in the stack ghost (parallel to the multi-move settle).
                            _createdNodes.Clear();
                            OnAddNodes?.Invoke(resolved.TargetParent, addPaths, DropRelation.AsChild, resolved.Slot);
                            if (_createdNodes.Count > 0)
                            {
                                var offsets = _ghost.GetCardOffsets();
                                settleStack = new List<(LayoutNode, float)>(_createdNodes.Count);
                                for (int i = 0; i < _createdNodes.Count; i++)
                                    settleStack.Add((_createdNodes[i], offsets != null && i < offsets.Length ? offsets[i] : 0f));
                                settleNode = _createdNodes[0];
                            }
                            _createdNodes.Clear();
                        }
                        else
                        {
                            _createdNode = null;
                            OnAddNode?.Invoke(resolved.TargetParent, payload.Path, DropRelation.AsChild, resolved.Slot);
                            settleNode = _createdNode;
                            _createdNode = null;
                        }
                        dispatched = true;
                    }
                    break;

                case CanvasDragPayload.DragKind.MoveCollectionItem:
                    if (resolved != null && resolved.State == ResolvedDrop.DropState.ItemGap &&
                        resolved.ItemToIndex >= 0 && resolved.ItemToIndex != payload.ItemIndex)
                    {
                        OnMoveCollectionItem?.Invoke(payload.CollectionPropPath, payload.ItemIndex, resolved.ItemToIndex);
                        dispatched = true;
                    }
                    break;
            }

            // The move handlers run synchronously inside that switch, so the flag has done its job. Clear
            // it immediately: a stale "this slot is a track ordinal" would suppress the re-base on the NEXT
            // non-drag move (a hierarchy arrow, an undo replay) and silently move the wrong node.
            CommittingCellTrack = false;

            if (settleNode != null)
            {
                if (settleStack != null)
                {
                    // Each moved card glides from its own slot in the ghost stack — or, for a
                    // hierarchy reorder, from its true pre-move canvas position (vertical glide).
                    foreach (var (node, offsetY) in settleStack)
                    {
                        Vector2 origin = canvasOriginByNode != null && canvasOriginByNode.TryGetValue(node, out var o)
                            ? o
                            : ghostWorld + new Vector2(0f, offsetY);
                        ScheduleSettle(node, origin);
                    }
                }
                else
                {
                    Vector2 origin = canvasOriginByNode != null && canvasOriginByNode.TryGetValue(settleNode, out var o)
                        ? o
                        : ghostWorld;
                    ScheduleSettle(settleNode, origin);
                }
            }
            else if (!dispatched &&
                     (payload.Kind == CanvasDragPayload.DragKind.MoveNode ||
                      payload.Kind == CanvasDragPayload.DragKind.MoveCollectionItem))
            {
                // Invalid release: the item goes home — the GRABBED card (not the stack top) lands
                // back on its restored source. The sideways shake layered onto the flight reads
                // as the stack being tossed back: a wordless "that drop wasn't valid".
                _ghost.Shake();
                _ghost.FlyBackTo(
                    _overlay.WorldToLocal(_sourceWorldPos) - new Vector2(0f, _ghost.GrabbedOffsetY),
                    () => _ghost.Remove());
            }
            else
            {
                _ghost.FadeOutAndRemove();
            }
        }

        /// <summary>Cancels the in-flight drag, springing the ghost back and clearing all indicators.</summary>
        public void CancelDrag()
        {
            if (!_dragging) return;

            _dragging = false;
            _autoScroll?.Pause();
            _autoScroll = null;
            _springTimer?.Pause();
            _springTimer = null;
            _springEl = null;

            _gap.ClearGap(instant: false); // shifted cards slide home
            _indicators.HideAll();
            _deleteZone.style.display = DisplayStyle.None;
            _deleteZone.RemoveFromClassList("drop-delete-zone--active");
            ResetDeleteZoneScale();
            _windowRoot.RemoveFromClassList("designer-dragging");
            _snapshot = null;
            _resolved = null;
            _overDelete = false;
            _overRemove = false;

            // Ghost flies home; the removed card pops back into its slot as the ghost lands
            // (restoring the layout recreates the exact origin position, so they line up).
            var lifted = new List<VisualElement>(_lifted);
            _lifted.Clear();
            var hidden = new List<VisualElement>(_hidden);
            _hidden.Clear();
            var tempSlots = new List<VisualElement>(_tempSlots);
            _tempSlots.Clear();
            void Restore()
            {
                foreach (var el in tempSlots) el?.RemoveFromHierarchy(); // freed tracks close before the card pops back
                foreach (var el in lifted) el?.RemoveFromClassList("drag-lifted");
                // Clear OUR reason, don't assert Flex — see Hide.
                foreach (var el in hidden) ElementVisibility.SetHidden(el, ElementVisibility.Reason.Drag, false);
            }
            if (_source != null && _source.panel != null)
            {
                // The GRABBED card in the stack flies home onto its restored source.
                _ghost.FlyBackTo(
                    _overlay.WorldToLocal(_sourceWorldPos) - new Vector2(0f, _ghost.GrabbedOffsetY),
                    () => { Restore(); _ghost.Remove(); });
            }
            else
            {
                Restore();
                _ghost.FadeOutAndRemove();
            }
            _source = null;
            _dragStack.Clear();
            _dragHeights.Clear();
            _addPaths.Clear();
        }

        private void Cleanup(bool instantGap, bool keepGhost = false)
        {
            _dragging = false;
            _autoScroll?.Pause();
            _autoScroll = null;
            _springTimer?.Pause();
            _springTimer = null;
            _springEl = null;

            _gap.ClearGap(instantGap);
            foreach (var el in _lifted) el?.RemoveFromClassList("drag-lifted");
            _lifted.Clear();
            foreach (var el in _tempSlots) el?.RemoveFromHierarchy();
            _tempSlots.Clear();
            // Clear OUR reason, don't assert Flex — see LiftAndHideSources.Hide.
            foreach (var el in _hidden) ElementVisibility.SetHidden(el, ElementVisibility.Reason.Drag, false);
            _hidden.Clear();

            if (!keepGhost) _ghost.Remove();
            _indicators.HideAll();
            _deleteZone.style.display = DisplayStyle.None;
            _deleteZone.RemoveFromClassList("drop-delete-zone--active");
            ResetDeleteZoneScale();
            _windowRoot.RemoveFromClassList("designer-dragging");

            _snapshot = null;
            _source = null;
            _overDelete = false;
            _overRemove = false;
            _dragStack.Clear();
            _dragHeights.Clear();
            _addPaths.Clear();
        }

        // ------------------------------------------------------------------ snapshot

        private void BuildSnapshot()
        {
            var snap = new DragSnapshot();

            if (_payload.Kind == CanvasDragPayload.DragKind.MoveCollectionItem)
            {
                if (_source?.parent != null)
                {
                    var (slots, rect) = DropSnapshotBuilder.BuildItemSlots(_source.parent, _scroll.contentContainer, _source);
                    snap.ItemSlots = slots;
                    snap.ItemsContainerRect = rect;
                    snap.ItemsFrame = _scroll.contentContainer;
                    snap.ItemsContainerEl = _source.parent;
                }
                _snapshot = snap;
                return;
            }

            bool isAdd = _payload.Kind == CanvasDragPayload.DragKind.AddBinding ||
                         _payload.Kind == CanvasDragPayload.DragKind.AddWidget;

            Func<VisualElement, Action> springLookup = el => _springTabs.TryGetValue(el, out var a) ? a : null;
            Func<VisualElement, LayoutNode> zoneLookup = el => _dropZones.TryGetValue(el, out var t) ? t : null;

            snap.Regions.Add(DropSnapshotBuilder.BuildCanvasRegion(
                _scroll, _canvasRoot, isAdd, _draggedClosure, springLookup, zoneLookup));

            foreach (var (scroll, kind) in _dropRegions)
            {
                if (scroll == null) continue;
                switch (kind)
                {
                    case DropRegionKind.Hierarchy:
                        snap.Regions.Add(DropSnapshotBuilder.BuildHierarchyRegion(
                            scroll, HierarchyRowsProvider?.Invoke(), _draggedClosure));
                        break;
                    case DropRegionKind.Organizer:
                        snap.Regions.Add(DropSnapshotBuilder.BuildOrganizerRegion(
                            scroll, _draggedClosure, zoneLookup));
                        break;
                }
            }

            _snapshot = snap;
        }

        // ------------------------------------------------------------------ resolution

        // The grab anchor within the grabbed card — capped to the row when the card is row-sized.
        private float GrabLocalYForMode(bool hierarchy)
            => hierarchy ? Mathf.Min(_grabLocalY, Mathf.Max(4f, _hierRowH - 4f)) : _grabLocalY;

        // Morph the ghost to match the region under the pointer (compact organizer/hierarchy rows vs
        // full canvas cards) and re-anchor so the grabbed card stays under the cursor through the
        // size change. Re-applied each move so the compact dims track organizer<->hierarchy too.
        private void SetGhostRegion(bool compact)
        {
            bool flipped = _ghostHier != compact;
            _ghostHier = compact;
            float before = _ghost.GrabbedOffsetY;
            if (compact) _ghost.SetCompactMode(_compactRowW, _hierRowH);
            else _ghost.SetCanvasMode();
            if (flipped || !Mathf.Approximately(before, _ghost.GrabbedOffsetY))
            {
                _grabOffset.y = _ghost.GrabbedOffsetY + GrabLocalYForMode(compact);
                _ghost.Follow(_overlay.WorldToLocal(_lastPanelPos) - _grabOffset);
            }
        }

        // Ghost width follows the resolved landing cell (shrink-only; 0 = mode default). The X grab
        // anchor re-derives from the ORIGINAL grab point on every change, so the cursor never slides
        // off the card as it narrows and lands back exactly where it was when the width restores.
        private void SetGhostTargetWidth(float w)
        {
            float before = _ghost.Width;
            _ghost.SetTargetWidth(w);
            if (Mathf.Approximately(before, _ghost.Width)) return;
            _grabOffset.x = Mathf.Clamp(_grabLocalX, 8f, _ghost.Width - 16f);
            _ghost.Follow(_overlay.WorldToLocal(_lastPanelPos) - _grabOffset);
        }

        // Uniform per-card heights for a region-local gap (organizer/hierarchy: one compact row each).
        private static float[] UniformHeights(int count, float h)
        {
            count = Mathf.Max(1, count);
            h = Mathf.Max(12f, h);
            var a = new float[count];
            for (int i = 0; i < count; i++) a[i] = h;
            return a;
        }

        // A representative organizer card's on-screen size (the dragged ones are hidden) so the ghost
        // and gap can match the organizer's compact rows. Width/height are transform-stable to read.
        private bool TryGetOrganizerRowSize(RegionSnapshot region, out float w, out float h)
        {
            w = 0f; h = 0f;
            var content = region?.Scroll?.contentContainer;
            if (content == null) return false;
            VisualElement found = null;
            content.Query<VisualElement>(className: "organizer-card")
                .Where(e => e.resolvedStyle.display != DisplayStyle.None && e.worldBound.height > 1f)
                .ForEach(e => { if (found == null) found = e; });
            if (found == null) return false;
            w = found.worldBound.width;
            h = found.worldBound.height;
            return true;
        }

        private void ResolveAndApply(Vector2 panelPos)
        {
            if (!_dragging || _snapshot == null) return;

            if (_payload.Kind == CanvasDragPayload.DragKind.MoveCollectionItem)
            {
                ResolveItemDrag(panelPos);
                return;
            }

            // Delete zone takes priority (only visible for MoveNode).
            if (_deleteZone.style.display == DisplayStyle.Flex && _deleteZone.worldBound.Contains(panelPos))
            {
                if (!_overDelete && EditorSpringFX.MotionEnabled)
                    _deleteZone.SpringScale(1.06f, EditorSpringFX.Tune(SpringConfig.Bouncy)); // leans toward the held stack
                _overDelete = true;
                _deleteZone.AddToClassList("drop-delete-zone--active");
                _resolved = null;
                _indicators.HideAll();
                _gap.ClearGap(false);
                UpdateSpring(null);
                SetGhostRegion(compact: false);
                SetGhostTargetWidth(0f);
                return;
            }
            if (_overDelete && EditorSpringFX.MotionEnabled)
                _deleteZone.SpringScale(1f, EditorSpringFX.Tune(SpringConfig.Snappy));
            _overDelete = false;
            _deleteZone.RemoveFromClassList("drop-delete-zone--active");

            // Bindings panel = unbind target: dragging an existing node onto it removes it (back to
            // unused). MoveNode only, and only while the Bindings tab is actually showing. The ghost
            // turns red + goes compact (it's about to live in the bindings list as an unused token).
            bool overRemoveNow = _payload.Kind == CanvasDragPayload.DragKind.MoveNode && _removeZone != null &&
                                 (_removeActive == null || _removeActive()) &&
                                 _removeZone.worldBound.Contains(panelPos);
            if (overRemoveNow)
            {
                _overRemove = true;
                _ghost.SetRemoveTint(true);
                _resolved = null;
                _indicators.HideAll();
                _gap.ClearGap(false);
                UpdateSpring(null);
                SetGhostRegion(compact: true);
                SetGhostTargetWidth(0f);
                return;
            }
            if (_overRemove) { _overRemove = false; _ghost.SetRemoveTint(false); }

            // While the snapshot is stale (spring tab opening, window resizing) the last valid
            // target stays frozen — no flicker while flex settles.
            if (_snapshot.Stale) return;

            RegionSnapshot region = null;
            foreach (var r in _snapshot.Regions)
            {
                if (r.Scroll == null) continue;
                if (r.Viewport.worldBound.Contains(panelPos)) { region = r; break; }
            }

            if (region == null)
            {
                _resolved = null;
                _indicators.HideAll();
                _gap.ClearGap(false);
                UpdateSpring(null);
                SetGhostRegion(compact: false);
                SetGhostTargetWidth(0f);
                return;
            }

            // Ghost presentation AND the drop gap follow the region under the pointer: compact rows
            // (the region's own row width + height) over the hierarchy AND organizer — a tab kanban
            // reads like a tree, not a canvas — full card size + true heights only over the canvas.
            if (region.Kind == DropRegionKind.Hierarchy || region.Kind == DropRegionKind.Organizer)
            {
                if (region.Kind == DropRegionKind.Hierarchy && region.HierRows != null)
                {
                    foreach (var row in region.HierRows)
                        if (row.Rect.height > 1f)
                        {
                            _hierRowH = row.Rect.height;
                            _compactRowW = Mathf.Clamp(row.Rect.width, 80f, 320f);
                            break;
                        }
                }
                else if (region.Kind == DropRegionKind.Organizer &&
                         TryGetOrganizerRowSize(region, out float ow, out float oh))
                {
                    _compactRowW = Mathf.Clamp(ow, 80f, 320f);
                    _hierRowH = oh;
                }
                // The gap opens ONE compact row per held card here (never the tall canvas card size).
                _gapProfile = new DragGapProfile(UniformHeights(_canvasGapHeights.Length, _hierRowH), _fallbackSpacingV);
                SetGhostRegion(compact: true);
            }
            else
            {
                _gapProfile = new DragGapProfile(_canvasGapHeights, _fallbackSpacingV);
                SetGhostRegion(compact: false);
            }

            // Spring tabs (canvas only — rect tests against the snapshot, corrected by any known
            // visual displacement; no Pick).
            SpringRect spring = null;
            if (region.Springs.Count > 0)
            {
                Vector2 pLocal = region.Frame.WorldToLocal(panelPos);
                foreach (var s in region.Springs)
                {
                    var r = s.Rect;
                    r.position += _gap.GetOffset(s.El);
                    if (r.Contains(pLocal)) { spring = s; break; }
                }
            }
            UpdateSpring(spring);

            bool isAdd = _payload.Kind == CanvasDragPayload.DragKind.AddBinding ||
                         _payload.Kind == CanvasDragPayload.DragKind.AddWidget;

            // The ghost is translate-positioned at (panelPos - _grabOffset), so this IS the held
            // card's left edge in panel space — no layout read needed.
            float ghostLeftX = panelPos.x - _grabOffset.x;
            _resolved = DropResolver.Resolve(region, panelPos, isAdd, _draggedTemplateOwner, _resolved,
                _gap.GetOffset, _gapProfile, DragTuning.HorizontalGapPx, ghostLeftX);
            ApplyResolved(_resolved);
        }

        private void ApplyResolved(ResolvedDrop r)
        {
            SetGhostTargetWidth(r == null || r.State == ResolvedDrop.DropState.None ? 0f : r.GhostWidth);

            if (r == null || r.State == ResolvedDrop.DropState.None)
            {
                _indicators.HideAll();
                _gap.ClearGap(false);
                return;
            }

            // Landing cells are the canvas's primary "lands here" signal; the insertion line(s) only
            // draw when a state has no landing rects (hierarchy line, zones, collection targets).
            bool hasLanding = r.LandingCellsWorld != null && r.LandingCellsWorld.Length > 0;

            switch (r.State)
            {
                case ResolvedDrop.DropState.Gap:
                    _indicators.ShowOutline(r.OutlineWorld, tint: false, thin: false, radius: r.OutlineRadius);
                    _indicators.ShowContextOutline(r.ContextOutlineWorld);
                    // Neutral ring on the tracked container this drop is passing over to land beside it.
                    _indicators.ShowEscapeRing(r.EscapedBoxWorld, r.OutlineRadiusEscaped);
                    if (hasLanding) _indicators.HideLine();
                    else _indicators.ShowLines(r.LineWorld, r.LineVertical, r.LineOffsets);
                    _indicators.ShowPhantomCells(r.PhantomCellsWorld, activeFirst: !hasLanding);
                    _indicators.ShowLandingCells(r.LandingCellsWorld, r.LandingCounts);
                    _indicators.ShowDisplacedCells(r.DisplacedCellsWorld); // null ⇒ hides; gap drops displace nothing
                    if (r.ReflowEls != null)
                    {
                        // A table-column reorder: an ordinary model-index insert (hence Gap, not Nest) whose
                        // preview is the real columns sliding to the arrangement the drop produces. There is
                        // no gap to open and no container to spring — the permutation IS the message.
                        _gap.ApplyReflow(r.ReflowEls, r.ReflowOffsets);
                    }
                    else if (r.ActiveSlot != null)
                    {
                        // GapMain is the full held-stack height; it MUST match the gap baked into
                        // ExpandTo (GapAnimator derives the natural height as expandTo - offset.y).
                        Vector2 offset = r.ActiveSlot.Horizontal
                            ? new Vector2(DragTuning.HorizontalGapPx, 0f)
                            : new Vector2(0f, r.GapMain);
                        // Siblings part AND the receiving container genuinely expands to make room.
                        _gap.Apply(r.ActiveSlot.ShiftSet, offset, r.ExpandEl, r.ExpandTo, r.ExpandPush);
                    }
                    else
                    {
                        // A committable drop that parts nothing must still SAY so, or whatever the previous
                        // target moved stays moved and the preview goes stale mid-drag. Every other branch
                        // reaches the animator on every resolve; this one used to be able to fall through.
                        _gap.Apply(null, Vector2.zero, null, 0f, null);
                    }
                    break;

                case ResolvedDrop.DropState.Nest:
                case ResolvedDrop.DropState.Zone:
                case ResolvedDrop.DropState.HierNest:
                    _indicators.ShowOutline(r.OutlineWorld, tint: true, thin: false, radius: r.OutlineRadius);
                    _indicators.ShowContextOutline(r.ContextOutlineWorld);
                    _indicators.ShowPhantomCells(r.PhantomCellsWorld, activeFirst: !hasLanding);
                    _indicators.ShowLandingCells(r.LandingCellsWorld, r.LandingCounts);
                    _indicators.ShowDisplacedCells(r.DisplacedCellsWorld);
                    _indicators.HideEscapeRing(); // dropping INTO something is never an escape
                    if (!hasLanding && r.LineWorld != Rect.zero) _indicators.ShowLines(r.LineWorld, r.LineVertical, r.LineOffsets);
                    else _indicators.HideLine();
                    if (r.ReflowEls != null)
                    {
                        // A reordering drop into a tracked container: the real cards slide to the tracks
                        // they will occupy, each by its own offset. That IS the preview, so the container
                        // must not also spring open (a reorder never changes its size).
                        _gap.ApplyReflow(r.ReflowEls, r.ReflowOffsets);
                    }
                    else
                    {
                        // Nest = append inside: the container expands by the incoming stack's size.
                        _gap.Apply(null, new Vector2(0f, r.GapMain), r.ExpandEl, r.ExpandTo, r.ExpandPush);
                    }
                    break;

                case ResolvedDrop.DropState.HierLine:
                    _indicators.ShowOutline(r.OutlineWorld, tint: false, thin: r.ThinOutline);
                    _indicators.HideContextOutline();
                    _indicators.HidePhantomCells();
                    _indicators.HideLandingCells();
                    _indicators.HideDisplacedCells();
                    _indicators.HideEscapeRing();
                    _indicators.ShowLine(r.LineWorld, vertical: false);
                    _gap.ClearGap(false);
                    break;
            }
        }

        private void ResolveItemDrag(Vector2 panelPos)
        {
            var slots = _snapshot.ItemSlots;
            if (slots == null || slots.Count == 0 || _snapshot.ItemsFrame == null)
            {
                _resolved = null;
                _indicators.HideAll();
                return;
            }

            if (!_scroll.contentViewport.worldBound.Contains(panelPos))
            {
                _resolved = null;
                _indicators.HideAll();
                _gap.ClearGap(false);
                return;
            }

            var frame = _snapshot.ItemsFrame;
            Vector2 p = frame.WorldToLocal(panelPos);

            ItemSlot best = null;
            float bestScore = float.MaxValue;
            float bestY = 0f;
            foreach (var s in slots)
            {
                float y = s.GapLine.y + (s.AboveEl != null ? _gap.GetOffset(s.AboveEl).y : 0f);
                bool active = _resolved != null && _resolved.State == ResolvedDrop.DropState.ItemGap &&
                              _resolved.ItemGapIndex == s.GapIndex;
                float d;
                if (active)
                {
                    // Inside the open gap = still this slot (sticky region, Unity-list behavior).
                    float y2 = y + _gapMainV;
                    d = p.y < y ? y - p.y : (p.y > y2 ? p.y - y2 : 0f);
                    d -= DragTuning.HysteresisPx;
                }
                else d = Mathf.Abs(p.y - y);
                if (d < bestScore) { bestScore = d; best = s; bestY = y; }
            }
            if (best == null) { _resolved = null; _indicators.HideAll(); return; }

            var line = new Rect(best.GapLine.xMin, bestY - 1f, best.GapLine.width, 2f);
            var outline = _snapshot.ItemsContainerRect;
            outline.height += _gapMainV; // the list expands to make room
            _resolved = new ResolvedDrop
            {
                State = ResolvedDrop.DropState.ItemGap,
                ItemToIndex = best.ToIndex,
                ItemGapIndex = best.GapIndex,
                LineWorld = DropSnapshotBuilder.ContentToWorld(frame, line),
                OutlineWorld = DropSnapshotBuilder.ContentToWorld(frame, outline)
            };

            _indicators.ShowOutline(_resolved.OutlineWorld, tint: false, thin: false);
            _indicators.ShowLine(_resolved.LineWorld, vertical: false);
            _gap.Apply(best.ShiftSet, new Vector2(0f, _gapMainV),
                _snapshot.ItemsContainerEl, _snapshot.ItemsContainerRect.height + _gapMainV, null);
        }

        // ------------------------------------------------------------------ springs

        private void UpdateSpring(SpringRect spring)
        {
            var el = spring?.El;
            if (el == _springEl) return; // no change — let an armed timer keep running
            _springEl = el;
            _springTimer?.Pause();
            _springTimer = null;
            if (spring != null)
            {
                var activate = spring.Activate;
                _springTimer = _overlay.schedule.Execute(() =>
                {
                    if (!_dragging) return;
                    activate();
                    // The switch flipped tab contents in place — every rect below it moved.
                    InvalidateSnapshot();
                }).StartingIn(DragTuning.SpringDelayMs);
            }
        }

        // ------------------------------------------------------------------ lift / settle

        private void LiftAndHideSources()
        {
            void Lift(VisualElement el)
            {
                if (el == null) return;
                el.AddToClassList("drag-lifted");
                _lifted.Add(el);
            }

            // Through the visibility ARBITER, not a raw style write: the canvas SEARCH filter hides cards the
            // same way (LayoutSearchFilter → ElementVisibility), and a direct Flex on the restore paths would
            // force-reveal a card the filter still wants hidden. Clearing our own reason instead leaves the
            // filter's hide intact.
            void Hide(VisualElement el)
            {
                if (el == null) return;
                ElementVisibility.SetHidden(el, ElementVisibility.Reason.Drag, true);
                _hidden.Add(el);
            }

            if (_payload.Kind == CanvasDragPayload.DragKind.MoveCollectionItem)
            {
                // The item row leaves the list; siblings close up.
                Hide(_source);
                return;
            }

            if (_payload.Kind != CanvasDragPayload.DragKind.MoveNode)
            {
                // Palette adds: the token row just dims so the bank shows what's in hand.
                Lift(_source);
                return;
            }

            // MoveNode: every canvas/organizer card of the dragged nodes leaves its layout.
            if (_dragged.Count > 0 && _canvasRoot != null && _canvasRoot.panel != null)
            {
                _canvasRoot.Query<VisualElement>()
                    .Where(e => e.userData is LayoutNode ln && _dragged.Contains(ln))
                    .ForEach(Hide);
            }
            foreach (var (scroll, kind) in _dropRegions)
            {
                if (kind != DropRegionKind.Organizer || scroll?.contentContainer == null) continue;
                scroll.contentContainer.Query<VisualElement>(className: "organizer-card")
                    .Where(e => e.userData is LayoutNode ln && _dragged.Contains(ln))
                    .ForEach(Hide);
            }

            // Hierarchy rows of the dragged nodes dim (the tree keeps its shape so indent
            // geometry stays stable), and a hierarchy-row source dims rather than hides.
            var rows = HierarchyRowsProvider?.Invoke();
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r.Row == null || r.Node == null) continue;
                    if (_draggedClosure.Contains(r.Node)) Lift(r.Row);
                }
            }
            if (_source != null && !_hidden.Contains(_source) && !_lifted.Contains(_source))
                Lift(_source);
        }

        // A tracked Rows/Columns container must not collapse below its track count while one of its
        // own children is in hand (the count promises N visible tracks): each lifted card's track
        // re-opens as a temporary "+" cell. Inserted AFTER the last visible card (before any real
        // "+" pads), matching where a rebuild would put the freed track — so the cell-fill preview's
        // first "+" cell stays exactly where an append genuinely lands. Runs before the deferred
        // snapshot build, so the snapshot sees the temp cells as ordinary reserved tracks; removed
        // on settle/cancel alongside the hidden cards' restore.
        private void InsertTempTrackSlots()
        {
            if (_canvasRoot == null) return;
            foreach (var el in _hidden)
            {
                if (el?.parent == null || !(el.userData is LayoutNode)) continue;
                if (!DropSnapshotBuilder.IsWithin(el, _canvasRoot)) continue; // organizer cards keep kanban geometry

                ContainerNode owner = null;
                for (var cur = el.parent; cur != null; cur = cur.parent)
                {
                    if (cur.userData is ContainerNode cn) { owner = cn; break; }
                    if (cur.userData is LayoutNode) break; // crossed a non-container node boundary
                }
                if (owner == null || owner.GridColumns <= 0) continue;
                // Fixed-constraint Grids promise their declared tracks the same way tracked
                // Rows/Columns do; a Flexible grid promises nothing (its column count is derived
                // from width), so lifting its child legitimately reflows it.
                bool trackedGrid = owner.Type == ContainerType.Grid
                                   && owner.GridConstraint != GridConstraint.Flexible;
                if (owner.Type != ContainerType.Rows && owner.Type != ContainerType.Columns && !trackedGrid)
                    continue;

                var parent = el.parent;
                int insertAt = parent.childCount;
                for (int i = 0; i < parent.childCount; i++)
                {
                    if (parent[i].ClassListContains("sasq-drop-slot")) { insertAt = i; break; }
                }
                // Overflow-wrapped cells live in grid-row wrappers whose pads are margin-less grid
                // cells; everywhere else the container's own slot kind lines up with the real pads.
                var kind = parent.ClassListContains("sasq-grid-row") ? ContainerType.Grid : owner.Type;
                var slot = LayoutTreeBuilder.MakeEmptyDropSlot(kind);
                parent.Insert(insertAt, slot);
                _tempSlots.Add(slot);
            }
        }

        // Unity-inspector feel on release: the ghost parks at the drop point, and once the rebuilt
        // card exists with valid layout, the ghost vanishes in the same frame the card starts a
        // spring glide from the ghost's position into its slot, plus a brief highlight pulse. Driven
        // by explicit value animations — never USS transitions, whose class-toggle timing can
        // collapse the whole "slide" into a single frame.
        private void ScheduleSettle(LayoutNode node, Vector2 ghostWorld)
        {
            _overlay.schedule.Execute(() => TrySettle(node, ghostWorld, canRetry: true)).StartingIn(16);
        }

        private void TrySettle(LayoutNode node, Vector2 ghostWorld, bool canRetry)
        {
            var el = FindCanvasElement(node);
            bool ready = el != null && el.panel != null &&
                         !float.IsNaN(el.worldBound.width) && el.worldBound.width > 0f;
            if (!ready)
            {
                // The rebuild's layout pass may not have run yet — give it one more beat.
                if (canRetry)
                    _overlay.schedule.Execute(() => TrySettle(node, ghostWorld, canRetry: false)).StartingIn(50);
                else if (!_dragging)
                    _ghost.FadeOutAndRemove();
                return;
            }

            if (!_dragging) _ghost.Remove(); // hand-off: the card takes over where the ghost was

            Vector2 delta = ghostWorld - (Vector2)el.worldBound.position;
            if (EditorSpringFX.MotionEnabled)
            {
                if (delta.magnitude > 4f && delta.magnitude < 600f)
                {
                    // Spring glide into the slot — the slight overshoot past rest IS the
                    // landing "thunk". Suppress the card's own USS translate transition
                    // while the spring drives it.
                    el.AddToClassList("drag-no-transition");
                    var captured = el;
                    captured.style.translate = new Translate(delta.x, delta.y);
                    SettleGlide(captured);
                }
                else
                {
                    // Dropped essentially in place — a tiny scale thunk is the feedback.
                    EditorSpringFX.LandPunch(el, 0.45f);
                }
            }

            Pulse(el);
        }

        // The zone hides instantly on drag end — kill any hover-scale spring so the next
        // drag's zone doesn't appear pre-inflated.
        private void ResetDeleteZoneScale()
        {
            Spring.CancelAll(_deleteZone);
            _deleteZone.style.scale = new Scale(Vector3.one);
        }

        private static async void SettleGlide(VisualElement captured)
        {
            try
            {
                // Pixel spring + settle-gated cleanup: 0.5px epsilon keeps the cleanup at
                // visual arrival instead of after ~1s of sub-pixel ringing.
                await captured.SpringTranslate(Vector2.zero, EditorSpringFX.Tune(new SpringConfig(0.3f, 0.35f, 0.5f)))
                    .WaitFor(SasquatchSpring.AwaitStrategy.PhysicalSettling);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                // detached mid-glide (another rebuild) — fall through to the cleanup
            }
            captured.style.translate = new Translate(0f, 0f);
            captured.RemoveFromClassList("drag-no-transition");
        }

        // Brief blue wash that fades over ~450ms. Manual animation: the card's background-color
        // starts as a keyword (unset), which USS transitions can't interpolate from.
        private static void Pulse(VisualElement el)
        {
            var prevBg = el.style.backgroundColor;
            var pulse = DesignerPalette.DropPulse; // skin-aware base; the tween animates its alpha
            var anim = SasquatchTween.Start(el, 1f, 0f, 450,
                (e, v) => e.style.backgroundColor = new Color(pulse.r, pulse.g, pulse.b, pulse.a * v));
            anim.KeepAlive();
            anim.OnCompleted(() => el.style.backgroundColor = prevBg);
        }

        private VisualElement FindCanvasElement(LayoutNode node)
        {
            if (node == null || _canvasRoot == null || _canvasRoot.panel == null) return null;
            VisualElement found = null;
            _canvasRoot.Query<VisualElement>()
                .Where(e => ReferenceEquals(e.userData, node))
                .ForEach(e => { if (found == null) found = e; });
            return found;
        }

        // ------------------------------------------------------------------ auto-scroll

        private void AutoScrollTick()
        {
            if (!_dragging) return;

            ScrollView target = null;
            if (_scroll.contentViewport.worldBound.Contains(_lastPanelPos)) target = _scroll;
            else
            {
                foreach (var (scroll, _) in _dropRegions)
                {
                    if (scroll != null && scroll.contentViewport.worldBound.Contains(_lastPanelPos))
                    {
                        target = scroll;
                        break;
                    }
                }
            }
            if (target == null) return;

            Rect vb = target.contentViewport.worldBound;
            float delta = 0f;
            if (_lastPanelPos.y < vb.yMin + AutoScrollMargin) delta = -AutoScrollSpeed;
            else if (_lastPanelPos.y > vb.yMax - AutoScrollMargin) delta = AutoScrollSpeed;
            if (delta == 0f) return;

            // Clamp to the real scrollable range. Without this floor at 0, dragging near the top
            // keeps subtracting from scrollOffset and shoves the whole canvas down — and it sticks
            // (only reopening the window resets scrollOffset).
            float maxOffset = Mathf.Max(0f, target.contentContainer.worldBound.height - vb.height);
            float newY = Mathf.Clamp(target.scrollOffset.y + delta, 0f, maxOffset);
            if (Mathf.Approximately(newY, target.scrollOffset.y)) return;
            target.scrollOffset = new Vector2(target.scrollOffset.x, newY);

            // The snapshot is content-local (scroll-proof), but the on-screen indicators need to
            // track the scroll between pointer moves.
            ResolveAndApply(_lastPanelPos);
        }

        // ------------------------------------------------------------------ helpers

        private LayoutNode RootModelNode()
        {
            if (_canvasRoot?.userData is LayoutNode root) return root;
            var rows = HierarchyRowsProvider?.Invoke();
            if (rows != null && rows.Count > 0) return rows[0].Node;
            return null;
        }

        private void AddClosure(LayoutNode node)
        {
            if (node == null || !_draggedClosure.Add(node)) return;
            if (node is ContainerNode c && c.Children != null)
                foreach (var ch in c.Children) AddClosure(ch);
            if (node is CollectionNode col && col.ItemLayout != null)
                AddClosure(col.ItemLayout);
        }

        // Fills _dragStack/_dragHeights (topmost held nodes, visual order, true heights) and
        // _fallbackSpacingV; returns the grabbed node's index within the stack. Must run BEFORE
        // LiftAndHideSources — it reads live card geometry.
        private int BuildDragStack(CanvasDragPayload payload, float srcH)
        {
            _dragStack.Clear();
            _dragHeights.Clear();
            _fallbackSpacingV = 4f;
            if (payload.Kind != CanvasDragPayload.DragKind.MoveNode || _dragged.Count == 0) return 0;

            float fallbackH = Mathf.Clamp(srcH > 1f ? srcH : DragTuning.AddGapPx,
                DragTuning.MinGapPx, DragTuning.MaxGapSanityPx);

            // Topmost filter on the MODEL (element-independent): a dragged node inside another
            // dragged node's subtree moves with its ancestor and contributes no height of its own.
            var topmost = new List<LayoutNode>();
            foreach (var n in _dragged)
            {
                bool covered = false;
                foreach (var m in _dragged)
                {
                    if (m == n) continue;
                    if (NodeContains(m, n)) { covered = true; break; }
                }
                if (!covered) topmost.Add(n);
            }

            // Visual order from the live cards; nodes without measurable geometry (inactive tab,
            // collapsed group) go to the back at the fallback height.
            var visible = new List<(LayoutNode node, Rect rect)>();
            var hiddenNodes = new List<LayoutNode>();
            foreach (var n in topmost)
            {
                var el = FindCanvasElement(n);
                if (el != null && el.worldBound.height > 1f) visible.Add((n, el.worldBound));
                else hiddenNodes.Add(n);
            }
            visible.Sort((a, b) =>
            {
                int byY = a.rect.yMin.CompareTo(b.rect.yMin);
                return byY != 0 ? byY : a.rect.xMin.CompareTo(b.rect.xMin);
            });
            foreach (var (n, rect) in visible)
            {
                _dragStack.Add(n);
                _dragHeights.Add(Mathf.Clamp(rect.height, DragTuning.MinGapPx, DragTuning.MaxGapSanityPx));
            }
            foreach (var n in hiddenNodes)
            {
                _dragStack.Add(n);
                // No live geometry (inactive tab / collapsed group) -> estimate the card height from the
                // model so a tall node (e.g. a big asset preview) doesn't drag as a skinny compact sliver.
                _dragHeights.Add(EstimateHiddenCardHeight(n, fallbackH));
            }

            // Inter-card spacing measured around the grabbed card's siblings — the spacing the ghost
            // stack uses, and the resolver's fallback when a target can't measure its own.
            var grabbedEl = FindCanvasElement(payload.Node);
            var sibHost = grabbedEl?.parent;
            if (sibHost != null)
            {
                var sibs = new List<Rect>();
                foreach (var ch in sibHost.Children())
                {
                    if (ch.resolvedStyle.display == DisplayStyle.None) continue;
                    if (!(ch.userData is LayoutNode)) continue;
                    if (ch.worldBound.height <= 1f) continue;
                    sibs.Add(ch.worldBound);
                }
                sibs.Sort((a, b) => a.yMin.CompareTo(b.yMin));
                var gaps = new List<float>();
                for (int i = 1; i < sibs.Count; i++)
                {
                    float g = sibs[i].yMin - sibs[i - 1].yMax;
                    if (g >= 0f && g <= 16f) gaps.Add(g);
                }
                if (gaps.Count > 0) { gaps.Sort(); _fallbackSpacingV = gaps[gaps.Count / 2]; }
            }

            int grabbedIndex = _dragStack.IndexOf(payload.Node);
            return grabbedIndex >= 0 ? grabbedIndex : 0;
        }

        // A node on an inactive tab has a display:none canvas element (worldBound == 0), so its TRUE card
        // height can't be measured live — and the compact source-row fallback makes a tall node drag as a
        // skinny sliver whose preview gap doesn't match the released card. Estimate from the model instead:
        // a persisted resize height (the StyleApplier min-height) and/or an asset preview's square
        // thumbnail (PreviewSize) — both of which drive the released card's real height.
        private static float EstimateHiddenCardHeight(LayoutNode node, float fallback)
        {
            float h = fallback;
            var sh = node.Style.height;
            if (sh.isOverridden && sh.value > 1f) h = Mathf.Max(h, sh.value);
            if (node is BindingNode bn && bn.DisplayType == DisplayType.AssetPreview && bn.PreviewSize > 1f)
                h = Mathf.Max(h, bn.PreviewSize + 16f); // thumbnail + card chrome
            return Mathf.Clamp(h, DragTuning.MinGapPx, DragTuning.MaxGapSanityPx);
        }

        // Whether `target` lives anywhere inside `root`'s subtree (root itself excluded).
        private static bool NodeContains(LayoutNode root, LayoutNode target)
        {
            if (root is ContainerNode c && c.Children != null)
            {
                foreach (var ch in c.Children)
                {
                    if (ch == target || NodeContains(ch, target)) return true;
                }
            }
            if (root is CollectionNode col && col.ItemLayout != null)
            {
                if (col.ItemLayout == target || NodeContains(col.ItemLayout, target)) return true;
            }
            return false;
        }

        // Which collection's ItemLayout template (if any) the node lives inside — template-scoped
        // sources may only target slots within the same template.
        private static CollectionNode FindTemplateOwner(LayoutNode node, LayoutNode root)
        {
            if (node == null || root == null) return null;
            CollectionNode found = null;

            bool Search(LayoutNode cur, CollectionNode owner)
            {
                if (cur == null) return false;
                if (cur == node) { found = owner; return true; }
                if (cur is ContainerNode c && c.Children != null)
                {
                    foreach (var ch in c.Children)
                        if (Search(ch, owner)) return true;
                }
                if (cur is CollectionNode col && col.ItemLayout != null)
                    return Search(col.ItemLayout, col);
                return false;
            }

            Search(root, null);
            return found;
        }
    }
}
