using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Per-session instance identity for a Unity object, resolved in a way that compiles across the whole
    /// supported editor range.
    ///
    /// WHY THIS EXISTS. There is no single expression that works on both ends of the range:
    ///   • Unity 6000.0 and 6000.1 have no <c>GetEntityId()</c> at all.
    ///   • <c>GetEntityId()</c> first appears in 6000.2, which is why the guard below says
    ///     <c>UNITY_6000_2_OR_NEWER</c> and NOT the version that retired the old API. Measured 2026-09-04 by
    ///     compiling <c>o.GetEntityId()</c> against each installed editor: absent on 6000.0, clean on
    ///     6000.2 / 6000.3 / 6000.4 / 6000.5. The guard was 6000.5 until then, which left 6000.4 taking the
    ///     <c>GetInstanceID()</c> branch AFTER Unity began warning on it — six CS0618 warnings in a buyer's
    ///     console on that editor, found by the cross-version matrix's shipped-warning scan.
    ///   • Unity 6000.5 marks <c>Object.GetInstanceID()</c> as an ERROR-level obsolete (CS0619) — it does not
    ///     warn, it fails the build, and with it the entire package. Before this shim, Sasquatch Inspector did
    ///     not compile on 6000.5 in any form.
    ///   • On 6000.5 the <c>EntityId</c> → <c>int</c> implicit cast is ALSO error-obsolete, so an int has to be
    ///     obtained via <c>GetHashCode()</c> rather than a cast, and string keys are formatted directly.
    ///
    /// This is the ONLY place in the package allowed to touch <c>GetInstanceID()</c> — every other call site goes
    /// through here, or 6000.5 stops compiling again. This file is also the sole entry on
    /// <c>TestNoDirectInstanceIdCalls</c>' allow-list, which fails if any other shipped file calls
    /// <c>GetInstanceID()</c> directly; renaming, moving, or splitting this file means moving that entry with it.
    /// The runtime assembly needs no equivalent: spring handles are keyed by <c>MotionKey</c>'s REFERENCE
    /// identity, not by an instance id.
    /// </summary>
    internal static class UnityObjectId
    {
        /// <summary>
        /// The object's instance identity as an int, for cache keys and hash combines. Null-safe (0).
        ///
        /// Values are per-session and must never be persisted or compared across editor sessions — on 6000.5+
        /// this is a hash of the EntityId rather than the raw instance id, so it is stable within a run but
        /// carries no guarantee of equalling any id Unity hands out elsewhere. Every caller uses it exactly
        /// that way: in-memory caches and hash inputs.
        /// </summary>
        internal static int Of(Object obj)
        {
            if (obj == null) return 0;
#if UNITY_6000_2_OR_NEWER
            return obj.GetEntityId().GetHashCode();
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>
        /// The object's instance identity as a string, for use inside a composed key. Preferred over
        /// <see cref="Of"/> wherever the value only ever gets concatenated: it formats the id directly and so
        /// avoids the int conversion entirely. Null-safe ("0").
        /// </summary>
        internal static string KeyOf(Object obj)
        {
            if (obj == null) return "0";
#if UNITY_6000_2_OR_NEWER
            return obj.GetEntityId().ToString();
#else
            return obj.GetInstanceID().ToString();
#endif
        }
    }
}
