using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using SasquatchInspector.Showcase;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// An onboarding "Showcase Sandbox" for Sasquatch Inspector. The left sidebar lists feature categories as
    /// collapsible groups; each parent shows an all-in-one page and its children drill into one feature each.
    /// Selecting one renders a fully-interactive live inspector (right-click any field to Quick-Edit it),
    /// alongside the demo type's real C# source and a short note. Every demo target, layout, and theme is
    /// created purely in-memory (no project files); "Open in Designer" hands an editable copy to the real
    /// Layout Designer.
    /// </summary>
    public sealed class SasquatchShowcaseWindow : EditorWindow
    {
        private sealed class Demo
        {
            public string Title;
            public string Key;   // stable per-page id for persistent overrides (defaults to Title)
            public string Notes;
            public string CodeDescription; // shown instead of C# source when the page references no members
            public string CodeOverride;    // when set, shown verbatim as "C# Source" (copyable), bypassing auto-extraction
            public Func<List<UnityEngine.Object>, ScriptableObject> CreateTarget;
            public Func<LayoutNode> BuildLayout;
            public Func<List<UnityEngine.Object>, SasquatchThemeAsset> BuildTheme; // nullable
            public bool IsAnimations;
        }

        private sealed class CategoryGroup
        {
            public Demo Parent;
            public readonly List<Demo> Children = new List<Demo>();
        }

        private List<CategoryGroup> _groups;
        private Demo _current;

        // Live (currently-shown) transient objects — destroyed when switching demo or closing the window.
        private readonly List<UnityEngine.Object> _liveObjects = new List<UnityEngine.Object>();
        private ScriptableObject _target;
        private InspectorLayoutAsset _layoutAsset;
        private SasquatchThemeAsset _theme;
        private SerializedObject _serializedObject;
        private bool _appearNext;
        private bool _debugConditions;

        // UI refs.
        private readonly List<(Demo demo, Button btn)> _demoButtons = new List<(Demo, Button)>();
        private VisualElement _previewContainer;

        // Wiggles the shown object's values and samples the recorder, so an armed Live Debug page draws a real
        // moving trace in Edit Mode instead of four flat lines. Installed on the window root, so it survives
        // page changes and reads whichever SerializedObject is current.
        private ShowcaseLiveDebugDriver _debugDriver;
        private Label _codeLabel;
        private Label _codeDescription;       // shown in place of the code when a page has no members
        private Label _codeSectionLabel;      // "C# Source" / "How it's added"
        private Button _copyButton;
        private string _codePlain;            // un-highlighted snippet for the copy button
        private Label _notesLabel;
        private Label _headerTitle;
        private Button _replayButton;
        private VisualElement _tryConditionsCard;   // "try the Condition Debugger" hint, shown only on condition pages

        // Set when the user opens the current page in the Designer, so the next time this window regains focus
        // (e.g. after saving there) we rebuild that page to pick up its persisted override — without disturbing
        // in-preview quick-edits on ordinary focus changes.
        private Demo _reloadOnFocus;

        /// <summary>Opens the Showcase Sandbox window.</summary>
        [MenuItem("Tools/Sasquatch Inspector/Showcase Sandbox", false, 12)]
        public static void Open()
        {
            var window = GetWindow<SasquatchShowcaseWindow>("Showcase Sandbox");
            // Wide enough that the preview pane matches a comfortably-docked inspector — a 5-column table
            // (e.g. the Large Inventory demo) then fits with column-resize slack instead of overflowing.
            window.minSize = new Vector2(1100f, 580f);
            window.Show();
        }

        private void CreateGUI()
        {
            _groups = BuildGroups();

            var root = rootVisualElement;
            var sheet = SasquatchPaths.LoadStyleSheet("SasquatchWidgets");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            // Window CHROME follows the EDITOR skin: without the token sheet the sasq-* classes
            // (title/hint) resolve their dark fallbacks — unreadable on a light editor. The demo
            // previews inside re-scope the variables on their own build roots per the resolved theme.
            SasquatchSkin.Apply(root, !EditorGUIUtility.isProSkin);

            // Header.
            var header = new VisualElement();
            header.style.paddingLeft = 12; header.style.paddingRight = 12;
            header.style.paddingTop = 8; header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0f, 0f, 0f, 0.35f);
            var title = new Label("Showcase Sandbox");
            title.AddToClassList("sasq-palettewin-title");
            header.Add(title);
            var hint = new Label("Pick a category — expand it for individual features. Everything is live: drag, type, reorder, right-click to Quick-Edit, then open it in the Designer.");
            hint.AddToClassList("sasq-palettewin-hint");
            hint.style.whiteSpace = WhiteSpace.Normal;
            header.Add(hint);
            root.Add(header);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            root.Add(body);

            body.Add(BuildSidebar());
            body.Add(BuildCenter());
            body.Add(BuildInfoPane());

            // Give the embedded ShowcaseStatBlock (Media ▸ In-Line Editor) a custom inline design via a
            // session-only registry override — cache-only, so it never touches the real inspector database.
            LayoutRegistry.RegisterSessionOverride(typeof(ShowcaseStatBlock), ShowcaseLayouts.StatBlock());

            _debugDriver = new ShowcaseLiveDebugDriver(() => _serializedObject);
            _debugDriver.Install(root);

            // Refresh instantly when a designer save lands on the page we're showing (no need to click back here).
            ShowcaseOverrides.OverrideSaved -= OnOverrideSaved;
            ShowcaseOverrides.OverrideSaved += OnOverrideSaved;

            SelectMakeDemo(_groups[0].Parent);
        }

        private void OnDisable()
        {
            ShowcaseOverrides.OverrideSaved -= OnOverrideSaved;
            LayoutRegistry.ClearSessionOverride(typeof(ShowcaseStatBlock));
            // Before DisposeLive: the driver holds a global chrome flag (DebugSampleClock.SandboxType) that
            // must not outlive the window, or every inspector in the editor would keep claiming a clock is
            // running for that type.
            DisarmLiveDebug();
            _debugDriver?.Dispose();
            _debugDriver = null;
            DisposeLive();
        }

        // A designer save persisted an override — if it's for the page on screen, rebuild it now so the change
        // shows instantly (while the designer is still focused), not only when this window regains focus.
        private void OnOverrideSaved(string key)
        {
            if (_current == null || _previewContainer == null || _current.Key != key) return;
            _reloadOnFocus = null; // refreshing now; don't double-refresh on next focus
            SelectMakeDemo(_current);
        }

        private void OnFocus()
        {
            // Fallback for the override path above (e.g. if the event was missed across a domain reload): after
            // opening a page in the Designer and saving, returning focus here reloads that page. Guarded so it
            // only fires for the just-opened page (not every focus).
            if (_reloadOnFocus == null || _previewContainer == null) return;
            var demo = _reloadOnFocus;
            _reloadOnFocus = null;
            SelectMakeDemo(demo);
        }

        // ----------------------------------------------------------------- Panels ----

        private VisualElement BuildSidebar()
        {
            var sidebar = new ScrollView(ScrollViewMode.Vertical);
            sidebar.style.width = 210;
            sidebar.style.flexShrink = 0;
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = new Color(0f, 0f, 0f, 0.35f);
            sidebar.style.paddingTop = 6; sidebar.style.paddingBottom = 6;
            sidebar.style.paddingLeft = 6; sidebar.style.paddingRight = 6;

            foreach (var group in _groups) BuildGroupUI(group, sidebar);
            return sidebar;
        }

        private void BuildGroupUI(CategoryGroup group, VisualElement sidebar)
        {
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginTop = 4;

            VisualElement childrenBox = null;
            VisualElement chevron = null;
            if (group.Children.Count > 0)
            {
                // A single chevron icon that spins 0°↔90° (spring) on toggle — matching the foldouts/cards.
                chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, DesignerPalette.IconMuted);
                chevron.style.width = 16;
                chevron.style.flexShrink = 0;
                chevron.pickingMode = PickingMode.Position; // icons ignore picking by default; make it clickable
                headerRow.Add(chevron);
            }
            else
            {
                var spacer = new VisualElement { style = { width = 16 } };
                headerRow.Add(spacer);
            }

            var parentBtn = MakeDemoButton(group.Parent);
            parentBtn.style.flexGrow = 1;
            parentBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerRow.Add(parentBtn);
            sidebar.Add(headerRow);

            if (group.Children.Count > 0)
            {
                childrenBox = new VisualElement();
                // Indent the children a touch more than the parent's chevron column, with a faint guide line, so
                // they clearly read as belonging to the parent folder.
                childrenBox.style.marginLeft = 26;
                childrenBox.style.paddingLeft = 4;
                childrenBox.style.borderLeftWidth = 1;
                childrenBox.style.borderLeftColor = DesignerPalette.Ink(0.08f);
                childrenBox.style.display = DisplayStyle.None; // every group starts collapsed
                foreach (var child in group.Children)
                {
                    var cb = MakeDemoButton(child);
                    cb.style.height = 24;
                    cb.style.fontSize = 11;
                    childrenBox.Add(cb);
                }
                sidebar.Add(childrenBox);

                bool expanded = false;
                EditorSpringFX.SpringCaret(chevron, false, animate: false); // seed the caret at its collapsed angle
                var caret = chevron;
                var box = childrenBox;
                chevron.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    expanded = !expanded;
                    EditorSpringFX.SpringCaret(caret, expanded);   // spin the caret with a little bounce
                    EditorSpringFX.Fold(box, expanded);            // spring the children open/closed
                    evt.StopPropagation();
                });
            }
        }

        private Button MakeDemoButton(Demo demo)
        {
            var btn = new Button(() => SelectMakeDemo(demo)) { text = demo.Title };
            btn.style.marginTop = 1; btn.style.marginBottom = 1;
            btn.style.height = 26;
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            _demoButtons.Add((demo, btn));
            return btn;
        }

        private VisualElement BuildCenter()
        {
            var center = new VisualElement();
            center.style.flexGrow = 1;
            center.style.minWidth = 0;

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 10; toolbar.style.paddingRight = 10;
            toolbar.style.paddingTop = 6; toolbar.style.paddingBottom = 6;

            _headerTitle = new Label { text = "" };
            _headerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _headerTitle.style.fontSize = 13;
            _headerTitle.style.flexGrow = 1;
            toolbar.Add(_headerTitle);

            _replayButton = new Button(() => { _appearNext = true; RebuildPreview(); }) { text = "↻ Replay" };
            _replayButton.style.display = DisplayStyle.None;
            toolbar.Add(_replayButton);

            var reset = new Button(() =>
            {
                if (_current == null) return;
                ShowcaseOverrides.Clear(_current.Key); // drop any saved edit so this truly reverts to the code default
                SelectMakeDemo(_current);
            }) { text = "Reset to Defaults" };
            reset.style.backgroundColor = new Color(0.80f, 0.45f, 0.15f);
            reset.style.color = Color.white;
            toolbar.Add(reset);
            center.Add(toolbar);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.paddingLeft = 10; scroll.style.paddingRight = 10;
            scroll.style.paddingBottom = 10;
            _previewContainer = new VisualElement();
            scroll.Add(_previewContainer);
            center.Add(scroll);

            return center;
        }

        private VisualElement BuildInfoPane()
        {
            var pane = new ScrollView(ScrollViewMode.Vertical);
            pane.style.width = 360;
            pane.style.flexShrink = 0;
            pane.style.borderLeftWidth = 1;
            pane.style.borderLeftColor = new Color(0f, 0f, 0f, 0.35f);
            pane.style.paddingLeft = 10; pane.style.paddingRight = 10;
            pane.style.paddingTop = 8; pane.style.paddingBottom = 10;

            var openBtn = new Button(OpenInDesigner) { text = "Open in Designer ▸" };
            openBtn.style.height = 28;
            openBtn.style.marginBottom = 8;
            pane.Add(openBtn);

            pane.Add(SectionLabel("About this demo"));
            _notesLabel = new Label { text = "" };
            _notesLabel.style.whiteSpace = WhiteSpace.Normal;
            _notesLabel.style.color = DesignerPalette.TextBody;
            _notesLabel.style.marginBottom = 10;
            pane.Add(_notesLabel);

            // "C# Source" header row: bold label + a copy button on the same line (shown whenever there's code).
            var codeHeader = new VisualElement();
            codeHeader.style.flexDirection = FlexDirection.Row;
            codeHeader.style.alignItems = Align.Center;
            codeHeader.style.marginTop = 2;
            codeHeader.style.marginBottom = 3;
            _codeSectionLabel = new Label("C# Source");
            _codeSectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _codeSectionLabel.style.color = DesignerPalette.TextPrimary;
            _codeSectionLabel.style.flexGrow = 1;
            codeHeader.Add(_codeSectionLabel);

            _copyButton = new Button(CopyCurrentSource) { tooltip = "Copy to clipboard" };
            _copyButton.style.width = 24;
            _copyButton.style.height = 20;
            _copyButton.style.paddingLeft = 0; _copyButton.style.paddingRight = 0;
            _copyButton.style.marginLeft = 0; _copyButton.style.marginRight = 0;
            _copyButton.style.alignItems = Align.Center;
            _copyButton.style.justifyContent = Justify.Center;
            _copyButton.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Copy, 13));
            codeHeader.Add(_copyButton);
            pane.Add(codeHeader);

            _codeLabel = new Label { enableRichText = true };
            _codeLabel.style.whiteSpace = WhiteSpace.Normal;
            _codeLabel.style.backgroundColor = DesignerPalette.CodeBlockBg;
            _codeLabel.style.color = DesignerPalette.CodeText;
            _codeLabel.style.paddingLeft = 8; _codeLabel.style.paddingRight = 8;
            _codeLabel.style.paddingTop = 6; _codeLabel.style.paddingBottom = 6;
            _codeLabel.style.borderTopLeftRadius = 4; _codeLabel.style.borderTopRightRadius = 4;
            _codeLabel.style.borderBottomLeftRadius = 4; _codeLabel.style.borderBottomRightRadius = 4;
            CodeHighlighter.ApplyCodeFont(_codeLabel);
            _codeLabel.style.fontSize = 11;
            pane.Add(_codeLabel);

            _tryConditionsCard = BuildTryConditionsCard();
            pane.Add(_tryConditionsCard);

            // Shown instead of the code box on pages that add no fields/methods (e.g. the Script Header).
            _codeDescription = new Label { text = "" };
            _codeDescription.style.whiteSpace = WhiteSpace.Normal;
            _codeDescription.style.color = DesignerPalette.TextBody;
            _codeDescription.style.display = DisplayStyle.None;
            pane.Add(_codeDescription);

            return pane;
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = DesignerPalette.TextPrimary;
            label.style.marginBottom = 3;
            label.style.marginTop = 2;
            return label;
        }

        // A styled hint card (shown only on pages that have conditions) telling people how to try the Condition
        // Debugger via the same right-click toggle the real inspector uses.
        private VisualElement BuildTryConditionsCard()
        {
            var card = new VisualElement();
            card.style.marginTop = 12;
            card.style.paddingLeft = 10; card.style.paddingRight = 10;
            card.style.paddingTop = 9; card.style.paddingBottom = 9;
            card.style.backgroundColor = DesignerPalette.ErrorCardBg;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = new Color(0.94f, 0.27f, 0.27f, 1f);
            card.style.borderTopRightRadius = 5; card.style.borderBottomRightRadius = 5;
            card.style.borderTopLeftRadius = 2; card.style.borderBottomLeftRadius = 2;

            var title = new Label("Try the Condition Debugger");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = DesignerPalette.ErrorCardTitle;
            title.style.marginBottom = 4;
            card.Add(title);

            var body = new Label(
                "Some fields above are hidden or disabled by conditions. Right-click anywhere in the preview and " +
                "choose “Debug Conditions” to reveal them in place — red = hidden, amber = disabled. Hover an overlay " +
                "to see exactly which check failed. (It's the same right-click toggle you'll use in the real Inspector.)");
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.color = DesignerPalette.TextBody;
            body.style.fontSize = 11;
            card.Add(body);

            return card;
        }

        // -------------------------------------------------------------- Selection ----

        private void SelectMakeDemo(Demo demo)
        {
            if (demo == null) return;
            _current = demo;

            foreach (var (d, btn) in _demoButtons)
                btn.style.backgroundColor = ReferenceEquals(d, demo)
                    ? (StyleColor)DesignerPalette.SidebarSelected
                    : new StyleColor(StyleKeyword.Null);

            DisarmLiveDebug();
            DisposeLive();
            _target = demo.CreateTarget(_liveObjects);

            _layoutAsset = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            _layoutAsset.hideFlags = HideFlags.DontSave;
            _liveObjects.Add(_layoutAsset);
            // Prefer this page's persisted override (a prior designer save); fall back to the code-built default.
            _layoutAsset.Root = ShowcaseOverrides.TryLoad(demo.Key, out var savedRoot) ? savedRoot : demo.BuildLayout();
            StampLayout(_layoutAsset, _target);

            _theme = demo.BuildTheme != null ? demo.BuildTheme(_liveObjects) : null;

            _serializedObject = new SerializedObject(_target);
            _appearNext = true;
            // Debug mode is per-page — don't carry it across demos. The "try it" hint card shows only on pages that
            // actually have conditions (so it's relevant only on the Validation & Conditions pages).
            _debugConditions = false;
            if (_tryConditionsCard != null)
            {
                _tryConditionsCard.style.display = ConditionBinder.LayoutHasConditions(_layoutAsset.Root)
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }
            RebuildPreview();

            _headerTitle.text = demo.Title;
            _notesLabel.text = demo.Notes;
            UpdateSourcePanel(demo);
            _replayButton.style.display = demo.IsAnimations ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Fills the right-hand source panel with the MINIMAL code for this page (just the fields/methods it uses),
        // or — when the page references no members — a short "how it's added" description with no copy button.
        private void UpdateSourcePanel(Demo demo)
        {
            // A hand-authored snippet (e.g. the Embedded Editor's own custom-editor source) wins over the
            // auto-extracted field source — shown as real, copyable "C# Source".
            if (!string.IsNullOrEmpty(demo.CodeOverride))
            {
                _codePlain = demo.CodeOverride;
                _codeSectionLabel.text = "C# Source";
                _copyButton.style.display = DisplayStyle.Flex;
                _codeLabel.text = CodeHighlighter.Highlight(demo.CodeOverride);
                _codeLabel.style.display = DisplayStyle.Flex;
                _codeDescription.style.display = DisplayStyle.None;
                return;
            }

            string snippet = ShowcaseSource.BuildSnippet(_target, _layoutAsset != null ? _layoutAsset.Root : null, out bool hasCode);
            if (hasCode)
            {
                _codePlain = snippet;
                _codeSectionLabel.text = "C# Source";
                _copyButton.style.display = DisplayStyle.Flex;
                _codeLabel.text = CodeHighlighter.Highlight(snippet);
                _codeLabel.style.display = DisplayStyle.Flex;
                _codeDescription.style.display = DisplayStyle.None;
            }
            else
            {
                _codePlain = null;
                _codeSectionLabel.text = "How it's added";
                _copyButton.style.display = DisplayStyle.None;
                _codeLabel.style.display = DisplayStyle.None;
                _codeDescription.text = !string.IsNullOrEmpty(demo.CodeDescription)
                    ? demo.CodeDescription
                    : "This element is structural — it adds no fields or methods to your script.";
                _codeDescription.style.display = DisplayStyle.Flex;
            }
        }

        private void CopyCurrentSource()
        {
            if (string.IsNullOrEmpty(_codePlain)) return;
            EditorGUIUtility.systemCopyBuffer = _codePlain;
            ShowNotification(new GUIContent("Copied"));
        }

        // Rebuilds the live inspector from the current layout/object/theme, re-attaching the Quick-Edit menu.
        // Passed to LayoutTreeBuilder as the rebuild callback, so collection add/remove, inline edits and
        // quick-edits refresh in place.
        private void RebuildPreview()
        {
            if (_previewContainer == null) return;
            _previewContainer.Clear();
            if (_target == null || _layoutAsset == null || _serializedObject == null) return;

            _serializedObject.Update();
            // liveInspector: TRUE — a deliberate opt-in, and the third caller ever to ask for it. Live Debug
            // is gated on this rather than on !designMode because the two are not the same question (the
            // designer's Preview sets designMode = false on purpose), so a new caller gets nothing until it
            // asks. Without it the bug glyph in the Script Header still BUILT and still toggled DebugModeState,
            // but DebugRowBinder.EnabledForBuild stayed false — no graph rows, so no transport bar and no
            // recorder either. A live-looking button that does nothing, on the one page whose whole subject is
            // that button. Everything it mounts is display:None until the glyph is clicked (DebugRunner's
            // ApplyArmed), so the pages that merely HAVE a Script Header are unchanged.
            var view = LayoutTreeBuilder.Build(_layoutAsset, _serializedObject, RebuildPreview, _theme,
                playAppearAnimations: _appearNext, debugConditions: _debugConditions, liveInspector: true);
            _appearNext = false;
            _previewContainer.Add(view);

            // Field search — in PARITY with both real render paths (VisualInspectorEditor /
            // SasquatchForceTakeover), which call this right after Build. The Script Header widget BUILDS the
            // magnifier and its inline field, but only Attach binds them to the filter engine — skip it and the
            // sandbox shows a live-looking, hover-revealed magnifier that does nothing when clicked.
            InspectorFieldSearch.Attach(view, _target);

            // Wire the right-click Quick-Edit menu onto the live preview (real-inspector behavior). Edits mutate
            // the in-memory layout and rebuild here; nothing persists to disk (the layout is transient).
            QuickEditMenu.Attach(view, new QuickEditContext
            {
                LayoutAsset = _layoutAsset,
                SerializedObject = _serializedObject,
                Target = _target,
                RebuildInspector = RebuildPreview,
                StableRoot = _previewContainer,
                // Window-local debug state (never touches the real inspector's EditorPrefs flag), toggled from the
                // same right-click menu the live inspector uses — so the sandbox teaches the real interaction.
                GetDebugConditions = () => _debugConditions,
                SetDebugConditions = v =>
                {
                    _debugConditions = v;
                    RebuildPreview();
                }
            });
        }

        private void OpenInDesigner()
        {
            if (_current == null) return;

            // Build a fresh, INDEPENDENT target + layout for the designer so it never shares (or outlives) the
            // window's live preview objects. The designer clones the layout internally, so we drop our copy
            // immediately afterward; the target is left to Unity's domain-reload cleanup.
            var handoff = new List<UnityEngine.Object>();
            var target = _current.CreateTarget(handoff);

            var layout = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            // Edit the page's current state — its saved override if one exists, else the code default.
            layout.Root = ShowcaseOverrides.TryLoad(_current.Key, out var savedRoot) ? savedRoot : _current.BuildLayout();
            StampLayout(layout, target);

            // Pass the page key so the designer's Save persists back to THIS page (ShowcaseOverrides), and arm a
            // reload so returning focus here after saving shows the change.
            SasquatchLayoutDesignerWindow.OpenWindowWithLayout(layout, target, target.GetType(), _current.Key);
            DestroyImmediate(layout);
            _reloadOnFocus = _current;
        }

        // ----------------------------------------------------------------- Helpers ----

        private static void StampLayout(InspectorLayoutAsset layout, ScriptableObject target)
        {
            if (layout == null || target == null) return;
            layout.TargetTypeName = target.GetType().AssemblyQualifiedName;
            if (ScriptGuidUtility.TryGetGuid(target, out string guid)) layout.ScriptGuid = guid;
        }

        private void DisposeLive()
        {
            foreach (var obj in _liveObjects)
                if (obj != null) DestroyImmediate(obj);
            _liveObjects.Clear();
            _target = null;
            _layoutAsset = null;
            _theme = null;
            _serializedObject = null;
        }

        /// <summary>
        /// Switches Live Debug off for the page we are leaving.
        ///
        /// <para>Same rule as <c>_debugConditions</c> above: a debug mode is PER PAGE here, so a sandbox demo
        /// never carries state into the next one. It matters more for this one, because Live Debug is armed per
        /// TYPE in SessionState rather than per window — six of these pages share ShowcaseCommonFieldsTarget, so
        /// arming it on the Live Debug page and clicking away would otherwise sprout graph strips and a
        /// transport bar on five pages that are about something else, and leave the switch on after the window
        /// closed. Deliberately not the real feature's own behaviour (where being armed is a session thing that
        /// survives on purpose) — these are demos, not an inspector you are working in.</para>
        /// </summary>
        private void DisarmLiveDebug()
        {
            if (_target == null) return;
            LiveDebug.DebugModeState.Set(_target.GetType(), false);
        }

        private static T Reg<T>(List<UnityEngine.Object> sink) where T : ScriptableObject
        {
            var obj = CreateInstance<T>();
            obj.hideFlags = HideFlags.DontSave;
            sink.Add(obj);
            return obj;
        }

        // A built-in Unity mesh for the Asset Preview demo — owned by Unity, so never tracked/destroyed.
        private static Mesh BuiltinMesh() => Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        // The shipped Showcase texture, by GUID (survives moves/renames) — seeds the field-bound Frame fill.
        // A project asset, so it is NOT tracked in the sink.
        private const string BrickWallGuid = "71e917249a0ccd24a80b44377dcb374c";
        private static Texture2D ShowcaseTexture()
        {
            string path = AssetDatabase.GUIDToAssetPath(BrickWallGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Three real ScriptableObjects for the Collections ▸ In-Line Editor Rows demo. Created here rather than
        // in a field initializer (CreateInstance from one is unsafe) and tracked in the sink so they're destroyed
        // with the page. They render ShowcaseStatBlock's designed layout — the same session override the Media
        // page's In-Line Editor uses.
        private static ShowcaseStatBlock MakeStatBlock(List<UnityEngine.Object> sink, string name, int value, Color color)
        {
            var block = Reg<ShowcaseStatBlock>(sink);
            block.name = name;
            block.statName = name;
            block.value = value;
            block.barColor = color;
            return block;
        }

        // ------------------------------------------------------------- Category data ----

        // Shown verbatim in the info pane for the Embedded Editor demo (it binds no members, so there's no
        // auto-extracted source). Mirrors ShowcaseElementsTargetEditor's real [SasquatchEmbed] compass slice.
        private const string EmbeddedEditorCodeSnippet =
@"[CustomEditor(typeof(ShowcaseElementsTarget))]
internal class ShowcaseElementsTargetEditor : Editor
{
    // A [SasquatchEmbed] slice: a self-contained piece of your OWN custom editor
    // that Sasquatch's Embedded Editor element can re-inline anywhere in a layout.
    [SasquatchEmbed(""Compass"")]
    public VisualElement Compass()
    {
        var dial = new VisualElement { style = { width = 200, height = 200 } };

        // 8 perfectly even 45-degree sectors + a two-tone needle, drawn with Painter2D.
        dial.generateVisualContent += ctx => DrawCompass(ctx, Heading());

        // Drag the dial to spin the needle, writing back to the 'heading' field.
        dial.RegisterCallback<PointerDownEvent>(e => SetHeading(e.localPosition));

        // Repaint whenever the bound 'heading' value changes.
        dial.TrackPropertyValue(serializedObject.FindProperty(""heading""),
                                _ => dial.MarkDirtyRepaint());
        return dial;
    }
}";

        private static List<CategoryGroup> BuildGroups()
        {
            var groups = new List<CategoryGroup>();

            // --- Common Fields ---
            Func<List<UnityEngine.Object>, ScriptableObject> commonFields = sink => Reg<ShowcaseCommonFieldsTarget>(sink);
            groups.Add(Group(
                MakeDemo("Common Fields", commonFields, ShowcaseLayouts.CommonFields,
                    "Everything the child pages show, on one page. A read-only row sits on its own at the top. " +
                    "The big card is one field per display type — Progress Bar, Stepper, rounded Toggle, Text, " +
                    "Rich Text, Min-Max, Rating, XY Pad, Vector Sliders, axis pips, File Path, Gradient, Curve — " +
                    "ending with a Label-Above row and a field carrying a \"?\" help chip. Then the nine Prefix & " +
                    "Suffix variations get their own card, and the last one is a computed C# property with no " +
                    "backing field."),
                MakeDemo("Script Header", commonFields, ShowcaseLayouts.CommonFields_ScriptHeader,
                    "A nicer replacement for Unity's greyed-out Script field. Single-click pings the script, " +
                    "double-click opens it. Its text is overridden here to 'Script Header'.",
                    "The Script Header isn't a field — it's added automatically at the top of every inspector. " +
                    "You can also add it manually from the Widgets tab in the Designer, or delete it altogether."),
                MakeDemo("Field Search", commonFields, ShowcaseLayouts.CommonFields_FieldSearch,
                    "Hover the Script Header and click the magnifier to filter this inspector by field name. " +
                    "Type to narrow it — non-matching rows collapse away while matched ones keep their section " +
                    "trail, and a collapsed section holding a match opens itself. ↑ / ↓ step through matches, " +
                    "Enter jumps to one, Esc closes. The count beside the field reads visible / total.",
                    "The search lives in the Script Header widget — nothing to add per field. Turn it off for a " +
                    "type with the header's Show Search option in the Designer."),
                MakeDemo("Progress Bar", commonFields, ShowcaseLayouts.CommonFields_ProgressBar,
                    "A float rendered as an interactive progress bar (drag to set), formatted value / max."),
                MakeDemo("Numeric Stepper", commonFields, ShowcaseLayouts.CommonFields_Stepper,
                    "An option on the plain Default display type, not a widget of its own: −/+ buttons around " +
                    "the value that step by Step Size at the field's native precision. The buttons are real " +
                    "Sasquatch buttons — style, size and hover animation are all yours."),
                MakeDemo("Rounded Toggle", commonFields, ShowcaseLayouts.CommonFields_RoundedToggle,
                    "A bool rendered as a rounded ON/OFF switch instead of a checkbox."),
                MakeDemo("Text", commonFields, ShowcaseLayouts.CommonFields_Text,
                    "A string rendered as a multi-line text area. The first box is Resizable — drag the grip in " +
                    "its bottom-right corner and it writes the height back into the layout. The second is Fixed, " +
                    "where Box Height is the only height control, which is how you get a taller box with no grip. " +
                    "Box Height 0 means auto: the natural height, or whatever a [TextArea] asked for."),
                MakeDemo("Rich Text", commonFields, ShowcaseLayouts.CommonFields_RichText,
                    "Edit raw rich-text tags (bold, color, size) while a live preview shows exactly what the " +
                    "player will see. The toolbar wraps the selected text; the Color chips come from your project " +
                    "palette. This string also carries {runeCount} and {heroName} — wrap a field name in braces " +
                    "and the preview renders its LIVE value, so editing either field above moves the text. Type " +
                    "\"{\" in the box for the field autocomplete (rest on a suggestion for a code peek at its declaration)."),
                MakeDemo("File Path", commonFields, ShowcaseLayouts.CommonFields_FilePath,
                    "A string in Path input mode: the box gains a Browse button and a picker filtered to the " +
                    "extensions you list (or set Browse Folders for a directory). The greyed hint over the empty " +
                    "box is the widget's Placeholder — it works on any Text field."),
                MakeDemo("Min-Max Slider", commonFields, ShowcaseLayouts.CommonFields_MinMax,
                    "A Vector2 (min, max) on a single slider, with editable numeric fields shown by default."),
                MakeDemo("Rating", commonFields, ShowcaseLayouts.CommonFields_Rating,
                    "An int rendered as a clickable row of pips — stars, hearts, or dots."),
                MakeDemo("XY Pad", commonFields, ShowcaseLayouts.CommonFields_XYPad,
                    "A Vector2 edited by dragging a dot around a 2D pad, with optional X / Y number fields."),
                MakeDemo("Vector Sliders", commonFields, ShowcaseLayouts.CommonFields_VectorSliders,
                    "A Vector3 / Vector4 as one labeled slider per axis, with a Normalize button."),
                MakeDemo("Axis Color Pips", commonFields, ShowcaseLayouts.CommonFields_AxisPips,
                    "Tint a pip inside each component field with its handle color — X red, Y green, Z blue, W " +
                    "neutral — so you can read an axis at a glance instead of counting boxes. A checkbox on any " +
                    "vector field, including the plain Default one (the Vector Sliders below are the same field, untinted)."),
                MakeDemo("Prefix & Suffix", commonFields, ShowcaseLayouts.CommonFields_UnitSuffix,
                    "Text or an icon on either side of a field — and each side is fully styleable, which is what " +
                    "this page is really for. Card 1 is placement: inside the input box, beside it, or on a pill. " +
                    "Card 2 is looks — a pill on both sides in different colors, an icon riding inside a pill, and " +
                    "a styled prefix on a plain string, because this is not a numeric feature. Card 3 is the " +
                    "awkward cases: a vector repeats the affix per axis (or once for the whole row), and the " +
                    "Slider / Progress Bar / Min-Max mount theirs around their own value read-outs. A Table can " +
                    "fold the unit into the column header instead — see Collections ▸ Table. Each side also " +
                    "carries its own color, bold/italic, size and gap; set it all in the Designer's Prefix & " +
                    "Suffix section, or from the right-click quick-edit menu."),
                MakeDemo("Label Placement", commonFields, ShowcaseLayouts.CommonFields_LabelPlacement,
                    "Where a field's label goes, and whether the field is editable. Label Above Field lifts the " +
                    "label onto its own row so the control gets the full width — worth it for a long string or a " +
                    "squeezed vector. Clearing the Label row entirely hides it, for a field whose container " +
                    "already names it. Read-Only draws the real widget and disables it."),
                MakeDemo("Field Help Chip", commonFields, ShowcaseLayouts.CommonFields_HelpChip,
                    "A \"?\" chip beside a field that reveals a markdown help card — bold, bullets, colors, inline " +
                    "code and links, the same renderer the Help Box uses. It stays folded until someone wants it, " +
                    "so a field can carry real explanation without spending a row on it. Turn it on per field, " +
                    "type the text, and pick which side the chip sits on."),
                MakeDemo("Gradient", commonFields, ShowcaseLayouts.CommonFields_Gradient,
                    "A Gradient edited inline: drag the color/alpha pins, click one to pick from your palette (or " +
                    "swap palettes on the fly), press Delete to remove the selected key, and pick a blend mode."),
                MakeDemo("Animation Curve", commonFields, ShowcaseLayouts.CommonFields_Curve,
                    "An AnimationCurve edited inline — drag keys and tangents, or apply a preset from the ribbon — " +
                    "with no modal curve-editor window."),
                MakeDemo("Computed Property", commonFields, ShowcaseLayouts.CommonFields_Computed,
                    "A C# property with no backing field, surfaced read-only — the ShowInInspector feature.")));

            // --- Dropdowns & Pickers ---
            Func<List<UnityEngine.Object>, ScriptableObject> drop = sink => Reg<ShowcaseDropdownsTarget>(sink);
            groups.Add(Group(
                MakeDemo("Dropdowns & Pickers", drop, ShowcaseLayouts.Dropdowns,
                    "One DropDown display type adapts to the field: an enum becomes a searchable list, a string a Tag " +
                    "picker, an int a Layer picker, a LayerMask a multi-select, and a string can read its options " +
                    "from a method. The three badge rows are the same enums as clickable chips instead — a [Flags] " +
                    "multi-select grid, a single-select segmented control, and a styled one with a per-value color " +
                    "and icon on each badge."),
                MakeDemo("Enum", drop, ShowcaseLayouts.Dropdowns_Enum, "An enum becomes a searchable dropdown of its values."),
                MakeDemo("Enum Badges", drop, ShowcaseLayouts.Dropdowns_EnumBadges,
                    "Clickable chips instead of a dropdown: a [Flags] enum becomes a multi-select grid (with " +
                    "None / All quick chips), a plain enum a single-select segmented control. The third row is " +
                    "the same enum styled: a per-value color AND icon, each label on a soft dark pill (Text " +
                    "Backdrop), laid out in a fixed two-column grid. Columns, badge height and spacing are all " +
                    "per field — setting a fixed column count is also what stops the grid re-wrapping as the " +
                    "inspector is resized."),
                MakeDemo("Tag", drop, ShowcaseLayouts.Dropdowns_Tag, "A string becomes a project Tag picker."),
                MakeDemo("Tag Chips", drop, ShowcaseLayouts.Dropdowns_TagChips,
                    "A string array edited as removable chips with an inline add field — the whole array at once."),
                MakeDemo("Layer", drop, ShowcaseLayouts.Dropdowns_Layer, "An int becomes a Layer picker."),
                MakeDemo("LayerMask", drop, ShowcaseLayouts.Dropdowns_LayerMask, "A LayerMask becomes a multi-select layer dropdown."),
                MakeDemo("Method-sourced", drop, ShowcaseLayouts.Dropdowns_Method, "Options are supplied by the WeaponClasses() method.")));

            // --- Media ---
            Func<List<UnityEngine.Object>, ScriptableObject> media = sink =>
            {
                var t = Reg<ShowcaseMediaTarget>(sink);
                t.previewMesh = BuiltinMesh();
#if SASQ_AUDIO
                t.sfx = ShowcaseAudio.GetExplosionClip(); // a real imported asset (plays reliably; not a tracked live object)
#endif
                // Prefer the shipped "Jump Particles" prefab (it shows the widget off better); the renderer
                // instantiates its own hidden copy, so the asset itself is NOT tracked/destroyed in the sink.
                // Fall back to the generated in-memory effect if the asset is missing/unimportable.
#if SASQ_PARTICLES
                // Gated whole: with the particle module gone, JumpParticles.prefab still LOADS (a GameObject is a
                // GameObject) but its ParticleSystem/ParticleSystemRenderer components cannot exist, so the `??`
                // fallback never fires and the demo would show an empty preview. Nothing reads vfxPrefab once the
                // particle demo pages are gated out, so not loading it at all is the honest degrade.
                t.vfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                                  "Assets/Sasquatch Inspector/Editor/Showcase/Assets/JumpParticles.prefab")
                              ?? ShowcaseParticles.CreateDemoEffect(sink); // generated in-memory effect, tracked for cleanup
#endif
                t.statBlock = Reg<ShowcaseStatBlock>(sink);
                return t;
            };
            groups.Add(Group(
                MakeDemo("Previews & References", media, ShowcaseLayouts.Previews,
                    "Everything you can do with an object reference. Three of these are previews — a 3D asset " +
                    "thumbnail, an AudioClip with a play button and scrubbable waveform, a click-to-play particle " +
                    "effect. The other two are about the reference itself: the In-Line Editor embeds the referenced " +
                    "object's own fields, and the last row's slot is EMPTY on purpose so its Create button shows — " +
                    "click it, pick where to save, and the new asset is made and assigned in one step."),
                MakeDemo("Asset Preview", media, ShowcaseLayouts.Previews_AssetPreview, "An object reference with a 3D asset-preview thumbnail."),
#if SASQ_AUDIO
                MakeDemo("Audio Preview", media, ShowcaseLayouts.Previews_AudioPreview, "An AudioClip with a play button and a scrubbable waveform."),
#endif
#if SASQ_PARTICLES
                MakeDemo("Particle Preview", media, ShowcaseLayouts.Previews_ParticlePreview, "A GameObject/ParticleSystem rendered as a looping, click-to-play video thumbnail."),
#endif
                MakeDemo("In-Line Editor", media, ShowcaseLayouts.Previews_InlineEditor, "A referenced object's own fields embedded inline, collapsible."),
                MakeDemo("Create Asset Button", media, ShowcaseLayouts.Previews_CreateAsset,
                    "An empty ScriptableObject reference shows a Create button: click it, pick where to save, " +
                    "and the new asset is created and assigned in one step. Works on the Default, Asset Preview " +
                    "and In-Line Editor display types — enable it per field in the Designer.")));

            // --- Containers ---
            Func<List<UnityEngine.Object>, ScriptableObject> cont = sink =>
            {
                var t = Reg<ShowcaseContainersTarget>(sink);
                t.portrait = ShowcaseTexture(); // seeds the field-bound Frame fill (a project asset, untracked)
                return t;
            };
            groups.Add(Group(
                MakeDemo("Containers", cont, ShowcaseLayouts.Containers,
                    "The same fields arranged with every container type. Drag the dividers in Columns and the " +
                    "Grid to resize tracks; the Toggle-Group foldout greys its contents when its checkbox is off."),
                MakeDemo("Columns", cont, ShowcaseLayouts.Containers_Columns, "Side-by-side columns with drag-resizable widths."),
                MakeDemo("Grid", cont, ShowcaseLayouts.Containers_Grid, "A fixed-column grid with drag-resizable rows and columns."),
                MakeDemo("Card", cont, ShowcaseLayouts.Containers_Card, "A rounded, padded, titled panel."),
                MakeDemo("Foldout", cont, ShowcaseLayouts.Containers_Foldout, "A collapsible titled section."),
                MakeDemo("Toggle-Group Foldout", cont, ShowcaseLayouts.Containers_ToggleGroup, "A foldout whose header IS a bound bool — greys the group when off."),
                MakeDemo("Tabs", cont, ShowcaseLayouts.Containers_Tabs, "A tab strip; each tab holds its own fields."),
                MakeDemo("Frame", cont, ShowcaseLayouts.Containers_Frame,
                    "A container that CLIPS its children to a shape (rounded-rect / circle / pill / diagonal split), " +
                    "with an optional image fill and an overlay wash for readable overlaid content."),
                MakeDemo("Frame Shapes & Fill", cont, ShowcaseLayouts.Containers_FrameShapes,
                    "The three shapes the Frame page can't show at once. Pill is a fully-rounded capsule. " +
                    "Diagonal Split is the one shape with STRUCTURE — two panes divided by an angled seam, with " +
                    "the seam's position, angle and width all adjustable. The last card's image fill is BOUND to a " +
                    "field rather than baked into the layout: swap the texture in the object field below it and the " +
                    "frame repaints, which is how you get a live portrait or icon frame.")));

            // --- Layout Elements ---
            Func<List<UnityEngine.Object>, ScriptableObject> elem = sink => Reg<ShowcaseElementsTarget>(sink);
            var embeddedDemo = MakeDemo("Embedded Editor", elem, ShowcaseLayouts.Elements_EmbeddedEditor,
                "Re-inlines a section of a type's OWN custom editor inside a Sasquatch layout. Here it embeds just " +
                "the [SasquatchEmbed] \"Compass\" slice of ShowcaseElementsTarget's editor — drag the dial to spin the " +
                "needle. The whole point: surface a piece of a custom editor you already have, right where you want it.");
            embeddedDemo.CodeOverride = EmbeddedEditorCodeSnippet;
            groups.Add(Group(
                MakeDemo("Layout Elements", elem, ShowcaseLayouts.Elements,
                    "Decorative building blocks you drag in from the Widgets palette's Elements section — they add " +
                    "structure and polish without binding to a field.",
                    "These are layout Elements, not fields — add them from the Widgets tab in the Designer. None bind " +
                    "to your script; each is configured entirely in the layout."),
                MakeDemo("Image", elem, ShowcaseLayouts.Elements_ImageBanner,
                    "A decorative image or banner drawn at a fixed height (referenced by asset GUID, so it survives moves).",
                    "Add an Image from the Widgets palette and pick a texture — it stores the asset GUID, not a path."),
                MakeDemo("Text Block", elem, ShowcaseLayouts.Elements_TextBlock,
                    "Free-form rich text placed anywhere — markdown plus Unity rich-text tags, with an optional card background.",
                    "Add a Text Block from the Widgets palette and type your text; it renders markdown and isn't bound to any field."),
                MakeDemo("Spacers & Dividers", elem, ShowcaseLayouts.Elements_Spacers,
                    "A blank gap (fixed px or flexible) and a labeled rule for visually separating groups of fields.",
                    "Add a Space or a Divider from the Widgets palette; drag its grip or type an exact size in the Designer."),
                embeddedDemo));

            // --- Validation & Conditions ---
            Func<List<UnityEngine.Object>, ScriptableObject> val = sink => Reg<ShowcaseValidationTarget>(sink);
            Func<List<UnityEngine.Object>, ScriptableObject> cond = sink => Reg<ShowcaseConditionsTarget>(sink);
            groups.Add(Group(
                MakeDemo("Validation & Conditions", val, ShowcaseLayouts.Validation,
                    "The validation rules start TRIPPED — fix the values to clear them. Toggle 'Enable Advanced' " +
                    "to show / enable the dependent fields. The last card shows info / warning / error help boxes."),
                MakeDemo("Required", val, ShowcaseLayouts.Validation_Required, "Warns while a required field is empty."),
                MakeDemo("Range", val, ShowcaseLayouts.Validation_Range, "Warns when a number falls outside its allowed range."),
                MakeDemo("Method validation", val, ShowcaseLayouts.Validation_Method, "Runs a bool method (team size must be even) and shows its message on failure."),
                MakeDemo("Conditional Show", val, ShowcaseLayouts.Validation_Show, "A field appears only while another bool is true."),
                MakeDemo("Conditional Enable", val, ShowcaseLayouts.Validation_Enable, "A field is editable only while another bool is true."),
                MakeDemo("Condition Debugger", cond, ShowcaseLayouts.ConditionDebugger,
                    "Several dependent fields start hidden/disabled by conditions. Turn on 'Debug Conditions' in the " +
                    "preview header to reveal them in place — red = hidden, amber = disabled, with a corner verdict " +
                    "pill. Hover any overlay to see the evaluation breakdown (e.g. isActive [TRUE] && hasAmmo [FALSE] " +
                    "→ HIDDEN). Flip the driver toggles / Weapon Mode and watch the overlays clear live."),
                MakeDemo("Help Boxes", val, ShowcaseLayouts.Validation_HelpBoxes, "Info / warning / error boxes, including an expandable one."),
                MakeDemo("Markdown", val, ShowcaseLayouts.Validation_Markdown,
                    "Help-box messages render lightweight markdown — bold, italics, palette/hex colors, inline " +
                    "code, fenced code blocks, and clickable links (click one to open it). The last box has " +
                    "markdown disabled, showing the raw characters.",
                    codeDescription:
                    "HelpBoxNode.Message renders lightweight markdown:\n\n" +
                    "•  **bold**  and  *italics*\n" +
                    "•  {Color|text} — a palette name, #hex, or a CSS color name\n" +
                    "•  `inline code`\n" +
                    "•  ``` fenced blocks ``` — a monospace, syntax-highlighted code panel\n" +
                    "•  [label](https://url) — a clickable link\n\n" +
                    "It's standard Markdown (only {Color|text} is a Sasquatch extension). Toggle it per box with " +
                    "the \"Render Markdown\" checkbox in the node's General Settings (designer) or quick-edit popup.")));

            // --- Styling & Themes ---
            Func<List<UnityEngine.Object>, ScriptableObject> styling = sink => Reg<ShowcaseStylingTarget>(sink);
            Func<List<UnityEngine.Object>, SasquatchThemeAsset> theme = sink =>
            {
                var th = Reg<SasquatchThemeAsset>(sink);
                ShowcaseLayouts.ConfigureCardTheme(th);
                return th;
            };
            groups.Add(Group(
                DemoThemed("Styling & Themes", styling, ShowcaseLayouts.Styling, theme,
                    "Any node can carry its own colors, fonts, bold/italic, backgrounds, borders, widget shape and " +
                    "section color. Nothing here needs a line of code — every card is the same fields wearing " +
                    "different clothes, and the last one is recolored wholesale by a theme asset via a style token."),
                MakeDemo("Text Styling", styling, ShowcaseLayouts.Styling_Text, "Per-field font style, size, and color."),
                MakeDemo("Backgrounds & Borders", styling, ShowcaseLayouts.Styling_Background, "A container with a custom background, border, radius, and padding."),
                MakeDemo("Slider Styling", styling, ShowcaseLayouts.Styling_Slider, "A slider with a custom knob shape, size, and bar/knob colors."),
                MakeDemo("Progress Bar Styling", styling, ShowcaseLayouts.Styling_ProgressBar,
                    "The same kind of value, two looks. The first ramps its fill color by value — the classic " +
                    "health-bar read, where the color IS the warning — and shows a percentage with a unit suffix. " +
                    "The second pins a flat fill and sits its readout on a dark pill (Text Backdrop), which stays " +
                    "legible over any fill color instead of adapting to it. Height, text mode and both colors are per field."),
                MakeDemo("Toggle Styles", styling, ShowcaseLayouts.Styling_Toggles,
                    "One bool, five looks: a checkbox, a modern checkbox, and three switches (rounded, square, " +
                    "slim). These are widget shapes rather than style tweaks — pick the one that fits the density " +
                    "of the inspector you're building. The switch styles can also carry ON / OFF text; the two " +
                    "checkbox looks hide it, since it's a switch affordance."),
                MakeDemo("Section Tint", styling, ShowcaseLayouts.Styling_SectionTint,
                    "One color drives a section's header, its body wash AND its left accent rail together, so " +
                    "color-coding a group costs one field instead of four style overrides. Works the same on a Card " +
                    "and a Foldout — handy for making Combat / Movement / Audio sections scannable at a glance."),
                DemoThemed("Themed Inspector", styling, ShowcaseLayouts.Styling_Themed, theme, "A node tagged with a style token, recolored by a theme asset.")));

            // --- Advanced ---
            Func<List<UnityEngine.Object>, ScriptableObject> adv = sink => Reg<ShowcaseAdvancedTarget>(sink);
            groups.Add(Group(
                MakeDemo("Advanced", adv, ShowcaseLayouts.Advanced,
                    "Pick colors from swatch grids / bars, choose a concrete [SerializeReference] type and edit it " +
                    "inline, and use the custom UI Toolkit UnityEvent drawer."),
                MakeDemo("Color Palette (Grid)", adv, ShowcaseLayouts.Advanced_ColorGrid, "Pick a color from a grid of swatches."),
                MakeDemo("Color Palette (Bar)", adv, ShowcaseLayouts.Advanced_ColorBar, "Pick a color from a bar of swatches."),
                MakeDemo("Polymorphic", adv, ShowcaseLayouts.Advanced_Polymorphic,
                    "Choose a concrete type for a [SerializeReference] field and edit it inline. Open the picker and " +
                    "hover a type: the code peek shows that type's declaration straight from your script, so you pick " +
                    "by what it actually contains rather than by name."),
                MakeDemo("UnityEvent", adv, ShowcaseLayouts.Advanced_UnityEvent,
                    "A UnityEvent drawn with the custom searchable method picker. Hovering a function in the Select " +
                    "Function popup peeks at its source — signature, parameters and all — before you commit to it.")));

            // --- Collections (placed above Buttons) ---
            Func<List<UnityEngine.Object>, ScriptableObject> coll = sink =>
            {
                var t = Reg<ShowcaseCollectionsTarget>(sink);
                // Real ScriptableObjects for the In-Line Editor Rows demo — each row edits one of these in place.
                t.statBlocks = new List<ShowcaseStatBlock>
                {
                    MakeStatBlock(sink, "Vitality", 72, new Color(0.40f, 0.85f, 1f)),
                    MakeStatBlock(sink, "Ferocity", 45, new Color(1f, 0.52f, 0.35f)),
                    MakeStatBlock(sink, "Cunning", 88, new Color(0.68f, 0.55f, 1f)),
                };
                return t;
            };
            Func<List<UnityEngine.Object>, ScriptableObject> inv = sink => Reg<ShowcaseInventoryTarget>(sink);
            groups.Add(Group(
                MakeDemo("Collections", coll, ShowcaseLayouts.Collections,
                    "Every way to draw a list, one card each. The loot list is a Table with drag-resizable, " +
                    "drag-REORDERABLE columns, search, multi-row select and per-row right-click menus; below it " +
                    "are a plain reorderable list, in-line editor rows, a chip list, a polymorphic list, and a " +
                    "120-item inventory at scale. The two style cards start FOLDED because each holds several " +
                    "copies of the same list — open them for the three List Styles (Rows, Minimal, Timeline) and " +
                    "the row frames (Line, Card, a tinted rail, zebra striping). Every card folds independently."),
                MakeDemo("Table", coll, ShowcaseLayouts.Collections_Table,
                    "One row per element, one column per item-layout field. Drag a divider between the headers to " +
                    "resize a column (double-click it to auto-fit to the widest content), drag a header itself to " +
                    "REORDER the columns, click one to sort, and right-click a row for Duplicate / Insert Above / " +
                    "Insert Below / Delete. Ctrl-click and Shift-click the row gutters to pick several, drag them " +
                    "as one block, Delete to remove them all; the trash button is hold-to-clear, with a ring that " +
                    "fills as you hold. A cell is a full binding, so it can be any widget — Weight here is a " +
                    "progress bar and Stars a rating row, inside the grid. Weight also shows an affix folded into " +
                    "the column header (\"Weight (kg)\") instead of repeating in all three cells."),
                MakeDemo("Reorderable list", coll, ShowcaseLayouts.Collections_List,
                    "List mode: each row shows the whole element. Drag a row's grip to reorder it."),
                MakeDemo("List Styles", coll, ShowcaseLayouts.Collections_ListStyles,
                    "The list's CONTAINER look — shell, index gutter and density. The same six-row list drawn " +
                    "three ways. Rows (the default) gives it a bordered shell and an edge down the gutter. " +
                    "Minimal drops the shell for air, good under a [Header] that already frames things. Timeline " +
                    "sinks it into a recessed well with a continuous rail, every index a node on it — for when " +
                    "the order IS the point: waypoints, spawn tables, dialogue lines. List mode only; a table has " +
                    "its own grid chrome."),
                MakeDemo("Row Frames & Zebra", coll, ShowcaseLayouts.Collections_RowFrames,
                    "How ONE row is drawn, which composes with every List Style. None leaves rows bare. Line puts " +
                    "a hairline between them, never after the last. Card gives each row a tray with a full-height " +
                    "left rail — the one that still reads when rows get tall, like In-Line Editor Rows. Frame " +
                    "Color tints the line or the rail. Zebra Striping is separate again: an alternating row tint " +
                    "you can turn on with or without a frame."),
                MakeDemo("In-Line Editor Rows", coll, ShowcaseLayouts.Collections_InlineRows,
                    "A list of ScriptableObject references where each row is that object's OWN inspector under a " +
                    "chevron — edit the items of an asset list in place, with no 'focused item' helper field. The " +
                    "rows use ShowcaseStatBlock's designed layout, because a layout saved for a type applies " +
                    "everywhere it appears. Collapsed rows build nothing until you open them, so long lists stay fast."),
                MakeDemo("Tag Chip list", coll, ShowcaseLayouts.Collections_TagChips,
                    "A whole string collection as removable chips with an inline add field — click a chip to edit it " +
                    "in place. This is the collection-level Display Type; the Dropdowns page shows the same look " +
                    "applied to a single string[] field instead."),
                MakeDemo("Polymorphic list", coll, ShowcaseLayouts.Collections_Polymorphic,
                    "A [SerializeReference] list: every row is a searchable type picker plus the chosen type's own " +
                    "fields, so one list can hold a mix of concrete implementations. Add a row and pick a different " +
                    "effect type to see it change shape."),
                MakeDemo("Large Inventory (paged)", inv, ShowcaseLayouts.Inventory,
                    "120 items. Pagination renders only 25 rows at a time (Prev/Next); click a column header to sort " +
                    "(view-only — the data order never changes); the search box filters before paging. Sorting pauses " +
                    "drag-reorder, with a 'clear' shortcut back to natural order.")));

            // --- Buttons (single page) ---
            Func<List<UnityEngine.Object>, ScriptableObject> btns = sink => Reg<ShowcaseButtonsTarget>(sink);
            groups.Add(Group(MakeDemo("Buttons", btns, ShowcaseLayouts.Buttons,
                "Buttons invoke methods on the target — shown here in all six styles (Modern, Soft, Outline, " +
                "Danger, Ghost, Classic) with varied sizes, alignment, icons, and hover/press feedback. 'Add Gold' " +
                "prompts for its parameter, 'Reset Data' asks for confirmation, and 'Run Task' is async (disables " +
                "until it finishes).")));

            // --- Animations (single page) ---
            Func<List<UnityEngine.Object>, ScriptableObject> anim = sink => Reg<ShowcaseAnimationsTarget>(sink);
            var animDemo = MakeDemo("Animations", anim, ShowcaseLayouts.Animations,
                "One row per trigger, then one button per effect. The top field fades in on Appear (use Replay to see " +
                "it again), the second pulses whenever the cursor passes over it (Hover — no click, no edit), and the " +
                "number field pops when its value changes (edit it and commit). Each Click button below plays a " +
                "different effect; the Spring ones are physics-driven and mashable.");
            animDemo.IsAnimations = true;
            groups.Add(Group(animDemo));

            // --- Live Quick-Edit (single page) ---
            groups.Add(Group(MakeDemo("Live Quick-Edit", commonFields, ShowcaseLayouts.QuickEdit,
                "Right-click any field — here or on any page — to change its display type, label, validation, and more, " +
                "inline, with no designer. Sandboxed: hit Reset to Defaults to revert.")));

            // --- Live Debug (single page) ---
            groups.Add(Group(MakeDemo("Live Debug", commonFields, ShowcaseLayouts.LiveDebug,
                "Watch your values while the game runs, in the inspector you already have open. Click the BUG " +
                "GLYPH in the Script Header at the top of this page and every field gains a trace strip — a " +
                "rolling graph of what the value did, the range it covered, and a pin for each Debug.Log the " +
                "object wrote at that moment. A transport bar pins itself to the top: scrub it, or click a sample " +
                "for a moment card with the exact value, the frame, and the console output around it. " +
                "This page drives its own values from a demo clock, so the trace fills right here; on your own " +
                "scripts recording runs in PLAY MODE, so you arm it and press Play. Ctrl+Alt+R starts and stops every " +
                "armed script, Ctrl+Alt+M drops a marker, and SasquatchDebug.Record(this) does the same from your " +
                "own code — see the second card.")));

            // --- Bonuses (parent + child pages, pinned to the bottom) ---
            Func<List<UnityEngine.Object>, ScriptableObject> bonus = sink => Reg<ShowcaseBonusesTarget>(sink);
            groups.Add(Group(
                MakeDemo("Bonuses", bonus, ShowcaseLayouts.Bonuses,
                    "Extras bundled with the tool, each in its own expandable card: a SceneReference field, a " +
                    "SasquatchString (a string whose {tokens} resolve at runtime), the SpringConfig Spring display " +
                    "type, a SerializedDictionary (a dictionary that reorders, nests and multi-edits), and the " +
                    "SasquatchSpring physics-tween sandbox."),
                MakeDemo("Scene Reference", bonus, ShowcaseLayouts.Bonuses_Scene,
                    "A rename-safe way to reference a Scene asset — stores the GUID, exposes the path + name at runtime."),
                MakeDemo("SasquatchString", bonus, ShowcaseLayouts.Bonuses_SasqString,
                    "A drop-in replacement for `string` whose {fieldName} tokens resolve to live values EVERYWHERE " +
                    "— not just in this preview. Change a field's type from string to SasquatchString and " +
                    "label.text = weapon.description prints \"Deals 12 Fire damage.\" at runtime: TextMeshPro, " +
                    "uGUI, UI Toolkit, Debug.Log, plain concatenation. No component to add, nothing to install, " +
                    "and it's a struct so it can never be null. Edit Ammo or Element and watch the preview move."),
                MakeDemo("Spring", bonus, ShowcaseLayouts.Bonuses_Spring,
                    "A SpringConfig edited with the custom Spring display type — a response graph plus a drag-to-feel slider."),
                MakeDemo("Serializable Dictionary", bonus, ShowcaseLayouts.Bonuses_Dictionary,
                    "A SerializedDictionary<K,V> — drawn as a Key/Value table with live duplicate-key detection, " +
                    "drag-reorder and any-type values. Unity serializes Dictionary<,> itself from 6.6; this works " +
                    "back to 6.0, nests inside a List<>, and keeps the order you drag rows into."),
#if UNITY_6000_6_OR_NEWER
                MakeDemo("Dictionary Table (native)", bonus, ShowcaseLayouts.Bonuses_NativeDictionary,
                    "Unity 6.6's OWN Dictionary<K,V> drawn with the Dictionary Table display type — drag-reorder " +
                    "that saves the order, a per-row menu and real value editors on top of Unity's serialization. " +
                    "Gated to 6.6+: on 6.0–6.5 a plain Dictionary does not serialize at all."),
#endif
                MakeDemo("Spring Physics Tweens", bonus, ShowcaseLayouts.Bonuses_Sandbox,
                    "Open the SasquatchSpring sandbox — the bonus spring-physics tween library.")));

            return groups;
        }

        private static Demo MakeDemo(string title, Func<List<UnityEngine.Object>, ScriptableObject> create,
            Func<LayoutNode> layout, string notes, string codeDescription = null) =>
            new Demo { Title = title, Key = title, CreateTarget = create, BuildLayout = layout, Notes = notes, CodeDescription = codeDescription };

        private static Demo DemoThemed(string title, Func<List<UnityEngine.Object>, ScriptableObject> create,
            Func<LayoutNode> layout, Func<List<UnityEngine.Object>, SasquatchThemeAsset> theme, string notes) =>
            new Demo { Title = title, Key = title, CreateTarget = create, BuildLayout = layout, BuildTheme = theme, Notes = notes };

        private static CategoryGroup Group(Demo parent, params Demo[] children)
        {
            var g = new CategoryGroup { Parent = parent };
            g.Children.AddRange(children);
            return g;
        }
    }
}
