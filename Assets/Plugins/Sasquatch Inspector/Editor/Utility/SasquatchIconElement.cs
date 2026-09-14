using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A monochrome vector icon drawn with <see cref="Painter2D"/> from geometry registered in
    /// <see cref="SasquatchIcons"/>. Vector geometry rather than unicode/emoji glyphs, so every icon
    /// renders identically (crisp, white, stroke-based) on every platform and DPI instead of falling
    /// back to the OS color-emoji font.
    ///
    /// Geometry lives in a 24x24 viewBox (Lucide convention) and is scaled to the element's
    /// content rect at draw time. Create instances via <see cref="SasquatchIcons.Create"/>.
    /// </summary>
    internal sealed class SasquatchIconElement : VisualElement
    {
        private string _iconId;
        private Color _tint;

        /// <summary>Swap the rendered icon (e.g. caret chevron flipping open/closed).</summary>
        public string IconId
        {
            get => _iconId;
            set
            {
                if (_iconId == value) return;
                _iconId = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Stroke color. Mesh data bakes the color, so changing it requires a repaint.</summary>
        public Color Tint
        {
            get => _tint;
            set
            {
                if (_tint == value) return;
                _tint = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Stroke width in viewBox units (Lucide draws at 2).</summary>
        public float StrokeWidth { get; set; } = SasquatchIcons.DefaultStrokeWidth;

        private bool _bold;

        /// <summary>
        /// When true the glyph keeps its hollow outline but draws with a much heavier stroke
        /// (<see cref="SasquatchIcons.BoldStrokeWidth"/>) — the same geometry, just emphasized.
        /// Used to signal state at a glance (e.g. the header pencil-ruler goes bold once a custom
        /// layout exists) without the solid-blob look of a full fill.
        /// </summary>
        public bool Bold
        {
            get => _bold;
            set
            {
                if (_bold == value) return;
                _bold = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Creates an icon element rendering lucide icon <paramref name="iconId"/> at <paramref name="size"/> px in <paramref name="tint"/>.</summary>
        public SasquatchIconElement(string iconId, float size, Color tint)
        {
            _iconId = iconId;
            _tint = tint;
            style.width = size;
            style.height = size;
            // Icons are decoration: never crush in flex rows, never steal pointer events. The
            // caret button's row handler checks `evt.target is Button`, so a pickable child here
            // would silently break click handling.
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 0.01f || rect.height < 0.01f) return;

            var geometry = SasquatchIcons.GetGeometry(_iconId);
            if (geometry == null || geometry.Count == 0) return;

            float scale = Mathf.Min(rect.width, rect.height) / SasquatchIcons.ViewBoxSize;
            // Center the square viewBox inside a possibly non-square rect.
            var offset = new Vector2(
                rect.x + (rect.width - SasquatchIcons.ViewBoxSize * scale) * 0.5f,
                rect.y + (rect.height - SasquatchIcons.ViewBoxSize * scale) * 0.5f);

            // Stroke width in device pixels. Below ~1px strokes turn faint and ragged, so clamp;
            // and when the width is already near a whole pixel count, quantize to it — a 1.08px
            // stroke smears across two half-bright rows instead of filling one.
            float ppp = EditorGUIUtility.pixelsPerPoint;
            float strokeWidth = _bold ? SasquatchIcons.BoldStrokeWidth : StrokeWidth;
            float deviceWidth = Mathf.Max(strokeWidth * scale, 1f) * ppp;
            float roundedWidth = Mathf.Max(1f, Mathf.Round(deviceWidth));
            if (Mathf.Abs(deviceWidth - roundedWidth) <= 0.25f) deviceWidth = roundedWidth;
            bool oddWidth = ((int)roundedWidth & 1) == 1;

            // Painter2D has no transform stack, so scale+offset are baked into every point.
            int count = geometry.Count;
            var pts = new Vector2[count][];
            for (int i = 0; i < count; i++)
            {
                var cmd = geometry[i];
                switch (cmd.Kind)
                {
                    case SasquatchIcons.CmdKind.Move:
                    case SasquatchIcons.CmdKind.Line:
                        pts[i] = new[] { cmd.P0 * scale + offset };
                        break;
                    case SasquatchIcons.CmdKind.Cubic:
                        pts[i] = new[] { cmd.P0 * scale + offset, cmd.P1 * scale + offset, cmd.P2 * scale + offset };
                        break;
                }
            }

            // Pixel-snap axis-aligned straight segments — and ONLY those. A hairline whose
            // centerline falls between device pixels renders as two half-bright rows (one line
            // of an icon visibly fatter than its siblings); an odd-width stroke is crisp
            // centered on a half-pixel, an even-width one on an integer. Diagonals and curves
            // are left alone: snapping a chevron's apex would visibly skew it, and bending a
            // curve by half a pixel buys nothing.
            float Grid(float v)
            {
                float dv = v * ppp;
                return (oddWidth ? Mathf.Floor(dv) + 0.5f : Mathf.Round(dv)) / ppp;
            }
            void SnapSegment(int aCmd, int aPt, int bCmd, int bPt)
            {
                if (aCmd < 0 || bCmd < 0) return;
                var a = pts[aCmd][aPt];
                var b = pts[bCmd][bPt];
                // A closed ring's seam is a ZERO-length segment (its Close lands back on its Move).
                // It has no axis to align to, and the x-test below would match it and drag the ring's
                // start point onto the pixel grid — re-opening the very seam the Close exists to shut.
                if (Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f) return;
                if (Mathf.Abs(a.x - b.x) < 0.01f)
                {
                    float x = Grid((a.x + b.x) * 0.5f);
                    pts[aCmd][aPt].x = x;
                    pts[bCmd][bPt].x = x;
                }
                else if (Mathf.Abs(a.y - b.y) < 0.01f)
                {
                    float y = Grid((a.y + b.y) * 0.5f);
                    pts[aCmd][aPt].y = y;
                    pts[bCmd][bPt].y = y;
                }
            }
            int prevCmd = -1, prevPt = 0;   // ref to the path's current point
            int startCmd = -1, startPt = 0; // ref to the current subpath's first point
            for (int i = 0; i < count; i++)
            {
                switch (geometry[i].Kind)
                {
                    case SasquatchIcons.CmdKind.Move:
                        prevCmd = i; prevPt = 0;
                        startCmd = i; startPt = 0;
                        break;
                    case SasquatchIcons.CmdKind.Line:
                        SnapSegment(prevCmd, prevPt, i, 0);
                        prevCmd = i; prevPt = 0;
                        break;
                    case SasquatchIcons.CmdKind.Cubic:
                        prevCmd = i; prevPt = 2;
                        break;
                    case SasquatchIcons.CmdKind.Close:
                        SnapSegment(prevCmd, prevPt, startCmd, startPt);
                        prevCmd = startCmd; prevPt = startPt;
                        break;
                }
            }

            var painter = mgc.painter2D;
            painter.lineWidth = deviceWidth / ppp;
            painter.strokeColor = _tint;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            painter.BeginPath();
            for (int i = 0; i < count; i++)
            {
                switch (geometry[i].Kind)
                {
                    case SasquatchIcons.CmdKind.Move:
                        painter.MoveTo(pts[i][0]);
                        break;
                    case SasquatchIcons.CmdKind.Line:
                        painter.LineTo(pts[i][0]);
                        break;
                    case SasquatchIcons.CmdKind.Cubic:
                        painter.BezierCurveTo(pts[i][0], pts[i][1], pts[i][2]);
                        break;
                    case SasquatchIcons.CmdKind.Close:
                        painter.ClosePath();
                        break;
                }
            }
            // Bold and default both just stroke the (hollow) outline — only the line weight differs,
            // baked into deviceWidth above. No Fill(), so the glyph never reads as a solid blob.
            painter.Stroke();
        }
    }
}
