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
        /// <summary>Vertical margin on a Modern Checkbox's box, top and bottom. 16px box + 2 + 2 = the same
        /// 20px row every other toggle style reserves, and the gap that keeps stacked checkboxes from
        /// touching. Mirrored by <c>SasqKit.StyleModernCheckbox</c> in the export kit.</summary>
        internal const float ModernCheckboxRowInset = 2f;

        /// <summary>
        /// The ink for a switch's ON/OFF overlay word. It rides ON TOP of the track fill, so the hard-coded
        /// white the stylesheet used to give it vanished on a light custom fill (yellow, pastels) — the exact
        /// failure the progress bar's readout already solves, so this uses the same helper at the same
        /// threshold and the two widgets flip together.
        /// <para>Returns <c>false</c> when the node authors its own ON/OFF colour, and the caller must then
        /// write NOTHING: <c>LayoutTreeBuilder</c> applies the authored one on a later scheduled tick, so an
        /// inline write here would race it.</para>
        /// <para>ONE derivation, deliberately static and node-driven: the live widget calls it, and so does
        /// the designer's colour-drag poke, which is the only way a Fill Color drag can preview the ink it is
        /// actually going to commit. It reads <see cref="BindingNode.DisplayStyleData"/> LIVE rather than a
        /// captured copy — that struct is copied by value into every widget builder, so a captured one goes
        /// stale the instant the designer edits a colour and the next geometry tick would reassert it over
        /// the poke (the same rule the slider's hover callbacks follow).</para>
        /// </summary>
        internal static bool TryResolveToggleOnOffInk(BindingNode node, bool isOn, bool isMixed, out Color ink)
        {
            ink = default;
            if (node == null || node.ToggleOnOffTextStyle.color.isOverridden) return false;
            if (isMixed)
            {
                ink = ContrastingReadoutColor(WidgetPalette.Mixed);
                return true;
            }
            Color bg = ResolveToggleOnOffBackdrop(node, isOn);
            if (isOn)
            {
                ink = ContrastingReadoutColor(bg);
                return true;
            }
            // OFF keeps its deliberately MUTED theme grey — that state is meant to recede, and full-contrast
            // ink would make it read as loud as ON. Auto-contrast steps in only where the grey actually
            // fails, which is a bright authored off-fill.
            Color faint = WidgetPalette.TextFaint;
            ink = ReadoutContrast(faint, bg) >= MinReadoutContrast ? faint : ContrastingReadoutColor(bg);
            return true;
        }

        /// <summary>
        /// The colour a switch's ON/OFF word actually sits on in a given state — the track fill, resolved
        /// through the same precedence the track itself uses (explicit → accent → house default). Split out
        /// because the DESIGNER needs it too: its contrast warning has to judge an AUTHORED ink against this
        /// exact backdrop, and a second copy of the precedence there would be a second thing to drift.
        /// </summary>
        internal static Color ResolveToggleOnOffBackdrop(BindingNode node, bool isOn)
        {
            var s = node.DisplayStyleData;
            // A translucent authored fill composites over the control surface before it is judged — the same
            // approximation the enum badge's tint makes.
            static Color Opaque(Color c) => c.a >= 0.999f ? c : Color.Lerp(WidgetPalette.ControlBg, c, c.a);

            if (isOn)
            {
                Color fillOn = s.toggleFillColor.a > 0.001f ? s.toggleFillColor
                             : s.accent.isOverridden ? s.accent.value
                             : WidgetPalette.ToggleOn;
                return Opaque(fillOn);
            }
            return Opaque(s.toggleFillColorOff.a > 0.001f ? s.toggleFillColorOff : WidgetPalette.ControlBg);
        }

        /// <summary>
        /// Re-inks the ON/OFF overlay inside an ALREADY-BUILT switch — the designer's live poke, so dragging
        /// Fill Color recolours the word on every tick instead of only at the debounced commit. A no-op on a
        /// checkbox style (no overlay), and on a node that authors its own colour.
        /// </summary>
        internal static void PokeToggleOnOffInk(VisualElement built, BindingNode node, bool isMixed = false)
        {
            if (built == null) return;
            var label = built.Q<Label>(className: "sq-toggle-onoff-label");
            if (label == null) return;
            // `.Q()` never matches the root, and the designer hands back the design-card WRAPPER in Design
            // mode but the element ITSELF in Live mode — so both forms have to be tried.
            var toggle = built as Toggle ?? built.Q<Toggle>();
            if (!TryResolveToggleOnOffInk(node, toggle != null && toggle.value, isMixed, out var ink)) return;
            label.style.color = ink;
        }

        private static VisualElement CreateToggle(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            // Toggle binds a bool; if the bound property isn't one (e.g. the field's type changed under a
            // saved layout still carrying this widget kind), fall back to a plain field rather than binding a
            // mismatched type — matching the guards on the object-reference widgets.
            if (prop.propertyType != SerializedPropertyType.Boolean)
                return new PropertyField(prop) { label = resolvedLabel };

            var toggle = new Toggle();
            toggle.label = resolvedLabel;

            // Seed the checked state SYNCHRONOUSLY. BindProperty applies the serialized value on a LATER
            // binding-update tick, not now — so without this the toggle is built in its default OFF state
            // (unchecked; grey track, knob at the left) and only flips ON when that tick lands. For the custom
            // switch styles the knob has a 0.15s slide on `left`, and the only thing masking that off→on intro
            // is the brief `sq-toggle--instant` window below. When a burst of full-canvas rebuilds floods the
            // update loop — e.g. mashing a select-only field card with Interact OFF, where every click re-runs
            // RefreshPreview() and rebuilds this toggle — the binding tick slips past that window and the knob
            // visibly slides even though the bound bool never changes. Seeding the value here makes the track and
            // knob render in the correct position from frame 0, independent of binding timing or rebuild churn.
            // Skipped for a mixed multi-selection, where MixedValueUI paints the muted "—" state instead.
            // Seeded BEFORE the bind — a bool can't be clamped, but SeedThenBind is the one approved pairing so
            // no widget has to reason about whether its own control coerces what it is handed.
            SeedThenBind(toggle, prop);

            if (toggle.labelElement != null)
            {
                toggle.labelElement.style.display = DisplayStyle.Flex;
                toggle.labelElement.style.color = WidgetPalette.LabelPrimary;
            }

            var style = node.DisplayStyleData;

            // Size: scale the whole switch/box (the input sub-element, built lazily, so re-apply on geometry).
            if (style.toggleSize != SliderSize.Normal)
            {
                float sf = style.toggleSize == SliderSize.ExtraLarge ? 1.5f : 1.25f;
                void ApplyToggleSize()
                {
                    var input = toggle.Q<VisualElement>(className: "unity-toggle__input");
                    if (input == null) return;
                    input.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(50f));
                    input.style.scale = new StyleScale(new Scale(new Vector2(sf, sf)));
                    // A CSS `scale` enlarges the switch VISUALLY but leaves its 20px layout box unchanged, so the
                    // extra height (scaling around the vertical center) spills past the row and is clipped top &
                    // bottom. Reserve that space with matching vertical margins so the row grows to the switch's
                    // true height and nothing is cut. (Same margin trick the .sq-toggle--slim variant already uses;
                    // slim keeps its own 5px base ON TOP of the scale overflow since inline margins replace the USS.)
                    bool slim = style.toggleStyle == ToggleStyle.Slim;
                    float pad = 10f * (sf - 1f) + (slim ? 5f : 0f);
                    input.style.marginTop = pad;
                    input.style.marginBottom = pad;
                }
                ApplyToggleSize();
                toggle.RegisterCallback<GeometryChangedEvent>(_ => ApplyToggleSize());
            }

            if (style.toggleStyle == ToggleStyle.ModernCheckbox)
            {
                // Vector-crisp checkbox: the stock tick is a tiny texture that blurs when scaled, so
                // this restyles the SAME bound Toggle — the checkmark element becomes the box itself
                // (fill/border/radius are vector geometry) and the tick is the shared Lucide check
                // (Painter2D). Everything stays sharp under the Size scale and on any DPI.
                toggle.AddToClassList("sasq-modern-checkbox");
                var box = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
                if (box != null)
                {
                    box.style.backgroundImage = StyleKeyword.None; // drop the bitmap tick
                    box.style.width = 16f;
                    box.style.height = 16f;
                    // The box is a SOLID filled block once checked, and it is the tallest thing in its row,
                    // so with no vertical margin the row measures exactly 16px and the box fills 100% of it.
                    // Stack five of them and the fills touch edge to edge — the checkboxes read as one
                    // continuous blue bar rather than five controls (reported 2026-08-21). Every OTHER toggle
                    // style already reserves a 20px row (.sq-toggle's 20px track; slim's 5+10+5), so these
                    // 2px margins both restore the gap AND put the Modern Checkbox on the same row rhythm as
                    // its siblings. It has to be a margin, not a taller box: the Inspector's own stylesheet
                    // zeroes the base-field margins a plain EditorWindow leaves at 1/3px, so the row's gap
                    // cannot be borrowed from the host.
                    box.style.marginTop = ModernCheckboxRowInset;
                    box.style.marginBottom = ModernCheckboxRowInset;
                    box.style.alignItems = Align.Center;
                    box.style.justifyContent = Justify.Center;
                    float r = style.cornerRadius.isOverridden ? Mathf.Max(0f, style.cornerRadius.value) : 4f;
                    box.style.borderTopLeftRadius = r; box.style.borderTopRightRadius = r;
                    box.style.borderBottomLeftRadius = r; box.style.borderBottomRightRadius = r;
                    box.style.borderTopWidth = 1.5f; box.style.borderBottomWidth = 1.5f;
                    box.style.borderLeftWidth = 1.5f; box.style.borderRightWidth = 1.5f;

                    var tick = SasquatchIcons.Create(SasquatchIcons.Ids.Check, 11f, Color.white);
                    tick.StrokeWidth = 3f; // the 2-unit default reads thin inside an 11px filled box
                    tick.AddToClassList("sasq-modern-checkbox__tick");
                    box.Add(tick);

                    bool hovered = false;
                    void ApplyModernCheckbox()
                    {
                        // Precedence mirrors the switch styles: explicit color → accent → house default.
                        Color fillOn = style.toggleFillColor.a > 0.001f ? style.toggleFillColor
                                     : style.accent.isOverridden ? style.accent.value
                                     : WidgetPalette.ProgressFill;
                        if (MixedValueUI.IsMixed(prop))
                        {
                            box.style.backgroundColor = WidgetPalette.Mixed;
                            SetBorders(box, WidgetPalette.MixedBorder);
                            tick.style.display = DisplayStyle.None;
                            return;
                        }
                        if (toggle.value)
                        {
                            Color fill = hovered ? Color.Lerp(fillOn, Color.white, 0.08f) : fillOn;
                            box.style.backgroundColor = fill;
                            SetBorders(box, style.toggleOutlineColor.a > 0.001f ? style.toggleOutlineColor : fill);
                            // Auto-contrast keeps the tick readable over any custom fill (pale fills flip it dark),
                            // and HOVER FLIPS IT to the opposite pole so the hover always reads. It has to be a flip:
                            // the fill nudge alone is invisible on a pale fill, because the tick there is ALREADY the
                            // dark end and 8% toward white moves neither it nor the box anywhere the eye can see.
                            // Both ends are derived from the RESTING fill, never the hovered one — a fill sitting on
                            // the contrast threshold would otherwise auto-flip and then flip back, cancelling out.
                            Color restTick = style.toggleKnobColor.a > 0.001f
                                ? style.toggleKnobColor
                                : ContrastingReadoutColor(fillOn);
                            tick.Tint = hovered ? OppositeReadoutColor(restTick) : restTick;
                            tick.style.display = DisplayStyle.Flex;
                        }
                        else
                        {
                            box.style.backgroundColor = style.toggleFillColorOff.a > 0.001f
                                ? (StyleColor)style.toggleFillColorOff
                                : new StyleColor(StyleKeyword.Null);
                            // Same rule for the empty box: an AUTHORED outline colour used to sit out the hover
                            // entirely (the brighten was gated on there being no override), so a styled checkbox
                            // had no off-state hover at all. It now shifts toward whichever pole it isn't.
                            bool authoredOutline = style.toggleOutlineColor.a > 0.001f;
                            Color outline = authoredOutline ? style.toggleOutlineColor : WidgetPalette.Ink(0.4f);
                            Color outlineHover = authoredOutline
                                ? Color.Lerp(outline, OppositeReadoutColor(outline), 0.45f)
                                : WidgetPalette.Ink(0.65f);
                            SetBorders(box, hovered ? outlineHover : outline);
                            tick.style.display = DisplayStyle.None;
                        }
                    }

                    ApplyModernCheckbox();
                    // The check POPS in: the tick springs up from tiny with a bouncy overshoot while
                    // the box gives a small "thunk" punch. Armed after a short window (the
                    // sq-toggle--instant precedent) so panel rebuilds and binding churn never replay
                    // the intro; unchecking stays instant — crisp off, playful on.
                    bool popArmed = false;
                    toggle.schedule.Execute(() => popArmed = true).ExecuteLater(60);
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        ApplyModernCheckbox();
                        if (popArmed && evt.newValue && EditorSpringFX.MotionEnabled)
                        {
                            EditorSpringFX.PopIn(tick, 0.2f, 0.6f);
                            EditorSpringFX.LandPunch(box, 0.4f);
                        }
                    });
                    // Hover reads live state (the designer-wide convention), so leaving always
                    // restores exactly what the current style dictates.
                    toggle.RegisterCallback<MouseEnterEvent>(_ => { hovered = true; ApplyModernCheckbox(); });
                    toggle.RegisterCallback<MouseLeaveEvent>(_ => { hovered = false; ApplyModernCheckbox(); });
                    // Multi-object mixed state resolves on a later binding tick; idempotent re-apply.
                    toggle.RegisterCallback<GeometryChangedEvent>(_ => ApplyModernCheckbox());
                }
            }
            else if (style.toggleStyle != ToggleStyle.Checkbox)
            {
                toggle.AddToClassList("sq-toggle");

                // Suppress the on/off slide transition for the first frame so a full-panel
                // rebuild (which re-creates this toggle) doesn't replay the intro animation.
                // Genuine user clicks happen after this window, so they still animate.
                toggle.AddToClassList("sq-toggle--instant");
                toggle.schedule.Execute(() => toggle.RemoveFromClassList("sq-toggle--instant")).ExecuteLater(60);

                // The knob is THROWN across rather than eased over: it overshoots the far end by a few px,
                // rocks back and pops as it lands. EditorSpringFX owns the whole handover, including taking
                // `left` off the transition list so the throw has a real jump to measure — which is also why
                // motion OFF now SNAPS the knob rather than falling back to the 0.15s lerp, matching what
                // "Enable Spring Animations" promises everywhere else. Wired here rather than in USS because
                // a spring is not a transition: it carries velocity across a re-target, so mashing the switch
                // continues the motion instead of restarting it from scratch.
                EditorSpringFX.SpringToggleKnob(toggle);

                if (style.toggleStyle == ToggleStyle.Rounded)
                {
                    toggle.AddToClassList("sq-toggle--rounded");
                }
                else if (style.toggleStyle == ToggleStyle.Square)
                {
                    toggle.AddToClassList("sq-toggle--square");
                }
                else if (style.toggleStyle == ToggleStyle.Slim)
                {
                    toggle.AddToClassList("sq-toggle--slim");
                }

                if (style.cornerRadius.isOverridden)
                {
                    var track = toggle.Q<VisualElement>(className: "unity-toggle__input");
                    if (track != null)
                    {
                        track.style.borderTopLeftRadius = style.cornerRadius.value;
                        track.style.borderTopRightRadius = style.cornerRadius.value;
                        track.style.borderBottomLeftRadius = style.cornerRadius.value;
                        track.style.borderBottomRightRadius = style.cornerRadius.value;
                    }
                    var knob = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
                    if (knob != null)
                    {
                        knob.style.borderTopLeftRadius = style.cornerRadius.value;
                        knob.style.borderTopRightRadius = style.cornerRadius.value;
                        knob.style.borderBottomLeftRadius = style.cornerRadius.value;
                        knob.style.borderBottomRightRadius = style.cornerRadius.value;
                    }
                }

                // Assigned by the ON/OFF overlay block further down (it needs the built track to parent to);
                // null until then, and null for good on every switch with the overlay off. ApplySwitchColors
                // owns its ink so the word re-derives its contrast on the same three triggers the track does —
                // build, value change, and the geometry tick where a mixed multi-selection finally resolves.
                Label onOffLabel = null;

                void ApplyOnOffInk()
                {
                    if (onOffLabel == null) return;
                    if (!TryResolveToggleOnOffInk(node, toggle.value, MixedValueUI.IsMixed(prop), out var ink)) return;
                    onOffLabel.style.color = ink;
                }

                // Apply switch colors dynamically
                void ApplySwitchColors()
                {
                    var track = toggle.Q<VisualElement>(className: "unity-toggle__input");
                    var knob = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
                    ApplyOnOffInk();

                    if (MixedValueUI.IsMixed(prop))
                    {
                        // Multi-object mixed: muted track, no confident ON/OFF fill.
                        if (track != null)
                        {
                            track.style.backgroundColor = WidgetPalette.Mixed;
                            track.style.borderTopColor = WidgetPalette.MixedBorder;
                            track.style.borderBottomColor = WidgetPalette.MixedBorder;
                            track.style.borderLeftColor = WidgetPalette.MixedBorder;
                            track.style.borderRightColor = WidgetPalette.MixedBorder;
                        }
                        return;
                    }

                    if (track != null)
                    {
                        if (toggle.value)
                        {
                            if (style.toggleFillColor.a > 0.001f)
                            {
                                track.style.backgroundColor = style.toggleFillColor;
                            }
                            else if (style.accent.isOverridden)
                            {
                                track.style.backgroundColor = style.accent.value;
                            }
                            else
                            {
                                track.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                            }
                        }
                        else
                        {
                            if (style.toggleFillColorOff.a > 0.001f)
                            {
                                track.style.backgroundColor = style.toggleFillColorOff;
                            }
                            else
                            {
                                track.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                            }
                        }

                        if (style.toggleOutlineColor.a > 0.001f)
                        {
                            track.style.borderTopColor = style.toggleOutlineColor;
                            track.style.borderBottomColor = style.toggleOutlineColor;
                            track.style.borderLeftColor = style.toggleOutlineColor;
                            track.style.borderRightColor = style.toggleOutlineColor;
                            track.style.borderTopWidth = 1;
                            track.style.borderBottomWidth = 1;
                            track.style.borderLeftWidth = 1;
                            track.style.borderRightWidth = 1;
                        }
                    }

                    if (knob != null)
                    {
                        if (style.toggleKnobColor.a > 0.001f)
                        {
                            knob.style.backgroundColor = style.toggleKnobColor;
                        }
                    }
                }

                ApplySwitchColors();
                toggle.RegisterCallback<GeometryChangedEvent>(_ => ApplySwitchColors());

                toggle.RegisterValueChangedCallback(evt =>
                {
                    ApplySwitchColors();
                });

                // Dynamic ON/OFF text overlay
                if (style.toggleShowOnOff)
                {
                    var input = toggle.Q<VisualElement>(className: "unity-toggle__input");
                    if (input != null)
                    {
                        onOffLabel = new Label(MixedValueUI.IsMixed(prop) ? MixedValueUI.Dash : (toggle.value ? "ON" : "OFF"));
                        onOffLabel.AddToClassList("sq-toggle-onoff-label");
                        onOffLabel.pickingMode = PickingMode.Ignore; // click-through: taps hit the toggle, not the overlay text
                        input.Add(onOffLabel);
                        // The label did not exist for the ApplySwitchColors call above — ink it now.
                        ApplyOnOffInk();

                        void ApplyOnOffTextPosition(bool isChecked)
                        {
                            var textStyle = node.ToggleOnOffTextStyle;
                            if (textStyle.align.isOverridden)
                            {
                                switch (textStyle.align.value)
                                {
                                    case TextAnchor.MiddleLeft:
                                    case TextAnchor.UpperLeft:
                                    case TextAnchor.LowerLeft:
                                        onOffLabel.style.left = 6;
                                        onOffLabel.style.right = StyleKeyword.Auto;
                                        onOffLabel.style.translate = new StyleTranslate(new Translate(0, Length.Percent(-50), 0));
                                        break;
                                    case TextAnchor.MiddleRight:
                                    case TextAnchor.UpperRight:
                                    case TextAnchor.LowerRight:
                                        onOffLabel.style.right = 6;
                                        onOffLabel.style.left = StyleKeyword.Auto;
                                        onOffLabel.style.translate = new StyleTranslate(new Translate(0, Length.Percent(-50), 0));
                                        break;
                                    case TextAnchor.MiddleCenter:
                                    case TextAnchor.UpperCenter:
                                    case TextAnchor.LowerCenter:
                                    default:
                                        onOffLabel.style.left = Length.Percent(50);
                                        onOffLabel.style.right = StyleKeyword.Auto;
                                        onOffLabel.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), Length.Percent(-50), 0));
                                        break;
                                }
                            }
                            else
                            {
                                // Default flipping behavior
                                onOffLabel.style.left = StyleKeyword.Null;
                                onOffLabel.style.right = StyleKeyword.Null;
                                onOffLabel.style.translate = StyleKeyword.Null;
                            }
                        }

                        ApplyOnOffTextPosition(toggle.value);

                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            onOffLabel.text = evt.newValue ? "ON" : "OFF";
                            ApplyOnOffTextPosition(evt.newValue);
                        });
                    }
                }
            }
            else
            {
                // Standard Checkbox styling
                void ApplyCheckboxStyles()
                {
                    var checkmark = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
                    if (checkmark != null)
                    {
                        if (style.toggleOutlineColor.a > 0.001f)
                        {
                            checkmark.style.borderTopColor = style.toggleOutlineColor;
                            checkmark.style.borderBottomColor = style.toggleOutlineColor;
                            checkmark.style.borderLeftColor = style.toggleOutlineColor;
                            checkmark.style.borderRightColor = style.toggleOutlineColor;
                            // Make sure border width is set so the outline color shows
                            checkmark.style.borderTopWidth = 1;
                            checkmark.style.borderBottomWidth = 1;
                            checkmark.style.borderLeftWidth = 1;
                            checkmark.style.borderRightWidth = 1;
                        }
                        if (style.toggleKnobColor.a > 0.001f)
                        {
                            checkmark.style.unityBackgroundImageTintColor = style.toggleKnobColor;
                        }
                    }
                }

                ApplyCheckboxStyles();
                toggle.RegisterCallback<GeometryChangedEvent>(_ => ApplyCheckboxStyles());
            }

            return toggle;
        }

        private static void SetBorders(VisualElement el, Color c)
        {
            el.style.borderTopColor = c; el.style.borderBottomColor = c;
            el.style.borderLeftColor = c; el.style.borderRightColor = c;
        }

    }
}
