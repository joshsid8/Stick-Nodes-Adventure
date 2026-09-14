using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Owns a <see cref="PreviewRenderUtility"/> that renders a live, INTERACTIVE 3D preview of a prefab/GameObject
    /// or a Mesh for the Asset Preview widget's "Interactive" mode. The camera orbits / pans / zooms (the same feel
    /// as the Spring Sandbox window); drive it with <see cref="Orbit"/> / <see cref="Pan"/> / <see cref="Zoom"/>,
    /// then <see cref="RenderToTexture"/> to render the current view into an owned RenderTexture you blit with
    /// GUI.DrawTexture.
    ///
    /// IMPORTANT (URP): like <c>ParticlePreviewRenderer</c>, this renders via
    /// <see cref="RenderPipeline.SubmitRenderRequest"/> into our own RenderTexture (NOT BeginPreview/Render inside an
    /// IMGUIContainer), driven from an EditorApplication.update tick — the only path that actually shows pixels in a
    /// live inspector under URP. Falls back to the built-in <c>camera.Render()</c> when no SRP is active. Disposal:
    /// <see cref="Cleanup"/> is idempotent and detach-safe.
    /// </summary>
    internal sealed class AssetPreviewRenderer
    {
        private PreviewRenderUtility _preview;
        private GameObject _instance;          // instantiated prefab copy, or a temp mesh host, living in the preview scene
        private Material _tempMaterial;        // owned default material created for the Mesh path (destroyed in Cleanup)
        private Material[] _litMaterials = Array.Empty<Material>(); // materials fed the PreviewLit uniforms each render
        private UnityEngine.Object _target;
        private Vector3 _boundsCenter = Vector3.zero;
        private float _boundsRadius = 1f;
        private RenderTexture _rt;
        private int _rtSize;

        // Orbit/pan/zoom camera state (ported from SpringSandboxWindow.FrameCamera / HandlePreviewMouse).
        private Vector2 _camEuler = new Vector2(15f, 25f);
        private float _camDistance = 4f;
        private Vector3 _camPivot = Vector3.zero;

        /// <summary>When true the viewport renders with an orthographic, straight-on (scene-view 2D) camera —
        /// orbit is meaningless, only pan + zoom apply. The widget defaults this ON for sprites/textures and lets
        /// the "2D" toggle button flip it. Mirrors the Spring Sandbox's 2D mode.</summary>
        public bool Orthographic { get; set; }

        // Soft cap on concurrently-live PreviewRenderUtility instances (shared spirit with ParticlePreviewRenderer):
        // the widget already guarantees only ONE asset preview renders at a time, but a stray leak can't blow past
        // this. Statics reset on domain reload.
        private static int s_liveUtilities;
        public const int MaxConcurrent = 8;

        // Live instances that own a PreviewRenderUtility, so they can ALL be torn down before a domain reload.
        // A PreviewRenderUtility holds a native preview scene; GC'ing one without Cleanup() — which is exactly
        // what happens on assembly reload, since the widget's DetachFromPanelEvent does NOT reliably fire first
        // — leaks the scene and its finalizer logs "A PreviewRenderUtility was not cleaned up properly...".
        // ParticlePreviewRenderer and the export kit's SasqAssetRenderer twin both already do this; this class,
        // the one they were modelled on, was the only holdout.
        private static readonly HashSet<AssetPreviewRenderer> s_liveInstances = new HashSet<AssetPreviewRenderer>();

        static AssetPreviewRenderer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CleanupAllLive;
            EditorApplication.quitting += CleanupAllLive;
        }

        /// <summary>Tears down every live PreviewRenderUtility before a domain reload / quit so no native preview
        /// scene leaks. Idempotent; Cleanup() removes the instance from the set, so iterate a copy.</summary>
        internal static void CleanupAllLive()
        {
            var copy = new List<AssetPreviewRenderer>(s_liveInstances);
            foreach (var r in copy) { try { r.Cleanup(); } catch (Exception) { } }
            s_liveInstances.Clear();
        }

        public Color BackgroundColor = new Color(0.13f, 0.13f, 0.15f, 1f);

        /// <summary>True once a renderable target (a prefab/GameObject with a Renderer, or a Mesh) is set up.</summary>
        public bool HasRenderable => _instance != null;

        /// <summary>Whether a reference can be shown in the interactive viewport: a Mesh, a GameObject/Component
        /// whose hierarchy contains a Renderer, a Sprite, or a Texture2D. (Sprites/textures render on a flat quad and
        /// default to the 2D camera — see <see cref="Orthographic"/>.) Other asset types keep the static thumbnail.</summary>
        public static bool CanRender(UnityEngine.Object target)
        {
            switch (target)
            {
                case null: return false;
                case Mesh _: return true;
                case Sprite _: return true;
                case Texture2D _: return true;
                case GameObject go: return go.GetComponentInChildren<Renderer>(true) != null;
                case Component c: return c.gameObject.GetComponentInChildren<Renderer>(true) != null;
                default: return false;
            }
        }

        /// <summary>True for the inherently-2D renderables (Sprite / Texture2D) — the widget defaults the 2D toggle ON
        /// for these and OFF (3D orbit) for meshes &amp; prefabs.</summary>
        public static bool IsTwoDByNature(UnityEngine.Object target) => target is Sprite || target is Texture2D;

        /// <summary>Point the renderer at a new prefab/mesh. Tears down any prior instance, builds a hidden copy into
        /// the preview scene, and frames the camera to its bounds. A null / non-renderable target leaves it empty.</summary>
        public void SetTarget(UnityEngine.Object target)
        {
            if (ReferenceEquals(target, _target) && _instance != null) return;
            _target = target;
            DestroyInstance();
            if (!CanRender(target)) return;

            // Concurrency soft cap: don't allocate another render scene past the cap.
            if (_preview == null && s_liveUtilities >= MaxConcurrent) return;

            EnsurePreview();

            try
            {
                if (target is Mesh mesh)
                {
                    _instance = new GameObject("SasqAssetPreviewMesh") { hideFlags = HideFlags.HideAndDontSave };
                    var mf = _instance.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = _instance.AddComponent<MeshRenderer>();
                    // Self-lit PreviewLit shader (under URP, incl. the 2D Renderer) so the mesh shades as a proper 3D
                    // object instead of rendering flat/white — the same fix used by the Spring Sandbox.
                    var previewShader = PickPreviewShader() ?? Shader.Find("Sprites/Default"); // last-resort guard
                    _tempMaterial = new Material(previewShader) { hideFlags = HideFlags.HideAndDontSave };
                    if (_tempMaterial.HasProperty("_BaseColor")) _tempMaterial.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.82f, 1f));
                    else if (_tempMaterial.HasProperty("_Color")) _tempMaterial.SetColor("_Color", new Color(0.8f, 0.8f, 0.82f, 1f));
                    mr.sharedMaterial = _tempMaterial;
                    _litMaterials = new[] { _tempMaterial };
                }
                else if (target is Sprite sprite)
                {
                    // Sprites render via a native SpriteRenderer (handles atlas rects + pivots), unlit. Same approach
                    // as the Spring Sandbox's sprite shape. No PreviewLit uniforms.
                    _instance = new GameObject("SasqAssetPreviewSprite") { hideFlags = HideFlags.HideAndDontSave };
                    var sr = _instance.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    _litMaterials = Array.Empty<Material>();
                }
                else if (target is Texture2D tex)
                {
                    // Textures render on a flat, double-sided unlit quad scaled to the texture's aspect ratio.
                    _instance = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                    // Strip the primitive's auto-added collider — no physics in a preview scene. Done by
                    // ALLOW-LIST (keep Transform / MeshFilter / Renderer, destroy the rest) rather than
                    // GetComponent<Collider>(): Collider lives in com.unity.modules.physics, a REMOVABLE
                    // built-in module, and naming it would stop this whole assembly compiling in a project
                    // that trims it. CreatePrimitive yields exactly Transform + MeshFilter + one Collider +
                    // MeshRenderer, and all three kept types live in the non-removable CoreModule, so this
                    // destroys precisely the collider. That premise is pinned by
                    // TestPrimitiveColliderStripAllowListPremise.
                    foreach (var comp in _instance.GetComponents<Component>())
                        if (comp != null && !(comp is Transform) && !(comp is MeshFilter) && !(comp is Renderer))
                            UnityEngine.Object.DestroyImmediate(comp);
                    var mr = _instance.GetComponent<MeshRenderer>();
                    var unlitShader = PickUnlitShader() ?? Shader.Find("Sprites/Default"); // last-resort guard
                    _tempMaterial = new Material(unlitShader) { hideFlags = HideFlags.HideAndDontSave };
                    if (_tempMaterial.HasProperty("_MainTex")) _tempMaterial.SetTexture("_MainTex", tex);
                    if (_tempMaterial.HasProperty("_BaseMap")) _tempMaterial.SetTexture("_BaseMap", tex);
                    mr.sharedMaterial = _tempMaterial;
                    float aspect = (float)tex.width / Mathf.Max(1, tex.height);
                    _instance.transform.localScale = aspect >= 1f
                        ? new Vector3(aspect, 1f, 1f)
                        : new Vector3(1f, 1f / aspect, 1f);
                    _litMaterials = Array.Empty<Material>();
                }
                else
                {
                    GameObject src = target as GameObject ?? (target as Component)?.gameObject;
                    if (src == null) return;
                    _instance = UnityEngine.Object.Instantiate(src);
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                    _litMaterials = CollectMaterials(_instance); // PreviewLit uniforms are a no-op on non-PreviewLit mats
                }
            }
            catch (Exception) { DestroyInstance(); return; }

            if (_instance == null) return;
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.identity;
            _preview.AddSingleGO(_instance);
            FrameToBounds();
        }

        // Pipeline-aware shader pick (mirrors SpringSandboxWindow.PickShader): every pipeline prefers the self-lit
        // PreviewLit shader, whose hand-fed key/fill/ambient uniforms shade the mesh IDENTICALLY under URP (incl.
        // the 2D Renderer), Built-in and HDRP — real preview lights read differently per pipeline (Standard under
        // Built-in looked washed-out next to URP; HDRP/Lit was near-black under auto-exposure). The native lit
        // shaders remain only as fallbacks for a corrupt/missing PreviewLit.
        private static Shader PickPreviewShader()
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

        // Double-sided unlit shader for the Texture2D quad — show the texture exactly as authored (no lighting),
        // and Sprites/Default renders Cull Off so the quad is visible from either side as it orbits.
        private static Shader PickUnlitShader()
        {
            return Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Standard");
        }

        private static Material[] CollectMaterials(GameObject root)
        {
            var set = new List<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                    if (m != null && !set.Contains(m)) set.Add(m);
            }
            return set.ToArray();
        }

        // Feeds the self-lit PreviewLit shader an explicit key/fill/ambient/rim, so a mesh shades correctly under any
        // pipeline (incl. the URP 2D Renderer). A no-op on materials without _PreviewKeyDir (URP/HDRP-Lit etc.).
        private void SetPreviewLightUniforms(Material m)
        {
            if (m == null || !m.HasProperty("_PreviewKeyDir")) return;
            m.SetVector("_PreviewKeyDir", new Vector3(0.32f, 0.55f, -0.77f).normalized);
            m.SetColor("_PreviewKeyColor", Color.white);
            m.SetVector("_PreviewFillDir", new Vector3(-0.5f, 0.1f, 0.6f).normalized);
            m.SetColor("_PreviewFillColor", new Color(0.22f, 0.24f, 0.32f, 1f));
            m.SetColor("_PreviewAmbient", new Color(0.13f, 0.13f, 0.16f, 1f));
            m.SetColor("_PreviewRimColor", new Color(0.16f, 0.22f, 0.30f, 1f));
            m.SetFloat("_PreviewRimPower", 2.5f);
        }

        // ----------------------------------------------------------------- camera ----

        /// <summary>Reset the camera to frame the whole asset (combined renderer bounds).</summary>
        public void FrameToBounds()
        {
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            if (_instance != null)
            {
                foreach (var r in _instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            }
            if (any && b.size.sqrMagnitude > 1e-5f)
            {
                _boundsCenter = b.center;
                _boundsRadius = Mathf.Max(0.05f, b.extents.magnitude);
            }
            else
            {
                _boundsCenter = Vector3.zero;
                _boundsRadius = 1f;
            }
            _camPivot = _boundsCenter;
            _camEuler = new Vector2(15f, 25f);
            float fov = (_preview != null ? _preview.camera.fieldOfView : 30f) * Mathf.Deg2Rad;
            _camDistance = _boundsRadius / Mathf.Max(0.01f, Mathf.Sin(fov * 0.5f)) * 1.4f;
        }

        public void Orbit(Vector2 delta)
        {
            _camEuler.y += delta.x * 0.4f;
            _camEuler.x = Mathf.Clamp(_camEuler.x + delta.y * 0.4f, -85f, 85f);
        }

        public void Pan(Vector2 delta)
        {
            if (_preview == null) return;
            var camT = _preview.camera.transform;
            float ps = _camDistance * 0.0022f;
            _camPivot += -camT.right * (delta.x * ps) + camT.up * (delta.y * ps);
        }

        public void Zoom(float scrollDeltaY)
        {
            float min = Mathf.Max(0.05f, _boundsRadius * 0.15f);
            float max = Mathf.Max(min + 0.1f, _boundsRadius * 12f);
            // Proportional zoom so it feels consistent for tiny and huge assets alike.
            _camDistance = Mathf.Clamp(_camDistance * (1f + scrollDeltaY * 0.05f), min, max);
        }

        /// <summary>The orbit/pan/zoom pose (euler, distance, pivot). Save it before <see cref="Cleanup"/> and
        /// restore it after a fresh <see cref="SetTarget"/> to keep the camera angle across a release/rebuild
        /// cycle — SetTarget recomputes the bounds, so only the framing is restored.</summary>
        public (Vector2 euler, float distance, Vector3 pivot) CameraState
        {
            get => (_camEuler, _camDistance, _camPivot);
            set { _camEuler = value.euler; _camDistance = value.distance; _camPivot = value.pivot; }
        }

        private void FrameCamera()
        {
            var cam = _preview.camera;
            cam.backgroundColor = BackgroundColor;
            cam.aspect = 1f;

            if (Orthographic)
            {
                // 2D: straight-on orthographic. orthographicSize tracks _camDistance (zoom) so swapping 2D↔3D keeps
                // the asset roughly the same on-screen size — same formula the Spring Sandbox uses.
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(0.05f, _camDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                cam.transform.rotation = Quaternion.identity;
                float backOff = _boundsRadius * 4f + 5f; // sit well outside the asset; ortho has no perspective anyway
                cam.transform.position = _camPivot + new Vector3(0f, 0f, -backOff);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = backOff + _boundsRadius * 8f + 10f;
                return;
            }

            cam.orthographic = false;
            var rot = Quaternion.Euler(_camEuler.x, _camEuler.y, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = _camPivot + rot * new Vector3(0f, 0f, -_camDistance);
            cam.nearClipPlane = Mathf.Max(0.01f, _camDistance - _boundsRadius * 4f);
            cam.farClipPlane = _camDistance + _boundsRadius * 8f;
        }

        /// <summary>True when the asset has been panned / zoomed / orbited so far that the viewport shows
        /// essentially nothing of it: its bounding sphere projects clear of the view rect, sits behind the lens,
        /// or covers fewer than <paramref name="minPixels"/> across.
        ///
        /// <para>This is what lets the widget tell "you lost the asset" apart from "nothing is assigned" — two
        /// states that otherwise render the same blank box. It is pure camera math over the bounds the framing
        /// already tracks (no pixel read-back, no extra render, no per-frame cost), so it is safe to poll.</para>
        ///
        /// <para>Returns false whenever there is nothing to judge (no renderable target / no preview scene) —
        /// an unknown answer must never accuse the user of losing an asset they never assigned.</para></summary>
        public bool IsTargetOffFrame(int viewportPixels, float minPixels = 3f)
        {
            if (_preview == null || _instance == null) return false;
            var cam = _preview.camera;
            FrameCamera(); // deterministic from the current pose; also what the next render will use

            // Sphere centre in CAMERA space: +z is forward, so z drives both the behind-lens test and the
            // perspective size falloff, while x/y give the lateral offset.
            Vector3 local = cam.transform.InverseTransformPoint(_boundsCenter);

            float halfHeight;
            if (cam.orthographic)
            {
                halfHeight = Mathf.Max(1e-4f, cam.orthographicSize);
            }
            else
            {
                // Entirely behind the near plane — orbiting past the asset puts it here.
                if (local.z + _boundsRadius <= cam.nearClipPlane) return true;
                halfHeight = Mathf.Max(1e-4f, Mathf.Max(local.z, 1e-4f)
                    * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }

            // FrameCamera pins aspect to 1, so half-width == half-height and one radius covers both axes.
            float span = 2f * halfHeight;
            float rNorm = _boundsRadius / span;                       // sphere radius in 0..1 viewport units
            if (rNorm * 2f * Mathf.Max(1, viewportPixels) < minPixels) return true;  // a speck: reads as blank

            float cx = local.x / span + 0.5f;                         // sphere centre in viewport space
            float cy = local.y / span + 0.5f;
            // No overlap between the sphere's bounding square and the 0..1 view rect => nothing is drawn.
            return cx + rNorm < 0f || cx - rNorm > 1f || cy + rNorm < 0f || cy - rNorm > 1f;
        }

        // ----------------------------------------------------------------- render ----

        /// <summary>Render the current camera view into our owned RenderTexture (sized to <paramref name="size"/>²)
        /// and return it. Renders via SubmitRenderRequest (URP), falling back to camera.Render() with no SRP. Call
        /// from an editor-update tick — NOT inside an IMGUI repaint. Returns null until a renderable target is set.</summary>
        public RenderTexture RenderToTexture(int size)
        {
            if (_preview == null || _instance == null) return null;
            size = Mathf.Clamp(size, 16, 2048);
            EnsureRenderTexture(size);

            var cam = _preview.camera;
            FrameCamera();

            // Re-feed the PreviewLit light uniforms each frame (the camera orbits around fixed world-space lights).
            for (int i = 0; i < _litMaterials.Length; i++) SetPreviewLightUniforms(_litMaterials[i]);

            RenderPipelineUtility.SubmitPreviewRender(cam, _rt);
            return _rt;
        }

        /// <summary>Render one frame at the current camera and read it back into a NEW owned Texture2D
        /// (RGBA32, no mips) — a dormant thumbnail the widget can show with zero per-frame cost. Returns null
        /// until a renderable target is set. The CALLER owns the returned texture (DestroyImmediate it). Meant
        /// to be drawn with alphaBlend:false like the live RT (URP leaves the RT alpha ≈ 0).</summary>
        public Texture2D CaptureStill(int size)
        {
            var rt = RenderToTexture(size);
            if (rt == null) return null;
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false);
            }
            catch (Exception) { UnityEngine.Object.DestroyImmediate(tex); return null; }
            finally { RenderTexture.active = prev; }
            return tex;
        }

        private void EnsureRenderTexture(int size)
        {
            if (_rt != null && _rtSize == size) return;
            ReleaseRenderTexture();
            _rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "SasqAssetPreviewRT",
                antiAliasing = 1
            };
            _rt.Create();
            _rtSize = size;
        }

        private void ReleaseRenderTexture()
        {
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.DestroyImmediate(_rt);
                _rt = null;
            }
            _rtSize = 0;
        }

        private void EnsurePreview()
        {
            if (_preview != null) return;
            _preview = new PreviewRenderUtility();
            s_liveUtilities++;
            s_liveInstances.Add(this); // so CleanupAllLive can reach it before the domain reload
            var cam = _preview.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            // Key+fill+ambient preset, centralised so it scales to physical lux under HDRP (byte-identical elsewhere).
            RenderPipelineUtility.ConfigurePreviewLights(_preview, 1.4f, 0.7f, new Color(0.28f, 0.28f, 0.32f, 1f));
        }

        private void DestroyInstance()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
            if (_tempMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_tempMaterial);
                _tempMaterial = null;
            }
            _litMaterials = Array.Empty<Material>();
        }

        /// <summary>Tear down the instance, render texture and PreviewRenderUtility. Idempotent and detach-safe.</summary>
        public void Cleanup()
        {
            DestroyInstance();
            ReleaseRenderTexture();
            if (_preview != null)
            {
                try { _preview.Cleanup(); } catch (Exception) { }
                _preview = null;
                s_liveUtilities = Mathf.Max(0, s_liveUtilities - 1);
            }
            s_liveInstances.Remove(this);
            _target = null;
        }
    }

    /// <summary>
    /// Ensures only ONE interactive Asset Preview viewport renders per frame, no matter how many are on screen — the
    /// one the user is hovering / interacting with. Activating a preview idles the previously-active one, so the rest
    /// stay completely idle (a frozen last frame or a static thumbnail, zero per-frame GPU work). Static, so it
    /// resets on domain reload.
    /// </summary>
    internal static class AssetPreviewCoordinator
    {
        private static object s_activeToken;
        private static Action s_stopActive;

        // While the user is mid-orbit/pan on one preview, the cursor will inevitably cross OTHER previews; their
        // PointerEnter must NOT steal "active" (that would freeze the drag). A preview takes this lock on
        // mouse-down and releases it on mouse-up (or detach); hover-activation is gated on it being free.
        private static object s_dragToken;
        public static bool IsDragLocked => s_dragToken != null;
        public static void BeginDrag(object token) { if (token != null) s_dragToken = token; }
        public static void EndDrag(object token) { if (ReferenceEquals(s_dragToken, token)) s_dragToken = null; }

        /// <summary>Make <paramref name="token"/> the single active preview: idle whoever was active, then start this
        /// one. No-op if it is already the active preview.</summary>
        public static void Activate(object token, Action start, Action stop)
        {
            if (token == null) return;
            if (ReferenceEquals(s_activeToken, token)) return;
            var prevStop = s_stopActive;
            s_activeToken = token;
            s_stopActive = stop;
            prevStop?.Invoke();   // idle the previously-active preview (its own Deactivate will now no-op)
            start?.Invoke();
        }

        /// <summary>Idle <paramref name="token"/> if it is the active preview. Idempotent.</summary>
        public static void Deactivate(object token)
        {
            if (token == null || !ReferenceEquals(s_activeToken, token)) return;
            var stop = s_stopActive;
            s_activeToken = null;
            s_stopActive = null;
            stop?.Invoke();
        }
    }

    /// <summary>
    /// Remembers where the user left an interactive preview viewport — the camera pose, the 2D toggle, and any
    /// dormant still — so that it survives an inspector rebuild. Shared by the Asset Preview and the
    /// ParticleSystem / VFX Preview, which are separate widgets with the identical failure mode.
    ///
    /// WHY: selecting another object and coming back is a FULL teardown of the inspector's element tree, so every
    /// piece of a widget's state that lives in its build closure dies with it. The preview therefore re-framed to
    /// bounds and re-warmed its thumbnail on every visit: the orbit/zoom/pan the user had set was gone, and the
    /// static Unity thumbnail visibly swapped to a freshly-warmed still a moment later (the "pop" on first focus).
    ///
    /// Keyed per FIELD (widget kind + owning object + property path) and stamped with the assigned reference, so
    /// re-assigning the field starts fresh instead of restoring a pose framed for the previous asset. The cache
    /// OWNS the stills it holds: a widget hands its capture over and must never destroy one it was given. Bounded
    /// (least-recently-used evicted) and torn down before a domain reload / quit, like the renderer's own
    /// live-instance sweep.
    /// </summary>
    internal static class PreviewViewState
    {
        /// <summary>Key prefixes, so the same field rendered by two different preview widgets never restores a
        /// pose the other renderer framed (their camera conventions differ).</summary>
        internal const string AssetKind = "asset";
        internal const string ParticleKind = "particle";

        /// <summary>One field's remembered viewport. Handed to the widget by <see cref="For"/> and mutated in
        /// place — the cache holds the same instance, so there is nothing to write back.</summary>
        internal sealed class ViewState
        {
            internal int AssetId;              // the reference this view was framed for (0 = none)
            internal bool HasPose;
            internal Vector2 CamEuler;
            internal float CamDistance;
            internal Vector3 CamPivot;
            // VFX only: its live VfxZoom framing re-fits every render, so the user's scroll-zoom and pan layer on
            // top as a multiplier + offset instead of driving distance/pivot. Unused by the asset preview.
            internal float UserZoom = 1f;
            internal Vector3 PanOffset;
            internal bool Is2D;
            internal bool Is2DOverridden;      // the user pressed "2D": stop auto-picking it from the asset type
            internal Texture2D Still;          // owned HERE — widgets read it, never destroy it
            // Whether the pose above framed the asset clean out of the viewport. Cached alongside the still
            // because that is the ONLY way the widget can keep telling "you lost it" from "nothing assigned"
            // while dormant: the live geometry test needs a renderable, which a released renderer no longer
            // has, and a still captured from an off-screen pose is itself blank. The still and this flag were
            // captured from the same frame, so they always agree.
            internal bool OffFrame;
            // A field with no resolvable key (a torn-down SerializedObject) gets an uncached state so it still
            // works; nothing would ever evict its still, so the WIDGET frees that one on teardown instead.
            internal bool Transient;
        }

        // A still is a small RGBA32 read-back (128–512²), but a long session sweeping hundreds of objects would
        // still accumulate. Oldest-touched entry goes first; its still is destroyed with it.
        private const int MaxEntries = 48;
        private static readonly Dictionary<string, ViewState> s_entries = new Dictionary<string, ViewState>();
        private static readonly List<string> s_order = new List<string>(); // LRU, index 0 = least recently used

        static PreviewViewState()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        /// <summary>A stable per-field key: the widget kind, the owning object's session identity and the property
        /// path. Null when the property can't be identified, which makes the widget fall back to its old
        /// per-instance behavior.</summary>
        internal static string KeyFor(SerializedProperty prop, string kind)
        {
            if (prop == null) return null;
            UnityEngine.Object owner;
            try { owner = prop.serializedObject?.targetObject; }
            catch (Exception) { return null; }
            if (owner == null) return null;
            return kind + "|" + UnityObjectId.KeyOf(owner) + "|" + prop.propertyPath;
        }

        /// <summary>The remembered view for <paramref name="key"/>, created on demand. If it was framed for a
        /// different reference than <paramref name="asset"/>, the asset-specific parts (pose + still) are dropped
        /// first — the 2D choice is deliberately kept, matching the exported widget's sticky "2D" button.</summary>
        internal static ViewState For(string key, UnityEngine.Object asset)
        {
            if (string.IsNullOrEmpty(key))
                return new ViewState { AssetId = UnityObjectId.Of(asset), Transient = true };
            if (!s_entries.TryGetValue(key, out var st))
            {
                st = new ViewState { AssetId = UnityObjectId.Of(asset) };
                s_entries[key] = st;
                Touch(key);
                Trim();
                return st;
            }
            Touch(key);
            Retarget(st, asset);
            return st;
        }

        /// <summary>Drop the parts of a view that only make sense for the reference it was framed for. No-op when
        /// the reference is unchanged, so it is safe to call from a property-tracking callback.</summary>
        internal static void Retarget(ViewState st, UnityEngine.Object asset)
        {
            if (st == null) return;
            int id = UnityObjectId.Of(asset);
            if (st.AssetId == id) return;
            st.AssetId = id;
            st.HasPose = false;
            st.UserZoom = 1f;
            st.PanOffset = Vector3.zero;
            st.OffFrame = false; // a fresh reference is framed to bounds, and an unknown answer must never accuse
            DestroyStill(st);
        }

        /// <summary>Hand a freshly-captured still to the cache, which destroys whatever it held before. Passing the
        /// texture the view already holds is a no-op (never destroys the one being handed back).</summary>
        internal static void StoreStill(ViewState st, Texture2D still)
        {
            if (st == null)
            {
                if (still != null) UnityEngine.Object.DestroyImmediate(still);
                return;
            }
            if (ReferenceEquals(st.Still, still)) return;
            DestroyStill(st);
            st.Still = still;
        }

        /// <summary>Free a state the cache does NOT own (see <see cref="ViewState.Transient"/>). Call from the
        /// widget's teardown; a no-op for a cached state, whose still outlives the widget on purpose.</summary>
        internal static void ReleaseIfTransient(ViewState st)
        {
            if (st != null && st.Transient) DestroyStill(st);
        }

        /// <summary>Forget everything and destroy the stills. Runs before a domain reload / quit.</summary>
        internal static void Clear()
        {
            foreach (var st in s_entries.Values) DestroyStill(st);
            s_entries.Clear();
            s_order.Clear();
        }

        private static void DestroyStill(ViewState st)
        {
            if (st?.Still == null) return;
            UnityEngine.Object.DestroyImmediate(st.Still);
            st.Still = null;
        }

        private static void Touch(string key)
        {
            s_order.Remove(key);
            s_order.Add(key);
        }

        private static void Trim()
        {
            while (s_order.Count > MaxEntries)
            {
                string oldest = s_order[0];
                s_order.RemoveAt(0);
                if (s_entries.TryGetValue(oldest, out var st)) { DestroyStill(st); s_entries.Remove(oldest); }
            }
        }
    }

    /// <summary>
    /// Serializes one-shot "warm capture" jobs (render-a-still-then-release) so that when many interactive
    /// previews appear at once — e.g. opening an inspector full of mesh fields — they don't each spin up a
    /// <see cref="PreviewRenderUtility"/> in the same frame (a hitch + a blow past the concurrency cap).
    /// One capture runs per editor tick; the queue auto-unsubscribes when drained. Static → resets on reload.
    /// </summary>
    internal static class AssetPreviewWarmupQueue
    {
        private static readonly Queue<Action> s_queue = new Queue<Action>();
        private static bool s_subscribed;

        public static void Enqueue(Action capture)
        {
            if (capture == null) return;
            s_queue.Enqueue(capture);
            if (!s_subscribed) { EditorApplication.update += Pump; s_subscribed = true; }
        }

        private static void Pump()
        {
            if (s_queue.Count == 0) { EditorApplication.update -= Pump; s_subscribed = false; return; }
            var next = s_queue.Dequeue();
            try { next?.Invoke(); } catch (Exception) { }
        }
    }
}
