using UnityEngine;
namespace SasquatchSpring
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The primary entry point for the Spring system. 
    /// Use specific helpers (Spring.Move, Spring.Scale) for common tasks, 
    /// or generic Spring.To() for custom properties.
    /// </summary>
    public static class Spring
    {
        /// <summary>
        /// Springs a Transform's WORLD position.
        /// </summary>
        public static MotionHandleVector3 Move(Transform transform, Vector3 target, SpringConfig config)
        {
            // The key is (target, slot) — see MotionKey. The `?? build` pattern skips rebuilding the
            // getter/setter closures on a cache hit (the common per-frame case): C# doesn't evaluate the right
            // side of ?? when the left side is non-null. The factory is a STATIC lambda that takes the target
            // as an argument, which is the other half of "allocation-free" — capturing `transform` directly
            // would make the compiler hoist it into a display class allocated at METHOD ENTRY, before the ??
            // can short-circuit, so every call would allocate whether it hit the cache or not.
            var agent = GetAgent(transform);
            var key = new MotionKey(transform, "Pos");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, transform, static tr => new MotionHandleVector3(() => tr.position, x => tr.position = x));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// Springs a Transform's LOCAL position.
        /// </summary>
        public static MotionHandleVector3 MoveLocal(Transform transform, Vector3 target, SpringConfig config)
        {
            var agent = GetAgent(transform);
            var key = new MotionKey(transform, "LocalPos");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, transform, static tr => new MotionHandleVector3(() => tr.localPosition, x => tr.localPosition = x));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// Springs a Transform's local scale to a target value.
        /// </summary>
        /// <param name="target">The target scale (uniform or vector).</param>
        public static MotionHandleVector3 Scale(Transform transform, Vector3 target, SpringConfig config)
        {
            var agent = GetAgent(transform);
            var key = new MotionKey(transform, "Scale");
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, transform, static tr => new MotionHandleVector3(() => tr.localScale, x => tr.localScale = x));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// Springs a Transform's local Euler rotation.
        /// <para><i>Note: Linear springs on angles can handle >360 degrees, but watch for gimbal lock on complex 3D rotations.</i></para>
        /// </summary>
        public static MotionHandleVector3 Rotate(Transform transform, Vector3 targetEuler, SpringConfig config)
        {
            var agent = GetAgent(transform);
            // LocalEuler, NOT the world-Euler slot SpringExtensions.RotateTo/RotateBy use — see
            // SpringExtensions.WorldEulerKey for why one shared key corrupts the rotation space.
            var key = new MotionKey(transform, SpringKeys.LocalEuler);
            var handle = agent.TryGetHandle<MotionHandleVector3>(key)
                         ?? agent.GetHandle(key, transform, static tr => new MotionHandleVector3(() => tr.localEulerAngles, x => tr.localEulerAngles = x));
            handle.To(targetEuler, config);
            return handle;
        }

        /// <summary>
        /// Springs a Transform's WORLD rotation to a Quaternion along the shortest arc. Gimbal-lock-free,
        /// unlike <see cref="Rotate"/> (which springs Euler components). Returns a handle you can await/cancel.
        /// </summary>
        public static MotionHandleQuaternion RotateTo(Transform transform, Quaternion target, SpringConfig config)
        {
            var agent = GetAgent(transform);
            var key = new MotionKey(transform, "RotQ");
            var handle = agent.TryGetHandle<MotionHandleQuaternion>(key)
                         ?? agent.GetHandle(key, transform, static tr => new MotionHandleQuaternion(() => tr.rotation, x => tr.rotation = x));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// Springs a SpriteRenderer's Color.
        /// </summary>
        public static MotionHandleColor Color(SpriteRenderer sprite, Color targetColor, SpringConfig config)
        {
            var agent = GetAgent(sprite);
            var key = new MotionKey(sprite, "Color");
            var handle = agent.TryGetHandle<MotionHandleColor>(key)
                         ?? agent.GetHandle(key, sprite, static s => new MotionHandleColor(() => s.color, x => s.color = x));
            handle.To(targetColor, config);
            return handle;
        }

        /// <summary>Low-level Vector3 spring with an explicit cache <paramref name="key"/> on a Component owner (used internally by the typed helpers).</summary>
        public static MotionHandleVector3 ToManual(Component owner, object key, System.Func<Vector3> getter, System.Action<Vector3> setter, Vector3 target, SpringConfig config)
        {
            var agent = GetAgent(owner);
            var handle = agent.GetHandle(new MotionKey(owner, key), (g: getter, s: setter),
                static a => new MotionHandleVector3(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>Low-level Color spring with an explicit cache <paramref name="key"/> on a Component owner.</summary>
        public static MotionHandleColor ToManual(Component owner, object key, System.Func<Color> getter, System.Action<Color> setter, Color target, SpringConfig config)
        {
            var agent = GetAgent(owner);
            var handle = agent.GetHandle(new MotionKey(owner, key), (g: getter, s: setter),
                static a => new MotionHandleColor(a.g, a.s));
            handle.To(target, config);
            return handle;
        }


        /// <summary>
        /// The universal Float spring. Can drive ANY float property.
        /// <para>Example: <code>Spring.To(this, "myFloat", () => myVal, x => myVal = x, 10f, SpringConfig.Snappy);</code></para>
        /// </summary>
        public static MotionHandleFloat To(Component owner, System.Func<float> getter, System.Action<float> setter, float target, SpringConfig config, string key = null)
        {
            var agent = GetAgent(owner);
            var handleKey = ComponentKey(agent, owner, key, setter);

            var handle = agent.GetHandle(handleKey, (g: getter, s: setter),
                static a => new MotionHandleFloat(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Vector3 spring. Can drive Position, Scale, or custom Vectors.
        /// </summary>
        public static MotionHandleVector3 To(Component owner, System.Func<Vector3> getter, System.Action<Vector3> setter, Vector3 target, SpringConfig config, string key = null)
        {
            var agent = GetAgent(owner);

            var handleKey = ComponentKey(agent, owner, key, setter);

            var handle = agent.GetHandle(handleKey, (g: getter, s: setter),
                static a => new MotionHandleVector3(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Color spring. Drives R, G, B, and A independently.
        /// </summary>
        public static MotionHandleColor To(Component owner, System.Func<Color> getter, System.Action<Color> setter, Color target, SpringConfig config, string key = null)
        {
            var agent = GetAgent(owner);
            var handleKey = ComponentKey(agent, owner, key, setter);

            var handle = agent.GetHandle(handleKey, (g: getter, s: setter),
                static a => new MotionHandleColor(a.g, a.s));

            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Vector4 spring. Good for shader properties.
        /// </summary>
        public static MotionHandleVector4 To(Component owner, System.Func<Vector4> getter, System.Action<Vector4> setter, Vector4 target, SpringConfig config, string key = null)
        {
            var agent = GetAgent(owner);
            var handleKey = ComponentKey(agent, owner, key, setter);

            var handle = agent.GetHandle(handleKey, (g: getter, s: setter),
                static a => new MotionHandleVector4(a.g, a.s));

            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Vector2 spring (2D position, RectTransform sizeDelta, etc.). Backed by a Vector3
        /// handle with z pinned to 0; the returned handle's Z axis is unused.
        /// </summary>
        public static MotionHandleVector3 To(Component owner, System.Func<Vector2> getter, System.Action<Vector2> setter, Vector2 target, SpringConfig config, string key = null)
        {
            var agent = GetAgent(owner);
            var handleKey = ComponentKey(agent, owner, key, setter);
            var handle = agent.GetHandle(handleKey, (g: getter, s: setter), static a => new MotionHandleVector3(
                () => { Vector2 v = a.g(); return new Vector3(v.x, v.y, 0f); },
                v => a.s(new Vector2(v.x, v.y))));
            handle.To(new Vector3(target.x, target.y, 0f), config);
            return handle;
        }

        /// <summary>
        /// Builds the cache key for a Component-owner <c>To(...)</c> call: the caller's <paramref name="key"/>
        /// when they gave one, else the setter delegate.
        /// </summary>
        /// <remarks>
        /// The OWNER is part of the key (see <see cref="MotionKey"/>) — without it a bare user string would be
        /// shared across every component on the GameObject, despite the API reading as per-component.
        ///
        /// The delegate fallback is an unbounded key space: <c>MulticastDelegate</c> equality is method+target,
        /// so the usual <c>this</c>-capturing lambda collapses to ONE entry, but a lambda that captures a
        /// per-call local is a brand new delegate — and therefore a new cache entry — on every call. The cache
        /// never evicts on its own, so we re-arm a prune here rather than growing forever.
        /// </remarks>
        private static MotionKey ComponentKey(MotionAgent agent, Component owner, string key, object setter)
        {
            if (key != null) return new MotionKey(owner, key);

            agent.PruneCompletedDynamicHandles();
            return new MotionKey(owner, setter);
        }

        private static MotionAgent GetAgent(Component c)
        {
            if (!c.transform.TryGetComponent(out MotionAgent agent))
            {
                agent = c.gameObject.AddComponent<MotionAgent>();
                // HideInInspector alone would let an edit-time spring BAKE this auto-added component into the
                // user's scene/prefab — invisible in the Inspector, and shipped in their build. DontSave keeps
                // it runtime-only. (Not HideAndDontSave: that bundles NotEditable, which we don't want.)
                agent.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            }
            return agent;
        }

        // =========================================================================================
        // OBJECT OWNERS (VisualElements, plain C# objects)
        // =========================================================================================
        // Component owners keep the MotionAgent path above (the agent dies with its GameObject).
        // Non-Component owners are tracked in a weak table: when the owner is garbage collected
        // its bucket disappears, and SpringVisualElementExtensions additionally cancels handles
        // when an element leaves its panel. Owners must be reference types — a boxed struct
        // would lose its bucket immediately.

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<object, MotionHandle>> s_objectHandles =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<object, MotionHandle>>();

        /// <summary>
        /// The universal Float spring for non-Component owners (e.g. VisualElements).
        /// </summary>
        public static MotionHandleFloat To(object owner, System.Func<float> getter, System.Action<float> setter, float target, SpringConfig config, string key = null)
        {
            var handle = GetObjectHandle(owner, key ?? (object)setter, (g: getter, s: setter),
                static a => new MotionHandleFloat(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Vector3 spring for non-Component owners (e.g. VisualElements).
        /// </summary>
        public static MotionHandleVector3 To(object owner, System.Func<Vector3> getter, System.Action<Vector3> setter, Vector3 target, SpringConfig config, string key = null)
        {
            var handle = GetObjectHandle(owner, key ?? (object)setter, (g: getter, s: setter),
                static a => new MotionHandleVector3(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Color spring for non-Component owners (e.g. VisualElements).
        /// </summary>
        public static MotionHandleColor To(object owner, System.Func<Color> getter, System.Action<Color> setter, Color target, SpringConfig config, string key = null)
        {
            var handle = GetObjectHandle(owner, key ?? (object)setter, (g: getter, s: setter),
                static a => new MotionHandleColor(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Vector4 spring for non-Component owners (e.g. VisualElements).
        /// </summary>
        public static MotionHandleVector4 To(object owner, System.Func<Vector4> getter, System.Action<Vector4> setter, Vector4 target, SpringConfig config, string key = null)
        {
            var handle = GetObjectHandle(owner, key ?? (object)setter, (g: getter, s: setter),
                static a => new MotionHandleVector4(a.g, a.s));
            handle.To(target, config);
            return handle;
        }

        /// <summary>
        /// The universal Vector2 spring for non-Component owners (e.g. VisualElements). Backed by a Vector3
        /// handle with z pinned to 0.
        /// </summary>
        public static MotionHandleVector3 To(object owner, System.Func<Vector2> getter, System.Action<Vector2> setter, Vector2 target, SpringConfig config, string key = null)
        {
            var handle = GetObjectHandle(owner, key ?? (object)setter, (g: getter, s: setter),
                static a => new MotionHandleVector3(
                    () => { Vector2 v = a.g(); return new Vector3(v.x, v.y, 0f); },
                    v => a.s(new Vector2(v.x, v.y))));
            handle.To(new Vector3(target.x, target.y, 0f), config);
            return handle;
        }

        /// <summary>
        /// Fetches (or creates) a keyed Vector3 handle WITHOUT retargeting it — for
        /// impulse effects (Punch) that must not disturb an in-flight spring's target.
        /// </summary>
        public static MotionHandleVector3 GetVector3Handle(object owner, object key, System.Func<Vector3> getter, System.Action<Vector3> setter)
        {
            return GetObjectHandle(owner, key, (g: getter, s: setter), static a => new MotionHandleVector3(a.g, a.s));
        }

        /// <summary>
        /// Cancels and forgets a single keyed handle on this owner (other springs on the
        /// same owner keep running).
        /// </summary>
        /// <remarks>
        /// Handles a COMPONENT owner too: those handles live on the object's <see cref="MotionAgent"/>, not in
        /// the weak table, so searching only the table would silently do nothing for every spring created
        /// through the Component API — including every extension method. A GameObject is not a Component, so
        /// <c>Spring.To(gameObject, …)</c> binds the object-owner overload and its handles ARE in the weak
        /// table; that case needs no special branch here (<see cref="CancelAll"/> does add one, because
        /// "stop everything on this object" should also reach the agent).
        /// </remarks>
        public static void Cancel(object owner, object key)
        {
            if (owner == null || key == null) return;

            // A COMPONENT owner's handles live on the GameObject's MotionAgent, keyed (component, key).
            // `c != null` is the Unity overload — a destroyed component has nothing left to cancel, and
            // reading c.transform on one would throw.
            if (owner is Component c && c != null && c.transform.TryGetComponent(out MotionAgent agent))
            {
                agent.CancelHandle(new MotionKey(c, key));
            }

            // A GameObject is not a Component, so Spring.To(gameObject, ...) binds the object-owner overload
            // and its handles are in the weak table below — same as any other plain owner. Fall through.
            if (s_objectHandles.TryGetValue(owner, out var bucket) && bucket.TryGetValue(key, out var handle))
            {
                handle.Cancel();
                bucket.Remove(key);
            }
        }

        /// <summary>
        /// Cancels and forgets every handle owned by this object.
        /// </summary>
        /// <remarks>
        /// For a COMPONENT owner this cancels only the springs writing to THAT component — the other
        /// components on the same GameObject keep animating. For a GAMEOBJECT owner it cancels everything on
        /// the object. See <see cref="Cancel"/> for why these two cases need handling at all.
        /// </remarks>
        public static void CancelAll(object owner)
        {
            if (owner == null) return;

            // Component: only the springs writing to THAT component (MotionKey makes this precise).
            if (owner is Component c && c != null && c.transform.TryGetComponent(out MotionAgent componentAgent))
            {
                componentAgent.CancelAllFor(c);
            }
            // GameObject: everything on the object — which is what "stop this object's springs" means, and
            // covers every handle the extension methods put on the agent for its components.
            else if (owner is GameObject go && go != null && go.TryGetComponent(out MotionAgent objectAgent))
            {
                objectAgent.CancelAll();
            }

            // Plain owners (VisualElements, C# objects) — and a GameObject/Component that was also used with
            // the object-owner overloads — keep handles in the weak table.
            if (s_objectHandles.TryGetValue(owner, out var bucket))
            {
                foreach (var handle in bucket.Values) handle.Cancel();
                bucket.Clear();
            }
        }

        /// <summary>
        /// FINISHES every in-flight spring owned by <paramref name="owner"/> — each writes its exact target
        /// value and resolves its awaiters as a normal settle — then forgets them. Settled handles are left
        /// alone. <see cref="CancelAll"/>'s counterpart for owners that must not be stranded mid-motion.
        /// </summary>
        /// <remarks>
        /// Scoped to plain owners (VisualElements and other C# objects), which is where stranding is possible:
        /// their springs drive INLINE styles, so a canceled motion is written onto the element and stays there
        /// with nothing alive to move it again. Component / GameObject owners are deliberately NOT swept — an
        /// agent's cleanup signal is destruction, where writing a final value to a dying target is wrong, and
        /// <see cref="CancelAll"/> remains the correct call for them.
        /// </remarks>
        public static void CompleteAll(object owner)
        {
            if (owner == null) return;

            if (s_objectHandles.TryGetValue(owner, out var bucket))
            {
                foreach (var handle in bucket.Values) handle.Complete();
                bucket.Clear();
            }
        }

        private static T GetObjectHandle<T, TArg>(object owner, object key, TArg arg, System.Func<TArg, T> factory) where T : MotionHandle
        {
            var bucket = s_objectHandles.GetOrCreateValue(owner);
            PruneCompletedDynamicHandles(bucket);
            return MotionHandleCache.GetHandle(bucket, key, arg, factory);
        }

        // Reused across prunes — this runs on the spring path and must not allocate per call.
        private static readonly List<object> s_pruneScratch = new List<object>();

        // Above this many cached handles, drop the settled delegate-keyed ones. A caller lambda that captures
        // a per-call local is a NEW delegate — and so a new bucket entry — every call, and nothing else evicts
        // them. Explicit string keys are bounded by the caller and are never pruned (keeping them cached is
        // what makes the per-frame retarget path allocation-free).
        private const int ObjectBucketPruneThreshold = 64;

        private static void PruneCompletedDynamicHandles(Dictionary<object, MotionHandle> bucket)
        {
            if (bucket.Count < ObjectBucketPruneThreshold) return;

            s_pruneScratch.Clear();
            foreach (var pair in bucket)
            {
                if (pair.Key is System.Delegate && pair.Value.IsComplete) s_pruneScratch.Add(pair.Key);
            }

            foreach (var key in s_pruneScratch) bucket.Remove(key);
            s_pruneScratch.Clear();
        }
    }
}
