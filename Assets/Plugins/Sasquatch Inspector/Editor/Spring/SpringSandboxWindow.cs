using System;
using System.Collections.Generic;
using System.Globalization;
using SasquatchSpring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    internal enum SpringSandboxShape { Sphere, Cube, Capsule, Quad, Mesh, Sprite }

    /// <summary>
    /// An interactive playground for SasquatchSpring: flick grouped buttons to fire REAL springs on a live
    /// 3D preview object, tune the feel with sliders + a bounce graph, and copy the exact code. Everything
    /// runs through the real engine (the editor driver ticks springs at edit time). Drag the object to feel
    /// it spring home; middle-drag pans, Alt-drag orbits, scroll zooms, and a 2D toggle flattens the view.
    /// </summary>
    public sealed class SpringSandboxWindow : EditorWindow
    {
        private enum DragMode { None, Object, Orbit, Pan }

        private const string YoyoKey = "sandbox.yoyo";
        private const string SpinKey = "sandbox.spin";
        private static readonly int s_dragHash = "SpringSandboxDrag".GetHashCode();

        // State (plain fields — the tuning UI is native UI Toolkit, so no backing ScriptableObject is needed).
        private SpringConfig _config = SpringConfig.Bouncy;
        private SpringSandboxShape _shape = SpringSandboxShape.Sphere;
        private Mesh _customMesh;
        private Sprite _sprite;
        private Color _baseColor = new Color(0.27f, 0.55f, 0.95f, 1f);
        private bool _wireframe = true;
        private bool _is2D;

        // Preview
        private PreviewRenderUtility _preview;
        private GameObject _go;
        private Material _material;
        private SpriteRenderer _spriteRenderer;
        private string _colorProp = "_BaseColor";
        private IMGUIContainer _previewIMGUI;
        private readonly Vector3 _home = Vector3.zero;

        // Camera
        private Vector2 _camEuler = new Vector2(15f, 20f);
        private float _camDistance = 4f;
        private Vector3 _camPivot = Vector3.zero;
        private DragMode _dragMode = DragMode.None;
        private bool _dragMoved;
        private Vector2 _lastDrag;

        // UI refs
        private Toggle _wireToggle;
        private VisualElement _fadeGroup;
        private ObjectField _meshField, _spriteField;
        private DropdownField _presetDropdown;
        private Slider _durationSlider, _bounceSlider;
        private SasquatchSpringControl _graph;
        private Button _2dButton, _growBtn;
        private bool _grownUp;

        // Code panel
        private Label _codeLabel;
        private string _snippet = "// Flick an action — the code appears here.";
        // The last "Random Color" color, kept so the Code panel shows the EXACT color that played
        // (re-rolling a fresh random inside the snippet would never match what you just saw).
        private Color _lastRandomColor = new Color(0.85f, 0.4f, 0.35f);

        // The action button currently "selected" (last clicked / showing code).
        private Button _selectedBtn;

        // Tabs: a "Single" object (the classic sandbox) and a "Multiple" grid that demos the Stagger helper.
        private enum SandboxTab { Single, Multiple }
        private SandboxTab _tab = SandboxTab.Single;
        private VisualElement _singlePanel, _multiPanel; // left-pane action cards per tab
        private VisualElement _objControlsBox;           // single-object controls (hidden in Multiple)
        private Button _tabSingleBtn, _tabMultiBtn;

        // Multiple-object grid.
        private const int MultiCols = 10;
        private const int MultiRows = 3;
        private const float MultiSpacing = 0.72f;     // horizontal (column) spacing — tight
        private const float MultiRowSpacing = 1.15f;  // vertical (row) spacing — roomier so a Hop doesn't clip the row above
        private const float MultiRest = 0.42f; // resting uniform scale of each grid sphere
        private readonly List<GameObject> _multiGo = new List<GameObject>();
        private readonly List<Material> _multiMats = new List<Material>();
        private Slider _gapSlider;
        private float _staggerGap = 0.05f;

        private double _repaintUntil;

        [MenuItem("Tools/Sasquatch Inspector/Spring Physics Sandbox", false, 13)]
        /// <summary>Opens the Spring Physics Sandbox window.</summary>
        public static void Open()
        {
            var w = GetWindow<SpringSandboxWindow>("Spring Sandbox");
            w.minSize = new Vector2(720f, 520f);
            w.Show();
        }

        private void OnEnable() => EditorApplication.update += OnEditorUpdate;

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SpringLoop.Stop(YoyoKey);
            SpringLoop.Stop(SpinKey);
            DestroyPreviewObject();
            DestroyMultiObjects();
            _preview?.Cleanup();
            _preview = null;
        }

        private void OnEditorUpdate()
        {
            if (_dragMode != DragMode.None || SpringLoop.IsRunning(YoyoKey) || SpringLoop.IsRunning(SpinKey) ||
                EditorApplication.timeSinceStartup < _repaintUntil)
                Repaint();
        }

        private void BumpRepaint() => _repaintUntil = EditorApplication.timeSinceStartup + 4.0;

        // ---------------------------------------------------------------- UI ----

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            // Chrome follows the EDITOR skin — without the token sheet the sasq-* classes (title etc.)
            // resolve their dark fallbacks, which are unreadable on a light editor.
            SasquatchSkin.Apply(root, !EditorGUIUtility.isProSkin);

            root.style.paddingLeft = 8; root.style.paddingRight = 8;
            root.style.paddingTop = 6; root.style.paddingBottom = 8;

            var title = new Label("Spring Physics Sandbox");
            title.AddToClassList("sasq-palettewin-title");
            title.style.flexShrink = 0;
            root.Add(title);

            var hint = new Label("Tune the feel, then flick an action — it runs the real SasquatchSpring call on the preview and shows you the code.");
            hint.AddToClassList("sasq-palettewin-hint");
            hint.style.flexShrink = 0;
            root.Add(hint);

            root.Add(BuildTabStrip());

            var toolbar = Row();
            toolbar.style.flexShrink = 0;
            toolbar.style.marginTop = 4; toolbar.style.marginBottom = 6;
            toolbar.Add(ToolButton("Reset", ResetObject));
            toolbar.Add(ToolButton("Snap", () => Obj()?.Snap()));
            toolbar.Add(ToolButton("Stop Loops", () => { SpringLoop.Stop(YoyoKey); SpringLoop.Stop(SpinKey); }));
            _2dButton = ToolButton("2D", Toggle2D);
            toolbar.Add(_2dButton);
            root.Add(toolbar);

            var body = Row();
            body.style.flexGrow = 1; body.style.minHeight = 0;
            // Stretch (not the Row() helper's default Center) so the side panels fill the body's height
            // and their ScrollViews actually clip + scroll instead of overflowing over the header.
            body.style.alignItems = Align.Stretch;
            root.Add(body);

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = 250; left.style.flexShrink = 0; left.style.minHeight = 0; left.style.marginRight = 8;
            body.Add(left);

            _singlePanel = new VisualElement();
            BuildActionGroups(_singlePanel);
            left.Add(_singlePanel);

            _multiPanel = new VisualElement();
            BuildMultiActionGroups(_multiPanel);
            left.Add(_multiPanel);

            var right = new ScrollView(ScrollViewMode.Vertical);
            right.style.flexGrow = 1; right.style.minHeight = 0;
            right.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(right);

            _previewIMGUI = new IMGUIContainer(OnPreviewGUI);
            _previewIMGUI.style.height = 300; _previewIMGUI.style.flexShrink = 0;
            _previewIMGUI.style.borderTopLeftRadius = 6; _previewIMGUI.style.borderTopRightRadius = 6;
            _previewIMGUI.style.borderBottomLeftRadius = 6; _previewIMGUI.style.borderBottomRightRadius = 6;
            _previewIMGUI.style.overflow = Overflow.Hidden;
            _previewIMGUI.style.marginBottom = 6;
            right.Add(_previewIMGUI);

            _objControlsBox = BuildObjectControls(); _objControlsBox.style.flexShrink = 0; right.Add(_objControlsBox);
            var tuning = BuildTuning(); tuning.style.flexShrink = 0; right.Add(tuning);
            var code = BuildCodePanel(); code.style.flexShrink = 0; right.Add(code);

            RefreshModeVisibility();
            SwitchTab(_tab);
            BumpRepaint();
        }

        // A two-button tab strip ("Single" / "Multiple") styled like the project's tab containers (a row with a
        // bottom border; the active tab is accented). Switching swaps the left action panel and the preview.
        private VisualElement BuildTabStrip()
        {
            var strip = new VisualElement();
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.flexShrink = 0;
            strip.style.marginTop = 4;
            // A near-invisible structural baseline: a prominent line here reads as "connecting" to the wrong
            // tab. The selected tab is marked by an accent fill + a size difference instead (see SwitchTab).
            strip.style.borderBottomWidth = 1;
            strip.style.borderBottomColor = DesignerPalette.DividerLine;

            _tabSingleBtn = MakeTabButton("Single", () => SwitchTab(SandboxTab.Single));
            _tabMultiBtn = MakeTabButton("Multiple", () => SwitchTab(SandboxTab.Multiple));
            strip.Add(_tabSingleBtn);
            strip.Add(_tabMultiBtn);
            return strip;
        }

        private static Button MakeTabButton(string label, Action onClick)
        {
            var b = new Button(onClick) { text = label };
            b.style.marginLeft = 0; b.style.marginRight = 2; b.style.marginTop = 0; b.style.marginBottom = 0;
            b.style.paddingLeft = 14; b.style.paddingRight = 14; b.style.paddingTop = 5; b.style.paddingBottom = 5;
            b.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            b.style.borderTopWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.borderBottomWidth = 0;
            b.style.borderTopLeftRadius = 4; b.style.borderTopRightRadius = 4;
            b.style.borderBottomLeftRadius = 0; b.style.borderBottomRightRadius = 0;
            // Scale from the bottom-center so a deselected (shrunk) tab stays on the baseline.
            b.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            return b;
        }

        private void SwitchTab(SandboxTab tab)
        {
            _tab = tab;
            bool single = tab == SandboxTab.Single;

            // Active-tab visuals: an accent fill on the selected tab, and the DESELECTED tab springs slightly
            // smaller — the size difference (not a baseline line) is the readability cue.
            var accentFill = DesignerPalette.AccentFill;
            if (_tabSingleBtn != null)
            {
                _tabSingleBtn.style.backgroundColor = single ? accentFill : new StyleColor(StyleKeyword.Null);
                SpringTab(_tabSingleBtn, single);
            }
            if (_tabMultiBtn != null)
            {
                _tabMultiBtn.style.backgroundColor = !single ? accentFill : new StyleColor(StyleKeyword.Null);
                SpringTab(_tabMultiBtn, !single);
            }

            if (_singlePanel != null) _singlePanel.style.display = single ? DisplayStyle.Flex : DisplayStyle.None;
            if (_multiPanel != null) _multiPanel.style.display = single ? DisplayStyle.None : DisplayStyle.Flex;
            if (_objControlsBox != null) _objControlsBox.style.display = single ? DisplayStyle.Flex : DisplayStyle.None;

            RebuildPreviewForTab();
            BumpRepaint();
        }

        // Springs a tab to full size (selected) or slightly smaller (deselected) — the size difference is the cue.
        private static void SpringTab(VisualElement tab, bool selected)
        {
            float target = selected ? 1f : 0.86f;
            if (EditorSpringFX.MotionEnabled) tab.SpringScale(target, EditorSpringFX.Tune(SpringConfig.Bouncy));
            else tab.style.scale = new Scale(new Vector3(target, target, 1f));
        }

        // Tear down the other mode's objects and build this mode's, then frame the camera for it.
        private void RebuildPreviewForTab()
        {
            EnsurePreview();
            _camPivot = Vector3.zero; _camEuler = new Vector2(15f, 20f);
            if (_tab == SandboxTab.Single)
            {
                DestroyMultiObjects();
                if (_go == null) BuildPreviewObject();
                _camDistance = 4f;
            }
            else
            {
                DestroyPreviewObject();
                if (_multiGo.Count == 0) BuildMultiObjects();
                _camDistance = 11f;
            }
        }

        private void RefreshModeVisibility()
        {
            bool sprite = SpriteMode();
            if (_fadeGroup != null) _fadeGroup.style.display = sprite ? DisplayStyle.Flex : DisplayStyle.None;
            if (_wireToggle != null) _wireToggle.style.display = sprite ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void BuildActionGroups(VisualElement parent)
        {
            // MOVE
            var move = Card(parent, "Move");
            move.Add(Act("Slide In", SasquatchIcons.Ids.ArrowRight, ColBlue,
                () => { var c = Obj().position; Obj().MoveFrom(c + Vector3.left * 3f, c, Cfg()); },
                () => $"// Slide in from the left to where it is now\ntransform.MoveFrom(transform.position + Vector3.left * 3f, transform.position, {C()});"));
            move.Add(Act("Move X", SasquatchIcons.Ids.ArrowRight, ColBlue,
                () => Obj().MoveX(Obj().position.x > 0.01f ? -1.5f : 1.5f, Cfg()),
                () => $"transform.MoveX(1.5f, {C()});"));
            move.Add(Act("Hop", SasquatchIcons.Ids.ArrowUp, ColBlue,
                () =>
                {
                    // Velocity scaled by the spring's frequency (like PunchScale) so the hop height is
                    // consistent and clearly visible across configs, instead of a tiny fixed impulse.
                    // Target HOME (not the current position) so mashing can't ratchet the resting target
                    // upward and lose the sphere off-screen — every hop springs back to where it started.
                    float omega = 2f * Mathf.PI * Mathf.Max(0.01f, Cfg().Frequency);
                    Spring.Move(Obj(), _home, Cfg()).Punch(Vector3.up * (0.7f * omega * 2.718f), Cfg());
                },
                () => $"// Impulse hop that always returns to the start position (no drift when mashed)\nSpring.Move(transform, homePosition, {C()}).Punch(Vector3.up * 25f, {C()});"));

            // SCALE
            var scale = Card(parent, "Scale");
            scale.Add(Act("Pop In", SasquatchIcons.Ids.Sparkles, ColGreen,
                () => Obj().ScaleFromTo(0f, 1f, Cfg()),
                () => $"// Pop in from zero\ntransform.ScaleFromTo(0f, 1f, {C()});"));
            scale.Add(Act("Scale Up", SasquatchIcons.Ids.Sparkles, ColGreen,
                () => Obj().ScaleTo(Obj().localScale.x > 1.01f ? 1f : 1.5f, Cfg()),
                () => $"transform.ScaleTo(1.5f, {C()});"));
            scale.Add(Act("Squash X", SasquatchIcons.Ids.AlignCenterVertical, ColGreen,
                () => Obj().ScaleX(Obj().localScale.x > 1.01f ? 1f : 1.6f, Cfg()),
                () => $"transform.ScaleX(1.6f, {C()});"));
            scale.Add(Act("Punch", SasquatchIcons.Ids.Zap, ColYellow,
                () => Obj().PunchScale(new Vector3(0.4f, 0.4f, 0.4f), Cfg()),
                () => $"// Impulse punch — doesn't change the resting scale\ntransform.PunchScale(new Vector3(0.4f, 0.4f, 0.4f), {C()});"));
            _growBtn = Act("Grow Up", SasquatchIcons.Ids.AlignEndVertical, ColGreen,
                () =>
                {
                    _grownUp = !_grownUp;
                    Obj().ScaleFrom(_grownUp ? 1.5f : 1f, SpringPivot.Bottom, Cfg());
                    var l = _growBtn.Q<Label>(); if (l != null) l.text = _grownUp ? "Grow Down" : "Grow Up";
                },
                () => $"// Scale anchored at the bottom\ntransform.ScaleFrom({(_grownUp ? "1.5" : "1")}f, SpringPivot.Bottom, {C()});");
            scale.Add(_growBtn);

            // ROTATE — all relative, so they're mashable (each click does something from the current pose).
            var rot = Card(parent, "Rotate");
            rot.Add(Act("Spin 120", SasquatchIcons.Ids.RotateCcw, ColPurple,
                () => Obj().RotateTo(Obj().rotation * Quaternion.Euler(0, 120, 0), Cfg()),
                () => $"// Gimbal-free shortest-arc rotation\ntransform.RotateTo(transform.rotation * Quaternion.Euler(0, 120, 0), {C()});"));
            rot.Add(Act("Tilt", SasquatchIcons.Ids.RotateCcw, ColPurple,
                () => Obj().RotateTo(Obj().rotation * Quaternion.Euler(15, 0, 25), Cfg()),
                () => $"transform.RotateTo(transform.rotation * Quaternion.Euler(15, 0, 25), {C()});"));
            rot.Add(Act("Flip Y", SasquatchIcons.Ids.RefreshCw, ColPurple,
                () => Obj().RotateBy(new Vector3(0, 180, 0), Cfg()),
                () => $"// Relative spin — stacks on whatever's playing\ntransform.RotateBy(new Vector3(0, 180, 0), {C()});"));

            // COLOR
            var col = Card(parent, "Color");
            col.Add(Act("Random Color", SasquatchIcons.Ids.Sparkles, ColBlue,
                () => { _lastRandomColor = RandomVivid(); SpringColor(_lastRandomColor); },
                () => SpriteMode()
                    ? $"spriteRenderer.ColorTo({ColorExpr(_lastRandomColor)}, {C()});"
                    : $"material.ColorTo(transform, \"{_colorProp}\", {ColorExpr(_lastRandomColor)}, {C()});"));

            // FADE (sprite-only — the mesh material is opaque)
            var fade = Card(parent, "Fade", out _fadeGroup);
            fade.Add(Act("Fade Out", SasquatchIcons.Ids.X, ColGray,
                () => SpringAlpha(0.15f),
                () => $"spriteRenderer.FadeTo(0.15f, {C()});"));
            fade.Add(Act("Fade In", SasquatchIcons.Ids.Check, ColGreen,
                () => SpringAlpha(1f),
                () => $"spriteRenderer.FadeTo(1f, {C()});"));

            // LOOPS
            var loop = Card(parent, "Loops");
            loop.Add(Act("Pulse (Yoyo)", SasquatchIcons.Ids.RefreshCw, ColYellow, StartYoyo,
                () => $"SpringLoop.Yoyo(key, owner,\n    _ => transform.ScaleTo(1.3f, {C()}),\n    _ => transform.ScaleTo(1f, {C()}));"));
            loop.Add(Act("Spin (Repeat)", SasquatchIcons.Ids.RotateCcw, ColYellow, StartSpin,
                () => $"SpringLoop.Repeat(key, owner,\n    async _ => await transform.RotateTo(transform.rotation * Quaternion.Euler(0, 90, 0), {C()}));"));
            loop.Add(Act("Stop", SasquatchIcons.Ids.X, ColRed,
                () => { SpringLoop.Stop(YoyoKey); SpringLoop.Stop(SpinKey); },
                () => "SpringLoop.Stop(key);"));

            // PRESET FEEL — all forced to the same Duration (0.6) so only the bounce/feel differs, making
            // them directly comparable.
            var feel = Card(parent, "Preset Feel");
            foreach (var p in Presets)
            {
                var preset = p;
                feel.Add(Act(preset.Name, SasquatchIcons.Ids.Play, ColGray,
                    () => { LoadConfig(new SpringConfig(0.6f, preset.B)); Obj().ScaleFromTo(0.6f, 1f, Cfg()); },
                    () => $"transform.ScaleFromTo(0.6f, 1f, new SpringConfig(0.6f, {F(preset.B)}f));"));
            }
        }

        private VisualElement BuildObjectControls()
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-group-content");
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.style.flexWrap = Wrap.Wrap;

            var shapeField = new EnumField("Object", _shape) { tooltip = "The preview mesh/sprite." };
            shapeField.style.minWidth = 170;
            shapeField.RegisterValueChangedCallback(e =>
            {
                _shape = (SpringSandboxShape)e.newValue;
                _meshField.style.display = _shape == SpringSandboxShape.Mesh ? DisplayStyle.Flex : DisplayStyle.None;
                _spriteField.style.display = _shape == SpringSandboxShape.Sprite ? DisplayStyle.Flex : DisplayStyle.None;
                BuildPreviewObject();
                RefreshModeVisibility();
            });
            box.Add(shapeField);

            _wireToggle = MakeRoundedToggle("Wireframe", _wireframe, v => { _wireframe = v; BumpRepaint(); });
            _wireToggle.style.marginLeft = 10;
            box.Add(_wireToggle);

            _meshField = new ObjectField("Mesh") { objectType = typeof(Mesh), allowSceneObjects = false };
            _meshField.style.minWidth = 220; _meshField.style.marginLeft = 6;
            _meshField.style.display = _shape == SpringSandboxShape.Mesh ? DisplayStyle.Flex : DisplayStyle.None;
            _meshField.RegisterValueChangedCallback(e => { _customMesh = e.newValue as Mesh; BuildPreviewObject(); });
            box.Add(_meshField);

            _spriteField = new ObjectField("Sprite") { objectType = typeof(Sprite), allowSceneObjects = false };
            _spriteField.style.minWidth = 220; _spriteField.style.marginLeft = 6;
            _spriteField.style.display = _shape == SpringSandboxShape.Sprite ? DisplayStyle.Flex : DisplayStyle.None;
            _spriteField.RegisterValueChangedCallback(e => { _sprite = e.newValue as Sprite; BuildPreviewObject(); });
            box.Add(_spriteField);

            var tint = new ColorField("Tint") { value = _baseColor };
            tint.style.width = 280; tint.style.marginLeft = 6; tint.style.marginTop = 4;
            tint.RegisterValueChangedCallback(e => { _baseColor = e.newValue; ApplyBaseColor(); BumpRepaint(); });
            box.Add(tint);

            return box;
        }

        private VisualElement BuildTuning()
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-group-content");
            box.style.marginTop = 6;

            var feelLabel = new Label("Feel");
            feelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            feelLabel.style.color = DesignerPalette.SandboxLabel;
            feelLabel.style.marginBottom = 2;
            box.Add(feelLabel);

            var names = new List<string>();
            foreach (var p in Presets) names.Add(p.Name);
            names.Add("Custom");
            _presetDropdown = new DropdownField("Preset", names, MatchPresetIndex());
            _presetDropdown.RegisterValueChangedCallback(e =>
            {
                int idx = names.IndexOf(e.newValue);
                if (idx >= 0 && idx < Presets.Length) LoadConfig(new SpringConfig(Presets[idx].D, Presets[idx].B));
            });
            box.Add(_presetDropdown);

            _durationSlider = new Slider("Duration", 0.05f, 3f) { value = _config.Duration, showInputField = true };
            _durationSlider.RegisterValueChangedCallback(e => { _config.Duration = e.newValue; OnTuningChanged(); });
            box.Add(_durationSlider);

            _bounceSlider = new Slider("Bounce", 0f, 2f) { value = _config.Bounce, showInputField = true };
            _bounceSlider.RegisterValueChangedCallback(e => { _config.Bounce = e.newValue; OnTuningChanged(); });
            box.Add(_bounceSlider);

            // The exact same Painter2D graph the inspector Spring widget uses (smooth, frequency-aware, auto-framed),
            // fed the sandbox's live config instead of a SerializedProperty.
            _graph = new SasquatchSpringControl(() => _config);
            _graph.style.flexGrow = 0; _graph.style.height = 70; _graph.style.marginTop = 4;
            box.Add(_graph);

            return box;
        }

        private VisualElement BuildCodePanel()
        {
            var wrap = new VisualElement();
            wrap.style.marginTop = 6;

            var header = Row();
            var label = new Label("Code");
            label.style.unityFontStyleAndWeight = FontStyle.Bold; label.style.color = DesignerPalette.SandboxLabel;
            label.style.flexGrow = 1;
            header.Add(label);
            header.Add(ToolButton("Copy", () => { EditorGUIUtility.systemCopyBuffer = _snippet; ShowNotification(new GUIContent("Copied snippet")); }));
            header.Add(ToolButton("Copy Config", () => { EditorGUIUtility.systemCopyBuffer = ConfigExpr(); ShowNotification(new GUIContent("Copied SpringConfig")); }));
            wrap.Add(header);

            _codeLabel = new Label { enableRichText = true, text = Highlight(_snippet) };
            _codeLabel.style.whiteSpace = WhiteSpace.Normal;
            _codeLabel.style.minHeight = 56;
            _codeLabel.style.backgroundColor = DesignerPalette.CodeBlockBg;
            _codeLabel.style.color = DesignerPalette.CodeText;
            _codeLabel.style.paddingLeft = 8; _codeLabel.style.paddingRight = 8;
            _codeLabel.style.paddingTop = 6; _codeLabel.style.paddingBottom = 6;
            _codeLabel.style.borderTopLeftRadius = 4; _codeLabel.style.borderTopRightRadius = 4;
            _codeLabel.style.borderBottomLeftRadius = 4; _codeLabel.style.borderBottomRightRadius = 4;
            _codeLabel.style.marginTop = 2;
            CodeHighlighter.ApplyCodeFont(_codeLabel);
            _codeLabel.style.fontSize = 12;
            wrap.Add(_codeLabel);
            return wrap;
        }

        // -------------------------------------------------------- Cards ----

        private VisualElement Card(VisualElement parent, string title) => Card(parent, title, out _);

        private VisualElement Card(VisualElement parent, string title, out VisualElement group)
        {
            group = new VisualElement();
            group.AddToClassList("sasq-group");
            group.style.flexShrink = 0;

            var header = new VisualElement();
            header.AddToClassList("sasq-group-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var chevron = new Label("▾");
            chevron.AddToClassList("sasq-group-chevron");
            header.Add(chevron);
            var titleLbl = new Label(title);
            titleLbl.AddToClassList("sasq-group-title");
            header.Add(titleLbl);
            group.Add(header);

            var content = new VisualElement();
            content.AddToClassList("sasq-group-content");
            content.style.flexDirection = FlexDirection.Row;
            content.style.flexWrap = Wrap.Wrap;
            group.Add(content);

            bool expanded = true;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                expanded = !expanded;
                chevron.text = expanded ? "▾" : "▸"; // ▾ / ▸
                if (EditorSpringFX.MotionEnabled) EditorSpringFX.Fold(content, expanded);
                else content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                evt.StopPropagation();
            });

            parent.Add(group);
            return content;
        }

        // A rounded on/off switch reusing the project's .sq-toggle styling (matches the layout designer's
        // "Rounded" toggle with Show ON/OFF text).
        private static Toggle MakeRoundedToggle(string label, bool initial, Action<bool> onChange)
        {
            var toggle = new Toggle(label) { value = initial };
            toggle.AddToClassList("sq-toggle");
            toggle.AddToClassList("sq-toggle--rounded");
            EditorSpringFX.SpringToggleKnob(toggle);   // the sandbox's own switches ride the house spring too
            if (toggle.labelElement != null) toggle.labelElement.style.color = DesignerPalette.TextPrimary;

            var inputEl = toggle.Q<VisualElement>(className: "unity-toggle__input");
            if (inputEl != null)
            {
                var onOff = new Label(initial ? "ON" : "OFF") { pickingMode = PickingMode.Ignore };
                onOff.AddToClassList("sq-toggle-onoff-label");
                inputEl.Add(onOff);
                toggle.RegisterValueChangedCallback(evt => { onOff.text = evt.newValue ? "ON" : "OFF"; onChange?.Invoke(evt.newValue); });
            }
            else
            {
                toggle.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            }
            return toggle;
        }

        // ---------------------------------------------------------- Preview ----

        private void EnsurePreview()
        {
            if (_preview == null)
            {
                _preview = new PreviewRenderUtility();
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.backgroundColor = new Color(0.13f, 0.13f, 0.15f, 1f);
                _preview.camera.fieldOfView = 30f;
                _preview.camera.nearClipPlane = 0.05f;
                _preview.camera.farClipPlane = 100f;
                // Key+fill+ambient preset, centralised so it scales to physical lux under HDRP (byte-identical
                // elsewhere). The self-lit PreviewLit shader ignores real lights under URP anyway; the scaling
                // only matters when PickShader falls back to HDRP/Lit under HDRP.
                RenderPipelineUtility.ConfigurePreviewLights(_preview, 1.45f, 0.7f, new Color(0.16f, 0.16f, 0.2f, 1f));
            }

            // Build only the active tab's objects, so EnsurePreview (also called every preview frame) never
            // resurrects the single object while the Multiple grid is showing, or vice-versa.
            if (_tab == SandboxTab.Single)
            {
                if (_go == null) BuildPreviewObject();
            }
            else
            {
                if (_multiGo.Count == 0) BuildMultiObjects();
            }
        }

        private void BuildPreviewObject()
        {
            if (_preview == null) { EnsurePreview(); return; }
            DestroyPreviewObject();

            if (_shape == SpringSandboxShape.Sprite)
            {
                _go = new GameObject("SandboxSprite");
                _spriteRenderer = _go.AddComponent<SpriteRenderer>();
                _spriteRenderer.sprite = _sprite;
                _spriteRenderer.color = _baseColor;
            }
            else
            {
                if (_shape == SpringSandboxShape.Mesh && _customMesh != null)
                {
                    _go = new GameObject("SandboxMesh");
                    _go.AddComponent<MeshFilter>().sharedMesh = _customMesh;
                    _go.AddComponent<MeshRenderer>();
                }
                else
                {
                    _go = GameObject.CreatePrimitive(ToPrimitive(_shape));
                    StripNonRenderComponents(_go); // drop the primitive's auto-added collider (module-free)
                }
                var previewShader = PickShader() ?? Shader.Find("Sprites/Default"); // last-resort guard
                _material = new Material(previewShader) { hideFlags = HideFlags.HideAndDontSave };
                ConfigureMaterial(_material);
                SetPreviewLightUniforms(_material);
                ApplyBaseColor();
                _go.GetComponent<MeshRenderer>().sharedMaterial = _material;
            }

            _go.hideFlags = HideFlags.HideAndDontSave;
            _go.transform.position = _home;
            _go.transform.rotation = Quaternion.identity;
            _go.transform.localScale = Vector3.one;
            _preview.AddSingleGO(_go);
        }

        private void DestroyPreviewObject()
        {
            SpringLoop.Stop(YoyoKey);
            SpringLoop.Stop(SpinKey);
            if (_go != null) { DestroyImmediate(_go); _go = null; }
            if (_material != null) { DestroyImmediate(_material); _material = null; }
            _spriteRenderer = null;
        }

        // ---------------------------------------------------------- Multiple (grid) ----

        private void BuildMultiActionGroups(VisualElement parent)
        {
            var info = new Label("Animate a grid of objects in sequence with a small time gap between each — the " +
                "spring library's Stagger helper. Flick an action and watch it ripple across the grid; the code " +
                "panel shows the real call.");
            info.style.whiteSpace = WhiteSpace.Normal;
            info.style.fontSize = 11;
            info.style.color = DesignerPalette.InfoDim;
            info.style.marginTop = 4; info.style.marginBottom = 4;
            parent.Add(info);

            var stagger = Card(parent, "Stagger");
            stagger.Add(MultiAct("Punch", SasquatchIcons.Ids.Zap, ColYellow, StaggerPunch, StaggerPunchCode));
            stagger.Add(MultiAct("Pop In", SasquatchIcons.Ids.Sparkles, ColGreen, StaggerPopIn, StaggerPopInCode));
            stagger.Add(MultiAct("Hop", SasquatchIcons.Ids.ArrowUp, ColBlue, StaggerHop, StaggerHopCode));
            stagger.Add(MultiAct("Reset", SasquatchIcons.Ids.RefreshCw, ColGray, ResetMultiObjects,
                () => "// Restore each item's resting scale, then Snap to stop any in-flight spring\n" +
                      "foreach (var item in items)\n{\n    item.localScale = Vector3.one * 0.4f;\n    item.Snap();\n}"));

            var gapBox = new VisualElement();
            gapBox.AddToClassList("sasq-group-content");
            gapBox.style.marginTop = 6;
            var gapLbl = new Label("Stagger Gap");
            gapLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            gapLbl.style.color = DesignerPalette.SandboxLabel;
            gapLbl.style.marginBottom = 2;
            gapBox.Add(gapLbl);
            _gapSlider = new Slider("Gap (s)", 0f, 0.2f) { value = _staggerGap, showInputField = true };
            _gapSlider.RegisterValueChangedCallback(e => _staggerGap = e.newValue);
            gapBox.Add(_gapSlider);
            parent.Add(gapBox);
        }

        // Like Act, but guards on the grid (Obj() is null in Multiple mode, which would block Act's clicks).
        private Button MultiAct(string label, string iconId, Color bg, Action run, Func<string> code)
        {
            var b = new Button();
            if (!string.IsNullOrEmpty(iconId))
            {
                var ic = SasquatchIcons.Create(iconId, 12);
                ic.style.marginRight = 4;
                b.Add(ic);
            }
            b.Add(new Label(label) { pickingMode = PickingMode.Ignore });
            StyleActButton(b, bg);
            b.RegisterCallback<PointerEnterEvent>(_ => b.style.backgroundColor = Lighten(bg, 0.13f));
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = bg);
            b.clicked += () =>
            {
                EnsurePreview();
                if (_multiGo.Count == 0) return;
                run();
                if (code != null) SetSnippet(code());
                SelectButton(b);
                BumpRepaint();
                if (EditorSpringFX.MotionEnabled) EditorSpringFX.PressBounce(b);
            };
            return b;
        }

        // Editor-safe stagger: the runtime SpringStagger needs a MonoBehaviour owner + Task.Delay (it can't run in
        // editor chrome), so we space the per-item START times with the UI Toolkit scheduler instead. The actual
        // spring motion is advanced by SpringEditorDriver; we only keep repainting through the whole sequence.
        private void RunStagger(Action<int> perItem)
        {
            int n = _multiGo.Count;
            if (n == 0) return;
            int gapMs = Mathf.Max(0, Mathf.RoundToInt(_staggerGap * 1000f));
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                _previewIMGUI.schedule.Execute(() =>
                {
                    if (idx < _multiGo.Count && _multiGo[idx] != null) perItem(idx);
                }).StartingIn(idx * gapMs);
            }
            BumpRepaintFor(n * _staggerGap + Mathf.Max(0.8f, _config.Duration) + 1.0);
        }

        private void StaggerPopIn() => RunStagger(i => _multiGo[i].transform.ScaleFromTo(0f, MultiRest, Cfg()));

        private void StaggerHop() => RunStagger(i =>
        {
            var t = _multiGo[i].transform;
            // Velocity scaled by frequency (like the single-object Hop) so the hop height reads across configs.
            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, Cfg().Frequency);
            Spring.Move(t, MultiHome(i), Cfg()).Punch(Vector3.up * (0.4f * omega * 2.718f), Cfg());
        });

        private void StaggerPunch() => RunStagger(i => _multiGo[i].transform.PunchScale(new Vector3(0.4f, 0.4f, 0.4f), Cfg()));

        private string StaggerPopInCode() =>
            $"// Pop every item in, {F(_staggerGap)}s apart\n" +
            $"await items.Stagger(this, {F(_staggerGap)}f, async (item, i) =>\n" +
            "{\n    item.localScale = Vector3.zero;\n" +
            $"    await item.ScaleTo(0.4f, {C()});\n}});";

        private string StaggerHopCode() =>
            "// Each item gets an upward impulse and springs back home, staggered\n" +
            $"await items.Stagger(this, {F(_staggerGap)}f, (item, i) =>\n" +
            "{\n" +
            $"    Spring.Move(item, item.position, {C()}).Punch(Vector3.up * 15f, {C()});\n" +
            "    return Task.CompletedTask;\n});";

        private string StaggerPunchCode() =>
            $"// Punch the scale of each item, staggered\n" +
            $"await items.Stagger(this, {F(_staggerGap)}f, async (item, i) =>\n" +
            $"    await item.PunchScale(new Vector3(0.4f, 0.4f, 0.4f), {C()}));";

        private static Vector3 MultiHome(int i)
        {
            int cx = i % MultiCols, cy = i / MultiCols;
            float x0 = -(MultiCols - 1) * 0.5f * MultiSpacing;
            float yTop = (MultiRows - 1) * 0.5f * MultiRowSpacing; // index 0 = TOP row; cy increases downward
            return new Vector3(x0 + cx * MultiSpacing, yTop - cy * MultiRowSpacing, 0f);
        }

        private void BuildMultiObjects()
        {
            if (_preview == null) return;
            DestroyMultiObjects();

            int count = MultiCols * MultiRows;
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                StripNonRenderComponents(go); // drop the primitive's auto-added collider (module-free)

                var gridShader = PickShader() ?? Shader.Find("Sprites/Default"); // last-resort guard
                var mat = new Material(gridShader) { hideFlags = HideFlags.HideAndDontSave };
                ConfigureMaterial(mat);
                SetPreviewLightUniforms(mat);
                mat.SetColor(_colorProp, Color.HSVToRGB(i / (float)count, 0.5f, 0.95f)); // gentle hue spread
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;

                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.position = MultiHome(i);
                go.transform.localScale = Vector3.one * MultiRest;
                go.transform.rotation = Quaternion.identity;
                _preview.AddSingleGO(go);
                _multiGo.Add(go);
                _multiMats.Add(mat);
            }
        }

        private void DestroyMultiObjects()
        {
            for (int i = 0; i < _multiGo.Count; i++)
            {
                var go = _multiGo[i];
                if (go == null) continue;
                Spring.CancelAll(go.transform);
                Spring.CancelAll(go);
                DestroyImmediate(go);
            }
            _multiGo.Clear();
            for (int i = 0; i < _multiMats.Count; i++)
                if (_multiMats[i] != null) DestroyImmediate(_multiMats[i]);
            _multiMats.Clear();
        }

        private void ResetMultiObjects()
        {
            _camPivot = Vector3.zero; _camEuler = new Vector2(15f, 20f); _camDistance = 11f;
            int count = Mathf.Max(1, _multiGo.Count);
            for (int i = 0; i < _multiGo.Count; i++)
            {
                var go = _multiGo[i];
                if (go == null) continue;
                go.transform.position = MultiHome(i);
                go.transform.localScale = Vector3.one * MultiRest;
                go.transform.rotation = Quaternion.identity;
                if (i < _multiMats.Count && _multiMats[i] != null)
                    _multiMats[i].SetColor(_colorProp, Color.HSVToRGB(i / (float)count, 0.5f, 0.95f));
                go.transform.Snap();
            }
            BumpRepaint();
        }

        private void BumpRepaintFor(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            if (until > _repaintUntil) _repaintUntil = until;
        }

        private void OnPreviewGUI()
        {
            EnsurePreview();
            var rect = new Rect(0, 0, _previewIMGUI.contentRect.width, _previewIMGUI.contentRect.height);
            if (rect.width < 4 || rect.height < 4) return;

            HandlePreviewMouse(rect);

            if (Event.current.type != EventType.Repaint) return;

            FrameCamera();
            // Feed the self-lit PreviewLit shader its light uniforms each frame (the key fix for the flat/"sprite"
            // look under URP — incl. the 2D Renderer, which does no 3D lighting). PickShader chooses PreviewLit
            // under URP; under Built-in/HDRP the native lit shader lights itself and this is a no-op.
            SetPreviewLightUniforms(_material);
            for (int i = 0; i < _multiMats.Count; i++) SetPreviewLightUniforms(_multiMats[i]);
            _preview.BeginPreview(rect, GUIStyle.none);
            // Solid grid in Multiple mode reads better than wireframe spheres.
            bool wire = _tab == SandboxTab.Single && _wireframe && !SpriteMode();
            if (wire) GL.wireframe = true;
            _preview.camera.Render();
            if (wire) GL.wireframe = false;
            var tex = _preview.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
        }

        // Feeds the self-lit PreviewLit shader an explicit light direction/color + ambient from the preview
        // utility's own key light, so the mesh shades correctly regardless of what the active pipeline does with
        // scene lights. No-op for materials that don't expose _PreviewKeyDir (Standard / URP-Lit / HDRP-Lit,
        // which light themselves), and for sprite mode (no material).
        private void SetPreviewLightUniforms(Material m)
        {
            if (m == null || !m.HasProperty("_PreviewKeyDir")) return;

            // "Key + Fill + Rim" preset: fixed world-space lights (the camera orbits around them), a soft cool
            // fill, low ambient, and a subtle cool rim so the silhouette reads against the dark background.
            // Self-lit from these uniforms, so it shades correctly under any pipeline (incl. the URP 2D Renderer).
            m.SetVector("_PreviewKeyDir", new Vector3(0.32f, 0.55f, -0.77f).normalized);
            m.SetColor("_PreviewKeyColor", Color.white);
            m.SetVector("_PreviewFillDir", new Vector3(-0.5f, 0.1f, 0.6f).normalized);
            m.SetColor("_PreviewFillColor", new Color(0.22f, 0.24f, 0.32f, 1f));
            m.SetColor("_PreviewAmbient", new Color(0.13f, 0.13f, 0.16f, 1f));
            m.SetColor("_PreviewRimColor", new Color(0.16f, 0.22f, 0.30f, 1f));
            m.SetFloat("_PreviewRimPower", 2.5f);
        }

        private void FrameCamera()
        {
            var cam = _preview.camera;
            if (_is2D)
            {
                cam.orthographic = true;
                // Match the perspective view's half-height at the pivot (distance * tan(fov/2)) so swapping to 2D
                // doesn't shrink the objects — keeps the scale consistent between 2D and 3D.
                cam.orthographicSize = Mathf.Max(0.2f, _camDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                cam.transform.rotation = Quaternion.identity;
                cam.transform.position = _camPivot + new Vector3(0, 0, -10f);
            }
            else
            {
                cam.orthographic = false;
                var rot = Quaternion.Euler(_camEuler.x, _camEuler.y, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = _camPivot + rot * new Vector3(0, 0, -_camDistance);
            }
        }

        private void HandlePreviewMouse(Rect rect)
        {
            int id = GUIUtility.GetControlID(s_dragHash, FocusType.Passive, rect);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition) && (e.button == 0 || e.button == 2))
                    {
                        GUIUtility.hotControl = id;
                        _lastDrag = e.mousePosition; _dragMoved = false;
                        if (e.button == 2) _dragMode = DragMode.Pan;
                        else if (e.alt && !_is2D) _dragMode = DragMode.Orbit;
                        else { _dragMode = DragMode.Object; Obj()?.Snap(); }
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id && _dragMode != DragMode.None)
                    {
                        Vector2 d = e.mousePosition - _lastDrag; _lastDrag = e.mousePosition;
                        var cam = _preview.camera.transform;
                        switch (_dragMode)
                        {
                            case DragMode.Orbit:
                                _camEuler.y += d.x * 0.4f;
                                _camEuler.x = Mathf.Clamp(_camEuler.x + d.y * 0.4f, -85f, 85f);
                                break;
                            case DragMode.Pan:
                                float ps = _camDistance * 0.0022f;
                                _camPivot += -cam.right * (d.x * ps) + cam.up * (d.y * ps);
                                break;
                            case DragMode.Object:
                                if (_go != null)
                                {
                                    float s = _camDistance * 0.0016f;
                                    _go.transform.position += cam.right * (d.x * s) - cam.up * (d.y * s);
                                    _dragMoved = true;
                                }
                                break;
                        }
                        Repaint(); e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        if (_dragMode == DragMode.Object && _dragMoved && _go != null)
                        {
                            Obj().MoveTo(_home, Cfg());
                            SetSnippet($"// Released — springs back home\ntransform.MoveTo(Vector3.zero, {C()});");
                            BumpRepaint();
                        }
                        _dragMode = DragMode.None;
                        e.Use();
                    }
                    break;
            }

            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                _camDistance = Mathf.Clamp(_camDistance + e.delta.y * 0.1f, 1.2f, 14f);
                Repaint(); e.Use();
            }
        }

        // ---------------------------------------------------------- Springs ----

        private Transform Obj() { EnsurePreview(); return _go != null ? _go.transform : null; }
        private SpringConfig Cfg() => _config;
        private bool SpriteMode() => _shape == SpringSandboxShape.Sprite;

        // Spring.To (not the .ColorTo extension) — .ColorTo's overload set drags in Light2D/TMP_Text
        // variants this editor assembly doesn't reference (CS0012). Snippets still show the idiomatic call.
        private void SpringColor(Color c)
        {
            if (SpriteMode())
            {
                if (_spriteRenderer != null)
                    Spring.To(_spriteRenderer, () => _spriteRenderer.color, col => _spriteRenderer.color = col, c, Cfg(), "sprColor");
            }
            else if (_material != null)
            {
                Spring.To(Obj(), () => _material.GetColor(_colorProp), col => _material.SetColor(_colorProp, col), c, Cfg(), "matColor");
            }
        }

        private void SpringAlpha(float a)
        {
            if (_spriteRenderer != null) _spriteRenderer.FadeTo(a, Cfg());
        }

        private void StartYoyo()
        {
            var owner = EnsureAgent(); if (owner == null) return;
            SpringLoop.Yoyo(YoyoKey, owner, _ => Obj().ScaleTo(1.3f, Cfg()), _ => Obj().ScaleTo(1f, Cfg()));
            BumpRepaint();
        }

        private void StartSpin()
        {
            var owner = EnsureAgent(); if (owner == null) return;
            SpringLoop.Repeat(SpinKey, owner, async _ => await Obj().RotateTo(Obj().rotation * Quaternion.Euler(0, 90, 0), Cfg()));
            BumpRepaint();
        }

        private MotionAgent EnsureAgent()
        {
            var t = Obj();
            if (t == null) return null;
            var a = t.GetComponent<MotionAgent>();
            if (a == null) a = t.gameObject.AddComponent<MotionAgent>();
            return a;
        }

        private void ResetObject()
        {
            if (_tab == SandboxTab.Multiple) { ResetMultiObjects(); return; }

            SpringLoop.Stop(YoyoKey); SpringLoop.Stop(SpinKey);
            _grownUp = false;
            if (_growBtn?.Q<Label>() is Label gl) gl.text = "Grow Up";
            _camPivot = Vector3.zero; _camEuler = new Vector2(15f, 20f); _camDistance = 4f;
            var t = Obj();
            if (t != null)
            {
                // Write the reset state FIRST, then Snap so every live spring adopts these values.
                // Snapping before the writes left the position/scale/color handles active with their
                // pre-reset targets, and the next engine tick overwrote the reset (then unregistered) —
                // which is why the first press appeared to do nothing and a second was needed.
                t.position = _home; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
                ApplyBaseColor();
                t.Snap();
            }
            BumpRepaint();
        }

        private void Toggle2D()
        {
            _is2D = !_is2D;
            _2dButton.style.backgroundColor = _is2D ? (StyleColor)DesignerPalette.AccentFill : new StyleColor(StyleKeyword.Null);
            BumpRepaint();
        }

        private void LoadConfig(SpringConfig c)
        {
            _config = c;
            SyncTuningUI();
            _graph?.MarkDirtyRepaint();
        }

        // ---------------------------------------------------------- Material ----

        // Pipeline-aware shader pick so the preview is properly 3D-shaded everywhere. Every pipeline prefers the
        // self-lit PreviewLit shader (hand-fed key/fill/ambient uniforms → the SAME shading under URP incl. the
        // 2D Renderer, Built-in, and HDRP; PreviewLit's untagged HDRP pass is drawn as SRPDefaultUnlit, which
        // HDRP's forward loop supports). Native lit shaders are fallbacks only — they read differently per
        // pipeline (washed-out Standard under Built-in, near-black HDRP/Lit under auto-exposure). All branches
        // expose _BaseColor or _Color (ConfigureMaterial probes both), so the color plumbing is unchanged.
        private static Shader PickShader()
        {
            switch (RenderPipelineUtility.Detect())
            {
                case RenderPipelineUtility.PipelineKind.Urp2D:
                case RenderPipelineUtility.PipelineKind.Urp3D:
                    return Shader.Find("Hidden/Sasquatch/PreviewLit")
                        ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                case RenderPipelineUtility.PipelineKind.Hdrp:
                    return RenderPipelineUtility.PickHdrpPreviewShader();
                default: // Built-in
                    return Shader.Find("Hidden/Sasquatch/PreviewLit")
                        ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            }
        }

        private void ConfigureMaterial(Material m)
        {
            _colorProp = m.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
        }

        private void ApplyBaseColor()
        {
            if (SpriteMode()) { if (_spriteRenderer != null) _spriteRenderer.color = _baseColor; }
            else if (_material != null) _material.SetColor(_colorProp, _baseColor);
        }

        // ------------------------------------------------------- Tuning + graph ----

        private void OnTuningChanged()
        {
            _presetDropdown?.SetValueWithoutNotify(MatchPresetIndex() < Presets.Length ? Presets[MatchPresetIndex()].Name : "Custom");
            _graph?.MarkDirtyRepaint();
            BumpRepaint();
        }

        private void SyncTuningUI()
        {
            _durationSlider?.SetValueWithoutNotify(_config.Duration);
            _bounceSlider?.SetValueWithoutNotify(_config.Bounce);
            int idx = MatchPresetIndex();
            _presetDropdown?.SetValueWithoutNotify(idx < Presets.Length ? Presets[idx].Name : "Custom");
        }

        private int MatchPresetIndex()
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Mathf.Approximately(Presets[i].D, _config.Duration) && Mathf.Approximately(Presets[i].B, _config.Bounce))
                    return i;
            return Presets.Length; // Custom
        }

        // ------------------------------------------------------- Code snippet ----

        private void SetSnippet(string s)
        {
            _snippet = s; // raw text for the clipboard
            if (_codeLabel != null) _codeLabel.text = Highlight(s);
        }

        // ------------------------------------------------------- Syntax highlight ----

        private static readonly HashSet<string> s_codeTypes = new HashSet<string>
        {
            "SpringConfig", "Vector3", "Vector2", "Quaternion", "Color", "Spring", "SpringLoop", "SpringPivot", "AwaitStrategy"
        };
        private static readonly HashSet<string> s_codeKeywords = new HashSet<string> { "new", "var", "await", "async" };

        // Delegates to the shared CodeHighlighter, passing the spring-specific type/keyword sets and keeping
        // the terse-snippet behaviour (no auto-teal for bare Capitalized words).
        private static string Highlight(string code) =>
            CodeHighlighter.Highlight(code, s_codeTypes, s_codeKeywords, capitalizedAsType: false);

        private string C() => ConfigExpr();

        private string ConfigExpr()
        {
            var c = _config;
            string preset = PresetName(c.Duration, c.Bounce);
            bool extras = !Mathf.Approximately(c.ResolvedFrequencyScale, 1f) || c.SettleEpsilon > 0f || c.Unscaled;
            if (preset != null && !extras) return "SpringConfig." + preset;

            string s = $"new SpringConfig({F(c.Duration)}f, {F(c.Bounce)}f)";
            var parts = new List<string>();
            if (!Mathf.Approximately(c.ResolvedFrequencyScale, 1f)) parts.Add($"FrequencyScale = {F(c.ResolvedFrequencyScale)}f");
            if (c.SettleEpsilon > 0f) parts.Add($"SettleEpsilon = {F(c.SettleEpsilon)}f");
            if (c.Unscaled) parts.Add("Unscaled = true");
            if (parts.Count > 0) s += " { " + string.Join(", ", parts) + " }";
            return s;
        }

        private static string ColorExpr(Color c) => $"new Color({F(c.r)}f, {F(c.g)}f, {F(c.b)}f)";
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static Color RandomVivid() => Color.HSVToRGB(UnityEngine.Random.value, 0.7f, 0.95f);

        // -------------------------------------------------------- Presets ----

        private struct Preset { public string Name; public float D; public float B; }
        private static readonly Preset[] Presets =
        {
            new Preset{Name="Default",D=0.6f,B=0.25f}, new Preset{Name="Snappy",D=0.3f,B=0f},
            new Preset{Name="Bouncy",D=0.6f,B=0.6f},  new Preset{Name="Boing",D=0.8f,B=1.3f},
            new Preset{Name="Glide",D=1.2f,B=0.5f},   new Preset{Name="Heavy",D=1.5f,B=0f},
            new Preset{Name="Direct",D=0.15f,B=0f},   new Preset{Name="Soft",D=1.0f,B=0.1f},
        };

        private static string PresetName(float d, float b)
        {
            foreach (var p in Presets)
                if (Mathf.Approximately(p.D, d) && Mathf.Approximately(p.B, b)) return p.Name;
            return null;
        }

        private static PrimitiveType ToPrimitive(SpringSandboxShape s)
        {
            switch (s)
            {
                case SpringSandboxShape.Cube: return PrimitiveType.Cube;
                case SpringSandboxShape.Capsule: return PrimitiveType.Capsule;
                case SpringSandboxShape.Quad: return PrimitiveType.Quad;
                default: return PrimitiveType.Sphere;
            }
        }

        // A preview-scene object only needs its render components, and CreatePrimitive also attaches a
        // Collider we do not want in a PreviewRenderUtility scene. Naming `Collider` would bind this whole
        // Editor assembly to com.unity.modules.physics, a REMOVABLE built-in module (2D-only projects trim
        // it routinely), so strip by ALLOW-LIST instead: a fresh primitive carries exactly Transform +
        // MeshFilter + one Collider + MeshRenderer, and all three kept types live in the non-removable
        // CoreModule. TestPrimitiveColliderStripAllowListPremise pins that premise across editor versions.
        private static void StripNonRenderComponents(GameObject go)
        {
            foreach (var comp in go.GetComponents<Component>())
                if (comp != null && !(comp is Transform) && !(comp is MeshFilter) && !(comp is Renderer))
                    DestroyImmediate(comp);
        }

        // ------------------------------------------------------- UI helpers ----

        private static readonly Color ColBlue = new Color(0.20f, 0.28f, 0.38f);
        private static readonly Color ColGreen = new Color(0.20f, 0.35f, 0.20f);
        private static readonly Color ColPurple = new Color(0.40f, 0.25f, 0.45f);
        private static readonly Color ColYellow = new Color(0.35f, 0.32f, 0.15f);
        private static readonly Color ColGray = new Color(0.25f, 0.25f, 0.25f);
        private static readonly Color ColRed = new Color(0.48f, 0.18f, 0.18f);

        private static VisualElement Row()
        {
            var v = new VisualElement();
            v.style.flexDirection = FlexDirection.Row;
            v.style.alignItems = Align.Center;
            return v;
        }

        private Button Act(string label, string iconId, Color bg, Action run, Func<string> code)
        {
            var b = new Button();
            if (!string.IsNullOrEmpty(iconId))
            {
                var ic = SasquatchIcons.Create(iconId, 12);
                ic.style.marginRight = 4;
                b.Add(ic);
            }
            b.Add(new Label(label) { pickingMode = PickingMode.Ignore });
            StyleActButton(b, bg);
            // Hover: lighten the fill so it's obvious the cursor is over it (the selection outline persists).
            b.RegisterCallback<PointerEnterEvent>(_ => b.style.backgroundColor = Lighten(bg, 0.13f));
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = bg);
            b.clicked += () =>
            {
                if (Obj() == null) return;
                run();
                if (code != null) SetSnippet(code());
                SelectButton(b);
                BumpRepaint();
                if (EditorSpringFX.MotionEnabled) EditorSpringFX.PressBounce(b);
            };
            return b;
        }

        // Outlines the active button (which action is showing code). The border is always 2px (transparent
        // when unselected) so toggling it never nudges the layout.
        private void SelectButton(Button b)
        {
            if (_selectedBtn != null && _selectedBtn != b) SetOutline(_selectedBtn, false);
            _selectedBtn = b;
            SetOutline(b, true);
        }

        private static void SetOutline(Button b, bool on)
        {
            var c = on ? DesignerPalette.SandboxGlow : new Color(0, 0, 0, 0);
            b.style.borderTopColor = c; b.style.borderBottomColor = c; b.style.borderLeftColor = c; b.style.borderRightColor = c;
        }

        private static Color Lighten(Color c, float amt) =>
            new Color(Mathf.Min(1f, c.r + amt), Mathf.Min(1f, c.g + amt), Mathf.Min(1f, c.b + amt), c.a);

        private static void StyleActButton(Button b, Color bg)
        {
            b.style.flexDirection = FlexDirection.Row;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            b.style.marginTop = 2; b.style.marginBottom = 2; b.style.marginLeft = 2; b.style.marginRight = 2;
            b.style.paddingLeft = 7; b.style.paddingRight = 7; b.style.paddingTop = 3; b.style.paddingBottom = 3;
            b.style.backgroundColor = bg; b.style.color = Color.white;
            // Always-present 2px border (transparent until selected) so the selection outline doesn't resize it.
            b.style.borderTopWidth = 2; b.style.borderBottomWidth = 2; b.style.borderLeftWidth = 2; b.style.borderRightWidth = 2;
            var clear = new Color(0, 0, 0, 0);
            b.style.borderTopColor = clear; b.style.borderBottomColor = clear; b.style.borderLeftColor = clear; b.style.borderRightColor = clear;
            b.style.borderTopLeftRadius = 4; b.style.borderTopRightRadius = 4; b.style.borderBottomLeftRadius = 4; b.style.borderBottomRightRadius = 4;
            b.style.fontSize = 11;
        }

        private Button ToolButton(string label, Action onClick)
        {
            var b = new Button(onClick) { text = label };
            b.style.marginRight = 4;
            return b;
        }

    }
}
