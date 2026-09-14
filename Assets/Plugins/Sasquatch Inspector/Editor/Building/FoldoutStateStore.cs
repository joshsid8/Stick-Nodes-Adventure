using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Durable, per-foldout open/closed memory for every collapsible site in a Sasquatch Inspector inspector
    /// (containers, the curve/gradient/particle group boxes, collection headers, help boxes…). Mimics Unity's
    /// native foldout behaviour — state survives selection changes, domain reloads, AND editor restarts — by
    /// backing it with <see cref="EditorPrefs"/> keyed by a STABLE composite (the target type's rename-proof
    /// MonoScript GUID + a per-site discriminator + title), hashed to keep the pref key bounded.
    ///
    /// Deliberately NOT stored on the layout model / database: open-state is per-user view state, and writing
    /// it back would dirty the shared layout asset and change the
    /// author-time <c>DefaultToOpen</c> default for everyone. <see cref="EditorPrefs"/> (not
    /// <see cref="SessionState"/>) is used so the memory survives a Unity restart, matching the user's
    /// expectation that a foldout stays how they left it "until I change it again".
    /// </summary>
    internal static class FoldoutStateStore
    {
        private const string Prefix = "SasquatchInspector.Foldout/";

        /// <summary>
        /// When false, <see cref="Get"/> ignores stored state (returns the fallback) and <see cref="Set"/> is a
        /// no-op. The verification suite turns this OFF so structural collapse/expand assertions seed
        /// deterministically from the layout's <c>DefaultToOpen</c> default instead of depending on prior runs or
        /// toggles (mirrors how the suite disables <c>EditorSpringFX.MotionEnabled</c>). Always true in normal use.
        /// </summary>
        internal static bool Enabled = true;

        /// <summary>Remembered open-state for <paramref name="key"/>, or <paramref name="fallback"/> (the layout's
        /// author-time default) when the user has never toggled this foldout.</summary>
        internal static bool Get(string key, bool fallback)
        {
            if (!Enabled || string.IsNullOrEmpty(key)) return fallback;
            return EditorPrefs.GetBool(Prefix + key, fallback);
        }

        /// <summary>Persists the open-state for <paramref name="key"/> (no-op for an empty key or when disabled).</summary>
        internal static void Set(string key, bool value)
        {
            if (!Enabled || string.IsNullOrEmpty(key)) return;
            EditorPrefs.SetBool(Prefix + key, value);
        }

        /// <summary>
        /// Builds a stable key for one collapsible site from the inspected <paramref name="target"/>'s type
        /// identity, a per-site <paramref name="discriminator"/> (a bound property path where one exists, else a
        /// structural node path), and a <paramref name="title"/>. Keyed by TYPE (the MonoScript GUID, matching
        /// the per-type layout model), so all instances of a type share open-state — approximating Unity's
        /// UIToolkit <c>viewDataKey</c> scoping. Returns null (→ non-persistent, falls back to the default) when
        /// the target has no resolvable identity.
        /// </summary>
        internal static string Key(UnityEngine.Object target, string discriminator, string title)
        {
            string id = null;
            if (target != null)
            {
                if (!ScriptGuidUtility.TryGetGuid(target, out id) || string.IsNullOrEmpty(id))
                    id = target.GetType().FullName; // weaker but still per-type fallback
            }
            if (string.IsNullOrEmpty(id)) return null;

            return Hash($"{id}|{discriminator}|{title}");
        }

        // 64-bit FNV-1a over the string's UTF-16 code units → 16-char hex. Bounds the pref-key length and avoids
        // path/registry-unfriendly characters; collisions across foldouts of one type are vanishingly unlikely.
        private static string Hash(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                hash = (hash ^ (byte)(c & 0xFF)) * prime;
                hash = (hash ^ (byte)(c >> 8)) * prime;
            }
            return hash.ToString("x16");
        }
    }
}
