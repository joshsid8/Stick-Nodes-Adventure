using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        /// <summary>Marks the row the Min/Max slider folds its value read-outs into when the horizontal row
        /// cannot fit. Tests and restyle sweeps look for it; nothing else creates it.</summary>
        internal const string MinMaxStackedRowClass = "sasq-minmax-stacked";

        /// <summary>Narrowest track worth drawing. Below this the slider is decoration rather than a control,
        /// so the row is over budget and the values fold onto their own line instead. Calibrated against the
        /// measured no-affix row, which fit comfortably with 33px of track.</summary>
        private const float MinMaxTrackFloor = 36f;

        /// <summary>Dead band on the way back to a single row, so dragging the inspector across the threshold
        /// cannot flap the layout back and forth on consecutive geometry passes.</summary>
        private const float MinMaxStackHysteresis = 8f;

        private static VisualElement CreateMinMaxSlider(BindingNode node, SerializedProperty prop, string resolvedLabel, bool isTableCell = false)
        {
            if (prop.propertyType == SerializedPropertyType.Vector2 || prop.propertyType == SerializedPropertyType.Vector2Int)
            {
                var slider = new MinMaxSlider();
                slider.label = resolvedLabel;
                ResolveLimits(node, prop, out float min, out float max, 0f, 100f);
                // The limits must CONTAIN the stored range, or displaying it rewrites it: a MinMaxSlider clamps
                // like any slider, and Unity's binding announces the clamped value on attach and writes it back.
                // See WidenToContainStoredValue.
                if (!prop.hasMultipleDifferentValues)
                {
                    Vector2 stored = prop.propertyType == SerializedPropertyType.Vector2Int
                        ? new Vector2(prop.vector2IntValue.x, prop.vector2IntValue.y)
                        : prop.vector2Value;
                    if (!float.IsNaN(stored.x) && !float.IsInfinity(stored.x)) min = Mathf.Min(min, stored.x);
                    if (!float.IsNaN(stored.y) && !float.IsInfinity(stored.y)) max = Mathf.Max(max, stored.y);
                }
                slider.lowLimit = min;
                slider.highLimit = max;

                if (prop.propertyType == SerializedPropertyType.Vector2Int)
                {
                    // MinMaxSlider's data binding only understands Vector2 — binding a Vector2Int logs
                    // "Field type ... is not compatible" and never syncs the thumbs. Drive that case by
                    // hand: seed + track from the property, round writes back through vector2IntValue.
                    slider.SetValueWithoutNotify(prop.vector2IntValue);
                    slider.RegisterValueChangedCallback(evt =>
                    {
                        var iv = new Vector2Int(Mathf.RoundToInt(evt.newValue.x), Mathf.RoundToInt(evt.newValue.y));
                        if (prop.vector2IntValue == iv) return;
                        prop.vector2IntValue = iv;
                        prop.serializedObject.ApplyModifiedProperties();
                    });
                    slider.TrackPropertyValueWithRefresh(prop, p => slider.SetValueWithoutNotify(p.vector2IntValue));
                }
                else
                {
                    // Seed BEFORE the bind: a MinMaxSlider clamps into lowLimit..highLimit exactly like a plain
                    // Slider, so the reverse order lets the bind's own clamp reach the property. See SeedThenBind.
                    SeedThenBind(slider, prop);
                }

                if (slider.labelElement != null)
                {
                    slider.labelElement.style.display = DisplayStyle.Flex;
                    slider.labelElement.style.color = WidgetPalette.LabelPrimary;
                }

                if (node.DisplayStyleData.accent.isOverridden)
                {
                    var minDragger = slider.Q<VisualElement>(className: "unity-min-max-slider__min-thumb");
                    var maxDragger = slider.Q<VisualElement>(className: "unity-min-max-slider__max-thumb");
                    if (minDragger != null)
                    {
                        minDragger.style.backgroundColor = node.DisplayStyleData.accent.value;
                    }
                    if (maxDragger != null)
                    {
                        maxDragger.style.backgroundColor = node.DisplayStyleData.accent.value;
                    }
                }

                if (node.DisplayStyleData.cornerRadius.isOverridden)
                {
                    var tracker = slider.Q<VisualElement>(className: "unity-min-max-slider__tracker");
                    if (tracker != null)
                    {
                        tracker.style.borderTopLeftRadius = node.DisplayStyleData.cornerRadius.value;
                        tracker.style.borderTopRightRadius = node.DisplayStyleData.cornerRadius.value;
                        tracker.style.borderBottomLeftRadius = node.DisplayStyleData.cornerRadius.value;
                        tracker.style.borderBottomRightRadius = node.DisplayStyleData.cornerRadius.value;
                    }
                }

                ApplySliderLook(slider, node);

                if (!node.DisplayStyleData.minMaxHideValues)
                {
                    var ws = node.DisplayStyleData;
                    string rawFmt = ws.minMaxLabelFormat;
                    string fmt = string.IsNullOrEmpty(rawFmt) ? "0.00" : rawFmt.Trim('"', '\'');

                    string FormatValue(float val)
                    {
                        try
                        {
                            return val.ToString(fmt);
                        }
                        catch
                        {
                            return val.ToString("0.00");
                        }
                    }

                    VisualElement minElement;
                    VisualElement maxElement;

                    if (!ws.minMaxReadOnlyValues)
                    {
                        var minField = new FloatField();
                        minField.style.width = 64;
                        minField.formatString = fmt;
                        var maxField = new FloatField();
                        maxField.style.width = 64;
                        maxField.formatString = fmt;

                        minField.RegisterValueChangedCallback(evt =>
                        {
                            var v = slider.value;
                            v.x = Mathf.Clamp(Mathf.Min(evt.newValue, v.y), slider.lowLimit, slider.highLimit);
                            slider.value = v;
                        });
                        maxField.RegisterValueChangedCallback(evt =>
                        {
                            var v = slider.value;
                            v.y = Mathf.Clamp(Mathf.Max(evt.newValue, v.x), slider.lowLimit, slider.highLimit);
                            slider.value = v;
                        });
                        slider.RegisterValueChangedCallback(evt =>
                        {
                            minField.SetValueWithoutNotify(evt.newValue.x);
                            maxField.SetValueWithoutNotify(evt.newValue.y);
                        });

                        minElement = minField;
                        maxElement = maxField;
                    }
                    else
                    {
                        var minLabel = new Label();
                        var maxLabel = new Label();

                        slider.RegisterValueChangedCallback(evt =>
                        {
                            minLabel.text = FormatValue(evt.newValue.x);
                            maxLabel.text = FormatValue(evt.newValue.y);
                        });

                        minElement = minLabel;
                        maxElement = maxLabel;
                    }

                    minElement.AddToClassList("sq-minmax-label");
                    maxElement.AddToClassList("sq-minmax-label");

                    // KILL UNITY'S INSPECTOR-FIELD RIGHT BLEED ON THE READ-OUTS.
                    //
                    // A field built inside an InspectorElement is tagged `unity-base-field__inspector-field`,
                    // whose USS carries `margin-right: -2`. On a FULL-WIDTH inspector row that is correct and
                    // deliberate: the row's input reaches the true right edge of the inspector. These read-outs
                    // are NOT rows — they are hand-placed children of the slider's own row — so the -2 makes the
                    // max box end 2px PAST the row's content edge, and the clip there scissors its two right
                    // corners flat. Measured live 2026-08-29: box 461..511 inside a MinMaxSlider ending at 509,
                    // which is a value box rounded on the left and sheared vertically on the right. Flex reports
                    // the line as fitting exactly (the negative margin is what pays for the overhang), so no
                    // width, no fold and no overflow sweep can see it — the ONLY tell is the box's own
                    // worldBound.xMax against its parent's — the same probe that catches the affix flavour of
                    // this shear (FieldAffixes.MountOnReadout's reservation landing on the wrong element).
                    //
                    // Applied to the ELEMENT rather than the mount so it holds in every arrangement: inline,
                    // Above/Below, auto-stacked, and inside an affix pair row (where the -2 would instead slide
                    // the suffix 2px back over the digits). The 6px gutters below live on the MOUNT and are
                    // unaffected. Only marginRight is cleared: marginLeft is Unity's standard 3px input inset,
                    // which is what makes the min box sit where every other field's input sits.
                    minElement.style.marginRight = 0;
                    maxElement.style.marginRight = 0;

                    // Reads the property directly, so the read-outs also show the mixed-value state.
                    void UpdateLabels()
                    {
                        bool mixed = MixedValueUI.IsMixed(prop);
                        Vector2 val = Vector2.zero;
                        if (prop.propertyType == SerializedPropertyType.Vector2)
                        {
                            val = prop.vector2Value;
                        }
                        else if (prop.propertyType == SerializedPropertyType.Vector2Int)
                        {
                            val = prop.vector2IntValue;
                        }

                        if (!ws.minMaxReadOnlyValues)
                        {
                            var minF = (FloatField)minElement;
                            var maxF = (FloatField)maxElement;
                            minF.showMixedValue = mixed; // native em-dash on multi-object mixed
                            maxF.showMixedValue = mixed;
                            if (minF.focusController?.focusedElement != minF)
                            {
                                minF.SetValueWithoutNotify(val.x);
                            }
                            if (maxF.focusController?.focusedElement != maxF)
                            {
                                maxF.SetValueWithoutNotify(val.y);
                            }
                        }
                        else
                        {
                            MixedValueUI.SetText((Label)minElement, mixed, FormatValue(val.x));
                            MixedValueUI.SetText((Label)maxElement, mixed, FormatValue(val.y));
                        }
                    }

                    UpdateLabels();
                    slider.TrackPropertyValueWithRefresh(prop, _ => UpdateLabels());

                    // Each value element gets its affixes in a mini pair row so they hug their number; with
                    // no affixes the mount IS the element (layout unchanged). Built per call — a VisualElement
                    // has ONE parent, so min and max cannot share one instance.
                    //
                    // The row is built for an INSIDE affix too, even though that one is overlaid on the
                    // read-out's own box and places nothing here. Two reasons, both load-bearing:
                    //  * This widget PINS its value box from the LIMITS, so the track's ends do not slide as
                    //    the digits change. A unit too long to fit inside has to demote, and demoting into a
                    //    box that may not widen paints past the field instead of giving way. This row is where
                    //    it lands (FieldAffixes.PairRowClass).
                    //  * ReflowValues folds on minMount.layout.width + maxMount.layout.width. A demoted affix
                    //    only counts towards that if the MOUNT is the row that contains it, not the bare box —
                    //    otherwise the fold under-reports by the whole overflow and never engages.
                    // A read-only read-out is a plain Label with no box, and degrades to an outside affix on
                    // its own.
                    VisualElement WithAffixes(VisualElement el)
                    {
                        var pre = FieldAffixes.PrefixFor(node, isTableCell);
                        var suf = FieldAffixes.SuffixFor(node, isTableCell);
                        if (pre.IsEmpty && suf.IsEmpty) return el;

                        var pair = FieldAffixes.NewPairRow();
                        pair.Add(el);
                        var pfx = FieldAffixes.MountOnReadout(el, pre, true, pair);
                        var sfx = FieldAffixes.MountOnReadout(el, suf, false, pair);
                        if (pfx != null) pair.Insert(0, pfx);
                        if (sfx != null) pair.Add(sfx);
                        return pair;
                    }
                    var minMount = WithAffixes(minElement);
                    var maxMount = WithAffixes(maxElement);

                    bool vertical = ws.sliderFieldPlacement == SliderFieldPlacement.Above
                                 || ws.sliderFieldPlacement == SliderFieldPlacement.Below;

                    if (vertical)
                    {
                        var wrapper = new VisualElement();
                        wrapper.style.flexDirection = FlexDirection.Column;

                        var labelRow = new VisualElement();
                        labelRow.style.flexDirection = FlexDirection.Row;
                        labelRow.style.justifyContent = Justify.SpaceBetween;
                        if (ws.sliderFieldPlacement == SliderFieldPlacement.Below)
                        {
                            labelRow.style.marginTop = 2;
                            wrapper.Add(slider);
                            wrapper.Add(labelRow);
                        }
                        else
                        {
                            labelRow.style.marginBottom = 2;
                            wrapper.Add(labelRow);
                            wrapper.Add(slider);
                        }
                        labelRow.Add(minMount);
                        labelRow.Add(maxMount);

                        void StyleLabels()
                        {
                            var ts = node.MinMaxLabelTextStyle;
                            labelRow.Query<TextElement>().ForEach(te =>
                            {
                                if (te.ClassListContains(UnitSuffixClass)) return; // suffix stays dim
                                if (ts.fontStyle.isOverridden) te.style.unityFontStyleAndWeight = ts.fontStyle.value;

                                // Match a normal field's size/color — same as the same-row placement: the standard
                                // field styling unless the user sets an explicit text-style override.
                                if (TextStyleApplier.IsUsableFontSize(ts.fontSize)) te.style.fontSize = ts.fontSize.value;
                                if (ts.color.isOverridden) te.style.color = ts.color.value;

                                // Keep the digits vertically centered in their box.
                                te.style.unityTextAlign = ts.align.isOverridden ? ts.align.value : TextAnchor.MiddleLeft;

                                if (ts.font != null) te.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ts.font));
                            });
                        }
                        StyleLabels();
                        labelRow.RegisterCallback<GeometryChangedEvent>(_ => StyleLabels());

                        return wrapper;
                    }

                    // Horizontal ("same row") — return the MinMaxSlider ITSELF (not a plain wrapper) so its native
                    // label keeps the inspector's aligned label column. A wrapper here does NOT align, which is why
                    // the label used to render to the RIGHT of the min value. Mount the value fields INSIDE the
                    // slider — min right after the label, max after the track — via .hierarchy (bypasses BaseField's
                    // contentContainer so the indices are reliable). Result: [label] [min] [===== track =====] [max].
                    // (No flexGrow on the slider itself: as the returned field it stretches to the row width like
                    // any field, and its own visualInput fills the space between the min/max fields — matching the
                    // no-labels `return slider` path. flexGrow here would instead stretch its HEIGHT in the column.)

                    // Margins live on the MOUNT so a suffix pair keeps number+unit hugging while the gap stays
                    // between the field and the slider track.
                    minMount.style.flexShrink = 0;
                    maxMount.style.flexShrink = 0;
                    minMount.style.marginRight = 6;
                    maxMount.style.marginLeft = 6;

                    // Fixed value-box width derived from the LIMITS (never the live value) so the track's start/end
                    // stay put as the numbers change width. Editable fields default to a fixed 64; the value
                    // LABELS would otherwise be auto-width, so a longer min value shoves the whole track right
                    // ("the slider bar start jumps with the value's digit count").
                    int valChars = Mathf.Max(FormatValue(slider.lowLimit).Length, FormatValue(slider.highLimit).Length, 3);
                    float valueBoxWidth = Mathf.Clamp(valChars * 8f + 10f, 44f, 100f);
                    minElement.style.width = valueBoxWidth;
                    maxElement.style.width = valueBoxWidth;

                    // Anchored on the INPUT, not on a hard-coded index. "Index 0 is the label" holds only while
                    // the field actually has a label element in its hierarchy; with a blank label the input can
                    // sit at 0, and Insert(1, …) then drops min AFTER the track — the wrong side of the slider.
                    // Finding the input and inserting before it is right in both shapes, so the layout no
                    // longer depends on an assumption about Unity's BaseField internals that we cannot pin.
                    var sliderInput = slider.Q(className: FieldAffixes.BaseFieldInputClass);
                    // NO minWidth floor on the track here, though MinMaxTrackFloor looks like it wants one.
                    // TRIED and REVERTED: pinning the input to 36px reds TestMinMaxReadOutsNeverBleedPastTheirRow
                    // — an editable read-out with a one-character suffix at a 320px row then bleeds 22px past
                    // the field. That is the trade the fold above already refuses to make ("the row paints past
                    // the edge and summons HorizontalOverflowRescue's rail... the one outcome that reads as
                    // broken to a buyer"): a collapsed track is ugly, an overflowing row is broken.
                    // The sliver this looked like it would fix was NOT a missing floor. It was measured inside a
                    // DiagonalSplit pane whose own width was oscillating 122↔119 every tick, so ReflowValues
                    // could never settle on a fold decision — fixed at the source by FrameBuilder's
                    // MinPaneContentWidth clamp. Do not re-add this without re-reading that test.
                    var minAnchor = sliderInput;
                    while (minAnchor != null && minAnchor.hierarchy.parent != slider) minAnchor = minAnchor.hierarchy.parent;
                    int minAt = minAnchor != null ? slider.hierarchy.IndexOf(minAnchor) : 1;
                    slider.hierarchy.Insert(minAt, minMount); // immediately before the track, after any label
                    slider.hierarchy.Add(maxMount);           // after the track/input, at the row's right edge

                    // AUTO-STACK when the row cannot fit.
                    //
                    // [label][min][track][max], with both value mounts pinned (flexShrink 0) so the track's
                    // ends do not slide as the numbers change width. Add affixes and the row wants more than
                    // the inspector column has. MEASURED at a 320px column: a ONE-CHARACTER "$"/"x" pair grows
                    // each mount from 58px to 91px, and by then the track has ALREADY collapsed to 0 — the
                    // flexible part is spent before the affixes are paid for, so the row paints past the edge
                    // and summons HorizontalOverflowRescue's rail. That rail is the one outcome that reads as
                    // broken to a buyer, so fold the values onto their own line instead: the same shape
                    // SliderFieldPlacement.Below gives, reached automatically. Same philosophy as
                    // FieldAffixes demoting an inside affix when the box would starve the number.
                    //
                    // WRAP, never a column. The label and the input must stay DIRECT children of the slider or
                    // the aligned label column and Unity's own base-field USS stop reaching them — a column
                    // would also stack every child, and this row has four. A single values row with
                    // flex-basis 100% claims its own flex line and leaves the other two where they were.
                    // See the label-above note, which records this exact row shape as the reason.
                    VisualElement stackedRow = null;
                    bool stacked = false;
                    int minHomeIndex = slider.hierarchy.IndexOf(minMount);

                    void StackValues()
                    {
                        if (stacked) return;
                        stacked = true;
                        if (stackedRow == null)
                        {
                            stackedRow = new VisualElement();
                            stackedRow.AddToClassList(MinMaxStackedRowClass);
                            stackedRow.style.flexDirection = FlexDirection.Row;
                            stackedRow.style.justifyContent = Justify.SpaceBetween;
                            stackedRow.style.alignItems = Align.Center;
                            stackedRow.style.flexBasis = new StyleLength(Length.Percent(100));
                            stackedRow.style.marginTop = 2;
                        }
                        minMount.RemoveFromHierarchy();
                        maxMount.RemoveFromHierarchy();
                        minMount.style.marginRight = 0;
                        maxMount.style.marginLeft = 0;
                        stackedRow.Add(minMount);
                        stackedRow.Add(maxMount);
                        slider.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
                        slider.hierarchy.Add(stackedRow);
                        // FLEX-BASIS 100% ONLY CLAIMS ITS OWN LINE IF A LINE-MATE OCCUPIES SOMETHING.
                        // Wrapping needs the line's bases to EXCEED the container, and Unity's own USS gives
                        // the track (`unity-base-field__input`) flex-basis 0 with grow 1. So the sum is
                        // 0 + 100% == exactly the width: a TIE, not an excess, and no wrap happens. The
                        // stacked row then shares the line with the track, and because it already spent every
                        // pixel of basis there is no free space left for grow to hand back — the track
                        // resolves to width ZERO. The fold "succeeds" (class on, wrap on) and produces no
                        // fold at all, just a slider with no slider in it.
                        //
                        // This only bites when the native label is HIDDEN. A visible label contributes its own
                        // basis, which is what pushed the sum over the line and made every ordinary inspector
                        // row fold correctly — so the failure is exclusive to the label-suppressed shapes:
                        // a column cell, a table cell, a Frame/split-pane cell (BuildColumnCellField renders
                        // its own label and blanks Unity's). MEASURED live in a −26° split pane, 188px of
                        // content: slider w=188, track w=0, stacked row w=188 sitting on the SAME line, whole
                        // widget 22px tall where a real fold is 44.
                        //
                        // One pixel of basis on the track turns the tie into an excess and the line breaks.
                        // It cannot cost the track any width — it grows into its line either way — and it is
                        // NOT the `minWidth` floor that TestMinMaxReadOutsNeverBleedPastTheirRow forbids: a
                        // basis is a starting size that flex may shrink, a minWidth is a hard floor that makes
                        // the row bleed. It is also only set WHILE STACKED, i.e. only when the row has already
                        // decided it cannot fit on one line.
                        if (minAnchor != null) minAnchor.style.flexBasis = 1f;
                    }

                    void UnstackValues()
                    {
                        if (!stacked) return;
                        stacked = false;
                        stackedRow.RemoveFromHierarchy();
                        minMount.RemoveFromHierarchy();
                        maxMount.RemoveFromHierarchy();
                        minMount.style.marginRight = 6;
                        maxMount.style.marginLeft = 6;
                        slider.style.flexWrap = UnityEngine.UIElements.Wrap.NoWrap;
                        slider.hierarchy.Insert(minHomeIndex, minMount);
                        slider.hierarchy.Add(maxMount);
                        // Hand the track's basis back to Unity's sheet — paired with the tie-break in
                        // StackValues, which explains why it was ever set.
                        if (minAnchor != null) minAnchor.style.flexBasis = StyleKeyword.Null;
                    }

                    void ReflowValues()
                    {
                        float available = slider.layout.width;
                        if (float.IsNaN(available) || available <= 0f) return;
                        float mounts = minMount.layout.width + maxMount.layout.width;
                        if (mounts <= 0f) return;   // nothing measured yet — decide on a later pass
                        // Count the label ONLY when it actually shares the track's line. "Label above" gives it
                        // a full-width line of its own, where it competes with nothing — charging the track for
                        // it there made `need` exceed `available` by construction, so every label-above row
                        // folded on its first geometry pass. Asked geometrically rather than by class so any
                        // other route to a label on its own line is covered too; before layout both rects read
                        // zero, which reports "not sharing" and biases toward NOT folding — the safe direction,
                        // and a later pass corrects it.
                        var labelEl = slider.labelElement;
                        bool labelOnTheTracksLine = labelEl != null
                            && labelEl.resolvedStyle.display != DisplayStyle.None
                            && minAnchor != null
                            && labelEl.layout.yMax > minAnchor.layout.y + 0.5f;
                        float labelWidth = labelOnTheTracksLine ? labelEl.layout.width : 0f;
                        // The two 6px mount gutters are counted whether or not they are currently applied, so
                        // `need` reads the same in both states. That is what makes the hysteresis below sound:
                        // a measurement that changed with the layout it triggers is a flap waiting to happen.
                        float need = labelWidth + mounts + MinMaxTrackFloor + 12f;

                        if (!stacked && need > available) StackValues();
                        else if (stacked && need + MinMaxStackHysteresis <= available) UnstackValues();
                    }

                    ReflowValues();
                    slider.RegisterCallback<GeometryChangedEvent>(_ => ReflowValues());

                    void StyleInlineLabels()
                    {
                        var ts = node.MinMaxLabelTextStyle;
                        slider.Query<TextElement>().ForEach(te =>
                        {
                            if (te.ClassListContains(UnitSuffixClass)) return; // suffix stays dim
                            // Only touch the min/max value elements (tagged sq-minmax-label) — never the slider's
                            // own aligned label or its track internals. Walk ANCESTORS (not just the direct parent):
                            // an editable value is a FloatField carrying the class, whose actual text sits a couple
                            // levels down in its input container, so a one-level parent check missed it (styling then
                            // did nothing in the Editable + Left/Right combo).
                            bool inMinMaxValue = false;
                            for (var e = (VisualElement)te; e != null && e != slider; e = e.parent)
                            {
                                if (e.ClassListContains("sq-minmax-label")) { inMinMaxValue = true; break; }
                            }
                            if (!inMinMaxValue) return;

                            if (ts.fontStyle.isOverridden) te.style.unityFontStyleAndWeight = ts.fontStyle.value;

                            // Match a normal field's size/color: the standard field styling unless the user sets
                            // an explicit text-style override.
                            if (TextStyleApplier.IsUsableFontSize(ts.fontSize)) te.style.fontSize = ts.fontSize.value;
                            if (ts.color.isOverridden) te.style.color = ts.color.value;

                            // Keep the digits centered on the track's centerline (unless the user overrides).
                            te.style.unityTextAlign = ts.align.isOverridden ? ts.align.value : TextAnchor.MiddleLeft;

                            if (ts.font != null) te.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ts.font));
                        });
                    }
                    StyleInlineLabels();
                    slider.RegisterCallback<GeometryChangedEvent>(_ => StyleInlineLabels());

                    return slider;
                }

                return slider;
            }

            var defaultField = new PropertyField(prop);
            if (node.IsLabelHidden) defaultField.label = "";
            else if (node.EffectiveLabelOverride != null) defaultField.label = node.EffectiveLabelOverride;
            return defaultField;
        }

    }
}
