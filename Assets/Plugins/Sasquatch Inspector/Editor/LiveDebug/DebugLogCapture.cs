using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// A rolling capture of console output during Play Mode, timestamped on the same clock as
    /// <see cref="DebugRecorder"/> so a log can be placed against the moment a value moved.
    ///
    /// <para><b>Why this earns its place.</b> The audience for Live Debug already writes <c>Debug.Log</c>
    /// everywhere — that is the habit the feature is competing with. Rather than ask them to stop, this pins
    /// the logs they already write next to the number they are about, and turns each one's stack trace into a
    /// jump to the exact line. It is the only signal here that can say something about WHY a value changed
    /// rather than merely what changed with it.</para>
    ///
    /// <para><c>Application.logMessageReceived</c> is public API and hands over the stack trace. Unity's own
    /// console backing store (<c>LogEntries</c>) is an editor internal and is out of bounds under Asset Store
    /// rule §2.5.g, so this keeps its own ring rather than reading the console window's.</para>
    ///
    /// <para>The non-threaded event is used deliberately: it is delivered on the main thread, so entries can be
    /// read straight from UI code with no lock and no marshalling. Logs raised on a worker thread are missed,
    /// which is the right trade — a background-thread log has no frame to be pinned to anyway.</para>
    /// </summary>
    internal static class DebugLogCapture
    {
        private const int Capacity = 256;

        internal struct Entry
        {
            public double Time;
            public LogType Type;
            public string Message;
            /// <summary>Project-relative source path of the first user frame, or null when none could be read.</summary>
            public string File;
            /// <summary>1-based line in <see cref="File"/> — what OpenFileAtLineExternal wants.</summary>
            public int Line;
            /// <summary>"Enemy.TakeDamage" for the first user frame, for the card's row label.</summary>
            public string Method;
            /// <summary>True for a <c>SasquatchDebug.Mark()</c> pin rather than a real console line.</summary>
            public bool IsMark;
            /// <summary>
            /// The raw trace, kept so the first-user-frame parse can be DEFERRED to the moment something
            /// actually asks for <see cref="File"/>. See <see cref="Resolve"/>.
            /// </summary>
            public string StackTrace;
            /// <summary>Whether <see cref="File"/>/<see cref="Line"/>/<see cref="Method"/> have been filled in.</summary>
            public bool FrameResolved;

            /// <summary>
            /// Monotonic id, assigned when this entry is pushed. Its only job is to let <see cref="Resolve"/>
            /// write a parsed copy BACK into the ring safely — <c>Seq % Capacity</c> is the slot it was
            /// written to, and comparing the id against whatever occupies that slot now proves the ring has
            /// not wrapped past it. Without an id a write-back could overwrite a newer, unrelated entry.
            /// </summary>
            public long Seq;
        }

        /// <summary>
        /// Raised for every captured line, so the capture controller can implement stop-on-error without
        /// polling the ring. The bool says whether it was one of OUR markers — a marker must never satisfy
        /// "stop on a warning", or Mark() would sometimes end the recording it was called to annotate.
        /// </summary>
        internal static event Action<LogType, bool> Logged;

        private static readonly Entry[] s_ring = new Entry[Capacity];
        private static int s_write;
        private static int s_count;
        /// <summary>Next value for <see cref="Entry.Seq"/>. See its note.</summary>
        private static long s_seq;
        private static bool s_hooked;
        private static int s_revision;

        /// <summary>
        /// Bumped on every push and every clear, and NEVER reset — a change here means the pin row can look
        /// different, full stop.
        ///
        /// <para><b>Why <see cref="Count"/> cannot be used for this.</b> Count caps at <see cref="Capacity"/>.
        /// Once the ring has wrapped it stops moving entirely, while every entry in it — and therefore every
        /// pin timestamp on every graph — keeps changing. A repaint gate keyed on Count would freeze the pin
        /// row at exactly the moment a session gets busy enough to matter, and would look like a rendering bug
        /// rather than a caching one.</para>
        /// </summary>
        internal static int Revision => s_revision;

        // "  at Enemy.TakeDamage (System.Single amount) [0x00000] in C:\...\Enemy.cs:88"  and the editor's
        // more common "Enemy.TakeDamage (System.Single amount) (at Assets/Scripts/Enemy.cs:88)". Both shapes
        // appear depending on script debugging, so both are matched.
        private static readonly Regex s_frame = new Regex(
            @"(?<method>[\w\.<>`\[\]]+)\s*\([^)]*\)\s*(?:\[0x[0-9a-fA-F]+\]\s*in\s+|\(at\s+)(?<file>[^:]+):(?<line>\d+)\)?",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        /// <summary>Starts capturing. Idempotent — the runner calls it whenever recording arms.</summary>
        internal static void Hook()
        {
            if (s_hooked) return;
            Application.logMessageReceived += OnLog;
            s_hooked = true;
        }

        /// <summary>Stops capturing and keeps what was collected, so a finished session is still inspectable.</summary>
        internal static void Unhook()
        {
            if (!s_hooked) return;
            Application.logMessageReceived -= OnLog;
            s_hooked = false;
        }

        internal static void Clear()
        {
            s_write = 0;
            s_count = 0;
            // Reset with the write cursor, so `Seq % Capacity` keeps naming the slot an entry actually sits
            // in — that identity is what makes Resolve's write-back safe.
            s_seq = 0;
            // Clearing changes the pin row as surely as a push does, and Count alone cannot say so from a
            // gate's point of view (256 -> 0 -> 256 across a play session is invisible to a wrapped ring).
            s_revision++;
        }

        static DebugLogCapture()
        {
            // A capture only means anything within one play session, and the hook must not survive a reload.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Clear();
                if (state == PlayModeStateChange.ExitingPlayMode) Unhook();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Unhook;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            var entry = new Entry
            {
                Time = EditorApplication.timeSinceStartup,
                Type = type,
                Message = condition,
            };

            // The regex frame parse used to run HERE, for every console line the editor ever produced —
            // measured at 4.84 us each, paid whether or not anything was recording and whether or not the
            // line would ever be looked at. Only the moment card reads File/Line/Method, and only for the
            // handful of entries inside its own time window, so the trace is kept and parsed on demand
            // instead. Same result for every entry that IS read; nothing is lost for the ones that are not.
            //
            // The trade is the retained string: 256 traces held instead of discarded (the ring is capped, so
            // this is bounded), against a parse that no longer scales with console traffic.
            entry.StackTrace = stackTrace;

            Push(entry);
        }

        private static void Push(Entry entry)
        {
            entry.Seq = s_seq++;
            s_ring[s_write] = entry;
            s_write = (s_write + 1) % Capacity;
            if (s_count < Capacity) s_count++;
            s_revision++;
            Logged?.Invoke(entry.Type, entry.IsMark);
        }

        /// <summary>
        /// Pins a <c>SasquatchDebug.Mark()</c> on the timeline. Stored in the same ring as console output, on
        /// the same clock, so it lands in the moment card beside whatever else moved on that frame — which is
        /// the entire point of being able to say "here, this frame looked wrong" while the game is running.
        /// </summary>
        internal static void AddMark(string label)
        {
            Push(new Entry
            {
                Time = EditorApplication.timeSinceStartup,
                Type = LogType.Log,
                Message = string.IsNullOrEmpty(label) ? "Marked" : label,
                IsMark = true,
            });
        }

        /// <summary>
        /// The first frame in <paramref name="stackTrace"/> that belongs to the user's own code. Frames inside
        /// UnityEngine/UnityEditor are skipped — the topmost frame of a <c>Debug.Log</c> call is
        /// <c>UnityEngine.Debug:Log</c>, which would send every jump to the same useless place.
        /// </summary>
        private static bool TryFirstUserFrame(string stackTrace, out string file, out int line, out string method)
        {
            file = null; line = 0; method = null;
            try
            {
                foreach (Match m in s_frame.Matches(stackTrace))
                {
                    string meth = m.Groups["method"].Value;
                    if (meth.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                        meth.StartsWith("UnityEditor.", StringComparison.Ordinal))
                        continue;

                    string path = m.Groups["file"].Value.Trim().Replace('\\', '/');
                    if (!int.TryParse(m.Groups["line"].Value, out int ln)) continue;

                    // An absolute path from a debug build still points inside the project; trimming to the
                    // Assets-relative form is what OpenFileAtLineExternal and AssetDatabase both expect.
                    int assets = path.IndexOf("Assets/", StringComparison.Ordinal);
                    if (assets > 0) path = path.Substring(assets);

                    file = path;
                    line = ln;
                    method = ShortMethod(meth);
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological stack costs the jump link, nothing else.
            }
            return false;
        }

        /// <summary>"Enemy.TakeDamage" from "MyGame.Enemies.Enemy.TakeDamage".</summary>
        private static string ShortMethod(string full)
        {
            string[] parts = full.Split('.');
            return parts.Length >= 2 ? parts[parts.Length - 2] + "." + parts[parts.Length - 1] : full;
        }

        /// <summary>Entries whose timestamp falls inside [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
        internal static List<Entry> Between(double from, double to)
        {
            var result = new List<Entry>();
            int oldest = s_count < Capacity ? 0 : s_write;
            for (int i = 0; i < s_count; i++)
            {
                var e = s_ring[(oldest + i) % Capacity];
                if (e.Time >= from && e.Time <= to) result.Add(e);
            }
            return result;
        }

        /// <summary>Every captured entry, oldest first — the source for the graph's log pins.</summary>
        internal static List<Entry> All() => Between(double.MinValue, double.MaxValue);

        /// <summary>
        /// Zero-allocation read of the same sequence <see cref="All"/> returns: <c>At(0)</c> is the oldest
        /// entry, <c>At(Count - 1)</c> the newest.
        ///
        /// <para>Exists because the graph called <see cref="All"/> once per graph per PAINT, and each call
        /// materialised a fresh <c>List&lt;Entry&gt;</c> — measured at 29,368 bytes, so 1.26 MB of garbage per
        /// repaint pass on a 44-graph fixture, for a list that was read once and dropped. Same entries, same
        /// order; only the container is gone.</para>
        ///
        /// <para>Valid only between captures, like any index into a ring: a log arriving mid-iteration shifts
        /// what an index means. Every caller reads it inside one synchronous paint, which is a single main-
        /// thread callback, and <c>logMessageReceived</c> is delivered on that same thread.</para>
        /// </summary>
        /// <summary>
        /// Fills in <see cref="Entry.File"/>/<see cref="Entry.Line"/>/<see cref="Entry.Method"/> from the
        /// retained stack trace, if they are not already. Call this before reading them — <see cref="OnLog"/>
        /// deliberately does not, so the parse is paid only for entries something actually displays.
        /// <para>Idempotent, and safe on an entry with no trace (it simply resolves to nothing, exactly as a
        /// failed parse did before).</para>
        /// </summary>
        internal static Entry Resolve(Entry entry)
        {
            if (entry.FrameResolved) return entry;
            entry.FrameResolved = true;
            if (!string.IsNullOrEmpty(entry.StackTrace)
                && TryFirstUserFrame(entry.StackTrace, out string file, out int line, out string method))
            {
                entry.File = file;
                entry.Line = line;
                entry.Method = method;
            }

            // WRITE THE RESULT BACK INTO THE RING, or this whole method is a no-op that costs 4.84 us a call.
            // Entry is a STRUCT and this takes it by value, so every field set above landed on a local copy:
            // `FrameResolved` stayed false for every entry in the ring forever, the early-out above was dead
            // code, and the "parse once, on demand" claim in the doc was simply untrue. It is not a
            // once-per-card cost either — DebugMomentCard rebuilds on every ScrubChanged, so DRAGGING THE
            // PLAYHEAD re-parsed every visible log's full stack trace on every sample it crossed.
            //
            // The struct and its pre-allocated ring are deliberate (OnLog runs for every console line the
            // editor produces, including a user logging from Update), so the fix is an id rather than making
            // Entry a class: the slot is `Seq % Capacity`, and the id must still match to prove the ring has
            // not wrapped past this entry and handed the slot to someone else.
            int slot = (int)(entry.Seq % Capacity);
            if (slot >= 0 && s_ring[slot].Seq == entry.Seq) s_ring[slot] = entry;
            return entry;
        }

        internal static Entry At(int index)
        {
            int oldest = s_count < Capacity ? 0 : s_write;
            return s_ring[(oldest + index) % Capacity];
        }

        /// <summary>The captured entry closest in time to <paramref name="time"/>, or null when nothing was logged.</summary>
        internal static Entry? NearestTo(double time)
        {
            if (s_count == 0) return null;
            int oldest = s_count < Capacity ? 0 : s_write;
            Entry best = default;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < s_count; i++)
            {
                var e = s_ring[(oldest + i) % Capacity];
                double d = e.Time > time ? e.Time - time : time - e.Time;
                if (d < bestDelta) { bestDelta = d; best = e; }
            }
            return best;
        }

        internal static int Count => s_count;
    }
}
