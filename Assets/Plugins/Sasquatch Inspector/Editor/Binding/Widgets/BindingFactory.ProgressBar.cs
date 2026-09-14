using System;
using System.Globalization;
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
        // Single source for the fill's default color, shared with the designer's swatch and mirrored in
        // the theme sheets (--sasq-progress-fill) so the widget's premium default matches the tool's
        // house accent instead of a one-off hue. A property (not a static readonly) so it follows the
        // effective skin of the build in flight.
        internal static Color ProgressFillDefault => WidgetPalette.ProgressFill;

        // Fill precedence: explicit per-node fill → the widget-style accent → the house default.
        internal static Color ResolveProgressFillColor(BindingNode node)
        {
            if (node.ProgressBarColor.a > 0.001f) return node.ProgressBarColor;
            if (node.DisplayStyleData.accent.isOverridden) return node.DisplayStyleData.accent.value;
            return ProgressFillDefault;
        }

        // Picks light vs dark readout text for an arbitrary backdrop color, by WCAG relative
        // luminance. Biased toward the classic light readout: mid-brightness backdrops (the house
        // blue, reds) keep white text, and only backdrops where dark text wins by a wide margin
        // (yellow / magenta / cyan / pastels) flip to near-black.
        internal static Color ContrastingReadoutColor(Color bg)
        {
            float lum = ReadoutLuminance(bg);
            float contrastVsWhite = 1.05f / (lum + 0.05f);
            float contrastVsDark = (lum + 0.05f) / 0.05f;
            return contrastVsDark > contrastVsWhite * 1.4f
                ? ReadoutDark
                : Color.white;
        }

        // WCAG relative luminance, the shared basis for every readout-contrast decision here.
        private static float ReadoutLuminance(Color c)
        {
            static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        /// <summary>
        /// WCAG contrast ratio (1:1 … 21:1) between two opaque colors. The question
        /// <see cref="ContrastingReadoutColor"/> cannot answer: it always returns one END of the readout pair,
        /// but some readouts want to KEEP a deliberately muted ink (a theme grey) wherever that grey still
        /// reads, and fall back to the full-contrast pair only where it doesn't. Ask this first, flip second.
        /// </summary>
        internal static float ReadoutContrast(Color a, Color b)
        {
            float la = ReadoutLuminance(a), lb = ReadoutLuminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        /// <summary>WCAG's floor for large/bold text and UI components — the bar a muted readout ink has to
        /// clear against its backdrop before <see cref="ReadoutContrast"/> hands it back untouched.</summary>
        internal const float MinReadoutContrast = 3f;

        internal static readonly Color ReadoutDark = new Color(0.07f, 0.07f, 0.07f, 0.95f);

        // The OTHER end of the readout pair from <paramref name="c"/>. Auto-contrast already picks whichever
        // end reads on a given backdrop, so a hover state that only nudges that backdrop is invisible in
        // whichever half of the range the tick has already flipped to. Flipping the readout itself is a change
        // the eye cannot miss at EITHER end. Alpha is carried over so an explicitly-tinted readout keeps it.
        internal static Color OppositeReadoutColor(Color c)
        {
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            var flipped = lum > 0.5f ? ReadoutDark : Color.white;
            flipped.a = c.a;
            return flipped;
        }

        private static VisualElement CreateProgressBar(BindingNode node, SerializedProperty prop, string resolvedLabel, bool isTableCell = false)
        {
            bool supported = prop.propertyType == SerializedPropertyType.Integer || prop.propertyType == SerializedPropertyType.Float;
            if (!supported)
            {
                return new PropertyField(prop) { label = resolvedLabel };
            }

            ResolveLimits(node, prop, out float min, out float max, 0f, 100f);
            if (max <= min) max = min + 1f;

            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-progress-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel))
            {
                labelEl.style.display = DisplayStyle.None;
            }
            wrapper.Add(labelEl);

            var inputContainer = new VisualElement();
            inputContainer.AddToClassList("unity-base-field__input");
            inputContainer.style.flexDirection = FlexDirection.Row;

            var track = new VisualElement();
            track.AddToClassList("sasq-progress-track");
            track.style.flexGrow = 1;

            var fill = new VisualElement();
            fill.AddToClassList("sasq-progress-fill");
            fill.style.backgroundColor = ResolveProgressFillColor(node);
            track.Add(fill);

            // Track background color override
            if (node.ProgressBarBgColor.a > 0.001f)
            {
                track.style.backgroundColor = node.ProgressBarBgColor;
            }

            // Corner radius (opt-in): round the track + fill together.
            if (node.DisplayStyleData.cornerRadius.isOverridden)
            {
                float r = Mathf.Max(0f, node.DisplayStyleData.cornerRadius.value);
                track.style.borderTopLeftRadius = r; track.style.borderTopRightRadius = r;
                track.style.borderBottomLeftRadius = r; track.style.borderBottomRightRadius = r;
                fill.style.borderTopLeftRadius = r; fill.style.borderTopRightRadius = r;
                fill.style.borderBottomLeftRadius = r; fill.style.borderBottomRightRadius = r;
            }
            // Thickness (opt-in): explicit track height, else the stylesheet default.
            if (node.ProgressBarThickness > 0f)
                track.style.height = node.ProgressBarThickness;

            // Value-Based Fill samples the fill color per-value from this gradient (default red→yellow→green).
            // Captured once — the ramp is design-time config, so a designer edit rebuilds the whole bar anyway.
            Gradient valueFillGradient = node.ProgressBarValueFill ? node.ProgressBarFillGradient : null;

            // Readout text. Default = split readout: TWO perfectly-aligned copies of the text — the
            // base copy spans the track, and a second copy rides in an invisible clip whose width
            // mirrors the fill — so the readout inverts color exactly at the fill's leading edge.
            // Each copy's color is chosen against ITS backdrop (ContrastingReadoutColor), keeping
            // these small glyphs crisp over any fill/track/gradient with no outline or shadow.
            // With Text Backdrop on, a single copy sits on a dark pill instead (guaranteed contrast).
            bool textBackdrop = node.ProgressBarTextBackdrop;
            var labelOverlay = new Label();
            labelOverlay.AddToClassList("sasq-progress-label");
            labelOverlay.pickingMode = PickingMode.Ignore; // read-only overlay: never intercept pointer events

            VisualElement backdropHost = null;  // Text Backdrop mode: full-track centering host + pill
            VisualElement fillLabelClip = null; // split mode: clip synced to the fill width
            Label fillLabel = null;             // split mode: the fill-side copy
            if (textBackdrop)
            {
                backdropHost = new VisualElement();
                backdropHost.AddToClassList("sasq-progress-backdrop-host");
                backdropHost.pickingMode = PickingMode.Ignore;
                var pill = new VisualElement();
                pill.AddToClassList("sasq-progress-label-backdrop");
                pill.pickingMode = PickingMode.Ignore;
                labelOverlay.AddToClassList("sasq-progress-label--boxed");
                pill.Add(labelOverlay);
                backdropHost.Add(pill);
                track.Add(backdropHost);
            }
            else
            {
                track.Add(labelOverlay);
                fillLabelClip = new VisualElement();
                fillLabelClip.AddToClassList("sasq-progress-label-clip");
                fillLabelClip.pickingMode = PickingMode.Ignore;
                fillLabel = new Label();
                fillLabel.AddToClassList("sasq-progress-label");
                fillLabel.AddToClassList("sasq-progress-label--fill");
                fillLabel.pickingMode = PickingMode.Ignore;
                fillLabelClip.Add(fillLabel);
                track.Add(fillLabelClip);
            }

            inputContainer.Add(track);
            wrapper.Add(inputContainer);

            // Adaptive readout colors. The track backdrop is read from resolvedStyle once laid out
            // (so it follows the theme sheet in either skin); the fill side re-resolves per value
            // when a value-fill gradient drives the color. Seed ≈ the dark-skin track.
            Color trackBackdrop = new Color(0.169f, 0.169f, 0.169f, 1f);
            Color fillBackdrop = ResolveProgressFillColor(node);
            bool mixed = false;

            void ApplyReadoutColors()
            {
                if (textBackdrop) return; // pill mode: fixed light-on-dark from the stylesheet
                if (mixed)
                {
                    labelOverlay.style.color = StyleKeyword.Null; // let the mixed tint through
                    return;
                }
                labelOverlay.style.color = ContrastingReadoutColor(trackBackdrop);
                // A translucent fill reads as itself composited over the track — judge that blend.
                Color eff = fillBackdrop.a >= 0.999f ? fillBackdrop : Color.Lerp(trackBackdrop, fillBackdrop, fillBackdrop.a);
                fillLabel.style.color = ContrastingReadoutColor(eff);
            }

            void SetReadoutText(string s)
            {
                labelOverlay.text = s;
                if (fillLabel != null) fillLabel.text = s;
            }

            void SetReadoutVisible(bool on)
            {
                var d = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (backdropHost != null) backdropHost.style.display = d;
                else { labelOverlay.style.display = d; fillLabelClip.style.display = d; }
            }

            track.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // The fill-side copy is absolute inside the moving clip: pin its width to the track's
                // content box so its centered text stays in register with the base copy.
                if (fillLabel != null) fillLabel.style.width = track.contentRect.width;
                var bg = track.resolvedStyle.backgroundColor;
                if (bg.a > 0.001f && bg != trackBackdrop)
                {
                    trackBackdrop = bg;
                    ApplyReadoutColors();
                }
            });

            // ===== Precision editing =========================================================================
            // A bare drag maps the pointer straight onto the range, so the finest step it can reach is ONE
            // PIXEL — 0.24 units on a 412px track over 0..100, with everything between simply unreachable.
            // Three layers sit on top of that, and none of them adds a control, an option, or a pixel of
            // chrome; each is invisible until it is reached for:
            //   * a held modifier turns the drag RELATIVE and low-gain — Alt because that is Unity's own
            //     fine-drag key on a numeric field, Shift because that is the cross-application convention;
            //   * the track takes focus, so the arrow keys nudge it (the standard slider keyboard contract,
            //     and the only way any of this is reachable without a mouse);
            //   * a double-click swaps the readout for a text box, for "set it to exactly 42.5".
            // The tooltip below is the discoverability surface.
            bool isInt = prop.propertyType == SerializedPropertyType.Integer;
            double range = max - min;

            // Full-precision write, clamped to the bar's own range: integers (incl. long/ulong) and double
            // route through the shared numeric helper rather than intValue/floatValue, so wide fields aren't
            // truncated. Clamping matters now that the value can arrive from a keystroke or a typed number
            // instead of a pointer position that was already inside the track.
            void WriteClamped(double v)
            {
                v = Math.Max(min, Math.Min(max, v));
                if (WriteNumeric(prop, v)) prop.serializedObject.ApplyModifiedProperties();
            }

            // One arrow press. An integer moves by whole units — its smallest real step — and a float by a
            // hundredth of the range, which lands on round numbers for the usual 0..1 and 0..100 spans.
            double BaseStep() => isInt ? 1d : range / 100d;

            // The active drag's gain: 0 = absolute (the pointer position IS the value). Alt is finer than
            // Shift so the two compose into a coarse/fine/ultra-fine ladder rather than fighting.
            float GainFor(bool shift, bool alt) => alt ? 0.01f : shift ? 0.1f : 0f;

            float dragGain = 0f;
            float anchorX = 0f;
            double anchorValue = 0d;

            void AnchorAt(float x)
            {
                anchorX = x;
                anchorValue = ReadNumeric(prop);
            }

            void DragTo(IPointerEvent evt)
            {
                var rect = track.worldBound;
                if (rect.width <= 0f) return;

                float gain = GainFor(evt.shiftKey, evt.altKey);
                // Pressing, releasing or swapping the modifier mid-drag RE-ANCHORS instead of recomputing
                // from the old reference — otherwise the value teleports the moment the key state changes,
                // which is exactly the jump the fine mode exists to avoid.
                if (gain != dragGain)
                {
                    dragGain = gain;
                    AnchorAt(evt.position.x);
                }

                if (dragGain <= 0f)
                {
                    float pct = Mathf.Clamp01((evt.position.x - rect.xMin) / rect.width);
                    WriteClamped(min + pct * range);
                    return;
                }
                // Relative scrub measured from the anchor, never accumulated per-move: an integer field
                // rounds every write, and summing rounded deltas would swallow every sub-unit motion.
                WriteClamped(anchorValue + (evt.position.x - anchorX) * (range / rect.width) * dragGain);
            }

            // Animated fill: the displayed width eases toward the value on the track's scheduler so
            // programmatic/undo/other-field changes glide instead of popping. It SNAPS (no animation)
            // on the first draw, while an interactive drag holds the pointer (the bar must never lag
            // the cursor), when motion is disabled in preferences, for the mixed multi-object state,
            // and during an explicit TrackedRefresh (reorder commits): there the row's binding now
            // holds a DIFFERENT element's value, and easing toward it would read as "the value just
            // changed" — the value must appear to move WITH its row, never to morph in place.
            // Only the width animates — the text readout always shows the real value immediately.
            bool dragging = false;
            float displayedPct = -1f; // first-draw sentinel
            float targetPct = 0f;
            IVisualElementScheduledItem fillAnim = null;

            void SetFillWidth(float pct)
            {
                var w = Length.Percent(pct * 100f);
                fill.style.width = w;
                if (fillLabelClip != null) fillLabelClip.style.width = w; // readout inverts with the fill
            }

            void MoveFillTo(float pct)
            {
                targetPct = pct;
                if (displayedPct < 0f || dragging || !EditorSpringFX.MotionEnabled ||
                    TrackedRefresh.IsExplicitRefresh ||
                    Mathf.Abs(targetPct - displayedPct) < 0.0025f)
                {
                    fillAnim?.Pause();
                    displayedPct = targetPct;
                    SetFillWidth(targetPct);
                    return;
                }

                if (fillAnim == null)
                {
                    fillAnim = track.schedule.Execute(() =>
                    {
                        displayedPct = Mathf.Lerp(displayedPct, targetPct, 0.25f);
                        if (Mathf.Abs(targetPct - displayedPct) < 0.0025f)
                        {
                            displayedPct = targetPct;
                            fillAnim.Pause();
                        }
                        SetFillWidth(displayedPct);
                    }).Every(16);
                }
                else
                {
                    fillAnim.Resume();
                }
            }
            // No detach Pause: UI Toolkit pauses the glide when the track leaves the panel and resumes it on
            // re-attach, so an interrupted ease still lands on the real value. An explicit Pause() never
            // resumes — a re-parent mid-glide (window tab drag) would freeze the fill at the intermediate
            // width, i.e. showing a value the data never had. A SETTLED glide stays paused (it paused itself
            // inside the tick, which UI Toolkit does not undo). See PanelLifetime.

            // Inline type-in: a double-click turns the readout into a text box. Built lazily — a bar nobody
            // double-clicks never pays for it.
            TextField typeIn = null;
            bool typeInOpen = false;

            void CloseTypeIn(bool commit)
            {
                if (!typeInOpen) return;
                typeInOpen = false; // FIRST — CloseTypeIn re-enters via the FocusOut this method causes
                if (commit)
                {
                    // Invariant first (that is how the box was seeded), then the editor's culture, so a
                    // comma-decimal locale can still type "42,5" and be understood.
                    string raw = typeIn.value != null ? typeIn.value.Trim() : "";
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                        double.TryParse(raw, out parsed))
                        WriteClamped(parsed);
                }
                typeIn.style.display = DisplayStyle.None;
                UpdateBar();   // restores the readout this hid, and re-syncs the fill to whatever landed
                track.Focus(); // hand focus back so the arrow keys keep working straight afterwards
            }

            void OpenTypeIn()
            {
                // Nothing sensible to seed from a mixed multi-selection — the em-dash is the honest answer,
                // and pre-filling one object's value invites writing it to the whole selection by accident.
                if (typeInOpen || mixed) return;
                if (typeIn == null)
                {
                    typeIn = new TextField { isDelayed = false };
                    typeIn.AddToClassList("sasq-progress-typein");
                    typeIn.style.position = Position.Absolute;
                    typeIn.style.left = 0f; typeIn.style.right = 0f; typeIn.style.top = 0f; typeIn.style.bottom = 0f;
                    typeIn.style.marginLeft = 0f; typeIn.style.marginRight = 0f;
                    typeIn.style.marginTop = 0f; typeIn.style.marginBottom = 0f;
                    var inner = typeIn.Q<VisualElement>(className: "unity-base-text-field__input");
                    if (inner != null)
                    {
                        inner.style.unityTextAlign = TextAnchor.MiddleCenter;
                        inner.style.fontSize = 11;
                        inner.style.marginLeft = 0f; inner.style.marginRight = 0f;
                    }
                    // The track's own pointer + key handlers must not also fire for the box on top of it:
                    // a click inside would re-scrub the value out from under the caret, and an arrow key
                    // meant to move the caret would nudge the bar.
                    typeIn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    typeIn.RegisterCallback<KeyDownEvent>(e =>
                    {
                        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CloseTypeIn(true); e.StopPropagation(); }
                        else if (e.keyCode == KeyCode.Escape) { CloseTypeIn(false); e.StopPropagation(); }
                    });
                    typeIn.RegisterCallback<FocusOutEvent>(_ => CloseTypeIn(true));
                    track.Add(typeIn);
                }
                typeInOpen = true;
                typeIn.SetValueWithoutNotify(ReadNumeric(prop).ToString("0.####", CultureInfo.InvariantCulture));
                typeIn.style.display = DisplayStyle.Flex;
                SetReadoutVisible(false);
                // Focus on the next tick: the box was display:none until a moment ago, and focusing an
                // unlaid-out text field drops the caret (and the first keystroke with it).
                typeIn.schedule.Execute(() => { typeIn.Focus(); typeIn.SelectAll(); }).StartingIn(0);
            }

            if (node.ProgressBarInteractive)
            {
                track.AddToClassList("sasq-progress-interactive");
                // Focusable so the keyboard can reach the bar at all. The tooltip is where every shortcut
                // below is actually discoverable — and it is deliberately set HERE, before the caller's
                // tooltip pass runs: a designer-authored (or [Tooltip]) string overwrites this one, so the
                // field's own documentation always wins over the shortcut crib sheet.
                track.focusable = true;
                track.tabIndex = 0;
                track.tooltip =
                    "Drag to set the value.\n" +
                    "Shift-drag for fine control, Alt-drag for ultra-fine.\n" +
                    "Double-click to type an exact value.\n" +
                    "When focused: arrows nudge (Shift x10, Alt /10), Page Up/Down jump, Home/End go to the ends.";

                // Focus ring: the track's USS border re-colored to the accent, cleared back to the
                // stylesheet on blur. A keyboard user has to be able to see which bar they are driving.
                void SetTrackBorder(StyleColor c)
                {
                    track.style.borderTopColor = c; track.style.borderBottomColor = c;
                    track.style.borderLeftColor = c; track.style.borderRightColor = c;
                }
                track.RegisterCallback<FocusEvent>(_ => SetTrackBorder(WidgetPalette.AccentUnity));
                track.RegisterCallback<BlurEvent>(_ => SetTrackBorder(StyleKeyword.Null));

                track.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || typeInOpen) return;
                    if (evt.clickCount >= 2) { OpenTypeIn(); evt.StopPropagation(); return; }
                    track.Focus(); // so the arrow keys work immediately after a click, like a native slider
                    track.CapturePointer(evt.pointerId);
                    dragging = true;
                    dragGain = GainFor(evt.shiftKey, evt.altKey);
                    // A fine gesture starts exactly WHERE THE VALUE ALREADY IS: pressing with the modifier
                    // held must not jump to the pointer first, or the precision is spent undoing the jump.
                    if (dragGain > 0f) AnchorAt(evt.position.x);
                    else DragTo(evt);
                    evt.StopPropagation();
                });
                track.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!track.HasPointerCapture(evt.pointerId)) return;
                    DragTo(evt);
                    evt.StopPropagation();
                });
                track.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (track.HasPointerCapture(evt.pointerId))
                    {
                        track.ReleasePointer(evt.pointerId);
                        dragging = false;
                        dragGain = 0f;
                        evt.StopPropagation();
                    }
                });

                track.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (typeInOpen) return; // the text box owns the keyboard while it is up

                    double step = BaseStep();
                    if (evt.shiftKey) step *= 10d;
                    else if (evt.altKey) step *= 0.1d;
                    // An integer can't move by less than one, and a rounded-to-zero step would make the key
                    // silently dead — floor it so every press is felt.
                    if (isInt) step = Math.Max(1d, Math.Round(step));

                    double cur = ReadNumeric(prop);
                    switch (evt.keyCode)
                    {
                        case KeyCode.LeftArrow:
                        case KeyCode.DownArrow: WriteClamped(cur - step); break;
                        case KeyCode.RightArrow:
                        case KeyCode.UpArrow: WriteClamped(cur + step); break;
                        case KeyCode.PageDown: WriteClamped(cur - step * 10d); break;
                        case KeyCode.PageUp: WriteClamped(cur + step * 10d); break;
                        case KeyCode.Home: WriteClamped(min); break;
                        case KeyCode.End: WriteClamped(max); break;
                        // Enter/F2 opens the type-in without a mouse, so the exact-value path is reachable
                        // from the keyboard too.
                        case KeyCode.Return:
                        case KeyCode.KeypadEnter:
                        case KeyCode.F2: OpenTypeIn(); break;
                        default: return;
                    }
                    evt.StopPropagation();
                });
            }

            void UpdateBar()
            {
                if (MixedValueUI.IsMixed(prop))
                {
                    // Multi-object mixed: no fake fill length — empty track + an em-dash readout.
                    fillAnim?.Pause();
                    displayedPct = 0f;
                    SetFillWidth(0f);
                    mixed = true;
                    SetReadoutVisible(true);
                    labelOverlay.AddToClassList(MixedValueUI.MixedTextClass);
                    ApplyReadoutColors(); // clears the inline color so the mixed tint shows
                    SetReadoutText(MixedValueUI.Dash);
                    return;
                }
                mixed = false;
                labelOverlay.RemoveFromClassList(MixedValueUI.MixedTextClass);

                double val = ReadNumeric(prop);

                float pct = Mathf.Clamp01((float)((val - min) / (max - min)));
                MoveFillTo(pct);

                if (node.ProgressBarValueFill && valueFillGradient != null)
                {
                    fillBackdrop = valueFillGradient.Evaluate(pct);
                    fill.style.backgroundColor = fillBackdrop;
                }
                ApplyReadoutColors();

                // Affix TEXT rides inside the overlay's format strings (this read-out is one string, so an
                // icon or a pill has nowhere to live here). EVERY mode that shows text carries it, Percentage
                // included; only None has no read-out to decorate. Both values are decorated in ValueAndMax so
                // "12 HP / 40 HP" reads as one pair of quantities.
                // Keep in lockstep with FieldAffixes.CapabilitiesFor, which hides the section for None.
                switch (node.ProgressBarTextMode)
                {
                    case ProgressBarTextMode.ValueAndMax:
                        SetReadoutVisible(true);
                        SetReadoutText($"{FieldAffixes.Decorate(node, isTableCell: isTableCell, value: val.ToString("0.##"))} / " +
                                       $"{FieldAffixes.Decorate(node, isTableCell: isTableCell, value: max.ToString("0.##"))} ({pct * 100f:0}%)");
                        break;
                    case ProgressBarTextMode.Percentage:
                        SetReadoutVisible(true);
                        // Decorated like every other mode. It used to render the bare percentage, so a unit
                        // typed on a Percentage bar was stored, shown in the designer, and silently dropped —
                        // the exact "drawn and ignored" behaviour the affix capability rules exist to prevent.
                        // A percentage is still a quantity, and a buyer may well want a word beside it.
                        SetReadoutText(FieldAffixes.Decorate(node, isTableCell: isTableCell,
                                                             value: $"{pct * 100f:0}%"));
                        break;
                    case ProgressBarTextMode.ValueOnly:
                        SetReadoutVisible(true);
                        SetReadoutText(FieldAffixes.Decorate(node, isTableCell: isTableCell, value: val.ToString("0.##")));
                        break;
                    case ProgressBarTextMode.None:
                    default:
                        SetReadoutVisible(false);
                        SetReadoutText("");
                        break;
                }
            }

            UpdateBar();
            track.TrackPropertyValueWithRefresh(prop, _ => UpdateBar());

            return wrapper;
        }

    }
}
