using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Forces crisp, native-resolution import settings on the Tutorial's illustration images (everything under
    /// <c>Editor/Tutorial/Media/</c>) so screenshots stay sharp in the Tutorial window: no block compression, no
    /// non-power-of-two rescaling, and a max size large enough that the source is never downscaled.
    /// Pairs with <see cref="TutorialMediaView"/>, which displays the result at native size (never upscaled past
    /// 1:1). Drop a PNG/JPG into the Media folder and it imports crisp automatically — no per-file fiddling.
    /// </summary>
    internal sealed class TutorialMediaImporter : AssetPostprocessor
    {
        private const string MediaFolder = "/Editor/Tutorial/Media/";

        private void OnPreprocessTexture()
        {
            if (assetPath.IndexOf(MediaFolder, System.StringComparison.OrdinalIgnoreCase) < 0) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            // Crispness is achieved at DISPLAY time, not here: TutorialMediaView sizes each image to an exact whole
            // number of device pixels and blits it 1:1 (resampling on shrink). These import settings just keep a
            // clean SOURCE for that — uncompressed truecolor (no block-compression colour shift) at full resolution,
            // plus a Kaiser mip chain sampled (filterMode Trilinear) by the one-time GPU downsample so a large
            // screenshot shrinks cleanly. The on-screen texture is always a mip-free 1:1 copy, so the mip chain
            // never softens the displayed result.
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            // ...but a mip chain is only an asset here if mip 0 actually survives to the GPU. QualitySettings'
            // global mipmap limit discards the top mip of every mipmapped texture, and it is PER QUALITY LEVEL:
            // "Very Low" ships with a limit of 1, and a fresh project can land there on its own (a clean Unity
            // 6.5 project does). The downsample would then read a half-resolution source and every tutorial
            // screenshot would render visibly soft — on the buyer's machine only, never on ours, because this
            // project sits on Ultra (limit 0). Opt these textures out so the chain is a quality win at any
            // quality level instead of a resolution loss at some of them.
            importer.ignoreMipmapLimit = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 8192;          // keep full source detail for the one-time downsample
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
        }
    }
}
