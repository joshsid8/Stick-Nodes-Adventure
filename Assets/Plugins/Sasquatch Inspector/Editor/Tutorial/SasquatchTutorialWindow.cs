using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The custom guided Tutorial: a paginated "steps" window that teaches the basic getting-started workflow in
    /// a few minutes, with live before→after inspector previews, optional illustrative images, and a
    /// graduation hand-off to the real Layout Designer. Built entirely from the package's own UI infra (springs,
    /// icons, markdown, palette) so it matches the rest of the tool. Opened from the Welcome popup or
    /// <c>Tools ▸ Sasquatch Inspector ▸ Tutorial</c>.
    /// </summary>
    public sealed class SasquatchTutorialWindow : EditorWindow
    {
        private static readonly Vector2 MinWindowSize = new Vector2(620f, 640f);

        // A single live inspector preview is capped to this height; past it the preview scrolls inside its own
        // frame rather than pushing the rest of a page around.
        private const float PreviewMaxHeight = 480f;
        // Skin-aware properties (not readonly fields) so a light editor gets the darkened brand hues.
        private static Color Gold => DesignerPalette.BrandGold;
        private static Color SaveBlue => DesignerPalette.AccentFill;

        // Tutorial body copy reads a touch larger than the shared 11px help-box default.
        private const float BodyFontSize = 13f;

        // Serialized so the current step survives a domain reload (this window is dockable and may outlive a
        // recompile). _furthest is the highest step reached so far — it gates how far the progress dots can jump.
        [SerializeField] private int _index;
        [SerializeField] private int _furthest;

        private List<TutorialStep> _steps;
        private readonly List<TutorialPreview> _previews = new();

        private VisualElement _dotsRow;
        private readonly List<VisualElement> _dotVisuals = new();
        private Label _stepCounter;
        private Label _stepTitle;
        private VisualElement _iconHost;
        private ScrollView _body;
        private VisualElement _pageHost;
        private Button _backBtn;
        private Button _nextBtn;

        public static void Open()
        {
            var w = GetWindow<SasquatchTutorialWindow>("Sasquatch Inspector Tutorial");
            w.minSize = MinWindowSize;
            w.Show();
        }

        private void OnDisable() => DisposePreviews();

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            // Chrome follows the EDITOR skin (light editors get the light token values for any sasq-* classes;
            // the live previews inside re-scope the variables on their own build roots per the resolved theme).
            SasquatchSkin.Apply(root, !EditorGUIUtility.isProSkin);

            _steps = TutorialContent.BuildSteps(this);
            if (_index < 0 || _index >= _steps.Count) _index = 0;
            _furthest = Mathf.Clamp(_furthest, _index, _steps.Count - 1);

            root.style.paddingLeft = 18; root.style.paddingRight = 18;
            root.style.paddingTop = 14; root.style.paddingBottom = 12;

            // Header: icon + title + "Step X of N".
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            _iconHost = new VisualElement();
            _iconHost.style.marginRight = 8;
            _iconHost.style.width = 22; _iconHost.style.height = 22;
            _iconHost.style.flexShrink = 0;
            headerRow.Add(_iconHost);
            _stepTitle = new Label();
            _stepTitle.AddToClassList("sasq-palettewin-title");
            _stepTitle.style.flexGrow = 1;
            headerRow.Add(_stepTitle);
            _stepCounter = new Label();
            _stepCounter.AddToClassList("sasq-palettewin-hint");
            _stepCounter.style.flexShrink = 0;
            headerRow.Add(_stepCounter);
            root.Add(headerRow);

            // Progress dots (interactive — see BuildDots / CanNavigateTo).
            _dotsRow = new VisualElement();
            _dotsRow.style.flexDirection = FlexDirection.Row;
            _dotsRow.style.alignItems = Align.Center;
            _dotsRow.style.marginTop = 8; _dotsRow.style.marginBottom = 10;
            root.Add(_dotsRow);

            // Body: a single scrollable page host whose blocks (content / media / preview / action) are rebuilt
            // per step so the action can sit above or below the preview.
            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.style.flexGrow = 1;
            _body.style.minHeight = 0;
            // Vertical-only: never let a hair-too-wide preview spawn a horizontal scrollbar (it steals ~38px of
            // vertical room, which is what made the preview look cramped on content-heavy pages).
            _body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _pageHost = new VisualElement();
            _body.Add(_pageHost);
            root.Add(_body);

            // Footer: Back / Next (becomes Finish on the last step).
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.marginTop = 10;
            footer.style.paddingTop = 10;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(0f, 0f, 0f, 0.35f);
            _backBtn = new Button(GoBack) { text = "◂  Back" };
            _backBtn.style.height = 28; _backBtn.style.minWidth = 90;
            EditorSpringFX.ButtonSpring(_backBtn);
            footer.Add(_backBtn);
            footer.Add(new VisualElement { style = { flexGrow = 1 } });
            _nextBtn = new Button(GoNext) { text = "Next  ▸" };
            _nextBtn.style.height = 28; _nextBtn.style.minWidth = 110;
            _nextBtn.style.backgroundColor = SaveBlue;
            _nextBtn.style.color = Color.white;
            EditorSpringFX.ButtonSpring(_nextBtn);
            footer.Add(_nextBtn);
            root.Add(footer);

            BuildDots();
            GoTo(_index);
        }

        private void BuildDots()
        {
            _dotsRow.Clear();
            _dotVisuals.Clear();
            for (int i = 0; i < _steps.Count; i++)
            {
                int target = i;   // capture for the click handler

                // A padded hit area around the small visual dot so it's comfortably clickable.
                var hit = new VisualElement();
                hit.style.flexDirection = FlexDirection.Row;
                hit.style.alignItems = Align.Center;
                hit.style.justifyContent = Justify.Center;
                hit.style.width = 20; hit.style.height = 16;
                hit.style.marginRight = 2;

                var dot = new VisualElement();
                dot.style.width = 9; dot.style.height = 9;
                dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                    dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 5;
                hit.Add(dot);
                _dotVisuals.Add(dot);

                hit.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    if (CanNavigateTo(target))
                    {
                        if (target != _index) GoTo(target);
                    }
                    else
                    {
                        // Forward jumps to not-yet-visited steps are blocked — a gentle "no" instead.
                        EditorSpringFX.Shake(_dotsRow, 0.5f);
                    }
                    evt.StopPropagation();
                });
                EditorSpringFX.HoverScale(hit, 1.18f);

                _dotsRow.Add(hit);
            }
        }

        // You may jump to any step you've already reached (<= _furthest), or step forward to exactly the next one
        // (_index + 1). You can NOT skip forward over steps you haven't seen — reaching a new step always advances
        // one page at a time, so reaching the frontier requires being on it.
        private bool CanNavigateTo(int i) => i >= 0 && i < _steps.Count && (i <= _furthest || i == _index + 1);

        private void RefreshDots()
        {
            for (int i = 0; i < _dotVisuals.Count; i++)
            {
                var dot = _dotVisuals[i];
                bool active = i == _index;
                bool visited = i <= _furthest;
                bool reachableNew = !visited && CanNavigateTo(i);   // the immediate next, still-unvisited step

                Color c;
                if (active) c = DesignerPalette.Accent;
                else if (visited) c = new Color(0.35f, 0.5f, 0.7f, 0.9f);   // completed (mid blue — reads on both skins)
                else if (reachableNew) c = DesignerPalette.Ink(0.34f);      // the next step you can take
                else c = DesignerPalette.Ink(0.14f);                         // locked (not yet reachable)

                dot.style.backgroundColor = c;

                // The current step's dot is a touch bigger so the page you're on reads at a glance.
                float size = active ? 13f : 9f;
                dot.style.width = size; dot.style.height = size;
                float r = size * 0.5f;
                dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                    dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = r;

                if (active) EditorSpringFX.PingScale(dot);
            }
        }

        private void GoBack() { if (_index > 0) GoTo(_index - 1); }

        private void GoNext()
        {
            if (_index < _steps.Count - 1) GoTo(_index + 1);
            else Close();   // Finish
        }

        private void GoTo(int i)
        {
            _index = Mathf.Clamp(i, 0, _steps.Count - 1);
            _furthest = Mathf.Max(_furthest, _index);
            var step = _steps[_index];

            DisposePreviews();
            _pageHost.Clear();

            // Header.
            _iconHost.Clear();
            if (!string.IsNullOrEmpty(step.IconId))
                _iconHost.Add(SasquatchIcons.Create(step.IconId, 20, Gold));
            _stepTitle.text = step.Title;
            _stepCounter.text = $"Step {_index + 1} of {_steps.Count}";

            // Build the page's blocks in order, collecting them so they can stagger in together.
            var blocks = new List<VisualElement>();
            if (step.Content != null)
            {
                VisualElement currentButtonRow = null;

                foreach (var block in step.Content)
                {
                    if (block.Kind == TutorialBlockKind.Button && !block.Secondary)
                    {
                        var btn = BuildSingleActionButton(block.ButtonLabel, block.ButtonAction, block.ButtonColor);
                        if (btn != null)
                        {
                            if (currentButtonRow == null || block.ButtonColor != null)
                            {
                                currentButtonRow = new VisualElement();
                                currentButtonRow.style.flexDirection = FlexDirection.Row;
                                currentButtonRow.style.flexWrap = Wrap.Wrap;
                                currentButtonRow.style.marginTop = 12;
                                currentButtonRow.style.marginBottom = 16;
                                currentButtonRow.style.flexShrink = 0;
                                _pageHost.Add(currentButtonRow);
                                blocks.Add(currentButtonRow);
                            }
                            btn.style.marginRight = 8;
                            currentButtonRow.Add(btn);
                        }
                        if (block.ButtonColor != null) currentButtonRow = null;
                        continue;
                    }

                    currentButtonRow = null;
                    var el = BuildBlock(block);
                    if (el == null) continue;
                    // Never let a tall block (esp. a full inspector preview) get flex-compressed to fit the
                    // viewport — keep its natural height so the page scrolls instead of squishing.
                    el.style.flexShrink = 0;
                    _pageHost.Add(el);
                    // The live inspector preview is heavy: let it appear at full size immediately rather than
                    // scale-popping in with the staggered text/images (a scaling inspector reads as "squished").
                    if (block.Kind != TutorialBlockKind.Preview) blocks.Add(el);
                }
            }

            _backBtn.SetEnabled(_index > 0);
            _nextBtn.text = _index < _steps.Count - 1 ? "Next  ▸" : "Finish  ✓";

            RefreshDots();
            EditorSpringFX.StaggerIn(blocks);

            // Land every new page scrolled to the very top so devs never miss content above the fold. Reset now
            // and again next frame (the scroll offset only clamps correctly once the new content has laid out).
            ResetScrollToTop();
            _body?.schedule.Execute(ResetScrollToTop);
        }

        private void ResetScrollToTop()
        {
            if (_body == null) return;
            _body.scrollOffset = new Vector2(_body.scrollOffset.x, 0f);
        }

        // Renders a single ordered page block, or null when it produces nothing (e.g. a preview factory that
        // returned null, or a button missing its label/action).
        private VisualElement BuildBlock(TutorialBlock block)
        {
            switch (block.Kind)
            {
                case TutorialBlockKind.Markdown:
                {
                    var content = SasquatchMarkdown.BuildMarkdownElement(block.Markdown, BodyFontSize);
                    content.style.marginBottom = 10;
                    return content;
                }
                case TutorialBlockKind.Image:
                    return TutorialMediaView.BuildOne(block.Media);
                case TutorialBlockKind.Preview:
                    return BuildPreviewBlock(block.PreviewFactory);
                case TutorialBlockKind.Button:
                    return BuildActionButton(block.ButtonLabel, block.ButtonAction, block.Secondary, block.ButtonColor);
                default:
                    return null;
            }
        }

        // Builds a framed live inspector preview, tracking it for disposal, or null when the factory yields nothing.
        private VisualElement BuildPreviewBlock(Func<TutorialPreview> factory)
        {
            if (factory == null) return null;
            var preview = factory();
            if (preview?.Element == null) return null;
            _previews.Add(preview);

            var frame = new VisualElement();
            frame.style.marginTop = 4;
            frame.style.paddingLeft = 10; frame.style.paddingRight = 10;
            frame.style.paddingTop = 8; frame.style.paddingBottom = 8;
            frame.style.backgroundColor = DesignerPalette.TutorialFrameBg;
            frame.style.borderTopLeftRadius = frame.style.borderTopRightRadius =
                frame.style.borderBottomLeftRadius = frame.style.borderBottomRightRadius = 6;

            // Cap the preview's footprint: below the cap it sizes to content (no scrollbar); above it, the preview
            // scrolls within this frame so it can never dominate the page. Vertical-only — a hair-too-wide inspector
            // must never spawn a horizontal scrollbar here (same reasoning as the page body).
            var previewScroll = new ScrollView(ScrollViewMode.Vertical);
            previewScroll.style.maxHeight = PreviewMaxHeight;
            previewScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            previewScroll.Add(preview.Element);
            frame.Add(previewScroll);
            return frame;
        }

        private Button BuildSingleActionButton(string label, Action action, Color? customColor = null)
        {
            if (string.IsNullOrEmpty(label) || action == null) return null;
            var act = new Button(action) { text = label };
            act.style.height = 30;
            act.style.backgroundColor = customColor ?? SaveBlue;
            act.style.color = Color.white;
            act.style.unityFontStyleAndWeight = FontStyle.Bold;
            act.style.paddingLeft = 14; act.style.paddingRight = 14;
            EditorSpringFX.ButtonSpring(act);
            return act;
        }

        // Builds an action button wrapped in a left-aligned host, or null when label/action is missing. A
        // secondary block renders as a soft, borderless "link" (a low-pressure alternative to the primary CTA).
        private VisualElement BuildActionButton(string label, Action action, bool secondary = false, Color? customColor = null)
        {
            if (string.IsNullOrEmpty(label) || action == null) return null;
            if (secondary) return BuildLinkButton(label, action);

            var host = new VisualElement();
            // Balance the breathing room above the button (the preceding paragraph's margin + this top margin)
            // with a roughly matching gap below it.
            host.style.marginTop = 12; host.style.marginBottom = 16;
            host.style.alignItems = Align.FlexStart;

            var act = BuildSingleActionButton(label, action, customColor);
            if (act == null) return null;
            host.Add(act);
            return host;
        }

        // A soft, borderless "link"-styled action for low-pressure secondary paths (mirrors the link styling used
        // by the Welcome / Bug-report windows). Sits tight to its neighbours so a small stack reads as a link list.
        private VisualElement BuildLinkButton(string label, Action action)
        {
            var host = new VisualElement();
            host.style.marginTop = 2; host.style.marginBottom = 4;
            host.style.alignItems = Align.FlexStart;

            var link = new Button(action) { text = label };
            link.style.backgroundColor = Color.clear;
            link.style.borderTopWidth = link.style.borderBottomWidth =
                link.style.borderLeftWidth = link.style.borderRightWidth = 0;
            link.style.paddingLeft = 0; link.style.paddingRight = 0;
            link.style.paddingTop = 3; link.style.paddingBottom = 3;
            link.style.marginLeft = 0;
            link.style.color = DesignerPalette.WelcomeLink;
            link.style.fontSize = 12;
            EditorSpringFX.ButtonSpring(link);
            host.Add(link);
            return host;
        }

        private void DisposePreviews()
        {
            foreach (var p in _previews) p?.Dispose();
            _previews.Clear();
        }
    }
}
