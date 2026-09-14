using System;
using System.Collections.Generic;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// LIVE TABLE COLUMN REORDER: hover a column header and a grip springs in on its left — the header text
    /// slides right to make room — then drag it and <b>the columns rearrange under your hand in real time</b>,
    /// the way the designer canvas has always done it. Dropping writes the new order to the layout asset, so
    /// every row keeps it.
    ///
    /// <para><b>Why a hover grip and not a mode.</b> A column header already answers a click (sort) and a
    /// drag on its divider (resize). A third gesture layered onto the same surface is how a reorder starts
    /// feeling unreliable, and putting it behind a mode button — tried first, rejected — turned a
    /// one-second action into three. A grip is its own surface: it appears only when the pointer is on that
    /// header, it is the only thing that starts a reorder, and nothing else changes meaning. Sorting moved
    /// to the ▲▼ glyph in the same pass so the two never contend.</para>
    ///
    /// <para><b>The preview slides the REAL columns.</b> First attempt drew an insertion caret, on the
    /// reasoning that a live cell's width is a flex weight and therefore unknowable. That reasoning was
    /// wrong, and <see cref="GapAnimator"/>'s docstring already said why: a reorder ADDS AND REMOVES
    /// NOTHING, so the post-drop layout is a pure permutation of the widths that are already on screen.
    /// Nothing resizes; only x changes. Sliding is therefore exact, not a guess — the same conclusion the
    /// designer's column drag reached, and the reason it looks the way it does.</para>
    ///
    /// <para><b>Both halves of a column move together.</b> Every displaced column translates its header AND
    /// every one of its data cells — they all live in one <c>columnCells[c]</c> list. A header left behind
    /// mislabels the whole table for the length of the drag, which is the single loudest way this can look
    /// broken.</para>
    ///
    /// <para><b>Targets are picked against FROZEN geometry, and measure the COLUMN rather than the
    /// pointer.</b> The header rects are captured once at pointer-down and never re-read, so travel →
    /// insertion index stays monotone however far the columns have already slid; reading live rects would
    /// feed the slide back into its own input. What is compared against them is the dragged column's own
    /// leading edge — see <see cref="TargetIndex"/> for why the pointer is the wrong probe when the grip
    /// it holds lives at the header's left edge.</para>
    ///
    /// <para><b>The grip is a painted <see cref="GripGlyph"/>, not a text character.</b> It was a Braille
    /// "⠿" and rendered as nothing at all: the editor font has no such glyph, so the element was there,
    /// sized and hit-testable, drawing empty. Every other grip in the package is this vector glyph for
    /// exactly that reason. The reveal springs SCALE and leaves opacity alone for the same class of reason —
    /// <see cref="EditorSpringFX.PopIn"/> starts at <c>opacity = 0</c>, so a spring that never ticks leaves
    /// an invisible control rather than a merely un-animated one.</para>
    ///
    /// <para><b>This writes to the LAYOUT ASSET.</b> The commit is <see cref="QuickEditPopup"/>'s pipeline
    /// step for step, <c>AdoptLiveLayoutEdit</c> included, which is what stops an open designer window's
    /// Save from destroying it. It sits alongside the column RESIZE grips, which have always written column
    /// widths to the asset with no opt-in either.</para>
    ///
    /// <para><b>Asset Store §2.5.g:</b> public API only. No editor-internal type is named, cast or
    /// reflected.</para>
    /// </summary>
    internal static class TableColumnGrip
    {
        internal const string GripClass = "sasq-col-grip";
        internal const string DraggingClass = "sasq-col-grip-dragging";
        /// <summary>Marks a header row whose tooltip mute is already wired, so N columns install one handler.</summary>
        internal const string MuteInstalledClass = "sasq-col-tooltip-muted";

        /// <summary>Pixels the header text gives up so the grip has somewhere to be.</summary>
        internal const float GripLane = 15f;

        /// <summary>
        /// Clear space between the resize divider's hit area and the move grip's, in header-local pixels.
        /// The two gestures live on adjacent surfaces and mean very different things — see the note where
        /// the grip is positioned.
        /// </summary>
        internal const float GripDeadBand = 4f;

        /// <summary>How far the columns NOT being dragged recede while a drag is in flight.</summary>
        private const float PassedColumnOpacity = 0.45f;

        // Quick and slightly springy: the lane fires on every header the pointer crosses, so a long settle
        // would leave a trail of still-moving labels behind the cursor.
        private static readonly SpringConfig LaneConfig = new SpringConfig(0.22f, 0.28f, 0.5f);
        // The displaced columns' glide. CollectionRowReorder's SlideConfig, one axis over — a re-target
        // mid-drag continues with its velocity instead of restarting, which is exactly what happens when the
        // pointer crosses several column boundaries in one sweep.
        private static readonly SpringConfig SlideConfig = new SpringConfig(0.25f, 0.2f, 0.5f);
        private static readonly SpringConfig PopConfig = new SpringConfig(0.35f, 0.55f);
        // The drop glide, borrowed wholesale from CollectionRowReorder's SettleConfig so a column lands with
        // the same weight a row does. The 0.5px epsilon is not cosmetic: the data move waits on this spring,
        // and the default epsilon would defer it by about a second after the drop.
        private static readonly SpringConfig SettleConfig = new SpringConfig(0.28f, 0.3f, 0.5f);
        private const string SettleKey = "col.settle";

        // One pending drop-settle finalizer per HEADER ROW. Each column's settle lives in its own closure,
        // so without this table grabbing a DIFFERENT column while a drop is still gliding would leave that
        // drop's deferred Commit in flight — and it would then fire with indices describing an arrangement
        // the new drag has already changed. Weak table: header rows die with their inspector.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Action> s_pendingSettles =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Action>();

        /// <summary>
        /// TRUE while any column anywhere is being dragged — the gate that stops OTHER columns' grips from
        /// springing in under the travelling pointer.
        ///
        /// <para>Reported: <i>"once we are dragging a column, grips from other columns start appearing based
        /// on my mouse pointer — that draws the eye to something unimportant."</i> Exactly right. Pointer
        /// CAPTURE redirects move/up to the grip, but <see cref="PointerEnterEvent"/> is still dispatched to
        /// whatever the pointer is physically over, so sweeping a column across its neighbours lit each one
        /// up in turn. A grip is an OFFER — "you could drag this" — and there is nothing to offer while a
        /// drag is already in flight.</para>
        ///
        /// <para><b>Global, not per table.</b> The whole point is that the eye should stay on the column in
        /// the user's hand; a grip blooming in a DIFFERENT table on the same inspector is the same
        /// distraction. One drag at a time is guaranteed by pointer capture, so a plain bool is enough —
        /// and it is cleared on every exit from the gesture, the lost-capture one included, so a drag that
        /// is taken away rather than finished cannot leave every grip in the project switched off.</para>
        /// </summary>
        private static bool s_dragInFlight;

        /// <summary>
        /// The single column drop still gliding ANYWHERE in the editor, or null.
        ///
        /// <para><see cref="s_pendingSettles"/> is keyed by header ROW, which reaches a different column of
        /// the SAME table and nothing else. Two tables in one inspector therefore missed each other: drop a
        /// column in table A, grab one in table B inside the ~0.3s glide, and A's deferred Commit fired on
        /// its own a moment later, rebuilt the whole inspector and killed the drag the user had in hand.
        /// Only one column drag can exist at a time (<see cref="s_dragInFlight"/> enforces it), so one
        /// global slot is all this needs.</para>
        /// </summary>
        private static Action s_pendingSettleAnywhere;

        /// <summary>
        /// TRUE between a committed reorder and the inspector rebuild that carries it out.
        ///
        /// <para>Reported as <i>"OCCASIONALLY … there's like a FLASH in the inspector, like for a single
        /// tick it was blank"</i>. The commit used to run the FULL inspector rebuild <b>inline, inside the
        /// pointer event that triggered it</b> — tearing down the grip, the header cell and every element
        /// UI Toolkit was still dispatching that event through. The displacement path already documents this
        /// exact hazard on its own rebuild signal (<i>"rebuilding synchronously would clear the element
        /// mid-event"</i>) and defers a tick for it; this path did not.</para>
        ///
        /// <para><b>Why only sometimes.</b> Two narrow doors, both timing-dependent, which is the whole
        /// character of the report:</para>
        /// <list type="number">
        /// <item>A drop whose landing offset is already within 0.5px of where the pointer left it skips the
        /// settle spring entirely and commits straight from inside <c>PointerUpEvent</c> — so whether the
        /// rebuild lands mid-dispatch depends on exactly where the user let go.</item>
        /// <item>Grabbing a column while a previous drop is still gliding runs that drop's finalizer from
        /// inside <c>PointerDownEvent</c> — same inline rebuild, and the gesture then went on to freeze
        /// geometry from, and capture the pointer on, elements that had just been destroyed.</item>
        /// </list>
        ///
        /// <para>The commit's MODEL half stays synchronous and ordered — undo, move, normalize, persist,
        /// adopt — so no index can go stale. Only the rebuild moves off the event stack. The previewed
        /// arrangement holds for that one tick, which is exactly right: it is already showing the order the
        /// rebuild is about to make real.</para>
        ///
        /// <para>The gate then <b>refuses to start a new drag</b> while it is raised. Freezing rects from a
        /// tree that is one tick from deletion produced a drag against dead elements — the second door
        /// above, and unfixable by deferring alone.</para>
        ///
        /// <para><b>It holds the ELEMENT, not a bool, and that is what makes it self-healing.</b> A
        /// scheduled item only ticks while its element is attached, so a panel torn down inside that one
        /// tick would leave a bare flag raised for the rest of the session and every column in the project
        /// permanently undraggable — a latch with no key. Reading the owner's <c>panel</c> instead means a
        /// rebuild that can no longer happen also can no longer block anything.</para>
        /// </summary>
        // WEAK. This used to be a strong reference, and it is only ever cleared by the scheduled callback
        // or by a later drag reading RebuildPending — so dropping a column and then clicking another
        // GameObject in the same tick left the static holding that header cell, and through its parent the
        // entire torn-down inspector tree and its bound SerializedObject, for the rest of the session.
        private static readonly System.WeakReference<VisualElement> s_rebuildOwner =
            new System.WeakReference<VisualElement>(null);

        private static VisualElement RebuildOwner =>
            s_rebuildOwner.TryGetTarget(out var owner) ? owner : null;

        /// <summary>True while a committed reorder's deferred rebuild is still owed — see <see cref="s_rebuildOwner"/>.</summary>
        private static bool RebuildPending
        {
            get
            {
                var owner = RebuildOwner;
                if (owner == null) return false;
                if (owner.panel != null) return true;
                s_rebuildOwner.SetTarget(null); // the tick can never come; release rather than latch
                return false;
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Offer gate
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// True when this table's columns are exactly the item template's own children, in order — the flat
        /// shape the designer's auto-fill produces and very nearly every table has.
        ///
        /// <para>The gate matters because a column is only movable if there is a model list to move it
        /// WITHIN. <c>CollectColumnBindings</c> flattens a NESTED template, so column 3 might be the second
        /// child of a sub-container: moving it "one to the right" is then not a single list operation, and
        /// the honest options are to guess or to decline. This declines — the grips never appear — and the
        /// designer canvas still arranges those layouts properly.</para>
        ///
        /// <para>A table whose columns were DERIVED from the element type (an item template with no
        /// bindings) is refused for the plainer reason that there is nothing authored to write back to.</para>
        /// </summary>
        internal static bool ColumnsAreArrangeable(CollectionNode node, IReadOnlyList<BindingNode> columns)
        {
            if (node == null || columns == null || columns.Count < 2) return false;
            if (!(node.ItemLayout is ContainerNode template) || template.Children == null) return false;
            if (template.Children.Count != columns.Count) return false;
            for (int i = 0; i < columns.Count; i++)
            {
                if (!ReferenceEquals(template.Children[i], columns[i])) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // Pure index / model helpers — no panel, so a test can drive them directly
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The column index a drag lands on, given every header's FROZEN world rect, which column is being
        /// dragged, and how far it has travelled in x. The dragged column is skipped, so this counts the
        /// OTHER headers it has passed.
        ///
        /// <para><b>It measures the COLUMN, not the pointer</b> — the fix for a genuinely awkward drag. The
        /// grip sits at the header's LEFT edge, so the pointer is at the dragged column's left edge for the
        /// whole gesture. Counting centres the POINTER had passed therefore charged the dragged column's own
        /// width to every rightward move: shoving a 194px column past a 231px neighbour (the reported
        /// table, read off its screenshot) needed
        /// <c>194 + 231/2 &#8776; 310px</c> of travel, while the same swap driven from the other side needed
        /// <c>194/2 &#8776; 97px</c>. Same two columns, same swap, three times the distance one way — which is
        /// exactly what "I have to drag it REALLY far before it swaps" is.</para>
        ///
        /// <para>The rule instead is the classic one: <b>the dragged column's LEADING EDGE in the direction
        /// that neighbour lies crosses the neighbour's centre.</b> Its right edge for a column to the right,
        /// its left edge for one to the left — which is to say, the swap fires when the column the user can
        /// see is halfway onto the one it is displacing. The dragged column's own width cancels out, so the
        /// cost of a swap is always half the NEIGHBOUR's width, in both directions, wide column or narrow.
        /// It is also the same distance the preview is about to move it, so committing at the halfway point
        /// is literally what it means.</para>
        ///
        /// <para>Still one threshold per neighbour and still monotone in <paramref name="dx"/> — because
        /// <paramref name="from"/> is the ORIGINAL index, frozen for the gesture, the answer is a plain
        /// step function of travel with no dead band to escape and nothing to oscillate between.</para>
        /// </summary>
        internal static int TargetIndex(IReadOnlyList<Rect> headerBounds, int from, float dx)
        {
            int n = headerBounds?.Count ?? 0;
            if (n == 0 || from < 0 || from >= n) return from;

            Rect self = headerBounds[from];
            float leadingLeft = self.xMin + dx;   // the edge that arrives first when travelling LEFT
            float leadingRight = self.xMax + dx;  // …and when travelling RIGHT

            int index = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == from) continue;
                float centre = headerBounds[i].center.x;
                bool passed = i < from ? leadingLeft > centre : leadingRight > centre;
                if (passed) index++;
            }
            return index;
        }

        /// <summary>
        /// How far each column has to slide, in x, to preview moving <paramref name="from"/> to
        /// <paramref name="to"/>. Index <paramref name="from"/> is left at 0 — the dragged column follows
        /// the pointer instead. <paramref name="lanes"/> is each column's full lane width, its own width
        /// plus the divider beside it, so the columns close up exactly rather than nearly.
        ///
        /// <para>Every displaced column moves by the DRAGGED column's lane, not by its own: the hole the
        /// dragged column leaves is one lane wide wherever it ends up, and it is that hole they are filling.</para>
        /// </summary>
        internal static float[] PreviewOffsets(IReadOnlyList<float> lanes, int from, int to)
        {
            int n = lanes?.Count ?? 0;
            var offsets = new float[n];
            if (n == 0 || from < 0 || from >= n || to == from) return offsets;
            to = Mathf.Clamp(to, 0, n - 1);

            float lane = lanes[from];
            if (to > from)
            {
                for (int c = from + 1; c <= to; c++) offsets[c] = -lane;
            }
            else
            {
                for (int c = to; c < from; c++) offsets[c] = lane;
            }
            return offsets;
        }

        /// <summary>
        /// How far the DRAGGED column itself has to end up from where it started, once it lands at
        /// <paramref name="to"/> — the total width of every lane it travels across. The mirror of
        /// <see cref="PreviewOffsets"/>: those columns each step aside by one dragged lane, and this one
        /// crosses all of theirs.
        ///
        /// <para>Used only on release. Mid-drag the column tracks the pointer, and on drop it springs from
        /// wherever the pointer left it to exactly this offset — which is where the rebuild is about to put
        /// it for real, so the glide ends on the truth rather than near it.</para>
        /// </summary>
        internal static float DraggedLandingOffset(IReadOnlyList<float> lanes, int from, int to)
        {
            int n = lanes?.Count ?? 0;
            if (n == 0 || from < 0 || from >= n || to == from) return 0f;
            to = Mathf.Clamp(to, 0, n - 1);

            float total = 0f;
            if (to > from)
            {
                for (int c = from + 1; c <= to; c++) total += lanes[c];
                return total;
            }
            for (int c = to; c < from; c++) total -= lanes[c];
            return total;
        }

        /// <summary>
        /// How far the dragged column is allowed to travel, so it can reach every slot but never paint
        /// outside the table: its left edge stops at the first column's left edge, its right edge at the
        /// last column's right edge.
        ///
        /// <para><b>Why it is clamped at all.</b> Dragged into the empty space right of the table, the
        /// column paints past the inspector's right edge — and <see cref="HorizontalOverflowRescue"/> polls
        /// for exactly that and raises its rescue rail along the bottom. That rail's whole contract is "a
        /// correct layout never shows this", so a drag ghost tripping it is the ghost misbehaving, not the
        /// rail. Clamping fixes it where it starts, and reads better besides: a column cannot be dragged
        /// somewhere it cannot go.</para>
        ///
        /// <para><b>The clamped offset is also what picks the target</b>, because <see cref="TargetIndex"/>
        /// measures the COLUMN and the clamp is exactly the column's travel. Nothing is lost: the two
        /// bounds land the column's left edge on the first header's left edge and its right edge on the
        /// last header's right edge, which resolve to index 0 and index n-1 respectively. Dragging further
        /// past either end simply saturates there, which is what every list does when you drag past its
        /// end. (It once read from the raw pointer instead — that is the bug <see cref="TargetIndex"/>
        /// documents, and unclamping was never what made the ends reachable.)</para>
        /// </summary>
        internal static float ClampDragOffset(IReadOnlyList<Rect> headerBounds, int from, float dx)
        {
            int n = headerBounds?.Count ?? 0;
            if (n == 0 || from < 0 || from >= n) return dx;
            float min = headerBounds[0].xMin - headerBounds[from].xMin;
            float max = headerBounds[n - 1].xMax - headerBounds[from].xMax;
            if (max < min) return 0f; // degenerate geometry: refuse to move rather than invert the clamp
            return Mathf.Clamp(dx, min, max);
        }

        /// <summary>Moves a column within the item template. Returns false when nothing would change.</summary>
        /// <summary>
        /// Whether <see cref="MoveColumn"/> would actually change anything — the same tests, without the
        /// mutation, so a caller can decide before it opens an Undo group.
        /// </summary>
        internal static bool CanMoveColumn(ContainerNode template, int from, int to)
        {
            if (template?.Children == null) return false;
            var kids = template.Children;
            if (from < 0 || from >= kids.Count) return false;
            return Mathf.Clamp(to, 0, kids.Count - 1) != from;
        }

        internal static bool MoveColumn(ContainerNode template, int from, int to)
        {
            if (template?.Children == null) return false;
            var kids = template.Children;
            if (from < 0 || from >= kids.Count) return false;
            int dest = Mathf.Clamp(to, 0, kids.Count - 1);
            if (dest == from) return false;
            var moved = kids[from];
            kids.RemoveAt(from);
            kids.Insert(dest, moved);
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // Wiring
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Gives one column header its hover grip and the drag that goes with it.
        /// <paramref name="columnCells"/> is the build's shared per-column element list — entry 0 is the
        /// header, the rest are that column's data cells — which is what lets a displaced column move as one
        /// piece. <paramref name="rebuild"/> must be the FULL inspector rebuild, not the rows-only one: a
        /// column move changes the header row and every cell.
        /// </summary>
        internal static void Attach(VisualElement headerCell, Label headerLabel, CollectionNode node,
            int columnIndex, List<VisualElement>[] columnCells, Action rebuild)
        {
            if (headerCell == null || headerLabel == null || node == null || columnCells == null) return;

            // A PAINTED glyph, never a text character — see the class docstring. Rotated a quarter turn so
            // the lines run vertically: this grip moves things sideways, and the row grips it borrows from
            // move things up and down.
            var grip = new VisualElement { name = GripClass };
            grip.AddToClassList(GripClass);
            grip.style.position = Position.Absolute;
            // A DEAD BAND on the left. The resize divider is a 7px-wide SIBLING occupying header-local
            // x in [-7, 0] with its hairline drawn at the centre (-3.5), so a grip starting at x = 1 left
            // barely a pixel between "drag to resize" and "drag to move". A user aiming at the visible
            // hairline and landing a few pixels right got a column MOVE — which rewrites the layout asset
            // and rebuilds the inspector — instead of a resize, with nothing arbitrating between them.
            // Starting the grip at GripDeadBand keeps the two gestures a comfortable miss apart.
            grip.style.left = GripDeadBand;
            grip.style.top = 0f;
            grip.style.bottom = 0f;
            grip.style.width = GripLane - GripDeadBand - 2f;
            grip.style.alignItems = Align.Center;
            grip.style.justifyContent = Justify.Center;
            grip.style.display = DisplayStyle.None;
            grip.tooltip = "Drag to move this column";

            var restTint = WidgetPalette.Ink(0.45f);
            var hoverTint = WidgetPalette.Ink(0.85f);
            var glyph = new GripGlyph { Tint = restTint };
            glyph.style.rotate = new Rotate(90f);
            grip.Add(glyph);
            grip.RegisterCallback<MouseEnterEvent>(_ => glyph.Tint = hoverTint);
            grip.RegisterCallback<MouseLeaveEvent>(_ => glyph.Tint = restTint);
            headerCell.Add(grip);

            // NO TOOLTIP MAY APPEAR WHILE A COLUMN IS IN FLIGHT — the list rule, applied here. An editor
            // tooltip anchors to the RECT of whichever element answers, and a dragged column's rects MOVE:
            // the grip stays under the cursor for the whole gesture, so its own caption pops up the instant
            // the user pauses and then re-anchors on every move, reading as a caption trailing the pointer.
            //
            // Installed on the header ROW, not on this cell, and that is the point: the mute walks ANCESTORS
            // for the marker, so one stamp silences every caption in the row — this grip, the other columns'
            // grips, and the resize dividers' "Drag to resize" between them, any of which the pointer can
            // cross mid-drag. Installed once however many columns there are.
            var headerRowForMute = headerCell.parent;
            if (headerRowForMute != null && !headerRowForMute.ClassListContains(MuteInstalledClass))
            {
                headerRowForMute.AddToClassList(MuteInstalledClass);
                DragTooltipMute.Install(headerRowForMute);
            }

            // The label's live left padding, and the value it RESTS at — which is not assumed to be zero. A
            // header label carries a little padding of its own, and springing home to a hard 0 would eat it,
            // shifting the text a pixel left the first time the pointer crossed. Read lazily on the first
            // reveal: at attach time the label has not been laid out and resolvedStyle has nothing to say.
            float lane = 0f;
            float rest = float.NaN;
            bool revealed = false;

            bool dragging = false;
            bool hovering = false;
            int target = columnIndex;
            int previewed = columnIndex;
            float grabX = 0f;
            float dragDx = 0f;          // the dragged column's live x offset, pointer- then spring-driven
            List<Rect> frozen = null;   // header rects, captured once at pointer-down
            List<float> lanes = null;   // each column's full lane width

            // Drop glide. The generation counter strands a superseded watcher so a re-press can never
            // double-Commit — the same guard, for the same reason, as CollectionRowReorder's settle.
            MotionHandleFloat settleHandle = null;
            bool settlePending = false;
            int settleTarget = columnIndex;
            int settleGeneration = 0;

            void EnsureRest()
            {
                if (!float.IsNaN(rest)) return;
                float resolved = headerLabel.resolvedStyle.paddingLeft;
                rest = float.IsNaN(resolved) ? 0f : resolved;
                lane = rest;
            }

            void SpringLane(float to)
            {
                if (!EditorSpringFX.MotionEnabled)
                {
                    lane = to;
                    headerLabel.style.paddingLeft = to;
                    return;
                }
                Spring.To((object)headerLabel,
                    () => lane,
                    v => { lane = v; headerLabel.style.paddingLeft = v; },
                    to, EditorSpringFX.Tune(LaneConfig), "col.grip.lane");
            }

            // Whether an edit made here could actually be SAVED. Resolved on first hover, never at Attach:
            // LayoutOwnerRegistry walks the element's ancestors, and the header cell is not in a tree yet
            // when Attach runs.
            //
            // REFUSES ONLY ON A POSITIVE "no". An inherited copy has no file of its own — both
            // BeginLayoutEditUndo and PersistLayoutEdit return immediately for one — so offering the grip
            // there promises a reorder that silently reverts at the next domain reload. But a scope that
            // simply cannot be resolved is NOT that answer, and treating it as one would quietly withdraw
            // the feature from any host the registry does not know about. Unknown keeps the grip; Commit
            // is the backstop that refuses for real.
            bool CanPersist()
            {
                var la = LayoutOwnerRegistry.ResolveScope(headerCell)?.LayoutAsset;
                return la == null || !la.IsInheritedCopy;
            }

            void Reveal()
            {
                if (revealed) return;
                // A drag is in flight somewhere: this header is only being crossed, not considered. See
                // s_dragInFlight. Note this leaves `revealed` false, so the re-arm in PointerMove below is
                // what brings the grip back once the gesture is over — no state to unwind.
                if (s_dragInFlight) return;
                // Offering a gesture that cannot be saved is worse than offering none — see Commit.
                if (!CanPersist()) return;
                revealed = true;
                EnsureRest();
                grip.style.display = DisplayStyle.Flex;
                SpringLane(rest + GripLane);
                // SCALE only. PopIn would start this at opacity 0, and a spring that never ticks would then
                // leave an invisible control rather than a merely un-animated one — fail visible.
                if (EditorSpringFX.MotionEnabled)
                {
                    grip.style.scale = new Scale(new Vector3(0.55f, 0.55f, 1f));
                    grip.SpringScale(1f, EditorSpringFX.Tune(PopConfig));
                }
            }

            void Conceal()
            {
                if (!revealed) return;
                revealed = false;
                EnsureRest();
                grip.style.display = DisplayStyle.None;
                SpringLane(rest);
            }

            headerCell.RegisterCallback<PointerEnterEvent>(_ => { hovering = true; Reveal(); });

            // RE-ARM, not a second reveal path. A header the pointer crossed DURING a drag was refused its
            // grip above, and PointerEnter has already been and gone — without this the offer would stay
            // missing until the pointer left and came back, which reads as the grip having broken. A
            // committed reorder rebuilds the table and moots it; a cancelled or no-op drop does not, and
            // that is the case this covers. Cheap: `revealed` short-circuits it on every ordinary move.
            headerCell.RegisterCallback<PointerMoveEvent>(_ =>
            {
                if (hovering && !revealed && !s_dragInFlight) Reveal();
            });
            headerCell.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hovering = false;
                // NOT mid-drag: the pointer leaves this header the instant the column starts travelling, and
                // collapsing the lane there would yank the grip out from under the cursor holding it.
                if (!dragging) Conceal();
            });

            // The header's own trailing element — the resize divider between this column and the next, or
            // the auto-fit lane after the last one. It is a SIBLING of the header cell in the header row,
            // added by LayoutTreeBuilder immediately after it, and so is not in columnCells: without this
            // the header cells slid and their divider hairlines stayed put, painting a vertical line
            // straight through the middle of a neighbouring header for the whole drag. Resolved live
            // rather than at Attach because the divider does not exist yet when Attach runs.
            VisualElement HeaderTrailerOf(int c)
            {
                if (c < 0 || c >= columnCells.Length) return null;
                var cells = columnCells[c];
                if (cells == null || cells.Count == 0) return null;
                var head = cells[0];           // entry 0 is the header (LayoutTreeBuilder adds it first)
                var hrow = head?.parent;
                if (hrow == null) return null;
                int i = hrow.IndexOf(head);
                if (i < 0 || i + 1 >= hrow.childCount) return null;
                return hrow.ElementAt(i + 1);
            }

            void ShiftElement(VisualElement el, float dx, bool spring)
            {
                if (el == null) return;
                if (spring && EditorSpringFX.MotionEnabled) el.SpringTranslate(new Vector2(dx, 0f), EditorSpringFX.Tune(SlideConfig));
                else el.style.translate = new Translate(dx, 0f);
            }

            /// <summary>
            /// Dims one column while a drag is in flight, so the column IN THE USER'S HAND is always the
            /// readable one.
            ///
            /// <para><b>Why not paint order.</b> UI Toolkit paints siblings in child order and has no
            /// z-index, so a column dragged rightwards is genuinely drawn UNDER the neighbours it passes —
            /// its opaque fields disappear behind theirs for the length of each spring. The row drag solves
            /// the same problem with BringToFront, but a row is floated into an overlay first; a column is
            /// still in flow, and reordering its siblings would physically MOVE it. Compensating that shift
            /// is possible but the header dividers and body spacers are not guaranteed the same width (the
            /// fused-column path widens a divider), so a miscalculation would misalign the header from its
            /// cells — strictly worse than the occlusion it fixes. Receding the passed columns instead costs
            /// no geometry and answers the actual complaint.</para>
            /// </summary>
            void SetColumnRecessed(int c, bool recessed)
            {
                if (c < 0 || c >= columnCells.Length || c == columnIndex) return;
                float o = recessed ? PassedColumnOpacity : 1f;
                foreach (var el in columnCells[c]) { if (el != null) el.style.opacity = o; }
                var trail = HeaderTrailerOf(c);
                if (trail != null) trail.style.opacity = o;
            }

            void SetAllColumnsRecessed(bool recessed)
            {
                for (int c = 0; c < columnCells.Length; c++) SetColumnRecessed(c, recessed);
            }

            // Every element of one column — its header, its header's divider, and every data cell — so a
            // displaced column moves as one piece.
            void ShiftColumn(int c, float dx, bool spring)
            {
                if (c < 0 || c >= columnCells.Length) return;
                foreach (var el in columnCells[c]) ShiftElement(el, dx, spring);
                ShiftElement(HeaderTrailerOf(c), dx, spring);
            }

            void ClearShifts()
            {
                for (int c = 0; c < columnCells.Length; c++)
                {
                    foreach (var el in columnCells[c]) ClearShift(el);
                    ClearShift(HeaderTrailerOf(c)); // the divider travelled too — see ShiftColumn
                }
                dragDx = 0f;
            }

            void ClearShift(VisualElement el)
            {
                if (el == null) return;
                el.CompleteSprings();
                el.style.translate = new Translate(0f, 0f);
            }

            // The dragged column's own offset, applied to its header and every one of its cells at once.
            void SetDragOffset(float dx)
            {
                dragDx = dx;
                ShiftColumn(columnIndex, dx, spring: false);
            }

            // Everything a drop has to do once the column has finished travelling. A committed move rebuilds
            // the inspector, which throws away every element this drag was moving — so the translations only
            // need clearing when nothing was committed, and clearing them BEFORE a commit would snap the
            // table back for one frame first.
            void FinishDrop(int landingTarget)
            {
                // Lifted BEFORE the commit: a committed move rebuilds the table and discards these
                // elements, but a DECLINED or no-op drop keeps them, and leaving them dimmed would strand a
                // half-faded table on screen.
                SetAllColumnsRecessed(false);
                bool moved = landingTarget != columnIndex
                             && Commit(headerCell, node, columnIndex, landingTarget, rebuild);
                if (!moved) ClearShifts();
                headerCell.RemoveFromClassList(DraggingClass);
                DragTooltipMute.SetDragging(headerCell.parent, false);
                if (!hovering) Conceal();
            }

            void DeregisterPendingSettle(Action self)
            {
                if (ReferenceEquals(s_pendingSettleAnywhere, self)) s_pendingSettleAnywhere = null;
                var row = headerCell.parent;
                if (row == null) return;
                if (s_pendingSettles.TryGetValue(row, out var cur) && cur == self) s_pendingSettles.Remove(row);
            }

            // Finish a still-running drop glide immediately — cancel the spring, run its deferred Commit
            // exactly once — so a new drag starting anywhere in this header row cannot orphan it.
            Action finalizeSettle = null;
            finalizeSettle = () =>
            {
                DeregisterPendingSettle(finalizeSettle);
                settleGeneration++; // strands the in-flight watcher
                if (settleHandle != null)
                {
                    settleHandle = null;
                    Spring.Cancel(headerCell, SettleKey);
                }
                if (!settlePending) return;
                settlePending = false;
                FinishDrop(settleTarget);
            };

            async void WatchSettle(MotionHandleFloat handle, int gen)
            {
                try
                {
                    await handle.WaitFor(AwaitStrategy.PhysicalSettling);
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // Fall through. If the cancel came from finalizeSettle the generation was bumped first
                    // and the check below strands us; any OTHER cancel source (a panel detach, an editor
                    // shutdown) must NOT swallow the deferred Commit — the user's reorder would silently
                    // revert after they had already seen the column land.
                }
                if (gen != settleGeneration) return;
                settleHandle = null;
                DeregisterPendingSettle(finalizeSettle);
                if (!settlePending) return;
                settlePending = false;
                FinishDrop(settleTarget);
            }

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                // If ANY drop in this header row is still gliding, finish it now. Each column owns its own
                // closure, so the table is the only way to reach a DIFFERENT column's pending Commit, and
                // leaving one in flight would let it fire against an arrangement this drag has changed.
                // ANY table first — s_pendingSettles only reaches this same header row, so a drop still
                // gliding in a DIFFERENT table would otherwise fire mid-gesture and rebuild the inspector
                // out from under this drag. See s_pendingSettleAnywhere.
                var pendingAnywhere = s_pendingSettleAnywhere;
                if (pendingAnywhere != null)
                {
                    s_pendingSettleAnywhere = null;
                    pendingAnywhere();
                }

                var headerRow = headerCell.parent;
                if (headerRow != null && s_pendingSettles.TryGetValue(headerRow, out var pendingElsewhere))
                {
                    s_pendingSettles.Remove(headerRow);
                    pendingElsewhere();
                }
                finalizeSettle(); // idempotent for our own column

                // Either finalizer may have committed a reorder, whose rebuild is now one tick out and will
                // replace every element this gesture is about to measure and capture. Starting here would
                // freeze rects from a dead tree. Refuse instead — the press is a no-op and the next one
                // lands on the fresh table. Same test for a cell already detached for any other reason.
                if (RebuildPending || headerCell.panel == null) return;

                // FREEZE the geometry. Every target decision for the rest of this gesture reads these rects,
                // never the live ones — the columns are about to start moving, and feeding that back into
                // the picker is how a preview starts chasing itself.
                frozen = new List<Rect>(columnCells.Length);
                for (int c = 0; c < columnCells.Length; c++)
                {
                    if (columnCells[c].Count == 0) { frozen = null; return; }
                    frozen.Add(columnCells[c][0].worldBound);
                }
                if (frozen.Count < 2) { frozen = null; return; }

                // Each column's full LANE: its own width plus the divider beside it, taken from the gap
                // between neighbours so the columns close up exactly rather than nearly. The last column has
                // no neighbour to measure against, so it borrows the gap the others share.
                lanes = new List<float>(frozen.Count);
                float gutter = Mathf.Max(0f, frozen[1].xMin - frozen[0].xMax);
                for (int c = 0; c < frozen.Count; c++)
                {
                    lanes.Add(c < frozen.Count - 1 ? frozen[c + 1].xMin - frozen[c].xMin : frozen[c].width + gutter);
                }

                dragging = true;
                s_dragInFlight = true; // no OTHER column may offer a grip while this one is travelling
                grabX = evt.position.x;
                dragDx = 0f;
                target = columnIndex;
                previewed = columnIndex;
                headerCell.AddToClassList(DraggingClass);
                SetAllColumnsRecessed(true); // the held column stays fully opaque — see SetColumnRecessed
                // Armed for the WHOLE gesture, which is not over at pointer-up: the column is still gliding
                // into its slot, still moving, and still under the cursor. Disarmed in FinishDrop.
                DragTooltipMute.SetDragging(headerCell.parent, true);
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || frozen == null) return;

                // The dragged column tracks the pointer exactly — no spring, because it is under the user's
                // hand and any easing there reads as lag.
                float dx = ClampDragOffset(frozen, columnIndex, evt.position.x - grabX);
                SetDragOffset(dx);

                // Targeted off the CLAMPED offset, i.e. off the column the user can actually see. The clamp
                // lands exactly on the first column's left edge and the last one's right edge, so every slot
                // is still reachable — dragging further past either end simply saturates, as it should.
                target = TargetIndex(frozen, columnIndex, dx);
                if (target != previewed)
                {
                    previewed = target;
                    var offsets = PreviewOffsets(lanes, columnIndex, target);
                    for (int c = 0; c < offsets.Length; c++)
                    {
                        if (c != columnIndex) ShiftColumn(c, offsets[c], spring: true);
                    }
                }
                evt.StopPropagation();
            });

            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                // Lowered at pointer-UP, not in FinishDrop: the drop GLIDE outlives the button release, and
                // holding the gate for it would mean a user who lets go and immediately moves to another
                // column watches its grip refuse to appear for the length of a spring. The tooltip mute is
                // held longer on purpose (a caption re-anchoring to a moving column is a real artefact); a
                // grip that appears on a column the pointer has genuinely settled on is not.
                s_dragInFlight = false;
                if (grip.HasPointerCapture(evt.pointerId)) grip.ReleasePointer(evt.pointerId);

                // THE LANDING. The column is wherever the pointer let go of it; it now GLIDES into the slot
                // it is about to occupy for real, exactly as a dropped row does. The data move waits for
                // that glide — committing mid-flight would rebuild the table out from under a column the
                // user is still watching travel. A drop that changes nothing glides home instead, which is
                // the same spring saying "that went nowhere".
                float landing = DraggedLandingOffset(lanes, columnIndex, target);
                if (!EditorSpringFX.MotionEnabled || Mathf.Abs(landing - dragDx) < 0.5f)
                {
                    FinishDrop(target);
                }
                else
                {
                    settleTarget = target;
                    settlePending = true;
                    int gen = ++settleGeneration;
                    settleHandle = Spring.To((object)headerCell,
                        () => dragDx,
                        SetDragOffset,
                        landing, EditorSpringFX.Tune(SettleConfig), SettleKey);
                    WatchSettle(settleHandle, gen);

                    // Expose this drop's finalizer to the rest of the header row (see PointerDown).
                    var row = headerCell.parent;
                    if (row != null)
                    {
                        s_pendingSettles.Remove(row);
                        s_pendingSettles.Add(row, finalizeSettle);
                        s_pendingSettleAnywhere = finalizeSettle; // reachable from a DIFFERENT table too
                    }
                }
                evt.StopPropagation();
            });

            // Lost capture (a focus change, a window blur, a rebuild under us) cancels: no data change, and
            // no glide either — there is nothing to land on when the gesture was taken away rather than
            // finished.
            grip.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!dragging) return;
                dragging = false;
                s_dragInFlight = false; // a gesture taken away must never strand the gate raised
                SetAllColumnsRecessed(false); // …nor a half-faded table
                headerCell.RemoveFromClassList(DraggingClass);
                DragTooltipMute.SetDragging(headerCell.parent, false);
                ClearShifts();
                if (!hovering) Conceal();
            });
        }

        // ---------------------------------------------------------------------------------------------
        // The commit — QuickEditPopup.Commit's pipeline, step for step
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true when the move was actually applied and the inspector rebuilt. The caller needs the
        /// answer: a commit that DECLINES leaves the table sitting in its previewed arrangement, and the
        /// translations have to be cleared by hand rather than swept away by a rebuild that never happened.
        /// </summary>
        private static bool Commit(VisualElement headerCell, CollectionNode node, int from, int to, Action rebuild)
        {
            if (!(node.ItemLayout is ContainerNode template)) return false;

            // The asset that OWNS this node — which is NOT always the inspected object's root layout: a
            // table rendered inside an in-line editor belongs to that nested layout, and routing its edit to
            // the outer asset would write the change into the wrong file.
            var scope = LayoutOwnerRegistry.ResolveScope(headerCell);
            var layout = scope?.LayoutAsset;
            if (layout?.Root == null) return false;

            var indexPath = QuickEditMenu.FindIndexPath(node, layout.Root);
            if (indexPath == null) return false; // a synthetic node, reachable from no layout root

            // An INHERITED COPY has no file of its own: both BeginLayoutEditUndo and PersistLayoutEdit
            // return immediately for one. Committing anyway moved the column in memory, rebuilt the table
            // around the new order, recorded nothing and saved nothing — so the arrangement silently
            // reverted at the next domain reload with no explanation. Refuse here, and the grip is
            // withheld up front (see CanPersist in Attach) so the user is never offered the gesture.
            if (layout.IsInheritedCopy) return false;

            // VALIDATED FIRST. BeginLayoutEditUndo runs Undo.RegisterCompleteObjectUndo, so opening it
            // before MoveColumn could decline left an empty "Reorder Table Columns" step on the stack —
            // the user's next Ctrl+Z then appeared to do nothing and their real edit needed a second press.
            if (!CanMoveColumn(template, from, to)) return false;

            LayoutRegistry.BeginLayoutEditUndo(layout, "Reorder Table Columns");
            if (!MoveColumn(template, from, to)) return false;

            // A held-open cell is a TRACK ORDINAL, so moving a child can invalidate one. The designer's own
            // RefreshAll never runs for a live edit, so normalizing here is what keeps the invariant true in
            // the SAVED asset — the same reason QuickEditPopup.Commit does it.
            EmptyCellFill.Normalize(layout.Root);
            LayoutRegistry.PersistLayoutEdit(layout);

            // Hand the edit to any designer window already holding this type. It works on a CLONE taken
            // before this commit, and its Save writes that clone's whole Root back — so without this, the
            // edit is destroyed by a Save the user has every reason to trust.
            var owner = scope?.Target;
            SasquatchLayoutDesignerWindow.AdoptLiveLayoutEdit(owner != null ? owner.GetType() : null, indexPath);

            // DEFERRED, one tick — see s_rebuildOwner. Everything above this line is the model change and
            // has already happened; all that is left is the teardown, and running THAT inside the pointer
            // event that asked for it destroys the element the event is still being dispatched through.
            //
            // Scheduled on the header cell, so it dies with the inspector rather than outliving it. If the
            // cell has no panel there is no scheduler to run on and nothing on screen to tear down — but the
            // asset HAS changed, so the rebuild is still invoked directly rather than dropped: a caller that
            // re-hosts the same layout must not be handed a stale tree.
            if (rebuild == null) return true;
            if (headerCell.panel == null)
            {
                rebuild();
                return true;
            }
            s_rebuildOwner.SetTarget(headerCell);
            headerCell.schedule.Execute(() =>
            {
                if (ReferenceEquals(RebuildOwner, headerCell)) s_rebuildOwner.SetTarget(null);
                rebuild();
            }).ExecuteLater(0);
            return true;
        }
    }
}
