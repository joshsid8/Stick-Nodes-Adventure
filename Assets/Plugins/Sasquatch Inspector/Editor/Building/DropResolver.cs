using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    // The fully-resolved drop for the current pointer position. Everything the engine needs to draw
    // the indicators, open the gap, and commit. Every committable state is expressed uniformly as
    // "insert into TargetParent at Slot" (the window's AsChild + slot path).
    /// <summary>
    /// The fully-resolved drop for the current pointer position — everything the engine needs to draw the
    /// indicators, open the gap, and commit. Every committable state is expressed uniformly as "insert into
    /// TargetParent at Slot".
    /// </summary>
    public sealed class ResolvedDrop
    {
        /// <summary>What kind of drop the pointer is over (gap insert, nest, zone, delete, hierarchy line/nest, item gap, or none).</summary>
        public enum DropState { None, Gap, Nest, Zone, Delete, HierLine, HierNest, ItemGap }

        public DropState State;
        public LayoutNode TargetParent;   // receiving container (CollectionNode only for Add-nest)
        public int Slot;                  // -1 = handler default (append / "+" zone wrap)
        public Rect LineWorld;            // world-space insertion line (Rect.zero = no line)
        public bool LineVertical;
        public Rect OutlineWorld;         // world-space parent outline (Rect.zero = none)
        public bool NestTint;
        public bool ThinOutline;          // hierarchy indent state: indicative-only outline draws thin
        public DropSlot ActiveSlot;       // Gap state only — drives the gap animator
        public int RowGroup;
        public VisualElement ExpandEl;    // children-host that expands for this state (null = none)
        public float ExpandTo;            // its target min-height (natural + gap)
        public List<VisualElement> ExpandPush; // elements layout-pushed below by the expansion
        public float GapMain;             // the effective vertical gap this drop opens (all held cards + spacing)
        public float[] LineOffsets;       // Y offsets of insertion lines 2..N relative to LineWorld (null = single line)
        public float GhostWidth;          // the landing cell/container's width — the ghost shrinks to it (0 = default)
        public Rect ContextOutlineWorld;  // thin secondary outline: the container/frame AROUND a cell/pane target (Rect.zero = none)
        public Rect[] PhantomCellsWorld;  // new-row preview: the "+" cells of the row that will exist if the drop lands (null = none)
        // Landing-cell preview: one full rect per HELD card, drawn in the bold "+"-cell style at the
        // exact spot that card will occupy (row/column-aware). When present, the indicator draws
        // these INSTEAD of the insertion line(s) — the full rectangle reads as "lands HERE", where
        // the old underline+dot read as "lands underneath". LineWorld/LineOffsets stay populated
        // (hierarchy + tests consume them); the indicator just prefers the rects.
        public Rect[] LandingCellsWorld;
        // Parallel to LandingCellsWorld when a big multi-drag has been collapsed to one rect per
        // column/row: how many held cards each rect stands for, so it can be labelled "＋3" instead of
        // drawing three rectangles. Null when every landing cell is its own card (the common case).
        public int[] LandingCounts;
        // Where EXISTING cards are pushed to when a drop reflows a tracked container (a 4th card into a
        // 3-track Rows starts a second column and carries a card into it). Landing cells answer "where
        // does the thing in my hand go"; these answer "what else moves because of it" — without them a
        // reflow is invisible until release. Null when nothing is displaced.
        public Rect[] DisplacedCellsWorld;
        // The real cards a reordering drop slides aside, with the per-element offset each moves by. Used
        // instead of DisplacedCellsWorld whenever the drop does NOT resize the cells: the cards moving
        // for real is a better preview than any rectangle describing the move. Parallel lists; null when
        // nothing slides. Applied as paint-time translations, so no layout is invalidated.
        public List<VisualElement> ReflowEls;
        public List<Vector2> ReflowOffsets;
        // The tracked container a drop is passing OVER to land BESIDE it (the escape band). Drawn as a
        // neutral ring so "not in this one" is a visible state rather than merely the absence of a cell
        // highlight. Rect.zero when the drop is not an escape.
        public Rect EscapedBoxWorld;
        /// <summary>Corner radius for <see cref="EscapedBoxWorld"/> (shaped containers).</summary>
        public float OutlineRadiusEscaped;
        public float OutlineRadius;       // shaped receiving container (Circle/Pill frame): outline corner radius (0 = default)
        public int ItemToIndex = -1;      // ItemGap state
        public int ItemGapIndex = -1;
        // Nest state only: Slot is a TRACK ORDINAL in a tracked container, not a model index. Tracks do
        // not renumber when a card is lifted (a temp pad holds its place), so this slot is already in
        // post-removal space and a move MUST NOT re-base it the way a model-index slot is re-based.
        public bool CellTrack;
        public int HierDepth = -1;        // HierLine state — chosen indent depth (for hysteresis)
        public float HierGapY;            // HierLine state — gap identity (for hysteresis)

        public bool IsCommittable =>
            State == DropState.Gap || State == DropState.Nest || State == DropState.Zone ||
            State == DropState.HierLine || State == DropState.HierNest;

        public static readonly ResolvedDrop None = new ResolvedDrop { State = DropState.None };
    }

    // Pure resolution math against a frozen RegionSnapshot plus the engine's KNOWN visual offsets
    // (the open gap's translations + the expansion's layout push, reported by GapAnimator). The same
    // pointer position over the same state always yields the same answer — no panel.Pick, no reads
    // of unknown layout — so indicator oscillation stays impossible by construction.
    internal static class DropResolver
    {
        /// <summary>Resolves the pointer position against a frozen <see cref="RegionSnapshot"/> into a <see cref="ResolvedDrop"/> — pure math, no live layout queries.</summary>
        public static ResolvedDrop Resolve(
            RegionSnapshot region,
            Vector2 panelPos,
            bool isAddPayload,
            CollectionNode draggedTemplateOwner,
            ResolvedDrop current,
            Func<VisualElement, Vector2> offsetOf,
            in DragGapProfile profile,
            float gapMainH,
            float hierDepthProbeX)
        {
            var frame = region.Frame;
            Vector2 p = frame.WorldToLocal(panelPos);

            Vector2 Off(VisualElement el) => el != null ? offsetOf(el) : Vector2.zero;

            // "+" zones (create a new tab) win outright — they are explicit affordances.
            foreach (var z in region.Zones)
            {
                var zr = z.Rect;
                zr.position += Off(z.El);
                if (!zr.Contains(p)) continue;
                return new ResolvedDrop
                {
                    State = ResolvedDrop.DropState.Zone,
                    TargetParent = z.Target,
                    Slot = -1,
                    OutlineWorld = DropSnapshotBuilder.ContentToWorld(frame, zr),
                    NestTint = true
                };
            }

            if (region.Kind == DropRegionKind.Hierarchy)
            {
                // Depth (child vs sibling) is chosen by the GHOST CARD's left edge, not the cursor —
                // with a card in hand, its left border is what visually lines up with the indent.
                float probeLocalX = frame.WorldToLocal(new Vector2(hierDepthProbeX, panelPos.y)).x;
                return ResolveHierarchy(region, p, probeLocalX, isAddPayload, draggedTemplateOwner != null, current);
            }

            return ResolveSlotted(region, p, isAddPayload, draggedTemplateOwner, current, Off, profile, gapMainH);
        }

        // ------------------------------------------------------------ canvas + organizer

        private static ResolvedDrop ResolveSlotted(
            RegionSnapshot region, Vector2 p, bool isAdd,
            CollectionNode draggedTemplateOwner, ResolvedDrop current,
            Func<VisualElement, Vector2> Off, DragGapProfile profile, float gapMainH)
        {
            bool SlotAllowed(DropSlot s) => isAdd || s.TemplateOwner == draggedTemplateOwner;
            bool NestAllowed(NestTarget n) => isAdd || n.TemplateOwner == draggedTemplateOwner;

            // Per-target effective gap: every held card's TRUE height plus the spacing the target
            // container puts between children. Single-card drags degenerate to the old gapMainV.
            float EffGapSlot(DropSlot s) => profile.EffectiveGapV(s.ChildSpacingV, s.HasChildSpacing);
            float EffGapNest(NestTarget n) => profile.EffectiveGapV(n.ChildSpacingV, n.HasChildSpacing);

            VisualElement activeExpandEl = current?.ExpandEl;
            float activeGap = current?.GapMain ?? 0f; // the gap that is actually OPEN right now

            // The expand host is a DESCENDANT of the container card, and its growth flows into every
            // ancestor card — inflate any card the host lives inside (not just an identity match).
            Rect VisualCardRect(CardRect c)
            {
                var r = c.Rect;
                r.position += Off(c.El);
                if (activeExpandEl != null && DropSnapshotBuilder.IsWithin(activeExpandEl, c.El)) r.height += activeGap;
                return r;
            }

            Rect VisualNestBody(NestTarget n)
            {
                var r = n.Body;
                r.position += Off(n.El);
                if (activeExpandEl != null && DropSnapshotBuilder.IsWithin(activeExpandEl, n.El)) r.height += activeGap;
                return r;
            }

            // Split frames: their pane layers are FULL-FRAME counter-rotated rects (the rotated-clipper
            // construction), so pane cards — and any content inside them — all report the same whole-frame
            // rect, including the parts the seam clips away. Clamp every pane descendant's rect to its
            // pane's conservative seam side so invisible clipped content can't grab the pointer; the
            // diagonal band between the clamps falls through to the frame itself, which the dispatch
            // below resolves by the pointer's exact side of the seam.
            List<(VisualElement paneEl, Rect clamp, Rect frameRect)> paneClamps = null;
            foreach (var c in region.Cards)
            {
                if (!(c.Node is ContainerNode fc) || !IsSplitFrame(fc)) continue;
                var fr = VisualCardRect(c);
                float halfTan = Mathf.Abs(Mathf.Tan(fc.Frame.SplitAngle * Mathf.Deg2Rad)) * fr.height * 0.5f;
                float mx = fr.x + fc.Frame.SplitPosition * fr.width;
                foreach (var pc in region.Cards)
                {
                    if (pc.Parent != fc) continue;
                    Rect clamp = pc.IndexInParent == 0
                        ? Rect.MinMaxRect(fr.xMin, fr.yMin, Mathf.Max(fr.xMin, mx - halfTan), fr.yMax)
                        : Rect.MinMaxRect(Mathf.Min(fr.xMax, mx + halfTan), fr.yMin, fr.xMax, fr.yMax);
                    (paneClamps ??= new List<(VisualElement, Rect, Rect)>()).Add((pc.El, clamp, fr));
                }
            }

            Rect ClampForCard(CardRect c, Rect vr)
            {
                if (paneClamps == null) return vr;
                foreach (var (paneEl, clamp, _) in paneClamps)
                {
                    if (c.El != paneEl && !DropSnapshotBuilder.IsWithin(c.El, paneEl)) continue;
                    float xMin = Mathf.Max(vr.xMin, clamp.xMin);
                    float xMax = Mathf.Min(vr.xMax, clamp.xMax);
                    return xMax > xMin ? Rect.MinMaxRect(xMin, vr.yMin, xMax, vr.yMax) : Rect.zero;
                }
                return vr;
            }

            // Pane descendants report full-frame rects (the rotated-clipper construction), so the
            // line/outline VISUALS emitted for them must be clamped to the pane's seam side too —
            // otherwise the indicator layer (which no clipper touches) draws them frame-wide.
            // The frame rect rides along for the thin context outline ("this pane lives in that frame").
            (Rect clamp, Rect frameRect)? PaneClampForEl(VisualElement el)
            {
                if (paneClamps == null || el == null) return null;
                foreach (var (paneEl, clamp, frameRect) in paneClamps)
                    if (el == paneEl || DropSnapshotBuilder.IsWithin(el, paneEl)) return (clamp, frameRect);
                return null;
            }

            Rect ClampVisualX(Rect r, Rect clamp)
            {
                float xMin = Mathf.Max(r.xMin, clamp.xMin);
                float xMax = Mathf.Min(r.xMax, clamp.xMax);
                return xMax > xMin ? Rect.MinMaxRect(xMin, r.yMin, xMax, r.yMax) : r;
            }

            // Deepest (most nested) card whose VISUAL rect contains the pointer; ties → smaller card.
            CardRect ctx = null;
            Rect ctxRect = default;
            foreach (var c in region.Cards)
            {
                var vr = ClampForCard(c, VisualCardRect(c));
                if (vr.width <= 0f || !vr.Contains(p)) continue;
                if (ctx == null
                    || c.Depth > ctx.Depth
                    || (c.Depth == ctx.Depth && vr.width * vr.height < ctxRect.width * ctxRect.height))
                {
                    ctx = c;
                    ctxRect = vr;
                }
            }

            DropSlot bestSlot = null;
            NestTarget bestNest = null;
            float bestScore = float.MaxValue;

            void ConsiderSlot(DropSlot s)
            {
                if (s == null || !SlotAllowed(s)) return;
                // A split Frame's direct slots are its pane positions — never a valid insertion point
                // (exactly two panes, always; drops route into a pane instead).
                if (s.Parent != null && IsSplitFrame(s.Parent)) return;
                bool active = MatchesCurrentGap(current, s);
                var o = Off(s.AboveEl ?? s.ParentEl);
                float d;
                if (s.Horizontal)
                {
                    float x = s.GapLine.x + o.x;
                    if (active)
                    {
                        // The open gap is a sticky REGION, not a line — pointing anywhere inside the
                        // space the siblings vacated keeps the current target (Unity-list behavior).
                        float x2 = x + gapMainH;
                        d = p.x < x ? x - p.x : (p.x > x2 ? p.x - x2 : 0f);
                    }
                    else d = Mathf.Abs(p.x - x);
                    float laneMin = s.GapLine.yMin + o.y, laneMax = s.GapLine.yMax + o.y;
                    if (p.y < laneMin || p.y > laneMax) d += DragTuning.OutOfLanePenalty;
                }
                else
                {
                    float y = s.GapLine.y + o.y;
                    if (active)
                    {
                        float y2 = y + EffGapSlot(s);
                        d = p.y < y ? y - p.y : (p.y > y2 ? p.y - y2 : 0f);
                    }
                    else d = Mathf.Abs(p.y - y);
                    if (p.x < s.GapLine.xMin - DragTuning.VerticalSlotXSlack ||
                        p.x > s.GapLine.xMax + DragTuning.VerticalSlotXSlack)
                        d += DragTuning.OutOfLanePenalty;
                }
                if (active) d -= DragTuning.HysteresisPx;
                if (d < bestScore) { bestScore = d; bestSlot = s; bestNest = null; }
            }

            void ConsiderNest(NestTarget n)
            {
                if (n == null || !NestAllowed(n)) return;
                // Never nest INTO the split frame itself — its children are the two panes.
                if (n.Node is ContainerNode nc && IsSplitFrame(nc)) return;
                float d = n.Empty ? 0f : DragTuning.GapCaptureBias;
                if (current != null && current.State == ResolvedDrop.DropState.Nest && current.TargetParent == n.Node)
                    d -= DragTuning.HysteresisPx;
                if (d < bestScore) { bestScore = d; bestNest = n; bestSlot = null; }
            }

            void ConsiderSlotsOf(VisualElement containerEl)
            {
                foreach (var s in region.Slots)
                    if (s.ParentEl == containerEl) ConsiderSlot(s);
            }

            void ConsiderNestOf(VisualElement el)
            {
                // A tracked container publishes one nest PER CELL, so exactly one of them may compete —
                // otherwise they'd all score identically (ConsiderNest scores by kind, not position) and
                // the first would win, which is the old "always fills the leftmost opening" behaviour.
                if (ConsiderCellNests(el, null)) return;
                foreach (var n in region.Nests)
                    if (n.El == el && !n.IsTabButton) ConsiderNest(n);
            }

            // The cell the pointer is in — or, when it is between/outside them (container padding, the
            // strip under a row), the nearest one. Containment is a PREFERENCE rather than a requirement
            // so anywhere-in-the-container still resolves to a sensible append, as it always has.
            // Matched by element or by owning node (the leaf branches below only know the parent node).
            // Returns false when the container has no cells, leaving the plain nest path to run.
            bool ConsiderCellNests(VisualElement el, LayoutNode owner)
            {
                NestTarget best = null;
                float bestD = float.MaxValue;
                foreach (var n in region.Nests)
                {
                    if (!n.IsCell || n.IsTabButton || !NestAllowed(n)) continue;
                    if (el != null ? n.El != el : !ReferenceEquals(n.Node, owner)) continue;
                    var b = VisualNestBody(n);
                    float d = b.Contains(p) ? 0f : SqrDistToRect(b, p);
                    // Sticky: the cell already showing keeps a small edge, so a pointer wobbling on a
                    // cell boundary doesn't flicker the highlight between neighbours.
                    if (current != null && current.State == ResolvedDrop.DropState.Nest
                        && ReferenceEquals(current.TargetParent, n.Node) && current.Slot == n.AppendIndex)
                        d -= DragTuning.HysteresisPx * DragTuning.HysteresisPx;
                    if (d < bestD) { bestD = d; best = n; }
                }
                if (best == null) return false;
                ConsiderNest(best);
                return true;
            }

            void ConsiderFlanking(CardRect card)
            {
                if (card.Parent == null || card.IndexInParent < 0) return;
                foreach (var s in region.Slots)
                {
                    if (s.Parent != card.Parent) continue;
                    if (s.Index != card.IndexInParent && s.Index != card.IndexInParent + 1) continue;
                    if (!DropSnapshotBuilder.IsWithin(card.El, s.ParentEl)) continue;
                    ConsiderSlot(s);
                }
            }

            // A TABLE's columns answer for themselves, header band included. Checked before everything below
            // because a table is a bounded set of columns living in two rows at once: the generic model can
            // only see the cells, so it would offer a sliver between them and leave the headers behind. The
            // usual two gates apply — a column may not leave its own item template, and a table inside a
            // split-frame pane keeps the resolver that knows how to clamp its visuals.
            var colBand = TableColumnResolver.FindBand(region, p);
            // The item-layout container hangs off its collection rather than living in a Children list, so
            // the split-frame walk has to start at the COLLECTION's own card — starting at the container
            // finds no parent and would answer "not in a frame" for every table in the project.
            ContainerNode colBandParent = null;
            if (colBand != null)
                foreach (var cr in region.Cards)
                    if (ReferenceEquals(cr.Node, colBand.TemplateOwner)) { colBandParent = cr.Parent; break; }
            if (colBand != null && (isAdd || colBand.TemplateOwner == draggedTemplateOwner)
                && (colBandParent == null || !IsUnderSplitFrame(region, colBandParent)))
            {
                var colDrop = TableColumnResolver.Resolve(region, colBand, p, current, isAdd, profile.Count);
                if (colDrop != null) return colDrop;
            }

            // Tab-strip buttons: nest targets anywhere (they sit outside the card walk).
            foreach (var n in region.Nests)
                if (n.IsTabButton && VisualNestBody(n).Contains(p)) ConsiderNest(n);

            // Set when the cell model hands a drop off to the sibling gaps beside a tracked container, so
            // the emitted drop can carry the ring that says which container is being passed over.
            Rect escapedBoxWorld = Rect.zero;
            float escapedRadius = 0f;

            // A TRACKED container's interior is a partition over its declared tracks, so it answers for
            // itself; only the escape band falls through, and it falls through to the SIBLING gaps alone —
            // never to this container's own interior slots, which is what "beside it" has to mean.
            //
            // Everything below this block is the model for every OTHER container (untracked stacks,
            // Flexible grids, Cards, Foldouts, Frames, Tabs), where insertion-between is the correct
            // semantics. A tracked container also falls through when it has no measurable cells at all —
            // e.g. every child hidden by a visibility condition — which is why its slots are still built.
            // See TrackedCellResolver.
            if (bestNest == null)
            {
                var box = TrackedCellResolver.FindBox(region, ctx);
                // Same two gates every other target obeys, which the cell path would otherwise skip:
                //  - TEMPLATE SCOPE: a drag from outside a collection's item template may not land inside
                //    it (and a drag from inside may not escape), exactly as NestAllowed enforces.
                //  - SPLIT-FRAME PANES: pane descendants report full-frame rects, so their indicators must
                //    be seam-clamped. The cell path draws its own visuals and knows nothing about clamps,
                //    so a tracked container inside a pane stays with the resolver that does.
                if (box != null && !(isAdd || box.TemplateOwner == draggedTemplateOwner)) box = null;
                // Walk the MODEL ancestry rather than asking the pane-clamp table: a pane's own container
                // element is not always the element the clamp is keyed on, so the table can miss it while
                // the node chain never does.
                if (box != null && IsUnderSplitFrame(region, box.Node)) box = null;
                if (box != null)
                {
                    var boxCard = ctx;
                    if (!ReferenceEquals(boxCard.Node, box.Node))
                        foreach (var cr in region.Cards)
                            if (ReferenceEquals(cr.Node, box.Node)) { boxCard = cr; break; }
                    var cellDrop = TrackedCellResolver.Resolve(
                        region, box, p, profile, current, Off(box.El));
                    if (cellDrop != null) return cellDrop;
                    // Escape: the drop belongs beside this container. Record it so the indicator can ring
                    // it neutrally — the sibling line alone looks identical to any other insert.
                    var escBody = box.Body;
                    escBody.position += Off(box.El);
                    escapedBoxWorld = DropSnapshotBuilder.ContentToWorld(region.Frame, escBody);
                    escapedRadius = box.OutlineRadius;
                    ConsiderFlanking(boxCard);
                    if (bestSlot != null) goto emit;
                    return ResolvedDrop.None;
                }
            }

            if (bestNest == null)
            {
                if (ctx == null)
                {
                    if (region.Kind == DropRegionKind.Canvas && region.CanvasRootEl != null && region.RootNode != null)
                    {
                        // Empty canvas space falls back to the root container. Organizer empty space
                        // is a deliberate no-op (never silently move something to root).
                        ConsiderSlotsOf(region.CanvasRootEl);
                        ConsiderNestOf(region.CanvasRootEl);
                    }
                }
                else if (ctx.IsTabs)
                {
                    // The Tabs chrome itself never accepts a drop (stray-tab guard); only its outer
                    // edges resolve, as the sibling gaps around the Tabs card.
                    if (DistToOuterEdge(ctx, ctxRect, p) <= DragTuning.EdgeBandPx) ConsiderFlanking(ctx);
                }
                else if (TryGetSplitFrameContext(region, ctx, out var frameCard, out var splitFrame))
                {
                    // Pointer over a split frame (its own body, or a pane card — including the diagonal
                    // band the pane clamps leave uncovered). The frame's outer edge escapes to its
                    // sibling gaps; the interior resolves by the pointer's exact side of the SEAM into
                    // that pane's own slots + nest. The frame itself never takes children.
                    var frameRect = VisualCardRect(frameCard);
                    if (DistToOuterEdge(frameCard, frameRect, p) <= DragTuning.EdgeBandPx)
                    {
                        ConsiderFlanking(frameCard);
                    }
                    else
                    {
                        float mx = frameRect.x + splitFrame.Frame.SplitPosition * frameRect.width;
                        float my = frameRect.y + frameRect.height * 0.5f;
                        float seamX = mx + (my - p.y) * Mathf.Tan(splitFrame.Frame.SplitAngle * Mathf.Deg2Rad);
                        int paneIndex = p.x < seamX ? 0 : 1;
                        VisualElement paneEl = null;
                        foreach (var pc in region.Cards)
                            if (pc.Parent == splitFrame && pc.IndexInParent == paneIndex) { paneEl = pc.El; break; }
                        if (paneEl != null)
                        {
                            ConsiderSlotsOf(paneEl);
                            ConsiderNestOf(paneEl);
                        }
                        else
                        {
                            ConsiderFlanking(frameCard); // degenerate model (missing pane) — never the frame itself
                        }
                    }
                }
                else if (!ctx.IsContainer)
                {
                    // Leaf (or collection) body → the two flanking gaps. Leaves never nest.
                    ConsiderFlanking(ctx);
                    if (isAdd && ctx.Node is CollectionNode) ConsiderNestOf(ctx.El);
                    // Tracked-Rows LAST child: its trailing append slot is gone (the nest owns the
                    // trailing region), so flanking alone would map the whole card to "insert
                    // above it" — let the nest compete over its lower half. Mid-list children keep
                    // both flanking gaps and never need (or want) the nest stealing the card body.
                    if (ctx.Parent != null && ctx.Parent.Type == ContainerType.Rows && ctx.Parent.GridColumns > 0)
                    {
                        bool hasBelowSlot = false;
                        foreach (var s in region.Slots)
                            if (s.Parent == ctx.Parent && s.Index == ctx.IndexInParent + 1
                                && DropSnapshotBuilder.IsWithin(ctx.El, s.ParentEl)) { hasBelowSlot = true; break; }
                        if (!hasBelowSlot && !ConsiderCellNests(null, ctx.Parent))
                            foreach (var n in region.Nests)
                                if (n.Node == ctx.Parent && !n.IsTabButton) ConsiderNest(n);
                    }
                    // Fixed-row grid LAST child: the trailing new-row slot is gone too (an append
                    // REFLOWS the grid into more columns), so mid-cell the nest's reflow/cell-fill
                    // prediction competes; the cell edges keep their in-row insert lines. Mid-grid
                    // children keep flanking only — their insert index is position-honest.
                    if (ctx.Parent != null && ctx.Parent.Type == ContainerType.Grid
                        && ctx.Parent.GridConstraint == GridConstraint.FixedRowCount
                        && ctx.IndexInParent == (ctx.Parent.Children?.Count ?? 0) - 1)
                    {
                        if (!ConsiderCellNests(null, ctx.Parent))
                            foreach (var n in region.Nests)
                                if (n.Node == ctx.Parent && !n.IsTabButton) ConsiderNest(n);
                    }
                }
                else
                {
                    // Container's own body (the deepest-card rule guarantees the pointer is not over
                    // any child card). Outer edge band → sibling gaps; otherwise its own gaps + nest.
                    if (DistToOuterEdge(ctx, ctxRect, p) <= DragTuning.EdgeBandPx) ConsiderFlanking(ctx);
                    ConsiderSlotsOf(ctx.El);
                    ConsiderNestOf(ctx.El);
                }
            }

            emit:
            var frame = region.Frame;

            if (bestSlot != null)
            {
                // Row-aware gap for the phantom new-row slot: the held cards land SIDE-BY-SIDE
                // filling rows of the grid's track count, so the space to open is the landing ROWS'
                // heights — the stacked sum lied for multi-drags (a 3-token drop into a 2-col grid
                // opened a 3-cards-tall gap for what lands as 2 rows).
                int phantomCols = bestSlot.PhantomCells?.Count ?? 0;
                float slotSpacing = bestSlot.HasChildSpacing ? bestSlot.ChildSpacingV : profile.FallbackSpacing;
                float eff = bestSlot.Horizontal ? 0f
                          : phantomCols > 0 ? profile.RowsGapV(phantomCols, bestSlot.ChildSpacingV, bestSlot.HasChildSpacing)
                          : EffGapSlot(bestSlot);

                // Siblings still shift by the full stack (eff — they move down one card per held
                // card), but a tracked container's empty "+" cells already reserve room below them:
                // the insert CONSUMES a cell, so the container itself only grows by the shortfall.
                // Without this, inserting into a Rows-count container inflated it a full card past
                // the "+" rows it will actually reuse.
                float expandGap = eff;
                if (!bestSlot.Horizontal && phantomCols == 0 && bestSlot.SpareCellsV > 0f)
                    expandGap = Mathf.Max(0f, eff - bestSlot.SpareCellsV);

                var o = Off(bestSlot.AboveEl ?? bestSlot.ParentEl);
                Rect line = bestSlot.Horizontal
                    ? new Rect(bestSlot.GapLine.x + o.x - 1f, bestSlot.GapLine.yMin + o.y, 2f, bestSlot.GapLine.height)
                    : new Rect(bestSlot.GapLine.xMin + o.x, bestSlot.GapLine.y + o.y - 1f, bestSlot.GapLine.width, 2f);

                var outline = bestSlot.ParentRect;
                outline.position += Off(bestSlot.ParentEl);
                if (!bestSlot.Horizontal) outline.height += expandGap; // the container is/will be expanded

                float ghostW = bestSlot.CellW;
                var paneClamp = PaneClampForEl(bestSlot.ParentEl);
                Rect context = Rect.zero;
                if (paneClamp.HasValue)
                {
                    line = ClampVisualX(line, paneClamp.Value.clamp);
                    outline = ClampVisualX(outline, paneClamp.Value.clamp);
                    ghostW = ghostW > 0f ? Mathf.Min(ghostW, paneClamp.Value.clamp.width) : paneClamp.Value.clamp.width;
                    context = paneClamp.Value.frameRect; // thin outline: the frame this pane lives in
                }

                Rect ClampToWorld(Rect r)
                {
                    if (paneClamp.HasValue) r = ClampVisualX(r, paneClamp.Value.clamp);
                    return DropSnapshotBuilder.ContentToWorld(frame, r);
                }

                // Landing-cell rects: one full rect per held card at the exact spot it will occupy.
                var landing = new List<Rect>(profile.Count);
                Rect[] phantomCells = null;
                if (phantomCols > 0)
                {
                    // New-row slot: the held cards fill the phantom row(s) left-to-right. The first
                    // phantom row also renders as context "+" cells (its unfilled remainder reads as
                    // the row that will exist); the bare line reads as a mid-row insert — suppressed.
                    var rowHeights = profile.RowHeights(phantomCols);
                    float rowY = 0f;
                    for (int i = 0; i < profile.Count; i++)
                    {
                        int r = i / phantomCols, ci = i % phantomCols;
                        if (ci == 0 && r > 0) rowY += rowHeights[r - 1] + slotSpacing;
                        var pc = bestSlot.PhantomCells[ci];
                        pc.position += o;
                        landing.Add(ClampToWorld(new Rect(
                            pc.x, pc.y + rowY, pc.width, Mathf.Max(22f, rowHeights[r] - 6f))));
                    }
                    float row0H = Mathf.Max(22f, rowHeights[0] - 6f);
                    phantomCells = new Rect[bestSlot.PhantomCells.Count];
                    for (int i = 0; i < phantomCells.Length; i++)
                    {
                        var pc = bestSlot.PhantomCells[i];
                        pc.position += o;
                        pc.height = row0H;
                        phantomCells[i] = ClampToWorld(pc);
                    }
                    line = Rect.zero;
                }
                else if (!bestSlot.Horizontal)
                {
                    // Vertical stack insert: the cards fill the opened gap top-to-bottom.
                    float y = bestSlot.GapLine.y + o.y;
                    for (int i = 0; i < profile.Count; i++)
                    {
                        landing.Add(ClampToWorld(new Rect(
                            bestSlot.GapLine.xMin + o.x, y, bestSlot.GapLine.width, profile.Heights[i])));
                        y += profile.Heights[i] + slotSpacing;
                    }
                }
                else
                {
                    // Horizontal insert. An UNTRACKED Columns (no cap, single row) re-splits into
                    // n+held equal columns on drop — predict the landing columns at their post-drop
                    // x/width (the old fixed 28px sliver at the seam was neither the size nor the
                    // place anything lands). Wrapped/tracked targets keep the classic lane sliver.
                    bool predicted = false;
                    var sp = bestSlot.Parent;
                    if (sp != null && sp.Type == ContainerType.Grid
                        && sp.GridConstraint == GridConstraint.FixedRowCount)
                    {
                        // Fixed-row grid in-row insert that RAISES the column count (the grid is
                        // full): the drop refills the whole grid row-major into ceil(n/rows)
                        // columns, so the held cards preview at their post-reflow cells — the
                        // slot's insert index decides which — and phantom "+" pads close the
                        // unfilled tail (the nest's reflow look, anchored to THIS insert).
                        int rowsCap = Mathf.Max(1, sp.GridColumns);
                        var cellRects = new List<Rect>();
                        int insertPos = 0;
                        foreach (var card in region.Cards)
                            if (card.Parent == sp && DropSnapshotBuilder.IsWithin(card.El, bestSlot.ParentEl))
                            {
                                cellRects.Add(VisualCardRect(card));
                                if (card.IndexInParent < bestSlot.Index) insertPos++;
                            }
                        int have = cellRects.Count;
                        int total = have + profile.Count;
                        int colsNow = Mathf.Max(1, Mathf.CeilToInt(have / (float)rowsCap));
                        int colsAfter = Mathf.Max(1, Mathf.CeilToInt(total / (float)rowsCap));
                        if (have > 0 && colsAfter > colsNow)
                        {
                            float bx0 = float.MaxValue, bx1 = float.MinValue, topY = float.MaxValue;
                            foreach (var cr in cellRects)
                            {
                                bx0 = Mathf.Min(bx0, cr.xMin);
                                bx1 = Mathf.Max(bx1, cr.xMax);
                                topY = Mathf.Min(topY, cr.yMin);
                            }
                            float gapX = Mathf.Max(2f, sp.Gap > 0f ? sp.Gap : 6f);
                            float wCol = Mathf.Max(24f, ((bx1 - bx0) - (colsAfter - 1) * gapX) / colsAfter);
                            float parentBottom = bestSlot.ParentRect.yMax + Off(bestSlot.ParentEl).y;
                            float rowPitch = Mathf.Max(26f, (parentBottom - 6f - topY + slotSpacing) / rowsCap);
                            for (int i = 0; i < profile.Count; i++)
                            {
                                int g = insertPos + i;
                                landing.Add(ClampToWorld(new Rect(bx0 + (g % colsAfter) * (wCol + gapX),
                                    topY + (g / colsAfter) * rowPitch,
                                    wCol, Mathf.Min(profile.Heights[i], rowPitch - 4f))));
                            }
                            int unfilled = colsAfter * rowsCap - total;
                            if (unfilled > 0)
                            {
                                phantomCells = new Rect[unfilled];
                                for (int u = 0; u < unfilled; u++)
                                {
                                    int g = total + u;
                                    phantomCells[u] = ClampToWorld(new Rect(bx0 + (g % colsAfter) * (wCol + gapX),
                                        topY + (g / colsAfter) * rowPitch, wCol, Mathf.Max(22f, rowPitch - 8f)));
                                }
                            }
                            ghostW = wCol;
                            predicted = true;
                        }
                    }
                    if (!predicted && sp != null && sp.Type == ContainerType.Columns && sp.GridColumns <= 0)
                    {
                        var sibs = new List<Rect>();
                        foreach (var card in region.Cards)
                            if (card.Parent == sp && DropSnapshotBuilder.IsWithin(card.El, bestSlot.ParentEl))
                                sibs.Add(VisualCardRect(card));
                        bool oneRow = sibs.Count > 0;
                        for (int i = 1; i < sibs.Count && oneRow; i++)
                            oneRow = sibs[i].yMin < sibs[0].yMax && sibs[0].yMin < sibs[i].yMax;
                        if (oneRow)
                        {
                            float bx0 = float.MaxValue, bx1 = float.MinValue, laneY = float.MaxValue, laneB = float.MinValue;
                            foreach (var sr in sibs)
                            {
                                bx0 = Mathf.Min(bx0, sr.xMin);
                                bx1 = Mathf.Max(bx1, sr.xMax);
                                laneY = Mathf.Min(laneY, sr.yMin);
                                laneB = Mathf.Max(laneB, sr.yMax);
                            }
                            int after = sibs.Count + profile.Count;
                            float innerGap = Mathf.Max(2f, sp.Gap > 0f ? sp.Gap : 4f);
                            float wAfter = Mathf.Max(24f, ((bx1 - bx0) - (after - 1) * innerGap) / after);
                            int k = 0;
                            float gx = bestSlot.GapLine.x + o.x;
                            foreach (var sr in sibs) if (sr.center.x < gx) k++;
                            for (int i = 0; i < profile.Count; i++)
                                landing.Add(ClampToWorld(new Rect(
                                    bx0 + (k + i) * (wAfter + innerGap), laneY, wAfter, laneB - laneY)));
                            ghostW = wAfter;
                            predicted = true;
                        }
                    }
                    if (!predicted)
                    {
                        // Gap between columns: one cell filling the lane the siblings vacate.
                        landing.Add(ClampToWorld(new Rect(
                            bestSlot.GapLine.x + o.x, bestSlot.GapLine.yMin + o.y, gapMainH, bestSlot.GapLine.height)));
                    }
                }

                return new ResolvedDrop
                {
                    State = ResolvedDrop.DropState.Gap,
                    TargetParent = bestSlot.Parent,
                    Slot = bestSlot.Index,
                    LineWorld = line == Rect.zero ? Rect.zero : DropSnapshotBuilder.ContentToWorld(frame, line),
                    LineVertical = bestSlot.Horizontal,
                    OutlineWorld = DropSnapshotBuilder.ContentToWorld(frame, outline),
                    OutlineRadius = bestSlot.ParentRadius,
                    GhostWidth = ghostW,
                    ContextOutlineWorld = context == Rect.zero ? Rect.zero : DropSnapshotBuilder.ContentToWorld(frame, context),
                    PhantomCellsWorld = phantomCells,
                    LandingCellsWorld = landing.Count > 0 ? landing.ToArray() : null,
                    ActiveSlot = bestSlot,
                    RowGroup = bestSlot.RowGroup,
                    ExpandEl = bestSlot.Horizontal || expandGap <= 0.5f ? null : bestSlot.ExpandHost,
                    ExpandTo = bestSlot.Horizontal || expandGap <= 0.5f ? 0f : bestSlot.ExpandHostHeight + expandGap,
                    ExpandPush = bestSlot.Horizontal ? null : bestSlot.LayoutPush,
                    GapMain = eff,
                    // One line per held card, spaced exactly as the cards will land. Horizontal flows
                    // keep a single line (widths are flex-weighted; spacing there would be a lie).
                    LineOffsets = bestSlot.Horizontal
                        ? null
                        : profile.LineOffsets(bestSlot.ChildSpacingV, bestSlot.HasChildSpacing),
                    // Non-zero only when the cell model routed here from a tracked container's escape
                    // band; it rings that container so "beside this one" is stated, not merely implied.
                    EscapedBoxWorld = escapedBoxWorld,
                    OutlineRadiusEscaped = escapedRadius
                };
            }

            if (bestNest != null)
            {
                // Horizontal containers never pre-grow for an append: the incoming card lands
                // SIDE-BY-SIDE, and springing the row's min-height open just stretches the cells
                // already in it (the "existing button gets taller" bug). Cell-fill nests come in
                // with a null ExpandHost from the snapshot — the "+" cell already reserves the room.
                bool expands = !bestNest.IsTabButton && !(bestNest.Node is CollectionNode)
                               && !bestNest.Horizontal && bestNest.ExpandHost != null;
                float eff = EffGapNest(bestNest);
                var paneClamp = PaneClampForEl(bestNest.El);
                Vector2 no = Off(bestNest.El);
                float spacing = bestNest.HasChildSpacing ? bestNest.ChildSpacingV : profile.FallbackSpacing;
                var nestContainer = bestNest.Node as ContainerNode;

                Rect ClampNestToWorld(Rect r)
                {
                    if (paneClamp.HasValue) r = ClampVisualX(r, paneClamp.Value.clamp);
                    return DropSnapshotBuilder.ContentToWorld(frame, r);
                }

                Rect line = Rect.zero;
                bool lineVertical = false;
                if (!bestNest.IsTabButton && bestNest.InteriorLine != Rect.zero)
                {
                    lineVertical = bestNest.InteriorLine.width <= 0.01f && bestNest.InteriorLine.height > 0.01f;
                    Rect il = bestNest.InteriorLine;
                    il.position += no;
                    Rect inflated = lineVertical
                        ? new Rect(il.x - 1f, il.yMin, 2f, il.height)
                        : new Rect(il.xMin, il.y - 1f, il.width, 2f);
                    if (paneClamp.HasValue) inflated = ClampVisualX(inflated, paneClamp.Value.clamp);
                    line = DropSnapshotBuilder.ContentToWorld(frame, inflated);
                }

                // ---- Landing-cell preview + per-case expansion (multi-token aware) ----
                // Every held card gets a full landing rect at the spot it will occupy. Cases:
                //   1. Visible "+" cells (tracked Rows/Columns, Grid pads): fill them in order; the
                //      overflow stacks below (Rows) or wraps into synthesized grid rows.
                //   2. EMPTY horizontal container with a hint (Columns tracks=0 / Flexible grid):
                //      predict the columns the drop CREATES and lay the cells side-by-side — the old
                //      stacked lines said "one column on the left" for what lands as N columns.
                //   3. Vertical append (below the last child / the empty hint): stacked full rects.
                //   4. Horizontal append after the last cell: one lane-height cell at the seam.
                var landing = new List<Rect>(profile.Count);
                Rect[] phantomCells = null;
                float ghostW = 0f;                 // 0 = fall through to outline width
                bool overflowGrow = false;         // grow the container's visible box (cases 1/2)
                float overflowGap = 0f;
                float vertGap = eff;               // vertical-append growth (reduced when the hint band is reused)
                Rect? outlineOverride = null;      // cap-split: the bold ring follows the shrunken first fill

                // "Rows: N" is a hard cap — a capped Rows container never stacks past its Nth
                // track; the overflow starts a NEW COLUMN beside the existing tracks (the vertical
                // transpose of the Columns cap). PredictRowsCapColumns lays each overflow card into
                // the predicted column(s), closes the last one with phantom "+" pads, and only
                // grows the container when the new column runs taller than the current box.
                int rowCap = nestContainer != null && nestContainer.Type == ContainerType.Rows
                    ? Mathf.Max(0, nestContainer.GridColumns) : 0;

                // A FIXED-ROW grid's column count is DERIVED (ceil(children / N)), so a drop that
                // overflows the reserved cells REFLOWS the whole grid into more columns — the
                // row-major refill moves the EXISTING children too. Filling the frozen pads and
                // then wrapping extra rows below would preview a layout the drop never produces.
                int gridRowCap = nestContainer != null && nestContainer.Type == ContainerType.Grid
                                 && nestContainer.GridConstraint == GridConstraint.FixedRowCount
                    ? Mathf.Max(1, nestContainer.GridColumns) : 0;

                // Predicted post-drop column geometry, hoisted so EVERY cell the preview draws can
                // use it: when the drop adds a column, the direct "+" fills must shrink to the
                // predicted width too (full-width fills said "you keep this width" — a lie the
                // moment the container splits).
                // PadCount, not CellRects.Count: CellRects starts at the targeted cell and includes the
                // EmptyCellNode children holding cells open, which VisibleChildCount already counts.
                int rcPads = bestNest.PadCount;
                int rcTracksNow = bestNest.VisibleChildCount + rcPads;
                int rcColsNow = 1, rcColsAfter = 1;
                float rcX0 = 0f, rcTopY = 0f, rcWCol = 0f, rcGapX = 0f, rcPadH = 28f;
                bool rcPredicted = false; // set when the cap prediction owns the preview/expansion
                if (rowCap > 0)
                {
                    rcColsNow = Mathf.Max(1, Mathf.CeilToInt(rcTracksNow / (float)rowCap));
                    int rcTotal = bestNest.VisibleChildCount + profile.Count;
                    rcColsAfter = Mathf.Max(rcColsNow, Mathf.CeilToInt(rcTotal / (float)rowCap));
                    rcX0 = bestNest.ContentBandX0 + no.x;
                    rcTopY = bestNest.ContentTopY + no.y;
                    rcGapX = Mathf.Max(2f, nestContainer.Gap > 0f ? nestContainer.Gap : 6f);
                    float innerW = Mathf.Max(40f, bestNest.ContentBandX1 + no.x - rcX0);
                    rcWCol = Mathf.Max(24f, (innerW - (rcColsAfter - 1) * rcGapX) / rcColsAfter);
                    if (rcPads > 0 && bestNest.CellRects != null && bestNest.CellRects.Count > 0)
                        rcPadH = bestNest.CellRects[0].height;
                }

                void PredictRowsCapColumns(int direct)
                {
                    var cb = bestNest.ContainerBody;
                    cb.position += no;
                    int over = profile.Count - direct;
                    float colY = rcTopY, colBottom = rcTopY;
                    int curCol = -1;
                    for (int m = 0; m < over; m++)
                    {
                        int t = rcTracksNow + m;    // tracks fill column-major: t / cap = column
                        int col = t / rowCap;
                        if (col != curCol) { curCol = col; colY = rcTopY; }
                        float cx = rcX0 + col * (rcWCol + rcGapX);
                        float h = profile.Heights[direct + m];
                        landing.Add(ClampNestToWorld(new Rect(cx, colY, rcWCol, h)));
                        colY += h + spacing;
                        colBottom = Mathf.Max(colBottom, colY - spacing);
                    }
                    // Phantom "+" pads close the last predicted column's unfilled tracks — the
                    // exact cells the regroup will render after the drop.
                    int unfilled = rcColsAfter * rowCap - (bestNest.VisibleChildCount + profile.Count);
                    if (unfilled > 0 && curCol >= 0)
                    {
                        var pads = new List<Rect>(unfilled);
                        float cx = rcX0 + (rcColsAfter - 1) * (rcWCol + rcGapX);
                        for (int u = 0; u < unfilled; u++)
                        {
                            pads.Add(ClampNestToWorld(new Rect(cx, colY, rcWCol, rcPadH)));
                            colY += rcPadH + spacing;
                            colBottom = Mathf.Max(colBottom, colY - spacing);
                        }
                        phantomCells = pads.ToArray();
                    }
                    overflowGap = Mathf.Max(0f, colBottom - (cb.yMax - 6f));
                    overflowGrow = overflowGap > 0.5f && bestNest.GrowHost != null;
                    ghostW = rcWCol;
                    line = Rect.zero; // the cells are the whole message
                    rcPredicted = true;
                }

                // Fixed-row grid reflow: lay every held card at its predicted post-reflow cell
                // (row-major, ceil((children+held)/rows) columns), close the tail with phantom
                // "+" pads. The row count is fixed, so the box doesn't grow — the classic append
                // gap must never open, and the ring union below wraps the predicted cells.
                void PredictGridRowReflow()
                {
                    var cb = bestNest.ContainerBody;
                    cb.position += no;
                    int have = bestNest.VisibleChildCount;
                    int total = have + profile.Count;
                    int colsAfter = Mathf.Max(1, Mathf.CeilToInt(total / (float)gridRowCap));
                    float bx0 = bestNest.ContentBandX0 + no.x;
                    float bx1 = bestNest.ContentBandX1 + no.x;
                    float gapX = Mathf.Max(2f, nestContainer.Gap > 0f ? nestContainer.Gap : 6f);
                    float wCol = Mathf.Max(24f, ((bx1 - bx0) - (colsAfter - 1) * gapX) / colsAfter);
                    float topY = bestNest.ContentTopY + no.y;
                    float rowPitch = Mathf.Max(26f, (cb.yMax - 6f - topY + spacing) / gridRowCap);
                    for (int i = 0; i < profile.Count; i++)
                    {
                        int g = have + i;
                        var cr = new Rect(bx0 + (g % colsAfter) * (wCol + gapX),
                            topY + (g / colsAfter) * rowPitch,
                            wCol, Mathf.Min(profile.Heights[i], rowPitch - 4f));
                        // Cell-fill nests (Body = a frozen "+" pad) move the bold ring to the first
                        // predicted fill — the pad is not where anything lands after the reflow. A
                        // FULL grid's nest Body already IS the container, and it must stay the ring:
                        // no thin context ring exists when Body == ContainerBody, so overriding here
                        // left the grid with no outline at all ("replaces that one cell" misread).
                        if (i == 0 && bestNest.ContainerBody != Rect.zero
                            && bestNest.ContainerBody != bestNest.Body)
                            outlineOverride = cr;
                        landing.Add(ClampNestToWorld(cr));
                    }
                    int unfilled = colsAfter * gridRowCap - total;
                    if (unfilled > 0)
                    {
                        var pads = new List<Rect>(unfilled);
                        for (int u = 0; u < unfilled; u++)
                        {
                            int g = total + u;
                            pads.Add(ClampNestToWorld(new Rect(bx0 + (g % colsAfter) * (wCol + gapX),
                                topY + (g / colsAfter) * rowPitch, wCol, Mathf.Max(22f, rowPitch - 8f))));
                        }
                        phantomCells = pads.ToArray();
                    }
                    ghostW = wCol;
                    line = Rect.zero; // the cells are the whole message
                    rcPredicted = true;
                }

                if (!bestNest.IsTabButton && !(bestNest.Node is CollectionNode))
                {
                    if (bestNest.CellRects != null && bestNest.CellRects.Count > 0
                        && gridRowCap > 0 && profile.Count > bestNest.CellRects.Count)
                    {
                        // Fixed-row grid with visible "+" cells, but more cards in hand than cells
                        // left: the drop reflows the column count, so the frozen pads are not
                        // where anything lands — predict the whole post-drop arrangement.
                        PredictGridRowReflow();
                    }
                    else if (bestNest.CellRects != null && bestNest.CellRects.Count > 0)
                    {
                        var cells = bestNest.CellRects;
                        int direct = Mathf.Min(profile.Count, cells.Count);
                        // Cap-splitting drop: the tracks these fills land in become a COLUMN of
                        // the split container — preview them at the predicted width (they keep
                        // their track's y; only x/width change) so the future reads correctly.
                        // The bold nest ring (Body = first "+" cell) shrinks with them, or it
                        // would draw a full-width ring behind the narrowed cells.
                        bool rcSplit = rowCap > 0 && !bestNest.Horizontal && rcColsAfter > rcColsNow;
                        for (int i = 0; i < direct; i++)
                        {
                            var cr = cells[i];
                            cr.position += no;
                            if (rcSplit)
                            {
                                cr.x = rcX0 + (rcColsNow - 1) * (rcWCol + rcGapX);
                                cr.width = rcWCol;
                                if (i == 0) outlineOverride = cr;
                            }
                            landing.Add(ClampNestToWorld(cr));
                        }
                        int overflow = profile.Count - direct;
                        if (overflow > 0)
                        {
                            var lastCell = cells[cells.Count - 1];
                            lastCell.position += no;
                            if (!bestNest.Horizontal && rowCap > 0)
                            {
                                // Capped Rows, tracks full: the rest start a new column.
                                PredictRowsCapColumns(direct);
                            }
                            else if (!bestNest.Horizontal)
                            {
                                // Uncapped vertical tracks full: the rest stack below the last "+" cell.
                                float y = lastCell.yMax + spacing;
                                for (int m = 0; m < overflow; m++)
                                {
                                    float h = profile.Heights[direct + m];
                                    landing.Add(ClampNestToWorld(new Rect(lastCell.x, y, lastCell.width, h)));
                                    y += h + spacing;
                                    overflowGap += h + spacing;
                                }
                                overflowGrow = bestNest.GrowHost != null;
                            }
                            else if (nestContainer != null &&
                                     (nestContainer.Type == ContainerType.Grid ||
                                      (nestContainer.Type == ContainerType.Columns && nestContainer.GridColumns > 0)))
                            {
                                // Grid pads / capped Columns tracks full: the rest wrap into new
                                // rows below the cell band (the Columns cap reuses the grid row
                                // machinery, so the same wrapped-rows preview is the honest one).
                                var cb = bestNest.ContainerBody;
                                cb.position += no;
                                float cellW = Mathf.Max(24f, cells[0].width);
                                int cols = nestContainer.Type == ContainerType.Columns
                                    ? Mathf.Max(1, nestContainer.GridColumns)
                                    : Mathf.Max(1, Mathf.RoundToInt((cb.width - 12f) / cellW));
                                float bandBottom = lastCell.yMax;
                                float rowY = bandBottom + spacing;
                                float rowH = 0f;
                                for (int m = 0; m < overflow; m++)
                                {
                                    int r = m / cols, ci = m % cols;
                                    if (ci == 0 && m > 0) { rowY += rowH + spacing; }
                                    rowH = 0f;
                                    for (int k = r * cols; k <= m; k++) rowH = Mathf.Max(rowH, profile.Heights[direct + k]);
                                    landing.Add(ClampNestToWorld(new Rect(
                                        cb.xMin + 6f + ci * cellW, rowY, cellW - 4f, profile.Heights[direct + m])));
                                }
                                overflowGap = rowY + rowH + spacing - bandBottom;
                                overflowGrow = bestNest.GrowHost != null;
                            }
                            // UNtracked Columns never have pads, so no other overflow case exists.
                        }
                        // Prediction paths pick their own (narrower) cell width; plain fills match the pads.
                        if (landing.Count > 0 && ghostW <= 0f) ghostW = cells[0].width;
                    }
                    else if (bestNest.Horizontal && bestNest.Empty && bestNest.HintRect.width > 0f && nestContainer != null)
                    {
                        // Empty Columns(tracks=0)/Flexible grid: predict the landing arrangement the
                        // drop actually creates and preview it side-by-side. The band spans the
                        // CONTAINER's content width (hint left edge → container right edge) — the
                        // hint label shrink-wraps to its text inside a flex row, so its own width
                        // under-reported the columns by half.
                        var hint = bestNest.HintRect;
                        hint.position += no;
                        var cbBand = bestNest.ContainerBody;
                        cbBand.position += no;
                        float bandX0 = hint.xMin;
                        float bandW = Mathf.Max(40f, cbBand.xMax - 8f - bandX0);
                        float modelGap = Mathf.Max(0f, nestContainer.Gap);
                        int cols;
                        if (nestContainer.Type != ContainerType.Grid)
                            cols = Mathf.Max(1, profile.Count); // Columns, no track count: one column per card
                        else if (nestContainer.GridConstraint == GridConstraint.FixedRowCount)
                            cols = Mathf.Max(1, Mathf.CeilToInt(profile.Count / (float)Mathf.Max(1, nestContainer.GridColumns)));
                        else if (nestContainer.GridConstraint == GridConstraint.FixedColumnCount)
                            cols = Mathf.Max(1, nestContainer.GridColumns);
                        else
                            // ApplyGridLayout's Flexible formula (min cell width 150) on the band.
                            cols = Mathf.Max(1, Mathf.FloorToInt((bandW + modelGap) / (150f + modelGap)));

                        float innerGap = Mathf.Max(2f, modelGap);
                        float cellW = Mathf.Max(24f, (bandW - (cols - 1) * innerGap) / cols);
                        var rowHeights = profile.RowHeights(cols);
                        float rowY = hint.yMin;
                        for (int i = 0; i < profile.Count; i++)
                        {
                            int r = i / cols, ci = i % cols;
                            if (ci == 0 && r > 0) rowY += rowHeights[r - 1] + spacing;
                            landing.Add(ClampNestToWorld(new Rect(
                                bandX0 + ci * (cellW + innerGap), rowY, cellW, rowHeights[r])));
                        }
                        // A grid wider than the drop leaves real "+" pad cells in the last row —
                        // show them as inactive context cells so the row's true shape reads.
                        int lastUsed = profile.Count - ((profile.Count - 1) / cols) * cols;
                        if (nestContainer.Type == ContainerType.Grid && lastUsed < cols)
                        {
                            var pads = new List<Rect>(cols - lastUsed);
                            float lastRowH = rowHeights[rowHeights.Length - 1];
                            for (int ci = lastUsed; ci < cols; ci++)
                                pads.Add(ClampNestToWorld(new Rect(
                                    bandX0 + ci * (cellW + innerGap), rowY, cellW, lastRowH)));
                            phantomCells = pads.ToArray();
                        }

                        // The container only needs to grow past the hint band it already reserves.
                        float totalH = (rowY + rowHeights[rowHeights.Length - 1]) - hint.yMin;
                        overflowGap = Mathf.Max(0f, totalH - hint.height);
                        overflowGrow = overflowGap > 0.5f && bestNest.ExpandHost != null;
                        line = Rect.zero; // the cells are the whole message
                        ghostW = cellW;
                    }
                    else if (rowCap > 0 && !bestNest.Horizontal && !bestNest.Empty
                             && bestNest.VisibleChildCount > 0
                             && bestNest.VisibleChildCount % rowCap == 0)
                    {
                        // FULL capped Rows (every track holds a child, no "+" pads left): the held
                        // cards start the next column(s) — a full-width stacked-append preview
                        // would promise rows the cap will never allow.
                        PredictRowsCapColumns(0);
                    }
                    else if (gridRowCap > 0 && !bestNest.Empty && bestNest.VisibleChildCount > 0)
                    {
                        // FULL fixed-row grid (every reserved cell holds a child): the appended
                        // cards trigger the same column reflow — predict it rather than draw the
                        // horizontal seam sliver.
                        PredictGridRowReflow();
                    }
                    else if (!lineVertical && bestNest.InteriorLine != Rect.zero)
                    {
                        // Vertical append: stacked rects. An EMPTY container's first card takes the
                        // hint's place — highlight the "Empty Container" band itself (not a new block
                        // below it), stack the rest under it, and only grow past what the hint
                        // already reserves.
                        Rect il = bestNest.InteriorLine;
                        il.position += no;
                        float y = il.y;
                        int startIdx = 0;
                        if (bestNest.Empty && bestNest.HintRect.width > 0f)
                        {
                            var hint = bestNest.HintRect;
                            hint.position += no;
                            float h0 = Mathf.Max(hint.height, profile.Heights[0]);
                            landing.Add(ClampNestToWorld(new Rect(hint.xMin, hint.yMin, hint.width, h0)));
                            startIdx = 1;
                            y = hint.yMin + h0 + spacing;
                        }
                        for (int i = startIdx; i < profile.Count; i++)
                        {
                            landing.Add(ClampNestToWorld(new Rect(il.xMin, y, il.width, profile.Heights[i])));
                            y += profile.Heights[i] + spacing;
                        }
                        // The box often already reserves the room the stack needs — the hint band,
                        // and a tall shaped frame's slack below the append line (a pill keeps its
                        // curve's height regardless of content). Grow only by how far the stack
                        // runs past the current bottom: the full-stack gap previewed a pill ~2x
                        // the height the drop actually produces.
                        var cbV = bestNest.ContainerBody != Rect.zero ? bestNest.ContainerBody : bestNest.Body;
                        cbV.position += no;
                        vertGap = Mathf.Clamp(y - spacing - (cbV.yMax - 6f), 0f, eff);
                    }
                    else if (lineVertical)
                    {
                        Rect il = bestNest.InteriorLine;
                        il.position += no;
                        if (nestContainer != null && nestContainer.Type == ContainerType.Columns
                            && nestContainer.GridColumns <= 0 && !bestNest.Empty)
                        {
                            // Untracked Columns append: the drop re-splits the row into n+held
                            // equal columns — predict them (same math as the slot branch).
                            int n = bestNest.VisibleChildCount;
                            int after = n + profile.Count;
                            float bx0 = bestNest.ContentBandX0 + no.x;
                            float bx1 = bestNest.ContentBandX1 + no.x;
                            float innerGap = Mathf.Max(2f, nestContainer.Gap > 0f ? nestContainer.Gap : 4f);
                            float wAfter = Mathf.Max(24f, ((bx1 - bx0) - (after - 1) * innerGap) / after);
                            for (int i = 0; i < profile.Count; i++)
                                landing.Add(ClampNestToWorld(new Rect(
                                    bx0 + (n + i) * (wAfter + innerGap), il.yMin, wAfter, Mathf.Max(22f, il.height))));
                            ghostW = wAfter;
                        }
                        else
                        {
                            // Horizontal append after the last cell: one lane-height landing cell.
                            landing.Add(ClampNestToWorld(new Rect(il.x, il.yMin, gapMainH, il.height)));
                        }
                    }
                }

                Rect outline;
                if (outlineOverride.HasValue)
                {
                    outline = outlineOverride.Value; // already offset by `no`
                }
                else
                {
                    outline = bestNest.Body;
                    outline.position += no;
                }
                if (expands) outline.height += vertGap;
                if (paneClamp.HasValue) outline = ClampVisualX(outline, paneClamp.Value.clamp);

                // Thin context outline: the container around a cell-fill "+" target, or the frame
                // around a pane target — the bold highlight says WHERE, this says INSIDE WHAT.
                Rect context = Rect.zero;
                bool contextIsContainer = false;
                if (bestNest.ContainerBody != Rect.zero && bestNest.ContainerBody != bestNest.Body)
                {
                    context = bestNest.ContainerBody;
                    context.position += no;
                    if (paneClamp.HasValue) context = ClampVisualX(context, paneClamp.Value.clamp);
                    contextIsContainer = true;
                }
                else if (paneClamp.HasValue)
                {
                    context = paneClamp.Value.frameRect;
                }

                // Overflow/empty-horizontal growth rides the container's VISIBLE box (safe for
                // horizontal targets — rows keep their height, space opens below the cell band);
                // the classic vertical append keeps its measured expand host + stacked gap.
                if (expands && vertGap <= 0.5f) expands = false; // hint band absorbs the whole stack
                // The cap prediction owns the expansion when it ran: the cards land in a NEW
                // COLUMN, so the classic stacked-append gap must never open — only the (usually
                // zero) taller-than-the-box shortfall it computed.
                if (rcPredicted) expands = false;
                VisualElement expandEl = expands ? bestNest.ExpandHost : null;
                float expandTo = expands ? bestNest.ExpandHostHeight + vertGap : 0f;
                float gapMain = expands ? vertGap : eff;
                List<VisualElement> expandPush = expands ? bestNest.LayoutPush : null;
                if (!expands && overflowGrow && overflowGap > 0.5f)
                {
                    bool useGrowHost = bestNest.CellRects != null && bestNest.GrowHost != null;
                    expandEl = useGrowHost ? bestNest.GrowHost : bestNest.ExpandHost;
                    expandTo = (useGrowHost ? bestNest.GrowHostHeight : bestNest.ExpandHostHeight) + overflowGap;
                    gapMain = overflowGap;
                    expandPush = bestNest.LayoutPush;
                    // The container's box genuinely grows for the overflow, so the thin "inside
                    // THIS container" outline must wrap the overflow cells too — frozen at the
                    // snapshot height it stopped short and the overflow rects floated outside it.
                    if (contextIsContainer) context.height += overflowGap;
                }

                // Ring honesty: whichever ring stands for the CONTAINER (the thin context ring
                // when the bold ring is a "+" cell, else the bold ring itself) must wrap EVERY
                // cell the preview draws — landing fills and phantom "+" pads alike. Predicted
                // cells (cap splits, reserved-track stacks, grid reflows) can extend past the
                // frozen snapshot box, and a ring that stops short reads as "these cells land
                // OUTSIDE the container".
                Rect outlineWorld = DropSnapshotBuilder.ContentToWorld(frame, outline);
                Rect contextWorld = context == Rect.zero
                    ? Rect.zero : DropSnapshotBuilder.ContentToWorld(frame, context);
                if (!bestNest.IsTabButton && (landing.Count > 0 || phantomCells != null))
                {
                    bool hasU = false;
                    Rect u = Rect.zero;
                    void GrowU(Rect r)
                    {
                        u = hasU
                            ? Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin),
                                Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax))
                            : r;
                        hasU = true;
                    }
                    foreach (var c in landing) GrowU(c);
                    if (phantomCells != null) foreach (var c in phantomCells) GrowU(c);
                    u.yMax += 5f; // hug below the last cell like the body's own bottom inset
                    if (contextIsContainer)
                        contextWorld = Rect.MinMaxRect(
                            Mathf.Min(contextWorld.xMin, u.xMin), Mathf.Min(contextWorld.yMin, u.yMin),
                            Mathf.Max(contextWorld.xMax, u.xMax), Mathf.Max(contextWorld.yMax, u.yMax));
                    else if (bestNest.ContainerBody == Rect.zero || bestNest.ContainerBody == bestNest.Body)
                        outlineWorld = Rect.MinMaxRect(
                            Mathf.Min(outlineWorld.xMin, u.xMin), Mathf.Min(outlineWorld.yMin, u.yMin),
                            Mathf.Max(outlineWorld.xMax, u.xMax), Mathf.Max(outlineWorld.yMax, u.yMax));
                }

                return new ResolvedDrop
                {
                    State = ResolvedDrop.DropState.Nest,
                    TargetParent = bestNest.Node,
                    Slot = bestNest.AppendIndex,
                    CellTrack = bestNest.IsCell,
                    LineWorld = line,
                    LineVertical = lineVertical,
                    OutlineWorld = outlineWorld,
                    OutlineRadius = bestNest.OutlineRadius,
                    GhostWidth = bestNest.IsTabButton ? 0f : (ghostW > 0f ? ghostW : outline.width),
                    ContextOutlineWorld = contextWorld,
                    PhantomCellsWorld = phantomCells,
                    LandingCellsWorld = landing.Count > 0 ? landing.ToArray() : null,
                    NestTint = true,
                    ExpandEl = expandEl,
                    ExpandTo = expandTo,
                    ExpandPush = expandPush,
                    GapMain = gapMain,
                    // Multi-line append preview only for vertical stacks (horizontal interior line).
                    LineOffsets = (!lineVertical && line != Rect.zero)
                        ? profile.LineOffsets(bestNest.ChildSpacingV, bestNest.HasChildSpacing)
                        : null
                };
            }

            return ResolvedDrop.None;
        }

        private static bool MatchesCurrentGap(ResolvedDrop current, DropSlot s)
        {
            return current != null
                && current.State == ResolvedDrop.DropState.Gap
                && current.ActiveSlot != null
                && current.ActiveSlot.Parent == s.Parent
                && current.Slot == s.Index
                && current.RowGroup == s.RowGroup
                && current.ActiveSlot.ParentEl == s.ParentEl;
        }

        // Squared distance from a point to a rect (0 inside). Squared is enough — it's only ever compared
        // against other distances and against the squared hysteresis margin.
        private static float SqrDistToRect(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return dx * dx + dy * dy;
        }

        private static float DistToOuterEdge(CardRect card, Rect visualRect, Vector2 p)
        {
            bool horizParent = card.Parent != null &&
                (card.Parent.Type == ContainerType.Columns || card.Parent.Type == ContainerType.Grid);
            return horizParent
                ? Mathf.Min(p.x - visualRect.xMin, visualRect.xMax - p.x)
                : Mathf.Min(p.y - visualRect.yMin, visualRect.yMax - p.y);
        }

        // ------------------------------------------------------------ hierarchy (indent reparenting)

        private static ResolvedDrop ResolveHierarchy(
            RegionSnapshot region, Vector2 p, float depthProbeX, bool isAdd, bool draggedInTemplate, ResolvedDrop current)
        {
            var rows = region.HierRows;
            if (rows == null || rows.Count == 0) return ResolvedDrop.None;
            var frame = region.Frame;

            // A. Middle-band nest on a container row (the one surviving hover band — Unity's
            // "drop on folder"), with band hysteresis so the edge doesn't flicker. Dragged rows are
            // pure geometry: their gaps work, but they never nest.
            HierRowSnap hover = null;
            foreach (var r in rows)
                if (p.y >= r.Rect.yMin && p.y < r.Rect.yMax) { hover = r; break; }

            if (hover != null && !hover.Dragged && CanNestRow(hover, isAdd, draggedInTemplate))
            {
                float t = (p.y - hover.Rect.yMin) / Mathf.Max(1f, hover.Rect.height);
                bool wasNesting = current != null &&
                                  current.State == ResolvedDrop.DropState.HierNest &&
                                  current.TargetParent == hover.Node;
                float band = wasNesting ? DragTuning.HierBandStay : DragTuning.HierBandEnter;
                if (t > band && t < 1f - band)
                {
                    int slot = hover.Node is ContainerNode hc ? (hc.Children?.Count ?? 0) : -1;
                    return new ResolvedDrop
                    {
                        State = ResolvedDrop.DropState.HierNest,
                        TargetParent = hover.Node,
                        Slot = slot,
                        OutlineWorld = DropSnapshotBuilder.ContentToWorld(frame, hover.Rect),
                        NestTint = true
                    };
                }
            }

            // B. Boundary insertion between rows; horizontal cursor position chooses the ancestor depth.
            HierRowSnap next = null, prev = null;
            int nextIdx = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Rect.center.y > p.y) { next = rows[i]; nextIdx = i; break; }
            }
            prev = next != null ? (nextIdx > 0 ? rows[nextIdx - 1] : null) : rows[rows.Count - 1];

            var rootRow = rows[0];
            if (prev == null)
            {
                // Above the first row: first child of root (line draws just under the root row).
                if (!(rootRow.Node is ContainerNode rootC)) return ResolvedDrop.None;
                if (draggedInTemplate && !isAdd) return ResolvedDrop.None;
                return EmitHierLine(region, rootC, 0, 1, rootRow.Rect.yMax, rootRow);
            }

            int maxDepth = (!prev.Dragged && prev.Node is ContainerNode pc && pc.Type != ContainerType.Tabs &&
                            !IsSplitFrame(pc) && TemplateOk(prev, isAdd, draggedInTemplate))
                ? prev.Depth + 1 : prev.Depth;
            int minDepth = next != null ? Mathf.Max(1, next.Depth) : 1;
            if (minDepth > maxDepth) minDepth = maxDepth;

            float gapY = next != null ? (prev.Rect.yMax + next.Rect.yMin) * 0.5f : prev.Rect.yMax;
            float chosenRaw = (depthProbeX - region.HierBaseX) / DragTuning.HierIndentUnit;

            // Depth-step deadzone: while parked at the same gap, the chosen depth only changes when
            // the cursor clearly crosses a depth boundary.
            int chosen;
            float dz = DragTuning.HierDepthDeadzonePx / DragTuning.HierIndentUnit;
            bool sameGap = current != null &&
                           current.State == ResolvedDrop.DropState.HierLine &&
                           current.HierDepth >= 0 &&
                           Mathf.Abs(current.HierGapY - gapY) < 0.5f;
            if (sameGap)
            {
                chosen = current.HierDepth;
                if (chosenRaw > chosen + 0.5f + dz || chosenRaw < chosen - 0.5f - dz)
                    chosen = Mathf.RoundToInt(chosenRaw);
            }
            else
            {
                chosen = Mathf.RoundToInt(chosenRaw);
            }
            chosen = Mathf.Clamp(chosen, minDepth, maxDepth);

            // Resolve parent + slot for the chosen depth; step shallower while invalid.
            for (int depth = chosen; depth >= minDepth; depth--)
            {
                if (TryResolveHierTarget(rows, prev, depth, isAdd, draggedInTemplate,
                        out ContainerNode target, out int slot, out HierRowSnap targetRow))
                {
                    return EmitHierLine(region, target, slot, depth, gapY, targetRow);
                }
            }
            return ResolvedDrop.None;
        }

        private static bool CanNestRow(HierRowSnap row, bool isAdd, bool draggedInTemplate)
        {
            if (isAdd)
                return (row.Node is ContainerNode c && c.Type != ContainerType.Tabs && !IsSplitFrame(c)) || row.Node is CollectionNode;
            if (row.Node is ContainerNode cn && cn.Type != ContainerType.Tabs && !IsSplitFrame(cn))
                return row.InItemTemplate == draggedInTemplate;
            return false;
        }

        // A split Frame manages its two pane containers itself (the Tabs precedent): nothing nests
        // directly into the frame — drops go into the panes, which are ordinary nestable containers.
        private static bool IsSplitFrame(ContainerNode c)
            => c.Type == ContainerType.Frame && c.Frame.Shape == FrameShape.DiagonalSplit;

        // True when `node` is a split Frame or lives anywhere inside one. Everything under a split frame
        // reports FULL-FRAME geometry (the counter-rotated clipper construction), so its indicators only
        // make sense once clamped to a seam side — which is knowledge the cell path does not have.
        private static bool IsUnderSplitFrame(RegionSnapshot region, ContainerNode node)
        {
            var cur = node;
            for (int guard = 0; cur != null && guard < 64; guard++)
            {
                if (IsSplitFrame(cur)) return true;
                ContainerNode parent = null;
                foreach (var c in region.Cards)
                    if (ReferenceEquals(c.Node, cur)) { parent = c.Parent; break; }
                cur = parent;
            }
            return false;
        }

        /// <summary>True when the context card IS a split frame or one of its pane cards; returns the
        /// frame's own card + node so the canvas dispatch can resolve by seam side.</summary>
        private static bool TryGetSplitFrameContext(
            RegionSnapshot region, CardRect ctx, out CardRect frameCard, out ContainerNode splitFrame)
        {
            frameCard = null;
            splitFrame = null;
            if (ctx.Node is ContainerNode own && IsSplitFrame(own))
            {
                frameCard = ctx;
                splitFrame = own;
                return true;
            }
            if (ctx.Parent != null && IsSplitFrame(ctx.Parent))
            {
                splitFrame = ctx.Parent;
                foreach (var c in region.Cards)
                    if (ReferenceEquals(c.Node, ctx.Parent)) { frameCard = c; break; }
                return frameCard != null;
            }
            return false;
        }

        private static bool TemplateOk(HierRowSnap row, bool isAdd, bool draggedInTemplate)
            => isAdd || row.InItemTemplate == draggedInTemplate;

        private static bool TryResolveHierTarget(
            List<HierRowSnap> rows, HierRowSnap prev, int depth, bool isAdd, bool draggedInTemplate,
            out ContainerNode target, out int slot, out HierRowSnap targetRow)
        {
            target = null; slot = 0; targetRow = null;

            if (depth == prev.Depth + 1)
            {
                // Nest into prev (the line sits one indent level under it). Tabs and split Frames never
                // take children via the line (their children are the tabs / panes themselves).
                if (prev.Dragged) return false;
                if (!(prev.Node is ContainerNode pc) || pc.Type == ContainerType.Tabs || IsSplitFrame(pc)) return false;
                if (!TemplateOk(prev, isAdd, draggedInTemplate)) return false;
                target = pc;
                slot = prev.Collapsed ? (pc.Children?.Count ?? 0) : 0;
                targetRow = prev;
                return true;
            }

            // Climb from prev to the ancestor at the chosen depth; insert after it in its parent.
            var a = prev;
            while (a != null && a.Depth > depth) a = FindRow(rows, a.Parent);
            if (a == null || a.Depth != depth) return false;
            if (a.Parent == null || a.Parent.Type == ContainerType.Tabs || IsSplitFrame(a.Parent)) return false;
            if (a.IndexInParent < 0) return false;

            targetRow = FindRow(rows, a.Parent);
            if (targetRow == null || targetRow.Dragged) return false;
            if (!TemplateOk(targetRow, isAdd, draggedInTemplate)) return false;

            target = a.Parent;
            slot = a.IndexInParent + 1;
            return true;
        }

        private static HierRowSnap FindRow(List<HierRowSnap> rows, LayoutNode node)
        {
            if (node == null) return null;
            foreach (var r in rows) if (r.Node == node) return r;
            return null;
        }

        private static ResolvedDrop EmitHierLine(
            RegionSnapshot region, ContainerNode target, int slot, int depth, float gapY, HierRowSnap targetRow)
        {
            var frame = region.Frame;
            float x = region.HierBaseX + depth * DragTuning.HierIndentUnit;
            var line = new Rect(x, gapY - 1f, Mathf.Max(20f, region.HierRightX - x), 2f);
            return new ResolvedDrop
            {
                State = ResolvedDrop.DropState.HierLine,
                TargetParent = target,
                Slot = slot,
                LineWorld = DropSnapshotBuilder.ContentToWorld(frame, line),
                LineVertical = false,
                OutlineWorld = targetRow != null
                    ? DropSnapshotBuilder.ContentToWorld(frame, targetRow.Rect)
                    : Rect.zero,
                // The outline here only INDICATES the receiving ancestor (the line is the real
                // signal), so it draws thin — unlike the direct middle-band nest, which stays bold.
                ThinOutline = true,
                HierDepth = depth,
                HierGapY = gapY
            };
        }
    }
}
