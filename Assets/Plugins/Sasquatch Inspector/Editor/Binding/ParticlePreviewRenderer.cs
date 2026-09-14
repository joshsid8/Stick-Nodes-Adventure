#if SASQ_PARTICLES // ParticleSystem lives in com.unity.modules.particlesystem, a REMOVABLE built-in module.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if SASQ_VFX
using UnityEngine.VFX;
#endif

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Owns a <see cref="PreviewRenderUtility"/> that renders a live, in-editor thumbnail of a ParticleSystem
    /// effect for the ParticleSystemPreview widget. Give it a target (a ParticleSystem or a GameObject/prefab),
    /// drive its simulation explicitly via <see cref="Seek"/> / <see cref="Step"/> (the preview scene never ticks
    /// on its own), then <see cref="RenderToTexture"/> to render the current state into an owned RenderTexture
    /// you can show in a UI Toolkit <c>Image</c>.
    ///
    /// IMPORTANT (URP): this renders via <see cref="RenderPipeline.SubmitRenderRequest"/> into our own
    /// RenderTexture, NOT via PreviewRenderUtility's BeginPreview/Render(true) inside an IMGUIContainer. Doing the
    /// SRP render from a scheduler tick (outside the inspector's IMGUI paint) is what makes it actually show in
    /// the live inspector — a nested SRP render request from inside the inspector repaint draws nothing.
    /// Disposal: <see cref="Cleanup"/> is idempotent and safe from a DetachFromPanelEvent.
    /// </summary>
    internal sealed class ParticlePreviewRenderer
    {
        private PreviewRenderUtility _preview;
        private GameObject _instance;                                  // instantiated copy living in the preview scene
        private ParticleSystem[] _allSystems = Array.Empty<ParticleSystem>();
        private ParticleSystem[] _rootSystems = Array.Empty<ParticleSystem>(); // only these are Simulate()d (withChildren)
#if SASQ_VFX
        private VisualEffect[] _effects = Array.Empty<VisualEffect>();   // fresh VFX Graph effect(s) built for the preview (optional package)
        private const float VfxPreviewDuration = 4f;   // VFX has no inherent duration; a synthetic loop length for the playback loop
#endif
        private UnityEngine.Object _target;                           // source whose hierarchy was instantiated
        private float _maxDuration = 1f;
        private Vector3 _boundsCenter = Vector3.zero;
        private float _boundsRadius = 1f;

        // Orbit/pan/zoom camera state (mirrors AssetPreviewRenderer). Euler defaults to ZERO so the resting view is
        // the original dead-on -Z framing; drag/scroll orbits/zooms from there. ComputeFramingBounds (re)seeds these.
        private Vector2 _camEuler = Vector2.zero;
        private float _camDistance = 4f;
        private Vector3 _camPivot = Vector3.zero;
        // VFX-only camera state: the VFX path keeps the designer "Preview Zoom" (VfxZoom) framing LIVE (its culling
        // bounds refine every render), so scroll-zoom and pan layer ON TOP as a transient multiplier + offset rather
        // than driving _camDistance/_camPivot (which the live re-framing would clobber). Orbit (_camEuler) is shared.
        private float _userZoom = 1f;
        private Vector3 _panOffset = Vector3.zero;

        /// <summary>When true, render with a straight-on orthographic (scene-view 2D) camera — orbit is ignored,
        /// only pan + zoom apply. Toggled by the widget's "2D" button.</summary>
        public bool Orthographic { get; set; }

        private bool HasVfx
        {
#if SASQ_VFX
            get => _effects != null && _effects.Length > 0;
#else
            get => false;
#endif
        }

        private RenderTexture _rt;                                     // owned render target shown in the widget's Image
        private int _rtSize;
        private Texture2D _metricScratch;                              // reused CPU readback buffer for CountDevelopedPixels (poster develop only)

        // Soft cap on concurrently-live PreviewRenderUtility instances across ALL particle widgets, so an
        // inspector full of these can't allocate unbounded render scenes/RTs. Over the cap, SetTarget leaves the
        // renderer empty (HasSystems == false) and the widget shows a static AssetPreview thumbnail instead; a
        // freed slot is re-acquired on the next refresh poll. Statics reset on domain reload.
        private static int s_liveUtilities;
        public const int MaxConcurrent = 8;

        // Live renderer instances that own a PreviewRenderUtility, so they can ALL be torn down before a domain
        // reload. A PreviewRenderUtility holds a native preview scene; if it's GC'd without Cleanup() (which is
        // what happens on assembly reload — the widget's DetachFromPanelEvent does NOT reliably fire first) the
        // scene leaks and its finalizer logs "A PreviewRenderUtility was not cleaned up properly...".
        private static readonly HashSet<ParticlePreviewRenderer> s_liveInstances = new HashSet<ParticlePreviewRenderer>();

        // Registered OUTSIDE any #if so the cleanup runs whether or not the VFX package is present (the leak is
        // not VFX-specific). This static ctor runs on first use each domain; the hook re-registers per reload.
        static ParticlePreviewRenderer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CleanupAllLive;
            EditorApplication.quitting += CleanupAllLive;
#if SASQ_VFX
            AssemblyReloadEvents.beforeAssemblyReload += ClearVfxPosterCache;
            EditorApplication.quitting += ClearVfxPosterCache;
#endif
        }

        /// <summary>Tests: how many PreviewRenderUtility instances the particle previews currently hold. A widget
        /// stranded without one is INVISIBLE from the element tree — it keeps its ▶ badge and quietly falls back to
        /// a static thumbnail — so this count is the only thing that tells a live preview from a dead one. Read it
        /// as a DELTA around a baseline: it is process-global and other previews may be open.</summary>
        internal static int LiveUtilityCountForTests => s_liveUtilities;

        /// <summary>Tears down every live PreviewRenderUtility before a domain reload / quit so no native preview
        /// scene leaks. Idempotent; Cleanup() removes the instance from the set, so iterate a copy.</summary>
        internal static void CleanupAllLive()
        {
            var copy = new List<ParticlePreviewRenderer>(s_liveInstances);
            foreach (var r in copy) { try { r.Cleanup(); } catch (Exception) { } }
            s_liveInstances.Clear();
        }

        /// <summary>Solid clear color behind the effect.</summary>
        public Color BackgroundColor = new Color(0.13f, 0.13f, 0.15f, 1f);

        /// <summary>VFX-only framing multiplier (bigger = the effect fills more of the thumbnail). A VFX Graph's
        /// VFXRenderer.bounds are CULLING bounds — usually far larger than the visible particles — so framing to them
        /// alone leaves the effect tiny. This zoom (designer "Preview Zoom") magnifies past the bounds; the default
        /// baseline already zooms in well past a bounds-fit. No effect on the ParticleSystem path (it frames from
        /// actual sampled particle positions, which are already tight).</summary>
        public float VfxZoom = 1f;

        /// <summary>True once a target with at least one previewable effect (ParticleSystem or, optionally, VFX Graph) has been set up.</summary>
        public bool HasSystems =>
            _instance != null &&
            ((_rootSystems != null && _rootSystems.Length > 0)
#if SASQ_VFX
             || (_effects != null && _effects.Length > 0)
#endif
            );

        /// <summary>Longest "play once" duration (system duration + max particle lifetime), clamped to a sane range.</summary>
        public float MaxDuration => _maxDuration;

        /// <summary>
        /// True when the current target is a render-driven VFX Graph effect (vs CPU-simulated ParticleSystems).
        /// The widget branches on this: VFX advances purely from <see cref="RenderToTexture"/> over real frame-deltas
        /// and uses <see cref="SetVfxPaused"/>/<see cref="RestartVfx"/> instead of <see cref="Seek"/>/<see cref="Step"/>.
        /// </summary>
        public bool IsVfx
        {
            get
            {
#if SASQ_VFX
                return _effects != null && _effects.Length > 0;
#else
                return false;
#endif
            }
        }

        /// <summary>VFX animate/freeze switch. Unpause to animate (each render advances real time); pause to freeze on
        /// the current frame. No-op for ParticleSystems.</summary>
        public void SetVfxPaused(bool paused)
        {
#if SASQ_VFX
            if (_effects == null) return;
            foreach (var v in _effects)
            {
                if (v == null) continue;
                try { v.pause = paused; } catch (Exception) { }
            }
#endif
        }

        /// <summary>Restart the VFX effect(s) from t=0 (Reinit). Used at play-start and to develop a fresh poster.
        /// The only supported VFX restart — <see cref="Seek"/> does not touch VFX. No-op for ParticleSystems.</summary>
        public void RestartVfx()
        {
#if SASQ_VFX
            if (_effects == null) return;
            foreach (var v in _effects)
            {
                if (v == null) continue;
                try { v.Reinit(); } catch (Exception) { }
            }
#endif
        }

        /// <summary>Set the VFX playback rate (1 = real time). Used to FAST-FORWARD the one-time poster develop so it
        /// finishes in a few frames instead of ~0.6s of real time. No-op for ParticleSystems.</summary>
        public void SetVfxPlayRate(float rate)
        {
#if SASQ_VFX
            if (_effects == null) return;
            foreach (var v in _effects)
            {
                if (v == null) continue;
                try { v.playRate = Mathf.Max(0.01f, rate); } catch (Exception) { }
            }
#endif
        }

