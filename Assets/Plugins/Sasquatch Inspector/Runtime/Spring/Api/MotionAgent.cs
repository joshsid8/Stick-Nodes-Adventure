using UnityEngine;
using System.Collections.Generic;

namespace SasquatchSpring
{
    /// <summary>Per-GameObject component (auto-added, hidden from the Add Component menu and the Inspector) that
    /// owns the object's <see cref="MotionHandle"/>s, keyed by <see cref="MotionKey"/>, and can snap or cancel
    /// them as a group. It lives for the GameObject's lifetime and is freed with it — settled handles are kept
    /// cached and REUSED (a later spring on the same key re-registers the existing handle) rather than
    /// re-allocated, and keeping them cached is what guarantees a single handle per key (no two springs
    /// fighting over one property).</summary>
    [AddComponentMenu("")] // internal, auto-managed — never surface it in the Add Component menu
    public class MotionAgent : MonoBehaviour
    {
        // Keyed by MotionKey (target + slot), NOT by a bare string: the agent is per-GameObject, so a bare
        // "Alpha"/"Color"/"Volume" would be shared by every component on the object and the second one to
        // spring would drive the first one's property. See MotionKey's remarks.
        private readonly Dictionary<MotionKey, MotionHandle> _handles = new Dictionary<MotionKey, MotionHandle>();

        // Scratch for the prune/cancel sweeps — a dictionary can't be mutated while it is being iterated,
        // and these run on the spring path, so they must not allocate per call.
        private readonly List<MotionKey> _keyScratch = new List<MotionKey>();

        // Above this many cached handles, a keyless Spring.To() call sweeps out its settled dynamic keys.
        // Constant-name slots are bounded by the API surface; delegate slots are not (a caller lambda that
        // captures a per-call local is a NEW delegate, and therefore a new entry, every single call).
        private const int PruneThreshold = 64;

        /// <summary>Returns the existing handle for <paramref name="key"/>, or creates one via <paramref name="factory"/>.</summary>
        /// <remarks>Pass a <c>static</c> lambda and hand the captured state through <paramref name="arg"/> — see
        /// <see cref="MotionHandleCache.GetHandle{TKey,TArg,T}"/> for why that matters on the per-frame path.</remarks>
        public T GetHandle<TArg, T>(MotionKey key, TArg arg, System.Func<TArg, T> factory) where T : MotionHandle
        {
            return MotionHandleCache.GetHandle(_handles, key, arg, factory);
        }

        /// <summary>Returns the cached handle for <paramref name="key"/> if present AND still of type
        /// <typeparamref name="T"/>; otherwise null. Allocation-free (no factory) so per-frame helpers can
        /// retarget an existing spring without rebuilding their getter/setter closures each frame.</summary>
        public T TryGetHandle<T>(MotionKey key) where T : MotionHandle
        {
            return MotionHandleCache.TryGetHandle<MotionKey, T>(_handles, key);
        }

        /// <summary>Immediately snaps every active motion on this object to its target, skipping the remaining animation.</summary>
        public void SnapAll()
        {
            foreach (var handle in _handles.Values)
            {
                handle.Snap();
            }
        }

        /// <summary>Cancels and forgets every handle on this GameObject. Awaiting Tasks resolve as canceled.</summary>
        public void CancelAll()
        {
            foreach (var handle in _handles.Values)
                handle.Cancel();
            _handles.Clear();
        }

        /// <summary>
        /// Cancels and forgets only the handles that write to <paramref name="target"/>, leaving the other
        /// components on this GameObject animating. This is the granularity <see cref="MotionKey"/> buys us.
        /// </summary>
        public void CancelAllFor(object target)
        {
            if (target == null) return;

            _keyScratch.Clear();
            foreach (var pair in _handles)
            {
                if (ReferenceEquals(pair.Key.Target, target)) _keyScratch.Add(pair.Key);
            }

            foreach (var key in _keyScratch)
            {
                if (_handles.TryGetValue(key, out var handle))
                {
                    handle.Cancel();
                    _handles.Remove(key);
                }
            }
            _keyScratch.Clear();
        }

        /// <summary>
        /// Cancels and forgets the single handle at <paramref name="key"/>. Returns true if one was there.
        /// </summary>
        public bool CancelHandle(MotionKey key)
        {
            if (!_handles.TryGetValue(key, out var handle)) return false;
            handle.Cancel();
            _handles.Remove(key);
            return true;
        }

        /// <summary>
        /// Self-healing sweep for the unbounded key space: drops settled handles whose slot is a delegate
        /// (a keyless <c>Spring.To</c>). Constant-name slots are never pruned — keeping those cached is what
        /// makes the per-frame retarget path allocation-free. No-op below <see cref="PruneThreshold"/> entries.
        /// </summary>
        internal void PruneCompletedDynamicHandles()
        {
            if (_handles.Count < PruneThreshold) return;

            _keyScratch.Clear();
            foreach (var pair in _handles)
            {
                if (pair.Key.IsDynamicSlot && pair.Value.IsComplete) _keyScratch.Add(pair.Key);
            }

            foreach (var key in _keyScratch) _handles.Remove(key);
            _keyScratch.Clear();
        }

        // NOTE: there is deliberately no Update()/self-destruct. Settled handles are kept cached and reused (see
        // the class summary), so _handles never empties on its own — a "Destroy(this) once the count hits 0"
        // sweep could never fire, and would cost a per-frame no-op on every ever-animated object. The agent is
        // freed with its GameObject via OnDestroy below.

        private void OnDestroy()
        {
            foreach (var handle in _handles.Values)
                handle.Cancel();
            _handles.Clear();
        }
    }
}
