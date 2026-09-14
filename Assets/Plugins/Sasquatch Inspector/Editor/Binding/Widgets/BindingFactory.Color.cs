using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // A plain Color field authored through Unity's own ColorField, but with the hdr / showAlpha flags driven by
        // the node instead of a [ColorUsage] attribute. This is used ONLY when the node diverges from the stock look
        // (HDR mode, or alpha hidden) — an untouched Color field still renders through PropertyField (see the Default
        // case in CreateBindingField), which keeps honoring any real [ColorUsage] attribute for free. The visual is
        // the identical Unity color swatch either way; HDR merely swaps the picker for the intensity-aware one and
        // stamps a small "HDR" badge on the swatch (mirroring Unity's own field, whose UITK version omits it).
        internal static VisualElement CreateColorField(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            bool hdr = node.ColorFieldMode == ColorFieldMode.HDR;
            var field = new ColorField(resolvedLabel)
            {
                showAlpha = node.ColorShowAlpha,
                hdr = hdr,
            };
            // Bind through the SerializedProperty so multi-object mixed values, undo, and prefab overrides all
            // behave exactly like the default field.
            SeedThenBind(field, prop); // seed BEFORE bind — see SeedThenBind

            // Stamp the "HDR" badge over the swatch when in HDR mode — Unity's IMGUI ColorField shows this, but the
            // UI Toolkit ColorField's own HDR label never renders in the inspector, so we hide it and add our own.
            if (hdr) ApplyColorFieldHdrBadge(field);
            return field;
        }

        // Hides a UITK ColorField's built-in (non-rendering) HDR label and stamps our own badge centered on the color
        // swatch (not the eyedropper), so the "HDR" cue is present and consistent with the gradient + palette corner.
        // Shared by the plain Color field and the Color Palette's "current color" bar.
        internal static void ApplyColorFieldHdrBadge(ColorField field)
        {
            var builtIn = field.Q(className: ColorField.hdrLabelUssClassName);
            if (builtIn != null) builtIn.style.display = DisplayStyle.None;
            // Center on the color swatch itself (the color container excludes the eyedropper); fall back gracefully.
            var host = field.Q(className: ColorField.colorContainerUssClassName)
                       ?? field.Q(className: ColorField.inputUssClassName)
                       ?? (VisualElement)field;
            AttachHdrBadgeCentered(host);
        }

        // Overlays the "HDR" badge on a host, centered on both axes (absolute + a -50%/-50% self-translate). Used for
        // the color swatch / color bar and the palette's continuous bar. Returns the badge for further tweaks.
        internal static Label AttachHdrBadgeCentered(VisualElement host)
        {
            var badge = CreateHdrBadge();
            badge.style.position = Position.Absolute;
            badge.style.left = Length.Percent(50);
            badge.style.top = Length.Percent(50);
            badge.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            host.Add(badge);
            return badge;
        }

        // The bare "HDR" badge element — a white bold tag on a translucent dark pill (reads over any color, including
        // a bright >1 HDR swatch), classed for tests + styling and click-through so it never blocks the control under
        // it. Unpositioned: callers place it (centered overlay, or in-flow beside another element).
        internal static Label CreateHdrBadge()
        {
            var badge = new Label("HDR") { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("sasq-hdr-badge");
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.color = Color.white;
            badge.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            badge.style.paddingLeft = 3;
            badge.style.paddingRight = 3;
            badge.style.paddingTop = 0;
            badge.style.paddingBottom = 0;
            badge.style.borderTopLeftRadius = 2;
            badge.style.borderTopRightRadius = 2;
            badge.style.borderBottomLeftRadius = 2;
            badge.style.borderBottomRightRadius = 2;
            return badge;
        }
    }
}
