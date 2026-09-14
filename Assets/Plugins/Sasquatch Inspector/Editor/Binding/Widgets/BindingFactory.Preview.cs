using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // Diagnostics for the idle Asset Preview poll (see the Every(250) below). Steady state must show the
        // POLLS climbing while the REPAINTS hold still: the poll has to keep asking Unity for the async
        // thumbnail, but once the answer stops changing it must stop forcing the panel to repaint. Same shape
        // as SasquatchTakeoverPrePaintDriver's s_PrePaintScans — a counter is the only way to observe "this
        // stopped happening", since a repaint request leaves nothing behind to assert on.
        internal static int s_IdlePreviewPolls;
        internal static int s_IdlePreviewRepaints;

        // Asset Preview widget: draws the object field with a thumbnail (Standard / ThumbnailOnly). The old
        // "Inline Editor" mode is now its own DisplayType.InlineEditor. Falls back to the default field for
        // non-object-reference properties (consistent with the other widgets).
        private static VisualElement CreateAssetPreview(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return new PropertyField(prop) { label = resolvedLabel };

            return CreateObjectFieldWithPreview(node, prop, resolvedLabel);
        }

        // Control-id seed for the interactive viewport's IMGUI drag (one per editor; the rect disambiguates instances).
        private static readonly int s_assetPreviewDragHash = "SasqAssetPreviewDrag".GetHashCode();

        private static VisualElement CreateObjectFieldWithPreview(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            var objField = new ObjectField(resolvedLabel);
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj != null)
            {
                GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out var fieldType);
                if (fieldType != null)
                {
                    if (fieldType.IsArray) objField.objectType = fieldType.GetElementType();
                    else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                        objField.objectType = fieldType.GetGenericArguments()[0];
                    else
                        objField.objectType = fieldType;
                }
            }
            SeedThenBind(objField, prop); // seed BEFORE bind — see SeedThenBind

            // Optional "Create" button shown while the reference is null (per-binding opt-in; SO fields only).
            // Mounted inside the field's input row by SetupPreviewLayout; the Expandable fold keeps its
            // header clean and skips it.
            Button createBtn = null;
            if (node.CreateAssetEnabled && AssetCreateUtility.IsEligible(prop, out var createFieldType))
            {
                createBtn = AssetCreateUtility.CreateButton(prop, createFieldType);
                AssetCreateUtility.AttachVisibility(createBtn, prop);
                createBtn.style.marginLeft = 4;
            }

            bool interactive = node.AssetPreviewInteractive;
            bool expandable = node.AssetPreviewMode == AssetPreviewMode.Expandable;
            bool isOpen = !expandable;            // the fold gates rendering; non-expandable is always "open"
            Action onFoldClose = null;            // set by the interactive path so a collapse idles the viewport
            Action onFoldOpen = null;             // set by the interactive path so an open warms a real thumbnail

            var thumbnailContainer = new VisualElement();
            thumbnailContainer.AddToClassList("sasq-asset-preview-container");
            thumbnailContainer.style.width = node.PreviewSize;
            thumbnailContainer.style.height = node.PreviewSize;

            // ---- The three ways this box shows nothing, made to look like three different things ----------
            // Blank-because-empty and blank-because-you-panned-it-away were pixel-identical before this: a dark
            // square either way. The empty overlay (dashed frame + glyph) states the box is a receptacle, the
            // notice pill states the asset is out there somewhere and offers the fix, and the inert Reset/2D
            // overlays hide entirely while empty — their mere presence becomes the "something is loaded" tell.
            // Declared here; SyncPreviewState (built after the render path) drives them.
            float boxSize = node.PreviewSize;
            var emptyOverlay = PreviewStateOverlay.BuildEmpty(
                SasquatchIcons.Ids.Frame,
                LayoutTreeBuilder.CurrentDesignMode
                    ? PreviewStateOverlay.EmptyAssetCaptionDesign
                    : PreviewStateOverlay.EmptyAssetCaption,
                boxSize);
            emptyOverlay.style.display = DisplayStyle.None;
            VisualElement offFrameNotice = null; // interactive only — a static thumbnail has no camera to lose
            Button resetBtn = null, twoDBtn = null;      // hidden while empty; hoisted so the sync can reach them
            Action syncPreviewState = null;              // assigned below, once both paths exist
            Func<bool> readOffFrame = () => false;       // the interactive path swaps in the cached verdict

            // Disposal-guarded value read (the SerializedObject can be torn down mid-rebuild).
            UnityEngine.Object CurrentVal()
            {
                try
                {
                    if (prop.serializedObject == null || prop.serializedObject.targetObject == null) return null;
                    return prop.objectReferenceValue;
                }
                catch (Exception) { return null; }
            }
            Texture StaticThumb(UnityEngine.Object val)
            {
                if (val == null) return null;
                var t = AssetPreview.GetAssetPreview(val);
                if (t == null) t = AssetPreview.GetMiniThumbnail(val);
                return t;
            }

            if (interactive)
            {
                // ---- Interactive 3D viewport (orbit / pan / zoom), single-active across all asset previews ----
                var renderer = new AssetPreviewRenderer();
                var activeToken = new object();
                int renderSize = Mathf.Clamp(Mathf.RoundToInt(node.PreviewSize * 2f), 128, 512);
                Texture currentTex = null;       // live RT while active; null → fall back to the dormant thumbnail
                // Camera pose + 2D choice + the dormant lit still all live in a cache keyed by this FIELD, not in
                // this closure: an inspector rebuild (any selection change) throws the closure away, and the user
                // expects the view they left — see PreviewViewState. Nothing else here owns `view.Still`.
                string viewKey = PreviewViewState.KeyFor(prop, PreviewViewState.AssetKind);
                var view = PreviewViewState.For(viewKey, CurrentVal());
                bool warmRequested = view.Still != null; // a restored still needs no load-time warm (and no pop)
                // The designer's authoring preview rebuilds on every keystroke; warm-capturing a PreviewRenderUtility
                // there would re-render the mesh per keystroke (severe lag + the thumbnail flicking to wireframe).
                // Captured at build time because LayoutTreeBuilder.CurrentDesignMode is only set DURING Build.
                bool designMode = LayoutTreeBuilder.CurrentDesignMode;
                // Sprites/textures default to the 2D (orthographic) camera; meshes/prefabs to 3D orbit. The "2D"
                // button flips it, and that choice sticks for the field (across rebuilds AND across re-assignment,
                // like the exported widget's twoDManual) — until then a new reference re-picks by its own nature.
                bool is2D = view.Is2DOverridden ? view.Is2D : AssetPreviewRenderer.IsTwoDByNature(CurrentVal());
                bool dragging = false;
                int dragMode = 0;                // 0 = orbit, 2 = pan
                Vector2 lastDrag = Vector2.zero;
                UnityEngine.Object lastTarget = null;
                EditorApplication.CallbackFunction tick = null;
                bool subscribed = false;
                var hostWindows = new List<EditorWindow>();
                IMGUIContainer surface = null;

                void CaptureHosts()
                {
                    // Match on the window that owns this widget's PANEL, not on a window type name: the inspector's
                    // host window types are internal, so only panel identity is available through public API — and
                    // it is the more robust match anyway. This method has no focusedWindow fallback, so a name that
                    // stopped matching (a renamed host type on a future editor) would leave hostWindows EMPTY and
                    // freeze the preview with no diagnostic. Falls back to the focused window only if panel
                    // resolution finds nothing.
                    var panel = surface?.panel;
                    if (panel != null)
                    {
                        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                        {
                            if (w == null || hostWindows.Contains(w)) continue;
                            if (w.rootVisualElement?.panel == panel) hostWindows.Add(w);
                        }
                    }
                    if (hostWindows.Count == 0)
                    {
                        var focused = EditorWindow.focusedWindow;
                        if (focused != null) hostWindows.Add(focused);
                    }
                }
                void RepaintHosts() { for (int i = 0; i < hostWindows.Count; i++) if (hostWindows[i] != null) hostWindows[i].Repaint(); }

                void SyncTargetIfChanged()
                {
                    var val = CurrentVal();
                    // Rebuild when the reference changed OR the renderer was released (dormant) since last active —
                    // SetTarget reframes to bounds, so restore the saved orbit angle to keep continuity.
                    if (!ReferenceEquals(val, lastTarget) || !renderer.HasRenderable)
                    {
                        lastTarget = val;
                        renderer.SetTarget(val);
                        if (view.HasPose && renderer.HasRenderable)
                            renderer.CameraState = (view.CamEuler, view.CamDistance, view.CamPivot);
                    }
                }

                // Remember the pose the user left, for this release/rebuild cycle and every later one.
                void RememberPose()
                {
                    var cam = renderer.CameraState;
                    view.CamEuler = cam.euler; view.CamDistance = cam.distance; view.CamPivot = cam.pivot;
                    view.HasPose = true;
                }

                // Re-judge whether the asset is framed out of sight and cache the verdict ON THE VIEW STATE.
                // Only meaningful while a renderable exists, so it deliberately does NOT clear the flag when it
                // cannot judge: a dormant preview (renderer released) still shows the still captured from that
                // same pose, which is just as blank — dropping the verdict there would lose the notice at
                // exactly the moment the user needs it. PreviewViewState.Retarget clears it on a new reference.
                void UpdateOffFrame()
                {
                    if (!renderer.HasRenderable) return;
                    view.OffFrame = renderer.IsTargetOffFrame(Mathf.RoundToInt(boxSize));
                }
                readOffFrame = () => view.OffFrame;

                tick = () =>
                {
                    if (!isOpen) return;
                    SyncTargetIfChanged();
                    renderer.Orthographic = is2D;
                    var rt = renderer.RenderToTexture(renderSize);
                    if (rt != null) { currentTex = rt; surface.MarkDirtyRepaint(); RepaintHosts(); }
                    UpdateOffFrame();
                    syncPreviewState?.Invoke();
                };
                IVisualElementScheduledItem releaseTimer = null;
                void StartActive()
                {
                    if (!isOpen) return;
                    if (!AssetPreviewRenderer.CanRender(CurrentVal())) return; // non-renderable → stays a static thumbnail
                    releaseTimer?.Pause();   // cancel a pending dormant-release (re-hovered in time)
                    SyncTargetIfChanged();   // rebuilds the renderer if it was released while dormant
                    CaptureHosts();
                    if (!subscribed) { EditorApplication.update += tick; subscribed = true; }
                }
                void StopActive()
                {
                    if (!subscribed) return;
                    EditorApplication.update -= tick; subscribed = false; // stop per-frame rendering immediately
                    // Grab a fresh dormant still from the last live frame (so the thumbnail reflects where the user
                    // left the orbit) and remember the camera angle, THEN release the heavy renderer shortly after —
                    // unless we re-activate first (a short debounce avoids thrash on fast hover sweeps across a list).
                    if (renderer.HasRenderable)
                    {
                        var still = renderer.CaptureStill(renderSize);
                        if (still != null) PreviewViewState.StoreStill(view, still);
                        RememberPose();
                        UpdateOffFrame(); // last chance to judge — the release timer is about to drop the renderable
                    }
                    currentTex = null; // fall back to the captured still while dormant
                    surface.MarkDirtyRepaint(); RepaintHosts();
                    releaseTimer?.Pause();
                    releaseTimer = surface.schedule.Execute(() => { if (!subscribed) renderer.Cleanup(); }).StartingIn(400);
                }
                void EnsureActive() => AssetPreviewCoordinator.Activate(activeToken, StartActive, StopActive);
                onFoldClose = () => AssetPreviewCoordinator.Deactivate(activeToken);

                // Render the scene JUST long enough to grab a REAL lit still, show it as the dormant image, then
                // release the renderer (zero idle GPU / no PreviewRenderUtility held). Re-hover rebuilds + restores
                // the orbit angle. Runs through AssetPreviewWarmupQueue so many previews warm one-at-a-time.
                void CaptureDormantThumb()
                {
                    if (!isOpen) return;
                    var val = CurrentVal();
                    if (!AssetPreviewRenderer.CanRender(val)) return; // non-renderable keeps Unity's static thumbnail
                    renderer.SetTarget(val);
                    if (!renderer.HasRenderable) return;              // concurrency cap hit / failed → keep static thumb
                    if (view.HasPose) renderer.CameraState = (view.CamEuler, view.CamDistance, view.CamPivot);
                    renderer.Orthographic = is2D;
                    renderer.RenderToTexture(renderSize);            // warm-up frame (URP pipeline setup)
                    var still = renderer.CaptureStill(renderSize);   // second frame + read-back
                    if (still != null) PreviewViewState.StoreStill(view, still);
                    RememberPose();
                    UpdateOffFrame();                                // judge while a renderable still exists
                    lastTarget = val;
                    renderer.Cleanup();                              // dormant: release everything
                    surface.MarkDirtyRepaint(); RepaintHosts();
                    syncPreviewState?.Invoke();
                }
                void RequestWarmCapture()
                {
                    if (designMode) return; // never render/capture in the constantly-rebuilt authoring preview
                    if (warmRequested || !isOpen) return;
                    if (!AssetPreviewRenderer.CanRender(CurrentVal())) return;
                    warmRequested = true;
                    AssetPreviewWarmupQueue.Enqueue(() =>
                    {
                        if (surface == null || surface.panel == null) return; // detached before our turn
                        if (subscribed) return;                                // user hovered first — leave will capture
                        CaptureDormantThumb();
                    });
                }
                onFoldOpen = RequestWarmCapture;

                void HandleMouse(Event e, Rect rect)
                {
                    if (!AssetPreviewRenderer.CanRender(CurrentVal())) return; // no orbit affordance for non-renderable refs
                    int id = GUIUtility.GetControlID(s_assetPreviewDragHash, FocusType.Passive, rect);
                    switch (e.GetTypeForControl(id))
                    {
                        case EventType.MouseDown:
                            if (rect.Contains(e.mousePosition) && (e.button == 0 || e.button == 2))
                            {
                                GUIUtility.hotControl = id;
                                lastDrag = e.mousePosition;
                                dragMode = (is2D || e.button == 2) ? 2 : 0; // 2D (or middle-drag) = pan; left in 3D = orbit
                                dragging = true;
                                AssetPreviewCoordinator.BeginDrag(activeToken); // lock so crossing another preview can't steal active
                                EnsureActive();
                                e.Use();
                            }
                            break;
                        case EventType.MouseDrag:
                            if (GUIUtility.hotControl == id)
                            {
                                Vector2 d = e.mousePosition - lastDrag; lastDrag = e.mousePosition;
                                if (dragMode == 2) renderer.Pan(d); else renderer.Orbit(d);
                                surface.MarkDirtyRepaint(); e.Use();
                            }
                            break;
                        case EventType.MouseUp:
                            if (GUIUtility.hotControl == id)
                            {
                                GUIUtility.hotControl = 0; dragging = false;
                                AssetPreviewCoordinator.EndDrag(activeToken); // release the hover lock
                                e.Use();
                            }
                            break;
                    }
                    // Zoom requires Alt+wheel so a plain scroll still scrolls the Inspector (testers found bare
                    // wheel-zoom hijacked the inspector scroll). Without Alt we leave the event unconsumed so it
                    // bubbles to the surrounding ScrollView.
                    if (e.type == EventType.ScrollWheel && e.alt && rect.Contains(e.mousePosition))
                    {
                        EnsureActive();
                        renderer.Zoom(e.delta.y);
                        surface.MarkDirtyRepaint();
                        e.Use();
                    }
                }

                surface = new IMGUIContainer(() =>
                {
                    var e = Event.current;
                    var rect = new Rect(0, 0, surface.contentRect.width, surface.contentRect.height);
                    HandleMouse(e, rect);
                    if (e.type != EventType.Repaint) return;
                    // Live RT while active → the captured lit still while dormant → Unity's async thumbnail as a
                    // last resort (non-renderable refs, or before a warm capture lands — design mode never warms
                    // one itself, though it will happily show a still the live inspector already cached for this
                    // field). The alpha-blend mode MUST match the source: the RT and the AssetPreview thumbnail
                    // have opposite alpha conventions.
                    Texture rtTex = currentTex ?? (Texture)view.Still; // a URP render-texture still
                    if (rtTex != null)
                    {
                        // alphaBlend:false — URP leaves the RT's alpha ≈ 0; blending it would draw transparent.
                        GUI.DrawTexture(rect, rtTex, ScaleMode.ScaleToFit, false);
                    }
                    else
                    {
                        // Unity's AssetPreview thumbnail is a normal Texture2D WITH a real alpha channel (the
                        // texture's transparent background). It must alpha-BLEND, or the raw RGB in the transparent
                        // regions shows through as garish fringing — the design-mode "ShaderGraph-looking" artifact.
                        // Compositing over the container's dark (#1e1e1e) background matches the live inspector look.
                        var thumb = StaticThumb(CurrentVal());
                        if (thumb != null) GUI.DrawTexture(rect, thumb, ScaleMode.ScaleToFit, true);
                    }
                });
                surface.AddToClassList("sasq-asset-preview-image");
                surface.style.flexGrow = 1;
                thumbnailContainer.Add(surface);
                thumbnailContainer.tooltip = "Drag to orbit · middle-drag to pan · Alt+scroll to zoom";

                // Reset View — recenter/reframe if the asset gets panned or zoomed out of sight. A small corner
                // overlay so it's always findable regardless of the display layout. Renders one frame even when idle.
                resetBtn = new Button(() =>
                {
                    SyncTargetIfChanged();
                    renderer.FrameToBounds();
                    RememberPose();   // the reframe IS the new remembered view — never restore the pose it replaced
                    renderer.Orthographic = is2D;
                    var rt = renderer.RenderToTexture(renderSize);
                    if (rt != null) { currentTex = rt; surface.MarkDirtyRepaint(); }
                    UpdateOffFrame();          // a reframe is the cure — clear the notice immediately
                    syncPreviewState?.Invoke();
                }) { tooltip = "Reset view (recenter the asset)" };
                resetBtn.AddToClassList("sasq-asset-preview-reset");
                resetBtn.focusable = false;  // decorative overlay: never a tab stop, and never the field's focus-delegation target
                resetBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.RotateCcw, 12));
                EditorSpringFX.ButtonSpring(resetBtn);
                thumbnailContainer.Add(resetBtn);

                // 2D / 3D toggle — blue when 2D (orthographic) is on, neutral-dark when off. On by default for
                // sprites/textures, off for meshes; available on anything renderable to compare 2D vs 3D.
                var twoDOnColor = WidgetPalette.BadgeAccent;
                twoDBtn = new Button { text = "2D", tooltip = "Toggle 2D (orthographic) view" };
                twoDBtn.AddToClassList("sasq-asset-preview-2d");
                twoDBtn.focusable = false;  // decorative overlay: never a tab stop, and never the field's focus-delegation target
                void Sync2DButton() => twoDBtn.style.backgroundColor = is2D ? twoDOnColor : new StyleColor(StyleKeyword.Null);
                twoDBtn.clicked += () =>
                {
                    is2D = !is2D;
                    view.Is2D = is2D;
                    view.Is2DOverridden = true; // a deliberate choice outranks the per-type default from here on
                    Sync2DButton();
                    renderer.Orthographic = is2D;
                    // The pointer is over the viewport to click this, so it's the active preview — re-render now for
                    // an instant swap (and refresh the dormant still on the next leave-capture).
                    if (renderer.HasRenderable)
                    {
                        var rt = renderer.RenderToTexture(renderSize);
                        if (rt != null) currentTex = rt;
                    }
                    surface.MarkDirtyRepaint();
                    UpdateOffFrame();          // ortho/perspective changes the framing, so re-judge
                    syncPreviewState?.Invoke();
                };
                Sync2DButton();
                EditorSpringFX.ButtonSpring(twoDBtn);
                thumbnailContainer.Add(twoDBtn);

                // The rescue pill: shown ONLY over a live viewport whose asset has been framed away, never over
                // the empty-slot look, so "lost it" and "never assigned one" can't be confused. Clicking it does
                // the same reframe the corner button does — the fix is on the thing telling you about the problem.
                offFrameNotice = PreviewStateOverlay.BuildNotice(
                    SasquatchIcons.Ids.Scan, PreviewStateOverlay.OffFrameText, boxSize,
                    () =>
                    {
                        SyncTargetIfChanged();
                        renderer.FrameToBounds();
                        RememberPose();
                        renderer.Orthographic = is2D;
                        var rt = renderer.RenderToTexture(renderSize);
                        if (rt != null) { currentTex = rt; surface.MarkDirtyRepaint(); }
                        UpdateOffFrame();
                        syncPreviewState?.Invoke();
                    });
                offFrameNotice.style.display = DisplayStyle.None;
                thumbnailContainer.Add(offFrameNotice);

                // Thumbnail-Only re-homes the object-picker selector to the bottom-right corner, so lift the
                // "2D" toggle up to the top-right to avoid stacking the two on top of each other.
                if (node.AssetPreviewMode == AssetPreviewMode.ThumbnailOnly)
                {
                    twoDBtn.style.top = 3;
                    twoDBtn.style.bottom = StyleKeyword.Auto;
                }

                // Hover makes this the single active viewport — UNLESS another preview is mid-orbit/pan (the cursor
                // crossing this one must not steal/freeze that drag). Leaving (when not mid-drag) idles this one.
                thumbnailContainer.RegisterCallback<PointerEnterEvent>(_ => { if (!AssetPreviewCoordinator.IsDragLocked) EnsureActive(); });
                thumbnailContainer.RegisterCallback<PointerLeaveEvent>(_ => { if (!dragging) AssetPreviewCoordinator.Deactivate(activeToken); });

                // At load, warm a real lit thumbnail (non-expandable shows immediately; expandable warms when the
                // fold first opens — see onFoldOpen). Queued so a panel full of mesh fields warms one at a time.
                thumbnailContainer.RegisterCallback<AttachToPanelEvent>(_ => RequestWarmCapture());
                // Reference changed: drop the stale still + camera (both were framed for the old asset), re-pick the
                // camera mode from the new asset's nature unless the user has taken the "2D" button over, re-warm.
                thumbnailContainer.TrackPropertyValueWithRefresh(prop, _ =>
                {
                    var val = CurrentVal();
                    PreviewViewState.Retarget(view, val);
                    if (!view.Is2DOverridden)
                    {
                        is2D = AssetPreviewRenderer.IsTwoDByNature(val);
                        Sync2DButton();
                        renderer.Orthographic = is2D;
                    }
                    warmRequested = false;
                    if (!subscribed) RequestWarmCapture();
                    surface.MarkDirtyRepaint();
                    syncPreviewState?.Invoke(); // assigned ⇄ cleared flips the empty overlay and the overlay buttons
                });

                // While idle, refresh so the async AssetPreview thumbnail (the last-resort fallback) upgrades once Unity generates it.
                // The state sync rides the same poll: a reference cleared by an Undo or by another inspector never
                // raises a change event here, and it must not leave a stale "Off screen" pill on an empty box.
                //
                // REPAINT ONLY WHEN WHAT WE WOULD DRAW ACTUALLY CHANGED. The poll must keep running — asking
                // AssetPreview for the thumbnail is what makes Unity generate it, and this is the only thing
                // that notices when it finally lands — but an unconditional MarkDirtyRepaint forced the whole
                // inspector panel to repaint 4x a second, per Asset Preview field, for as long as the inspector
                // was open and idle. Nothing else on an idle panel asks for that.
                //
                // While dormant the surface draws `currentTex ?? view.Still ?? StaticThumb(CurrentVal())` (see
                // the IMGUIContainer above), and the only one of those that can change without us being told is
                // the async thumbnail. So comparing the texture we would draw against the one we last drew is
                // an input-equality skip: a repaint that would paint the identical texture is skipped, and the
                // moment the thumbnail arrives (or the reference changes) the comparison fails and we repaint.
                // The live path is untouched — while `subscribed`, the per-frame tick owns the repaints.
                Texture lastDrawnThumb = null;
                bool everDrew = false;
                thumbnailContainer.schedule.Execute(() =>
                {
                    if (!subscribed)
                    {
                        s_IdlePreviewPolls++;
                        Texture wouldDraw = currentTex ?? (Texture)view.Still ?? StaticThumb(CurrentVal());
                        if (!everDrew || !ReferenceEquals(wouldDraw, lastDrawnThumb))
                        {
                            lastDrawnThumb = wouldDraw;
                            everDrew = true;
                            s_IdlePreviewRepaints++;
                            surface.MarkDirtyRepaint();
                        }
                    }
                    syncPreviewState?.Invoke();
                }).Every(250);

                // Torn down for good — NOT on any detach: a re-parent (window tab drag, collection row
                // reorder) re-attaches this same widget, so freeing the renderer and the captured still
                // there blanked a live preview. The idle poll needs no explicit Pause either (UI Toolkit
                // pauses on detach and resumes on attach; an explicit Pause() never resumes). See PanelLifetime.
                PanelLifetime.OnRealDetach(thumbnailContainer, () =>
                {
                    releaseTimer?.Pause();
                    AssetPreviewCoordinator.EndDrag(activeToken); // never leave a dangling hover lock if torn down mid-drag
                    AssetPreviewCoordinator.Deactivate(activeToken);
                    if (subscribed) { EditorApplication.update -= tick; subscribed = false; } // critical: no leaked update closure
                    renderer.Cleanup();
                    // The dormant still is NOT freed here: PreviewViewState owns it and hands it back to the
                    // next build of this same field, which is what removes the re-warm pop on re-selection. That
                    // cache is bounded and clears itself before a domain reload. The one exception is a field the
                    // cache could not key, whose state nothing else would ever free.
                    PreviewViewState.ReleaseIfTransient(view);
                });
            }
            else
            {
                // ---- Static thumbnail (Unity's async AssetPreview), the original behavior ----
                var thumbnailImg = new Image();
                thumbnailImg.AddToClassList("sasq-asset-preview-image");
                thumbnailContainer.Add(thumbnailImg);

                void UpdateThumbnail()
                {
                    var val = CurrentVal();
                    if (val == null)
                    {
                        thumbnailImg.image = null;
                        // ThumbnailOnly keeps the (empty) box so the corner assign button stays put; Standard hides it.
                        // Whichever way it goes, syncPreviewState below dresses the box that DOES stay on screen —
                        // those are the modes where a blank square was previously unexplained.
                        thumbnailContainer.style.display = !expandable && node.AssetPreviewMode == AssetPreviewMode.ThumbnailOnly
                            ? DisplayStyle.Flex : (expandable ? DisplayStyle.Flex : DisplayStyle.None);
                        syncPreviewState?.Invoke();
                        return;
                    }
                    thumbnailContainer.style.display = DisplayStyle.Flex;
                    thumbnailImg.image = StaticThumb(val);
                    syncPreviewState?.Invoke();
                }
                UpdateThumbnail();
                // No detach Pause: UI Toolkit already pauses a scheduled item when its element leaves the
                // panel and RESUMES it on re-attach, while an explicit Pause() is a permanent stop — which
                // froze this poll for good after any re-parent (window tab drag). See PanelLifetime.
                thumbnailImg.schedule.Execute(UpdateThumbnail).Every(250);
                thumbnailImg.TrackPropertyValueWithRefresh(prop, _ => UpdateThumbnail());
            }

            // The empty overlay goes on LAST so it paints above the viewport/thumbnail, but it never picks, so
            // the box stays a drop target and the re-homed object-picker selector (ThumbnailOnly) stays clickable.
            thumbnailContainer.Add(emptyOverlay);

            // ---- The single place that decides which of the three blank-looking states is on screen ---------
            // Everything that can change the answer routes here rather than poking the overlays directly, so the
            // states are mutually exclusive by construction: an empty box can never also claim to be "off screen".
            bool wasOffFrame = false;
            syncPreviewState = () =>
            {
                bool empty = CurrentVal() == null;
                emptyOverlay.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;

                // Reset / 2D act on a camera that has nothing to look at while the slot is empty. Hiding them is
                // not just decluttering: their presence becomes the instant "something IS loaded here" tell.
                if (resetBtn != null) resetBtn.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
                if (twoDBtn != null) twoDBtn.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;

                bool offFrame = !empty && readOffFrame();
                if (offFrameNotice != null)
                    offFrameNotice.style.display = offFrame ? DisplayStyle.Flex : DisplayStyle.None;

                // One-shot on the RISING edge only. A continuously pulsing button in a live inspector is noise
                // (and a permanent repaint source); a single punch as the state appears is the nudge.
                if (offFrame && !wasOffFrame && resetBtn != null) EditorSpringFX.PingScale(resetBtn, 1.35f);
                wasOffFrame = offFrame;

                // The orbit instructions are a lie on an empty slot — swap the whole tooltip with the state.
                if (interactive)
                    thumbnailContainer.tooltip = empty
                        ? PreviewStateOverlay.EmptyAssetTooltip
                        : "Drag to orbit · middle-drag to pan · Alt+scroll to zoom";
                else if (empty)
                    thumbnailContainer.tooltip = PreviewStateOverlay.EmptyAssetTooltip;
            };
            syncPreviewState();

            void SetupPreviewLayout()
            {
                var inputEl = objField.Q<VisualElement>(className: "unity-base-field__input");
                if (inputEl == null) return;

                if (node.AssetPreviewMode == AssetPreviewMode.ThumbnailOnly)
                {
                    // 1. Hide the standard display text and field border/bg
                    var display = objField.Q<VisualElement>(className: "unity-object-field-display");
                    if (display != null) display.style.display = DisplayStyle.None;

                    var pickerEl = inputEl.Q<VisualElement>(className: "unity-object-field__input");
                    if (pickerEl != null)
                    {
                        pickerEl.style.borderTopWidth = 0;
                        pickerEl.style.borderBottomWidth = 0;
                        pickerEl.style.borderLeftWidth = 0;
                        pickerEl.style.borderRightWidth = 0;
                        pickerEl.style.backgroundColor = Color.clear;
                        pickerEl.style.paddingLeft = 0;
                        pickerEl.style.paddingRight = 0;
                        pickerEl.style.marginLeft = 0;
                        pickerEl.style.marginRight = 0;
                        pickerEl.style.width = StyleKeyword.Null;
                    }

                    // 2. Position the selector absolutely inside the thumbnail container
                    var selector = objField.Q<VisualElement>(className: "unity-object-field__selector");
                    if (selector != null)
                    {
                        thumbnailContainer.Add(selector);
                        selector.style.position = Position.Absolute;
                        selector.style.right = 2;
                        selector.style.bottom = 2;
                        selector.style.width = 16;
                        selector.style.height = 16;
                    }

                    // 3. Clear margins on thumbnail container and apply alignment
                    thumbnailContainer.style.marginLeft = 0;
                    thumbnailContainer.style.marginTop = 0;
                    inputEl.style.flexDirection = FlexDirection.Row;
                    inputEl.style.alignItems = Align.Center;

                    switch (node.PreviewAlignment)
                    {
                        case ImageAlignment.Center:
                            inputEl.style.justifyContent = Justify.Center;
                            break;
                        case ImageAlignment.Right:
                            inputEl.style.justifyContent = Justify.FlexEnd;
                            break;
                        case ImageAlignment.Left:
                        default:
                            inputEl.style.justifyContent = Justify.FlexStart;
                            break;
                    }

                    inputEl.Add(thumbnailContainer);
                }
                else
                {
                    // Standard layout (beside or below)
                    var display = objField.Q<VisualElement>(className: "unity-object-field-display");
                    if (display != null) display.style.display = DisplayStyle.Flex;

                    var pickerEl = inputEl.Q<VisualElement>(className: "unity-object-field__input");
                    if (pickerEl != null)
                    {
                        pickerEl.style.borderTopWidth = StyleKeyword.Null;
                        pickerEl.style.borderBottomWidth = StyleKeyword.Null;
                        pickerEl.style.borderLeftWidth = StyleKeyword.Null;
                        pickerEl.style.borderRightWidth = StyleKeyword.Null;
                        pickerEl.style.backgroundColor = StyleKeyword.Null;
                        pickerEl.style.paddingLeft = StyleKeyword.Null;
                        pickerEl.style.paddingRight = StyleKeyword.Null;
                        pickerEl.style.marginLeft = StyleKeyword.Null;
                        pickerEl.style.marginRight = StyleKeyword.Null;
                    }

                    // Restore selector back to pickerEl if it was moved
                    var selector = objField.Q<VisualElement>(className: "unity-object-field__selector");
                    if (selector != null && pickerEl != null && selector.parent != pickerEl)
                    {
                        pickerEl.Add(selector);
                        selector.style.position = Position.Relative;
                        selector.style.right = StyleKeyword.Null;
                        selector.style.bottom = StyleKeyword.Null;
                        selector.style.width = StyleKeyword.Null;
                        selector.style.height = StyleKeyword.Null;
                    }

                    inputEl.style.justifyContent = StyleKeyword.Null;

                    var placement = node.PreviewPlacement;
                    bool stacked = placement == ElementPlacement.Above || placement == ElementPlacement.Below;
                    if (stacked)
                    {
                        inputEl.style.flexDirection = FlexDirection.Column;
                        inputEl.style.alignItems = Align.FlexStart;
                        thumbnailContainer.style.marginLeft = 0;
                        thumbnailContainer.style.marginTop = placement == ElementPlacement.Below ? 4 : 0;
                        thumbnailContainer.style.marginBottom = placement == ElementPlacement.Above ? 4 : 0;
                        if (pickerEl != null) pickerEl.style.width = Length.Percent(100);
                    }
                    else
                    {
                        inputEl.style.flexDirection = FlexDirection.Row;
                        inputEl.style.alignItems = Align.Center;
                        thumbnailContainer.style.marginTop = 0;
                        thumbnailContainer.style.marginLeft = placement == ElementPlacement.Right ? 6 : 0;
                        thumbnailContainer.style.marginRight = placement == ElementPlacement.Left ? 6 : 0;
                        if (pickerEl != null)
                        {
                            pickerEl.style.flexGrow = 1;
                            pickerEl.style.width = StyleKeyword.Null;
                        }
                    }
                    // Above/Left place the thumbnail BEFORE the field; Below/Right after it.
                    if (placement == ElementPlacement.Above || placement == ElementPlacement.Left)
                        inputEl.Insert(0, thumbnailContainer);
                    else
                        inputEl.Add(thumbnailContainer);
                }

                // Create button rides at the end of the input row in every non-expandable mode; Add() is
                // move-safe if the layout is ever re-run.
                if (createBtn != null) inputEl.Add(createBtn);
            }

            // ---- The thumbnail behaves like an object slot, at every level ----
            // Unity puts BOTH object-slot affordances on ObjectFieldDisplay and nowhere else: drag-to-assign, and
            // focus + Delete to un-assign. This widget either sits beside that display (Standard / Expandable) or
            // hides it outright (ThumbnailOnly) — so the thumbnail box, the interactive viewport and the
            // folded-open panel had neither, and ThumbnailOnly could only ever be assigned through the picker
            // and never cleared by keyboard at all. The thumbnail claims drops on the way DOWN; ThumbnailOnly
            // additionally makes the WHOLE row a drop surface (bubble, so it only ever sees drops the thumbnail
            // didn't claim), because no live display is left in that mode to take them.
            //
            // LIVE ONLY, like the collection drop zone — and for Delete the stakes are higher than for drops:
            // the designer canvas previews the user's object AND owns Delete as "remove the selected node", so a
            // thumbnail that swallowed the key there would break card deletion.
            if (!LayoutTreeBuilder.CurrentDesignMode)
            {
                ObjectSlotSurface.AttachDropTarget(thumbnailContainer, prop, objField.objectType);
                ObjectSlotSurface.AttachClearShortcut(thumbnailContainer, objField, prop);
                if (node.AssetPreviewMode == AssetPreviewMode.ThumbnailOnly)
                    ObjectSlotSurface.AttachDropTarget(objField, prop, objField.objectType, TrickleDown.NoTrickleDown);
            }

            // Ping-on-click: clicking ANYWHERE on the widget pings the referenced asset in the Project window,
            // EXCEPT on its own interactive controls — the object-picker selector (keeps opening the picker) and
            // the overlay buttons (Reset view / 2D toggle / Create), which own their click and must not ALSO ping.
            // Trickle-down so it fires even if a child stops the event; MouseDownEvent is left alone so
            // double-click-to-open still works.
            static bool WithinOwnControl(VisualElement el)
            {
                while (el != null)
                {
                    if (el is Button) return true; // Reset / 2D / Create overlays run their own action
                    if (el.ClassListContains("unity-object-field__selector")) return true;
                    el = el.parent;
                }
                return false;
            }
            objField.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (WithinOwnControl(evt.target as VisualElement)) return;
                var current = CurrentVal();
                if (current != null) EditorGUIUtility.PingObject(current);
            }, TrickleDown.TrickleDown);

            if (expandable)
            {
                // Wrap the preview in the shared expandable group — identical to the curve/gradient/particle widgets.
                // The bound ObjectField becomes the header; the preview panel folds below it.
                var foldContent = new VisualElement();
                foldContent.style.flexDirection = FlexDirection.Column;
                foldContent.style.alignItems = node.PreviewAlignment == ImageAlignment.Center ? Align.Center
                    : node.PreviewAlignment == ImageAlignment.Right ? Align.FlexEnd : Align.FlexStart;
                thumbnailContainer.style.marginTop = 2;
                foldContent.Add(thumbnailContainer);

                string title = string.IsNullOrEmpty(resolvedLabel) ? "Asset" : resolvedLabel;
                var root = new VisualElement();
                root.AddToClassList("sasq-asset-preview-field");

                void HandleExpanded(bool open, bool userInitiated)
                {
                    isOpen = open;
                    if (open) onFoldOpen?.Invoke();   // warm a real thumbnail the first time it's revealed (no-op when static)
                    else onFoldClose?.Invoke();       // idle the interactive viewport on collapse (no-op when static)
                }

                // Join the shared label column so this row's input box lines up with every other field —
                // including a sibling curve/gradient preview. FieldLabelColumn strips the stock class and
                // drives the label itself. The standalone export aligns this row the same way
                // (ExportedWidgetKit.AssetPreviewBox).
                objField.AddToClassList("unity-base-field__aligned");

                root.Add(BuildExpandableGroup(title, headerExtra: null, content: foldContent,
                    stateKey: FoldoutStateStore.Key(prop.serializedObject.targetObject, prop.propertyPath, title),
                    fallbackExpanded: false,
                    headerMainOverride: objField,
                    onExpandedChanged: HandleExpanded));

                // Keep the ObjectField's input box from toggling the fold (assign/drag without collapsing), while the
                // rest of the header bar + caret still toggle. Mirrors the particle widget's StopHeaderFieldInput.
                void StopHeaderFieldInput()
                {
                    var box = objField.Q<VisualElement>(className: "unity-object-field__input");
                    if (box == null) return;
                    box.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    box.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                }
                if (objField.Q<VisualElement>(className: "unity-object-field__input") != null) StopHeaderFieldInput();
                else objField.RegisterCallback<AttachToPanelEvent>(_ => StopHeaderFieldInput());

                return root;
            }

            // Non-expandable: place the preview beside / below / over the field via the layout surgery.
            var initialInput = objField.Q<VisualElement>(className: "unity-base-field__input");
            if (initialInput != null) SetupPreviewLayout();
            else objField.RegisterCallback<AttachToPanelEvent>(_ => SetupPreviewLayout());

            return objField;
        }

    }
}
