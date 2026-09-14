using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The condition-debugger overlay: when "Debug Conditions" is on, a node that conditions would hide/disable
    /// is revealed in place with a translucent wash (red = hidden, amber = disabled) + a corner verdict pill.
    /// The overlay is a pickable inset-0 child of the host, so it both blocks editing of the revealed field and
    /// drives a rich floating hover card (PASS/FAIL chips + the evaluation breakdown). Modeled on
    /// <see cref="QuickEditGlow"/>: absolute inset-0, dies with the host on rebuild; one shared card at a time.
    /// </summary>
    public static class ConditionDebugOverlay
    {
        public const string OverlayName = "sasq-condition-debug-overlay";
        public const string PillName = "sasq-condition-debug-pill";
        public const string CardName = "sasq-condition-debug-card";
        public const string DesignPipName = "sasq-condition-design-pip";

        /// <summary>EditorPrefs key for the per-developer live-inspector debug toggle (set via the inspector right-click menu).</summary>
        public const string LivePrefKey = "SasquatchInspector.DebugConditions";

        private const string ClassOverlay = "sasq-cond-debug-overlay";
        private const string ClassHidden = "sasq-cond-debug-hidden";
        private const string ClassDisabled = "sasq-cond-debug-disabled";
        private const string ClassBuildRoot = "sasq-build-root";
        private const string ClassHostHooked = "sasq-cond-debug-hooked";

        // The single floating card shown at a time, and the overlay it currently belongs to.
        private static VisualElement _card;
        private static VisualElement _cardOwner;

        // Whether the card is currently up. Tracked as a flag rather than read back off `resolvedStyle.display`,
        // which only catches up on the next style pass — a show landing in the same frame as a hide has to see
        // the truth immediately. See the `alreadyUp` short-circuit in ShowCard.
        private static bool _cardVisible;

        // The card queued for removal by HideCard (see QueueCardRemoval) and whether a delayCall is already
        // pending for it, so a burst of hover in/out never stacks up more than one queued removal.
        private static VisualElement _pendingCardRemoval;
        private static bool _cardRemovalQueued;

        /// <summary>
        /// Creates / updates / removes the overlay on <paramref name="host"/> to match <paramref name="report"/>
        /// (idempotent). In <paramref name="designMode"/> the overlay never absorbs picking (so designer cards stay
        /// selectable/draggable) and the hover card is driven from the host; otherwise the overlay is pickable so
        /// the revealed field reads as read-only and the overlay itself drives the hover card.
        /// </summary>
        public static void Apply(VisualElement host, ConditionEvaluationReport report, bool designMode = false)
        {
            if (host == null) return;

            var overlay = host.Q<VisualElement>(OverlayName);

            if (report == null || !report.HasOverlay)
            {
                if (overlay != null)
                {
                    if (_cardOwner == overlay) HideCard();
                    overlay.RemoveFromHierarchy();
                }
                return;
            }

            bool created = overlay == null;
            if (created)
            {
                overlay = MakeOverlay(designMode);
                host.Add(overlay);
                if (designMode) EnsureHostHover(host);
            }

            bool hidden = !report.Visible;
            overlay.EnableInClassList(ClassHidden, hidden);
            overlay.EnableInClassList(ClassDisabled, !hidden);

            var pill = overlay.Q<Label>(PillName);
            if (pill != null) pill.text = hidden ? "HIDDEN" : "DISABLED";

            // Stash the latest report so the (already-registered) hover handlers and any open card read live data.
            overlay.userData = report;
            if (_cardOwner == overlay) PopulateCard(_card, report);

            // Keep the overlay painting ON TOP of any custom background/border the field carries from the
            // Appearance styling. It's absolutely positioned (out of flex flow), so reordering it only changes
            // paint order — it never reflows the flex siblings.
            overlay.BringToFront();
        }

        /// <summary>
        /// The lightweight design-canvas indicator: a small corner HIDDEN/DISABLED pip — no wash, no outline, no
        /// hover card, and <see cref="PickingMode.Ignore"/> so it never blocks selecting the field. Lets the author
        /// see a field's live condition state while it stays fully visible and selectable for styling. Idempotent;
        /// removes the pip when the field currently passes its conditions. (Live Preview shows the real hide/disable;
        /// "Debug Conditions" turns on the full reveal-in-place overlay.)
        /// </summary>
        public static void ApplyDesignPip(VisualElement host, ConditionEvaluationReport report)
        {
            if (host == null) return;
            var pip = host.Q<Label>(DesignPipName);

            if (report == null || !report.HasOverlay)
            {
                pip?.RemoveFromHierarchy();
                return;
            }

            bool hidden = !report.Visible;
            if (pip == null)
            {
                pip = new Label { name = DesignPipName, pickingMode = PickingMode.Ignore };
                pip.style.position = Position.Absolute;
                pip.style.top = 2;
                pip.style.right = 2;
                pip.style.paddingLeft = 5;
                pip.style.paddingRight = 5;
                pip.style.paddingTop = 1;
                pip.style.paddingBottom = 1;
                pip.style.fontSize = 9;
                pip.style.unityFontStyleAndWeight = FontStyle.Bold;
                pip.style.color = new Color(1f, 1f, 1f, 0.92f);
                pip.style.borderTopLeftRadius = 3;
                pip.style.borderTopRightRadius = 3;
                pip.style.borderBottomLeftRadius = 3;
                pip.style.borderBottomRightRadius = 3;
                host.Add(pip);
            }

            pip.text = hidden ? "HIDDEN" : "DISABLED";
            pip.style.backgroundColor = hidden
                ? new Color(0.70f, 0.24f, 0.20f, 0.90f)   // muted red
                : new Color(0.78f, 0.55f, 0.13f, 0.90f);  // muted amber
            pip.BringToFront();
        }

        private static VisualElement MakeOverlay(bool designMode)
        {
            // Design mode: ignore picking so the designer card underneath stays selectable/draggable (hover is
            // driven from the host instead). Otherwise: pickable, so it blocks edits to the revealed field and
            // receives the hover itself.
            var overlay = new VisualElement { name = OverlayName, pickingMode = designMode ? PickingMode.Ignore : PickingMode.Position };
            overlay.AddToClassList(ClassOverlay);
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.top = 0;
            overlay.style.right = 0;
            overlay.style.bottom = 0;
            overlay.style.opacity = 0f;
            overlay.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            overlay.style.transitionDuration = new List<TimeValue> { new TimeValue(0.12f, TimeUnit.Second) };
            // Fade in via AttachToPanel rather than schedule.Execute — the initial build pass runs while the
            // element is still DETACHED (a detached scheduler may never tick), so scheduling would leave the
            // overlay stuck at opacity 0 until the next value change. AttachToPanel fires reliably on (re)attach.
            overlay.RegisterCallback<AttachToPanelEvent>(_ => overlay.style.opacity = 1f);

            var pill = new Label { name = PillName, pickingMode = PickingMode.Ignore };
            pill.AddToClassList("sasq-cond-debug-pill");
            overlay.Add(pill);

            if (!designMode)
            {
                overlay.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.currentTarget is VisualElement ov && ov.userData is ConditionEvaluationReport rep)
                        ShowCard(ov, rep);
                });
                overlay.RegisterCallback<PointerLeaveEvent>(evt =>
                {
                    if (_cardOwner == evt.currentTarget) HideCard();
                });
            }
            overlay.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (_cardOwner == overlay) HideCard();
            });

            return overlay;
        }

        // Design-mode hover: the overlay ignores picking, so the host (the designer card) drives the card. Registered
        // once per host; the handler looks up the host's current overlay + report so it no-ops once the field passes.
        private static void EnsureHostHover(VisualElement host)
        {
            if (host.ClassListContains(ClassHostHooked)) return;
            host.AddToClassList(ClassHostHooked);

            host.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (!(evt.currentTarget is VisualElement h)) return;
                var ov = h.Q<VisualElement>(OverlayName);
                if (ov != null && ov.userData is ConditionEvaluationReport rep) ShowCard(ov, rep);
            });
            host.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_cardOwner != null && _cardOwner.parent == host) HideCard();
            });
        }

        // ---- Floating card ----

        private static void ShowCard(VisualElement overlay, ConditionEvaluationReport report)
        {
            var host = overlay.parent;
            var root = FindBuildRoot(host);
            if (root == null)
            {
                // No build root to host an always-on-top card — fall back to the native tooltip.
                host.tooltip = report.Detail;
                return;
            }

            if (_card == null) _card = MakeCard();

            // A show that lands while THIS overlay's card is already up refreshes it in place. Re-running the
            // display/opacity dance would restart the 0.1s fade from zero, so a burst of shows reads as a strobe
            // instead of as one steady card. (The re-pick loop that used to produce such a burst is fixed in
            // PopulateCard — this makes sure a burst from any other source cannot be SEEN either: the
            // member-driver poll re-applying, a re-hover inside one frame, a queued removal racing a hover.)
            bool alreadyUp = _cardVisible && _cardOwner == overlay && _card.parent == root;

            _cardOwner = overlay;
            PopulateCard(_card, report);

            if (_card.parent != root) { _card.RemoveFromHierarchy(); root.Add(_card); }
            if (!alreadyUp)
            {
                _card.style.display = DisplayStyle.Flex;
                _card.style.opacity = 0f;
            }
            _card.BringToFront();
            _cardVisible = true;

            // Position after the card has measured itself, then clamp within the root.
            _card.RegisterCallback<GeometryChangedEvent>(OnCardGeometry);
            PositionCard(_card, host, root);
            if (!alreadyUp) _card.schedule.Execute(() => { if (_card != null) _card.style.opacity = 1f; });
        }

        private static void OnCardGeometry(GeometryChangedEvent evt)
        {
            if (_card == null || _cardOwner == null) return;
            var host = _cardOwner.parent;
            var root = FindBuildRoot(host);
            if (root != null) PositionCard(_card, host, root);
        }

        private static void PositionCard(VisualElement card, VisualElement host, VisualElement root)
        {
            var hostWorld = host.worldBound;
            var rootWorld = root.worldBound;
            if (float.IsNaN(hostWorld.x) || float.IsNaN(rootWorld.x)) return;

            float left = hostWorld.x - rootWorld.x;
            float top = hostWorld.yMax - rootWorld.y + 4f;

            float cardW = card.resolvedStyle.width;
            float cardH = card.resolvedStyle.height;
            float rootW = root.resolvedStyle.width;
            float rootH = root.resolvedStyle.height;

            if (cardW > 0f && rootW > 0f && left + cardW > rootW - 2f)
                left = Mathf.Max(2f, rootW - cardW - 2f);
            if (left < 2f) left = 2f;

            // Flip above the field if it would overflow the bottom.
            if (cardH > 0f && rootH > 0f && top + cardH > rootH - 2f)
            {
                float above = (hostWorld.y - rootWorld.y) - cardH - 4f;
                top = above >= 2f ? above : Mathf.Max(2f, rootH - cardH - 2f);
            }

            card.style.left = left;
            card.style.top = top;
        }

        private static void HideCard()
        {
            var card = _card;
            _cardOwner = null;
            _cardVisible = false;
            if (card == null) return;
            card.UnregisterCallback<GeometryChangedEvent>(OnCardGeometry);
            card.style.display = DisplayStyle.None;

            // …AND take it out of the tree. Hiding alone left this STATIC element parented into whichever build
            // root last hosted a hovered overlay, and both live paths DISCARD that root wholesale on rebuild
            // (rootContainer.Clear() / container.Clear()). Clear() only nulls the DIRECT child's parent link, so
            // a grandchild card keeps pointing at the discarded root and transitively holds the whole
            // Bind(serializedObject)-ed subtree alive — the exact opposite of this overlay's stated "dies with
            // the host on rebuild" contract. ShowCard re-parents on the next hover, so removal costs nothing.
            QueueCardRemoval(card);
        }

        // DEFERRED removal, like QuickEditGlow.Hide — but for a different reason, so it uses a different timer.
        // QuickEditGlow schedules on the ELEMENT so its 0.15s fade-out can play first; here the card is already
        // display:none, and the reason to defer is safety: HideCard runs from a PointerLeave handler and from the
        // overlay's own DetachFromPanelEvent, and the detach case fires while UI Toolkit is walking the very
        // subtree the card sits in — mutating that hierarchy mid-walk is not something to risk. An ELEMENT
        // scheduler is no good for it either: on the rebuild path the card is detached along with the discarded
        // root, and a detached element's scheduler may never tick, which is precisely the case the removal
        // exists to clean up. EditorApplication.delayCall always fires.
        private static void QueueCardRemoval(VisualElement card)
        {
            _pendingCardRemoval = card;
            if (_cardRemovalQueued) return;
            _cardRemovalQueued = true;
            EditorApplication.delayCall += FlushPendingCardRemoval;
        }

        /// <summary>
        /// Performs the removal <see cref="HideCard"/> queued. Internal so tests can pump it deterministically
        /// instead of waiting on an editor tick; the editor itself reaches it through
        /// <see cref="EditorApplication.delayCall"/>.
        /// </summary>
        internal static void FlushPendingCardRemoval()
        {
            _cardRemovalQueued = false;
            var card = _pendingCardRemoval;
            _pendingCardRemoval = null;
            if (card == null) return;
            // A hover landed between the hide and this tick: ShowCard has already re-homed and re-shown this very
            // card, so pulling it out now would blank a card the user is looking at.
            if (_cardOwner != null && ReferenceEquals(card, _card)) return;
            card.RemoveFromHierarchy();
        }

        // ---- Test seams (internal → visible to SasquatchInspector.Tests.Editor via InternalsVisibleTo) -----

        /// <summary>Tests: the shared floating card, or null before one has ever been shown.</summary>
        internal static VisualElement CardForTests => _card;

        /// <summary>Tests: the hover-in path, exactly as the overlay's own PointerEnter handler calls it.</summary>
        internal static void ShowCardForTests(VisualElement overlay, ConditionEvaluationReport report)
            => ShowCard(overlay, report);

        /// <summary>Tests: the hover-out / detach teardown, verbatim.</summary>
        internal static void HideCardForTests() => HideCard();

        private static VisualElement MakeCard()
        {
            var card = new VisualElement { name = CardName, pickingMode = PickingMode.Ignore };
            card.AddToClassList("sasq-cond-card");
            card.style.position = Position.Absolute;
            card.style.display = DisplayStyle.None;
            card.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            card.style.transitionDuration = new List<TimeValue> { new TimeValue(0.1f, TimeUnit.Second) };
            return card;
        }

        private static void PopulateCard(VisualElement card, ConditionEvaluationReport report)
        {
            if (card == null || report == null) return;
            card.Clear();

            bool hidden = report.Verdict == ConditionVerdict.Hidden;

            var title = new Label(hidden ? "HIDDEN" : "DISABLED");
            title.AddToClassList("sasq-cond-card-title");
            title.EnableInClassList("sasq-cond-card-title--hidden", hidden);
            title.EnableInClassList("sasq-cond-card-title--disabled", !hidden);
            card.Add(title);

            foreach (var rule in report.Rules)
            {
                if (rule.Counted) AppendRule(card, rule);
            }

            var divider = new VisualElement();
            divider.AddToClassList("sasq-cond-divider");
            card.Add(divider);

            var headline = new Label(report.Headline);
            headline.AddToClassList("sasq-cond-headline");
            card.Add(headline);

            MakeSubtreeUnpickable(card);
        }

        /// <summary>
        /// Drops <see cref="PickingMode.Ignore"/> on the card AND every descendant, after every populate (the
        /// children are rebuilt from scratch each time, so a one-shot pass at construction would not hold).
        /// </summary>
        /// <remarks>
        /// The card is a pure read-out — nothing in it is interactive, and nothing in it may ever take the
        /// pointer. But <c>pickingMode</c> is PER ELEMENT in UI Toolkit: it does NOT inherit. The card's own
        /// Ignore therefore stopped at the card itself, while every Label / row it builds defaulted straight back
        /// to <see cref="PickingMode.Position"/>. Any one of those children that overlapped the overlay it
        /// describes then STOLE the pick from it — and since the pick is what decides pointer enter/leave, the
        /// overlay was handed a PointerLeave with the mouse sitting still inside its own rect: HideCard → the card
        /// leaves → the overlay is topmost again → PointerEnter → ShowCard → the card is back under the pointer →
        /// PointerLeave … a re-pick loop that ran hundreds of times per mouse move and read as a violent flicker.
        /// That overlap is not an exotic case either: <see cref="PositionCard"/>'s last resort, for when the card
        /// fits in neither the gap below the field nor the one above it, clamps it inside the build root — which
        /// parks it ACROSS the hovered row. A SHORT layout (a handful of fields, most of them conditional) hits
        /// that every time, which is why the debugger flickered on small components and looked fine on big ones.
        /// So the sweep is the fix rather than the placement: an unpickable card may sit anywhere, the pointer
        /// included, and change nothing about which element is under the mouse.
        /// </remarks>
        private static void MakeSubtreeUnpickable(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (var child in element.Children()) MakeSubtreeUnpickable(child);
        }

        // Renders one rule as a sentence header ("Show when all of:") followed by its items — bare terms inline,
        // group items under an indented "all/any of:" sublabel — each term a PASS/FAIL chip + rule + live value.
        private static void AppendRule(VisualElement card, EvaluatedRule rule)
        {
            var header = new Label($"{ConditionEvaluationReport.OutcomeLabel(rule.Outcome)} when {ConditionEvaluationReport.MatchWord(rule.Match)} of:");
            header.AddToClassList("sasq-cond-group-label");
            card.Add(header);

            foreach (var item in rule.Items)
            {
                if (!item.Counted) continue;

                if (item.IsGroup)
                {
                    var sub = new Label($"{ConditionEvaluationReport.MatchWord(item.Match)} of:");
                    sub.AddToClassList("sasq-cond-group-label");
                    sub.style.marginLeft = 10;
                    sub.style.opacity = 0.75f;
                    card.Add(sub);
                    foreach (var t in item.Terms) card.Add(MakeTermRow(t, true));
                }
                else
                {
                    foreach (var t in item.Terms) card.Add(MakeTermRow(t, false));
                }
            }
        }

        private static VisualElement MakeTermRow(ConditionTermResult term, bool indented)
        {
            var row = new VisualElement();
            row.AddToClassList("sasq-cond-row");
            if (indented) row.style.marginLeft = 18;

            var chip = new Label(term.Passed ? "PASS" : "FAIL");
            chip.AddToClassList("sasq-cond-chip");
            chip.AddToClassList(term.Passed ? "sasq-cond-chip-pass" : "sasq-cond-chip-fail");
            row.Add(chip);

            var ruleLabel = new Label(term.RuleText);
            ruleLabel.AddToClassList("sasq-cond-rule");
            row.Add(ruleLabel);

            var actual = new Label($"now {term.ActualValueText}");
            actual.AddToClassList("sasq-cond-actual");
            row.Add(actual);

            return row;
        }

        private static VisualElement FindBuildRoot(VisualElement element)
        {
            var cur = element;
            while (cur != null)
            {
                if (cur.ClassListContains(ClassBuildRoot)) return cur;
                cur = cur.parent;
            }
            return null;
        }
    }
}
