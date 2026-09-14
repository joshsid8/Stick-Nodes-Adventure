using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace SasquatchSpring
{
    /// <summary>
    /// Pure C# singleton owning the spring simulation buffers. It does not tick itself:
    /// SpringRuntimeDriver drives it in play mode and in builds, and Sasquatch Inspector's
    /// SpringEditorDriver drives it from EditorApplication.update at edit time.
    /// </summary>
    public sealed class SpringEngine
    {
        private static SpringEngine _instance;

        /// <summary>Lazily creates the engine on first use.</summary>
        public static SpringEngine Instance => _instance ?? (_instance = new SpringEngine());

        /// <summary>
        /// True while a live engine exists. Use this instead of comparing Instance to
        /// null — reading Instance would resurrect a disposed engine.
        /// </summary>
        public static bool HasInstance => _instance != null;

        public NativeArray<float> CurrentValues;
        public NativeArray<float> Velocities;
        public NativeArray<float> Targets;
        public NativeArray<float> Frequencies;
        public NativeArray<float> DampingRatios;
        public NativeArray<float> SettleEpsilons;
        // Per-spring time mode: 1 = advance on unscaled time (ignores Time.timeScale), 0 = scaled.
        public NativeArray<float> Unscaled;

        public NativeArray<float> OutputValues;
        public NativeArray<float> OutputVelocities;

        private readonly Dictionary<int, MotionHandle> _activeHandles = new Dictionary<int, MotionHandle>();
        private readonly List<int> _recycledIds = new List<int>();
        private int _highWaterMark = 0;
        private int _capacity = 2048;
        private bool _isShutdown = false;

        // Reused per-tick scratch (the engine ticks every editor frame forever, so the
        // update loop must not allocate).
        private readonly List<int> _iterScratch = new List<int>(256);
        private readonly HashSet<MotionHandle> _settledHandles = new HashSet<MotionHandle>();
        private readonly List<int> _settledIds = new List<int>(64);

        // Warn-once keys for a target setter that throws an exception the handle itself doesn't catch
        // (see UpdateEngine). Keyed by handle type + exception type + message so distinct faults each
        // surface once instead of spamming every frame.
        private static readonly HashSet<string> _warnedSyncThrows = new HashSet<string>();

        // A finite float is neither NaN nor ±Infinity. Non-finite targets/velocities are the classic
        // "spring never settles, spams the console, leaks its slot" trigger — guarded at every entry point.
        private static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private SpringEngine()
        {
            Allocate(_capacity);
        }

        private void Allocate(int size)
        {
            _capacity = size;
            CurrentValues = new NativeArray<float>(size, Allocator.Persistent);
            Velocities = new NativeArray<float>(size, Allocator.Persistent);
            Targets = new NativeArray<float>(size, Allocator.Persistent);
            Frequencies = new NativeArray<float>(size, Allocator.Persistent);
            DampingRatios = new NativeArray<float>(size, Allocator.Persistent);
            SettleEpsilons = new NativeArray<float>(size, Allocator.Persistent);
            Unscaled = new NativeArray<float>(size, Allocator.Persistent);
            OutputValues = new NativeArray<float>(size, Allocator.Persistent);
            OutputVelocities = new NativeArray<float>(size, Allocator.Persistent);
        }

        private void Resize(int newSize)
        {
            if (_isShutdown || !CurrentValues.IsCreated) return;

            var newCurrent = new NativeArray<float>(newSize, Allocator.Persistent);
            var newVel = new NativeArray<float>(newSize, Allocator.Persistent);
            var newTarg = new NativeArray<float>(newSize, Allocator.Persistent);
            var newFreq = new NativeArray<float>(newSize, Allocator.Persistent);
            var newDamp = new NativeArray<float>(newSize, Allocator.Persistent);
            var newEps = new NativeArray<float>(newSize, Allocator.Persistent);
            var newUnscaled = new NativeArray<float>(newSize, Allocator.Persistent);
            var newOutVal = new NativeArray<float>(newSize, Allocator.Persistent);
            var newOutVel = new NativeArray<float>(newSize, Allocator.Persistent);

            NativeArray<float>.Copy(CurrentValues, newCurrent, CurrentValues.Length);
            NativeArray<float>.Copy(Velocities, newVel, Velocities.Length);
            NativeArray<float>.Copy(Targets, newTarg, Targets.Length);
            NativeArray<float>.Copy(Frequencies, newFreq, Frequencies.Length);
            NativeArray<float>.Copy(DampingRatios, newDamp, DampingRatios.Length);
            NativeArray<float>.Copy(SettleEpsilons, newEps, SettleEpsilons.Length);
            NativeArray<float>.Copy(Unscaled, newUnscaled, Unscaled.Length);
            // Copy the output buffers too: a Resize can be triggered mid-UpdateEngine when a
            // handle's Sync setter registers a new spring at the capacity boundary. The Sync loop
            // is still reading freshly-computed OutputValues/OutputVelocities for handles it hasn't
            // synced yet — a zeroed realloc would snap those springs to 0 for the rest of the tick.
            NativeArray<float>.Copy(OutputValues, newOutVal, OutputValues.Length);
            NativeArray<float>.Copy(OutputVelocities, newOutVel, OutputVelocities.Length);

            CurrentValues.Dispose();
            Velocities.Dispose();
            Targets.Dispose();
            Frequencies.Dispose();
            DampingRatios.Dispose();
            SettleEpsilons.Dispose();
            Unscaled.Dispose();
            OutputValues.Dispose();
            OutputVelocities.Dispose();

            CurrentValues = newCurrent;
            Velocities = newVel;
            Targets = newTarg;
            Frequencies = newFreq;
            DampingRatios = newDamp;
            SettleEpsilons = newEps;
            Unscaled = newUnscaled;
            OutputValues = newOutVal;
            OutputVelocities = newOutVel;

            _capacity = newSize;
        }

        /// <summary>Registers a new spring slot starting at <paramref name="startValue"/>, owned by <paramref name="handle"/>; returns its id.</summary>
        public int Register(float startValue, MotionHandle handle)
        {
            if (_isShutdown || !CurrentValues.IsCreated) return -1;

            int id;
            if (_recycledIds.Count > 0)
            {
                id = _recycledIds[_recycledIds.Count - 1];
                _recycledIds.RemoveAt(_recycledIds.Count - 1);
            }
            else
            {
                id = _highWaterMark++;
            }

            if (id >= _capacity)
            {
                Resize(_capacity * 2);
            }

            if (!CurrentValues.IsCreated) return -1;

            if (!IsFinite(startValue)) startValue = 0f; // never seed a slot with NaN/Infinity
            CurrentValues[id] = startValue;
            Velocities[id] = 0f;
            Targets[id] = startValue;
            Frequencies[id] = 15f;
            DampingRatios[id] = 1f;
            SettleEpsilons[id] = SpringConfig.DefaultSettleEpsilon;
            Unscaled[id] = 0f;

            _activeHandles[id] = handle;
            return id;
        }

        /// <summary>Reads a spring's live velocity (in the sprung value's units/second). 0 if the id is dead.</summary>
        public float GetVelocity(int id)
        {
            if (_isShutdown || !Velocities.IsCreated || id < 0 || id >= Velocities.Length) return 0f;
            return Velocities[id];
        }

        /// <summary>Releases spring <paramref name="id"/>'s slot so it can be recycled.</summary>
        public void Unregister(int id)
        {
            if (_isShutdown) return;

            if (_activeHandles.ContainsKey(id))
            {
                _activeHandles.Remove(id);
                _recycledIds.Add(id);
            }
        }

        /// <summary>Sets spring <paramref name="id"/>'s target and feel (frequency, damping, settle epsilon, scaled/unscaled time).</summary>
        public void UpdateTarget(int id, float target, float freq, float damp, float settleEpsilon = SpringConfig.DefaultSettleEpsilon, bool unscaled = false)
        {
            if (_isShutdown || !Targets.IsCreated || id < 0 || id >= Targets.Length) return;

            // A non-finite target (NaN/Infinity from a bad caller value) can never satisfy the settle test, so
            // the spring would write NaN to the bound value EVERY frame — Unity logs "transform.position is not
            // valid" forever — and never release its slot (a slow native-array leak). Reject the retarget; the
            // spring holds its last good target. Frequency/damping are sanitized too so a bad feel can't poison
            // the integrator into producing NaN.
            if (!IsFinite(target)) return;

            Targets[id] = target;
            Frequencies[id] = IsFinite(freq) ? freq : 15f;
            DampingRatios[id] = IsFinite(damp) ? damp : 1f;
            SettleEpsilons[id] = settleEpsilon > 0f ? settleEpsilon : SpringConfig.DefaultSettleEpsilon;
            Unscaled[id] = unscaled ? 1f : 0f;
        }

        /// <summary>
        /// Test seam: the slot range the per-frame job actually simulates. Exposed so the suite can pin that
        /// an idle scene costs nothing — see <see cref="CompactHighWaterMark"/>.
        /// </summary>
        internal int SimulatedWidth => _highWaterMark;

        /// <summary>Test seam: how many spring slots are currently live.</summary>
        internal int ActiveSlotCount => _activeHandles.Count;

        /// <summary>Adds a one-shot velocity impulse to spring <paramref name="id"/> (used by Punch).</summary>
        public void Impulse(int id, float velocityAmount)
        {
            if (_isShutdown || !Velocities.IsCreated || id < 0 || id >= Velocities.Length) return;
            if (!IsFinite(velocityAmount)) return; // a non-finite punch would blow the spring up to NaN

            Velocities[id] += velocityAmount;
        }

        /// <summary>
        /// Steps the simulation once. Exactly one driver calls this per frame, passing both the
        /// scaled (Time.deltaTime) and unscaled (Time.unscaledDeltaTime) step; each spring picks the
        /// one matching its <see cref="SpringConfig.Unscaled"/> flag. The editor driver passes the
        /// same value for both (there is no game time-scale at edit time).
        /// </summary>
        public void UpdateEngine(float deltaTime) => UpdateEngine(deltaTime, deltaTime);

        /// <summary>Advances all springs one tick (Burst job), using the scaled or unscaled clock per spring, then syncs each handle.</summary>
        public void UpdateEngine(float scaledDt, float unscaledDt)
        {
            // Run while EITHER clock is advancing: when the game is paused (timeScale 0) scaledDt is 0
            // but unscaled springs must still tick. Scaled springs then receive dt 0 and simply hold.
            if (_isShutdown || _highWaterMark == 0 || (scaledDt <= 0f && unscaledDt <= 0f)) return;

            var job = new SpringJob
            {
                ScaledDt = scaledDt < 0f ? 0f : scaledDt,
                UnscaledDt = unscaledDt < 0f ? 0f : unscaledDt,
                Unscaled = Unscaled,
                CurrentValues = CurrentValues,
                Velocities = Velocities,
                Targets = Targets,
                Frequencies = Frequencies,
                DampingRatios = DampingRatios,
                SettleEpsilons = SettleEpsilons,
                NewValues = OutputValues,
                NewVelocities = OutputVelocities
            };

#if SASQ_BURST
            job.Schedule(_highWaterMark, 64).Complete();
#else
            job.Run(_highWaterMark);
#endif

            // Iterate a snapshot: Sync() runs user setters which may start new springs
            // (mutating _activeHandles) mid-loop.
            _iterScratch.Clear();
            foreach (var id in _activeHandles.Keys) _iterScratch.Add(id);

            _settledHandles.Clear();
            _settledIds.Clear();

            foreach (var id in _iterScratch)
            {
                if (!_activeHandles.TryGetValue(id, out var handle)) continue;
                if (id >= OutputValues.Length) continue;

                float newVal = OutputValues[id];
                float newVel = OutputVelocities[id];

                CurrentValues[id] = newVal;
                Velocities[id] = newVel;

                bool settled;
                try
                {
                    settled = handle.Sync(id, newVal, newVel);
                }
                catch (System.Exception e)
                {
                    // The handle catches Missing/NullReference on its own target, but ANY other throw from a
                    // user setter (ArgumentException, ObjectDisposedException, a collection-mutation
                    // InvalidOperationException, …) would otherwise abort the whole tick and re-fire every
                    // frame — starving every spring iterated after this one and permanently spamming the
                    // console. Isolate the offender: cancel it (frees its slots + faults its awaiter), warn
                    // once, and keep ticking the rest. Iterating a snapshot (_iterScratch) means Cancel's
                    // mutation of _activeHandles here is safe.
                    WarnSyncThrewOnce(handle, e);
                    try { handle.Cancel(); } catch { /* best-effort teardown */ }
                    continue;
                }

                if (settled)
                {
                    _settledHandles.Add(handle);
                }
            }

            if (_settledHandles.Count > 0)
            {
                // A settled multi-axis handle (Vector3/Color/Vector4) owns several ids but
                // reports completion once — sweep out every id it still occupies. Sweep the
                // SNAPSHOT, not the live dictionary: settle signals can run user code that
                // immediately re-To()s a settled handle, and sweeping its freshly registered
                // id would recycle a slot the handle still points at (two springs would then
                // fight over one slot).
                foreach (var id in _iterScratch)
                {
                    if (_activeHandles.TryGetValue(id, out var handle) && _settledHandles.Contains(handle))
                    {
                        _settledIds.Add(id);
                    }
                }

                foreach (var id in _settledIds)
                {
                    Unregister(id);
                }
            }

            CompactHighWaterMark();
        }

        /// <summary>
        /// Shrinks the simulated slot range back down when the top slots free up.
        /// </summary>
        /// <remarks>
        /// <c>_highWaterMark</c> only ever grew: it is the width passed to the spring job, so a game that once
        /// had N concurrent springs kept scheduling (and running) an N-wide job EVERY FRAME for the rest of the
        /// process — including while nothing at all was animating, because the early-out above tests
        /// "has anything EVER registered", not "is anything active". Shrinking to one past the highest LIVE id
        /// makes an idle scene cost nothing.
        /// </remarks>
        private void CompactHighWaterMark()
        {
            if (_activeHandles.Count == 0)
            {
                _highWaterMark = 0;
                _recycledIds.Clear();
                return;
            }

            int maxLive = -1;
            foreach (var id in _activeHandles.Keys)
            {
                if (id > maxLive) maxLive = id;
            }

            int newMark = maxLive + 1;
            if (newMark >= _highWaterMark) return;

            _highWaterMark = newMark;

            // Recycled ids at or above the new mark are back in "never allocated" territory — the
            // _highWaterMark++ path will hand them out again, so leaving them on the free list would issue
            // one slot to two springs.
            for (int i = _recycledIds.Count - 1; i >= 0; i--)
            {
                if (_recycledIds[i] >= newMark) _recycledIds.RemoveAt(i);
            }
        }

        // Logs a throwing setter ONCE per handle-type + exception-type + message (the offending handle is
        // canceled right after, so under normal use this fires a single time). Bounded string keys keep a
        // pathological re-target-every-frame-of-a-throwing-setter loop from flooding the console.
        private static void WarnSyncThrewOnce(MotionHandle handle, System.Exception e)
        {
            string key = (handle != null ? handle.GetType().Name : "?") + "|" + e.GetType().Name + "|" + e.Message;
            if (_warnedSyncThrows.Add(key))
                UnityEngine.Debug.LogError("[Sasquatch Spring] A spring's target setter threw and the spring was " +
                                           "canceled to protect the shared engine: " + e);
        }

        /// <summary>
        /// Disposes the native buffers and cancels all live handles. The next Instance
        /// access creates a fresh engine. Called on domain reload / editor quit (by the
        /// editor driver) and on application quit in builds (by the runtime driver).
        /// </summary>
        public static void ShutdownInstance()
        {
            if (_instance == null) return;

            var engine = _instance;
            _instance = null; // null first so handle.Cancel() can't resurrect us
            engine.DisposeInternal();
        }

        private void DisposeInternal()
        {
            _isShutdown = true; // makes Register/Unregister no-ops while we cancel handles

            // Cancel survivors so awaited Tasks resolve and stale ids can't poke a
            // future engine's arrays. Values may repeat (multi-axis handles); Cancel is
            // idempotent.
            var handles = new List<MotionHandle>(_activeHandles.Values);
            foreach (var handle in handles)
            {
                handle.Cancel();
            }

            _activeHandles.Clear();
            _recycledIds.Clear();
            _highWaterMark = 0;

            if (CurrentValues.IsCreated) CurrentValues.Dispose();
            if (Velocities.IsCreated) Velocities.Dispose();
            if (Targets.IsCreated) Targets.Dispose();
            if (Frequencies.IsCreated) Frequencies.Dispose();
            if (DampingRatios.IsCreated) DampingRatios.Dispose();
            if (SettleEpsilons.IsCreated) SettleEpsilons.Dispose();
            if (Unscaled.IsCreated) Unscaled.Dispose();
            if (OutputValues.IsCreated) OutputValues.Dispose();
            if (OutputVelocities.IsCreated) OutputVelocities.Dispose();
        }
    }
}
