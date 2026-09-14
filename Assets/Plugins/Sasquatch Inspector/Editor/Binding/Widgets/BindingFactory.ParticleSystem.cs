using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        private const string ParticlePlayGlyph = "▶";
        private const string ParticleStopGlyph = "■";

        // Live-restyle hooks so the designer can push size / background / play-button / zoom edits to an
        // already-rendered particle preview WITHOUT a full node rebuild (which re-inits the PreviewRenderUtility
        // per tick and thrashes). Keyed by the preview panel element ("sasq-particle-preview-panel").
        //   s_particleLiveRestyle  — per drag tick: cheap style + camera props (+ a live re-render for ParticleSystems).
        //   s_particleForceReRender — once at commit: re-develop a frozen VFX poster so zoom/background reframe.
        internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Action> s_particleLiveRestyle = new();
        internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Action> s_particleForceReRender = new();

#if SASQ_PARTICLES // The preview renderer needs ParticleSystem (com.unity.modules.particlesystem, a REMOVABLE built-in module).
        // ParticleSystemPreview widget: a ParticleSystem/GameObject field with a live, in-editor preview that
        // behaves like a video. The bound effect is instantiated into an off-screen PreviewRenderUtility scene,
        // rendered (through URP) into a RenderTexture and blitted to screen by an IMGUIContainer (GUI.DrawTexture);
        // idle it shows a rendered "poster" frame with a ▶ overlay, and clicking the panel plays the effect —
        // simulated from t=0 to the system's duration and looping back — until clicked again to stop. While playing
        // the ▶ hides; hovering the thumbnail reveals a ■ stop badge so playback is never occluded.
        // Three things had to line up: (1) the SRP render is via SubmitRenderRequest from an EditorApplication.update
        // tick — OUTSIDE the inspector's IMGUI paint, since a nested SRP render from inside IMGUI paint draws nothing
        // in URP; (2) playback is driven from EditorApplication.update, NOT the UI Toolkit panel scheduler, which
        // stalls when the inspector is idle/unfocused; (3) the frame is shown via IMGUIContainer + GUI.DrawTexture,
        // NOT a UI Toolkit Image — an Image caches its bound texture and won't re-sample a RenderTexture whose GPU
        // content changes, so the preview would freeze on frame 0. The host inspector window is Repaint()'d each
        // tick so the IMGUIContainer actually blits. Falls back to the default field for non-object-reference
        // properties, consistent with the other widgets.
        //
        // Built on a REAL ObjectField(label) (not a hand-rolled label) so it participates in Unity's
        // unity-base-field__aligned label column — that's what keeps consecutive previews left-aligned (the old
        // hand-rolled label sized to its own text, leaking a different x into each thumbnail). The thumbnail panel
        // is arranged per ParticlePreviewMode: FieldAndThumbnail (below), Standard (beside), ThumbnailOnly
        // (just the panel + a corner assign button + drag-drop), and Expandable (a caret left of the field that
        // spring-opens the panel; optionally auto-plays on open and stops on close). Mirrors the AssetPreview
        // widget's layout surgery (BindingFactory.Preview.cs).
        private static VisualElement CreateParticleSystemPreview(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return new PropertyField(prop) { label = resolvedLabel };

            var mode = node.ParticlePreviewMode;
            // Whether the preview panel is currently showing (Expandable starts collapsed; the other modes are always
            // open). Declared up here because the IMGUIContainer's onGUI (built below) reads it to skip GPU work for a
            // collapsed VFX. SetExpanded flips it when the caret toggles.
            bool isOpen = mode != ParticlePreviewMode.Expandable;
            float size = Mathf.Clamp(node.ParticleThumbnailSize, 16f, 256f); // thumbnail edge (designer 16-256 slider)
            int renderSize = Mathf.Clamp(Mathf.RoundToInt(size * 2f), 128, 512); // render crisper than display, downscaled by the blit

            // Real ObjectField WITH the label so the field joins the inspector's aligned label column (the fix for
            // mis-aligned consecutive previews). Constrained to the resolved field type (ParticleSystem/GameObject),
            // unwrapping array/list element types — mirrors the AudioPreview/AssetPreview widgets.
            var objField = new ObjectField(resolvedLabel) { objectType = typeof(GameObject) };
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj != null)
            {
                GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out var fieldType);
                if (fieldType != null)
                {
                    Type elem = fieldType;
                    if (fieldType.IsArray) elem = fieldType.GetElementType();
                    else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                        elem = fieldType.GetGenericArguments()[0];
                    if (elem != null && typeof(UnityEngine.Object).IsAssignableFrom(elem))
                        objField.objectType = elem;
                }
            }
            SeedThenBind(objField, prop); // seed BEFORE bind — see SeedThenBind

            // Disposal-safe: the inspector disposes the SerializedObject before tearing the panel down, so any
            // property access (even from a scheduled tick or detach) can throw. Returning null keeps callers safe.
            UnityEngine.Object CurrentTarget()
            {
                try
                {
                    if (prop.serializedObject == null || prop.serializedObject.targetObject == null)
                        return null;
                    return prop.objectReferenceValue;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // ----- Preview panel: an IMGUIContainer blitting the renderer's RenderTexture, with a ▶ play badge
            //       overlaid like a video thumbnail. Click the panel to toggle playback; the badge hides while
            //       playing (YouTube-style) so it never occludes the effect. The badge is absolutely positioned
            //       (anchor + manual offset), so the panel itself does no centering. -----
            var previewWrap = new VisualElement();
            previewWrap.AddToClassList("sasq-particle-preview-panel");
            previewWrap.style.position = Position.Relative;
            previewWrap.style.width = size;
            previewWrap.style.height = size;
            previewWrap.style.backgroundColor = node.ParticleBackgroundColor;
            previewWrap.style.borderTopLeftRadius = 4;
            previewWrap.style.borderTopRightRadius = 4;
            previewWrap.style.borderBottomLeftRadius = 4;
            previewWrap.style.borderBottomRightRadius = 4;
            previewWrap.style.overflow = Overflow.Hidden;

            // Display the rendered frame via an IMGUIContainer that blits the RenderTexture with GUI.DrawTexture.
            // WHY NOT a UI Toolkit Image: an Image element caches its bound texture and does NOT re-sample a
            // RenderTexture whose GPU content changed between frames — so live playback stays frozen on the first
            // frame even though the RT is updating (confirmed: window repaints + RT litPixels climb, screen frozen).
            // GUI.DrawTexture re-samples the live texture on every repaint (the same display path SpringSandboxWindow
            // uses). The SRP render still happens OUTSIDE IMGUI (in the EditorApplication.update tick); only the blit
            // is in IMGUI, which is always valid.
            // Renderer + playback state are declared up here (before the IMGUIContainer's onGUI) because, for the VFX
            // path, the onGUI itself drives rendering and must capture these (a VisualEffect only advances when
            // PreviewRenderUtility.Render runs inside an IMGUI repaint).
            var renderer = new ParticlePreviewRenderer();
            renderer.BackgroundColor = node.ParticleBackgroundColor; // camera clear color (applied each render)
            renderer.VfxZoom = node.ParticlePreviewZoom;             // VFX-only framing zoom (ParticleSystems ignore it)
            UnityEngine.Object lastTarget = null;
            bool playing = false;
            // Live Background Color / Preview Zoom tick on a frozen VFX poster: re-render the PAUSED effect
            // once with the new camera settings on the next IMGUI repaint (same particles, new backdrop/framing,
            // one cheap frame — no re-develop). Set by LiveRestyle; the commit's re-develop then restores the
            // best-frame poster under the new cache key.
            bool vfxRecolorPending = false;
            Color lastLiveBg = node.ParticleBackgroundColor;
            float lastLiveZoom = node.ParticlePreviewZoom;
            var playToken = new object();        // identity for the global single-active playback coordinator
            bool needsRender = true;             // a poster (re)render is pending
            double playStartTime = 0;
            float lastSim = 0f;
            bool posterPumping = false;          // VFX only: developing a fresh frozen poster (unpaused, hidden) — see StartVfxPoster
            double posterPumpStart = 0;          // when the current VFX poster-pump started (real seconds)
            bool vfxPosterReady = false;          // VFX only: a developed still already exists (don't re-develop on resize/repaint)
            int posterBestScore = 0;             // VFX only: best (most-developed) frame score seen this develop — we cache the BEST frame, not the freeze-moment one
            int posterPlateauFrames = 0;         // VFX only: consecutive develop frames since the best score last improved
            const float VfxDevelopMaxSeconds = 1.5f; // hard cap on the one-time develop (real seconds)
            const float VfxDevelopRate = 4f;      // play rate during the develop so it reaches a representative frame sooner
            const int MinDevelopScore = 200;     // a frame must be at least this "developed" (non-uniform pixels) before the develop can end early
            const int PosterPlateauFrames = 16;  // end the develop once the best frame hasn't improved for this many frames (the effect has settled)
            EditorApplication.CallbackFunction playUpdate = null; // EditorApplication.update driver while playing
            bool updateSubscribed = false;                        // idempotent subscribe/unsubscribe guard
            // Re-arms the PanelLifetime teardown after Tick() has taken a render utility back (see the re-acquire
            // branch there). Hoisted because Tick is declared long before the OnRealDetach registration that
            // assigns it — a local function may not reference a local declared later in the block.
            Action rearmRendererCleanup = null;
            var hostWindows = new List<EditorWindow>();           // inspector window(s) to Repaint() while playing

            Texture currentTex = null; // a live RenderTexture while previewing, or a static AssetPreview thumbnail when there's no ParticleSystem
            IMGUIContainer surface = null;
            // ---- Why this panel is blank, told apart -------------------------------------------------------
            // A blank particle panel had THREE unrelated causes that all rendered as the flat background color:
            // nothing assigned, the effect framed out of the viewport, or an effect that simply has no particles
            // alive at the poster time (a one-shot that already finished, or a poster time of 0). Each now says
            // so, and the two fixable ones offer the fix. See PreviewStateOverlay.
            VisualElement emptyOverlay = null, offFrameNotice = null, noParticlesNotice = null;
            // Hoisted (not `var` at their construction site) because the Reset button's own click lambda reaches
            // RenderNow → SyncPreviewState → resetBtn: assigning it inside its own initializer would leave it
            // formally unassigned at that capture.
            Button resetBtn = null, twoDBtn = null;
            bool blankPoster = false;   // the SETTLED poster shows essentially no particles (readback-sampled, sparingly)
            bool wasOffFrame = false;   // rising-edge latch for the one-shot Reset pulse
            // Below this "developed pixel" count a frame is visually empty. Same metric the VFX poster develop
            // already scores with, so the two agree on what "nothing there" means.
            const int BlankPosterScore = 40;
            bool interactive = node.ParticlePreviewInteractive; // orbit/pan/zoom + the Reset/2D overlays (default ON)
            // Camera pose + 2D choice live in a per-FIELD cache, not this closure: an inspector rebuild (any
            // selection change) throws the closure away, and the user expects the angle they left. Same store and
            // the same reasoning as the Asset Preview — see PreviewViewState. No still is cached here: a
            // ParticleSystem poster re-renders from the restored pose, and a VFX poster already has its own
            // ParticlePreviewRenderer.s_vfxPosterCache.
            string viewKey = PreviewViewState.KeyFor(prop, PreviewViewState.ParticleKind);
            var view = PreviewViewState.For(viewKey, CurrentTarget());
            bool is2D = view.Is2DOverridden && view.Is2D;   // particles default to 3D orbit; the "2D" button flips it
            renderer.Orthographic = is2D;

            // Push/pull the user-driven camera pose through the cache. Restore runs AFTER SetTarget, which reframes.
            void RememberPose()
            {
                var cam = renderer.CameraState;
                view.CamEuler = cam.euler; view.CamDistance = cam.distance; view.CamPivot = cam.pivot;
                view.UserZoom = cam.userZoom; view.PanOffset = cam.panOffset;
                view.HasPose = true;
            }
            void RestorePoseIfAny()
            {
                if (!view.HasPose) return;
                renderer.CameraState = (view.CamEuler, view.CamDistance, view.CamPivot, view.UserZoom, view.PanOffset);
            }
            bool camDragging = false, camDragged = false;  // orbit/pan drag state (distinguished from a click, which toggles playback)
            bool overControl = false;                       // pointer is over the Reset/2D overlay (don't lighten/show the play badge)
            bool hovering = false;                          // pointer is over the preview thumbnail (drives the play-badge reveal/lighten)
            Vector2 camLastPos = Vector2.zero, camStartPos = Vector2.zero;
            int camButton = 0;
            surface = new IMGUIContainer(() =>
            {
                if (Event.current == null || Event.current.type != EventType.Repaint) return;
                var rect = new Rect(0, 0, surface.contentRect.width, surface.contentRect.height);
                // VFX MUST render HERE, inside the IMGUI paint: a VisualEffect only advances+draws when
                // PreviewRenderUtility.Render runs inside an IMGUI repaint — rendering it from the EditorApplication.
                // update tick (or via SubmitRenderRequest) drew nothing. We render
                // every frame while animating (play / poster-pump); once frozen we re-blit the last developed RT, so an
                // idle paused effect costs no GPU. ParticleSystems are unaffected (IsVfx == false): they keep rendering
                // from the update tick and this just blits, exactly as before.
                if (renderer.IsVfx && renderer.HasSystems && isOpen)
                {
                    if (posterPumping)
                    {
                        // Developing the idle poster: ADVANCE the effect (render to the RT) but DON'T reveal the
                        // developing frames — the thumbnail must look STATIC, not auto-play. currentTex stays the
                        // placeholder asset icon until the develop finishes and reveals the best frame. Score each
                        // frame and snapshot the most-developed one (a freshly-restarted VFX is near-empty, so the
                        // arbitrary freeze-moment frame is often blank — we cache the BEST frame instead).
                        renderer.RenderToTexture(renderSize);
                        int score = renderer.CountDevelopedPixels();
                        if (score > posterBestScore)
                        {
                            posterBestScore = score;
                            posterPlateauFrames = 0;
                            renderer.CacheCurrentVfxPoster(); // snapshot this frame as the current best
                        }
                        else posterPlateauFrames++;
                    }
                    else if (playing || currentTex == null)
                    {
                        // Playing (user clicked) shows live frames; once frozen the first render here captures the
                        // developed still into currentTex, after which we just re-blit it (no GPU).
                        var tex = renderer.RenderToTexture(renderSize);
                        if (tex != null) currentTex = tex;
                    }
                    else if (vfxRecolorPending && vfxPosterReady)
                    {
                        // Live Background Color / Zoom: one paused re-render with the new camera settings so the
                        // frozen poster follows the drag tick-by-tick. Deliberately NOT cached: the paused frame
                        // may differ from the developed best-frame poster, which the commit's re-develop restores.
                        vfxRecolorPending = false;
                        var tex = renderer.RenderToTexture(renderSize);
                        if (tex != null) currentTex = tex;
                    }
                }
                if (currentTex == null) return;
                // For our composited RenderTexture (live frame or cached poster), alphaBlend:false draws its RGB
                // opaquely — the RT's alpha is ≈0 under URP, so blending it would draw transparent (the original
                // freeze-on-frame-0 bug). For a Texture2D placeholder/fallback (the VFX asset icon while developing, or
                // the no-ParticleSystem asset thumbnail) we DO want alpha so the icon's transparent areas show the panel
                // background rather than a black box. Graphics.DrawTexture(+material) is a GL draw that UI Toolkit's IMGUI
                // compositing does NOT capture inside a nested inspector IMGUIContainer under SRP; GUI.DrawTexture IS.
                bool keepAlpha = !(currentTex is RenderTexture);
                GUI.DrawTexture(rect, currentTex, ScaleMode.ScaleToFit, keepAlpha);
            });
            surface.style.position = Position.Absolute;
            surface.style.left = 0;
            surface.style.top = 0;
            surface.style.width = size;   // explicit size: an IMGUIContainer's intrinsic size is 0x0; pin it to the
            surface.style.height = size;  // panel so a centering parent can't collapse it (else GUI.DrawTexture has no area)
            surface.pickingMode = PickingMode.Ignore; // let the play button on top receive clicks
            previewWrap.Add(surface);

            float PosterTime() => renderer.MaxDuration * Mathf.Clamp01(node.ParticlePosterTime); // designer-set "alive" still

            // Play badge — the ▶ glyph styled with the project accent-blue convention, overlaid over the panel. It's
            // a pure visual affordance (pickingMode Ignore): the whole preview panel owns the play/stop toggle. The
            // badge lightens to badgeHover while the mouse is over the thumbnail so it reads as interactive. It is
            // absolutely positioned from the designer-chosen anchor + manual offset (see PositionPlayButton).
            var badgeNormal = WidgetPalette.BadgeAccentSoft;
            var badgeHover = WidgetPalette.BadgeAccentHover; // lighter accent blue on hover
            var playBtn = new Button { text = ParticlePlayGlyph, tooltip = "Click to play (click again to stop)" };
            playBtn.AddToClassList("sasq-particle-play-button");
            playBtn.focusable = false; // decorative overlay: never a tab stop, nor the field's focus-delegation target
            playBtn.pickingMode = PickingMode.Ignore; // clicks fall through to previewWrap, which owns the toggle
            playBtn.style.position = Position.Absolute;
            playBtn.style.paddingLeft = 0;
            playBtn.style.paddingRight = 0;
            playBtn.style.marginLeft = 0;
            playBtn.style.marginRight = 0;
            playBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            playBtn.style.backgroundColor = badgeNormal;
            playBtn.style.color = Color.white;
            playBtn.style.borderTopLeftRadius = 4;
            playBtn.style.borderTopRightRadius = 4;
            playBtn.style.borderBottomLeftRadius = 4;
            playBtn.style.borderBottomRightRadius = 4;
            previewWrap.Add(playBtn);

            // Reset-view + 2D/3D toggle — small corner overlays matching the asset preview. PositionOverlayButtons
            // (below) puts them in bottom corners by default, relocating to the top when the play badge would overlap.
            // StopPropagation on BOTH their pointer-down AND click so pressing them ONLY runs their own action —
            // never starts a camera drag and never toggles playback (which lives on previewWrap).
            // RememberPose after the reset: the reframe IS the new remembered view, or the pose it replaced would
            // come straight back on the next rebuild.
            resetBtn = new Button(() => { renderer.ResetView(); RememberPose(); RenderNow(); }) { tooltip = "Reset view (recenter the effect)" };
            resetBtn.focusable = false; // decorative overlay: never a tab stop, nor the field's focus-delegation target
            resetBtn.AddToClassList("sasq-asset-preview-reset");
            resetBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.RotateCcw, 12));
            resetBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            resetBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            EditorSpringFX.ButtonSpring(resetBtn);
            previewWrap.Add(resetBtn);

            var twoDOnColor = WidgetPalette.BadgeAccent;
            twoDBtn = new Button { text = "2D", tooltip = "Toggle 2D (orthographic) view" };
            twoDBtn.focusable = false; // decorative overlay: never a tab stop, nor the field's focus-delegation target
            twoDBtn.AddToClassList("sasq-asset-preview-2d"); // shared pill look (bottom-right by default)
            void Sync2DButton() => twoDBtn.style.backgroundColor = is2D ? twoDOnColor : new StyleColor(StyleKeyword.Null);
            twoDBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            twoDBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            twoDBtn.RegisterCallback<PointerEnterEvent>(_ => { overControl = true; SyncPlayButton(); });
            twoDBtn.RegisterCallback<PointerLeaveEvent>(_ => { overControl = false; SyncPlayButton(); });
            // The 2D choice sticks for the field from here on (it outranks the 3D-orbit default), exactly like the
            // Asset Preview's button.
            twoDBtn.clicked += () => { is2D = !is2D; view.Is2D = is2D; view.Is2DOverridden = true; Sync2DButton(); RenderNow(); };
            Sync2DButton();
            EditorSpringFX.ButtonSpring(twoDBtn);
            previewWrap.Add(twoDBtn);

            // Empty slot: dashed frame + glyph, never picking, so the panel stays a drop target underneath.
            emptyOverlay = PreviewStateOverlay.BuildEmpty(
                SasquatchIcons.Ids.Sparkles,
                LayoutTreeBuilder.CurrentDesignMode
                    ? PreviewStateOverlay.EmptyEffectCaptionDesign
                    : PreviewStateOverlay.EmptyEffectCaption,
                size);
            emptyOverlay.style.display = DisplayStyle.None;
            previewWrap.Add(emptyOverlay);

            // Framed away — the same reframe the corner button does, on the thing reporting the problem.
            offFrameNotice = PreviewStateOverlay.BuildNotice(
                SasquatchIcons.Ids.Scan, PreviewStateOverlay.OffFrameText, size,
                () => { renderer.ResetView(); RememberPose(); RenderNow(); SyncPlayButton(); });
            offFrameNotice.style.display = DisplayStyle.None;
            previewWrap.Add(offFrameNotice);

            // Alive, framed, and still nothing to see: the effect has no particles at this poster time. Playing
            // it is the answer (and is what the user was reaching for anyway), so the pill just does that.
            noParticlesNotice = PreviewStateOverlay.BuildNotice(
                SasquatchIcons.Ids.Play, PreviewStateOverlay.NoParticlesText, size,
                () => { if (!playing) TogglePlayback(); });
            noParticlesNotice.style.display = DisplayStyle.None;
            previewWrap.Add(noParticlesNotice);

            // Reset button's over-control hover (registered here, after twoDBtn exists, since SyncPlayButton reads it).
            resetBtn.RegisterCallback<PointerEnterEvent>(_ => { overControl = true; SyncPlayButton(); });
            resetBtn.RegisterCallback<PointerLeaveEvent>(_ => { overControl = false; SyncPlayButton(); });

            // Size the badge to the designer's pixel width (height + glyph scale from the 44x32 base), then place it
            // at the chosen 9-position anchor over the size×size panel plus the manual offset. Computed once at
            // build (the values are static per build; changing an option triggers a RefreshPreview rebuild).
            void PositionPlayButton()
            {
                float w = Mathf.Clamp(node.ParticlePlayButtonWidth, 24f, 96f);
                float h = w * (32f / 44f);
                playBtn.style.width = w;
                playBtn.style.height = h;
                playBtn.style.fontSize = 14f * (w / 44f);

                const float inset = 4f; // keep edge anchors off the very border
                int col = ((int)node.ParticlePlayButtonAnchor) % 3; // 0 left, 1 center, 2 right
                int row = ((int)node.ParticlePlayButtonAnchor) / 3; // 0 top,  1 middle, 2 bottom
                float left = col == 0 ? inset : col == 2 ? size - w - inset : (size - w) * 0.5f;
                float top = row == 0 ? inset : row == 2 ? size - h - inset : (size - h) * 0.5f;
                left += node.ParticlePlayButtonOffset.x;
                top -= node.ParticlePlayButtonOffset.y; // +Y nudges the badge UP (UITK 'top' grows downward, so invert)
                playBtn.style.left = left;
                playBtn.style.top = top;
            }
            PositionPlayButton();

            // Place the reset (left) + 2D (right) overlays in the BOTTOM corners by default (matching the asset
            // preview). If the play badge is anchored along the bottom, slide the colliding overlay up to the TOP
            // corner instead so they never sit under the play/stop button wherever the designer put it.
            void PositionOverlayButtons()
            {
                int col = ((int)node.ParticlePlayButtonAnchor) % 3; // 0 left, 1 center, 2 right
                int row = ((int)node.ParticlePlayButtonAnchor) / 3; // 0 top,  1 middle, 2 bottom
                bool playBottom = row == 2;
                bool resetToTop = playBottom && col <= 1;  // play bottom-left/center → reset up out of the way
                bool twoDToTop = (playBottom && col >= 1)   // play bottom-right/center → 2D up out of the way
                              || node.ParticlePreviewMode == ParticlePreviewMode.ThumbnailOnly; // selector sits bottom-right here
                resetBtn.style.left = 3; resetBtn.style.right = StyleKeyword.Null;
                if (resetToTop) { resetBtn.style.top = 3; resetBtn.style.bottom = StyleKeyword.Null; }
                else { resetBtn.style.top = StyleKeyword.Null; resetBtn.style.bottom = 3; }
                twoDBtn.style.right = 3; twoDBtn.style.left = StyleKeyword.Null;
                if (twoDToTop) { twoDBtn.style.top = 3; twoDBtn.style.bottom = StyleKeyword.Null; }
                else { twoDBtn.style.top = StyleKeyword.Null; twoDBtn.style.bottom = 3; }
            }
            PositionOverlayButtons();

            // Paused: always show the ▶ play badge. Playing: show a ■ stop badge ONLY while the mouse is over the
            // thumbnail, so playback isn't occluded but the user can see (and click) stop on hover. Also gates the
            // badge on whether the target actually has a previewable ParticleSystem.
            void SyncPlayButton()
            {
                playBtn.SetEnabled(renderer.HasSystems);
                playBtn.text = playing ? ParticleStopGlyph : ParticlePlayGlyph;
                // "Effective" hover = over the thumbnail itself, NOT over the Reset/2D overlay buttons, and not
                // mid-drag. Used for BOTH the ■ stop-badge visibility and the play/stop lighten, so hovering a
                // control button neither lightens the badge nor reveals the stop icon while playing.
                bool effHover = hovering && !overControl && !camDragging;
                bool show = !playing || effHover;
                playBtn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                playBtn.style.backgroundColor = effHover ? badgeHover : badgeNormal;
                bool showControls = interactive && renderer.HasSystems; // overlays only when interactive + renderable
                resetBtn.style.display = showControls ? DisplayStyle.Flex : DisplayStyle.None;
                twoDBtn.style.display = showControls ? DisplayStyle.Flex : DisplayStyle.None;
                // A ▶ badge over an empty slot promises playback of nothing. Reset/2D already vanish with
                // HasSystems; the badge only ever went DISABLED, which still read as "there's something here".
                // Hidden strictly on an EMPTY slot: an assigned non-particle asset shows a real thumbnail, so a
                // greyed badge over it is informative rather than misleading.
                if (CurrentTarget() == null) playBtn.style.display = DisplayStyle.None;
                SyncPreviewState();
            }

            // The single place that decides which blank-state overlay is on screen. Routing every caller here
            // (rather than poking the overlays directly) is what keeps the three states mutually exclusive: an
            // empty panel can never also claim to be off-screen, and neither can shout over a live playback.
            void SyncPreviewState()
            {
                bool empty = CurrentTarget() == null;
                if (emptyOverlay != null)
                    emptyOverlay.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;

                // Judge only what can be judged: no systems (a non-particle asset showing its static thumbnail)
                // is not a blank panel, and a playing effect is self-explanatory.
                bool judgeable = !empty && renderer.HasSystems && isOpen && !playing && !posterPumping;
                bool offFrame = judgeable && interactive && renderer.IsTargetOffFrame(Mathf.RoundToInt(size));
                // Off-frame outranks blank: if the effect is out of view we cannot know whether it has particles,
                // and offering "press play" for something you cannot see would be the wrong fix.
                bool noParticles = judgeable && !offFrame && blankPoster;

                if (offFrameNotice != null)
                    offFrameNotice.style.display = offFrame ? DisplayStyle.Flex : DisplayStyle.None;
                if (noParticlesNotice != null)
                    noParticlesNotice.style.display = noParticles ? DisplayStyle.Flex : DisplayStyle.None;

                // One-shot on the rising edge: a permanently pulsing button is noise and a standing repaint source.
                if (offFrame && !wasOffFrame && resetBtn != null) EditorSpringFX.PingScale(resetBtn, 1.35f);
                wasOffFrame = offFrame;

                previewWrap.tooltip = empty ? PreviewStateOverlay.EmptyEffectTooltip : null;
            }
            SyncPlayButton();
            previewWrap.RegisterCallback<PointerEnterEvent>(_ => { hovering = true; SyncPlayButton(); });
            previewWrap.RegisterCallback<PointerLeaveEvent>(_ => { hovering = false; SyncPlayButton(); });

            void Draw()
            {
                // VFX renders inside the IMGUIContainer's onGUI (the only context that advances+draws a VisualEffect);
                // here we only request a repaint so onGUI runs. ParticleSystems render right here as before.
                if (renderer.IsVfx)
                {
                    surface.MarkDirtyRepaint();
                    RepaintHostWindows();
                    return;
                }
                renderer.Orthographic = is2D;
                currentTex = renderer.RenderToTexture(renderSize);
                surface.MarkDirtyRepaint();
            }

            // Re-render the CURRENT (paused or playing) frame after a camera change (orbit/pan/zoom or 2D toggle),
            // so the new angle shows immediately. ParticleSystems can re-render on demand here; VFX only renders from
            // its onGUI, so for VFX this just requests a repaint (the camera change lands on the next live frame).
            void RenderNow()
            {
                renderer.Orthographic = is2D;
                if (!renderer.IsVfx && renderer.HasSystems)
                {
                    var t = renderer.RenderToTexture(renderSize);
                    if (t != null) currentTex = t;
                }
                surface.MarkDirtyRepaint();
                if (hostWindows.Count == 0) CaptureHostWindows(); // needed so the blit actually repaints when idle
                RepaintHostWindows();
                // Every caller of this is a camera change (orbit / pan / zoom / 2D / reset), which is precisely
                // what makes an effect leave or re-enter the viewport — so re-judge on the spot rather than
                // waiting for the next poster render.
                SyncPreviewState();
            }

            // Force the inspector window(s) to repaint each playback frame. CRITICAL: the IMGUIContainer's blit only
            // runs on a host-window repaint, and EditorApplication.update does NOT trigger one on its own, so without
            // this the RenderTexture animates on the GPU but the on-screen surface stays frozen. Telling the host
            // EditorWindow to Repaint() (the same mechanism SpringSandboxWindow uses) is what makes the animation
            // visible. Captured once at play start (cheap), repainted every tick.
            void CaptureHostWindows()
            {
                hostWindows.Clear();
                var focused = EditorWindow.focusedWindow; // at play-click time this is the inspector being clicked
                if (focused != null) hostWindows.Add(focused);
                // Then whichever window actually OWNS this widget's panel. Matching on the element's panel rather
                // than on a window TYPE NAME is both more correct and the only version that is public API: the
                // inspector's host window types are internal, and a name match silently stops matching if a future
                // editor renames the host type — the preview would then animate on the GPU and never repaint, with
                // no error. Panel identity also
                // reaches a docked inspector, a floating Properties window, or any custom host, and it finds the
                // ONE window that needs repainting instead of every inspector open in the project. Same idiom as
                // PopupCodePeek.ResolveOverlayHost and QuickEditPopup.ScreenRectOf.
                var panel = surface?.panel;
                if (panel != null)
                {
                    foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    {
                        if (w == null || hostWindows.Contains(w)) continue;
                        if (w.rootVisualElement?.panel == panel) hostWindows.Add(w);
                    }
                }
            }
            void RepaintHostWindows()
            {
                for (int i = 0; i < hostWindows.Count; i++)
                    if (hostWindows[i] != null) hostWindows[i].Repaint();
            }

            // Subscribe/unsubscribe the playback driver to EditorApplication.update. Both are idempotent so every
            // call site (start, stop, auto-loop reset, detach) is identical and a double += / stray -= is harmless.
            void StartPlaybackDriver()
            {
                if (updateSubscribed) return;           // guard against double += (e.g. rapid re-click)
                EditorApplication.update += playUpdate;
                updateSubscribed = true;
            }
            void StopPlaybackDriver()
            {
                if (!updateSubscribed) return;          // idempotent; safe when not subscribed
                EditorApplication.update -= playUpdate;
                updateSubscribed = false;
            }

            // ----- VFX (render-driven) playback helpers. A VisualEffect advances ONLY when PreviewRenderUtility.Render
            //       runs inside the IMGUIContainer's onGUI (SubmitRenderRequest / update-tick renders neither advance
            //       nor draw it). So for VFX we never render from the update tick:
            //       we keep the effect unpaused and pump host-window Repaints each tick, and onGUI renders+advances one
            //       frame per repaint (mirrors Unity's VFXAssetEditor: pause = !animated, Repaint every frame). "Play"
            //       pumps indefinitely; the idle "poster" pumps once per target until the effect settles (capturing the
            //       most-developed frame) then freezes on it. -----
            void StartVfxPlay()
            {
                posterPumping = false;
                renderer.SetVfxPlayRate(1f); // normal-speed playback (the develop may have left it fast-forwarded)
                renderer.RestartVfx();
                renderer.SetVfxPaused(false);
                playing = true;
                ParticlePlaybackCoordinator.NotifyStarted(playToken, StopPlayback); // stop other playing previews past the cap
                CaptureHostWindows();
                StartPlaybackDriver();
            }
            void StartVfxPoster()
            {
                if (playing) return;
                // INSTANT path: this .vfx already has a developed thumbnail (cached from an earlier selection, even a
                // different object using the same asset). Show it immediately — no develop, no "loading" beat.
                var cached = renderer.TryGetCachedVfxPoster();
                if (cached != null)
                {
                    currentTex = cached;
                    vfxPosterReady = true;
                    renderer.SetVfxPaused(true);
                    surface.MarkDirtyRepaint();
                    return;
                }
                // First time for this asset: develop ONCE (hidden), capturing the most-developed frame, then cache it
                // for instant reuse. Bridge the develop with Unity's generic VFX asset icon so the panel never shows a
                // black/empty square while the effect spawns (Unity has no rendered static thumbnail for .vfx).
                var placeholder = renderer.GetVfxAssetThumbnail();
                if (placeholder == null)
                {
                    var fb = CurrentTarget();
                    if (fb != null) placeholder = AssetPreview.GetMiniThumbnail(fb);
                }
                if (placeholder != null) currentTex = placeholder;
                posterBestScore = 0;
                posterPlateauFrames = 0;
                renderer.RestartVfx();
                renderer.SetVfxPlayRate(VfxDevelopRate);
                renderer.SetVfxPaused(false);
                posterPumping = true;
                posterPumpStart = EditorApplication.timeSinceStartup;
                CaptureHostWindows();
                StartPlaybackDriver();
                surface.MarkDirtyRepaint();
            }

            // Expandable mode renders lazily: nothing is drawn (no SRP work) until the caret opens the panel. The
            // other three modes are always "open". SetExpanded (below) flips isOpen (declared at the top) when the
            // caret toggles.

            // The on-demand render entry point for the idle POSTER frame (target change, resize, attach, property
            // change). Runs outside the inspector's IMGUI paint (callbacks), so the SRP render request is valid and
            // actually produces pixels. Playback frames are NOT driven here — see playUpdate (EditorApplication.update).
            void Tick()
            {
                var target = CurrentTarget();
                bool targetChanged = !ReferenceEquals(target, lastTarget);
                // The renderer can go empty UNDER a widget that is still alive and still pointed at the same effect:
                // the concurrency soft cap can turn an acquire down, and a re-parent whose detach outlasts a tick
                // runs the PanelLifetime teardown on a widget that then comes straight back (Unity re-parents
                // inspector content for tab/dock moves and layout changes). Neither of those changes the TARGET, so
                // a "did the reference change?" guard on its own strands the preview for good: the ▶ badge is still
                // on screen, clicking it hits TogglePlayback's `!HasSystems` early-out and does nothing, and the
                // panel silently degrades to a static mini-thumbnail until something force-rebuilds the inspector
                // (a designer Save). Re-acquiring is what the Asset Preview widget's SyncTargetIfChanged has always
                // done via `|| !renderer.HasRenderable` — this is the same rule on the particle path.
                bool reacquire = !targetChanged && renderer.NeedsAcquire(target);
                if (targetChanged || reacquire)
                {
                    lastTarget = target;
                    // A pose framed for the PREVIOUS effect means nothing on this one; the 2D choice is kept
                    // (it is a preference about the field, not about the asset). A re-acquire is the SAME effect
                    // as before, so its remembered framing must survive untouched — retargeting there would throw
                    // away the angle the user left every time a dock move rebuilt the renderer.
                    if (targetChanged) PreviewViewState.Retarget(view, target);
                    renderer.SetTarget(target);
                    renderer.Orthographic = is2D; // keep the camera mode across target changes
                    // SetTarget reframes to the new bounds, so the remembered pose is re-applied on top — this is
                    // what makes a rebuild (and the first build after a selection change) open where the user left it.
                    RestorePoseIfAny();
                    // target changed mid-play / mid-pump -> stop cleanly so a stale driver can't drive the new target
                    if (playing || posterPumping) { playing = false; posterPumping = false; ParticlePlaybackCoordinator.NotifyStopped(playToken); StopPlaybackDriver(); }
                    vfxPosterReady = false; // the new target needs its own one-time poster develop
                    posterBestScore = 0;
                    posterPlateauFrames = 0;
                    blankPoster = false;    // unjudged on a new effect — never accuse before a poster exists
                    lastSim = 0f;
                    needsRender = true;
                    // A spent teardown watch frees nothing, so the utility we just took back would outlive the
                    // widget's next real detach and hold a slot against the cap for the rest of the session.
                    if (reacquire) rearmRendererCleanup?.Invoke();
                    SyncPlayButton();
                }

                if (!isOpen) return; // Expandable & collapsed: skip all rendering/GPU work until opened

                if (!renderer.HasSystems)
                {
                    // No previewable ParticleSystem (a non-particle object, or the concurrency cap was hit): show a
                    // static asset thumbnail instead of an empty panel. GetMiniThumbnail is immediate; GetAssetPreview
                    // upgrades to a richer thumbnail once Unity finishes generating it (picked up by the refresh poll).
                    var fb = CurrentTarget();
                    Texture fallback = fb != null ? (AssetPreview.GetAssetPreview(fb) ?? AssetPreview.GetMiniThumbnail(fb)) : null;
                    if (!ReferenceEquals(currentTex, fallback)) { currentTex = fallback; surface.MarkDirtyRepaint(); }
                    return;
                }

                if (!playing && needsRender)
                {
                    if (renderer.IsVfx)
                    {
                        // A VFX can't be deterministically seeked — it develops a poster by briefly pumping real
                        // repaints (unpause, render a few frames inside onGUI, then freeze). Do this ONCE per target:
                        // not again on resize/repaint (that caused the repeated "auto-play"). Once a still exists,
                        // onGUI just re-blits it.
                        if (!vfxPosterReady && !posterPumping) StartVfxPoster();
                    }
                    else
                    {
                        renderer.Seek(PosterTime());
                        Draw();
                        // ONE read-back per settled poster, never per frame: Draw() has just rendered the poster
                        // into the RT synchronously, so this is the cheapest correct moment to ask whether that
                        // frame actually shows anything. (Playback frames deliberately skip it — a playing effect
                        // explains itself, and sampling every frame would be a read-back per repaint.)
                        blankPoster = renderer.CountDevelopedPixels() < BlankPosterScore;
                    }
                    needsRender = false;
                    SyncPreviewState();
                }
            }

            // The playback driver: runs off EditorApplication.update (NOT the panel scheduler, which stalls when the
            // inspector is idle/unfocused) while playing. Loops 0 -> MaxDuration until the user clicks stop. Runs
            // outside IMGUI paint, so the SRP render in Draw() is valid. Self-stops if the widget is detached or the
            // target is gone — EditorApplication.update can fire once more AFTER DetachFromPanelEvent.
            playUpdate = () =>
            {
                if (CurrentTarget() == null || !renderer.HasSystems)
                {
                    playing = false; posterPumping = false;
                    StopPlaybackDriver();
                    SyncPlayButton();
                    return;
                }

                if (renderer.IsVfx)
                {
                    // VFX advances inside onGUI; the driver's only job is to keep the host repainting so onGUI fires
                    // (one render = one sim step). "Play" pumps indefinitely; a poster pumps until the effect settles
                    // then pauses on the best developed frame (after which onGUI just re-blits the cached RT — no GPU).
                    surface.MarkDirtyRepaint();
                    RepaintHostWindows();
                    if (posterPumping && !playing)
                    {
                        // End the develop when the effect has SETTLED (best frame meaningfully developed AND not
                        // improving), or at a hard time cap — whichever comes first. The best frame was already cached
                        // in onGUI as it was found, so here we just freeze and reveal it.
                        float pumped = (float)(EditorApplication.timeSinceStartup - posterPumpStart);
                        bool settled = posterBestScore >= MinDevelopScore && posterPlateauFrames >= PosterPlateauFrames;
                        bool capped = pumped >= VfxDevelopMaxSeconds;
                        if (settled || capped)
                        {
                            renderer.SetVfxPaused(true);     // freeze the effect (no more GPU once frozen)
                            renderer.SetVfxPlayRate(1f);     // restore real-time rate for actual playback
                            posterPumping = false;
                            vfxPosterReady = true;
                            // The develop already scored every frame it saw, so the best score IS the answer to
                            // "did anything ever appear?" — no extra read-back needed. `capped` without a score
                            // means 1.5s of pumping produced nothing visible, which is exactly the blank case.
                            blankPoster = posterBestScore < BlankPosterScore;
                            SyncPreviewState(); // the develop just settled — this is when the verdict becomes real
                            StopPlaybackDriver();
                            // Reveal the best developed frame from the cache. If nothing ever developed (cache empty),
                            // keep the placeholder icon rather than flashing a black square.
                            var cached = renderer.TryGetCachedVfxPoster();
                            if (cached != null) currentTex = cached;
                            surface.MarkDirtyRepaint();
                            RepaintHostWindows();
                        }
                    }
                    return;
                }

                if (!playing) { StopPlaybackDriver(); SyncPlayButton(); return; }

                float elapsed = (float)(EditorApplication.timeSinceStartup - playStartTime);
                if (elapsed >= renderer.MaxDuration)
                {
                    // Loop: restart the cycle from t=0 and keep playing until the user clicks stop.
                    renderer.Seek(0f);
                    playStartTime = EditorApplication.timeSinceStartup;
                    lastSim = 0f;
                }
                else
                {
                    renderer.Step(elapsed - lastSim);
                    lastSim = elapsed;
                }
                Draw();
                RepaintHostWindows(); // make the updated RenderTexture actually show on screen
            };

            // Click anywhere on the preview panel to toggle playback (YouTube-style). The ▶ badge is only a hint
            // (pickingMode Ignore) and is hidden during playback, so the second click lands on the panel and stops it.
            void TogglePlayback()
            {
                if (!renderer.HasSystems) return;
                if (renderer.IsVfx)
                {
                    if (playing || posterPumping)
                    {
                        // Stop = freeze IMMEDIATELY on the current frame (which becomes the poster). No re-develop —
                        // that was the ~1s "stop lag". The frame shown when you click stop is already a developed one.
                        playing = false;
                        posterPumping = false;
                        ParticlePlaybackCoordinator.NotifyStopped(playToken);
                        StopPlaybackDriver();
                        renderer.SetVfxPlayRate(1f); // in case stop interrupted a fast-forwarded develop
                        renderer.SetVfxPaused(true);
                        vfxPosterReady = true;
                    }
                    else
                    {
                        StartVfxPlay();
                    }
                    SyncPlayButton();
                    return; // VFX renders in onGUI; the driver pumps the repaints (no immediate Draw needed)
                }
                if (playing)
                {
                    playing = false;
                    ParticlePlaybackCoordinator.NotifyStopped(playToken);
                    StopPlaybackDriver();
                    renderer.Seek(PosterTime());
                }
                else
                {
                    playing = true;
                    ParticlePlaybackCoordinator.NotifyStarted(playToken, StopPlayback); // stop other playing previews past the cap
                    playStartTime = EditorApplication.timeSinceStartup;
                    lastSim = 0f;
                    renderer.Seek(0f);
                    CaptureHostWindows();
                    StartPlaybackDriver();
                }
                SyncPlayButton();
                Draw(); // render the first frame immediately (t=0 on start, poster on stop)
            }
            // Click the panel to toggle playback — EXCEPT on the object-picker "assign" selector that
            // ThumbnailOnly mode re-parents into the panel (clicking it should open the picker, not play).
            static bool WithinSelector(VisualElement el)
            {
                while (el != null)
                {
                    if (el.ClassListContains("unity-object-field__selector")) return true;
                    el = el.parent;
                }
                return false;
            }
            previewWrap.RegisterCallback<ClickEvent>(evt =>
            {
                if (WithinSelector(evt.target as VisualElement)) return;
                if (camDragged) { camDragged = false; return; } // that was a camera drag, not a click — don't toggle
                TogglePlayback();
            });

            // Orbit (left-drag) / pan (middle-drag, or any drag while in 2D) / zoom (Alt+scroll) the preview camera.
            // A plain click still toggles playback (handler above): a press only becomes a "drag" once the pointer
            // moves past a small threshold, and that click is then suppressed. RenderNow re-renders so the new angle
            // shows whether the effect is playing or paused (ParticleSystem; VFX updates on its next live frame).
            previewWrap.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!interactive || !renderer.HasSystems) return;
                if (WithinSelector(evt.target as VisualElement)) return;
                if (evt.button != 0 && evt.button != 2) return;
                camDragging = true; camDragged = false; camButton = evt.button;
                camStartPos = camLastPos = (Vector2)evt.position;
                previewWrap.CapturePointer(evt.pointerId);
                SyncPlayButton(); // hide the stop badge for the duration of the press/drag
            });
            previewWrap.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!camDragging) return;
                Vector2 pos = (Vector2)evt.position;
                Vector2 d = pos - camLastPos;
                camLastPos = pos;
                if (!camDragged)
                {
                    if ((pos - camStartPos).sqrMagnitude < 9f) return; // 3px before a press counts as a drag
                    camDragged = true;
                }
                if (is2D || camButton == 2) renderer.Pan(d); else renderer.Orbit(d);
                RememberPose();
                RenderNow();
                evt.StopPropagation();
            });
            previewWrap.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!camDragging) return;
                camDragging = false;
                if (previewWrap.HasPointerCapture(evt.pointerId)) previewWrap.ReleasePointer(evt.pointerId);
                SyncPlayButton(); // restore the badge once the drag ends
            });
            previewWrap.RegisterCallback<WheelEvent>(evt =>
            {
                // Zoom requires Alt+wheel so a plain scroll still scrolls the Inspector. Without Alt we leave the
                // event un-stopped so it bubbles to the surrounding ScrollView instead of hijacking it.
                if (!interactive || !renderer.HasSystems || !evt.altKey) return;
                renderer.Zoom(evt.delta.y);
                RememberPose();
                RenderNow();
                evt.StopPropagation();
            });

            // Stop playback and return to the poster frame (used when an Expandable preview is collapsed).
            void StopPlayback()
            {
                if (renderer.IsVfx)
                {
                    if (!playing && !posterPumping) return;
                    playing = false; posterPumping = false;
                    ParticlePlaybackCoordinator.NotifyStopped(playToken);
                    StopPlaybackDriver();
                    renderer.SetVfxPlayRate(1f);
                    renderer.SetVfxPaused(true); // freeze immediately on collapse, keeping the last developed frame
                    vfxPosterReady = true;
                    SyncPlayButton();
                    return;
                }
                if (!playing) return;
                playing = false;
                ParticlePlaybackCoordinator.NotifyStopped(playToken);
                StopPlaybackDriver();
                renderer.Seek(PosterTime());
                SyncPlayButton();
            }

            // Map the shared ImageAlignment (Left/Center/Right) to a cross-axis Align (for column layouts:
            // FieldAndThumbnail + the Expandable fold wrapper) or a main-axis Justify (for the ThumbnailOnly row).
            Align AlignFor(ImageAlignment a) => a == ImageAlignment.Center ? Align.Center : a == ImageAlignment.Right ? Align.FlexEnd : Align.FlexStart;
            Justify JustifyFor(ImageAlignment a) => a == ImageAlignment.Center ? Justify.Center : a == ImageAlignment.Right ? Justify.FlexEnd : Justify.FlexStart;

            // Arrange the field + preview panel for the three "always-open" modes (Expandable is built separately).
            // Mirrors the AssetPreview widget's layout surgery — the panel lives INSIDE the field's
            // unity-base-field__input, so the field's aligned label column governs its position (alignment fix).
            // Object DROPS onto the panel are ObjectSlotSurface's job, not the field's — see the note in the
            // ThumbnailOnly branch below.
            void SetupParticleLayout()
            {
                var inputEl = objField.Q<VisualElement>(className: "unity-base-field__input");
                if (inputEl == null) return;
                var pickerEl = inputEl.Q<VisualElement>(className: "unity-object-field__input");

                if (mode == ParticlePreviewMode.ThumbnailOnly)
                {
                    // Hide the field text + border so only the live thumbnail shows; re-parent the object-picker
                    // selector into the panel as a small corner "assign" button.
                    // NOTE: hiding the display is exactly what KILLS the field's drag-and-drop — ObjectFieldDisplay
                    // is the only element an ObjectField wires drop handling to, so "the bound field stays in the
                    // tree" (this comment's old claim) never made the thumbnail droppable. ObjectSlotSurface, wired
                    // at the end of this builder, is what actually provides it.
                    var display = objField.Q<VisualElement>(className: "unity-object-field-display");
                    if (display != null) display.style.display = DisplayStyle.None;
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
                    }
                    var selector = objField.Q<VisualElement>(className: "unity-object-field__selector");
                    if (selector != null)
                    {
                        previewWrap.Add(selector);
                        selector.style.position = Position.Absolute;
                        selector.style.right = 2;
                        selector.style.bottom = 2;
                        selector.style.width = 16;
                        selector.style.height = 16;
                    }
                    previewWrap.style.marginLeft = 0;
                    previewWrap.style.marginTop = 0;
                    inputEl.style.flexDirection = FlexDirection.Row;
                    inputEl.style.alignItems = Align.Center;
                    inputEl.style.justifyContent = JustifyFor(node.PreviewAlignment); // Left/Center/Right (like the asset preview)
                    inputEl.Add(previewWrap);
                }
                else if (mode == ParticlePreviewMode.Standard)
                {
                    // Field left, preview right, one line (like the asset preview "beside" layout).
                    inputEl.style.flexDirection = FlexDirection.Row;
                    inputEl.style.alignItems = Align.Center;
                    previewWrap.style.marginLeft = 6;
                    previewWrap.style.marginTop = 0;
                    if (pickerEl != null) pickerEl.style.flexGrow = 1;
                    inputEl.Add(previewWrap);
                }
                else // FieldAndThumbnail (default): field on top, thumbnail below.
                {
                    // inputEl holds the field box AND its assign selector as direct children (the ⊕ is a sibling of
                    // the field box, not inside it). Column-stacking inputEl directly would drop the selector onto
                    // its own row BELOW the field. So keep the field box + selector together in a ROW, then stack
                    // the thumbnail beneath that row in a column — the selector stays on the field's right edge.
                    var fieldRow = new VisualElement();
                    fieldRow.style.flexDirection = FlexDirection.Row;
                    fieldRow.style.alignItems = Align.Center;
                    fieldRow.style.width = Length.Percent(100);
                    var existingKids = new List<VisualElement>();
                    foreach (var child in inputEl.Children()) existingKids.Add(child);
                    foreach (var child in existingKids) fieldRow.Add(child);
                    if (pickerEl != null) pickerEl.style.flexGrow = 1; // field box fills; the selector hugs the right

                    inputEl.style.flexDirection = FlexDirection.Column;
                    inputEl.style.alignItems = AlignFor(node.PreviewAlignment); // Left/Center/Right (like the asset preview)
                    inputEl.Add(fieldRow);
                    previewWrap.style.marginTop = 4;
                    previewWrap.style.marginLeft = 0;
                    inputEl.Add(previewWrap);
                }
            }

            VisualElement root;
            if (mode == ParticlePreviewMode.Expandable)
            {
                // Encased foldout-style group SHARED with the Curve/Gradient widgets and the expandable
                // containers (caret + outline + spring-fold + one encased unit). The bound ObjectField is the
                // header's main element (kept editable — the shared builder StopPropagation's its clicks), and
                // the preview panel folds open below. Open-state is remembered per type via FoldoutStateStore.
                //
                // CRITICAL: fold a WRAPPER around the panel, not the panel itself. The panel's only children
                // (the IMGUIContainer surface + the play badge) are position:absolute, so folding the panel
                // directly (whose height SpringFold sets to Auto while measuring) opens it to ~nothing. The
                // wrapper keeps the fixed-height panel as an in-flow child, so its natural height measures full.
                var foldContent = new VisualElement();
                foldContent.AddToClassList("sasq-particle-fold-content");
                foldContent.style.alignItems = AlignFor(node.PreviewAlignment); // Left/Center/Right (like the asset preview)
                foldContent.Add(previewWrap);

                // expanded → drive render/play; userInitiated gates auto-play so a restored-open foldout doesn't
                // auto-play on selection (only an explicit user open does, matching the original behavior).
                void HandleExpanded(bool e, bool userInitiated)
                {
                    isOpen = e;
                    if (e)
                    {
                        needsRender = true;
                        if (userInitiated)
                        {
                            Tick(); // render the poster now that we're open
                            if (node.ParticlePlayOnOpen && !playing) TogglePlayback();
                        }
                        // restored-open: the AttachToPanel/poll Tick (below) renders the poster on attach
                    }
                    else
                    {
                        StopPlayback(); // stop immediately on collapse (don't wait out the close animation)
                    }
                }

                // Same header shape as the asset-preview row, so it joins the same label column — otherwise a
                // particle row stacked with curve/gradient/asset rows is the one whose field edge is off.
                objField.AddToClassList("unity-base-field__aligned");

                root = BuildExpandableGroup(
                    title: resolvedLabel,
                    headerExtra: null,
                    content: foldContent,
                    stateKey: FoldoutStateStore.Key(prop.serializedObject.targetObject, prop.propertyPath, resolvedLabel),
                    fallbackExpanded: node.ParticleDefaultToOpen,
                    headerMainOverride: objField,
                    onExpandedChanged: HandleExpanded);

                // Make the WHOLE header bar toggle the fold, EXCEPT the object-input box (so the user can still
                // assign/drag/pick the effect there without it collapsing). The shared builder does NOT
                // blanket-stop the field, so the field's label + the padding + the empty header all toggle; we
                // stop propagation on ONLY the input box. The box sub-element doesn't exist until the field
                // attaches to a panel, so resolve it on attach.
                void StopHeaderFieldInput()
                {
                    var inputBox = objField.Q<VisualElement>(className: "unity-object-field__input");
                    if (inputBox == null) return;
                    inputBox.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    inputBox.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                }
                if (objField.Q<VisualElement>(className: "unity-object-field__input") != null) StopHeaderFieldInput();
                else objField.RegisterCallback<AttachToPanelEvent>(_ => StopHeaderFieldInput());
            }
            else
            {
                // The field's input element may not exist until the field attaches to a panel (mirrors AssetPreview).
                var initialInput = objField.Q<VisualElement>(className: "unity-base-field__input");
                if (initialInput != null) SetupParticleLayout();
                else objField.RegisterCallback<AttachToPanelEvent>(_ => SetupParticleLayout());
                root = objField;
            }

            root.AddToClassList("sasq-particle-preview-field");

            // ---- The preview PANEL behaves like an object slot, at every level ----
            // The panel is not Unity's ObjectFieldDisplay, and that display is the ONLY element an ObjectField
            // wires drag-to-assign and Delete-to-unassign to — so dropping an effect onto the thumbnail did
            // nothing, Delete did nothing, and ThumbnailOnly (which hides the display outright) could only be
            // assigned through the corner picker. Same defect and same fix as the Asset Preview widget: see
            // ObjectSlotSurface. LIVE ONLY — the designer canvas previews the user's object and owns Delete as
            // "remove the selected node".
            if (!LayoutTreeBuilder.CurrentDesignMode)
            {
                ObjectSlotSurface.AttachDropTarget(previewWrap, prop, objField.objectType);
                ObjectSlotSurface.AttachClearShortcut(previewWrap, objField, prop);
                if (mode == ParticlePreviewMode.ThumbnailOnly)
                    ObjectSlotSurface.AttachDropTarget(objField, prop, objField.objectType, TrickleDown.NoTrickleDown);
            }

            // Designer live-edit hooks (see the tables' doc comment). LiveRestyle pushes the cheap, per-tick
            // updates (panel size/color, play-button size/anchor/offset, camera props) straight onto the rendered
            // element; ForceReRender re-develops a frozen VFX poster so a zoom/background change actually reframes.
            void LiveRestyle()
            {
                size = Mathf.Clamp(node.ParticleThumbnailSize, 16f, 256f);
                previewWrap.style.width = size;
                previewWrap.style.height = size;
                // The blit surface must follow the panel: it's pinned to an explicit size (an
                // IMGUIContainer has no intrinsic size), so leaving it behind kept drawing the effect
                // at the OLD size inside the growing/shrinking panel until the commit rebuild — the
                // "content lags the canvas by the debounce" jank. The rendered frame now scales in
                // lockstep with the drag (ScaleToFit blit); the commit re-renders it crisp.
                surface.style.width = size;
                surface.style.height = size;
                previewWrap.style.backgroundColor = node.ParticleBackgroundColor;
                renderer.BackgroundColor = node.ParticleBackgroundColor;
                renderer.VfxZoom = node.ParticlePreviewZoom;
                PositionPlayButton();     // re-reads the (now-updated) size + button width/anchor/offset
                PositionOverlayButtons();
                // ParticleSystems re-render cheaply so background/framing show instantly. A frozen VFX poster
                // follows color/zoom drags via a one-frame PAUSED re-render on the next repaint (flag consumed
                // in onGUI — the render must run inside an IMGUI repaint). Gated on the values actually
                // changing so SIZE drags never swap the best-frame poster for the paused end-state frame.
                if (!renderer.IsVfx) RenderNow();
                else
                {
                    if (vfxPosterReady && (lastLiveBg != node.ParticleBackgroundColor ||
                                           !Mathf.Approximately(lastLiveZoom, node.ParticlePreviewZoom)))
                        vfxRecolorPending = true;
                    surface?.MarkDirtyRepaint();
                }
                lastLiveBg = node.ParticleBackgroundColor;
                lastLiveZoom = node.ParticlePreviewZoom;
            }
            void ForceReRender()
            {
                renderer.BackgroundColor = node.ParticleBackgroundColor;
                renderer.VfxZoom = node.ParticlePreviewZoom;
                if (renderer.IsVfx) { vfxPosterReady = false; posterBestScore = 0; posterPlateauFrames = 0; }
                needsRender = true;
                Tick();
            }
            s_particleLiveRestyle.Add(previewWrap, LiveRestyle);
            s_particleForceReRender.Add(previewWrap, ForceReRender);

            // Re-render the poster when the reference changes or the panel is (re)sized; render once on attach.
            objField.RegisterValueChangedCallback(_ => { needsRender = true; Tick(); });
            root.TrackPropertyValueWithRefresh(prop, _ => { needsRender = true; Tick(); });
            // SIZE changes only. A GeometryChangedEvent also fires when the element merely MOVES — and in
            // Standard mode (and Compact/FieldAndThumbnail with Center/Right alignment) dragging the
            // inspector's edge moves this box on every frame while its pixel size stays pinned. Each of those
            // used to run a full Tick(): a Simulate restart, a draw into a render texture up to 512x512, and
            // then CountDevelopedPixels — a SYNCHRONOUS GPU->CPU stall (ReadPixels + Apply + GetPixels32 over
            // as many as 262k pixels, walked twice). Measured on a 60-frame width drag: Standard mode 1.47ms
            // per frame against 0.05ms for the default FieldAndThumbnail layout, whose fixed box never fires.
            //
            // Skipping a move is an input-equality skip, not a deferral: the render output depends on the
            // render SIZE, the target, the zoom and the background — never on where the box sits on screen —
            // so a re-render after a pure move produces the identical texture. Every other trigger is
            // untouched (target change, TrackPropertyValue, LiveRestyle, ForceReRender, attach).
            previewWrap.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.size == evt.oldRect.size) return; // moved, not resized — same pixels
                needsRender = true;
                Tick();
            });
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                needsRender = true;
                Tick();
            });

            // Low-frequency poll (4Hz) used ONLY for the idle/fallback path: it lets the async AssetPreview
            // thumbnail upgrade once Unity finishes generating it, and re-acquires a render utility if the
            // concurrency cap frees up. Playback does NOT depend on this (it runs off EditorApplication.update);
            // when a ParticleSystem is present and idle, Tick() is a cheap no-op, so this never drives rendering.
            root.schedule.Execute(Tick).Every(250);

            // ----- Disposal. Playback is driven by EditorApplication.update (see playUpdate); the poster/idle
            //       renders are driven by the event callbacks above. The detach handler MUST detach the update
            //       driver AND clean up the PreviewRenderUtility + RenderTexture (widgets attach/detach far more
            //       often than windows open/close, so leaking per rebuild adds up fast). -----
            //       Gated on a REAL detach: a re-parent (window tab drag, collection row reorder) re-attaches
            //       this same widget, and Tick() only re-acquires when the TARGET changed — so a Cleanup()
            //       there permanently downgraded the live preview to a static thumbnail. The 4Hz refresh poll
            //       above needs no explicit Pause: UI Toolkit pauses scheduled items on detach and resumes
            //       them on attach, whereas an explicit Pause() is never resumed. See PanelLifetime.
            //       A release here is NOT always final: PanelLifetime can only ask "still off-panel a tick later?",
            //       and a long-enough re-parent answers yes for a widget that is coming back. Tick() re-acquires
            //       when it finds itself renderer-less, and re-arms this watch through the returned callback.
            rearmRendererCleanup = PanelLifetime.OnRealDetach(root, () =>
            {
                playing = false;
                posterPumping = false;
                ParticlePlaybackCoordinator.NotifyStopped(playToken);
                StopPlaybackDriver(); // critical: a leaked EditorApplication.update closure would throw every editor tick
                renderer.Cleanup();
            });

            return root;
        }
