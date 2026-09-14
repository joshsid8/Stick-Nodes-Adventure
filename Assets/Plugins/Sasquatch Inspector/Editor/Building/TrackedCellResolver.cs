using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The drop model for TRACKED containers — a Columns/Rows with an explicit track count, or a
    /// fixed-constraint Grid. Everything else (untracked stacks, Flexible grids, Cards, Foldouts, Frames,
    /// Tabs) keeps the slot/nest resolver, which is the right model for it.
    ///
    /// WHY: a drop model should match its container's declared semantics, and there are two families.
    /// An UNBOUNDED flow (a plain Rows/Columns, a kanban column, a Figma auto-layout frame) has open
    /// capacity, so the only thing you are choosing is ORDER — an insertion line between items is right.
    /// A BOUNDED grid (an explicit track count; CSS Grid with fixed tracks, Webflow/Framer grids,
    /// gridstack, a phone home screen) has DECLARED capacity, so you are choosing a CELL — and "between
    /// cells" is not a concept that exists in it.
    ///
    /// Typing "3" into Rows moves a container from the first family to the second, but the existing
    /// resolver keeps offering family-one semantics inside it: measured over a 3-track container holding
    /// two open cells and a button, ~80% of the interior resolved to a gap-insert that GREW the container
    /// by a row — so a container the user pinned to 3 tracks previewed as 4, and the drop could overflow
    /// into a second column. Even the dead centre of an open "＋" cell resolved to a gap rather than to
    /// the cell.
    ///
    /// THE MODEL:
    ///   1. The interior is partitioned across the declared tracks by nearest track centre, so every
    ///      interior pixel maps to exactly one track — no dead zones and no threshold flicker.
    ///   2. EVERY track is a target, occupied or not. Landing on an occupied one inserts there and pushes
    ///      the occupant along (the capacity overflow stays visible rather than silent).
    ///   3. The container NEVER grows on hover. The tracks were declared; previewing a fourth is a lie.
    ///   4. A margin band around the interior means "not in here — beside it", and escapes to the sibling
    ///      insert around the container. The band runs along the PARENT's axis, because "beside" is only
    ///      expressible in the parent's flow direction.
    /// </summary>
    internal static class TrackedCellResolver
    {
        // The escape band is PURELY proportional, with a cap and deliberately NO minimum.
        //
        // A fixed floor looks harmless on a tall Rows container and is catastrophic on a short one: a
        // Columns container is a single row of cells maybe 40px tall, so a 14px floor on each edge left
        // ~12px of interior and handed 70% of the container to "drop beside me" — measured, not guessed.
        // Below the point where a band would be meaningful there simply isn't one: moving the pointer OFF
        // the container already resolves to the parent, so escape is never actually lost.
        internal const float EscapeBandMax = 22f;
        internal const float EscapeBandFraction = 0.10f;
        // A floor so the band stays aimable on a SHORT container — a Columns row is ~40px tall, where a
        // pure 10% band is ~4px and the append gesture is a coin flip. The floor is itself capped as a
        // FRACTION (below), because a fixed floor with no cap is what handed 70% of a short container to
        // the escape band before. Cheap to be generous here: these bands run along the CROSS axis, which
        // the tracks do not divide, so widening them costs no track reachability at all.
        internal const float BandMinPx = 8f;
        internal const float BandMaxFraction = 0.22f;

        /// <summary>Distinct indicator marks a preview may draw before it collapses to a summary.</summary>
        internal const int MaxSimultaneousMarks = 8;
        /// <summary>Context "＋" pads kept once a preview is over budget.</summary>
        internal const int MaxContextPads = 2;

        /// <summary>Pointer is in the escape band — the drop belongs BESIDE this container, not in it.</summary>
        internal const int TrackEscape = -1;
        /// <summary>Pointer is under the container — append into it (a new row/column if it is full).</summary>
        internal const int TrackAppend = -2;

        /// <summary>The track the pointer is over, or <see cref="TrackEscape"/>/<see cref="TrackAppend"/>.</summary>
        internal static int PickTrack(TrackedBox box, Vector2 p, Vector2 offset)
        {
            var body = box.Body;
            body.position += offset;

            // The bands run along the container's CROSS axis — the one its tracks do NOT divide. A Rows
            // container stacks vertically, so its left/right margins belong to nobody; its top and bottom
            // are track 0 and track N, and a band there would swallow them (measuring against the PARENT's
            // axis did exactly that, making the first and last track unreachable). Columns/Grid are the
            // transpose: tracks run across, so top/bottom are the free margins.
            //
            // The two margins do NOT mean the same thing. Dragging past the END of a container reads as
            // "add this to it" — for a Rows stack that gesture is already the last TRACK, so it just
            // works; for a horizontal container the same downward drift landed in the cross-axis band and
            // meant "put it beside me" instead, which is why appending to a full grid felt wrong. So the
            // trailing margin appends and only the leading one escapes.
            float extent = box.Horizontal ? body.height : body.width;
            float band = Mathf.Min(
                Mathf.Min(Mathf.Max(extent * EscapeBandFraction, BandMinPx), EscapeBandMax),
                extent * BandMaxFraction);
            float toLeading = box.Horizontal ? p.y - body.yMin : p.x - body.xMin;
            float toTrailing = box.Horizontal ? body.yMax - p.y : body.xMax - p.x;

            if (toLeading <= band) return TrackEscape;
            // Only a horizontal container grows along this axis, so only it can append here. A vertical
            // container's trailing margin is its RIGHT edge, which stays "beside me" as designed.
            if (toTrailing <= band) return box.Horizontal ? TrackAppend : TrackEscape;

            // Nearest track centre. A pure partition: every remaining pixel belongs to exactly one track.
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < box.Tracks.Count; i++)
            {
                var t = box.Tracks[i];
                t.position += offset;
                float d = (t.center - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Resolves the pointer against a tracked container. Returns null when the pointer is in the escape
        /// band, which means "let the caller place this BESIDE the container" — the existing sibling-gap
        /// path handles that unchanged.
        /// </summary>
        internal static ResolvedDrop Resolve(
            RegionSnapshot region, TrackedBox box, Vector2 p, in DragGapProfile profile,
            ResolvedDrop current, Vector2 offset)
        {
            int track = PickTrack(box, p, offset);
            if (track == TrackEscape) return null;
            if (track == TrackAppend) track = AppendTrackOf(box);

            // Hysteresis: once a track is showing it keeps a small edge, so a pointer wobbling on a track
            // boundary can't strobe between neighbours.
            if (current != null && current.State == ResolvedDrop.DropState.Nest && current.CellTrack
                && ReferenceEquals(current.TargetParent, box.Node) && current.Slot != track)
            {
                int held = current.Slot;
                if (held >= 0 && held < box.Tracks.Count)
                {
                    var heldRect = box.Tracks[held];
                    heldRect.position += offset;
                    var pickRect = box.Tracks[track];
                    pickRect.position += offset;
                    float dHeld = (heldRect.center - p).sqrMagnitude;
                    float dPick = (pickRect.center - p).sqrMagnitude;
                    if (dHeld - dPick < DragTuning.HysteresisPx * DragTuning.HysteresisPx) track = held;
                }
            }

            var frame = region.Frame;
            var plan = Predict(box, track, profile.Count, offset);

            Rect ToWorld(Rect r) => DropSnapshotBuilder.ContentToWorld(frame, r);
            Rect[] ToWorldAll(List<Rect> rs)
            {
                if (rs.Count == 0) return null;
                var outArr = new Rect[rs.Count];
                for (int i = 0; i < rs.Count; i++) outArr[i] = ToWorld(rs[i]);
                return outArr;
            }

            // MARK BUDGET. Past roughly seven distinct marks a preview stops being read and starts being
            // decoration, and a big multi-drag blew straight through that: seven cards into a three-track
            // Rows drew eleven. Over budget, the landing cells collapse to ONE rect per column/row with a
            // count on it — which is honest (each rect covers exactly the contiguous run of tracks that
            // group receives) and says the useful thing louder: "three go in this column, three in the
            // next, one in the last". The context pads are trimmed too; they are the least load-bearing
            // mark on screen. Under budget nothing changes, so ordinary one- and two-card drags keep the
            // per-cell preview that is already clear.
            var landing = plan.Landing;
            var landingGroups = plan.LandingGroups;
            var pads = plan.Pads;
            int[] landingCounts = null;
            if (landing.Count + pads.Count + 2 > MaxSimultaneousMarks)
            {
                var merged = new List<Rect>();
                var counts = new List<int>();
                for (int i = 0; i < landing.Count; i++)
                {
                    if (merged.Count > 0 && landingGroups[i] == landingGroups[i - 1])
                    {
                        var u = merged[merged.Count - 1];
                        merged[merged.Count - 1] = Rect.MinMaxRect(
                            Mathf.Min(u.xMin, landing[i].xMin), Mathf.Min(u.yMin, landing[i].yMin),
                            Mathf.Max(u.xMax, landing[i].xMax), Mathf.Max(u.yMax, landing[i].yMax));
                        counts[counts.Count - 1]++;
                        continue;
                    }
                    merged.Add(landing[i]);
                    counts.Add(1);
                }
                landing = merged;
                landingCounts = counts.ToArray();
                if (pads.Count > MaxContextPads) pads = pads.GetRange(0, MaxContextPads);
            }

            // A pure REORDER keeps every track's size, so the honest preview is the real thing: slide the
            // actual cards to the tracks they will occupy. Translation is a paint-time transform (the same
            // mechanism GapAnimator already uses for gap slots) — it never invalidates layout, so no bound
            // widget re-measures and none of the label-column / min-height machinery wakes up.
            //
            // A SPLIT is deliberately NOT animated this way. It resizes every cell, and resizing live
            // bound widgets mid-drag is the expensive, fragile half — that case keeps the overlay.
            List<VisualElement> reflowEls = null;
            List<Vector2> reflowOffsets = null;
            if (plan.PureReorder && plan.Moves.Count > 0)
            {
                reflowEls = new List<VisualElement>(plan.Moves.Count);
                reflowOffsets = new List<Vector2>(plan.Moves.Count);
                foreach (var (fromTrack, to) in plan.Moves)
                {
                    if (fromTrack < 0 || fromTrack >= box.TrackEls.Count) continue;
                    var el = box.TrackEls[fromTrack];
                    if (el == null) continue;
                    var from = box.Tracks[fromTrack];
                    from.position += offset;
                    reflowEls.Add(el);
                    reflowOffsets.Add(to.position - from.position);
                }
                if (reflowEls.Count == 0) { reflowEls = null; reflowOffsets = null; }
            }

            return new ResolvedDrop
            {
                State = ResolvedDrop.DropState.Nest,
                TargetParent = box.Node,
                Slot = track,           // a TRACK ORDINAL — never re-based; see ResolvedDrop.CellTrack
                CellTrack = true,
                NestTint = true,
                LineWorld = Rect.zero,  // the highlighted cells ARE the message
                OutlineWorld = ToWorld(landing.Count > 0 ? landing[0] : plan.Bounds),
                OutlineRadius = box.OutlineRadius,
                // The context ring covers the PREDICTED bounds, so when the drop adds a column the ring
                // visibly widens to enclose it — the container is seen to grow before you let go.
                ContextOutlineWorld = ToWorld(plan.Bounds),
                LandingCellsWorld = ToWorldAll(landing),
                LandingCounts = landingCounts,
                // The cards themselves carry the "what else moves" message when they can actually move;
                // the amber overlay is the fallback for the split, where they cannot.
                DisplacedCellsWorld = reflowEls != null ? null : ToWorldAll(plan.Displaced),
                ReflowEls = reflowEls,
                ReflowOffsets = reflowOffsets,
                PhantomCellsWorld = ToWorldAll(pads),
                GhostWidth = plan.Landing.Count > 0 ? plan.Landing[0].width : 0f,
                // Rule 3: a declared track count is a promise. Never spring the box open on hover — the
                // predicted geometry above already shows the real consequence.
                ExpandEl = null,
                ExpandTo = 0f,
                ExpandPush = null,
                GapMain = 0f
            };
        }

        /// <summary>The predicted post-drop arrangement: where the held cards land, which existing cards
        /// are pushed and to where, the leftover open tracks, and the container's resulting bounds.</summary>
        internal struct Plan
        {
            public List<Rect> Landing;    // one per held card, at its post-drop track
            public List<int> LandingGroups; // parallel: which column/row each landing cell falls in
            public List<Rect> Displaced;  // existing cards whose track CHANGES because of the drop
            public List<Rect> Pads;       // open tracks left over, including the new group's tail
            public Rect Bounds;           // the container's box after the drop (grows by a column/row)
            // (original track -> post-drop rect) for every existing card that moves. When the drop does
            // not change the cell GEOMETRY these become real translations; see Resolve.
            public List<(int fromTrack, Rect to)> Moves;
            // True when the drop keeps the container's current group count — i.e. it is a pure REORDER
            // within the existing tracks, so every track keeps its size and the real cards can simply
            // slide. False when the drop splits the container, which resizes every cell.
            public bool PureReorder;
        }

        // Mirrors EmptyCellFill.InsertAtCell exactly — replace an OPEN track, otherwise insert and push —
        // then lays the resulting sequence out over as many groups (columns for Rows, rows for
        // Columns/Grid) as the declared cap needs. Preview and commit must agree or the preview is a lie.
        internal static Plan Predict(TrackedBox box, int target, int heldCount, Vector2 offset)
        {
            int cap = Mathf.Max(1, box.Node.GridColumns);

            // 1. The final occupancy sequence. -1 = open, -2 = a held card, >=0 = the original track it
            //    came from (so a move can be spotted by comparing final position to origin).
            var seq = new List<int>(box.Tracks.Count + heldCount);
            for (int i = 0; i < box.Tracks.Count; i++) seq.Add(box.IsOpen(i) ? -1 : i);
            for (int h = 0; h < heldCount; h++)
            {
                int at = target + h;
                if (at < seq.Count && seq[at] == -1) seq[at] = -2;      // consume the open track
                else if (at < seq.Count) seq.Insert(at, -2);           // insert and push the rest along
                else { while (seq.Count < at) seq.Add(-1); seq.Add(-2); }
            }

            // 1b. Mirror the trailing trim the commit performs (EmptyCellFill.Normalize): an open track
            //     with nothing after it holds nothing, so the push a drop causes is ABSORBED by the free
            //     tracks below rather than overflowing. Without this, dropping onto the occupied first
            //     track of a half-empty container previewed a column split that the commit never
            //     produces — the preview promising a reflow that then doesn't happen is as bad as one
            //     that hides a reflow that does.
            while (seq.Count > 0 && seq[seq.Count - 1] == -1) seq.RemoveAt(seq.Count - 1);

            // 2. Geometry. Tracks run along the primary axis within a group; extra groups are laid out
            //    along the cross axis — a capped Rows gains COLUMNS to its right, a capped Columns/Grid
            //    gains ROWS below. Both reuse the existing tracks' primary-axis positions so the
            //    prediction lines up with the container that is actually on screen.
            int groups = Mathf.Max(1, Mathf.CeilToInt(seq.Count / (float)cap));
            // Fill the last group's tail with open tracks so the new column/row is drawn COMPLETE. The
            // trim above already decided how many groups there are (that is the honest question); this
            // only says what the final group looks like, and after the drop the regroup really does
            // render "＋" pads there. Without it a drop that creates a column previewed the cards landing
            // in mid-air with no column around them.
            while (seq.Count < groups * cap) seq.Add(-1);
            float modelGap = Mathf.Max(2f, box.Node.Gap > 0f ? box.Node.Gap : 6f);

            float bandMin = float.MaxValue, bandMax = float.MinValue;
            foreach (var t in box.Tracks)
            {
                bandMin = Mathf.Min(bandMin, box.Horizontal ? t.yMin : t.xMin);
                bandMax = Mathf.Max(bandMax, box.Horizontal ? t.yMax : t.xMax);
            }
            // Cross-axis extent of one group after the split: the band divided among the groups (a Rows
            // container's columns share its width) — for Columns/Grid the new rows simply stack, each as
            // tall as the existing row.
            float groupExtent = box.Horizontal
                ? (bandMax - bandMin)
                : ((bandMax - bandMin) - (groups - 1) * modelGap) / groups;

            Rect TrackRect(int index)
            {
                int g = index / cap, j = index % cap;
                var baseline = box.Tracks[Mathf.Min(j, box.Tracks.Count - 1)];
                baseline.position += offset;
                if (box.Horizontal)
                {
                    // Tracks run left-to-right; group g is a new ROW below.
                    float rowH = baseline.height;
                    return new Rect(baseline.x, bandMin + offset.y + g * (rowH + modelGap), baseline.width, rowH);
                }
                // Tracks run top-to-bottom; group g is a new COLUMN to the right.
                return new Rect(bandMin + offset.x + g * (groupExtent + modelGap), baseline.y, groupExtent, baseline.height);
            }

            int groupsNow = Mathf.Max(1, Mathf.CeilToInt(box.Tracks.Count / (float)cap));
            var plan = new Plan
            {
                Landing = new List<Rect>(),
                LandingGroups = new List<int>(),
                Displaced = new List<Rect>(),
                Pads = new List<Rect>(),
                Moves = new List<(int, Rect)>(),
                PureReorder = groups == groupsNow
            };
            bool hasBounds = false;
            Rect bounds = default;
            void Grow(Rect r)
            {
                bounds = hasBounds
                    ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                        Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax))
                    : r;
                hasBounds = true;
            }

            for (int i = 0; i < seq.Count; i++)
            {
                var r = TrackRect(i);
                Grow(r);
                if (seq[i] == -2) { plan.Landing.Add(r); plan.LandingGroups.Add(i / cap); }
                else if (seq[i] == -1) plan.Pads.Add(r);
                else if (seq[i] != i)
                {
                    plan.Displaced.Add(r);           // overlay form (used when the cells resize)
                    plan.Moves.Add((seq[i], r));     // motion form (used when they don't)
                }
            }

            var body = box.Body;
            body.position += offset;
            Grow(body); // never shrink below the container that is already on screen
            plan.Bounds = hasBounds ? bounds : body;
            return plan;
        }

        // Where an APPEND lands: the first open track, or one past the end when every track is taken (which
        // is what makes the container reflow into a new row/column). Deliberately the first OPEN track and
        // not the end of the list — appending into a container that still has a "＋" showing should fill
        // that "＋", never conjure a row while an opening sits there unused.
        internal static int AppendTrackOf(TrackedBox box)
        {
            for (int i = 0; i < box.Tracks.Count; i++)
                if (box.IsOpen(i)) return i;
            return box.Tracks.Count;
        }

        /// <summary>The tracked box the pointer should resolve against, or null when none applies.</summary>
        internal static TrackedBox FindBox(RegionSnapshot region, CardRect ctx)
        {
            if (ctx == null) return null;
            // Over the container's own body, or over one of its children (a card inside a track).
            var owner = ctx.Node as ContainerNode;
            if (owner == null || !EmptyCellFill.IsTrackedCellContainer(owner)) owner = ctx.Parent;
            if (owner == null || !EmptyCellFill.IsTrackedCellContainer(owner)) return null;
            foreach (var b in region.TrackedBoxes)
                if (ReferenceEquals(b.Node, owner)) return b;
            return null;
        }
    }
}
