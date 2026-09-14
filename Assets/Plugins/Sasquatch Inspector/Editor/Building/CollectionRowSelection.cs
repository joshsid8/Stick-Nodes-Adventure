using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Multi-row selection for a live collection: which array indices are picked, and the pure index math
    /// that turns "drag this set to that gap" into a sequence of <c>MoveArrayElement</c> calls.
    ///
    /// <para><b>Why the state hangs off the ROWS CONTAINER.</b> A collection rebuilds its rows in place
    /// (<c>PopulateListRows</c>) for a search, a sort, a page hop or an add/remove — the container survives,
    /// the row elements do not. Keying the selection on the container therefore keeps it across a
    /// repopulate (the indices are still meaningful) while a FULL inspector rebuild makes a new container
    /// and correctly drops it. Same weak-table pattern, and the same reasoning, as
    /// <see cref="LayoutNodeRegistry"/> and <c>CollectionRowReorder.s_pendingSettles</c>.</para>
    ///
    /// <para><b>Indices are ABSOLUTE array indices</b>, never page-relative — matching the row
    /// <c>userData</c> that <c>LayoutTreeBuilder</c> stamps. Pagination is handled by the caller's
    /// <c>indexOffset</c>, exactly as the single-row reorder already does.</para>
    ///
    /// <para><b>Asset Store §2.5.g:</b> public UI Toolkit API only — no editor-internal type is named,
    /// cast or reflected.</para>
    /// </summary>
    internal sealed class CollectionRowSelection
    {
        // NOTE: a selected row carries no marker CLASS of its own, deliberately. One was written here
        // (`sasq-row--selected`) with no rule behind it in any stylesheet, which is worse than nothing: it
        // reads as a theming hook, and a buyer who styled it would find their rule beaten anyway, because
        // ApplyRowSeparator writes a row's background and borders INLINE and an inline style beats every
        // selector. The overlay below is both the marker and the paint, and it works precisely because it
        // is a separate element with no inline competition.

        private static readonly ConditionalWeakTable<VisualElement, CollectionRowSelection> s_byContainer =
            new ConditionalWeakTable<VisualElement, CollectionRowSelection>();

        private readonly HashSet<int> _indices = new HashSet<int>();

        // The row a Shift-range extends FROM. Set by every plain click and every Ctrl-toggle-on; a
        // Shift-click does NOT move it, so shift-clicking twice re-ranges from the same origin instead of
        // walking the anchor along behind the cursor (the behavior every file browser has).
        private int _anchor = -1;

        /// <summary>The selection attached to <paramref name="rowsContainer"/>, created on first ask.</summary>
        internal static CollectionRowSelection For(VisualElement rowsContainer)
        {
            if (rowsContainer == null) return null;
            if (s_byContainer.TryGetValue(rowsContainer, out var existing)) return existing;
            var created = new CollectionRowSelection();
            s_byContainer.Add(rowsContainer, created);
            return created;
        }

        // NOTE: there is deliberately no Forget(container). Every page hop, sort, search, add and delete
        // funnels through a repopulate that calls Clear() on this same instance, which is the correct
        // shape: dropping the TABLE ENTRY instead would strand the Esc and undo handlers already
        // registered on the container, and the next For() would wire a second pair whose stale closures
        // fight the new selection.

        /// <summary>
        /// The selection already held for <paramref name="rowsContainer"/>, or null. Unlike <see cref="For"/>
        /// this never CREATES one — for callers that only want to know whether rows are picked (the row
        /// menu) and must not mint a selection for a collection that has no reorder wired at all.
        /// </summary>
        internal static CollectionRowSelection Peek(VisualElement rowsContainer)
        {
            if (rowsContainer == null) return null;
            return s_byContainer.TryGetValue(rowsContainer, out var existing) ? existing : null;
        }

        internal int Count => _indices.Count;

        internal bool Contains(int index) => _indices.Contains(index);

        /// <summary>The selected indices, ascending. A fresh list — safe to hold across a mutation.</summary>
        internal List<int> Sorted()
        {
            var list = new List<int>(_indices);
            list.Sort();
            return list;
        }

        internal void Clear()
        {
            _indices.Clear();
            _anchor = -1;
        }

        internal void SelectOnly(int index)
        {
            _indices.Clear();
            _indices.Add(index);
            _anchor = index;
        }

        /// <summary>Ctrl/Cmd-click: add or remove one row. Removing the anchor surrenders it.</summary>
        internal void Toggle(int index)
        {
            if (_indices.Remove(index))
            {
                if (_anchor == index) _anchor = -1;
                return;
            }
            _indices.Add(index);
            _anchor = index;
        }

        /// <summary>
        /// Shift-click: select the inclusive span between the anchor and <paramref name="index"/>. With no
        /// anchor (nothing picked yet, or the anchor was Ctrl-clicked away) this degrades to a plain click,
        /// which is the only sane answer — a range needs two ends.
        /// </summary>
        internal void SelectRange(int index)
        {
            if (_anchor < 0) { SelectOnly(index); return; }
            int lo = Mathf.Min(_anchor, index);
            int hi = Mathf.Max(_anchor, index);
            _indices.Clear();
            for (int i = lo; i <= hi; i++) _indices.Add(i);
            // Anchor deliberately NOT moved — see the field comment.
        }

        /// <summary>
        /// Drops indices that no longer exist. The array can shrink under a held selection from anywhere —
        /// another inspector, an undo, a script — and a stale index would move the wrong element.
        /// </summary>
        internal void Prune(int arraySize)
        {
            // Anchor FIRST, and unconditionally. It outlives the set (SelectRange reads it when _indices
            // is empty), so returning early on an empty set would leave a stale anchor past the end of a
            // shrunken array and let the next Shift-click range from an element that no longer exists.
            if (_anchor >= arraySize) _anchor = -1;
            if (_indices.Count == 0) return;
            _indices.RemoveWhere(i => i < 0 || i >= arraySize);
        }

        /// <summary>
        /// Re-points the selection at where a committed block move actually landed: the rows end up as one
        /// contiguous run starting at <paramref name="destinationGap"/>. Without this the selection would
        /// still name the OLD arrangement, lighting up whichever rows had shuffled into those slots.
        /// </summary>
        internal void AfterBlockMove(int arraySize, IReadOnlyList<int> moved, int destinationGap)
        {
            if (moved == null || moved.Count == 0) return;
            int count = Mathf.Min(moved.Count, arraySize);
            int start = Mathf.Clamp(destinationGap, 0, Mathf.Max(0, arraySize - count));
            _indices.Clear();
            for (int i = 0; i < count; i++) _indices.Add(start + i);
            _anchor = start;
        }

        /// <summary>
        /// Registers Esc-clears-the-selection on the rows container, once. Registered in the TRICKLE-DOWN
        /// phase because the focused element during editing is a field deep inside a row: the container is
        /// the one place above every one of them that the key is guaranteed to pass through.
        /// </summary>
        internal void EnsureEscapeHandler(VisualElement rowsContainer)
        {
            if (rowsContainer == null || _escapeWired) return;
            _escapeWired = true;
            // FOCUSABLE, or the handler below is nearly dead code. A KeyDownEvent is dispatched down the
            // path to the FOCUSED element, so a trickle-down handler on this container only ever runs when
            // focus is already inside it. Every element on the gutter chain — the grip, the row, the lane,
            // this container — is a plain non-focusable VisualElement, so after picking rows purely by
            // Ctrl/Shift-clicking grips nothing in the subtree held focus and Esc never arrived at all.
            // The fix is to focus something INSIDE the container on a selection gesture — see FocusForKeys,
            // which focuses the GRIP the user just clicked rather than this container. Deliberately the
            // grip: a ScrollView scrolls its focused element into view, and focusing a tall rows container
            // would yank the inspector to the top of the collection on every Ctrl-click. The grip is one
            // row tall and already under the cursor, so scrolling it into view is a no-op — and Esc still
            // trickles down THROUGH this container on its way there, which is all this handler needs.
            rowsContainer.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape || _indices.Count == 0) return;
                Clear();
                Paint(rowsContainer);
                // NOT StopPropagation: Esc means several things in an inspector (close a popup, revert a
                // field) and swallowing it here would break every one of them for the sake of a highlight.
            }, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Puts keyboard focus inside the collection — on <paramref name="grip"/>, the element the user
        /// just clicked — so <see cref="EnsureEscapeHandler"/>'s Esc actually arrives. Called from the
        /// selection gestures only, never from a drag: a drag must not disturb whatever field was being
        /// edited, and it does not need the key.
        /// </summary>
        internal void FocusForKeys(VisualElement rowsContainer, VisualElement grip)
        {
            if (rowsContainer == null || grip == null || !_escapeWired) return;
            if (grip.panel == null) return;
            grip.focusable = true;
            grip.Focus();
        }

        private bool _escapeWired;

        /// <summary>
        /// Registers Delete / Backspace-removes-the-picked-rows on the rows container, once. The keyboard
        /// half of the multi-select gesture: pick rows by Ctrl/Shift-clicking their gutters, press Delete,
        /// and the whole set goes as ONE undo step (<see cref="CollectionRowMenu.DeleteMany"/>).
        ///
        /// <para><b>The gesture is gated on the GRIP, not merely on "something is selected".</b> Like
        /// <see cref="EnsureEscapeHandler"/> this listens in the TRICKLE-DOWN phase, which puts it above
        /// every field in every row and above the collection's search box — so an ungated handler would eat
        /// the Delete key of anyone typing in a string field or a search filter while rows happened to be
        /// lit, and silently destroy their data instead of a character. The selection gestures focus the
        /// GRIP the user clicked (<see cref="FocusForKeys"/>), so "the event is travelling to a grip" is
        /// exactly "the user's hands are on the row chrome, not in a field". Clicking into any field moves
        /// focus off the grip and hands the key straight back.</para>
        ///
        /// <para><b>The rebuild is DEFERRED off the event stack.</b> The rows repopulate destroys the grip
        /// this very event is being dispatched to; running it inline tears down UI Toolkit's own dispatch
        /// target. The DATA half stays synchronous, so no index can go stale between the read and the
        /// write — the same split <c>TableColumnGrip.Commit</c> documents.</para>
        /// </summary>
        internal void EnsureDeleteHandler(VisualElement rowsContainer, SerializedProperty arrayProp,
            SerializedObject serializedObject, System.Action onStructuralChange)
        {
            if (rowsContainer == null || arrayProp == null || serializedObject == null) return;
            if (onStructuralChange == null || _deleteWired) return;
            _deleteWired = true;

            // The PATH, not the SerializedProperty. This handler outlives every in-place rows rebuild (it is
            // registered on the container, which survives one), so it must not hold an iterator captured at
            // the first build — re-resolving is what CollectionEyedropper does across its own arm, and for
            // the same reason.
            string arrayPath = arrayProp.propertyPath;

            rowsContainer.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace) return;
                if (_indices.Count == 0) return;
                if (!IsOnRowChrome(rowsContainer, evt.target)) return;

                serializedObject.Update();
                var live = serializedObject.FindProperty(arrayPath);
                if (live == null || !live.isArray) return;

                Prune(live.arraySize);
                var picked = Sorted();
                if (picked.Count == 0) return;

                // Model first, synchronously: one DeleteMany, one ApplyModifiedProperties, one Ctrl+Z.
                CollectionRowMenu.DeleteMany(live, serializedObject, picked);
                // The indices named elements that no longer exist — there is no honest mapping from the old
                // arrangement to the new one, which is the same reason the undo guard clears rather than
                // re-derives.
                Clear();
                // …and the repaint, off the event stack. Scheduled on the CONTAINER, which survives a
                // rows-only repopulate; a scheduled item only ticks while its element is attached, so a
                // panel torn down first cancels this rather than stranding it.
                rowsContainer.schedule.Execute(() => onStructuralChange()).ExecuteLater(0);
                // Swallowed, unlike Esc: nothing else in an inspector should also act on the Delete that
                // just removed rows.
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        private bool _deleteWired;

        /// <summary>
        /// True when <paramref name="target"/> is a reorder grip inside <paramref name="rowsContainer"/>, or
        /// sits within one. Both grip shapes count: the skinned gutter lane
        /// (<c>LayoutTreeBuilder.ListGutterGripClass</c>) and the Classic standalone handle
        /// (<c>LayoutTreeBuilder.ReorderGripClass</c>). Walks ancestors because the element that actually
        /// holds focus can be the glyph inside the grip rather than the grip itself.
        /// </summary>
        private static bool IsOnRowChrome(VisualElement rowsContainer, IEventHandler target)
        {
            var el = target as VisualElement;
            while (el != null)
            {
                if (el.ClassListContains(LayoutTreeBuilder.ListGutterGripClass)
                    || el.ClassListContains(LayoutTreeBuilder.ReorderGripClass)) return true;
                if (el == rowsContainer) return false; // reached the top without crossing a grip
                el = el.parent;
            }
            return false;
        }

        /// <summary>
        /// Drops the selection on undo/redo, once. A REORDER does not change <c>arraySize</c>, so nothing
        /// else invalidates one: <c>AttachExternalArraySizeSync</c> compares sizes and returns, the
        /// whole-inspector undo gate fingerprints array sizes and managed-reference ids (a permutation
        /// hashes identically), and the two <c>Clear()</c> sites only run from a repopulate that never
        /// happens. So undoing a block drag left the DESTINATION rows lit while the data underneath them
        /// had gone back — and the next drag moved those, not the rows the user had picked.
        ///
        /// <para>Clearing rather than re-deriving is deliberate: the arrangement the selection described no
        /// longer exists, and there is no honest mapping from it to the reverted one.</para>
        /// </summary>
        internal void EnsureUndoGuard(VisualElement rowsContainer)
        {
            if (rowsContainer == null || _undoWired) return;
            _undoWired = true;

            Undo.UndoRedoCallback onUndoRedo = null;
            onUndoRedo = () =>
            {
                // Panel-less is either a teardown (OnRealDetach below drops us shortly) or a momentary
                // re-parent — the same reasoning AttachExternalArraySizeSync's undo hook documents.
                if (rowsContainer.panel == null) return;
                if (_indices.Count == 0 && _anchor < 0) return;
                Clear();
                Paint(rowsContainer);
            };
            Undo.undoRedoPerformed += onUndoRedo;
            PanelLifetime.OnRealDetach(rowsContainer, () => Undo.undoRedoPerformed -= onUndoRedo);
        }

        private bool _undoWired;

        /// <summary>
        /// Brings every row in the container into agreement with the selection.
        ///
        /// <para>The highlight is an OVERLAY CHILD, never the row's own background.
        /// <c>LayoutTreeBuilder.ApplyRowSeparator</c> writes background and borders INLINE for zebra tints
        /// and Card trays, and an inline style beats any stylesheet — so tinting the row itself would show
        /// nothing on precisely the rows most likely to be picked. The overlay is absolute and
        /// picking-disabled, so it changes no layout and eats no clicks.</para>
        /// </summary>
        internal void Paint(VisualElement rowsContainer)
        {
            if (rowsContainer == null) return;
            foreach (var child in rowsContainer.Children())
            {
                if (!(child.userData is int index)) continue;
                SetRowSelected(child, _indices.Contains(index));
            }
        }

        /// <summary>Adds or removes one row's selection overlay. Idempotent.</summary>
        internal static void SetRowSelected(VisualElement row, bool selected)
        {
            if (row == null) return;
            // DIRECT CHILDREN ONLY. `row.Q(className:)` is a recursive descendant search, and a collection
            // row can CONTAIN another collection (a nested CollectionNode in the item template, an inline
            // editor rendering an element's own layout) whose rows wear this very class. With Q, an outer
            // row "found" an inner row's overlay: selecting the outer row painted nothing (it took the
            // already-present early return) and deselecting it deleted the INNER list's highlight while
            // that list still held the selection. Both lists are live at once, so this is reachable by
            // ordinary use, not a corner case.
            VisualElement existing = null;
            foreach (var child in row.Children())
            {
                if (child.ClassListContains(OverlayClass)) { existing = child; break; }
            }
            if (!selected)
            {
                existing?.RemoveFromHierarchy();
                return;
            }
            if (existing != null) return;

            var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList(OverlayClass);
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.right = 0f;
            overlay.style.top = 0f;
            overlay.style.bottom = 0f;
            // THE RAIL. An absolute child is inset from the PADDING box, i.e. just inside whatever border
            // the row itself draws — so a plain 2px rail on a Card row landed immediately inside the tray's
            // own 3px grey rail and read as a double stripe, the exact artefact ApplyRowSeparator's own
            // comment calls out as looking like a bug. Where the row already has a left border, the overlay
            // steps out over it and REPLACES it with the accent at the same width; where it has none (Rows,
            // Line, zebra) the old 2px rail is unchanged.
            float rowRail = row.resolvedStyle.borderLeftWidth;
            if (float.IsNaN(rowRail) || rowRail <= 0f) rowRail = 0f;
            if (rowRail > 0f)
            {
                overlay.style.left = -rowRail;
                overlay.style.borderLeftWidth = rowRail;
            }
            else
            {
                overlay.style.borderLeftWidth = 2f;
            }
            // Follow the row's own corners. ApplyRowSeparator rounds a Card tray (and only a Card tray)
            // inline, and nothing clips a row's children — so a square overlay laid over a rounded row
            // pokes four small accent nubs out past the tray. Read from resolvedStyle rather than the
            // node's style, because the radius is whatever the separator actually applied.
            CopyCornerRadius(row, overlay);
            row.Add(overlay);
        }

        /// <summary>USS class on the overlay element that paints a selected row.</summary>
        internal const string OverlayClass = "sasq-row-select-overlay";

        /// <summary>Mirrors <paramref name="from"/>'s four corner radii onto <paramref name="to"/>.</summary>
        private static void CopyCornerRadius(VisualElement from, VisualElement to)
        {
            var rs = from.resolvedStyle;
            float tl = rs.borderTopLeftRadius, tr = rs.borderTopRightRadius;
            float bl = rs.borderBottomLeftRadius, br = rs.borderBottomRightRadius;
            if (float.IsNaN(tl) || tl <= 0f) tl = 0f;
            if (float.IsNaN(tr) || tr <= 0f) tr = 0f;
            if (float.IsNaN(bl) || bl <= 0f) bl = 0f;
            if (float.IsNaN(br) || br <= 0f) br = 0f;
            if (tl <= 0f && tr <= 0f && bl <= 0f && br <= 0f) return; // square row: leave the defaults alone
            to.style.borderTopLeftRadius = tl;
            to.style.borderTopRightRadius = tr;
            to.style.borderBottomLeftRadius = bl;
            to.style.borderBottomRightRadius = br;
        }

        // -----------------------------------------------------------------------------------------------
        // Pure index math. No panel, no SerializedProperty — unit-testable on its own, which is where the
        // block reorder actually lives or dies.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The final arrangement, as original indices, when the <paramref name="selected"/> rows are lifted
        /// out and re-inserted at <paramref name="destinationGap"/>.
        ///
        /// <para><paramref name="destinationGap"/> counts gaps in the array WITH the selection removed —
        /// 0 is "before everything left", <c>arraySize - selected.Count</c> is "after everything left".
        /// That is the same quantity <c>CollectionRowReorder.ReflowPlaceholder</c> already returns, which
        /// is why it needs no translation at the call site.</para>
        ///
        /// <para>A NON-CONTIGUOUS selection lands CONTIGUOUS, in its original relative order. That is what
        /// every file browser and layer stack does, and there is no other coherent answer — the rows have
        /// to go somewhere, and "somewhere" is one block.</para>
        /// </summary>
        internal static int[] ComputeBlockPermutation(int arraySize, IReadOnlyList<int> selected, int destinationGap)
        {
            if (arraySize <= 0) return new int[0];

            var picked = new HashSet<int>();
            if (selected != null)
            {
                foreach (int i in selected)
                {
                    if (i >= 0 && i < arraySize) picked.Add(i);
                }
            }
            if (picked.Count == 0 || picked.Count == arraySize)
            {
                // Nothing to move, or everything moves as one — either way the order is unchanged.
                var identity = new int[arraySize];
                for (int i = 0; i < arraySize; i++) identity[i] = i;
                return identity;
            }

            var block = new List<int>();
            var rest = new List<int>();
            for (int i = 0; i < arraySize; i++)
            {
                if (picked.Contains(i)) block.Add(i);
                else rest.Add(i);
            }

            int gap = Mathf.Clamp(destinationGap, 0, rest.Count);
            var result = new List<int>(arraySize);
            result.AddRange(rest.GetRange(0, gap));
            result.AddRange(block);
            result.AddRange(rest.GetRange(gap, rest.Count - gap));
            return result.ToArray();
        }

        /// <summary>
        /// The ordered <c>MoveArrayElement(from, to)</c> pairs that turn the current array into
        /// <see cref="ComputeBlockPermutation"/>'s arrangement. Empty when nothing actually moves.
        ///
        /// <para>Selection-sort shape: walk the target order left to right and, whenever the element that
        /// belongs at slot <c>k</c> is elsewhere, move it there. Every slot below <c>k</c> is already
        /// final, so a move can only ever disturb slots at or after <c>k</c> — which is what makes the
        /// sequence terminate in at most <c>arraySize - 1</c> moves.</para>
        /// </summary>
        internal static List<(int from, int to)> ComputeBlockMove(int arraySize, IReadOnlyList<int> selected, int destinationGap)
        {
            var moves = new List<(int from, int to)>();
            var target = ComputeBlockPermutation(arraySize, selected, destinationGap);
            if (target.Length == 0) return moves;

            // `working[i]` = which ORIGINAL element currently sits at slot i.
            var working = new List<int>(target.Length);
            for (int i = 0; i < target.Length; i++) working.Add(i);

            for (int k = 0; k < target.Length; k++)
            {
                int want = target[k];
                if (working[k] == want) continue;
                int at = working.IndexOf(want);
                if (at < 0) continue; // unreachable for a permutation; cheap insurance
                moves.Add((at, k));
                working.RemoveAt(at);
                working.Insert(k, want);
            }
            return moves;
        }

        /// <summary>
        /// The span of slots a block move actually re-values, as [lo, hi] — everything from the first slot
        /// that changes to the last. <c>CollectionRowReorder.RefreshRowsInRange</c> needs it for the same
        /// reason a single move does: change-DRIVEN widgets (TrackPropertyValue) would otherwise animate
        /// from the previous row's value for a frame.
        /// </summary>
        internal static bool TryGetTouchedRange(int arraySize, IReadOnlyList<int> selected, int destinationGap,
            out int lo, out int hi)
        {
            lo = hi = -1;
            var target = ComputeBlockPermutation(arraySize, selected, destinationGap);
            for (int i = 0; i < target.Length; i++)
            {
                if (target[i] == i) continue;
                if (lo < 0) lo = i;
                hi = i;
            }
            return lo >= 0;
        }

        /// <summary>
        /// Where the element originally at <paramref name="original"/> ends up — the block twin of
        /// <c>CollectionRowReorder.DisplayIndexAfterMove</c>, used to preview the "[i]" labels at drop time.
        /// Returns <paramref name="original"/> unchanged when it isn't in the array at all.
        /// </summary>
        internal static int DisplayIndexAfterBlockMove(int original, int arraySize, IReadOnlyList<int> selected, int destinationGap)
        {
            var target = ComputeBlockPermutation(arraySize, selected, destinationGap);
            for (int i = 0; i < target.Length; i++)
            {
                if (target[i] == original) return i;
            }
            return original;
        }
    }
}
