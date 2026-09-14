using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// A drawn miniature of one debug row, with leader lines pointing at the four things nobody can be
    /// expected to guess: the trace, the console pins, the playhead, and the readout beside it.
    ///
    /// <para><b>Why a drawing and not a list.</b> Every item in the legend is a piece of NOTATION — a yellow
    /// tick, a vertical line, "↓38 ↑100". Naming those in prose asks the reader to hold a description in their
    /// head, go and find the thing on screen, and match them up. A picture of the row with lines pointing at
    /// its parts collapses that into one glance, and it is the same picture whichever field they were actually
    /// looking at.</para>
    ///
    /// <para><b>Two rules inherited from <see cref="DebugGraphElement"/>, both load-bearing.</b>
    /// (1) <b>Painter2D cannot draw text</b>, so every caption here is a real child element positioned in
    /// percentages over the same normalised grid the paint pass uses — the constants below are the single
    /// source of truth for both, or the lines would point at where the labels used to be.
    /// (2) <b>Capture skin colours at construction.</b> The constructor runs inside the caller's
    /// <c>WidgetPalette</c> scope; the paint callback does not, and would resolve against whatever skin
    /// happened to be current when Unity decided to repaint.</para>
    /// </summary>
    internal sealed class DebugLegendDiagram : VisualElement
    {
        private const float BoxHeight = 138f;

        // ---- the normalised grid ------------------------------------------------------------------------
        // Fractions of the box, shared by the paint pass and the caption placement. Nothing here is a pixel,
        // so the diagram survives the card being widened without the leaders drifting off their targets.
        private const float WellL = 0.030f, WellR = 0.575f, WellT = 0.300f, WellB = 0.620f;
        private const float ReadoutX = 0.610f;
        private const float PlayheadX = 0.400f;
        private const float Tick1X = 0.170f, Tick2X = 0.490f;

        private const float CapTopY = 0.010f;     // "console output"
        private const float CapPlayY = 0.700f;    // "the playhead"
        private const float CapReadY = 0.855f;    // "value + range seen"
        private const float RightLane = 0.965f;   // the return lane the readout leader runs up

        // Captured skin colours — see the class note.
        private readonly Color _wellBg;
        private readonly Color _border;
        private readonly Color _trace;
        private readonly Color _traceFill;
        private readonly Color _leader;

        /// <summary>
        /// The real playhead's colour, from the palette rather than a literal.
        ///
        /// <para>These two were <c>static readonly</c> literals holding only the DARK values, under a comment
        /// promising they were kept in lockstep with the USS token and with the graph's pins. They were not:
        /// on a light editor this diagram taught the notation in the wrong ink, which is the one thing a
        /// legend must never do. They are PROPERTIES now, not fields — the palette answer depends on the
        /// current skin, and a static readonly field would freeze whichever skin happened to load first.</para>
        /// </summary>
        private readonly Color _playheadInk;

        /// <summary>The warning pin colour, resolved through the same palette entry the pins and the moment
        /// card's chip use. Same reason, same rule.</summary>
        private readonly Color _pinInk;

        internal DebugLegendDiagram()
        {
            _wellBg = WidgetPalette.WellBg;
            _border = WidgetPalette.Border;
            // Captured with the rest, for the same reason: Paint runs outside the build's palette scope.
            _playheadInk = WidgetPalette.Playhead;
            _pinInk = WidgetPalette.WarningCalloutText;
            _trace = WidgetPalette.Accent;
            _traceFill = new Color(_trace.r, _trace.g, _trace.b, 0.16f);
            _leader = WidgetPalette.TextGray;

            style.height = BoxHeight;
            style.marginTop = 2;
            style.marginBottom = 2;
            // Nothing here is clickable, and a diagram that eats pointer events inside a settings card would
            // silently break scrolling over it.
            pickingMode = PickingMode.Ignore;

            generateVisualContent += Paint;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());

            Caption("console output", 0.030f, CapTopY, false);
            Caption("the playhead — the sample you are reading", 0.030f, CapPlayY, false);
            Caption("value + range seen", 0.030f, CapReadY, false);

            // The row's own readout, drawn as text because that is what it is on the real thing.
            Caption("89.49", ReadoutX, WellT + 0.01f, true);
            Caption("↓38  ↑100", ReadoutX, WellT + 0.16f, false);
        }

        /// <summary>One positioned caption. Percent-positioned over the same grid the leaders are drawn on.</summary>
        private void Caption(string text, float x, float y, bool strong)
        {
            var label = new Label(text);
            label.style.position = Position.Absolute;
            label.style.left = Length.Percent(x * 100f);
            label.style.top = Length.Percent(y * 100f);
            label.style.fontSize = strong ? 10 : 9;
            label.style.unityFontStyleAndWeight = strong ? FontStyle.Bold : FontStyle.Normal;
            label.style.color = strong ? WidgetPalette.TextStrong : WidgetPalette.TextGray;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.pickingMode = PickingMode.Ignore;
            Add(label);
        }

        private void Paint(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            float w = contentRect.width;
            float h = contentRect.height;
            if (w <= 1f || h <= 1f) return;

            float wl = WellL * w, wr = WellR * w, wt = WellT * h, wb = WellB * h;

            // ---- the well ----
            painter.fillColor = _wellBg;
            painter.BeginPath();
            painter.MoveTo(new Vector2(wl, wt));
            painter.LineTo(new Vector2(wr, wt));
            painter.LineTo(new Vector2(wr, wb));
            painter.LineTo(new Vector2(wl, wb));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = _border;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(wl, wt));
            painter.LineTo(new Vector2(wr, wt));
            painter.LineTo(new Vector2(wr, wb));
            painter.LineTo(new Vector2(wl, wb));
            painter.ClosePath();
            painter.Stroke();

            // ---- the trace: a decaying wobble, so it reads as recorded data rather than as decoration ----
            const int Steps = 34;
            painter.BeginPath();
            painter.MoveTo(new Vector2(wl, TraceY(0, Steps, wt, wb)));
            for (int i = 1; i <= Steps; i++)
                painter.LineTo(new Vector2(Mathf.Lerp(wl, wr, i / (float)Steps), TraceY(i, Steps, wt, wb)));
            painter.LineTo(new Vector2(wr, wb));
            painter.LineTo(new Vector2(wl, wb));
            painter.ClosePath();
            painter.fillColor = _traceFill;
            painter.Fill();

            painter.BeginPath();
            painter.MoveTo(new Vector2(wl, TraceY(0, Steps, wt, wb)));
            for (int i = 1; i <= Steps; i++)
                painter.LineTo(new Vector2(Mathf.Lerp(wl, wr, i / (float)Steps), TraceY(i, Steps, wt, wb)));
            painter.strokeColor = _trace;
            painter.lineWidth = 1.5f;
            painter.Stroke();

            // ---- console pins, on the top edge where the real ones sit ----
            painter.fillColor = _pinInk;
            Pin(painter, Tick1X * w, wt, h);
            Pin(painter, Tick2X * w, wt, h);

            // ---- the playhead ----
            painter.strokeColor = _playheadInk;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(PlayheadX * w, wt));
            painter.LineTo(new Vector2(PlayheadX * w, wb));
            painter.Stroke();

            // ---- leaders ----
            painter.strokeColor = _leader;
            painter.lineWidth = 1f;

            // "console output" -> the first pin. Down and across, never diagonal across the well.
            painter.BeginPath();
            painter.MoveTo(new Vector2(0.115f * w, (CapTopY + 0.095f) * h));
            painter.LineTo(new Vector2(0.115f * w, (WellT - 0.055f) * h));
            painter.LineTo(new Vector2(Tick1X * w, (WellT - 0.055f) * h));
            painter.LineTo(new Vector2(Tick1X * w, wt));
            painter.Stroke();

            // "the playhead" -> the vertical line.
            painter.BeginPath();
            painter.MoveTo(new Vector2(0.115f * w, CapPlayY * h));
            painter.LineTo(new Vector2(0.115f * w, (WellB + 0.055f) * h));
            painter.LineTo(new Vector2(PlayheadX * w, (WellB + 0.055f) * h));
            painter.LineTo(new Vector2(PlayheadX * w, wb));
            painter.Stroke();

            // "value + range seen" -> the readout. Routed round the outside on the right so it never crosses
            // the well or the other two leaders.
            float lane = RightLane * w;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0.335f * w, (CapReadY + 0.045f) * h));
            painter.LineTo(new Vector2(lane, (CapReadY + 0.045f) * h));
            painter.LineTo(new Vector2(lane, (WellT + 0.10f) * h));
            painter.LineTo(new Vector2(0.905f * w, (WellT + 0.10f) * h));
            painter.Stroke();
        }

        /// <summary>A pin: a short stub hanging off the well's top edge, the shape the real ones draw.</summary>
        private static void Pin(Painter2D painter, float x, float top, float h)
        {
            float pw = 2f;
            float ph = 0.085f * h;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x - pw * 0.5f, top));
            painter.LineTo(new Vector2(x + pw * 0.5f, top));
            painter.LineTo(new Vector2(x + pw * 0.5f, top + ph));
            painter.LineTo(new Vector2(x - pw * 0.5f, top + ph));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>Deterministic sample curve — a decaying drift, framed inside the well with a little headroom.</summary>
        private static float TraceY(int i, int steps, float top, float bottom)
        {
            float t = i / (float)steps;
            // No randomness: the legend must draw the same picture every repaint, and Mathf.Sin is enough to
            // look like a value that moved rather than a straight line that did not.
            float v = 0.72f - 0.45f * t + 0.10f * Mathf.Sin(t * 11f) * (1f - t);
            float pad = (bottom - top) * 0.12f;
            return Mathf.Lerp(bottom - pad, top + pad, Mathf.Clamp01(v));
        }
    }
}
