using System;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A novel "feel the spring" slider. The handle rests at center; drag it anywhere and RELEASE, and it springs
    /// back to center using the bound <see cref="SpringConfig"/> — so you literally watch (and feel, by dragging)
    /// the configured Duration/Bounce. The motion uses the user's REAL config (not a tuned editor variant) and is
    /// advanced by the shared SpringEditorDriver, so it animates live in the inspector without entering play mode.
    /// </summary>
    internal sealed class SasquatchSpringTester : VisualElement
    {
        private const float Center = 0.5f;
        // Captured at construction (runs inside the build's WidgetPalette scope); paint + hover reuse them.
        private readonly Color IdleBg = WidgetPalette.TesterIdleBg;    // recessed at rest
        private readonly Color HoverBg = WidgetPalette.TesterHoverBg;  // lifts on hover
        private readonly Color _accent = WidgetPalette.TesterAccent;
        private readonly Color _handleFill = WidgetPalette.TesterHandle;
        private readonly Color _centerTick = WidgetPalette.Ink(0.22f);

        private readonly Func<SpringConfig> _config;
        private float _t01 = Center;     // handle position; may briefly exceed [0,1] during overshoot (visualized).
        private bool _dragging;

        public SasquatchSpringTester(Func<SpringConfig> configProvider)
        {
            _config = configProvider;
            AddToClassList("sasq-spring-tester");
            style.height = 30;
            style.flexGrow = 1;
            style.backgroundColor = IdleBg;
            style.borderTopLeftRadius = 15; style.borderTopRightRadius = 15;
            style.borderBottomLeftRadius = 15; style.borderBottomRightRadius = 15;
            pickingMode = PickingMode.Position;

            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);
            RegisterCallback<DetachFromPanelEvent>(_ => Spring.CancelAll(this));

            // Darker at rest, lighter on hover — a clear "this is interactive" cue WITHOUT resizing the control.
            RegisterCallback<PointerEnterEvent>(_ => style.backgroundColor = HoverBg);
            RegisterCallback<PointerLeaveEvent>(_ => { if (!_dragging) style.backgroundColor = IdleBg; });
        }

        private float HandleRadius => contentRect.height * 0.42f;
        private float Pad => HandleRadius + 2f;
        private float Usable => Mathf.Max(1f, contentRect.width - 2f * Pad);

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            Spring.CancelAll(this);          // stop any in-flight spring-back so the grab is instant
            _dragging = true;
            this.CapturePointer(evt.pointerId);
            SetFromPointer(evt.localPosition.x);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || !this.HasPointerCapture(evt.pointerId)) return;
            SetFromPointer(evt.localPosition.x);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging) return;
            _dragging = false;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            ReleaseToCenter();
            evt.StopPropagation();
        }

        private void SetFromPointer(float x)
        {
            _t01 = Mathf.Clamp01((x - Pad) / Usable);
            MarkDirtyRepaint();
        }

        // Spring the handle back to center using the user's actual config (so they feel THEIR spring). The setter
        // is left unclamped so overshoot past center is visible; SpringEditorDriver advances it each editor tick.
        private void ReleaseToCenter()
        {
            var cfg = _config != null ? _config() : SpringConfig.Bouncy;
            if (!EditorSpringFX.MotionEnabled) { _t01 = Center; MarkDirtyRepaint(); return; }
            Spring.To((object)this, () => _t01, v => { _t01 = v; MarkDirtyRepaint(); }, Center, cfg, "springback");
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 4f || rect.height < 4f) return;

            var p = mgc.painter2D;
            float r = HandleRadius;
            float cy = rect.height * 0.5f;
            float centerX = Pad + Center * Usable;
            float handleX = Pad + _t01 * Usable; // _t01 may exceed [0,1] on overshoot → handle nudges past center

            var accent = _accent;

            // Center tick.
            p.strokeColor = _centerTick;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(centerX, cy - r * 0.75f));
            p.LineTo(new Vector2(centerX, cy + r * 0.75f));
            p.Stroke();

            // Speed dots from center to the handle (angry-birds style): a fixed count of dots, so a BIGGER
            // displacement spreads them WIDER ("it'll be fast") and a small one bunches them up ("slow"). They
            // re-space every frame, so during the spring-back the spacing tracks the handle's distance from center.
            const int dotCount = 6;
            for (int i = 1; i <= dotCount; i++)
            {
                float f = i / (float)dotCount;            // 0..1 from center → handle
                float dx = centerX + (handleX - centerX) * f;
                float dotR = Mathf.Lerp(1.4f, 2.6f, f);    // grow toward the handle
                float a = Mathf.Lerp(0.25f, 0.9f, f);
                FillCircle(p, new Vector2(dx, cy), dotR, new Color(accent.r, accent.g, accent.b, a));
            }

            // Handle: soft glow + bright dot + accent ring.
            FillCircle(p, new Vector2(handleX, cy), r + 3f, new Color(accent.r, accent.g, accent.b, 0.16f));
            FillCircle(p, new Vector2(handleX, cy), r, _handleFill);
            p.strokeColor = accent;
            p.lineWidth = 2f;
            p.BeginPath();
            p.Arc(new Vector2(handleX, cy), r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        private static void FillCircle(Painter2D p, Vector2 c, float radius, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }
    }
}
