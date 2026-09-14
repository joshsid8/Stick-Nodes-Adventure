using System.Collections.Generic;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace SasquatchInspector.Editor
{
    // Self-contained drag-to-reorder for collection rows in the LIVE inspector / preview (List & Table).
    // Pointer-capture based, with no dependency on the designer's CanvasDragController, so it works in a
    // real inspector too. The grabbed row floats under the cursor (position:absolute) while a same-height
    // placeholder reflows to the slot under the pointer so the other rows part to make space; on release
    // the floating row springs into that slot (animated settle, lands with a little weight) and then
    // commits via MoveArrayElement. Data is only ever mutated on drop (an explicit end-of-interaction
    // action), never mid-drag.
    /// <summary>
    /// Self-contained drag-to-reorder for collection rows in the live inspector / preview (List &amp; Table).
    /// Pointer-capture based with no dependency on the designer's <see cref="CanvasDragController"/>: the grabbed
    /// row floats while a placeholder reflows, and the data is moved (via MoveArrayElement) only on drop.
    /// </summary>
    public static class CollectionRowReorder
    {
        // USS class LayoutTreeBuilder puts on each row's "[i]" label so the reorder can
        // retext it the moment the user drops (see PreviewIndexLabels below).
        public const string IndexLabelClass = "sasq-row-index";

        private const string SettleKey = "row.top";
        // 0.5px settle epsilon: these are pixel springs, and Commit waits on settle —
        // the default epsilon would defer the data move by ~a second after the drop.
        /// <summary>
        /// How far the pointer must travel from the press before the gesture counts as a DRAG rather than
        /// a click, in world pixels. Small enough that a deliberate nudge still reorders, large enough that
        /// hand tremor on a click does not — the same order of magnitude every OS file browser uses.
        /// </summary>
        private const float DragThreshold = 4f;

        /// <summary>Placeholder's own vertical margin, top + bottom — see <see cref="MakePlaceholder"/>.</summary>
        private const float PlaceholderMargin = 2f;

        private static readonly SpringConfig SettleConfig = new SpringConfig(0.28f, 0.3f, 0.5f);
        private static readonly SpringConfig SlideConfig = new SpringConfig(0.25f, 0.2f, 0.5f);

        // One pending drop-settle finalizer per rows CONTAINER. FinalizePendingSettle
        // lives in a per-row closure, so without this table it only guards re-pressing
        // the SAME row — grabbing a DIFFERENT row while a previous drop is still
        // settling would leave that drop's deferred Commit in flight (two placeholders,
        // a stale fromIndex, rows rebinding under the new drag). Weak table: containers
        // die with their inspector.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, System.Action> s_pendingSettles =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, System.Action>();

        // --- Floating-row fidelity (public for the verification tests) ------------------------
        // The row floats by reparenting into an overlay ANCESTOR (the ScrollView's content
        // container) — usually ABOVE the subtree that carries the layout's stylesheets. Without
        // intervention every class-driven style dies for the duration of the drag (the 14px
        // reorder grip collapses to 0x0, visibly shifting the row's content left). BeginFloat
        // carries the abandoned ancestors' stylesheets onto the row itself, and neutralizes the
        // box-model terms the layout engine adds to absolute insets (parent border + child
        // margin) so the float lands EXACTLY on the row's original world position.

        /// <summary>Per-row bookkeeping captured while a row floats: the stylesheets carried up to it and the box-model insets neutralized so it lands on its original world position.</summary>
        public sealed class FloatState
        {
            internal readonly List<StyleSheet> CarriedSheets = new List<StyleSheet>();
            internal StyleLength MarginTop, MarginBottom, MarginLeft, MarginRight;
        }

        /// <summary>
        /// World point → the left/top insets that place a margin-less absolute child of
        /// <paramref name="overlay"/> at that point (the layout engine adds the parent's
        /// border on top of the inset, so it must be subtracted here).
        /// </summary>
        public static Vector2 OverlayInsetFromWorld(VisualElement overlay, Vector2 world)
        {
            Vector2 local = overlay.WorldToLocal(world);
            float bl = overlay.resolvedStyle.borderLeftWidth;
            float bt = overlay.resolvedStyle.borderTopWidth;
            if (float.IsNaN(bl)) bl = 0f;
            if (float.IsNaN(bt)) bt = 0f;
            return new Vector2(local.x - bl, local.y - bt);
        }

        /// <summary>
        /// Reparents <paramref name="row"/> into <paramref name="overlayHost"/> as an absolute
        /// floater positioned exactly over <paramref name="originalWorldBound"/>. Returns the
        /// state EndFloat needs to undo the stylesheet carry and margin neutralization.
        /// </summary>
        public static FloatState BeginFloat(VisualElement row, VisualElement overlayHost, Rect originalWorldBound)
        {
            var state = new FloatState();

            // Collect stylesheets from the chain the row is about to leave (parent → overlay).
            for (var cur = row.parent; cur != null && cur != overlayHost; cur = cur.parent)
            {
                for (int i = 0; i < cur.styleSheets.count; i++)
                {
                    var sheet = cur.styleSheets[i];
                    if (sheet != null && !row.styleSheets.Contains(sheet) && !state.CarriedSheets.Contains(sheet))
                    {
                        row.styleSheets.Add(sheet);
                        state.CarriedSheets.Add(sheet);
                    }
                }
            }

            // Margins shift an absolute child off its insets — zero them while floating.
            state.MarginTop = row.style.marginTop; row.style.marginTop = 0f;
            state.MarginBottom = row.style.marginBottom; row.style.marginBottom = 0f;
            state.MarginLeft = row.style.marginLeft; row.style.marginLeft = 0f;
            state.MarginRight = row.style.marginRight; row.style.marginRight = 0f;

            row.RemoveFromHierarchy();
            overlayHost.Add(row);
            row.style.position = Position.Absolute;
            row.style.width = originalWorldBound.width;

            Vector2 inset = OverlayInsetFromWorld(overlayHost, new Vector2(originalWorldBound.xMin, originalWorldBound.yMin));
            row.style.left = inset.x;
            row.style.top = inset.y;
            return state;
        }

        /// <summary>Undoes BeginFloat's stylesheet carry and margin neutralization.</summary>
        public static void EndFloat(VisualElement row, FloatState state)
        {
            if (state == null) return;
            foreach (var sheet in state.CarriedSheets) row.styleSheets.Remove(sheet);
            state.CarriedSheets.Clear();
            row.style.marginTop = state.MarginTop;
            row.style.marginBottom = state.MarginBottom;
            row.style.marginLeft = state.MarginLeft;
            row.style.marginRight = state.MarginRight;
        }

        // Pure index math (unit-tested via SasquatchVerificationTests): the MoveArrayElement destination is the number of OTHER rows whose vertical
        // center sits above the cursor.
        /// <summary>The destination index for the dragged row given the rows' center-Y positions (pure math).</summary>
        public static int ComputeTargetIndex(IReadOnlyList<float> rowCenterYs, int draggedIndex, float cursorY)
        {
            int gap = 0;
            for (int i = 0; i < rowCenterYs.Count; i++)
            {
                if (i == draggedIndex) continue;
                if (cursorY > rowCenterYs[i]) gap++;
            }
            return gap;
        }

        // Pure index math: the index the element originally at `original` ends up at after
        // MoveArrayElement(from, to). Used to preview the "[i]" labels at drop time.
        /// <summary>Where index <paramref name="original"/> ends up after moving an element from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static int DisplayIndexAfterMove(int original, int from, int to)
        {
            if (original == from) return to;
            if (from < to && original > from && original <= to) return original - 1;
            if (to < from && original >= to && original < from) return original + 1;
            return original;
        }

        // --- "[i]" label preview ------------------------------------------------------------
        // The data Commit is deferred until the drop spring settles (committing mid-flight
        // would rebind the floating row's fields to a different element). The bracket labels
        // are pure cosmetics though, so they snap to the post-drop arrangement the moment the
        // pointer is released; Commit's rebind then resets them to each row's own slot index,
        // which is the same final picture.

        private static void SetIndexLabel(VisualElement row, int index)
        {
            var lbl = row.Q<Label>(className: IndexLabelClass);
            // The format is asked of the LABEL, not of the global List skin: a TABLE row carries this same
            // class and always keeps its brackets, so a skin-wide check here would reformat a surface the
            // build never skinned. See LayoutTreeBuilder.FormatIndexLabel.
            if (lbl != null) lbl.text = LayoutTreeBuilder.FormatIndexLabel(lbl, index);
        }

        // The dragged rows are FLOATING in an overlay ancestor at this point, so they are not among
        // rowsContainer's children and have to be relabelled separately. One permutation is computed for
        // the whole drop and inverted into original → final, which is both the block answer and — for a
        // single row — exactly DisplayIndexAfterMove's answer, from one piece of arithmetic instead of two.
        private static void PreviewIndexLabels(VisualElement rowsContainer, IReadOnlyList<VisualElement> draggedRows,
            int arraySize, IReadOnlyList<int> from, int gap)
        {
            var target = CollectionRowSelection.ComputeBlockPermutation(arraySize, from, gap);
            if (target.Length == 0) return;

            var finalOf = new Dictionary<int, int>(target.Length);
            for (int i = 0; i < target.Length; i++) finalOf[target[i]] = i;

            if (draggedRows != null)
            {
                foreach (var r in draggedRows)
                {
                    if (r != null && r.userData is int o && finalOf.TryGetValue(o, out int f)) SetIndexLabel(r, f);
                }
            }
            foreach (var child in rowsContainer.Children())
            {
                if (child.userData is int original && finalOf.TryGetValue(original, out int slot)) SetIndexLabel(child, slot);
            }
        }

        private static void ResetIndexLabels(VisualElement rowsContainer)
        {
            foreach (var child in rowsContainer.Children())
            {
                if (child.userData is int original) SetIndexLabel(child, original);
            }
        }

        // Wires drag-reorder onto `grip`. `row` must carry its array index in userData (int). `rowsContainer`
        // holds all the sibling row elements. `arrayProp` is the array/list SerializedProperty.
        // `indexOffset` is the raw array index of the first row currently in `rowsContainer` — non-zero when
        // the collection is PAGINATED (only the current page's rows are present). The placeholder reflow
        // counts page-relative gaps, so the drop destination is offset back to an absolute array index;
        // `userData`/`fromIndex` are already absolute. Reorder is only wired in natural order (no sort/filter),
        // so within a page the present rows are contiguous and a within-page drag maps cleanly.
        /// <summary>Wires drag-to-reorder onto a collection's rows container (live inspector &amp; preview).</summary>
        public static void Attach(
            VisualElement grip,
            VisualElement row,
            VisualElement rowsContainer,
            SerializedProperty arrayProp,
            SerializedObject serializedObject,
            System.Action onReordered = null,
            int indexOffset = 0,
            bool allowMultiSelect = false,
            System.Action onStructuralChange = null)
        {
            if (grip == null || row == null || rowsContainer == null) return;

            // No tooltip may appear while this row is in flight. The row travels WITH the pointer and the
            // grip stays exactly under the cursor, so the grip's own "Drag to reorder" would pop up the
            // instant the user paused mid-gesture and then re-anchor on every move — reading as a caption
            // dragging along behind the cursor. Stock Unity raises none during a reorder; neither do we.
            DragTooltipMute.Install(row);

            // Multi-row selection is OPT-IN per call site. The three non-collection callers
            // (UnityEventWidget, SerializedDictionaryDrawer, SasquatchColorPaletteWindow) pass nothing and
            // keep exactly today's single-row gesture: `selection` stays null and every branch below
            // collapses to the one-row path it has always taken.
            CollectionRowSelection selection = allowMultiSelect ? CollectionRowSelection.For(rowsContainer) : null;
            selection?.EnsureEscapeHandler(rowsContainer);
            selection?.EnsureUndoGuard(rowsContainer);
            // Delete/Backspace removes the picked rows. Needs a way to redraw the collection, so it is only
            // wired where the caller supplied one — the three non-collection callers pass nothing and keep a
            // selection that is purely a drag affordance, with no destructive key on it.
            selection?.EnsureDeleteHandler(rowsContainer, arrayProp, serializedObject, onStructuralChange);

            bool dragging = false;
            // A press that never moved commits NOTHING. The single-row path never needed this (from == to
            // makes CommitMove a no-op), but a non-contiguous block's "no move" destination is its own
            // COLLAPSE — a real data change — so a stray click must be ruled out explicitly.
            bool movedDuringDrag = false;
            // Where the press landed, in world pixels. A drag is only a drag once the pointer has left
            // this by more than DragThreshold — see the constant.
            Vector2 pressWorld = Vector2.zero;
            int toIndex = -1;
            VisualElement placeholder = null;
            float grabOffsetY = 0f;
            float currentTop = 0f;
            float lockedLeft = 0f;
            VisualElement overlayHost = null;

            // The rows in flight, ASCENDING by array index — one entry for an ordinary drag, N for a block
            // drag. Everything below is written against these lists so the single-row path is literally the
            // N == 1 case rather than a separate branch that can drift out of step with it.
            var dragRows = new List<VisualElement>();
            var dragIndices = new List<int>();
            var dragStates = new List<FloatState>();
            // Each row's y-offset from the TOP OF THE STACK while floating: the running sum of the heights
            // above it. Not the rows' original offsets — a non-contiguous selection collapses into one
            // contiguous block the instant it is grabbed, which is both the honest preview of where it
            // lands and the only arrangement the placeholder can be sized for.
            var dragStackOffsets = new List<float>();
            float dragStackHeight = 0f; // total height of the floating block, in world pixels
            int primarySlot = 0; // index within dragRows of the row the pointer actually grabbed

            // Settle spring (drop glide). The generation counter strands a superseded
            // watcher so a re-press can never double-Commit.
            MotionHandleFloat settleHandle = null;
            bool settlePending = false;
            List<int> settleIndices = null;
            int settleTo = -1;
            int settleGeneration = 0;

            // Sibling slide springs
            var rowSprings = new Dictionary<VisualElement, MotionHandleVector3>();
            var registeredCallbacks = new List<(VisualElement child, EventCallback<GeometryChangedEvent> callback)>();

            VisualElement FindOverlayHost(VisualElement element)
            {
                VisualElement curr = element;
                VisualElement best = element;
                while (curr != null)
                {
                    if (curr is ScrollView sv)
                    {
                        return sv.contentContainer;
                    }
                    if (curr.parent != null)
                    {
                        best = curr;
                    }
                    curr = curr.parent;
                }
                return best;
            }

            float GetTranslateY(VisualElement el)
            {
                var t = el.style.translate;
                if (t.keyword == StyleKeyword.Undefined && t.value.y.unit == LengthUnit.Pixel)
                {
                    return t.value.y.value;
                }
                return 0f;
            }

            void HandleRowGeometryChanged(GeometryChangedEvent evt, VisualElement child)
            {
                if (!dragging) return;
                // Motion off: skip the counter-translate trick entirely — rows just reflow.
                if (!EditorSpringFX.MotionEnabled) return;

                float delta = evt.oldRect.y - evt.newRect.y;
                if (Mathf.Abs(delta) < 0.5f) return;

                // Counter-translate to the old position, then spring home. The keyed spring
                // retargets with preserved velocity when the placeholder hops slots quickly.
                float startDelta = delta + GetTranslateY(child);
                child.style.translate = new Translate(0f, startDelta);
                rowSprings[child] = child.SpringTranslate(Vector2.zero, EditorSpringFX.Tune(SlideConfig));
            }

            void RegisterSiblingCallbacks()
            {
                foreach (var child in rowsContainer.Children())
                {
                    if (child == placeholder || dragRows.Contains(child)) continue;
                    if (!(child.userData is int)) continue;

                    VisualElement targetChild = child;
                    EventCallback<GeometryChangedEvent> cb = evt => HandleRowGeometryChanged(evt, targetChild);

                    targetChild.RegisterCallback(cb);
                    registeredCallbacks.Add((targetChild, cb));
                }
            }

            void UnregisterSiblingCallbacks()
            {
                foreach (var (child, cb) in registeredCallbacks)
                {
                    if (child != null)
                    {
                        child.UnregisterCallback(cb);
                        child.style.translate = StyleKeyword.Null;
                    }
                }
                registeredCallbacks.Clear();

                foreach (var handle in rowSprings.Values)
                {
                    handle?.Cancel();
                }
                rowSprings.Clear();
            }

            // Set by Restore when it finds the rows were rebuilt under us mid-drag — Commit then skips the
            // data move, because `from`/`to` were recorded against the arrangement that no longer exists.
            bool containerRebuilt = false;

            void Restore()
            {
                // Lifted here rather than on pointer-up, because the gesture is not over at pointer-up: the
                // row is still gliding into its slot on the settle spring, still under the cursor, and still
                // moving. Restore is the one funnel every ending takes — commit, no-op drop, lost capture.
                DragTooltipMute.SetDragging(row, false);
                foreach (var r in dragRows) DragTooltipMute.SetDragging(r, false);

                for (int i = 0; i < dragRows.Count; i++)
                {
                    var r = dragRows[i];
                    EndFloat(r, i < dragStates.Count ? dragStates[i] : null);
                    r.style.position = Position.Relative;
                    r.style.left = StyleKeyword.Null;
                    r.style.top = StyleKeyword.Null;
                    r.style.width = StyleKeyword.Null;
                    r.style.opacity = StyleKeyword.Null;
                }
                dragStates.Clear();

                // The placeholder was inserted INTO rowsContainer, so losing its parent means the container
                // was cleared (an external array-size sync, an undo, another inspector) while this row was
                // floating in a ScrollView ancestor — out of reach of itemsContainer.Clear(). Re-inserting
                // the float there would leave a duplicate stale row bound to an index that has moved.
                containerRebuilt = placeholder != null && placeholder.parent == null;
                if (placeholder != null) { placeholder.RemoveFromHierarchy(); placeholder = null; }

                if (containerRebuilt)
                {
                    // The rebuild already produced these rows' replacements.
                    foreach (var r in dragRows) r.RemoveFromHierarchy();
                }
                else
                {
                    // BringToFront moved each row to the end of rowsContainer; return them to their home
                    // slots.
                    ReturnRowsHome(rowsContainer, dragRows, dragIndices);
                }

                dragRows.Clear();
                dragIndices.Clear();
                dragStackOffsets.Clear();

                UnregisterSiblingCallbacks();
            }

            // `indices` are the dragged rows' ABSOLUTE array indices, ascending, snapshotted BEFORE
            // Restore (which clears the live drag lists). `to` is the destination gap — the same quantity
            // for one row and for a block, which is why both arms take it unchanged.
            void Commit(List<int> indices, int to)
            {
                Restore();
                if (containerRebuilt) return; // stale indices — see Restore
                if (indices == null || indices.Count == 0) { ResetIndexLabels(rowsContainer); return; }

                bool moved = indices.Count > 1
                    ? CommitBlockMove(arrayProp, serializedObject, indices, to, rowsContainer)
                    : CommitMove(arrayProp, serializedObject, indices[0], to, rowsContainer);

                if (moved)
                {
                    // Let the caller react to a committed reorder (e.g. persist to disk + refresh other
                    // inspectors). Only fires on a real move, never on a no-op drop or a typed edit.
                    onReordered?.Invoke();
                    // The selection named the OLD arrangement; re-point it at where the block actually
                    // landed, so it stays lit under the cursor and a second drag moves the same rows.
                    selection?.AfterBlockMove(arrayProp.arraySize, indices, to);
                }
                selection?.Paint(rowsContainer);
                // The labels may still carry the drop-time preview; with the row back in its
                // DOM slot and the values rebound, each row shows its own slot index again.
                ResetIndexLabels(rowsContainer);
            }

            // Finish a still-running drop settle immediately (cancel the spring, run its deferred Commit
            // exactly once), so a new drag starting on this row can't orphan the previous drop's placeholder.
            void FinalizePendingSettle()
            {
                DeregisterPendingSettle();
                settleGeneration++; // strands the in-flight watcher
                if (settleHandle != null)
                {
                    settleHandle = null;
                    Spring.Cancel(row, SettleKey);
                }
                if (settlePending)
                {
                    settlePending = false;
                    Commit(settleIndices, settleTo);
                }
            }

            // Drop this row's entry from the container's pending-settle table — but only if the
            // entry is still OURS (delegate equality: same closure target + method), so a newer
            // drop's registration is never clobbered.
            void DeregisterPendingSettle()
            {
                if (s_pendingSettles.TryGetValue(rowsContainer, out var cur) && cur == (System.Action)FinalizePendingSettle)
                {
                    s_pendingSettles.Remove(rowsContainer);
                }
            }

            async void WatchSettle(MotionHandleFloat handle, int gen)
            {
                try
                {
                    await handle.WaitFor(AwaitStrategy.PhysicalSettling);
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // Fall through. If the cancel came from FinalizePendingSettle the generation
                    // was bumped first and the check below strands us; any OTHER cancel source
                    // (panel detach on rebuild, engine shutdown) must NOT swallow the deferred
                    // Commit — the user's reorder would silently revert.
                }
                if (gen != settleGeneration) return;
                settleHandle = null;
                DeregisterPendingSettle();
                if (!settlePending) return;
                settlePending = false;
                Commit(settleIndices, settleTo);
            }

            // Press starts on the grip, but we capture the pointer on the ROW and float that same row. A
            // UI Toolkit element keeps pointer capture even while its layout changes (the designer's canvas
            // drag relies on the same trick), so move/up keep flowing to the row.
            // Positions the floating block. Every row shares one left edge and stacks downward from
            // `currentTop` by its precomputed offset, so the whole set moves as one rigid body.
            void ApplyStackTops()
            {
                for (int i = 0; i < dragRows.Count && i < dragStackOffsets.Count; i++)
                {
                    dragRows[i].style.left = lockedLeft;
                    dragRows[i].style.top = currentTop + dragStackOffsets[i];
                }
            }

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (!(row.userData is int idx)) return;

                // FIRST, before any selection gesture reads or writes state: if ANY drop in this
                // container is still settling, finish it now — each row has its own closure, so the table
                // is the only way to reach a DIFFERENT row's pending Commit. Leaving it in flight would
                // stack two placeholders and let the late MoveArrayElement rebind rows underneath this new
                // drag with a stale fromIndex.
                //
                // ORDER MATTERS. This used to sit BELOW the selection gestures, and the pending Commit's
                // own `selection.AfterBlockMove` then overwrote whatever the press had just decided — a
                // Ctrl-click landing inside the ~0.3s settle glide was silently discarded, and a plain
                // press was re-read as `Count > 1` and dragged the block that had just landed instead of
                // the one row under the cursor. Draining first means every gesture below sees the
                // arrangement the user is actually looking at.
                if (s_pendingSettles.TryGetValue(rowsContainer, out var pendingElsewhere))
                {
                    s_pendingSettles.Remove(rowsContainer);
                    pendingElsewhere();
                }
                // Idempotent for our own row (covers the table-bypassing same-row re-press).
                FinalizePendingSettle();

                // --- selection gestures -------------------------------------------------------------
                // Read from the GRIP only. A click anywhere else in the row still just edits the field
                // under the cursor, which is the whole reason selection does not live on the row itself.
                if (selection != null)
                {
                    selection.Prune(arrayProp.arraySize);
                    if (evt.actionKey) // Ctrl, or Cmd on macOS — toggle one row, no drag
                    {
                        selection.Toggle(idx);
                        selection.Paint(rowsContainer);
                        selection.FocusForKeys(rowsContainer, grip); // so Esc can reach the handler — see there
                        evt.StopPropagation();
                        return;
                    }
                    if (evt.shiftKey) // extend the span from the anchor, no drag
                    {
                        selection.SelectRange(idx);
                        selection.Prune(arrayProp.arraySize);
                        selection.Paint(rowsContainer);
                        selection.FocusForKeys(rowsContainer, grip);
                        evt.StopPropagation();
                        return;
                    }
                    // A plain press on a row that is NOT part of a live multi-selection resets to just
                    // that row — today's behaviour exactly, plus a highlight. A plain press on a row that
                    // IS in one keeps the set and drags the whole block.
                    if (!selection.Contains(idx) || selection.Count <= 1) selection.SelectOnly(idx);
                    selection.Paint(rowsContainer);
                }


                // --- resolve the rows in flight -----------------------------------------------------
                dragRows.Clear(); dragIndices.Clear(); dragStates.Clear(); dragStackOffsets.Clear();
                if (selection != null && selection.Count > 1)
                {
                    // Walked in DOM order, which IS ascending array index here — reorder is only ever
                    // wired in natural order (no sort, no filter). Only rows PRESENT in this container can
                    // travel; the selection is cleared on a page hop, so an absent index means the array
                    // moved under us and dropping it is the safe read.
                    foreach (var child in rowsContainer.Children())
                    {
                        if (!(child.userData is int childIdx)) continue;
                        if (!selection.Contains(childIdx)) continue;
                        dragRows.Add(child);
                        dragIndices.Add(childIdx);
                    }
                }
                if (dragRows.Count == 0 || !dragRows.Contains(row))
                {
                    // An ordinary single-row drag — or a block the grabbed row somehow isn't in, which the
                    // gestures above cannot produce but which must never drag a set the user can't see.
                    dragRows.Clear(); dragIndices.Clear();
                    dragRows.Add(row); dragIndices.Add(idx);
                }
                primarySlot = Mathf.Max(0, dragRows.IndexOf(row));

                toIndex = idx;
                dragging = true;
                movedDuringDrag = false;
                pressWorld = evt.position;
                // EVERY row in flight, not just the grabbed one. A block drag carries N rows past the
                // cursor and each of them owns a "Drag to reorder" gutter tooltip; muting only the grabbed
                // row left the other N-1 captions popping and re-anchoring behind the moving stack —
                // exactly the artefact the mute exists to prevent.
                foreach (var r in dragRows) DragTooltipMute.SetDragging(r, true);

                // Measure EVERY row before lifting any of them: BeginFloat removes a row from the
                // hierarchy, which reflows everything below it. Measure first, then float.
                //
                // MARGINS COUNT. worldBound is the border box and excludes margin, but ApplyRowSeparator
                // writes marginBottom INLINE on every row (2px None, 2/4px Line, 4/6px Card) and BeginFloat
                // zeroes it on lift. So each lifted row vacates height + margin while a height-only
                // placeholder gives back only height: a 4-row Card block used to yank ~24px of layout out
                // from under everything below it for the length of the drag. Measured here, before the
                // float, because that is the last moment the real margins are still on the elements.
                var rects = new List<Rect>(dragRows.Count);
                dragStackHeight = 0f;
                for (int i = 0; i < dragRows.Count; i++)
                {
                    var r = dragRows[i];
                    Rect b = r.worldBound;
                    rects.Add(b);
                    dragStackOffsets.Add(dragStackHeight);
                    float gap = r.resolvedStyle.marginTop + r.resolvedStyle.marginBottom;
                    if (float.IsNaN(gap) || gap < 0f) gap = 0f;
                    dragStackHeight += (b.height > 4f ? b.height : 22f) + gap;
                }
                // The placeholder brings its own margin, so it must ask for that much less height.
                float placeholderHeight = Mathf.Max(1f, dragStackHeight - PlaceholderMargin);

                Rect rb = rects[primarySlot];
                grabOffsetY = evt.position.y - rb.yMin;

                // Placeholder fills the slot so the list height stays stable while the block floats. It is
                // as tall as the WHOLE stack and sits where the first dragged row was.
                placeholder = MakePlaceholder(placeholderHeight);
                rowsContainer.Insert(Mathf.Clamp(rowsContainer.IndexOf(dragRows[0]), 0, rowsContainer.childCount), placeholder);

                overlayHost = FindOverlayHost(row);

                // Float each row by reparenting to the overlayHost (renders on top of other
                // containers); BeginFloat carries the layout's stylesheets along and places
                // the row pixel-exact over its original spot.
                for (int i = 0; i < dragRows.Count; i++)
                {
                    dragStates.Add(BeginFloat(dragRows[i], overlayHost, rects[i]));
                    dragRows[i].style.opacity = 0.92f;
                    dragRows[i].BringToFront();
                }

                lockedLeft = OverlayInsetFromWorld(overlayHost, new Vector2(rb.xMin, rb.yMin)).x;
                // The STACK's top, not the grabbed row's — with a block, the grabbed row sits
                // dragStackOffsets[primarySlot] below it. For one row that term is 0 and this is unchanged.
                currentTop = OverlayInsetFromWorld(overlayHost,
                    new Vector2(rb.xMin, evt.position.y - grabOffsetY - dragStackOffsets[primarySlot])).y;
                ApplyStackTops();

                RegisterSiblingCallbacks();

                row.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                // A DEAD ZONE, not a boolean. This used to flip on the first PointerMove of any size, and
                // a hand tremor between press and release is a PointerMove — which made the "a press that
                // never moved commits NOTHING" guard below unreachable in practice. That is harmless for a
                // single row (the destination comes back as its own slot and CommitMove no-ops) and
                // harmless for a contiguous block (the permutation comes out identity), but a
                // NON-CONTIGUOUS block collapses into one run the instant it is grabbed, so its "no move"
                // destination is already a real data change. Without the dead zone, clicking a row of a
                // {0, 3, 5} selection silently rewrote the array and pushed an undo step.
                if (!movedDuringDrag && (evt.position - (Vector3)pressWorld).sqrMagnitude >= DragThreshold * DragThreshold)
                {
                    movedDuringDrag = true;
                }

                // The stack's top in world space: the cursor, less where inside the grabbed row it landed,
                // less how far that row sits down the stack. All three terms are 0 for a single row.
                float stackTopWorldY = evt.position.y - grabOffsetY - dragStackOffsets[primarySlot];
                currentTop = OverlayInsetFromWorld(overlayHost, new Vector2(0f, stackTopWorldY)).y;
                ApplyStackTops();

                // What the gap is measured against. ONE row keeps using the cursor itself — that is the
                // shipped feel and nothing here should disturb it. A BLOCK uses its own vertical centre
                // instead: with several rows in flight the cursor can sit anywhere in a tall stack, and
                // measuring from it would drift the landing slot away from the floating ghost.
                float refY = dragRows.Count > 1
                    ? stackTopWorldY + dragStackHeight * 0.5f
                    : evt.position.y;

                // ReflowPlaceholder returns a page-relative gap; offset it to an absolute destination index.
                toIndex = indexOffset + ReflowPlaceholder(rowsContainer, placeholder, dragRows, refY);
                evt.StopPropagation();
            });

            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                // Snapshot before anything can Restore: the drag lists are cleared there, and both the
                // immediate and the deferred Commit need these.
                var indices = new List<int>(dragIndices);
                int to = toIndex;
                var droppedRows = new List<VisualElement>(dragRows);
                int size = arrayProp.arraySize;
                if (row.HasPointerCapture(evt.pointerId)) row.ReleasePointer(evt.pointerId);

                // A press with no movement is not a reorder. Put everything back and keep the selection
                // the press just made — this is what makes plain-clicking a grip a pure "select this row".
                if (!movedDuringDrag)
                {
                    Restore();
                    // A plain press on a row that was ALREADY part of a multi-selection had to keep the set
                    // intact, because it might have been the start of a block drag. It wasn't — so now
                    // collapse to the row the user actually clicked. This is the file-browser rule, and the
                    // decision genuinely cannot be made any earlier than the release.
                    //
                    // NOT after a rebuild, though. Restore reports one via containerRebuilt, and the
                    // repopulate that caused it has already cleared the selection on purpose; re-seeding it
                    // here with this row's pre-rebuild userData would resurrect an index the rebuild
                    // deliberately discarded — out of range entirely if the array also shrank.
                    if (selection != null && !containerRebuilt && !evt.actionKey && !evt.shiftKey
                        && row.userData is int pressed)
                    {
                        selection.SelectOnly(pressed);
                        selection.Prune(arrayProp.arraySize);
                    }
                    if (!containerRebuilt) selection?.Paint(rowsContainer);
                    // …and NOW put focus on the grip, so Esc and Delete reach the container's handlers. Not
                    // at pointer-DOWN like the Ctrl/Shift gestures: a plain press might have been the start
                    // of a drag, and stealing focus mid-drag would end whatever field edit was in progress
                    // for a gesture that turned out to be a reorder. The release is the first moment this is
                    // known to be a click.
                    if (!containerRebuilt) selection?.FocusForKeys(rowsContainer, grip);
                    ResetIndexLabels(rowsContainer);
                    evt.StopPropagation();
                    return;
                }

                // Animated settle: glide the floating block down into the placeholder's slot, then commit.
                float targetTop;
                if (placeholder != null && placeholder.parent != null)
                {
                    targetTop = OverlayInsetFromWorld(overlayHost,
                        new Vector2(placeholder.worldBound.xMin, placeholder.worldBound.yMin)).y;
                }
                else
                {
                    targetTop = currentTop;
                }
                float startTop = currentTop;

                if (Mathf.Abs(targetTop - startTop) < 0.5f || !EditorSpringFX.MotionEnabled)
                {
                    Commit(indices, to);
                }
                else
                {
                    // The reorder is decided the moment the pointer lifts — show the new
                    // "[i]" arrangement NOW instead of after the glide finishes. Only the
                    // data Commit waits for the spring.
                    PreviewIndexLabels(rowsContainer, droppedRows, size, indices, to);

                    settleIndices = indices;
                    settleTo = to;
                    settlePending = true;
                    int gen = ++settleGeneration;
                    settleHandle = Spring.To((object)row,
                        () => currentTop,
                        v => { currentTop = v; ApplyStackTops(); },
                        targetTop, EditorSpringFX.Tune(SettleConfig), SettleKey);
                    WatchSettle(settleHandle, gen);

                    // Expose this drop's finalizer to the rest of the container (see PointerDown).
                    s_pendingSettles.Remove(rowsContainer);
                    s_pendingSettles.Add(rowsContainer, FinalizePendingSettle);
                }
                evt.StopPropagation();
            });

            // Lost capture (focus change, Esc, window blur) cancels: restore in place, no data change.
            row.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!dragging) return;
                dragging = false;
                Restore();
            });
        }

        // The data half of a drop, shared by every Attach closure and callable on its own (the closure
        // half — Restore/float bookkeeping — is pointer-driven and panel-dependent). Returns true when a
        // real move was applied.
        /// <summary>
        /// Applies the array move for a committed drop and reflows the values in place — no rebuild: the
        /// rows are bound by array index, so syncing the SerializedObject reflows the moved values into
        /// the existing row elements (a rebuild would blank every field for a frame while new
        /// PropertyFields populate). BindProperty fields re-sync on their own; change-DRIVEN widgets
        /// (TrackPropertyValue-based) need the explicit refresh: the dragged row's trackers were
        /// re-baselined by the drag's reparenting (float out, restore home) and would swallow the move
        /// outright, and the shifted rows' trackers would fire a binding tick LATE — one frame of every
        /// affected widget animating/blipping from the previous row's value. A move from→to shifts
        /// exactly the rows in [from,to], so those are refreshed synchronously, inside the commit,
        /// under <see cref="TrackedRefresh.IsExplicitRefresh"/> (animated displays snap): each value
        /// appears to move WITH its row, never to morph in place.
        /// </summary>
        /// <summary>
        /// True when this reorder would be applied across a MULTI-SELECTION whose targets disagree about
        /// how long the array is.
        ///
        /// <para>Every index a reorder computes — the bounds check, the block permutation, the refresh
        /// range — comes from <c>arrayProp.arraySize</c>, which on a multi-edit SerializedObject reports
        /// ONE target's length. Unity then replays the moves against every target. When the lengths agree
        /// that is exactly right and multi-edit reordering keeps working; when they do not, the shorter
        /// target receives a permutation derived from a longer array and quietly ends up in an arrangement
        /// nobody asked for. Refusing is the honest answer: the drop simply does not take, the rows are
        /// already back in their home slots, and no data is scrambled behind the user's back.</para>
        /// </summary>
        /// <summary>
        /// True when a multi-object selection's targets hold DIFFERENT numbers of elements in this array.
        /// Every structural edit through a multi-target <see cref="SerializedObject"/> reads target[0]'s
        /// length and forces every other target to it, so a longer target's tail is overwritten rather than
        /// shifted — see <c>multi-edit-array-append-destroys-data</c>. Internal rather than private because
        /// the dictionary drawer needs the same guard on its own Add / Delete / row menu.
        /// </summary>
        internal static bool TargetsDisagreeOnLength(SerializedProperty arrayProp, SerializedObject serializedObject)
        {
            if (serializedObject == null || !serializedObject.isEditingMultipleObjects) return false;
            if (arrayProp == null) return false;
            var sizeProp = serializedObject.FindProperty(arrayProp.propertyPath + ".Array.size");
            return sizeProp != null && sizeProp.hasMultipleDifferentValues;
        }

        internal static bool CommitMove(
            SerializedProperty arrayProp, SerializedObject serializedObject, int from, int to, VisualElement rowsContainer)
        {
            if (to < 0 || to == from || arrayProp == null) return false;
            if (TargetsDisagreeOnLength(arrayProp, serializedObject)) return false;
            // Both ends must still exist. A drop recorded against an array that shrank underneath it
            // (a delete from elsewhere, an undo) would otherwise move the wrong element or throw.
            int size = arrayProp.arraySize;
            if (from < 0 || from >= size || to >= size) return false;
            arrayProp.MoveArrayElement(from, to);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            RefreshRowsInRange(rowsContainer, from, to);
            return true;
        }

        /// <summary>
        /// The block twin of <see cref="CommitMove"/>: drops the <paramref name="selected"/> elements in as
        /// one contiguous run at <paramref name="destinationGap"/>. Returns true when the array actually
        /// changed.
        ///
        /// <para><b>Every move first, then ONE ApplyModifiedProperties.</b> That is what makes a block
        /// reorder a single Undo step — applying per move would leave the user pressing Ctrl+Z once per
        /// element, unpicking their drag one row at a time.</para>
        ///
        /// <para>The refresh afterwards is <see cref="CommitMove"/>'s, for the same reason documented
        /// there: change-DRIVEN widgets would otherwise animate from the previous row's value for a frame.
        /// The range is every slot the permutation actually re-values, which for a block is generally wider
        /// than [from, to].</para>
        /// </summary>
        internal static bool CommitBlockMove(
            SerializedProperty arrayProp, SerializedObject serializedObject,
            IReadOnlyList<int> selected, int destinationGap, VisualElement rowsContainer)
        {
            if (arrayProp == null || selected == null || selected.Count == 0) return false;
            if (TargetsDisagreeOnLength(arrayProp, serializedObject)) return false;
            int size = arrayProp.arraySize;

            // Both ends must still exist. A drop recorded against an array that shrank underneath it
            // (a delete from elsewhere, an undo) would otherwise move the wrong elements or throw.
            foreach (int i in selected)
            {
                if (i < 0 || i >= size) return false;
            }

            var moves = CollectionRowSelection.ComputeBlockMove(size, selected, destinationGap);
            if (moves.Count == 0) return false;

            foreach (var (from, to) in moves) arrayProp.MoveArrayElement(from, to);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            if (CollectionRowSelection.TryGetTouchedRange(size, selected, destinationGap, out int lo, out int hi))
            {
                RefreshRowsInRange(rowsContainer, lo, hi);
            }
            return true;
        }

        /// <summary>
        /// Explicitly refreshes the change-driven widgets of every row whose bound array index (row
        /// userData) falls in [<paramref name="a"/>, <paramref name="b"/>] — the rows a data move or
        /// swap actually re-valued. Rows on other pagination pages simply aren't present and are
        /// skipped; rows outside the range kept their values, so their trackers stay silent.
        /// </summary>
        internal static void RefreshRowsInRange(VisualElement rowsContainer, int a, int b)
        {
            if (rowsContainer == null) return;
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            foreach (var child in rowsContainer.Children())
            {
                if (child.userData is int idx && idx >= lo && idx <= hi)
                    TrackedRefresh.RefreshSubtree(child);
            }
        }

        /// <summary>
        /// The DOM slot in <paramref name="rowsContainer"/> that puts a row carrying ABSOLUTE array index
        /// <paramref name="absoluteIndex"/> back among its siblings in ascending index order.
        ///
        /// <para><b>A rows container's CHILD LIST IS NOT ITS ROW LIST.</b> Timeline inserts its continuous
        /// rail at child 0, and the "no items match" / "list is empty" / hard-cap notes are appended after
        /// the last row — so <c>childIndex == arrayIndex</c> holds only for the styles that happen to have
        /// no rail. <see cref="Restore"/> re-inserted a floated row at its raw array index, which under
        /// Timeline landed every row ONE SLOT TOO HIGH: drag the last row to the top and the list came back
        /// reading 0, 1, 3, 2, with the bottom two rows' values transposed — each row still showed its own
        /// (correctly moved) array slot, so it read as the DATA having scrambled. Ask the siblings who they
        /// are instead of counting them, and the rail, the notes and pagination all stop mattering.</para>
        ///
        /// <para>Indices are compared ABSOLUTE-to-ABSOLUTE (row <c>userData</c> is absolute on every page),
        /// which is why no <c>indexOffset</c> appears here.</para>
        /// </summary>
        internal static int HomeSlotFor(VisualElement rowsContainer, int absoluteIndex)
        {
            if (rowsContainer == null) return 0;
            int slot = -1;
            int count = rowsContainer.childCount;
            for (int i = 0; i < count; i++)
            {
                if (!(rowsContainer[i].userData is int idx)) continue; // rail, notes, placeholder
                if (idx > absoluteIndex) return i;                     // the first row that belongs AFTER us
                slot = i + 1;                                          // …otherwise we go just after this one
            }
            // No row sibling at all (the container holds only a rail): append, which keeps the rail first.
            return slot >= 0 ? slot : count;
        }

        /// <summary>
        /// Returns floated rows to their home slots in <paramref name="rowsContainer"/>, ascending — so each
        /// insert only ever pushes rows that belong after it, which reconstructs the original arrangement
        /// even for a non-contiguous block. Slots come from <see cref="HomeSlotFor"/>, never from the array
        /// index itself.
        /// </summary>
        internal static void ReturnRowsHome(
            VisualElement rowsContainer, IReadOnlyList<VisualElement> rows, IReadOnlyList<int> indices)
        {
            if (rowsContainer == null || rows == null || indices == null) return;
            int n = Mathf.Min(rows.Count, indices.Count);
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                r.RemoveFromHierarchy();
                rowsContainer.Insert(HomeSlotFor(rowsContainer, indices[i]), r);
            }
        }

        // Moves the placeholder to the gap under the cursor (other rows reflow to make space) and returns
        // that gap = the MoveArrayElement destination index (post-removal).
        private static int ReflowPlaceholder(VisualElement rowsContainer, VisualElement placeholder,
            List<VisualElement> draggedRows, float cursorY)
        {
            int gap = 0;
            VisualElement insertBefore = null;
            foreach (var child in rowsContainer.Children())
            {
                if (child == placeholder || (draggedRows != null && draggedRows.Contains(child))) continue;
                if (!(child.userData is int)) continue; // only real rows are reorder targets
                if (cursorY < child.worldBound.center.y) { insertBefore = child; break; }
                gap++;
            }

            if (placeholder != null)
            {
                placeholder.RemoveFromHierarchy();
                if (insertBefore != null) rowsContainer.Insert(rowsContainer.IndexOf(insertBefore), placeholder);
                else rowsContainer.Add(placeholder);
            }
            return gap;
        }

        private static VisualElement MakePlaceholder(float height)
        {
            var ph = new VisualElement { pickingMode = PickingMode.Ignore };
            ph.style.height = height;
            ph.style.marginTop = 1;
            ph.style.marginBottom = 1;
            ph.style.backgroundColor = new Color(0.18f, 0.4f, 0.7f, 0.18f);
            ph.style.borderTopWidth = 1; ph.style.borderBottomWidth = 1; ph.style.borderLeftWidth = 1; ph.style.borderRightWidth = 1;
            var line = new Color(0.3f, 0.55f, 0.9f, 0.9f);
            ph.style.borderTopColor = line; ph.style.borderBottomColor = line; ph.style.borderLeftColor = line; ph.style.borderRightColor = line;
            ph.style.borderTopLeftRadius = 3; ph.style.borderTopRightRadius = 3; ph.style.borderBottomLeftRadius = 3; ph.style.borderBottomRightRadius = 3;
            return ph;
        }
    }
}
