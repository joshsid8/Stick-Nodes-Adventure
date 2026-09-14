using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Lightweight VS-dark-style C# syntax highlighter that wraps tokens in UI Toolkit rich-text
    /// <c>&lt;color=#hex&gt;</c> spans for display in a <c>Label</c> (comments green, keywords blue, types teal,
    /// members yellow, strings/numbers tinted). Per-line so line comments are trivial; a single tokenizing
    /// pass handles the rest without nested colour tags. Used by both the Showcase window's code pane and the
    /// Spring Sandbox snippet panel.
    /// </summary>
    internal static class CodeHighlighter
    {
        // VS-dark palette.
        private const string ColComment = "#6A9955";
        private const string ColString = "#CE9178";
        private const string ColNumber = "#B5CEA8";
        private const string ColType = "#4EC9B0";
        private const string ColKeyword = "#569CD6";
        private const string ColMember = "#DCDCAA";
        private const string ColIdent = "#9CDCFE";

        // Temporary stand-in for a source '<' while we highlight (U+0001 never appears in real source). Restored
        // at the end wrapped in a <noparse> tag so the angle bracket displays literally instead of being parsed.
        private const string AngleSentinel = "";
        private const string LiteralOpenAngle = "<noparse><</noparse>";

        // Matches a (single-line) string literal, a number, or an identifier word — in that priority order so a
        // quoted region is consumed whole and its inner words aren't re-coloured.
        private static readonly Regex s_token =
            new Regex("\"[^\"]*\"|\\b\\d+(?:\\.\\d+)?[fFdDmMlLuU]*\\b|\\b[A-Za-z_]\\w*\\b", RegexOptions.Compiled);

        private static readonly HashSet<string> s_csharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "get", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
            "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
            "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "set", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "value", "var", "virtual", "void",
            "volatile", "while", "yield"
        };

        // Cached per domain — CreateDynamicFontFromOSFont allocates a native Font, so minting one per call
        // would leak.
        private static UnityEngine.Font s_mono;
        private static bool s_monoUnavailable;

        /// <summary>The editor-side monospace font for code display (Consolas/Menlo/Courier fallback chain), or null.</summary>
        internal static UnityEngine.Font MonospaceFont()
        {
            // CreateDynamicFontFromOSFont mints an UNSAVED native Font, and Unity destroys those on scene
            // load / play-mode transitions. A plain "resolved once per domain" latch therefore kept handing
            // out a DESTROYED font for the rest of the session — and a Label whose font is destroyed measures
            // 0x0 and draws NOTHING (an empty padded box, not merely proportional text), which is how a code
            // peek turned into a tiny empty rectangle. So: keep it alive with DontSave, and re-mint if it
            // dies anyway. The "unavailable" latch is separate, so a machine that has none of these fonts
            // pays the OS lookup once instead of on every call.
            if (s_mono != null) return s_mono;
            if (s_monoUnavailable) return null;
            try { s_mono = UnityEngine.Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New", "monospace" }, 12); }
            catch { s_mono = null; }
            if (s_mono == null) { s_monoUnavailable = true; return null; }
            s_mono.hideFlags = UnityEngine.HideFlags.DontSave;
            return s_mono;
        }

        /// <summary>
        /// Fonts <paramref name="label"/> for code display: monospace when the OS has one, else the editor's
        /// own font. The fallback is not cosmetic — a fontless Label resolves to no glyphs AND no measured
        /// size, and a code pane can be parented outside a window's rootVisualElement (a panel's bare
        /// visualTree inherits no font at all), where that renders as an empty box.
        /// </summary>
        internal static void ApplyCodeFont(UnityEngine.UIElements.TextElement label)
        {
            if (label == null) return;
            var font = MonospaceFont();
            if (font == null)
            {
                // EditorStyles is lazily built from the skin; outside a GUI context it can still be cold.
                try { font = UnityEditor.EditorStyles.standardFont; }
                catch { font = null; }
            }
            if (font == null) return;
            label.style.unityFontDefinition = UnityEngine.UIElements.FontDefinition.FromFont(font);
            label.style.unityFont = font;
        }

        /// <summary>Highlights <paramref name="code"/> as C# for a rich-text Label.</summary>
        public static string Highlight(string code) => Highlight(code, null, null, true);

        /// <summary>
        /// Highlights <paramref name="code"/>, optionally folding in caller-specific <paramref name="extraTypes"/>
        /// (coloured as types) and <paramref name="extraKeywords"/> (coloured as keywords). When
        /// <paramref name="capitalizedAsType"/> is true, any otherwise-unclassified Capitalized identifier is
        /// treated as a type (good for arbitrary source); pass false to keep terse hand-written snippets neutral.
        /// </summary>
        public static string Highlight(string code, HashSet<string> extraTypes, HashSet<string> extraKeywords,
            bool capitalizedAsType = true)
        {
            if (string.IsNullOrEmpty(code)) return code ?? string.Empty;

            // Neutralize any '<' already in the source (rich-text tags inside a string literal, e.g.
            // "<color=#FF6600><b>...", or generics like List<int>) so the display Label's rich-text engine renders
            // them LITERALLY instead of interpreting them — while the colour spans we add below stay real. The
            // copy button copies the un-highlighted plain text, so it is unaffected.
            code = code.Replace("<", AngleSentinel);

            var lines = code.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                // Whole-line comments (incl. /// XML docs) are coloured wholesale; trailing comments are left to
                // the tokenizer (a deliberate, minor limitation matching the original Spring highlighter).
                if (line.TrimStart().StartsWith("//"))
                {
                    lines[i] = ColorSpan(line, ColComment);
                    continue;
                }

                string local = line; // capture for the closure's member-lookback
                lines[i] = s_token.Replace(line, m =>
                {
                    string t = m.Value;
                    if (t[0] == '"') return ColorSpan(t, ColString);
                    if (char.IsDigit(t[0])) return ColorSpan(t, ColNumber);
                    if (s_csharpKeywords.Contains(t) || (extraKeywords != null && extraKeywords.Contains(t)))
                        return ColorSpan(t, ColKeyword);
                    if ((extraTypes != null && extraTypes.Contains(t)) ||
                        (capitalizedAsType && t[0] >= 'A' && t[0] <= 'Z'))
                        return ColorSpan(t, ColType);
                    bool member = m.Index > 0 && local[m.Index - 1] == '.';
                    return ColorSpan(t, member ? ColMember : ColIdent);
                });
            }

            // Restore the source '<' so it displays literally (the colour spans we added above stay real tags).
            return string.Join("\n", lines).Replace(AngleSentinel, LiteralOpenAngle);
        }

        private static string ColorSpan(string t, string hex) => $"<color={hex}>{t}</color>";
    }
}
