using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental; // Pointer*LinkTagEvent (link-tag pointer events live here)
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Converts the lightweight markdown a designer can type into a HelpBox message into UI Toolkit content,
    /// and wires links so <c>[label](url)</c> opens in the browser (with a hand cursor on hover). Mirrors
    /// <see cref="CodeHighlighter"/>'s "emit spans, fail soft" philosophy: a malformed marker degrades to literal
    /// text rather than throwing, so authoring is forgiving.
    ///
    /// Grammar (all escapable with a leading backslash, e.g. <c>\*</c>):
    ///   **bold**  *italics*  `inline code`  {Color|text}  [label](https://url)
    ///   ```                       a fenced code block — rendered as a real monospace, syntax-highlighted
    ///   multi-line code           dark panel (like the Showcase "C# Source" pane), NOT a rich-text span.
    ///   ```
    /// plus raw Unity rich-text tags — <c>&lt;b&gt;</c>, <c>&lt;i&gt;</c>, <c>&lt;color=…&gt;</c>,
    /// <c>&lt;size=…&gt;</c>, <c>&lt;sprite=…&gt;</c>, … — which pass straight through to the renderer.
    ///
    /// <para>Inline runs go through <see cref="ToRichText"/> (a single rich-text string). A message that contains
    /// a fenced block can't be one string (a code panel needs its own element + monospace font), so
    /// <see cref="BuildMarkdownElement"/> splits it into stacked text-Labels and code panels.</para>
    ///
    /// <para><b>Injection defense:</b> in inline text, every literal run is wrapped in
    /// <c>&lt;noparse&gt;…&lt;/noparse&gt;</c> while our own tags are emitted OUTSIDE — <see cref="EmitLiteral"/>
    /// is the single chokepoint for that. The one deliberate opening: a KNOWN rich-text tag (see
    /// <see cref="s_richTextTag"/>) is emitted outside the wrap so authors can mix raw tags with markdown.
    /// Anything else angle-bracketed (unknown or malformed tags) still displays literally, and <c>\&lt;</c>
    /// escapes even a known tag (e.g. <c>\&lt;b&gt;</c> shows "&lt;b&gt;").</para>
    /// </summary>
    internal static class SasquatchMarkdown
    {
        // Only the link color is theme-dependent now; code uses the dark VS-style palette from CodeHighlighter.
        private struct Theme { public string LinkHex; }

        private static Theme ResolveTheme() =>
            new Theme { LinkHex = EditorGUIUtility.isProSkin ? "#5B9DEF" : "#1A5FB4" };

        // Chip behind inline `code`. GOTCHA: TextCore draws <mark> ON TOP of the glyphs (a TMP-style highlight
        // overlay), so the chip MUST stay translucent or it erases the very code it highlights — the original
        // near-opaque #1E1E22F0 rendered as a dark blob with ~6% text ghosting. Neutral gray at ~24% alpha reads
        // as a chip on the dark help-box bg AND the accent-soft help cards while barely muting the syntax tint.
        private const string InlineCodeBgHex = "#8080803C";

        // The fenced code panel — matches the Showcase "C# Source" pane.
        private static readonly Color CodePanelBg = new Color(0.12f, 0.12f, 0.13f);
        private static readonly Color CodePanelFg = new Color(0.83f, 0.83f, 0.83f);

        // Cursor:link class toggled on the host Label only while the pointer is over a link span.
        private const string LinkHoverClass = "sasq-md-link-hover";

        // Breaks an author-typed literal "</noparse>" so it can't close our wrapper early (see EmitLiteral).
        private static readonly Regex s_noparseCloser =
            new Regex("</noparse>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A zero-width space wedged after the '<' defangs an author-typed "</noparse>" (renders identically).
        private const string NoparseBreak = "<​/noparse>";

        // The rich-text tags allowed to pass through the noparse injection defense verbatim (open or close,
        // optional =value or space-separated attributes). Only a KNOWN name gets through — "<foo>" or an
        // unclosed "<b" stays literal text. "noparse" is deliberately absent: an author-typed noparse could
        // close/open our own literal wrapping mid-run. Longest-first so alternation can't stop at a prefix.
        // Anchored (\G) so it must match exactly at the scan position ParseInline hands it.
        private static readonly Regex s_richTextTag = new Regex(
            @"\G</?(?:strikethrough|line-indent|line-height|font-weight|smallcaps|lowercase|uppercase|gradient" +
            @"|allcaps|voffset|sprite|cspace|mspace|indent|margin|rotate|color|space|style|width|align|alpha" +
            @"|size|mark|font|link|nobr|pos|sub|sup|br|b|i|u|s)(?:[ =][^<>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ---- Public surface ----

        /// <summary>True when <paramref name="markdown"/> has a fenced ``` code block (and so needs the
        /// element-based <see cref="BuildMarkdownElement"/> rather than a single rich-text Label).</summary>
        internal static bool HasFencedCode(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return false;
            foreach (var line in markdown.Replace("\r", "").Split('\n'))
                if (line.TrimStart().StartsWith("```")) return true;
            return false;
        }

        /// <summary>Converts <paramref name="markdown"/>'s INLINE syntax to UI Toolkit rich-text. Pure and
        /// panel-free (unit-testable). Does not handle fenced blocks — see <see cref="BuildMarkdownElement"/>.</summary>
        internal static string ToRichText(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return markdown ?? string.Empty;
            return ParseInline(markdown, 0, markdown.Length, ResolveTheme());
        }

        /// <summary>Sets a Label's text to rendered inline markdown, enables rich text, and wires link clicks +
        /// hover cursor. Idempotent (static handlers, url in the link id), so safe to re-apply on the same label.</summary>
        internal static void ApplyMarkdownToLabel(Label label, string markdown)
        {
            if (label == null) return;
            label.enableRichText = true;
            label.pickingMode = PickingMode.Position; // link-tag pointer events need a hit-testable element
            label.text = ToRichText(markdown);

            WireLinkEvents(label);
        }

        // The ONLY link-tag wiring in the package, isolated so the dependency on UnityEngine.UIElements.Experimental
        // (the Pointer*LinkTagEvent types — link-tag pointer events have no non-experimental equivalent) lives in one
        // spot. Idempotent (unregister-then-register, static handlers). DEGRADATION: if Unity ever removes these
        // events, only this method and its three OnLink* handlers break; the markdown text (including the rendered
        // <link> spans) still displays — links simply become inert (no click/hover), which is an acceptable fallback.
        private static void WireLinkEvents(Label label)
        {
            label.UnregisterCallback<PointerUpLinkTagEvent>(OnLinkUp);
            label.RegisterCallback<PointerUpLinkTagEvent>(OnLinkUp);
            label.UnregisterCallback<PointerOverLinkTagEvent>(OnLinkOver);
            label.RegisterCallback<PointerOverLinkTagEvent>(OnLinkOver);
            label.UnregisterCallback<PointerOutLinkTagEvent>(OnLinkOut);
            label.RegisterCallback<PointerOutLinkTagEvent>(OnLinkOut);
        }

        /// <summary>Builds a message that may contain fenced code blocks into a stacked column: text paragraphs
        /// as rich-text Labels (inline markdown + links), fenced ``` blocks as monospace, syntax-highlighted dark
        /// code panels (like the Showcase code pane).</summary>
        internal static VisualElement BuildMarkdownElement(string markdown, float? fontSize = null)
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            if (string.IsNullOrEmpty(markdown)) return root;

            var lines = markdown.Replace("\r", "").Split('\n');
            var text = new StringBuilder();
            var code = new StringBuilder();
            bool inCode = false;

            void FlushText()
            {
                string t = text.ToString().Trim('\n');
                text.Clear();
                if (t.Length > 0) root.Add(MakeTextBlock(t, fontSize));
            }
            void FlushCode()
            {
                root.Add(MakeCodeBlock(code.ToString().Trim('\n')));
                code.Clear();
            }

            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCode) { FlushCode(); inCode = false; }
                    else { FlushText(); inCode = true; }
                    continue;
                }
                var buf = inCode ? code : text;
                if (buf.Length > 0) buf.Append('\n');
                buf.Append(line);
            }
            if (inCode && code.Length > 0) FlushCode(); // unterminated fence → render what we have
            FlushText();
            return root;
        }

        private static Label MakeTextBlock(string markdown, float? fontSize = null)
        {
            var label = new Label();
            label.AddToClassList("sasq-helpbox-message"); // so ApplyNodeTextStyles' MessageTextStyle still applies
            label.style.whiteSpace = WhiteSpace.Normal;
            // Default matches the native help-box label / .sasq-help-expand-details; callers (e.g. the Tutorial)
            // may pass a larger size for more readable body copy.
            label.style.fontSize = fontSize ?? 11;
            ApplyMarkdownToLabel(label, markdown);
            return label;
        }

        private static Label MakeCodeBlock(string code)
        {
            var label = new Label { enableRichText = true };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.backgroundColor = CodePanelBg;
            label.style.color = CodePanelFg;
            label.style.paddingLeft = 8; label.style.paddingRight = 8;
            label.style.paddingTop = 6; label.style.paddingBottom = 6;
            label.style.borderTopLeftRadius = 4; label.style.borderTopRightRadius = 4;
            label.style.borderBottomLeftRadius = 4; label.style.borderBottomRightRadius = 4;
            label.style.marginTop = 4; label.style.marginBottom = 2;
            CodeHighlighter.ApplyCodeFont(label);
            label.style.fontSize = 11;
            label.text = CodeHighlighter.Highlight(code);
            return label;
        }

        // ---- Link interaction (static handlers → idempotent registration) ----

        private static void OnLinkUp(PointerUpLinkTagEvent evt)
        {
            string url = evt.linkID;
            if (string.IsNullOrEmpty(url)) return;
            Application.OpenURL(url);     // precedent: SasquatchWelcomeWindow's external-link buttons
            evt.StopPropagation();        // don't also toggle an expandable header on the same gesture
        }

        private static void OnLinkOver(PointerOverLinkTagEvent evt) =>
            (evt.target as VisualElement)?.AddToClassList(LinkHoverClass);     // cursor:link while over a link

        private static void OnLinkOut(PointerOutLinkTagEvent evt) =>
            (evt.target as VisualElement)?.RemoveFromClassList(LinkHoverClass);

        // ---- Inline parser (recursive descent; regex can't cleanly skip markers inside code spans) ----

        private static string ParseInline(string s, int start, int end, Theme t)
        {
            var sb = new StringBuilder(end - start + 16);
            var lit = new StringBuilder(); // literal run pending a noparse wrap

            int i = start;
            while (i < end)
            {
                char c = s[i];

                if (c == '\\' && i + 1 < end && IsEscapable(s[i + 1]))
                {
                    lit.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '`') // inline code: `code` — contents are NOT re-parsed
                {
                    int close = IndexOfUnescaped(s, '`', i + 1, end);
                    if (close > i)
                    {
                        Flush(sb, lit);
                        sb.Append(EmitInlineCode(s.Substring(i + 1, close - (i + 1))));
                        i = close + 1;
                        continue;
                    }
                    lit.Append(c); i++; continue;
                }

                if (c == '*' && i + 1 < end && s[i + 1] == '*') // bold: **text**
                {
                    int close = IndexOfMarker(s, "**", i + 2, end);
                    if (close > i + 1)
                    {
                        Flush(sb, lit);
                        sb.Append("<b>").Append(ParseInline(s, i + 2, close, t)).Append("</b>");
                        i = close + 2;
                        continue;
                    }
                    lit.Append(c); i++; continue;
                }

                if (c == '*') // italics: *text*
                {
                    int close = IndexOfUnescaped(s, '*', i + 1, end);
                    if (close > i)
                    {
                        Flush(sb, lit);
                        sb.Append("<i>").Append(ParseInline(s, i + 1, close, t)).Append("</i>");
                        i = close + 1;
                        continue;
                    }
                    lit.Append(c); i++; continue;
                }

                if (c == '{') // color: {Color|text}
                {
                    int close = FindBalancedBrace(s, i + 1, end);
                    if (close > i)
                    {
                        int bar = IndexOfUnescaped(s, '|', i + 1, close);
                        if (bar > i)
                        {
                            Flush(sb, lit);
                            string token = s.Substring(i + 1, bar - (i + 1)).Trim();
                            string inner = ParseInline(s, bar + 1, close, t);
                            if (TryResolveColor(token, out var col))
                                sb.Append("<color=").Append(ColorHexUtility.ToHex(col)).Append('>')
                                  .Append(inner).Append("</color>");
                            else
                                sb.Append(inner);
                            i = close + 1;
                            continue;
                        }
                    }
                    lit.Append(c); i++; continue;
                }

                if (c == '<') // raw rich text: a recognized Unity tag passes through verbatim (outside noparse)
                {
                    var tag = s_richTextTag.Match(s, i, end - i);
                    if (tag.Success)
                    {
                        Flush(sb, lit);
                        sb.Append(tag.Value);
                        i += tag.Length;
                        continue;
                    }
                    lit.Append(c); i++; continue;
                }

                if (c == '[') // hyperlink: [label](url)
                {
                    int textClose = IndexOfUnescaped(s, ']', i + 1, end);
                    if (textClose > i && textClose + 1 < end && s[textClose + 1] == '(')
                    {
                        int urlClose = IndexOfUnescaped(s, ')', textClose + 2, end);
                        if (urlClose > textClose + 1)
                        {
                            Flush(sb, lit);
                            string url = SanitizeUrl(s.Substring(textClose + 2, urlClose - (textClose + 2)));
                            string inner = ParseInline(s, i + 1, textClose, t);
                            sb.Append("<link=\"").Append(url).Append("\"><color=").Append(t.LinkHex).Append("><u>")
                              .Append(inner).Append("</u></color></link>");
                            i = urlClose + 1;
                            continue;
                        }
                    }
                    lit.Append(c); i++; continue;
                }

                lit.Append(c);
                i++;
            }

            Flush(sb, lit);
            return sb.ToString();
        }

        private static void Flush(StringBuilder sb, StringBuilder lit)
        {
            if (lit.Length == 0) return;
            sb.Append(EmitLiteral(lit.ToString()));
            lit.Length = 0;
        }

        /// <summary>Wraps an author literal so its content is never parsed as rich text (injection defense).</summary>
        private static string EmitLiteral(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            if (raw.IndexOf("</noparse>", StringComparison.OrdinalIgnoreCase) >= 0)
                raw = s_noparseCloser.Replace(raw, NoparseBreak);
            return "<noparse>" + raw + "</noparse>";
        }

        // Inline `code`: a translucent chip (see InlineCodeBgHex — mark overlays the glyphs) with
        // CodeHighlighter's syntax tint (so it reads as code, never flat red).
        // No padding spaces — they would push a leading token out of alignment in a multi-token snippet.
        private static string EmitInlineCode(string code) =>
            "<mark=" + InlineCodeBgHex + ">" + CodeHighlighter.Highlight(code) + "</mark>";

        // ---- Color resolution: hex → palette swatch → CSS/Unity name ----

        /// <summary>Resolves a color token (hex, a palette swatch name, or a CSS/Unity name). Hex beats a
        /// same-spelled swatch; a swatch beats a same-spelled CSS name.</summary>
        internal static bool TryResolveColor(string token, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(token)) return false;
            token = token.Trim();

            if (LooksLikeHex(token) && ColorHexUtility.TryParseHex(token, out color))
                return true;

            var lib = SasquatchPaletteLibraryManager.Current;
            if (lib != null)
            {
                var def = lib.DefaultPalette;
                if (TryFindSwatch(def, token, out color)) return true;
                if (lib.Palettes != null)
                {
                    foreach (var p in lib.Palettes)
                        if (!ReferenceEquals(p, def) && TryFindSwatch(p, token, out color)) return true;
                }
            }

            if (ColorUtility.TryParseHtmlString(token.ToLowerInvariant(), out color)) return true;

            color = Color.white;
            return false;
        }

        private static bool TryFindSwatch(SasquatchColorPaletteLibrary.Palette palette, string name, out Color color)
        {
            color = Color.white;
            if (palette == null || palette.Swatches == null) return false;
            foreach (var sw in palette.Swatches)
            {
                if (string.Equals(sw.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    color = sw.color;
                    return true;
                }
            }
            return false;
        }

        // Hash-prefixed is always hex; a bare token must be exactly 6 or 8 hex digits (so "#abc" stays explicit
        // but bare words like "beef"/"decade" don't get silently parsed as colors).
        private static bool LooksLikeHex(string token)
        {
            if (token.StartsWith("#")) return true;
            if (token.Length != 6 && token.Length != 8) return false;
            foreach (char ch in token)
                if (!Uri.IsHexDigit(ch)) return false;
            return true;
        }

        // ---- Small scanning helpers (all backslash-escape aware) ----

        private static bool IsEscapable(char c) =>
            c == '*' || c == '_' || c == '`' || c == '{' || c == '}' ||
            c == '[' || c == ']' || c == '|' || c == '\\' || c == '<';

        private static int IndexOfUnescaped(string s, char target, int from, int end)
        {
            for (int i = from; i < end; i++)
            {
                if (s[i] == '\\' && i + 1 < end) { i++; continue; }
                if (s[i] == target) return i;
            }
            return -1;
        }

        private static int IndexOfMarker(string s, string marker, int from, int end)
        {
            int len = marker.Length;
            for (int i = from; i + len <= end; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                bool match = true;
                for (int k = 0; k < len; k++)
                    if (s[i + k] != marker[k]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        // Index of the '}' that closes the '{' opened just before 'from', honoring nesting.
        private static int FindBalancedBrace(string s, int from, int end)
        {
            int depth = 0;
            for (int i = from; i < end; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < end) { i++; continue; }
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return -1;
        }

        // Strip characters that would break the <link="…"> attribute (the url is an attribute value, not noparse).
        private static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            var sb = new StringBuilder(url.Length);
            foreach (char ch in url.Trim())
            {
                if (ch == '"' || ch == '<' || ch == '>' || char.IsControl(ch)) continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
