#if SASQ_PARTICLES // ParticleSystem lives in com.unity.modules.particlesystem, a REMOVABLE built-in module.
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds the Particle Preview demo's effect in-memory: a hidden GameObject carrying a configured
    /// ParticleSystem (plus a generated soft-dot material/texture), so the Showcase demo is entirely
    /// self-contained in the package — no dependency on any project prefab. The ParticleSystemPreview widget
    /// instantiates its OWN copy into its preview scene, so this original is only the reference; everything
    /// created here is registered to the demo's cleanup sink and torn down when the demo changes.
    /// </summary>
    internal static class ShowcaseParticles
    {
        /// <summary>Create the demo VFX GameObject and register it (and its generated assets) to <paramref name="sink"/>.</summary>
        public static GameObject CreateDemoEffect(List<UnityEngine.Object> sink)
        {
            var go = new GameObject("Showcase VFX");
            go.hideFlags = HideFlags.HideAndDontSave;
            sink.Add(go);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // the widget drives simulation; never self-play

            var main = ps.main;
            main.duration = 2.0f;
            main.loop = true;
            main.startLifetime = 1.3f;
            main.startSpeed = 2.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startColor = new Color(1f, 0.65f, 0.25f, 1f); // warm ember
            main.gravityModifier = -0.05f;                     // gentle upward drift
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.rateOverTime = 70f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.25f;

            // Hue shift + fade in/out over life for a lively look.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.85f, 0.40f), 0f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.15f), 0.55f),
                    new GradientColorKey(new Color(0.70f, 0.20f, 0.35f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.70f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));

            // A material + soft round texture so particles read as glowing dots, not hard white squares.
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateParticleMaterial(sink);

            return go;
        }

        // A transparent, texture-driven material that renders reliably in BOTH URP and the built-in pipeline.
        // Sprites/Default is always present, alpha-blends out of the box (no URP keyword/blend-state wrangling),
        // and multiplies by the per-particle vertex color, so startColor + colorOverLifetime tint it correctly.
        private static Material CreateParticleMaterial(List<UnityEngine.Object> sink)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");

            if (shader == null) shader = Shader.Find("Sprites/Default"); // last-resort guard (all fallbacks stripped)
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            sink.Add(mat);

            var tex = CreateSoftDotTexture(sink);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            return mat;
        }

        // A 64² radial alpha falloff (white core → transparent edge) so billboards read as soft round dots.
        private static Texture2D CreateSoftDotTexture(List<UnityEngine.Object> sink)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                name = "ShowcaseParticleDot"
            };
            var px = new Color32[s * s];
            float r = s * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a; // soft falloff
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            sink.Add(tex);
            return tex;
        }
    }
}
#endif // SASQ_PARTICLES
