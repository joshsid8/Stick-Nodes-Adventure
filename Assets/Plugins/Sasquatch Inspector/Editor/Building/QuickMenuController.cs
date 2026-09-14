using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Capture-phase right-click takeover for the LIVE inspector. A bubble-phase
    /// ContextualMenuManipulator on the built root would be at the mercy of every child control:
    /// TextField, ObjectField, Toggle, MinMaxSlider and friends consume the right-click before it
    /// bubbles, so the quick menu would never appear on exactly those widgets. Here the root intercepts
    /// the right-click in TrickleDown (the capture phase visits the root BEFORE the target, so no
    /// child StopPropagation or pointer capture can starve us), suppresses every native/child menu
    /// path, and shows ONE unified menu composed from: rebuilt native essentials
    /// (QuickMenuNativeEssentials) → the quick-edit chain (QuickEditMenu.AppendChainEntries) →
    /// per-element contributors (QuickMenuContributors) → an unconditional "Open in Layout Designer"
    /// footer. The core guarantee: ANY element under the layout root gets the menu for free — no
    /// per-widget opt-in, current or future.
    /// </summary>
    public static class QuickMenuController
    {
        /// <summary>
        /// Test seam: when set, Show hands the composed model here instead of opening UI. Public rather
        /// than internal because the verification tests live in a separate assembly. Never set
        /// outside tests.
        /// </summary>
        public static Action<QuickMenuModel, VisualElement, Vector2> ShowOverrideForTests;

        // How long a stashed right-press stays honourable. Past this, the release that never reached this
        // handler is treated as an abandoned gesture rather than a pending menu.
        private const double StaleRightPressSeconds = 5.0;

        /// <summary>Installs the capture-phase right-click takeover on <paramref name="builtRoot"/>, composing and showing the one unified menu.</summary>
        public static void Attach(VisualElement builtRoot, QuickEditContext ctx)
        {
            if (builtRoot == null || ctx?.LayoutAsset == null) return;

            // Right-press stashed on down, menu shown on up (Windows convention) — and BOTH halves
            // are stopped immediately so neither the child control nor the editor's native context-menu
            // manager (which opens the native menu from the MouseUp path on Windows) ever reacts. A left
            // press clears the stash so a lost up-event can't fire a stale menu later.
            VisualElement pendingTarget = null;
            Vector2 pendingPos = default;
            int pendingPointerId = PointerId.invalidPointerId;
            double pendingAt = 0;

            builtRoot.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) { pendingTarget = null; return; }
                pendingTarget = evt.target as VisualElement;
                pendingPos = new Vector2(evt.position.x, evt.position.y);
                pendingPointerId = evt.pointerId;
                pendingAt = EditorApplication.timeSinceStartup;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            builtRoot.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 1) return;
                evt.StopImmediatePropagation();
                // Nobody holds the right button for seconds meaning to open a menu, so an aged stash is a
                // gesture that ended somewhere this handler never saw. Clear it rather than honour it.
                if (pendingTarget == null || evt.pointerId != pendingPointerId
                    || EditorApplication.timeSinceStartup - pendingAt > StaleRightPressSeconds)
                {
                    pendingTarget = null;
                    pendingPointerId = PointerId.invalidPointerId;
                    return;
                }
                var target = pendingTarget;
                var pos = pendingPos;
                pendingTarget = null;
                Show(BuildMenu(ctx, builtRoot, target), builtRoot, pos);
            }, TrickleDown.TrickleDown);

            // A gesture whose right-UP never arrives here leaves the stash armed, and the next right-up
            // carrying the same pointer id then opens a menu for a row the user abandoned — a press that
            // started outside the inspector and released inside it is enough. A LEFT press already cleared
            // the stash; an explicit cancel and an age limit are the other two ends.
            //
            // Deliberately NOT PointerLeaveEvent: the panel's pointer-position updater synthesizes
            // enter/leave around a dispatch, so clearing on leave disarms legitimate presses (it broke
            // TestQuickMenuParityAcrossRenderPaths on both render paths).
            builtRoot.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (evt.pointerId != pendingPointerId) return;
                pendingTarget = null;
                pendingPointerId = PointerId.invalidPointerId;
            }, TrickleDown.TrickleDown);

            // Compatibility mouse events are dispatched alongside pointer events and their
            // interleaving has shifted across editor versions — suppress them explicitly rather
            // than trusting the paired-stop behavior of any particular version.
            builtRoot.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 1) evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            builtRoot.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button == 1) evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            // Belt and braces: even if some path still synthesizes a context-menu event (IMGUI
            // bridge, text fields, OS menu key), kill it before any child or the panel's menu
            // manager can open a competing menu.
            builtRoot.RegisterCallback<ContextClickEvent>(evt => evt.StopImmediatePropagation(),
                TrickleDown.TrickleDown);
            builtRoot.RegisterCallback<ContextualMenuPopulateEvent>(evt => evt.StopImmediatePropagation(),
                TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Pure composition — no UI. Sections add unconditionally; QuickMenuModel collapses the
        /// separators between empty neighbors. This is the unit-test surface.
        /// </summary>
        public static QuickMenuModel BuildMenu(QuickEditContext ctx, VisualElement builtRoot, VisualElement deepTarget)
        {
            var model = new QuickMenuModel();
            if (deepTarget == null) deepTarget = builtRoot;

            var request = new QuickMenuRequest
            {
                Ctx = ctx,
                Root = builtRoot,
                DeepTarget = deepTarget
            };

            QuickMenuNativeEssentials.Contribute(request, model);
            model.AddSeparator();
            QuickEditMenu.AppendChainEntries(model, ctx, builtRoot, deepTarget);
            model.AddSeparator();
            QuickMenuContributors.Collect(deepTarget, builtRoot, request, model);
            model.AddSeparator();
            AppendOpenInDesigner(model, ctx, builtRoot, deepTarget);
            return model;
        }

        // The footer is UNCONDITIONAL: a right-click on layout margins (empty chain) still gets a
        // menu instead of nothing. With a chain, the deepest editable node becomes the deep-link.
        private static void AppendOpenInDesigner(QuickMenuModel model, QuickEditContext ctx,
            VisualElement builtRoot, VisualElement deepTarget)
        {
            var root = ctx?.LayoutAsset?.Root;
            System.Collections.Generic.List<int> deepestPath = null;
            LayoutOwnerScope deepestScope = null;
            if (root != null)
            {
                var chain = QuickEditMenu.ComputeAncestorChain(deepTarget, builtRoot);
                QuickEditMenu.DeepestEditable(chain, root, out deepestScope, out deepestPath);
            }

            // Condition Debugger toggle — only when the host wired it up AND this layout actually has conditions,
            // sitting right above the designer deep-link. The host owns the on/off state + the rebuild.
            if (ctx?.GetDebugConditions != null && ctx.SetDebugConditions != null && ConditionBinder.LayoutHasConditions(root))
            {
                bool on = ctx.GetDebugConditions();
                model.Add(on ? "Debug Conditions  ✓" : "Debug Conditions", () => ctx.SetDebugConditions(!on));
            }

            model.Add("Open in Layout Designer", () =>
            {
                // Deep-link into the OWNING layout: inside an inline editor that's the nested object/type
                // and a path relative to the nested root, not the outer one (falls back to ctx.Target for
                // a margin click with no chain).
                var target = deepestScope?.Target ?? ctx.Target;
                var window = SasquatchLayoutDesignerWindow.OpenWindowForType(target.GetType(), target);
                if (window != null && deepestPath != null) window.SelectNodeByIndexPath(deepestPath);
            });
        }

        private static void Show(QuickMenuModel model, VisualElement builtRoot, Vector2 panelPosition)
        {
            if (model == null || !model.HasActionableItems) return;
            if (ShowOverrideForTests != null)
            {
                ShowOverrideForTests(model, builtRoot, panelPosition);
                return;
            }

            // GenericMenu = the native OS menu, identical look to the contextual menus users see
            // everywhere else in the editor. Event.current is set while the editor panel dispatches
            // pointer events (IMGUI event loop drives UITK panels); if some path arrives without
            // it, the UITK dropdown at the click position is a faithful stand-in.
            if (Event.current != null)
            {
                var menu = new GenericMenu();
                foreach (var item in model.Items)
                {
                    if (item.IsSeparator) { menu.AddSeparator(""); continue; }
                    // GenericMenu treats '/' as a submenu split — node titles may contain one.
                    var content = new GUIContent(item.Label.Replace("/", "∕"));
                    if (!item.Enabled) { menu.AddDisabledItem(content); continue; }
                    var action = item.Action;
                    menu.AddItem(content, false, () => action?.Invoke());
                }
                menu.ShowAsContext();
            }
            else
            {
                var menu = new GenericDropdownMenu();
                foreach (var item in model.Items)
                {
                    if (item.IsSeparator) { menu.AddSeparator(""); continue; }
                    if (!item.Enabled) { menu.AddDisabledItem(item.Label, false); continue; }
                    var action = item.Action;
                    menu.AddItem(item.Label, false, () => action?.Invoke());
                }
                // GenericDropdownMenu.DropDown swapped its 3rd arg from `bool anchored` to
                // DropdownMenuSizeMode in Unity 6000.3; content-sizing == anchored:false on the older signature.
#if UNITY_6000_3_OR_NEWER
                menu.DropDown(new Rect(panelPosition, Vector2.zero), builtRoot, DropdownMenuSizeMode.Content);
#else
                menu.DropDown(new Rect(panelPosition, Vector2.zero), builtRoot, anchored: false);
#endif
            }
        }
    }
}
