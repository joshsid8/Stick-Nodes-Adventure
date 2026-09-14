using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>Attaches a dropdown autocomplete to a <see cref="TextField"/>, sourced from a live suggestion provider (used by the function/method pickers).</summary>
    public static class AutocompleteTextField
    {
        /// <summary>
        /// A single autocomplete entry. <see cref="Value"/> is what gets committed to the field when accepted;
        /// <see cref="Detail"/> is optional secondary text shown after it in a lighter, smaller style (e.g. a
        /// method's parameter list) — purely informational, never committed.
        /// </summary>
        public struct Suggestion
        {
            public string Value;
            public string Detail;

            /// <summary>Creates a suggestion with a committed <paramref name="value"/> and optional informational <paramref name="detail"/>.</summary>
            public Suggestion(string value, string detail = null)
            {
                Value = value;
                Detail = detail;
            }
        }

        // Back-compat overload: plain string suggestions (value == display, no detail).
        /// <summary>Attaches autocomplete with plain string suggestions (value = display text, no detail).
        /// <paramref name="richPreviewProvider"/> opts into the hover code peek — see the Suggestion overload.</summary>
        public static void Attach(TextField field, Func<IEnumerable<string>> suggestionProvider,
            Func<Suggestion, string> richPreviewProvider = null,
            Action<string> externalPeekShow = null, Action externalPeekHide = null)
        {
            Attach(field, () => suggestionProvider?.Invoke()?.Select(s => new Suggestion(s))
                                ?? Enumerable.Empty<Suggestion>(), richPreviewProvider, externalPeekShow, externalPeekHide);
        }

        // How long a suggestion must stay hovered (or arrow-selected) before its code peek appears —
        // long enough that plain list-skimming never flashes panes, short enough to feel on-demand.
        private const long HoverPreviewDelayMs = 350;

        // How long after a blur the list waits before retiring itself. The delay exists so a click ON the
        // popup (which blurs the field first) lands before the popup dies; the CONDITION on the far side of
        // it — "is focus still inside this autocomplete?" — is what makes the wait safe. Shared by both
        // flavours and both of the suggestion list's arming sites, so the window can never drift apart.
        // internal: the scroll-drag regression tests hold the pointer past this deadline and no further.
        internal const long BlurDismissDelayMs = 150;

        // The list used to stop at 8 rows with no way to see the rest, which was survivable while typing was
        // the only way to narrow it and became a real cap once the chevron started opening the whole list.
        // The height is what bounds the popup now; the count is only a runaway guard on a huge type.
        private const int MaxSuggestions = 200;
        internal const float MaxPopupHeight = 260f;

        // Row geometry, plus the numbers the placement math needs BEFORE any layout pass exists (the popup is
        // positioned the moment it is built, so nothing in it has a resolved height yet).
        // internal: the squish regression test measures real rows against this floor.
        internal const float RowMinHeight = 18f;
        private const float EstimatedRowHeight = 20f;
        private const float MinPopupHeight = 64f;
        private const float PreferredPopupWidth = 260f;

        /// <summary>Element name of the code-peek card both autocomplete flavours raise.</summary>
        internal const string PeekPaneName = "sasq-autocomplete-code-peek";

        /// <summary>Element name of the <c>{fieldName}</c> token suggestion popup.</summary>
        internal const string TokenPopupName = "sasq-token-autocomplete";

        /// <summary>
        /// Re-attaches the stylesheets from <paramref name="field"/>'s own ancestry onto a popup that is parented
        /// to the panel ROOT.
        ///
        /// This is the root cause behind a whole family of popup defects. `panel.visualTree` sits OUTSIDE
        /// `rootVisualElement`, which is where an EditorWindow's theme stylesheet is attached — so an element
        /// parented there inherits NO USS AT ALL. Every Unity control put there arrives unstyled, and a
        /// ScrollView is the worst case, because most of what makes it a ScrollView is USS rather than code:
        /// without it, `unity-content-and-vertical-scroll-container` is not `flex-direction: row`, so the
        /// vertical scroller lays out as a zero-height strip UNDER the list instead of a bar beside it; and the
        /// content viewport has no `overflow: hidden`, so nothing is clipped and the rows paint over the window.
        /// (The font copying below is the same bug, found earlier and patched one symptom at a time.)
        ///
        /// Added top-down — the theme first, the window's own sheets last — so the cascade matches what the
        /// source itself resolves. Inline styles set by the caller still win over all of it.
        ///
        /// The walk INCLUDES <paramref name="source"/>, so a popup that has already adopted can in turn seed
        /// another element parented to the same root (the code-peek card hangs off the panel root too, and its
        /// only reachable styled relative is the popup it belongs to).
        /// </summary>
        /// <summary>Test seam: the adoption mechanism on its own, so it can be measured in a live window
        /// independently of any popup that uses it.</summary>
        internal static void AdoptAncestorStyleSheetsForTests(VisualElement target, VisualElement source)
            => AdoptAncestorStyleSheets(target, source);

        private static void AdoptAncestorStyleSheets(VisualElement popup, VisualElement source)
        {
            if (popup == null || source == null) return;

            var sheets = new List<StyleSheet>();
            for (var ancestor = source; ancestor != null; ancestor = ancestor.parent)
                for (int i = ancestor.styleSheets.count - 1; i >= 0; i--)
                    sheets.Add(ancestor.styleSheets[i]);

            var seen = new HashSet<StyleSheet>();
            for (int i = sheets.Count - 1; i >= 0; i--)
                if (sheets[i] != null && seen.Add(sheets[i]))
                    popup.styleSheets.Add(sheets[i]);
        }

        /// <summary>Element name of the suggestion list popup. Named so a HOST can tell an open list from a closed
        /// one: the quick-edit menu asks before letting Escape close the whole menu, so the first Escape dismisses
        /// the list and only the next one closes the menu (see <c>QuickEditPopup.HasOpenSuggestionList</c>).</summary>
        internal const string SuggestionPopupName = "sasq-autocomplete-suggestions";

        /// <summary>
        /// Attaches autocomplete whose entries come from <paramref name="suggestionProvider"/> (with optional
        /// detail text). <paramref name="richPreviewProvider"/>, when given, turns on the hover code peek: after
        /// a short hover (or arrow-key dwell) on a suggestion, its returned rich text — e.g. a highlighted
        /// source snippet from <see cref="MethodSourcePreview"/> — shows in a code-styled pane attached to the
        /// popup (beside it when there's room, under it in narrow hosts like the inspector). Null/empty = no
        /// peek for that entry.
        ///
        /// <paramref name="externalPeekShow"/>/<paramref name="externalPeekHide"/>, when given, take OVER peek
        /// rendering: instead of an in-panel pane (which a tiny dropdown host like the quick-edit popup would
        /// clip), the host is handed the rich text and draws the peek itself — e.g. as a connected card below
        /// its own window. Show is called with the current snippet (update-in-place as you browse); Hide when
        /// the peek should retire.
        /// </summary>
        public static void Attach(TextField field, Func<IEnumerable<Suggestion>> suggestionProvider,
            Func<Suggestion, string> richPreviewProvider = null,
            Action<string> externalPeekShow = null, Action externalPeekHide = null)
        {
            if (field == null) return;

            // A ScrollView, not a plain container: the chevron and a re-click both open the WHOLE list now, and
            // "the whole list" on a class with thirty methods has to be reachable. VisualElement's indexer,
            // Add, IndexOf and Children() all route through contentContainer, so every row lookup below reads
            // exactly as it did when this was a bare VisualElement.
            ScrollView popup = null;
            List<Suggestion> currentSuggestions = new();
            int selectedIndex = -1;
            bool suppressShow = false;
            VisualElement previewPane = null;
            IVisualElementScheduledItem previewTimer = null;

            Color normalText = DesignerPalette.TextPrimary;
            Color detailText = DesignerPalette.TextMuted;
            Color highlightBg = DesignerPalette.AccentFillSoft;

            field.focusable = true;

            Action dismissPreview = () =>
            {
                previewTimer?.Pause();
                previewTimer = null;
                externalPeekHide?.Invoke(); // no-op unless a host renders the peek in its own window
                if (previewPane != null)
                {
                    previewPane.RemoveFromHierarchy();
                    previewPane = null;
                }
            };

            // Builds and attaches the code peek for suggestion `idx` (see ShowPeekPane for the placement).
            Action<int> showPreviewFor = idx =>
            {
                if (richPreviewProvider == null || popup == null || field.panel == null ||
                    idx < 0 || idx >= currentSuggestions.Count) { dismissPreview(); return; }

                string rich = null;
                try { rich = richPreviewProvider(currentSuggestions[idx]); }
                catch { /* best-effort peek — a provider hiccup just shows nothing */ }
                if (string.IsNullOrEmpty(rich)) { dismissPreview(); return; }

                // A host can render the peek in its OWN window (the quick-edit popup does: its panel is a tiny
                // dropdown that would clip an in-panel pane). Update in place — no dismiss-then-show — so
                // hovering down the suggestion list doesn't flicker the connected peek card.
                if (externalPeekShow != null) { externalPeekShow(rich); return; }

                dismissPreview();
                float rowY = idx < popup.childCount ? popup[idx].worldBound.y : popup.worldBound.y;
                previewPane = ShowPeekPane(rich, popup, field.panel.visualTree, rowY);
            };

            // Arms (or re-arms) the dwell timer that shows the peek for `idx`.
            Action<Func<int>> schedulePreview = resolveIdx =>
            {
                if (richPreviewProvider == null) return;
                previewTimer?.Pause();
                previewTimer = field.schedule.Execute(() => showPreviewFor(resolveIdx())).StartingIn(HoverPreviewDelayMs);
            };

            Action dismissPopup = () =>
            {
                dismissPreview();
                if (popup != null)
                {
                    popup.RemoveFromHierarchy();
                    popup = null;
                }
                selectedIndex = -1;
            };

            // "Is the caret in the field itself?" — the strict test, and the one the OPENING paths want: a
            // list is raised by typing/clicking in the field, never by focus sitting on the popup's scroller.
            Func<bool> isFieldFocused = () =>
            {
                var focused = field.panel?.focusController?.focusedElement as VisualElement;
                while (focused != null)
                {
                    if (focused == field) return true;
                    focused = focused.parent;
                }
                return false;
            };

            // Is focus still somewhere that counts as "inside this autocomplete"?
            //
            // The field-only version of this walk is what made DRAGGING THE LIST'S OWN SCROLLBAR close the
            // list: the popup is parented to the PANEL ROOT, not to the field, and Unity's ScrollView carries
            // a FOCUSABLE `ScrollerSlider` (measured: `slider.focusable == true`, the dragger itself false).
            // So pressing the scroll handle blurs the field and moves focus to the slider — a legitimate
            // interaction WITH the popup that read as "the user left", and the 150ms blur deadline then tore
            // the list down mid-drag. Time-based, not movement-based, which is exactly how it was reported.
            //
            // The popup, not just the field, is therefore ours. Anything Unity decides to make focusable
            // inside that ScrollView in a future version is covered by the same walk.
            Func<bool> focusIsInsideAutocomplete = () =>
            {
                var focused = field.panel?.focusController?.focusedElement as VisualElement;
                while (focused != null)
                {
                    if (focused == field || (popup != null && focused == popup)) return true;
                    focused = focused.parent;
                }
                return false;
            };

            // The shared blur deadline. Deferred so a click ON the popup lands first, and CONDITIONAL so a
            // blur that is immediately taken back — by the popup's own scroller, or by a host that rewrites
            // the text and re-focuses a tick later — never closes a list that is legitimately still in use.
            Action armBlurDismiss = () => field.schedule.Execute(() =>
            {
                if (!focusIsInsideAutocomplete()) dismissPopup();
            }).ExecuteLater(BlurDismissDelayMs);

            Action<string> selectValue = (val) =>
            {
                // Accepting a suggestion sets field.value programmatically. We're inside an event dispatch
                // (a Tab/Enter key handler or a suggestion's pointer-down), so the resulting ChangeEvent is
                // queued and fires AFTER we dismiss — which would immediately re-open the popup (the new
                // text matches itself). Suppress that one re-show so accepting actually closes the list.
                // Only a real value change raises the event, so don't arm the flag when the text is
                // already `val` (otherwise the next genuine keystroke would be wrongly swallowed).
                suppressShow = !string.Equals(field.value, val, StringComparison.Ordinal);
                field.value = val;
                dismissPopup();
                field.Focus();
            };

            Action updateSelectionVisuals = () =>
            {
                if (popup == null) return;
                var children = popup.Children().ToList();
                for (int i = 0; i < children.Count; i++)
                {
                    var row = children[i];
                    bool selected = i == selectedIndex;
                    if (selected) row.style.backgroundColor = highlightBg;
                    else row.style.backgroundColor = StyleKeyword.Null;

                    // First child is the value label; tint it white when selected.
                    if (row.childCount > 0 && row[0] is Label nameLabel)
                        nameLabel.style.color = selected ? Color.white : normalText;
                }
            };

            // showAll bypasses the type-to-filter narrowing — what the chevron and a re-click want, because at
            // that point the text is a value you already committed to, not a query you are still typing.
            Action<bool> showSuggestions = null;
            showSuggestions = showAll =>
            {
                dismissPopup();

                var allSuggestions = suggestionProvider?.Invoke();
                if (allSuggestions == null) return;

                var pool = allSuggestions.Where(s => s.Value != null).ToList();
                string typedText = field.value ?? "";

                // A field holding an EXACT match is a committed value, not half-typed input. Filtering by it
                // left a one-row list showing you the entry you were trying to get away from — which is what
                // made re-opening a full field feel like it did nothing. Show everything and land on the
                // current entry instead, the same rule the UnityEvent function picker follows.
                bool committed = pool.Any(s => string.Equals(s.Value, typedText, StringComparison.Ordinal));

                currentSuggestions = (showAll || committed
                        ? pool
                        : pool.Where(s => s.Value.IndexOf(typedText, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(MaxSuggestions)
                    .ToList();

                if (currentSuggestions.Count == 0) return;

                popup = new ScrollView(ScrollViewMode.Vertical) { name = SuggestionPopupName };
                popup.verticalScrollerVisibility = ScrollerVisibility.Auto;
                popup.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                popup.style.position = Position.Absolute;
                // The content container is itself a flex child of contentViewport, so with UI Toolkit's default
                // flex-shrink of 1 it is squashed to the viewport's height. A ScrollView derives its whole scroll
                // RANGE from that layout, so a shrunk container reports "the content fits" (range 0) however tall
                // the rows inside it actually are: no scrollbar, nothing to scroll, and — because the vertical
                // scroller then never goes live — a NullReferenceException out of Unity's own wheel handler.
                // The rows opt out of shrinking too (see below); BOTH levels are needed, and the container level
                // is invisible without measuring, because the rows look right while the list still cannot scroll.
                // FIRST: give it the editor's stylesheets. A ScrollView on the panel root is otherwise entirely
                // unstyled — see AdoptAncestorStyleSheets. Everything set inline below still overrides it.
                AdoptAncestorStyleSheets(popup, field);

                popup.contentContainer.style.flexShrink = 0;
                // …and now the OTHER half of that trade. Making the container un-shrinkable also makes it the
                // contentViewport's min-content height, and a flex item's automatic minimum (min-height: auto)
                // refuses to shrink below that — so the VIEWPORT grew to the full 60-row height and painted
                // straight out the bottom of the popup's own box, over the panel, unbounded. `min-height: 0` is
                // the standard flexbox release valve; the explicit overflow is the guarantee that no descendant
                // can ever paint outside the box again, whatever a future Unity changes about ScrollView.
                popup.contentViewport.style.minHeight = 0;
                popup.style.overflow = Overflow.Hidden;

                // ---- Placement, CLAMPED TO THE PANEL ---------------------------------------------------------
                // The popup parents to the panel ROOT, so nothing else bounds it: a long list under a field near
                // the bottom of a short properties panel simply ran off the window with its tail unreachable.
                var rootRect = field.panel.visualTree.worldBound;
                var fieldRect = field.worldBound;
                bool haveRoot = rootRect.width > 1f && rootRect.height > 1f;

                // Width: fill the room to the field's right instead of a flat 200px. A driver box is ~104px wide
                // and a real serialized path ("inventory.slots.Array.data[0].displayName") needs far more.
                float availWidth = haveRoot ? Mathf.Max(160f, rootRect.xMax - fieldRect.x - 6f) : PreferredPopupWidth;
                float width = Mathf.Clamp(Mathf.Max(fieldRect.width, PreferredPopupWidth), 160f, availWidth);
                float left = haveRoot
                    ? Mathf.Max(rootRect.xMin + 4f, Mathf.Min(fieldRect.x, rootRect.xMax - width - 4f))
                    : fieldRect.x;
                popup.style.width = width;
                popup.style.left = left;

                // Height: below the field by default, flipped ABOVE when there is materially more room up there,
                // and clamped to whichever side it lands on — so the list is never taller than the panel holding
                // it. Sized from an ESTIMATED row height because no row has been laid out yet.
                float roomBelow = haveRoot ? rootRect.yMax - fieldRect.yMax - 6f : MaxPopupHeight;
                float roomAbove = haveRoot ? fieldRect.yMin - rootRect.yMin - 6f : MaxPopupHeight;
                float wantedHeight = Mathf.Min(MaxPopupHeight, currentSuggestions.Count * EstimatedRowHeight + 6f);
                bool flipUp = haveRoot && roomBelow < wantedHeight && roomAbove > roomBelow;
                popup.style.maxHeight = Mathf.Max(MinPopupHeight,
                    Mathf.Min(MaxPopupHeight, flipUp ? roomAbove : roomBelow));
                if (flipUp)
                {
                    // Anchored by its BOTTOM edge so it grows upward from the field without needing a resolved
                    // height (which does not exist yet).
                    popup.style.top = StyleKeyword.Auto;
                    popup.style.bottom = rootRect.yMax - fieldRect.yMin + 2f;
                }
                else
                {
                    popup.style.top = fieldRect.yMax;
                }

                // OPAQUE, not the near-opaque popup fill: this floats over a dense properties panel, and at 95%
                // the buttons and section headers underneath read straight through the suggestion text.
                popup.style.backgroundColor = DesignerPalette.PopupBgOpaque;
                popup.style.borderTopWidth = 1;
                popup.style.borderBottomWidth = 1;
                popup.style.borderLeftWidth = 1;
                popup.style.borderRightWidth = 1;
                popup.style.borderTopColor = DesignerPalette.BorderMid;
                popup.style.borderBottomColor = DesignerPalette.BorderMid;
                popup.style.borderLeftColor = DesignerPalette.BorderMid;
                popup.style.borderRightColor = DesignerPalette.BorderMid;
                popup.style.borderTopLeftRadius = 4;
                popup.style.borderTopRightRadius = 4;
                popup.style.borderBottomLeftRadius = 4;
                popup.style.borderBottomRightRadius = 4;
                popup.style.paddingTop = 2;
                popup.style.paddingBottom = 2;

                // The popup is parented to the panel root, where Labels don't reliably inherit
                // the editor's default font (text renders invisibly). Copy the field's resolved
                // font onto the popup so the suggestion labels cascade it.
                var fontDef = field.resolvedStyle.unityFontDefinition;
                if (fontDef.fontAsset != null || fontDef.font != null)
                {
                    popup.style.unityFontDefinition = fontDef;
                }
                else if (field.resolvedStyle.unityFont != null)
                {
                    popup.style.unityFont = field.resolvedStyle.unityFont;
                }
                // Match the field's text size so suggestions read like the rest of the panel.
                float baseFontSize = field.resolvedStyle.fontSize;
                popup.style.fontSize = baseFontSize;

                for (int i = 0; i < currentSuggestions.Count; i++)
                {
                    var sug = currentSuggestions[i];
                    string val = sug.Value;

                    // A row so the value and its (lighter, smaller) detail can be styled independently.
                    var item = new VisualElement();
                    item.style.flexDirection = FlexDirection.Row;
                    item.style.alignItems = Align.Center;
                    item.style.paddingLeft = 8;
                    item.style.paddingRight = 8;
                    item.style.paddingTop = 3;
                    item.style.paddingBottom = 3;
                    // THE squish fix. A ScrollView's content container is a flex COLUMN and UI Toolkit's default
                    // flex-shrink is 1, so once the rows total more than maxHeight EVERY ROW SHRINKS to fit
                    // instead of overflowing. On a script with many bindings that turned ~35 suggestions into 35
                    // unreadable slivers — and no scrollbar appeared, because nothing ever overflowed. (Same
                    // reason the wheel threw: a ScrollView with nothing to scroll has no live vertical scroller.)
                    item.style.flexShrink = 0;
                    item.style.minHeight = RowMinHeight;

                    var nameLabel = new Label(val);
                    nameLabel.style.color = normalText;
                    // A long serialized path ellipsizes rather than wrapping to a second line — ragged
                    // double-height rows are the other half of what made this list look messy.
                    nameLabel.style.flexShrink = 1;
                    nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
                    nameLabel.style.overflow = Overflow.Hidden;
                    nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                    item.Add(nameLabel);

                    if (!string.IsNullOrEmpty(sug.Detail))
                    {
                        var detailLabel = new Label(sug.Detail);
                        detailLabel.style.color = detailText;
                        detailLabel.style.fontSize = Mathf.Max(9f, baseFontSize - 2f);
                        detailLabel.style.marginLeft = 6;
                        detailLabel.style.flexShrink = 0;   // the name gives way first; the hint stays readable
                        detailLabel.style.whiteSpace = WhiteSpace.NoWrap;
                        detailLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                        item.Add(detailLabel);
                    }

                    item.RegisterCallback<MouseEnterEvent>(evt =>
                    {
                        if (popup.IndexOf(item) != selectedIndex)
                            item.style.backgroundColor = DesignerPalette.AccentTabBg;
                        // Dwell-to-peek: resolve the index at fire time — rows can't reorder, but the
                        // popup may have been rebuilt by a keystroke between hover and fire.
                        schedulePreview(() => popup != null ? popup.IndexOf(item) : -1);
                    });
                    item.RegisterCallback<MouseLeaveEvent>(evt =>
                    {
                        if (popup.IndexOf(item) != selectedIndex)
                            item.style.backgroundColor = StyleKeyword.Null;
                        else
                            item.style.backgroundColor = highlightBg;
                        previewTimer?.Pause(); // an un-fired peek dies with the hover; a shown one persists until the next row/dismiss
                    });
                    item.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        selectValue(val);
                        evt.StopPropagation();
                    });

                    popup.Add(item);
                }

                // Leaving the popup entirely retires the peek — it exists for while you're browsing the list.
                popup.RegisterCallback<MouseLeaveEvent>(_ => dismissPreview());

                // The list lives in the DESIGNER's own panel, so a wheel over it would otherwise also scroll the
                // properties panel behind — dragging the field out from under its own popup. It scrolls ITSELF
                // here, in the TRICKLE-DOWN phase, so Unity's ScrollView.OnScrollWheel never runs at all: that
                // handler reads a single-line height off the live vertical scroller, and threw a
                // NullReferenceException whenever the list had no scroller to read (see the flex-shrink note
                // above) — a throw inside event dispatch that takes the whole panel's input down with it.
                // Capturing on the ScrollView cuts the event off before contentViewport, where Unity's is bound.
                popup.RegisterCallback<WheelEvent>(e =>
                {
                    var sv = popup;
                    if (sv == null) return;
                    float maxScroll = Mathf.Max(0f, sv.contentContainer.layout.height - sv.contentViewport.layout.height);
                    var offset = sv.scrollOffset;
                    offset.y = Mathf.Clamp(offset.y + e.delta.y * EstimatedRowHeight, 0f, maxScroll);
                    sv.scrollOffset = offset;
                    e.StopPropagation();
                    e.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);

                // Focus can leave the FIELD without ever coming back to it — press the list's scrollbar and
                // focus is on the popup's slider, so the field's own FocusOut has already fired and will not
                // fire again. Without a deadline armed from HERE too, clicking away after a scroll-drag would
                // strand the list on screen forever. FocusOut bubbles, so the ScrollView sees its slider's.
                popup.RegisterCallback<FocusOutEvent>(_ => armBlurDismiss());

                // Hand focus back when a scroll-drag ends. The list stays open either way now, but ↑/↓/Enter/
                // Escape are all registered on the FIELD, so leaving focus parked on the scroller would leave
                // an open list you can only work with the mouse — and Escape dead, which the quick-edit menu
                // relies on (it defers its own Escape to an open list). Pointer UP, never down: during the
                // drag the slider must keep the pointer. Rows commit on pointer DOWN and dismiss the popup,
                // so a row click never reaches this.
                //
                // TRICKLE-DOWN, and this is not a style choice: the scroller's dragger is driven by a
                // Clickable, and Clickable STOPS PROPAGATION on the release — so a bubble-phase handler here
                // is never called at all (measured: the release never reached the ScrollView). Trickling
                // means we see it on the way IN, before the slider consumes it.
                //
                // The re-focus itself is deferred to the next tick rather than run mid-dispatch, so the
                // slider still completes its own release (and drops the pointer capture) against the focus
                // it had when the gesture started. The state is re-tested at fire time.
                popup.RegisterCallback<PointerUpEvent>(_ =>
                {
                    if (popup == null || isFieldFocused() || !focusIsInsideAutocomplete()) return;
                    field.schedule.Execute(() =>
                    {
                        if (popup == null || isFieldFocused() || !focusIsInsideAutocomplete()) return;
                        field.Focus();
                    }).ExecuteLater(0);
                }, TrickleDown.TrickleDown);

                field.panel.visualTree.Add(popup);
                popup.BringToFront();

                // Land on the committed entry: highlighted, so the list says which one is live, and scrolled
                // into view, so that answer survives a list too long to show at once. ↑/↓ then start there
                // rather than at the top.
                selectedIndex = currentSuggestions.FindIndex(
                    s => string.Equals(s.Value, typedText, StringComparison.Ordinal));
                if (selectedIndex >= 0)
                {
                    updateSelectionVisuals();
                    // ScrollTo before the ScrollView has a resolved layout is a silent no-op, so it has to wait
                    // for one — the trap the UnityEvent picker's reopen-scroll hit. A one-shot GeometryChanged
                    // is the guaranteed post-layout hook; a bare schedule can still land a frame too early.
                    // Held in a variable rather than written as a local function so Register and Unregister are
                    // provably the same delegate instance.
                    EventCallback<GeometryChangedEvent> scrollOnce = null;
                    scrollOnce = _ =>
                    {
                        popup?.UnregisterCallback(scrollOnce);
                        if (popup != null && selectedIndex >= 0 && selectedIndex < popup.childCount)
                            popup.ScrollTo(popup[selectedIndex]);
                    };
                    popup.RegisterCallback(scrollOnce);
                }
            };

            // A text-free at-rest marker so users can tell — just by looking — that a field carries
            // autocomplete. A muted down-chevron pinned at the input's right edge reads as "there's a list
            // here" and matches the tool's other field chrome.
            const string AutocompleteMarkerClass = "sasq-autocomplete-marker";
            if (field.Q(className: AutocompleteMarkerClass) == null)
            {
                var input = field.Q<VisualElement>(className: "unity-base-text-field__input")
                            ?? field.Q<VisualElement>(className: "unity-base-field__input");
                if (input != null)
                {
                    // Full-height host centers the glyph via layout (justify/align center), never a top:50% + translate
                    // render transform — that half-pixel shift lands hairlines between device rows at 125%/150% scaling.
                    var marker = new VisualElement();
                    marker.AddToClassList(AutocompleteMarkerClass);
                    marker.style.position = Position.Absolute;
                    marker.style.right = 3;
                    marker.style.top = 0;
                    marker.style.bottom = 0;
                    marker.style.width = 12;
                    marker.style.justifyContent = Justify.Center;
                    marker.style.alignItems = Align.Center;
                    marker.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ChevronDown, 12f, DesignerPalette.IconMuted));

                    // The chevron is a REAL target, not decoration. It looks exactly like the thing that opens
                    // the list, so that is where the hand goes — and it used to be PickingMode.Ignore, meaning
                    // the click fell through and merely dropped a caret in the text. Clicking it opens the FULL
                    // list, which is also the way out of a field you filled with the wrong entry: the list you
                    // need at that moment is precisely the one that is NOT narrowed to the value you are
                    // replacing. Clicking again closes it, the way a dropdown does.
                    marker.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        // Stop before the TextField sees it: a click reaching the field would place a caret and,
                        // on a first click, arm the FocusIn path to reopen the list narrowed a moment later.
                        evt.StopPropagation();
                        if (popup != null) { dismissPopup(); return; }
                        field.Focus();        // so ↑/↓/Enter work immediately, exactly as when you type
                        showSuggestions(true);
                    });

                    // Reserve room on the INPUT so typed text stops short of the chevron.
                    input.style.paddingRight = 15;

                    // The chevron itself must be parented to the FIELD, NOT the input. Unity's text input
                    // carries overflow:hidden and clips its children to its CONTENT box — i.e. INSIDE the
                    // padding we just reserved. A marker sitting in that right-edge padding strip is
                    // geometrically valid, Visible, opacity 1, and un-clipped by any ancestor's *bounds*
                    // (so it looks present in the visual tree and in every geometry check) yet its worldClip
                    // collapses to zero width and it paints NOTHING. The outer TextField imposes no such
                    // clip, and its right edge coincides with the input's, so anchoring the marker here draws
                    // the glyph over the reserved gap exactly where it was meant to sit. (Verified live: marker
                    // in-input → clipped visible-width 0px; marker on-field → visible-width 12px.)
                    field.Add(marker);
                }
            }

            field.RegisterValueChangedCallback(evt =>
            {
                // Swallow the single deferred ChangeEvent raised by accepting a suggestion (see selectValue).
                if (suppressShow)
                {
                    suppressShow = false;
                    return;
                }
                if (isFieldFocused())
                {
                    showSuggestions(false);
                }
            });

            field.RegisterCallback<FocusInEvent>(evt =>
            {
                field.schedule.Execute(() =>
                {
                    // popup != null ⇒ the chevron already opened the full list on this same click; leave it be
                    // rather than replacing it with the narrowed one.
                    if (isFieldFocused() && popup == null)
                    {
                        showSuggestions(false);
                    }
                }).ExecuteLater(10);
            });

            // Clicking a field that is ALREADY focused raises no FocusIn, so nothing reopened the list — that,
            // plus the self-filtering, is the whole of "clicking a full field does nothing". Deferred past the
            // click so the caret/focus work settles first, and skipped when a list is already up (that click is
            // placing a caret, not asking for the list again).
            field.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || popup != null) return;
                field.schedule.Execute(() =>
                {
                    if (isFieldFocused() && popup == null) showSuggestions(false);
                }).ExecuteLater(12);
            });

            // Both arming sites go through armBlurDismiss — see it for why the check is conditional and why
            // "still ours" has to include the popup, not just the field.
            field.RegisterCallback<FocusOutEvent>(evt => armBlurDismiss());

            // The popup lives on the panel root, so it survives the field being removed (e.g. when
            // selecting another node rebuilds the properties panel). Dismiss it on detach so it
            // can't get orphaned/stuck on screen.
            field.RegisterCallback<DetachFromPanelEvent>(evt => dismissPopup());

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (popup == null) return;

                switch (evt.keyCode)
                {
                    case KeyCode.DownArrow:
                        selectedIndex = (selectedIndex + 1) % currentSuggestions.Count;
                        updateSelectionVisuals();
                        schedulePreview(() => selectedIndex); // dwell on an arrow-selected row peeks too
                        evt.StopPropagation();
                        break;
                    case KeyCode.UpArrow:
                        selectedIndex = selectedIndex <= 0 ? currentSuggestions.Count - 1 : selectedIndex - 1;
                        updateSelectionVisuals();
                        schedulePreview(() => selectedIndex);
                        evt.StopPropagation();
                        break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                    case KeyCode.Tab:
                        if (selectedIndex >= 0 && selectedIndex < currentSuggestions.Count)
                        {
                            selectValue(currentSuggestions[selectedIndex].Value);
                            evt.StopPropagation();
                        }
                        else
                        {
                            dismissPopup();
                        }
                        break;
                    case KeyCode.Escape:
                        dismissPopup();
                        evt.StopPropagation();
                        break;
                }
            }, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Builds the code-peek card for <paramref name="rich"/>, parents it to <paramref name="panelRoot"/> and
        /// places it against <paramref name="popup"/>: beside the popup when the panel has the width for it
        /// (designer), otherwise attached underneath (the inspector's popup spans nearly the full window, so
        /// there is never side room there), flipping above when the bottom would clip. <paramref name="rowY"/>
        /// is the hovered row's top, used to align a side-attached card. Shared by BOTH autocomplete flavours.
        /// </summary>
        private static VisualElement ShowPeekPane(string rich, VisualElement popup, VisualElement panelRoot, float rowY)
        {
            // Named so a test (and anyone reading a UI Toolkit Debugger tree) can pick the card out of a
            // panel root it shares with the popup and the window's own containers.
            var pane = new VisualElement { name = PeekPaneName, pickingMode = PickingMode.Ignore };
            // Panel root again — see AdoptAncestorStyleSheets. The popup is this pane's only styled relative,
            // and it has already adopted, so it is the seed. (The explicit code font below predates this and
            // stays: it is a deliberate face choice, not a fallback for the missing cascade.)
            AdoptAncestorStyleSheets(pane, popup);
            pane.style.position = Position.Absolute;
            pane.style.backgroundColor = DesignerPalette.CodeBlockBg;
            pane.style.borderTopWidth = 1;
            pane.style.borderBottomWidth = 1;
            pane.style.borderLeftWidth = 1;
            pane.style.borderRightWidth = 1;
            pane.style.borderTopColor = DesignerPalette.BorderMid;
            pane.style.borderBottomColor = DesignerPalette.BorderMid;
            pane.style.borderLeftColor = DesignerPalette.BorderMid;
            pane.style.borderRightColor = DesignerPalette.BorderMid;
            pane.style.borderTopLeftRadius = 4;
            pane.style.borderTopRightRadius = 4;
            pane.style.borderBottomLeftRadius = 4;
            pane.style.borderBottomRightRadius = 4;
            pane.style.paddingLeft = 8;
            pane.style.paddingRight = 8;
            pane.style.paddingTop = 6;
            pane.style.paddingBottom = 6;
            pane.style.maxWidth = 440;
            pane.style.overflow = Overflow.Hidden;

            var code = new Label { enableRichText = true, text = rich, pickingMode = PickingMode.Ignore };
            code.style.color = DesignerPalette.CodeText;
            code.style.whiteSpace = WhiteSpace.NoWrap;
            code.style.fontSize = 11;
            // The pane hangs off the PANEL ROOT, which inherits no font of its own — a fontless Label
            // there measures 0x0 and the peek renders as an empty box, so this must never no-op.
            CodeHighlighter.ApplyCodeFont(code);
            pane.Add(code);

            // Provisional spot (under the popup) so the first painted frame is already sane; the
            // geometry callback below refines once the pane's real size is known.
            pane.style.left = popup.worldBound.x;
            pane.style.top = popup.worldBound.yMax + 4;
            pane.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (pane.panel == null || popup.panel == null) return;
                float w = pane.resolvedStyle.width;
                float h = pane.resolvedStyle.height;
                float panelRight = panelRoot.worldBound.xMax - 2;
                float panelBottom = panelRoot.worldBound.yMax - 2;
                if (popup.worldBound.xMax + 4 + w <= panelRight)
                {
                    // Side-attached (designer-sized hosts): align with the hovered row.
                    pane.style.left = popup.worldBound.xMax + 4;
                    pane.style.top = Mathf.Max(2, Mathf.Min(rowY, panelBottom - h));
                }
                else
                {
                    // Bottom-attached (narrow hosts): under the popup, above it when that would clip.
                    pane.style.left = Mathf.Max(2, Mathf.Min(popup.worldBound.x, panelRight - w));
                    float below = popup.worldBound.yMax + 4;
                    pane.style.top = below + h <= panelBottom
                        ? below
                        : Mathf.Max(2, popup.worldBound.y - 4 - h);
                }
            });
            panelRoot.Add(pane);
            pane.BringToFront();
            return pane;
        }

        /// <summary>
        /// TOKEN-scoped autocomplete for <c>{fieldName}</c> references inside a larger text (the
        /// Text widget's rich editor): suggestions appear only while the caret sits inside an open
        /// "{identifier" run, filter on the partial name, and accepting completes just that token —
        /// adding the closing brace when absent — leaving the rest of the text untouched. Popup
        /// look and keyboard handling mirror <see cref="Attach"/>. A light poll keeps the popup in
        /// sync with caret-only moves (arrow keys, clicks, programmatic SelectRange), which raise
        /// no ChangeEvent.
        ///
        /// <paramref name="richPreviewProvider"/>, when given, turns on the same hover code peek the method /
        /// property pickers have: dwell on a suggested field name and its declaration — attributes included —
        /// shows in a code-styled pane beside the popup. Null/empty = no peek for that name.
        /// </summary>
        public static void AttachTokenAutocomplete(TextField field, Func<IEnumerable<string>> suggestionProvider,
            Func<string, string> richPreviewProvider = null)
        {
            if (field == null) return;

            VisualElement popup = null;
            List<string> currentNames = new();
            int selectedIndex = -1;
            int tokenBraceStart = -1;
            int tokenCaretEnd = -1; // captured at Show() — clicking a suggestion blurs the field, resetting cursorIndex
            string lastShownQuery = null;
            bool suppressShow = false;
            VisualElement previewPane = null;
            IVisualElementScheduledItem previewTimer = null;

            Color normalText = DesignerPalette.TextPrimary;
            Color highlightBg = DesignerPalette.AccentFillSoft;

            void DismissPreview()
            {
                previewTimer?.Pause();
                previewTimer = null;
                if (previewPane != null)
                {
                    previewPane.RemoveFromHierarchy();
                    previewPane = null;
                }
            }

            // Builds and attaches the code peek for the suggestion at `idx` — the declaration of the field
            // that token would reference, which is the whole point: a {token} says nothing about the field's
            // type or its [Range]/[Tooltip] attributes, and the peek does.
            void ShowPreviewFor(int idx)
            {
                if (richPreviewProvider == null || popup == null || field.panel == null ||
                    idx < 0 || idx >= currentNames.Count) { DismissPreview(); return; }

                string rich = null;
                try { rich = richPreviewProvider(currentNames[idx]); }
                catch { /* best-effort peek — a provider hiccup just shows nothing */ }
                if (string.IsNullOrEmpty(rich)) { DismissPreview(); return; }

                DismissPreview();
                float rowY = idx < popup.childCount ? popup[idx].worldBound.y : popup.worldBound.y;
                previewPane = ShowPeekPane(rich, popup, field.panel.visualTree, rowY);
            }

            // Arms (or re-arms) the dwell timer that shows the peek for the row `resolveIdx` picks at fire time.
            void SchedulePreview(Func<int> resolveIdx)
            {
                if (richPreviewProvider == null) return;
                previewTimer?.Pause();
                previewTimer = field.schedule.Execute(() => ShowPreviewFor(resolveIdx())).StartingIn(HoverPreviewDelayMs);
            }

            void Dismiss()
            {
                DismissPreview();
                popup?.RemoveFromHierarchy();
                popup = null;
                selectedIndex = -1;
                lastShownQuery = null;
                tokenBraceStart = -1; // clear the capture with the query, so re-entering the SAME token re-Shows
            }

            bool IsFieldFocused()
            {
                var focused = field.panel?.focusController?.focusedElement as VisualElement;
                while (focused != null)
                {
                    if (focused == field) return true;
                    focused = focused.parent;
                }
                return false;
            }

            // Same rule as the suggestion flavour's focusIsInsideAutocomplete, and same reason: this popup is
            // parented to the PANEL ROOT, so focus landing on anything focusable INSIDE it is not the user
            // leaving. Nothing in this list is focusable today (plain rows, no ScrollView — it is capped at 8
            // entries), but the suggestion list was plain too until it grew one, and that is the change that
            // shipped the scroll-drag dismissal. Both dismissal paths ask this instead of IsFieldFocused.
            bool FocusIsInsideAutocomplete()
            {
                var focused = field.panel?.focusController?.focusedElement as VisualElement;
                while (focused != null)
                {
                    if (focused == field || (popup != null && focused == popup)) return true;
                    focused = focused.parent;
                }
                return false;
            }

            // The token under the caret: scans back over identifier chars to an opening '{'.
            // query = the partial name typed so far ("" right after the brace).
            bool TryGetTokenQuery(out int braceStart, out string query)
            {
                braceStart = -1;
                query = null;
                string text = field.value ?? string.Empty;
                int caret = Mathf.Clamp(field.cursorIndex, 0, text.Length);
                for (int i = caret - 1; i >= 0; i--)
                {
                    char ch = text[i];
                    if (ch == '{')
                    {
                        braceStart = i;
                        query = text.Substring(i + 1, caret - i - 1);
                        return true;
                    }
                    if (!char.IsLetterOrDigit(ch) && ch != '_') return false;
                }
                return false;
            }

            void UpdateSelectionVisuals()
            {
                if (popup == null) return;
                var children = popup.Children().ToList();
                for (int i = 0; i < children.Count; i++)
                {
                    bool selected = i == selectedIndex;
                    children[i].style.backgroundColor = selected ? highlightBg : (StyleColor)StyleKeyword.Null;
                    if (children[i].childCount > 0 && children[i][0] is Label nameLabel)
                        nameLabel.style.color = selected ? Color.white : normalText;
                }
            }

            void Commit(string name)
            {
                // Use the token bounds captured at Show() — a pointer-down on the popup blurs the
                // field first, which resets cursorIndex and would corrupt a live read. Any text or
                // caret change between Show and Commit re-runs Show (ChangeEvent + the poll), so
                // the captured bounds are current.
                string text = field.value ?? string.Empty;
                int caret = Mathf.Clamp(tokenCaretEnd, 0, text.Length);
                if (tokenBraceStart < 0 || tokenBraceStart >= text.Length || text[tokenBraceStart] != '{' || caret <= tokenBraceStart)
                {
                    Dismiss();
                    return;
                }
                bool hasClose = caret < text.Length && text[caret] == '}';
                string updated = text.Substring(0, tokenBraceStart + 1) + name + (hasClose ? "" : "}") + text.Substring(caret);
                int newCaret = tokenBraceStart + 1 + name.Length + 1; // just past the closing brace
                suppressShow = true;
                field.value = updated;
                Dismiss();
                field.schedule.Execute(() =>
                {
                    field.Focus();
                    field.SelectRange(newCaret, newCaret);
                }).ExecuteLater(0);
            }

            void Show()
            {
                if (!TryGetTokenQuery(out int braceStart, out string query))
                {
                    Dismiss();
                    return;
                }

                Dismiss();
                var all = suggestionProvider?.Invoke();
                if (all == null) return;
                currentNames = all
                    .Where(s => !string.IsNullOrEmpty(s) &&
                                (query.Length == 0 || s.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(8)
                    .ToList();
                if (currentNames.Count == 0) return;

                tokenBraceStart = braceStart;
                tokenCaretEnd = braceStart + 1 + query.Length;
                lastShownQuery = query;

                popup = new VisualElement { name = TokenPopupName };
                AdoptAncestorStyleSheets(popup, field);   // same panel-root blind spot as the suggestion list
                popup.style.position = Position.Absolute;
                popup.style.left = field.worldBound.x;
                popup.style.top = field.worldBound.yMax;
                popup.style.width = Mathf.Max(200f, field.worldBound.width);
                // Opaque for the same reason as the suggestion list: at 95% the panel behind reads through it.
                popup.style.backgroundColor = DesignerPalette.PopupBgOpaque;
                popup.style.borderTopWidth = 1;
                popup.style.borderBottomWidth = 1;
                popup.style.borderLeftWidth = 1;
                popup.style.borderRightWidth = 1;
                popup.style.borderTopColor = DesignerPalette.BorderMid;
                popup.style.borderBottomColor = DesignerPalette.BorderMid;
                popup.style.borderLeftColor = DesignerPalette.BorderMid;
                popup.style.borderRightColor = DesignerPalette.BorderMid;
                popup.style.borderTopLeftRadius = 4;
                popup.style.borderTopRightRadius = 4;
                popup.style.borderBottomLeftRadius = 4;
                popup.style.borderBottomRightRadius = 4;
                popup.style.paddingTop = 2;
                popup.style.paddingBottom = 2;

                // Popup parents to the panel root where the editor font doesn't reliably cascade.
                var fontDef = field.resolvedStyle.unityFontDefinition;
                if (fontDef.fontAsset != null || fontDef.font != null) popup.style.unityFontDefinition = fontDef;
                else if (field.resolvedStyle.unityFont != null) popup.style.unityFont = field.resolvedStyle.unityFont;
                popup.style.fontSize = field.resolvedStyle.fontSize;

                foreach (var name in currentNames)
                {
                    string val = name;
                    var item = new VisualElement();
                    item.style.flexDirection = FlexDirection.Row;
                    item.style.alignItems = Align.Center;
                    item.style.paddingLeft = 8;
                    item.style.paddingRight = 8;
                    item.style.paddingTop = 4;
                    item.style.paddingBottom = 4;

                    var nameLabel = new Label(val);
                    nameLabel.style.color = normalText;
                    item.Add(nameLabel);

                    item.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        if (popup.IndexOf(item) != selectedIndex)
                            item.style.backgroundColor = DesignerPalette.AccentTabBg;
                        // Dwell-to-peek: resolve the index at fire time — the popup may have been rebuilt
                        // by a keystroke between hover and fire.
                        SchedulePreview(() => popup != null ? popup.IndexOf(item) : -1);
                    });
                    item.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        item.style.backgroundColor = popup.IndexOf(item) == selectedIndex
                            ? highlightBg : (StyleColor)StyleKeyword.Null;
                        previewTimer?.Pause(); // an un-fired peek dies with the hover; a shown one persists
                    });
                    item.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        Commit(val);
                        evt.StopPropagation();
                    });
                    popup.Add(item);
                }

                // Leaving the popup entirely retires the peek — it exists for while you're browsing the list.
                popup.RegisterCallback<MouseLeaveEvent>(_ => DismissPreview());

                // Focus that goes field → popup → elsewhere never raises a second FocusOut on the FIELD, so
                // without this the list could be stranded on screen. The caret poll below would also retire
                // it, but only while the field still owns the caret — arm from here too and neither path
                // depends on the other.
                popup.RegisterCallback<FocusOutEvent>(_ => field.schedule.Execute(() =>
                {
                    if (!FocusIsInsideAutocomplete()) Dismiss();
                }).ExecuteLater(BlurDismissDelayMs));

                // Give the caret back after any in-popup drag ends, so ↑/↓/Enter/Escape (all bound to the
                // FIELD) keep working. Rows Commit on pointer DOWN and dismiss, so they never reach this.
                popup.RegisterCallback<PointerUpEvent>(_ =>
                {
                    if (popup == null || IsFieldFocused() || !FocusIsInsideAutocomplete()) return;
                    field.Focus();
                });

                field.panel.visualTree.Add(popup);
                popup.BringToFront();
                selectedIndex = -1;
            }

            field.RegisterValueChangedCallback(evt =>
            {
                if (suppressShow)
                {
                    suppressShow = false;
                    return;
                }
                if (IsFieldFocused()) Show();
            });

            // Caret-only moves (arrows, clicks, deferred SelectRange after the {} button) raise no
            // ChangeEvent — a light poll keeps the popup honest, re-querying ONLY when the token
            // under the caret actually changed.
            field.schedule.Execute(() =>
            {
                // FocusIsInsideAutocomplete, not IsFieldFocused: focus parked inside the popup is the user
                // WORKING the list (a scroll-drag), and retiring it there is the whole scroll-drag defect.
                // The token read below still needs the caret, so it is skipped while focus is away.
                if (!FocusIsInsideAutocomplete())
                {
                    if (popup != null) Dismiss();
                    return;
                }
                if (!IsFieldFocused()) return;
                bool inToken = TryGetTokenQuery(out int braceStart, out string query);
                if (!inToken)
                {
                    if (popup != null) Dismiss();
                }
                // Re-query on a changed token POSITION as well as changed text. Keying on the query alone let
                // the caret hop between two runs spelled the same ("{pl … {pl") without re-Showing, leaving
                // Show()'s captured brace/caret pointing at the FIRST one — so accepting a suggestion rewrote
                // the wrong token. braceStart + query fully determine the capture, so comparing both is exact.
                else if (braceStart != tokenBraceStart ||
                         !string.Equals(query, lastShownQuery, StringComparison.Ordinal))
                {
                    Show();
                }
            }).Every(150);

            // A blur only dismisses if focus STAYS away. The rich-text toolbar's {} button blurs the field,
            // rewrites the text, then re-focuses it and moves the caret on a deferred tick — so an
            // UNCONDITIONAL dismiss here fired 150ms after the click and tore down the list the caret poll
            // had raised a moment earlier. Measured before the guard: list up at 149ms, gone at 150ms, back
            // at 299ms — the "dropdown flashes in and out once or twice before settling" report. The poll
            // above already retires the list on a real blur, using exactly this test.
            field.RegisterCallback<FocusOutEvent>(_ => field.schedule.Execute(() =>
            {
                if (!FocusIsInsideAutocomplete()) Dismiss();
            }).ExecuteLater(BlurDismissDelayMs));
            field.RegisterCallback<DetachFromPanelEvent>(_ => Dismiss());

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (popup == null) return;
                switch (evt.keyCode)
                {
                    case KeyCode.DownArrow:
                        selectedIndex = (selectedIndex + 1) % currentNames.Count;
                        UpdateSelectionVisuals();
                        SchedulePreview(() => selectedIndex); // dwell on an arrow-selected row peeks too
                        evt.StopPropagation();
                        break;
                    case KeyCode.UpArrow:
                        selectedIndex = selectedIndex <= 0 ? currentNames.Count - 1 : selectedIndex - 1;
                        UpdateSelectionVisuals();
                        SchedulePreview(() => selectedIndex);
                        evt.StopPropagation();
                        break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                    case KeyCode.Tab:
                        if (selectedIndex >= 0 && selectedIndex < currentNames.Count)
                        {
                            Commit(currentNames[selectedIndex]);
                            evt.StopPropagation();
                        }
                        else
                        {
                            Dismiss(); // no highlight → let Enter type its newline normally
                        }
                        break;
                    case KeyCode.Escape:
                        Dismiss();
                        evt.StopPropagation();
                        break;
                }
            }, TrickleDown.TrickleDown);
        }
    }
}