#if SASQ_VFX
        // Developed-poster cache, keyed by VisualEffectAsset instance id, so re-selecting an object (or any object that
        // uses the same .vfx) shows its thumbnail INSTANTLY instead of re-developing. Static → survives the widget /
        // inspector rebuilds that happen on every selection change. Per editor session (statics reset on domain reload).
        private static readonly Dictionary<int, RenderTexture> s_vfxPosterCache = new Dictionary<int, RenderTexture>();

        // Generous soft cap: one RT per unique .vfx viewed in a session. Real use touches a handful; when this is
        // exceeded, the oldest cached poster is evicted so a long browsing session can't grow the cache unbounded.
        private const int MaxVfxPosters = 32;

        /// <summary>Releases + destroys every cached VFX poster RenderTexture and empties the cache (reload/quit).
        /// (The reload/quit hook is registered in the type's static ctor, alongside CleanupAllLive.)</summary>
        internal static void ClearVfxPosterCache()
        {
            foreach (var rt in s_vfxPosterCache.Values)
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            s_vfxPosterCache.Clear();
        }

        // Releases + drops the oldest cached poster (enumeration order ≈ insertion order — good enough for a soft
        // memory bound). Removes outside the enumeration so we never mutate the dictionary mid-iterate.
        private static void EvictOldestVfxPoster()
        {
            int oldestKey = 0;
            bool found = false;
            foreach (var k in s_vfxPosterCache.Keys) { oldestKey = k; found = true; break; }
            if (!found) return;
            if (s_vfxPosterCache.TryGetValue(oldestKey, out var rt) && rt != null)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            s_vfxPosterCache.Remove(oldestKey);
        }

        private int CurrentVfxAssetId()
        {
            if (_effects == null || _effects.Length == 0 || _effects[0] == null) return 0;
            var asset = _effects[0].visualEffectAsset;
            return UnityObjectId.Of(asset);
        }

        // The poster BAKES the camera clear color + framing zoom into its pixels, so those settings are
        // part of the cache identity. Keyed on the asset alone, a Background Color / Preview Zoom change
        // would hit the cache and hand back the stale poster (old color baked in), and the static
        // thumbnail would only pick up the new color once PLAY bypassed the cache. Two nodes showing the
        // same .vfx with different styling each cache their own poster.
        private int CurrentVfxPosterKey()
        {
            int id = CurrentVfxAssetId();
            if (id == 0) return 0;
            unchecked
            {
                Color32 bg = BackgroundColor;
                int key = id;
                key = key * 397 ^ (bg.r | (bg.g << 8) | (bg.b << 16) | (bg.a << 24));
                key = key * 397 ^ Mathf.RoundToInt(VfxZoom * 100f);
                return key == 0 ? 1 : key; // 0 is the "no asset" sentinel
            }
        }
