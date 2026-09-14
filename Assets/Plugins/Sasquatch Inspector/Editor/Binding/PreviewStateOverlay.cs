using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The two "why is this panel blank?" overlays shared by the Asset Preview and Particle &amp; VFX Preview
    /// widgets.
    ///
    /// <para>Both widgets draw into a fixed-size box that shows nothing at all in THREE unrelated situations:
    /// no reference is assigned; a reference IS assigned but the user orbited/panned/zoomed it out of the
    /// viewport; or (particles) the effect is alive but has no particles at the current poster time. Those
    /// rendered identically — an empty dark square — so "I assigned it and it vanished" was indistinguishable
    /// from "I never assigned anything". These overlays make the three states look like different KINDS of
    /// thing rather than three shades of blank:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Empty</b> — a dashed frame + muted glyph + caption. Deliberately the language of a receptacle,
    /// which is honest: the box really is a drop target (see <see cref="ObjectSlotSurface"/>).</item>
    /// <item><b>Notice</b> — a pill floated over the LIVE viewport background, never the dashed empty look, so
    /// the two can never be confused. Clicking it runs the fix (recenter / play).</item>
    /// </list>
    ///
    /// <para>Colors come from <see cref="WidgetPalette"/> rather than USS because the standalone export kit
    /// carries a baked twin of both overlays and has no stylesheet — keeping the source of truth in C# is what
    /// keeps the twins honest. The class names are hooks for tests and user restyling, not required styling.</para>
    /// </summary>
    internal static class PreviewStateOverlay
    {
        /// <summary>Class on the empty-slot overlay root (absolute fill, never picks).</summary>
        internal const string EmptyClass = "sasq-preview-empty";

        /// <summary>Class on the notice pill (a Button, so it owns its own click).</summary>
        internal const string NoticeClass = "sasq-preview-notice";

        /// <summary>Below this box edge (px) there is no room for a caption under the glyph — glyph only, with
        /// the wording moved to the tooltip so the affordance is never simply lost on a small thumbnail.</summary>
        internal const float CaptionMinBox = 64f;

        /// <summary>Below this box edge (px) the notice pill drops its text and shows just its icon.</summary>
        internal const float NoticeTextMinBox = 88f;

        // ---- Shared wording. Kept here (not at the call sites) so the two widgets, the designer canvas and
        // the standalone export kit's baked twin can never drift into three different phrasings of the same
        // state — the exact way the render paths have gone out of sync before.
        internal const string EmptyAssetCaption = "Drop asset";
        internal const string EmptyEffectCaption = "Drop effect";
        // The designer canvas previews the user's real data but is deliberately NOT an object-drop surface
        // (that is live-only — see ObjectSlotSurface), so an invitation to drop there would be a promise the
        // canvas cannot keep. Same visual, honest wording.
        internal const string EmptyAssetCaptionDesign = "No asset";
        internal const string EmptyEffectCaptionDesign = "No effect";
        internal const string OffFrameText = "Off screen — click to recenter";
        internal const string NoParticlesText = "No particles at this time — press play";

        /// <summary>Tooltip for the box while nothing is assigned. The orbit instructions are a lie on an
        /// empty slot, so the caller swaps to this.</summary>
        internal const string EmptyAssetTooltip = "Nothing assigned — drop an asset here, or use the picker.";
        internal const string EmptyEffectTooltip = "Nothing assigned — drop a particle effect here, or use the picker.";

        /// <summary>
        /// The "nothing assigned" overlay: a dashed frame, a large muted glyph, and (when the box is big
        /// enough) a caption. Absolutely fills its parent and NEVER picks — the box underneath stays a live
        /// drop target and the object-picker selector on top stays clickable.
        /// </summary>
        /// <param name="iconId">A <see cref="SasquatchIcons.Ids"/> glyph describing what belongs here.</param>
        /// <param name="caption">Short instruction, e.g. "Drop asset". Also becomes the tooltip.</param>
        /// <param name="boxSize">The box's edge length in px — drives glyph scale and caption suppression.</param>
        internal static VisualElement BuildEmpty(string iconId, string caption, float boxSize)
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList(EmptyClass);
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;

            var frame = new DashedFrameElement { pickingMode = PickingMode.Ignore };
            frame.Tint = WidgetPalette.EmptyHintBorder;
            frame.style.position = Position.Absolute;
            frame.style.left = 0; frame.style.top = 0; frame.style.right = 0; frame.style.bottom = 0;
            root.Add(frame);

            // Scaled to the box so a 32px thumbnail and a 256px panel both look deliberate rather than
            // "a fixed icon floating in space".
            float glyph = Mathf.Clamp(Mathf.Round(boxSize * 0.28f), 10f, 44f);
            var icon = SasquatchIcons.Create(iconId, glyph, WidgetPalette.DropSlotText);
            icon.pickingMode = PickingMode.Ignore;
            icon.style.opacity = 0.55f; // the glyph is a hint, never a subject
            root.Add(icon);

            if (boxSize >= CaptionMinBox && !string.IsNullOrEmpty(caption))
            {
                var lbl = new Label(caption) { pickingMode = PickingMode.Ignore };
                lbl.style.fontSize = 10;
                lbl.style.marginTop = 4;
                lbl.style.color = WidgetPalette.DropSlotText;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                root.Add(lbl);
            }

            // No tooltip here: this root never picks, so it could not show one. The CALLER owns the box's
            // tooltip and swaps it with the state — the orbit instructions are a lie on an empty slot anyway.
            return root;
        }

        /// <summary>
        /// The "assigned, but you can't see it" pill — a soft dark capsule centred over the viewport with an
        /// icon, a short explanation, and a click that runs the fix. A <see cref="Button"/> so it owns its
        /// click; pointer-down and click are stopped here so pressing it can never ALSO start a camera drag or
        /// toggle particle playback (both of which live on the panel underneath).
        /// </summary>
        /// <param name="iconId">A <see cref="SasquatchIcons.Ids"/> glyph for the fix (reframe / play).</param>
        /// <param name="text">Short sentence, e.g. "Off screen — click to recenter". Always the tooltip too,
        /// so the meaning survives the small-box layout that drops the text.</param>
        /// <param name="boxSize">The box's edge length in px — below <see cref="NoticeTextMinBox"/> the pill
        /// shows its icon alone rather than crushing or clipping the sentence.</param>
        /// <param name="onClick">The fix. Runs on click.</param>
        /// <returns>A NON-picking absolute-fill host with the pill centred inside it. Show/hide the host; the
        /// host itself never swallows pointer events, so the viewport underneath keeps its full drag area
        /// while only the pill's own footprint is clickable.</returns>
        internal static VisualElement BuildNotice(string iconId, string text, float boxSize, Action onClick)
        {
            // Centre by LAYOUT, never by `left:50% + translate -50%`: a half-element transform lands the pill
            // on a fractional device pixel at 125%/150% display scaling and renders it permanently soft (the
            // same root cause as the stacked-hairline artifact). A flex-centring host has no such problem.
            var host = new VisualElement { pickingMode = PickingMode.Ignore };
            host.style.position = Position.Absolute;
            host.style.left = 0; host.style.top = 0; host.style.right = 0; host.style.bottom = 0;
            host.style.alignItems = Align.Center;
            host.style.justifyContent = Justify.Center;

            var pill = new Button(() => onClick?.Invoke()) { tooltip = text };
            pill.AddToClassList(NoticeClass);
            pill.focusable = false; // a transient overlay is never a tab stop nor a focus-delegation target
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.justifyContent = Justify.Center;
            pill.style.flexShrink = 1;
            pill.style.paddingLeft = 7; pill.style.paddingRight = 7;
            pill.style.paddingTop = 4; pill.style.paddingBottom = 4;
            pill.style.marginLeft = 0; pill.style.marginRight = 0;
            pill.style.marginTop = 0; pill.style.marginBottom = 0;
            pill.style.maxWidth = Length.Percent(92);
            pill.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f); // over rendered pixels: skin-neutral by design
            pill.style.borderTopWidth = 1; pill.style.borderBottomWidth = 1;
            pill.style.borderLeftWidth = 1; pill.style.borderRightWidth = 1;
            var edge = new Color(1f, 1f, 1f, 0.24f);
            pill.style.borderTopColor = edge; pill.style.borderBottomColor = edge;
            pill.style.borderLeftColor = edge; pill.style.borderRightColor = edge;
            pill.style.borderTopLeftRadius = 9; pill.style.borderTopRightRadius = 9;
            pill.style.borderBottomLeftRadius = 9; pill.style.borderBottomRightRadius = 9;

            var icon = SasquatchIcons.Create(iconId, 12, new Color(1f, 1f, 1f, 0.9f));
            icon.pickingMode = PickingMode.Ignore;
            icon.style.flexShrink = 0;
            pill.Add(icon);

            if (boxSize >= NoticeTextMinBox && !string.IsNullOrEmpty(text))
            {
                var lbl = new Label(text) { pickingMode = PickingMode.Ignore };
                lbl.style.marginLeft = 5;
                lbl.style.fontSize = 10;
                lbl.style.color = new Color(1f, 1f, 1f, 0.92f);
                lbl.style.whiteSpace = WhiteSpace.Normal;
                lbl.style.flexShrink = 1;
                pill.Add(lbl);
            }

            // The panel underneath owns camera drags (asset) and the play toggle (particle). Without BOTH of
            // these, pressing the pill would ALSO do that.
            pill.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            pill.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            EditorSpringFX.ButtonSpring(pill);

            host.Add(pill);
            return host;
        }

        /// <summary>
        /// A dashed rounded-rect outline drawn as ONE element via <see cref="Painter2D"/>. UI Toolkit has no
        /// dashed <c>border-style</c>, and building the dashes from child VisualElements would hit the same
        /// per-child device-rounding artifact that made stacked hairlines render at uneven thicknesses at
        /// 125%/150% display scaling — so this snaps every dash to the device-pixel grid itself, exactly like
        /// <see cref="GripGlyph"/>.
        /// </summary>
        internal sealed class DashedFrameElement : VisualElement
        {
            private Color _tint = new Color(0.4f, 0.4f, 0.4f, 0.5f);

            /// <summary>Dash color. The mesh bakes it, so a change needs a repaint.</summary>
            internal Color Tint
            {
                get => _tint;
                set { if (_tint == value) return; _tint = value; MarkDirtyRepaint(); }
            }

            internal DashedFrameElement()
            {
                generateVisualContent += OnGenerate;
            }

            private void OnGenerate(MeshGenerationContext mgc)
            {
                var rect = contentRect;
                if (rect.width < 4f || rect.height < 4f) return;

                var dashes = ComputeDashRects(rect, EditorGUIUtility.pixelsPerPoint);
                if (dashes.Length == 0) return;

                var painter = mgc.painter2D;
                painter.fillColor = _tint;
                painter.BeginPath();
                for (int i = 0; i < dashes.Length; i++)
                {
                    var r = dashes[i];
                    painter.MoveTo(new Vector2(r.xMin, r.yMin));
                    painter.LineTo(new Vector2(r.xMax, r.yMin));
                    painter.LineTo(new Vector2(r.xMax, r.yMax));
                    painter.LineTo(new Vector2(r.xMin, r.yMax));
                    painter.ClosePath();
                }
                painter.Fill();
            }

            /// <summary>Dash length in points.</summary>
            internal const float Dash = 4f;
            /// <summary>Gap between dashes in points.</summary>
            internal const float Gap = 3f;
            /// <summary>How far each edge's dash run stops short of the corner, faking a rounded frame without
            /// dashing along an arc.</summary>
            internal const float CornerInset = 5f;

            /// <summary>
            /// Every dash rect in local points, each edge on the device-pixel grid. The run on each side is
            /// CENTRED in the space between the two corner insets, so opposite edges stay symmetric at any
            /// size instead of accumulating the remainder on one end. Internal (not private) because the
            /// verification tests live in another assembly and this is the unit-testable half.
            /// </summary>
            internal static Rect[] ComputeDashRects(Rect contentRect, float ppp)
            {
                // Mathf.Round is banker's rounding, which picks inconsistent sides at successive .5 products.
                float RoundHalfUp(float v) => Mathf.Floor(v + 0.5f);

                ppp = Mathf.Max(0.5f, ppp);
                float th = Mathf.Max(1f, RoundHalfUp(1f * ppp));       // stroke thickness, whole device px
                float dash = Mathf.Max(th, RoundHalfUp(Dash * ppp));
                float gap = Mathf.Max(1f, RoundHalfUp(Gap * ppp));
                float inset = Mathf.Max(0f, RoundHalfUp(CornerInset * ppp));

                float left = RoundHalfUp(contentRect.x * ppp);
                float top = RoundHalfUp(contentRect.y * ppp);
                float w = Mathf.Max(1f, RoundHalfUp(contentRect.width * ppp));
                float h = Mathf.Max(1f, RoundHalfUp(contentRect.height * ppp));

                var list = new System.Collections.Generic.List<Rect>();

                // Walk one edge, emitting centred dashes along `runLength`. `horizontal` swaps which axis the
                // dash extends along; `fixedPos` is the other axis' (already snapped) coordinate.
                void Run(float runStart, float runLength, float fixedPos, bool horizontal)
                {
                    if (runLength < dash) return;
                    int n = Mathf.Max(1, Mathf.FloorToInt((runLength + gap) / (dash + gap)));
                    float used = n * dash + (n - 1) * gap;
                    float offset = RoundHalfUp((runLength - used) * 0.5f);
                    for (int i = 0; i < n; i++)
                    {
                        float p = runStart + offset + i * (dash + gap);
                        list.Add(horizontal
                            ? new Rect(p / ppp, fixedPos / ppp, dash / ppp, th / ppp)
                            : new Rect(fixedPos / ppp, p / ppp, th / ppp, dash / ppp));
                    }
                }

                float hRun = w - 2f * inset;
                float vRun = h - 2f * inset;
                Run(left + inset, hRun, top, horizontal: true);            // top
                Run(left + inset, hRun, top + h - th, horizontal: true);   // bottom
                Run(top + inset, vRun, left, horizontal: false);           // left
                Run(top + inset, vRun, left + w - th, horizontal: false);  // right

                return list.ToArray();
            }
        }
    }
}
