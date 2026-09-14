using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Renders a Tutorial step's <see cref="TutorialMedia"/> (a still image) into a UI Toolkit element. Source
    /// files live under <c>Editor/Tutorial/Media/</c>, are referenced by file name, and load through the
    /// AssetDatabase. Images size responsively (capped width, aspect preserved). A missing file degrades to a tidy
    /// placeholder rather than an error, so a step that references art you haven't dropped in yet still renders cleanly.
    /// </summary>
    internal static class TutorialMediaView
    {
        private const float MaxImageWidth = 560f;

        /// <summary>Builds a stacked block of all of a step's media, or <c>null</c> when the step has none.</summary>
        public static VisualElement BuildBlock(TutorialMedia[] media)
        {
            if (media == null || media.Length == 0) return null;

            var col = new VisualElement();
            col.style.marginTop = 2;
            col.style.marginBottom = 4;
            foreach (var m in media)
            {
                if (m == null || string.IsNullOrEmpty(m.Id)) continue;
                col.Add(BuildOne(m));
            }
            return col.childCount > 0 ? col : null;
        }

        /// <summary>Builds a single media item (image) with caption, framed and native-sized. Used both by
        /// <see cref="BuildBlock"/> and by the Tutorial window's per-block image rendering.</summary>
        internal static VisualElement BuildOne(TutorialMedia m)
        {
            var wrap = new VisualElement();
            wrap.style.marginBottom = 12;
            wrap.style.alignItems = Align.Center;

            string path = ResolvePath(m.Id);
            wrap.Add(string.IsNullOrEmpty(path)
                ? Placeholder($"Image “{m.Id}” not found — drop it into Editor/Tutorial/Media/")
                : BuildImageElement(path, m.Id));

            if (!string.IsNullOrEmpty(m.Caption))
            {
                var cap = new Label(m.Caption);
                cap.AddToClassList("sasq-palettewin-hint");
                cap.style.marginTop = 5;
                cap.style.whiteSpace = WhiteSpace.Normal;
                cap.style.unityTextAlign = TextAnchor.MiddleCenter;
                wrap.Add(cap);
            }
            return wrap;
        }

        // Locates "<id>.<ext>" under the package's Editor/Tutorial/Media folder (relocation-safe via AssetDatabase).
        private static string ResolvePath(string id)
        {
            string folder = SasquatchPaths.PackageRoot + "/Editor/Tutorial/Media";
            if (!AssetDatabase.IsValidFolder(folder)) return null;

            foreach (string guid in AssetDatabase.FindAssets(id, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), id, System.StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return null;
        }

        private static VisualElement BuildImageElement(string path, string id)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return Placeholder($"Couldn't load image “{id}”");

            // The texture is handed to the frame, which displays it at a crisp 1:1 device-pixel size (resampling on
            // shrink) instead of letting UI Toolkit bilinear-scale it — scaleMode is moot once the sizes match.
            var still = new Image { scaleMode = ScaleMode.ScaleToFit };
            return Frame(still, tex, tex.width, tex.height);
        }

        // Wraps an image in a rounded, clipped, subtly-bordered frame and sizes it CRISP. The element is set to a
        // whole number of DEVICE pixels (never above the source's native size) and fed a texture resampled to
        // exactly that size, so UI Toolkit blits it 1:1 — no element-level bilinear softening, no sub-pixel-size
        // blur. <paramref name="source"/> is the image's texture (downsampled on shrink via TutorialImageScaler).
        private static VisualElement Frame(Image image, Texture2D source, int texW, int texH)
        {
            var frame = new VisualElement();
            // Cap at the image's NATIVE DEVICE pixels (texW / pixelsPerPoint), not its logical width: UI Toolkit
            // measures in points, so dividing native px by pixelsPerPoint maps "native pixels → points" and keeps
            // the image ≤ 1:1 with the screen (never upscaled). Still clamped to the 560-pt panel cap.
            frame.style.maxWidth = Mathf.Min(texW / EditorGUIUtility.pixelsPerPoint, MaxImageWidth);
            frame.style.width = Length.Percent(100);
            frame.style.overflow = Overflow.Hidden;
            frame.style.backgroundColor = new Color(0.10f, 0.10f, 0.11f, 1f);
            frame.style.borderTopLeftRadius = frame.style.borderTopRightRadius =
                frame.style.borderBottomLeftRadius = frame.style.borderBottomRightRadius = 6;
            float border = 1f;
            frame.style.borderTopWidth = frame.style.borderBottomWidth =
                frame.style.borderLeftWidth = frame.style.borderRightWidth = border;
            var line = new Color(1f, 1f, 1f, 0.10f);
            frame.style.borderTopColor = frame.style.borderBottomColor =
                frame.style.borderLeftColor = frame.style.borderRightColor = line;
            frame.Add(image);

            float aspect = texH / (float)Mathf.Max(1, texW);
            int lastW = -1, lastH = -1;

            // Pick the largest whole-device-pixel size that fits the available width without upscaling, snap the
            // element to it, and (for stills) feed it a texture resampled to exactly that size.
            void Fit(float availablePoints)
            {
                float ppp = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                int availDev = Mathf.RoundToInt(availablePoints * ppp);
                int targetW = Mathf.Clamp(Mathf.Min(availDev, texW), 1, texW);
                int targetH = Mathf.Max(1, Mathf.RoundToInt(targetW * aspect));
                if (targetW == lastW && targetH == lastH) return; // already at this size — avoid a layout feedback loop

                // Whole device pixels in, whole device pixels out: targetW/H are integers, so width/height land on
                // exact pixel boundaries (no sub-pixel blur), and the texture is shown at that exact size (no scaling).
                image.style.width = targetW / ppp;
                image.style.height = targetH / ppp;

                var display = TutorialImageScaler.GetDisplayTexture(source, targetW, targetH);
                image.image = display;
                // Latch this size only once the final texture is ready. A downscale that transiently failed to
                // resample falls back to the full-res source — leave it unlatched so the next geometry pass retries.
                if (targetW == source.width || !ReferenceEquals(display, source)) { lastW = targetW; lastH = targetH; }
            }

            frame.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.width > 1f) Fit(evt.newRect.width);
            });
            // Seed a size immediately (native, capped to the panel) so the image never flashes at its raw measured
            // size before the first geometry pass; the callback narrows it if the panel is actually smaller.
            Fit(Mathf.Min(texW / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint), MaxImageWidth));
            return frame;
        }

        private static VisualElement Placeholder(string message)
        {
            var box = new VisualElement();
            box.style.width = Length.Percent(100);
            box.style.maxWidth = MaxImageWidth;
            box.style.paddingTop = box.style.paddingBottom = 18;
            box.style.paddingLeft = box.style.paddingRight = 14;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.style.backgroundColor = new Color(0.14f, 0.14f, 0.15f, 1f);
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius =
                box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 6;
            box.style.borderTopWidth = box.style.borderBottomWidth =
                box.style.borderLeftWidth = box.style.borderRightWidth = 1;
            var dashed = new Color(1f, 1f, 1f, 0.14f);
            box.style.borderTopColor = box.style.borderBottomColor =
                box.style.borderLeftColor = box.style.borderRightColor = dashed;

            var label = new Label(message);
            label.AddToClassList("sasq-palettewin-hint");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            box.Add(label);
            return box;
        }

    }
}
