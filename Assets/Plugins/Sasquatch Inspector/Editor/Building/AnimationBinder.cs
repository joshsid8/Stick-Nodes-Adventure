using System;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
// Animation tweens + easing go through SasquatchTween / SasquatchEasing (the sole consumers of
// UnityEngine.UIElements.Experimental) so this file carries no direct experimental-API dependency.

namespace SasquatchInspector.Editor
{
    // Non-spring effects are one-shot tweens driven by UI Toolkit's experimental ValueAnimation (no extra
    // update loop); Spring effects run on the spring engine's editor tick.
    /// <summary>Wires a node's configured animations onto its built element as one-shot tweens or spring motions (live preview / inspector only, never in design mode).</summary>
    public static class AnimationBinder
    {
        // playAppear gates only the Appear trigger: the inspector rebuilds its VisualElements on every
        // undo/value-change, and replaying the appear tween each time reads as a flicker. Callers pass false
        // on rebuilds (true only for a genuine first appearance). Suppressing it is safe — the element's
        // default style already IS the animation's end state (opacity 1, no offset/scale), so it just shows
        // normally. Event-driven triggers (Hover/Click/ValueChanged) are always wired, since the recreated
        // elements need their callbacks re-registered every build.
        /// <summary>Wires <paramref name="node"/>'s animations onto <paramref name="element"/>; <paramref name="playAppear"/> gates the one-time Appear tween (callers pass false on rebuilds).</summary>
        public static void Apply(VisualElement element, LayoutNode node, bool playAppear = true)
        {
            if (element == null || node?.Animations == null) return;

            foreach (var anim in node.Animations)
            {
                if (anim == null || anim.Effect == AnimEffect.None) continue;
                var a = anim; // capture per-iteration

                switch (a.Trigger)
                {
                    case AnimTrigger.Appear:
                        // Play shortly after the element is laid out (first appearance only).
                        if (playAppear)
                            element.schedule.Execute(() => Play(element, a)).ExecuteLater(1);
                        break;
                    case AnimTrigger.Hover:
                        // Capture phase so a child Button's Clickable can't swallow the pointer first.
                        element.RegisterCallback<PointerEnterEvent>(_ => Play(element, a), TrickleDown.TrickleDown);
                        break;
                    case AnimTrigger.Click:
                        // Capture phase: a Button's built-in Clickable consumes PointerDownEvent in the
                        // target/bubble phase, so a bubble-phase handler here would never fire on buttons.
                        element.RegisterCallback<PointerDownEvent>(_ => Play(element, a), TrickleDown.TrickleDown);
                        break;
                    case AnimTrigger.ValueChanged:
                        RegisterAnyValueChanged(element, () => Play(element, a));
                        break;
                }
            }
        }

        /// <summary>
        /// Scale Pop's eased progress → uniform scale. Pops from 0.85 up to 1.
        /// <para>
        /// <b>UNCLAMPED, and that is the whole point.</b> Scale Pop eases with <c>OutBack</c> by default
        /// (<see cref="AnimEasing.EaseOut"/> is the model default), and OutBack's defining feature is that it
        /// rises ABOVE 1 near the end before settling — that overshoot IS the pop. <c>Mathf.Lerp</c> clamps
        /// its interpolant to [0,1], so it threw that entire stretch away and the effect peaked at exactly
        /// 1.000000: a plain ease-in to full size, wearing the name of a bounce. The tween itself never
        /// clamped (<c>SasquatchTween</c> interpolates with <c>LerpUnclamped</c>), so this one call was the
        /// only thing standing between the authored curve and the screen.
        /// </para>
        /// Internal rather than inlined so the curve can be pinned directly — the tween advances on wall
        /// clock, which no synchronous test pump can step deterministically.
        /// </summary>
        internal static float ScalePopScale(float t) => Mathf.LerpUnclamped(0.85f, 1f, t);

        /// <summary>Plays a single <see cref="NodeAnimation"/> on <paramref name="element"/> immediately.</summary>
        public static void Play(VisualElement element, NodeAnimation anim)
        {
            if (element == null || anim == null || anim.Effect == AnimEffect.None) return;

            int ms = Mathf.Max(10, Mathf.RoundToInt(anim.DurationSeconds * 1000f));
            Func<float, float> ease = ToEasing(anim.Easing);

            switch (anim.Effect)
            {
                case AnimEffect.Fade:
                    // Finish by RELEASING the inline opacity, not by writing a literal 1. An inline style
                    // outranks every stylesheet rule on the element forever, so settling on 1 would leave the
                    // field permanently blind to USS opacity — including the editor theme's
                    // `.unity-disabled { opacity: .5 }`, i.e. a field with an Appear-Fade could never look
                    // disabled again (a condition's Disable outcome would flip enabledSelf and change nothing).
                    SasquatchTween.Start(element, 0f, 1f, ms, (e, t) => e.style.opacity = t)
                        .Ease(ease)
                        .OnCompleted(() => element.style.opacity = StyleKeyword.Null);
                    break;

                case AnimEffect.ScalePop:
                    SasquatchTween.Start(element, 0f, 1f, ms, (e, t) =>
                        {
                            float s = ScalePopScale(t);
                            e.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(50), Length.Percent(50)));
                            e.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                        })
                        // a touch of overshoot reads as a "pop"
                        .Ease(anim.Easing == AnimEasing.EaseOut ? SasquatchEasing.OutBack : ease)
                        .OnCompleted(() => element.style.scale = new StyleScale(new Scale(Vector3.one)));
                    break;

