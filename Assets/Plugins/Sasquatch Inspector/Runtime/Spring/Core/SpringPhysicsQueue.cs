#if SASQ_PHYSICS || SASQ_PHYSICS2D
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchSpring
{
    /// <summary>
    /// Buffers Rigidbody writes produced by the spring tick (which runs on the RENDER clock, from
    /// <see cref="SpringRuntimeDriver"/>.Update) and applies them in FixedUpdate, on the PHYSICS clock.
    /// </summary>
    /// <remarks>
    /// <c>Rigidbody.MovePosition</c> / <c>MoveRotation</c> are documented as FixedUpdate calls: they set the
    /// body's target for the NEXT physics step, and the physics engine interpolates toward it over that step.
    /// Calling them from Update means zero calls in some physics steps and several in others (the counts only
    /// line up when the frame rate happens to equal the fixed rate), which shows up as jitter and as collision
    /// response computed against a target that already moved. The Rigidbody spring helpers advertise
    /// "safe for physics interactions", so the write has to be delivered on the right clock.
    ///
    /// The spring itself still ADVANCES per rendered frame — that is what makes the motion smooth at high
    /// frame rates. Only the write is deferred. Each body keeps just its latest pending value, so several
    /// Update ticks between two physics steps collapse into one MovePosition, which is exactly the semantics
    /// MovePosition wants.
    ///
    /// Outside play mode there is no FixedUpdate, so writes are applied inline instead (edit-time preview).
    ///
    /// The Get* readers exist because <c>rb.position</c> does NOT reflect a MovePosition until the physics
    /// step runs, so a spring reading it would see its own writes lag by a step. MotionHandle.To() treats a
    /// getter/engine divergence over 0.01 as a TELEPORT and resets the engine value AND zeroes velocity — so
    /// a per-frame re-target (the "follow a moving point" pattern) would zero its own velocity every frame
    /// and crawl. Reading the pending value keeps the getter and the engine in agreement.
    /// </remarks>
    internal static class SpringPhysicsQueue
    {
        /// <summary>
        /// The exception a destroyed body must raise, matching what touching one directly would have thrown.
        /// </summary>
        /// <remarks>
        /// Swallowing the destroyed case with a silent early-return would be a REGRESSION: before the queue
        /// existed the setter called body.MovePosition(...) directly, which throws MissingReferenceException
        /// on a destroyed body, and every MotionHandle.Sync catches exactly that to cancel the spring and fault
        /// its awaiter. A silent no-op instead leaves the spring running against nothing and reporting SUCCESS.
        /// </remarks>
        private static MissingReferenceException DestroyedBody() =>
            new MissingReferenceException("The Rigidbody this spring targets has been destroyed.");

#if SASQ_PHYSICS
        private struct Pending3D
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool HasPosition;
            public bool HasRotation;
        }

        private static readonly Dictionary<Rigidbody, Pending3D> s_pending3D = new Dictionary<Rigidbody, Pending3D>();
        private static readonly List<Rigidbody> s_scratch3D = new List<Rigidbody>();

        /// <summary>Queues a world-position write for <paramref name="body"/> (applied next FixedUpdate).</summary>
        public static void SetPosition(Rigidbody body, Vector3 position)
        {
            if (body == null) throw DestroyedBody();
            if (!Application.isPlaying) { body.MovePosition(position); return; }

            s_pending3D.TryGetValue(body, out var pending);
            pending.Position = position;
            pending.HasPosition = true;
            s_pending3D[body] = pending;
        }

        /// <summary>Queues a rotation write for <paramref name="body"/> (applied next FixedUpdate).</summary>
        public static void SetRotation(Rigidbody body, Quaternion rotation)
        {
            if (body == null) throw DestroyedBody();
            if (!Application.isPlaying) { body.MoveRotation(rotation); return; }

            s_pending3D.TryGetValue(body, out var pending);
            pending.Rotation = rotation;
            pending.HasRotation = true;
            s_pending3D[body] = pending;
        }

        /// <summary>The position the spring last WROTE — the queued value if one is waiting, else the live one.</summary>
        public static Vector3 GetPosition(Rigidbody body)
        {
            // Throw rather than return zero: MotionHandle.TryRead catches this and cancels the spring, which
            // is the correct outcome. Returning a default would read as a teleport to the origin.
            if (body == null) throw DestroyedBody();
            if (s_pending3D.TryGetValue(body, out var pending) && pending.HasPosition) return pending.Position;
            return body.position;
        }

        /// <summary>The rotation the spring last WROTE — the queued value if one is waiting, else the live one.</summary>
        public static Quaternion GetRotation(Rigidbody body)
        {
            if (body == null) throw DestroyedBody();
            if (s_pending3D.TryGetValue(body, out var pending) && pending.HasRotation) return pending.Rotation;
            return body.rotation;
        }
#endif

#if SASQ_PHYSICS2D
        private struct Pending2D
        {
            public Vector2 Position;
            public float Rotation;
            public bool HasPosition;
            public bool HasRotation;
        }

        private static readonly Dictionary<Rigidbody2D, Pending2D> s_pending2D = new Dictionary<Rigidbody2D, Pending2D>();
        private static readonly List<Rigidbody2D> s_scratch2D = new List<Rigidbody2D>();

        /// <summary>Queues a position write for <paramref name="body"/> (applied next FixedUpdate).</summary>
        public static void SetPosition(Rigidbody2D body, Vector2 position)
        {
            if (body == null) throw DestroyedBody();
            if (!Application.isPlaying) { body.MovePosition(position); return; }

            s_pending2D.TryGetValue(body, out var pending);
            pending.Position = position;
            pending.HasPosition = true;
            s_pending2D[body] = pending;
        }

        /// <summary>Queues a rotation (degrees) write for <paramref name="body"/> (applied next FixedUpdate).</summary>
        public static void SetRotation(Rigidbody2D body, float degrees)
        {
            if (body == null) throw DestroyedBody();
            if (!Application.isPlaying) { body.MoveRotation(degrees); return; }

            s_pending2D.TryGetValue(body, out var pending);
            pending.Rotation = degrees;
            pending.HasRotation = true;
            s_pending2D[body] = pending;
        }

        /// <summary>The position the spring last WROTE — the queued value if one is waiting, else the live one.</summary>
        public static Vector2 GetPosition(Rigidbody2D body)
        {
            if (body == null) throw DestroyedBody();
            if (s_pending2D.TryGetValue(body, out var pending) && pending.HasPosition) return pending.Position;
            return body.position;
        }

        /// <summary>The rotation the spring last WROTE — the queued value if one is waiting, else the live one.</summary>
        public static float GetRotation(Rigidbody2D body)
        {
            if (body == null) throw DestroyedBody();
            if (s_pending2D.TryGetValue(body, out var pending) && pending.HasRotation) return pending.Rotation;
            return body.rotation;
        }
#endif

        /// <summary>
        /// Applies and clears every queued write. Called once per physics step by
        /// <see cref="SpringRuntimeDriver"/>.FixedUpdate.
        /// </summary>
        public static void Flush()
        {
#if SASQ_PHYSICS
            if (s_pending3D.Count > 0)
            {
                // Iterate a snapshot of the KEYS: MovePosition can wake a body whose contacts destroy
                // another queued body, and clearing as we go would mutate the dictionary mid-iteration.
                s_scratch3D.Clear();
                foreach (var body in s_pending3D.Keys) s_scratch3D.Add(body);

                foreach (var body in s_scratch3D)
                {
                    if (!s_pending3D.TryGetValue(body, out var pending)) continue;
                    if (body == null) continue; // destroyed since it was queued

                    if (pending.HasPosition) body.MovePosition(pending.Position);
                    if (pending.HasRotation) body.MoveRotation(pending.Rotation);
                }

                s_pending3D.Clear();
                s_scratch3D.Clear();
            }
#endif
#if SASQ_PHYSICS2D
            if (s_pending2D.Count > 0)
            {
                s_scratch2D.Clear();
                foreach (var body in s_pending2D.Keys) s_scratch2D.Add(body);

                foreach (var body in s_scratch2D)
                {
                    if (!s_pending2D.TryGetValue(body, out var pending)) continue;
                    if (body == null) continue;

                    if (pending.HasPosition) body.MovePosition(pending.Position);
                    if (pending.HasRotation) body.MoveRotation(pending.Rotation);
                }

                s_pending2D.Clear();
                s_scratch2D.Clear();
            }
#endif
        }

        /// <summary>Drops every queued write without applying it (engine shutdown / play-mode exit).</summary>
        public static void Clear()
        {
#if SASQ_PHYSICS
            s_pending3D.Clear();
            s_scratch3D.Clear();
#endif
#if SASQ_PHYSICS2D
            s_pending2D.Clear();
            s_scratch2D.Clear();
#endif
        }
    }
}
#endif
