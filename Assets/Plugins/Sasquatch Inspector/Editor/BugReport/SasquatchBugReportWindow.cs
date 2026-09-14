using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The in-editor "Report a Bug" form (Tools ▸ Sasquatch Inspector ▸ Report a Bug). The user describes the
    /// problem and, on Send, it goes straight to my issue tracker as a card via <see cref="CodecksReporter"/> —
    /// only ever on their explicit click, and only the fields shown here (message, an optional email/video link,
    /// an optional file, and the small diagnostics block the "What gets sent" box spells out).
    ///
    /// If no Report Token is configured, or the send fails, the window falls back to a pre-filled email so a
    /// user is never left with a dead end. Built programmatically in UI Toolkit and sharing
    /// <c>SasquatchWidgets.uss</c>, the same way as <see cref="SasquatchWelcomeWindow"/>.
    /// </summary>
    public sealed class SasquatchBugReportWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(460f, 640f);

        private TextField _desc;
        private EnumField _severity;
        private TextField _video;
        private TextField _email;
        private string _attachmentPath;
        private Label _attachmentLabel;
        private Button _clearAttachmentBtn;
        private Label _status;
        private Button _sendBtn;
        private Button _emailInsteadBtn;

        /// <summary>Opens the bug-report window.</summary>
        [MenuItem("Tools/Sasquatch Inspector/Report a Bug", false, 15)]
        public static void Open()
        {
            var w = GetWindow<SasquatchBugReportWindow>(true, "Report a Bug", true);
            w.minSize = WindowSize;
            w.Show();
        }

        private void CreateGUI() => BuildForm();

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private VisualElement Frame()
        {
            var root = rootVisualElement;
            root.Clear();

            var sheet = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            SasquatchSkin.Apply(root, !EditorGUIUtility.isProSkin);

            root.style.paddingLeft = 16; root.style.paddingRight = 16;
            root.style.paddingTop = 14; root.style.paddingBottom = 14;
            return root;
        }

        private void BuildForm()
        {
            var root = Frame();

            // ---- Header ----
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var bug = SasquatchIcons.Create(SasquatchIcons.Ids.Bug, 22, DesignerPalette.BrandGold);
            bug.style.marginRight = 8;
            header.Add(bug);
            var title = new Label("Report a Bug");
            title.AddToClassList("sasq-palettewin-title");
            header.Add(title);
            root.Add(header);

            var intro = new Label(
                "Found a bug? Tell me what happened and it goes straight to my tracker — I read every report " +
                "and reply when I can. The more detail (steps to reproduce, what you expected), the faster I can fix it.");
            intro.AddToClassList("sasq-palettewin-hint");
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginTop = 6;
            intro.style.marginBottom = 12;
            root.Add(intro);

            // If bug reporting isn't wired up in this copy, offer the email path and stop here.
            if (!CodecksReporter.IsConfigured)
            {
                root.Add(EmailFallbackPanel());
                return;
            }

            // ---- Scrollable body so the form fits on small displays ----
            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(scroll);

            _desc = new TextField { multiline = true };
            _desc.style.minHeight = 96;
            _desc.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(Field("What happened?", _desc,
                "Required. Steps to reproduce and what you expected help the most."));

            _severity = new EnumField(CodecksReporter.Severity.Normal);
            scroll.Add(Field("How bad is it?", _severity,
                "Normal is fine for most. Use Critical only for crashes or data loss."));

            _video = new TextField();
            scroll.Add(Field("Video link (optional)", _video,
                "Paste a clip link (YouTube, Streamable, Drive…). No file-size limit — link as many as you like."));

            _email = new TextField();
            scroll.Add(Field("Your email (optional)", _email,
                "So I can reach you when it's fixed. Left out otherwise."));

            scroll.Add(AttachmentRow());
            scroll.Add(WhatGetsSentBox());

            // ---- Status line (hidden until sending / on error) ----
            _status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6, display = DisplayStyle.None } };
            root.Add(_status);

            // ---- Footer ----
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.marginTop = 10;
            footer.style.paddingTop = 10;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(0f, 0f, 0f, 0.30f);

            _emailInsteadBtn = LinkButton("Email it to me instead", SasquatchIcons.Ids.ExternalLink,
                () => OpenEmailFallback());
            _emailInsteadBtn.style.display = DisplayStyle.None;
            footer.Add(_emailInsteadBtn);

            footer.Add(new VisualElement { style = { flexGrow = 1 } });

            var cancel = new Button(Close) { text = "Cancel" };
            cancel.style.height = 28; cancel.style.minWidth = 76; cancel.style.marginRight = 6;
            EditorSpringFX.ButtonSpring(cancel);
            footer.Add(cancel);

            _sendBtn = new Button(Submit) { text = "Send Report" };
            _sendBtn.style.height = 28; _sendBtn.style.minWidth = 110;
            _sendBtn.style.backgroundColor = DesignerPalette.AccentFill;
            EditorSpringFX.ButtonSpring(_sendBtn);
            footer.Add(_sendBtn);

            root.Add(footer);

            if (EditorSpringFX.MotionEnabled) EditorSpringFX.SlideInY(root, -8f);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Pieces
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        // A stacked "label over control (+ hint)" form row.
        private static VisualElement Field(string label, VisualElement control, string hint)
        {
            var col = new VisualElement { style = { marginBottom = 12 } };
            var l = new Label(label);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 12;
            l.style.marginBottom = 3;
            col.Add(l);
            col.Add(control);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = new Label(hint);
                h.AddToClassList("sasq-palettewin-hint");
                h.style.whiteSpace = WhiteSpace.Normal;
                h.style.marginTop = 3;
                col.Add(h);
            }
            return col;
        }

        private VisualElement AttachmentRow()
        {
            var col = new VisualElement { style = { marginBottom = 12 } };
            var l = new Label("Screenshot or file (optional)");
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 12;
            l.style.marginBottom = 3;
            col.Add(l);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var choose = new Button(ChooseAttachment);
            choose.style.flexDirection = FlexDirection.Row;
            choose.style.alignItems = Align.Center;
            choose.style.height = 24;
            var upIcon = SasquatchIcons.Create(SasquatchIcons.Ids.Upload, 13, DesignerPalette.IconMuted);
            upIcon.style.marginRight = 5;
            choose.Add(upIcon);
            choose.Add(new Label("Choose file…"));
            EditorSpringFX.ButtonSpring(choose);
            row.Add(choose);

            _attachmentLabel = new Label("No file chosen");
            _attachmentLabel.AddToClassList("sasq-palettewin-hint");
            _attachmentLabel.style.marginLeft = 8;
            _attachmentLabel.style.flexShrink = 1;
            _attachmentLabel.style.overflow = Overflow.Hidden;
            row.Add(_attachmentLabel);

            _clearAttachmentBtn = new Button(ClearAttachment) { text = "✕" };
            _clearAttachmentBtn.style.height = 20;
            _clearAttachmentBtn.style.marginLeft = 6;
            _clearAttachmentBtn.style.display = DisplayStyle.None;
            row.Add(_clearAttachmentBtn);

            col.Add(row);

            var hint = new Label(
                "Use your OS snip tool (Win+Shift+S / Cmd+Shift+4), then attach the image. " +
                "Images, clips, logs, zips or PDFs up to 10 MB — zip anything else, and for " +
                "larger media use the video link above.");
            hint.AddToClassList("sasq-palettewin-hint");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 3;
            col.Add(hint);
            return col;
        }

        // The transparency box: exactly what leaves the machine, plus the third-party disclosure + privacy link.
        private VisualElement WhatGetsSentBox()
        {
            var box = new VisualElement();
            box.style.marginTop = 2;
            box.style.marginBottom = 4;
            box.style.paddingLeft = 10; box.style.paddingRight = 10;
            box.style.paddingTop = 8; box.style.paddingBottom = 9;
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius =
                box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 6;
            box.style.borderTopWidth = box.style.borderBottomWidth =
                box.style.borderLeftWidth = box.style.borderRightWidth = 1;
            var line = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            box.style.borderTopColor = box.style.borderBottomColor =
                box.style.borderLeftColor = box.style.borderRightColor = line;
            box.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.06f);

            var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var info = SasquatchIcons.Create(SasquatchIcons.Ids.Info, 13, DesignerPalette.IconMuted);
            info.style.marginRight = 5;
            headerRow.Add(info);
            var h = new Label("What gets sent");
            h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.fontSize = 11;
            headerRow.Add(h);
            box.Add(headerRow);

            var diag = new Label(CodecksReporter.BuildDiagnostics());
            diag.AddToClassList("sasq-palettewin-hint");
            diag.style.whiteSpace = WhiteSpace.Normal;
            diag.style.marginTop = 5;
            box.Add(diag);

            var noteRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 6, flexWrap = Wrap.Wrap } };
            var note = new Label("Your report and the above go to my issue tracker (Codecks). Nothing else leaves your machine.");
            note.AddToClassList("sasq-palettewin-hint");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.flexShrink = 1;
            noteRow.Add(note);
            var privacy = LinkButton("Privacy", SasquatchIcons.Ids.ExternalLink,
                () => Application.OpenURL(WelcomeLinks.CodecksPrivacy));
            noteRow.Add(privacy);
            box.Add(noteRow);

            return box;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Attachment handling
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private void ChooseAttachment()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Attach a screenshot or file", "",
                CodecksReporter.AttachmentPanelFilters);
            if (string.IsNullOrEmpty(path)) return;

            // The panel filter already steers to these, but it's advisory on some platforms — check for real.
            if (!CodecksReporter.IsAllowedAttachment(path))
            {
                EditorUtility.DisplayDialog("That file type can't be attached",
                    "Attach an image or clip (png, jpg, gif, webp, mp4, mov, webm) or a log/document " +
                    "(txt, log, json, zip, pdf). Anything else: zip it first and attach the zip.", "OK");
                return;
            }

            long size = 0;
            try { size = new System.IO.FileInfo(path).Length; } catch { /* unreadable — handled below */ }
            if (size <= 0 || size > CodecksReporter.AttachmentMaxBytes)
            {
                EditorUtility.DisplayDialog("File too large",
                    "That file is over the 10 MB limit (or couldn't be read). For larger media, paste a link in " +
                    "the \"Video link\" field instead — there's no size limit there.", "OK");
                return;
            }

            _attachmentPath = path;
            _attachmentLabel.text = System.IO.Path.GetFileName(path);
            _clearAttachmentBtn.style.display = DisplayStyle.Flex;
        }

        private void ClearAttachment()
        {
            _attachmentPath = null;
            _attachmentLabel.text = "No file chosen";
            _clearAttachmentBtn.style.display = DisplayStyle.None;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Send
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private void Submit()
        {
            string message = _desc.value;
            if (string.IsNullOrWhiteSpace(message))
            {
                ShowStatus("Please describe the bug before sending.", error: true);
                return;
            }
            if (message.Length > CodecksReporter.MessageMaxChars)
            {
                ShowStatus($"That description is a little long for one report (the limit is " +
                    $"{CodecksReporter.MessageMaxChars:N0} characters). Trim it down — long console logs " +
                    "do better as a .txt attachment.", error: true);
                return;
            }

            if (!CodecksReporter.TryValidateVideoLinks(_video.value, out string linkError))
            {
                ShowStatus(linkError, error: true);
                return;
            }

            string email = _email.value;
            if (!string.IsNullOrWhiteSpace(email) && !CodecksReporter.LooksLikeEmail(email))
            {
                ShowStatus("That email address doesn't look quite right — mind double-checking it? " +
                    "(Or clear the field; it's optional.)", error: true);
                return;
            }

            _sendBtn.SetEnabled(false);
            _emailInsteadBtn.style.display = DisplayStyle.None;
            ShowStatus("Sending…", error: false);

            var severity = (CodecksReporter.Severity)_severity.value;
            CodecksReporter.Send(message, severity, _email.value, _video.value, _attachmentPath, OnSent);
        }

        private void OnSent(CodecksReporter.Result result)
        {
            if (result.ok)
            {
                ShowThankYou(result.attachmentFailed);
                return;
            }

            _sendBtn.SetEnabled(true);
            ShowStatus((result.error ?? "Something went wrong.") + " You can email it to me instead.", error: true);
            _emailInsteadBtn.style.display = DisplayStyle.Flex;
        }

        private void ShowStatus(string text, bool error)
        {
            _status.text = text;
            _status.style.display = DisplayStyle.Flex;
            _status.style.color = error ? new Color(0.85f, 0.35f, 0.30f) : DesignerPalette.IconMuted;
        }

        private void ShowThankYou(bool attachmentFailed)
        {
            var root = Frame();
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;

            var check = SasquatchIcons.Create(SasquatchIcons.Ids.Check, 46, new Color(0.36f, 0.72f, 0.42f));
            check.style.marginBottom = 10;
            root.Add(check);

            var t = new Label("Thanks — your report is in!");
            t.AddToClassList("sasq-palettewin-title");
            t.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(t);

            string sub = "It just landed as a card in my tracker. If you left your email, I'll follow up when it's fixed.";
            if (attachmentFailed)
                sub += "\n\n(Your file couldn't be uploaded, but the report itself went through fine.)";
            var s = new Label(sub);
            s.AddToClassList("sasq-palettewin-hint");
            s.style.whiteSpace = WhiteSpace.Normal;
            s.style.unityTextAlign = TextAnchor.MiddleCenter;
            s.style.marginTop = 6; s.style.marginBottom = 16;
            s.style.maxWidth = 320;
            root.Add(s);

            var close = new Button(Close) { text = "Close" };
            close.style.height = 28; close.style.minWidth = 100;
            EditorSpringFX.ButtonSpring(close);
            root.Add(close);

            if (EditorSpringFX.MotionEnabled) EditorSpringFX.PopIn(root);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────
        //  Email fallback
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        // Panel shown when no Report Token is configured — a friendly nudge to the email path.
        private VisualElement EmailFallbackPanel()
        {
            var col = new VisualElement();
            var note = new Label(
                "Send me the details by email and I'll get right on it — the more you can include (steps, your " +
                "Unity version, any console errors, a screenshot or clip), the faster I can squash it.");
            note.AddToClassList("sasq-palettewin-hint");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 12;
            col.Add(note);

            var email = new Button(() => OpenEmailFallback()) { text = $"Email {WelcomeLinks.SupportEmail}" };
            email.style.height = 30;
            EditorSpringFX.ButtonSpring(email);
            col.Add(email);
            return col;
        }

        // Opens the user's mail app pre-filled with the report + diagnostics. Used both as the no-token fallback
        // and as the "email it instead" escape hatch when a send fails.
        private void OpenEmailFallback()
        {
            string message = _desc != null ? _desc.value : string.Empty;
            string video = _video != null ? _video.value : string.Empty;
            string subject = "Sasquatch Inspector — Bug report";
            string body = CodecksReporter.ComposeContent(message, video);
            Application.OpenURL(
                $"mailto:{WelcomeLinks.SupportEmail}" +
                $"?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────

        // A small, borderless icon+text "link" button (mirrors SasquatchWelcomeWindow's link styling).
        private static Button LinkButton(string text, string iconId, Action onClick)
        {
            var linkColor = DesignerPalette.WelcomeLink;
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.backgroundColor = Color.clear;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.paddingLeft = 4; btn.style.paddingRight = 2;
            btn.style.paddingTop = 2; btn.style.paddingBottom = 2;
            btn.style.marginLeft = 4; btn.style.marginRight = 0;

            var icon = SasquatchIcons.Create(iconId, 11, linkColor);
            icon.style.marginRight = 4;
            btn.Add(icon);
            var label = new Label(text);
            label.style.color = linkColor;
            label.style.fontSize = 12;
            btn.Add(label);

            EditorSpringFX.ButtonSpring(btn);
            return btn;
        }
    }
}
