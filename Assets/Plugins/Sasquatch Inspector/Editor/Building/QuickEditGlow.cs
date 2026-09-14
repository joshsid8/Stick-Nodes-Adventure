using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The accent outline drawn around the node being quick-edited, so the user always sees exactly
    /// what the popup targets — including headerless containers that are otherwise invisible. The
    /// glow is an absolutely-positioned inset-0 child of the target element (pickingMode Ignore), so
    /// it never affects layout, tracks scrolling for free, and dies gracefully with its host when the
    /// inspector rebuilds. Only one glow exists at a time.
    /// </summary>
    public static class QuickEditGlow
    {
        private static VisualElement _current;

        private static readonly Color BorderColor = new Color(0.35f, 0.55f, 0.95f, 0.9f);
        private static readonly Color FillColor = new Color(0.18f, 0.35f, 0.58f, 0.12f);

        /// <summary>Draws the accent glow outline around <paramref name="host"/> (replacing any existing glow).</summary>
        public static void Show(VisualElement host)
        {
            Hide();
            if (host == null || host.panel == null) return;

            var glow = MakeOverlay();
            host.Add(glow);
            glow.schedule.Execute(() => glow.style.opacity = 1f);

            _current = glow;
        }

        /// <summary>
        /// A one-shot attention flash on an element — fade in, hold briefly, fade out, self-remove.
        /// Used by the designer's "Open in Designer" deep link to ping the node the user came from.
        /// Independent of the persistent quick-edit glow (the two can coexist).
        /// </summary>
        public static void Ping(VisualElement host, int holdMs = 900)
        {
            if (host == null || host.panel == null) return;

            var glow = MakeOverlay();
            host.Add(glow);
            glow.schedule.Execute(() =>
            {
                glow.style.opacity = 1f;
                // A springy scale ping on the HOST alongside the flash — it pops in slightly
                // enlarged and settles back to rest. "Over here!" with a physical nudge.
                EditorSpringFX.PingScale(host);
            });
            glow.schedule.Execute(() =>
            {
                glow.style.opacity = 0f;
                glow.schedule.Execute(() => glow.RemoveFromHierarchy()).ExecuteLater(200);
            }).ExecuteLater(holdMs);
        }

        private static VisualElement MakeOverlay()
        {
            var glow = new VisualElement { name = "sasq-quick-edit-glow", pickingMode = PickingMode.Ignore };
            glow.style.position = Position.Absolute;
            glow.style.left = 0;
            glow.style.top = 0;
            glow.style.right = 0;
            glow.style.bottom = 0;
            glow.style.borderLeftWidth = 2;
            glow.style.borderRightWidth = 2;
            glow.style.borderTopWidth = 2;
            glow.style.borderBottomWidth = 2;
            glow.style.borderLeftColor = BorderColor;
            glow.style.borderRightColor = BorderColor;
            glow.style.borderTopColor = BorderColor;
            glow.style.borderBottomColor = BorderColor;
            glow.style.borderTopLeftRadius = 4;
            glow.style.borderTopRightRadius = 4;
            glow.style.borderBottomLeftRadius = 4;
            glow.style.borderBottomRightRadius = 4;
            glow.style.backgroundColor = FillColor;

            // 0.15s ease-out fade, matching the stylesheet's transition convention. Opacity starts at 0;
            // callers schedule the fade-in so the transition actually plays.
            glow.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            glow.style.transitionDuration = new List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
            glow.style.opacity = 0f;
            return glow;
        }

        /// <summary>
        /// Re-targets the glow after an inspector rebuild destroyed its host: finds the (new) element
        /// carrying <paramref name="node"/> as userData under <paramref name="stableRoot"/> and re-shows.
        /// Drops silently when the node no longer renders (hidden by a condition, collapsed tab, …).
        /// </summary>
        public static void Retarget(VisualElement stableRoot, LayoutNode node)
        {
            Hide();
            if (stableRoot == null || node == null) return;

            var host = stableRoot.Query<VisualElement>()
                                 .Where(e => ReferenceEquals(e.userData, node))
                                 .First();
            if (host != null) Show(host);
        }

        /// <summary>Removes the current glow, if any.</summary>
        public static void Hide()
        {
            var glow = _current;
            _current = null;
            if (glow == null) return;

            // Fade out, then remove. If the host was already destroyed with a rebuild, RemoveFromHierarchy
            // on a detached element is a harmless no-op.
            glow.style.opacity = 0f;
            glow.schedule.Execute(() => glow.RemoveFromHierarchy()).ExecuteLater(160);
        }
    }
}
