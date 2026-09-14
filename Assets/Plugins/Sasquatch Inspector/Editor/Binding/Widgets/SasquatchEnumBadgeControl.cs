using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The draw + edit surface for the Enum Badge Grid widget — a grid of clickable toggle chips that replaces
    /// Unity's cramped enum dropdown. A <c>[System.Flags]</c> enum becomes a MULTI-select grid (click a chip to
    /// flip its bit, with optional None/All quick chips); a plain enum becomes a SINGLE-select segmented control
    /// (radio behaviour).
    ///
    /// The enum value is NEVER stored on the BindingNode — it lives on the bound <see cref="SerializedProperty"/>,
    /// read/written via <c>enumValueFlag</c> (the underlying numeric mask, matching <c>ConditionEvaluator</c>).
    /// External edits (undo, multi-object, a sibling field) refresh the chips via <c>TrackPropertyValue</c>.
    /// Each badge maps to an enum member's underlying NUMERIC value (never the display index), so sparse and
    /// explicit enums toggle correctly. A badge is simply active when ALL of its bits are set (no partial state)
    /// — clean and predictable. NOTE: a [Flags] enum is only well-behaved with power-of-two values (1, 2, 4, 8…);
    /// sequential values (0,1,2,3,…) overlap bitwise (e.g. 3 == 1|2) and will co-activate, by design of [Flags].
    /// The active-badge tint reuses the node's <see cref="DisplayStyleData.accent"/> (default <c>WidgetPalette.AccentUnity</c>).
    /// </summary>
    internal sealed class SasquatchEnumBadgeControl : VisualElement
    {
        private struct Entry
        {
            public long value;   // the member's underlying numeric value (0 = None; allBits = All/Everything)
            public Button badge;
            public VisualElement pill;  // Text Backdrop only: the label's dark pill; null when the option is off
        }

        private readonly SerializedProperty _prop;
        private readonly BindingNode _node;
        private readonly Type _enumType;
        private readonly bool _multi;
        private readonly bool _editable;
        private readonly Color _accent;

        private readonly List<Entry> _entries = new();
        // Per-value tint/icon overrides, keyed by the member's underlying numeric value (last entry wins on dup).
        private readonly Dictionary<long, EnumBadgeOverride> _overrides = new();
        // Every value the enum DECLARES, and the OR of them — filled by BuildBadges, read by MissingBadgeFor
        // to tell "a member that should have a chip doesn't" from "the field holds an undeclared value".
        private readonly HashSet<long> _declaredValues = new();
        private long _declaredBits;

        // Fixed-column mode packs badges into EXPLICIT rows rather than letting flex-wrap discover them —
        // see the note in BuildBadges. Null / 0 in auto-wrap mode, where the grid IS the row.
        private VisualElement _gridRow;
        private int _gridRowFill;

        // AUTO-WRAP FLAP GUARD. The BuildBadges note explains why a discovered wrap is unstable and packs
        // fixed-column grids into explicit rows to avoid it — but auto-wrap mode still asks flex-wrap, so it
        // kept the defect. On its own that is the stutter that note describes. Inside a DiagonalSplit frame it
        // is a closed LOOP, because a split pane's width is a function of the frame's HEIGHT
        // (tiltSpan = |tan(SplitAngle)|·h/2 in FrameBuilder.LayoutSplit): a badge wraps → the row is 16px
        // taller → the frame is 16px taller → tiltSpan grows → the seam pad widens → the pane is ~1px
        // narrower → the badge un-wraps → repeat, EVERY TICK, forever. Measured on a −26° split at 38 flips
        // per 40 ticks, in the live Inspector and the designer canvas at once.
        //
        // The cure is to wrap against a LATCHED width instead of the live one, asymmetrically: follow a
        // narrowing immediately (so the row can never overflow), but require real growth before widening
        // again. The loop only ever supplies ~1px of growth, so it can never reach the dead zone and the
        // decision holds. Same shape as MinMaxStackHysteresis and StackUnflipHysteresisPx — the RETURN trip
        // is the one that costs extra.
        private float _wrapWidthLatch = float.NaN;
        private const float WrapWidthDeadZone = 6f;

        public SasquatchEnumBadgeControl(SerializedProperty prop, BindingNode node, Type enumType, bool multiSelect, bool editable)
        {
            _prop = prop;
            _node = node;
            _enumType = enumType;
            _multi = multiSelect;
            _editable = editable;
            _accent = node.DisplayStyleData.accent.Resolve(WidgetPalette.AccentUnity);
            if (node.EnumBadgeOverrides != null)
                foreach (var o in node.EnumBadgeOverrides)
                    _overrides[o.value] = o;

            AddToClassList("sasq-enum-badge-grid");
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;
            style.flexGrow = 1;

            BuildBadges();
            Refresh();

            // No-ops in fixed-column mode, which already cannot ask flex-wrap anything.
            RegisterCallback<GeometryChangedEvent>(_ => LatchWrapWidth());

            // External changes (undo, multi-edit, a sibling editing the same field) → re-sync the chip states.
            this.TrackPropertyValueWithRefresh(prop, _ => Refresh());
        }

        // ===========================================================================================
        // Build
        // ===========================================================================================
        private void BuildBadges()
        {
            Clear();
            _entries.Clear();

            // A FIXED COLUMN COUNT IS AUTHORED, SO IT MUST NOT BE DISCOVERED BY FLEX-WRAP.
            //
            // The old fixed-column grid was one wrapping row of cells at `1/cols` each — a total of exactly
            // 100% with ZERO slack, so the LAST badge sat permanently one rounding error away from a second
            // line. Measured live on a 5-member enum: sweeping the inspector's width across 250–700px flipped
            // the last chip on and off across 60 of 362 layout frames, in a scattered non-monotonic pattern
            // (wrapped at 290 and 303 and 315, not wrapped between). Dragging the inspector edge therefore
            // read as the row physically stuttering — the chip jumping down and snapping back several times
            // per second — which is what a user reports, not "the grid wraps at width X".
            //
            // A width THRESHOLD would be a legitimate wrap. This was not one: the same width could produce
            // either answer depending on which intermediate pass the width arrived through, so no width made
            // it stable. Packing `cols` badges into explicit row containers removes the question entirely —
            // the row count is arithmetic on the badge count, and flex-wrap is never consulted.
            bool fixedCols = _node.EnumBadgeColumns > 0;
            _gridRow = null;
            _gridRowFill = 0;
            // A rebuild changes the badge set, so the old latch describes a layout that no longer exists.
            _wrapWidthLatch = float.NaN;
            style.maxWidth = StyleKeyword.Null;
            style.flexDirection = fixedCols ? FlexDirection.Column : FlexDirection.Row;
            style.flexWrap = fixedCols ? Wrap.NoWrap : Wrap.Wrap;
            // Column direction makes alignItems the HORIZONTAL axis: Center would collapse every row to its
            // content width and un-do the 1/cols basis the cells carry.
            style.alignItems = fixedCols ? Align.Stretch : Align.Center;

            Array values = Enum.GetValues(_enumType);
            string[] rawNames = Enum.GetNames(_enumType);

            // Display-name map (raw member name → nicified label, honoring [InspectorName]) from the property.
            var display = new Dictionary<string, string>();
            string[] propNames = _prop.enumNames;
            string[] propDisplay = _prop.enumDisplayNames;
            for (int i = 0; i < propNames.Length && i < propDisplay.Length; i++)
                display[propNames[i]] = propDisplay[i];

            // allBits = OR of every positive member (compound members add no new bits). Track whether a 0-value
            // member is declared, so we don't ALSO add a synthetic None (which would hide the real member).
            long allBits = 0;
            bool hasZeroMember = false;
            _declaredValues.Clear();
            _declaredBits = 0;
            for (int i = 0; i < values.Length; i++)
            {
                long v = ToLong(values.GetValue(i));
                if (v > 0) allBits |= v;
                if (v == 0) hasZeroMember = true;
                _declaredValues.Add(v);
                _declaredBits |= v;
            }

            long curMask = CurrentMask();
            // Quick chips are a [Flags]-only convenience. None is suppressed when the enum already declares a
            // 0-value member (that member IS "none"); All only when there are at least two distinct bits.
            bool showNone = _multi && _node.EnumBadgeShowAllNone && !hasZeroMember;
            bool showAll = _multi && _node.EnumBadgeShowAllNone && CountBits(allBits) >= 2;

            var seen = new HashSet<long>();
            if (showAll) seen.Add(allBits); // a declared member equal to allBits is shown as the synthetic "All"

            if (showNone) AddBadge("None", 0, special: true);

            for (int i = 0; i < values.Length; i++)
            {
                long v = ToLong(values.GetValue(i));
                if (!seen.Add(v)) continue; // skip allBits (shown as All) + any exact-duplicate aliases

                string raw = rawNames[i];
                var fi = _enumType.GetField(raw);
                bool obsolete = fi != null && Attribute.IsDefined(fi, typeof(ObsoleteAttribute));
                bool currentlySet = _multi
                    ? (v == 0 ? curMask == 0 : (curMask & v) == v)
                    : curMask == v;
                // Hide [Obsolete] members unless explicitly requested or the value is currently set (so an
                // existing selection always stays visible / clearable).
                if (obsolete && !_node.EnumBadgeIncludeObsolete && !currentlySet) continue;

                string label = display.TryGetValue(raw, out var d) && !string.IsNullOrEmpty(d)
                    ? d
                    : ObjectNames.NicifyVariableName(raw);
                AddBadge(label, v, special: false);
            }

            // Synthetic All chip last.
            if (showAll) AddBadge("All", allBits, special: true);
        }

        private void AddBadge(string text, long value, bool special)
        {
            var badge = new Button { text = text };
            badge.AddToClassList("sasq-enum-badge");
            if (special) badge.AddToClassList("sasq-enum-badge--special");

            // An inner icon+label row exists when a per-value icon is set, OR when the Text Backdrop needs
            // content to sit on its pill - "badge.text" is a string on the Button, there is nothing to parent a
            // pill to. A badge with neither keeps Unity's own button text rendering, exactly as before. (Twin of
            // LayoutTreeBuilder.BuildButtonElement, which forces the same row for the same reason.) The label is
            // a plain child either way, so the button's inherited text color still styles it - which is how the
            // active / mixed / contrast ink below keeps working with no second assignment here.
            EnumBadgeOverride ov = default;
            bool hasIcon = !special && _overrides.TryGetValue(value, out ov) && !string.IsNullOrEmpty(ov.iconId);
            VisualElement pill = null;
            // Set when the label moves off `badge.text` and onto an inner Label — the fit below has to write
            // to whichever of the two actually carries the words.
            Label innerLabel = null;
            float iconReserve = 0f;
            if (hasIcon || _node.EnumBadgeTextBackdrop)
            {
                badge.text = "";
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("sasq-enum-badge__row");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.Center;

                // Text Backdrop: the row stays the full-width centring host and a hugging pill rides inside it,
                // holding the icon AND the label. Built in BOTH states so the chip's width never depends on
                // whether it is lit; Refresh() only paints it.
                var content = row;
                if (_node.EnumBadgeTextBackdrop)
                {
                    pill = new VisualElement { pickingMode = PickingMode.Ignore };
                    pill.AddToClassList("sasq-enum-badge__backdrop");
                    row.Add(pill);
                    content = pill;
                }

                if (hasIcon)
                {
                    float iconSize = Mathf.Clamp(_node.EnumBadgeHeight - 8f, 10f, 16f);
                    var icon = SasquatchIcons.Create(ov.iconId, iconSize);
                    icon.pickingMode = PickingMode.Ignore;
                    icon.style.marginRight = 4;
                    icon.style.flexShrink = 0f;   // a glyph cannot shrink; the LABEL is what gives way
                    content.Add(icon);
                    iconReserve = iconSize + 4f;
                }
                // The clip trio the chip itself carries in USS has to ride the inner Label as well: once an
                // icon or a backdrop pill exists the words live HERE, `badge.text` is empty, and the button's
                // own `text-overflow` has nothing left to truncate. Twin of the same three lines in
                // ExportedWidgetKit's EnumBadges section, which has always had them.
                innerLabel = new Label(text) { pickingMode = PickingMode.Ignore };
                innerLabel.style.whiteSpace = WhiteSpace.NoWrap;
                innerLabel.style.overflow = Overflow.Hidden;
                innerLabel.style.textOverflow = TextOverflow.Ellipsis;
                innerLabel.style.flexShrink = 1f;
                innerLabel.style.minWidth = 0f;
                content.Add(innerLabel);
                badge.Add(row);
            }

            float h = _node.EnumBadgeHeight;
            badge.style.height = h;
            badge.style.minHeight = h;

            int cols = _node.EnumBadgeColumns;
            float gap = _node.EnumBadgeSpacing; // 0 = stylesheet default; >0 = explicit gap (half on each side)
            if (cols > 0)
            {
                // Fixed grid: a CELL wrapper holds exactly 1/cols of the row (flexBasis alone can't fight the
                // badge margins under flex-wrap — wrapping is decided from flexBasis+margin BEFORE shrink, so
                // margins push the count one short). The badge fills the cell; the gap is the cell's padding.
                var cell = new VisualElement();
                cell.AddToClassList("sasq-enum-badge-cell");
                cell.style.flexBasis = Length.Percent(100f / cols);
                cell.style.flexGrow = 0;
                // Shrinkable, unlike the old wrapping grid. Inside a NoWrap row an over-long line has to go
                // somewhere, and a hair of shrink is invisible where an overflow is not.
                cell.style.flexShrink = 1;

                badge.style.flexGrow = 1;
                badge.style.marginLeft = 0;
                badge.style.marginRight = 0;
                badge.style.marginTop = 0;
                badge.style.marginBottom = 0;

                if (gap > 0f)
                {
                    cell.style.paddingLeft = gap * 0.5f; cell.style.paddingRight = gap * 0.5f;
                    cell.style.paddingTop = gap * 0.5f; cell.style.paddingBottom = gap * 0.5f;
                }

                cell.Add(badge);

                // Open a new row every `cols` badges. The grid is a COLUMN in this mode, so each row lands on
                // its own line by construction — no wrap decision, no rounding cliff.
                if (_gridRow == null || _gridRowFill >= cols)
                {
                    _gridRow = new VisualElement();
                    _gridRow.AddToClassList("sasq-enum-badge-row");
                    _gridRow.style.flexDirection = FlexDirection.Row;
                    _gridRow.style.flexWrap = Wrap.NoWrap;
                    Add(_gridRow);
                    _gridRowFill = 0;
                }
                _gridRow.Add(cell);
                _gridRowFill++;
            }
            else
            {
                // Auto-wrap: natural-width badges with USS margins for the gap. An explicit width applies ONLY
                // here — with a fixed column count the cell above already dictates the badge width, and the
                // column count wins (a width would either overflow the row or leave a ragged gap).
                float w = _node.EnumBadgeWidth;
                if (w > 0f)
                {
                    badge.style.width = w;
                    badge.style.minWidth = w;
                    badge.style.flexShrink = 0f;
                }
                if (gap > 0f)
                {
                    badge.style.marginLeft = gap * 0.5f; badge.style.marginRight = gap * 0.5f;
                    badge.style.marginTop = gap * 0.5f; badge.style.marginBottom = gap * 0.5f;
                }
                Add(badge);
            }

            // A SQUEEZED CHIP MUST STILL SHOW A LETTER. Wired ONLY where the chip's width is dictated from
            // outside it — a fixed-column cell, or an explicit per-badge width. An auto-sized chip hugs its
            // own label, so there is nothing to squeeze, and re-deriving its padding from a width that its
            // padding produces would be a loop.
            if (cols > 0 || _node.EnumBadgeWidth > 0f)
                KeepOneGlyphVisible(badge, pill, innerLabel ?? (TextElement)badge, text, iconReserve);

            if (_editable)
            {
                long v = value;
                badge.clicked += () =>
                {
                    if (EditorSpringFX.MotionEnabled) EditorSpringFX.PressBounce(badge);
                    OnBadgeClicked(v);
                };
            }

            _entries.Add(new Entry { value = value, badge = badge, pill = pill });
        }

        // Authored horizontal padding, mirroring `.sasq-enum-badge` and `.sasq-enum-badge__backdrop` in
        // SasquatchWidgets.uss. Held as CONSTANTS rather than read back from resolvedStyle on purpose: the
        // fit below writes those very properties inline, so reading them would compound its own last answer
        // and ratchet every chip's padding to the floor over a few layout passes.
        private const float BadgePadX = 8f;
        private const float BackdropPadX = 6f;
        private const float MinBadgePadX = 1f;
        private const string EllipsisGlyph = "…";
        // UI Toolkit lays out on WHOLE pixels, so a padding of 3.4 resolves to a content box one pixel
        // narrower than the arithmetic promised — and one pixel is the whole margin here, since the fit
        // deliberately hands back every pixel it does not need. Measured: without this the four reported
        // chips each landed EXACTLY 1px under their own requirement and still drew the bare ellipsis. So
        // the budget buys a spare pixel and the per-side pad is FLOORED, never rounded.
        private const float BadgeFitCushion = 1f;

        /// <summary>
        /// Keeps at least ONE LETTER visible in a chip too narrow for its whole label.
        /// <para>
        /// `text-overflow: ellipsis` is the right answer everywhere else, but it degrades badly at the last
        /// step: once the content box is under the width of "letter + ellipsis" Unity renders the ELLIPSIS
        /// ALONE — a chip reading "…", which says strictly less than the single letter it had room for.
        /// MEASURED at the reported setting (4 columns, 4px spacing, a 62%-weight column): a 32px cell gives
        /// the chip a 10px content box, and "…" is exactly 10px at font 11 while "M…" is 19px.
        /// </para>
        /// <para>
        /// So the chip gives up its BREATHING ROOM before it gives up the word. The 8px side padding (and
        /// the backdrop pill's 6px) is clawed back proportionally, down to a 1px floor, and only as far as
        /// the fit actually demands — a chip with room to spare keeps every authored pixel. When even the
        /// floor will not seat "letter + ellipsis", the ellipsis itself is dropped and the bare first glyph
        /// is drawn: one letter beats three dots.
        /// </para>
        /// <para>
        /// There is no feedback loop here because the caller only wires it where the chip's width comes from
        /// OUTSIDE — padding writes then change the content box, never <c>layout.width</c>, so the geometry
        /// event that drives this cannot re-fire itself.
        /// </para>
        /// </summary>
        private static void KeepOneGlyphVisible(Button badge, VisualElement pill, TextElement textHost,
                                                string fullText, float reservedPx)
        {
            if (badge == null || textHost == null || string.IsNullOrEmpty(fullText)) return;

            string oneGlyph = FirstGlyph(fullText);
            bool showingBareGlyph = false;
            float appliedPad = float.NaN;
            float needOneGlyph = float.NaN;   // width of "<glyph>…" in this chip's own font
            float needFull = float.NaN;

            badge.RegisterCallback<GeometryChangedEvent>(_ => Fit());

            void Fit()
            {
                float w = badge.layout.width;
                if (float.IsNaN(w) || w <= 0f) return;
                // MeasureTextSize on a detached element returns NaN AND, on 6.6, storms the console with DPI
                // warnings — see measuretextsize-off-panel-is-the-66-dpi-warning-storm.
                if (badge.panel == null) return;

                if (float.IsNaN(needOneGlyph))
                {
                    float one = MeasureWidth(textHost, oneGlyph + EllipsisGlyph);
                    float all = MeasureWidth(textHost, fullText);
                    if (float.IsNaN(one) || float.IsNaN(all)) return;   // font not resolved yet; a later pass carries it
                    needOneGlyph = one;
                    needFull = all;
                }

                float chrome = badge.resolvedStyle.borderLeftWidth + badge.resolvedStyle.borderRightWidth + reservedPx;
                float authoredPad = BadgePadX * 2f;
                float minPad = MinBadgePadX * 2f;
                if (pill != null)
                {
                    chrome += pill.resolvedStyle.borderLeftWidth + pill.resolvedStyle.borderRightWidth;
                    authoredPad += BackdropPadX * 2f;
                    minPad += MinBadgePadX * 2f;
                }

                float room = w - chrome;   // what is left once every horizontal pad is clawed back

                float pad, pillPad;
                bool bare;
                if (room - authoredPad >= needFull - 0.01f)
                {
                    // The whole label fits with room to spare: nothing to trade, nothing to truncate.
                    pad = BadgePadX; pillPad = BackdropPadX; bare = false;
                }
                else
                {
                    float budget = Mathf.Clamp(room - needOneGlyph - BadgeFitCushion, minPad, authoredPad);
                    float t = authoredPad > minPad ? (budget - minPad) / (authoredPad - minPad) : 1f;
                    pad = Mathf.Max(MinBadgePadX, Mathf.Floor(Mathf.Lerp(MinBadgePadX, BadgePadX, t)));
                    pillPad = Mathf.Max(MinBadgePadX, Mathf.Floor(Mathf.Lerp(MinBadgePadX, BackdropPadX, t)));
                    bare = room - minPad - BadgeFitCushion < needOneGlyph - 0.01f;
                }

                if (float.IsNaN(appliedPad) || Mathf.Abs(pad - appliedPad) > 0.01f)
                {
                    appliedPad = pad;
                    badge.style.paddingLeft = pad;
                    badge.style.paddingRight = pad;
                    if (pill != null)
                    {
                        pill.style.paddingLeft = pillPad;
                        pill.style.paddingRight = pillPad;
                    }
                }

                if (bare != showingBareGlyph)
                {
                    showingBareGlyph = bare;
                    textHost.text = bare ? oneGlyph : fullText;
                    // Unity must not re-ellipsize the single glyph we just chose — that is the very outcome
                    // this method exists to prevent.
                    textHost.style.textOverflow = bare ? TextOverflow.Clip : TextOverflow.Ellipsis;
                }
            }
        }

        /// <summary>The first PRINTED character — a surrogate pair is one glyph, and half of one is a box.</summary>
        private static string FirstGlyph(string s)
            => s.Length >= 2 && char.IsHighSurrogate(s[0]) && char.IsLowSurrogate(s[1])
                ? s.Substring(0, 2)
                : s.Substring(0, 1);

        private static float MeasureWidth(TextElement e, string text)
            => e.MeasureTextSize(text, 0f, VisualElement.MeasureMode.Undefined,
                                       0f, VisualElement.MeasureMode.Undefined).x;

        /// <summary>
        /// Pins the width the auto-wrap grid decides its line breaks against — see the WrapWidthDeadZone note.
        /// Narrowing follows immediately so a row can never overflow; widening must clear the dead zone, which
        /// is what starves the split-frame feedback loop of the ~1px it needs to un-wrap a badge.
        /// </summary>
        private void LatchWrapWidth()
        {
            if (_node.EnumBadgeColumns > 0) return;   // fixed grid: rows are arithmetic, nothing to latch
            var host = hierarchy.parent;
            if (host == null) return;
            float avail = host.contentRect.width;
            if (float.IsNaN(avail) || avail <= 0f) return;   // pre-layout; a later pass carries a real width
            if (!float.IsNaN(_wrapWidthLatch)
                && avail >= _wrapWidthLatch
                && avail <= _wrapWidthLatch + WrapWidthDeadZone) return;
            _wrapWidthLatch = avail;
            style.maxWidth = avail;
        }

        // ===========================================================================================
        // Interaction
        // ===========================================================================================
        private void OnBadgeClicked(long value)
        {
            long cur = CurrentMask();
            long next;
            if (!_multi)
            {
                next = value; // radio: select exactly this value
            }
            else if (value == 0)
            {
                next = 0; // a 0-value member / None clears the mask
            }
            else
            {
                bool on = (cur & value) == value; // all of this (possibly compound) value's bits are set
                next = on ? (cur & ~value) : (cur | value);
            }

            try
            {
                _prop.enumValueFlag = unchecked((int)next);
                _prop.serializedObject.ApplyModifiedProperties();
            }
            catch { /* SerializedObject disposed mid-teardown */ }

            Refresh();
        }

        // ===========================================================================================
        // Refresh (active / mixed states — no partial half-state)
        // ===========================================================================================
        private void Refresh()
        {
            bool mixed;
            try { mixed = _prop.hasMultipleDifferentValues; }
            catch { return; }
            long mask = CurrentMask();

            // The badge SET is value-dependent in exactly one place: an [Obsolete] member gets a chip only
            // while it is currently set (see BuildBadges). That decision was made once, in the constructor —
            // so a value that arrives AFTERWARDS (an undo, a sibling editing the same field, a re-bind onto
            // a different object) has no chip at all. A plain enum then renders every chip inactive with no
            // hint of what's selected, and a [Flags] enum gets an invisible sticky bit the user can't clear.
            // Re-derive the grid when that happens; cheap, and a strict no-op the rest of the time.
            if (!mixed && MissingBadgeFor(mask))
            {
                BuildBadges();
                mask = CurrentMask();
            }

            foreach (var e in _entries)
            {
                var b = e.badge;
                b.RemoveFromClassList("sasq-enum-badge--active");
                b.RemoveFromClassList("sasq-enum-badge--mixed");
                e.pill?.RemoveFromClassList("sasq-enum-badge__backdrop--lit");
                ClearBadgeColors(b);

                if (mixed)
                {
                    b.AddToClassList("sasq-enum-badge--mixed");
                    continue;
                }

                bool active;
                if (!_multi)
                    active = mask == e.value;
                else if (e.value == 0)
                    active = mask == 0;
                else
                    active = (mask & e.value) == e.value;

                if (active)
                {
                    b.AddToClassList("sasq-enum-badge--active");
                    // A per-value override tints THIS badge; otherwise the shared accent.
                    Color tint = _overrides.TryGetValue(e.value, out var ov) && ov.color.isOverridden
                        ? ov.color.value
                        : _accent;
                    b.style.backgroundColor = tint;
                    // Border a shade DARKER than the fill so the USS `--active` 3px bottom border reads as a
                    // visible "selected" underline instead of vanishing into the same-color fill.
                    SetBadgeBorder(b, Color.Lerp(tint, Color.black, 0.3f));
                    if (e.pill != null)
                    {
                        // TEXT BACKDROP: the label is not sitting on the tint any more, it is sitting on the
                        // pill - which composites to near-black over every fill in either skin (the brightest
                        // possible case, a pure white accent, still measures 5.6:1 against white ink). So the
                        // ink is PINNED light here rather than derived, exactly as the styled Button's
                        // ResolveButtonInkColor does for its own pill. Deriving it against the fill instead
                        // would put near-black text on the dark pill for every light accent - the very bug the
                        // contrast derivation exists to prevent, reintroduced one layer up.
                        e.pill.AddToClassList("sasq-enum-badge__backdrop--lit");
                        b.style.color = Color.white;
                    }
                    else
                    {
                        // Readable label over ANY tint, the same derivation the progress bar's readout uses.
                        // The USS `--active` rule hard-codes white, which is right for the house blue and every
                        // dark accent but disappears on a light one — a white badge measured 1.00:1 and a yellow
                        // 1.22:1, i.e. no text at all. Inline beats USS in every state (including :hover, whose
                        // own `color` would otherwise re-whiten the label on mouse-over), so this holds.
                        b.style.color = BindingFactory.ContrastingReadoutColor(EffectiveBadgeFill(tint));
                    }
                }
            }
        }

        // ===========================================================================================
        // Helpers
        // ===========================================================================================
        private long CurrentMask()
        {
            try { return _prop.enumValueFlag; }
            catch { return 0; }
        }

        /// <summary>
        /// True when a DECLARED member that <paramref name="mask"/> currently selects has no badge in the
        /// grid — the tell that the build-time [Obsolete] filter is now out of date. Single-select asks
        /// "is the exact value represented"; [Flags] asks whether every set bit is covered by some chip.
        /// Restricted to declared members on purpose: an out-of-range value stored in the field can never
        /// gain a chip, so treating it as missing would rebuild the grid on every single refresh.
        /// </summary>
        private bool MissingBadgeFor(long mask)
        {
            if (_entries.Count == 0) return false;

            if (!_multi)
            {
                if (!_declaredValues.Contains(mask)) return false; // nothing declared to show
                foreach (var e in _entries)
                    if (e.value == mask) return false;
                return true;
            }

            long covered = 0;
            foreach (var e in _entries) covered |= e.value;
            return (mask & _declaredBits & ~covered) != 0;
        }

        private static void ClearBadgeColors(VisualElement b)
        {
            b.style.backgroundColor = StyleKeyword.Null;
            b.style.borderTopColor = StyleKeyword.Null;
            b.style.borderBottomColor = StyleKeyword.Null;
            b.style.borderLeftColor = StyleKeyword.Null;
            b.style.borderRightColor = StyleKeyword.Null;
            // The contrast ink too — an idle or mixed badge must fall back to its USS color, and a stale
            // near-black left over from a light active tint would be invisible on the dark idle chip.
            b.style.color = StyleKeyword.Null;
        }

        /// <summary>
        /// The color a badge's label actually sits on. A translucent tint reads as itself composited over
        /// the idle chip fill, so scoring the raw color would call a 20%-alpha white "white" and pick dark
        /// ink for what is really a dark badge. Twin of the progress bar's fill-over-track blend.
        /// </summary>
        private static Color EffectiveBadgeFill(Color tint)
            => tint.a >= 0.999f ? tint : Color.Lerp(WidgetPalette.ControlBg, tint, tint.a);

        private static void SetBadgeBorder(VisualElement b, Color c)
        {
            b.style.borderTopColor = c;
            b.style.borderBottomColor = c;
            b.style.borderLeftColor = c;
            b.style.borderRightColor = c;
        }

        private static long ToLong(object enumBoxed)
        {
            try { return Convert.ToInt64(enumBoxed); }
            catch
            {
                // ulong-backed enums with the high bit set overflow Int64 — reinterpret the bits instead.
                try { return unchecked((long)Convert.ToUInt64(enumBoxed)); }
                catch { return 0; }
            }
        }

        private static int CountBits(long v)
        {
            int c = 0;
            ulong u = unchecked((ulong)v);
            while (u != 0) { c++; u &= u - 1; }
            return c;
        }
    }
}
