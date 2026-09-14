using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SasquatchSpring
{
    /// <summary>Runs repeating spring animations — one-direction <see cref="Repeat"/> or back-and-forth <see cref="Yoyo"/> — keyed by an owner object so they can be stopped or queried.</summary>
    public static class SpringLoop
    {
        private static Dictionary<object, CancellationTokenSource> _activeLoops = new Dictionary<object, CancellationTokenSource>();

        /// <summary>Stops the loop registered under <paramref name="key"/> (if any).</summary>
        public static void Stop(object key)
        {
            if (_activeLoops.TryGetValue(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _activeLoops.Remove(key);
            }
        }

        /// <summary>True while a loop is active for <paramref name="key"/>.</summary>
        public static bool IsRunning(object key)
        {
            return _activeLoops.ContainsKey(key);
        }

        /// <summary>
        /// Repeats a single async action forever (one direction), restarting it each time it completes.
        /// Auto-stops when the owner is destroyed. Call <see cref="Stop"/> with the same key to end it.
        /// </summary>
        public static async void Repeat(
            object key,
            MonoBehaviour owner,
            Func<CancellationToken, Task> action
        )
        {
            if (owner == null) return;

            Stop(key);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(owner.destroyCancellationToken);
            _activeLoops[key] = cts;
            var token = cts.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await action(token);
                    if (token.IsCancellationRequested) break;
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (_activeLoops.ContainsKey(key) && _activeLoops[key] == cts)
                {
                    _activeLoops.Remove(key);
                    cts.Dispose();
                }
            }
        }

        /// <summary>
        /// Alternates two async actions forever (forward, then back), restarting the pair each cycle. Auto-stops
        /// when the owner is destroyed; end it with <see cref="Stop"/> using the same key.
        /// </summary>
        public static async void Yoyo(
            object key,
            MonoBehaviour owner,
            Func<CancellationToken, Task> moveOut,
            Func<CancellationToken, Task> moveBack
        )
        {
            if (owner == null) return;

            Stop(key);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(owner.destroyCancellationToken);
            _activeLoops[key] = cts;
            var token = cts.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await moveOut(token);
                    if (token.IsCancellationRequested) break;

                    await Task.Yield();

                    await moveBack(token);
                    if (token.IsCancellationRequested) break;

                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (_activeLoops.ContainsKey(key) && _activeLoops[key] == cts)
                {
                    _activeLoops.Remove(key);
                    cts.Dispose();
                }
            }
        }
    }
}
