using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared helpers for the "mixed value" state shown when MULTIPLE objects are selected and a bound field
    /// differs across them. Unity's standard fields already show their native "—" dash; our custom widgets read
    /// the raw <see cref="SerializedProperty"/> value directly, so without this they'd paint one object's value
    /// as if it were authoritative. Each custom widget calls <see cref="IsMixed"/> inside its existing refresh
    /// closure and renders a muted / em-dash state instead; the muted color lives in
    /// <see cref="WidgetPalette.Mixed"/> (skin-aware).
    /// </summary>
    public static class MixedValueUI
    {
        /// <summary>The em-dash placeholder shown where a single value can't be (matches Unity's mixed dash).</summary>
        public const string Dash = "—";

        /// <summary>USS class for an italic, muted em-dash placeholder on a text readout.</summary>
        public const string MixedTextClass = "sasq-mixed-text";

        /// <summary>
        /// True when several objects are being edited at once AND this property's value differs across them.
        /// Teardown-safe: a disposed SerializedObject (mid inspector rebuild) returns false rather than throwing.
        /// </summary>
        public static bool IsMixed(SerializedProperty prop)
        {
            if (prop == null) return false;
            try
            {
                return prop.serializedObject != null
                       && prop.serializedObject.isEditingMultipleObjects
                       && prop.hasMultipleDifferentValues;
            }
            catch
            {
                return false; // SerializedObject disposed mid-teardown
            }
        }

        /// <summary>
        /// Sets a text readout to either the real value or a muted em-dash placeholder, toggling
        /// <see cref="MixedTextClass"/> so the styling tracks the state both ways. One-liner for the
        /// text-style widgets (dropdowns, min/max labels, scene).
        /// </summary>
        public static void SetText(TextElement label, bool mixed, string realText)
        {
            if (label == null) return;
            if (mixed)
            {
                label.AddToClassList(MixedTextClass);
                label.text = Dash;
            }
            else
            {
                label.RemoveFromClassList(MixedTextClass);
                label.text = realText;
            }
        }

        /// <summary>
        /// Ghosts a graphical control (curve / gradient / spring graph) on multi-object mixed: dims
        /// <paramref name="control"/> and floats a centered muted em-dash over it, so no single graph is shown as
        /// authoritative. The overlay is added to <paramref name="host"/> (the control's containing well) and
        /// re-evaluates via <c>TrackPropertyValueWithRefresh</c>. Stays interactive (overlay ignores picking), so the first
        /// edit resolves the mix. No-op when not multi-editing.
        /// </summary>
        public static void AttachGraphGhost(VisualElement host, VisualElement control, SerializedProperty prop)
        {
            if (host == null || control == null || prop == null) return;

            var overlay = new Label(Dash);
            overlay.AddToClassList(MixedTextClass);
            overlay.pickingMode = PickingMode.Ignore;
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.unityTextAlign = TextAnchor.MiddleCenter;
            overlay.style.fontSize = 18;
            overlay.style.display = DisplayStyle.None;
            host.Add(overlay);

            void Eval()
            {
                bool mixed = IsMixed(prop);
                overlay.style.display = mixed ? DisplayStyle.Flex : DisplayStyle.None;
                control.style.opacity = mixed ? 0.25f : 1f;
            }
            Eval();
            host.TrackPropertyValueWithRefresh(prop, _ => Eval());
        }
    }
}
