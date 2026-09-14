using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;
using System.Collections.Generic;
using System.Linq;

namespace SasquatchInspector.Editor
{
    // These two compile-time editors make Sasquatch Inspector's VisualInspectorEditor the resolved inspector for every
    // MonoBehaviour / ScriptableObject that has no editor of its own. They are `isFallback` child-class globals:
    // Unity's resolver runs a NON-fallback pass first across the whole inheritance chain and only consults
    // fallback editors when that finds nothing, so ANY real [CustomEditor] — the developer's own or a third
    // party's — outranks us and draws instead. That IS the deference contract, enforced by Unity itself. For a
    // type with no custom editor we win over Unity's generic inspector, and VisualInspectorEditor then decides
    // per-instance whether to draw the saved layout or fall back to the default inspector.
    /// <summary>Fallback inspector for any MonoBehaviour without its own [CustomEditor]; draws the saved Sasquatch Inspector layout or the default inspector. See <see cref="VisualInspectorEditor"/>.</summary>
    [CustomEditor(typeof(MonoBehaviour), editorForChildClasses: true, isFallback = true)]
    [CanEditMultipleObjects]
    public class SasquatchMonoBehaviourFallbackEditor : VisualInspectorEditor {}

    /// <summary>Fallback inspector for any ScriptableObject without its own [CustomEditor]; draws the saved Sasquatch Inspector layout or the default inspector. See <see cref="VisualInspectorEditor"/>.</summary>
    [CustomEditor(typeof(ScriptableObject), editorForChildClasses: true, isFallback = true)]
    [CanEditMultipleObjects]
    public class SasquatchScriptableObjectFallbackEditor : VisualInspectorEditor {}

    /// <summary>
    /// Base inspector that renders a type's saved Sasquatch Inspector layout via <see cref="LayoutTreeBuilder"/>, falling
    /// back to Unity's default inspector when the type has no layout or the global override is disabled.
    /// </summary>
    public class VisualInspectorEditor : UnityEditor.Editor
    {
        private VisualElement rootContainer;

        // Appear animations should play only on the first build of this editor session, not on every
        // rebuild (undo/value-change), where replaying them reads as a flicker. A fresh editor instance is
        // created per selection, so this naturally re-plays when the user selects a new object.
        private bool _hasBuiltContent;

        // The Inspector's ScrollView, resolved on attach and remembered so OnDisable can still reach it — by
        // then this body is already on its way out of the panel. See CreateInspectorGUI.
        private ScrollView _hostScroll;

        // "This editor is being DESTROYED, not repopulated." That distinction is the whole reason the scroll
        // guard can tell a selection change from the row rebuild it exists for: a fresh Editor instance is
        // created per selection (see _hasBuiltContent), whereas Unity repopulating a row REUSES this instance
        // and merely calls CreateInspectorGUI again — so OnDisable runs for the first and not the second.
        // Without it the guard reserves the outgoing object's content height across an ordinary selection
        // change and pops the whole layout eight ticks later (see InspectorScrollPreservation).
        private bool _editorTearingDown;

        private void OnEnable()
        {
            _hasBuiltContent = false;
            _editorTearingDown = false;   // instances are reused across a domain reload
            _undoGate.Invalidate(); // instances are reused across a domain reload; never trust a stale baseline
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;

            // Belt AND braces, because the order of OnDisable against our body's DetachFromPanelEvent is
            // Unity's to change, not ours: the flag stops a detach that runs after this from arming a guard,
            // and the Cancel hands back a reservation a detach that ran BEFORE this already took out.
            _editorTearingDown = true;
            InspectorScrollPreservation.Cancel(_hostScroll);
            _hostScroll = null;
        }

        // Baseline for the undo gate — see UndoRebuildGate. Per-editor, so each surface judges its own target.
        private readonly UndoRebuildGate _undoGate = new UndoRebuildGate();
        private InspectorLayoutAsset _builtLayoutAsset; // what the last build rendered, for the gate's baseline

        private void ForceVisualRebuild()
        {
            RebuildInspectorGUI();
        }

        /// <summary>
        /// The <c>Undo.undoRedoPerformed</c> entry point. Undo is a GLOBAL editor event with no payload, so
        /// this fires for every undo anywhere — a Scene-view transform move included — and a rebuild here
        /// costs 1.28ms on a 29-field layout and 21.4ms on a 200-row table, per open surface, while the user
        /// holds Ctrl+Z. The gate answers the narrow question the rebuild exists for (did anything STRUCTURAL
        /// move?) for ~0.027ms, and rebuilds whenever it cannot prove the answer is no.
        ///
        /// <c>serializedObject.Update()</c> comes FIRST and unconditionally: after an undo Unity has already
        /// reverted the target while this editor still holds its pre-undo cache, so the gate must compare
        /// against the post-undo state — and the live bindings need the refreshed cache regardless of whether
        /// a rebuild follows.
        /// </summary>
        private void OnUndoRedo()
        {
            if (_editorTearingDown || target == null) return;
            try { serializedObject.Update(); }
            catch { ForceVisualRebuild(); return; } // disposed under us — rebuild rather than reason about it

            if (_undoGate.ShouldRebuild(serializedObject, _builtLayoutAsset))
                ForceVisualRebuild();
        }

