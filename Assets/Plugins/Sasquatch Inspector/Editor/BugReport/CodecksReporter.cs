using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Sends a user-submitted bug report to my issue tracker (Codecks "User Reports"), where each report becomes
    /// a card in one deck. The whole thing is EDITOR-ONLY (this assembly never compiles into a player build) and
    /// only ever runs when a user fills in the <see cref="SasquatchBugReportWindow"/> form and clicks Send —
    /// nothing is collected or transmitted silently.
    ///
    /// Flow (see https://manual.codecks.io/user-reports/): one JSON POST creates the card and, when the user
    /// attached a file, returns a signed S3 upload target; a second best-effort multipart POST uploads the file.
    /// A failed upload never blocks the report — the card is already created by that point.
    ///
    /// No third-party JSON library is used (nothing extra to bundle): the request body is built by
    /// hand and the small, fixed response shape is read by the focused helpers at the bottom of this file.
    /// </summary>
    internal static class CodecksReporter
    {
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Configuration
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The Codecks <b>Report Token</b>. Get it from Codecks ▸ Organization Settings ▸ User Reports ▸ add an
        /// application (name it "Sasquatch Inspector", pick the deck reports should land in). Codecks designs this
        /// token to be embedded in a shipping app: it can ONLY create report cards in that one deck, is
        /// rate-limited, and can be revoked/rotated any time from Codecks. It is editor-only here, so it never
        /// reaches a player build. Do NOT paste your <i>Access Key</i> — that one is sensitive and must never ship.
        /// When this is left empty, the Report a Bug window quietly falls back to a plain email instead.
        /// </summary>
        public const string ReportToken = "rt_kvQvpx7O17sXis0FtbS27bLS";

        /// <summary>Whether a Report Token has been configured (otherwise the window uses the email fallback).</summary>
        public static bool IsConfigured => !string.IsNullOrEmpty(ReportToken);

        private const string CreateReportUrl = "https://api.codecks.io/user-report/v1/create-report?token=";

        /// <summary>Largest attachment we'll upload. Bigger media should go through the "video link" field
        /// instead (paste a link — no file-size ceiling), so the report stays fast and within Codecks' quota.</summary>
        public const long AttachmentMaxBytes = 10L * 1024 * 1024;

        // ── Input limits. The window enforces these with friendly errors before sending; ComposeContent and
        //    Send re-apply them as a backstop so every path (including the email fallback) gets the same cleanup.
        /// <summary>Longest description one report will carry — a full stack trace plus repro steps fits with
        /// room to spare; anything bigger belongs in a .txt/.log attachment.</summary>
        public const int MessageMaxChars = 10000;
        /// <summary>Cap for the free-typed video-link field.</summary>
        public const int LinkFieldMaxChars = 2000;
        /// <summary>Cap for the email field (the RFC ceiling for a whole address).</summary>
        public const int EmailMaxChars = 254;

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>How urgent the reporter feels the bug is. Maps to Codecks' severity values; <see cref="Normal"/>
        /// sends none (Codecks' default).</summary>
        public enum Severity { Normal, Minor, Major, Critical }

        /// <summary>Outcome handed back to the window on the main thread when the send finishes.</summary>
        public struct Result
        {
            /// <summary>The card was created.</summary>
            public bool ok;
            /// <summary>Human-readable reason when <see cref="ok"/> is false.</summary>
            public string error;
            /// <summary>The card was created but the attached file didn't upload (best-effort — not a hard failure).</summary>
            public bool attachmentFailed;
        }

        /// <summary>
        /// Composes the exact text that will be sent as the card body: the user's message (its first line becomes
        /// the card title), an optional video link, then the auto-collected diagnostics. Exposed so the window can
        /// show the user precisely what leaves their machine before they hit Send.
        /// </summary>
        public static string ComposeContent(string message, string videoLink)
        {
            message = CleanText(message, MessageMaxChars);
            videoLink = CleanText(videoLink, LinkFieldMaxChars, singleLine: true);

            var sb = new StringBuilder();
            sb.Append(message.Length == 0 ? "(no description)" : message);
            if (videoLink.Length > 0)
                sb.Append("\n\nVideo: ").Append(videoLink);
            sb.Append("\n\n————————————————\n").Append(BuildDiagnostics());
            return sb.ToString();
        }

        /// <summary>Editor/project info that helps me reproduce the bug, gathered locally at send time.</summary>
        public static string BuildDiagnostics()
        {
            string pipeline = "Built-in Render Pipeline";
            var srp = GraphicsSettings.currentRenderPipeline;
            if (srp != null) pipeline = srp.GetType().Name;

            return
                $"Sasquatch Inspector: v{SasquatchVersion.Current}\n" +
                $"Unity: {Application.unityVersion}\n" +
                $"OS: {SystemInfo.operatingSystem}\n" +
                $"Render Pipeline: {pipeline}\n" +
                $"Graphics API: {SystemInfo.graphicsDeviceType}";
        }

        /// <summary>Total create-report attempts. Transient gateway blips (e.g. a 502 Bad Gateway) and dropped
        /// connections are common with serverless backends and clear on a retry, so we quietly re-try a couple
        /// times before ever showing the user an error.</summary>
        private const int MaxCreateAttempts = 3;

        /// <summary>
        /// Sends the report asynchronously and invokes <paramref name="onDone"/> on the main thread when finished.
        /// <paramref name="userEmail"/>, <paramref name="videoLink"/> and <paramref name="attachmentPath"/> are all
        /// optional. Safe to call only when <see cref="IsConfigured"/> is true.
        /// </summary>
        public static void Send(string message, Severity severity, string userEmail, string videoLink,
            string attachmentPath, Action<Result> onDone)
        {
            if (!IsConfigured)
            {
                onDone?.Invoke(new Result { ok = false, error = "Bug reporting isn't configured in this copy." });
                return;
            }

            string content = ComposeContent(message, videoLink);
            userEmail = CleanText(userEmail, EmailMaxChars, singleLine: true);

            bool hasAttachment = !string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath);
            string attachmentName = hasAttachment ? Path.GetFileName(attachmentPath) : null;
            if (hasAttachment && (new FileInfo(attachmentPath).Length > AttachmentMaxBytes
                                  || !IsAllowedAttachment(attachmentPath)))
            {
                // Too big (or a type we don't take) — send the report without it rather than failing outright.
                // The window filters both up front; this is the backstop.
                hasAttachment = false;
                attachmentName = null;
            }

            string body = BuildRequestBody(content, severity, userEmail, attachmentName);
            PostCreateReport(body, MaxCreateAttempts, hasAttachment, attachmentPath, onDone);
        }

        // Creates the card, re-trying transient failures (a dropped connection or an HTTP 5xx gateway error such
        // as a 502) up to MaxCreateAttempts times before surfacing the error. On success, kicks off the optional
        // best-effort attachment upload. A 4xx (e.g. 401 bad token) is NOT retried — retrying won't help.
        private static void PostCreateReport(string body, int attemptsLeft, bool hasAttachment,
            string attachmentPath, Action<Result> onDone)
        {
            var req = new UnityWebRequest(CreateReportUrl + UnityWebRequest.EscapeURL(ReportToken), "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");

            req.SendWebRequest().completed += _ =>
            {
                bool netOk = req.result == UnityWebRequest.Result.Success;
                long code = req.responseCode;
                string text = req.downloadHandler != null ? req.downloadHandler.text : null;
                req.Dispose();

                if (netOk && ResponseSaysOk(text))
                {
                    if (!hasAttachment)
                    {
                        onDone?.Invoke(new Result { ok = true });
                        return;
                    }

                    // Card exists; the file upload is best-effort from here on.
                    UploadAttachment(text, attachmentPath, uploadOk =>
                        onDone?.Invoke(new Result { ok = true, attachmentFailed = !uploadOk }));
                    return;
                }

                // Transient = a dropped connection (code 0) or any 5xx gateway error (502/503/504…).
                bool transient = code == 0 || code >= 500;
                if (transient && attemptsLeft > 1)
                {
                    PostCreateReport(body, attemptsLeft - 1, hasAttachment, attachmentPath, onDone);
                    return;
                }

                onDone?.Invoke(new Result { ok = false, error = DescribeError(code) });
            };
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Input validation & cleanup
        //  The window calls the Try…/Looks… checks on Send to give friendly errors; CleanText also runs inside
        //  ComposeContent/Send so both the API path and the email fallback send tidy text no matter what.
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tidies free-typed text before it goes anywhere: normalizes Windows line endings, drops control
        /// characters (they mangle cards and are never typed on purpose), trims, and caps the length.
        /// <paramref name="singleLine"/> fields (the email and video-link boxes) additionally fold newlines to
        /// spaces and drop invisible formatting characters (zero-width and bidirectional marks) — those can make
        /// a pasted address read as something other than what it really is.
        /// </summary>
        public static string CleanText(string s, int maxChars, bool singleLine = false)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var sb = new StringBuilder(Math.Min(s.Length, maxChars + 16));
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\n') continue; // \r\n → \n (the \n is kept next pass)
                    c = '\n';                                           // lone \r → \n
                }
                if (c == '\n' || c == '\t') { sb.Append(singleLine ? ' ' : c); continue; }
                if (char.IsControl(c)) continue;
                if (singleLine && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                sb.Append(c);
            }

            string result = sb.ToString().Trim();
            if (result.Length > maxChars) result = result.Substring(0, maxChars).TrimEnd() + "…";
            return result;
        }

        /// <summary>
        /// Checks the optional video-link field: every whitespace-separated entry must be a normal web address —
        /// http/https, a real dotted host name, no <c>login@</c> prefix and no raw-IP host (video sites never
        /// hand out links shaped like that; they're the classic ways to make an address read as one site while
        /// pointing at another). Returns false with a user-facing reason. Empty/blank input is fine.
        /// </summary>
        public static bool TryValidateVideoLinks(string raw, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;

            foreach (string part in raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                // Links pasted without the scheme ("www.youtube.com/…") are common — judge them as https.
                string candidate = part.IndexOf("://", StringComparison.Ordinal) < 0 ? "https://" + part : part;

                Uri uri;
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    error = $"\"{Snip(part)}\" doesn't look like a web link. Paste the clip's full address " +
                            "(https://…), or clear the field — it's optional.";
                    return false;
                }

                bool dottedHost = uri.HostNameType == UriHostNameType.Dns && uri.Host.IndexOf('.') > 0;
                if (!dottedHost || !string.IsNullOrEmpty(uri.UserInfo))
                {
                    error = $"\"{Snip(part)}\" isn't a normal video address. Paste the plain https:// link " +
                            "the video site gave you.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Loose shape check for the optional email field — one @, a dotted domain, no spaces. Kept
        /// permissive on purpose; it exists to catch typos and pastes of the wrong thing, not to police RFCs.</summary>
        public static bool LooksLikeEmail(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Length > EmailMaxChars) return false;

            int at = s.IndexOf('@');
            if (at <= 0 || at != s.LastIndexOf('@')) return false;

            string domain = s.Substring(at + 1);
            if (domain.Length < 3 || domain[0] == '.' || domain[domain.Length - 1] == '.'
                || domain.IndexOf('.') < 0) return false;

            foreach (char c in s)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            return true;
        }

        /// <summary>The file types a report can carry — exactly the set <see cref="MimeFor"/> can name. Anything
        /// else is refused up front (the hint says to zip it), so a report never hands over a file type the
        /// tracker side can't safely open. Keep this, <see cref="AttachmentPanelFilters"/> and MimeFor in step.</summary>
        private static readonly string[] AllowedAttachmentExtensions =
            { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".mov", ".webm", ".txt", ".log", ".json", ".zip", ".pdf" };

        /// <summary>Filter pairs for <c>EditorUtility.OpenFilePanelWithFilters</c>, mirroring
        /// <see cref="AllowedAttachmentExtensions"/>.</summary>
        public static readonly string[] AttachmentPanelFilters =
        {
            "Image or clip", "png,jpg,jpeg,gif,webp,mp4,mov,webm",
            "Log or document", "txt,log,json,zip,pdf",
        };

        /// <summary>Whether the file is a type the report accepts (by extension, case-insensitive).</summary>
        public static bool IsAllowedAttachment(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            return ext.Length > 0 && Array.IndexOf(AllowedAttachmentExtensions, ext) >= 0;
        }

        // Keeps a rejected paste readable inside a one-line error message.
        private static string Snip(string s) => s.Length <= 40 ? s : s.Substring(0, 39) + "…";

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Request building
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private static string BuildRequestBody(string content, Severity severity, string userEmail, string fileName)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"content\":").Append(JsonString(content));

            string sev = SeverityKey(severity);
            if (sev != null) sb.Append(",\"severity\":").Append(JsonString(sev));

            if (!string.IsNullOrWhiteSpace(userEmail))
                sb.Append(",\"userEmail\":").Append(JsonString(userEmail.Trim()));

            if (!string.IsNullOrEmpty(fileName))
                sb.Append(",\"fileNames\":[").Append(JsonString(fileName)).Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        private static string SeverityKey(Severity s)
        {
            switch (s)
            {
                case Severity.Minor: return "low";
                case Severity.Major: return "high";
                case Severity.Critical: return "critical";
                default: return null; // Normal → omit (Codecks default)
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Attachment upload (S3 signed multipart POST)
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        // The create-report response carries, per attached filename, an { url, fields } target. S3 wants every
        // field replayed verbatim as multipart form-data, then Content-Type, then the file part LAST.
        private static void UploadAttachment(string createResponse, string filePath, Action<bool> onDone)
        {
            string uploadUrl;
            List<KeyValuePair<string, string>> fields;
            if (!TryReadUploadTarget(createResponse, out uploadUrl, out fields) || string.IsNullOrEmpty(uploadUrl))
            {
                onDone?.Invoke(false);
                return;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(filePath); }
            catch { onDone?.Invoke(false); return; }

            string mime = MimeFor(filePath);
            var sections = new List<IMultipartFormSection>();
            bool hasContentType = false;
            foreach (var f in fields)
            {
                sections.Add(new MultipartFormDataSection(f.Key, f.Value));
                if (f.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) hasContentType = true;
            }
            if (!hasContentType) sections.Add(new MultipartFormDataSection("Content-Type", mime));
            sections.Add(new MultipartFormFileSection("file", bytes, Path.GetFileName(filePath), mime));

            var up = UnityWebRequest.Post(uploadUrl, sections);
            up.SendWebRequest().completed += _ =>
            {
                bool ok = up.result == UnityWebRequest.Result.Success;
                up.Dispose();
                onDone?.Invoke(ok);
            };
        }

        private static string MimeFor(string path)
        {
            switch ((Path.GetExtension(path) ?? string.Empty).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".mp4": return "video/mp4";
                case ".mov": return "video/quicktime";
                case ".webm": return "video/webm";
                case ".txt":
                case ".log": return "text/plain";
                case ".json": return "application/json";
                case ".zip": return "application/zip";
                case ".pdf": return "application/pdf";
                default: return "application/octet-stream";
            }
        }

        private static string DescribeError(long responseCode)
        {
            if (responseCode == 0)
                return "Couldn't reach the server — check your internet connection.";
            if (responseCode == 401 || responseCode == 403)
                return "The bug reporter's access token was rejected.";
            if (responseCode >= 500)
                return $"The issue tracker had a temporary hiccup ({responseCode}). Please try again in a moment.";
            return $"The server returned an error ({responseCode}).";
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Minimal, focused JSON reading (only what this fixed response shape needs — not a general parser)
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        // The create-report response is { "ok": true, "cardId": "...", "uploadUrls": [...] }. We only need "ok".
        private static bool ResponseSaysOk(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            int k = json.IndexOf("\"ok\"", StringComparison.Ordinal);
            if (k < 0) return true; // no explicit flag but HTTP already succeeded — treat as ok
            int colon = json.IndexOf(':', k);
            if (colon < 0) return false;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            return i < json.Length && json[i] == 't';
        }

        // Reads url + fields from the first entry of the "uploadUrls" array (we only ever attach one file).
        private static bool TryReadUploadTarget(string json, out string url, out List<KeyValuePair<string, string>> fields)
        {
            url = null;
            fields = null;
            if (string.IsNullOrEmpty(json)) return false;
            int arr = json.IndexOf("\"uploadUrls\"", StringComparison.Ordinal);
            if (arr < 0) return false;

            url = FindString(json, "url", arr);
            if (!TryFindObject(json, "fields", arr, out int open, out int close)) return false;
            fields = ParseFlatStringObject(json, open, close);
            return true;
        }

        // Finds "key":"value" after `from` and returns the unescaped value (or null).
        private static string FindString(string s, string key, int from)
        {
            int k = s.IndexOf("\"" + key + "\"", from, StringComparison.Ordinal);
            if (k < 0) return null;
            int i = SkipToValue(s, k + key.Length + 2);
            return i >= 0 && TryReadJsonString(s, i, out string v, out _) ? v : null;
        }

        // Finds "key": { ... } after `from` and returns the span of the object braces.
        private static bool TryFindObject(string s, string key, int from, out int open, out int close)
        {
            open = close = -1;
            int k = s.IndexOf("\"" + key + "\"", from, StringComparison.Ordinal);
            if (k < 0) return false;
            int i = k + key.Length + 2;
            while (i < s.Length && s[i] != '{') { if (s[i] == '}' || s[i] == ']') return false; i++; }
            if (i >= s.Length) return false;

            open = i;
            int depth = 0;
            bool inStr = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"') inStr = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) { close = i; return true; }
            }
            return false;
        }

        // Reads a flat { "k":"v", ... } object of string values into ordered pairs.
        private static List<KeyValuePair<string, string>> ParseFlatStringObject(string s, int open, int close)
        {
            var list = new List<KeyValuePair<string, string>>();
            int i = open + 1;
            while (i < close)
            {
                while (i < close && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                if (i >= close || s[i] != '"') break;
                if (!TryReadJsonString(s, i, out string key, out int afterKey)) break;
                i = SkipToValue(s, afterKey);
                if (i < 0 || i >= close || s[i] != '"') break; // only string values are expected here
                if (!TryReadJsonString(s, i, out string val, out int afterVal)) break;
                i = afterVal;
                list.Add(new KeyValuePair<string, string>(key, val));
            }
            return list;
        }

        // From just after a key token, skip whitespace + the ':' and return the index of the value's first char.
        private static int SkipToValue(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] != ':') return -1;
            i++;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        // Reads a JSON string starting at s[i]=='"', unescaping it; `end` lands just past the closing quote.
        private static bool TryReadJsonString(string s, int i, out string value, out int end)
        {
            value = null;
            end = i;
            if (i >= s.Length || s[i] != '"') return false;
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); end = i; return true; }
                if (c == '\\')
                {
                    if (i >= s.Length) return false;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) return false;
                            sb.Append((char)Convert.ToUInt16(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return false;
        }

        private static string JsonString(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
