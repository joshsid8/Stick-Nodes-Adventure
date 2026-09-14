using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Window-free gating logic for the first-run Welcome popup — every EditorPrefs read/write and the version
    /// comparison, split out from the window so it's unit-testable. State lives in EditorPrefs (per-user on the
    /// machine), but the "last seen version" is scoped <b>per project</b> via a stable suffix (see
    /// <see cref="LastSeenVersionKey"/>): importing the package into a fresh project greets the user again, which
    /// is also what makes the first-run experience testable without wiping prefs.
    /// Mirrors the versioned run-once pref convention in <see cref="EditorSpringFX"/> (keys are
    /// <c>"Sasquatch Inspector."</c>-prefixed).
    ///
    /// The version itself IS the whole gate — there is deliberately no permanent "don't show again" opt-out.
    /// The window is the only route to the Tutorial, so opting out of it stranded people away from the one
    /// thing they most needed; showing it once per install and once per major update is quiet enough.
    /// </summary>
    internal static class WelcomeState
    {
        // The version a user last had the Welcome shown for ("" = never), scoped per project by the suffix in
        // LastSeenVersionKey. A MAJOR bump past this re-arms the auto-show (with a "What's New" panel).
        private const string LastSeenVersionKeyBase = "SasquatchInspector.Welcome.LastSeenVersion";

        /// <summary>
        /// The per-project EditorPrefs key for <see cref="LastSeenVersion"/>. EditorPrefs are global per-user on
        /// the machine, so we append a stable per-project id (a hash of the project's Assets path) to track "seen"
        /// independently for each project.
        /// </summary>
        internal static string LastSeenVersionKey => LastSeenVersionKeyBase + "." + ProjectId();

        // A stable, restart-safe id for the current project. Derived from Application.dataPath via FNV-1a — we do
        // NOT use string.GetHashCode(), which is randomized per process on CoreCLR and would change every launch.
        // Moving/renaming the project changes the path (re-greets once, which is harmless).
        private static string ProjectId()
        {
            string path = Application.dataPath ?? "";
            uint hash = 2166136261u;
            foreach (char c in path)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash.ToString("x8");
        }

        /// <summary>The last package version this user had the Welcome shown for ("" if never).</summary>
        public static string LastSeenVersion => EditorPrefs.GetString(LastSeenVersionKey, "");

        /// <summary>
        /// Whether the popup should auto-open on editor load: the current MAJOR version is newer than the last
        /// one acknowledged. A fresh install has an empty <see cref="LastSeenVersion"/> (major 0), so any 1.x+
        /// release shows it the first time — and nothing else until the next major.
        /// </summary>
        public static bool ShouldAutoShow() =>
            ParseMajor(LastSeenVersion) < SasquatchVersion.Major;

        /// <summary>
        /// Whether this showing is an UPGRADE (a real prior version that has since bumped a major) — drives the
        /// "What's New" card. False on a fresh install, so first-timers don't see a changelog.
        /// </summary>
        public static bool IsUpgrade() =>
            !string.IsNullOrEmpty(LastSeenVersion) && ParseMajor(LastSeenVersion) < SasquatchVersion.Major;

        /// <summary>
        /// Records that the user has now seen the Welcome for the current version, so it won't re-pop on the
        /// next domain reload within this major.
        /// </summary>
        public static void Acknowledge() =>
            EditorPrefs.SetString(LastSeenVersionKey, SasquatchVersion.Current);

        /// <summary>The integer MAJOR component of a SemVer-ish string ("2.3.1" → 2). 0 on empty/garbage.</summary>
        public static int ParseMajor(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            int dot = version.IndexOf('.');
            string head = dot >= 0 ? version.Substring(0, dot) : version;
            return int.TryParse(head, out int major) ? major : 0;
        }
    }
}
