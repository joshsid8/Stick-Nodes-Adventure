using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Classifies the project's active render pipeline and centralises the "render a preview camera into an
    /// owned RenderTexture" path so editor previews look correct under every pipeline. Detection uses only the
    /// public <see cref="GraphicsSettings"/> API plus the pipeline asset's type name, with no URP/HDRP package
    /// reference and no <c>#if</c> guards, so it compiles and runs whether or not those
    /// packages are installed (mirrors how <c>ParticlePreviewRenderer</c> already submits render
    /// requests through core <c>UnityEngine.Rendering</c> only).
    /// </summary>
    internal static class RenderPipelineUtility
    {
        // Urp2D is retained so consumer switch statements stay valid, but Detect() no longer distinguishes it:
        // the 2D-vs-3D split only picked a shader and the self-lit PreviewLit shader renders both, so there is
        // nothing to gain from telling them apart. All URP now maps to Urp3D.
        internal enum PipelineKind { BuiltIn, Urp3D, Urp2D, Hdrp }

        // --- HDRP preview tuning ------------------------------------------------------------------------------
        // Directional-light intensity multiplier applied ONLY under HDRP, where Light.intensity is interpreted in
        // physical lux — a 1.4 "unit" key that reads correctly under URP/Built-in is near-black in HDRP. ~21000
        // lux/unit puts the 1.4/0.7 preset near ~30k/~15k lux (bright, not blown) and PRESERVES each call site's
        // relative key/fill so URP/Built-in output stays byte-identical. HDRP auto-exposure makes the absolute
        // value scene-dependent, so pinning it exactly would need a fixed-exposure Volume in the preview scene.
        // Re-tuning this needs a project that actually has HDRP installed — the effect of changing it is not
        // observable in a Built-in/URP project, so don't adjust it on reasoning alone.
        internal const float HdrpLuxPerUnit = 21000f;

        // PreviewLit.shader carries an untagged (⇒ SRPDefaultUnlit) pass inside an HDRenderPipeline-tagged
        // SubShader. HDRP's render loop treats SRPDefaultUnlit as a supported fallback pass, so the self-lit
        // PreviewLit shader is preferred under HDRP too: its hand-fed light uniforms make physical light units
        // irrelevant and keep the preview byte-identical to URP/Built-in (which was the whole point — HDRP/Lit
        // at HdrpLuxPerUnit rendered near-black under auto-exposure).
        // static readonly (not const) so the disabled branch in PickHdrpPreviewShader isn't compiled out as
        // unreachable — flip to false to fall back to HDRP/Lit + lux-scaled lights if a regression ever appears.
        internal static readonly bool HdrpPrefersPreviewLit = true;

        /// <summary>
        /// Best-effort classification of the active pipeline. Unknown SRPs fall back to <see cref="PipelineKind.Urp3D"/>
        /// (the SubmitRenderRequest path), which is the safe choice for any SRP. The URP-2D vs URP-3D split is only
        /// used to pick a shader; misclassifying it is harmless because the Spring preview uses the self-lit
        /// "PreviewLit" shader for ALL URP variants.
        /// </summary>
        internal static PipelineKind Detect()
        {
            var rp = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            return Classify(rp != null ? rp.GetType().Name : null);
        }

        /// <summary>
        /// Pure classification of a render-pipeline asset TYPE NAME into a <see cref="PipelineKind"/>, split out
        /// of <see cref="Detect"/> so the mapping can be unit-tested without assigning a real RenderPipelineAsset
        /// to GraphicsSettings. Behavior is identical to the old inline branch: null/empty → BuiltIn,
        /// "HDRenderPipelineAsset" → Hdrp, and everything else (all URP variants + any third-party SRP) → Urp3D,
        /// which renders through the SubmitRenderRequest path with the self-lit PreviewLit shader — so URP-2D
        /// never needs distinguishing from URP-3D.
        /// </summary>
        internal static PipelineKind Classify(string pipelineTypeName)
        {
            if (string.IsNullOrEmpty(pipelineTypeName)) return PipelineKind.BuiltIn;
            if (pipelineTypeName == "HDRenderPipelineAsset") return PipelineKind.Hdrp;
            return PipelineKind.Urp3D;
        }

        /// <summary>
        /// Shared HDRP branch for the preview <c>PickShader</c> sites (AssetPreviewRenderer + SpringSandboxWindow).
        /// Prefers the self-lit PreviewLit shader when <see cref="HdrpPrefersPreviewLit"/> is enabled; otherwise
        /// HDRP/Lit (lit by the intensity-scaled preview lights), falling back to Standard / PreviewLit.
        /// </summary>
        internal static Shader PickHdrpPreviewShader()
        {
            if (HdrpPrefersPreviewLit)
            {
                var self = Shader.Find("Hidden/Sasquatch/PreviewLit");
                if (self != null) return self;
            }
            return Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/Sasquatch/PreviewLit");
        }

        /// <summary>
        /// Centralised "render this preview camera into <paramref name="rt"/>": SubmitRenderRequest under an active
        /// SRP (URP/HDRP), falling back to <c>camera.Render()</c> when no SRP is active or the request is
        /// unsupported (Built-in). Shared by AssetPreviewRenderer + ParticlePreviewRenderer so the two stay in
        /// lockstep behind one pipeline seam. Never throws — at worst the frame is blank.
        /// </summary>
        internal static void SubmitPreviewRender(Camera cam, RenderTexture rt)
        {
            if (cam == null || rt == null) return;

            bool submitted = false;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                try
                {
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req))
                    {
                        RenderPipeline.SubmitRenderRequest(cam, req);
                        submitted = true;
                    }
                }
                catch (Exception) { submitted = false; }
            }
            if (!submitted)
            {
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;
            }
        }

        /// <summary>
        /// Centralised preview key+fill+ambient light setup. URP/Built-in receive <paramref name="keyIntensity"/>
        /// and <paramref name="fillIntensity"/> verbatim (byte-identical to the old inline setup); under HDRP the
        /// two directional intensities are scaled to physical lux via <see cref="HdrpLuxPerUnit"/>. Light
        /// rotations/colors match the long-standing key/fill preset. Callers keep their own camera + ambient values
        /// (ambient is passed in because the sandbox and asset previews differ).
        /// </summary>
        internal static void ConfigurePreviewLights(PreviewRenderUtility preview, float keyIntensity, float fillIntensity, Color ambient)
        {
            if (preview == null) return;
            preview.ambientColor = ambient;

            bool hdrp = Detect() == PipelineKind.Hdrp;
            if (preview.lights.Length > 0)
            {
                preview.lights[0].intensity = hdrp ? keyIntensity * HdrpLuxPerUnit : keyIntensity;
                preview.lights[0].color = Color.white;
                preview.lights[0].transform.rotation = Quaternion.Euler(38f, 32f, 0f);
            }
            if (preview.lights.Length > 1)
            {
                preview.lights[1].intensity = hdrp ? fillIntensity * HdrpLuxPerUnit : fillIntensity;
                preview.lights[1].color = new Color(0.78f, 0.84f, 1f);
                preview.lights[1].transform.rotation = Quaternion.Euler(-18f, -130f, 0f);
            }
        }
    }
}
