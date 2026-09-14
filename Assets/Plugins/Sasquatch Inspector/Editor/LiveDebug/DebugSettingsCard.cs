using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The recording-settings panel behind the transport's gear: which clock, what starts a recording, what
    /// stops one.
    ///
    /// <para><b>Why a card and not a menu.</b> Stage 1 put the single tick-source choice in a
    /// <c>GenericMenu</c>, which was right for one setting and wrong for five — two of them carry a NUMBER
    /// (a delay, a duration), and a menu cannot hold a field. More importantly, these settings only make sense
    /// read together: "start after 3s, stop on the first error" is one sentence, and a menu shows you one word
    /// of it at a time.</para>
    ///
    /// <para>Each picker carries a plain-language line underneath saying what it will actually do, rebuilt as
    /// the value changes. That line is the feature — an enum called <c>OnWarningOrError</c> means nothing until
    /// something says "freezes the instant anything logs a warning, so you keep the run-up to it".</para>
    ///
    /// <para>Same overlay pattern as <see cref="DebugMomentCard"/>: an absolutely positioned child of the build
    /// root, so it dies with the next rebuild and can never steal focus from the Game view — which matters
    /// here, because you configure this WHILE playing.</para>
    /// </summary>
    internal static class DebugSettingsCard
    {
        private const string CardName = "sasq-debug-settings-card";
        private const float Width = 300f;
        private const float Gap = 4f;

        internal static void Hide(VisualElement root) => root?.Q<VisualElement>(CardName)?.RemoveFromHierarchy();

        /// <summary>Opens the card under <paramref name="anchor"/>, or closes it when it is already open.</summary>
        internal static void Toggle(VisualElement root, VisualElement anchor, Type type)
        {
            if (root == null || type == null) return;
            if (root.Q<VisualElement>(CardName) != null) { Hide(root); return; }

            var card = Build(root, type);
            // Under the pinned toolbar, never over it — see DebugOverlayHost.
            DebugOverlayHost.Add(root, card);
            card.RegisterCallback<GeometryChangedEvent>(_ => Place(root, anchor, card));
            Place(root, anchor, card);
        }

        private static VisualElement Build(VisualElement root, Type type)
        {
            var card = new VisualElement { name = CardName };
            card.AddToClassList("sasq-debug-card");
            card.style.position = Position.Absolute;
            card.style.width = Width;
            card.style.backgroundColor = DesignerPalette.CodeBlockBg;
            card.style.borderTopWidth = card.style.borderBottomWidth =
                card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = DesignerPalette.PopupBorder;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;
            card.style.paddingTop = card.style.paddingBottom = 6;
            card.style.paddingLeft = card.style.paddingRight = 8;

            card.Add(Header("Recording settings", () => Hide(root)));
            Populate(card, root, type);
            return card;
        }

        /// <summary>
        /// Rebuilt wholesale on every change rather than patched. The rows are interdependent — picking
        /// "After a delay" has to make a seconds field appear — and a settings card that is rebuilt in one
        /// place cannot drift out of sync with the settings it edits.
        /// </summary>
        private static void Rebuild(VisualElement card, VisualElement root, Type type)
        {
            // Keep the header, drop everything under it.
            while (card.childCount > 1) card.RemoveAt(1);
            Populate(card, root, type);
        }

        private static void Populate(VisualElement card, VisualElement root, Type type)
        {
            // ---- the clock ----
            card.Add(SectionTitle("Sample on"));
            var tick = new EnumField(DebugRecordSettings.TickSourceFor(type));
            Slim(tick);
            tick.RegisterValueChangedCallback(evt =>
            {
                DebugRecordSettings.SetTickSource(type, (DebugTickSource)evt.newValue);
                Rebuild(card, root, type);
            });
            card.Add(tick);

            if (DebugRecordSettings.TickSourceFor(type) == DebugTickSource.Interval)
            {
                // isDelayed on every numeric field in this card, matching the convention the package already
                // follows at a dozen other sites. Without it the callback fires on EVERY KEYSTROKE and calls
                // Rebuild, which tears down and recreates the very field being typed into — so a value had to
                // be valid at every prefix to survive. "Stop after 120 samples" was literally unreachable
                // through the UI: the field died at "1".
                var rate = new FloatField("Times/sec")
                { value = DebugRecordSettings.SampleRateFor(type), isDelayed = true };
                Slim(rate);
                rate.RegisterValueChangedCallback(evt =>
                {
                    DebugRecordSettings.SetSampleRate(type, evt.newValue);
                    Rebuild(card, root, type);
                });
                card.Add(rate);
            }
            card.Add(Hint(DebugRecordSettings.Describe(type, DebugRecordSettings.TickSourceFor(type))));

            // ---- start ----
            card.Add(SectionTitle("Start recording"));
            var start = new EnumField(DebugRecordSettings.StartTriggerFor(type));
            Slim(start);
            start.RegisterValueChangedCallback(evt =>
            {
                DebugRecordSettings.SetStartTrigger(type, (DebugStartTrigger)evt.newValue);
                Rebuild(card, root, type);
            });
            card.Add(start);

            if (DebugRecordSettings.StartTriggerFor(type) == DebugStartTrigger.AfterDelay)
            {
                var delay = new FloatField("Delay (s)")
                { value = DebugRecordSettings.StartDelayFor(type), isDelayed = true };
                Slim(delay);
                delay.RegisterValueChangedCallback(evt =>
                {
                    DebugRecordSettings.SetStartDelay(type, evt.newValue);
                    Rebuild(card, root, type);
                });
                card.Add(delay);
            }
            card.Add(Hint(DebugRecordSettings.Describe(type, DebugRecordSettings.StartTriggerFor(type))));

            // ---- stop ----
            card.Add(SectionTitle("Stop recording"));
            var stop = new EnumField(DebugRecordSettings.StopTriggerFor(type));
            Slim(stop);
            stop.RegisterValueChangedCallback(evt =>
            {
                DebugRecordSettings.SetStopTrigger(type, (DebugStopTrigger)evt.newValue);
                Rebuild(card, root, type);
            });
            card.Add(stop);

            switch (DebugRecordSettings.StopTriggerFor(type))
            {
                case DebugStopTrigger.AfterSeconds:
                {
                    var secs = new FloatField("Seconds")
                    { value = DebugRecordSettings.StopAfterSecondsFor(type), isDelayed = true };
                    Slim(secs);
                    secs.RegisterValueChangedCallback(evt =>
                    {
                        DebugRecordSettings.SetStopAfterSeconds(type, evt.newValue);
                        Rebuild(card, root, type);
                    });
                    card.Add(secs);
                    break;
                }
                case DebugStopTrigger.AfterSamples:
                {
                    var n = new IntegerField("Samples")
                    { value = DebugRecordSettings.StopAfterSamplesFor(type), isDelayed = true };
                    Slim(n);
                    n.RegisterValueChangedCallback(evt =>
                    {
                        DebugRecordSettings.SetStopAfterSamples(type, evt.newValue);
                        Rebuild(card, root, type);
                    });
                    card.Add(n);
                    break;
                }
            }
            card.Add(Hint(DebugRecordSettings.Describe(type, DebugRecordSettings.StopTriggerFor(type))));

            // ---- the two ways in that are NOT settings ----
            // Discoverability, not decoration: a shortcut nobody knows about and an API nobody knows exists are
            // both worth exactly nothing, and this card is the only place either of them is ever mentioned.
            var rule = new VisualElement();
            rule.style.height = 1;
            rule.style.marginTop = 8;
            rule.style.marginBottom = 6;
            rule.style.backgroundColor = DesignerPalette.BorderMid;
            card.Add(rule);

            card.Add(Hint("These always work, regardless of the settings above:"));

            // The shortcut line IS the link. A separate "rebind in Edit > Shortcuts" row at the bottom of the
            // card put the action a long way from the thing it acts on, and made the card look like it had two
            // unrelated footers. Clicking the binding is the obvious gesture anyway.
            var shortcutRow = new VisualElement();
            shortcutRow.style.flexDirection = FlexDirection.Row;
            shortcutRow.style.alignItems = Align.Center;
            shortcutRow.Add(Mono("Shortcut   "));
            var binding = Mono(DebugShortcuts.CurrentBinding());
            binding.style.color = WidgetPalette.Accent;
            binding.tooltip = "Open Edit ▸ Shortcuts to change it";
            binding.RegisterCallback<PointerEnterEvent>(_ => binding.style.unityFontStyleAndWeight = FontStyle.Bold);
            binding.RegisterCallback<PointerLeaveEvent>(_ => binding.style.unityFontStyleAndWeight = FontStyle.Normal);
            binding.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                EditorApplication.ExecuteMenuItem("Edit/Shortcuts...");
            });
            shortcutRow.Add(binding);
            card.Add(shortcutRow);
            // Says out loud whether the key will survive the Game view taking focus — the one situation where
            // an editor shortcut quietly stops working, and the exact moment you reach for it. Null once the
            // shortcut is unbound, because the line above has already said the only thing worth saying.
            string caveat = DebugShortcuts.FocusCaveat();
            if (!string.IsNullOrEmpty(caveat)) card.Add(Hint("           " + caveat));
            card.Add(Mono("Script     SasquatchDebug.Record(this)"));
            card.Add(Mono("           SasquatchDebug.Stop(this)"));
            card.Add(Mono("           SasquatchDebug.Mark(\"here\")"));

            AddLegend(card);
        }

        /// <summary>
        /// What the notation beside each graph means.
        ///
        /// <para><b>Why it lives here and not on the toolbar.</b> The bar already wraps to three rows at 198px,
        /// and it is pinned — anything added to it is a control that follows you down the whole inspector. The
        /// gear is where this feature already keeps its explanations, and the card floats over the fields
        /// rather than riding the pin.</para>
        ///
        /// <para><b>And why it is not only tooltips.</b> The same text is on the readouts themselves, which is
        /// the right answer for someone who has already spotted the number and wondered. But the TIPS switch
        /// exists precisely so you can silence those while reading a graph — so the legend needed one home
        /// that survives it being switched off.</para>
        /// </summary>
        private static void AddLegend(VisualElement card)
        {
            // COLLAPSED BY DEFAULT, both of them. This card is already the tallest thing the feature puts on
            // screen and it is anchored to a button inside the inspector, so every row added unconditionally
            // is a row that pushes the settings — the reason anyone opened it — further from the gear. A
            // reference you read once belongs behind a disclosure; the settings you came for do not.
            var legend = Expander("Legend", SasquatchIcons.Ids.Info, out var legendBody);
            card.Add(legend);

            // The picture first: every item below is a piece of NOTATION, and naming notation in prose asks
            // the reader to hold a description in their head while they go and find the thing on screen.
            legendBody.Add(new DebugLegendDiagram());

            legendBody.Add(Legend("2 changes", "How many times a bool, enum or text field changed while recording."));
            legendBody.Add(Legend("never changed", "Nothing wrote a different value to this field for the whole recording."));
            legendBody.Add(Legend("409 of 469", "Which sample the playhead sits on, out of the ones the buffer still holds."));
            legendBody.Add(Legend("4.73s", "How long after recording started that sample was taken."));
            legendBody.Add(Legend("yellow value", "You are reading the PAST. White means the playhead is following live data."));

            var keys = Expander("Keyboard", SasquatchIcons.Ids.Terminal, out var keysBody);
            card.Add(keys);
            keysBody.Add(Hint("Click the toolbar first — these drive the playhead, so it has to have focus."));
            keysBody.Add(Mono("← →        step one sample"));
            keysBody.Add(Mono("Shift + ← → step ten"));
            keysBody.Add(Mono("Home       oldest sample still held"));
            keysBody.Add(Mono("End        back to live"));
        }

        /// <summary>
        /// A collapsed disclosure box, reusing the package's own expandable Help Box chrome
        /// (<c>sasq-help-expand-*</c> plus <see cref="EditorSpringFX"/>) rather than inventing a second one —
        /// a settings card that folds differently from the help boxes in the inspector behind it would read as
        /// a different product.
        /// </summary>
        private static VisualElement Expander(string title, string iconId, out VisualElement body)
        {
            var container = new VisualElement();
            container.style.marginTop = 8;

            var header = new VisualElement();
            header.AddToClassList("sasq-help-expand-header");
            // Padding trimmed from the shared class: that one is sized for a full-width inspector help box,
            // and this card is 300px wide with two of these stacked in it.
            header.style.paddingTop = header.style.paddingBottom = 3;
            header.style.paddingLeft = header.style.paddingRight = 6;

            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, WidgetPalette.ChevronDim);
            chevron.AddToClassList("sasq-help-expand-chevron");
            header.Add(chevron);
            EditorSpringFX.SpringCaret(chevron, false, animate: false);

            var icon = SasquatchIcons.Create(iconId, 12, WidgetPalette.TextGray);
            icon.style.marginRight = 5;
            header.Add(icon);

            var label = new Label(title);
            label.style.flexGrow = 1;
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = WidgetPalette.LabelPrimary;
            header.Add(label);
            container.Add(header);

            body = new VisualElement();
            body.AddToClassList("sasq-help-expand-details");
            body.style.paddingTop = body.style.paddingBottom = 6;
            body.style.paddingLeft = body.style.paddingRight = 8;
            body.style.display = DisplayStyle.None;
            container.Add(body);

            // Tracked in a bool rather than read back from style.display, because the spring fold keeps
            // display:Flex until the collapse animation settles — the same note the inspector help box carries.
            var details = body;
            bool open = false;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                open = !open;
                EditorSpringFX.SpringCaret(chevron, open);
                EditorSpringFX.Fold(details, open);
                evt.StopPropagation();
            });
            return container;
        }

        /// <summary>One legend line: the notation as it appears on screen, then what it means.</summary>
        private static VisualElement Legend(string notation, string meaning)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 2;

            var term = new Label(notation);
            term.style.color = WidgetPalette.TextStrong;
            term.style.fontSize = 9;
            term.style.unityFontStyleAndWeight = FontStyle.Bold;
            // Fixed column, not auto: the point of a legend is that the eye can run down the left edge and
            // find the notation it is looking for without reading any of the explanations.
            term.style.width = 78;
            term.style.flexShrink = 0;
            term.style.whiteSpace = WhiteSpace.NoWrap;
            term.style.overflow = Overflow.Hidden;
            row.Add(term);

            var text = new Label(meaning);
            text.style.color = WidgetPalette.TextGray;
            text.style.fontSize = 9;
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            row.Add(text);
            return row;
        }

        // ---- chrome ------------------------------------------------------------------------------------

        /// <summary>Compact field geometry — a settings card at inspector width has no room for Unity's default label column.</summary>
        private static void Slim(VisualElement field)
        {
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.marginTop = 1;
            field.style.fontSize = 11;
            var label = field.Q<Label>();
            if (label != null)
            {
                label.style.minWidth = 64;
                label.style.fontSize = 11;
            }
        }

        private static VisualElement Header(string title, Action onClose)
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 2;

            var label = new Label(title);
            label.style.color = WidgetPalette.Accent;
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.flexGrow = 1;
            bar.Add(label);

            var close = new Button(onClose) { text = "✕" };
            close.style.width = 18;
            close.style.height = 16;
            close.style.fontSize = 10;
            close.style.marginTop = close.style.marginBottom = close.style.marginLeft = close.style.marginRight = 0;
            close.style.paddingTop = close.style.paddingBottom = 0;
            close.style.paddingLeft = close.style.paddingRight = 0;
            bar.Add(close);
            return bar;
        }

        private static Label SectionTitle(string text)
        {
            var l = new Label(text);
            l.style.color = WidgetPalette.TextGray;
            l.style.fontSize = 10;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 7;
            l.style.marginBottom = 1;
            return l;
        }

        private static Label Hint(string text)
        {
            var l = new Label(text);
            l.style.color = WidgetPalette.TextGray;
            l.style.fontSize = 9;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 2;
            l.style.marginLeft = 2;
            return l;
        }

        private static Label Mono(string text)
        {
            var l = new Label(text);
            l.style.color = DesignerPalette.CodeText;
            l.style.fontSize = 9;
            l.style.marginLeft = 2;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            CodeHighlighter.ApplyCodeFont(l);
            return l;
        }

        /// <summary>Under the gear when there is room, flipped above when there is not, always clamped inside the root.</summary>
        private static void Place(VisualElement root, VisualElement anchor, VisualElement card)
        {
            var rootRect = root.worldBound;
            if (rootRect.width <= 0f) return;

            Rect a = anchor != null && anchor.panel != null ? anchor.worldBound : rootRect;
            float h = card.resolvedStyle.height;
            if (float.IsNaN(h) || h <= 0f) h = 220f;

            // FIT THE WIDTH TO THE HOST FIRST — same degenerate clamp as the moment card had. `Width` is a
            // fixed 300, and when the root is narrower the clamp range collapses to [0, 0], so the card sat at
            // left = 0 and overhung by 63px at a 250px inspector. This card is where EVERY recording option
            // lives, so the part that goes off-screen is a numeric field's value box.
            float w = rootRect.width > 0f ? Mathf.Min(Width, rootRect.width) : Width;
            card.style.width = w;

            // Right-aligned to the gear: the button lives at the right end of the bar, and a card that opened
            // leftward from there would hang off the inspector on a narrow window.
            float left = Mathf.Clamp(a.xMax - rootRect.xMin - w, 0f, Mathf.Max(0f, rootRect.width - w));
            // FLIP ABOVE ONLY IF IT ACTUALLY FITS ABOVE. The old rule flipped whenever the card would not
            // fit below, and then clamped the result to 0 with Mathf.Max — which is the TOP OF THE ROOT,
            // exactly where the pinned transport bar lives. The bar is BringToFront()ed, so the card lost its
            // top rows to it. That is reachable whenever the card is taller than the inspector is tall, which
            // for a short layout is most of the time. Staying below the anchor is always below the bar, and a
            // card that runs off the bottom is still reachable by scrolling; one hidden behind a pinned
            // toolbar is not.
            float below = a.yMax - rootRect.yMin + Gap;
            float above = a.yMin - rootRect.yMin - h - Gap;
            float top = below + h > rootRect.height && above >= 0f ? above : below;

            card.style.left = left;
            card.style.top = top;
        }
    }
}
