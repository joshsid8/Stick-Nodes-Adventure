using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Finds the places in the project's own source that assign — or read — a given field.
    ///
    /// <para><b>What this is, precisely.</b> A text search with a good presentation: not a compiler, and not a
    /// runtime observation. The inspector samples values; it cannot see which line wrote one, and getting that
    /// would need IL weaving or an attached debugger, both of which break the package's promise that your code
    /// is never touched. So the honest offer is "here is where it could have come from", and every string here
    /// is phrased that way.</para>
    ///
    /// <para><b>Scope is the whole design, and it took two passes to get right.</b> Pass 1 searched every
    /// <c>.cs</c> under <c>Assets/</c> for a field *name*: 25 lines across 17 files for <c>health</c> while the
    /// declaring script held 3, because half a dozen unrelated scripts have their own <c>health</c>. Pass 2
    /// narrowed that to an opt-in "search the rest of the project" section. Pass 3 deleted it. Even filtered to
    /// files that name the declaring type it was a guess presented as a finding, and the question it answered —
    /// "does some OTHER script assign a field with this name?" — is one almost nobody has. The scope is now
    /// exactly one file, the one that declares the field, and every row in it is a fact.</para>
    ///
    /// <para>What replaced it is <see cref="Result.OwnReads"/>: who READS this value. That was already being
    /// collected and thrown away as a count, and it answers a question people genuinely have — "if I change
    /// this, what breaks?"</para>
    /// </summary>
    internal static class DebugWriteSiteScanner
    {
        internal struct Site
        {
            /// <summary>Project-relative path.</summary>
            public string Path;
            /// <summary>1-based, ready for OpenFileAtLineExternal.</summary>
            public int Line;
            /// <summary>The source line, trimmed — shown as the row label.</summary>
            public string Text;
            /// <summary>True when the line assigns the field; false when it only reads it.</summary>
            public bool IsWrite;
        }

        internal sealed class Result
        {
            /// <summary>Assignments inside the declaring type's own file — the trustworthy answer.</summary>
            public List<Site> OwnWrites = new List<Site>();
            /// <summary>Reads inside the declaring type's own file. Useful ("who looks at this?"), kept separate.</summary>
            public List<Site> OwnReads = new List<Site>();
            public string OwnFile;
        }

        private static readonly Dictionary<(Type, string), Result> s_cache = new Dictionary<(Type, string), Result>();

        [InitializeOnLoadMethod]
        private static void Register()
        {
            AssemblyReloadEvents.beforeAssemblyReload += s_cache.Clear;
        }

        /// <summary>
        /// Writes and reads inside the declaring type's own file. One file, one read, cached per (type, field)
        /// until the next assembly reload — cheap enough that the card can build it on every open.
        /// </summary>
        internal static Result Find(Type declaringType, string fieldName)
        {
            var result = new Result();
            if (declaringType == null || string.IsNullOrEmpty(fieldName)) return result;

            var key = (declaringType, fieldName);
            if (s_cache.TryGetValue(key, out var cached)) return cached;

            if (MethodSourcePreview.TryLocateField(declaringType, fieldName, out string ownPath, out _))
            {
                result.OwnFile = ownPath;
                ScanFile(ownPath, BuildWritePattern(fieldName), BuildReadPattern(fieldName), result.OwnWrites, result.OwnReads);
            }

            s_cache[key] = result;
            return result;
        }

        /// <summary>
        /// Matches an assignment: plain <c>=</c> (but not <c>==</c>, <c>&gt;=</c>, …), a compound assignment, or
        /// an increment/decrement on either side. A declaration with an initialiser assigns too, and is kept.
        /// </summary>
        private static Regex BuildWritePattern(string fieldName)
        {
            string f = Regex.Escape(fieldName);
            return new Regex(
                $@"(?<![\w.])(?:this\.)?{f}\s*(?:(?<!=|!|<|>|\+|-|\*|/|%|&|\||\^)=(?!=)|\+=|-=|\*=|/=|%=|\+\+|--)"
                + $@"|(?:\+\+|--)\s*(?:this\.)?{f}(?![\w])",
                RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
        }

        /// <summary>Any other mention of the field — a comparison, an interpolation, an argument.</summary>
        private static Regex BuildReadPattern(string fieldName)
            => new Regex($@"(?<![\w.])(?:this\.)?{Regex.Escape(fieldName)}(?![\w])",
                RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

        private static void ScanFile(string path, Regex write, Regex read, List<Site> writes, List<Site> reads)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { return; }
            ScanText(path, text, write, read, writes, reads);
        }

        private static void ScanText(string path, string text, Regex write, Regex read, List<Site> writes, List<Site> reads)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].TrimEnd('\r');
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                // Cheap comment rejection. It cannot see a block comment's interior — the accepted cost of not
                // writing a C# parser for a hint list. A spurious row costs one glance.
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("///", StringComparison.Ordinal)) continue;

                bool isWrite;
                try { isWrite = write.IsMatch(raw); }
                catch (RegexMatchTimeoutException) { continue; }

                if (isWrite)
                {
                    writes.Add(MakeSite(path, i + 1, trimmed, true));
                    continue;
                }
                if (read == null || reads == null) continue;

                try { if (!read.IsMatch(raw)) continue; }
                catch (RegexMatchTimeoutException) { continue; }
                reads.Add(MakeSite(path, i + 1, trimmed, false));
            }
        }

        private static Site MakeSite(string path, int line, string trimmed, bool isWrite) => new Site
        {
            Path = path,
            Line = line,
            Text = trimmed.Length > 120 ? trimmed.Substring(0, 117) + "…" : trimmed,
            IsWrite = isWrite,
        };

        /// <summary>The card's heading — names the FILE, and says "assign", never "did".</summary>
        internal static string Describe(Result result, string fieldName)
        {
            if (result == null || result.OwnWrites.Count == 0)
                return $"Nothing in this script assigns “{fieldName}”";
            string file = string.IsNullOrEmpty(result.OwnFile) ? "this script" : Path.GetFileName(result.OwnFile);
            string n = result.OwnWrites.Count == 1 ? "1 place" : $"{result.OwnWrites.Count} places";
            return $"{n} in {file} assign “{fieldName}”";
        }
    }
}
