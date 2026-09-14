using System;
using SasquatchSpring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A live, Painter2D spring-response graph for a <see cref="SpringConfig"/>: integrates a virtual 0→1 step
    /// through <see cref="SpringMath.Evaluate"/> (the same sampling the SpringConfigDrawer and the Spring Sandbox
    /// use) and draws the resulting overshoot/settle curve. Reads Duration/Bounce/FrequencyScale live from the
    /// bound property; call <see cref="Refresh"/> (or rely on the tracked property) to redraw after an edit.
    /// </summary>
    internal sealed class SasquatchSpringControl : VisualElement
    {
        private readonly Func<SpringConfig> _configProvider;

        /// <summary>The response-curve stroke color (skin-aware default; captured at construction).</summary>
        public Color LineColor = WidgetPalette.ResolveLegacy(
            new Color(0.45f, 0.78f, 1f, 1f), WidgetPalette.CurveLineDarkDefault, WidgetPalette.CurveLineLight);

        // Reference-line inks captured at construction (generateVisualContent runs after the build scope).
        private readonly Color _refLineStrong = WidgetPalette.Ink(0.18f);
        private readonly Color _refLineFaint = WidgetPalette.Ink(0.07f);

        /// <summary>Inspector widget: reads the bound SpringConfig and live-tracks edits.</summary>
        public SasquatchSpringControl(SerializedProperty prop)
            : this(() => BindingFactory.ReadSpringConfig(prop))
        {
            this.TrackPropertyValueWithRefresh(prop, _ => MarkDirtyRepaint());
        }

        /// <summary>Config-provider variant (e.g. the Spring Sandbox window's live config) — same exact drawing.</summary>
        public SasquatchSpringControl(Func<SpringConfig> configProvider)
        {
            _configProvider = configProvider;
            AddToClassList("sasq-spring-graph");
            style.flexGrow = 1;
            style.backgroundColor = WidgetPalette.SpringWellBg;
            style.borderTopLeftRadius = 4; style.borderTopRightRadius = 4;
            style.borderBottomLeftRadius = 4; style.borderBottomRightRadius = 4;
            // A contrasting border (a touch heavier at the bottom) so the graph well reads as a contained panel.
            var border = WidgetPalette.SpringWellBorder;
            style.borderTopWidth = 1; style.borderLeftWidth = 1; style.borderRightWidth = 1; style.borderBottomWidth = 2;
            style.borderTopColor = border; style.borderLeftColor = border; style.borderRightColor = border; style.borderBottomColor = border;
            style.overflow = Overflow.Hidden;

            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        public void Refresh() => MarkDirtyRepaint();

        // The response-curve sample buffer (see OnGenerateVisualContent). Grow-only and fully rewritten each
        // paint, so it carries nothing between frames.
        private float[] _sampleBuffer;

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 4f || rect.height < 4f) return;

            var p = mgc.painter2D;
            var cfg = _configProvider != null ? _configProvider() : SpringConfig.Bouncy;
            float freq = cfg.Frequency, damp = cfg.Damping;

            float window = Mathf.Max(0.05f, cfg.Duration) * 1.4f; // a little past settle to show the tail
            // Dense enough to stay smooth even for high-bounce springs (many oscillations): scale with BOTH the
            // pixel width and the oscillation count in the window (~30 samples/cycle), mirroring the curve widget.
            float cycles = Mathf.Max(1f, freq * window);
            int samples = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(rect.width, cycles * 30f)), 256, 800);
            float dt = window / samples;

            // Sample first, tracking the actual min/max, so we can FRAME the whole curve (overshoot included)
            // instead of clamping the early peaks flat against a fixed range.
            // Grown, never reallocated per paint: this is inside OnGenerateVisualContent, so a live spring
            // preview allocates up to 801 floats on every repaint. Every entry is written by the loop below
            // before anything reads it, so reuse cannot show a stale sample.
            if (_sampleBuffer == null || _sampleBuffer.Length < samples + 1)
                _sampleBuffer = new float[samples + 1];
            var vals = _sampleBuffer;
            float pos = 0f, vel = 0f;
            float min = 0f, max = 1f; // always include the start (0) and the target (1)
            for (int i = 0; i <= samples; i++)
            {
                vals[i] = pos;
                if (pos < min) min = pos;
                if (pos > max) max = pos;
                SpringMath.Evaluate(dt, pos, vel, 1f, freq, damp, SpringConfig.DefaultSettleEpsilon, out pos, out vel);
            }
            float pad = (max - min) * 0.10f + 0.04f;
            float lo = min - pad, hi = max + pad;
            float top = 8f, bottom = rect.height - 8f;
            float ValueToY(float v) => Mathf.Lerp(bottom, top, Mathf.InverseLerp(lo, hi, v));

            // Reference lines: the target (value 1) and the start (value 0).
            p.lineWidth = 1f;
            p.strokeColor = _refLineStrong;
            p.BeginPath(); p.MoveTo(new Vector2(0f, ValueToY(1f))); p.LineTo(new Vector2(rect.width, ValueToY(1f))); p.Stroke();
            p.strokeColor = _refLineFaint;
            p.BeginPath(); p.MoveTo(new Vector2(0f, ValueToY(0f))); p.LineTo(new Vector2(rect.width, ValueToY(0f))); p.Stroke();

            // The response curve.
            p.strokeColor = LineColor;
            p.lineWidth = 2f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(0f, ValueToY(vals[0])));
            for (int i = 1; i <= samples; i++) p.LineTo(new Vector2(rect.width * i / samples, ValueToY(vals[i])));
            p.Stroke();
        }
    }
}
