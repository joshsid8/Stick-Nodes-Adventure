using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Vector Sliders widget: a <see cref="Vector3"/>/<see cref="Vector4"/> shown as per-axis sliders (range from
    /// <see cref="BindingFactory.ResolveLimits"/>, default -1..1) each paired with a numeric field, plus an
    /// optional Normalize button that rescales the vector to unit length. Good for direction / normal fields.
    /// Falls back to a plain field for any other property type (fuzzer-safe).
    /// </summary>
    public static partial class BindingFactory
    {
        private static VisualElement CreateVectorSliders(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            bool isVec3 = prop.propertyType == SerializedPropertyType.Vector3;
            bool isVec4 = prop.propertyType == SerializedPropertyType.Vector4;
            if (!isVec3 && !isVec4)
            {
                // Fuzzer-safe fallback. Pips still travel, because the emitter's own fallthrough for this
                // case lands on the pips-on PropertyField branch — dropping them here would make the
                // exported editor and the live inspector disagree on a Vector2 left set to this widget.
                var fallback = new PropertyField(prop) { label = resolvedLabel };
                if (node.VectorAxisPips) AxisPips.Attach(fallback, prop);
                return fallback;
            }

            ResolveLimits(node, prop, out float min, out float max, -1f, 1f);
            if (max <= min) max = min + 1f;

            int n = isVec4 ? 4 : 3;
            string[] axes = { "X", "Y", "Z", "W" };

            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel)) labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            var input = new VisualElement();
            input.AddToClassList("unity-base-field__input");
            input.style.flexDirection = FlexDirection.Column;
            wrapper.Add(input);

            Vector4 ReadV() => isVec4 ? prop.vector4Value : (Vector4)prop.vector3Value;
            // ONE axis at a time. A whole-vector write would carry the OTHER axes — read off the first
            // selected object — onto every other object in a multi-selection, wiping their Y/Z. Writing the
            // sub-property leaves the siblings alone, exactly as Unity's own vector field does. The
            // whole-vector fallback only runs if the sub-property can't be resolved (never for a real
            // Vector3/Vector4), and is then no worse than before.
            void WriteAxis(int axis, float value)
            {
                prop.serializedObject.Update();
                if (!WriteVectorAxis(prop, axis, value))
                {
                    var v = ReadV();
                    v[axis] = value;
                    if (isVec4) prop.vector4Value = v;
                    else prop.vector3Value = new Vector3(v.x, v.y, v.z);
                }
                prop.serializedObject.ApplyModifiedProperties();
            }

            var sliders = new Slider[n];
            var fields = new FloatField[n];

            // Focus lands on the field's INNER text element, never on the FloatField itself, so a plain
            // `focusedElement != field` comparison reads "not focused" while the user is typing. Walk up
            // from whatever holds focus instead (same shape as the XY pad's guard).
            static bool Typing(VisualElement f)
            {
                var focused = f?.focusController?.focusedElement as VisualElement;
                for (; focused != null; focused = focused.parent)
                    if (focused == f) return true;
                return false;
            }

            void Refresh()
            {
                prop.serializedObject.Update();
                var v = ReadV();
                for (int i = 0; i < n; i++)
                {
                    sliders[i].SetValueWithoutNotify(Mathf.Clamp(v[i], min, max));
                    // Never push into a field the user is typing in — these are DELAYED fields, so an
                    // uncommitted edit lives only in the text box and a blind push would discard it. Every
                    // sibling widget guards this; Vector Sliders was the one that didn't.
                    if (!Typing(fields[i])) fields[i].SetValueWithoutNotify(v[i]);
                }
            }

            for (int i = 0; i < n; i++)
            {
                int axis = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var axisLabel = new Label(axes[i]);
                axisLabel.style.width = 14;
                axisLabel.style.color = WidgetPalette.LabelSecondary;
                axisLabel.style.fontSize = 11;

                var slider = new Slider(min, max);
                // The slider is the row's flexible piece. The theme gives every base-field
                // flex-shrink:0 + basis:auto, so without this the fixed axis label + value field +
                // the slider's natural basis can exceed the input column and the row paints past the
                // inspector's right edge (the value fields looked "cut off"). Basis 0 + shrink makes
                // the row always fit exactly; the floor keeps a usable slider at extreme widths.
                slider.style.flexGrow = 1;
                slider.style.flexShrink = 1;
                slider.style.flexBasis = 0f;
                slider.style.minWidth = 40;
                slider.style.marginRight = 4;
                // Honor the shared slider styling (bar/knob color, hover tint, knob shape, size, corner radius)
                // exactly like the Slider widget — a no-op when nothing is overridden.
                ApplySliderLook(slider, node);

                // When ApplySliderLook re-centers a resized/custom-shape knob to 50% but (at the default bar
                // height) leaves the thin track at the theme's fixed stock offset, the knob floats above the
                // track in this compact row — so pull the track to 50% too, sharing the knob's reference.
                // ONLY for that case: for the STOCK knob (default shape + normal size) ApplySliderLook no-ops
                // and the theme already keeps the knob, track, AND grow-on-focus hover ring mutually aligned.
                // Re-centering there instead pulls the track out of flow and COLLAPSES the control's height,
                // which drops the hover ring below the knob (the "ring not in line + knob too high" bug — it
                // showed up on normal size + default knob only). Registered AFTER ApplySliderLook's own
                // GeometryChanged so it wins; the standalone Slider widget never uses this.
                var vws = node.DisplayStyleData;
                bool knobRecentered = vws.sliderKnobShape != SliderKnobShape.Default || vws.sliderSize != SliderSize.Normal;
                if (knobRecentered)
                {
                    var capturedSlider = slider;
                    void CenterTrack()
                    {
                        var tr = capturedSlider.Q<VisualElement>(className: "unity-slider__tracker")
                              ?? capturedSlider.Q<VisualElement>(className: "unity-base-slider__tracker");
                        if (tr == null) return;
                        tr.style.position = Position.Absolute;
                        tr.style.left = 0f; tr.style.right = 0f;
                        tr.style.top = Length.Percent(50f);
                        float th = tr.resolvedStyle.height;
                        tr.style.marginTop = -(th > 0.5f ? th : 2f) * 0.5f;
                    }
                    CenterTrack();
                    slider.RegisterCallback<GeometryChangedEvent>(_ => CenterTrack());
                }

                var valField = new FloatField { isDelayed = true };
                // Axis color pips reserve a 9px lane inside the input, so the box grows by that much to
                // keep the same room for digits it has without them (Default's composite fields are
                // flexible and absorb the lane; this one is a fixed width and would just clip earlier).
                valField.style.width = node.VectorAxisPips ? 61 : 52;
                // The editor theme gives the field a NEGATIVE right margin, which lets its painted box
                // poke past the input column's right edge (it read as "cut off" against the rows
                // above/below). Zero it so the value box ends flush with every other row.
                valField.style.marginRight = 0;
                // Clip a long value to the box so a many-decimal number can't paint past the inspector's right
                // edge and trip the horizontal-overflow rail (HorizontalOverflowRescue stops descending at an
                // inline overflow:hidden). Matches Unity's own vector sub-fields, which clip rather than grow.
                valField.style.overflow = Overflow.Hidden;
                // Axis color pips, same as the Default vector field. The generic label sweep can't reach these:
                // the row's "X" is a bare Label of ours, and the value field carries no base-field label of its
                // own — so name the axis explicitly. The pip rides the value box's right edge, which is the same
                // quiet corner it takes on a Default component field.
                if (node.VectorAxisPips) AxisPips.Decorate(valField, axes[axis]);

                sliders[i] = slider;
                fields[i] = valField;

                slider.RegisterValueChangedCallback(evt =>
                {
                    WriteAxis(axis, evt.newValue);
                    valField.SetValueWithoutNotify(evt.newValue);
                });
                valField.RegisterValueChangedCallback(evt =>
                {
                    WriteAxis(axis, evt.newValue);
                    slider.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, min, max));
                });

                row.Add(axisLabel);
                row.Add(slider);
                row.Add(valField);
                input.Add(row);
            }

            if (node.VectorShowNormalize)
            {
                // Per-target: a unit vector has to be derived from each object's OWN direction. Normalizing
                // the first object's vector and writing that to the whole selection would give every
                // selected object the SAME direction, not its own normalized one.
                var normBtn = new Button(() =>
                {
                    MutatePerTarget(prop, p =>
                    {
                        Vector4 v = isVec4 ? p.vector4Value : (Vector4)p.vector3Value;
                        float mag = v.magnitude;
                        if (mag <= 1e-6f) return;
                        v /= mag;
                        if (isVec4) p.vector4Value = v;
                        else p.vector3Value = new Vector3(v.x, v.y, v.z);
                    });
                    Refresh();
                }) { text = "Normalize" };
                // Full Button-widget look via a transient ButtonNode carrying this node's normalize appearance.
                var styleNode = new ButtonNode
                {
                    ButtonText = "Normalize",
                    FullWidth = false, // hug the content; placement is set by VectorNormalizeAlignment below
                    StylePreset = node.NormalizeButtonStyle,
                    AccentColor = node.NormalizeButtonAccent,
                    CornerRadius = node.NormalizeButtonCornerRadius,
                    Size = node.NormalizeButtonSize,
                    HoverAnimation = node.NormalizeButtonHoverAnimation,
                    HoverColor = node.NormalizeButtonHoverColor,
                    PressedColor = node.NormalizeButtonPressedColor,
                };
                LayoutTreeBuilder.ApplyButtonChrome(normBtn, styleNode);
                // Placement (ApplyButtonChrome set alignSelf=FlexStart for non-full-width; override per the node).
                normBtn.style.alignSelf = node.VectorNormalizeAlignment == ImageAlignment.Center ? Align.Center
                                       : node.VectorNormalizeAlignment == ImageAlignment.Right ? Align.FlexEnd
                                       : Align.FlexStart;
                input.Add(normBtn);
            }

            Refresh();
            wrapper.TrackPropertyValueWithRefresh(prop, _ => Refresh());
            return wrapper;
        }
    }
}
