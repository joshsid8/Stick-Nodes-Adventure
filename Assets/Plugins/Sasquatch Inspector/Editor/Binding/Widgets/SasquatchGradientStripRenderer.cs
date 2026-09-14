using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds a <see cref="Texture2D"/> rendering of a <see cref="Gradient"/> for the Gradient editor widget — the
    /// gradient evaluated one column per texture pixel, composited OVER a checkerboard so transparency reads.
    /// Because <see cref="Gradient.Evaluate"/> honors the gradient's <see cref="GradientMode"/>, the strip is
    /// automatically correct for Blend (smooth), Fixed (stepped), and PerceptualBlend with no extra work.
    ///
    /// IMPORTANT: this is STATELESS — each call returns a fresh, caller-OWNED texture. The caller MUST destroy it
    /// (on rebuild + on DetachFromPanel). Deliberately NO shared cache: evicting an entry frees a texture that live
    /// elements may still reference — the preset swatches never re-fetch, so they would render black.
    /// </summary>
    internal static class SasquatchGradientStripRenderer
    {
        private const int CheckerCell = 4; // checker square size in texture pixels

        /// <summary>Content signature of a gradient (color/alpha keys + mode) — drives the control's dirty check.</summary>
        public static int Signature(Gradient g)
        {
            if (g == null) return 0;
            unchecked
            {
                int h = (int)g.mode * 397;
                var ck = g.colorKeys;
                for (int i = 0; i < ck.Length; i++)
                {
                    h = h * 31 + ck[i].time.GetHashCode();
                    h = h * 31 + ck[i].color.GetHashCode();
                }
                var ak = g.alphaKeys;
                for (int i = 0; i < ak.Length; i++)
                {
                    h = h * 31 + ak[i].time.GetHashCode();
                    h = h * 31 + ak[i].alpha.GetHashCode();
                }
                return h;
            }
        }

        /// <summary>Creates a fresh, caller-owned strip texture. The caller is responsible for destroying it.</summary>
        public static Texture2D Create(Gradient gradient, int width, int height, bool checker, Color checkerA, Color checkerB)
            => RenderInto(null, gradient, width, height, checker, checkerA, checkerB);

        // The pixel scratch buffer, reused across renders. A strip is redrawn on EVERY frame of a width drag
        // (by design — the dirty check keys on the pixel width, so a drag is a miss by construction and
        // re-rasterising is what keeps the strip sharp), and at a typical 300x32 that was ~38 KB of Color32
        // discarded per frame. Grow-only, and fully overwritten before SetPixels32 reads it.
        private static Color32[] _scratch;

        /// <summary>
        /// Renders the strip into <paramref name="existing"/>, reusing its native texture object when it can,
        /// and returns the texture to use. Pass null to create one.
        ///
        /// <para>WHAT THIS DOES NOT CHANGE: the strip is still re-rasterised at the exact requested width on
        /// every call. That per-frame re-render is the product — bucketing the width or stretching a smaller
        /// texture would be visible, and is off the table. What goes away is the native allocate / upload /
        /// free cycle around it: <see cref="Texture2D.Reinitialize(int,int)"/> resizes the existing native
        /// object in place, so a drag reuses one texture instead of minting and destroying one per frame.</para>
        ///
        /// <para>The caller still owns the returned texture and must destroy it (see the control's
        /// PanelLifetime.OnRealDetach teardown) — reuse changes when it is allocated, never who owns it.</para>
        /// </summary>
        public static Texture2D RenderInto(Texture2D existing, Gradient gradient, int width, int height,
                                           bool checker, Color checkerA, Color checkerB)
        {
            if (gradient == null || width <= 0 || height <= 0) return existing;

            width = Mathf.Clamp(width, 1, 4096);
            height = Mathf.Clamp(height, 1, 512);

            var tex = existing;
            if (tex == null)
            {
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
            else if (tex.width != width || tex.height != height)
            {
                // Reinitialize leaves the contents undefined, which is fine: every pixel is written below
                // before Apply. It keeps the same managed AND native object, so anything holding a reference
                // (the element's backgroundImage) keeps pointing at a live texture.
                tex.Reinitialize(width, height);
            }

            if (_scratch == null || _scratch.Length < width * height) _scratch = new Color32[width * height];
            var pixels = _scratch;
            Color opaqueBlack = new Color(0f, 0f, 0f, 1f);

            for (int x = 0; x < width; x++)
            {
                float t = width > 1 ? x / (float)(width - 1) : 0f;
                Color col = gradient.Evaluate(Mathf.Clamp01(t)); // rgb = color channel, a = alpha channel (honors mode)
                Color opaque = new Color(col.r, col.g, col.b, 1f);

                for (int y = 0; y < height; y++)
                {
                    Color baseColor;
                    if (checker)
                    {
                        bool even = (((x / CheckerCell) + (y / CheckerCell)) & 1) == 0;
                        baseColor = even ? checkerA : checkerB;
                    }
                    else
                    {
                        baseColor = opaqueBlack;
                    }
                    // Composite color × alpha over the backing so transparency shows.
                    pixels[y * width + x] = Color.Lerp(baseColor, opaque, col.a);
                }
            }

            // The BLOCK overload of SetPixels32, not the whole-array one: the whole-array overload demands an
            // array whose length EXACTLY equals the pixel count, which would force a fresh buffer on every
            // frame of a drag and give back the entire reason the scratch buffer exists. The block overload
            // reads blockWidth*blockHeight entries from the front and ignores any surplus.
            //
            // And NOT SetPixelData, which was the first attempt: it writes raw bytes with no colour
            // conversion, so on this texture it rendered visibly different pixels
            // (measured: RGBA(82,74,74) became RGBA(63,55,54) — a gamma difference, not a rounding one).
            // TestGradientStripReuseMatchesAFreshRender is what caught that, and it compares every pixel
            // against a verbatim copy of the old create-and-SetPixels32 path across seven sizes.
            tex.SetPixels32(0, 0, width, height, pixels, 0);
            tex.Apply(false);
            return tex;
        }
    }
}
