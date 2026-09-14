using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>When a node's animation fires (Appear once / Hover / ValueChanged / Click).</summary>
    public enum AnimTrigger
    {
        Appear = 0,        // plays once when the field first appears (not on every inspector rebuild)
        Hover = 1,         // plays on pointer enter
        ValueChanged = 2,  // plays when the bound value changes
        Click = 3          // plays on pointer down (great for buttons)
    }

    /// <summary>The visual effect an animation plays (fade, scale-pop, slide, color pulse, shake, and spring-physics variants).</summary>
    // APPEND ONLY — serialized as int in layout assets; reordering breaks saved layouts.
    public enum AnimEffect
    {
        None = 0,
        Fade = 1,        // fade in
        ScalePop = 2,    // pop from slightly small to full size
        Slide = 3,       // slide up into place
        ColorPulse = 4,  // a quick accent flash overlay
        Shake = 5,       // horizontal shake (good for error/invalid feedback)
        SpringPop = 6,   // physical scale pop — overshoot driven by Bounce
        SpringSlide = 7, // slides up + fades in, settles with spring physics
        SpringShake = 8  // physical velocity impulse on X — decays naturally
    }

    /// <summary>Easing curve for non-spring animation effects (spring effects use physics instead).</summary>
    public enum AnimEasing
    {
        Linear = 0,
        EaseIn = 1,
        EaseOut = 2,
        EaseInOut = 3,
        Back = 4         // slight overshoot
    }

    /// <summary>One configured animation on a node: a <see cref="AnimTrigger"/>, an <see cref="AnimEffect"/>, timing/easing, and (for spring effects) a bounce amount.</summary>
    [Serializable]
    public class NodeAnimation
    {
        [SerializeField] private AnimTrigger trigger = AnimTrigger.Appear;
        [SerializeField] private AnimEffect effect = AnimEffect.Fade;
        [SerializeField] private float durationSeconds = 0.25f;
        [SerializeField] private AnimEasing easing = AnimEasing.EaseOut;
        [SerializeField] private Color pulseColor = new Color(0.30f, 0.55f, 0.95f, 1f);
        // Spring effects only (easing is ignored for those — physics replaces it).
        // Old assets without this stream load the field-initializer default.
        [SerializeField, Range(0f, 2f)] private float bounce = 0.35f;

        public AnimTrigger Trigger { get => trigger; set => trigger = value; }
        public AnimEffect Effect { get => effect; set => effect = value; }
        public float DurationSeconds { get => durationSeconds; set => durationSeconds = value; }
        public AnimEasing Easing { get => easing; set => easing = value; }
        public Color PulseColor { get => pulseColor; set => pulseColor = value; }
        public float Bounce { get => bounce; set => bounce = value; }

        /// <summary>True for effects driven by the spring engine (Bounce applies, Easing doesn't).</summary>
        public bool IsSpringEffect =>
            effect == AnimEffect.SpringPop || effect == AnimEffect.SpringSlide || effect == AnimEffect.SpringShake;
    }
}
