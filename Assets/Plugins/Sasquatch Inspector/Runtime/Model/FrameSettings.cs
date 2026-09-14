using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SasquatchInspector.Runtime
{
    /// <summary>Shape of a Frame container's clip region. Values are serialized by int — append only.</summary>
    public enum FrameShape
    {
        RoundedRect = 0,
        Circle = 1,
        Pill = 2,
        DiagonalSplit = 3
    }

    /// <summary>Which edge a Frame's overlay wash hugs (Full = the whole frame).</summary>
    public enum FrameOverlayEdge
    {
        Bottom = 0,
        Top = 1,
        Full = 2
    }

    /// <summary>
    /// Settings for a <see cref="ContainerType.Frame"/> container: a shaped container that CLIPS its
    /// children to the shape (rounded rect / circle / pill / diagonal two-pane split), with an optional
    /// image fill behind the content (a static texture by GUID, or a Texture2D/Sprite field bound from
    /// the inspected object so each asset shows its own image) and an optional gradient overlay wash
    /// between the fill and the content. A plain [Serializable] class (not a struct): it is stored as a single field
    /// on ContainerNode, and Unity default-constructs a missing plain class field, so both appending this
    /// field to ContainerNode and appending fields inside this class keep old layouts deserializing clean.
    /// The frame's solid background color, border ring, and RoundedRect corner radius intentionally live
    /// on the generic LayoutNode.Style (backgroundColor / borderColor+borderWidth / borderRadius) — the
    /// builder paints them on the clipping root, so they follow the shape for free. Likewise width /
    /// height floor / alignment ride Style.width / Style.height / Style.alignSelf.
    /// </summary>
    [Serializable]
    public class FrameSettings
    {
        /// <summary>Clamp bounds for <see cref="AspectRatio"/> (width / height).</summary>
        public const float MinAspect = 0.1f;
        public const float MaxAspect = 10f;

        /// <summary>Clamp bounds for <see cref="SplitPosition"/> (normalized seam midpoint).</summary>
        public const float MinSplitPos = 0.05f;
        public const float MaxSplitPos = 0.95f;

        /// <summary>Clamp bound for <see cref="SplitAngle"/> magnitude, degrees from vertical.</summary>
        public const float MaxSplitAngle = 60f;

        /// <summary>Clamp bounds for <see cref="FillZoom"/> (mirrors <see cref="ImageBannerNode"/>).</summary>
        public const float MinZoom = 0.1f;
        public const float MaxZoom = 8f;

        /// <summary>Clamp bounds for <see cref="OverlaySizePercent"/>.</summary>
        public const float MinOverlayPercent = 5f;
        public const float MaxOverlayPercent = 100f;

        /// <summary>Default seam line for a fresh DiagonalSplit, so the split reads as two panes
        /// immediately instead of being invisible until the user styles it. Soft translucent white
        /// stays visible over any pane color/fill in both editor themes.</summary>
        public static readonly Color DefaultSeamColor = new Color(1f, 1f, 1f, 0.55f);
        public const float DefaultSeamWidth = 2f;

        [SerializeField] private FrameShape shape = FrameShape.RoundedRect;

        // Aspect: Circle always renders 1:1 regardless of these (the builder enforces it).
        [SerializeField] private bool lockAspect = false;
        [SerializeField] private float aspectRatio = 1f;

        // Image fill. The binding path wins over the static GUID when both are serialized (the properties
        // panel keeps them mutually exclusive); empty both ⇒ color-only fill (Style.backgroundColor).
        [SerializeField] private string fillBindingPath = "";
        [SerializeField] private string fillTextureGuid = "";
        // The UI offers only ScaleToFit / StretchToFill; ScaleAndCrop still renders for any data that
        // carries it, it's just no longer offered.
        [SerializeField] private ImageBannerScaleMode fillScaleMode = ImageBannerScaleMode.StretchToFill;
        [SerializeField] private float fillZoom = 1f;
        // Framing focus (0..1 per axis, 0.5 = centered): which part of a zoomed fill stays visible.
        [SerializeField] private Vector2 fillFocus = new Vector2(0.5f, 0.5f);

        // Content anchoring deliberately has NO field here: a frame's content block anchors via the
        // generic Style.alignItems (horizontal) + Style.justify (vertical), which the builder transfers
        // onto the content host (the Columns-container precedent) — no duplicate anchor UI.

        // Overlay wash between fill and content ("scrim" in web design — renamed for humans). The wash's
        // color AND alpha profile come from a Gradient (sampled from the edge inward), which replaced the
        // old single overlayColor + overlayFade bool: a color fading to transparent reproduces the classic
        // edge scrim, but any multi-stop gradient is now possible. overlayEnabled is the master on/off.
        [SerializeField] private bool overlayEnabled;
        // Lazily seeded (see OverlayGradient) so an untouched overlay reads as the default dark edge fade
        // without storing a gradient (mirrors ProgressBarFillGradient), keeping mere viewing non-dirtying.
        [SerializeField] private Gradient overlayGradient;
        [SerializeField] private bool overlayGradientSeeded;

        [FormerlySerializedAs("scrimEdge")]
        [SerializeField] private FrameOverlayEdge overlayEdge = FrameOverlayEdge.Bottom;
        [FormerlySerializedAs("scrimSizePercent")]
        [SerializeField] private float overlaySizePercent = 40f;

        // LEGACY (migration-only, no longer surfaced in the UI): pre-gradient overlays stored a single
        // color + a Fade bool. OverlayEnabled/OverlayGradient synthesize an equivalent gradient from these
        // until the user touches the overlay, then clear them. Kept serialized so old layouts migrate clean.
        [FormerlySerializedAs("scrimColor")]
        [SerializeField] private OverridableColor overlayColor;
        [FormerlySerializedAs("scrimGradient")]
        [SerializeField] private bool overlayFade = true;

        // DiagonalSplit only. splitAngle is degrees from vertical; the sign flips "/" vs "\".
        // The seam defaults VISIBLE (a fresh split must never be invisible); layouts saved before
        // this default carry their explicit 0/unset values and are re-seeded at the designer's
        // shape-switch edit point instead.
        [SerializeField] private float splitPosition = 0.5f;
        [SerializeField] private float splitAngle = 15f;
        [SerializeField] private OverridableColor seamColor = new OverridableColor { isOverridden = true, value = new Color(1f, 1f, 1f, 0.55f) }; // == DefaultSeamColor
        [SerializeField] private float seamWidth = DefaultSeamWidth;
        // Per-pane image fills (DiagonalSplit only), painted under each pane's content, clipped at the
        // seam. Static GUIDs (the ImageBanner pattern). Appended → old layouts deserialize clean.
        [SerializeField] private string paneAFillGuid = "";
        [SerializeField] private string paneBFillGuid = "";

        public FrameShape Shape
        {
            get => shape;
            set => shape = value;
        }

        /// <summary>RoundedRect/Pill only: lock the frame's height to <see cref="AspectRatio"/> from its
        /// width. Circle is always locked 1:1 regardless of this flag.</summary>
        public bool LockAspect
        {
            get => lockAspect;
            set => lockAspect = value;
        }

        /// <summary>Width / height ratio used when <see cref="LockAspect"/> is on, clamped to
        /// [<see cref="MinAspect"/>..<see cref="MaxAspect"/>].</summary>
        public float AspectRatio
        {
            get => aspectRatio;
            set => aspectRatio = Mathf.Clamp(value, MinAspect, MaxAspect);
        }

        /// <summary>Binding path of a Texture2D/Sprite field on the inspected object used as the fill
        /// image (each object shows its own image). Wins over <see cref="FillTextureGuid"/>; empty ⇒
        /// fall back to the static texture.</summary>
        public string FillBindingPath
        {
            get => fillBindingPath;
            set => fillBindingPath = value;
        }

        /// <summary>Asset GUID of a static fill Texture2D/Sprite (same image for every object of the
        /// type). Empty and no binding ⇒ color-only fill.</summary>
        public string FillTextureGuid
        {
            get => fillTextureGuid;
            set => fillTextureGuid = value;
        }

        /// <summary>How the fill image fits the frame. The UI offers Fit / Stretch only.</summary>
        public ImageBannerScaleMode FillScaleMode
        {
            get => fillScaleMode;
            set => fillScaleMode = value;
        }

        /// <summary>Uniform zoom on top of the scale mode (1 = unchanged), clamped to
        /// [<see cref="MinZoom"/>..<see cref="MaxZoom"/>]. The frame's clip crops the overflow.</summary>
        public float FillZoom
        {
            get => fillZoom;
            set => fillZoom = Mathf.Clamp(value, MinZoom, MaxZoom);
        }

        /// <summary>Framing focus for a zoomed fill (0..1 per axis, 0.5/0.5 = centered): which part of
        /// the image stays visible when the zoom overflows the frame.</summary>
        public Vector2 FillFocus
        {
            get => fillFocus;
            set => fillFocus = new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }

        /// <summary>Master on/off for the overlay wash. A legacy overlay (old single-color data) reads as
        /// enabled until the user touches it; turning it off clears that legacy source so it can't resurface.</summary>
        public bool OverlayEnabled
        {
            get => overlayEnabled || overlayColor.isOverridden;
            set
            {
                overlayEnabled = value;
                overlayColor = default; // drop the legacy synthesis source once the new model is in charge
            }
        }

        /// <summary>The overlay wash's color + alpha profile, sampled from the edge (t=0) inward. Until the
        /// user sets one, the getter returns a default dark edge fade WITHOUT storing it (so merely viewing a
        /// frame never dirties the asset); a legacy single-color overlay is synthesized into an equivalent
        /// gradient on the fly. Assigning a gradient pins it, enables the overlay, and retires the legacy data.</summary>
        public Gradient OverlayGradient
        {
            get
            {
                if (overlayGradientSeeded && overlayGradient != null) return overlayGradient;
                if (overlayColor.isOverridden) return LegacyOverlayGradient();
                return DefaultOverlayGradient();
            }
            set
            {
                overlayGradient = value;
                overlayGradientSeeded = true;
                overlayEnabled = true;
                overlayColor = default;
            }
        }

        /// <summary>The out-of-the-box overlay ramp: translucent black at the edge fading to transparent
        /// inward — the classic "darken an edge so overlaid text stays readable" wash.</summary>
        public static Gradient DefaultOverlayGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // Synthesizes the pre-gradient overlay (single color + Fade bool) into an equivalent gradient so old
        // layouts keep rendering: solid ⇒ constant color/alpha; faded ⇒ full alpha at the edge → transparent.
        private Gradient LegacyOverlayGradient()
        {
            var c = overlayColor.value;
            var rgb = new Color(c.r, c.g, c.b);
            bool solid = overlayEdge == FrameOverlayEdge.Full || !overlayFade;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(rgb, 0f), new GradientColorKey(rgb, 1f) },
                solid
                    ? new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(c.a, 1f) }
                    : new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>Which edge the overlay gradient grows from — Bottom or Top — or Full (the gradient spans
        /// the whole frame, bottom → top; <see cref="OverlaySizePercent"/> is ignored).</summary>
        public FrameOverlayEdge OverlayEdge
        {
            get => overlayEdge;
            set => overlayEdge = value;
        }

        /// <summary>How far the overlay gradient reaches from the edge, as a percentage of the frame's height
        /// (ignored for Full), clamped to [<see cref="MinOverlayPercent"/>..<see cref="MaxOverlayPercent"/>].</summary>
        public float OverlaySizePercent
        {
            get => overlaySizePercent;
            set => overlaySizePercent = Mathf.Clamp(value, MinOverlayPercent, MaxOverlayPercent);
        }

        /// <summary>DiagonalSplit only: normalized position of the seam midpoint along the frame width,
        /// clamped to [<see cref="MinSplitPos"/>..<see cref="MaxSplitPos"/>].</summary>
        public float SplitPosition
        {
            get => splitPosition;
            set => splitPosition = Mathf.Clamp(value, MinSplitPos, MaxSplitPos);
        }

        /// <summary>DiagonalSplit only: seam tilt in degrees from vertical, clamped to
        /// [-<see cref="MaxSplitAngle"/>..<see cref="MaxSplitAngle"/>]; the sign flips "/" vs "\".</summary>
        public float SplitAngle
        {
            get => splitAngle;
            set => splitAngle = Mathf.Clamp(value, -MaxSplitAngle, MaxSplitAngle);
        }

        /// <summary>DiagonalSplit only: optional seam line color; unset ⇒ no drawn seam.</summary>
        public OverridableColor SeamColor
        {
            get => seamColor;
            set => seamColor = value;
        }

        /// <summary>DiagonalSplit only: seam line width in pixels; 0 ⇒ no drawn seam.</summary>
        public float SeamWidth
        {
            get => seamWidth;
            set => seamWidth = Mathf.Max(0f, value);
        }

        /// <summary>DiagonalSplit only: static image fill painted under Pane A's content (clipped at the seam).</summary>
        public string PaneAFillGuid
        {
            get => paneAFillGuid;
            set => paneAFillGuid = value;
        }

        /// <summary>DiagonalSplit only: static image fill painted under Pane B's content (clipped at the seam).</summary>
        public string PaneBFillGuid
        {
            get => paneBFillGuid;
            set => paneBFillGuid = value;
        }
    }
}