                case AnimEffect.Slide:
                    SasquatchTween.Start(element, 0f, 1f, ms, (e, t) =>
                        {
                            float y = Mathf.Lerp(14f, 0f, t);
                            e.style.translate = new StyleTranslate(new Translate(0, y, 0));
                            e.style.opacity = t;
                        })
                        .Ease(ease)
                        .OnCompleted(() =>
                        {
                            element.style.translate = new StyleTranslate(new Translate(0, 0, 0));
                            element.style.opacity = StyleKeyword.Null; // hand opacity back to the stylesheet (see Fade)
                        });
                    break;

                case AnimEffect.ColorPulse:
                {
                    var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                    overlay.style.position = Position.Absolute;
                    overlay.style.left = 0; overlay.style.right = 0; overlay.style.top = 0; overlay.style.bottom = 0;
                    overlay.style.borderTopLeftRadius = 4; overlay.style.borderTopRightRadius = 4;
                    overlay.style.borderBottomLeftRadius = 4; overlay.style.borderBottomRightRadius = 4;
                    var start = anim.PulseColor; start.a = 0.5f;
                    overlay.style.backgroundColor = start;
                    element.Add(overlay);
                    SasquatchTween.Start(overlay, 0.5f, 0f, ms, (e, alpha) =>
                        {
                            var col = anim.PulseColor; col.a = alpha;
                            e.style.backgroundColor = col;
                        })
                        .Ease(ease)
                        .OnCompleted(() => overlay.parent?.Remove(overlay));
                    break;
                }

                case AnimEffect.Shake:
                    SasquatchTween.Start(element, 0f, 1f, ms, (e, t) =>
                        {
                            // a few oscillations that decay to zero
                            float x = Mathf.Sin(t * Mathf.PI * 6f) * 6f * (1f - t);
                            e.style.translate = new StyleTranslate(new Translate(x, 0, 0));
                        })
                        .Ease(SasquatchEasing.Linear)
                        .OnCompleted(() => element.style.translate = new StyleTranslate(new Translate(0, 0, 0)));
                    break;

                // Spring effects are driven by the SasquatchSpring engine instead of a tween:
                // repeat triggers (hover spam, click mashing) retarget the same spring rather
                // than stacking animations, and Bounce replaces Easing. Duration maps onto the
                // spring's settle time.
                case AnimEffect.SpringPop:
                {
                    var cfg = SpringConfig(anim);
                    element.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(50), Length.Percent(50)));
                    element.style.scale = new StyleScale(new Scale(new Vector3(0.85f, 0.85f, 1f)));
                    element.SpringScale(1f, cfg);
                    break;
                }

                case AnimEffect.SpringSlide:
                {
                    var cfg = SpringConfig(anim);
                    element.style.translate = new StyleTranslate(new Translate(0, 14, 0));
                    element.style.opacity = 0f;
                    element.SpringTranslate(Vector2.zero, cfg);
                    element.SpringOpacityIn(cfg); // settles by releasing the inline pin (see Fade)
                    break;
                }

                case AnimEffect.SpringShake:
                {
                    // Impulse around the rest position — no visible reset, so it reads
                    // cleanly even when re-triggered mid-shake.
                    var cfg = SpringConfig(anim);
                    element.PunchTranslateX(220f * (0.5f + anim.Bounce), cfg);
                    break;
                }
            }
        }

        private static SpringConfig SpringConfig(NodeAnimation anim)
        {
            return new SpringConfig(Mathf.Max(0.1f, anim.DurationSeconds), anim.Bounce);
        }

        // ChangeEvent is generic per value-type, so register the common ones; they bubble up to the
        // card/field root we attach here.
        private static void RegisterAnyValueChanged(VisualElement e, Action onChanged)
        {
            e.RegisterCallback<ChangeEvent<bool>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<int>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<long>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<float>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<double>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<string>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<Color>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<Vector2>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<Vector3>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<Vector4>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<UnityEngine.Object>>(_ => onChanged());
            e.RegisterCallback<ChangeEvent<Enum>>(_ => onChanged());
        }

        private static Func<float, float> ToEasing(AnimEasing e)
        {
            switch (e)
            {
                case AnimEasing.Linear: return SasquatchEasing.Linear;
                case AnimEasing.EaseIn: return SasquatchEasing.InQuad;
                case AnimEasing.EaseOut: return SasquatchEasing.OutQuad;
                case AnimEasing.EaseInOut: return SasquatchEasing.InOutQuad;
                case AnimEasing.Back: return SasquatchEasing.OutBack;
                default: return SasquatchEasing.OutQuad;
            }
        }
    }
}
