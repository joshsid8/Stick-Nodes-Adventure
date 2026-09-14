using UnityEngine;

namespace SasquatchSpring
{
    /// <summary>
    /// Describes a spring's feel: <see cref="Duration"/> (settle time), <see cref="Bounce"/> (overshoot), plus
    /// optional settle epsilon, frequency scale, and unscaled-time. Use a preset (<see cref="Bouncy"/>,
    /// <see cref="Snappy"/>, …) or construct one directly.
    /// </summary>
    [System.Serializable]
    public struct SpringConfig
    {
        [Range(0.01f, 10f)] public float Duration;
        [Range(0f, 2f)] public float Bounce;

        // How close (in the sprung value's own units) counts as "arrived". 0 means
        // "use the engine default" (DefaultSettleEpsilon), which suits normalized
        // values like opacity or scale. Pixel-space springs should pass ~0.5f —
        // otherwise the spring rings invisibly for ~a second before settle callbacks
        // (display flips, deferred commits) fire.
        public float SettleEpsilon;

        // Scales the oscillation frequency WITHOUT changing how long the spring takes
        // to settle — the damping ratio is compensated so the decay envelope stays put.
        // This is the axis Duration/Bounce can't reach alone: scaling Duration time-
        // stretches the whole motion, while this keeps the responsiveness and only
        // loosens (<1) or tightens (>1) the wobble. 0 means "use 1" so default-
        // constructed and previously-serialized configs behave stock.
        public float FrequencyScale;

        // When true, this spring advances on UNSCALED time (Time.unscaledDeltaTime) so it keeps
        // animating while the game is paused (Time.timeScale == 0) — pause menus, time-stop UI, etc.
        // Default false (scaled). Appended field, so existing serialized configs deserialize as scaled.
        public bool Unscaled;

        public const float DefaultSettleEpsilon = 0.001f;

        public float Frequency => GetFrequency();
        public float Damping => GetDamping();

        /// <summary>The epsilon the engine should simulate with (0 resolves to the default).</summary>
        public float ResolvedSettleEpsilon => SettleEpsilon > 0f ? SettleEpsilon : DefaultSettleEpsilon;

        /// <summary>The frequency scale the engine should simulate with (0 resolves to 1).</summary>
        public float ResolvedFrequencyScale => FrequencyScale > 0f ? FrequencyScale : 1f;

        /// <summary>
        /// True for a config that was never configured by anyone.
        /// </summary>
        /// <remarks>
        /// Unity's serializer never runs a struct's constructor and never applies a field initializer to a
        /// field added to an already-serialized asset, so a SpringConfig can arrive entirely zero-filled —
        /// which is a legitimate-looking but dead 0.01s / no-bounce spring, i.e. an instant snap. Duration is
        /// an unambiguous sentinel: the [Range(0.01f, 10f)] floor means an authored value can never be exactly
        /// 0. The other fields are checked too so this can never fire on a config someone deliberately built.
        ///
        /// Every constructor sets FrequencyScale = 1, so a hand-written <c>new SpringConfig(0f, 0f)</c> is NOT
        /// unconfigured and keeps its literal meaning — only a zero-filled struct resolves.
        ///
        /// The editor drawers DISPLAY the same Boing preset for an unconfigured config, and only bake it into the
        /// serialized data once the user actually edits one — drawing an inspector must never write. So this fold
        /// is the sole authority everywhere the data is still zero-filled: in a PLAYER, and in the editor right up
        /// until that first edit.
        /// </remarks>
        private bool IsUnconfigured =>
            Duration == 0f && Bounce == 0f && FrequencyScale == 0f && SettleEpsilon == 0f && !Unscaled;

        // The friendly default a never-configured config resolves to — matches the Boing preset below.
        private const float UnconfiguredDuration = 0.8f;
        private const float UnconfiguredBounce = 1.3f;

        private float ResolvedDuration => IsUnconfigured ? UnconfiguredDuration : Duration;
        private float ResolvedBounce => IsUnconfigured ? UnconfiguredBounce : Bounce;

        /// <summary>
        /// This config with the never-configured sentinel already baked in, so the result is safe to MUTATE.
        /// </summary>
        /// <remarks>
        /// <see cref="IsUnconfigured"/> only fires while EVERY field is still zero, so writing any single field
        /// on a zero-filled config destroys the sentinel and leaves Duration 0 — a 0.01s snap. Anything that
        /// adjusts a copy (e.g. a caller supplying an action-appropriate <see cref="SettleEpsilon"/>) must
        /// start from this, not from the raw struct.
        /// </remarks>
        public SpringConfig Resolved
        {
            get
            {
                if (!IsUnconfigured) return this;
                var resolved = this;
                resolved.Duration = UnconfiguredDuration;
                resolved.Bounce = UnconfiguredBounce;
                return resolved;
            }
        }

        /// <summary>Creates a config with the given settle <paramref name="duration"/> and <paramref name="bounce"/> (overshoot).</summary>
        public SpringConfig(float duration, float bounce)
        {
            Duration = duration;
            Bounce = bounce;
            SettleEpsilon = 0f;
            FrequencyScale = 1f;
            Unscaled = false;
        }

        /// <summary>Creates a config with an explicit <paramref name="settleEpsilon"/> (use ~0.5 for pixel-space springs).</summary>
        public SpringConfig(float duration, float bounce, float settleEpsilon)
        {
            Duration = duration;
            Bounce = bounce;
            SettleEpsilon = settleEpsilon;
            FrequencyScale = 1f;
            Unscaled = false;
        }

