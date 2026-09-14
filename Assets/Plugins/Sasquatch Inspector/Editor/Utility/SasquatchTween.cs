using System;
using UnityEngine;
using UnityEngine.UIElements;
#if !SASQ_TWEEN_FALLBACK
using UnityEngine.UIElements.Experimental;
#endif

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The ONE place Sasquatch Inspector depends on <c>UnityEngine.UIElements.Experimental</c> for animation. Every tween in
    /// the package (AnimationBinder effects, the drag-drop pulse) goes through <see cref="Start"/>, and every easing
    /// curve through <see cref="SasquatchEasing"/> — so the day Unity moves/renames the experimental
    /// <c>ValueAnimation</c> API, this single file is the only thing that breaks, and the fix is to define
    /// <c>SASQ_TWEEN_FALLBACK</c> (below), which swaps in a self-contained scheduler-driven tween using only stable
    /// public API. Nothing else in the package references the experimental animation namespace.
    ///
    /// DEFAULT build = the experimental impl (frame-synced, identical to the long-standing behavior). The fallback is
    /// written and compiled-checked now so break-day is a one-symbol flip, not a rewrite.
    /// </summary>
    internal static class SasquatchTween
    {
        /// <summary>
        /// Starts a value tween from <paramref name="from"/> to <paramref name="to"/> over <paramref name="durationMs"/>,
        /// invoking <paramref name="onValue"/>(element, value) each frame. Returns a <see cref="Handle"/> for
        /// chaining <see cref="Handle.Ease"/> / <see cref="Handle.OnCompleted"/> / <see cref="Handle.KeepAlive"/>.
        /// Mirrors the experimental <c>element.experimental.animation.Start(from, to, ms, cb)</c> surface exactly.
        /// </summary>
        internal static Handle Start(VisualElement element, float from, float to, int durationMs, Action<VisualElement, float> onValue)
        {
            if (element == null) return default;
#if SASQ_TWEEN_FALLBACK
            return new Handle(new FallbackTween(element, from, to, durationMs, onValue));
#else
            var anim = element.experimental.animation.Start(from, to, durationMs, (e, t) => onValue?.Invoke(e, t));
            return new Handle(anim);
#endif
        }

        /// <summary>Chainable tween handle. A thin wrapper over the underlying animation so no caller ever names the
        /// experimental type. Each method returns the same handle so calls can chain or be issued separately.</summary>
        internal readonly struct Handle
        {
#if SASQ_TWEEN_FALLBACK
            private readonly FallbackTween _tween;
            internal Handle(FallbackTween tween) { _tween = tween; }
            public Handle Ease(Func<float, float> easing) { _tween?.SetEase(easing); return this; }
            public Handle OnCompleted(Action onCompleted) { _tween?.SetCompleted(onCompleted); return this; }
            public Handle KeepAlive() { return this; } // fallback stays alive until it completes or the element detaches
#else
            private readonly ValueAnimation<float> _anim;
            internal Handle(ValueAnimation<float> anim) { _anim = anim; }
            public Handle Ease(Func<float, float> easing) { _anim?.Ease(easing); return this; }
            public Handle OnCompleted(Action onCompleted) { _anim?.OnCompleted(onCompleted); return this; }
            public Handle KeepAlive() { _anim?.KeepAlive(); return this; }
#endif
        }

#if SASQ_TWEEN_FALLBACK
        // Scheduler-driven fallback tween — stable public API only (schedule.Execute(...).Every(16)). Auto-stops when
        // the element leaves the panel (mirrors the experimental tween's detach teardown). Only compiled when the
        // experimental path is disabled; kept here so the seam is proven, not hypothetical.
        internal sealed class FallbackTween
        {
            private readonly VisualElement _element;
            private readonly float _from, _to;
            private readonly int _durationMs;
            private readonly Action<VisualElement, float> _onValue;
            private Func<float, float> _ease = t => t;
            private Action _onCompleted;
            private readonly IVisualElementScheduledItem _item;
            private long _startMs = -1;
            private bool _done;

            internal FallbackTween(VisualElement element, float from, float to, int durationMs, Action<VisualElement, float> onValue)
            {
                _element = element;
                _from = from;
                _to = to;
                _durationMs = Mathf.Max(1, durationMs);
                _onValue = onValue;
                _item = element.schedule.Execute(Step).Every(16);
            }

            internal void SetEase(Func<float, float> ease) { if (ease != null) _ease = ease; }
            internal void SetCompleted(Action onCompleted) { _onCompleted = onCompleted; }

            private void Step(TimerState ts)
            {
                if (_done) return;
                if (_element == null || _element.panel == null) { _item?.Pause(); return; } // detached — stop silently
                if (_startMs < 0) _startMs = ts.now;

                float p = Mathf.Clamp01((ts.now - _startMs) / (float)_durationMs);
                float v = Mathf.LerpUnclamped(_from, _to, _ease(p));
                _onValue?.Invoke(_element, v);

                if (p >= 1f)
                {
                    _done = true;
                    _item?.Pause();
                    _onCompleted?.Invoke();
                }
            }
        }
#endif
    }

    /// <summary>
    /// The easing curves Sasquatch Inspector animates with, funnelled through one type so no effect code names the
    /// experimental <c>Easing</c> class. On the default build these delegate to Unity's <c>Easing</c> so shipped
    /// motion is byte-identical; under <c>SASQ_TWEEN_FALLBACK</c> they use vendored Robert-Penner implementations
    /// (the standard forms Unity's <c>Easing</c> itself uses) so the curves keep working once the experimental API
    /// is gone. Only the five curves the package actually uses are provided.
    /// </summary>
    internal static class SasquatchEasing
    {
#if SASQ_TWEEN_FALLBACK
        public static float Linear(float t) => t;
        public static float InQuad(float t) => t * t;
        public static float OutQuad(float t) => t * (2f - t);
        public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
        public static float OutBack(float t) { const float s = 1.70158f; t -= 1f; return t * t * ((s + 1f) * t + s) + 1f; }
#else
        public static float Linear(float t) => Easing.Linear(t);
        public static float InQuad(float t) => Easing.InQuad(t);
        public static float OutQuad(float t) => Easing.OutQuad(t);
        public static float InOutQuad(float t) => Easing.InOutQuad(t);
        public static float OutBack(float t) => Easing.OutBack(t);
#endif
    }
}
