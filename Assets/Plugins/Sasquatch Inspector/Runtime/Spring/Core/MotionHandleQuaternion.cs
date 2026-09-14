using System;
using UnityEngine;

namespace SasquatchSpring
{
    /// <summary>
    /// Springs a rotation along the SHORTEST ARC by driving a single scalar "remaining angle" spring
    /// (in degrees) from the captured delta down to zero. Because it works on an axis+angle rather than
    /// Euler components, it is free of the gimbal lock the Euler-based <see cref="Spring.Rotate"/> path
    /// can hit on complex 3D rotations. The scalar spring can overshoot past zero (rotating slightly
    /// beyond the target, then easing back) for a natural rotational bounce.
    /// </summary>
    public class MotionHandleQuaternion : MotionHandle
    {
        // Degrees-space settle thresholds (the float handle's normalized 0.0001 thresholds are far too
        // tight for an angle measured in degrees).
        private const float SETTLE_ANGLE = 0.02f;     // ~settled when within 1/50 of a degree
        private const float SETTLE_ANGLE_VEL = 0.1f;  // deg/sec
        private const float VISUAL_ANGLE = 0.5f;

        public int ID;
        private readonly Func<Quaternion> _getter;
        private readonly Action<Quaternion> _setter;

        private Quaternion _base;    // rotation captured when the current To() began
        private Quaternion _target;  // destination rotation
        private Vector3 _axis;       // shortest-arc axis from _base to _target
        private float _totalAngle;   // full signed delta angle (degrees) from _base to _target

        /// <summary>Springs a rotation along the shortest arc toward the target; reads/writes via the getter/setter.</summary>
        public MotionHandleQuaternion(Func<Quaternion> getter, Action<Quaternion> setter)
        {
            _getter = getter;
            _setter = setter;
            _base = _getter();
            _target = _base;
            _axis = Vector3.up;
            _totalAngle = 0f;
            // One float slot drives the whole rotation: the remaining angle, springing toward 0.
            ID = SpringEngine.Instance.Register(0f, this);
        }

        public override bool IsActive => ID != -1;

        /// <summary>Live angular speed in degrees/second (0 once settled).</summary>
        public float Velocity => (ID != -1 && SpringEngine.HasInstance) ? Mathf.Abs(SpringEngine.Instance.GetVelocity(ID)) : 0f;

        /// <inheritdoc/>
        public override void Snap()
        {
            if (!TryRead(_getter, out _base)) { Cancel(); return; }
            _target = _base;
            _totalAngle = 0f;
            if (ID != -1)
            {
                SpringEngine.Instance.CurrentValues[ID] = 0f;
                SpringEngine.Instance.Velocities[ID] = 0f;
                SpringEngine.Instance.Targets[ID] = 0f;
            }
        }

        /// <summary>(Re)targets the rotation toward <paramref name="target"/> using <paramref name="config"/>.</summary>
        public void To(Quaternion target, SpringConfig config)
        {
            if (ID == -1) ID = SpringEngine.Instance.Register(0f, this);
            if (ID == -1) return;

            // Recapture the live rotation as the spring's base each call, so retargeting (and continuous
            // "follow" use) stays smooth — the applied angle starts at 0 from wherever we are right now.
            if (!TryRead(_getter, out _base)) { Cancel(); return; }
            _target = target;

            Quaternion delta = target * Quaternion.Inverse(_base);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;                  // take the shortest direction
            if (axis.sqrMagnitude < 1e-6f || float.IsNaN(axis.x)) axis = Vector3.up;

            _axis = axis;
            _totalAngle = angle;

            // Drive the remaining-angle value from the full delta down to 0. Velocity is left untouched
            // for momentum continuity across retargets.
            SpringEngine.Instance.CurrentValues[ID] = _totalAngle;
            SpringEngine.Instance.UpdateTarget(ID, 0f, config.Frequency, config.Damping, config.ResolvedSettleEpsilon, config.Unscaled);

            BeginCycle();
        }

        /// <inheritdoc/>
        public override bool Sync(int id, float value, float velocity)
        {
            // value = remaining angle (degrees). Applied rotation = base rotated by (total - remaining).
            float applied = _totalAngle - value;
            Quaternion rot = Quaternion.AngleAxis(applied, _axis) * _base;

            try { _setter(rot); }
            catch (MissingReferenceException) { return HandleCrash(); }
            catch (NullReferenceException) { return HandleCrash(); }

            float remaining = Mathf.Abs(value);
            bool isSettled = remaining < SETTLE_ANGLE && Mathf.Abs(velocity) < SETTLE_ANGLE_VEL;

            if (_tcs != null && !_tcs.Task.IsCompleted)
            {
                bool shouldSignal = (_strategy == AwaitStrategy.PhysicalSettling && isSettled) ||
                                    (_strategy == AwaitStrategy.VisualArrival && remaining < VISUAL_ANGLE);
                if (shouldSignal) SignalTaskComplete();
            }

            if (isSettled)
            {
                _setter(_target); // land exactly on the target orientation
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
            if (SpringEngine.HasInstance && ID != -1) SpringEngine.Instance.Unregister(ID);
            ID = -1;
            SignalTaskCanceled();
        }

        /// <inheritdoc/>
        public override void Complete()
        {
            if (!IsActive) return;   // see MotionHandleFloat.Complete

            // Sync's settle branch lands on _target directly rather than reconstructing the rotation from a
            // remaining angle of 0 — the two agree, and the direct write cannot accumulate axis-angle drift.
            try { _setter(_target); }
            catch (MissingReferenceException) { Cancel(); return; }
            catch (NullReferenceException) { Cancel(); return; }

            if (SpringEngine.HasInstance && ID != -1) SpringEngine.Instance.Unregister(ID);
            ID = -1;
            SignalTaskComplete();
        }
    }
}
