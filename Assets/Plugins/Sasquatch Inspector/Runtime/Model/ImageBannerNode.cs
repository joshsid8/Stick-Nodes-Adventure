using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>Horizontal placement of a banner image within its row.</summary>
    public enum ImageBannerAlignment
    {
        Left,
        Center,
        Right
    }

    /// <summary>How a banner image is fit into its box: preserve aspect (fit), fill and crop, or stretch to fill.</summary>
    public enum ImageBannerScaleMode
    {
        ScaleToFit,
        ScaleAndCrop,
        StretchToFill
    }

    /// <summary>
    /// A purely decorative image / banner leaf: renders a texture (referenced by asset GUID so it survives moves
    /// and renames) at a fixed height with a chosen alignment and scale mode. A plain leaf (like
    /// <see cref="SpaceNode"/>) — never offered in the Display Type popup. The GUID→asset resolve happens in the
    /// editor builder, so the model stays Runtime-clean (just a string).
    /// </summary>
    [Serializable]
    public class ImageBannerNode : LayoutNode
    {
        /// <summary>Clamp bounds for the banner height, shared by the setter and the builder.</summary>
        public const float MinHeight = 8f;
        public const float MaxHeight = 800f;

        /// <summary>Clamp bounds for the zoom factor, shared by the setter and the builder.</summary>
        public const float MinZoom = 0.1f;
        public const float MaxZoom = 8f;

        [SerializeField] private string textureGuid = "";
        [SerializeField] private ImageBannerAlignment alignment = ImageBannerAlignment.Center;
        [SerializeField] private ImageBannerScaleMode scaleMode = ImageBannerScaleMode.ScaleToFit;
        [SerializeField] private float height = 80f;
        // Uniform zoom applied on top of the scale mode (1 = unchanged; >1 enlarges-and-crops within the box).
        [SerializeField] private float zoom = 1f;
        // ScaleToFit only: tighten the fit to the texture's opaque (non-transparent) bounding box before fitting.
        // On by default (transparent margins almost never want to be preserved); a no-op on images without any.
        [SerializeField] private bool cropAlpha = true;

        /// <summary>Asset GUID of the Texture2D/Sprite to display; empty ⇒ an empty placeholder.</summary>
        public string TextureGuid
        {
            get => textureGuid;
            set => textureGuid = value;
        }

        /// <summary>Horizontal placement of the image within the banner width.</summary>
        public ImageBannerAlignment Alignment
        {
            get => alignment;
            set => alignment = value;
        }

        /// <summary>How the image is fit into its box (Fit preserves aspect and honors <see cref="Alignment"/>).</summary>
        public ImageBannerScaleMode ScaleMode
        {
            get => scaleMode;
            set => scaleMode = value;
        }

        /// <summary>Banner height in pixels, clamped to [<see cref="MinHeight"/>..<see cref="MaxHeight"/>].</summary>
        public float Height
        {
            get => height;
            set => height = Mathf.Clamp(value, MinHeight, MaxHeight);
        }

        /// <summary>Uniform zoom on top of the scale mode (1 = unchanged), clamped to
        /// [<see cref="MinZoom"/>..<see cref="MaxZoom"/>]. Zoom &gt; 1 enlarges the image and crops it to the box.</summary>
        public float Zoom
        {
            get => zoom;
            set => zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
        }

        /// <summary>ScaleToFit only: tighten the fit to the texture's opaque (non-transparent) bounding box so
        /// transparent margins don't waste space. Ignored by the Crop / Stretch modes.</summary>
        public bool CropAlpha
        {
            get => cropAlpha;
            set => cropAlpha = value;
        }
    }
}
