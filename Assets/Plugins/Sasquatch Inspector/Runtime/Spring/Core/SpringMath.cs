#if SASQ_BURST
using Unity.Mathematics;
#endif

namespace SasquatchSpring
{
    /// <summary>
    /// Closed-form damped harmonic oscillator step shared by the Burst job and the
    /// managed fallback path.
    /// IMPORTANT: when Burst is present the helpers below must route through
    /// Unity.Mathematics — Burst's JIT cannot compile System.MathF intrinsics
    /// ("Unable to find internal function System.MathF::Exp"). System.MathF is used
    /// only in the no-Burst fallback, which never passes through the Burst compiler.
    /// (com.unity.burst depends on com.unity.mathematics, so SASQ_BURST guarantees
    /// math.* is available.)
    /// </summary>
    public static class SpringMath
    {
        public const float SettleEpsilon = SpringConfig.DefaultSettleEpsilon;
        private const float PI = 3.14159265358979f;

        /// <summary>Advances one spring a single tick: integrates the current value/velocity toward the target for the given frequency/damping, outputting the new position and velocity.</summary>
        public static void Evaluate(
            float deltaTime,
            float current, float velocity, float target,
            float frequency, float dampingRatio, float settleEpsilon,
            out float newPos, out float newVel)
        {
            float omega = 2f * PI * frequency;
            float z = dampingRatio;
            float offset = current - target;

            if (z >= 1f)
            {
                // Critically damped / overdamped: pure exponential decay.
                float exp = Exp(-omega * deltaTime);
                float c1 = offset;
                float c2 = velocity + omega * offset;

                newPos = (c1 + c2 * deltaTime) * exp + target;
                newVel = (velocity - c2 * omega * deltaTime) * exp;
            }
            else
            {
                // Underdamped: decaying oscillation.
                float omegaD = omega * Sqrt(1f - z * z);
                float decay = Exp(-z * omega * deltaTime);
                float c1 = offset;
                float c2 = (velocity + z * omega * offset) / omegaD;

                float cos = Cos(omegaD * deltaTime);
                float sin = Sin(omegaD * deltaTime);

                newPos = decay * (c1 * cos + c2 * sin) + target;
                newVel = decay * ((c2 * omegaD - c1 * z * omega) * cos - (c1 * omegaD + c2 * z * omega) * sin);
            }

            // Snap when within epsilon of the target AND the velocity is small enough
            // that the remaining motion is invisible. Velocity is compared against
            // eps * omega so the criterion has position units regardless of stiffness.
            float eps = settleEpsilon > 0f ? settleEpsilon : SettleEpsilon;
            if (Abs(newPos - target) < eps && Abs(newVel) < eps * omega)
            {
                newPos = target;
                newVel = 0f;
            }

            // Last-resort NaN/Infinity net: a non-finite result (numerical blow-up, or a bad feel that slipped
            // past the entry guards) can NEVER satisfy the epsilon test above, so the spring would write NaN to
            // the bound value every frame forever and never release its slot. Snap to the (entry-guarded,
            // finite) target so the handle settles and self-recycles.
            if (!IsFinite(newPos) || !IsFinite(newVel))
            {
                newPos = target;
                newVel = 0f;
            }
        }

#if SASQ_BURST
        private static float Exp(float x) => math.exp(x);
        private static float Sqrt(float x) => math.sqrt(x);
        private static float Cos(float x) => math.cos(x);
        private static float Sin(float x) => math.sin(x);
        private static float Abs(float x) => math.abs(x);
        private static bool IsFinite(float x) => math.isfinite(x);
#else
        private static float Exp(float x) => System.MathF.Exp(x);
        private static float Sqrt(float x) => System.MathF.Sqrt(x);
        private static float Cos(float x) => System.MathF.Cos(x);
        private static float Sin(float x) => System.MathF.Sin(x);
        private static float Abs(float x) => System.MathF.Abs(x);
        private static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
#endif
    }
}
