using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Produces a crisp, exactly-sized display texture for a Tutorial image. <see cref="TutorialMediaView"/> renders
    /// every image at a size measured in DEVICE pixels and then blits it 1:1, so the on-screen result never goes
    /// through UI Toolkit's element-level bilinear scaling — the #1 source of the "mild blur".
    ///
    /// When the requested size matches the source we hand back the source unchanged (true native 1:1). When it's
    /// smaller we downsample ONCE on the GPU — <see cref="Graphics.Blit"/> through a temporary RenderTexture,
    /// sampling the source's pre-filtered mip chain (filterMode Trilinear) for a clean shrink — into a cached,
    /// mip-free <see cref="Texture2D"/> and return that. Upscaling is never requested (callers clamp the target to
    /// the source size), so an image is shown at native size or smaller, never blown up.
    ///
    /// Results are cached by (source instance id, width, height) and owned here. A small soft cap bounds memory;
    /// everything is destroyed before an assembly reload / on quit so no native textures leak.
    /// </summary>
    internal static class TutorialImageScaler
    {
        private readonly struct Key
        {
            public readonly int Id, W, H;
            public Key(int id, int w, int h) { Id = id; W = w; H = h; }
            public override int GetHashCode() => (Id * 397) ^ (W * 31) ^ H;
            public override bool Equals(object o) => o is Key k && k.Id == Id && k.W == W && k.H == H;
        }

        // ~19 media files shown at a handful of sizes each; the cap is a generous backstop, not a working limit.
        private const int MaxEntries = 48;

        // Unsharp-mask strength applied to DOWNSCALED images only (a shrink softens text; this claws back the edge
        // contrast). Native-size images are shown untouched. ~0.5 is tasteful — higher rings, lower does little.
        private const float UnsharpAmount = 0.55f;
        private static readonly Dictionary<Key, Texture2D> Cache = new Dictionary<Key, Texture2D>();

        static TutorialImageScaler()
        {
            // HideAndDontSave textures survive a domain reload as leaked native objects unless we destroy them first.
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        /// <summary>
        /// Returns a texture to display for <paramref name="source"/> at exactly <paramref name="devW"/>×
        /// <paramref name="devH"/> device pixels. Returns the source itself when that equals its native size
        /// (zero resampling); otherwise a cached high-quality downsample. Never upscales (the size is clamped to
        /// the source). Falls back to the source if a GPU resample fails, so callers always get a valid texture.
        /// </summary>
        public static Texture GetDisplayTexture(Texture2D source, int devW, int devH)
        {
            if (source == null) return null;
            devW = Mathf.Clamp(devW, 1, source.width);
            devH = Mathf.Clamp(devH, 1, source.height);

            // Native size requested → show the source directly, no resampling at all.
            if (devW == source.width && devH == source.height) return source;

            var key = new Key(UnityObjectId.Of(source), devW, devH);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = Resample(source, devW, devH);
            if (tex == null) return source; // never hand back nothing — degrade to the (slightly soft) source

            if (Cache.Count >= MaxEntries) Clear(); // soft cap: drop the lot (re-resamples are cheap + rare)
            Cache[key] = tex;
            return tex;
        }

        // One-time GPU downsample: Blit the source (sampling its mip chain) into a temp RT, then read it back into a
        // mip-free, sRGB Texture2D sized exactly to the target so it can be blitted 1:1 with no further filtering.
        private static Texture2D Resample(Texture2D source, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevActive = RenderTexture.active;
            var prevFilter = source.filterMode;
            Texture2D outTex = null;
            try
            {
                source.filterMode = FilterMode.Trilinear; // sample the pre-filtered mip chain for a clean shrink
                Graphics.Blit(source, rt);

                RenderTexture.active = rt;
                outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, // irrelevant at 1:1, but a safe default if ever sampled off-grid
                    wrapMode = TextureWrapMode.Clamp,
                };
                outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                Sharpen(outTex, UnsharpAmount); // recover edge crispness lost to the shrink (unsharp mask)
                outTex.Apply(false);
            }
            catch
            {
                if (outTex != null) { Object.DestroyImmediate(outTex); outTex = null; }
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                source.filterMode = prevFilter;
            }
            return outTex;
        }

        // In-place unsharp mask: sharpened = original + amount·(original − 3×3-gaussian-blur). Operated on the
        // resampled texture's CPU pixels (it's small + one-time + cached). Edge pixels clamp to the border.
        private static void Sharpen(Texture2D tex, float amount)
        {
            int w = tex.width, h = tex.height;
            if (w < 3 || h < 3 || amount <= 0f) return;

            var src = tex.GetPixels32();
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
            {
                int ym = (y > 0 ? y - 1 : 0) * w;
                int yc = y * w;
                int yp = (y < h - 1 ? y + 1 : h - 1) * w;
                for (int x = 0; x < w; x++)
                {
                    int xm = x > 0 ? x - 1 : 0;
                    int xp = x < w - 1 ? x + 1 : w - 1;

                    Color32 c00 = src[ym + xm], c01 = src[ym + x], c02 = src[ym + xp];
                    Color32 c10 = src[yc + xm], c11 = src[yc + x], c12 = src[yc + xp];
                    Color32 c20 = src[yp + xm], c21 = src[yp + x], c22 = src[yp + xp];

                    // 3×3 gaussian (1 2 1 / 2 4 2 / 1 2 1) / 16
                    float blurR = ((c00.r + c02.r + c20.r + c22.r) + 2 * (c01.r + c10.r + c12.r + c21.r) + 4 * c11.r) / 16f;
                    float blurG = ((c00.g + c02.g + c20.g + c22.g) + 2 * (c01.g + c10.g + c12.g + c21.g) + 4 * c11.g) / 16f;
                    float blurB = ((c00.b + c02.b + c20.b + c22.b) + 2 * (c01.b + c10.b + c12.b + c21.b) + 4 * c11.b) / 16f;

                    dst[yc + x] = new Color32(
                        (byte)(Mathf.Clamp(c11.r + amount * (c11.r - blurR), 0f, 255f) + 0.5f),
                        (byte)(Mathf.Clamp(c11.g + amount * (c11.g - blurG), 0f, 255f) + 0.5f),
                        (byte)(Mathf.Clamp(c11.b + amount * (c11.b - blurB), 0f, 255f) + 0.5f),
                        c11.a);
                }
            }
            tex.SetPixels32(dst);
        }

        /// <summary>Destroys all cached display textures and empties the cache (reload / quit / soft-cap).</summary>
        public static void Clear()
        {
            foreach (var tex in Cache.Values)
                if (tex != null) Object.DestroyImmediate(tex);
            Cache.Clear();
        }
    }
}
