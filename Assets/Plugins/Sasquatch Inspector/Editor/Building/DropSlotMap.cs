using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    // Which pane a drop region is. The canvas gets the full slot/nest treatment, the hierarchy gets
    // the Unity-style indent resolver, the organizer (Tab kanban) gets flat per-column slots.
    /// <summary>Which pane a drop region is — the canvas (full slot/nest), the hierarchy (indent resolver), or the organizer (flat per-column slots).</summary>
    public enum DropRegionKind { Canvas, Hierarchy, Organizer }

    // All the feel constants for drop resolution in one place, with the reasoning attached.
    internal static class DragTuning
    {
        // A gap line within this many px beats the surrounding container's nest body, so the thin
        // padding strip around children still inserts instead of nesting.
        public const float GapCaptureBias = 18f;
        // The current target keeps this much advantage before a neighbor can steal it (anti-flicker).
        public const float HysteresisPx = 8f;
        // Pointer within this many px of a container card's outer edge (along ITS parent's axis)
        // resolves to the sibling gaps around the container, never to nesting into it.
        public const float EdgeBandPx = 10f;
        // Added to a slot's score when the pointer is outside the slot's lane (wrong wrap-row, or far
        // left/right of a vertical gap). Keeps the slot reachable as a last resort but never preferred.
        public const float OutOfLanePenalty = 1000f;
        // Horizontal slack allowed around a vertical-stack gap line before it counts as out-of-lane.
        public const float VerticalSlotXSlack = 24f;
        // Gap the siblings open for the incoming card: the source's TRUE height (it left the layout,
        // so total size is conserved and the preview matches the final drop exactly), with only a
        // sanity cap; a fixed value for palette adds (the new element's size isn't known yet).
        public const float MinGapPx = 24f;
        public const float MaxGapSanityPx = 600f;
        public const float AddGapPx = 36f;
        // Columns cells are flex-weighted, so a source-width horizontal gap would shove the row off
        // the canvas. A fixed visual gap + the insertion line carries the meaning.
        public const float HorizontalGapPx = 28f;
        // Hierarchy middle-band nest: enter band vs stay band (band hysteresis).
        public const float HierBandEnter = 0.30f;
        public const float HierBandStay = 0.25f;
        // Hierarchy indent unit — must match the depth term of RenderTree's
        // `paddingLeft = HierEyeGutter + depth * 15`. The gutter (the visibility eye's column) is a fixed
        // offset, NOT part of the unit: it is folded into HierBaseX in BuildHierarchyRegion instead, so the
        // X→depth mapping keeps measuring from where depth 0's CONTENT starts.
        public const float HierIndentUnit = 15f;
        // Extra px (in indent-units) the cursor must cross past a depth boundary before the chosen
        // depth steps (anti-jitter on the X→depth mapping).
        public const float HierDepthDeadzonePx = 4f;
        // Flex layout needs a beat to settle after a spring-tab switch before rects are re-read.
        public const int SnapshotRebuildDelayMs = 32;
        // Spring-loaded tabs: hover dwell before the tab auto-opens mid-drag.
        public const int SpringDelayMs = 450;
    }

    // What the drag is carrying, size-wise: one entry per held card (TRUE heights, stack order).
    // A single-card drag is a one-entry profile, which degenerates everywhere to the old single
    // gapMainV behavior. FallbackSpacing fills in when a target container has too few children to
    // measure its own spacing.
    /// <summary>What a drag is carrying size-wise: the true height of each held card (stack order), with a fallback spacing for containers too sparse to measure their own.</summary>
    public readonly struct DragGapProfile
    {
        public readonly float[] Heights;
        public readonly float FallbackSpacing;

        /// <summary>Creates a gap profile from per-card <paramref name="heights"/> and a <paramref name="fallbackSpacing"/>.</summary>
        public DragGapProfile(float[] heights, float fallbackSpacing)
        {
            Heights = heights != null && heights.Length > 0 ? heights : new[] { DragTuning.AddGapPx };
            FallbackSpacing = fallbackSpacing;
        }

        public int Count => Heights.Length;

        public float TotalHeights
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < Heights.Length; i++) sum += Heights[i];
                return sum;
            }
        }

        // The full vertical gap to open: every held card plus the spacing BETWEEN them (the spacing
        // above the first card is the target container's own, already part of the slot geometry).
        /// <summary>The effective vertical gap to open at a slot, given the container's measured slot spacing.</summary>
        public float EffectiveGapV(float slotSpacing, bool hasSlotSpacing)
        {
            float spacing = hasSlotSpacing ? slotSpacing : FallbackSpacing;
            return TotalHeights + (Count - 1) * spacing;
        }

        // World-space Y offsets of insertion lines 2..N relative to line 1 (line i marks where held
        // card i's top edge will land).
        /// <summary>The Y offsets of the 2nd…Nth insertion lines relative to the first (multi-card drag).</summary>
        public float[] LineOffsets(float slotSpacing, bool hasSlotSpacing)
        {
            if (Count <= 1) return null;
            float spacing = hasSlotSpacing ? slotSpacing : FallbackSpacing;
            var offsets = new float[Count - 1];
            float y = 0f;
            for (int i = 1; i < Count; i++)
            {
                y += Heights[i - 1] + spacing;
                offsets[i - 1] = y;
            }
            return offsets;
        }

        // Row-aware landing math for HORIZONTAL targets (Columns/Grid): the held cards land
        // side-by-side filling rows of `cellsPerRow`, so the vertical space a drop needs is the sum
        // of the landing ROWS (each row as tall as its tallest card), not the full stacked heights —
        // the old stacked math opened a gap N cards tall for cards that land in one row.
        /// <summary>Height of each landing row when the held cards fill left-to-right, <paramref name="cellsPerRow"/> per row.</summary>
        public float[] RowHeights(int cellsPerRow)
        {
            cellsPerRow = Mathf.Max(1, cellsPerRow);
            int rows = Mathf.Max(1, Mathf.CeilToInt(Count / (float)cellsPerRow));
            var rowHeights = new float[rows];
            for (int i = 0; i < Count; i++)
            {
                int r = i / cellsPerRow;
                rowHeights[r] = Mathf.Max(rowHeights[r], Heights[i]);
            }
            return rowHeights;
        }

        /// <summary>The effective vertical gap a horizontal target must open: the landing rows' heights plus inter-row spacing.</summary>
        public float RowsGapV(int cellsPerRow, float slotSpacing, bool hasSlotSpacing)
        {
            float spacing = hasSlotSpacing ? slotSpacing : FallbackSpacing;
            var rowHeights = RowHeights(cellsPerRow);
            float sum = 0f;
            foreach (var h in rowHeights) sum += h;
            return sum + (rowHeights.Length - 1) * spacing;
        }
    }

    // Mirror of the window's private HierRow, handed to the engine via HierarchyRowsProvider.
    /// <summary>A hierarchy row's model parent, depth and flags, for the indent drop resolver.</summary>
    public struct HierRowInfo
    {
        public LayoutNode Node;
        public int Depth;
        public ContainerNode Parent;   // model parent (null for root / ItemLayout template roots)
        public int IndexInParent;      // -1 for root / template roots
        public bool Nestable;          // non-Tabs ContainerNode outside an item template
        public bool Collapsed;
        public bool InItemTemplate;    // lives under a CollectionNode.ItemLayout
        public VisualElement Row;
    }

    // One legal insertion point: "insert into Parent.Children at Index". GapLine is a degenerate rect
    // (zero height for vertical stacks, zero width for horizontal rows) in content-local space.
    /// <summary>One insertion slot in a frozen drop snapshot: its gap line, the receiving parent's rect, and the sibling elements that shift to open the gap.</summary>
    public sealed class DropSlot
    {
        public ContainerNode Parent;
        public int Index;
        public Rect GapLine;
        public bool Horizontal;
        public int RowGroup;                 // wrap-row id within the parent (0 for vertical stacks)
        public VisualElement ParentEl;       // the receiving container's card element
        public Rect ParentRect;              // its content-local rect (for the parent outline)
        public VisualElement AboveEl;        // child element just above/left of the gap (null for slot 0)
        public CollectionNode TemplateOwner; // non-null when this slot lives inside an ItemLayout template
        public readonly List<VisualElement> ShiftSet = new(); // direct siblings that translate when this gap opens
        public List<VisualElement> LayoutPush;                // elements layout-pushed below by the container expansion (shared per container)
        // The element that actually GROWS for the preview: the children-host (structural parent of
        // the child cards), NOT ParentEl. Expandable containers wrap the bordered .sasq-group-content
        // INSIDE the userData root — expanding the root grows an invisible shell while the visible
        // border stays put. Growing the host instead propagates outward through every wrapper.
        public VisualElement ExpandHost;
        public float ExpandHostHeight;       // the host's content-local height at snapshot time
        public float ChildSpacingV;          // measured vertical spacing between this container's children
        public bool HasChildSpacing;         // false when <2 children were available to measure
        public float CellW;                  // the landing cell's width — the ghost shrinks to match (0 = default)
        // New-row slots only: one content-local rect per column of the row that will EXIST if the
        // drop lands (x-spans mirror the last real row; heights are filled in at resolve time from
        // the opened gap). The resolver presents these as phantom "+" cells instead of a bare line.
        public List<Rect> PhantomCells;
        // Shaped receiving container (Circle/Pill/rounded Frame): the parent outline's corner
        // radius, so the drop ring hugs the shape instead of boxing it. 0 = default square-ish ring.
        public float ParentRadius;
        // Vertical space the container's empty "+" placeholder cells already reserve (tracked Rows).
        // An insert consumes a reserved cell, so the preview only grows the container by the
        // SHORTFALL beyond this — not by the full stack (the "container inflates past its own
        // '+' rows" preview lie). 0 = no reserved cells.
        public float SpareCellsV;
    }

    // A "drop INSIDE this container (append)" target — active only over the container's own body.
    /// <summary>A container the pointer can nest into, with its body rect and the interior append line/index.</summary>
    public sealed class NestTarget
    {
        public LayoutNode Node;              // ContainerNode, or CollectionNode (Add payloads only)
        public Rect Body;
        public int AppendIndex;              // insertion index for the append (-1 = handler default)
        public VisualElement El;
        public bool IsTabButton;             // tab-strip button: outline only, no interior line
        // ONE VISIBLE CELL of a tracked container (a "＋" pad, or an EmptyCellNode holding a cell open)
        // rather than the container as a whole. A tracked container emits one of these per cell, each with
        // its own AppendIndex, so a drop can land in ANY opening instead of always the first free one. The
        // resolver picks between them by containment; see ConsiderCellNests.
        public bool IsCell;
        public bool Empty;                   // no visible children → nest wins instantly
        public bool Horizontal;              // Columns/Grid container (side-by-side append)
        public Rect ContainerBody;           // the container's FULL body — when Body is a cell-fill "+"
                                             // cell, this drives the thin context outline around the container
        public CollectionNode TemplateOwner;
        public Rect InteriorLine;            // content-local line at the append position (Rect.zero = none)
        public List<VisualElement> LayoutPush; // elements layout-pushed below by the container expansion
        public VisualElement ExpandHost;     // see DropSlot.ExpandHost — must be the SAME element the
        public float ExpandHostHeight;       // container's slots use, or hovering near the bottom
        public float ChildSpacingV;          // thrashes release/re-expand between slot and nest
        public bool HasChildSpacing;
        // Landing-cell preview data (multi-token-aware drops):
        public List<Rect> CellRects;         // the cells a drop here fills, from THIS cell rightward/down
        // Visual-only "＋" pads the container renders past its last child. Distinct from CellRects, which
        // starts at the targeted cell and also counts the EmptyCellNode children holding cells open —
        // those are already in VisibleChildCount, so the track-cap math must not add them twice.
        public int PadCount;
        public Rect HintRect;                // the "Empty Container" hint band (Rect.zero when none)
        // Safe grow-below host for overflow rows: the container's VISIBLE box (never a grid row —
        // springing a row's min-height stretches its cells; the new-row-slot precedent).
        public VisualElement GrowHost;
        public float GrowHostHeight;
        public float OutlineRadius;          // shaped container (Circle/Pill/rounded Frame) ring radius; 0 = default
        // Rows track-cap prediction inputs: how many children are actually visible, and the
        // content band the tracks occupy (children + "+" pads). The resolver uses these to lay
        // overflow landing cells into the NEW COLUMN a capped Rows container will create.
        public int VisibleChildCount;
        public float ContentTopY;            // content-local top of the first track
        public float ContentBandX0;          // content-local x span of the track band
        public float ContentBandX1;
    }

    // A tracked container (Columns/Rows with a track count, or a fixed-constraint Grid) reduced to what a
    // CELL-BASED drop model needs: its declared tracks in order, and what sits in each. Occupied tracks are
    // included — a partition that only knows the EMPTY cells has nothing to snap to over the full half of
    // the container, which is why the pointer could highlight a cell nowhere near it.
    //
    // Populated for every tracked container. See TrackedCellResolver for the model that consumes it.
    /// <summary>A tracked container's declared tracks in order, with each track's occupant (null = open).</summary>
    public sealed class TrackedBox
    {
        public ContainerNode Node;
        public VisualElement El;
        public Rect Body;                                  // the container's content-local box
        public readonly List<Rect> Tracks = new();         // one rect per visible track, in track order
        public readonly List<LayoutNode> Occupants = new(); // parallel to Tracks; null = an open "＋" cell
        // Parallel to Tracks: the real element in each track, so a REORDER can slide the actual cards to
        // their post-drop tracks instead of describing the move with overlay rectangles.
        public readonly List<VisualElement> TrackEls = new();
        public CollectionNode TemplateOwner;
        public float OutlineRadius;
        public bool Horizontal;                            // tracks run left-to-right (Columns/Grid)

        /// <summary>True when track <paramref name="i"/> is free — an unfilled "＋" pad or a held-open cell.</summary>
        public bool IsOpen(int i) =>
            i >= 0 && i < Occupants.Count && (Occupants[i] == null || Occupants[i] is EmptyCellNode);
    }

    // One column of a Table-mode collection on the designer canvas, as the drop model sees it: BOTH halves
    // (the header that names it and the representative row's cell that shows it), its model position, and
    // whether it is the column currently in hand.
    /// <summary>A table column's frozen geometry — header cell and data cell — for the column drop model.</summary>
    public sealed class TableColumnSnap
    {
        public LayoutNode Node;
        public int ModelIndex;          // index within the item-layout container's Children
        public Rect Cell;               // content-local rect of the representative row's cell
        public Rect Header;             // content-local rect of the column header
        public VisualElement CellEl;
        public VisualElement HeaderEl;
        public bool Dragged;            // this column's card is the thing being dragged
    }

    // A Table-mode collection's columns, reduced to what a REORDER model needs. Sibling of TrackedBox: the
    // same "a bounded set of tracks, and you are choosing which one" idea, but a table's tracks are columns
    // that live in two rows at once and never wrap, so it gets its own snapshot rather than bending that one.
    /// <summary>A table's column band on the designer canvas — every column's two halves, in layout order.</summary>
    public sealed class TableColumnBand
    {
        public ContainerNode Node;                 // the item-layout container the columns are children of
        public CollectionNode TemplateOwner;
        public VisualElement RowEl;                // the representative design row
        public Rect Body;                          // header ∪ row: the region this model answers for
        public Rect HeaderBand;                    // the header row's own rect
        public Rect RowBand;                       // the data row's own rect
        public readonly List<TableColumnSnap> Columns = new();
        public float Gap;                          // measured px between one column's cell and the next
    }

    // Snapshot of one card (any node) for context resolution — which card is under the pointer.
    /// <summary>A card's frozen rect + model parent in a drop snapshot (and whether it's a nestable container).</summary>
    public sealed class CardRect
    {
        public LayoutNode Node;
        public Rect Rect;
        public ContainerNode Parent;         // model parent (null when not a direct child, e.g. template roots)
        public int IndexInParent;
        public bool IsContainer;             // nestable container (non-Tabs ContainerNode)
        public bool IsTabs;
        public int Depth;
        public CollectionNode TemplateOwner;
        public VisualElement El;
    }

    /// <summary>An explicit drop zone's target node, rect, and element.</summary>
    public sealed class ZoneRect { public LayoutNode Target; public Rect Rect; public VisualElement El; }
    /// <summary>A spring-loaded tab's activate action, rect, and element.</summary>
    public sealed class SpringRect { public Action Activate; public Rect Rect; public VisualElement El; }

    // Collection-item reorder mode: one gap between item rows. ToIndex is the final
    // remove-then-insert index expected by MoveCollectionItem.
    /// <summary>One insertion slot between collection rows (List/Table reorder).</summary>
    public sealed class ItemSlot
    {
        public int GapIndex;
        public int ToIndex;
        public Rect GapLine;
        public VisualElement AboveEl;        // row just above the gap (null for the top gap)
        public readonly List<VisualElement> ShiftSet = new();
    }

    /// <summary>A hierarchy row's frozen geometry + flags for the indent drop resolver.</summary>
    public sealed class HierRowSnap
    {
        public LayoutNode Node;
        public int Depth;
        public ContainerNode Parent;
        public int IndexInParent;
        public bool Collapsed;
        public bool InItemTemplate;
        public bool Dragged;       // part of the dragged subtree: geometry only — never a nest/parent target
        public VisualElement Row;
        public Rect Rect;
    }

    // Everything resolvable about one drop region, captured once per drag (rects are content-local,
    // i.e. relative to the ScrollView's contentContainer, so scrolling never invalidates them).
    /// <summary>A frozen snapshot of one drop region — its slots, nest targets, cards, zones, and spring tabs.</summary>
    public sealed class RegionSnapshot
    {
        public DropRegionKind Kind;
        public ScrollView Scroll;
        public VisualElement Frame => Scroll.contentContainer;
        public VisualElement Viewport => Scroll.contentViewport;

        public readonly List<DropSlot> Slots = new();
        public readonly List<NestTarget> Nests = new();
        public readonly List<CardRect> Cards = new();
        public readonly List<ZoneRect> Zones = new();
        public readonly List<SpringRect> Springs = new();
        // Tracked containers, for the cell-based drop model. Always measured, inert unless it is enabled.
        public readonly List<TrackedBox> TrackedBoxes = new();
        // Table-mode collections' column bands (canvas, design mode only). See TableColumnResolver.
        public readonly List<TableColumnBand> TableColumnBands = new();

        // Canvas only
        public VisualElement CanvasRootEl;
        public ContainerNode RootNode;

        // Hierarchy only
        public List<HierRowSnap> HierRows;
        public float HierBaseX;   // content-local x of depth 0
        public float HierRightX;  // right edge for the insertion line
    }

    /// <summary>The complete frozen geometry for a drag across all regions (built once; the resolver works purely off it).</summary>
    public sealed class DragSnapshot
    {
        public readonly List<RegionSnapshot> Regions = new();
        public List<ItemSlot> ItemSlots;     // MoveCollectionItem mode only
        public Rect ItemsContainerRect;
        public VisualElement ItemsFrame;
        public VisualElement ItemsContainerEl;
        public bool Stale;                   // a layout change happened; rebuild pending
    }

    // Builds the geometry snapshots. Pure reads — never mutates the tree or styles.
    // Public, like the data types it returns (DropSlot / ItemSlot / DragGapProfile), so the slot and
    // index math can be exercised from outside this assembly.
    /// <summary>Builds the frozen <see cref="DragSnapshot"/> for a drag in a single measurement pass (the resolver then works purely off it).</summary>
    public static class DropSnapshotBuilder
    {
        /// <summary>Converts a world-space rect into <paramref name="frame"/>'s content-local space.</summary>
        public static Rect WorldToContent(VisualElement frame, Rect world)
            => new Rect(frame.WorldToLocal(world.position), world.size);

        /// <summary>Converts a content-local rect in <paramref name="frame"/> into world space.</summary>
        public static Rect ContentToWorld(VisualElement frame, Rect local)
            => new Rect(frame.LocalToWorld(local.position), local.size);

        /// <summary>True when <paramref name="el"/> is <paramref name="ancestor"/> or nested inside it.</summary>
        public static bool IsWithin(VisualElement el, VisualElement ancestor)
        {
            for (var cur = el; cur != null; cur = cur.parent)
                if (cur == ancestor) return true;
            return false;
        }

        // The element expansion should grow: the children-host (structural parent of the child card
        // elements). Growing it propagates outward through every wrapper — including the bordered
        // .sasq-group-content of expandable containers, which sits BELOW the userData root and
        // never grew when min-height was applied to the root itself. Empty containers fall back to
        // the visible .sasq-group-content (so the border still grows), else the card root.
        private static (VisualElement host, float height) ResolveExpandHost(
            VisualElement cEl, List<(VisualElement el, LayoutNode node)> childEls, VisualElement frame)
        {
            VisualElement host = null;
            if (childEls != null && childEls.Count > 0)
                host = childEls[childEls.Count - 1].el.parent;
            if (host == null) host = FindVisibleGroupContent(cEl);
            if (host == null) host = cEl;
            return (host, WorldToContent(frame, host.worldBound).height);
        }

        private static VisualElement FindVisibleGroupContent(VisualElement root)
        {
            foreach (var ch in root.Children())
            {
                if (ch.ClassListContains("sasq-group-content"))
                    return ch.resolvedStyle.display == DisplayStyle.None ? null : ch;
                var found = FindVisibleGroupContent(ch);
                if (found != null) return found;
            }
            return null;
        }

        // The corner radius the drop ring should use so it hugs a SHAPED container (a Circle/Pill
        // frame outlined by a square ring was the "circle previews as a rectangle" complaint).
        // Non-frames keep 0 (the indicator's default USS radius). Read once at snapshot time —
        // inline style first (BuildShell always seeds it; resolvedStyle can lag a live poke).
        private static float ShapedOutlineRadius(ContainerNode c, Rect body, VisualElement cEl)
        {
            if (c.Type != ContainerType.Frame || c.Frame == null) return 0f;
            switch (c.Frame.Shape)
            {
                case FrameShape.Circle: return Mathf.Min(body.width, body.height) * 0.5f;
                case FrameShape.Pill: return body.height * 0.5f;
                case FrameShape.DiagonalSplit: return 0f;
                default:
                    var inline = cEl.style.borderTopLeftRadius;
                    if (inline.keyword == StyleKeyword.Undefined) return Mathf.Max(0f, inline.value.value);
                    float r = cEl.resolvedStyle.borderTopLeftRadius;
                    return float.IsNaN(r) ? 0f : Mathf.Max(0f, r);
            }
        }

        // Median vertical gap between adjacent children — the spacing the held cards will adopt
        // when dropped here. Unmeasurable with fewer than two children (the drag's fallback
        // spacing applies instead).
        private static (float spacing, bool has) MeasureChildSpacingV(List<Rect> rects)
        {
            if (rects == null || rects.Count < 2) return (0f, false);
            var gaps = new List<float>(rects.Count - 1);
            for (int i = 1; i < rects.Count; i++)
                gaps.Add(Mathf.Clamp(rects[i].yMin - rects[i - 1].yMax, 0f, 32f));
            gaps.Sort();
            return (gaps[gaps.Count / 2], true);
        }

        // ---------------------------------------------------------------- canvas

        /// <summary>Measures the canvas into a <see cref="RegionSnapshot"/> (slots, nest targets, cards, zones, spring tabs).</summary>
        public static RegionSnapshot BuildCanvasRegion(
            ScrollView scroll,
            VisualElement canvasRoot,
            bool isAddPayload,
            HashSet<LayoutNode> draggedClosure,
            Func<VisualElement, Action> springLookup,
            Func<VisualElement, LayoutNode> zoneLookup)
        {
            var region = new RegionSnapshot { Kind = DropRegionKind.Canvas, Scroll = scroll };
            if (canvasRoot == null || canvasRoot.panel == null) return region;

            region.CanvasRootEl = canvasRoot;
            region.RootNode = canvasRoot.userData as ContainerNode;

            var frame = scroll.contentContainer;
            var childElsBy = new Dictionary<VisualElement, List<(VisualElement el, LayoutNode node)>>();

            Walk(canvasRoot, null, 0, null);
            Sweep(canvasRoot);
            BuildLayoutPushSets(region, childElsBy, draggedClosure);
            return region;

            void Walk(VisualElement el, ContainerNode nearestContainer, int depth, CollectionNode templateOwner)
            {
                if (el.resolvedStyle.display == DisplayStyle.None) return;
                // Tab-strip buttons carry tab userData but are nest targets via the spring sweep,
                // and header action buttons should never become cards.
                if (el is Button) return;

                var node = el.userData as LayoutNode;
                // A TABLE-mode collection never builds its item-layout container: it renders one
                // representative ROW whose cells hold that container's children side by side. The row
                // therefore stands in as the container's element — which is the whole reason a column card
                // has a parent to be reordered within, and why a dropped field lands at the column position
                // it previewed on. A marker CLASS rather than userData on purpose: userData is the node
                // identity a dozen systems key on (selection, ping, lift, click-to-deselect) and the row is
                // not a card. Only the drop map needs to see through it.
                if (node == null && templateOwner != null && el.ClassListContains(LayoutTreeBuilder.TableColumnHostClass))
                    node = templateOwner.ItemLayout as ContainerNode;
                if (node != null && draggedClosure.Contains(node)) return; // lifted: not a target

                // A held-open cell is never a CARD. It looks and behaves like the "＋" placeholder beside
                // it, so it resolves through its container's per-cell nest targets (AddContainerNest)
                // rather than as a leaf with flanking gaps — a card here would make the pointer's own
                // cell the deepest context and hide the cell target underneath it.
                if (node is EmptyCellNode) return;

                if (node is ContainerNode c)
                {
                    bool isTabs = c.Type == ContainerType.Tabs;
                    int idxInParent = (nearestContainer?.Children != null) ? nearestContainer.Children.IndexOf(c) : -1;
                    region.Cards.Add(new CardRect
                    {
                        Node = c,
                        Rect = WorldToContent(frame, el.worldBound),
                        Parent = idxInParent >= 0 ? nearestContainer : null,
                        IndexInParent = idxInParent,
                        IsContainer = !isTabs,
                        IsTabs = isTabs,
                        Depth = depth,
                        TemplateOwner = templateOwner,
                        El = el
                    });

                    if (!isTabs)
                    {
                        var childEls = new List<(VisualElement el, LayoutNode node)>();
                        CollectDirectChildElements(el, c, childEls);
                        SortByModelOrder(childEls, c);
                        childElsBy[el] = childEls;
                        var slotRects = new List<Rect>();
                        Rect hintRect = Rect.zero;
                        CollectPlaceholderRects(el, frame, slotRects, ref hintRect);
                        // The cells IN LAYOUT ORDER (children and "＋" pads interleaved). childEls is
                        // sorted by MODEL order and slotRects holds only pads, so neither can say where a
                        // pad sits RELATIVE to the children — and mid-drag that is exactly what matters:
                        // the lifted child is still in the model while a temp filler pad stands in for it.
                        var cellOrder = new List<(VisualElement el, LayoutNode node)>();
                        CollectCellOrder(el, c, cellOrder);
                        BuildContainerSlots(region, c, el, childEls, templateOwner, frame, draggedClosure, slotRects, hintRect);
                        AddContainerNest(region, c, el, childEls, templateOwner, frame, draggedClosure, slotRects, hintRect, cellOrder);
                        AddTrackedBox(region, c, el, templateOwner, frame, cellOrder);
                        AddTableColumnBand(region, c, el, templateOwner, frame, draggedClosure);
                    }

                    foreach (var ch in el.Children()) Walk(ch, c, depth + 1, templateOwner);
                    return;
                }

                if (node is CollectionNode col)
                {
                    // Only List/Table collections have an EDITABLE item-layout template: they accept field drops
                    // (to add columns) and their rendered rows are template instances the map should walk into.
                    // Default / CustomWidget / TagChips have no such template — they render a self-contained
                    // widget, so treat them as LEAVES (no drop-into, no recursion). Otherwise the walk descends
                    // into the widget's own internals (e.g. the Tag Chips "add" row) and drops slots inside it,
                    // making the reorder indicator cut through the widget instead of sitting at its edge.
                    bool hasItemLayout = col.DisplayMode == CollectionDisplayMode.List
                                      || col.DisplayMode == CollectionDisplayMode.Table;
                    int idxInParent = (nearestContainer?.Children != null) ? nearestContainer.Children.IndexOf(col) : -1;
                    region.Cards.Add(new CardRect
                    {
                        Node = col,
                        Rect = WorldToContent(frame, el.worldBound),
                        Parent = idxInParent >= 0 ? nearestContainer : null,
                        IndexInParent = idxInParent,
                        IsContainer = false,
                        Depth = depth,
                        TemplateOwner = templateOwner,
                        El = el
                    });
                    if (isAddPayload && hasItemLayout)
                    {
                        // Dropping a field on a collection creates/extends its item-layout template.
                        region.Nests.Add(new NestTarget
                        {
                            Node = col,
                            Body = WorldToContent(frame, el.worldBound),
                            AppendIndex = -1,
                            El = el,
                            TemplateOwner = templateOwner,
                            InteriorLine = Rect.zero
                        });
                    }
                    // Template instances inside get cards/slots scoped to this collection (List/Table only).
                    if (hasItemLayout)
                        foreach (var ch in el.Children()) Walk(ch, nearestContainer, depth + 1, col);
                    return;
                }

                if (node != null)
                {
                    // Leaf card (binding/button/helpbox/header) — record and stop (fields live inside).
                    int idxInParent = (nearestContainer?.Children != null) ? nearestContainer.Children.IndexOf(node) : -1;
                    region.Cards.Add(new CardRect
                    {
                        Node = node,
                        Rect = WorldToContent(frame, el.worldBound),
                        Parent = idxInParent >= 0 ? nearestContainer : null,
                        IndexInParent = idxInParent,
                        IsContainer = false,
                        Depth = depth,
                        TemplateOwner = templateOwner,
                        El = el
                    });
                    return;
                }

                foreach (var ch in el.Children()) Walk(ch, nearestContainer, depth, templateOwner);
            }

            void Sweep(VisualElement el)
            {
                if (el.resolvedStyle.display == DisplayStyle.None) return;

                var act = springLookup?.Invoke(el);
                if (act != null)
                {
                    var r = WorldToContent(frame, el.worldBound);
                    region.Springs.Add(new SpringRect { Activate = act, Rect = r, El = el });
                    // A spring-registered tab button doubles as "drop ON the tab = append into it".
                    if (el.userData is ContainerNode tab && tab.Type != ContainerType.Tabs && !draggedClosure.Contains(tab))
                    {
                        region.Nests.Add(new NestTarget
                        {
                            Node = tab,
                            Body = r,
                            AppendIndex = tab.Children?.Count ?? 0,
                            El = el,
                            IsTabButton = true,
                            InteriorLine = Rect.zero
                        });
                    }
                }

                var zt = zoneLookup?.Invoke(el);
                if (zt != null)
                    region.Zones.Add(new ZoneRect { Target = zt, Rect = WorldToContent(frame, el.worldBound), El = el });

                foreach (var ch in el.Children()) Sweep(ch);
            }
        }

        // Depth-first query that stops at any element whose userData is a model child of `c`.
        // Immune to every wrapper the builder creates because it keys purely on userData.
        private static void CollectDirectChildElements(VisualElement root, ContainerNode c,
            List<(VisualElement el, LayoutNode node)> result)
        {
            foreach (var ch in root.Children())
            {
                if (ch.resolvedStyle.display == DisplayStyle.None) continue;
                if (ch.userData is LayoutNode ln && c.Children != null && c.Children.Contains(ln))
                {
                    result.Add((ch, ln));
                    continue;
                }
                if (ch is Button) continue;
                CollectDirectChildElements(ch, c, result);
            }
        }

        // Visible "+" placeholder cells (`sasq-drop-slot`) and the "Empty Container" hint
        // (`sasq-empty-hint`) are visual-only chrome with no userData, so the userData-keyed walks
        // never see them. This bounded walk collects their content-local rects for THIS container
        // only — it never descends into any node-owning subtree, so a nested container's
        // placeholders stay its own. Visual order = fill order (the first "+" cell is where an
        // appended child genuinely lands).
        private static void CollectPlaceholderRects(VisualElement root, VisualElement frame,
            List<Rect> slotRects, ref Rect hintRect)
        {
            foreach (var ch in root.Children())
            {
                if (ch.resolvedStyle.display == DisplayStyle.None) continue;
                if (ch.userData is LayoutNode) continue;
                if (ch.ClassListContains("sasq-drop-slot"))
                {
                    slotRects.Add(WorldToContent(frame, ch.worldBound));
                    continue;
                }
                if (ch.ClassListContains("sasq-empty-hint"))
                {
                    hintRect = WorldToContent(frame, ch.worldBound);
                    continue;
                }
                if (ch is Button) continue;
                CollectPlaceholderRects(ch, frame, slotRects, ref hintRect);
            }
        }

        // One bounded DFS returning this container's cells IN LAYOUT ORDER: each entry is either a direct
        // model child (node != null) or a visual-only "＋" pad (node == null). The interleaving is the
        // point — CollectDirectChildElements re-sorts to model order and CollectPlaceholderRects drops the
        // children entirely, so only this walk can say which children a given pad sits between.
        private static void CollectCellOrder(VisualElement root, ContainerNode c,
            List<(VisualElement el, LayoutNode node)> result)
        {
            foreach (var ch in root.Children())
            {
                if (ch.resolvedStyle.display == DisplayStyle.None) continue;
                if (ch.userData is LayoutNode ln)
                {
                    if (c.Children != null && c.Children.Contains(ln)) result.Add((ch, ln));
                    continue; // never descend into a node-owning subtree — its cells are its own
                }
                if (ch.ClassListContains("sasq-drop-slot")) { result.Add((ch, null)); continue; }
                if (ch is Button) continue;
                CollectCellOrder(ch, c, result);
            }
        }

        private static void SortByModelOrder(List<(VisualElement el, LayoutNode node)> childEls, ContainerNode c)
        {
            if (c.Children == null || childEls.Count < 2) return;
            childEls.Sort((a, b) => c.Children.IndexOf(a.node).CompareTo(c.Children.IndexOf(b.node)));
        }

        private static int ModelIndexOf(ContainerNode c, LayoutNode node)
        {
            int i = c.Children?.IndexOf(node) ?? -1;
            return i;
        }

        // Which way this container's children FLOW in the element it was measured on. Normally the node's own
        // type answers it; a table's column host is the exception — the container model says nothing about
        // the row it is being previewed as, so the row's marker class is the authority there.
        private static bool IsHorizontalFlow(ContainerNode c, VisualElement cEl)
            => c.Type == ContainerType.Columns
            || c.Type == ContainerType.Grid
            || (cEl != null && cEl.ClassListContains(LayoutTreeBuilder.TableColumnHostClass));

        private static void BuildContainerSlots(RegionSnapshot region, ContainerNode c, VisualElement cEl,
            List<(VisualElement el, LayoutNode node)> childEls, CollectionNode templateOwner,
            VisualElement frame, HashSet<LayoutNode> dragged, List<Rect> slotRects, Rect hintRect)
        {
            Rect cRect = WorldToContent(frame, cEl.worldBound);
            bool horizontal = IsHorizontalFlow(c, cEl);
            var (expandHost, expandHostH) = ResolveExpandHost(cEl, childEls, frame);
            float parentRadius = ShapedOutlineRadius(c, cRect, cEl);

            if (childEls.Count == 0)
            {
                // Empty container: one interior slot. The nest target carries the visuals; this slot
                // just makes the geometry complete. Anchor it to the first "+" cell / the hint when
                // present — a stretched empty container (split-frame pane) puts yMax a whole frame
                // below where the content actually reads.
                Rect gap;
                float cellW = 0f;
                // A fixed-ROW grid's reserved "+" cells STACK (one per row track), so its empty-
                // container slot is a vertical target even though Grid normally flows horizontally.
                bool slotsStack = c.Type == ContainerType.Grid
                                  && c.GridConstraint == GridConstraint.FixedRowCount;
                if (slotRects.Count > 0)
                {
                    var cell = slotRects[0];
                    gap = horizontal && !slotsStack
                        ? new Rect(cell.xMin, cell.yMin, 0f, cell.height)
                        : new Rect(cell.xMin, cell.yMin, cell.width, 0f);
                    cellW = cell.width;
                }
                else if (hintRect.width > 0f)
                {
                    gap = new Rect(hintRect.xMin, hintRect.yMax + 3f, hintRect.width, 0f);
                    cellW = hintRect.width;
                }
                else
                {
                    gap = new Rect(cRect.xMin + 6f, cRect.yMax - 9f, Mathf.Max(0f, cRect.width - 12f), 0f);
                }
                var slot = new DropSlot
                {
                    Parent = c,
                    Index = c.Children?.Count ?? 0,
                    GapLine = gap,
                    Horizontal = horizontal && slotRects.Count > 0 && !slotsStack,
                    RowGroup = 0,
                    ParentEl = cEl,
                    ParentRect = cRect,
                    TemplateOwner = templateOwner,
                    ExpandHost = expandHost,
                    ExpandHostHeight = expandHostH,
                    CellW = cellW,
                    ParentRadius = parentRadius
                };
                region.Slots.Add(slot);
                return;
            }

            var rects = new List<Rect>(childEls.Count);
            foreach (var (el, _) in childEls) rects.Add(WorldToContent(frame, el.worldBound));

            if (!horizontal)
            {
                // Rows track-cap overflow: the children sit in SIDE-BY-SIDE column stacks
                // (column-major fill), so the vertical slot math must run per column — a midpoint
                // between the bottom of one stack and the top of the next is meaningless. RowGroup
                // carries the column index so gap hysteresis never bridges columns. Gated on the
                // cap so a plain Rows with per-child alignment quirks can never false-positive.
                var colGroups = c.Type == ContainerType.Rows && c.GridColumns > 0
                    ? GroupWrapCols(rects)
                    : null;
                if (colGroups != null && colGroups.Count > 1)
                {
                    BuildRowsColumnSlots(region, c, cEl, childEls, templateOwner, dragged, slotRects,
                        rects, colGroups, cRect, expandHost, expandHostH, parentRadius);
                    return;
                }

                var (childSpacing, hasSpacing) = MeasureChildSpacingV(rects);
                float x0 = float.MaxValue, x1 = float.MinValue;
                foreach (var r in rects) { x0 = Mathf.Min(x0, r.xMin); x1 = Mathf.Max(x1, r.xMax); }

                // Empty "+" cells (tracked Rows) already reserve room below the children — an insert
                // consumes one, so the preview's expansion should only cover the shortfall.
                float spareV = 0f;
                foreach (var sr in slotRects) spareV += sr.height + (hasSpacing ? childSpacing : 4f);

                for (int i = 0; i <= childEls.Count; i++)
                {
                    // Tracked Rows: the region BELOW the last child belongs to the nest — cell-fill
                    // onto the reserved "+" tracks (under the cap) or the new-column prediction (at
                    // it). A trailing gap slot there opened a phantom "extra row" band for a drop
                    // that actually lands in a track / next column (the image-7 noise).
                    if (i == childEls.Count && c.Type == ContainerType.Rows && c.GridColumns > 0)
                        continue;

                    Rect? a = i > 0 ? rects[i - 1] : (Rect?)null;
                    Rect? b = i < childEls.Count ? rects[i] : (Rect?)null;
                    float y = (a.HasValue && b.HasValue) ? (a.Value.yMax + b.Value.yMin) * 0.5f
                            : b.HasValue ? b.Value.yMin - 3f
                            : a.Value.yMax + 3f;

                    int modelIndex = i < childEls.Count
                        ? ModelIndexOf(c, childEls[i].node)
                        : ModelIndexOf(c, childEls[childEls.Count - 1].node) + 1;
                    if (modelIndex < 0) modelIndex = c.Children?.Count ?? 0;

                    var slot = new DropSlot
                    {
                        Parent = c,
                        Index = modelIndex,
                        GapLine = new Rect(x0, y, x1 - x0, 0f),
                        Horizontal = false,
                        RowGroup = 0,
                        ParentEl = cEl,
                        ParentRect = cRect,
                        AboveEl = i > 0 ? childEls[i - 1].el : null,
                        TemplateOwner = templateOwner,
                        ExpandHost = expandHost,
                        ExpandHostHeight = expandHostH,
                        ChildSpacingV = childSpacing,
                        HasChildSpacing = hasSpacing,
                        CellW = x1 - x0,
                        ParentRadius = parentRadius,
                        SpareCellsV = spareV
                    };
                    for (int j = i; j < childEls.Count; j++)
                        if (!dragged.Contains(childEls[j].node)) slot.ShiftSet.Add(childEls[j].el);
                    region.Slots.Add(slot);
                }
            }
            else
            {
                var rows = GroupWrapRows(rects);
                for (int r = 0; r < rows.Count; r++)
                {
                    var cells = rows[r];
                    float rowTop = float.MaxValue, rowBottom = float.MinValue;
                    foreach (int ci in cells)
                    {
                        rowTop = Mathf.Min(rowTop, rects[ci].yMin);
                        rowBottom = Mathf.Max(rowBottom, rects[ci].yMax);
                    }

                    for (int k = 0; k <= cells.Count; k++)
                    {
                        Rect? a = k > 0 ? rects[cells[k - 1]] : (Rect?)null;
                        Rect? b = k < cells.Count ? rects[cells[k]] : (Rect?)null;
                        float x = (a.HasValue && b.HasValue) ? (a.Value.xMax + b.Value.xMin) * 0.5f
                                : b.HasValue ? b.Value.xMin - 3f
                                : a.Value.xMax + 3f;

                        int modelIndex = k < cells.Count
                            ? ModelIndexOf(c, childEls[cells[k]].node)
                            : ModelIndexOf(c, childEls[cells[cells.Count - 1]].node) + 1;
                        if (modelIndex < 0) modelIndex = c.Children?.Count ?? 0;

                        var slot = new DropSlot
                        {
                            Parent = c,
                            Index = modelIndex,
                            GapLine = new Rect(x, rowTop, 0f, rowBottom - rowTop),
                            Horizontal = true,
                            RowGroup = r,
                            ParentEl = cEl,
                            ParentRect = cRect,
                            AboveEl = k > 0 ? childEls[cells[k - 1]].el : null,
                            TemplateOwner = templateOwner,
                            ExpandHost = expandHost,
                            ExpandHostHeight = expandHostH,
                            // The ghost lands as a cell — shrink it to the neighbor it'll size like.
                            CellW = b.HasValue ? b.Value.width : a.Value.width,
                            ParentRadius = parentRadius
                        };
                        // Only the cells after the gap, within the SAME wrap-row, slide right.
                        for (int j = k; j < cells.Count; j++)
                            if (!dragged.Contains(childEls[cells[j]].node)) slot.ShiftSet.Add(childEls[cells[j]].el);
                        region.Slots.Add(slot);
                    }
                }

                // A FULL last wrap-row gets one "start a new row" slot beneath it, with vertical-stack
                // semantics (horizontal line, container genuinely grows downward). Without it a drop
                // below the row can only resolve to row-interior gaps, which visually reorders the
                // row ("third grid item lands mid-row"). Under-filled rows don't need it — their "+"
                // cells / trailing gap already mean append.
                // A table's column host has no track capacity of its own (the row is a preview, not the
                // container's real layout), so it never grows a second row — whatever GridColumns the item
                // container happens to carry from List mode would otherwise invent a phantom row below the
                // table for a drop that can only ever land in the single row of columns.
                int trackCount = cEl.ClassListContains(LayoutTreeBuilder.TableColumnHostClass)
                    ? 0
                    : ResolveTrackCount(c, rows);
                var lastRow = rows[rows.Count - 1];
                // A fixed-ROW grid never gains a row: its column count is DERIVED, so an append
                // REFLOWS the whole grid into more columns. The trailing region belongs to the
                // nest's reflow prediction — a phantom "new row" slot here previews a layout the
                // drop can never produce ("third item onto a full 2-row grid looks like a 3rd row").
                bool fixedRowTracks = c.Type == ContainerType.Grid
                                      && c.GridConstraint == GridConstraint.FixedRowCount;
                if (!fixedRowTracks && trackCount > 0 && lastRow.Count >= trackCount)
                {
                    float rowX0 = float.MaxValue, rowX1 = float.MinValue, rowBottom = float.MinValue;
                    foreach (int ci in lastRow)
                    {
                        rowX0 = Mathf.Min(rowX0, rects[ci].xMin);
                        rowX1 = Mathf.Max(rowX1, rects[ci].xMax);
                        rowBottom = Mathf.Max(rowBottom, rects[ci].yMax);
                    }
                    var lastChild = childEls[lastRow[lastRow.Count - 1]];
                    int li = ModelIndexOf(c, lastChild.node);
                    var (rowSpacing, hasRowSpacing) = MeasureWrapRowSpacing(rows, rects);
                    // The default expand host (last child's PARENT) is the last .sasq-grid-row itself
                    // (grid cells live in row wrappers) — springing ITS min-height stretches the
                    // existing row's cells (alignItems:Stretch) over the phantom cells ("buttons get
                    // fat"). A wrap-Columns host has the same failure via align-content stretch. Grow
                    // the container's visible box instead: rows keep their natural height and the
                    // space opens BELOW them, exactly where the phantom row draws.
                    var newRowHost = FindVisibleGroupContent(cEl) ?? cEl;
                    float newRowHostH = WorldToContent(frame, newRowHost.worldBound).height;
                    // The phantom next row: one "+" cell per column, x-spans mirroring the last real
                    // row's cells so the preview is column-aligned. Heights resolve later (they fill
                    // the gap the container opens).
                    var phantom = new List<Rect>(lastRow.Count);
                    foreach (int ci in lastRow)
                        phantom.Add(new Rect(rects[ci].xMin, rowBottom + 3f, rects[ci].width, 0f));
                    var newRow = new DropSlot
                    {
                        Parent = c,
                        Index = li >= 0 ? li + 1 : (c.Children?.Count ?? 0),
                        GapLine = new Rect(rowX0, rowBottom + 3f, rowX1 - rowX0, 0f),
                        Horizontal = false,
                        RowGroup = rows.Count,
                        ParentEl = cEl,
                        ParentRect = cRect,
                        AboveEl = lastChild.el,
                        TemplateOwner = templateOwner,
                        ExpandHost = newRowHost,
                        ExpandHostHeight = newRowHostH,
                        ChildSpacingV = rowSpacing,
                        HasChildSpacing = hasRowSpacing,
                        CellW = (rowX1 - rowX0) / trackCount,
                        PhantomCells = phantom,
                        ParentRadius = parentRadius
                    };
                    region.Slots.Add(newRow);
                }
            }
        }

        // Vertical slots for a track-capped Rows container that has wrapped into side-by-side
        // column stacks: the single-column math per column, with RowGroup = column index. "+"
        // pads always close the LAST column, so a column's reserved room (SpareCellsV) is simply
        // the slack between its own stack's bottom and the deepest content in the band — an
        // insert there re-flows children into that slack instead of growing the container.
        private static void BuildRowsColumnSlots(RegionSnapshot region, ContainerNode c, VisualElement cEl,
            List<(VisualElement el, LayoutNode node)> childEls, CollectionNode templateOwner,
            HashSet<LayoutNode> dragged, List<Rect> slotRects, List<Rect> rects, List<List<int>> colGroups,
            Rect cRect, VisualElement expandHost, float expandHostH, float parentRadius)
        {
            float bandBottom = float.MinValue;
            foreach (var r in rects) bandBottom = Mathf.Max(bandBottom, r.yMax);
            foreach (var sr in slotRects) bandBottom = Mathf.Max(bandBottom, sr.yMax);

            for (int g = 0; g < colGroups.Count; g++)
            {
                var members = colGroups[g];
                var colRects = new List<Rect>(members.Count);
                foreach (int ci in members) colRects.Add(rects[ci]);
                var (childSpacing, hasSpacing) = MeasureChildSpacingV(colRects);

                float x0 = float.MaxValue, x1 = float.MinValue, colBottom = float.MinValue;
                foreach (var r in colRects)
                {
                    x0 = Mathf.Min(x0, r.xMin);
                    x1 = Mathf.Max(x1, r.xMax);
                    colBottom = Mathf.Max(colBottom, r.yMax);
                }
                float spareV = Mathf.Max(0f, bandBottom - colBottom);

                for (int k = 0; k <= members.Count; k++)
                {
                    // The LAST column's trailing region belongs to the nest (cell-fill onto its
                    // "+" pads, or the new-column prediction when full) — same rule as the
                    // single-column tracked Rows. Earlier columns keep their trailing slot: it's
                    // the genuine "between this column's last and the next column's first" seam.
                    if (g == colGroups.Count - 1 && k == members.Count) continue;

                    Rect? a = k > 0 ? colRects[k - 1] : (Rect?)null;
                    Rect? b = k < members.Count ? colRects[k] : (Rect?)null;
                    float y = (a.HasValue && b.HasValue) ? (a.Value.yMax + b.Value.yMin) * 0.5f
                            : b.HasValue ? b.Value.yMin - 3f
                            : a.Value.yMax + 3f;

                    int modelIndex = k < members.Count
                        ? ModelIndexOf(c, childEls[members[k]].node)
                        : ModelIndexOf(c, childEls[members[members.Count - 1]].node) + 1;
                    if (modelIndex < 0) modelIndex = c.Children?.Count ?? 0;

                    var slot = new DropSlot
                    {
                        Parent = c,
                        Index = modelIndex,
                        GapLine = new Rect(x0, y, x1 - x0, 0f),
                        Horizontal = false,
                        RowGroup = g,
                        ParentEl = cEl,
                        ParentRect = cRect,
                        AboveEl = k > 0 ? childEls[members[k - 1]].el : null,
                        TemplateOwner = templateOwner,
                        ExpandHost = expandHost,
                        ExpandHostHeight = expandHostH,
                        ChildSpacingV = childSpacing,
                        HasChildSpacing = hasSpacing,
                        CellW = x1 - x0,
                        ParentRadius = parentRadius,
                        SpareCellsV = spareV
                    };
                    // Only the SAME column's later members slide down for the gap.
                    for (int j = k; j < members.Count; j++)
                        if (!dragged.Contains(childEls[members[j]].node)) slot.ShiftSet.Add(childEls[members[j]].el);
                    region.Slots.Add(slot);
                }
            }
        }

        // The container's effective column count — what makes a wrap-row "full". Explicit tracks when
        // the model has them; a Flexible grid falls back to its first (widest) measured row; plain
        // Columns without a track count has no defined row capacity (0 = never full).
        private static int ResolveTrackCount(ContainerNode c, List<List<int>> rows)
        {
            if (c.Type == ContainerType.Grid)
            {
                if (c.GridConstraint == GridConstraint.FixedColumnCount) return Mathf.Max(1, c.GridColumns);
                if (c.GridConstraint == GridConstraint.FixedRowCount)
                {
                    int total = 0;
                    foreach (var r in rows) total += r.Count;
                    return Mathf.Max(1, Mathf.CeilToInt(total / (float)Mathf.Max(1, c.GridColumns)));
                }
                return rows.Count > 0 ? rows[0].Count : 0;
            }
            return c.GridColumns > 0 ? c.GridColumns : 0;
        }

        // Median vertical spacing between adjacent wrap-row bands (for the new-row slot's gap sizing).
        private static (float spacing, bool has) MeasureWrapRowSpacing(List<List<int>> rows, List<Rect> rects)
        {
            if (rows.Count < 2) return (0f, false);
            var bands = new List<Rect>(rows.Count);
            foreach (var row in rows)
            {
                float top = float.MaxValue, bottom = float.MinValue;
                foreach (int ci in row)
                {
                    top = Mathf.Min(top, rects[ci].yMin);
                    bottom = Mathf.Max(bottom, rects[ci].yMax);
                }
                bands.Add(Rect.MinMaxRect(0f, top, 1f, bottom));
            }
            return MeasureChildSpacingV(bands);
        }

        // Consecutive cells belong to the same wrap-row while they overlap vertically by more than
        // half the smaller height. Reconstructs rows from geometry, so it works for Columns wrap and
        // for Grid whether or not the builder regrouped the DOM.
        private static List<List<int>> GroupWrapRows(List<Rect> rects)
        {
            var rows = new List<List<int>>();
            float bandMin = 0f, bandMax = 0f;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (rows.Count > 0)
                {
                    float overlap = Mathf.Min(bandMax, r.yMax) - Mathf.Max(bandMin, r.yMin);
                    float minH = Mathf.Max(1f, Mathf.Min(bandMax - bandMin, r.height));
                    if (overlap > 0.5f * minH)
                    {
                        rows[rows.Count - 1].Add(i);
                        bandMin = Mathf.Min(bandMin, r.yMin);
                        bandMax = Mathf.Max(bandMax, r.yMax);
                        continue;
                    }
                }
                rows.Add(new List<int> { i });
                bandMin = r.yMin;
                bandMax = r.yMax;
            }
            return rows;
        }

        // Transpose of GroupWrapRows: consecutive cells belong to the same COLUMN stack while they
        // overlap horizontally by more than half the smaller width. A track-capped Rows container
        // fills column-major, so a model-order walk stays inside one column until it wraps right;
        // a normal single-column stack always collapses to one group (children share the x band).
        private static List<List<int>> GroupWrapCols(List<Rect> rects)
        {
            var cols = new List<List<int>>();
            float bandMin = 0f, bandMax = 0f;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (cols.Count > 0)
                {
                    float overlap = Mathf.Min(bandMax, r.xMax) - Mathf.Max(bandMin, r.xMin);
                    float minW = Mathf.Max(1f, Mathf.Min(bandMax - bandMin, r.width));
                    if (overlap > 0.5f * minW)
                    {
                        cols[cols.Count - 1].Add(i);
                        bandMin = Mathf.Min(bandMin, r.xMin);
                        bandMax = Mathf.Max(bandMax, r.xMax);
                        continue;
                    }
                }
                cols.Add(new List<int> { i });
                bandMin = r.xMin;
                bandMax = r.xMax;
            }
            return cols;
        }

        private static void AddContainerNest(RegionSnapshot region, ContainerNode c, VisualElement cEl,
            List<(VisualElement el, LayoutNode node)> childEls, CollectionNode templateOwner,
            VisualElement frame, HashSet<LayoutNode> dragged, List<Rect> slotRects, Rect hintRect,
            List<(VisualElement el, LayoutNode node)> cellOrder)
        {
            Rect body = WorldToContent(frame, cEl.worldBound);
            bool horizontal = IsHorizontalFlow(c, cEl);
            var (expandHost, expandHostH) = ResolveExpandHost(cEl, childEls, frame);

            int visible = 0;
            foreach (var (_, node) in childEls) if (!dragged.Contains(node)) visible++;

            // Track band for the Rows cap prediction: x span + top of the children and "+" pads
            // (falls back to the hint / body inset for an empty container).
            float bandX0 = float.MaxValue, bandX1 = float.MinValue, bandTop = float.MaxValue;
            void GrowBand(Rect r)
            {
                bandX0 = Mathf.Min(bandX0, r.xMin);
                bandX1 = Mathf.Max(bandX1, r.xMax);
                bandTop = Mathf.Min(bandTop, r.yMin);
            }
            foreach (var (bel, _) in childEls) GrowBand(WorldToContent(frame, bel.worldBound));
            foreach (var sr in slotRects) GrowBand(sr);
            if (bandX0 > bandX1)
            {
                if (hintRect.width > 0f) GrowBand(hintRect);
                else GrowBand(new Rect(body.xMin + 6f, body.yMax - 9f, Mathf.Max(0f, body.width - 12f), 0f));
            }

            float childSpacing = 0f;
            bool hasSpacing = false;

            Rect interior;
            int appendIndex;
            if (childEls.Count == 0)
            {
                // Anchor the append line to the hint when present — a stretched empty container
                // (split-frame pane) puts yMax a whole frame below where the content reads.
                interior = hintRect.width > 0f
                    ? new Rect(hintRect.xMin, hintRect.yMax + 3f, hintRect.width, 0f)
                    : new Rect(body.xMin + 6f, body.yMax - 9f, Mathf.Max(0f, body.width - 12f), 0f);
                appendIndex = c.Children?.Count ?? 0;
            }
            else
            {
                var last = WorldToContent(frame, childEls[childEls.Count - 1].el.worldBound);
                if (horizontal)
                {
                    interior = new Rect(last.xMax + 3f, last.yMin, 0f, last.height);
                }
                else
                {
                    float x0 = float.MaxValue, x1 = float.MinValue;
                    var rects = new List<Rect>(childEls.Count);
                    foreach (var (el, _) in childEls)
                    {
                        var r = WorldToContent(frame, el.worldBound);
                        rects.Add(r);
                        x0 = Mathf.Min(x0, r.xMin); x1 = Mathf.Max(x1, r.xMax);
                    }
                    (childSpacing, hasSpacing) = MeasureChildSpacingV(rects);
                    interior = new Rect(x0, last.yMax + 3f, x1 - x0, 0f);
                }
                int li = ModelIndexOf(c, childEls[childEls.Count - 1].node);
                appendIndex = li >= 0 ? li + 1 : (c.Children?.Count ?? 0);
            }

            // Visible "+" cells turn the nest into a CELL FILL: outline the landing cell, no vertical
            // expansion (the cell already reserves the space), and no redundant interior line when the
            // container is empty (the glowing cell is the whole message). Under-filled containers keep the
            // interior line at the append edge. ContainerBody keeps the full body for the thin context
            // outline. `body` is seeded with the FIRST cell so the fallback single-nest path below (a
            // container with no cells at all) is unchanged.
            Rect containerBody = body;
            var cells = CollectCellTargets(c, cellOrder, frame);
            if (cells.Count > 0)
            {
                body = cells[0].rect;
                expandHost = null;
                expandHostH = 0f;
                if (childEls.Count == 0) interior = Rect.zero;
            }

            // Safe grow-below host for overflow landing rows (more held cards than visible "+"
            // cells): the container's VISIBLE box — the new-row-slot precedent; never a grid row,
            // whose min-height spring would stretch its existing cells.
            var growHost = FindVisibleGroupContent(cEl) ?? cEl;
            float growHostH = WorldToContent(frame, growHost.worldBound).height;
            float outlineRadius = ShapedOutlineRadius(c, containerBody, cEl);

            NestTarget MakeNest(Rect nestBody, int index, bool isCell, List<Rect> cellRects) => new NestTarget
            {
                Node = c,
                Body = nestBody,
                AppendIndex = index,
                El = cEl,
                Empty = visible == 0,
                Horizontal = horizontal,
                ContainerBody = containerBody,
                TemplateOwner = templateOwner,
                InteriorLine = interior,
                ExpandHost = expandHost,
                ExpandHostHeight = expandHostH,
                ChildSpacingV = childSpacing,
                HasChildSpacing = hasSpacing,
                IsCell = isCell,
                CellRects = cellRects,
                PadCount = slotRects.Count,
                HintRect = hintRect,
                GrowHost = growHost,
                GrowHostHeight = growHostH,
                OutlineRadius = outlineRadius,
                VisibleChildCount = visible,
                ContentTopY = bandTop,
                ContentBandX0 = bandX0,
                ContentBandX1 = bandX1
            };

            if (cells.Count == 0)
            {
                region.Nests.Add(MakeNest(body, appendIndex, false, null));
                return;
            }

            // ONE NEST PER VISIBLE CELL. Children are a flat list, so a cell's model index IS where a drop
            // into it belongs; the commit pads the skipped positions with EmptyCellNodes. Without this the
            // container offered a single target at the first free cell, which is why a three-track Columns
            // could only ever be filled left-to-right. A multi-card drag fills from the targeted cell
            // rightward, so each nest carries the cells from its own position on.
            for (int i = 0; i < cells.Count; i++)
            {
                var cellRects = new List<Rect>(cells.Count - i);
                for (int j = i; j < cells.Count; j++) cellRects.Add(cells[j].rect);
                region.Nests.Add(MakeNest(cells[i].rect, cells[i].index, true, cellRects));
            }
        }

        // Every cell of a tracked container that a drop can land in, in layout order: the EmptyCellNode
        // children holding cells open, plus the visual-only "＋" pads.
        //
        // Reduces a tracked container to its declared tracks, in order, with each track's occupant. Purely
        // additive: nothing reads this unless the cell-based drop model is enabled.
        private static void AddTrackedBox(RegionSnapshot region, ContainerNode c, VisualElement cEl,
            CollectionNode templateOwner, VisualElement frame,
            List<(VisualElement el, LayoutNode node)> cellOrder)
        {
            if (!EmptyCellFill.IsTrackedCellContainer(c) || cellOrder.Count == 0) return;
            // A table's column host is never a tracked BOX: its "tracks" are columns of a preview row, it
            // renders no "＋" pads to fill, and the cell resolver's escape band would swallow the gaps
            // between columns — the one thing this row exists to offer.
            if (cEl.ClassListContains(LayoutTreeBuilder.TableColumnHostClass)) return;

            Rect body = WorldToContent(frame, cEl.worldBound);
            var box = new TrackedBox
            {
                Node = c,
                El = cEl,
                Body = body,
                TemplateOwner = templateOwner,
                OutlineRadius = ShapedOutlineRadius(c, body, cEl),
                // Which way the DECLARED tracks run — not merely "is this a grid". A fixed-ROW-count Grid
                // pins its rows and derives its columns, so its tracks stack VERTICALLY and it gains a
                // column exactly like a capped Rows; treating it as horizontal laid its overflow out along
                // the wrong axis and put the escape band across its first and last track.
                Horizontal = c.Type == ContainerType.Columns
                             || (c.Type == ContainerType.Grid && c.GridConstraint != GridConstraint.FixedRowCount)
            };
            foreach (var (el, node) in cellOrder)
            {
                box.Tracks.Add(WorldToContent(frame, el.worldBound));
                box.Occupants.Add(node); // null for a "＋" pad; an EmptyCellNode is likewise an OPEN track
                box.TrackEls.Add(el);
            }
            region.TrackedBoxes.Add(box);
        }

        // The column band of a Table-mode collection's representative row — both halves of every column, in
        // layout order. Only the design row carries the marker class, so this is design-mode-only by
        // construction, and it is measured for every table whether or not a drag is in flight (same
        // always-measure/inert-until-used rule as AddTrackedBox).
        //
        // Deliberately NOT built from childEls: that walk drops display:none children, and the one column
        // whose card is hidden is precisely the one in hand — the band needs it, because its CELL is still
        // holding its width open in the row and the whole prediction is a permutation of those widths.
        private static void AddTableColumnBand(RegionSnapshot region, ContainerNode c, VisualElement cEl,
            CollectionNode templateOwner, VisualElement frame, HashSet<LayoutNode> dragged)
        {
            if (!cEl.ClassListContains(LayoutTreeBuilder.TableColumnHostClass)) return;

            var band = new TableColumnBand
            {
                Node = c,
                TemplateOwner = templateOwner,
                RowEl = cEl,
                RowBand = WorldToContent(frame, cEl.worldBound),
            };

            Rect headerBand = Rect.zero;
            foreach (var cellEl in cEl.Children())
            {
                if (!cellEl.ClassListContains(LayoutTreeBuilder.TableCellClass)) continue;
                if (cellEl.childCount == 0 || !(cellEl[0].userData is LayoutNode node)) return; // synthesized column: no model to reorder
                var headerEl = TableColumnLink.Header(cellEl);
                if (headerEl == null) return;   // unpaired halves ⇒ no honest preview; leave it to the slots
                int modelIndex = c.Children != null ? c.Children.IndexOf(node) : -1;
                if (modelIndex < 0) return;

                var header = WorldToContent(frame, headerEl.worldBound);
                headerBand = headerBand == Rect.zero
                    ? header
                    : Rect.MinMaxRect(Mathf.Min(headerBand.xMin, header.xMin), Mathf.Min(headerBand.yMin, header.yMin),
                                      Mathf.Max(headerBand.xMax, header.xMax), Mathf.Max(headerBand.yMax, header.yMax));

                band.Columns.Add(new TableColumnSnap
                {
                    Node = node,
                    ModelIndex = modelIndex,
                    Cell = WorldToContent(frame, cellEl.worldBound),
                    Header = header,
                    CellEl = cellEl,
                    HeaderEl = headerEl,
                    Dragged = dragged.Contains(node),
                });
            }

            if (band.Columns.Count == 0 || headerBand == Rect.zero) return;

            // The lane between two columns (the divider the resizer lives on). Measured rather than taken
            // from TableDividerWidth so the prediction stays exact if the row's spacing ever changes.
            if (band.Columns.Count > 1)
                band.Gap = Mathf.Max(0f, band.Columns[1].Cell.xMin - band.Columns[0].Cell.xMax);

            band.HeaderBand = headerBand;
            band.Body = Rect.MinMaxRect(
                Mathf.Min(headerBand.xMin, band.RowBand.xMin), Mathf.Min(headerBand.yMin, band.RowBand.yMin),
                Mathf.Max(headerBand.xMax, band.RowBand.xMax), Mathf.Max(headerBand.yMax, band.RowBand.yMax));
            region.TableColumnBands.Add(band);
        }

        // A cell's index is its TRACK ORDINAL — how many visible cells precede it in layout order.
        //
        // It is emphatically NOT `Children.Count + i`, nor anything derived from ModelIndexOf. Mid-drag
        // the lifted card is hidden and a temp filler pad stands in for its track, so the model still
        // counts a child the layout no longer shows. Indexing off the model then overshoots by one per
        // lifted card, and the commit padded REAL EmptyCellNodes to reach the phantom index — pushing the
        // child count past the track cap and splitting the container into a second column. That is the
        // "previewed as the top row, landed as a new column" bug.
        //
        // A track ordinal is what the user is actually aiming at, and — because the lifted card is
        // removed before the insert — it is already in POST-REMOVAL space. Its consumer must therefore
        // NOT re-base it the way a model-index slot is re-based; see ResolvedDrop.CellTrack.
        private static List<(Rect rect, int index)> CollectCellTargets(ContainerNode c,
            List<(VisualElement el, LayoutNode node)> cellOrder, VisualElement frame)
        {
            var cells = new List<(Rect rect, int index)>();
            for (int track = 0; track < cellOrder.Count; track++)
            {
                var (el, node) = cellOrder[track];
                // Real children occupy a track but are not droppable cells; only open ones are.
                if (node != null && !(node is EmptyCellNode)) continue;
                cells.Add((WorldToContent(frame, el.worldBound), track));
            }
            return cells;
        }

        // A vertical gap genuinely EXPANDS its container (min-height growth), and the layout pushes
        // everything after each vertical ancestor down for real. These sets record exactly what the
        // layout will move, so the resolver can correct its frozen geometry by the known offsets.
        // Stops at horizontal ancestors (Columns/Grid — the real height change there depends on which
        // cell is tallest) and at collection frames (a gap inside a template stays in the template).
        private static void BuildLayoutPushSets(RegionSnapshot region,
            Dictionary<VisualElement, List<(VisualElement el, LayoutNode node)>> childElsBy,
            HashSet<LayoutNode> dragged)
        {
            var pushByContainer = new Dictionary<VisualElement, List<VisualElement>>();

            List<VisualElement> PushFor(VisualElement containerEl)
            {
                if (pushByContainer.TryGetValue(containerEl, out var cached)) return cached;
                var set = new List<VisualElement>();
                var curEl = containerEl;
                while (true)
                {
                    VisualElement anc = null;
                    for (var cur = curEl.parent; cur != null; cur = cur.parent)
                    {
                        if (cur.userData is CollectionNode || cur.userData is ContainerNode) { anc = cur; break; }
                    }
                    if (anc == null) break;
                    if (anc.userData is CollectionNode) break;

                    var acn = (ContainerNode)anc.userData;
                    if (acn.Type == ContainerType.Columns || acn.Type == ContainerType.Grid) break;

                    // Tabs: hidden sibling tabs can't shift; just climb through.
                    if (acn.Type != ContainerType.Tabs && childElsBy.TryGetValue(anc, out var ancChildren))
                    {
                        int idx = ancChildren.FindIndex(e => e.el == curEl || IsWithin(curEl, e.el));
                        if (idx >= 0)
                        {
                            for (int j = idx + 1; j < ancChildren.Count; j++)
                                if (!dragged.Contains(ancChildren[j].node)) set.Add(ancChildren[j].el);
                        }
                    }
                    curEl = anc;
                }
                pushByContainer[containerEl] = set;
                return set;
            }

            foreach (var slot in region.Slots)
                if (!slot.Horizontal) slot.LayoutPush = PushFor(slot.ParentEl);

            foreach (var nest in region.Nests)
                if (!nest.IsTabButton && !(nest.Node is CollectionNode)) nest.LayoutPush = PushFor(nest.El);
        }

        // ---------------------------------------------------------------- organizer (tab kanban)

        /// <summary>Measures the Tab Organizer's columns into a <see cref="RegionSnapshot"/> of flat per-column slots.</summary>
        public static RegionSnapshot BuildOrganizerRegion(
            ScrollView scroll,
            HashSet<LayoutNode> draggedClosure,
            Func<VisualElement, LayoutNode> zoneLookup)
        {
            var region = new RegionSnapshot { Kind = DropRegionKind.Organizer, Scroll = scroll };
            var frame = scroll.contentContainer;
            if (frame == null || frame.panel == null) return region;

            WalkOrg(frame);

            // Expanding a column pushes the kanban's lower wrap-rows down for real — record which
            // cells those are so the resolver can correct by the known offset.
            var cells = new List<CardRect>();
            foreach (var c in region.Cards) if (c.Depth == 1) cells.Add(c);
            foreach (var cell in cells)
            {
                var push = new List<VisualElement>();
                foreach (var other in cells)
                    if (other != cell && other.Rect.yMin >= cell.Rect.yMax - 1f) push.Add(other.El);
                foreach (var slot in region.Slots) if (slot.ParentEl == cell.El) slot.LayoutPush = push;
                foreach (var nest in region.Nests) if (nest.El == cell.El) nest.LayoutPush = push;
            }
            return region;

            void WalkOrg(VisualElement el)
            {
                if (el.resolvedStyle.display == DisplayStyle.None) return;

                var zt = zoneLookup?.Invoke(el);
                if (zt != null)
                    region.Zones.Add(new ZoneRect { Target = zt, Rect = WorldToContent(frame, el.worldBound), El = el });

                if (el.ClassListContains("organizer-tab-cell") && el.userData is ContainerNode tab)
                {
                    BuildOrganizerCell(el, tab);
                    return;
                }
                foreach (var ch in el.Children()) WalkOrg(ch);
            }

            void BuildOrganizerCell(VisualElement cell, ContainerNode tab)
            {
                Rect cellRect = WorldToContent(frame, cell.worldBound);
                region.Cards.Add(new CardRect
                {
                    Node = tab, Rect = cellRect, Parent = null, IndexInParent = -1,
                    IsContainer = true, Depth = 1, El = cell
                });

                var cardEls = new List<(VisualElement el, LayoutNode node)>();
                CollectDirectChildElements(cell, tab, cardEls);
                SortByModelOrder(cardEls, tab);

                var rects = new List<Rect>(cardEls.Count);
                foreach (var (cel, _) in cardEls) rects.Add(WorldToContent(frame, cel.worldBound));

                var (expandHost, expandHostH) = ResolveExpandHost(cell, cardEls, frame);
                var (childSpacing, hasSpacing) = MeasureChildSpacingV(rects);

                int visible = 0;
                for (int i = 0; i < cardEls.Count; i++)
                {
                    var (cel, node) = cardEls[i];
                    if (draggedClosure.Contains(node)) continue;
                    visible++;
                    region.Cards.Add(new CardRect
                    {
                        Node = node, Rect = rects[i], Parent = tab,
                        IndexInParent = ModelIndexOf(tab, node), IsContainer = false, Depth = 2, El = cel
                    });
                }

                if (cardEls.Count == 0)
                {
                    region.Slots.Add(new DropSlot
                    {
                        Parent = tab, Index = tab.Children?.Count ?? 0,
                        GapLine = new Rect(cellRect.xMin + 6f, cellRect.yMax - 12f, Mathf.Max(0f, cellRect.width - 12f), 0f),
                        ParentEl = cell, ParentRect = cellRect, AboveEl = null,
                        ExpandHost = expandHost, ExpandHostHeight = expandHostH
                    });
                }
                else
                {
                    float x0 = float.MaxValue, x1 = float.MinValue;
                    foreach (var r in rects) { x0 = Mathf.Min(x0, r.xMin); x1 = Mathf.Max(x1, r.xMax); }

                    for (int i = 0; i <= cardEls.Count; i++)
                    {
                        Rect? a = i > 0 ? rects[i - 1] : (Rect?)null;
                        Rect? b = i < cardEls.Count ? rects[i] : (Rect?)null;
                        float y = (a.HasValue && b.HasValue) ? (a.Value.yMax + b.Value.yMin) * 0.5f
                                : b.HasValue ? b.Value.yMin - 2f
                                : a.Value.yMax + 2f;

                        int modelIndex = i < cardEls.Count
                            ? ModelIndexOf(tab, cardEls[i].node)
                            : ModelIndexOf(tab, cardEls[cardEls.Count - 1].node) + 1;
                        if (modelIndex < 0) modelIndex = tab.Children?.Count ?? 0;

                        var slot = new DropSlot
                        {
                            Parent = tab, Index = modelIndex,
                            GapLine = new Rect(x0, y, x1 - x0, 0f),
                            ParentEl = cell, ParentRect = cellRect,
                            AboveEl = i > 0 ? cardEls[i - 1].el : null,
                            ExpandHost = expandHost, ExpandHostHeight = expandHostH,
                            ChildSpacingV = childSpacing, HasChildSpacing = hasSpacing
                        };
                        for (int j = i; j < cardEls.Count; j++)
                            if (!draggedClosure.Contains(cardEls[j].node)) slot.ShiftSet.Add(cardEls[j].el);
                        region.Slots.Add(slot);
                    }
                }

                Rect interior = cardEls.Count == 0
                    ? new Rect(cellRect.xMin + 6f, cellRect.yMax - 12f, Mathf.Max(0f, cellRect.width - 12f), 0f)
                    : new Rect(rects[rects.Count - 1].xMin, rects[rects.Count - 1].yMax + 2f, rects[rects.Count - 1].width, 0f);

                int lastIdx = cardEls.Count > 0 ? ModelIndexOf(tab, cardEls[cardEls.Count - 1].node) : -1;
                region.Nests.Add(new NestTarget
                {
                    Node = tab, Body = cellRect,
                    AppendIndex = lastIdx >= 0 ? lastIdx + 1 : (tab.Children?.Count ?? 0),
                    El = cell, Empty = visible == 0, InteriorLine = interior,
                    ExpandHost = expandHost, ExpandHostHeight = expandHostH,
                    ChildSpacingV = childSpacing, HasChildSpacing = hasSpacing
                });
            }
        }

        // ---------------------------------------------------------------- hierarchy

        /// <summary>Measures the hierarchy tree into a <see cref="RegionSnapshot"/> for the indent drop resolver.</summary>
        public static RegionSnapshot BuildHierarchyRegion(
            ScrollView scroll,
            List<HierRowInfo> rows,
            HashSet<LayoutNode> draggedClosure)
        {
            var region = new RegionSnapshot { Kind = DropRegionKind.Hierarchy, Scroll = scroll, HierRows = new List<HierRowSnap>() };
            if (rows == null) return region;
            var frame = scroll.contentContainer;

            float baseX = float.NaN, rightX = 0f;
            foreach (var r in rows)
            {
                if (r.Row == null || r.Row.panel == null) continue;
                if (r.Row.resolvedStyle.display == DisplayStyle.None) continue;
                if (r.Node == null) continue;

                // Dragged rows stay in the snapshot as GEOMETRY (they're still visible, dimmed, in
                // the tree) so gap detection snaps to their edges instead of treating their area as
                // one big void — but they're flagged so they can never be a nest/parent target.
                bool draggedRow = draggedClosure.Contains(r.Node);

                var rect = WorldToContent(frame, r.Row.worldBound);
                if (r.Depth == 0 && float.IsNaN(baseX)) baseX = rect.xMin;
                rightX = Mathf.Max(rightX, rect.xMax);

                region.HierRows.Add(new HierRowSnap
                {
                    Node = r.Node,
                    Depth = r.Depth,
                    Parent = r.Parent,
                    IndexInParent = r.IndexInParent,
                    Collapsed = r.Collapsed,
                    InItemTemplate = r.InItemTemplate,
                    Dragged = draggedRow,
                    Row = r.Row,
                    Rect = rect
                });
            }
            if (float.IsNaN(baseX)) baseX = region.HierRows.Count > 0 ? region.HierRows[0].Rect.xMin : 0f;
            // baseX is the ROW element's left edge, but a row's content starts one eye-gutter in (the visibility
            // eye's column, which does not indent with depth). The indent resolver maps X→depth as
            // `(probeX - HierBaseX) / HierIndentUnit`, so without this every drag would read one gutter's worth
            // deeper than the pointer looks — dropping at depth 1 while aiming at depth 0.
            region.HierBaseX = baseX + SasquatchLayoutDesignerWindow.HierEyeGutter;
            region.HierRightX = rightX;
            return region;
        }

        // ---------------------------------------------------------------- collection items

        /// <summary>Measures a collection's rows into insertion slots plus the container rect.</summary>
        public static (List<ItemSlot> slots, Rect containerRect) BuildItemSlots(
            VisualElement itemsContainer, VisualElement frame, VisualElement sourceRow)
        {
            var rows = new List<VisualElement>();
            foreach (var ch in itemsContainer.Children())
            {
                if (ch.resolvedStyle.display == DisplayStyle.None) continue;
                if (!(ch.userData is int)) continue;
                rows.Add(ch);
            }

            int srcIdx = rows.IndexOf(sourceRow);
            var slots = new List<ItemSlot>();
            Rect containerRect = WorldToContent(frame, itemsContainer.worldBound);
            if (rows.Count == 0) return (slots, containerRect);

            var rects = new List<Rect>(rows.Count);
            foreach (var r in rows) rects.Add(WorldToContent(frame, r.worldBound));

            float x0 = float.MaxValue, x1 = float.MinValue;
            foreach (var r in rects) { x0 = Mathf.Min(x0, r.xMin); x1 = Mathf.Max(x1, r.xMax); }

            for (int g = 0; g <= rows.Count; g++)
            {
                Rect? a = g > 0 ? rects[g - 1] : (Rect?)null;
                Rect? b = g < rows.Count ? rects[g] : (Rect?)null;
                float y = (a.HasValue && b.HasValue) ? (a.Value.yMax + b.Value.yMin) * 0.5f
                        : b.HasValue ? b.Value.yMin - 2f
                        : a.Value.yMax + 2f;

                var slot = new ItemSlot
                {
                    GapIndex = g,
                    ToIndex = (srcIdx >= 0 && g > srcIdx) ? g - 1 : g,
                    GapLine = new Rect(x0, y, x1 - x0, 0f),
                    AboveEl = g > 0 ? rows[g - 1] : null
                };
                for (int j = g; j < rows.Count; j++)
                    if (rows[j] != sourceRow) slot.ShiftSet.Add(rows[j]);
                slots.Add(slot);
            }
            return (slots, containerRect);
        }
    }
}
