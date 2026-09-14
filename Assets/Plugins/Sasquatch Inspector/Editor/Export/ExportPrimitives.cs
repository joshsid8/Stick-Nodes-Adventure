using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SasquatchInspector.Editor.Export
{
    /// <summary>
    /// A tiny indentation-aware source builder used by the standalone-editor exporter. Not a general code model —
    /// just enough to emit readable, correctly-indented C# without hand-managing whitespace at every call site.
    /// </summary>
    internal sealed class CodeWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private int _indent;

        public CodeWriter(int baseIndent = 0) { _indent = baseIndent; }

        /// <summary>Writes one line at the current indent (blank line when <paramref name="text"/> is null/empty).</summary>
        public void Line(string text = "")
        {
            if (!string.IsNullOrEmpty(text))
            {
                for (int i = 0; i < _indent; i++) _sb.Append("    ");
                _sb.Append(text);
            }
            _sb.Append('\n');
        }

        /// <summary>Writes a <c>//</c> comment line at the current indent.</summary>
        public void Comment(string text) => Line("// " + text);

        /// <summary>Opens a brace block: writes <c>{</c> and increases indent.</summary>
        public void OpenBrace()
        {
            Line("{");
            _indent++;
        }

        /// <summary>Closes a brace block: decreases indent and writes <c>}</c> (plus an optional suffix such as <c>;</c>).</summary>
        public void CloseBrace(string suffix = "")
        {
            _indent--;
            Line("}" + suffix);
        }

        public void Push() => _indent++;
        public void Pop() => _indent--;

        /// <summary>
        /// Appends a pre-rendered multi-line source block, re-indenting every line by the CURRENT indent so a block
        /// generated at indent 0 nests correctly wherever it's inserted (its own relative indentation is preserved).
        /// </summary>
        public void AppendBlock(string block)
        {
            if (string.IsNullOrEmpty(block)) return;
            string[] lines = block.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                // Split on a trailing '\n' yields a final empty element — don't emit a spurious blank line for it.
                if (i == lines.Length - 1 && lines[i].Length == 0) break;
                Line(lines[i]);
            }
        }

        public override string ToString() => _sb.ToString();
    }

    /// <summary>
    /// Hands out unique, valid C# identifiers for the generated file. A single allocator is shared across an
    /// export for BOTH helper-method names and local variable names, so a name is never reused anywhere — cheap
    /// insurance against a collision between two same-named fields in different generated methods.
    /// </summary>
    internal sealed class NameAllocator
    {
        private readonly HashSet<string> _used = new HashSet<string>();

        /// <summary>Reserves a unique identifier derived from <paramref name="desired"/> (sanitized), appending a numeric suffix on collision.</summary>
        public string Reserve(string desired, string fallback = "el")
        {
            string root = Sanitize(desired, fallback);
            string candidate = root;
            int n = 1;
            while (_used.Contains(candidate))
            {
                candidate = root + n;
                n++;
            }
            _used.Add(candidate);
            return candidate;
        }

        private static string Sanitize(string s, string fallback)
        {
            if (string.IsNullOrEmpty(s)) s = fallback;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-') sb.Append('_');
            }
            if (sb.Length == 0) sb.Append(fallback);
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');

            // A section title is user text, and most NewVar call sites append a literal suffix ("Field",
            // "Slider", "Content"…) that makes a keyword collision impossible. The container root does not —
            // it declares `var {name} = new VisualElement();` from the title alone. A section titled `event`
            // or `class` therefore emitted `var event = …` (CS1041) and broke the whole generated file.
            // `so` is reserved for the same reason: it is the generated build method's own parameter, so a
            // section titled "so" shadowed it (CS0136).
            string name = sb.ToString();
            if (name == "so" || ReservedWords.Contains(name)) name = "_" + name;
            return name;
        }

        // C# reserved keywords, plus `so`. Contextual keywords (var, value, nameof…) are legal identifiers and
        // are deliberately absent — over-escaping would churn variable names in every existing export diff.
        private static readonly HashSet<string> ReservedWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
            "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
            "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
            "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
            "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
            "ushort", "using", "virtual", "void", "volatile", "while",
        };
    }

    /// <summary>Formats C# literals (strings, floats, colors) for the generated source, always in the invariant culture.</summary>
    internal static class Lit
    {
        /// <summary>A C# string literal for <paramref name="s"/> (or the token <c>null</c> when it is null).</summary>
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>A C# <c>float</c> literal (invariant, with an <c>f</c> suffix); non-finite values collapse to <c>0f</c>.</summary>
        public static string F(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "0f";
            return v.ToString("0.######", CultureInfo.InvariantCulture) + "f";
        }

        /// <summary>A C# expression constructing <paramref name="c"/> via <c>new Color(r, g, b, a)</c>.</summary>
        public static string Col(Color c) => $"new Color({F(c.r)}, {F(c.g)}, {F(c.b)}, {F(c.a)})";

        /// <summary>A C# <c>double</c> literal (invariant); non-finite values collapse to <c>1</c>.</summary>
        public static string D(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "1";
            return v.ToString("0.############", CultureInfo.InvariantCulture);
        }
    }
}