        /// <summary>
        /// Rebuilds this editor's body IN PLACE so a just-saved layout for this type shows immediately. This is the
        /// targeted post-save refresh seam (<see cref="SasquatchEditorRegistrar.RefreshInspectorsForType"/>) that
        /// replaces the global selection nudge: only the affected editor rebuilds, so unrelated components, previews,
        /// and other inspectors don't churn. Safe before <see cref="CreateInspectorGUI"/> has run — rootContainer is
        /// null then and <see cref="RebuildInspectorGUI"/> no-ops, and the deferred CreateInspectorGUI builds fresh.
        /// </summary>
        internal void RebuildForLayoutChange() => RebuildInspectorGUI();

        /// <summary>Unity entry point: builds and returns the inspector's UI Toolkit root for the selected target.</summary>
        public override VisualElement CreateInspectorGUI()
        {
            rootContainer = new VisualElement();
            RebuildInspectorGUI();

            // Inject the header pencil. Our fallback editor is now the active editor for every component without
            // its own [CustomEditor], so OUR editor draws here and we have `target` directly. The pencil lands on
            // the per-component header HOST (the InspectorElement's parent); it survives the rootContainer.Clear()
            // in RebuildInspectorGUI because it lives on the host, not inside rootContainer. TryInject self-gates
            // (MonoBehaviour-only + IsTypeEditable + override
            // enabled) and is idempotent, so it stays a no-op for non-editable types and never double-injects with
            // the global scan (SasquatchInspectorHeaderPencil.ScanRoot). Components drawn by a third party's own
            // [CustomEditor] (where we deferred) still get the pencil from that global scan. SO/asset MAIN headers
            // use the IMGUI finishedDefaultHeaderGUI path, so TryInject's MonoBehaviour guard skips them here.
            rootContainer.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (target == null) return;
                var host = rootContainer.GetFirstAncestorOfType<InspectorElement>()?.parent;
                SasquatchInspectorHeaderPencil.TryInject(host, target.GetType(), target);
            });

