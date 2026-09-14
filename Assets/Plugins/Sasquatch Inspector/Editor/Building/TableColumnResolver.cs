using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The drop model for TABLE COLUMNS on the designer canvas — reordering a table's columns by dragging
    /// the binding card in its cell.
    ///
    /// WHY ITS OWN MODEL. The generic slot resolver answers this correctly but previews it badly: it opens a
    /// fixed 28px sliver between two cells and slides everything after it by that same 28px. A column is not
    /// 28px wide, the sliver is not where the column lands, and the header — the half of the column you were
    /// actually reading — never moves at all. The preview said "something goes near here"; the question being
    /// asked is "which column ends up where".
    ///
    /// THE MODEL. Column widths are flex weights that travel WITH the binding node, and a reorder neither
    /// adds nor removes a column, so the post-drop layout is a pure PERMUTATION of the widths already on
    /// screen. Nothing resizes; every column simply belongs at a different x. That is the one case where the
    /// honest preview is the real thing rather than a drawing of it (<see cref="TrackedCellResolver"/> makes
    /// the same call for a tracked container's pure reorder): each column's two halves — header and cell —
    /// slide to the exact x they will occupy, and the empty cell of the column in hand travels with them, so
    /// the gap you see IS the slot the column drops into.
    ///
    /// Translation is a paint-time transform, so no bound widget re-measures and none of the label-column /
    /// min-height machinery wakes up mid-drag. The USS translate transition on the cells does the gliding.
    ///
    /// TARGET PICKING is against the FROZEN column centres, never the translated ones: the mapping from
    /// pointer-x to insertion index then stays monotonic and stable no matter how far the cards have already
    /// slid, which is what stops the "columns strobe between two arrangements" failure that reading live
    /// geometry would cause.
    /// </summary>
    internal static class TableColumnResolver
    {
        /// <summary>Vertical slack around the header/row band that still counts as "over the columns".</summary>
        internal const float BandPadY = 6f;

        /// <summary>
        /// How far outside the column the landing ring is drawn. A column header's label starts flush against
        /// its cell's left edge, so a ring drawn exactly ON that edge paints over the first glyph — measured:
        /// "Item Name" read as "tem Name" for the whole drag. The divider lane between columns is wider than
        /// this, so the ring sits in the gap rather than on either neighbour.
        /// </summary>
        internal const float LandingRingInset = 2f;

        /// <summary>The band the pointer is over, or null.</summary>
        internal static TableColumnBand FindBand(RegionSnapshot region, Vector2 p)
        {
            if (region?.TableColumnBands == null) return null;
            TableColumnBand best = null;
            float bestArea = float.MaxValue;
            foreach (var band in region.TableColumnBands)
            {
                var body = band.Body;
                body.yMin -= BandPadY;
                body.yMax += BandPadY;
                if (!body.Contains(p)) continue;
                float area = body.width * body.height;
                if (area < bestArea) { bestArea = area; best = band; }   // innermost table wins
            }
            return best;
        }

        /// <summary>
        /// Resolves a drop over <paramref name="band"/>. Returns a Gap-state drop whose Slot is an ordinary
        /// MODEL INDEX — the same answer the generic slots give, so the commit path is unchanged and the
        /// re-base rule still applies — carrying the column-slide preview instead of an insertion sliver.
        /// </summary>
        /// <param name="isAdd">A new binding from the palette rather than a column being moved.</param>
        /// <param name="addCount">How many new bindings are in hand (add payloads only).</param>
        internal static ResolvedDrop Resolve(
            RegionSnapshot region, TableColumnBand band, Vector2 p, ResolvedDrop current,
            bool isAdd, int addCount)
        {
            if (band == null || band.Columns.Count == 0) return null;

            // The columns that STAY put: everything not in hand. For an add nothing is in hand, so every
            // column stays and the incoming ones are extra.
            var stay = new List<TableColumnSnap>(band.Columns.Count);
            var held = new List<TableColumnSnap>();
            foreach (var col in band.Columns)
            {
                if (!isAdd && col.Dragged) held.Add(col);
                else stay.Add(col);
            }
            // A move that is somehow carrying no column of this table (a card from elsewhere in the same
            // template) is not a column reorder — let the ordinary slot model answer it.
            if (!isAdd && held.Count == 0) return null;

            int incoming = isAdd ? Mathf.Max(1, addCount) : held.Count;

            // ---- 1. Where does it land? ------------------------------------------------------------------
            // Insertion position among the staying columns, by frozen centres: the classic tab-strip
            // partition. Every pixel of the band maps to exactly one position, and the mapping never moves.
            int target = 0;
            for (int i = 0; i < stay.Count; i++)
                if (stay[i].Cell.center.x < p.x) target = i + 1;

            // Hysteresis on the ONE boundary that separates the previous answer from this one, so a pointer
            // resting on a column centre can't strobe between two arrangements.
            int currentTarget = CurrentTargetOf(current, band, stay);
            if (currentTarget >= 0 && currentTarget != target)
            {
                int boundary = Mathf.Min(currentTarget, target);
                if (boundary >= 0 && boundary < stay.Count
                    && Mathf.Abs(p.x - stay[boundary].Cell.center.x) < DragTuning.HysteresisPx)
                    target = currentTarget;
            }

            // ---- 2. The post-drop arrangement ------------------------------------------------------------
            // Widths: a move conserves them exactly (the held column's width is still reserved in the row).
            // An add cannot — a new column takes a share of the same space — so it previews at the width the
            // incoming column will END UP with, and the columns it displaces keep their current widths for
            // the slide. That is a deliberate approximation of WIDTH in exchange for an exact preview of
            // ORDER, which is the thing the gesture is choosing.
            float incomingWidth = 0f;
            if (isAdd)
            {
                float total = 0f;
                foreach (var col in band.Columns) total += col.Cell.width;
                incomingWidth = total / Mathf.Max(1, band.Columns.Count + incoming);
            }

            float x = band.Columns[0].Cell.xMin;
            var landing = new List<Rect>(incoming);
            var reflowEls = new List<VisualElement>();
            var reflowOffsets = new List<Vector2>();

            void Place(TableColumnSnap col)
            {
                float dx = x - col.Cell.xMin;
                if (Mathf.Abs(dx) > 0.5f)
                {
                    // BOTH halves, always together. Sliding the value without its name is worse than not
                    // animating at all — it puts every column under the wrong header for the whole drag.
                    reflowEls.Add(col.CellEl);
                    reflowOffsets.Add(new Vector2(dx, 0f));
                    reflowEls.Add(col.HeaderEl);
                    reflowOffsets.Add(new Vector2(dx, 0f));
                }
                x += col.Cell.width + band.Gap;
            }

            for (int i = 0; i <= stay.Count; i++)
            {
                if (i == target)
                {
                    // The slot itself. For a move the held column's own (empty) cell travels here, so the
                    // hole in the row IS the landing cell; for an add nothing exists yet, so the columns
                    // after it simply part by the incoming width.
                    for (int h = 0; h < incoming; h++)
                    {
                        float w = isAdd ? incomingWidth : held[h].Cell.width;
                        landing.Add(new Rect(x - LandingRingInset, band.Body.yMin,
                            w + LandingRingInset * 2f, band.Body.height));
                        if (!isAdd) Place(held[h]);
                        else x += w + band.Gap;
                    }
                }
                if (i < stay.Count) Place(stay[i]);
            }

            // ---- 3. The model slot -----------------------------------------------------------------------
            // Insert BEFORE the staying column now at `target`, or past the last one. Model indices (not
            // column ordinals) because the item container may hold non-column children too.
            int slot;
            if (stay.Count == 0) slot = band.Columns[0].ModelIndex;
            else if (target < stay.Count) slot = stay[target].ModelIndex;
            else slot = stay[stay.Count - 1].ModelIndex + 1;

            var frame = region.Frame;
            Rect ToWorld(Rect r) => DropSnapshotBuilder.ContentToWorld(frame, r);
            var landingWorld = new Rect[landing.Count];
            for (int i = 0; i < landing.Count; i++) landingWorld[i] = ToWorld(landing[i]);

            return new ResolvedDrop
            {
                State = ResolvedDrop.DropState.Gap,
                TargetParent = band.Node,
                Slot = slot,
                // A MODEL index, so the commit's "an earlier sibling was detached" re-base must run. This is
                // emphatically not a cell TRACK ordinal — see ResolvedDrop.CellTrack.
                CellTrack = false,
                // The full-height landing rect spans header AND row, because a column is both. The insertion
                // line is suppressed for the same reason the tracked model suppresses it: with the real cells
                // parting around the slot, a line drawn in that slot is one mark too many.
                LandingCellsWorld = landingWorld,
                // Zero = draw the slot, but no "＋" glyph in it. A reorder adds nothing, and the rect it
                // draws already encloses the moving column's own header — a plus sign there is both the
                // wrong word and the wrong place. An ADD keeps the glyph, because it IS adding a column.
                LandingCounts = isAdd ? null : ZeroCounts(landingWorld.Length),
                LineWorld = Rect.zero,
                // The house language for a bounded target: the BOLD ring is "lands here" and the thin one is
                // "inside this" (TrackedCellResolver does the same). Ringing the whole table boldly and then
                // filling the slot put two loud blue marks on one small band and made the table itself read as
                // the thing being placed.
                OutlineWorld = landingWorld.Length > 0 ? landingWorld[0] : ToWorld(band.Body),
                ContextOutlineWorld = ToWorld(band.Body),
                // The ghost matches the COLUMN, not the ring drawn around it.
                GhostWidth = landing.Count > 0 ? landing[0].width - LandingRingInset * 2f : 0f,
                // ALWAYS non-null, even when this target moves nothing. An empty list means "this drop owns
                // the slide, and it is showing zero displacement" — which is exactly the state that has to
                // push the PREVIOUS target's columns back home. Returning null here instead made the one
                // target that displaces nothing — dragging the end column back to its own end — leave the
                // whole table frozen in the last arrangement it did displace: the columns never slid back,
                // so the reorder looked stuck and the landing ring read as though it were highlighting
                // whichever column was sitting in that slot.
                ReflowEls = reflowEls,
                ReflowOffsets = reflowOffsets,
                // A reorder never grows anything: no gap to open, no container to spring.
                ActiveSlot = null,
                ExpandEl = null,
                ExpandTo = 0f,
                GapMain = 0f,
            };
        }

        private static int[] ZeroCounts(int n)
        {
            if (n <= 0) return null;
            return new int[n]; // all zeros
        }

        // Which insertion position the CURRENT drop represents, so hysteresis has something to hold onto.
        // Recovered from the committed model slot rather than stored, because the resolver is stateless.
        private static int CurrentTargetOf(ResolvedDrop current, TableColumnBand band, List<TableColumnSnap> stay)
        {
            if (current == null || current.State != ResolvedDrop.DropState.Gap
                || !ReferenceEquals(current.TargetParent, band.Node)) return -1;
            for (int i = 0; i < stay.Count; i++)
                if (stay[i].ModelIndex == current.Slot) return i;
            if (stay.Count > 0 && current.Slot == stay[stay.Count - 1].ModelIndex + 1) return stay.Count;
            return -1;
        }
    }
}