#endif

        /// <summary>A cached, already-developed thumbnail for the current VFX asset, or null if none yet. When non-null
        /// the widget can show it INSTANTLY (no develop). No-op (null) for ParticleSystems.</summary>
        public Texture TryGetCachedVfxPoster()
        {
#if SASQ_VFX
            int id = CurrentVfxPosterKey();
            if (id == 0) return null;
            return s_vfxPosterCache.TryGetValue(id, out var tex) && tex != null ? tex : null;
#else
            return null;
#endif
        }

        /// <summary>Unity's generic mini icon for the current VFX asset — an instant placeholder shown WHILE the
        /// rendered poster develops (Unity provides no rendered static thumbnail for .vfx, so the panel would
        /// otherwise be empty/black for the brief one-time develop). Null for non-VFX targets.</summary>
        public Texture GetVfxAssetThumbnail()
        {
#if SASQ_VFX
            if (_effects == null || _effects.Length == 0 || _effects[0] == null) return null;
            var asset = _effects[0].visualEffectAsset;
            return asset != null ? AssetPreview.GetMiniThumbnail(asset) : null;
#else
            return null;
#endif
        }

        /// <summary>Snapshot the current <see cref="_rt"/> frame into a persistent RenderTexture and cache it for the
        /// current VFX asset, so future selections are instant. This is a pure GPU copy (<c>Graphics.CopyTexture</c>),
        /// NOT a <c>ReadPixels</c> → Texture2D readback: the cached poster is therefore colorspace-identical to the
        /// live <see cref="_rt"/> that playback already displays correctly (a CPU readback can gamma-crush dim VFX
        /// frames toward black — a thumbnail blacker than the panel around it). Call when the best develop
        /// frame has been found.</summary>
        public void CacheCurrentVfxPoster()
        {
#if SASQ_VFX
            int id = CurrentVfxPosterKey();
            if (id == 0 || _rt == null) return;
            // Reuse the cached RT across the many "best frame improved" snapshots a single develop produces; only
            // (re)allocate when absent or the format/size changed (the develop's _rt size is stable, so this is one
            // allocation per asset). CopyTexture is a pure GPU blit — no CPU readback, no colorspace conversion.
            s_vfxPosterCache.TryGetValue(id, out var poster);
            if (poster == null || poster.width != _rt.width || poster.height != _rt.height || poster.graphicsFormat != _rt.graphicsFormat)
            {
                if (poster != null) { poster.Release(); UnityEngine.Object.DestroyImmediate(poster); }
                // Bound the cache before introducing a brand-new asset's poster (re-allocating an already-cached
                // id doesn't grow the count, so it's exempt).
                if (!s_vfxPosterCache.ContainsKey(id) && s_vfxPosterCache.Count >= MaxVfxPosters)
                    EvictOldestVfxPoster();
                poster = new RenderTexture(_rt.descriptor) { hideFlags = HideFlags.HideAndDontSave, name = "SasqVfxPosterCache" };
                poster.Create();
                s_vfxPosterCache[id] = poster;
            }
            Graphics.CopyTexture(_rt, poster);
#endif
        }

        /// <summary>A colorspace-independent "develop score": the number of pixels whose color differs noticeably from
        /// the frame's MEAN color — i.e. how much non-uniform (particle) content the current <see cref="_rt"/> shows.
        /// A freshly-restarted VFX is near-empty (score ≈ 0) and this climbs as it spawns, so the widget uses it to
        /// pick the most-developed frame for the static poster instead of freezing on an arbitrary (often empty)
        /// frame. Measuring variation-from-mean (rather than distance-from-background) makes it robust to global
        /// brightness/colorspace. Reads <see cref="_rt"/> back to the CPU — call sparingly (poster develop only).</summary>
        public int CountDevelopedPixels()
        {
            if (_rt == null) return 0;
            int w = _rt.width, h = _rt.height;
            if (_metricScratch == null || _metricScratch.width != w || _metricScratch.height != h)
            {
                if (_metricScratch != null) UnityEngine.Object.DestroyImmediate(_metricScratch);
                _metricScratch = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "SasqVfxMetricScratch" };
            }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _metricScratch.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            _metricScratch.Apply(false);
            RenderTexture.active = prev;

            var px = _metricScratch.GetPixels32();
            if (px.Length == 0) return 0;
            long sr = 0, sg = 0, sb = 0;
            for (int i = 0; i < px.Length; i++) { sr += px[i].r; sg += px[i].g; sb += px[i].b; }
            int mr = (int)(sr / px.Length), mg = (int)(sg / px.Length), mb = (int)(sb / px.Length);
            int n = 0;
            for (int i = 0; i < px.Length; i++)
                if (Mathf.Abs(px[i].r - mr) + Mathf.Abs(px[i].g - mg) + Mathf.Abs(px[i].b - mb) > 24) n++;
            return n;
        }

        // --------------------------------------------------------------- target resolution ----

        /// <summary>Resolve the GameObject to instantiate from a ParticleSystem (its GO), a GameObject, or any
        /// Component (its GO — also covers a VFX Graph VisualEffect). Outputs whether that hierarchy actually
        /// contains a previewable effect (a ParticleSystem or, when VFX Graph is present, a VisualEffect).</summary>
        private static GameObject ResolveSource(UnityEngine.Object target, out bool hasPreviewable)
        {
            hasPreviewable = false;
            GameObject src = null;
            if (target is ParticleSystem ps) src = ps.gameObject;
            else if (target is GameObject go) src = go;
            else if (target is Component c) src = c.gameObject; // also covers a VisualEffect component
            if (src == null) return null;
            hasPreviewable = src.GetComponentInChildren<ParticleSystem>(true) != null
#if SASQ_VFX
                          || src.GetComponentInChildren<VisualEffect>(true) != null
#endif
                          ;
            return src;
        }

        /// <summary>Whether a given object reference can be previewed (a GameObject/ParticleSystem — or a VisualEffect when VFX Graph is present).</summary>
        public static bool CanPreview(UnityEngine.Object target) => ResolveSource(target, out var has) != null && has;

        /// <summary>
        /// True when this renderer holds no preview scene for <paramref name="target"/> even though that target IS
        /// previewable — i.e. <see cref="SetTarget"/> has to run again for the same reference. Two ways to land here
        /// without the target ever changing: the concurrency soft cap turned an acquire down, or <see cref="Cleanup"/>
        /// freed the utility under a widget that then came back (a re-parent whose detach outlasted a tick).
        /// <para>Deliberately NOT <c>!HasSystems</c>: an assigned object that simply contains no particles is a
        /// SETTLED answer, and re-resolving it on every idle poll would re-walk its hierarchy forever.</para>
        /// </summary>
        public bool NeedsAcquire(UnityEngine.Object target) => target != null && _preview == null && CanPreview(target);

        /// <summary>
        /// Point the renderer at a new source. Tears down any prior instance, instantiates a hidden copy of the
        /// source into the preview scene, caches its (root) ParticleSystems, computes duration + framing. A null
        /// target or one without particles leaves the renderer empty (HasSystems == false).
        /// </summary>
        public void SetTarget(UnityEngine.Object target)
        {
            if (ReferenceEquals(target, _target) && _instance != null) return;
            _target = target;
            DestroyInstance();

            var src = ResolveSource(target, out var hasPreviewable);
            if (src == null || !hasPreviewable) return;

            // Concurrency soft cap: once many particle widgets are already live, don't allocate another render
            // utility — leave this one empty so the widget falls back to a static thumbnail. A deliberate bound,
            // not a silent truncation. Renderers that already own a utility (_preview != null) keep working.
            if (_preview == null && s_liveUtilities >= MaxConcurrent) return;

            EnsurePreview();

#if SASQ_VFX
            // VFX path: a VisualEffect INSTANTIATED from the user's hierarchy renders nothing in a preview scene
            // (verified empirically: 0 px, stays culled). Unity's own built-in .vfx preview instead creates a FRESH
            // VisualEffect and assigns the asset — that renders. So for any VFX-bearing source, build fresh effects
            // from the source assets and skip the instantiate path entirely.
            var srcEffects = src.GetComponentsInChildren<VisualEffect>(true);
            bool hasVfx = false;
            foreach (var se in srcEffects) { if (se != null) { hasVfx = true; break; } } // any VisualEffect (matches CanPreview)
            if (hasVfx)
            {
                BuildAndAddVfx(srcEffects);
                return;
            }
#endif

            _instance = UnityEngine.Object.Instantiate(src);
            _instance.hideFlags = HideFlags.HideAndDontSave;
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.identity;

            _allSystems = _instance.GetComponentsInChildren<ParticleSystem>(true);
            _rootSystems = CollectRootSystems(_allSystems);

            // Stop everything (instantiate may leave systems in a playing state) and make the seed stable so the
            // poster frame matches what playback shows at the same time. Guarded — both can warn on odd states.
            foreach (var ps in _rootSystems)
            {
                if (ps == null) continue;
                try { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); } catch (Exception) { }
            }
            foreach (var ps in _allSystems)
            {
                if (ps == null) continue;
                try { ps.useAutoRandomSeed = false; } catch (Exception) { }
            }

            _maxDuration = ComputeMaxDuration(_allSystems);
            _preview.AddSingleGO(_instance);
            ComputeFramingBounds();
        }

