using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The first-install Welcome popup. Thanks the buyer and routes them to the in-editor Tutorial, the Showcase
    /// Sandbox, or the documentation, and (after a major update) shows a "What's New" panel. Auto-shown once by
    /// <see cref="WelcomeBootstrap"/> — see <see cref="WelcomeState"/> for the gate; re-openable any time via
    /// <c>Tools ▸ Sasquatch Inspector ▸ Welcome</c>. Built programmatically in UI Toolkit and sharing
    /// <c>SasquatchWidgets.uss</c>, the same way as <see cref="SasquatchBugReportWindow"/>.
    /// </summary>
    public sealed class SasquatchWelcomeWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(560f, 600f);

        // Brand-ish accents reused from the rest of the chrome (skin-aware properties, not readonly fields).
        private static Color Gold => DesignerPalette.BrandGold;
        private static Color PrimaryBlue => DesignerPalette.AccentFill;

        [MenuItem("Tools/Sasquatch Inspector/Welcome", false, 0)]
        private static void OpenFromMenu() => ShowWelcome(auto: false);

        /// <summary>Opens the Welcome popup and records the acknowledgement, so it won't re-pop on the next
        /// reload — whether this is the first-run auto-show (<paramref name="auto"/>) or a manual reopen.</summary>
        public static void ShowWelcome(bool auto)
        {
            var w = CreateInstance<SasquatchWelcomeWindow>();
            w.titleContent = new GUIContent("Welcome to Sasquatch Inspector");
            w.minSize = w.maxSize = WindowSize;
            w.position = CenteredRect(WindowSize);
            // Record immediately so a domain reload right after opening doesn't re-pop within this major version.
            WelcomeState.Acknowledge();
            w.ShowUtility();
        }

        private static Rect CenteredRect(Vector2 size)
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            float x = main.x + (main.width - size.x) * 0.5f;
            float y = main.y + (main.height - size.y) * 0.5f;
            return new Rect(x, y, size.x, size.y);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            // Chrome follows the EDITOR skin (light editors get the light token values for any sasq-* classes).
            SasquatchSkin.Apply(root, !EditorGUIUtility.isProSkin);

            root.style.paddingLeft = 18; root.style.paddingRight = 18;
            root.style.paddingTop = 16; root.style.paddingBottom = 14;

            // ---- Header: sparkle + title + thank-you subtitle ----
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 4;
            var spark = SasquatchIcons.Create(SasquatchIcons.Ids.Sparkles, 26, Gold);
            spark.style.marginRight = 8;
            titleRow.Add(spark);
            var title = new Label("Thanks for installing Sasquatch Inspector!");
            title.AddToClassList("sasq-palettewin-title");
            titleRow.Add(title);
            root.Add(titleRow);

            var hint = new Label(
                "You just made building beautiful, custom inspectors a whole lot faster. Here's where to start —");
            hint.AddToClassList("sasq-palettewin-hint");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 14;
            root.Add(hint);

            // ---- "What's New" (only after a major update, never on a fresh install) ----
            if (WelcomeState.IsUpgrade())
                root.Add(BuildWhatsNewCard());

            // ---- Primary calls-to-action ----
            root.Add(BuildCta(
                "Take the 5-minute Tutorial",
                "A quick, hands-on tour: turn a plain script into a polished inspector.",
                SasquatchIcons.Ids.Zap, Gold, primary: true,
                () => { SasquatchTutorialWindow.Open(); Close(); }));

            root.Add(BuildCta(
                "Explore the Showcase Sandbox",
                "A live gallery of every widget — drag, type, and right-click to Quick-Edit.",
                SasquatchIcons.Ids.LayoutGrid, DesignerPalette.Accent, primary: false,
                SasquatchShowcaseWindow.Open));

            root.Add(BuildCta(
                "Open the Documentation",
                "The full written guide and reference.",
                SasquatchIcons.Ids.FileCode, DesignerPalette.WelcomeIconDim, primary: false,
                OpenDocs));

            // ---- Secondary text links: Report a Bug • YouTube ----
            var links = new VisualElement();
            links.style.flexDirection = FlexDirection.Row;
            links.style.alignItems = Align.Center;
            links.style.marginTop = 10;
            links.Add(LinkButton("Report a Bug", SasquatchIcons.Ids.Bug, SasquatchBugReportWindow.Open));
            links.Add(LinkDot());
            links.Add(LinkButton("YouTube channel", SasquatchIcons.Ids.ExternalLink,
                () => Application.OpenURL(WelcomeLinks.YouTube)));
            root.Add(links);

            // ---- Footer: Close, pinned to the bottom ----
            // No "Don't show this again" opt-out: the window is already show-once (WelcomeState gates it on the
            // last acknowledged MAJOR), and a permanent opt-out only stranded people away from the Tutorial,
            // which is the one thing they have to come back here to reach.
            var spacer = new VisualElement { style = { flexGrow = 1 } };
            root.Add(spacer);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.marginTop = 12;
            footer.style.paddingTop = 10;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(0f, 0f, 0f, 0.35f);

            var close = new Button(Close) { text = "Close" };
            close.style.height = 26;
            close.style.minWidth = 76;
            EditorSpringFX.ButtonSpring(close);
            footer.Add(close);
            root.Add(footer);

            EditorSpringFX.PopIn(root);
        }

        // A full-width "list button" CTA: an accent icon chip, a bold title, and a one-line subtitle. The
        // primary action is tinted to stand out; the rest read as quiet secondary rows.
        private VisualElement BuildCta(string titleText, string subtitleText, string iconId, Color accent,
            bool primary, Action onClick)
        {
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.height = StyleKeyword.Auto;
            btn.style.paddingTop = 9; btn.style.paddingBottom = 9;
            btn.style.paddingLeft = 10; btn.style.paddingRight = 10;
            btn.style.marginTop = 5; btn.style.marginBottom = 0;
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (primary)
            {
                btn.style.backgroundColor = PrimaryBlue;
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor = new Color(0.3f, 0.5f, 0.75f, 1f);
            }

            var iconChip = SasquatchIcons.Create(iconId, 18, accent);
            iconChip.style.marginRight = 10;
            iconChip.style.flexShrink = 0;
            btn.Add(iconChip);

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            textCol.style.flexShrink = 1;
            var t = new Label(titleText);
            t.style.unityFontStyleAndWeight = FontStyle.Bold;
            t.style.color = DesignerPalette.WelcomeTitle;
            t.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(t);
            var s = new Label(subtitleText);
            s.style.fontSize = 11;
            s.style.color = primary ? DesignerPalette.WelcomeSubPrimary : DesignerPalette.WelcomeSubSecondary;
            s.style.whiteSpace = WhiteSpace.Normal;
            s.style.marginTop = 1;
            textCol.Add(s);
            btn.Add(textCol);

            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 14, DesignerPalette.WelcomeChevron);
            chevron.style.marginLeft = 6;
            chevron.style.flexShrink = 0;
            btn.Add(chevron);

            EditorSpringFX.HoverScale(btn, 1.012f, EditorSpringFX.HoverLiftConfig);
            return btn;
        }

        // A small, borderless icon+text "link" styled button (opens a window or a URL). Content is built from
        // children (not the Button's own text) so the icon and label never overlap.
        private Button LinkButton(string text, string iconId, Action onClick)
        {
            var linkColor = DesignerPalette.WelcomeLink;
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.backgroundColor = Color.clear;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
            btn.style.paddingLeft = 2; btn.style.paddingRight = 2;
            btn.style.paddingTop = 2; btn.style.paddingBottom = 2;
            btn.style.marginLeft = 0; btn.style.marginRight = 0;

            var icon = SasquatchIcons.Create(iconId, 12, linkColor);
            icon.style.marginRight = 4;
            btn.Add(icon);
            var label = new Label(text);
            label.style.color = linkColor;
            label.style.fontSize = 12;
            btn.Add(label);

            EditorSpringFX.ButtonSpring(btn);
            return btn;
        }

        // The "•" separator between secondary text links.
        private Label LinkDot()
        {
            var dot = new Label("•");
            dot.style.color = DesignerPalette.WelcomeDot;
            dot.style.marginLeft = 6; dot.style.marginRight = 6;
            return dot;
        }

        // A collapsible ".sasq-group" card listing the highlights of this major release. Built only on upgrade.
        private VisualElement BuildWhatsNewCard()
        {
            var group = new VisualElement();
            group.AddToClassList("sasq-group");
            group.style.marginBottom = 12;

            var header = new VisualElement();
            header.AddToClassList("sasq-group-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 11, DesignerPalette.IconMuted);
            chevron.AddToClassList("sasq-group-chevron");
            chevron.style.marginRight = 4;
            header.Add(chevron);
            var htitle = new Label($"What's new in v{SasquatchVersion.Major}");
            htitle.AddToClassList("sasq-group-title");
            header.Add(htitle);
            group.Add(header);

            var content = new VisualElement();
            content.AddToClassList("sasq-group-content");
            content.Add(SasquatchMarkdown.BuildMarkdownElement(WelcomeLinks.WhatsNew(SasquatchVersion.Major)));
            group.Add(content);

            bool expanded = true;
            EditorSpringFX.SpringCaret(chevron, true, animate: false);
            header.pickingMode = PickingMode.Position;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                expanded = !expanded;
                EditorSpringFX.SpringCaret(chevron, expanded);
                EditorSpringFX.Fold(content, expanded);
                evt.StopPropagation();
            });
            return group;
        }

        // Opens the online docs URL when one is configured; otherwise opens the bundled user-manual PDF. Falls
        // back to a gentle placeholder only if neither is available, so the button is never a dead link.
        private void OpenDocs()
        {
            if (!string.IsNullOrEmpty(WelcomeLinks.Docs))
            {
                Application.OpenURL(WelcomeLinks.Docs);
                return;
            }

            string manual = SasquatchPaths.UserManualPath;
            if (!string.IsNullOrEmpty(manual))
                EditorUtility.OpenWithDefaultApp(manual);
            else
                ShowNotification(new GUIContent("Documentation is coming soon!"));
        }
    }
}
