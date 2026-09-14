using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchSpring
{
    /// <summary>
    /// Spring extensions for UI Toolkit. Springs drive inline styles (opacity, scale,
    /// translate, height) and auto-cancel when the element leaves its panel. They work
    /// identically at edit time (editor windows, inspectors) and at runtime — the
    /// engine just needs a driver ticking it.
    /// </summary>
    public static class SpringVisualElementExtensions
    {
        // Fixed keys: one persistent handle per element per styled property. Falling
        // back to the setter-lambda key would leak a new handle on every call.
        private const string OpacityKey = "ve.opacity";
        private const string ScaleKey = "ve.scale";
        private const string TranslateKey = "ve.translate";
        private const string HeightKey = "ve.height";
        private const string RotateKey = "ve.rotate";

        private static readonly object s_hookSentinel = new object();
        private static readonly ConditionalWeakTable<VisualElement, object> s_detachHooked =
            new ConditionalWeakTable<VisualElement, object>();

        /// <summary>Springs the element's opacity toward <paramref name="target"/>.</summary>
        public static MotionHandleFloat SpringOpacity(this VisualElement ve, float target, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            return Spring.To((object)ve,
                () => GetOpacity(ve),
                v => ve.style.opacity = v,
                target, config, OpacityKey);
        }

        // The last stretch of a fade-IN, where the inline value is dropped instead of written. Far enough
        // inside the target (1/1000 of the whole range) that the swap can't be seen, and re-releasing on
        // every later tick is a harmless no-op.
        private const float OpacityReleaseThreshold = 0.999f;

        /// <summary>
        /// Fades the element UP to full and then hands <c>opacity</c> BACK to the stylesheet.
        /// <para>
        /// Use this for every fade-IN rather than <c>SpringOpacity(1f, …)</c>. A spring drives opacity as an
        /// INLINE style, and an inline style outranks every stylesheet rule on that element — permanently.
        /// Settling on a literal 1 therefore does not return the element to "normal": it PINS it there and
        /// leaves the element blind to USS opacity for the rest of its life. The rule that costs the most is
        /// the editor theme's <c>.unity-disabled { opacity: .5 }</c>, which is HOW a disabled control greys
        /// out — so a field that has faded in once can never look disabled again.
        /// </para>
        /// Releasing the inline value is also the honest end state when the stylesheet's own opacity ISN'T 1
        /// (a field disabled while it was still fading in lands on the wash, not on full strength).
        /// </summary>
        public static MotionHandleFloat SpringOpacityIn(this VisualElement ve, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            return Spring.To((object)ve,
                () => GetOpacity(ve),
                v =>
                {
                    if (v >= OpacityReleaseThreshold) ve.style.opacity = StyleKeyword.Null;
                    else ve.style.opacity = v;
                },
                1f, config, OpacityKey);
        }

        /// <summary>Uniform XY scale spring (Z stays 1).</summary>
        public static MotionHandleVector3 SpringScale(this VisualElement ve, float uniformTarget, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            return Spring.To((object)ve,
                () => GetScale(ve),
                v => ve.style.scale = new Scale(new Vector3(v.x, v.y, 1f)),
                new Vector3(uniformTarget, uniformTarget, 1f), config, ScaleKey);
        }

        /// <summary>
        /// Spring the element's <c>rotate</c> toward <paramref name="targetDegrees"/> (clockwise,
        /// degrees). Transform-only, so it never reflows siblings — used to spin disclosure carets
        /// 0°→90° on open. Center the transform origin first if you want it to pivot in place.
        /// </summary>
        public static MotionHandleFloat SpringRotate(this VisualElement ve, float targetDegrees, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            return Spring.To((object)ve,
                () => GetRotate(ve),
                v => ve.style.rotate = new Rotate(Angle.Degrees(v)),
                targetDegrees, config, RotateKey);
        }

        /// <summary>Pixel-space translate spring.</summary>
        public static MotionHandleVector3 SpringTranslate(this VisualElement ve, Vector2 target, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            return Spring.To((object)ve,
                () => GetTranslate(ve),
                v => ve.style.translate = new Translate(v.x, v.y),
                new Vector3(target.x, target.y, 0f), config, TranslateKey);
        }

        /// <summary>Explicit pixel height spring (callers restore height:auto on settle).</summary>
        public static MotionHandleFloat SpringHeight(this VisualElement ve, float targetPx, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            // Clamp: an underdamped spring closing toward 0 overshoots negative, and a
            // negative height is invalid — UI Toolkit falls back to AUTO, snapping the
            // element to its full natural height for that frame. The clamp turns the
            // underswing into a clean stop at the floor.
            return Spring.To((object)ve,
                () => GetHeight(ve),
                v => ve.style.height = Mathf.Max(0f, v),
                targetPx, config, HeightKey);
        }

        /// <summary>
        /// Scale impulse around the current target — a "pop"/"thunk" that wobbles and
        /// returns without disturbing an in-flight scale spring's destination.
        /// </summary>
        public static MotionHandleVector3 PunchScale(this VisualElement ve, float velocity, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            var handle = Spring.GetVector3Handle(ve, ScaleKey,
                () => GetScale(ve),
                v => ve.style.scale = new Scale(new Vector3(v.x, v.y, 1f)));
            handle.Punch(new Vector3(velocity, velocity, 0f), config);
            return handle;
        }

        /// <summary>Horizontal shake impulse — decays naturally around the rest position.</summary>
        public static MotionHandleVector3 PunchTranslateX(this VisualElement ve, float velocity, SpringConfig config)
        {
            EnsureDetachCleanup(ve);
            var handle = Spring.GetVector3Handle(ve, TranslateKey,
                () => GetTranslate(ve),
                v => ve.style.translate = new Translate(v.x, v.y));
            handle.Punch(new Vector3(velocity, 0f, 0f), config);
            return handle;
        }

        /// <summary>Cancels every spring owned by this element, leaving each value where it stood.</summary>
        public static void CancelSprings(this VisualElement ve)
        {
            Spring.CancelAll(ve);
        }

        /// <summary>Finishes every in-flight spring on this element at its target value (see <see cref="Spring.CompleteAll"/>).</summary>
        public static void CompleteSprings(this VisualElement ve)
        {
            Spring.CompleteAll(ve);
        }

        // Detached-element setters don't throw (they write invisibly forever), so panel
        // detach is the cleanup signal. The hook outlives the sweep — it stays valid if
        // the element is re-attached and springs again. Internal so SpringFold's custom
        // fraction spring gets the same cleanup as the keyed style springs here.
        //
        // FINISH, don't cancel. A DETACH IS NOT RELIABLY A TEARDOWN: dragging an Inspector tab re-parents the
        // whole visual tree, and CollectionRowReorder.BeginFloat lifts a live row out of its list and drops it
        // into an overlay host mid-drag. Cancelling stops the driver and leaves the value wherever it stood —
        // and these springs drive INLINE styles, so "wherever it stood" is pinned on the element by its own
        // inline style with nothing alive to drive it home. Measured before this was changed: a re-attached
        // element kept scale 0.85 forever (SpringPop), opacity 0 forever (SpringSlide — i.e. permanently
        // INVISIBLE), and a half-open SpringFold never finalized because WatchSettle swallows the cancel.
        // Completing releases the engine slots exactly as cancelling did, so the cleanup this hook exists for
        // is unchanged; the only difference is that the element lands where the motion was going. The export
        // kit's baked spring solver already does this, deliberately — the two now agree.
        internal static void EnsureDetachCleanup(VisualElement ve)
        {
            if (s_detachHooked.TryGetValue(ve, out _)) return;
            s_detachHooked.Add(ve, s_hookSentinel);
            ve.RegisterCallback<DetachFromPanelEvent>(_ => Spring.CompleteAll(ve));
        }

        // Getters prefer the last-written inline style over resolvedStyle: resolved
        // values lag a frame behind layout, and a stale read would trip the engine's
        // teleport check on every retarget (killing spring velocity mid-flight).

        private static float GetOpacity(VisualElement ve)
        {
            var st = ve.style.opacity;
            return st.keyword == StyleKeyword.Undefined ? st.value : ve.resolvedStyle.opacity;
        }

        private static Vector3 GetScale(VisualElement ve)
        {
            var st = ve.style.scale;
            return st.keyword == StyleKeyword.Undefined ? st.value.value : ve.resolvedStyle.scale.value;
        }

        private static Vector3 GetTranslate(VisualElement ve)
        {
            var st = ve.style.translate;
            if (st.keyword == StyleKeyword.Undefined)
            {
                var t = st.value;
                return new Vector3(t.x.value, t.y.value, t.z);
            }
            return ve.resolvedStyle.translate;
        }

        private static float GetRotate(VisualElement ve)
        {
            var st = ve.style.rotate;
            Angle a = st.keyword == StyleKeyword.Undefined ? st.value.angle : ve.resolvedStyle.rotate.angle;
            return ToDegrees(a);
        }

        // We always author caret angles in degrees, but resolvedStyle (read before the first inline
        // write) can report another unit — normalize so the spring never starts from a bogus value.
        private static float ToDegrees(Angle a)
        {
            switch (a.unit)
            {
                case AngleUnit.Gradian: return a.value * 0.9f;
                case AngleUnit.Radian: return a.value * Mathf.Rad2Deg;
                case AngleUnit.Turn: return a.value * 360f;
                default: return a.value; // Degree
            }
        }

        private static float GetHeight(VisualElement ve)
        {
            var st = ve.style.height;
            if (st.keyword == StyleKeyword.Undefined && st.value.unit == LengthUnit.Pixel)
            {
                return st.value.value;
            }
            float h = ve.resolvedStyle.height;
            return float.IsNaN(h) ? 0f : h;
        }
    }
}
