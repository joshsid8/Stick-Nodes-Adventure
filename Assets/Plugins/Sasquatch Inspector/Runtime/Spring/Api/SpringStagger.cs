using UnityEngine;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SasquatchSpring
{
    /// <summary>Plays an animation across a list of items with a per-item delay (a staggered cascade).</summary>
    public static class SpringStagger
    {
        /// <summary>
        /// Staggers an animation across a list.
        /// <para>Safe to call multiple times (Stackable). Automatically stops if 'owner' is destroyed.</para>
        /// </summary>
        /// <param name="unscaled">Measure the gap on unscaled time, so a cascade keeps running while the game
        /// is paused (<c>Time.timeScale == 0</c>). Match this to the <c>SpringConfig.Unscaled</c> of the
        /// springs the action starts.</param>
        public static Task Stagger<T>(
            IList<T> items,
            MonoBehaviour owner,
            float staggerTime,
            System.Func<T, int, Task> animationAction,
            bool unscaled = false
        )
        {
            if (items == null || items.Count == 0 || owner == null) return Task.CompletedTask;

            var token = owner.destroyCancellationToken;

            return RunInternal(items, staggerTime, animationAction, token, unscaled);
        }

        private static async Task RunInternal<T>(
            IList<T> items,
            float gap,
            System.Func<T, int, Task> action,
            CancellationToken ct,
            bool unscaled
        )
        {
            List<Task> activeTasks = new List<Task>(items.Count);

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    var item = items[i];

                    if (item is UnityEngine.Object obj && obj == null) continue;
                    if (item == null) continue;

                    activeTasks.Add(RunOne(item, i, action, ct));

                    // Stagger is the gap BETWEEN items — don't wait after the final one (it would
                    // delay the returned Task's completion by one extra gap for no reason).
                    if (gap > 0f && i < items.Count - 1)
                    {
                        await WaitSeconds(gap, unscaled, ct);
                    }
                }

                await Task.WhenAll(activeTasks);
            }
            catch (System.OperationCanceledException)
            {

            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Waits <paramref name="seconds"/> of GAME time by accumulating frame deltas.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>Task.Delay</c>, for two reasons:
        ///   • Task.Delay is wall-clock. The cascade would keep firing items while the springs it starts are
        ///     frozen by <c>Time.timeScale == 0</c> — the whole list lands stacked on the pause frame.
        ///   • Task.Delay needs a timer thread, which WebGL's single-threaded IL2CPP runtime does not have; a
        ///     delay that never completes strands the cascade permanently half-played. Task.Yield posts to
        ///     Unity's SynchronizationContext, which is pumped on the main thread on every platform.
        /// A paused game therefore holds the cascade rather than racing through it, which is the intent.
        /// </remarks>
        private static async Task WaitSeconds(float seconds, bool unscaled, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (ct.IsCancellationRequested) throw new System.OperationCanceledException(ct);
                await Task.Yield();
                elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            }
        }

        private static async Task RunOne<T>(T item, int index, System.Func<T, int, Task> action, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await action(item, index);
            }
            catch (System.OperationCanceledException) { }
            catch (System.Exception e)
            {
                if (item is UnityEngine.Object obj && obj == null) return;
                Debug.LogError($"[Sasquatch Spring] Stagger error on item {index}: {e.Message}");
            }
        }
    }
}
