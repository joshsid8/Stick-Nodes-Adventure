using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Rating widget: draws a small whole-number field as a clickable row of pips (stars / hearts / dots).
    /// Clicking the Nth pip sets the value to N; clicking the current top pip clears it back one (so the max
    /// pip toggles the score off). Reads/writes through <see cref="BindingFactory.ReadNumeric"/> /
    /// <see cref="BindingFactory.WriteNumeric"/>. Falls back to a plain field for any non-integer property
    /// (fuzzer-safe — the LayoutFuzzer iterates every DisplayType against mismatched field types).
    /// </summary>
    public static partial class BindingFactory
    {
        private struct RatingGlyphPair { public string Filled; public string Hollow; }

        private static RatingGlyphPair RatingGlyphs(RatingIconStyle style)
        {
            switch (style)
            {
                case RatingIconStyle.Hearts: return new RatingGlyphPair { Filled = "♥", Hollow = "♡" };
                case RatingIconStyle.Dots:   return new RatingGlyphPair { Filled = "●", Hollow = "○" };
                default:                     return new RatingGlyphPair { Filled = "★", Hollow = "☆" };
            }
        }

        private static VisualElement CreateRating(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.Integer)
                return new PropertyField(prop) { label = resolvedLabel };

            int count = Mathf.Clamp(node.RatingCount <= 0 ? 5 : node.RatingCount, 1, 10);
            var glyphs = RatingGlyphs(node.RatingStyle);
            float pipSize = node.RatingPipSize <= 0f ? 15f : Mathf.Clamp(node.RatingPipSize, 8f, 48f);
            bool animate = node.RatingAnimate;

            // Colors: "on" defaults to the accent (else a warm gold), "off" to a muted grey, and the hover
            // preview to a lightened "on" — each independently overridable per node.
            Color selectedDefault = node.DisplayStyleData.accent.isOverridden
                ? node.DisplayStyleData.accent.value
                : new Color(0.98f, 0.78f, 0.28f, 1f);
            Color selected = node.RatingSelectedColor.Resolve(selectedDefault);
            Color unselected = node.RatingUnselectedColor.Resolve(new Color(0.45f, 0.45f, 0.47f, 1f));
            Color hover = node.RatingHoverColor.Resolve(Color.Lerp(selected, Color.white, 0.35f));

            // On the LIGHT skin the warm-gold fill (and the muted-grey hollow pips) wash out against the
            // near-white field — trace every glyph with a thin dark outline so the stars read cleanly. The
            // dark skin already has enough contrast, so it's left untouched.
            bool lightSkin = WidgetPalette.IsLight;
            var pipOutline = new Color(0.13f, 0.13f, 0.14f, 0.9f);
            float pipOutlineWidth = Mathf.Max(0.75f, pipSize * 0.05f);

            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel)) labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            var input = new VisualElement();
            input.AddToClassList("unity-base-field__input");
            input.style.flexDirection = FlexDirection.Row;
            input.style.alignItems = Align.Center;
            // WRAP rather than run off the row. A pip is a glyph Label sized by fontSize, so it has a hard
            // intrinsic width and cannot shrink — 10 pips at the default 15px want ~190px and a 300pt
            // inspector gives this input 129px, so the tail pips painted up to 59px past the row and the clip
            // cut them in half (measured 2026-09-01). Shrinking the glyphs all the way down would make a
            // rating unreadable at the exact moment the user needs to count it, so the row gives way onto a
            // second line — the same instinct as the Min/Max fold and the affix demote.
            //
            // ...but wrapping is the LAST resort, not the first. The "costs nothing, 5 pips at 15px is 85px"
            // claim this note used to carry was wrong: a glyph's ADVANCE is wider than its font size, so a
            // 15px "★" lays out 18px and five of them want 5x(18+2) = 100px, not 85. MEASURED in a −26°
            // split pane whose left side is 90px: the default five-star rating dropped its fifth star onto a
            // second line, which both looks broken and costs the frame 17px of height that the opposite pane
            // then shows as dead space. An 8% shortfall does not deserve a second line.
            //
            // So FitPips (below) shrinks the glyphs a bounded amount first and only lets flex-wrap have the
            // row when even the floor will not fit. Both halves are load-bearing: the shrink keeps ordinary
            // narrow layouts on one line, the wrap still catches 10 pips in a 60px cell.
            input.style.flexWrap = Wrap.Wrap;
            wrapper.Add(input);

            var pips = new Label[count];
            int hoverIndex = -1; // 1-based pip count previewed while hovering; -1 = not hovering

            void Paint()
            {
                prop.serializedObject.Update();
                bool mixed = MixedValueUI.IsMixed(prop);
                int val = mixed ? 0 : (int)ReadNumeric(prop);
                for (int i = 0; i < count; i++)
                {
                    // While hovering, preview the score the cursor would set (in the hover color); otherwise
                    // show the committed value in the selected color.
                    bool on = hoverIndex >= 0 ? i < hoverIndex : (!mixed && i < val);
                    pips[i].text = on ? glyphs.Filled : glyphs.Hollow;
                    pips[i].style.color = hoverIndex >= 0 ? (on ? hover : unselected) : (on ? selected : unselected);
                }
            }

            for (int i = 0; i < count; i++)
            {
                int oneBased = i + 1;
                var pip = new Label(glyphs.Hollow);
                pip.style.fontSize = pipSize;
                pip.style.marginRight = 2;
                pip.style.color = unselected;
                if (lightSkin)
                {
                    pip.style.unityTextOutlineColor = pipOutline;
                    pip.style.unityTextOutlineWidth = pipOutlineWidth;
                }
                // Springy hover-lift + press-dip (transform-only; no-op when motion is disabled globally).
                if (animate) EditorSpringFX.ButtonSpring(pip, 1.18f, 0.9f);
                pip.RegisterCallback<PointerEnterEvent>(_ => { hoverIndex = oneBased; Paint(); });
                // Guarded so moving between adjacent pips (Enter(new) + Leave(old) in either order) settles
                // on the newly-hovered pip rather than clearing the preview.
                pip.RegisterCallback<PointerLeaveEvent>(_ => { if (hoverIndex == oneBased) { hoverIndex = -1; Paint(); } });
                pip.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    prop.serializedObject.Update();
                    // On a MIXED selection Paint() forces the pips to 0 — there is no visible "current top
                    // pip" to step down FROM, and reading one anyway means the first object's hidden value
                    // decides the click: clicking the Nth pip on an all-empty row would write N-1, one short
                    // of both the hover preview and what the user asked for. A mixed click always SETS.
                    int cur = MixedValueUI.IsMixed(prop) ? -1 : (int)ReadNumeric(prop);
                    // Clicking the current top pip clears one step (a click-to-toggle-off affordance).
                    int next = cur == oneBased ? oneBased - 1 : oneBased;
                    if (WriteNumeric(prop, next))
                        prop.serializedObject.ApplyModifiedProperties();
                    hoverIndex = oneBased; // keep previewing under the cursor after the click
                    Paint();
                    evt.StopPropagation();
                });
                pips[i] = pip;
                input.Add(pip);
            }

            Paint();

            // SHRINK-TO-FIT, THEN WRAP (see the note on flexWrap above).
            //
            // The scale is derived from the AVAILABLE width alone, never from the pips' current size, so it
            // is a pure function of the row and cannot chase itself: the pip's natural advance is recovered
            // by dividing the laid-out width by the scale already applied, which returns the same number at
            // every scale. That makes this a one-shot settle rather than an iteration.
            //
            // TWO GUARDS, because this widget lives inside DiagonalSplit frames and a font-size write there
            // closes a loop that nothing else would: smaller pips → shorter row → shorter frame → smaller
            // tiltSpan → WIDER pane → room to grow the pips again. (1) The applied size is quantised to a
            // WHOLE pixel, so sub-pixel width noise cannot produce a new size at all. (2) The decision is
            // taken against a LATCHED width, asymmetrically — follow a narrowing at once so the row can
            // never overflow, but demand real growth before widening again. The loop above only ever
            // supplies about 0.5px of width per pixel of height (tan 26° / 2), so it can never clear the
            // dead zone and the decision holds. Same shape as SasquatchEnumBadgeControl's wrap latch, and
            // the same reason.
            const float PipGap = 2f;          // the marginRight each pip carries
            const float MinPipScale = 0.7f;   // below this a rating stops being countable at a glance
            const float MinPipFontPx = 9f;
            const float FitWidthDeadZone = 6f;
            // AIM STRICTLY UNDER THE LINE, never at it. Flex wraps whenever the items do not fit, so a fit
            // that lands EXACTLY on the available width is a coin toss decided by rounding — solving for
            // `need == avail` produced a row that still wrapped at the size that was supposed to save it
            // (measured: 5 pips at font 13 needing 88.0px in an 88.0px input). Two pixels of headroom costs
            // one font step at most and makes the answer deterministic.
            const float FitSafetyPx = 2f;
            float appliedScale = 1f;
            float fitWidthLatch = float.NaN;

            void FitPips()
            {
                float avail = input.contentRect.width;
                if (float.IsNaN(avail) || avail <= 0f) return;   // pre-layout; a later pass carries a width
                if (!float.IsNaN(fitWidthLatch)
                    && avail >= fitWidthLatch
                    && avail <= fitWidthLatch + FitWidthDeadZone) return;

                float advance = pips[0].layout.width;
                if (float.IsNaN(advance) || advance <= 0f) return;   // nothing measured yet
                fitWidthLatch = avail;

                float natural = advance / appliedScale;              // the advance at the AUTHORED size
                float room = avail - count * PipGap - FitSafetyPx;
                float floorScale = Mathf.Max(MinPipScale, MinPipFontPx / pipSize);
                float scale = Mathf.Clamp(room / (count * natural), floorScale, 1f);
                float px = Mathf.Max(MinPipFontPx, Mathf.Floor(pipSize * scale));
                scale = px / pipSize;
                if (Mathf.Abs(scale - appliedScale) < 0.001f) return;

                appliedScale = scale;
                for (int i = 0; i < count; i++)
                {
                    pips[i].style.fontSize = px;
                    // The outline is sized from the glyph, so it has to follow it down or a shrunk pip on
                    // the light skin ends up mostly outline.
                    if (lightSkin) pips[i].style.unityTextOutlineWidth = Mathf.Max(0.75f, px * 0.05f);
                }
            }
            input.RegisterCallback<GeometryChangedEvent>(_ => FitPips());

            // A live value change repaints only when not mid-hover (hover already shows a preview).
            wrapper.TrackPropertyValueWithRefresh(prop, _ => { if (hoverIndex < 0) Paint(); });
            return wrapper;
        }
    }
}