#endif // SASQ_PARTICLES
    }

    /// <summary>
    /// Caps how many Particle System Preview widgets PLAY (animate + render) at once, project-wide. The limit is
    /// the <see cref="SasquatchInspector.Runtime.SasquatchSettings.MaxParticlePreviews"/> setting (default 1, so pressing
    /// play on one instantly stops any other). When a preview starts, the oldest playing preview(s) are stopped
    /// until the cap is satisfied — keeping the editor responsive when many previews share one inspector.
    /// Idle/poster thumbnails are cheap and unaffected; only active playback is coordinated. Static, so it resets
    /// on domain reload.
    /// </summary>
    internal static class ParticlePlaybackCoordinator
    {
        // Oldest → newest playing previews and how to stop each.
        private static readonly List<(object token, Action stop)> s_playing = new List<(object, Action)>();

        private static int Max
        {
            get
            {
                var s = SasquatchSettingsManager.Current;
                return s != null ? Mathf.Max(1, s.MaxParticlePreviews) : 1;
            }
        }

        /// <summary>A preview started playing. Re-registers it as the most recent and stops the oldest playing
        /// preview(s) so at most <see cref="Max"/> play simultaneously.</summary>
        public static void NotifyStarted(object token, Action stopSelf)
        {
            if (token == null || stopSelf == null) return;
            s_playing.RemoveAll(e => ReferenceEquals(e.token, token));
            s_playing.Add((token, stopSelf));
            int max = Max;
            // Remove BEFORE invoking the stop so the invoked StopPlayback's own NotifyStopped is a harmless no-op
            // (no re-entrant mutation of the list we're trimming).
            while (s_playing.Count > max)
            {
                var oldest = s_playing[0];
                s_playing.RemoveAt(0);
                try { oldest.stop?.Invoke(); } catch { /* a torn-down widget; ignore */ }
            }
        }

        /// <summary>A preview stopped playing (user stop, collapse, target change, detach). Idempotent.</summary>
        public static void NotifyStopped(object token)
        {
            if (token == null) return;
            s_playing.RemoveAll(e => ReferenceEquals(e.token, token));
        }
    }
}
