using System.Collections.Generic;

namespace SasquatchSpring
{
    /// <summary>
    /// Shared get-or-replace logic for keyed handle caches. Used by MotionAgent (for
    /// GameObject owners) and by Spring's object-owner buckets (VisualElements, plain
    /// C# objects) so the two paths can't drift apart.
    /// </summary>
    public static class MotionHandleCache
    {
        /// <summary>Returns the cached handle for <paramref name="key"/> if it's still type <typeparamref name="T"/>; otherwise cancels any stale one and creates a fresh handle via <paramref name="factory"/>.</summary>
        /// <remarks>
        /// <paramref name="arg"/> exists so callers can pass a <c>static</c> lambda. A static lambda captures
        /// nothing, so the compiler caches ONE delegate instance for the whole process and the getter/setter
        /// closures are built inside the factory — i.e. only on a cache MISS. A capturing lambda instead
        /// allocates a display class on EVERY call (the compiler hoists the captured variable at method entry,
        /// before any <c>??</c> short-circuit can skip it), which lands squarely in the per-frame retarget path.
        /// </remarks>
        public static T GetHandle<TKey, TArg, T>(Dictionary<TKey, MotionHandle> handles, TKey key, TArg arg, System.Func<TArg, T> factory)
            where T : MotionHandle
        {
            if (handles.TryGetValue(key, out var existing))
            {
                if (existing is T typed) return typed;
                existing.Cancel();
            }

            var newHandle = factory(arg);
            handles[key] = newHandle;
            return newHandle;
        }

        /// <summary>Returns the cached handle for <paramref name="key"/> if present AND still of type <typeparamref name="T"/>; otherwise null. Never creates.</summary>
        public static T TryGetHandle<TKey, T>(Dictionary<TKey, MotionHandle> handles, TKey key) where T : MotionHandle
        {
            if (handles.TryGetValue(key, out var existing) && existing is T typed) return typed;
            return null;
        }
    }
}
