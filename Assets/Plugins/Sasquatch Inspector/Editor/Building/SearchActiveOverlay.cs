using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The unmistakable "a search filter is hiding fields right now" indicator: an accent frame drawn
    /// around the WHOLE body while a field search is active. It is an absolutely-positioned inset-0 child
    /// (<see cref="PickingMode.Ignore"/>), so it never affects layout, tracks scrolling for free, and dies
    /// with its host when the inspector rebuilds. Because it lives on the OUTERMOST styled element it is
    /// immune to whatever styling the user applies to the inner widgets — the frame survives.
    ///
    /// The discreet <c>n/m</c> count in the header is easy to miss; this frame makes "search is on"
    /// obvious. Shipped alongside — and driven by — <see cref="InspectorFieldSearch"/> (inspector) and the
    /// Layout Designer Properties search, which each call <see cref="Show"/>/<see cref="Hide"/> from the one
    /// spot that knows whether a query is actually filtering.
    ///
    /// Show/Hide are idempotent and safe to call every keystroke: re-showing an existing frame only refreshes
    /// its accent (so a skin flip re-tints it) and cancels any pending fade-out, so rapid clear→retype never
    /// strands or double-draws the frame.
    /// </summary>
    public static class SearchActiveOverlay
    {
        private const string FrameName = "sasq-search-active-frame";

        /// <summary>
        /// The other half of the shared "search is on" language: the magnifier reads as ENGAGED while its field
        /// is open, before any query is typed (the gold frame only shows up once something is actually being
        /// filtered). Every surface with a magnifier calls this from the one place that flips its open/closed
        /// chrome — inspector Script Header, designer Properties, designer Canvas.
        ///
        /// This sets only the GLYPH; each surface decides what "engaged" looks like on its own chrome, because
        /// the two idioms differ. The Script Header's buttons are faceless (they fade in on hover), so there the
        /// glyph itself turns accent blue. The designer's are filled chrome buttons, so there the BUTTON takes
        /// the accent fill and the glyph goes white on it — see PaintSearchToggle. What must never happen either
        /// way: accent appearing on a merely-HOVERED magnifier, which would announce a search that isn't open.
        /// </summary>
        /// <param name="toggle">The magnifier button (the icon is found beneath it).</param>
        /// <param name="open">Whether the search field is showing.</param>
        /// <param name="openTint">Glyph colour while open — accent on a faceless button, white on a filled one.</param>
        /// <param name="idleTint">The icon's resting tint — pass the same default it was created with.</param>
        public static void TintSearchToggle(VisualElement toggle, bool open, Color openTint, Color idleTint)
        {
            var icon = toggle?.Q<SasquatchIconElement>();
            if (icon != null) icon.Tint = open ? openTint : idleTint;
        }

        // Per-frame state so a fade-out scheduled by Hide can be cancelled by a subsequent Show (fast
        // clear→retype). Stored on the frame's userData — the frame is our own private element.
        private sealed class FrameState { public bool Removing; }

        /// <summary>Draws (or refreshes) the accent frame around <paramref name="host"/>. Idempotent.</summary>
        public static void Show(VisualElement host, Color accent)
        {
            // NO panel guard. Both inspector render paths build + re-apply the saved query while the body is
            // still DETACHED (VisualInspectorEditor.CreateInspectorGUI calls RebuildInspectorGUI before returning
            // the root; SasquatchForceTakeover.BuildBody calls Rebuild before returning the container), so a
            // panel!=null gate would silently drop the frame on exactly the rebuilds that restore a query —
            // undo, domain reload, reselect — leaving the count on with no frame. UIElements re-registers a
            // scheduled item when its element attaches, so the fade-in below still plays.
            if (host == null) return;

            var frame = FindFrame(host);
            if (frame != null)
            {
                // Already framed: cancel any in-flight fade-out, refresh the accent (skin may have flipped),
                // and make sure it's fully visible.
                ((FrameState)frame.userData).Removing = false;
                ApplyAccent(frame, accent);
                frame.style.opacity = 1f;
                return;
            }

            frame = MakeFrame(accent);
            // hierarchy.Add (not Add) so a ScrollView host frames its fixed VIEWPORT rather than dropping the
            // frame into the scrolled content container (where it would scroll away — and be wiped by Clear()).
            // For a plain container host this is identical to Add.
            host.hierarchy.Add(frame);
            // Opacity starts at 0; schedule the flip so the transition actually plays.
            frame.schedule.Execute(() => frame.style.opacity = 1f);
        }

        /// <summary>Fades out and removes the accent frame on <paramref name="host"/>, if any.</summary>
        public static void Hide(VisualElement host)
        {
            if (host == null) return;
            var frame = FindFrame(host);
            if (frame == null) return;

            var state = (FrameState)frame.userData;
            state.Removing = true;
            frame.style.opacity = 0f;
            // Remove only if a Show hasn't revived it in the meantime.
            frame.schedule.Execute(() =>
            {
                if (((FrameState)frame.userData).Removing) frame.RemoveFromHierarchy();
            }).ExecuteLater(200);
        }

        private static VisualElement FindFrame(VisualElement host)
        {
            // Added via host.hierarchy, so scan the hierarchy children (a ScrollView's own children, not its
            // scrolled content). A shallow scan avoids matching anything deeper.
            foreach (var child in host.hierarchy.Children())
                if (child.name == FrameName) return child;
            return null;
        }

        private static VisualElement MakeFrame(Color accent)
        {
            var frame = new VisualElement { name = FrameName, pickingMode = PickingMode.Ignore, userData = new FrameState() };
            frame.style.position = Position.Absolute;
            frame.style.left = 0;
            frame.style.top = 0;
            frame.style.right = 0;
            frame.style.bottom = 0;
            frame.style.borderLeftWidth = 2;
            frame.style.borderRightWidth = 2;
            frame.style.borderTopWidth = 2;
            frame.style.borderBottomWidth = 2;
            frame.style.borderTopLeftRadius = 4;
            frame.style.borderTopRightRadius = 4;
            frame.style.borderBottomLeftRadius = 4;
            frame.style.borderBottomRightRadius = 4;
            ApplyAccent(frame, accent);

            // 0.15s ease-out fade, matching the stylesheet's transition convention.
            frame.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "opacity" };
            frame.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
            frame.style.opacity = 0f;
            return frame;
        }

        private static void ApplyAccent(VisualElement frame, Color accent)
        {
            frame.style.borderLeftColor = accent;
            frame.style.borderRightColor = accent;
            frame.style.borderTopColor = accent;
            frame.style.borderBottomColor = accent;
            // A whisper-faint wash inside the frame so the whole body reads as "filtered", not just its edges.
            // Kept low (the frame draws ON TOP of the content) so it tints rather than washes out the fields —
            // the 2px border does the loud "search is on" work.
            //
            // The alpha is scaled by the accent's PERCEIVED luminance rather than fixed: a bright accent (brand
            // gold, ~0.79) veils the body far more than a mid one (accent blue, ~0.44) at the same alpha, so a
            // flat value that reads as a whisper in blue reads as a yellow film in gold. Scaling keeps the wash
            // equally faint whatever color drives the frame.
            float lum = 0.2126f * accent.r + 0.7152f * accent.g + 0.0722f * accent.b;
            float wash = Mathf.Lerp(0.065f, 0.035f, Mathf.Clamp01(lum));
            frame.style.backgroundColor = new Color(accent.r, accent.g, accent.b, wash);
        }
    }
}
