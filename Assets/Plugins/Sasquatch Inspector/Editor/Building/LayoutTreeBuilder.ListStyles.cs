using System;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    // The List renderer's look, split along TWO orthogonal axes so they never have to be reasoned about
    // together:
    //
    //   CollectionListStyle    → the CONTAINER: shell, index gutter, density, ordinal treatment.
    //   CollectionRowSeparator → the ROW SURFACE: bare / hairline / tray. (ApplyRowSeparator, main file.)
    //
    // Every style composes with every frame. There is exactly ONE interaction between them — Timeline owns
    // the left edge, so a Card frame inside it drops the tray's left rail (ApplyRowSeparator's
    // suppressRail) rather than drawing two rails a few pixels apart.
    //
    // What is NOT a style choice, and is therefore drawn identically by all three: the ordinal gutter whose
    // lane doubles as the drag handle, hover-revealed row actions, the header count pill, the one-line
    // search+sort toolbar, the footer add strip, and the framed empty state. Those replaced the historical
    // List chrome outright (an always-visible "-" per row, "[0]" brackets, three stacked full-width stock
    // controls) because they are better everywhere, not because they suit one look.
    public static partial class LayoutTreeBuilder
    {
        // --- Marker classes ---------------------------------------------------------------------------
        // The per-row style classes must sit AFTER .sasq-list-row in the sheet's SOURCE ORDER: they are
        // single-class selectors exactly like it, so specificity ties and order is the only reason a style
        // can drop the base row treatment.
        internal const string ListRowClass = "sasq-list-row";
        // The table twin. Given a name because it now has a READER as well as its two writers
        // — CollectionRowMenu walks the ancestor chain looking for it, and a walk that reads a
        // literal the writers could rename is the shape this codebase keeps getting bitten by.
        // NOT a prefix of TableRowActionClass as far as ClassListContains is concerned: that
        // matches whole class names, so "sasq-table-row-action" never answers to this.
        internal const string TableRowClass = "sasq-table-row";
        internal const string ListShellClass = "sasq-list-shell";
        // "This header must never grow a bottom seam." Carried on the ELEMENT rather than passed to
        // SyncHeaderSeam, because the seam is rewritten from two toggle call sites that run long after the
        // shell was styled — a parameter would have to be threaded through both, and a third one added later
        // would default to wrong. See ListStyleSuppressesHeaderSeam.
        internal const string NoHeaderSeamClass = "sasq-no-header-seam";

        internal const string ListGutterClass = "sasq-list-gutter";
        // Only on a lane that actually CONTAINS a handle. The ordinal's hover fade-out is scoped to this: an
        // un-draggable collection has no grip to swap in, so an unscoped rule would just make the number
        // vanish under the pointer.
        internal const string ListGutterDraggableClass = "sasq-list-gutter--draggable";
        internal const string ListGutterGripClass = "sasq-list-gutter-grip";
        // ALSO the "this index label belongs to a LIST row" flag — a TABLE row carries the same
        // CollectionRowReorder.IndexLabelClass and keeps its brackets, so the drop-time relabel keys off
        // this rather than off the collection's style (see FormatIndexLabel).
        internal const string ListOrdinalClass = "sasq-list-ordinal";
        internal const string ListNodeClass = "sasq-list-node";      // Timeline: the disc behind the ordinal
        internal const string ListRailClass = "sasq-list-rail";      // Timeline: the continuous vertical rail
        internal const string ListCountPillClass = "sasq-list-count";
        internal const string ListToolbarLineClass = "sasq-list-toolbar-line";
        internal const string ListAddFooterClass = "sasq-list-add-footer";
        internal const string ListEmptyBoxClass = "sasq-list-empty";
        // The per-row remove control. A STABLE identity for it matters: before this existed, every test that
        // wanted "the row's delete" found it by `b.text == "-"`, so turning the glyph into an icon broke a
        // dozen tests that had nothing to do with the change. Query the class, never the glyph.
        internal const string ListRemoveClass = "sasq-list-remove";

        // Gutter width. Timeline needs more: it holds a rail AND a node disc, where the others hold only a
        // right-aligned number.
        private const float ListGutterWidth = 26f;
        private const float ListGutterWidthTimeline = 34f;
        // Where Timeline's rail sits inside the gutter, and how big the node on it is. The rail is centred
        // under the node so the two read as one object.
        private const float ListNodeSize = 16f;
        private const float ListRailWidth = 1f;
        // ROW CHROME IS CENTRED ON THE ROW. The row itself is `align-items: FlexStart` and must stay that
        // way — a row is only as tall as its tallest CELL, so centring the cells sinks the short ones (see
        // TestCollectionRowsTopAlignTheirCells). But the ordinal, the drag grip and the ✕ are not cells:
        // they are the row's chrome, and they were each pinned a couple of px from the row's TOP edge. On a
        // multi-field row — the common case, an item template with three fields — that left all three
        // huddled against the top edge while the body they belong to filled 90px below them, which is what
        // was reported. They now centre on the row's cross axis, so the three read as one band across a row
        // of any height. The grip already did this (`.sasq-reorder-grip` pins `align-self: center`), which
        // is exactly why it was the odd one out.
        //
        // Mechanism per piece, since all three are positioned differently:
        //   ordinal   — absolute, pinned top AND bottom, so the lane's own `-unity-text-align: middle-*`
        //               centres the glyph. (A fixed height + a `top` offset cannot centre.)
        //   grip      — its host fills the lane (the drag target is the whole gutter), so `justify-content`
        //               is what places the glyph inside it.
        //   ✕ / ▲▼    — plain flex children of the row, so `align-self: center` (StyleTableRowButton).
        // Breathing room between the fields and the ✕. At 1px the button read as part of the field.
        private const float ListRowActionGap = 6f;

        /// <summary>The per-row marker class for a style.</summary>
        internal static string ListRowStyleClass(CollectionListStyle style)
        {
            switch (style)
            {
                case CollectionListStyle.Minimal: return "sasq-list-row--minimal";
                case CollectionListStyle.Timeline: return "sasq-list-row--timeline";
                default: return "sasq-list-row--rows";
            }
        }

        /// <summary>The outer-container marker class for a style.</summary>
        internal static string ListShellStyleClass(CollectionListStyle style)
        {
            switch (style)
            {
                case CollectionListStyle.Minimal: return "sasq-list--minimal";
                case CollectionListStyle.Timeline: return "sasq-list--timeline";
                default: return "sasq-list--rows";
            }
        }

        /// <summary>
        /// The text for a row's index label. LIST ordinals drop the brackets; everything else — notably a
        /// TABLE row, which carries the same <see cref="CollectionRowReorder.IndexLabelClass"/> — keeps
        /// them. Asked of the LABEL rather than of the collection so a drop-time relabel can never reformat
        /// a surface the current build didn't own.
        /// </summary>
        internal static string FormatIndexLabel(Label label, int index)
        {
            return label != null && label.ClassListContains(ListOrdinalClass)
                ? index.ToString()
                : $"[{index}]";
        }

        /// <summary>
        /// Timeline owns the row's left edge, so a Card frame there must not also draw the tray's 3px rail —
        /// two rails a few pixels apart reads as a rendering bug. The single decision point, so neither the
        /// live renderer nor a future export twin can forget it (the twin of Table's Card→Line degrade).
        /// </summary>
        internal static bool ListStyleSuppressesRowRail(CollectionListStyle style)
            => style == CollectionListStyle.Timeline;

        /// <summary>
        /// The Timeline rail's geometry handler, per rows container — so re-attaching the rail REPLACES it
        /// instead of stacking another one.
        ///
        /// <para>The rows container outlives its rows: an in-place rebuild (a keystroke in the search box, a
        /// page turn, a sort) <c>Clear()</c>s it and repopulates. That drops the old rail, but a callback
        /// registered on the CONTAINER survives — <c>Clear()</c> removes children, it does not unregister
        /// anything. So every rebuild added another <c>Cap</c>, and each stale one stayed live: a full row
        /// scan on the next geometry event, writing the result to a rail that had been detached. Ten
        /// characters typed into the search box left ten handlers, nine of them working for nobody.</para>
        ///
        /// <para>Weakly keyed, so a container that goes away takes its entry with it.</para>
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, RailCapHolder>
            s_timelineRailCaps = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, RailCapHolder>();

        private sealed class RailCapHolder { public EventCallback<GeometryChangedEvent> Handler; }

        /// <summary>
        /// Timeline's recessed well runs unbroken BEHIND the header — that is the whole point of the shell —
        /// so the 1px separator every other expandable list draws under its header would cut a hard
        /// <c>#1e1e1e</c> line straight across it. Rows keeps the seam (its shell outline frames the box) and
        /// Minimal keeps it too (a light rule is that style's only structure), so Timeline is alone here.
        ///
        /// <para>The single decision point, for the same reason as
        /// <see cref="ListStyleSuppressesRowRail"/>: the live renderer and the export kit both ask it.</para>
        /// </summary>
        internal static bool ListStyleSuppressesHeaderSeam(CollectionListStyle style)
            => style == CollectionListStyle.Timeline;

        // --- Shell ------------------------------------------------------------------------------------

        /// <summary>
        /// Restyles the collection's outer box + header bar for the style. The expandable branch writes its
        /// shell borders INLINE, which no stylesheet can override, so anything that removes or recolors them
        /// has to happen here rather than in USS.
        /// </summary>
        internal static void ApplyListShellStyle(
            CollectionListStyle style, VisualElement container, VisualElement headerRow,
            VisualElement contentTarget)
        {
            if (container == null) return;
            container.AddToClassList(ListShellClass);
            container.AddToClassList(ListShellStyleClass(style));
            // Stamped here, where the style is known, and read by SyncHeaderSeam on every later toggle.
            // EnableInClassList rather than AddToClassList so a re-style onto a REUSED header (Timeline ->
            // Rows) takes the seam back, instead of leaving the element permanently seamless.
            if (headerRow != null)
                headerRow.EnableInClassList(NoHeaderSeamClass, ListStyleSuppressesHeaderSeam(style));

            switch (style)
            {
                case CollectionListStyle.Minimal:
                    // No box at all — a rule under the header is the only structure.
                    SetShellBorderWidth(container, 0);
                    container.style.paddingLeft = 0;
                    container.style.paddingRight = 0;
                    if (headerRow != null)
                    {
                        headerRow.style.backgroundColor = Color.clear;
                        headerRow.style.paddingLeft = 0;
                        headerRow.style.borderBottomWidth = 1;
                        headerRow.style.borderBottomColor = WidgetPalette.RowDivider;
                    }
                    if (contentTarget != null && contentTarget != container)
                    {
                        contentTarget.style.backgroundColor = Color.clear;
                        contentTarget.style.paddingLeft = 0;
                        contentTarget.style.paddingRight = 0;
                    }
                    break;

                case CollectionListStyle.Timeline:
                    // A RECESSED WELL behind the rail. Without it the rail floated on nothing and the style
                    // read as a variant of Minimal — the darker body is what gives the rail and its nodes
                    // something to sit ON, and it is the whole reason a separate "Well" style was not needed.
                    //
                    // The well goes on the CONTAINER, not on the content area. A non-expandable collection
                    // has contentTarget == container, so the header sits INSIDE the dark panel there and the
                    // whole thing reads as one surface; an expandable one has a separate content element, so
                    // tinting only that left the header floating above a detached rounded box. Painting the
                    // container gives both shapes the same unbroken panel, header included.
                    SetShellBorderWidth(container, 0);
                    container.style.backgroundColor = WidgetPalette.WellBg;
                    container.style.borderTopLeftRadius = 4;
                    container.style.borderTopRightRadius = 4;
                    container.style.borderBottomLeftRadius = 4;
                    container.style.borderBottomRightRadius = 4;
                    if (headerRow != null)
                    {
                        // Transparent so the well reads straight through the header bar.
                        headerRow.style.backgroundColor = Color.clear;
                        // Correct FIRST paint only — this runs once, while SyncHeaderSeam writes the seam
                        // back on every collapse/expand. NoHeaderSeamClass (stamped above) is what makes it
                        // stick; without it one collapse+reopen left a permanent hairline across the well.
                        headerRow.style.borderBottomWidth = 0;
                    }
                    if (contentTarget != null)
                    {
                        contentTarget.style.paddingBottom = 6;
                        // .sasq-group-content paints its own fill, which would cover the container's well.
                        // (Only the expandable shape has a separate content element to clear.)
                        if (contentTarget != container) contentTarget.style.backgroundColor = Color.clear;
                        else contentTarget.style.paddingTop = 6;
                    }
                    break;

                // Rows: the historical shell is already what this style wants.
            }
        }

        private static void SetShellBorderWidth(VisualElement container, float w)
        {
            container.style.borderTopWidth = w;
            container.style.borderBottomWidth = w;
            container.style.borderLeftWidth = w;
            container.style.borderRightWidth = w;
        }

        /// <summary>
        /// Moves the element count out of the header's title STRING and into a pill beside it, and restyles
        /// the "+" as a quiet icon button. Returns the pill so the in-place rebuild can retext it, or null
        /// for a deliberately blanked title — a hidden title takes its count with it, exactly as the
        /// historical header text did.
        /// </summary>
        internal static Label ApplyListHeaderStyle(
            CollectionNode node, SerializedProperty prop,
            VisualElement headerRow, Label titleLabel, Button addBtn, bool templateNote)
        {
            if (headerRow == null || titleLabel == null) return null;

            StyleListAddButton(addBtn);

            string title = ResolveCollectionTitle(node, prop);
            titleLabel.text = title;
            // Nothing to sit a pill next to. The label element still has to exist and still has to be the
            // flexGrow spacer holding the "+" at the right edge, so it is left exactly as-is.
            if (string.IsNullOrEmpty(title)) return null;

            // The title stops growing so the pill can sit against it; a dedicated spacer takes over holding
            // the "+" out at the right edge. flexShrink + hidden overflow keep a long title from shoving the
            // pill off the end instead of ellipsing itself.
            titleLabel.style.flexGrow = 0;
            titleLabel.style.flexShrink = 1;
            titleLabel.style.overflow = Overflow.Hidden;

            var pill = new Label(templateNote ? "tmpl" : prop.arraySize.ToString());
            pill.AddToClassList(ListCountPillClass);
            pill.pickingMode = PickingMode.Ignore;

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1;

            int at = headerRow.IndexOf(titleLabel);
            headerRow.Insert(at + 1, pill);
            headerRow.Insert(at + 2, spacer);
            return pill;
        }

        /// <summary>The header "+" as a quiet icon button — the footer strip is the loud add affordance now,
        /// and two shouty ones compete.</summary>
        private static void StyleListAddButton(Button addBtn)
        {
            if (addBtn == null) return;
            addBtn.text = string.Empty;
            addBtn.Clear();
            addBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Plus, 11, WidgetPalette.LabelMuted));
            addBtn.AddToClassList("sasq-list-add-header");
            addBtn.tooltip = "Add an item";
            // CreateCollectionAddButton pins 8px of horizontal padding INLINE, which no stylesheet can
            // override — clear it or the icon sits in a text button's worth of empty space.
            addBtn.style.paddingLeft = 4;
            addBtn.style.paddingRight = 4;
        }

        /// <summary>
        /// The one-line search+sort strip, and the row that both controls are added to. The caller must also
        /// route its search-box RE-creation here: the box is destroyed and rebuilt as a list crosses the 1↔2
        /// element boundary, and putting it back in the toolbar column would silently revert the layout
        /// mid-session.
        /// </summary>
        internal static VisualElement MakeListToolbarLine(VisualElement toolbar)
        {
            if (toolbar == null) return null;
            var line = new VisualElement();
            line.AddToClassList(ListToolbarLineClass);
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;

            // NOTHING IN THIS STRIP MAY WRAP. It is one line of fixed-height controls, and a wrapped one
            // does not merely look wrong — Yoga measures wrapped text one character per line at small
            // widths and takes that as the flex LINE's cross size, so the ROW reports a height that no
            // child of it carries (a full subtree walk finds nothing tall, which is what made this so hard
            // to attribute). MEASURED with no frame involved: the row went 20px -> 747px the moment its
            // width crossed under ~90px. Inside a DiagonalSplit frame that closed a loop, because pane
            // width is a function of frame HEIGHT (seam pad carries tan(angle)*h/2): taller frame -> wider
            // seam pad -> narrower pane -> taller row -> taller frame. It ran a frame to 4,211,400px.
            //
            // Set on the LINE (covering every control, present and future) and re-applied on attach and on
            // the first geometry pass, because the controls here materialize their inner TextElements
            // lazily: a popup's value text does not exist at construction, so a one-shot walk misses it —
            // that near-miss is why the first cut of this fix moved the cliff instead of removing it.
            // It must be written on the TEXT ELEMENTS themselves, never on their field: Unity's own
            // stylesheet sets white-space on those labels, and a USS rule beats an inherited value, so
            // setting it on the field alone is silently a no-op.
            void NoWrapEveryLabel()
            {
                foreach (var te in line.Query<TextElement>().ToList())
                {
                    te.style.whiteSpace = WhiteSpace.NoWrap;
                    te.style.overflow = Overflow.Hidden;
                    te.style.textOverflow = TextOverflow.Ellipsis;
                }
            }
            line.RegisterCallback<AttachToPanelEvent>(_ => NoWrapEveryLabel());
            line.RegisterCallback<GeometryChangedEvent>(_ => NoWrapEveryLabel());

            toolbar.Add(line);
            return line;
        }

        // --- Rows -------------------------------------------------------------------------------------

        /// <summary>
        /// Row geometry a style owns that <see cref="ApplyRowSeparator"/> also writes. That method sets
        /// padding and margins INLINE (so a stylesheet cannot reach them), and Minimal's whole identity is
        /// air — so its extra breathing room is applied here, AFTER the separator pass. Every other style
        /// defers to the frame entirely.
        /// </summary>
        internal static void ApplyListRowStyleGeometry(CollectionListStyle style, VisualElement itemRow)
        {
            if (itemRow == null || style != CollectionListStyle.Minimal) return;
            itemRow.style.paddingTop = 5f;
            itemRow.style.paddingBottom = 5f;
        }

        /// <summary>
        /// The ordinal lane: a fixed-width, FULL-ROW-HEIGHT gutter holding the index, with the drag handle
        /// stacked on top of it. Hover cross-fades the number out and the grip in, so the handle needs no
        /// column of its own, the lane never changes width, and nothing reflows as the pointer travels down
        /// the list — while the drag target grows from a 14x7 nub to the whole gutter.
        /// <para>
        /// Timeline additionally gets a node disc behind that stack, which is what the continuous rail
        /// threads through.
        /// </para>
        /// </summary>
        internal static VisualElement MakeListGutter(
            CollectionListStyle style, int index, bool withGrip, out VisualElement gripTarget, out Label ordinal)
        {
            gripTarget = null;
            bool timeline = style == CollectionListStyle.Timeline;

            var lane = new VisualElement();
            lane.AddToClassList(ListGutterClass);
            lane.style.width = timeline ? ListGutterWidthTimeline : ListGutterWidth;
            lane.style.flexShrink = 0;
            // Stretch, not a fixed height: the gutter's edge (or Timeline's rail) is meant to be ONE
            // unbroken line down the row, and a fixed-height lane leaves a stub beside a tall row.
            lane.style.alignSelf = Align.Stretch;

            if (timeline)
            {
                // The disc the ordinal sits in, in a host that FILLS the lane — the disc has to track the
                // ordinal exactly (the number is drawn inside it), so both are centred by the same lane
                // rather than by two hand-tuned offsets that can drift apart. Centred on the rail's x too,
                // so the disc and the rail read as one object.
                var nodeHost = new VisualElement { pickingMode = PickingMode.Ignore };
                nodeHost.style.position = Position.Absolute;
                nodeHost.style.left = 0;
                nodeHost.style.right = 0;
                nodeHost.style.top = 0;
                nodeHost.style.bottom = 0;
                nodeHost.style.alignItems = Align.Center;
                nodeHost.style.justifyContent = Justify.Center;

                var node = new VisualElement { pickingMode = PickingMode.Ignore };
                node.AddToClassList(ListNodeClass);
                node.style.width = ListNodeSize;
                node.style.height = ListNodeSize;
                node.style.flexShrink = 0;
                nodeHost.Add(node);
                lane.Add(nodeHost);
            }

            ordinal = new Label(index.ToString());
            ordinal.AddToClassList(CollectionRowReorder.IndexLabelClass);
            ordinal.AddToClassList(ListOrdinalClass);
            ordinal.pickingMode = PickingMode.Ignore;
            // TOP AND BOTTOM, not top + a fixed height: the label then spans the lane and the sheet's
            // `-unity-text-align: middle-right` (middle-center under Timeline) centres the number on the
            // row. A 16px box at a 2px offset cannot centre — it can only sit at the top.
            ordinal.style.position = Position.Absolute;
            ordinal.style.left = 0;
            ordinal.style.right = 0;
            ordinal.style.top = 0;
            ordinal.style.bottom = 0;
            lane.Add(ordinal);

            if (withGrip)
            {
                lane.AddToClassList(ListGutterDraggableClass);
                var gripHost = new VisualElement();
                gripHost.AddToClassList(ListGutterGripClass);
                gripHost.style.position = Position.Absolute;
                gripHost.style.left = 0;
                gripHost.style.right = 0;
                gripHost.style.top = 0;
                gripHost.style.bottom = 0;
                gripHost.tooltip = "Drag to reorder";
                // Glyph CENTRED in the lane, because the ordinal it cross-fades with is centred too — the
                // two occupy the same lane and swapping one for the other must not move the eye. The HIT
                // AREA is still the lane's full height.
                gripHost.style.alignItems = Align.Center;
                gripHost.style.justifyContent = Justify.Center;
                gripHost.Add(MakeReorderGrip());
                lane.Add(gripHost);
                gripTarget = gripHost;
            }

            return lane;
        }

        /// <summary>
        /// Timeline's continuous rail: one absolutely-positioned line behind the rows, threaded through
        /// every node disc. Built per row-population pass and added to the rows container itself — the
        /// container is Cleared on every in-place rebuild, so anything that must survive lives outside it
        /// and anything rebuilt with the rows lives inside.
        /// <para>
        /// It is CAPPED to the first and last node rather than run edge to edge: a rail that continues past
        /// the last row reads as "there's more below", the same reason the last row never gets a hairline.
        /// The cap needs real geometry, so it is applied on the rows container's next layout.
        /// </para>
        /// </summary>
        internal static void AttachTimelineRail(
            CollectionListStyle style, VisualElement itemsContainer, int rowCount)
        {
            if (style != CollectionListStyle.Timeline || itemsContainer == null || rowCount <= 0) return;

            var rail = new VisualElement { pickingMode = PickingMode.Ignore };
            rail.AddToClassList(ListRailClass);
            rail.style.position = Position.Absolute;
            rail.style.left = (ListGutterWidthTimeline - ListRailWidth) * 0.5f;
            rail.style.width = ListRailWidth;
            // Hidden until it has been measured, so it never flashes at full height for a frame.
            rail.style.top = 0;
            rail.style.height = 0;
            itemsContainer.Insert(0, rail);

            // Memo for the row scan below. Finding the first and last node dots costs one subtree query PER
            // ROW, and Cap runs on every relayout — so a 25-row list re-queried 25 subtrees every time the
            // inspector's geometry moved, to re-derive two elements that only change when the rows do.
            //
            // The key is exact rather than approximate: rows are only ever added, removed, reordered or paged
            // by a rebuild that Clear()s and repopulates itemsContainer wholesale, and every one of those
            // replaces its first or last child with a different object. So (childCount, first child, last
            // child) identifies the row set the memo was taken from. The memo is also dropped the moment
            // either cached node leaves the tree, so a stale one can never be measured.
            int capCount = -1;
            VisualElement capFirstChild = null, capLastChild = null;
            VisualElement capFirstNode = null, capLastNode = null;

            void Cap(GeometryChangedEvent _ = null)
            {
                int count = itemsContainer.childCount;
                var firstChild = count > 0 ? itemsContainer[0] : null;
                var lastChild = count > 0 ? itemsContainer[count - 1] : null;

                VisualElement firstNode, lastNode;
                if (count == capCount && ReferenceEquals(firstChild, capFirstChild)
                    && ReferenceEquals(lastChild, capLastChild)
                    && capFirstNode != null && capFirstNode.panel != null
                    && capLastNode != null && capLastNode.panel != null)
                {
                    firstNode = capFirstNode;
                    lastNode = capLastNode;
                }
                else
                {
                    firstNode = null;
                    lastNode = null;
                    foreach (var row in itemsContainer.Children())
                    {
                        // Rows carry their array index in userData; the rail and any note labels do not.
                        if (!(row.userData is int)) continue;
                        var node = row.Q<VisualElement>(className: ListNodeClass);
                        if (node == null) continue;
                        if (firstNode == null) firstNode = node;
                        lastNode = node;
                    }
                    capCount = count;
                    capFirstChild = firstChild;
                    capLastChild = lastChild;
                    capFirstNode = firstNode;
                    capLastNode = lastNode;
                }

                if (firstNode == null || lastNode == null) { rail.style.height = 0; return; }

                float top = firstNode.worldBound.center.y - itemsContainer.worldBound.yMin;
                float bottom = lastNode.worldBound.center.y - itemsContainer.worldBound.yMin;
                if (float.IsNaN(top) || float.IsNaN(bottom) || bottom <= top) { rail.style.height = 0; return; }
                rail.style.top = top;
                rail.style.height = bottom - top;
            }

            // Re-cap on every relayout, not once: rows change height when an in-line editor opens, when the
            // inspector is resized, and while a drag reflows the placeholder.
            //
            // ...but exactly ONE handler per container. This method runs again on every in-place rebuild,
            // and the container survives them all — see s_timelineRailCaps for what stacking them cost.
            if (s_timelineRailCaps.TryGetValue(itemsContainer, out var previous) && previous.Handler != null)
                itemsContainer.UnregisterCallback<GeometryChangedEvent>(previous.Handler);
            EventCallback<GeometryChangedEvent> capHandler = Cap;
            itemsContainer.RegisterCallback<GeometryChangedEvent>(capHandler);
            s_timelineRailCaps.Remove(itemsContainer);
            s_timelineRailCaps.Add(itemsContainer, new RailCapHolder { Handler = capHandler });
            Cap();
        }

        /// <summary>
        /// The per-row delete as a hover-revealed ✕ icon, matching the Table row and the UnityEvent card.
        /// </summary>
        internal static void StyleListDeleteButton(Button delBtn, bool hoverReveal)
        {
            if (delBtn == null) return;
            delBtn.AddToClassList(ListRemoveClass);
            delBtn.text = string.Empty;
            delBtn.Clear();
            delBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.X, 9, WidgetPalette.LabelMuted));
            delBtn.tooltip = "Remove this item";
            // StyleTableRowButton pins a 14x14 box; clear the inline padding first or it fights that.
            delBtn.style.paddingLeft = 0;
            delBtn.style.paddingRight = 0;
            StyleTableRowButton(delBtn, hoverReveal);
            // …then place it, AFTER the styler (which writes its own margins). The VERTICAL placement is the
            // styler's job now — it sets `align-self: center` on every row-action button, so the ✕ tracks
            // the grip and the ordinal at the row's middle instead of hugging its top edge.
            delBtn.style.marginLeft = ListRowActionGap;
            delBtn.style.marginRight = 2;
        }

        // --- Sort direction ---------------------------------------------------------------------------

        /// <summary>
        /// The sort-direction toggle. Hidden outright unless a sort is active — a permanently-disabled
        /// control is pure noise, and there is nothing to reserve space for, so this collapses (unlike the
        /// row actions, which fade so the columns never twitch). When active it takes the accent, matching
        /// the "Sorted by X · reorder paused" note beside it so the two read as ONE state.
        /// <para>
        /// Deliberately NOT amber: in this package amber means "this pointed somewhere and no longer does"
        /// (the UnityEvent card's stale-reference signal). An active sort is engaged, not broken.
        /// </para>
        /// </summary>
        internal static void UpdateSortDirectionButton(Button sortDirBtn, bool sorted, bool descending, bool interactive)
        {
            if (sortDirBtn == null) return;
            sortDirBtn.style.display = sorted ? DisplayStyle.Flex : DisplayStyle.None;
            if (!sorted) return;

            // ONE arrow that SPINS a half turn between ascending and descending, rather than two icons
            // swapped in place — a swap says nothing about which way it just went. The glyph is a little
            // larger than a row action's: this button carries a soft accent fill, and a 9px arrow floating
            // in it read as an empty blue chip.
            var icon = sortDirBtn.Q<SasquatchIconElement>();
            if (icon == null)
            {
                sortDirBtn.Clear();
                icon = SasquatchIcons.Create(SasquatchIcons.Ids.ArrowUp, 11, WidgetPalette.AccentIcon);
                sortDirBtn.Add(icon);
                sortDirBtn.userData = null; // no prior direction yet — the first paint must snap, not spin
            }
            // Animate ONLY on a real direction change. UpdateListSortUi re-runs on every search / page /
            // rebuild, and spinning the arrow on each of those would make it twitch constantly.
            bool spin = sortDirBtn.userData is bool was && was != descending;
            sortDirBtn.userData = descending;
            EditorSpringFX.SpringFlipArrow(icon, descending, spin);

            sortDirBtn.tooltip = descending ? "Sorting descending — click for ascending"
                                            : "Sorting ascending — click for descending";
            sortDirBtn.SetEnabled(interactive);
        }

        /// <summary>
        /// One-time styling for the sort-direction toggle: a SOFT accent button (tinted fill + accent
        /// border) rather than a bare glyph, so the control reads as a button you can press and some of the
        /// accent is visible around the arrow. Stretched to its row so it matches the height of the sort
        /// dropdown beside it — a short button next to a full-height field looks unfinished.
        /// </summary>
        internal static void StyleSortDirectionButton(Button sortDirBtn)
        {
            if (sortDirBtn == null) return;
            sortDirBtn.text = string.Empty;
            sortDirBtn.AddToClassList("sasq-list-sortdir");
            // Overrides the sort row's align-items: Center, which is what was keeping it short.
            sortDirBtn.style.alignSelf = Align.Stretch;
            sortDirBtn.style.display = DisplayStyle.None; // nothing is sorted at build time
        }

        /// <summary>
        /// The sort note's direction arrow as a real ICON. It was a "▲" text character, which renders at the
        /// font's own weight and never matched the icon set the rest of the package draws with. Shared by the
        /// List and Table notes so the two can't drift.
        /// </summary>
        internal static void SetSortNoteDirection(VisualElement iconHost, bool descending)
        {
            if (iconHost == null) return;
            iconHost.Clear();
            iconHost.Add(SasquatchIcons.Create(
                descending ? SasquatchIcons.Ids.ArrowDown : SasquatchIcons.Ids.ArrowUp,
                9, WidgetPalette.AccentIcon));
        }

        // --- Footer / empty state ---------------------------------------------------------------------

        /// <summary>
        /// The full-width "＋ Add Item" strip under the rows. Lives OUTSIDE the rows container, because an
        /// in-place rebuild Clears that container on every add/remove — a footer built inside it would work
        /// exactly once.
        /// </summary>
        internal static Button MakeListAddFooter(
            SerializedProperty prop, SerializedObject serializedObject, Action onRebuildRequired, bool suppress,
            Action onAppended = null)
        {
            var btn = new Button(() =>
            {
                // Append: the size before the grow is the new element's index. Same hand-off the header "+"
                // makes — see CollectionRowEntrance.
                CollectionRowEntrance.Arm(prop, prop.arraySize);
                prop.arraySize++;
                serializedObject.ApplyModifiedProperties();
                // …and the same reveal the header "+" makes: clear the search / sort / page state that would
                // otherwise park the new row off-screen. See CreateCollectionAddButton for why. Between the
                // grow and the rebuild, because the page clamp reads the new size and the rebuild draws it.
                onAppended?.Invoke();
                onRebuildRequired?.Invoke();
            });
            btn.AddToClassList(ListAddFooterClass);
            btn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Plus, 11, WidgetPalette.LabelMuted));
            var label = new Label("Add Item");
            label.AddToClassList("sasq-list-add-footer-label");
            label.pickingMode = PickingMode.Ignore;
            btn.Add(label);
            EditorSpringFX.ButtonSpring(btn);
            // Design select-only: a click must fall through and SELECT the collection card rather than
            // append a row and rebuild the canvas out from under the selection.
            if (suppress) DisableInteractivityRecursively(btn);
            return btn;
        }

        /// <summary>Wraps the shared italic empty-list note in a quiet inset panel, so an empty collection
        /// reads as a designed empty STATE rather than a bare box with a sentence in it.</summary>
        internal static VisualElement SkinListEmptyNote(Label note)
        {
            if (note == null) return null;
            var box = new VisualElement();
            box.AddToClassList(ListEmptyBoxClass);
            note.style.paddingTop = 0;
            note.style.paddingBottom = 0;
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            box.Add(note);
            return box;
        }
    }
}