        /// <summary>
        /// A balanced, standard feel. Good for UI and general movement.
        /// <br/>
        /// <code>
        ///       _
        ///      / \
        /// ____/   \__________
        /// </code>
        /// </summary>
        public static SpringConfig Default => new SpringConfig(0.6f, 0.25f);

        /// <summary>
        /// Very quick, no overshoot. Good for snappy UI toggles or camera cuts.
        /// <br/>
        /// <code>
        ///      __
        ///    _/
        /// __/   _________
        /// </code>
        /// </summary>
        public static SpringConfig Snappy => new SpringConfig(0.3f, 0f);

        /// <summary>
        /// Playful and energetic. Notable overshoot and oscillation.
        /// <br/>
        /// <code>
        ///       /\
        ///      /  \  _
        /// ____/    \/ \______
        /// </code>
        /// </summary>
        public static SpringConfig Bouncy => new SpringConfig(0.6f, 0.6f);

        /// <summary>
        /// An energetic flick — a big overshoot and several fast wobbles before it settles, like
        /// a flicked door-spring. (boing!) High bounce packed into a short duration reads as a
        /// quick, lively vibration rather than a slow squishy wobble.
        /// <br/>
        /// <code>
        ///       /\
        ///      /  \    /\
        /// ____/    \  /  \  /\
        ///           \/    \/  \_
        /// </code>
        /// </summary>
        public static SpringConfig Boing => new SpringConfig(0.8f, 1.3f);

        /// <summary>
        /// Slow, smooth, slightly loose travel with one soft overshoot. The long duration spreads
        /// the single gentle bounce out into an unhurried drift — good for large or lazy moves.
        /// <br/>
        /// <code>
        ///        _
        ///       / \    _
        /// _____/   \__/ \____
        /// </code>
        /// </summary>
        public static SpringConfig Glide => new SpringConfig(1.2f, 0.5f);

        /// <summary>
        /// Slow and heavy. No bounce. Feels like a heavy door closing.
        /// <br/>
        /// <code>
        ///          _
        ///        _/
        /// ______/   _________
        /// </code>
        /// </summary>
        public static SpringConfig Heavy => new SpringConfig(1.5f, 0f);

        /// <summary>
        /// Extremely precise and mechanical. Very fast, absolutely no bounce.
        /// <br/>
        /// <code>
        ///      _
        ///    _|
        /// __|   _____________
        /// </code>
        /// </summary>
        public static SpringConfig Direct => new SpringConfig(0.15f, 0f);

        /// <summary>
        /// A soft, subtle ease-out feel. Good for hovering or background elements.
        /// <br/>
        /// <code>
        ///         __
        ///       _/
        /// _____/    _________
        /// </code>
        /// </summary>
        public static SpringConfig Soft => new SpringConfig(1.0f, 0.1f);

        private float GetDamping()
        {
            // Compensates FrequencyScale so the decay envelope (z * omega) — and with it
            // the settle time — stays constant. Clamped at 1 because SpringMath treats
            // every z >= 1 as critically damped anyway.
            return Mathf.Clamp(GetBaseDamping() / ResolvedFrequencyScale, 0.01f, 1f);
        }

        private float GetBaseDamping()
        {
            float bounce = ResolvedBounce;
            if (bounce <= 1f)
            {
                // Standard range: Bounce 0..1 -> damping 1.0..0.2 — the mapping the presets below are tuned for.
                return Mathf.Lerp(1f, 0.2f, bounce);
            }
            else
            {
                // Super Bounce Range (1 to 2) -> Damping (0.2 to 0.01)
                // We map the extra bounce to the remaining tiny slice of damping.
                // We clamp at 0.01f to prevent the "Explosion" (Negative Damping).
                float t = bounce - 1f;
                return Mathf.Lerp(0.2f, 0.01f, t);
            }
        }

        private float GetFrequency()
        {
            float d = GetBaseDamping();
            // The coefficient is no longer a flat 0.64 — it rises with the (bounce-derived) base damping
            // so Duration stays the true settle time across the whole Bounce range (see SettleCoefficient).
            // Keying it to the BASE damping — not the FrequencyScale-adjusted ratio — keeps the decay
            // envelope (z*omega) independent of FrequencyScale, preserving that knob's "same settle time"
            // contract. For the presets (FrequencyScale 1) base damping IS the resolved ratio, so the
            // no-bounce settle-time fix lands exactly as intended.
            return ResolvedFrequencyScale * SettleCoefficient(d) / (d * Mathf.Max(0.01f, ResolvedDuration));
        }

        /// <summary>
        /// Frequency coefficient that keeps <see cref="Duration"/> ≈ the 1.8% settle time for any
        /// Bounce. For a lightly damped spring the decay envelope is a clean exponential and 0.64 is
        /// exact (the base damping cancels). As the damping ratio z → 1 the envelope prefactor
        /// 1/√(1−z²) — and, at critical damping, the (1+ωt) tail — stretch the real settle time up to
        /// ~1.45× Duration; the coefficient rises toward the critically-damped value to compensate,
        /// so no-bounce presets no longer drag. Exact for the underdamped envelope, capped at the
        /// critical value (the underdamped formula would otherwise diverge as z → 1).
        /// </summary>
        private static float SettleCoefficient(float z)
        {
            const float ln18 = 4.017f;        // −ln(0.018): decay magnitude needed to reach 1.8%
            const float twoPi = 6.2831853f;
            const float criticalC = 0.95f;    // settles critical damping to 1.8% at exactly t = Duration
            if (z >= 1f) return criticalC;
            float c = (ln18 - 0.5f * Mathf.Log(1f - z * z)) / twoPi;
            return Mathf.Min(c, criticalC);
        }
    }
}
