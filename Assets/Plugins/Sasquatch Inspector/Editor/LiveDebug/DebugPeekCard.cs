using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The code peek that hangs off the moment card: a syntax-highlighted snippet of the line a log or a write
    /// site points at, shown while the pointer rests on that row.
    ///
    /// <para><b>Why it is a SEPARATE card and not a pane inside the moment card.</b> Pass 2 appended the
    /// snippet into the row it belonged to, which meant the card you were reading grew and shrank under your
    /// eyes every time the pointer crossed a link — while the row you were aiming at moved. Reading and
    /// re-aiming at the same time is not a thing people can do. This is an independent absolutely-positioned
    /// card beside the moment card, so the moment card's geometry never changes at all.</para>
    ///
    /// <para><b>And it suppresses the row's tooltip while it is open.</b> The tooltip and the peek answer the
    /// same question in the same place, and the tooltip is drawn on top, so the peek was regularly covered by
    /// a duplicate of its own header. The path moves INTO this card's header line instead — one answer, in one
    /// place, that nothing overlaps.</para>
    ///
    /// <para>Sizing comes from <see cref="CodePeekMetrics"/>, shared with the quick-edit popup's peek, so the
    /// two can never drift into looking like different features.</para>
    /// </summary>
    internal static class DebugPeekCard
    {
        private const string CardName = "sasq-debug-peek-card";
        private const float Gap = CodePeekMetrics.JoinOverlap;
        private const float MinWidth = 240f;

        /// <summary>
        /// Attaches hover-to-peek to one source link.
        /// </summary>
        /// <param name="root">The build root the card is parented to — the same host the moment card uses.</param>
        /// <param name="anchor">The card to sit beside, normally the moment card.</param>
        /// <param name="link">The label the pointer has to rest on.</param>
        internal static void Attach(VisualElement root, VisualElement anchor, VisualElement link,
            string path, int line)
        {
            if (root == null || link == null || string.IsNullOrEmpty(path)) return;

            // The path still belongs on the link for accessibility and for the click gesture, but it must not
            // be VISIBLE while the peek is: see the class note. Stashed and restored rather than rebuilt so a
            // future change to the tooltip text only has to happen in one place (MakeJump).
            string tooltip = link.tooltip;

            link.RegisterCallback<PointerEnterEvent>(_ =>
            {
                link.tooltip = null;
                Show(root, anchor, path, line);
            });

            link.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                link.tooltip = tooltip;
                Hide(root);
            });

            // A card whose link is torn down mid-hover would otherwise be left floating with nothing to close
            // it — the row can vanish under the pointer whenever the playhead moves.
            link.RegisterCallback<DetachFromPanelEvent>(_ => Hide(root));
        }

        internal static void Hide(VisualElement root) => root?.Q<VisualElement>(CardName)?.RemoveFromHierarchy();

        private static void Show(VisualElement root, VisualElement anchor, string path, int line)
        {
            Hide(root);

            string rich = ReadSnippet(path, line);
            if (string.IsNullOrEmpty(rich)) return;

            var card = new VisualElement { name = CardName };
            card.AddToClassList("sasq-debug-peek");
            card.style.position = Position.Absolute;
            // Never picks: the pointer is resting on the LINK, and a card that could take the pointer would
            // steal the PointerLeave that closes it — the classic peek that will not go away.
            card.pickingMode = PickingMode.Ignore;
            card.style.backgroundColor = DesignerPalette.CodeBlockBg;
            card.style.borderTopWidth = card.style.borderBottomWidth =
                card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = DesignerPalette.PopupBorder;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;
            card.style.paddingTop = card.style.paddingBottom = 5;
            card.style.paddingLeft = card.style.paddingRight = 7;
            card.style.overflow = Overflow.Hidden;

            var header = new Label($"{System.IO.Path.GetFileName(path)}:{line}");
            header.style.fontSize = 9;
            header.style.color = WidgetPalette.TextGray;
            header.style.marginBottom = 3;
            header.pickingMode = PickingMode.Ignore;
            card.Add(header);

            var code = new Label(rich) { enableRichText = true, pickingMode = PickingMode.Ignore };
            code.style.color = DesignerPalette.CodeText;
            code.style.fontSize = 10;
            code.style.whiteSpace = WhiteSpace.NoWrap;
            // A panel-parented label inherits no font; without this the card renders as an empty box.
            CodeHighlighter.ApplyCodeFont(code);
            card.Add(code);

            float width = Mathf.Max(MinWidth, CodePeekMetrics.ContentWidth(rich, MinWidth, CodePeekMetrics.MaxWidth));
            // Never wider than the host. A snippet is allowed to clip (overflow is hidden); a card hanging off
            // the side of the inspector is not.
            float room = root.worldBound.width;
            if (room > 0f) width = Mathf.Min(width, room);
            card.style.width = width;

            DebugOverlayHost.Add(root, card);
            // Placed after a layout pass as well as immediately: the height depends on how many lines the
            // snippet produced, and the fit-then-flip decision needs that height.
            card.RegisterCallback<GeometryChangedEvent>(_ => Place(root, anchor, card, width));
            Place(root, anchor, card, width);
        }

        /// <summary>
        /// Right of the anchor if it fits, left if that fits instead, otherwise below — and above only when
        /// below would run off the bottom AND above genuinely fits. Never on top of the anchor, and never
        /// pinned to the top of the root, which is where the pinned transport bar lives.
        ///
        /// <para>Beside is TRIED first because a card level with its anchor reads as attached to it, but an
        /// inspector is usually narrower than a 340px moment card plus a snippet, so below/above is the path
        /// that actually runs most of the time. Both are fine; what is not fine is overlapping the card being
        /// read, which is the failure this class exists to end.</para>
        /// </summary>
        private static void Place(VisualElement root, VisualElement anchor, VisualElement card, float width)
        {
            var rootRect = root.worldBound;
            if (rootRect.width <= 0f) return;

            Rect anchorRect = anchor != null && anchor.panel != null ? anchor.worldBound : card.worldBound;
            float h = card.resolvedStyle.height;
            if (float.IsNaN(h) || h <= 0f) h = 80f;

            float anchorLeft = anchorRect.xMin - rootRect.xMin;
            float anchorRight = anchorRect.xMax - rootRect.xMin;
            float anchorTop = anchorRect.yMin - rootRect.yMin;
            float anchorBottom = anchorRect.yMax - rootRect.yMin;

            float left;
            float top;
            if (anchorRight + Gap + width <= rootRect.width)
            {
                left = anchorRight + Gap;                                                       // right
                top = anchorTop;
            }
            else if (anchorLeft - Gap - width >= 0f)
            {
                left = anchorLeft - Gap - width;                                                // left
                top = anchorTop;
            }
            else
            {
                // An inspector is usually narrower than the moment card plus a snippet, so this is the COMMON
                // path, not an edge case. Below, then above — never beside-and-overlapping, because a peek
                // sitting on top of the card it belongs to is the exact problem this class was built to end.
                left = Mathf.Clamp(anchorLeft, 0f, Mathf.Max(0f, rootRect.width - width));
                // FLIP ABOVE ONLY IF IT ACTUALLY FITS ABOVE — the rule DebugMomentCard.Place and
                // DebugSettingsCard.Place already carry, and the one this card never received when they were
                // fixed. Flipping whenever the card would not fit BELOW produces a negative top, and the
                // clamp that used to sit under this block pinned that to 0 — the TOP OF THE ROOT, which is
                // where the pinned transport bar lives. DebugOverlayHost.Add inserts cards BEFORE the bar and
                // child order is paint order, so the peek ended up UNDERNEATH the toolbar: the one place it
                // cannot be read from at all. Measured at [0, 83] against an anchor at [10, 160] in a 200px
                // root — an ordinary short inspector with the moment card open on the first field's graph.
                // Staying below the anchor is always below the bar, and a peek that runs off the bottom is
                // still readable by scrolling.
                float below = anchorBottom + Gap;
                float above = anchorTop - Gap - h;
                top = below + h > rootRect.height && above >= 0f ? above : below;
            }

            card.style.left = left;
            // No vertical clamp. Every branch above already yields a top inside the root — the two beside
            // branches sit level with the anchor, and the third is now guarded — so the Mathf.Max(0f, ...)
            // that used to be here could only ever fire on the unguarded flip, which is precisely how "does
            // not fit above" became "sits on top of the transport bar".
            card.style.top = top;
        }

        /// <summary>A few lines of highlighted source centred on <paramref name="line"/> (1-based), or null.</summary>
        private static string ReadSnippet(string path, int line)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var lines = System.IO.File.ReadAllLines(path);
                if (lines.Length == 0) return null;
                int idx = Mathf.Clamp(line - 1, 0, lines.Length - 1);
                int from = Mathf.Max(0, idx - 2);
                int to = Mathf.Min(lines.Length - 1, idx + 2);
                var slice = new System.Text.StringBuilder();
                for (int i = from; i <= to; i++)
                {
                    if (i > from) slice.Append('\n');
                    slice.Append(lines[i].TrimEnd());
                }
                return CodeHighlighter.Highlight(slice.ToString());
            }
            catch (Exception)
            {
                // A snippet is a nicety. An unreadable file (locked, deleted between the scan and the hover)
                // means no peek, never a broken card.
                return null;
            }
        }
    }
}