#if SASQ_VFX
        // Mirror Unity's own built-in .vfx preview: a FRESH GameObject with a FRESH VisualEffect per source asset,
        // move it into the preview scene, then assign the asset (after the GO is in the preview scene, as Unity does).
        // Rendering (RenderToTexture) advances the effect — no Simulate (which produced nothing for instantiated effects).
        private void BuildAndAddVfx(VisualEffect[] srcEffects)
        {
            // The VisualEffect must live on the SAME GameObject we move into the preview scene — a child VFX isn't
            // registered and renders nothing. So preview a single fresh effect on _instance directly (the common
            // case is one VisualEffect field; a multi-effect source previews its primary effect).
            VisualEffectAsset asset = null;
            foreach (var se in srcEffects) { if (se != null && se.visualEffectAsset != null) { asset = se.visualEffectAsset; break; } }

            // DontSave (NOT HideAndDontSave) — matches Unity's own built-in .vfx preview; a HideInHierarchy GO's VFX did not render.
            _instance = new GameObject("SasqVfxPreview") { hideFlags = HideFlags.DontSave };
            _allSystems = Array.Empty<ParticleSystem>();
            _rootSystems = Array.Empty<ParticleSystem>();

            var vfx = _instance.AddComponent<VisualEffect>();
            vfx.pause = true; // start frozen; the widget drives play/freeze via SetVfxPaused and advances it through RenderToTexture
            _effects = new[] { vfx };
            _maxDuration = VfxPreviewDuration;

            AddInstanceToPreview(); // AddSingleGO(_instance) — move the VFX's own GO into the preview camera's scene

            // Assign the asset AFTER the GO is in the preview scene (Unity's order), with a stable seed.
            try { vfx.resetSeedOnPlay = false; vfx.startSeed = 1234u; vfx.visualEffectAsset = asset; }
            catch (Exception) { }

            // Conservative starting frame; the real frame is picked up by RenderToTexture, which refines the camera
            // from the VFXRenderer's world bounds after each render (exactly how Unity's own built-in .vfx preview frames it).
            // NO synchronous warm-up loop: a VFX advances ONLY when PreviewRenderUtility.Render runs INSIDE an IMGUI
            // repaint (verified empirically), so the widget develops the poster by pumping real
            // repaints. Rendering here (outside IMGUI) would neither advance nor draw the effect.
            _boundsCenter = Vector3.zero;
            _boundsRadius = 1.5f;
        }
