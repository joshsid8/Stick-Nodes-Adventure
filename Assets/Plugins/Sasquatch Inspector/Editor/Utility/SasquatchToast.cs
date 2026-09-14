using SasquatchSpring;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The transient "Copied" flourish: a small label that springs in over a control, holds for a beat, and
    /// fades out.
    ///
    /// <para><b>Why it exists at all.</b> A clipboard write has no visible effect anywhere on screen. Without
    /// an acknowledgement the control is indistinguishable from a dead button — which is the same defect the
    /// transport bar's third rule was written for, and it has now been fixed twice in this package by two
    /// different mechanisms. This is the one that reads best: it appears where the eye already is, says what
    /// happened, and takes itself away.</para>
    ///
    /// <para><b>Extracted so the copies cannot drift.</b> The colour palette grew this first and three other
    /// places want it; a second hand-rolled version would eventually differ in hold time or fade curve, and
    /// two "same" flourishes that behave differently read as a bug in whichever one you saw second.</para>
    ///
    /// <para>The USS class is still <c>sasq-swatch-toast</c> — historical, from where it was born. Renaming it
    /// would touch the stylesheet and three widgets for no behaviour change, so the name stays and this note
    /// stands in for it.</para>
    /// </summary>
    internal static class SasquatchToast
    {
        private const string ToastClass = "sasq-swatch-toast";

        /// <summary>How long the message holds before it starts to leave.</summary>
        private const long HoldMs = 950;

        /// <summary>How long the fade gets before the element is removed.</summary>
        private const long FadeMs = 240;

        /// <summary>
        /// Shows <paramref name="message"/> over <paramref name="host"/>. The host needs no preparation: the
        /// toast is absolutely positioned by its own class, anchored to the host's top-right.
        /// </summary>
        /// <param name="host">The element to float over. Must outlive the toast, or nothing is shown.</param>
        /// <param name="topOffset">Nudges the toast down, for hosts whose top-right corner is already busy.</param>
        internal static void Show(VisualElement host, string message, float topOffset = 0f)
        {
            if (host == null || string.IsNullOrEmpty(message)) return;

            // One at a time. Clicking twice in quick succession would otherwise stack two labels at the same
            // absolute position, where the older one fades out from under the newer and leaves it looking
            // half-transparent for no reason anyone could explain.
            host.Q<Label>(className: ToastClass)?.RemoveFromHierarchy();

            var toast = new Label(message);
            toast.AddToClassList(ToastClass);
            toast.pickingMode = PickingMode.Ignore;
            if (topOffset != 0f) toast.style.top = topOffset;
            host.Add(toast);

            if (EditorSpringFX.MotionEnabled) EditorSpringFX.SlideInY(toast, -6f);

            toast.schedule.Execute(() =>
            {
                if (EditorSpringFX.MotionEnabled)
                {
                    toast.SpringOpacity(0f, EditorSpringFX.Tune(SpringConfig.Snappy));
                    toast.schedule.Execute(() => toast.RemoveFromHierarchy()).ExecuteLater(FadeMs);
                }
                else
                {
                    // Motion off is a real accessibility setting, not a fallback: the message still has to
                    // appear and still has to leave, it just does both instantly.
                    toast.RemoveFromHierarchy();
                }
            }).ExecuteLater(HoldMs);
        }
    }
}
