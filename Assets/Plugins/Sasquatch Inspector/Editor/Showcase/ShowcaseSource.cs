using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Produces the MINIMAL C# snippet for a Showcase page: it walks the page's layout to learn which members
    /// (fields / properties / methods) it actually references, then lifts just those declarations — with their
    /// attributes and any nested types they use — straight out of the demo type's real <c>.cs</c>. No usings,
    /// namespace, or class wrapper, so the info pane reads as "here's the field that did this." When a page
    /// references no members at all (e.g. the Script Header page), <see cref="BuildSnippet"/> reports
    /// <c>hasCode = false</c> so the window can show a short description instead.
    /// </summary>
    internal static class ShowcaseSource
    {
        // A member declaration is "public Foo bar...", "public void Baz(...)", etc. We anchor on an access
        // modifier on the same line (every Showcase target member has one) so a usage like "bar = 5;" inside a
        // method body is never mistaken for the declaration.
        private static readonly Regex s_modifier = new Regex(@"\b(public|private|protected|internal)\b", RegexOptions.Compiled);

        /// <summary>
        /// Builds the curated source snippet for <paramref name="target"/> given the page's <paramref name="root"/>
        /// layout. Returns the plain (un-highlighted) text; <paramref name="hasCode"/> is false when the page
        /// references no members.
        /// </summary>
        public static string BuildSnippet(ScriptableObject target, LayoutNode root, out bool hasCode)
        {
            hasCode = false;
            if (target == null || root == null) return null;

            var memberNames = new HashSet<string>(StringComparer.Ordinal);
            CollectMembers(root, memberNames);
            if (memberNames.Count == 0) return null;

            var mono = MonoScript.FromScriptableObject(target);
            string source = mono != null ? mono.text : null;
            if (string.IsNullOrEmpty(source)) return null;

            var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Type targetType = target.GetType();

            // The names of nested types (declared inside the target) referenced by the chosen members, so a
            // collection like List<LootEntry> also shows the LootEntry struct (and any enum it uses).
            var nestedTypeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in memberNames)
                CollectNestedTypes(targetType, name, nestedTypeNames);

            // Collect the source line-ranges to lift, then merge + order them.
            var ranges = new List<(int start, int end)>();
            foreach (var name in memberNames)
            {
                var r = FindMemberRange(lines, name);
                if (r.HasValue) ranges.Add(r.Value);
            }
            foreach (var typeName in nestedTypeNames)
            {
                var r = FindTypeRange(lines, typeName);
                if (r.HasValue) ranges.Add(r.Value);
            }

            if (ranges.Count == 0) return null;

            string snippet = Emit(lines, ranges);
            if (string.IsNullOrWhiteSpace(snippet)) return null;

            hasCode = true;
            return snippet;
        }

        // ---------------------------------------------------------------- Layout walk ----

        private static void CollectMembers(LayoutNode node, HashSet<string> into)
        {
            switch (node)
            {
                case BindingNode b:
                    AddTopSegment(b.PropertyPath, into);
                    AddName(b.ValueSourceMethod, into);
                    AddName(b.ValidationMethod, into);
                    break;
                case PropertyNode p:
                    AddName(p.PropertyName, into);
                    break;
                case ButtonNode btn:
                    AddName(btn.MethodName, into);
                    break;
                case CollectionNode c:
                    // The collection FIELD is the top-level member; its ItemLayout binds the ELEMENT type's
                    // members, which surface instead through the nested-type declaration (CollectNestedTypes).
                    AddTopSegment(c.PropertyPath, into);
                    break;
                case ContainerNode cont:
                    foreach (var child in cont.Children) CollectMembers(child, into);
                    break;
            }
        }

        private static void AddName(string name, HashSet<string> into)
        {
            if (!string.IsNullOrEmpty(name)) into.Add(name);
        }

        // The first path segment is the member on the inspected object ("damageRange", or "foo" of "foo.bar").
        private static void AddTopSegment(string path, HashSet<string> into)
        {
            if (string.IsNullOrEmpty(path)) return;
            int dot = path.IndexOf('.');
            int bracket = path.IndexOf('[');
            int cut = path.Length;
            if (dot >= 0) cut = Mathf.Min(cut, dot);
            if (bracket >= 0) cut = Mathf.Min(cut, bracket);
            string seg = path.Substring(0, cut).Trim();
            if (seg.Length > 0) into.Add(seg);
        }

        // ---------------------------------------------------------------- Reflection ----

        // Adds nested types (declared inside targetType) reachable from member `name` — directly, as an array /
        // List element, and one level into a nested type's own fields (so LootEntry pulls in its Rarity enum).
        private static void CollectNestedTypes(Type targetType, string name, HashSet<string> into)
        {
            Type memberType = ResolveMemberType(targetType, name);
            AddNestedType(targetType, memberType, into);
        }

        private static void AddNestedType(Type targetType, Type t, HashSet<string> into)
        {
            t = ElementType(t);
            if (t == null || !(t.IsNested && t.DeclaringType == targetType)) return;
            if (!into.Add(t.Name)) return; // already handled (also guards cycles)

            // Recurse one level into the nested type's own fields (e.g. LootEntry.rarity -> Rarity enum).
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                AddNestedType(targetType, f.FieldType, into);
        }

        private static Type ResolveMemberType(Type targetType, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var f = targetType.GetField(name, flags);
            if (f != null) return f.FieldType;
            var p = targetType.GetProperty(name, flags);
            if (p != null) return p.PropertyType;
            return null;
        }

        // Unwraps T[] / List<T> to T; otherwise returns the type unchanged.
        private static Type ElementType(Type t)
        {
            if (t == null) return null;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return t.GetGenericArguments()[0];
            return t;
        }

        // ---------------------------------------------------------------- Extraction ----

        // Finds the source line-range of a member's declaration: the line whose access modifier + name read as
        // a declaration, extended backward over its attributes/comments and forward to the end of the member.
        private static (int start, int end)? FindMemberRange(string[] lines, string name)
        {
            var nameRx = new Regex(@"(?<![\w.])" + Regex.Escape(name) + @"\s*[\(\{;=<]");
            for (int i = 0; i < lines.Length; i++)
            {
                if (!s_modifier.IsMatch(lines[i])) continue;
                if (!nameRx.IsMatch(lines[i])) continue;
                return (ExtendBackward(lines, i), ExtendForward(lines, i));
            }
            return null;
        }

        // Same as FindMemberRange but for a nested type declaration (class / struct / enum Name).
        private static (int start, int end)? FindTypeRange(string[] lines, string typeName)
        {
            var rx = new Regex(@"\b(class|struct|enum)\s+" + Regex.Escape(typeName) + @"\b");
            for (int i = 0; i < lines.Length; i++)
            {
                if (!rx.IsMatch(lines[i])) continue;
                return (ExtendBackward(lines, i), ExtendForward(lines, i));
            }
            return null;
        }

        // Walks up to absorb the declaration's leading attribute lines ([...]) and /// or // comment lines.
        //
        // THIS IS WHY THE Targets/ SCRIPTS CARRY NO COMMENTS ABOVE THEIR FIELDS. Absorbing them is correct —
        // a [Tooltip] or an explanatory line is part of the declaration a user would write — but it also means
        // every maintenance note left above a Showcase target's field is published verbatim into the info
        // pane, where it reads as clutter in what is supposed to be a clean "here's the field that did this"
        // snippet. So the targets keep their notes above the CLASS, which this never reaches: the walk starts
        // at the member and stops at the first line that is neither an attribute nor a comment.
        //
        // Same trap for a blank line — it stops the walk, so a comment separated from its field by one is
        // silently NOT lifted. Do not rely on that to hide a note; put it above the class where it is obvious.
        private static int ExtendBackward(string[] lines, int declLine)
        {
            int start = declLine;
            for (int i = declLine - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[") || t.StartsWith("//") || t.StartsWith("///")) start = i;
                else break;
            }
            return start;
        }

        // Walks down until the member/type is complete: brace depth returns to 0 (ignoring braces inside
        // strings/comments) on a line that ends the statement or block (';' or '}').
        private static int ExtendForward(string[] lines, int declLine)
        {
            int depth = 0;
            bool inBlockComment = false;
            for (int i = declLine; i < lines.Length; i++)
            {
                depth += NetBraces(lines[i], ref inBlockComment);
                string t = lines[i].TrimEnd();
                if (depth <= 0 && (t.EndsWith("}") || t.EndsWith(";")))
                    return i;
            }
            return Mathf.Min(declLine, lines.Length - 1);
        }

        // Net '{' minus '}' on a line, skipping string/char literals and // and /* */ comments so braces inside
        // an interpolated string (e.g. $"{expr}") or a comment don't throw the balance off.
        private static int NetBraces(string line, ref bool inBlockComment)
        {
            int net = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inBlockComment)
                {
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { inBlockComment = false; i++; }
                    continue;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;            // line comment
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*') { inBlockComment = true; i++; continue; }

                if (c == '"') { i = SkipString(line, i); continue; }
                if (c == '\'') { i = SkipChar(line, i); continue; }

                if (c == '{') net++;
                else if (c == '}') net--;
            }
            return net;
        }

        // Returns the index of the closing quote of a "..." literal starting at openIndex (handles \" escapes
        // and "" verbatim/interpolated escapes), or the last index if unterminated on this line.
        private static int SkipString(string line, int openIndex)
        {
            for (int i = openIndex + 1; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\') { i++; continue; }
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { i++; continue; } // "" escape
                    return i;
                }
            }
            return line.Length - 1;
        }

        private static int SkipChar(string line, int openIndex)
        {
            for (int i = openIndex + 1; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\') { i++; continue; }
                if (c == '\'') return i;
            }
            return line.Length - 1;
        }

        // Merges overlapping/adjacent ranges, orders them by source position, joins with a blank line between
        // non-contiguous blocks, and strips the common leading indentation so the snippet sits at column 0.
        private static string Emit(string[] lines, List<(int start, int end)> ranges)
        {
            ranges.Sort((a, b) => a.start.CompareTo(b.start));

            // Merge only ranges that actually OVERLAP (not merely adjacent), so each member stays its own block
            // and gets a blank line between it and the next for readability.
            var merged = new List<(int start, int end)>();
            foreach (var r in ranges)
            {
                if (merged.Count > 0 && r.start <= merged[merged.Count - 1].end)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.start, Math.Max(last.end, r.end));
                }
                else merged.Add(r);
            }

            // Common indentation across all emitted non-blank lines.
            int minIndent = int.MaxValue;
            foreach (var (start, end) in merged)
                for (int i = start; i <= end; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    int indent = lines[i].Length - lines[i].TrimStart().Length;
                    if (indent < minIndent) minIndent = indent;
                }
            if (minIndent == int.MaxValue) minIndent = 0;

            var sb = new StringBuilder();
            for (int b = 0; b < merged.Count; b++)
            {
                if (b > 0) sb.Append("\n\n"); // blank line between separate blocks
                var (start, end) = merged[b];
                for (int i = start; i <= end; i++)
                {
                    string line = lines[i];
                    line = line.Length >= minIndent ? line.Substring(minIndent) : line.TrimStart();
                    sb.Append(line.TrimEnd());
                    if (i < end) sb.Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
