using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchSpring
{
    /// <summary>
    /// Springs a content element between display:none and its natural auto height —
    /// foldouts and collapsible groups that physically "bounce open". Re-entrant safe:
    /// hammering the header simply retargets the live fraction spring, so velocity
    /// carries through direction changes.
    ///
    /// The spring drives an open FRACTION (0..1, overshoot above 1 gives the bounce),
    /// and the fraction scales the element's height AND its vertical paddings/margins
    /// together. Driving height alone stalls visibly near the end of a collapse: the
    /// layout engine floors a box's height at padding+border, so the last ~10-15px
    /// would freeze ("pause") until display:none snapped them away.
    /// </summary>
    public static class SpringFold
    {
        private const string FoldKey = "sasq.fold";

        private static readonly ConditionalWeakTable<VisualElement, FoldState> s_states =
            new ConditionalWeakTable<VisualElement, FoldState>();

        private sealed class FoldState
        {
            public bool TargetExpanded;
            public bool Animating;
            public bool Measuring;        // true during BeginExpand's deferred measure (no spring yet)
            public bool Initialized;      // SettledExpanded is meaningful only once this is true
            public bool SettledExpanded;  // the last FULLY settled state — the idle early-return trusts
                                          // this, never the live display (which Unity's Foldout or a
                                          // just-finished toggle can leave mid-transition)
            public int Generation;        // voids stale settle callbacks (the fraction spring)
            public int MeasureGen;        // voids a stale deferred measure independently of Generation,
                                          // so a same-direction re-toggle can't strand a pending measure
            public float Fraction = 1f;   // current open fraction (spring value; can overshoot > 1)

            // Vertical box metrics captured when an animation starts (px, resolved).
            public float FullHeight;
            public float PadTopPx, PadBottomPx, MarginTopPx, MarginBottomPx;

            // Original INLINE values, restored verbatim on settle so the element's own
            // styling (inline or USS-driven) is never polluted by the animation.
            public bool BoxSaved;
            public StyleLength SavedPadTop, SavedPadBottom, SavedMarginTop, SavedMarginBottom;
            public bool OverflowSaved;
            public StyleEnum<Overflow> SavedOverflow;
        }

        /// <summary>Springs <paramref name="content"/> open or closed (animating height + vertical padding/margin together); re-entrant-safe, and invokes <paramref name="onSettled"/> when it settles.</summary>
        public static void SetExpanded(VisualElement content, bool expanded, SpringConfig config, Action<bool> onSettled = null)
        {
            if (content == null) return;

            var state = s_states.GetValue(content, _ => new FoldState());
            state.TargetExpanded = expanded;
            int gen = ++state.Generation;

            if (state.Animating)
            {
                if (state.Measuring)
                {
                    // An expand is still measuring (display shown but no spring/height yet).
                    if (expanded)
                        // Same direction — the pending measure already targets expanded; let it
                        // finish. Capture validates against MeasureGen and starts the spring on the
                        // CURRENT Generation, so this gen bump can't strand it.
                        return;

                    // Reversed to collapse before the expand became visible: snap back to hidden
                    // with no animation (nothing was on screen yet) and abandon the measure.
                    state.MeasureGen++;
                    state.Measuring = false;
                    EndMeasureStyles(content);
                    content.style.display = DisplayStyle.None;
                    content.style.height = StyleKeyword.Auto;
                    RestoreVerticalBox(content, state);
                    RestoreOverflow(content, state);
                    state.Animating = false;
                    MarkSettled(state, false);
                    onSettled?.Invoke(false);
                    return;
                }

                // A live fraction spring exists — retarget it (velocity carries through).
                StartSpring(content, state, gen, config, onSettled, expanded ? 1f : 0f);
                return;
            }

            // Idle. Skip only when we genuinely settled in the requested state already — tracked,
            // NOT read from the live display. A display written by Unity's Foldout or a just-
            // finished toggle can sit mid-transition and would otherwise trick us into dropping a
            // real flip (the content then lags the caret by one). Self-heal the display in case it
            // drifted, then return.
            if (state.Initialized && state.SettledExpanded == expanded)
            {
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }

            if (expanded) BeginExpand(content, state, gen, config, onSettled);
            else BeginCollapse(content, state, gen, config, onSettled);
        }

        private static void BeginExpand(VisualElement content, FoldState state, int gen, SpringConfig config, Action<bool> onSettled)
        {
            state.Animating = true;
            state.Measuring = true;
            int measureGen = ++state.MeasureGen;
            SaveOverflow(content, state);

            // Measure the natural height invisibly. position:Absolute keeps siblings
            // from shifting during the measure frame; left+right pin the width to the
            // parent's content box — without them an absolute element shrink-wraps and
            // wrapped text measures the WRONG height.
            content.style.display = DisplayStyle.Flex;
            content.style.visibility = Visibility.Hidden;
            content.style.position = Position.Absolute;
            content.style.left = 0f;
            content.style.right = 0f;
            content.style.height = StyleKeyword.Auto;

            bool captured = false;
            EventCallback<GeometryChangedEvent> geomCb = null;

            void Capture(int retriesLeft)
            {
                // Superseded by a newer transition (a reverse-to-collapse or a fresh expand bumps
                // MeasureGen): drop, and unregister our measure callback or it fires forever.
                if (measureGen != state.MeasureGen) { if (geomCb != null) content.UnregisterCallback(geomCb); return; }
                if (captured) return;

                float h = content.resolvedStyle.height;
                if ((float.IsNaN(h) || h <= 0f) && retriesLeft > 0)
                {
                    content.schedule.Execute(() => Capture(retriesLeft - 1)).ExecuteLater(0);
                    return;
                }
                captured = true;
                if (geomCb != null) content.UnregisterCallback(geomCb);
                state.Measuring = false;
                EndMeasureStyles(content);

                if (float.IsNaN(h) || h <= 0f)
                {
                    // Measurement failed (empty content?) — open instantly, no animation.
                    content.style.height = StyleKeyword.Auto;
                    RestoreOverflow(content, state);
                    state.Animating = false;
                    MarkSettled(state, true);
                    onSettled?.Invoke(true);
                    return;
                }

                CaptureVerticalBox(content, state, h);
                content.style.overflow = Overflow.Hidden; // clips bouncy overshoot
                state.Fraction = 0f;
                ApplyFraction(content, state, 0f);
                // Spring/settle on the CURRENT Generation: same-direction re-toggles during the
                // measure bump Generation without restarting the measure, so the spring (and the
                // finalize gate in WatchSettle) must own the latest one.
                StartSpring(content, state, state.Generation, config, onSettled, 1f);
            }

            // Dual capture: GeometryChangedEvent on freshly re-displayed elements is
            // unreliable (Unity 6.3), so a next-frame schedule is the guaranteed path.
            geomCb = _ => Capture(1);
            content.RegisterCallback(geomCb);
            content.schedule.Execute(() => Capture(1)).ExecuteLater(0);
        }

        private static void BeginCollapse(VisualElement content, FoldState state, int gen, SpringConfig config, Action<bool> onSettled)
        {
            state.Animating = true;
            SaveOverflow(content, state);

            // Freeze the current height to explicit pixels first — the spring can't
            // start from "auto".
            float h = content.resolvedStyle.height;
            if (float.IsNaN(h) || h <= 0f)
            {
                // Nothing visible to animate; just hide.
                content.style.display = DisplayStyle.None;
                content.style.height = StyleKeyword.Auto;
                RestoreOverflow(content, state);
                state.Animating = false;
                MarkSettled(state, false);
                onSettled?.Invoke(false);
                return;
            }

            CaptureVerticalBox(content, state, h);
            content.style.overflow = Overflow.Hidden;
            state.Fraction = 1f;
            ApplyFraction(content, state, 1f);
            StartSpring(content, state, gen, config, onSettled, 0f);
        }

        private static void StartSpring(VisualElement content, FoldState state, int gen, SpringConfig config, Action<bool> onSettled, float targetFraction)
        {
            // Closing is critically damped (no overshoot): a bouncy collapse undershoots past 0
            // (clamped) then springs back to a small positive fraction, flashing the top of the
            // content into view for a blink. Opening keeps its bounce. (SpringConfig is a struct,
            // so this only affects this spring.) Critical damping also settles ~4-5x slower than its
            // nominal duration (the frequency formula divides by damping, plus the long exponential
            // tail), so override to a short duration to land the close near ~0.35s instead of ~2s.
            if (targetFraction <= 0f)
            {
                config.Bounce = 0f;
                config.Duration = 0.08f;
            }

            // The spring runs in FRACTION space, so a pixel-tuned SettleEpsilon from the
            // caller's config would be wildly wrong (0.5 of a fraction = half the fold).
            // Convert: settle once the remaining motion is under ~half a pixel.
            config.SettleEpsilon = Mathf.Clamp(0.5f / Mathf.Max(1f, state.FullHeight), 0.0005f, 0.05f);

            SpringVisualElementExtensions.EnsureDetachCleanup(content);
            var handle = Spring.To((object)content,
                () => state.Fraction,
                f => { state.Fraction = f; ApplyFraction(content, state, f); },
                targetFraction, config, FoldKey);
            WatchSettle(content, state, gen, handle, onSettled);
        }

        private static void ApplyFraction(VisualElement content, FoldState state, float fraction)
        {
            // Lower clamp only: negative heights/paddings are invalid styling (height
            // would even snap to AUTO), but overshoot ABOVE the natural size is the
            // expand bounce and must pass through.
            float f = Mathf.Max(0f, fraction);
            content.style.height = state.FullHeight * f;
            content.style.paddingTop = state.PadTopPx * f;
            content.style.paddingBottom = state.PadBottomPx * f;
            content.style.marginTop = state.MarginTopPx * f;
            content.style.marginBottom = state.MarginBottomPx * f;
        }

        private static void CaptureVerticalBox(VisualElement content, FoldState state, float fullHeight)
        {
            state.FullHeight = fullHeight;
            state.PadTopPx = ZeroIfNaN(content.resolvedStyle.paddingTop);
            state.PadBottomPx = ZeroIfNaN(content.resolvedStyle.paddingBottom);
            state.MarginTopPx = ZeroIfNaN(content.resolvedStyle.marginTop);
            state.MarginBottomPx = ZeroIfNaN(content.resolvedStyle.marginBottom);

            if (!state.BoxSaved)
            {
                state.SavedPadTop = content.style.paddingTop;
                state.SavedPadBottom = content.style.paddingBottom;
                state.SavedMarginTop = content.style.marginTop;
                state.SavedMarginBottom = content.style.marginBottom;
                state.BoxSaved = true;
            }
        }

        private static void RestoreVerticalBox(VisualElement content, FoldState state)
        {
            if (!state.BoxSaved) return;
            content.style.paddingTop = state.SavedPadTop;
            content.style.paddingBottom = state.SavedPadBottom;
            content.style.marginTop = state.SavedMarginTop;
            content.style.marginBottom = state.SavedMarginBottom;
            state.BoxSaved = false;
        }

        private static float ZeroIfNaN(float v) => float.IsNaN(v) ? 0f : v;

        private static async void WatchSettle(VisualElement content, FoldState state, int gen, MotionHandleFloat handle, Action<bool> onSettled)
        {
            try
            {
                await handle.WaitFor(AwaitStrategy.PhysicalSettling);
            }
            catch (TaskCanceledException)
            {
                return; // detached or shut down mid-flight
            }

            if (gen != state.Generation) return; // a newer toggle owns the finish

            if (state.TargetExpanded)
            {
                // Ensure visible (a native-Foldout flip may have left display:None) and hand layout
                // back to flexbox so dynamic content can reflow.
                content.style.display = DisplayStyle.Flex;
                content.style.height = StyleKeyword.Auto;
            }
            else
            {
                content.style.display = DisplayStyle.None;
                content.style.height = StyleKeyword.Auto; // fresh measure next expand
            }

            RestoreVerticalBox(content, state);
            RestoreOverflow(content, state);
            state.Animating = false;
            MarkSettled(state, state.TargetExpanded);
            onSettled?.Invoke(state.TargetExpanded);
        }

        private static void MarkSettled(FoldState state, bool expanded)
        {
            state.Initialized = true;
            state.SettledExpanded = expanded;
        }

        // Reverts the invisible-measure styling applied by BeginExpand so the element flows
        // normally again (relative, visible, no left/right pin).
        private static void EndMeasureStyles(VisualElement content)
        {
            content.style.position = Position.Relative;
            content.style.left = StyleKeyword.Null;
            content.style.right = StyleKeyword.Null;
            content.style.visibility = Visibility.Visible;
        }

        private static void SaveOverflow(VisualElement content, FoldState state)
        {
            if (state.OverflowSaved) return;
            state.SavedOverflow = content.style.overflow;
            state.OverflowSaved = true;
        }

        private static void RestoreOverflow(VisualElement content, FoldState state)
        {
            if (!state.OverflowSaved) return;
            content.style.overflow = state.SavedOverflow;
            state.OverflowSaved = false;
        }
    }
}
