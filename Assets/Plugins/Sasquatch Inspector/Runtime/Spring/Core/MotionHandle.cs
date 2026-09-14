using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SasquatchSpring
{
    /// <summary>
    /// Base class for a single in-flight spring: awaitable completion (<see cref="WaitFor"/> / <see cref="OnComplete"/>)
    /// plus <see cref="Snap"/> / <see cref="Cancel"/> control. Subclasses drive a float, Vector3, Vector4 or Color.
    /// </summary>
    public abstract class MotionHandle
    {
        protected TaskCompletionSource<bool> _tcs;
        protected AwaitStrategy _strategy;
        // True once this handle has been canceled without a live _tcs to record it in. See SignalTaskCanceled.
        protected bool _canceled;

        protected const float SETTLE_POS_THRESHOLD = 0.0001f;
        protected const float SETTLE_VEL_THRESHOLD = 0.0001f;
        protected const float VISUAL_THRESHOLD_FLOAT = 0.001f;
        protected const float VISUAL_THRESHOLD_VECTOR = 0.001f;

        /// <summary>Returns a Task that completes when the spring reaches the given <see cref="AwaitStrategy"/>; faults (canceled) if the spring is canceled.</summary>
        public Task WaitFor(AwaitStrategy strategy)
        {
            // CANCELED OUTRANKS EVERYTHING, and must be tested first. Cancel() often cannot record itself in
            // _tcs: _tcs is null when nobody has awaited yet, and TrySetCanceled is a no-op when _tcs already
            // completed on a PREVIOUS cycle. In both cases the branches below would report the dead spring as
            // a clean settle, and the caller's OnComplete would run for a destroyed object — exactly what the
            // comment below says must not happen. _canceled is the authoritative record; BeginCycle() clears
            // it whenever a fresh (re)target succeeds, so a live handle never reaches here with it set.
            if (_canceled)
            {
                var dead = NewCompletionSource();
                dead.TrySetCanceled();
                return dead.Task;
            }

            if (_tcs != null && _tcs.Task.IsCompleted)
            {
                // A canceled spring must surface as canceled — returning CompletedTask
                // here would make awaiters treat a dead spring as "settled" and run
                // their completion logic (display flips, deferred commits) spuriously.
                return _tcs.Task.IsCanceled ? _tcs.Task : Task.CompletedTask;
            }

            // Nobody has ever awaited this handle AND it is no longer registered — it settled before anyone
            // asked. Falling through would mint a fresh TaskCompletionSource that NOTHING can ever complete:
            // no engine tick reaches a handle with no live ids, so the caller would await forever.
            if (_tcs == null && !IsActive) return Task.CompletedTask;

            if (_tcs == null) _tcs = NewCompletionSource();
            _strategy = strategy;
            return _tcs.Task;
        }

        /// <summary>
        /// Reads a getter that may touch a destroyed UnityEngine.Object.
        /// </summary>
        /// <remarks>
        /// The Sync path has always caught Missing/NullReference around the SETTER, but To()/Snap() re-read the
        /// cached handle's GETTER outside any guard — and a cached handle outlives its target (the agent lives
        /// on the GameObject; the component it writes to can be destroyed on its own). That threw
        /// MissingReferenceException straight out of the public spring API into the caller.
        /// </remarks>
        protected static bool TryRead<T>(Func<T> getter, out T value)
        {
            try
            {
                value = getter();
                return true;
            }
            catch (MissingReferenceException) { value = default; return false; }
            catch (NullReferenceException) { value = default; return false; }
        }

        /// <summary>Engine tick callback: writes the latest sprung value to the target and returns true once this handle has settled (so the engine can recycle its slots).</summary>
        public abstract bool Sync(int id, float value, float velocity);
        /// <summary>Stops the spring immediately, unregisters it, and marks any awaiter canceled.</summary>
        public abstract void Cancel();
        /// <summary>Jumps the value straight to its target, skipping the rest of the motion.</summary>
        public abstract void Snap();

        /// <summary>
        /// FINISHES the motion: writes the exact target value through the setter, releases the spring, and
        /// resolves awaiters as a normal SETTLE. The end state is identical to letting the spring run out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is <see cref="Cancel"/>'s counterpart for the case where the motion must stop but the element
        /// must not be left mid-flight. Cancel stops the driver and leaves the value wherever it stood — and
        /// because these springs drive INLINE styles, "wherever it stood" is written on the element by its own
        /// inline style with nothing alive to move it again. A canceled fade-in is an element pinned invisible;
        /// a canceled pop is an element pinned at 85% size.
        /// </para>
        /// <para>
        /// Distinct from <see cref="Snap"/>, which RETARGETS the spring to wherever the value happens to be
        /// right now (it stops the motion by moving the goalposts). Complete moves the value to the goal.
        /// </para>
        /// <para>
        /// Virtual rather than abstract deliberately: <see cref="MotionHandle"/> is public, so a new abstract
        /// member would break any subclass outside this package. The default is the old behaviour, and every
        /// handle in the package overrides it — <c>TestEveryMotionHandleOverridesComplete</c> is what keeps a
        /// future subclass from silently inheriting the cancel.
        /// </para>
        /// </remarks>
        public virtual void Complete() => Cancel();

        /// <summary>True while this spring is registered and still moving toward its target.</summary>
        public abstract bool IsActive { get; }

        /// <summary>True once the spring has settled, been canceled, or its owner was destroyed.</summary>
        public bool IsComplete => !IsActive;

        /// <summary>
        /// Fluent, await-free completion callback: runs <paramref name="onComplete"/> on the main
        /// thread once the spring settles. Skipped if the spring is canceled (owner destroyed / Cancel()).
        /// Handy for devs who hold a handle (from <see cref="Spring"/>.To/Move/etc.) and don't want to await.
        /// </summary>
        public MotionHandle OnComplete(Action onComplete, AwaitStrategy strategy = AwaitStrategy.PhysicalSettling)
        {
            if (onComplete != null) RunOnComplete(onComplete, strategy);
            return this;
        }

        private async void RunOnComplete(Action onComplete, AwaitStrategy strategy)
        {
            try { await WaitFor(strategy); }
            catch (OperationCanceledException) { return; }
            onComplete();
        }

        protected void SignalTaskComplete() => _tcs?.TrySetResult(true);

        // _canceled is the record Cancel() can't leave in _tcs when nobody has awaited yet (_tcs is null,
        // so TrySetCanceled has nothing to set). WaitFor reads it to tell "settled" from "canceled".
        protected void SignalTaskCanceled()
        {
            _canceled = true;
            _tcs?.TrySetCanceled();
        }

        /// <summary>Clears the canceled mark as a fresh (re)target begins. Call from every To()/Punch().</summary>
        protected void BeginCycle()
        {
            _canceled = false;
            if (_tcs != null && _tcs.Task.IsCompleted) _tcs = NewCompletionSource();
        }

        // Async continuations: without this flag, TrySetResult would run awaiters
        // INLINE — i.e. user completion code (commits, hierarchy changes, new springs)
        // would execute inside the engine's Sync loop, mid-tick. Posting to the sync
        // context defers them to the next editor/player pump instead.
        protected static TaskCompletionSource<bool> NewCompletionSource() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // FLOAT HANDLE----------------------------------------------------------------------------------------------------------------------
    /// <summary>A spring handle driving a single float (e.g. an alpha or scalar).</summary>
    public class MotionHandleFloat : MotionHandle
    {
        public int ID;
        private System.Func<float> _getter;
        private System.Action<float> _setter;
        private float _target;

        /// <summary>Registers a float spring that reads/writes via the supplied getter/setter.</summary>
        public MotionHandleFloat(System.Func<float> getter, System.Action<float> setter)
        {
            _getter = getter;
            _setter = setter;
            float val = _getter();
            _target = val;
            ID = SpringEngine.Instance.Register(_getter(), this);
        }

        public override bool IsActive => ID != -1;

        /// <summary>Live velocity in the sprung value's units/second (0 once settled).</summary>
        public float Velocity => (ID != -1 && SpringEngine.HasInstance) ? SpringEngine.Instance.GetVelocity(ID) : 0f;

        /// <inheritdoc/>
        public override void Snap()
        {
            if (!TryRead(_getter, out float current)) { Cancel(); return; }
            _target = current;
            if (ID != -1)
            {
                SpringEngine.Instance.CurrentValues[ID] = current;
                SpringEngine.Instance.Velocities[ID] = 0f;
                SpringEngine.Instance.Targets[ID] = current;
            }
        }

        /// <summary>(Re)targets the spring toward <paramref name="target"/> using <paramref name="config"/>; re-registers if it had already settled.</summary>
        public void To(float target, SpringConfig config)
        {
            _target = target;
            if (!TryRead(_getter, out float currentReal)) { Cancel(); return; }

            if (ID == -1) ID = SpringEngine.Instance.Register(currentReal, this);
            else
            {
                float currentEngine = SpringEngine.Instance.CurrentValues[ID];
                if (Mathf.Abs(currentReal - currentEngine) > 0.01f)
                {
                    SpringEngine.Instance.CurrentValues[ID] = currentReal;
                    SpringEngine.Instance.Velocities[ID] = 0f;
                }
            }

            if (ID == -1) return;

            SpringEngine.Instance.UpdateTarget(ID, target, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            BeginCycle();
        }

        /// <inheritdoc/>
        public override bool Sync(int id, float value, float velocity)
        {
            try
            {
                _setter(value);
            }
            catch (MissingReferenceException) { return HandleCrash(); }
            catch (NullReferenceException) { return HandleCrash(); }

            float dist = Mathf.Abs(value - _target);
            bool isSettled = dist < SETTLE_POS_THRESHOLD && Mathf.Abs(velocity) < SETTLE_VEL_THRESHOLD;

            if (_tcs != null && !_tcs.Task.IsCompleted)
            {
                bool shouldSignal = (_strategy == AwaitStrategy.PhysicalSettling && isSettled) ||
                                    (_strategy == AwaitStrategy.VisualArrival && dist < VISUAL_THRESHOLD_FLOAT);
                if (shouldSignal) SignalTaskComplete();
            }

            if (isSettled)
            {
                _setter(_target);
                SignalTaskComplete();
                ID = -1;
                return true; 
            }
            return false;
        }

        private bool HandleCrash()
        {
            SignalTaskCanceled();
            ID = -1;
            return true;
        }

        /// <inheritdoc/>
        public override void Cancel()
        {
            // HasInstance, not Instance != null: the lazy Instance getter would
            // resurrect a disposed engine. Ids are cleared so a later To() re-registers
            // instead of poking a recycled slot.
            if (SpringEngine.HasInstance && ID != -1) SpringEngine.Instance.Unregister(ID);
            ID = -1;
            SignalTaskCanceled();
        }

        /// <inheritdoc/>
        public override void Complete()
        {
            // Already settled (or never registered): the value is at its target and the handle may be a
            // CACHED one whose property has since been written by other code. Re-writing _target here would
            // clobber that later value, so an inactive handle is a no-op.
            if (!IsActive) return;

            // Through the SETTER, not the engine slot — this is the same call Sync's settle branch makes, and
            // the setter is where the end state is decided. SpringOpacityIn's setter hands opacity BACK to the
            // stylesheet at the top of its range; poking the engine value instead would settle on an inline 1
            // and pin the element opaque for the rest of its life.
            try { _setter(_target); }
            catch (MissingReferenceException) { Cancel(); return; }
            catch (NullReferenceException) { Cancel(); return; }

            if (SpringEngine.HasInstance && ID != -1) SpringEngine.Instance.Unregister(ID);
            ID = -1;
            SignalTaskComplete();
        }
    }

    // VECTOR3 HANDLE----------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>A spring handle driving a Vector3 (one sub-spring per axis); settles only when all axes are at rest.</summary>
    public class MotionHandleVector3 : MotionHandle
    {
        public int ID_X, ID_Y, ID_Z;
        private System.Func<Vector3> _getter;
        private System.Action<Vector3> _setter;
        private Vector3 _current, _target;
        // Per-axis "this axis is at rest" flags. Settle is only declared when ALL three are
        // true: deciding from a single axis would tear the whole handle down the instant a
        // static axis was synced ahead of a moving sibling (see Sync).
        private bool _settledX, _settledY, _settledZ;

        /// <summary>Registers a Vector3 spring (one sub-spring per axis) reading/writing via the getter/setter.</summary>
        public MotionHandleVector3(System.Func<Vector3> getter, System.Action<Vector3> setter)
        {
            _getter = getter;
            _setter = setter;
            _current = _getter();
            _target = _current;
            Register();
        }

        /// <summary>Swaps the setter the spring writes through (e.g. after the target element is rebuilt).</summary>
        public void UpdateSetter(System.Action<Vector3> newSetter) => _setter = newSetter;

        public override bool IsActive => ID_X != -1;

        /// <summary>Live per-axis velocity in units/second (zero once settled).</summary>
        public Vector3 Velocity => (ID_X != -1 && SpringEngine.HasInstance)
            ? new Vector3(SpringEngine.Instance.GetVelocity(ID_X), SpringEngine.Instance.GetVelocity(ID_Y), SpringEngine.Instance.GetVelocity(ID_Z))
            : Vector3.zero;

        private void Register()
        {
            // -1 explicitly, not just `return`: these are plain int fields that default to 0, and 0 is a VALID
            // slot id — leaving them would make IsActive report true and point the handle at another spring.
            // SignalTaskCanceled records WHY we bailed: To()/Punch() exit on the ID_X == -1 test right after
            // this, without calling Cancel(), so without the mark WaitFor would report a dead target as a
            // clean settle. (MotionHandleFloat reads its getter in To() directly and cancels there.)
            if (!TryRead(_getter, out _current)) { ID_X = -1; ID_Y = -1; ID_Z = -1; SignalTaskCanceled(); return; }
            ID_X = SpringEngine.Instance.Register(_current.x, this);
            ID_Y = SpringEngine.Instance.Register(_current.y, this);
            ID_Z = SpringEngine.Instance.Register(_current.z, this);
        }

        /// <inheritdoc/>
        public override void Snap()
        {
            if (!TryRead(_getter, out _current)) { Cancel(); return; }
            _target = _current;
            if (ID_X != -1) { SpringEngine.Instance.CurrentValues[ID_X] = _current.x; SpringEngine.Instance.Velocities[ID_X] = 0; SpringEngine.Instance.Targets[ID_X] = _current.x; }
            if (ID_Y != -1) { SpringEngine.Instance.CurrentValues[ID_Y] = _current.y; SpringEngine.Instance.Velocities[ID_Y] = 0; SpringEngine.Instance.Targets[ID_Y] = _current.y; }
            if (ID_Z != -1) { SpringEngine.Instance.CurrentValues[ID_Z] = _current.z; SpringEngine.Instance.Velocities[ID_Z] = 0; SpringEngine.Instance.Targets[ID_Z] = _current.z; }
        }

        /// <summary>(Re)targets the spring toward <paramref name="target"/> using <paramref name="config"/>.</summary>
        public void To(Vector3 target, SpringConfig config)
        {
            _target = target;
            _settledX = _settledY = _settledZ = false;
            if (ID_X == -1) Register();
            if (ID_X == -1) return;

            // TELEPORT CHECK
            if (!TryRead(_getter, out Vector3 currentReal)) { Cancel(); return; }
            Vector3 currentEngine = new Vector3(SpringEngine.Instance.CurrentValues[ID_X], SpringEngine.Instance.CurrentValues[ID_Y], SpringEngine.Instance.CurrentValues[ID_Z]);

            if (Vector3.Distance(currentReal, currentEngine) > 0.01f)
            {
                SpringEngine.Instance.CurrentValues[ID_X] = currentReal.x; SpringEngine.Instance.Velocities[ID_X] = 0f;
                SpringEngine.Instance.CurrentValues[ID_Y] = currentReal.y; SpringEngine.Instance.Velocities[ID_Y] = 0f;
                SpringEngine.Instance.CurrentValues[ID_Z] = currentReal.z; SpringEngine.Instance.Velocities[ID_Z] = 0f;
            }

            SpringEngine.Instance.UpdateTarget(ID_X, target.x, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_Y, target.y, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_Z, target.z, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);

            BeginCycle();
        }

        /// <summary>Applies a one-shot velocity impulse (a "punch") while keeping the current target.</summary>
        public void Punch(Vector3 velocityVector, SpringConfig config)
        {
            if (ID_X == -1) Register();
            if (ID_X == -1) return;

            // A fresh impulse: clear the rest flags so an axis that happens to start at its
            // target (e.g. an upward-only Punch leaves X/Z at rest) can't pre-settle the handle
            // before the impulsed axis has a chance to move.
            _settledX = _settledY = _settledZ = false;

            SpringEngine.Instance.UpdateTarget(ID_X, _target.x, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_Y, _target.y, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_Z, _target.z, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);

            SpringEngine.Instance.Impulse(ID_X, velocityVector.x);
            SpringEngine.Instance.Impulse(ID_Y, velocityVector.y);
            SpringEngine.Instance.Impulse(ID_Z, velocityVector.z);

            // A punch is a fresh cycle just like a To(). Without this the second `await t.PunchScale(...)`
            // sees the FIRST punch's already-completed _tcs and returns instantly, so the caller's await
            // falls straight through while the object is still visibly wobbling.
            BeginCycle();
        }

        /// <inheritdoc/>
        public override bool Sync(int id, float value, float velocity)
        {
            // Record THIS axis's rest state from its own value+velocity. Sync runs once per axis
            // per frame and _current is only partially updated mid-sweep, so a per-axis flag is the
            // only order-independent way to know the axis is done — using the whole-vector distance
            // here would read ~0 for a not-yet-synced moving sibling and false-settle the handle.
            bool atRest = Mathf.Abs(velocity) < SETTLE_VEL_THRESHOLD;
            if (id == ID_X) { _current.x = value; _settledX = atRest && Mathf.Abs(value - _target.x) < SETTLE_POS_THRESHOLD; }
            if (id == ID_Y) { _current.y = value; _settledY = atRest && Mathf.Abs(value - _target.y) < SETTLE_POS_THRESHOLD; }
            if (id == ID_Z) { _current.z = value; _settledZ = atRest && Mathf.Abs(value - _target.z) < SETTLE_POS_THRESHOLD; }

            try
            {
                _setter(_current);
            }
            catch (MissingReferenceException) { return HandleCrash(); }
            catch (NullReferenceException) { return HandleCrash(); }

            float dist = Vector3.Distance(_current, _target);
            bool isSettled = _settledX && _settledY && _settledZ;

            if (_tcs != null && !_tcs.Task.IsCompleted)
            {
                bool shouldSignal = (_strategy == AwaitStrategy.PhysicalSettling && isSettled) ||
                                    (_strategy == AwaitStrategy.VisualArrival && dist < VISUAL_THRESHOLD_VECTOR);
                if (shouldSignal) SignalTaskComplete();
            }

            if (isSettled)
            {
                // Every axis is at rest — snap to the exact target and sweep all sibling ids
                // (the engine recycles them once we return true).
                _current = _target;
                _setter(_target);
                SignalTaskComplete();
                ID_X = -1; ID_Y = -1; ID_Z = -1;
                return true;
            }
            return false;
        }

        private bool HandleCrash()
        {
            SignalTaskCanceled();
            ID_X = -1; ID_Y = -1; ID_Z = -1;
            return true;
        }

        /// <inheritdoc/>
        public override void Cancel()
        {
            if (SpringEngine.HasInstance && ID_X != -1)
            {
                SpringEngine.Instance.Unregister(ID_X);
                SpringEngine.Instance.Unregister(ID_Y);
                SpringEngine.Instance.Unregister(ID_Z);
            }
            ID_X = -1; ID_Y = -1; ID_Z = -1;
            SignalTaskCanceled();
        }

        /// <inheritdoc/>
        public override void Complete()
        {
            if (!IsActive) return;   // see MotionHandleFloat.Complete

            _current = _target;
            try { _setter(_target); }
            catch (MissingReferenceException) { Cancel(); return; }
            catch (NullReferenceException) { Cancel(); return; }

            if (SpringEngine.HasInstance && ID_X != -1)
            {
                SpringEngine.Instance.Unregister(ID_X);
                SpringEngine.Instance.Unregister(ID_Y);
                SpringEngine.Instance.Unregister(ID_Z);
            }
            ID_X = -1; ID_Y = -1; ID_Z = -1;
            _settledX = _settledY = _settledZ = true;
            SignalTaskComplete();
        }
    }

    // COLOR HANDLE------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>A spring handle driving a Color (one sub-spring per RGBA channel); settles when all channels rest.</summary>
    public class MotionHandleColor : MotionHandle
    {
        public int ID_R, ID_G, ID_B, ID_A;
        private System.Func<Color> _getter;
        private System.Action<Color> _setter;
        private Color _current, _target;
        // Per-channel "this channel is at rest" flags. Settle is only declared once ALL are true —
        // deciding from the whole-color distance would read ~0 for a not-yet-synced moving sibling
        // (mid-sweep _current is only partially updated) and tear the handle down early (see Sync).
        private bool _settledR, _settledG, _settledB, _settledA;

        /// <summary>Registers a Color spring (one sub-spring per channel) reading/writing via the getter/setter.</summary>
        public MotionHandleColor(System.Func<Color> getter, System.Action<Color> setter)
        {
            _getter = getter;
            _setter = setter;
            _current = _getter();
            _target = _current;
            Register();
        }

        public override bool IsActive => ID_R != -1;

        /// <summary>Live per-channel velocity (r,g,b,a) in units/second (zero once settled).</summary>
        public Vector4 Velocity => (ID_R != -1 && SpringEngine.HasInstance)
            ? new Vector4(SpringEngine.Instance.GetVelocity(ID_R), SpringEngine.Instance.GetVelocity(ID_G), SpringEngine.Instance.GetVelocity(ID_B), SpringEngine.Instance.GetVelocity(ID_A))
            : Vector4.zero;

        private void Register()
        {
            // -1 explicitly (see MotionHandleVector3.Register): 0 is a valid slot id, and the mark tells
            // WaitFor this was a dead target rather than a settle.
            if (!TryRead(_getter, out _current)) { ID_R = -1; ID_G = -1; ID_B = -1; ID_A = -1; SignalTaskCanceled(); return; }
            ID_R = SpringEngine.Instance.Register(_current.r, this);
            ID_G = SpringEngine.Instance.Register(_current.g, this);
            ID_B = SpringEngine.Instance.Register(_current.b, this);
            ID_A = SpringEngine.Instance.Register(_current.a, this);
        }

        /// <inheritdoc/>
        public override void Snap()
        {
            if (!TryRead(_getter, out _current)) { Cancel(); return; }
            _target = _current;
            if (ID_R != -1) { SpringEngine.Instance.CurrentValues[ID_R] = _current.r; SpringEngine.Instance.Velocities[ID_R] = 0; SpringEngine.Instance.Targets[ID_R] = _current.r; }
            if (ID_G != -1) { SpringEngine.Instance.CurrentValues[ID_G] = _current.g; SpringEngine.Instance.Velocities[ID_G] = 0; SpringEngine.Instance.Targets[ID_G] = _current.g; }
            if (ID_B != -1) { SpringEngine.Instance.CurrentValues[ID_B] = _current.b; SpringEngine.Instance.Velocities[ID_B] = 0; SpringEngine.Instance.Targets[ID_B] = _current.b; }
            if (ID_A != -1) { SpringEngine.Instance.CurrentValues[ID_A] = _current.a; SpringEngine.Instance.Velocities[ID_A] = 0; SpringEngine.Instance.Targets[ID_A] = _current.a; }
        }

        /// <summary>(Re)targets the spring toward <paramref name="target"/> color using <paramref name="config"/>.</summary>
        public void To(Color target, SpringConfig config)
        {
            _target = target;
            _settledR = _settledG = _settledB = _settledA = false;
            if (ID_R == -1) Register();
            if (ID_R == -1) return;

            if (!TryRead(_getter, out Color currentReal)) { Cancel(); return; }
            float diff = Mathf.Abs(currentReal.r - SpringEngine.Instance.CurrentValues[ID_R]) +
                         Mathf.Abs(currentReal.g - SpringEngine.Instance.CurrentValues[ID_G]) +
                         Mathf.Abs(currentReal.b - SpringEngine.Instance.CurrentValues[ID_B]) +
                         Mathf.Abs(currentReal.a - SpringEngine.Instance.CurrentValues[ID_A]);

            if (diff > 0.05f)
            {
                SpringEngine.Instance.CurrentValues[ID_R] = currentReal.r; SpringEngine.Instance.Velocities[ID_R] = 0f;
                SpringEngine.Instance.CurrentValues[ID_G] = currentReal.g; SpringEngine.Instance.Velocities[ID_G] = 0f;
                SpringEngine.Instance.CurrentValues[ID_B] = currentReal.b; SpringEngine.Instance.Velocities[ID_B] = 0f;
                SpringEngine.Instance.CurrentValues[ID_A] = currentReal.a; SpringEngine.Instance.Velocities[ID_A] = 0f;
            }

            SpringEngine.Instance.UpdateTarget(ID_R, target.r, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_G, target.g, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_B, target.b, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_A, target.a, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);

            BeginCycle();
        }

        /// <inheritdoc/>
        public override bool Sync(int id, float value, float velocity)
        {
            // Record THIS channel's rest state from its OWN value+velocity. Sync runs once per
            // channel per frame; a per-channel flag is the only order-independent way to know the
            // channel is done — the whole-color distance below reads stale (~0) values for a
            // not-yet-synced moving sibling and would false-settle the handle.
            bool atRest = Mathf.Abs(velocity) < SETTLE_VEL_THRESHOLD;
            if (id == ID_R) { _current.r = value; _settledR = atRest && Mathf.Abs(value - _target.r) < SETTLE_POS_THRESHOLD; }
            else if (id == ID_G) { _current.g = value; _settledG = atRest && Mathf.Abs(value - _target.g) < SETTLE_POS_THRESHOLD; }
            else if (id == ID_B) { _current.b = value; _settledB = atRest && Mathf.Abs(value - _target.b) < SETTLE_POS_THRESHOLD; }
            else if (id == ID_A) { _current.a = value; _settledA = atRest && Mathf.Abs(value - _target.a) < SETTLE_POS_THRESHOLD; }

            try
            {
                _setter(_current);
            }
            catch (MissingReferenceException) { return HandleCrash(); }
            catch (NullReferenceException) { return HandleCrash(); }

            float dist = Mathf.Abs(_current.r - _target.r) + Mathf.Abs(_current.g - _target.g) + Mathf.Abs(_current.b - _target.b) + Mathf.Abs(_current.a - _target.a);
            bool isSettled = _settledR && _settledG && _settledB && _settledA;

            if (_tcs != null && !_tcs.Task.IsCompleted)
            {
                bool shouldSignal = (_strategy == AwaitStrategy.PhysicalSettling && isSettled) ||
                                    (_strategy == AwaitStrategy.VisualArrival && dist < 0.01f);
                if (shouldSignal) SignalTaskComplete();
            }

            if (isSettled)
            {
                // Every channel is at rest — snap to the exact target and sweep all sibling ids
                // (the engine recycles them once we return true).
                _current = _target;
                _setter(_target);
                SignalTaskComplete();
                ID_R = -1; ID_G = -1; ID_B = -1; ID_A = -1;
                return true;
            }
            return false;
        }

        private bool HandleCrash()
        {
            SignalTaskCanceled();
            ID_R = -1; ID_G = -1; ID_B = -1; ID_A = -1;
            return true;
        }

        /// <inheritdoc/>
        public override void Cancel()
        {
            if (SpringEngine.HasInstance && ID_R != -1)
            {
                SpringEngine.Instance.Unregister(ID_R); SpringEngine.Instance.Unregister(ID_G);
                SpringEngine.Instance.Unregister(ID_B); SpringEngine.Instance.Unregister(ID_A);
            }
            ID_R = -1; ID_G = -1; ID_B = -1; ID_A = -1;
            SignalTaskCanceled();
        }

        /// <inheritdoc/>
        public override void Complete()
        {
            if (!IsActive) return;   // see MotionHandleFloat.Complete

            _current = _target;
            try { _setter(_target); }
            catch (MissingReferenceException) { Cancel(); return; }
            catch (NullReferenceException) { Cancel(); return; }

            if (SpringEngine.HasInstance && ID_R != -1)
            {
                SpringEngine.Instance.Unregister(ID_R); SpringEngine.Instance.Unregister(ID_G);
                SpringEngine.Instance.Unregister(ID_B); SpringEngine.Instance.Unregister(ID_A);
            }
            ID_R = -1; ID_G = -1; ID_B = -1; ID_A = -1;
            _settledR = _settledG = _settledB = _settledA = true;
            SignalTaskComplete();
        }
    }

    // VECTOR4 HANDLE------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    /// <summary>A spring handle driving a Vector4 (one sub-spring per component); settles when all components rest.</summary>
    public class MotionHandleVector4 : MotionHandle
    {
        public int ID_X, ID_Y, ID_Z, ID_W;
        private System.Func<Vector4> _getter;
        private System.Action<Vector4> _setter;
        private Vector4 _current, _target;
        // Per-axis "this axis is at rest" flags — settle only when ALL four are true. Deciding from
        // the whole-vector distance false-settles the handle the instant a static axis is synced
        // ahead of a moving sibling (mid-sweep _current is only partially updated; see Sync).
        private bool _settledX, _settledY, _settledZ, _settledW;

        /// <summary>Registers a Vector4 spring (one sub-spring per component) reading/writing via the getter/setter.</summary>
        public MotionHandleVector4(System.Func<Vector4> getter, System.Action<Vector4> setter)
        {
            _getter = getter;
            _setter = setter;
            _current = _getter();
            _target = _current;
            Register();
        }

        public override bool IsActive => ID_X != -1;

        /// <summary>Live per-component velocity in units/second (zero once settled).</summary>
        public Vector4 Velocity => (ID_X != -1 && SpringEngine.HasInstance)
            ? new Vector4(SpringEngine.Instance.GetVelocity(ID_X), SpringEngine.Instance.GetVelocity(ID_Y), SpringEngine.Instance.GetVelocity(ID_Z), SpringEngine.Instance.GetVelocity(ID_W))
            : Vector4.zero;

        private void Register()
        {
            // -1 explicitly (see MotionHandleVector3.Register): 0 is a valid slot id, and the mark tells
            // WaitFor this was a dead target rather than a settle.
            if (!TryRead(_getter, out _current)) { ID_X = -1; ID_Y = -1; ID_Z = -1; ID_W = -1; SignalTaskCanceled(); return; }
            ID_X = SpringEngine.Instance.Register(_current.x, this);
            ID_Y = SpringEngine.Instance.Register(_current.y, this);
            ID_Z = SpringEngine.Instance.Register(_current.z, this);
            ID_W = SpringEngine.Instance.Register(_current.w, this);
        }

        /// <inheritdoc/>
        public override void Snap()
        {
            if (!TryRead(_getter, out _current)) { Cancel(); return; }
            _target = _current;
            if (ID_X != -1) { SpringEngine.Instance.CurrentValues[ID_X] = _current.x; SpringEngine.Instance.Velocities[ID_X] = 0; SpringEngine.Instance.Targets[ID_X] = _current.x; }
            if (ID_Y != -1) { SpringEngine.Instance.CurrentValues[ID_Y] = _current.y; SpringEngine.Instance.Velocities[ID_Y] = 0; SpringEngine.Instance.Targets[ID_Y] = _current.y; }
            if (ID_Z != -1) { SpringEngine.Instance.CurrentValues[ID_Z] = _current.z; SpringEngine.Instance.Velocities[ID_Z] = 0; SpringEngine.Instance.Targets[ID_Z] = _current.z; }
            if (ID_W != -1) { SpringEngine.Instance.CurrentValues[ID_W] = _current.w; SpringEngine.Instance.Velocities[ID_W] = 0; SpringEngine.Instance.Targets[ID_W] = _current.w; }
        }

        /// <summary>(Re)targets the spring toward <paramref name="target"/> using <paramref name="config"/>.</summary>
        public void To(Vector4 target, SpringConfig config)
        {
            _target = target;
            _settledX = _settledY = _settledZ = _settledW = false;
            if (ID_X == -1) Register();
            if (ID_X == -1) return;

            // TELEPORT CHECK
            if (!TryRead(_getter, out Vector4 currentReal)) { Cancel(); return; }
            Vector4 currentEngine = new Vector4(SpringEngine.Instance.CurrentValues[ID_X], SpringEngine.Instance.CurrentValues[ID_Y], SpringEngine.Instance.CurrentValues[ID_Z], SpringEngine.Instance.CurrentValues[ID_W]);

            if (Vector4.Distance(currentReal, currentEngine) > 0.01f)
            {
                SpringEngine.Instance.CurrentValues[ID_X] = currentReal.x; SpringEngine.Instance.Velocities[ID_X] = 0f;
                SpringEngine.Instance.CurrentValues[ID_Y] = currentReal.y; SpringEngine.Instance.Velocities[ID_Y] = 0f;
                SpringEngine.Instance.CurrentValues[ID_Z] = currentReal.z; SpringEngine.Instance.Velocities[ID_Z] = 0f;
                SpringEngine.Instance.CurrentValues[ID_W] = currentReal.w; SpringEngine.Instance.Velocities[ID_W] = 0f;
            }

            SpringEngine.Instance.UpdateTarget(ID_X, target.x, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_Y, target.y, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_Z, target.z, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);
            SpringEngine.Instance.UpdateTarget(ID_W, target.w, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);

            BeginCycle();
        }

        /// <inheritdoc/>
        public override bool Sync(int id, float value, float velocity)
        {
            // Per-axis rest flags (see the field comment): record THIS axis's rest state from its
            // own value+velocity. The whole-vector distance below reads ~0 for a not-yet-synced
            // moving sibling mid-sweep and would false-settle the handle.
            bool atRest = Mathf.Abs(velocity) < SETTLE_VEL_THRESHOLD;
            if (id == ID_X) { _current.x = value; _settledX = atRest && Mathf.Abs(value - _target.x) < SETTLE_POS_THRESHOLD; }
            else if (id == ID_Y) { _current.y = value; _settledY = atRest && Mathf.Abs(value - _target.y) < SETTLE_POS_THRESHOLD; }
            else if (id == ID_Z) { _current.z = value; _settledZ = atRest && Mathf.Abs(value - _target.z) < SETTLE_POS_THRESHOLD; }
            else if (id == ID_W) { _current.w = value; _settledW = atRest && Mathf.Abs(value - _target.w) < SETTLE_POS_THRESHOLD; }

            try
            {
                _setter(_current);
            }
            catch (MissingReferenceException) { return HandleCrash(); }
            catch (NullReferenceException) { return HandleCrash(); }

            float dist = Vector4.Distance(_current, _target);
            bool isSettled = _settledX && _settledY && _settledZ && _settledW;

            if (_tcs != null && !_tcs.Task.IsCompleted)
            {
                bool shouldSignal = (_strategy == AwaitStrategy.PhysicalSettling && isSettled) ||
                                    (_strategy == AwaitStrategy.VisualArrival && dist < VISUAL_THRESHOLD_VECTOR);
                if (shouldSignal) SignalTaskComplete();
            }

            if (isSettled)
            {
                // Every axis is at rest — snap to the exact target and sweep all sibling ids
                // (the engine recycles them once we return true).
                _current = _target;
                _setter(_target);
                SignalTaskComplete();
                ID_X = -1; ID_Y = -1; ID_Z = -1; ID_W = -1;
                return true;
            }
            return false;
        }

        private bool HandleCrash()
        {
            SignalTaskCanceled();
            ID_X = -1; ID_Y = -1; ID_Z = -1; ID_W = -1;
            return true;
        }

        /// <inheritdoc/>
        public override void Cancel()
        {
            if (SpringEngine.HasInstance && ID_X != -1)
            {
                SpringEngine.Instance.Unregister(ID_X); SpringEngine.Instance.Unregister(ID_Y);
                SpringEngine.Instance.Unregister(ID_Z); SpringEngine.Instance.Unregister(ID_W);
            }
            ID_X = -1; ID_Y = -1; ID_Z = -1; ID_W = -1;
            SignalTaskCanceled();
        }

        /// <inheritdoc/>
        public override void Complete()
        {
            if (!IsActive) return;   // see MotionHandleFloat.Complete

            _current = _target;
            try { _setter(_target); }
            catch (MissingReferenceException) { Cancel(); return; }
            catch (NullReferenceException) { Cancel(); return; }

            if (SpringEngine.HasInstance && ID_X != -1)
            {
                SpringEngine.Instance.Unregister(ID_X); SpringEngine.Instance.Unregister(ID_Y);
                SpringEngine.Instance.Unregister(ID_Z); SpringEngine.Instance.Unregister(ID_W);
            }
            ID_X = -1; ID_Y = -1; ID_Z = -1; ID_W = -1;
            _settledX = _settledY = _settledZ = _settledW = true;
            SignalTaskComplete();
        }
    }
}
