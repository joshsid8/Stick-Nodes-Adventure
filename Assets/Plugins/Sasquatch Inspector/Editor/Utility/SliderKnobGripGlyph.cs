using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The vertical fader ridges engraved on the Grip slider knob, drawn as a single element with
    /// <see cref="Painter2D"/> instead of N stacked 1px child VisualElements.
    ///
    /// Stacked 1px divs hit the same device-pixel rounding the reorder grips hit before
    /// <see cref="GripGlyph"/> — but WORSE, because a slider knob is positioned
    /// along its track by an inline <c>translate.x</c> that BaseSlider drives from the value AND the
    /// track width. UI Toolkit pixel-snaps element LAYOUT rects to the device grid but never snaps
    /// <c>translate</c>, so the ridge block lands on a fractional device pixel whose offset changes
    /// with the inspector width — a ½px shift smears a 1px line across two half-bright columns
    /// (blurry at some widths, crisp at others). Drawing every ridge here with one quantized width,
    /// height and pitch snapped in WORLD device space cancels the knob's sub-pixel translate, so the
    /// ridges stay crisp at any width, value or display scale. Local-only snapping (what
    /// <see cref="GripGlyph"/>/<see cref="SasquatchIconElement"/> do) would not — it aligns to the
    /// element's own origin, which the translate has already pushed off the grid.
    ///
    /// Internal rather than private because the verification tests drive <see cref="ComputeRidgeRects"/>
    /// directly and query the element by type.
    /// </summary>
    internal sealed class SliderKnobGripGlyph : VisualElement
    {
        private int _count = 3;
        private Color _tint = Color.white;
        private float _ridgeW = 1f, _ridgeH = 6f, _gap = 2f; // points; refreshed by Configure

        /// <summary>Ridge count (3 on the main knob, 2 on min-max thumbs). Exposed for the tests.</summary>
        public int RidgeCount => _count;

        public SliderKnobGripGlyph()
        {
            // Cover the whole knob so the ridge block centers on the knob's content box; the ridges
            // are decoration and must never intercept the drag pointer.
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f; style.right = 0f; style.top = 0f; style.bottom = 0f;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Sets the ridge count, per-ridge geometry (points) and color, then repaints.</summary>
        public void Configure(int count, float ridgeW, float ridgeH, float gap, Color tint)
        {
            _count = Mathf.Max(0, count);
            _ridgeW = ridgeW;
            _ridgeH = ridgeH;
            _gap = gap;
            _tint = tint;
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_count <= 0) return;
            var rect = contentRect;
            if (rect.width < 0.01f || rect.height < 0.01f) return;

            var wb = worldBound; // includes the knob's inline translate — that's exactly what we snap away
            if (float.IsNaN(wb.x) || float.IsNaN(wb.y)) return;

            var ridges = ComputeRidgeRects(rect, _count, _ridgeW, _ridgeH, _gap, wb.x, wb.y,
                EditorGUIUtility.pixelsPerPoint);

            var painter = mgc.painter2D;
            painter.fillColor = _tint;
            painter.BeginPath();
            for (int i = 0; i < ridges.Length; i++)
            {
                var r = ridges[i];
                painter.MoveTo(new Vector2(r.xMin, r.yMin));
                painter.LineTo(new Vector2(r.xMax, r.yMin));
                painter.LineTo(new Vector2(r.xMax, r.yMax));
                painter.LineTo(new Vector2(r.xMin, r.yMax));
                painter.ClosePath();
            }
            painter.Fill();
        }

        /// <summary>
        /// The ridge rects in LOCAL points, with every edge on the device-pixel grid in WORLD space:
        /// one shared whole-pixel width, one shared whole-pixel height, one shared whole-pixel pitch,
        /// the block centered on the content rect. <paramref name="worldOriginX"/>/<paramref name="worldOriginY"/>
        /// are the world-point coordinates of the element's local (0,0) (i.e. <c>worldBound.position</c>);
        /// folding them into the snap is what cancels the knob's sub-pixel translate. Public because the
        /// verification tests live in another assembly.
        /// </summary>
        public static Rect[] ComputeRidgeRects(Rect contentRect, int count, float ridgeWpt, float ridgeHpt,
            float gapPt, float worldOriginX, float worldOriginY, float ppp)
        {
            if (count <= 0) return Array.Empty<Rect>();

            // Mathf.Round is banker's rounding (half-to-even), which would pick inconsistent sides at
            // successive .5 scale products — always round half up instead (matches GripGlyph).
            float RoundHalfUp(float v) => Mathf.Floor(v + 0.5f);

            // All sizing in whole device pixels, mapped back to points only at the end. Both edges of
            // a filled rect on integer device columns/rows render crisp regardless of width parity.
            float wDev = Mathf.Max(1f, RoundHalfUp(ridgeWpt * ppp));                 // ridge width
            float hDev = Mathf.Max(1f, RoundHalfUp(ridgeHpt * ppp));                 // ridge height
            float pitchDev = Mathf.Max(wDev + 1f, RoundHalfUp((ridgeWpt + gapPt) * ppp)); // left-to-left, ≥1px gap
            float blockWDev = (count - 1) * pitchDev + wDev;

            // Center the block on the content rect's center expressed in WORLD device space, then snap:
            // this is where the knob's fractional translate gets quantized onto the grid.
            float leftDev = RoundHalfUp((worldOriginX + contentRect.center.x) * ppp - blockWDev * 0.5f);
            float topDev = RoundHalfUp((worldOriginY + contentRect.center.y) * ppp - hDev * 0.5f);

            var rects = new Rect[count];
            for (int i = 0; i < count; i++)
            {
                // device -> local points: local = worldDevice/ppp - worldOrigin
                float xLocal = (leftDev + i * pitchDev) / ppp - worldOriginX;
                float yLocal = topDev / ppp - worldOriginY;
                rects[i] = new Rect(xLocal, yLocal, wDev / ppp, hDev / ppp);
            }
            return rects;
        }
    }
}
