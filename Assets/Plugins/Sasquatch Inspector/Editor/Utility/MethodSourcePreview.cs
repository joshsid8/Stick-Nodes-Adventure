using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Extracts a short, syntax-highlighted source snippet for a method — the hover "code peek" behind the
    /// button method pickers. Resolution is best-effort: the method's declaring type → its MonoScript path
    /// (the rename-proof GUID map) → a brace-matched slice of the file, capped at <see cref="MaxLines"/>
    /// lines with a trailing ellipsis. Returns null whenever anything can't be resolved (no backing file,
    /// unreadable, declaration not found) — the picker simply shows no peek. Heuristic by design: this is a
    /// reminder of what a method does, not a compiler; a pathological string literal full of braces may
    /// over- or under-slice, which costs a slightly odd preview and nothing else.
    /// </summary>
    internal static class MethodSourcePreview
    {
        internal const int MaxLines = 12;

        /// <summary>What shape of declaration a snippet is looking for — each has its own line pattern.</summary>
        private enum Kind { Method, Property, Field, Type }

        // Per-domain cache — statics reset on domain reload, which is exactly when source can change.
        private static readonly Dictionary<(Type, string), string> _cache = new();

        /// <summary>
        /// Highlighted rich-text snippet for the first declaration of method <paramref name="methodName"/> on
        /// <paramref name="type"/> (resolved through the same lookup the button invoker uses, so inherited
        /// methods read from their declaring type's file), or null when no source can be found.
        /// </summary>
        public static string GetSnippet(Type type, string methodName, Type sourceHint = null)
            => CachedSnippet(type, methodName, Kind.Method, sourceHint);

        /// <summary>Highlighted snippet for a C# property declaration (auto, expression-bodied, or full
        /// accessor block) — the peek behind the C# Property pickers. Null when no source can be found.</summary>
        public static string GetPropertySnippet(Type type, string propertyName, Type sourceHint = null)
            => CachedSnippet(type, propertyName, Kind.Property, sourceHint);

        /// <summary>Highlighted snippet for a FIELD declaration, with the attribute lines above it — which are
        /// the interesting part: <c>[SerializeField]</c>, <c>[Range]</c>, <c>[Tooltip]</c>. Null when no
        /// source can be found.</summary>
        public static string GetFieldSnippet(Type type, string fieldName, Type sourceHint = null)
            => CachedSnippet(type, fieldName, Kind.Field, sourceHint);

        /// <summary>Highlighted snippet for a TYPE declaration — its attributes, the class/struct line with
        /// its base list, and the head of its body (the fields are what identify it). Null when no source can
        /// be found.</summary>
        public static string GetTypeSnippet(Type type, Type sourceHint = null)
            => type == null ? null : CachedSnippet(type, ShortName(type), Kind.Type, sourceHint);

        /// <summary>
        /// Highlighted snippet for a member whose KIND isn't known up front. A condition driver is offered as
        /// one flat list of serialized fields, bool properties and bool methods, so the peek has to work out
        /// which it got: property → method → field, first that resolves. The declaration patterns don't
        /// overlap, so at most one of them can match.
        /// </summary>
        public static string GetMemberSnippet(Type type, string name, Type sourceHint = null)
            => GetPropertySnippet(type, name, sourceHint)
               ?? GetSnippet(type, name, sourceHint)
               ?? GetFieldSnippet(type, name, sourceHint);

        private static string CachedSnippet(Type type, string name, Kind kind, Type sourceHint)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;
            // The hint widens which FILES are searched, so two lookups that differ only by hint can legitimately
            // differ in result — a hintless miss must not poison a hinted hit.
            var key = (type, KindPrefix(kind) + name + "|" + (sourceHint != null ? sourceHint.FullName : ""));
            if (_cache.TryGetValue(key, out string cached)) return cached;

            string snippet = null;
            try { snippet = BuildSnippet(type, name, kind, sourceHint); }
            catch { /* best-effort: an unparseable file just shows no peek */ }
            _cache[key] = snippet;
            return snippet;
        }

        private static string KindPrefix(Kind kind)
        {
            switch (kind)
            {
                case Kind.Property: return "P:";
                case Kind.Field: return "F:";
                case Kind.Type: return "T:";
                default: return "M:";
            }
        }

        internal static string BuildSnippet(Type type, string name, bool property = false)
            => BuildSnippet(type, name, property ? Kind.Property : Kind.Method, null);

        private static string BuildSnippet(Type type, string name, Kind kind, Type sourceHint)
        {
            // Resolution only decides WHICH FILE to read (inherited members live in their declaring type's
            // file); a member that doesn't reflect through these lookups (e.g. a static value-source method)
            // just falls back to the asked type's own file, where the text search still finds it.
            var declaring = ResolveDeclaringType(type, name, kind);
            if (declaring == null) return null;

            string search = kind == Kind.Type ? ShortName(declaring) : name;
            foreach (string path in CandidateSourcePaths(declaring, sourceHint))
            {
                var lines = File.ReadAllLines(path);
                int decl = FindDeclarationLine(lines, search, kind);
                if (decl < 0) continue;    // not in this file — the next candidate might have it

                var slice = kind == Kind.Field ? SliceField(lines, decl) : SliceMember(lines, decl);
                if (kind == Kind.Type) PrependAttributeLines(lines, decl, slice);
                if (slice.Count > 0) return CodeHighlighter.Highlight(string.Join("\n", slice));
            }
            return null;
        }

        // Located declarations, keyed like _cache. Separate from it because a located line is wanted far less
        // often than a snippet (only when a code card opens), so widening the snippet cache's value type would
        // cost every peek a tuple it never reads.
        private static readonly Dictionary<(Type, string), (string Path, int Line)> _locations = new();

        /// <summary>
        /// The source file and <b>1-based</b> declaration line for a member — the pair
        /// <c>InternalEditorUtility.OpenFileAtLineExternal</c> needs to jump straight to it. Resolution is the
        /// same best-effort walk <see cref="BuildSnippet"/> does, and returns false on any miss.
        /// </summary>
        /// <remarks>
        /// <see cref="FindDeclarationLine"/> is 0-based (it indexes the lines array); editors count from 1.
        /// The conversion happens here, once, so no caller has to remember it.
        /// </remarks>
        internal static bool TryLocateField(Type type, string fieldName, out string path, out int line,
            Type sourceHint = null)
            => TryLocate(type, fieldName, Kind.Field, sourceHint, out path, out line);

        /// <summary>As <see cref="TryLocateField"/>, for a property, method or field — first shape that resolves.</summary>
        internal static bool TryLocateMember(Type type, string name, out string path, out int line,
            Type sourceHint = null)
            => TryLocate(type, name, Kind.Property, sourceHint, out path, out line)
               || TryLocate(type, name, Kind.Method, sourceHint, out path, out line)
               || TryLocate(type, name, Kind.Field, sourceHint, out path, out line);

        private static bool TryLocate(Type type, string name, Kind kind, Type sourceHint,
            out string path, out int line)
        {
            path = null;
            line = 0;
            if (type == null || string.IsNullOrEmpty(name)) return false;

            var key = (type, KindPrefix(kind) + name + "|@|" + (sourceHint != null ? sourceHint.FullName : ""));
            if (_locations.TryGetValue(key, out var hit))
            {
                path = hit.Path;
                line = hit.Line;
                return path != null;
            }

            try
            {
                var declaring = ResolveDeclaringType(type, name, kind);
                if (declaring != null)
                {
                    string search = kind == Kind.Type ? ShortName(declaring) : name;
                    foreach (string candidate in CandidateSourcePaths(declaring, sourceHint))
                    {
                        var lines = File.ReadAllLines(candidate);
                        int decl = FindDeclarationLine(lines, search, kind);
                        if (decl < 0) continue;
                        path = candidate;
                        line = decl + 1;
                        break;
                    }
                }
            }
            catch { /* best-effort, exactly like the snippet path: an unreadable file simply offers no jump */ }

            _locations[key] = (path, line);
            return path != null;
        }

        private static Type ResolveDeclaringType(Type type, string name, Kind kind)
        {
            if (kind == Kind.Type) return type;

            System.Reflection.MemberInfo member;
            switch (kind)
            {
                case Kind.Property: member = PropertyInvoker.ResolveProperty(type, name); break;
                case Kind.Field: member = ResolveField(type, name); break;
                default: member = ButtonInvoker.ResolveMethod(type, name); break;
            }
            return member != null ? member.DeclaringType : type;
        }

        // Walked base-first with DeclaredOnly so a `new`-shadowed field resolves to the one actually in scope.
        private static System.Reflection.FieldInfo ResolveField(Type type, string name)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, flags);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>
        /// The .cs files that might declare <paramref name="declaring"/>, best first. Each is only accepted
        /// once the declaration is actually FOUND in it, so a wrong guess costs a file read, never a wrong
        /// peek. The GUID map alone is not enough: it only knows top-level MonoBehaviour / ScriptableObject
        /// classes whose filename matches the class name, and the types these peeks care about mostly aren't
        /// that. In order:
        ///  1. its own MonoScript, when it has one;
        ///  2. a script named after it — the convention, and a lone [Serializable] class's best hope;
        ///  3. its OUTER type's file — a nested type lives there by definition;
        ///  4. the file of <paramref name="hint"/>, the type that owns the thing being peeked. Companion
        ///     classes are the norm, not the exception: a [SerializeReference] hierarchy and the nested rows
        ///     behind a property path are usually written in the host's own file.
        /// </summary>
        private static IEnumerable<string> CandidateSourcePaths(Type declaring, Type hint)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string p in FilesFor(declaring))
                if (seen.Add(p)) yield return p;

            for (var outer = declaring.DeclaringType; outer != null; outer = outer.DeclaringType)
                foreach (string p in FilesFor(outer))
                    if (seen.Add(p)) yield return p;

            if (hint != null && hint != declaring)
                foreach (string p in FilesFor(hint))
                    if (seen.Add(p)) yield return p;
        }

        private static IEnumerable<string> FilesFor(Type type)
        {
            if (ScriptGuidUtility.TryGetGuid(type, out string guid) && !string.IsNullOrEmpty(guid))
            {
                string mapped = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(mapped) && File.Exists(mapped)) yield return mapped;
            }

            string shortName = ShortName(type);
            foreach (string found in AssetDatabase.FindAssets(shortName + " t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(found);
                if (string.IsNullOrEmpty(p) || !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                // FindAssets matches names loosely, so "Stats" also returns "StatsPanel.cs" — take the exact one.
                if (!string.Equals(Path.GetFileNameWithoutExtension(p), shortName, StringComparison.Ordinal)) continue;
                if (File.Exists(p)) yield return p;
            }
        }

        /// <summary>A generic type's reflected name carries an arity suffix (<c>Stack`1</c>); source doesn't.</summary>
        private static string ShortName(Type type)
        {
            string n = type.Name;
            int tick = n.IndexOf('`');
            return tick >= 0 ? n.Substring(0, tick) : n;
        }

        // A declaration line starts (after optional attributes) with modifiers/return type and then the
        // member name — followed by '(' for a method, or by '{' / '=>' / end-of-line (next-line-brace
        // style) for a property. Call sites ("Foo(x);", "x = Foo(", "obj.Foo(") and plain locals don't fit
        // those shapes, so this reliably skips them without needing a real parser.
        //
        // A FIELD is the one shape that needs a stricter guard: "TYPE name;" also describes `return health;`
        // and any local, so at least one attribute or modifier is REQUIRED — which every serialized field
        // has (public, or [SerializeField] private).
        private const string DeclModifiers =
            @"(?:public|private|protected|internal|static|virtual|override|sealed|async|extern|new|unsafe|partial|readonly|const|volatile|abstract|ref)\s+";

        private static int FindDeclarationLine(string[] lines, string name, Kind kind)
        {
            string escaped = Regex.Escape(name);
            string pattern;
            switch (kind)
            {
                case Kind.Type:
                    pattern = @"^\s*(?:\[[^\]]*\]\s*)*(?:" + DeclModifiers + @")*(?:(?:class|struct|interface|record|enum)\s+)+" + escaped + @"\b";
                    break;
                case Kind.Field:
                    pattern = @"^\s*(?:(?:\[[^\]]*\]\s*)+(?:" + DeclModifiers + @")*|(?:" + DeclModifiers + @")+)"
                              + @"[\w<>\[\],\.\?]+\s+" + escaped + @"\s*(?:=|;|,)";
                    break;
                case Kind.Property:
                    pattern = @"^\s*(?:\[[^\]]*\]\s*)*(?:" + DeclModifiers + @")*[\w<>\[\],\.\?]+\s+" + escaped + @"\s*(\{|=>|$)";
                    break;
                default:
                    pattern = @"^\s*(?:\[[^\]]*\]\s*)*(?:" + DeclModifiers + @")*[\w<>\[\],\.\?]+\s+" + escaped + @"\s*\(";
                    break;
            }

            // These patterns run over EVERY line of a file nobody here wrote, and the field shape nests
            // quantifiers (modifiers, then a type token that can also match a modifier word) — the classic
            // backtracking blow-up. A match timeout bounds it: the throw lands in BuildSnippet's caller,
            // which treats any failure as "no peek", so a pathological line costs a missing card and
            // nothing else.
            var decl = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;
                if (decl.IsMatch(lines[i])) return i;
            }
            return -1;
        }

        // The attribute lines sitting immediately above a declaration are part of what you came to see — a
        // field's [SerializeField] / [Range] / [Tooltip], a type's [Serializable]. Collected separately from
        // the brace-matched body so an attribute argument containing braces can't confuse the depth count.
        private static void PrependAttributeLines(string[] lines, int decl, List<string> slice)
        {
            int indent = lines[decl].Length - lines[decl].TrimStart().Length;
            int first = FirstAttributeLine(lines, decl);
            for (int i = decl - 1; i >= first; i--) slice.Insert(0, Dedent(lines[i], indent));
        }

        private static int FirstAttributeLine(string[] lines, int decl)
        {
            int first = decl;
            for (int i = decl - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) first = i;
                else break;
            }
            return first;
        }

        // A field runs to the line that ends its statement — normally the declaration line itself, but a
        // collection initialiser can span several. Attributes above it come along.
        private static List<string> SliceField(string[] lines, int decl)
        {
            var captured = new List<string>();
            int indent = lines[decl].Length - lines[decl].TrimStart().Length;
            for (int i = FirstAttributeLine(lines, decl); i < lines.Length && captured.Count < MaxLines; i++)
            {
                captured.Add(Dedent(lines[i], indent));
                if (i >= decl && lines[i].TrimEnd().EndsWith(";")) break;
            }
            return captured;
        }

        // De-indents to the declaration's own indent, so the peek starts at column 0.
        private static string Dedent(string line, int indent)
            => line.Length > indent && string.IsNullOrWhiteSpace(line.Substring(0, indent))
                ? line.Substring(indent)
                : line.TrimStart();

        // From the declaration line: an expression body runs to the first line ending in ';' before any '{';
        // a block body (method or property accessors) runs to the brace that closes the one opening it.
        // Capped at MaxLines + an ellipsis line, and de-indented to the declaration's own indent so the
        // peek starts at column 0.
        private static List<string> SliceMember(string[] lines, int decl)
        {
            var captured = new List<string>();
            int indent = lines[decl].Length - lines[decl].TrimStart().Length;
            int depth = 0;
            bool sawBrace = false;
            bool truncated = false;

            for (int i = decl; i < lines.Length; i++)
            {
                string line = lines[i];
                if (captured.Count >= MaxLines) { truncated = true; break; }
                captured.Add(Dedent(line, indent));

                foreach (char c in line)
                {
                    if (c == '{') { depth++; sawBrace = true; }
                    else if (c == '}') depth--;
                }
                if (sawBrace && depth <= 0) return captured;

                // Expression-bodied: "=> …;" ends the method before any brace opens.
                if (!sawBrace && line.TrimEnd().EndsWith(";")) return captured;
            }

            if (truncated) captured.Add("    …");
            return captured;
        }
    }
}
