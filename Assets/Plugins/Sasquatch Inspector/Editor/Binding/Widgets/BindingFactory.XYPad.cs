using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// XY Pad widget: a Painter2D pad for a <see cref="Vector2"/> (or <see cref="Vector2Int"/>). Dragging the
    /// handle sets (x, y) within the field's numeric range (from <see cref="BindingFactory.ResolveLimits"/>,
    /// default 0..1); Y is drawn inverted so "up" is a higher value. Falls back to a plain field for any other
    /// property type, so a stale binding degrades instead of throwing.
    /// </summary>
    public static partial class BindingFactory
    {
        private const float XYPadSize = 120f;

        private static VisualElement CreateXYPad(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            bool isVec2 = prop.propertyType == SerializedPropertyType.Vector2;
            bool isVec2Int = prop.propertyType == SerializedPropertyType.Vector2Int;
            if (!isVec2 && !isVec2Int)
                return new PropertyField(prop) { label = resolvedLabel };

            ResolveLimits(node, prop, out float min, out float max, 0f, 1f);
            if (max <= min) max = min + 1f;

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
            // Horizontal placement of the fixed-size pad within the input column.
            input.style.alignItems = node.XYPadAlignment == ImageAlignment.Center ? Align.Center
                                   : node.XYPadAlignment == ImageAlignment.Right ? Align.FlexEnd
                                   : Align.FlexStart;
            wrapper.Add(input);

            var accent = node.DisplayStyleData.accent.isOverridden ? node.DisplayStyleData.accent.value : WidgetPalette.Accent;
            // Per-part colors: an unset OverridableColor tracks the theme (handle → accent, bg → well, grid → border).
            var handleColor = node.XYPadHandleColor.isOverridden ? node.XYPadHandleColor.value : accent;
            var bgColor = node.XYPadBackgroundColor.isOverridden ? node.XYPadBackgroundColor.value : WidgetPalette.WellBg;
            var gridColor = node.XYPadGridColor.isOverridden ? node.XYPadGridColor.value : WidgetPalette.Border;

            Vector2 ReadVec() => isVec2Int ? (Vector2)prop.vector2IntValue : prop.vector2Value;
            // Both axes at once — correct for a PAD DRAG, where the pointer genuinely sets X and Y together.
            void WriteVec(Vector2 v)
            {
                prop.serializedObject.Update();
                if (isVec2Int)
                {
                    if (!WriteVectorAxis(prop, 0, v.x) || !WriteVectorAxis(prop, 1, v.y))
                        prop.vector2IntValue = new Vector2Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));
                }
                else if (!WriteVectorAxis(prop, 0, v.x) || !WriteVectorAxis(prop, 1, v.y))
                {
                    prop.vector2Value = v;
                }
                prop.serializedObject.ApplyModifiedProperties();
            }
            // ONE axis — for the typed X / Y fields, where the user changed exactly one. A whole-vector write
            // would carry the OTHER axis (read off the first selected object) onto every other object in a
            // multi-selection, wiping its opposite component. Unity's own Vector2 field writes only the
            // changed sub-property; so does this.
            void WriteAxis(int axis, float value)
            {
                prop.serializedObject.Update();
                if (!WriteVectorAxis(prop, axis, value))
                {
                    var v = ReadVec();
                    v[axis] = value;
                    if (isVec2Int) prop.vector2IntValue = new Vector2Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));
                    else prop.vector2Value = v;
                }
                prop.serializedObject.ApplyModifiedProperties();
            }

            var pad = new VisualElement();
            pad.style.width = XYPadSize;
            pad.style.height = XYPadSize;
            pad.style.backgroundColor = bgColor;
            pad.style.borderTopWidth = 1; pad.style.borderBottomWidth = 1;
            pad.style.borderLeftWidth = 1; pad.style.borderRightWidth = 1;
            pad.style.borderTopColor = WidgetPalette.Border; pad.style.borderBottomColor = WidgetPalette.Border;
            pad.style.borderLeftColor = WidgetPalette.Border; pad.style.borderRightColor = WidgetPalette.Border;
            pad.style.borderTopLeftRadius = 4; pad.style.borderTopRightRadius = 4;
            pad.style.borderBottomLeftRadius = 4; pad.style.borderBottomRightRadius = 4;

            pad.generateVisualContent += mgc =>
            {
                var rect = pad.contentRect;
                float w = rect.width, h = rect.height;
                if (w < 4f || h < 4f) return;
                var p = mgc.painter2D;

                // Value grid: lines every XYPadGridSize value units on BOTH axes, drawn at the SAME positions the
                // snap step lands on (absolute multiples of the step, clamped to [min,max]). Skipped when the step
                // would be denser than ~4px so a tiny grid size never floods the pad with a solid fill.
                if (node.XYPadShowGrid)
                {
                    float step = Mathf.Max(1e-4f, node.XYPadGridSize);
                    float pxPerStep = step / (max - min) * w; // square pad ⇒ same spacing on X and Y
                    if (pxPerStep >= 4f)
                    {
                        var gl = gridColor; gl.a *= 0.6f; // grid lines sit a touch under the center crosshair
                        p.strokeColor = gl;
                        p.lineWidth = 1f;
                        p.BeginPath();
                        int k0 = Mathf.CeilToInt(min / step - 1e-4f);
                        int k1 = Mathf.FloorToInt(max / step + 1e-4f);
                        for (int k = k0; k <= k1; k++)
                        {
                            float f = Mathf.InverseLerp(min, max, k * step);
                            float gx = Mathf.Round(f * w) + 0.5f;          // crisp 1px lines
                            float gy = Mathf.Round((1f - f) * h) + 0.5f;
                            p.MoveTo(new Vector2(gx, 0f)); p.LineTo(new Vector2(gx, h)); // vertical (X value)
                            p.MoveTo(new Vector2(0f, gy)); p.LineTo(new Vector2(w, gy)); // horizontal (Y value)
                        }
                        p.Stroke();
                    }
                }

                // Static center crosshair — the pad's orientation anchor. It belongs to the grid, so it shows
                // only when the grid is on: "no grid" is then a clean pad (just the handle), not a lone cross.
                if (node.XYPadShowGrid)
                {
                    p.strokeColor = gridColor;
                    p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(Mathf.Round(w * 0.5f) + 0.5f, 0f)); p.LineTo(new Vector2(Mathf.Round(w * 0.5f) + 0.5f, h));
                    p.MoveTo(new Vector2(0f, Mathf.Round(h * 0.5f) + 0.5f)); p.LineTo(new Vector2(w, Mathf.Round(h * 0.5f) + 0.5f));
                    p.Stroke();
                }

                var v = ReadVec();
                float fx = Mathf.Clamp01(Mathf.InverseLerp(min, max, v.x));
                float fy = Mathf.Clamp01(Mathf.InverseLerp(min, max, v.y));
                float hx = fx * w;
                float hy = (1f - fy) * h; // invert Y: up = higher value

                // Optional handle crosshair — full-width lines drawn THROUGH the handle so they track it as it
                // moves (opt-in, independent of the grid). A pointer aid that reads the exact X/Y off the edges.
                if (node.XYPadHandleCrosshair)
                {
                    p.strokeColor = node.XYPadCrosshairColor.isOverridden ? node.XYPadCrosshairColor.value : gridColor;
                    p.lineWidth = Mathf.Max(0.5f, node.XYPadCrosshairThickness);
                    p.BeginPath();
                    p.MoveTo(new Vector2(Mathf.Round(hx) + 0.5f, 0f)); p.LineTo(new Vector2(Mathf.Round(hx) + 0.5f, h));
                    p.MoveTo(new Vector2(0f, Mathf.Round(hy) + 0.5f)); p.LineTo(new Vector2(w, Mathf.Round(hy) + 0.5f));
                    p.Stroke();
                }

                // Handle: soft glow + solid dot, in the resolved handle color.
                p.fillColor = new Color(handleColor.r, handleColor.g, handleColor.b, 0.18f);
                p.BeginPath(); p.Arc(new Vector2(hx, hy), 9f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
                p.fillColor = handleColor;
                p.BeginPath(); p.Arc(new Vector2(hx, hy), 5f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            };

            // Under the pad: either a compact read-only "(x, y)" label, or (when XYPadEditableFields is on) two
            // synced number fields you can type into — mirrors the Min-Max slider's editable-labels pattern.
            bool editable = node.XYPadEditableFields;

            var readout = new Label();
            readout.style.fontSize = 10;
            readout.style.marginTop = 3;
            readout.style.color = WidgetPalette.LabelSecondary;

            var fieldsRow = new VisualElement();
            fieldsRow.style.flexDirection = FlexDirection.Row;
            fieldsRow.style.alignItems = Align.Center;
            fieldsRow.style.marginTop = 3;
            fieldsRow.style.width = XYPadSize; // sit under the pad
            // Bare (unlabeled) fields so the input isn't squeezed to zero width by a base-field label column;
            // a small X/Y prefix label sits before each (mirrors the Vector Sliders row).
            var xAxisLbl = new Label("X") { pickingMode = PickingMode.Ignore };
            xAxisLbl.style.width = 11; xAxisLbl.style.fontSize = 10; xAxisLbl.style.color = WidgetPalette.LabelSecondary;
            var yAxisLbl = new Label("Y") { pickingMode = PickingMode.Ignore };
            yAxisLbl.style.width = 11; yAxisLbl.style.fontSize = 10; yAxisLbl.style.color = WidgetPalette.LabelSecondary; yAxisLbl.style.marginLeft = 6;
            var xField = new FloatField { isDelayed = true };
            var yField = new FloatField { isDelayed = true };
            xField.style.flexGrow = 1; xField.style.flexBasis = 0;
            yField.style.flexGrow = 1; yField.style.flexBasis = 0;
            // Unity tags a field built inside an InspectorElement `unity-base-field__inspector-field`, whose
            // USS carries margin-right: -2 so a FULL-WIDTH row's input reaches the inspector's true edge.
            // These two are hand-placed inside a row this widget PINS to the pad's width, so that bleed put
            // the Y box 2px past the row and the clip sheared its rounded right corners flat. Same defect
            // the Min/Max read-outs carry, found by the geometry audit on 2026-08-29. Only marginRight is
            // cleared: marginLeft is Unity's standard 3px input inset and is what spaces each box from its
            // X/Y caption.
            xField.style.marginRight = 0;
            yField.style.marginRight = 0;
            bool xFocused = false, yFocused = false;
            xField.RegisterCallback<FocusInEvent>(_ => xFocused = true);
            xField.RegisterCallback<FocusOutEvent>(_ => xFocused = false);
            yField.RegisterCallback<FocusInEvent>(_ => yFocused = true);
            yField.RegisterCallback<FocusOutEvent>(_ => yFocused = false);

            void UpdateReadout()
            {
                var v = ReadVec();
                if (editable)
                {
                    if (!xFocused) xField.SetValueWithoutNotify(isVec2Int ? Mathf.Round(v.x) : v.x);
                    if (!yFocused) yField.SetValueWithoutNotify(isVec2Int ? Mathf.Round(v.y) : v.y);
                }
                else
                {
                    readout.text = isVec2Int ? $"({(int)v.x}, {(int)v.y})" : $"({v.x:0.###}, {v.y:0.###})";
                }
            }

            if (editable)
            {
                xField.RegisterValueChangedCallback(evt => { WriteAxis(0, evt.newValue); pad.MarkDirtyRepaint(); });
                yField.RegisterValueChangedCallback(evt => { WriteAxis(1, evt.newValue); pad.MarkDirtyRepaint(); });
            }

            void SetFromLocal(Vector2 local)
            {
                var rect = pad.contentRect;
                if (rect.width <= 0f || rect.height <= 0f) return;
                float fx = Mathf.Clamp01(local.x / rect.width);
                float fy = Mathf.Clamp01(1f - local.y / rect.height);
                float vx = Mathf.Lerp(min, max, fx);
                float vy = Mathf.Lerp(min, max, fy);
                // Snap the dragged value onto the same grid lines that are drawn (absolute multiples of the step).
                if (node.XYPadSnapToGrid)
                {
                    float step = Mathf.Max(1e-4f, node.XYPadGridSize);
                    vx = Mathf.Clamp(Mathf.Round(vx / step) * step, min, max);
                    vy = Mathf.Clamp(Mathf.Round(vy / step) * step, min, max);
                }
                WriteVec(new Vector2(vx, vy));
                pad.MarkDirtyRepaint();
                UpdateReadout();
            }

            bool dragging = false;
            pad.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                dragging = true;
                pad.CapturePointer(evt.pointerId);
                SetFromLocal((Vector2)evt.localPosition);
                evt.StopPropagation();
            });
            pad.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                SetFromLocal((Vector2)evt.localPosition);
                evt.StopPropagation();
            });
            pad.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                pad.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            input.Add(pad);
            if (editable)
            {
                fieldsRow.Add(xAxisLbl);
                fieldsRow.Add(xField);
                fieldsRow.Add(yAxisLbl);
                fieldsRow.Add(yField);
                input.Add(fieldsRow);
            }
            else
            {
                input.Add(readout);
            }
            UpdateReadout();
            wrapper.TrackPropertyValueWithRefresh(prop, _ => { pad.MarkDirtyRepaint(); UpdateReadout(); });
            return wrapper;
        }
    }
}
