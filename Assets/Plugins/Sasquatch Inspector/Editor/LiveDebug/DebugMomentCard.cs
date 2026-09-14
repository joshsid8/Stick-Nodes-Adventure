using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The "what happened here" overlay: click a sample on any graph and this answers with three things, in
    /// descending order of how much they can be trusted.
    ///
    /// <list type="number">
    /// <item><b>What else changed on this frame.</b> Fact, and free — every channel shares one sample clock,
    /// so this is a read, not an inference. It is the section that replaces a scattered-Debug.Log session.</item>
    /// <item><b>Console output around this moment</b>, each row carrying the first user frame of its stack, so
    /// it peeks and jumps to a real line. Correlation, honestly labelled as such.</item>
    /// <item><b>Places in the project that assign this field.</b> A text search, phrased as a hint.</item>
    /// </list>
    ///
    /// <para>None of the three claims to know which line wrote the value, because the inspector genuinely
    /// cannot know that — it samples, and nothing tells it who wrote. Every string here is written to keep
    /// that boundary visible rather than blur it.</para>
    ///
    /// <para>Placement follows the peek-overlay pattern the quick-edit popup uses: an absolutely positioned
    /// child of the build root, hung off the graph's own rect and flipped above when there is no room below.
    /// A second EditorWindow would steal focus and is not an option here.</para>
    /// </summary>
    internal static class DebugMomentCard
    {
        private const string CardName = "sasq-debug-moment-card";
        private const float Width = 340f;
        private const float MaxHeight = 420f;
        private const float Gap = 4f;
        /// <summary>
        /// How far either side of the playhead a console line still counts as "near this moment".
        ///
        /// <para>Internal rather than private because the GRAPH needs the same number: a log pin swells exactly
        /// when its message is inside this window, so the tick on the timeline and the row in this card are
        /// driven by one definition instead of two that can drift.</para>
        /// </summary>
        internal const double LogWindowSeconds = 0.35d;
        /// <summary>Width of the severity gutter, so message text and its source link share one left edge.</summary>
        private const float ChipWidth = 40f;

        /// <summary>
        /// Opens (or moves) the card for one sample. Releasing the pointer on a graph ALWAYS ends with the
        /// card open.
        ///
        /// <para><b>It used to toggle, and the toggle was unusable.</b> Clicking the same sample twice closed
        /// it — reasonable in isolation, but the card FOLLOWS THE PLAYHEAD, so a drag rewrote its
        /// <c>userData</c> to wherever the playhead had reached. By the time the pointer came up, "the sample
        /// you just released on" and "the sample the card is already showing" were the same sample every
        /// time, so every drag-release closed the card it had just finished updating. Skipping ahead of the
        /// playhead and releasing did the same thing. What that looked like from outside was a card that
        /// appeared and vanished on no discernible rule.</para>
        ///
        /// <para>So there is no toggle: the card has a close button, and clicking a different sample moves
        /// it. Both are gestures whose outcome you can predict before you commit to them, which is the thing
        /// the toggle could not offer.</para>
        /// </summary>
        internal static void Show(VisualElement root, DebugGraphElement graph, int absolute,
            DebugRecorder recorder, Type targetType)
        {
            if (root == null || graph == null || recorder == null || absolute < 0) return;

            root.Q<VisualElement>(CardName)?.RemoveFromHierarchy();

            var card = Build(root, graph, absolute, recorder, targetType);
            card.userData = (graph, absolute);
            // Under the pinned toolbar, never over it. The bar re-asserts itself as the last child when it
            // syncs, but the redraw tick deliberately stops while the game is not playing — which is exactly
            // when a finished recording gets scrubbed, so the self-heal could never be the primary mechanism.
            DebugOverlayHost.Add(root, card);

            // FOLLOW THE PLAYHEAD. Pass 1 built the card once, from a click, and never rebuilt it — so dragging
            // moved the playhead while the card kept describing the frame you first picked. Every "nothing
            // changed" and "no console output" line was answering a question you had stopped asking.
            Action onScrub = () =>
            {
                if (card.panel == null) return;
                int now = recorder.ReadoutAbsolute;
                if (now < 0) return;
                Rebuild(root, card, graph, now, recorder, targetType);
            };
            recorder.ScrubChanged += onScrub;

            // CLOSE WHEN THE SUBJECT LEAVES. A card describing one sample of one graph is debris once that
            // graph has scrolled off, and worse than debris while it floats over unrelated fields still
            // claiming to describe something. Driven off the scroller rather than off geometry: a ScrollView
            // moves its content by TRANSFORM, so scrolling raises no GeometryChangedEvent at all.
            var scroll = graph.GetFirstAncestorOfType<ScrollView>();
            Action<float> onScrolled = null;
            if (scroll != null)
            {
                onScrolled = _ =>
                {
                    if (card.panel == null) return;
                    if (!DebugOverlayHost.IsVisibleIn(scroll, graph)) Hide(root);
                };
                scroll.verticalScroller.valueChanged += onScrolled;
            }

            PanelLifetime.OnRealDetach(card, () =>
            {
                recorder.ScrubChanged -= onScrub;
                if (scroll != null && onScrolled != null) scroll.verticalScroller.valueChanged -= onScrolled;
            });

            // Placed after a layout pass: the card's height depends on how many rows the three sections
            // produced, and the flip-above decision needs that height.
            card.RegisterCallback<GeometryChangedEvent>(_ => Place(root, graph, card));
            Place(root, graph, card);
        }

        /// <summary>Re-renders an open card for a new sample, keeping its element (and therefore its placement).</summary>
        private static void Rebuild(VisualElement root, VisualElement card, DebugGraphElement graph,
            int absolute, DebugRecorder recorder, Type targetType)
        {
            card.Clear();
            card.userData = (graph, absolute);
            PopulateCard(root, card, graph, absolute, recorder, targetType);
            Place(root, graph, card);
        }

        internal static void Hide(VisualElement root) => root?.Q<VisualElement>(CardName)?.RemoveFromHierarchy();

        private static void Place(VisualElement root, DebugGraphElement graph, VisualElement card)
        {
            var rootRect = root.worldBound;
            var graphRect = graph.worldBound;
            if (rootRect.width <= 0f || graphRect.width <= 0f) return;

            float h = card.resolvedStyle.height;
            if (float.IsNaN(h) || h <= 0f) h = 120f;

            // FIT THE WIDTH TO THE HOST FIRST. `Width` is a fixed 340 and the clamp below was written against
            // it as if it always fitted — but when the root is NARROWER than the card the clamp range
            // degenerates to [0, 0], so the card parked at left = 0 and simply hung off the right edge with
            // nothing to catch it: 103px lost at a 250px inspector, and ~78px at the 275px floor Unity's own
            // InspectorWindow.minSize allows. The card's three sections ARE the click-a-graph gesture, and it
            // has no horizontal scroll to get them back. DebugPeekCard already does exactly this (:106); this
            // was the asymmetry inside one card family. Re-fitted on every Place, so a resize corrects it too.
            float w = rootRect.width > 0f ? Mathf.Min(Width, rootRect.width) : Width;
            card.style.width = w;

            float left = Mathf.Clamp(graphRect.xMin - rootRect.xMin, 0f, Mathf.Max(0f, rootRect.width - w));
            float below = graphRect.yMax - rootRect.yMin + Gap;
            // Flip above when the card would run off the bottom — the same fit-then-flip rule the code peek
            // uses, so the card never covers the graph it is describing.
            // FLIP ABOVE ONLY IF IT ACTUALLY FITS ABOVE. The old rule flipped whenever the card would not
            // fit below, and then clamped the result to 0 with Mathf.Max — which is the TOP OF THE ROOT,
            // exactly where the pinned transport bar lives. The bar is BringToFront()ed, so the card lost its
            // top rows to it. That is reachable whenever the card is taller than the inspector is tall, which
            // for a short layout is most of the time. Staying below the anchor is always below the bar, and a
            // card that runs off the bottom is still reachable by scrolling; one hidden behind a pinned
            // toolbar is not.
            float above = graphRect.yMin - rootRect.yMin - h - Gap;
            float top = below + h > rootRect.height && above >= 0f ? above : below;

            card.style.left = left;
            card.style.top = top;
        }

        private static VisualElement Build(VisualElement root, DebugGraphElement graph, int absolute,
            DebugRecorder recorder, Type targetType)
        {
            var card = new VisualElement { name = CardName };
            card.AddToClassList("sasq-debug-card");
            card.style.position = Position.Absolute;
            card.style.width = Width;
            card.style.maxHeight = MaxHeight;
            card.style.backgroundColor = DesignerPalette.CodeBlockBg;
            card.style.borderTopWidth = card.style.borderBottomWidth =
                card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = DesignerPalette.PopupBorder;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;
            card.style.paddingTop = card.style.paddingBottom = 6;
            card.style.paddingLeft = card.style.paddingRight = 8;
            card.style.overflow = Overflow.Hidden;

            PopulateCard(root, card, graph, absolute, recorder, targetType);
            return card;
        }

        private static void PopulateCard(VisualElement root, VisualElement card, DebugGraphElement graph,
            int absolute, DebugRecorder recorder, Type targetType)
        {
            double t = recorder.TimeOf(absolute);
            int frame = recorder.FrameOf(absolute);
            // t+ counts from the START OF THE RECORDING (DebugRecorder.ElapsedAt), not from the oldest sample
            // still in the ring. The old anchor silently re-based itself once the buffer wrapped, so the same
            // label meant two different things at two points in one session.
            // No "t+" prefix: it read as a variable name and the units are already on the number. What the
            // zero point IS goes in the tooltip, which is where a fact you check twice a session belongs.
            card.Add(Header($"{graph.Channel.Label}  ·  frame {frame}  ·  {recorder.ElapsedAt(absolute):0.00}s",
                "Seconds since this recording started",
                () => CopyMoment(graph, absolute, recorder),
                () => Hide(root),
                // Hosted on the CARD, not on the header row: the header is 16px tall and would clip it.
                () => card));

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.maxHeight = MaxHeight - 34f;
            // ScrollViewMode.Vertical picks the CONTENT layout, not the scroller set — the horizontal scroller
            // stays on Auto and appears the moment anything overflows. On a card this narrow that bar is never
            // the right answer: it costs 13px of the vertical budget to hide half of a line that should have
            // wrapped. Everything in here wraps now, so the bar can only ever be a symptom.
            body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            card.Add(body);

            AddCoChangeSection(body, recorder, absolute, frame);
            AddConsoleSection(root, card, body, recorder, absolute, t);
            AddWriteSiteSection(root, card, body, graph.Channel, targetType);
        }

        // ---- section 1: co-change ----------------------------------------------------------------------

        private static void AddCoChangeSection(VisualElement body, DebugRecorder recorder, int absolute, int frame)
        {
            var changes = recorder.ChangesAt(absolute);
            body.Add(SectionTitle(changes.Count == 0
                ? $"Nothing changed on frame {frame}"
                : changes.Count == 1 ? $"1 field changed on frame {frame}" : $"{changes.Count} fields changed on frame {frame}"));

            foreach (var (channel, from, to) in changes)
            {
                // A VECTOR GETS AXIS ROWS, not one long line.
                //
                // "Position  -3.388955, 0, -2.048441 → -3.394456, 0, -2.049827" is forty-five characters of
                // value on a 340px card. It did not fit, and because a Label in a Row does not shrink by
                // default it did not wrap either — it pushed the card into a HORIZONTAL SCROLLBAR, so the
                // second half of the diff was off-screen and the number that actually moved had to be found by
                // scrolling and then by eye, digit against digit, across an arrow. The vertical space a
                // per-axis breakdown costs is the cheapest thing on this card; the answer to "which axis
                // moved" being a whole line rather than a substring is the point of the section.
                //
                // Unchanged components are simply absent. On a Vector3 that is usually two rows out of three,
                // and an axis that did not move is not part of the answer.
                var parts = recorder.ComponentChangesAt(channel, absolute);
                if (parts.Count > 0)
                {
                    body.Add(Row(Text(channel.Label, DesignerPalette.CodeText, 11, FontStyle.Bold)));
                    foreach (var (axis, before, now) in parts)
                    {
                        var part = Row();
                        part.style.marginLeft = 14;
                        var letter = Text(axis, WidgetPalette.TextGray, 11, FontStyle.Bold);
                        // A fixed gutter so the values line up under each other and the column can be read
                        // down rather than across.
                        letter.style.width = 14;
                        letter.style.flexShrink = 0;
                        part.Add(letter);
                        part.Add(Delta(before, now));
                        body.Add(part);
                    }
                    continue;
                }

                var row = Row();
                row.Add(Text(channel.Label, DesignerPalette.CodeText, 11, FontStyle.Bold));
                var delta = Delta(from, to);
                delta.style.marginLeft = 6;
                row.Add(delta);
                body.Add(row);
            }

            if (changes.Count > 0) return;

            // "Nothing changed" is usually TRUE and that is exactly the problem: a field that moves on four
            // frames out of twelve hundred leaves ~99.7% of frames quiet, so an honest empty answer reads as a
            // broken panel. Point at the nearest real event instead of stopping at a dead end.
            int back = recorder.NearestChange(absolute, -1);
            int fwd = recorder.NearestChange(absolute, +1);
            int nearest = -1;
            if (back >= 0 && fwd >= 0) nearest = (absolute - back) <= (fwd - absolute) ? back : fwd;
            else if (back >= 0) nearest = back;
            else if (fwd >= 0) nearest = fwd;

            if (nearest < 0)
            {
                body.Add(Text("No field on this script has changed yet.", WidgetPalette.TextGray, 10, FontStyle.Italic));
                return;
            }

            int delta2 = nearest - absolute;
            string which = delta2 < 0 ? $"{-delta2} frame{(delta2 == -1 ? "" : "s")} back" : $"{delta2} frame{(delta2 == 1 ? "" : "s")} ahead";
            var jump = Text($"Nearest change is {which} — frame {recorder.FrameOf(nearest)} ▸", WidgetPalette.Accent, 10, FontStyle.Normal);
            jump.style.marginLeft = 4;
            jump.tooltip = "Jump the playhead to the nearest recorded change";
            jump.RegisterCallback<PointerEnterEvent>(_ => jump.style.unityFontStyleAndWeight = FontStyle.Bold);
            jump.RegisterCallback<PointerLeaveEvent>(_ => jump.style.unityFontStyleAndWeight = FontStyle.Normal);
            jump.RegisterCallback<ClickEvent>(evt => { evt.StopPropagation(); recorder.SetScrub(nearest); });
            body.Add(jump);
        }

        // ---- section 2: console ------------------------------------------------------------------------

        private static void AddConsoleSection(VisualElement root, VisualElement card, VisualElement body,
            DebugRecorder recorder, int absolute, double sampleTime)
        {
            var logs = DebugLogCapture.Between(sampleTime - LogWindowSeconds, sampleTime + LogWindowSeconds);
            body.Add(SectionTitle(logs.Count == 0
                ? "No console output near this moment"
                : $"Console output within ±{LogWindowSeconds:0.00}s"));

            if (logs.Count == 0)
            {
                // Same dead-end problem as the co-change section: an empty answer is usually correct and always
                // useless. Point at the nearest log the trace actually holds.
                var nearest = DebugLogCapture.NearestTo(sampleTime);
                if (nearest.HasValue)
                {
                    int at = recorder.AbsoluteNearestTime(nearest.Value.Time);
                    string label = at >= 0
                        ? $"Nearest log is at frame {recorder.FrameOf(at)} ▸"
                        : "Nearest log is outside the recorded window";
                    var jump = Text(label, WidgetPalette.Accent, 10, FontStyle.Normal);
                    jump.style.marginLeft = 4;
                    if (at >= 0)
                    {
                        jump.RegisterCallback<PointerEnterEvent>(_ => jump.style.unityFontStyleAndWeight = FontStyle.Bold);
                        jump.RegisterCallback<PointerLeaveEvent>(_ => jump.style.unityFontStyleAndWeight = FontStyle.Normal);
                        jump.RegisterCallback<ClickEvent>(evt => { evt.StopPropagation(); recorder.SetScrub(at); });
                    }
                    body.Add(jump);
                }
                else if (DebugLogCapture.Count == 0)
                {
                    body.Add(Text("Nothing has been logged this run.", WidgetPalette.TextGray, 10, FontStyle.Italic));
                }
                return;
            }

            foreach (var entry in logs)
            {
                var row = Row();
                row.style.flexDirection = FlexDirection.Column;

                string msg = entry.Message ?? "";
                int nl = msg.IndexOf('\n');
                if (nl >= 0) msg = msg.Substring(0, nl);

                // Severity is NAMED, not merely coloured. Colour alone made a white line and a gold line look
                // like two arbitrary styles rather than a log and a warning — nothing on the card said which
                // was which, and roughly one in twelve readers cannot tell them apart by hue at all. The colour
                // stays, as reinforcement.
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.FlexStart;
                line.Add(SeverityChip(entry.Type, entry.IsMark));
                var color = entry.IsMark ? WidgetPalette.Accent : LogColor(entry.Type);
                var text = Text(msg.Length > 90 ? msg.Substring(0, 87) + "…" : msg, color, 11, FontStyle.Normal);
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                line.Add(text);
                row.Add(line);

                // The frame parse is deferred until here — see DebugLogCapture.Resolve. This is the only
                // reader of File/Line/Method, and only for the entries inside the card's own window, so the
                // regex no longer runs for every console line the editor produces.
                var resolved = DebugLogCapture.Resolve(entry);
                if (!string.IsNullOrEmpty(resolved.File))
                {
                    string fileName = System.IO.Path.GetFileName(resolved.File);
                    var link = Text($"{resolved.Method}  ({fileName}:{resolved.Line})", WidgetPalette.Accent, 10, FontStyle.Normal);
                    link.style.marginTop = 1;
                    link.style.marginLeft = ChipWidth;
                    MakeJump(link, resolved.File, resolved.Line);
                    DebugPeekCard.Attach(root, card, link, resolved.File, resolved.Line);
                    row.Add(link);
                }
                body.Add(row);
            }
        }

        /// <summary>Fixed-width severity gutter: a colour dot plus the word, so the row says what it is.</summary>
        private static VisualElement SeverityChip(LogType type, bool isMark = false)
        {
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.width = ChipWidth;
            chip.style.flexShrink = 0;
            chip.style.marginTop = 2;
            chip.pickingMode = PickingMode.Ignore;

            // A marker is YOUR annotation, not the console's, so it gets the accent colour rather than a
            // severity — reading "log" beside a line you deliberately pinned would be actively misleading.
            var color = isMark ? WidgetPalette.Accent : LogColor(type);
            var dot = new VisualElement();
            dot.style.width = dot.style.height = 5;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 3;
            dot.style.backgroundColor = color;
            dot.style.marginRight = 3;
            dot.style.flexShrink = 0;
            chip.Add(dot);

            var word = Text(isMark ? "mark" : SeverityWord(type), color, 9, FontStyle.Normal);
            word.style.flexShrink = 0;
            chip.Add(word);
            return chip;
        }

        private static string SeverityWord(LogType type)
        {
            switch (type)
            {
                case LogType.Error:     return "error";
                case LogType.Exception: return "except";
                case LogType.Assert:    return "assert";
                case LogType.Warning:   return "warn";
                default:                return "log";
            }
        }

        private static Color LogColor(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return WidgetPalette.DangerText;
                case LogType.Warning:
                    return WidgetPalette.WarningCalloutText;
                default:
                    return DesignerPalette.CodeText;
            }
        }

        // ---- section 3: write sites --------------------------------------------------------------------

        private static void AddWriteSiteSection(VisualElement root, VisualElement card, VisualElement body,
            DebugChannel channel, Type targetType)
        {
            string fieldName = LeafName(channel.Path);
            var found = DebugWriteSiteScanner.Find(targetType, fieldName);

            body.Add(SectionTitle(DebugWriteSiteScanner.Describe(found, fieldName)));
            // NOTE: there is deliberately no disclaimer line here any more. "Any of these could have written it
            // — the inspector samples values and cannot see the writer" was defensive text that took four
            // percent of a 340px card to apologise for a heading that already says "assign", not "did". The
            // honesty lives in the wording of the heading, where it costs nothing.
            foreach (var site in found.OwnWrites) body.Add(SiteRow(root, card, site));

            AddReadSection(root, card, body, found, fieldName);
        }

        /// <summary>
        /// "Who READS this value" — collapsed by default, expanded on click.
        ///
        /// <para>This replaced the project-wide write search, which asked "does some other script assign a field
        /// with this NAME?" — a question that produced mostly false positives and that almost nobody has. The
        /// read list answers one people genuinely do have: if I change this, what breaks? The data was already
        /// being collected by <see cref="DebugWriteSiteScanner"/> and thrown away as a count, so it costs
        /// nothing to show.</para>
        /// </summary>
        private static void AddReadSection(VisualElement root, VisualElement card, VisualElement body,
            DebugWriteSiteScanner.Result found, string fieldName)
        {
            int n = found.OwnReads.Count;
            if (n == 0) return;

            var rows = new VisualElement();
            rows.style.display = DisplayStyle.None;

            string noun = n == 1 ? "place reads" : "places read";
            var toggle = Text($"{n} {noun} “{fieldName}” ▸", WidgetPalette.Accent, 9, FontStyle.Normal);
            toggle.style.marginLeft = 4;
            toggle.style.marginTop = 3;
            toggle.tooltip = "Lines in this script that read the field without assigning it.";
            toggle.RegisterCallback<PointerEnterEvent>(_ => toggle.style.unityFontStyleAndWeight = FontStyle.Bold);
            toggle.RegisterCallback<PointerLeaveEvent>(_ => toggle.style.unityFontStyleAndWeight = FontStyle.Normal);
            toggle.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                bool open = rows.style.display == DisplayStyle.None;
                // Built on FIRST expand, not on card open: a card that never gets expanded should not pay for
                // rows nobody asked to see, and a field with thirty reads would otherwise dominate the scroll.
                if (open && rows.childCount == 0)
                    foreach (var site in found.OwnReads) rows.Add(SiteRow(root, card, site));
                rows.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                toggle.text = $"{n} {noun} “{fieldName}” " + (open ? "▾" : "▸");
            });

            body.Add(toggle);
            body.Add(rows);
        }

        private static VisualElement SiteRow(VisualElement root, VisualElement card, DebugWriteSiteScanner.Site site)
        {
            var row = Row();
            row.style.flexDirection = FlexDirection.Column;
            row.Add(Text(site.Text, DesignerPalette.CodeText, 10, FontStyle.Normal));
            var link = Text($"{System.IO.Path.GetFileName(site.Path)}:{site.Line}", WidgetPalette.Accent, 9, FontStyle.Normal);
            MakeJump(link, site.Path, site.Line);
            DebugPeekCard.Attach(root, card, link, site.Path, site.Line);
            row.Add(link);
            return row;
        }

        // ---- shared chrome -----------------------------------------------------------------------------

        /// <summary>
        /// Wires a label to open its source. Single click pings the asset, double click opens at the line —
        /// the same gesture pair the Script Header uses, so it needs no explaining.
        /// </summary>
        private static void MakeJump(VisualElement link, string path, int line)
        {
            link.tooltip = $"{path}:{line}  —  double-click to open";
            link.RegisterCallback<PointerEnterEvent>(_ => link.style.unityFontStyleAndWeight = FontStyle.Bold);
            link.RegisterCallback<PointerLeaveEvent>(_ => link.style.unityFontStyleAndWeight = FontStyle.Normal);
            link.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                if (evt.clickCount >= 2)
                {
                    // Straight to the external editor rather than AssetDatabase.OpenAsset: that pipeline
                    // validates every [OnOpenAsset] handler in the project and logs a third-party package's
                    // signature error under our stack trace. Same call, same reason, as the Script Header's
                    // Open button — and it is the only line-aware jump in the package.
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, line);
                }
                else
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                }
            });
        }

        private static VisualElement Header(string title, string tooltip, Action onCopy, Action onClose,
            Func<VisualElement> onToastHost = null)
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 4;

            var label = Text(title, WidgetPalette.Accent, 11, FontStyle.Bold);
            label.style.flexGrow = 1;
            label.style.overflow = Overflow.Hidden;
            label.tooltip = tooltip;
            bar.Add(label);

            // COPY THIS MOMENT. The toolbar already copies the whole trace as CSV, which is the right size for
            // a spreadsheet and the wrong size for a message to a colleague: a bug report needs the four lines
            // describing the frame that went wrong, not twelve hundred rows they have to search. Different
            // question, different button.
            var copy = IconButton(SasquatchIcons.Ids.Copy,
                "Copy this moment as text — the frame, how far into the recording it is, every field that " +
                "changed on it, and any console output nearby. Pastes into a bug report or a message.",
                out _);
            copy.clicked += () =>
            {
                onCopy?.Invoke();
                // A clipboard write has no visible effect anywhere on screen, so it MUST say so. The same
                // flourish the colour palette uses when it copies a hex — one acknowledgement shape across the
                // package, rather than this card inventing a second one. Nudged below the header so it does not
                // land on top of the two buttons that sit in that corner.
                SasquatchToast.Show(onToastHost != null ? onToastHost() : bar, "Copied", 20f);
            };
            bar.Add(copy);

            var close = IconButton(SasquatchIcons.Ids.X, "Close this card", out _);
            close.clicked += () => onClose?.Invoke();
            bar.Add(close);
            return bar;
        }

        /// <summary>
        /// A square icon button for the card's header.
        ///
        /// <para><b>Vector icons, never text glyphs</b> — the transport bar's first rule, and the copy button
        /// is why it now applies here too. <c>❐</c> renders at whatever width the editor font happens to give
        /// it and reads as a window rather than as a copy, so the control was unidentifiable before anyone even
        /// got as far as its tooltip. The close button moved with it: one vector and one glyph side by side in
        /// the same header is worse than either on its own.</para>
        /// </summary>
        private static Button IconButton(string iconId, string tooltip, out SasquatchIconElement icon)
        {
            var b = new Button { tooltip = tooltip };
            b.style.width = 18;
            b.style.height = 16;
            b.style.marginTop = b.style.marginBottom = b.style.marginLeft = b.style.marginRight = 0;
            b.style.paddingTop = b.style.paddingBottom = 0;
            b.style.paddingLeft = b.style.paddingRight = 0;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;

            icon = SasquatchIcons.Create(iconId, 11f, WidgetPalette.TextGray);
            icon.pickingMode = PickingMode.Ignore;
            b.Add(icon);
            return b;
        }

        /// <summary>
        /// The moment as plain text, ready to paste into a bug report or a message. Deliberately NOT CSV: this
        /// is prose for a human reading it inline, where the trace export is a table for a spreadsheet.
        /// </summary>
        internal static void CopyMoment(DebugGraphElement graph, int absolute, DebugRecorder recorder)
        {
            var sb = new System.Text.StringBuilder();
            string owner = string.IsNullOrEmpty(recorder.TargetName) ? "script" : recorder.TargetName;
            sb.Append(owner).Append(" — ").Append(graph.Channel.Label).Append('\n');
            sb.Append("frame ").Append(recorder.FrameOf(absolute))
              .Append("  ·  ").Append(recorder.ElapsedAt(absolute).ToString("0.00"))
              .Append("s into the recording\n");

            var changes = recorder.ChangesAt(absolute);
            sb.Append('\n');
            if (changes.Count == 0)
            {
                sb.Append("Nothing changed on this frame.\n");
            }
            else
            {
                sb.Append(changes.Count == 1 ? "1 field changed:\n" : changes.Count + " fields changed:\n");
                foreach (var (channel, from, to) in changes)
                    sb.Append("  ").Append(channel.Label).Append(": ").Append(from)
                      .Append(" -> ").Append(to).Append('\n');
            }

            double t = recorder.TimeOf(absolute);
            var logs = DebugLogCapture.Between(t - LogWindowSeconds, t + LogWindowSeconds);
            sb.Append('\n');
            if (logs.Count == 0)
            {
                sb.Append("No console output within ±").Append(LogWindowSeconds.ToString("0.00")).Append("s.\n");
            }
            else
            {
                sb.Append("Console output within ±").Append(LogWindowSeconds.ToString("0.00")).Append("s:\n");
                for (int i = 0; i < logs.Count; i++)
                    sb.Append("  [").Append(logs[i].Type).Append("] ").Append(logs[i].Message).Append('\n');
            }

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
        }

        private static Label SectionTitle(string text)
        {
            var label = Text(text, WidgetPalette.TextGray, 10, FontStyle.Bold);
            label.style.marginTop = 6;
            label.style.marginBottom = 2;
            return label;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.marginLeft = 4;
            return row;
        }

        private static VisualElement Row(VisualElement child)
        {
            var row = Row();
            row.Add(child);
            return row;
        }

        /// <summary>
        /// One "before → after" value, allowed to WRAP rather than widen the card.
        ///
        /// <para>UI Toolkit's default <c>flex-shrink</c> is 0, not CSS's 1 — so a Label in a Row keeps its full
        /// content width no matter how narrow the parent is, and <c>white-space: normal</c> never gets an
        /// excuse to break a line. That is how a long value pushed this card into a horizontal scrollbar
        /// instead of onto a second row. Every value the card prints goes through here.</para>
        /// </summary>
        private static Label Delta(string before, string now)
        {
            var label = Text($"{before} → {now}", WidgetPalette.Accent, 11, FontStyle.Normal);
            label.style.flexShrink = 1;
            return label;
        }

        private static Label Text(string text, Color color, float size, FontStyle style)
        {
            var label = new Label(text);
            label.style.color = color;
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = style;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static string LeafName(string path)
        {
            int dot = path.LastIndexOf('.');
            return dot >= 0 && dot < path.Length - 1 ? path.Substring(dot + 1) : path;
        }
    }
}
