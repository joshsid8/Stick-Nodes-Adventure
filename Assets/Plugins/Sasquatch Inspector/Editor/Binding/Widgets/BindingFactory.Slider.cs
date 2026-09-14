using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        private static VisualElement CreateSlider(BindingNode node, SerializedProperty prop, string resolvedLabel, bool isTableCell = false)
        {
            // The node's stored limits win when explicitly set, so a slider range set in the designer (or
            // imported from a [Range] attribute) stays baked into the layout even if the attribute is later
            // removed. When unset, the bounds fall back to the live [Range] attribute, then to these per-type
            // defaults (0..1 for float, 0..100 for int) — so pre-existing slider layouts are unchanged.
            if (prop.propertyType == SerializedPropertyType.Integer)
            {
                var slider = new SliderInt();
                slider.label = resolvedLabel;

                ResolveLimits(node, prop, out float min, out float max, 0f, 100f);
                WidenToContainStoredValue(prop, ref min, ref max); // or displaying it REWRITES it — see below
                slider.lowValue = (int)Mathf.Floor(min);
                slider.highValue = (int)Mathf.Ceil(max);

                SeedThenBind(slider, prop); // ORDER MATTERS on a clamping control — see SeedThenBind
                ArmStoredValueWiden(slider, prop, min, max, isInt: true);

                if (slider.labelElement != null)
                {
                    slider.labelElement.style.display = DisplayStyle.Flex;
                    slider.labelElement.style.color = WidgetPalette.LabelPrimary;
                }

                ApplySliderLook(slider, node);
                return WrapSliderField(slider, prop, node, isInt: true, isTableCell: isTableCell);
            }
            else if (prop.propertyType == SerializedPropertyType.Float)
            {
                var slider = new Slider();
                slider.label = resolvedLabel;

                ResolveLimits(node, prop, out float min, out float max, 0f, 1f);
                WidenToContainStoredValue(prop, ref min, ref max); // or displaying it REWRITES it — see below
                slider.lowValue = min;
                slider.highValue = max;

                SeedThenBind(slider, prop); // ORDER MATTERS on a clamping control — see SeedThenBind
                ArmStoredValueWiden(slider, prop, min, max, isInt: false);

                if (slider.labelElement != null)
                {
                    slider.labelElement.style.display = DisplayStyle.Flex;
                    slider.labelElement.style.color = WidgetPalette.LabelPrimary;
                }

                ApplySliderLook(slider, node);
                return WrapSliderField(slider, prop, node, isInt: false, isTableCell: isTableCell);
            }

            var defaultField = new PropertyField(prop);
            if (node.IsLabelHidden) defaultField.label = "";
            else if (node.EffectiveLabelOverride != null) defaultField.label = node.EffectiveLabelOverride;
            return defaultField;
        }

        /// <summary>
        /// Stamps the bound value onto a just-bound control SYNCHRONOUSLY. <c>BindProperty</c> applies it on a
        /// LATER binding-update tick, not now — so without this the control is born EMPTY: a slider knob hard
        /// against the left end of its track, a text field showing nothing, a number reading zero.
        ///
        /// Measured on DemoInventory while rapidly toggling the designer's Interact switch (every toggle is a
        /// full canvas rebuild, and under that flood the bind tick slips):
        ///   • Carry Capacity, range 0–200, value 120 — laid out at <c>val=0, knob 0%</c> for FIVE TO EIGHT
        ///     frames before snapping to 55%, on 3 of ~22 toggles.
        ///   • The slots table's <c>label</c> cell — laid out BLANK for ~10 frames.
        /// Intermittent, which is why both read as a "redraw blip" rather than a steady bug.
        ///
        /// This is the identical trap <c>CreateToggle</c> closed for the switch knob in July; nothing else in
        /// the widget factory got the same treatment. THE RULE: every control we hand to <c>BindProperty</c>
        /// must be seeded here in the same breath, because an unbound control does not render as BLANK — it
        /// renders as a confident, wrong value (zero, empty, unchecked) that the user reads as real.
        ///
        /// Skipped for a mixed multi-selection: <c>MixedValueUI</c> paints the muted em-dash state, and
        /// stamping the first object's value would silently claim the others agree.
        ///
        /// SEED FIRST, BIND SECOND — always go through <see cref="SeedThenBind"/> rather than calling this
        /// yourself after <c>BindProperty</c>. The reverse order silently REWRITES the user's data; see there.
        /// </summary>
        // ===== A slider's own bounds must CONTAIN the value it is bound to =====
        // A Slider physically cannot hold a value outside lowValue..highValue — every route in, the `value`
        // setter and SetValueWithoutNotify alike, clamps. And Unity's binding ANNOUNCES the field's current
        // value when it attaches (SerializedObjectBindingToBaseField.OnFieldAttached → SendEvent, carrying
        // previousValue == newValue), which its own FieldValueChanged handler writes straight back to the
        // property. Put those together and a stored value outside the widget's range is DESTROYED by being
        // looked at: DemoPlayerStats lost critChance 0.18 → 1, attackPower 84 → 60, shieldCharge 0.65 → 1 on
        // selection, with nobody touching anything.
        //
        // There is no ordering that escapes it — the field simply cannot represent the number. So the control's
        // OWN bounds stretch to swallow the outlier instead. The node's authored range is untouched; only this
        // one control's runtime bounds move, and only while the data sits outside them. The knob still lands at
        // the end of the track it would have pinned to, and the companion value field now agrees with it
        // instead of contradicting it.

        /// <summary>Widens <paramref name="min"/>/<paramref name="max"/> just enough to contain the stored value.</summary>
        private static void WidenToContainStoredValue(SerializedProperty prop, ref float min, ref float max)
        {
            // A mixed multi-selection paints the em-dash state and has no single value to contain.
            if (prop == null || prop.hasMultipleDifferentValues) return;
            float v;
            if (prop.propertyType == SerializedPropertyType.Integer) v = prop.longValue;
            else if (prop.propertyType == SerializedPropertyType.Float) v = (float)prop.doubleValue;
            else return;
            if (float.IsNaN(v) || float.IsInfinity(v)) return; // garbage data must not blow the track open
            if (v < min) min = v;
            if (v > max) max = v;
        }

        /// <summary>
        /// Keeps the widening true for a value that goes out of range AFTER the build — a script writing the
        /// field while the inspector is open. Widen-only: the authored bounds are never tightened underneath a
        /// user mid-drag, and the next rebuild starts from the authored range again.
        /// </summary>
        private static void ArmStoredValueWiden(VisualElement slider, SerializedProperty prop, float min, float max, bool isInt)
        {
            slider.TrackPropertyValueWithRefresh(prop, p =>
            {
                float lo = min, hi = max;
                WidenToContainStoredValue(p, ref lo, ref hi);
                if (isInt)
                {
                    var si = slider as SliderInt;
                    if (si == null) return;
                    if (lo < si.lowValue) si.lowValue = (int)Mathf.Floor(lo);
                    if (hi > si.highValue) si.highValue = (int)Mathf.Ceil(hi);
                }
                else
                {
                    var sf = slider as Slider;
                    if (sf == null) return;
                    if (lo < sf.lowValue) sf.lowValue = lo;
                    if (hi > sf.highValue) sf.highValue = hi;
                }
            });
        }

        /// <summary>
        /// Seeds a control from its property and THEN binds it. The single approved way to pair the two.
        /// </summary>
        /// <remarks>
        /// THE BUG THIS EXISTS TO PREVENT — a silent write performed by merely DISPLAYING an object:
        ///
        /// A <c>Slider</c>/<c>SliderInt</c> CLAMPS whatever it is handed into its own <c>lowValue..highValue</c>,
        /// including through <c>SetValueWithoutNotify</c>. So on a stored value that sits outside the widget's
        /// range — a designer range narrower than the data, a <c>[Range]</c> added after the values were
        /// authored, a value written from code — the sequence bind → seed leaves the control holding the CLAMPED
        /// number while Unity's two-way binding still believes the field agrees with the property. Its first
        /// sync assigns the real value, the slider clamps it AGAIN, and that second clamp is dispatched as a
        /// genuine <c>ChangeEvent</c> — indistinguishable from a drag. <c>SerializedObjectBindingToBaseField
        /// .FieldValueChanged</c> then writes the clamped number straight back to the property.
        ///
        /// Measured on <c>DemoPlayerStats</c>: selecting the object, touching nothing, rewrote
        /// <c>critChance</c> 0.18 → 1, <c>attackPower</c> 84 → 60 and <c>shieldCharge</c> 0.65 → 1. On a prefab
        /// instance that is three override arrows nobody made; on a plain object it is the values, gone.
        ///
        /// Seeding FIRST closes it: the clamp lands before any binding exists to notice it, so the binding's
        /// first sync is a no-op and no <c>ChangeEvent</c> is ever raised. The control still paints its real
        /// value on frame one, which is the whole point of seeding.
        ///
        /// Non-clamping controls (text, colour, object) cannot hit the trap at all — they are routed through
        /// here anyway so no future widget has to know which kind it is holding.
        /// </remarks>
        internal static void SeedThenBind(VisualElement control, SerializedProperty prop)
        {
            SeedBoundValue(control, prop);
            var bindable = control as IBindable;
            if (bindable != null) BindingExtensions.BindProperty(bindable, prop);
        }

        internal static void SeedBoundValue(VisualElement control, SerializedProperty prop)
        {
            if (control == null || prop == null || prop.hasMultipleDifferentValues) return;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    // longValue covers every integer width; each control clamps into its own range itself.
                    if (control is INotifyValueChanged<int> asInt) asInt.SetValueWithoutNotify((int)prop.longValue);
                    else if (control is INotifyValueChanged<long> asLong) asLong.SetValueWithoutNotify(prop.longValue);
                    break;
                case SerializedPropertyType.Float:
                    if (control is INotifyValueChanged<float> asFloat) asFloat.SetValueWithoutNotify((float)prop.doubleValue);
                    else if (control is INotifyValueChanged<double> asDouble) asDouble.SetValueWithoutNotify(prop.doubleValue);
                    break;
                case SerializedPropertyType.String:
                    if (control is INotifyValueChanged<string> asStr) asStr.SetValueWithoutNotify(prop.stringValue ?? "");
                    break;
                case SerializedPropertyType.Boolean:
                    if (control is INotifyValueChanged<bool> asBool) asBool.SetValueWithoutNotify(prop.boolValue);
                    break;
                case SerializedPropertyType.Color:
                    if (control is INotifyValueChanged<Color> asColor) asColor.SetValueWithoutNotify(prop.colorValue);
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (control is INotifyValueChanged<UnityEngine.Object> asObj)
                        asObj.SetValueWithoutNotify(prop.objectReferenceValue);
                    break;
                case SerializedPropertyType.Vector2:
                    if (control is INotifyValueChanged<Vector2> asV2) asV2.SetValueWithoutNotify(prop.vector2Value);
                    break;
                case SerializedPropertyType.Vector3:
                    if (control is INotifyValueChanged<Vector3> asV3) asV3.SetValueWithoutNotify(prop.vector3Value);
                    break;
                case SerializedPropertyType.Vector4:
                    if (control is INotifyValueChanged<Vector4> asV4) asV4.SetValueWithoutNotify(prop.vector4Value);
                    break;
            }
        }

        // Markers preventing duplicate event registration — ApplyNow re-runs on every GeometryChanged.
        private const string KnobHoverBoundClass = "sasq-slider-knob-hover-bound";
        private const string TrackHoverBoundClass = "sasq-slider-track-hover-bound";
        private const string RingFxBoundClass = "sasq-slider-ring-fx-bound";
        private const string GripDirtyBoundClass = "sasq-slider-grip-dirty-bound";

        // The focus/hover ring grows 4px past the knob on every side, mirroring the editor theme's
        // 18x18 ring around the stock 10px knob (radius likewise +4 so capsules get capsule rings).
        private const float RingPad = 4f;

        /// <summary>The editor theme's own slider-track thickness (measured 2px on Unity 6000.0, dark and
        /// light). Used ONLY as the fallback when re-anchoring a Normal-size track before its height has
        /// resolved — the resolved value is preferred whenever it is real, so a future theme that ships a
        /// different track still centres correctly on the second layout pass.</summary>
        internal const float StockTrackHeight = 2f;

        // The Grip knob's engraved ridges — the visual signature of a mixer-fader cap. A single
        // SliderKnobGripGlyph child of the dragger (world-snapped so the knob's inline translate can't
        // blur the 1px lines — see the class doc), ignored by hit-testing; its count/size/color refresh
        // on every look application and it is removed again when the shape stops being Grip.
        private static void SyncGripRidges(VisualElement dragEl, bool wantGrip, int count, float knobH, Color capColor)
        {
            var glyph = dragEl.Q<SliderKnobGripGlyph>();
            if (!wantGrip || count <= 0)
            {
                glyph?.RemoveFromHierarchy();
                return;
            }

            if (glyph == null)
            {
                glyph = new SliderKnobGripGlyph();
                dragEl.Add(glyph);
            }

            float ridgeW = Mathf.Max(1f, Mathf.Round(knobH * 0.12f));
            float ridgeH = Mathf.Round(knobH * 0.5f);
            float gap = Mathf.Max(2f, Mathf.Round(knobH * 0.12f));
            // Engraved look: ridges darken a bright cap and lighten a dark one.
            float lum = 0.299f * capColor.r + 0.587f * capColor.g + 0.114f * capColor.b;
            Color ridgeColor = Color.Lerp(capColor, lum > 0.5f ? Color.black : Color.white, 0.40f);
            ridgeColor.a = 1f;

            glyph.Configure(count, ridgeW, ridgeH, gap, ridgeColor);
        }

        // Hover tint when the user set a knob color but no hover color: brighten, or darken when the
        // knob is already bright, so feedback never silently disappears (the theme's :hover rule is
        // dead the moment an inline background color is set). Internal because the test assembly asserts
        // against this exact derivation — changing the formula reddens a test.
        internal static Color AutoKnobHoverTint(Color c)
        {
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            Color target = lum > 0.7f ? Color.black : Color.white;
            Color tint = Color.Lerp(c, target, 0.22f);
            tint.a = c.a;
            return tint;
        }

        // Applies a node's slider look (track color/thickness, knob color, knob shape/roundness) to a built
        // Slider/SliderInt. Each piece applies only when set (alpha 0 / 0 / Default = unset), so an unstyled
        // slider keeps Unity's default look. Deferred onto a GeometryChanged tick too, because the tracker /
        // dragger sub-elements are built lazily.
        private static void ApplySliderLook(VisualElement slider, BindingNode node)
        {
            if (SessionState.GetBool("SASQ_DIAG_NOLOOK", false)) return;
            var ws = node.DisplayStyleData;
            bool hasBarColor = ws.sliderBarColor.a > 0.001f;
            bool hasKnobColor = ws.sliderKnobColor.a > 0.001f;
            bool hasKnobHoverColor = ws.sliderKnobHoverColor.a > 0.001f;
            bool hasCenterBarColor = ws.sliderCenterBarColor.a > 0.001f;
            bool hasCenterBarHoverColor = ws.sliderCenterBarHoverColor.a > 0.001f;
            bool hasShape = ws.sliderKnobShape != SliderKnobShape.Default;
            bool sized = ws.sliderSize != SliderSize.Normal;
            bool hasBarHoverColor = ws.sliderBarHoverColor.a > 0.001f;
            bool hasRadius = ws.cornerRadius.isOverridden;
            if (!hasBarColor && !hasKnobColor && !hasKnobHoverColor && !hasCenterBarColor && !hasCenterBarHoverColor && !hasShape && !sized && !hasBarHoverColor && !hasRadius) return;

            // Size scales the knob height + the bar (track) height together; the knob stays larger than the bar.
            float knobH = ws.sliderSize == SliderSize.ExtraLarge ? 22f : ws.sliderSize == SliderSize.Large ? 16f : 12f;
            // EVERY track height here is EVEN, and that is load-bearing, not tidiness. The track and the
            // knob are both centred with `top: 50%` + `marginTop: -h/2`, and Yoga rounds each part's
            // resolved position to the pixel grid. An ODD height turns that offset into a half pixel, so
            // the two parts round to DIFFERENT pixels and the knob sits half a pixel off its bar
            // (invisible at 100% UI scale, a whole device pixel at 200%). ExtraLarge was 9f and did
            // exactly that in its 22px lane. Same parity, same rounding, one centre line -- at any lane
            // height, which is the whole point of the contract below.
            float barH = ws.sliderSize == SliderSize.ExtraLarge ? 8f : ws.sliderSize == SliderSize.Large ? 6f : 0f; // 0 = leave Unity default

            // THE CROSS-AXIS CONTRACT, in one flag: the moment the knob (or a min-max centre bar/thumb) is
            // re-anchored to top:50%, EVERY part that must line up with it has to be re-anchored the same
            // way — including the track, which at Normal size keeps the theme's own thickness and would
            // otherwise stay on the theme's fixed pixel offset. Mixing the two references is what let the
            // knob float off the bar in any lane that is not exactly the stock height.
            bool knobOwnsCrossAxis = hasShape || sized;

            // Min-max sliders style their parts to scale the STOCK min-max look: one contiguous
            // block — narrow thumbs flush with (and the same height as) the center bar, outer ends
            // rounded. Capsule reads ridiculous as a range handle, so a min-max demotes it to Default
            // (the designer doesn't offer it there; this guards stale data).
            bool isMinMax = slider is MinMaxSlider;
            var shape = ws.sliderKnobShape;
            if (isMinMax && shape == SliderKnobShape.Capsule) shape = SliderKnobShape.Default;

            // Main dragger geometry (single-knob sliders); the focus ring derives from it too.
            float knobW, knobR;
            switch (shape)
            {
                case SliderKnobShape.Square:    knobW = knobH;                       knobR = Mathf.Max(0f, ws.sliderKnobRoundness); break;
                case SliderKnobShape.Capsule:   knobW = knobH * 2f;                  knobR = knobH * 0.5f; break;
                case SliderKnobShape.Rectangle: knobW = knobH * 2f;                  knobR = Mathf.Max(0f, ws.sliderKnobRoundness); break;
                case SliderKnobShape.Grip:      knobW = Mathf.Round(knobH * 1.5f);   knobR = Mathf.Round(knobH * 0.3f); break;
                default:                        knobW = knobH;                       knobR = knobH * 0.5f; break;
            }

            // Min-max thumb width — stock is a 7px handle in a 10px block (0.7), Square keeps its 1:1
            // identity, Rectangle stays visibly wider, Grip needs room for its ridges.
            float thumbW;
            switch (shape)
            {
                case SliderKnobShape.Square:    thumbW = knobH; break;
                case SliderKnobShape.Rectangle: thumbW = Mathf.Round(knobH * 1.3f); break;
                case SliderKnobShape.Grip:      thumbW = Mathf.Max(8f, Mathf.Round(knobH * 0.8f)); break;
                default:                        thumbW = Mathf.Max(6f, Mathf.Round(knobH * 0.7f)); break;
            }

            void ApplyNow()
            {
                var tracker = slider.Q<VisualElement>(className: "unity-slider__tracker")
                           ?? slider.Q<VisualElement>(className: "unity-base-slider__tracker")
                           ?? slider.Q<VisualElement>(className: "unity-min-max-slider__tracker");
                
                var dragger = slider.Q<VisualElement>(className: "unity-slider__dragger")
                           ?? slider.Q<VisualElement>(className: "unity-base-slider__dragger");

                var minThumb = slider.Q<VisualElement>(className: "unity-min-max-slider__min-thumb");
                var maxThumb = slider.Q<VisualElement>(className: "unity-min-max-slider__max-thumb");
                var centerBar = slider.Q<VisualElement>(className: "unity-min-max-slider__dragger");

                // The editor theme positions every slider part with FIXED pixel offsets tuned only
                // for the stock geometry (17px control, 2px bar, 10px knob): tracker top:8/margin:2,
                // dragger top:4/margin:-2, min-max thumbs top:0 inside the center bar — NOT with
                // top:50%. So any part we resize must own its cross axis completely: absolute
                // position + top:50% + marginTop -h/2. Keeping the theme's top while adding our own
                // margin is what scattered every shape × size combo to a different off-center offset
                // (measured per-combo on Unity 6000.3). Never touch
                // `translate` — BaseSlider drives the knob along the main axis with an inline
                // translate.x, and the theme never centers with it.

                if (centerBar != null)
                {
                    centerBar.style.backgroundColor = hasCenterBarColor ? (StyleColor)ws.sliderCenterBarColor : (StyleColor)StyleKeyword.Null;

                    // The center range hovers through our own callbacks too — and unlike the knobs,
                    // the theme has NO native :hover rule for it, so with no custom colors the tint
                    // derives from the theme's own resolved color. Colors are read live on every
                    // event: capturing them at bind time goes stale, because ApplyNow re-runs on the
                    // SAME elements when the designer edits a color and the bound-marker blocks rewiring.
                    if (!centerBar.ClassListContains(KnobHoverBoundClass))
                    {
                        centerBar.AddToClassList(KnobHoverBoundClass);
                        centerBar.RegisterCallback<PointerEnterEvent>(_ =>
                        {
                            // A DISABLED element still receives pointer events (which is why the theme's own
                            // rule is :hover:enabled), and hover fires no geometry event — so a repaint here
                            // would never be undone. KeepSliderKnobsSolidWhileDisabled owns the colour then,
                            // whatever disabled it: read-only, a Toggle Group, or a condition outcome.
                            if (!centerBar.enabledInHierarchy) return;
                            var w = node.DisplayStyleData;
                            Color rest = w.sliderCenterBarColor.a > 0.001f ? w.sliderCenterBarColor : centerBar.resolvedStyle.backgroundColor;
                            centerBar.style.backgroundColor = w.sliderCenterBarHoverColor.a > 0.001f
                                ? w.sliderCenterBarHoverColor
                                : AutoKnobHoverTint(rest);
                        });
                        centerBar.RegisterCallback<PointerLeaveEvent>(_ =>
                        {
                            if (!centerBar.enabledInHierarchy) return;
                            var w = node.DisplayStyleData;
                            centerBar.style.backgroundColor = w.sliderCenterBarColor.a > 0.001f
                                ? (StyleColor)w.sliderCenterBarColor
                                : (StyleColor)StyleKeyword.Null;
                        });
                    }

                    // When the look is styled, the center bar matches the THUMB height so thumbs +
                    // bar read as ONE contiguous block (the stock min-max look, just scaled); the
                    // thin track underneath keeps its own barH. Untouched theme otherwise.
                    float centerH = (hasShape || sized) ? knobH : barH;
                    if (centerH > 0f)
                    {
                        centerBar.style.height = centerH;
                        centerBar.style.top = Length.Percent(50f);
                        centerBar.style.marginTop = -centerH * 0.5f;
                    }
                    else
                    {
                        centerBar.style.height = StyleKeyword.Null;
                        centerBar.style.top = StyleKeyword.Null;
                        centerBar.style.marginTop = StyleKeyword.Null;
                    }
                }

                if (tracker != null)
                {
                    if (hasBarColor) tracker.style.backgroundColor = ws.sliderBarColor;

                    // Corner rounding (opt-in) — plain Slider only; MinMax owns its own contiguous-block radii.
                    if (hasRadius && !isMinMax)
                    {
                        float rr = Mathf.Max(0f, ws.cornerRadius.value);
                        tracker.style.borderTopLeftRadius = rr; tracker.style.borderTopRightRadius = rr;
                        tracker.style.borderBottomLeftRadius = rr; tracker.style.borderBottomRightRadius = rr;
                    }

                    // Bar hover tint — the theme has no native :hover on the track, so it runs through our own
                    // callbacks, read live (like the knob/center-bar hover). No-ops when no hover color is set.
                    if (!tracker.ClassListContains(TrackHoverBoundClass))
                    {
                        tracker.AddToClassList(TrackHoverBoundClass);
                        tracker.RegisterCallback<PointerEnterEvent>(_ =>
                        {
                            if (node.IsReadOnly) return; // a read-only track must not light up under the pointer
                            var w = node.DisplayStyleData;
                            if (w.sliderBarHoverColor.a <= 0.001f) return; // unset → leave the resting track color
                            tracker.style.backgroundColor = w.sliderBarHoverColor;
                        });
                        tracker.RegisterCallback<PointerLeaveEvent>(_ =>
                        {
                            if (node.IsReadOnly) return;
                            var w = node.DisplayStyleData;
                            tracker.style.backgroundColor = w.sliderBarColor.a > 0.001f
                                ? (StyleColor)w.sliderBarColor : (StyleColor)StyleKeyword.Null;
                        });
                    }

                    if (barH > 0f)
                    {
                        // The theme tracker is a relatively-positioned flex child; pull it out of
                        // the flow (keeping full width via left/right) so the resized dragger's
                        // flow slot can't shift it.
                        tracker.style.position = Position.Absolute;
                        tracker.style.left = 0f;
                        tracker.style.right = 0f;
                        tracker.style.height = barH;
                        tracker.style.top = Length.Percent(50f);
                        tracker.style.marginTop = -barH * 0.5f;
                    }
                    else if (knobOwnsCrossAxis)
                    {
                        // SIZE IS NORMAL, SO THE BAR KEEPS THE THEME'S THICKNESS — BUT THE KNOB HAS ALREADY
                        // MOVED TO top:50%, AND THE BAR MUST SHARE THAT REFERENCE OR THE TWO ONLY MEET BY
                        // COINCIDENCE. The theme centres the thin track with a FIXED `top: 8px` tuned for the
                        // stock 17-18px lane; the knob centres on a PERCENTAGE of whatever the lane actually
                        // measures. Those agree at exactly one lane height. Measured (Unity 6000.0, all four
                        // non-Default shapes at Normal): 18px lane with the value field -> 0.00px, 17px lane
                        // with the field hidden -> 0.50px (invisible at 100% UI scale, a whole device pixel at
                        // 200%), and a 12px lane inside a TABLE CELL -> 3.00px, which is the knob floating
                        // clean off the bar. Same story on the MinMaxSlider, whose centre bar and thumbs also
                        // move to 50%: a steady 1.00px at Normal size — right on the old test's tolerance.
                        //
                        // So re-anchor the bar the same way, keeping its own (theme) thickness: read the
                        // resolved height rather than assume 2px. `h > 0.5f` is the NaN guard too — an
                        // unlaid-out element resolves NaN, and NaN fails the comparison, so it takes the
                        // stock fallback instead of writing a NaN margin (see mathf-max-does-not-sanitize-nan).
                        //
                        // ONLY when the knob was re-anchored. At stock geometry (Default shape + Normal size)
                        // ApplySliderLook leaves every part on the theme's mutually-consistent offsets, and
                        // pulling the track out of flow there collapses the control's height and drops the
                        // grow-on-focus ring below the knob.
                        tracker.style.position = Position.Absolute;
                        tracker.style.left = 0f;
                        tracker.style.right = 0f;
                        tracker.style.height = StyleKeyword.Null; // the theme's own thickness stands
                        tracker.style.top = Length.Percent(50f);
                        float th = tracker.resolvedStyle.height;
                        tracker.style.marginTop = -(th > 0.5f ? th : StockTrackHeight) * 0.5f;
                    }
                    else
                    {
                        tracker.style.position = StyleKeyword.Null;
                        tracker.style.left = StyleKeyword.Null;
                        tracker.style.right = StyleKeyword.Null;
                        tracker.style.height = StyleKeyword.Null;
                        tracker.style.top = StyleKeyword.Null;
                        tracker.style.marginTop = StyleKeyword.Null;
                    }
                }

                // part: 0 = main dragger, 1 = min thumb, 2 = max thumb.
                void StyleDragger(VisualElement dragEl, int part)
                {
                    if (dragEl == null) return;
                    bool isThumb = part != 0;
                    if (hasKnobColor) dragEl.style.backgroundColor = ws.sliderKnobColor;

                    // An inline background color silently kills the theme's :hover rule, so hover
                    // feedback runs through our own callbacks. Colors are read live on every event:
                    // capturing them at bind time goes stale, because ApplyNow re-runs on the SAME
                    // elements when the designer edits a color and the bound-marker blocks rewiring.
                    // With no custom colors the callbacks no-op, leaving the theme's :hover alive.
                    if (!dragEl.ClassListContains(KnobHoverBoundClass))
                    {
                        dragEl.AddToClassList(KnobHoverBoundClass);
                        dragEl.RegisterCallback<PointerEnterEvent>(_ =>
                        {
                            // A disabled knob is painted solid by KeepSliderKnobsSolidWhileDisabled and must
                            // stay that way: the theme's own :hover rule already carries an :enabled guard,
                            // and hover fires no geometry event, so a repaint here would never be undone.
                            // Keyed on enabledInHierarchy, not IsReadOnly — a Toggle Group or a condition
                            // disables the field just as thoroughly, and from ABOVE it.
                            if (!dragEl.enabledInHierarchy) return;
                            var w = node.DisplayStyleData;
                            if (w.sliderKnobHoverColor.a > 0.001f) dragEl.style.backgroundColor = w.sliderKnobHoverColor;
                            else if (w.sliderKnobColor.a > 0.001f) dragEl.style.backgroundColor = AutoKnobHoverTint(w.sliderKnobColor);
                        });
                        dragEl.RegisterCallback<PointerLeaveEvent>(_ =>
                        {
                            if (!dragEl.enabledInHierarchy) return;
                            var w = node.DisplayStyleData;
                            dragEl.style.backgroundColor = w.sliderKnobColor.a > 0.001f
                                ? (StyleColor)w.sliderKnobColor
                                : (StyleColor)StyleKeyword.Null;
                        });
                    }

                    if (hasShape || sized)
                    {
                        dragEl.style.width = isThumb ? thumbW : knobW;
                        dragEl.style.height = knobH;

                        if (isThumb)
                        {
                            // Outer end rounded, inner end square — with the center bar at the same
                            // height, thumbs + bar read as ONE pill (stock min-max look, scaled).
                            // Square/Rectangle keep their corner identity on all four corners.
                            float rOuter = shape == SliderKnobShape.Square || shape == SliderKnobShape.Rectangle
                                ? Mathf.Max(0f, ws.sliderKnobRoundness)
                                : shape == SliderKnobShape.Grip ? Mathf.Round(knobH * 0.3f) : knobH * 0.5f;
                            float rInner = shape == SliderKnobShape.Square || shape == SliderKnobShape.Rectangle
                                ? Mathf.Max(0f, ws.sliderKnobRoundness)
                                : 0f;
                            bool isMin = part == 1;
                            dragEl.style.borderTopLeftRadius = isMin ? rOuter : rInner;
                            dragEl.style.borderBottomLeftRadius = isMin ? rOuter : rInner;
                            dragEl.style.borderTopRightRadius = isMin ? rInner : rOuter;
                            dragEl.style.borderBottomRightRadius = isMin ? rInner : rOuter;
                        }
                        else
                        {
                            dragEl.style.borderTopLeftRadius = knobR;
                            dragEl.style.borderTopRightRadius = knobR;
                            dragEl.style.borderBottomLeftRadius = knobR;
                            dragEl.style.borderBottomRightRadius = knobR;
                        }

                        // Thumbs are theme-absolute CHILDREN of the center bar — centering them at
                        // 50% of the (already centered, same-height) bar centers them in the control.
                        // The main dragger must leave the flex flow for its top:50% to mean the lane.
                        if (!isThumb) dragEl.style.position = Position.Absolute;
                        dragEl.style.top = Length.Percent(50f);
                        dragEl.style.marginTop = -knobH * 0.5f;

                        SyncGripRidges(dragEl, shape == SliderKnobShape.Grip, isThumb ? 2 : 3, knobH,
                            hasKnobColor ? ws.sliderKnobColor : WidgetPalette.SliderKnobDefault);
                    }
                    else
                    {
                        dragEl.style.width = StyleKeyword.Null;
                        dragEl.style.height = StyleKeyword.Null;
                        dragEl.style.borderTopLeftRadius = StyleKeyword.Null;
                        dragEl.style.borderTopRightRadius = StyleKeyword.Null;
                        dragEl.style.borderBottomLeftRadius = StyleKeyword.Null;
                        dragEl.style.borderBottomRightRadius = StyleKeyword.Null;
                        if (!isThumb) dragEl.style.position = StyleKeyword.Null;
                        dragEl.style.top = StyleKeyword.Null;
                        dragEl.style.marginTop = StyleKeyword.Null;
                        SyncGripRidges(dragEl, wantGrip: false, count: 0, knobH: 0f, capColor: default);
                    }
                }

                StyleDragger(dragger, 0);
                StyleDragger(minThumb, 1);
                StyleDragger(maxThumb, 2);

                // The knob rides the track via an inline translate BaseSlider recomputes on every
                // value change, and that translate is never pixel-snapped. The grip glyph snaps it
                // away, but only for the position it was last painted at — so it must repaint at each
                // new resting value or it blurs again exactly like the old divs. Geometry changes
                // (inspector resize) already re-run ApplyNow; value changes need their own nudge.
                // Guarded so the repeated ApplyNow passes don't stack handlers.
                if (shape == SliderKnobShape.Grip && !slider.ClassListContains(GripDirtyBoundClass))
                {
                    slider.AddToClassList(GripDirtyBoundClass);
                    void RedirtyGrips()
                    {
                        foreach (var g in slider.Query<SliderKnobGripGlyph>().ToList()) g.MarkDirtyRepaint();
                    }
                    if (slider is Slider sf) sf.RegisterValueChangedCallback(_ => RedirtyGrips());
                    else if (slider is SliderInt sfi) sfi.RegisterValueChangedCallback(_ => RedirtyGrips());
                    else if (slider is MinMaxSlider smm) smm.RegisterValueChangedCallback(_ => RedirtyGrips());
                }

                // The focus/hover ring (Slider only — MinMaxSlider has no such element): the theme's
                // dragger-border is a blue disc BaseSlider keeps glued to the knob via the same
                // inline translate, revealed by GROWING it from 0x0 to a fixed 18x18 circle on
                // hover/focus. A reshaped knob needs a matching ring, but inline geometry kills the
                // theme's grow-on-state rules in every state — so the ring gets its final shape
                // permanently and visibility moves to opacity driven by our own hover/focus tracking.
                var ring = slider.Q<VisualElement>(className: "unity-base-slider__dragger-border");
                if (ring != null)
                {
                    if (hasShape || sized)
                    {
                        float ringW = knobW + RingPad * 2f;
                        float ringH = knobH + RingPad * 2f;
                        float ringR = knobR + RingPad;
                        ring.style.width = ringW;
                        ring.style.height = ringH;
                        ring.style.borderTopLeftRadius = ringR;
                        ring.style.borderTopRightRadius = ringR;
                        ring.style.borderBottomLeftRadius = ringR;
                        ring.style.borderBottomRightRadius = ringR;
                        ring.style.top = Length.Percent(50f);
                        ring.style.marginTop = -ringH * 0.5f;
                        ring.style.marginLeft = -RingPad; // translate pins our left edge to the knob's

                        if (!slider.ClassListContains(RingFxBoundClass))
                        {
                            slider.AddToClassList(RingFxBoundClass);
                            // The ring is a FOCUS indicator, not a hover one: pointer-down focuses
                            // the slider, so it appears the moment a drag starts and stays after
                            // release — until focus moves elsewhere. Plain hovering never shows it.
                            slider.RegisterCallback<FocusInEvent>(_ => ring.style.opacity = 1f);
                            slider.RegisterCallback<FocusOutEvent>(_ => ring.style.opacity = 0f);
                            ring.style.opacity = 0f;
                        }
                    }
                    else
                    {
                        // Stock knob geometry — the theme's own grow-on-hover ring fits it; leave it be.
                        ring.style.width = StyleKeyword.Null;
                        ring.style.height = StyleKeyword.Null;
                        ring.style.borderTopLeftRadius = StyleKeyword.Null;
                        ring.style.borderTopRightRadius = StyleKeyword.Null;
                        ring.style.borderBottomLeftRadius = StyleKeyword.Null;
                        ring.style.borderBottomRightRadius = StyleKeyword.Null;
                        ring.style.top = StyleKeyword.Null;
                        ring.style.marginTop = StyleKeyword.Null;
                        ring.style.marginLeft = StyleKeyword.Null;
                        ring.style.opacity = StyleKeyword.Null;
                    }
                }

                // An oversized knob otherwise overflows the theme's 17px control; grow the control
                // so the row breathes with it (the ring pad rides along; no-op at stock sizes).
                // MUST go through the arbiter, NOT a raw style.minHeight write: ApplyNow is registered on
                // GeometryChangedEvent, so it re-runs on every layout pass and a bare write would permanently
                // erase a Rows container's authored row height (and the node's own height grip) from this same
                // element — the row would silently snap back to the knob height on the frame after any drag.
                MinHeightFloors.SetWidget(slider, (hasShape || sized) ? knobH : 0f);
            }

            ApplyNow();
            slider.RegisterCallback<GeometryChangedEvent>(_ => ApplyNow());
        }

        // ===== A DISABLED slider keeps SOLID knobs, whatever disabled it =====
        // Anything dimmed by SetEnabled(false) gets the editor theme's `.unity-disabled { opacity: .5 }`.
        // UI Toolkit composites opacity PER ELEMENT down the subtree rather than flattening it as a group,
        // so at 50% every part blends with whatever sits behind it — and a knob sits on the track. Measured
        // off a dark-skin capture: a #383838 backdrop, a knob body of 104/255, and 112/255 where the track
        // passes under it. That band across the middle is what makes a disabled knob read as hollow.
        //
        // A child cannot undo it (composite opacity is parent x own), so the dim moves OFF the subtree:
        // the dimmed root goes back to opacity 1, every part that isn't a knob fades at 0.5 on its own
        // (identical result — none of those overlap each other), and the knobs are painted opaque with
        // the exact colour the 50% composite produced. Same brightness as before, no band.
        //
        // THREE things disable a subtree, and all three produce this defect, so the pass is armed for
        // EVERY field rather than only for read-only ones (it costs one bool read on an enabled field,
        // and no display-type list has to be kept in sync — a [Range] Default field, whose slider Unity
        // builds lazily, is covered for free):
        //   1. `IsReadOnly` on the field itself;
        //   2. a Toggle Group whose bound bool is off — the disable lands on the group's CONTENT;
        //   3. a condition rule with the Enable/Disable outcome — the disable lands on the node element.
        // (2) and (3) can also flip at RUNTIME, long after build. SetEnabled changes opacity, not layout,
        // so it dispatches NO GeometryChangedEvent and UI Toolkit exposes no enabled-changed event either
        // — nothing can notice the flip on its own. Those two sites call RefreshDisabledKnobsUnder, which
        // is the single shared trigger; everything else self-arms off the geometry tick below.
        //
        // Source colours come from the MODEL, never read back off the element, so the pass is idempotent
        // and safe to re-run — which is what keeps ApplySliderLook's own re-application of a custom knob
        // colour from winning the last word.
        //
        // A background colour set on the field itself deliberately stays at full strength (it is the
        // surface the knob is composited against, so dimming both would double up); only the controls
        // inside it fade.
        internal static void KeepSliderKnobsSolidWhileDisabled(VisualElement fieldRoot, BindingNode node)
        {
            if (fieldRoot == null || node == null) return;
            _disabledKnobFields.Remove(fieldRoot);
            _disabledKnobFields.Add(fieldRoot, node);
            if (!fieldRoot.ClassListContains(DisabledKnobHookedClass))
            {
                fieldRoot.AddToClassList(DisabledKnobHookedClass);
                fieldRoot.RegisterCallback<GeometryChangedEvent>(_ => RefreshDisabledKnobs(fieldRoot, node));
            }
            RefreshDisabledKnobs(fieldRoot, node);
        }

        /// <summary>Fields whose disabled-knob pass is armed, and the node whose colours it paints with.
        /// Lets <see cref="RefreshDisabledKnobsUnder"/> re-run a field's pass without the caller holding
        /// the node — a Toggle Group or a condition only knows the subtree it just toggled.</summary>
        private static readonly ConditionalWeakTable<VisualElement, BindingNode> _disabledKnobFields =
            new ConditionalWeakTable<VisualElement, BindingNode>();

        private const string DisabledKnobHookedClass = "sasq-disabled-knob-hooked";
        /// <summary>Marks an element whose subtree dim this pass re-homed, so a restore only unwinds what
        /// it actually wrote (an unconditional clear would stomp an opacity some other writer owns).</summary>
        private const string DisabledDimHostClass = "sasq-disabled-dim-host";
        /// <summary>Marks a FIELD whose knobs this pass painted. Distinct from the host marker above: the
        /// element that hosted the dim is usually an ancestor (the Toggle Group's content, the condition's
        /// node element), and the restore has to be driven off what THIS field did.</summary>
        private const string DisabledKnobPaintedClass = "sasq-disabled-knob-painted";

        /// <summary>
        /// Re-runs the disabled-knob pass for every armed field under <paramref name="subtree"/>, and
        /// re-homes (or unwinds) the subtree's own dim. THE trigger for a disable that flips after build —
        /// a Toggle Group's bound bool or a condition's Enable/Disable outcome — because UI Toolkit fires
        /// no event when an element's enabled state changes. Safe to call when nothing underneath holds a
        /// slider: it costs one query and writes nothing.
        /// </summary>
        internal static void RefreshDisabledKnobsUnder(VisualElement subtree)
        {
            if (subtree == null) return;

            // The subtree root is where the theme's dim landed, so it is where the redistribution has to
            // happen — a field several levels down cannot undo an ancestor's opacity.
            if (!subtree.enabledSelf) RedistributeDimOffKnobs(subtree);
            else UnwindRedistributedDim(subtree);

            foreach (var field in subtree.Query<VisualElement>(className: DisabledKnobHookedClass).ToList())
            {
                BindingNode node;
                if (_disabledKnobFields.TryGetValue(field, out node)) RefreshDisabledKnobs(field, node);
            }
        }

        private static void RefreshDisabledKnobs(VisualElement fieldRoot, BindingNode node)
        {
            bool disabled = !fieldRoot.enabledInHierarchy;
            // The cheap common case: an enabled field whose knobs this pass never painted. One class check,
            // no query — this runs on every field's every geometry tick, so it must not walk the tree for
            // nothing. The marker has to live on the FIELD, not on whatever hosted the dim: when an ancestor
            // is what disabled us (a Toggle Group, a condition), the host is that ancestor, and keying the
            // early-out off the host meant an re-enabled field returned before clearing its knob colour —
            // the knob stayed stuck at the dimmed grey.
            if (!disabled && !fieldRoot.ClassListContains(DisabledKnobPaintedClass)) return;

            var knobs = CollectSliderKnobs(fieldRoot);
            // No slider in this field (or a PropertyField that hasn't built its [Range] slider yet):
            // leave the theme's own dim completely alone.
            if (knobs.Count == 0) return;

            if (!disabled)
            {
                fieldRoot.RemoveFromClassList(DisabledKnobPaintedClass);
                UnwindRedistributedDim(fieldRoot); // no-op unless this FIELD was the host (read-only)
                // Hand the knobs back to ApplySliderLook / the stylesheet; it re-runs on geometry and
                // re-applies a custom knob colour, so clearing here cannot strand a styled slider.
                foreach (var knob in knobs) knob.style.backgroundColor = StyleKeyword.Null;
                return;
            }

            // A field disabled in its own right (read-only) is where the dim landed, so redistribute here
            // too. When an ANCESTOR is what disabled it, that ancestor already did its own pass via
            // RefreshDisabledKnobsUnder and this is a no-op on the shared chain.
            if (!fieldRoot.enabledSelf) RedistributeDimOffKnobs(fieldRoot);
            fieldRoot.AddToClassList(DisabledKnobPaintedClass);

            var ws = node.DisplayStyleData;
            Color backdrop = ResolveReadOnlyBackdrop(fieldRoot);
            Color knobSrc = ws.sliderKnobColor.a > 0.001f ? ws.sliderKnobColor : WidgetPalette.SliderKnobDefault;
            Color barSrc = ws.sliderCenterBarColor.a > 0.001f ? ws.sliderCenterBarColor : WidgetPalette.SliderKnobDefault;

            foreach (var knob in knobs)
            {
                bool isCentreBar = knob.ClassListContains(MinMaxCentreBarClass);
                knob.style.backgroundColor = DimOntoBackdrop(isCentreBar ? barSrc : knobSrc, backdrop);
            }
        }

        /// <summary>Moves <paramref name="root"/>'s blanket dim onto its parts, leaving every knob's
        /// ancestor chain opaque. The knob set is collected from ROOT, not from any one field, so several
        /// sliders sharing a disabled container all compute the SAME answer — otherwise each field's pass
        /// would re-dim its neighbour's chain and the two would fight every frame.</summary>
        private static void RedistributeDimOffKnobs(VisualElement root)
        {
            var knobs = CollectSliderKnobs(root);
            if (knobs.Count == 0) return; // nothing that can go see-through: leave the theme's dim alone

            root.style.opacity = 1f;
            root.AddToClassList(DisabledDimHostClass);

            // Every ancestor of a knob, up to and including the root. These must stay opaque so nothing
            // composites through a knob; they are layout containers (plus the min-max centre bar, which
            // is painted like the knobs it parents).
            var onPath = new HashSet<VisualElement>();
            foreach (var knob in knobs)
            {
                for (var e = knob; e != null; e = e.parent)
                {
                    if (!onPath.Add(e)) break; // already covered by another knob's chain
                    if (e == root) break;
                }
            }

            DimOffPath(root, onPath);
        }

        /// <summary>Undoes <see cref="RedistributeDimOffKnobs"/> on <paramref name="root"/> — and only if it
        /// was this pass that dimmed it.</summary>
        private static void UnwindRedistributedDim(VisualElement root)
        {
            if (!root.ClassListContains(DisabledDimHostClass)) return;
            root.RemoveFromClassList(DisabledDimHostClass);
            root.style.opacity = StyleKeyword.Null;
            ClearSubtreeOpacity(root);
        }

        // ===== The same cure, for a row dimmed by CHROME rather than by read-only =====
        // The designer's Properties panel greys a style row it considers "not overridden" with a plain
        // opacity: .5 on the field. That is the identical per-element composite trap described above — the
        // track reads through the knob and the control looks hollow — and it bit every slider row in the
        // panel (Badge Height / Badge Spacing / Progress Bar Height / Rating Size / …), which is most of
        // them. Nothing here has a BindingNode behind it, so the knob colour comes from the palette.
        //
        // Rows with no slider under them keep the plain root opacity: the cheap path, and the one nearly
        // every row takes. The dim state rides on a CLASS so the geometry re-run below reads the same
        // answer without a closure, and re-entry (the panel re-syncs this on every commit) can neither
        // stack callbacks nor lose track of what it already wrote.
        private const string SolidKnobDimClass = "sasq-solid-knob-dim";
        private const string SolidKnobDimHookedClass = "sasq-solid-knob-dim-hooked";
        private const string SolidKnobDimAppliedClass = "sasq-solid-knob-dim-applied";

        /// <summary>Dims <paramref name="root"/> to half strength (or restores it) the way a plain
        /// <c>opacity</c> write would, except that any slider knob inside it stays SOLID.</summary>
        internal static void ApplyDimWithSolidKnobs(VisualElement root, bool dimmed)
        {
            if (root == null) return;
            root.EnableInClassList(SolidKnobDimClass, dimmed);
            if (!root.ClassListContains(SolidKnobDimHookedClass))
            {
                root.AddToClassList(SolidKnobDimHookedClass);
                // The knob is painted the colour the 50% composite produced, which needs the surface
                // BEHIND it — and that only resolves once the row has been laid out. Re-running is free:
                // every source colour is a constant, never read back off the element, so the pass is
                // idempotent no matter how often geometry fires.
                root.RegisterCallback<GeometryChangedEvent>(_ => ApplyDimWithSolidKnobsNow(root));
            }
            ApplyDimWithSolidKnobsNow(root);
        }

        private static void ApplyDimWithSolidKnobsNow(VisualElement root)
        {
            bool dimmed = root.ClassListContains(SolidKnobDimClass);
            var knobs = CollectSliderKnobs(root);
            if (knobs.Count == 0)
            {
                // No slider under here (a popup, a toggle, a colour swatch): the plain dim is exactly
                // right and costs one write. A row whose slider is built LATER still lands in the branch
                // below on the geometry pass that builds it.
                root.style.opacity = dimmed ? 0.5f : 1f;
                return;
            }

            root.style.opacity = 1f;

            if (!dimmed)
            {
                // Only unwind a subtree this pass actually dimmed — an unconditional clear would stomp an
                // opacity some other writer owns.
                if (!root.ClassListContains(SolidKnobDimAppliedClass)) return;
                root.RemoveFromClassList(SolidKnobDimAppliedClass);
                ClearSubtreeOpacity(root);
                foreach (var knob in knobs) knob.style.backgroundColor = StyleKeyword.Null;
                return;
            }

            root.AddToClassList(SolidKnobDimAppliedClass);

            var onPath = new HashSet<VisualElement>();
            foreach (var knob in knobs)
            {
                for (var e = knob; e != null; e = e.parent)
                {
                    if (!onPath.Add(e)) break;
                    if (e == root) break;
                }
            }

            DimOffPath(root, onPath);

            // One source colour for every knob AND the min-max centre bar: chrome rows are the designer's
            // own controls, so none of them carries a user knob colour the way a bound widget can.
            Color backdrop = ResolveReadOnlyBackdrop(root);
            Color knobColor = DimOntoBackdrop(WidgetPalette.SliderKnobDefault, backdrop);
            foreach (var knob in knobs) knob.style.backgroundColor = knobColor;
        }

        // Undo of DimOffPath: hands every part back to its stylesheet so nothing stays stuck at half
        // strength once the row is no longer dimmed. Mirrors DimOffPath's traversal exactly, or a part it
        // reached would be one this never clears.
        private static void ClearSubtreeOpacity(VisualElement element)
        {
            for (int i = 0; i < element.childCount; i++)
            {
                var child = element[i];
                child.style.opacity = StyleKeyword.Null;
                ClearSubtreeOpacity(child);
            }
        }

        private const string MinMaxCentreBarClass = "unity-min-max-slider__dragger";

        // The parts that must stay opaque: the single-knob dragger, and the min-max thumbs plus the centre
        // bar that parents them. `unity-slider__dragger` is queried alongside the base class exactly as
        // ApplySliderLook does, so a version that renames the part can't quietly drop out.
        private static readonly string[] SliderKnobClasses =
        {
            "unity-base-slider__dragger",
            "unity-slider__dragger",
            MinMaxCentreBarClass,
            "unity-min-max-slider__min-thumb",
            "unity-min-max-slider__max-thumb",
        };

        private static List<VisualElement> CollectSliderKnobs(VisualElement root)
        {
            var knobs = new List<VisualElement>();
            foreach (var cls in SliderKnobClasses)
            {
                foreach (var el in root.Query<VisualElement>(className: cls).ToList())
                {
                    if (!knobs.Contains(el)) knobs.Add(el); // the two dragger class names can hit the same element
                }
            }
            return knobs;
        }

        // Fades everything that is NOT on a knob's chain, one element at a time. Opacity is re-asserted on
        // the chain itself so a part that moves between runs can't stay stuck at half strength.
        private static void DimOffPath(VisualElement element, HashSet<VisualElement> onPath)
        {
            for (int i = 0; i < element.childCount; i++)
            {
                var child = element[i];
                if (onPath.Contains(child))
                {
                    child.style.opacity = 1f;
                    DimOffPath(child, onPath);
                }
                else
                {
                    // Whole subtree, in one write — a knob's own children (the Grip glyph) land here too,
                    // and 50% over the now-opaque knob is exactly what the theme's dim used to give them.
                    child.style.opacity = 0.5f;
                }
            }
        }

        // What a part LOOKS like under the theme's 50% dim, painted as an opaque colour so nothing behind
        // it can read through. Internal: the verification suite asserts against this exact derivation.
        internal static Color DimOntoBackdrop(Color c, Color backdrop)
        {
            var dimmed = Color.Lerp(backdrop, c, 0.5f * Mathf.Clamp01(c.a));
            dimmed.a = 1f;
            return dimmed;
        }

        // The nearest opaque surface the knob actually sits on — a container the user gave a background
        // wins over the palette guess. Near-transparent washes (row cards sit at 4.5% alpha) are skipped:
        // they shift the backdrop by less than a level, so treating them as the surface would be wrong.
        private static Color ResolveReadOnlyBackdrop(VisualElement from)
        {
            for (var e = from; e != null; e = e.parent)
            {
                var bg = e.resolvedStyle.backgroundColor;
                if (bg.a >= 0.95f) return bg;
            }
            return WidgetPalette.ReadOnlyBackdrop;
        }

        // Optionally adds an editable value field around the slider per the node's placement. Right uses Unity's
        // built-in inline input field; Left/Above/Below build a separate field bound to the same property and
        // arrange it around the slider. No field when sliderShowField is off (Unity's plain slider).
        private static VisualElement WrapSliderField(VisualElement sliderElement, SerializedProperty prop, BindingNode node, bool isInt, bool isTableCell = false)
        {
            var ws = node.DisplayStyleData;
            if (ws.sliderHideField) return sliderElement; // hidden only when explicitly turned off (default: shown)
                                                          // (no value shown → the unit suffix is skipped too)

            // Wide numeric fields need an exact companion: a 32-bit built-in input (or IntegerField/FloatField)
            // would truncate a long/ulong or double. LongField/DoubleField bind via longValue/doubleValue, so
            // the typed value stays exact (the slider drag itself is inherently float-resolution).
            bool wideInt = isInt && !FitsInt32(prop);
            bool isDouble = !isInt && IsDoubleProperty(prop);
            bool needsExact = wideInt || isDouble;

            if (ws.sliderFieldPlacement == SliderFieldPlacement.Right && !needsExact)
            {
                if (sliderElement is Slider s) s.showInputField = true;
                else if (sliderElement is SliderInt si) si.showInputField = true;

                // The read-out is Unity's inline text field, built by showInputField above. Hand it to the
                // decorator FIRST: an "Inside field" affix overlays that box and comes back null, leaving
                // nothing to place. Only an outside affix still needs a slot in the row.
                var input = sliderElement.Q(className: "unity-base-field__input") ?? sliderElement;
                var inlineBox = input.Q(className: "unity-base-text-field");
                // `input` doubles as the demote landing site. It is the slider's own base-field input row, it
                // holds the flexible track, and it is exactly where an OUTSIDE affix goes below — so a unit too
                // long for the inline field lands beside the number instead of inside a box Unity pins to 50px
                // and paints past. See FieldAffixes.PairRowClass.
                var prefixOut = FieldAffixes.MountOnReadout(inlineBox, FieldAffixes.PrefixFor(node, isTableCell), true, input);
                var suffixOut = FieldAffixes.MountOnReadout(inlineBox, FieldAffixes.SuffixFor(node, isTableCell), false, input);
                if (prefixOut != null || suffixOut != null)
                {
                    // Mount INSIDE the BaseField's input container (around the inline text field) so the
                    // aligned label-column metrics stay untouched — no wrapper element. The prefix has to
                    // land just before the NUMBER, not before the track, so it is anchored to the inline
                    // text field rather than inserted at index 0.
                    if (prefixOut != null) FieldAffixes.InsertBefore(input, inlineBox, prefixOut);
                    if (suffixOut != null) input.Add(suffixOut);
                }
                return sliderElement;
            }

            // Each read-out is seeded synchronously for the same reason the slider itself is (see
            // SeedSliderValue): bound alone it reads "0" until the binding tick lands, so the number flashes
            // zero beside a knob that is already in the right place.
            BindableElement valueField;
            if (isInt)
            {
                if (wideInt) { var f = new LongField(); SeedThenBind(f, prop); valueField = f; }
                else { var f = new IntegerField(); SeedThenBind(f, prop); valueField = f; }
            }
            else
            {
                if (isDouble) { var f = new DoubleField(); SeedThenBind(f, prop); valueField = f; }
                else { var f = new FloatField(); SeedThenBind(f, prop); valueField = f; }
            }
            valueField.style.flexShrink = 0;

            bool vertical = ws.sliderFieldPlacement == SliderFieldPlacement.Above
                         || ws.sliderFieldPlacement == SliderFieldPlacement.Below;
            valueField.style.width = vertical ? 80 : 56;

            // Pair the value field with its affixes in a mini row so they hug the number in every placement
            // (the row stands in for the bare field below).
            //
            // Built BEFORE the mounts, and whenever either side has CONTENT — not just when a mount hands
            // something back. An "Inside field" affix is overlaid on the box and returns null, so this row
            // stays empty around it; but the box is pinned to 56px, and a unit long enough to run out of room
            // there has to demote somewhere that can widen. This row is that somewhere.
            var preAffix = FieldAffixes.PrefixFor(node, isTableCell);
            var sufAffix = FieldAffixes.SuffixFor(node, isTableCell);
            VisualElement fieldWithSuffix = valueField;
            VisualElement pairRow = null;
            if (!preAffix.IsEmpty || !sufAffix.IsEmpty)
            {
                pairRow = FieldAffixes.NewPairRow();
                pairRow.Add(valueField);
                fieldWithSuffix = pairRow;
            }

            var prefix = FieldAffixes.MountOnReadout(valueField, preAffix, true, pairRow);
            var suffix = FieldAffixes.MountOnReadout(valueField, sufAffix, false, pairRow);
            if (prefix != null) pairRow.Insert(0, prefix);
            if (suffix != null) pairRow.Add(suffix);

            // LEFT: sit the field INSIDE the slider's own base-field, just right of its (aligned) label but left
            // of the track — so nothing ever lands left of the label. The base-field's input keeps flex-growing
            // to fill the rest, so the track's tracker/dragger internals are never touched.
            if (ws.sliderFieldPlacement == SliderFieldPlacement.Left)
            {
                var label = sliderElement.Q<Label>(className: "unity-base-field__label");
                int labelIdx = label != null ? sliderElement.IndexOf(label) : -1;
                fieldWithSuffix.style.marginRight = 6;
                fieldWithSuffix.style.alignSelf = Align.Center;
                sliderElement.Insert(labelIdx >= 0 ? labelIdx + 1 : 0, fieldWithSuffix);
                return sliderElement;
            }

            var container = new VisualElement();
            container.style.flexDirection = vertical ? FlexDirection.Column : FlexDirection.Row;
            if (vertical)
            {
                // Slider full-width on its own line; the field above/below it, aligned per sliderFieldAlignment.
                fieldWithSuffix.style.alignSelf = ws.sliderFieldAlignment switch
                {
                    SliderFieldAlignment.Left => Align.FlexStart,
                    SliderFieldAlignment.Right => Align.FlexEnd,
                    _ => Align.Center,
                };
                sliderElement.style.alignSelf = Align.Stretch;
            }
            else
            {
                // Field and slider side by side; the slider takes the remaining width.
                container.style.alignItems = Align.Center;
                sliderElement.style.flexGrow = 1;
            }

            // Only Above prepends now (Left returned early above; Right/Below append).
            bool fieldFirst = ws.sliderFieldPlacement == SliderFieldPlacement.Above;
            if (fieldFirst) { container.Add(fieldWithSuffix); container.Add(sliderElement); }
            else { container.Add(sliderElement); container.Add(fieldWithSuffix); }
            return container;
        }

    }
}
