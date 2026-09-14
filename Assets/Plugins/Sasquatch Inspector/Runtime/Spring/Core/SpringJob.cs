using Unity.Collections;
using Unity.Jobs;
#if SASQ_BURST
using Unity.Burst;
#endif

namespace SasquatchSpring
{
    /// <summary>Parallel job that advances every registered spring one tick (the engine's hot path);
    /// Burst-compiled when the Burst package is installed.</summary>
#if SASQ_BURST
    [BurstCompile]
#endif
    public struct SpringJob : IJobParallelFor
    {
        [ReadOnly] public float ScaledDt;
        [ReadOnly] public float UnscaledDt;
        [ReadOnly] public NativeArray<float> Unscaled;
        [ReadOnly] public NativeArray<float> CurrentValues;
        [ReadOnly] public NativeArray<float> Velocities;
        [ReadOnly] public NativeArray<float> Targets;

        [ReadOnly] public NativeArray<float> Frequencies;
        [ReadOnly] public NativeArray<float> DampingRatios;
        [ReadOnly] public NativeArray<float> SettleEpsilons;

        public NativeArray<float> NewValues;
        public NativeArray<float> NewVelocities;

        /// <summary>Advances spring <paramref name="i"/> by one tick via <see cref="SpringMath.Evaluate"/>.</summary>
        public void Execute(int i)
        {
            // Unscaled springs advance on the unscaled clock so they keep moving while the game is
            // paused; scaled springs use Time.deltaTime (and freeze at timeScale 0, where ScaledDt is 0).
            float dt = Unscaled[i] > 0.5f ? UnscaledDt : ScaledDt;

            SpringMath.Evaluate(
                dt,
                CurrentValues[i], Velocities[i], Targets[i],
                Frequencies[i], DampingRatios[i], SettleEpsilons[i],
                out float newPos, out float newVel);

            NewValues[i] = newPos;
            NewVelocities[i] = newVel;
        }
    }
}
