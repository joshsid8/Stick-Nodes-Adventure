using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The three-line drag-handle glyph used by reorder grips, drawn as a single element with
    /// <see cref="Painter2D"/> instead of three 1px child VisualElements. The layout engine
    /// rounds each child's rect to the device-pixel grid independently, so at fractional
    /// display scaling (125%/150%) three "identical" stacked hairlines round to different
    /// heights (1/2/1 device px — the middle line visibly fatter at rest). Drawing all three
    /// here with one quantized thickness and pitch makes them identical at any DPI, the same
    /// approach <see cref="SasquatchIconElement"/> uses for icon strokes.
    /// Public rather than internal because the verification tests live in a separate assembly.
    /// </summary>
    public sealed class GripGlyph : VisualElement
    {
        public static readonly Color DefaultTint = new Color(0.533f, 0.533f, 0.533f, 1f); // #888888
        public static readonly Color HoverTint = Color.white;

        private Color _tint = DefaultTint;

        /// <summary>Line color. Mesh data bakes the color, so changing it requires a repaint.</summary>
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

        /// <summary>Creates the three-line grip glyph at its default 9×7 box.</summary>
        public GripGlyph()
        {
            // Same visual box as the old three-div grip (9x7 at 100% scale); containers center it.
            style.width = 9f;
            style.height = 7f;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore; // hover/drag events belong to the grip container
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 0.01f || rect.height < 0.01f) return;

            var lines = ComputeLineRects(rect, EditorGUIUtility.pixelsPerPoint);

            var painter = mgc.painter2D;
            painter.fillColor = _tint;
            painter.BeginPath();
            for (int i = 0; i < lines.Length; i++)
            {
                var r = lines[i];
                painter.MoveTo(new Vector2(r.xMin, r.yMin));
                painter.LineTo(new Vector2(r.xMax, r.yMin));
                painter.LineTo(new Vector2(r.xMax, r.yMax));
                painter.LineTo(new Vector2(r.xMin, r.yMax));
                painter.ClosePath();
            }
            painter.Fill();
        }

        /// <summary>
        /// The three line rects in local points, with every edge on the device-pixel grid:
        /// one shared whole-pixel thickness, one shared whole-pixel pitch, block centered on
        /// a whole-pixel row. Public because the verification tests live in another assembly.
        /// </summary>
        public static Rect[] ComputeLineRects(Rect contentRect, float ppp)
        {
            // Mathf.Round is banker's rounding (half-to-even), which would pick inconsistent
            // sides at successive .5 scale products — always round half up instead.
            float RoundHalfUp(float v) => Mathf.Floor(v + 0.5f);

            // All sizing in whole device pixels, mapped back to points only at the end.
            float th = Mathf.Max(1f, RoundHalfUp(1f * ppp));         // line thickness
            float pitch = Mathf.Max(th + 1f, RoundHalfUp(3f * ppp)); // center-to-center distance
            float blockH = 2f * pitch + th;                          // top edge to bottom edge

            float top = RoundHalfUp((contentRect.y + contentRect.height * 0.5f) * ppp - blockH * 0.5f);
            float left = RoundHalfUp(contentRect.x * ppp);
            float width = Mathf.Max(1f, RoundHalfUp(contentRect.width * ppp));

            var rects = new Rect[3];
            for (int i = 0; i < 3; i++)
            {
                rects[i] = new Rect(left / ppp, (top + i * pitch) / ppp, width / ppp, th / ppp);
            }
            return rects;
        }
    }
}
