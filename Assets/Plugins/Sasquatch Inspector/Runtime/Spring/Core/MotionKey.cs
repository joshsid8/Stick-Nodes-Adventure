using System;
using System.Runtime.CompilerServices;

namespace SasquatchSpring
{
    /// <summary>
    /// Identifies one cached spring on a <see cref="MotionAgent"/>: the object being written to, the
    /// property slot, and an optional sub-id (a shader property id for the Material springs).
    /// </summary>
    /// <remarks>
    /// The TARGET is part of the key on purpose. A <see cref="MotionAgent"/> is per-GameObject, so a bare
    /// "Alpha" / "Color" / "Volume" string is shared by every component on that object: the second component
    /// to spring would be handed the FIRST one's cached handle — getter/setter closures and all — and would
    /// silently animate the wrong component. The Image+CanvasGroup fade rig and the two-AudioSource
    /// "AudioManager" are the everyday shapes that hit it.
    ///
    /// Identity is REFERENCE identity, not <c>Equals</c>: a destroyed-and-recreated component is correctly a
    /// different key, and it sidesteps instance ids entirely — <c>GetInstanceID</c> is ERROR-level obsolete on
    /// Unity 6000.5 and its replacement <c>GetEntityId</c> does not exist on 6000.0, the package floor.
    ///
    /// A struct with <see cref="IEquatable{T}"/> so Dictionary lookups never box — this sits in the
    /// per-frame retarget path.
    /// </remarks>
    public readonly struct MotionKey : IEquatable<MotionKey>
    {
        // The component/material the spring writes to.
        private readonly object _target;
        // Usually a constant string literal ("Pos", "Alpha", …). For the keyless Spring.To overloads it is
        // the caller's setter delegate, which is why this is object rather than string.
        private readonly object _slot;
        // Shader property id for Material springs; 0 otherwise.
        private readonly int _sub;

        /// <summary>Creates a key for <paramref name="slot"/> on <paramref name="target"/>.</summary>
        public MotionKey(object target, object slot, int sub = 0)
        {
            _target = target;
            _slot = slot;
            _sub = sub;
        }

        /// <summary>The object this spring writes to (null for a target-less key).</summary>
        public object Target => _target;

        /// <summary>
        /// True when the slot is a delegate rather than a constant name — i.e. the key came from a keyless
        /// <c>Spring.To</c> call. Those keys are unbounded (a caller-supplied lambda that captures a per-call
        /// local is a NEW delegate every call), so <see cref="MotionAgent"/> prunes settled ones.
        /// </summary>
        internal bool IsDynamicSlot => _slot is Delegate;

        /// <inheritdoc/>
        public bool Equals(MotionKey other) =>
            ReferenceEquals(_target, other._target) &&
            _sub == other._sub &&
            (ReferenceEquals(_slot, other._slot) || (_slot != null && _slot.Equals(other._slot)));

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MotionKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                // RuntimeHelpers.GetHashCode is the identity hash — UnityEngine.Object overrides GetHashCode,
                // and we want the same reference semantics the Equals above uses.
                int hash = _target != null ? RuntimeHelpers.GetHashCode(_target) : 0;
                hash = (hash * 397) ^ (_slot != null ? _slot.GetHashCode() : 0);
                return (hash * 397) ^ _sub;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"MotionKey({_target?.GetType().Name ?? "null"}, {_slot}, {_sub})";
    }

    /// <summary>
    /// Slot names that more than one API writes to, so the two call sites can't drift apart.
    /// </summary>
    /// <remarks>
    /// Only the slots with a REAL cross-file contract live here. The one-API slots ("Alpha", "FOV", …) stay
    /// as literals at their single call site, where they read better.
    /// </remarks>
    internal static class SpringKeys
    {
        /// <summary>Transform.eulerAngles — SpringExtensions.RotateTo(Vector3) / RotateBy.</summary>
        public const string WorldEuler = "RotWorld";

        /// <summary>Transform.localEulerAngles — Spring.Rotate. MUST differ from <see cref="WorldEuler"/>.</summary>
        public const string LocalEuler = "RotLocal";
    }
}
