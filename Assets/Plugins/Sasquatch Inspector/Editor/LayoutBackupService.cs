using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The plain-File-IO engine behind Sasquatch Inspector's rolling layout backups, factored out of
    /// <see cref="LayoutRegistry"/> so it is unit-testable against a temp directory with a fake clock instead of the
    /// real project paths + EditorPrefs. It owns three concerns: the throttle decision (is a backup due?), taking a
    /// snapshot (copy the whole layouts folder — every <c>.asset</c> AND its <c>.meta</c>, so GUIDs survive — into a
    /// timestamped subfolder), and pruning to the newest N. Every dependency is injected:
    /// <list type="bullet">
    ///   <item>source + backups-root directories (absolute paths), as providers so a relocated User Data folder is
    ///   picked up lazily;</item>
    ///   <item>a UTC clock (so throttle boundaries are deterministic in tests);</item>
    ///   <item>a "last backup time" store (EditorPrefs in production, an in-memory field in tests).</item>
    /// </list>
    /// Every operation is best-effort: a failure logs a warning and never throws, because a backup must never break
    /// the save it is protecting. The config (enabled? how many kept? how often?) is passed per call rather than
    /// stored, so the service stays a pure mechanism and tests can drive every combination explicitly.
    /// </summary>
    internal sealed class LayoutBackupService
    {
        /// <summary>Prefix of every snapshot subfolder: <c>Layouts_{yyyyMMdd_HHmmss}</c>.</summary>
        internal const string SnapshotDirPrefix = "Layouts_";

        private readonly Func<string> _sourceDirAbs;
        private readonly Func<string> _backupsRootAbs;
        private readonly Func<DateTime> _utcNow;
        private readonly Func<DateTime?> _readLastBackupUtc;
        private readonly Action<DateTime> _writeLastBackupUtc;

        internal LayoutBackupService(
            Func<string> sourceDirAbs,
            Func<string> backupsRootAbs,
            Func<DateTime> utcNow,
            Func<DateTime?> readLastBackupUtc,
            Action<DateTime> writeLastBackupUtc)
        {
            _sourceDirAbs = sourceDirAbs ?? throw new ArgumentNullException(nameof(sourceDirAbs));
            _backupsRootAbs = backupsRootAbs ?? throw new ArgumentNullException(nameof(backupsRootAbs));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _readLastBackupUtc = readLastBackupUtc ?? throw new ArgumentNullException(nameof(readLastBackupUtc));
            _writeLastBackupUtc = writeLastBackupUtc ?? throw new ArgumentNullException(nameof(writeLastBackupUtc));
        }

        /// <summary>True when at least <paramref name="frequency"/> has elapsed since the last recorded backup, or
        /// there is no record yet. A non-positive frequency means "always due".</summary>
        internal bool IsBackupDue(TimeSpan frequency)
        {
            if (frequency <= TimeSpan.Zero) return true;
            DateTime? last = _readLastBackupUtc();
            if (!last.HasValue) return true;
            return (_utcNow() - last.Value) >= frequency;
        }

        /// <summary>Automatic path: takes a snapshot only when <paramref name="enabled"/> AND a backup is due per
        /// <paramref name="frequency"/>. Returns the snapshot directory, or null when skipped / nothing to snapshot.</summary>
        internal string BackupIfDue(bool enabled, TimeSpan frequency, int maxKept)
        {
            if (!enabled) return null;
            if (!IsBackupDue(frequency)) return null;
            return WriteSnapshot(maxKept);
        }

        /// <summary>Unconditionally snapshots the current source folder (every file — each layout <c>.asset</c> AND
        /// its <c>.meta</c> ⇒ GUIDs preserved) into <c>{root}/Layouts_{stamp}</c>, prunes to the newest
        /// <paramref name="maxKept"/>, and records the backup time. Bypasses the enabled/throttle gates — the restore
        /// path uses it to guarantee its "current layouts are backed up first" reversibility promise. Returns the
        /// snapshot dir, or null when there was nothing to snapshot or it failed. Best-effort — never throws.
        /// <para><paramref name="protectDirAbs"/> is exempted from the prune. The restore path MUST pass the snapshot
        /// it is restoring from: the snapshot written here always sorts newest, so at the shipped cap of 1 an
        /// unprotected prune would delete the very backup being restored (and at any full cap N, the oldest — which
        /// is exactly what a user picks when rolling further back).</para></summary>
        internal string WriteSnapshot(int maxKept, string protectDirAbs = null)
        {
            try
            {
                string sourceAbs = _sourceDirAbs();
                if (string.IsNullOrEmpty(sourceAbs) || !Directory.Exists(sourceAbs)) return null; // nothing saved yet
                if (Directory.GetFiles(sourceAbs, "*.asset").Length == 0) return null;             // empty — nothing to snapshot

                string rootAbs = _backupsRootAbs();
                if (string.IsNullOrEmpty(rootAbs)) return null;

                // Stamp the folder from the (injectable) clock so tests get unique, ordered names without touching
                // the wall clock. Local time keeps the name human-readable in the restore menu.
                // InvariantCulture is load-bearing, not cosmetic: ListBackups sorts these names with
                // CompareOrdinal and relies on lexicographic == chronological. A culture whose default
                // calendar is not Gregorian (th-TH is Buddhist, ar-SA Um al-Qura) writes a different year,
                // so a region change between snapshots would invert that order and prune the NEWEST backups.
                string stamp = _utcNow().ToLocalTime().ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                string destDir = Path.Combine(rootAbs, SnapshotDirPrefix + stamp);
                Directory.CreateDirectory(destDir);

                // Copy every file (each layout .asset AND its .meta) so a restore preserves GUIDs.
                foreach (string f in Directory.GetFiles(sourceAbs))
                    File.Copy(f, Path.Combine(destDir, Path.GetFileName(f)), overwrite: true);

                Prune(maxKept, protectDirAbs);
                _writeLastBackupUtc(_utcNow());
                return destDir;
            }
            catch (Exception e)
            {
                // Backups are best-effort insurance — a failure must never break the actual save.
                Debug.LogWarning("[Sasquatch Inspector] Layout backup skipped: " + e.Message);
                return null;
            }
        }

        /// <summary>Deletes all but the newest <paramref name="maxKept"/> snapshot folders. maxKept is clamped to ≥ 1.
        /// <paramref name="protectDirAbs"/>, when given, is never deleted and does not consume a kept slot — a caller
        /// that is actively reading a snapshot passes it so the prune cannot pull the folder out from under it.
        /// Best-effort — never throws.</summary>
        internal void Prune(int maxKept, string protectDirAbs = null)
        {
            maxKept = Math.Max(1, maxKept);
            string protectedFull = NormalizeDir(protectDirAbs);
            var dirs = ListBackups(); // newest first
            int kept = 0;
            foreach (string dir in dirs)
            {
                // The protected dir is skipped outright rather than counted, so protecting one snapshot never
                // evicts an extra one to stay under the cap.
                if (protectedFull != null && NormalizeDir(dir) == protectedFull) continue;
                if (kept < maxKept) { kept++; continue; }
                try { Directory.Delete(dir, recursive: true); } catch { /* leave it; pruning is best-effort */ }
            }
        }

        // Comparable absolute form for a directory path: full path, no trailing separator, case-insensitive on the
        // platforms where the filesystem is (Windows/macOS). Null/empty in ⇒ null out ("nothing protected").
        private static string NormalizeDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                return Path.GetFullPath(dir)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           .ToLowerInvariant();
            }
            catch { return null; }
        }

        /// <summary>Absolute paths of the current snapshot folders, newest first. Empty when none exist / on error.</summary>
        internal List<string> ListBackups()
        {
            var result = new List<string>();
            try
            {
                string rootAbs = _backupsRootAbs();
                if (string.IsNullOrEmpty(rootAbs) || !Directory.Exists(rootAbs)) return result;
                var dirs = Directory.GetDirectories(rootAbs, SnapshotDirPrefix + "*");
                // Sort by folder name descending. The embedded yyyyMMdd_HHmmss stamp is monotonic, so lexicographic
                // order equals chronological order — no dependency on filesystem mtime resolution (deterministic in tests).
                Array.Sort(dirs, (a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
                result.AddRange(dirs);
            }
            catch { /* return whatever we gathered */ }
            return result;
        }
    }
}