#endif

        /// <summary>Roots = systems not nested under another ParticleSystem; only these are Simulate()d with
        /// withChildren:true so nested systems and sub-emitters aren't double-driven.</summary>
        private static ParticleSystem[] CollectRootSystems(ParticleSystem[] systems)
        {
            var roots = new List<ParticleSystem>();
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                bool nested = false;
                var t = ps.transform.parent;
                while (t != null)
                {
                    if (t.GetComponent<ParticleSystem>() != null) { nested = true; break; }
                    t = t.parent;
                }
                if (!nested) roots.Add(ps);
            }
            return roots.ToArray();
        }

        private static float ComputeMaxDuration(ParticleSystem[] systems)
        {
            float max = 0.1f;
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                var main = ps.main;
                var sl = main.startLifetime;
                // Covers Constant + RandomBetweenTwoConstants; curve modes fall back to the max constant.
                float life = Mathf.Max(sl.constant, sl.constantMax);
                float total = main.duration + life;
                if (total > max) max = total;
            }
            return Mathf.Clamp(max, 0.25f, 12f);
        }

        // --------------------------------------------------------------- simulation ----

        /// <summary>Deterministically simulate from t=0 to <paramref name="absoluteTime"/> (restart each call).
        /// Used for the poster frame and to reset to the start of playback.</summary>
        public void Seek(float absoluteTime)
        {
            float t = Mathf.Max(0f, absoluteTime);
            foreach (var ps in _rootSystems)
            {
                if (ps == null) continue;
                try { ps.Simulate(t, true, true, false); } catch (Exception) { }
            }
            // VFX is intentionally NOT handled here: a VisualEffect can't be deterministically seeked (Simulate did
            // nothing for these preview effects), and Reinit-on-seek wipes the effect mid-playback. VFX restart is
            // explicit via RestartVfx(); _rootSystems is empty for a VFX target, so this method is a no-op for it.
        }

        /// <summary>Advance the current simulation by <paramref name="delta"/> seconds (no restart). Cheap, smooth,
        /// framerate-independent stepping for playback.</summary>
        public void Step(float delta)
        {
            if (delta <= 0f) return;
            foreach (var ps in _rootSystems)
            {
                if (ps == null) continue;
                try { ps.Simulate(delta, true, false, false); } catch (Exception) { }
            }
            // VFX needs no explicit Step — RenderToTexture (BeginPreview/Render) advances it each frame.
        }

        // --------------------------------------------------------------- render ----

        /// <summary>
        /// Render the current simulation state into our owned <see cref="RenderTexture"/> (created/resized to
        /// <paramref name="size"/>²) and return it. Renders through the active SRP via SubmitRenderRequest (URP),
        /// falling back to the built-in <c>camera.Render()</c> when no SRP is active. Call from a scheduler tick /
        /// editor update — NOT from inside an IMGUI repaint. Returns null until a target with particles is set.
        /// </summary>
        public RenderTexture RenderToTexture(int size)
        {
            if (_preview == null || _instance == null) return null;
            size = Mathf.Clamp(size, 16, 2048);
            EnsureRenderTexture(size);

            var cam = _preview.camera;
            cam.aspect = 1f;

#if SASQ_VFX
            // VFX renders ONLY via Unity's preview path (BeginPreview/Render/EndPreview), which also ADVANCES the
            // effect each call — SubmitRenderRequest neither advances nor draws a VFX in a preview scene. Driven
            // from the editor update tick (not inside IMGUI paint), so the SRP render is valid. Frame the camera
            // AFTER BeginPreview (it reconfigures the camera), then blit into _rt for the widget's IMGUIContainer.
            if (_effects != null && _effects.Length > 0)
            {
                var rect = new Rect(0, 0, size, size);
                _preview.BeginPreview(rect, GUIStyle.none);
                // Frame AFTER BeginPreview (it reconfigures the camera), mirroring Unity's own built-in .vfx preview.
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 10000f;
                cam.clearFlags = CameraClearFlags.SolidColor; // BeginPreview can reset this; keep the configured bg color
                cam.backgroundColor = BackgroundColor;
                // A VFX Graph's VFXRenderer.bounds are CULLING bounds, typically MUCH bigger than the visible
                // particles, so a bounds-fit camera leaves the effect tiny (≈10% fill). Baseline-zoom
                // well INTO the bounds (≈3.5× past a bounds-fit) so a typical effect fills the thumbnail, then divide
                // by the designer "Preview Zoom" (VfxZoom) for per-effect control. _boundsCenter/_boundsRadius are
                // refined from the live VFXRenderer.bounds after each render (below). Orbit (_camEuler), pan
                // (_panOffset) and scroll-zoom (_userZoom) layer ON TOP so VFX gets the same interactivity as
                // ParticleSystems while the live VfxZoom framing keeps working.
                float r = Mathf.Max(0.05f, _boundsRadius);
                float fit = r / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad); // distance at which the bounds sphere just fits
                float vdist = Mathf.Max(0.25f, fit * 0.35f / Mathf.Max(0.1f, VfxZoom)) * Mathf.Max(0.1f, _userZoom);
                Vector3 vpivot = _boundsCenter + _panOffset;
                if (Orthographic)
                {
                    cam.orthographic = true;
                    cam.orthographicSize = Mathf.Max(0.05f, vdist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                    cam.transform.rotation = Quaternion.identity;
                    cam.transform.position = vpivot + new Vector3(0f, 0f, -Mathf.Max(vdist, r * 2f + 5f));
                }
                else
                {
                    cam.orthographic = false;
                    var vrot = Quaternion.Euler(_camEuler.x, _camEuler.y, 0f);
                    cam.transform.rotation = vrot;
                    cam.transform.position = vpivot + vrot * new Vector3(0f, 0f, -vdist);
                }
                _preview.Render(true);
                var src = _preview.EndPreview() as RenderTexture;
                if (src == null) return _rt;
                // Graphics.Blit of the EndPreview texture produced nothing here; copy the pixels directly into a
                // format-matched _rt (the widget's IMGUIContainer expects a stable owned RenderTexture).
                if (_rt == null || _rt.width != src.width || _rt.height != src.height || _rt.graphicsFormat != src.graphicsFormat)
                {
                    ReleaseRenderTexture();
                    _rt = new RenderTexture(src.descriptor) { hideFlags = HideFlags.HideAndDontSave, name = "SasqParticlePreviewRT" };
                    _rt.Create();
                    _rtSize = src.width;
                }
                Graphics.CopyTexture(src, _rt);
                // Refine framing from the now-valid VFXRenderer bounds so the next frame fits the effect snugly
                // (a VFXRenderer's bounds are only meaningful once it has rendered/simulated at least once).
                var vr = (_effects.Length > 0 && _effects[0] != null) ? _effects[0].GetComponent<Renderer>() : null;
                if (vr != null && vr.bounds.size.sqrMagnitude > 1e-4f)
                {
                    _boundsCenter = vr.bounds.center;
                    _boundsRadius = Mathf.Max(0.1f, vr.bounds.extents.magnitude);
                }
                return _rt;
            }
#endif

            FrameCamera();

            // Render through the active SRP (URP/HDRP) via SubmitRenderRequest; fall back to the built-in
            // camera.Render when no SRP is active or the request is unsupported.
            RenderPipelineUtility.SubmitPreviewRender(cam, _rt);
            return _rt;
        }

        private void EnsureRenderTexture(int size)
        {
            if (_rt != null && _rtSize == size) return;
            ReleaseRenderTexture();
            _rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "SasqParticlePreviewRT",
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

        // --------------------------------------------------------------- internals ----

        private void EnsurePreview()
        {
            if (_preview != null) return;

            _preview = new PreviewRenderUtility();
            s_liveUtilities++;
            s_liveInstances.Add(this);
            var cam = _preview.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            // Key+fill+ambient preset, centralised so it scales to physical lux under HDRP (byte-identical elsewhere).
            RenderPipelineUtility.ConfigurePreviewLights(_preview, 1.4f, 0.7f, new Color(0.28f, 0.28f, 0.32f, 1f));
        }

        // Frame from the effect's ACTUAL particle extent, sampled across its lifetime so playback never flies
        // out of frame. Particle positions are reliable after Simulate; ParticleSystemRenderer.bounds is not.
        private void ComputeFramingBounds()
        {
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            ParticleSystem.Particle[] buffer = null;

            float[] samples = { 0.25f, 0.5f, 0.75f, 1.0f };
            foreach (var f in samples)
            {
                Seek(_maxDuration * f);
                // A VFX-only target has no CPU-readable particles to sample; one seed-seek is enough to develop
                // the effect, and its world bounds are then picked up by the renderer-bounds fallback below.
                if (_allSystems.Length == 0) break;
                foreach (var ps in _allSystems)
                {
                    if (ps == null) continue;
                    int count = ps.particleCount;
                    if (count <= 0) continue;
                    if (buffer == null || buffer.Length < count) buffer = new ParticleSystem.Particle[count];
                    int n = ps.GetParticles(buffer);
                    bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                    var tr = ps.transform;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = world ? buffer[i].position : tr.TransformPoint(buffer[i].position);
                        float s = Mathf.Max(0f, buffer[i].GetCurrentSize(ps)) * 0.5f;
                        var pb = new Bounds(p, new Vector3(s, s, s) * 2f);
                        if (!any) { b = pb; any = true; } else b.Encapsulate(pb);
                    }
                }
            }

            // Fall back to renderer bounds, then a sane default, when no particles were sampled (e.g. a mesh-only
            // child or an effect that emits later than our samples).
            if (!any && _instance != null)
            {
                var renderers = _instance.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            }

            if (any && b.size.sqrMagnitude > 1e-4f)
            {
                _boundsCenter = b.center;
                _boundsRadius = Mathf.Max(0.25f, b.extents.magnitude);
            }
            else
            {
                _boundsCenter = Vector3.zero;
                _boundsRadius = 1.5f;
            }

            // Seed the orbit camera to frame these bounds dead-on (euler 0 = the original straight -Z view), so the
            // resting preview looks identical to before; drag/scroll then orbits/zooms from here.
            ResetView();
        }

        /// <summary>Recenter the orbit camera to frame the CURRENT bounds dead-on (the "reset view" affordance).
        /// Camera-only — does NOT re-sample bounds (that would re-seek the simulation), so it's safe mid-playback.</summary>
        public void ResetView()
        {
            _camPivot = _boundsCenter;
            _camEuler = Vector2.zero;
            _userZoom = 1f;                 // VFX: back to the VfxZoom default framing
            _panOffset = Vector3.zero;      // VFX: clear pan
            float fovRad = (_preview != null ? _preview.camera.fieldOfView : 30f) * Mathf.Deg2Rad;
            _camDistance = _boundsRadius / Mathf.Max(0.01f, Mathf.Sin(fovRad * 0.5f)) * 1.25f;
        }

        /// <summary>The full user-driven camera pose. FIVE values, not three: the ParticleSystem path drives
        /// euler/distance/pivot directly, while the VFX path layers <c>_userZoom</c> and <c>_panOffset</c> on top of
        /// its live VfxZoom re-framing (see the field comments) — restore only three and a VFX preview snaps back to
        /// the default zoom. Save before a teardown, restore AFTER <see cref="SetTarget"/>, which reframes.</summary>
        public (Vector2 euler, float distance, Vector3 pivot, float userZoom, Vector3 panOffset) CameraState
        {
            get => (_camEuler, _camDistance, _camPivot, _userZoom, _panOffset);
            set
            {
                _camEuler = value.euler;
                _camDistance = value.distance;
                _camPivot = value.pivot;
                _userZoom = value.userZoom;
                _panOffset = value.panOffset;
            }
        }

        /// <summary>Orbit the camera (3D mode only; ignored visually in 2D).</summary>
        public void Orbit(Vector2 delta)
        {
            _camEuler.y += delta.x * 0.4f;
            _camEuler.x = Mathf.Clamp(_camEuler.x + delta.y * 0.4f, -85f, 85f);
        }

        /// <summary>Pan the camera pivot in screen space (works in both 2D and 3D).</summary>
        public void Pan(Vector2 delta)
        {
            if (_preview == null) return;
            var camT = _preview.camera.transform;
            if (HasVfx)
            {
                float pv = Mathf.Max(0.05f, _boundsRadius) * 0.004f; // proportional to the effect size
                _panOffset += -camT.right * (delta.x * pv) + camT.up * (delta.y * pv);
                return;
            }
            float ps = _camDistance * 0.0022f;
            _camPivot += -camT.right * (delta.x * ps) + camT.up * (delta.y * ps);
        }

        /// <summary>Zoom (distance in 3D; orthographicSize tracks distance in 2D). For VFX this scales a transient
        /// multiplier on top of the live VfxZoom framing, so the designer "Preview Zoom" slider keeps working.</summary>
        public void Zoom(float scrollDeltaY)
        {
            if (HasVfx)
            {
                _userZoom = Mathf.Clamp(_userZoom * (1f + scrollDeltaY * 0.05f), 0.1f, 10f);
                return;
            }
            float min = Mathf.Max(0.05f, _boundsRadius * 0.15f);
            float max = Mathf.Max(min + 0.1f, _boundsRadius * 12f);
            _camDistance = Mathf.Clamp(_camDistance * (1f + scrollDeltaY * 0.05f), min, max);
        }

        private void FrameCamera()
        {
            var cam = _preview.camera;
            cam.backgroundColor = BackgroundColor;
            cam.aspect = 1f;

            if (Orthographic)
            {
                // 2D: straight-on orthographic; size tracks distance so 2D↔3D keeps a consistent on-screen scale.
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(0.05f, _camDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                cam.transform.rotation = Quaternion.identity;
                float backOff = _boundsRadius * 4f + 5f;
                cam.transform.position = _camPivot + new Vector3(0f, 0f, -backOff);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = backOff + _boundsRadius * 8f + 10f;
                return;
            }

            // 3D: orbit around the pivot. Euler 0 reproduces the original dead-on -Z framing; flat 2D effects read
            // correctly at rest and billboards always face the camera.
            cam.orthographic = false;
            var rot = Quaternion.Euler(_camEuler.x, _camEuler.y, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = _camPivot + rot * new Vector3(0f, 0f, -_camDistance);
            cam.nearClipPlane = Mathf.Max(0.01f, _camDistance - _boundsRadius * 4f);
            cam.farClipPlane = _camDistance + _boundsRadius * 8f;
        }

        /// <summary>True when the effect has been panned / zoomed / orbited clear of the viewport: its bounding
        /// sphere projects outside the view rect, sits behind the lens, or covers fewer than
        /// <paramref name="minPixels"/> across. Lets the widget tell "you lost the effect" apart from "nothing is
        /// assigned" — two states that otherwise render the same blank panel. Pure camera math over the bounds the
        /// framing already tracks: no read-back, no extra render (unlike <see cref="CountDevelopedPixels"/>).
        ///
        /// <para>The VFX branch reads the camera AS LAST FRAMED rather than re-deriving it: that path frames
        /// inline in <see cref="RenderToTexture"/> from <c>VfxZoom</c> + the live culling bounds, which this
        /// cannot reproduce without duplicating it. So it answers false until a first render has happened. Note a
        /// VFX Graph's bounds are CULLING bounds — far larger than the visible particles — so the test is
        /// deliberately conservative there: it under-reports rather than accusing a perfectly framed effect.</para>
        ///
        /// <para>Returns false whenever there is nothing to judge, so an unknown answer never accuses the user.</para></summary>
        public bool IsTargetOffFrame(int viewportPixels, float minPixels = 3f)
        {
            if (_preview == null || _instance == null) return false;
            var cam = _preview.camera;
            if (HasVfx)
            {
                if (_rt == null) return false; // never rendered → the camera has not been framed yet
            }
            else
            {
                FrameCamera(); // deterministic from the current pose; also what the next render will use
            }

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
                if (local.z + _boundsRadius <= cam.nearClipPlane) return true; // entirely behind the lens
                halfHeight = Mathf.Max(1e-4f, Mathf.Max(local.z, 1e-4f)
                    * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }

            // Both framing paths pin aspect to 1, so half-width == half-height and one radius covers both axes.
            float span = 2f * halfHeight;
            float rNorm = _boundsRadius / span;                       // sphere radius in 0..1 viewport units
            if (rNorm * 2f * Mathf.Max(1, viewportPixels) < minPixels) return true;  // a speck: reads as blank

            float cx = local.x / span + 0.5f;                         // sphere centre in viewport space
            float cy = local.y / span + 0.5f;
            return cx + rNorm < 0f || cx - rNorm > 1f || cy + rNorm < 0f || cy - rNorm > 1f;
        }

        private void DestroyInstance()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
            _allSystems = Array.Empty<ParticleSystem>();
            _rootSystems = Array.Empty<ParticleSystem>();
#if SASQ_VFX
            _effects = Array.Empty<VisualEffect>();
#endif
        }

#if SASQ_VFX
        // Add the fresh VFX GameObject to the preview scene via PreviewRenderUtility.AddSingleGO, which moves it into
        // the preview camera's scene (PreviewRenderUtility sets camera.scene to it). A VisualEffect registers with the
        // VFXManager in its OnEnable and is then simulated/drawn for any camera that renders its scene (the SRP's VFX
        // pass), so sharing the preview camera's scene is enough; we handle cleanup ourselves in DestroyInstance/Cleanup.
        // AddSingleGO is public API across the whole supported editor range.
        private void AddInstanceToPreview()
        {
            _preview.AddSingleGO(_instance);
        }
#endif

        /// <summary>Tear down the instance, render texture and PreviewRenderUtility. Idempotent and detach-safe.</summary>
        public void Cleanup()
        {
            DestroyInstance();
            ReleaseRenderTexture();
            if (_metricScratch != null) { UnityEngine.Object.DestroyImmediate(_metricScratch); _metricScratch = null; }
            if (_preview != null)
            {
                try { _preview.Cleanup(); } catch (Exception) { }
                _preview = null;
                s_liveUtilities = Mathf.Max(0, s_liveUtilities - 1);
                s_liveInstances.Remove(this);
            }
            _target = null;
        }
    }
}
#endif // SASQ_PARTICLES
