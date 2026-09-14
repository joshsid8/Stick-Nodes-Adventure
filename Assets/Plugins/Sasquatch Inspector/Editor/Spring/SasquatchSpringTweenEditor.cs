using System.Collections.Generic;
using System.Linq;
using SasquatchSpring;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// UI Toolkit inspector for <see cref="SasquatchSpringTween"/>, assembled from Sasquatch Inspector's own
    /// widgets: it hands a hand-authored, in-memory <see cref="InspectorLayoutAsset"/> to
    /// <see cref="LayoutTreeBuilder"/>, so the tween inherits everything the layout designer offers — axis color
    /// pips on the vector fields, the palette-bar color fields, rounded toggles, the interactive Spring config
    /// knob, the custom UnityEvent card, and Modern action buttons — with no bespoke widget code here.
    ///
    /// The confusing "one target of every type" problem is solved with live <see cref="ConditionRule"/>s: only
    /// the Start / Target field that matches the chosen <c>action</c> is ever shown (a Move shows a Vector, a Fade
    /// shows a float, a Color action shows a color). Start sits above Target so it reads "from → to". Play / Stop
    /// are shown only for the Manual trigger (the other triggers auto-fire) and stay disabled outside Play mode,
    /// so edit mode can never mutate the real transform or spawn a stray spring agent.
    /// </summary>
    [CustomEditor(typeof(SasquatchSpringTween))]
    [CanEditMultipleObjects]
    public sealed class SasquatchSpringTweenEditor : UnityEditor.Editor
    {
        // ── Property paths (nameof keeps them refactor-proof) ──────────────────────────────────────────────
        private static class P
        {
            public const string Action = nameof(SasquatchSpringTween.action);
            public const string Trigger = nameof(SasquatchSpringTween.trigger);
            public const string Loop = nameof(SasquatchSpringTween.loop);
            public const string VectorTarget = nameof(SasquatchSpringTween.vectorTarget);
            public const string FloatTarget = nameof(SasquatchSpringTween.floatTarget);
            public const string ColorTarget = nameof(SasquatchSpringTween.colorTarget);
            public const string Relative = nameof(SasquatchSpringTween.relative);
            public const string UseFromValue = nameof(SasquatchSpringTween.useFromValue);
            public const string FromVector = nameof(SasquatchSpringTween.fromVector);
            public const string FromFloat = nameof(SasquatchSpringTween.fromFloat);
            public const string FromColor = nameof(SasquatchSpringTween.fromColor);
            public const string Config = nameof(SasquatchSpringTween.config);
            public const string OnComplete = nameof(SasquatchSpringTween.onComplete);
        }

        // The Action values that share a value TYPE. Conditions key off these so exactly one Start and one Target
        // field is visible for any Action (Vector → vector fields, Fade → float, Color → color).
        private static readonly string[] VectorActions =
        {
            nameof(SasquatchSpringTween.SpringAction.MovePosition),
            nameof(SasquatchSpringTween.SpringAction.MoveLocalPosition),
            nameof(SasquatchSpringTween.SpringAction.Scale),
            nameof(SasquatchSpringTween.SpringAction.Rotate),
            nameof(SasquatchSpringTween.SpringAction.AnchoredPosition),
            nameof(SasquatchSpringTween.SpringAction.SizeDelta),
        };
        private static readonly string[] FadeActions =
        {
            nameof(SasquatchSpringTween.SpringAction.FadeCanvasGroup),
            nameof(SasquatchSpringTween.SpringAction.FadeImage),
            nameof(SasquatchSpringTween.SpringAction.FadeSprite),
        };
        private static readonly string[] ColorActions =
        {
            nameof(SasquatchSpringTween.SpringAction.ColorGraphic),
        };

        private VisualElement _root;
        private InspectorLayoutAsset _fieldsLayout;
        private InspectorLayoutAsset _footerLayout;
        private readonly List<Button> _playStopButtons = new List<Button>();
        private HelpBox _editModeHint;

        private void OnEnable()
        {
            // A structural undo (e.g. removing a UnityEvent listener) needs a fresh build; value edits refresh
            // through the bindings on their own.
            Undo.undoRedoPerformed += RebuildInspector;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildInspector;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            // The layouts are transient, editor-only scaffolding — free them with the editor.
            if (_fieldsLayout != null) DestroyImmediate(_fieldsLayout);
            if (_footerLayout != null) DestroyImmediate(_footerLayout);
        }

        /// <summary>Unity entry point: builds the styled inspector for the selected tween(s).</summary>
        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            RebuildInspector();
            return _root;
        }

        // Rebuilds the body in place. Also the onRebuildRequired handed to the builder, so a widget that needs a
        // structural refresh (the UnityEvent card adding/removing a call) re-lays the whole editor.
        private void RebuildInspector()
        {
            if (_root == null) return;
            _root.Clear();
            _playStopButtons.Clear();
            _editModeHint = null;
            serializedObject.Update();

            // Unity's overloaded == is the correct null test here (a destroyed asset is "== null" but not `is null`).
            if (_fieldsLayout == null) _fieldsLayout = BuildFieldsLayout();
            if (_footerLayout == null) _footerLayout = BuildFooterLayout();

            // Defense in depth: a widget throw should degrade to Unity's default inspector, never a blank one.
            try
            {
                _root.Add(LayoutTreeBuilder.Build(_fieldsLayout, serializedObject, RebuildInspector));

                var footerView = LayoutTreeBuilder.Build(_footerLayout, serializedObject, RebuildInspector);
                footerView.style.marginTop = 8;
                _root.Add(footerView);

                // The footer is isolated (its own build), so these queries only see the two action buttons and the
                // one hint — safe to drive the Play-mode enable state without touching the widgets' own buttons.
                _playStopButtons.AddRange(footerView.Query<Button>().ToList());
                _editModeHint = footerView.Q<HelpBox>();
                RefreshPlayState();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Sasquatch Inspector] Building the Spring Tween inspector threw — showing the " +
                                 $"default inspector instead. {ex}");
                _root.Clear();
                _playStopButtons.Clear();
                _editModeHint = null;
                InspectorElement.FillDefaultInspector(_root, serializedObject, this);
                // RebuildInspector also runs on undo/redo and on a widget's rebuild request, long after Unity
                // bound the tree CreateInspectorGUI returned — and FillDefaultInspector's fresh PropertyFields
                // build nothing until they are bound. Without this the "degrade to the default inspector"
                // rescue would hand back the blank inspector it exists to prevent (same rule as
                // VisualInspectorEditor.DrawDefaultFallback).
                _root.Bind(serializedObject);
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange _) => RefreshPlayState();

        // Play / Stop drive the live spring engine, so they only make sense in Play mode; in edit mode they stay
        // greyed and the hint explains why. (The Manual-only visibility is handled by a condition on the footer.)
        private void RefreshPlayState()
        {
            bool playing = Application.isPlaying;
            foreach (var b in _playStopButtons) b?.SetEnabled(playing);
            if (_editModeHint != null)
                _editModeHint.style.display = playing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ── Layout authoring ───────────────────────────────────────────────────────────────────────────────

        private static InspectorLayoutAsset BuildFieldsLayout()
        {
            var root = new ContainerNode { Type = ContainerType.Rows };

            // What & When — the Action drives which fields below are relevant.
            root.Children.Add(Field(P.Action, DisplayType.Default, header: "What & When"));
            root.Children.Add(Field(P.Trigger, DisplayType.Default));
            root.Children.Add(Field(P.Loop, DisplayType.Default));

            // Start (optional) — above Target so the inspector reads "from → to". The explicit start values only
            // appear once "Use From Value" is on AND they match the Action type.
            root.Children.Add(Field(P.UseFromValue, DisplayType.Toggle, header: "Start (optional)",
                toggle: ToggleStyle.Rounded));
            root.Children.Add(Field(P.FromVector, DisplayType.Default, vectorPips: true,
                rule: ShowIfFromValueAndActionIn(VectorActions)));
            root.Children.Add(Field(P.FromFloat, DisplayType.Default,
                rule: ShowIfFromValueAndActionIn(FadeActions)));
            root.Children.Add(Field(P.FromColor, DisplayType.ColorPalette, colorBar: true,
                rule: ShowIfFromValueAndActionIn(ColorActions)));

            // Target — the same "Target" header on all three so it survives whichever one the Action reveals.
            root.Children.Add(Field(P.VectorTarget, DisplayType.Default, header: "Target", vectorPips: true,
                rule: ShowIfActionIn(VectorActions)));
            root.Children.Add(Field(P.FloatTarget, DisplayType.Default, header: "Target",
                rule: ShowIfActionIn(FadeActions)));
            root.Children.Add(Field(P.ColorTarget, DisplayType.ColorPalette, header: "Target", colorBar: true,
                rule: ShowIfActionIn(ColorActions)));
            // "Relative" only means anything for the vector actions (it's a positional offset).
            root.Children.Add(Field(P.Relative, DisplayType.Toggle, toggle: ToggleStyle.Rounded,
                rule: ShowIfActionIn(VectorActions)));

            // Feel — the SpringConfig knob (auto-routed to the interactive Spring widget) tucked inside an
            // expandable card so it can be collapsed out of the way. The card header supplies the "Feel" title.
            var feelCard = new ContainerNode
            {
                Type = ContainerType.Card,
                Expandable = true,
                DefaultToOpen = true,
                Title = "Feel",
            };
            feelCard.Children.Add(Field(P.Config, DisplayType.Spring));
            root.Children.Add(feelCard);

            // Events — the custom UnityEvent card.
            root.Children.Add(Field(P.OnComplete, DisplayType.UnityEvent, header: "Events"));

            return WrapLayout(root);
        }

        private static InspectorLayoutAsset BuildFooterLayout()
        {
            // Whole footer shows only for the Manual trigger (OnEnable / OnStart fire the tween on their own).
            var root = new ContainerNode { Type = ContainerType.Rows, Gap = 6 };
            root.ConditionRules.Add(ShowIfTriggerManual());

            // Play / Stop side by side; Disabled tracks so there's no stray resize divider between the buttons.
            var buttonRow = new ContainerNode
            {
                Type = ContainerType.Columns,
                Gap = 6,
                TrackResize = TrackResizeMode.Disabled,
            };
            buttonRow.Children.Add(ActionButton(nameof(SasquatchSpringTween.Play), "Play"));
            buttonRow.Children.Add(ActionButton(nameof(SasquatchSpringTween.Stop), "Stop"));
            root.Children.Add(buttonRow);

            root.Children.Add(new HelpBoxNode
            {
                Message = "Enter Play mode to fire the tween from these buttons " +
                          "(the Manual trigger only plays from here — also handy for testing a Yoyo loop).",
                MessageType = HelpBoxMessageType.Info,
            });

            return WrapLayout(root);
        }

        // Builds a single field binding with the requested widget + options.
        private static BindingNode Field(string path, DisplayType type, string header = null,
            bool vectorPips = false, bool colorBar = false, ToggleStyle? toggle = null, ConditionRule rule = null)
        {
            var node = new BindingNode { PropertyPath = path, DisplayType = type };
            if (!string.IsNullOrEmpty(header)) node.Header = header;
            if (vectorPips) node.VectorAxisPips = true;               // axis-colored pips (X red / Y green / Z blue)
            if (colorBar)                                             // palette bar + current-color field on the right
            {
                node.ColorPaletteLayout = ColorPaletteLayout.Bar;
                node.ColorShowBar = true;
                node.ColorBarPlacement = ColorBarPlacement.Right;
            }
            if (toggle.HasValue)
                node.DisplayStyleData = new DisplayStyleData { toggleStyle = toggle.Value };
            if (rule != null) node.ConditionRules.Add(rule);
            return node;
        }

        private static ButtonNode ActionButton(string method, string text) => new ButtonNode
        {
            MethodName = method,
            ButtonText = text,
            StylePreset = ButtonStyle.Modern,
            FullWidth = true,
        };

        // Show when `action` is any of the given members (an OR over enum-equality terms; the condition value is
        // the enum member NAME, which ConditionEvaluator parses).
        private static ConditionRule ShowIfActionIn(string[] actionNames)
        {
            var rule = new ConditionRule(ConditionOutcome.Show, ConditionMatch.Any);
            foreach (var name in actionNames)
                rule.Items.Add(ConditionGroup.Leaf(new VisualCondition(P.Action, ConditionOperator.Equals, name)));
            return rule;
        }

        // Show when "Use From Value" is on AND `action` is any of the given members.
        private static ConditionRule ShowIfFromValueAndActionIn(string[] actionNames)
        {
            var rule = new ConditionRule(ConditionOutcome.Show, ConditionMatch.All);
            rule.Items.Add(ConditionGroup.Leaf(new VisualCondition(P.UseFromValue, ConditionOperator.IsTrue)));

            var actionGroup = new ConditionGroup
            {
                IsGroup = true,
                Match = ConditionMatch.Any,
                Conditions = new List<VisualCondition>(),
            };
            foreach (var name in actionNames)
                actionGroup.Conditions.Add(new VisualCondition(P.Action, ConditionOperator.Equals, name));
            rule.Items.Add(actionGroup);
            return rule;
        }

        private static ConditionRule ShowIfTriggerManual()
        {
            var rule = new ConditionRule(ConditionOutcome.Show, ConditionMatch.All);
            rule.Items.Add(ConditionGroup.Leaf(new VisualCondition(
                P.Trigger, ConditionOperator.Equals, nameof(SasquatchSpringTween.TriggerMode.Manual))));
            return rule;
        }

        // Wraps a node tree in a transient, unsaved layout asset for LayoutTreeBuilder to render.
        private static InspectorLayoutAsset WrapLayout(LayoutNode root)
        {
            var asset = CreateInstance<InspectorLayoutAsset>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.Root = root;
            return asset;
        }
    }
}