            // Scroll survival across a row REPOPULATION, in parity with the displacement path
            // (SasquatchForceTakeover). Unity rebuilds a row by throwing this body away and calling
            // CreateInspectorGUI again — measured on a locked inspector, the row's content identity changes on
            // every Project-window click. The capture inside RebuildInspectorGUI cannot see that coming: it runs
            // on a brand-new rootContainer that is not in the panel yet, so it finds no ScrollView and protects
            // nothing (traced: the guard's hold counter stayed at 0 for the whole session). The OLD body's
            // detach is the only moment that still knows both the scroll position and the height it had.
            float lastContentHeight = 0f;
            rootContainer.RegisterCallback<AttachToPanelEvent>(_ =>
                _hostScroll = InspectorScrollPreservation.FindScrollView(rootContainer));
            rootContainer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_hostScroll != null) lastContentHeight = _hostScroll.contentContainer.layout.height;
            });
            rootContainer.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // A detach because this EDITOR is going away is a selection change, not the row repopulation
                // this guard is for — holding the outgoing object's height across it pops the incoming layout.
                if (_editorTearingDown) return;
                InspectorScrollPreservation.HoldAcrossRebuild(_hostScroll, lastContentHeight);
            });

            return rootContainer;
        }

        private void RebuildInspectorGUI()
        {
            if (rootContainer == null) return;

            // Capture current scroll position of parent ScrollView
            ScrollView scrollView = null;
            Vector2 scrollOffset = Vector2.zero;
            VisualElement current = rootContainer.parent;
            while (current != null)
            {
                if (current is ScrollView sv)
                {
                    scrollView = sv;
                    scrollOffset = sv.scrollOffset;
                    break;
                }
                current = current.parent;
            }

            // Capture focused element path
            Focusable focusedElement = rootContainer.panel?.focusController?.focusedElement;
            string focusedBindingPath = (focusedElement as BindableElement)?.bindingPath;
            string focusedName = (focusedElement as VisualElement)?.name;

            // Lock the height to prevent scroll clamp jump during container rebuild
            float currentHeight = rootContainer.resolvedStyle.height;
            if (!float.IsNaN(currentHeight) && currentHeight > 0)
            {
                rootContainer.style.height = currentHeight;
            }

            rootContainer.Clear();

            // 1. Check if global override is enabled in EditorPrefs
            if (!SasquatchEditorMenu.IsOverrideEnabled())
            {
                DrawDefaultFallback(rootContainer);
                RestoreScrollAndFocus(scrollView, scrollOffset, focusedBindingPath, focusedName);
                return;
            }

            // 2. Safeguard target
            if (target == null)
            {
                DrawDefaultFallback(rootContainer);
                RestoreScrollAndFocus(scrollView, scrollOffset, focusedBindingPath, focusedName);
                return;
            }

            // Sync the SerializedObject before the rebuild reads it. After an undo/redo (Undo.undoRedoPerformed
            // → ForceVisualRebuild) Unity has already reverted the target, but this editor's SerializedObject
            // still holds its pre-undo cache — so without this, the rebuild reads a stale array size and a
            // structural change (e.g. an undone table-row delete) wouldn't reappear until the object is
            // reselected. (Plain value edits are masked because the binding system re-reads each frame.)
            serializedObject.Update();

            System.Type targetType = target.GetType();

            // 3. Query the registry for a registered layout asset. With no layout (or override off)
            // we draw the completely normal inspector — entering the designer is a deliberate action
            // via the component's context / ⋮ menu (see SasquatchContextMenu), not an always-on button.
            //
            // Top-level defense-in-depth boundary: every leaf/widget path is individually guarded, so no throw is
            // known to reach here — but if one ever did (BuildNode / ReconcileLayout), it would propagate out of
            // CreateInspectorGUI, blank the inspector, and re-log on every repaint. Degrade to Unity's default
            // inspector instead, warned once per target type.
            try
            {
            _builtLayoutAsset = null;
            if (LayoutRegistry.TryGet(targetType, target, out var layoutAsset) && !layoutAsset.LayoutHidden)
            {
                _builtLayoutAsset = layoutAsset; // the undo gate watches this asset's dirty counter
                // 3.5 Unbound-field handling (Project Settings → Sasquatch Inspector). AutoAppend reconciles the
                // aliased layout root in place BEFORE Build — adds new fields in code order, removes
                // deleted ones, and renames in place (styling survives) — so this same rebuild renders the
                // result; no extra rebuild request, no loop (ReconcileLayout is a strict no-op when the
                // layout already matches the code). Suppressed while a designer edits this type: its stale
                // clone would clobber the changes on Save; they sync on the next rebuild after it closes.
                var settings = SasquatchSettingsManager.Current;

                // Button rename self-heal — deliberately OUTSIDE the Unbound Field Handling switch: it
                // repairs method bindings the user already made (a renamed method relinks by signature),
                // never imports anything, so the "keep drift visible" contract of the non-AutoAppend modes
                // doesn't apply. Same designer-editing guard as the field sync: the designer's stale clone
                // would clobber a relink on Save, so its own clone heal covers that window instead.
                if (!SasquatchLayoutDesignerWindow.IsEditingType(targetType))
                    ButtonMethodSync.HealLayout(targetType, layoutAsset);

                switch (settings.UnboundFieldHandling)
                {
                    case UnboundFieldSyncMode.AutoAppend:
                        if (!SasquatchLayoutDesignerWindow.IsEditingType(targetType))
                            UnboundFieldSync.ReconcileLayout(targetType, layoutAsset, settings.ApplyScriptAttributesOnSync);
                        break;
                    case UnboundFieldSyncMode.NotificationBanner:
                        if (!SasquatchLayoutDesignerWindow.IsEditingType(targetType))
                        {
                            var banner = UnboundFieldBanner.TryCreate(targetType, layoutAsset,
                                settings.ApplyScriptAttributesOnSync, ForceVisualRebuild);
                            if (banner != null) rootContainer.Add(banner);
                        }
                        break;
                }

                // Condition Debugger: opt-in per-developer (EditorPrefs), toggled from the inspector's right-click
                // menu (see QuickMenuController) — no chrome at the top of the inspector. Only has an effect when
                // this layout actually has conditions. Revealed fields are read-only (the overlay absorbs input).
                bool layoutHasConditions = ConditionBinder.LayoutHasConditions(layoutAsset.Root);
                bool debugConditions = layoutHasConditions && EditorPrefs.GetBool(ConditionDebugOverlay.LivePrefKey, false);

                var layoutView = LayoutTreeBuilder.Build(layoutAsset, serializedObject, ForceVisualRebuild,
                    playAppearAnimations: !_hasBuiltContent,
                    onLayoutEdited: () => LayoutRegistry.PersistLayoutEdit(layoutAsset),
                    debugConditions: debugConditions,
                    // Live Debug in PARITY with the DISPLACEMENT path (SasquatchForceTakeover). This is one of
                    // only two Build callers that is a real Inspector, and the feature is opt-in for exactly
                    // that reason — see LayoutTreeBuilder's note where EnabledForBuild is derived.
                    liveInspector: true);
                _hasBuiltContent = true;
                // Overflow rescue: a broken row that paints past the right edge is otherwise unreachable
                // (the Inspector window scrolls vertically only). Layout-neutral while everything fits.
                rootContainer.Add(HorizontalOverflowRescue.Wrap(layoutView));

                // Field search (in PARITY with the DISPLACEMENT path — SasquatchForceTakeover). The search UI
                // lives inside the Script Header widget; Attach binds it to the filter engine and returns null
                // when the layout carries no search-enabled Script Header.
                InspectorFieldSearch.Attach(layoutView, target);

                // The right-click quick-edit menu is REAL-inspector-only (the designer builds its
                // canvas/preview directly via LayoutTreeBuilder and never passes through here, so this
                // wiring point is the gate). RebuildInspector is the targeted per-editor rebuild — the
                // quick-edit popup must never trigger the registrar's selection nudge, which would
                // steal focus and close it.
                QuickEditMenu.Attach(layoutView, new QuickEditContext
                {
                    LayoutAsset = layoutAsset,
                    SerializedObject = serializedObject,
                    Target = target,
                    RebuildInspector = ForceVisualRebuild,
                    StableRoot = rootContainer,
                    GetDebugConditions = () => EditorPrefs.GetBool(ConditionDebugOverlay.LivePrefKey, false),
                    SetDebugConditions = v => { EditorPrefs.SetBool(ConditionDebugOverlay.LivePrefKey, v); ForceVisualRebuild(); },
                });
            }
            else
            {
                // No layout for this type — OR the layout is HIDDEN (the designer's eye button): draw exactly what
                // Unity's default inspector would. (Our fallback editor is the resolved editor for every type
                // without its own [CustomEditor], so this path is the common case for ordinary components/assets
                // and must stay transparent.)
                //
                // Hidden deliberately lands here rather than in a branch of its own: "show what the user would see
                // without Sasquatch Inspector" IS this path, and routing through it means a hidden layout can never
                // drift from the un-styled rendering. _builtLayoutAsset stays null, so the undo gate treats this
                // exactly like an unstyled component; un-hiding rebuilds via RefreshInspectorsForType, not the gate.
                // The header pencil is still injected by CreateInspectorGUI, so the designer (and the eye button
                // that un-hides) stays one click away — hiding is never a one-way door.
                // PARITY: the displacement path releases the row on the same flag (SasquatchForceTakeover).
                DrawDefaultFallback(rootContainer);
            }
            }
            catch (System.Exception ex)
            {
                // A styled build threw: clear any half-built content and degrade to Unity's default inspector so
                // the user still sees a working (unstyled) inspector rather than a blank one.
                rootContainer.Clear();
                DrawDefaultFallback(rootContainer);
                WarnInspectorBuildFailedOnce(targetType, ex);
                _builtLayoutAsset = null;
            }

            // Baseline for the undo gate: what this build just put on screen. Set at the very end so it can
            // never record a state the user is not looking at, and set on EVERY exit — including the
            // no-layout and degraded paths, where a stale baseline would suppress a rebuild that was needed.
            _undoGate.Capture(serializedObject, _builtLayoutAsset);

            RestoreScrollAndFocus(scrollView, scrollOffset, focusedBindingPath, focusedName);
        }

        // Warn-once per target type + exception type, so a styled-build failure that recurs on every repaint
        // surfaces a single diagnostic rather than flooding the console.
        private static readonly HashSet<string> _warnedBuildFailures = new HashSet<string>();
        private static void WarnInspectorBuildFailedOnce(System.Type targetType, System.Exception ex)
        {
            string typeName = targetType != null ? targetType.FullName : "?";
            if (_warnedBuildFailures.Add(typeName + "|" + ex.GetType().Name))
            {
                Debug.LogWarning($"[Sasquatch Inspector] Building the styled inspector for " +
                                 $"'{(targetType != null ? targetType.Name : "?")}' threw — showing Unity's default " +
                                 $"inspector instead. {ex}");
            }
        }

        private void RestoreScrollAndFocus(ScrollView scrollView, Vector2 scrollOffset, string focusedBindingPath, string focusedName)
        {
            if (scrollView != null)
            {
                rootContainer.schedule.Execute(() =>
                {
                    scrollView.scrollOffset = scrollOffset;
                    rootContainer.style.height = StyleKeyword.Null;

                    // …and keep protecting it across the settle. This single write lands ONE tick after the
                    // rebuild, while the fresh content is still mid-relayout and shorter than its final height,
                    // so on its own it restores a value the ScrollView promptly clamps — the user ends up
                    // partway up the inspector. Releasing the height lock on this same line is what exposes
                    // that. Same guard the DISPLACEMENT path uses (SasquatchForceTakeover): the two render
                    // paths have drifted apart three times before, so scroll survival is wired on BOTH.
                    InspectorScrollPreservation.RestoreAcrossSettle(scrollView, scrollOffset.y);
                }).ExecuteLater(1);
            }
            else
            {
                rootContainer.schedule.Execute(() =>
                {
                    rootContainer.style.height = StyleKeyword.Null;
                }).ExecuteLater(1);
            }

            if (!string.IsNullOrEmpty(focusedBindingPath) || !string.IsNullOrEmpty(focusedName))
            {
                rootContainer.schedule.Execute(() =>
                {
                    VisualElement elementToFocus = null;
                    if (!string.IsNullOrEmpty(focusedBindingPath))
                    {
                        elementToFocus = rootContainer.Query<BindableElement>()
                            .Where(el => el.bindingPath == focusedBindingPath)
                            .First();
                    }
                    if (elementToFocus == null && !string.IsNullOrEmpty(focusedName))
                    {
                        elementToFocus = rootContainer.Q(focusedName);
                    }
                    if (elementToFocus != null && elementToFocus is Focusable f)
                    {
                        f.Focus();
                    }
                }).ExecuteLater(1);
            }
        }

        private void DrawDefaultFallback(VisualElement container)
        {
            InspectorElement.FillDefaultInspector(container, serializedObject, this);

            // FillDefaultInspector only CREATES the PropertyFields; an unbound PropertyField never builds its
            // inner field and renders as literally nothing. Unity binds the tree CreateInspectorGUI RETURNS —
            // once — so the first build is covered, but every LATER rebuild refills a container Unity will never
            // revisit again, and the whole component body comes back BLANK: no fields, no script row, no error.
            // That hits every path that re-enters this branch on a live editor — releasing a type from the
            // designer without saving (close/Discard, "Pick a different script…"), removing a layout, toggling
            // the global override off, and undo/redo on any un-styled component. So bind what we build, exactly
            // as the styled path does (LayoutTreeBuilder); re-binding an already-bound container is supported and
            // is what makes the fresh fields resolve on a rebuild.
            container.Bind(serializedObject);
        }
    }

    /// <summary>
    /// Drives the inspector header pencil: (1) subscribes the <see cref="UnityEditor.Editor.finishedDefaultHeaderGUI"/>
    /// event to draw the pencil on ScriptableObject/asset MAIN headers via IMGUI, and (2) runs a throttled scan
    /// that injects the pencil into MonoBehaviour COMPONENT headers drawn by Unity's default inspector (types we
    /// own are injected directly by <see cref="VisualInspectorEditor"/>). Runs automatically on editor load.
    ///
    /// Component headers are reached via the public <see cref="InspectorElement"/> tree and each row's own
    /// <c>m_Script</c> field. See <see cref="SasquatchInspectorHeaderPencil"/> for details.
    /// </summary>
    [InitializeOnLoad]
    public static class GlobalInspectorHeaderGUI
    {
        private static Texture2D s_HoverTex;
        private static GUIStyle s_HoverStyle;
        private static double s_LastScanTime;

        // Flash mitigation, layered (outer → inner):
        //   • This throttled scan + BURST: on a selection change — or when the active-editor set changes with no
        //     selection event (component add/remove, undo, paste) — a short burst scans unthrottled each tick.
        //     It is the DISCOVERY layer (new/reopened windows, blanket custom-editor rows) and the release path.
        //   • SasquatchTakeoverPrePaintDriver (armed by every ScanRoot pass): re-runs the scan synchronously
        //     inside the panel update, before layout/paint, the moment the row set changes — the layer that makes
        //     takeover flash-free on rebuilds this update-loop scan could only catch one tick late.
        //   • The claim sheet (SasquatchForceTakeover.ClaimClass): keeps a claimed row's repopulated native
        //     content hidden at style-resolution time until the next scan re-displaces it.
        // Steady state (no change) falls back to the 0.15 s throttle, so idle cost is unchanged.
        internal const int BurstTicks = 12;
        internal static int s_BurstTicksRemaining;
        internal static long s_LastTrackerStamp;
        internal static int s_ScanCount; // diag: how many times the throttle/burst gate green-lit a scan.

        static GlobalInspectorHeaderGUI()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI -= OnFinishedDefaultHeaderGUI;
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnFinishedDefaultHeaderGUI;

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;

            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;

            // Both header textures are HideAndDontSave, which is load-bearing (see MakeTex) but also means
            // DontUnloadUnusedAsset: nothing reclaims them, and the statics holding them are wiped by the
            // reload while the natives are not. Without this they would strand one 16x16 + one 64x1 per
            // recompile for the life of the process. Same sweep the other texture caches take.
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseHeaderTextures;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseHeaderTextures;
            EditorApplication.quitting -= ReleaseHeaderTextures;
            EditorApplication.quitting += ReleaseHeaderTextures;
        }

        /// <summary>
        /// Destroys the cached header textures and drops the styles that point at them. Safe to call at any
        /// time: <see cref="SoHeaderHoverStyle"/> re-creates the wash on the next hover repaint and the
        /// <c>FadeTex</c> accessor re-creates the ramp on next use, both gated on the texture being null.
        /// </summary>
        internal static void ReleaseHeaderTextures()
        {
            if (s_HoverTex != null) UnityEngine.Object.DestroyImmediate(s_HoverTex);
            s_HoverTex = null;
            s_HoverStyle = null; // its background is gone; the gate re-mints both together
            if (s_FadeTex != null) UnityEngine.Object.DestroyImmediate(s_FadeTex);
            s_FadeTex = null;
        }

        private static void OnSelectionChanged()
        {
            s_BurstTicksRemaining = BurstTicks;
            s_LastScanTime = EditorApplication.timeSinceStartup;
            s_ScanCount++;
            ScanAllInspectors();
        }

        private static void OnEditorUpdate()
        {
            if (RunScanTick()) ScanAllInspectors();
        }

        /// <summary>
        /// Runs the header/takeover scan across every inspector window RIGHT NOW, and arms the burst so the next few
        /// ticks rescan too. Used by the targeted post-save refresh
        /// (<see cref="SasquatchEditorRegistrar.RefreshInspectorsForType"/>): the DISPLACEMENT rebuild rides the scan,
        /// so requesting one immediately makes a layout change show this frame instead of up to a throttle tick later.
        /// The burst covers rows still mid-rebuild from the save's asset reimport (their fresh InspectorElements are
        /// picked up on a following tick).
        /// </summary>
        internal static void RequestImmediateScan()
        {
            s_BurstTicksRemaining = BurstTicks;
            ScanAllInspectors();
        }

        // The per-tick throttle/burst decision, factored out so tests can drive the cadence without live windows.
        // Returns true when this tick should scan (and bumps s_ScanCount + the throttle clock when it does). A
        // tracker-stamp change re-arms the burst; while the burst is live every tick scans; otherwise the 0.15 s
        // throttle applies.
        internal static bool RunScanTick()
        {
            long stamp = TrackerStamp();
            if (stamp != s_LastTrackerStamp)
            {
                s_LastTrackerStamp = stamp;
                s_BurstTicksRemaining = BurstTicks;
            }

            if (s_BurstTicksRemaining > 0)
            {
                s_BurstTicksRemaining--;
                s_LastScanTime = EditorApplication.timeSinceStartup;
                s_ScanCount++;
                return true;
            }

            if (EditorApplication.timeSinceStartup - s_LastScanTime < 0.15)
                return false;
            s_LastScanTime = EditorApplication.timeSinceStartup;
            s_ScanCount++;
            return true;
        }

        // Cheap per-tick fingerprint of the active-editor set: an order-sensitive hash of the tracked editors'
        // instance ids. Changes when a row is added/removed/reordered (component add/remove, undo, paste) even with
        // no Selection.selectionChanged event, so the burst re-arms and the new row is displaced promptly.
        private static long TrackerStamp()
        {
            var editors = ActiveEditorTracker.sharedTracker.activeEditors;
            long h = 17;
            if (editors != null)
                foreach (var e in editors)
                    h = h * 31 + UnityObjectId.Of(e);
            return h;
        }

        // Injects the pencil into MonoBehaviour component headers drawn by Unity's DEFAULT inspector (types we
        // own are injected directly by VisualInspectorEditor). We scan EVERY editor window's root rather than
        // only the inspector window: SasquatchInspectorHeaderPencil.ScanRoot acts solely on the
        // InspectorElement rows it finds, so this safely covers locked/floating inspectors (and embedded ones).
        // ── COST, MEASURED (2026-08-14) — before optimising this, read the numbers ───────────────────────
        // The whole sweep is 0.99-1.12ms per call, 6.7x/second ⇒ ~7ms/second, ~0 bytes. Of that,
        // Resources.FindObjectsOfTypeAll<EditorWindow>() alone is 0.863ms — **87%**. The per-window work is
        // the remaining ~0.13ms, and ScanRoot already early-outs on a root with no InspectorElement, so
        // there is very little left in it.
        //
        // Two things were tried and REVERTED as not worth their machinery:
        //   • Caching "this root holds no InspectorElement" so the sweep can skip it. Exact (ScanRoot on such
        //     a root provably does nothing) but measured at only 1.026 -> 0.991ms — 3.4% — because ScanRoot's
        //     own early-out already does the same walk. A ConditionalWeakTable plus an invalidation contract
        //     is not worth 0.3ms/second.
        //   • Caching the ENUMERATION, which is where the 87% is. Rejected on behaviour, not cost: this scan
        //     is the only thing that discovers a NEWLY OPENED inspector window, and delaying that is exactly
        //     the takeover flash race SasquatchTakeoverPrePaintDriver exists to close.
        //
        // Note also what this is NOT allowed to become: an "is this window an inspector" test. Naming or
        // casting InspectorWindow is an Asset Store §2.5.g violation, and it is the tempting shortcut here.
        private static void ScanAllInspectors()
        {
            if (!SasquatchEditorMenu.IsOverrideEnabled()) return;

            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            if (windows == null) return;

            foreach (var win in windows)
            {
                if (win == null) continue;
                var root = win.rootVisualElement;
                if (root == null) continue;

                // Resolve the SO/asset backdrop fill from the live editor theme once per skin (the USS
                // variables surface on window roots — see SasquatchInspectorHeaderPencil.TryResolveThemeColor).
                if (!s_SoHeaderFillResolved || s_SoHeaderFillIsPro != EditorGUIUtility.isProSkin)
                {
                    if (SasquatchInspectorHeaderPencil.TryResolveThemeColor(
                            root, "--unity-colors-toolbar-background", out var fill))
                    {
                        s_SoHeaderFill = fill;
                        s_SoHeaderFillResolved = true;
                        s_SoHeaderFillIsPro = EditorGUIUtility.isProSkin;
                    }
                }

                SasquatchInspectorHeaderPencil.ScanRoot(root);
            }
        }

        // ───────────────────────────── SCRIPTABLEOBJECT / ASSET HEADER PENCIL — TUNE HERE ─────────────────────────────
        // ScriptableObject (and other asset) MAIN headers draw the pencil through the IMGUI path
        // (OnFinishedDefaultHeaderGUI, below) — components use the UI Toolkit path instead. If the pencil isn't
        // sitting nicely inline with Unity's native header buttons (help / preset / ⋮) for a ScriptableObject,
        // nudge these two numbers and recompile — they ONLY affect SO/asset headers, never component headers:
        //   • SoHeaderPencilNudge = pixels added to the DERIVED horizontal inset, measured from the header
        //                          CONTENT's right edge (the probed edge Unity's own buttons right-align to —
        //                          NOT the window edge). LARGER = pencil moves LEFT, away from the edge.
        //   • SoHeaderPencilTop   = pixels from the top of the header down to the pencil's TOP
        //                          (LARGER = pencil moves DOWN).
        // The inset is DERIVED, not tuned. This header packs its icons with ZERO spacing (it passes no spacing
        // argument to Unity's header-item pass), so the ⋮ menu owns the slot at the very right edge, the preset
        // icon the one beside it, and the pencil belongs FLUSH in the third — NativeInspectorHeaderIcons
        // .FlushSlotInset(3, slot). The old hand-tuned 61 predates knowing that: it left the pencil ~8px adrift
        // of a pair Unity draws touching, which read as "not one of the native icons".
        //
        // ⚠ THE BASELINE ASSUMES NO HELP "?", which is the normal case here on EVERY Unity version: this
        // header serves the user's ScriptableObjects/assets, and Unity has never drawn a "?" for one (its
        // fallback to the generic scripting-reference page only ever applied to a Component or a MonoScript).
        // The Unity 6.6 change that dropped "?" from COMPONENT titlebars therefore does not touch this path.
        // What DOES put a "?" here is the user's own [HelpURL] on the type — the strip then grows one slot
        // leftward, and InsetFromHelpAbsent moves the pencil out to slot 4 for exactly that case.
        //
        // SoHeaderPencilNudge is the only knob left: +N moves the pencil LEFT (away from the edge), -N right.
        // It is -4 because the ANCHOR is not Unity's anchor. Our probe measures the header CONTENT edge (inside
        // the vertical group Unity opens under the big header), while Unity right-aligns its icons to the
        // header rect's own xMax — 4px further right, that group's style padding, which is not reachable
        // through public API to subtract properly. MEASURED, on 6000.6 @ ppp=1 / UI scale 100, by pixel-reading
        // a screenshot of a real SO header: Unity's ⋮ / preset / "?" glyph centres sat at 590.5 / 574.5 / 558.5
        // — a perfect 16px rhythm — and the pencil's at 538.5, i.e. 20 from the "?" instead of 16. Exactly 4.
        private const float SoHeaderPencilNudge = -4f;
        private const float SoHeaderPencilTop = 5f;

        // ── SO/asset header title-clip backdrop ─────────────────────────────────────────────────────────
        // Same idea as the component-path backdrop (see SasquatchInspectorHeaderPencil.BackdropName): Unity
        // clips the big header's title 4px before ITS leftmost native button (Editor.DrawHeaderGUI:
        // titleRect.xMax = headerItems.xMin - 4) — that clip edge sits 2px INSIDE our pencil's box, so a long
        // asset name draws under the pencil. Repainting the header fill across the pencil box + a fade band
        // to its left makes long titles cut off before the icon. The big header's fill is the TOOLBAR grey
        // (#3C3C3C dark / #CBCBCB light — measured live on 6000.3 + foundations.unity.com), NOT the component
        // titlebar grey (#3E3E3E), and it has no hover tint (verified live), so a static fill is correct.
        private const float SoHeaderBackdropTop = 2f;
        private const float SoHeaderBackdropHeight = 22f;
        private static Texture2D s_FadeTex;
        private static Color s_SoHeaderFill;
        private static bool s_SoHeaderFillResolved;
        private static bool s_SoHeaderFillIsPro;

        // Cached live-theme fill, falling back to the documented palette until a scan resolves the USS var.
        private static Color SoHeaderFill =>
            s_SoHeaderFillResolved && s_SoHeaderFillIsPro == EditorGUIUtility.isProSkin
                ? s_SoHeaderFill
                : (EditorGUIUtility.isProSkin ? (Color)new Color32(0x3C, 0x3C, 0x3C, 0xFF)
                                              : (Color)new Color32(0xCB, 0xCB, 0xCB, 0xFF));

        // White horizontal alpha ramp (0 → 1), tinted at draw time via GUI.DrawTexture's color parameter, so
        // one cached texture serves every theme color.
        private static Texture2D FadeTex
        {
            get
            {
                if (s_FadeTex == null)
                {
                    const int w = 64;
                    s_FadeTex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                    };
                    var px = new Color[w];
                    for (int i = 0; i < w; i++) px[i] = new Color(1f, 1f, 1f, i / (float)(w - 1));
                    s_FadeTex.SetPixels(px);
                    s_FadeTex.Apply();
                }
                return s_FadeTex;
            }
        }

        private static void OnFinishedDefaultHeaderGUI(UnityEditor.Editor editor)
        {
            // ANCHOR PROBE — reserve a zero-size, full-width layout row FIRST, unconditionally. Unity fires
            // finishedDefaultHeaderGUI from inside a full-width vertical group it opens directly under the big
            // header (see Editor.DrawHeaderGUI: BeginVertical(BaseStyles.postLargeHeaderBackground,
            // ExpandWidth(true))), so this probe's xMax IS the header content's true right edge — the exact
            // edge Unity right-aligns its own help / preset / ⋮ buttons to (DrawHeaderHelpAndSettingsGUI
            // anchors at rect.xMax).
            //
            // Do NOT anchor to EditorGUIUtility.currentViewWidth: that is the whole window's width, which does
            // not shrink when the inspector grows a vertical scrollbar — but the header content (and Unity's
            // buttons with it) DOES. Window-width anchoring parked the pencil ~13px too far right — on top of
            // the help icon — exactly whenever the inspector had anything to scroll. The probe tracks the real
            // edge in every host (docked, floating, narrow, scrollbar or not).
            //
            // The probe must be reserved on EVERY event, before ANY early-out below: IMGUI demands identical
            // layout calls on Layout and Repaint. It is zero-height with GUIStyle.none (no margins), so it
            // occupies no visible space. On Layout/Used events GetRect returns Unity's 1x1 dummy rect — the
            // xMax > 1 guard falls back to the view-width anchor then, and nothing draws or hit-tests on
            // those events anyway.
            Rect anchorProbe = GUILayoutUtility.GetRect(0f, 100000f, 0f, 0f, GUIStyle.none, GUILayout.ExpandWidth(true));

            if (editor == null || editor.target == null) return;
            var targetType = editor.target.GetType();
            // Components never reach here — finishedDefaultHeaderGUI fires only for the MAIN object header — so
            // this path serves ScriptableObject/asset main headers. Skip MonoBehaviours defensively so one ever
            // shown as a main object can't double up with the UI Toolkit component pencil.
            if (editor.target is MonoBehaviour) return;
            if (!SasquatchContextMenu.IsTypeEditable(targetType)) return;

            // IMGUI can't host Painter2D, so this path bakes the SAME vector geometry into a
            // texture: 64px source (crisp on HiDPI), glyph at 13/16 of the box to mirror the
            // UI Toolkit button's 13px-icon-in-16px-button proportions. Bold (thick-stroke) glyph =
            // a custom Sasquatch layout exists for this type; thin = none yet. RenderToTexture caches
            // on the bold flag, so both variants bake once and are reused.
            bool hasCustomLayout = LayoutRegistry.TryGet(targetType, editor.target, out var headerLayout);
            var iconTex = SasquatchIcons.RenderToTexture(
                SasquatchIcons.Ids.PencilRuler, 64, SasquatchInspectorHeaderPencil.HeaderIconTint,
                glyphScale: SasquatchInspectorHeaderPencil.HeaderGlyphSize / 16f, bold: hasCustomLayout);

            if (iconTex == null) return;

            float buttonSize = 16f;
            // Real events carry the real probe; Layout/Used carry the 1x1 dummy (nothing draws/clicks then).
            float contentRightEdge = anchorProbe.xMax > 1f ? anchorProbe.xMax : EditorGUIUtility.currentViewWidth;
            // see the SoHeaderPencilNudge/Top block above. One slot is the ⋮ button's own measured width, so a
            // [HelpURL] "?" costs exactly that — not the component titlebar's 20.
            float slot = NativeInspectorHeaderIcons.AssetHeaderSlot;
            float rightInset = NativeInspectorHeaderIcons.InsetFromHelpAbsent(
                NativeInspectorHeaderIcons.FlushSlotInset(3, slot) + SoHeaderPencilNudge,
                slot, targetType, editor.target);
            float x = contentRightEdge - rightInset;
            Rect buttonRect = new Rect(x, SoHeaderPencilTop, buttonSize, buttonSize);

            // Title-clip backdrop (see SoHeaderBackdropTop docs above): repaint the header fill over the
            // pencil box + a fade band so a long asset name cuts off before the icon instead of drawing
            // beneath it. Invisible when the name is short — it's the exact header color.
            if (Event.current.type == EventType.Repaint)
            {
                var fill = SoHeaderFill;
                EditorGUI.DrawRect(new Rect(x, SoHeaderBackdropTop, buttonSize, SoHeaderBackdropHeight), fill);
                GUI.DrawTexture(
                    new Rect(x - SasquatchInspectorHeaderPencil.BackdropFadeWidth, SoHeaderBackdropTop,
                        SasquatchInspectorHeaderPencil.BackdropFadeWidth, SoHeaderBackdropHeight),
                    FadeTex, ScaleMode.StretchToFill, true, 0f, fill, 0f, 0f);
            }

            Vector2 mousePos = Event.current.mousePosition;
            bool isHovered = buttonRect.Contains(mousePos);

            if (Event.current.type == EventType.MouseMove)
            {
                editor.Repaint();
            }

            // Hover wash. The style is resolved per repaint (see SoHeaderHoverStyle) rather than cached
            // behind a one-shot gate — its backing texture can die under it and the draw then silently
            // no-ops.
            if (isHovered && Event.current.type == EventType.Repaint)
            {
                GUI.Box(buttonRect, "", SoHeaderHoverStyle());
            }

            // Draw explicitly (the texture is 64px; GUIStyle.none would blit it unscaled), then an
            // invisible button on top for the click + tooltip.
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(buttonRect, iconTex, ScaleMode.ScaleToFit, true);
            }
            if (GUI.Button(buttonRect, new GUIContent("", SasquatchInspectorHeaderPencil.PencilTooltip(targetType, hasCustomLayout, headerLayout)), GUIStyle.none))
            {
                SasquatchLayoutDesignerWindow.OpenWindowForType(editor.target.GetType(), editor.target);
            }
        }

        /// <summary>
        /// The pencil's hover wash: a flat translucent tint drawn behind the icon box.
        ///
        /// The rebuild gate has to test the TEXTURE, not just the style. A GUIStyle is a plain managed
        /// object — it never becomes fake-null, so a `s_HoverStyle == null` gate re-enters exactly once
        /// per domain reload. The backing texture, on the other hand, is a UnityEngine.Object that Unity
        /// destroys on scene load unless it is marked DontSave (see <see cref="MakeTex"/>), and a
        /// GUIStyle whose background has been destroyed makes GUI.Box draw NOTHING — silently, with no
        /// error. That combination is what killed the hover wash after the first scene load and kept it
        /// dead until the next recompile. Both halves are fixed: the texture survives scene loads, and
        /// this gate re-creates it anyway if anything else destroys it.
        /// </summary>
        internal static GUIStyle SoHeaderHoverStyle()
        {
            if (s_HoverStyle == null || s_HoverTex == null)
            {
                Color hoverColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.1f) : new Color(0f, 0f, 0f, 0.05f);
                s_HoverTex = MakeTex(16, 16, hoverColor);
                if (s_HoverStyle == null) s_HoverStyle = new GUIStyle();
                s_HoverStyle.normal.background = s_HoverTex;
            }
            return s_HoverStyle;
        }

        /// <summary>
        /// A flat 1-color texture for IMGUI backgrounds. HideAndDontSave is load-bearing, not tidiness:
        /// without it Unity destroys the texture on the next scene load and the GUIStyle holding it keeps
        /// drawing nothing (see <see cref="SoHeaderHoverStyle"/>). Same treatment the fade ramp and the
        /// icon textures already get.
        /// </summary>
        internal static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height) { hideFlags = HideFlags.HideAndDontSave };
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}
