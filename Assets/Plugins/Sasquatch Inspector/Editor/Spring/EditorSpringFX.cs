using System;
using System.Collections.Generic;
using SasquatchSpring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// One-line spring effects for Sasquatch Inspector's own editor chrome (designer canvas,
    /// popups, banners). Every effect respects the "reduce motion" preference and
    /// degrades to an instant state change when motion is disabled.
    /// </summary>
    public static class EditorSpringFX
    {
        private const string MotionPrefKey = "SasquatchInspector.MotionEnabled";

        // PROCESS-LOCAL SUPPRESSION. Null = follow the user's saved preference; non-null = a run is currently
        // forcing motion. It lives in a plain static ON PURPOSE, so it dies with the domain and CANNOT outlive
        // the run that set it.
        //
        // This is the whole point of the split below, and it is a scar. The saved preference lives in
        // EditorPrefs, which is MACHINE-WIDE — one store shared by every Unity editor on the box, across every
        // project and every Unity version. Deterministic runs turn motion off for their duration
        // (DocScreenshotCapture, and several test fixtures pin it off in OneTimeSetUp so springs settle
        // instantly and frames reproduce) and restore it in teardown. But a run that is CANCELLED, throws, or
        // domain-reloads mid-flight never reaches teardown — and with the old code that left a persisted,
        // machine-wide `false` behind. Every Sasquatch spring, in every project, silently did nothing until
        // somebody noticed and flipped it back. It cost a session to find, because the symptom is "instant"
        // rather than "broken" and the console is clean.
        //
        // So the ORDINARY setter no longer persists. A dead run can stall this session at worst; the next
        // domain reload always returns the editor to whatever the user actually chose. Only
        // <see cref="UserMotionPreference"/> writes the durable value, and only the settings toggle uses it.
        private static bool? s_motionOverride;

        /// <summary>
        /// Should a chrome effect animate right now? The user's saved preference (or a run's temporary
        /// override), AND the spring clock actually being able to advance — see <see cref="SpringClockFrozen"/>,
        /// which forces this false while play mode is paused so folds and pop-ins snap instead of hanging
        /// half-drawn.
        /// <para>ASSIGNING IS TEMPORARY AND PROCESS-LOCAL — it does NOT change the user's saved setting. That is
        /// what tests and capture runs want: <c>var was = MotionPreferenceNow; MotionEnabled = false; … ;
        /// MotionEnabled = was;</c> is safe even if the restore never runs. Save <see cref="MotionPreferenceNow"/>,
        /// not this, or a restore taken during a pause would pin motion off. To change what the USER chose,
        /// write <see cref="UserMotionPreference"/>.</para>
        /// </summary>
        public static bool MotionEnabled
        {
            get { return MotionPreferenceNow && !SpringClockFrozen; }
            // ALWAYS pin the assigned value — never "clear the override when it matches the saved preference".
            // That shortcut looks tidier and quietly breaks nesting: the fixture-wide OneTimeSetUp suppression
            // and ~9 individual tests that switch motion back ON both write through here, and an equality-clear
            // let the inner `= true` wipe the OUTER suppression instead of stacking under it. Three
            // "premise: motion is off for the fixture" tests went order-dependent on exactly that. This is the
            // pre-existing persisted semantics, unchanged — only the storage moved out of EditorPrefs.
            set { s_motionOverride = value; }
        }

        /// <summary>
        /// The motion preference ALONE — the saved value, or a run's temporary override — with no
        /// clock-freeze gate. This is what a save/restore should capture and what the settings UI shows:
        /// <see cref="MotionEnabled"/> can read false purely because play mode is paused, and storing
        /// THAT back as "the preference" would pin motion off for the rest of the session.
        /// </summary>
        internal static bool MotionPreferenceNow => s_motionOverride ?? EditorPrefs.GetBool(MotionPrefKey, true);

        // TEST SEAM. Null = ask the editor. Process-local and never persisted, exactly like
        // s_motionOverride above, so a test that dies before its teardown cannot outlive the domain.
        internal static Func<bool> ClockFrozenProbe;

        /// <summary>
        /// Is the spring clock STOPPED right now? True only while play mode is PAUSED.
        /// <para>Neither driver ticks then: SpringEditorDriver stands down for the whole of play mode, and
        /// SpringRuntimeDriver.Update does not run while the editor is paused — that is what pause means.
        /// A spring started in that window therefore never advances, and because most chrome effects set
        /// their START pose first and let the spring carry it to the end pose (SpringFold writes height 0,
        /// PopIn writes opacity 0), the element is left invisible indefinitely. The Live Debug settings
        /// card's Legend and Keyboard expanders are the visible case: click while paused, nothing opens.</para>
        /// <para>So <see cref="MotionEnabled"/> reads false while the clock is frozen and every chrome call
        /// site takes the reduced-motion branch it already carries — the fold snaps open instead of
        /// springing. Deliberately NOT solved by ticking the engine here instead: that engine is shared
        /// with the user's own runtime springs, and advancing it would move scene objects through the
        /// pause, which is the one thing a paused editor must not do.</para>
        /// </summary>
        internal static bool SpringClockFrozen =>
            ClockFrozenProbe != null
                ? ClockFrozenProbe()
                : EditorApplication.isPlaying && EditorApplication.isPaused;

        /// <summary>
        /// The user's SAVED "Enable Spring Animations" choice — durable, machine-wide EditorPrefs. Only the
        /// settings toggle should write this; everything else wants <see cref="MotionEnabled"/>. Writing it
        /// also drops any live suppression, so an explicit user choice always wins immediately.
        /// </summary>
        public static bool UserMotionPreference
        {
            get { return EditorPrefs.GetBool(MotionPrefKey, true); }
            set
            {
                EditorPrefs.SetBool(MotionPrefKey, value);
                s_motionOverride = null;
            }
        }

        // --- Baked house feel --------------------------------------------------------------
        // Every chrome spring is a touch quicker, much bouncier, and oscillates slower
        // (looser) than its literal config says. Applied inside Tune(), so all call-site
        // configs keep their RELATIVE character and these three numbers define the house style.

        private const float BakedDurationScale = 0.875f;
        private const float BakedBounceBoost = 0.7f;
        private const float BakedFrequencyScale = 0.6f;

        // --- Debug tuning knobs ----------------------------------------------------------
        // Machine-wide EditorPrefs that exaggerate every Sasquatch Inspector chrome spring (folds,
        // reorder, drag ghost, gaps, popups, banners, tabs) so the motion can be eyeballed
        // properly. Stock behavior = scale 1.0 / boost 0.0, which means the baked house feel
        // above, not the raw call-site configs. User-authored NodeAnimation springs are NOT
        // affected — those have their own Duration/Bounce fields.

        private const string DurationScaleKey = "SasquatchInspector.Spring.DurationScale";
        private const string BounceBoostKey = "SasquatchInspector.Spring.BounceBoost";
        private const string FrequencyScaleKey = "SasquatchInspector.Spring.FrequencyScale";

        // Same machine-wide-store rule as MotionEnabled above: read through, never cache.

        /// <summary>Multiplies every chrome spring's Duration (1 = stock, 4 = slow motion).</summary>
        public static float DebugDurationScale
        {
            get { return EditorPrefs.GetFloat(DurationScaleKey, 1f); }
            set { EditorPrefs.SetFloat(DurationScaleKey, Mathf.Clamp(value, 0.1f, 10f)); }
        }

        /// <summary>Added to every chrome spring's Bounce (0 = stock, 1 = very wobbly).</summary>
        public static float DebugBounceBoost
        {
            get { return EditorPrefs.GetFloat(BounceBoostKey, 0f); }
            set { EditorPrefs.SetFloat(BounceBoostKey, Mathf.Clamp(value, 0f, 2f)); }
        }

        /// <summary>
        /// Multiplies every chrome spring's oscillation frequency WITHOUT changing its
        /// settle time (damping is compensated inside SpringConfig). Below 1 = looser,
        /// rounder wobble; above 1 = tighter, buzzier. 1 = stock.
        /// </summary>
        public static float DebugFrequencyScale
        {
            get { return EditorPrefs.GetFloat(FrequencyScaleKey, 1f); }
            set { EditorPrefs.SetFloat(FrequencyScaleKey, Mathf.Clamp(value, 0.1f, 4f)); }
        }

        public static bool DebugTuningActive =>
            !Mathf.Approximately(DebugDurationScale, 1f) ||
            DebugBounceBoost > 0f ||
            !Mathf.Approximately(DebugFrequencyScale, 1f);

        /// <summary>Resets the debug spring-tuning multipliers to their defaults (1× duration/frequency, no bounce boost).</summary>
        public static void ResetDebugTuning()
        {
            DebugDurationScale = 1f;
            DebugBounceBoost = 0f;
            DebugFrequencyScale = 1f;
        }

        // The baked house feel used to live in these EditorPrefs while it was being tuned.
        // Reset the knobs once so machines that still carry the tuning values don't apply
        // them on top of the now-baked constants.
        private const string BakedMigrationKey = "SasquatchInspector.Spring.BakedDefaults.v1";

        [InitializeOnLoadMethod]
        private static void MigrateBakedDefaults()
        {
            if (EditorPrefs.GetBool(BakedMigrationKey, false)) return;
            EditorPrefs.SetBool(BakedMigrationKey, true);
            ResetDebugTuning();
        }

        /// <summary>
        /// Applies the baked house feel, then the debug knobs, to a call-site config. Every
        /// chrome spring call site uses this at SPRING-START time, so slider changes apply
        /// to the very next interaction.
        /// </summary>
        public static SpringConfig Tune(SpringConfig config)
        {
            config.Duration = Mathf.Clamp(config.Duration * BakedDurationScale * DebugDurationScale, 0.01f, 10f);
            config.Bounce = Mathf.Clamp(config.Bounce + BakedBounceBoost + DebugBounceBoost, 0f, 2f);
            config.FrequencyScale = Mathf.Clamp(config.ResolvedFrequencyScale * BakedFrequencyScale * DebugFrequencyScale, 0.1f, 4f);
            return config;
        }

        /// <summary>
        /// Tuning for foldout / group expand-collapse. No SettleEpsilon here: SpringFold
        /// runs in fraction space (0..1) and derives its own epsilon from the measured
        /// height, so a pixel-tuned value would be wrong by orders of magnitude.
        /// </summary>
        public static readonly SpringConfig FoldConfig = new SpringConfig(0.35f, 0.3f);

        /// <summary>
        /// Spring-folds content between hidden and its natural height; instant display
        /// toggle when motion is off.
        /// </summary>
        public static void Fold(VisualElement content, bool expanded)
        {
            if (content == null) return;
            if (MotionEnabled) SpringFold.SetExpanded(content, expanded, Tune(FoldConfig));
            else content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Spring-folds the content of a Unity <see cref="UnityEngine.UIElements.Foldout"/>. The
        /// Foldout's own (earlier-registered) value-changed handler has ALREADY snapped the
        /// content's display to the target state by the time our handler runs. SpringFold tracks its
        /// own settled state (it no longer reads the live display), so it acts on every flip without
        /// a "pre-toggle" hack — we only need to re-show the content for a COLLAPSE so SpringFold can
        /// measure the full height before animating it to 0; an EXPAND keeps Unity's display:Flex.
        /// Degrades to an instant toggle when motion is off, and SpringFold falls back to instant if
        /// it can't measure.
        /// </summary>
        public static void FoldNativeFoldout(VisualElement content, bool expanded)
        {
            if (content == null) return;
            if (!MotionEnabled)
            {
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }
            if (!expanded) content.style.display = DisplayStyle.Flex; // show so the collapse can measure
            SpringFold.SetExpanded(content, expanded, Tune(FoldConfig));
        }

        /// <summary>Scale + fade entrance with a slight overshoot (popups, menus).</summary>
        public static void PopIn(VisualElement ve, float startScale = 0.9f, float bounce = 0.5f)
        {
            if (ve == null || !MotionEnabled) return;
            CenterOrigin(ve);
            ve.style.scale = new Scale(new Vector3(startScale, startScale, 1f));
            ve.style.opacity = 0f;
            ve.SpringScale(1f, Tune(new SpringConfig(0.35f, bounce)));
            // SpringOpacityIn, never SpringOpacity(1f): a fade that settles on an inline 1 PINS the element's
            // opacity above every stylesheet rule forever, and the rule it outranks is the editor theme's
            // `.unity-disabled { opacity: .5 }`. That matters here because a condition's Show outcome pops the
            // field in through this very call — after which its Enable/Disable outcome would still flip
            // enabledSelf but render identically. See SpringVisualElementExtensions.SpringOpacityIn.
            ve.SpringOpacityIn(Tune(SpringConfig.Snappy));
        }

        /// <summary>The fade behind a hover-revealed control. Quick and overshoot-free — a reveal that
        /// wobbles reads as a glitch, not as feedback, because it fires on every pointer pass.</summary>
        public static readonly SpringConfig RevealFadeConfig = new SpringConfig(0.2f, 0f);

        /// <summary>
        /// Shows or hides a HOVER-REVEALED control — a chrome button that is faded out until the pointer is
        /// inside the thing it acts on. Springs on the way in (a small pop plus a fade, so the button reads
        /// as arriving rather than blinking on) and fades flat on the way out.
        /// <para>
        /// Both directions go through the SAME two spring keys (opacity and scale), so a reveal interrupted
        /// half-way — the pointer clipping a card's corner — is taken over rather than raced: the fade-out
        /// picks up from wherever the fade-in got to. That is why this is one call and not two, and why
        /// callers must NOT write <c>style.opacity</c> themselves after the first frame.
        /// </para>
        /// The caller still owns the RESTING state: set <c>style.opacity = 0f</c> when the control is built,
        /// or it flashes at full strength for one frame before the first hide.
        /// </summary>
        public static void RevealFade(VisualElement ve, bool shown, float startScale = 0.6f)
        {
            if (ve == null) return;
            if (!MotionEnabled)
            {
                // Reduce-motion still has to REVEAL — a no-op here would leave the button invisible forever,
                // which is the one failure this control cannot survive.
                ve.style.opacity = shown ? 1f : 0f;
                return;
            }

            if (shown)
            {
                PopIn(ve, startScale, 0.45f);
            }
            else
            {
                // Opacity only. The scale spring is left to settle on its own: it is already heading to 1,
                // and yanking it back down would make a control shrink as it disappears — a second motion
                // the eye reads as a separate event.
                ve.SpringOpacity(0f, Tune(RevealFadeConfig));
            }
        }

        /// <summary>Small scale impulse — the "thunk" of a card landing in its slot.</summary>
        public static void LandPunch(VisualElement ve, float strength = 0.6f)
        {
            if (ve == null || !MotionEnabled) return;
            CenterOrigin(ve);
            ve.PunchScale(strength, Tune(SpringConfig.Bouncy));
        }

        /// <summary>Press-in then spring back to rest with a slight overshoot — tactile button feedback.</summary>
        public static void PressBounce(VisualElement ve, float depress = 0.92f)
        {
            if (ve == null || !MotionEnabled) return;
            CenterOrigin(ve);
            ve.style.scale = new Scale(new Vector3(depress, depress, 1f)); // instant dip
            ve.SpringScale(1f, Tune(SpringConfig.Bouncy));                  // spring back up
        }

        /// <summary>Horizontal shake — a wordless "no" for rejected actions.</summary>
        public static void Shake(VisualElement ve, float strength = 1f)
        {
            if (ve == null || !MotionEnabled) return;
            ve.PunchTranslateX(140f * strength, Tune(new SpringConfig(0.4f, 1.1f)));
        }

        /// <summary>Slide-down + fade entrance (banners, revealed rows).</summary>
        public static void SlideInY(VisualElement ve, float fromPx = -8f, float bounce = 0.35f)
        {
            if (ve == null || !MotionEnabled) return;
            ve.style.translate = new Translate(0f, fromPx);
            ve.style.opacity = 0f;
            ve.SpringTranslate(Vector2.zero, Tune(new SpringConfig(0.35f, bounce)));
            ve.SpringOpacityIn(Tune(SpringConfig.Snappy));
        }

        /// <summary>
        /// Cascade-pops a panel's field rows in, one after another — the tab-switch entrance. Each
        /// row springs up from <paramref name="startScale"/> with a little bounce (a "punch") and
        /// fades, staggered top-to-bottom by <paramref name="gapSeconds"/>. The gap auto-shrinks so a
        /// long form never cascades longer than <paramref name="maxWindowSeconds"/>. No-op when motion
        /// is disabled (caller leaves the rows at rest). Scale/opacity are transform-only, so the
        /// cascade never reflows the layout. Uses UI Toolkit's scheduler (the runtime SpringStagger
        /// needs a MonoBehaviour owner and can't run in editor chrome).
        /// </summary>
        public static void CascadeFieldsIn(VisualElement panel, float startScale = 0.92f,
            float gapSeconds = 0.03f, float maxWindowSeconds = 0.32f)
        {
            if (panel == null || !MotionEnabled) return;
            var rows = ResolveCascadeRows(panel);
            if (rows.Count == 0) return;

            float gap = rows.Count > 1 ? Mathf.Min(gapSeconds, maxWindowSeconds / (rows.Count - 1)) : 0f;

            // Hide everything synchronously first so nothing flashes at rest before its turn.
            foreach (var row in rows)
            {
                CenterOrigin(row);
                row.style.opacity = 0f;
                row.style.scale = new Scale(new Vector3(startScale, startScale, 1f));
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                long delayMs = (long)(i * gap * 1000f);
                row.schedule.Execute(() =>
                {
                    row.SpringScale(1f, Tune(new SpringConfig(0.32f, 0.55f))); // bouncy punch
                    row.SpringOpacityIn(Tune(SpringConfig.Snappy));            // quick fade, then unpin
                }).StartingIn(delayMs);
            }
        }

        /// <summary>
        /// Pops an explicit, ordered set of elements in one after another (scale + fade with a slight bounce),
        /// staggered by <paramref name="gapSeconds"/> and auto-compressed so the whole sequence never runs longer
        /// than <paramref name="maxWindowSeconds"/>. Like <see cref="CascadeFieldsIn"/>, but over a caller-supplied
        /// list (e.g. a page's content / image / preview / action blocks) instead of a panel's children — so it
        /// won't unwrap or reorder anything. Transform-only and a no-op when motion is disabled.
        /// </summary>
        public static void StaggerIn(IList<VisualElement> elements, float startScale = 0.94f,
            float gapSeconds = 0.06f, float maxWindowSeconds = 0.45f)
        {
            if (elements == null || elements.Count == 0 || !MotionEnabled) return;
            int n = elements.Count;
            float gap = n > 1 ? Mathf.Min(gapSeconds, maxWindowSeconds / (n - 1)) : 0f;

            foreach (var ve in elements)
            {
                if (ve == null) continue;
                CenterOrigin(ve);
                ve.style.opacity = 0f;
                ve.style.scale = new Scale(new Vector3(startScale, startScale, 1f));
            }

            for (int i = 0; i < n; i++)
            {
                var ve = elements[i];
                if (ve == null) continue;
                long delayMs = (long)(i * gap * 1000f);
                ve.schedule.Execute(() =>
                {
                    ve.SpringScale(1f, Tune(new SpringConfig(0.32f, 0.5f))); // bouncy punch
                    ve.SpringOpacityIn(Tune(SpringConfig.Snappy));           // quick fade, then unpin
                }).StartingIn(delayMs);
            }
        }

        // The fields to cascade. A tab's content is usually a Rows container whose children ARE the
        // field rows; Box/Card tabs wrap them one level down. Unwrap single-child wrappers so we
        // stagger the real rows rather than one outer block.
        private static List<VisualElement> ResolveCascadeRows(VisualElement panel)
        {
            var host = panel;
            while (host.childCount == 1 && host[0].childCount > 0) host = host[0];
            var rows = new List<VisualElement>(host.childCount);
            foreach (var child in host.Children()) rows.Add(child);
            return rows;
        }

        /// <summary>
        /// A calm, low-overshoot hover lift. The default hover spring (Snappy + the +0.7 house
        /// bounce) overshoots its target, so a 1.03 scale reads closer to ~5%; this config keeps a
        /// 1.03 lift settling right around 3%. Pass it to <see cref="HoverScale"/> for the designer's
        /// list-row cards (Bindings / Widgets / Tools).
        /// </summary>
        public static readonly SpringConfig HoverLiftConfig = new SpringConfig(0.2f, 0.1f);

        /// <summary>
        /// The Grow hover for FULL-WIDTH buttons: a snappy, overshoot-free ~1.035 rise. Full-width buttons fill
        /// the inspector edge-to-edge with almost no horizontal slack, so the regular Grow (1.06 + the +0.7 house
        /// overshoot ≈ 1.073 peak) pushed the rounded right corner ~8px off-screen and popped a horizontal
        /// scrollbar. Measured against a real full-width button (viewport right edge ≈ 483px): 1.073 overflowed
        /// +7.6px, 1.06 +4.6px, whereas 1.035 stayed ~1px INSIDE the edge. Kept overshoot-free (bounce 0.12) so
        /// the peak equals the nominal and never eats that margin.
        /// </summary>
        private static readonly SpringConfig GrowFullWidthConfig = new SpringConfig(0.16f, 0.12f);

        /// <summary>
        /// A subtle "lift" while the cursor is over <paramref name="ve"/>: springs it up to
        /// <paramref name="scale"/> on hover and back to rest on leave. Transform-only, so it never
        /// reflows siblings, and a no-op when motion is disabled. Used to give field/widget cards a
        /// little life on hover. Pass <paramref name="config"/> to override the default bouncy spring
        /// (e.g. <see cref="HoverLiftConfig"/> for a tighter ~3% lift); when null it tracks the live
        /// debug tuning.
        /// </summary>
        public static void HoverScale(VisualElement ve, float scale = 1.02f, SpringConfig? config = null)
        {
            if (ve == null) return;
            ve.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!MotionEnabled) return;
                CenterOrigin(ve);
                ve.SpringScale(scale, config ?? Tune(SpringConfig.Snappy));
            });
            ve.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!MotionEnabled) return;
                ve.SpringScale(1f, config ?? Tune(SpringConfig.Snappy));
            });
        }

        /// <summary>
        /// The standard button feel: a calm, low-overshoot hover lift plus a gentle press-dip on pointer-down —
        /// the same "Lift" motion the Open-in-Layout-Designer button uses. Transform-only (never reflows siblings,
        /// never steals focus), so it is safe on non-focusable toolbar buttons and full-width bars alike, and a
        /// no-op when motion is disabled. This is the shared routine behind every "small button" hover in the
        /// editor chrome — rich-text toolbar, list +/-/X/pager, gradient controls, etc. (the gradient widget's
        /// <c>ApplyGradientButtonSpring</c> forwards here). The lift is small (1.03) and the spring is passed
        /// un-<see cref="Tune"/>d (no +0.7 house-bounce overshoot), so a hovered button can't grow enough to pop a
        /// horizontal scrollbar. Callers who want the wobblier feel use <see cref="ApplyButtonHover"/> with
        /// <see cref="Runtime.ButtonHoverAnimation.Bounce"/>.
        /// </summary>
        public static void ButtonSpring(VisualElement ve, float hoverScale = 1.03f, float pressDepress = 0.98f)
        {
            if (ve == null) return;
            HoverScale(ve, hoverScale, HoverLiftConfig);
            ve.RegisterCallback<PointerDownEvent>(_ => PressBounce(ve, pressDepress));
        }

        /// <summary>
        /// Applies a <see cref="Runtime.ButtonHoverAnimation"/>'s hover/press feedback to a widget button.
        /// Every option is transform-only (never reflows siblings, never steals focus) and a no-op when
        /// motion is disabled — so it's safe on full-width bars and non-focusable buttons alike:
        ///   • Bounce — the original springy lift + press-dip (unchanged default).
        ///   • None   — no transform (the preset's color-on-hover, wired by the caller, still shows).
        ///   • Lift   — a calm, low-overshoot rise with a gentle press.
        ///   • Grow   — a larger, snappier rise (a gentler, overshoot-free ~1.035 rise on full-width buttons, which
        ///              have no horizontal slack — see <paramref name="fullWidth"/>).
        ///   • Pop    — a quick scale "pop" each time the cursor enters.
        /// <paramref name="fullWidth"/> tells the Grow case the button spans the full container width (no
        /// horizontal room to grow into), so it uses the smaller <see cref="GrowFullWidthConfig"/> rise instead of
        /// the bigger one that would push a full-width button's rounded edge off the inspector.
        /// </summary>
        public static void ApplyButtonHover(VisualElement ve, Runtime.ButtonHoverAnimation animation, bool fullWidth = false)
        {
            if (ve == null) return;
            switch (animation)
            {
                case Runtime.ButtonHoverAnimation.None:
                    break; // no transform feedback
                case Runtime.ButtonHoverAnimation.Lift:
                    HoverScale(ve, 1.03f, HoverLiftConfig);
                    ve.RegisterCallback<PointerDownEvent>(_ => PressBounce(ve, 0.98f));
                    break;
                case Runtime.ButtonHoverAnimation.Grow:
                    if (fullWidth)
                    {
                        // No horizontal slack — keep the rise small + overshoot-free so the rounded edge stays on-screen.
                        HoverScale(ve, 1.035f, GrowFullWidthConfig);
                        ve.RegisterCallback<PointerDownEvent>(_ => PressBounce(ve, 0.98f));
                    }
                    else
                    {
                        HoverScale(ve, 1.06f, Tune(SpringConfig.Snappy));
                        ve.RegisterCallback<PointerDownEvent>(_ => PressBounce(ve, 0.95f));
                    }
                    break;
                case Runtime.ButtonHoverAnimation.Pop:
                    ve.RegisterCallback<PointerEnterEvent>(_ => LandPunch(ve, 0.5f));
                    ve.RegisterCallback<PointerDownEvent>(_ => PressBounce(ve, 0.94f));
                    break;
                default: // Bounce — springy lift + press-dip. Inlined (not ButtonSpring) so it stays genuinely
                         // wobbly: ButtonSpring is the calm default, so the user-selectable Bounce option keeps
                         // the Tune(Bouncy) overshoot that visually distinguishes it from Lift.
                    HoverScale(ve, 1.03f, Tune(SpringConfig.Bouncy));
                    ve.RegisterCallback<PointerDownEvent>(_ => PressBounce(ve, 0.96f));
                    break;
            }
        }

        /// <summary>
        /// Spins a disclosure caret between closed (0°, a single chevron pointing right) and open
        /// (90°, pointing down) with a slight overshoot. Transform-only, so it never touches the
        /// fold's height geometry — safe even on the design canvas. Pass <paramref name="animate"/>
        /// false (or when motion is off) to snap the angle, used to set the initial state on first
        /// build without a spin.
        /// </summary>
        public static void SpringCaret(VisualElement icon, bool open, bool animate = true)
        {
            if (icon == null) return;
            CenterOrigin(icon);
            float target = open ? CaretOpenAngle : 0f;
            if (!animate || !MotionEnabled)
            {
                icon.CancelSprings();
                icon.style.rotate = new Rotate(Angle.Degrees(target));
                return;
            }
            icon.SpringRotate(target, Tune(SpringConfig.Bouncy));
        }

        private const float CaretOpenAngle = 90f;

        /// <summary>
        /// Spins a direction arrow a half turn (0° ↔ 180°) with the same overshoot the disclosure caret
        /// uses — for a sort toggle, where swapping an up ICON for a down one conveys no sense of which way
        /// it just turned. Rotating the SAME arrow does, and it reuses the motion language already in the
        /// package instead of inventing one. Transform-only, so it never touches layout.
        /// <para>Pass <paramref name="animate"/> false to snap: the first build, and any rebuild that did
        /// not actually change direction (a search/page refresh re-runs the same update).</para>
        /// </summary>
        public static void SpringFlipArrow(VisualElement icon, bool flipped, bool animate = true)
        {
            if (icon == null) return;
            CenterOrigin(icon);
            float target = flipped ? 180f : 0f;
            if (!animate || !MotionEnabled)
            {
                icon.CancelSprings();
                icon.style.rotate = new Rotate(Angle.Degrees(target));
                return;
            }
            icon.SpringRotate(target, Tune(SpringConfig.Bouncy));
        }


        // --- Toggle switch ---------------------------------------------------------------
        // The switch styles (Rounded / Square / Slim) used to slide their knob on a flat 0.15s CSS
        // transition — the same speed the whole way across, arriving exactly on the mark. A latch does
        // not move like that. These numbers throw it instead.

        /// <summary>
        /// The knob's slide. Once <see cref="Tune"/> has had it this settles in ~0.25s with a ~14%
        /// overshoot, so a 24px throw carries the knob about 3px past the far end and rocks back.
        /// Deliberately restrained: bounce 0.3 here would double that and visibly launch the knob out
        /// of the pill.
        /// </summary>
        public static readonly SpringConfig ToggleKnobConfig = new SpringConfig(0.28f, 0.15f);

        // The knob's landing pop, as an IMPULSE VELOCITY rather than a scale — a Punch kicks a spring that
        // is already sitting at rest, and the peak it reaches is roughly velocity / angular frequency, so at
        // the tuned Bouncy stiffness this reads as about 6%.
        private const float ToggleKnobPopVelocity = 2f;

        // The ON/OFF overlay CROSSES THE KNOB'S PATH, so it is faded rather than nudged.
        //
        // The overlay swaps its word AND its side of the track in one instant — and the side it swaps TO is
        // the side the knob is just leaving. Measured on a Rounded switch turning ON: at 31ms the knob spans
        // x 135.8-150.4 and the overlay spans 131.8-151.3. They are on top of each other, and the overlay is
        // the later child so it draws ON TOP: white "ON" smeared across a white knob for the first third of
        // the trip. It reads as a broken knob, and that is how it was reported.
        //
        // There is no arrangement that avoids this — the two genuinely swap sides and cannot both fit in a
        // 42px track while passing — so the overlay sits the crossing out. It goes to zero the moment the
        // switch flips and fades back in once the knob has vacated its seat. The delay is measured from the
        // same run: the knob is past home at 92ms, so 110ms clears it with room to spare, and the fade lands
        // under the tail of the overshoot rather than fighting the throw.
        //
        // (This overlap predates the spring — the 0.15s CSS lerp did it too, for a third as long.)
        private const long ToggleOnOffFadeDelayMs = 110;
        private static readonly SpringConfig ToggleOnOffFadeConfig = new SpringConfig(0.25f, 0f);

        // A sanity ceiling on the flip distance. The widest switch is a 44px track at the 1.5x Extra
        // Large scale, so a bigger jump than this is not a knob sliding — it is the whole control being
        // re-laid out while we happened to be waiting on a flip, and springing that would fling the knob
        // in from somewhere off the row.
        private const float MaxToggleKnobTravel = 80f;

        /// <summary>
        /// Hands a Sasquatch switch-style <see cref="Toggle"/>'s knob to the spring engine: it is thrown
        /// across the track, overshoots the far end by a few pixels and rocks back, with a small scale pop
        /// as it lands. The ON/OFF overlay, if the switch carries one, sits the crossing out.
        /// <para>Call it once, right after the <c>sq-toggle</c> classes go on — it wires its own callbacks
        /// and needs nothing from the caller afterwards.</para>
        /// <para>With motion off, or before anyone has pressed the switch, the knob SNAPS to its side. That is
        /// a deliberate change from the stylesheet's old 0.15s ease, and it is the promise "Enable Spring
        /// Animations" already makes everywhere else in the package — off means instant, not "a different
        /// animation".</para>
        /// </summary>
        public static void SpringToggleKnob(Toggle toggle)
        {
            if (toggle == null) return;
            var knob = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
            if (knob == null) return;
            var track = toggle.Q<VisualElement>(className: "unity-toggle__input");

            // Take `left` OFF the transition list. Below, `left` stops moving at all — but until the pin
            // lands the stylesheet still owns it, and an eased slide underneath a spring would be two
            // animations on one pixel. The colour fade stays; it is not in play here.
            knob.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("background-color") };
            knob.style.transitionDuration = new List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
            // The overshoot carries the knob a few px past the end of the track — let it peek out rather
            // than be shaved off by the pill it is riding in.
            if (track != null) track.style.overflow = Overflow.Visible;

            bool pinned = false;      // `left` has been frozen and the transform now owns the knob's position
            bool pinnedOn = false;    // which state the knob was standing in when `left` was frozen
            float travel = 0f;        // px between the two resting positions
            IVisualElementScheduledItem overlayFade = null;

            // A THROW IS A GESTURE, so it is armed by one: the first time a human presses or types on this
            // switch. Everything that moves the knob before that is machinery — the synchronous seed, the
            // binding pushing the serialized value, an inspector rebuild, an Undo restoring state — and every
            // one of those should land the knob on its side with no animation at all.
            //
            // This replaces the timer that used to do the job (`sq-toggle--instant`, 60ms). A timer cannot
            // win: the value it guards against arrives whenever the binding updater gets to it, which under a
            // burst of full-panel rebuilds is AFTER any window you pick — measured, and the symptom is a
            // switch that renders ON in blue with its knob still sliding in from the left. Layout was no
            // better a gate; the late bind lands after the first layout too.
            //
            // A press cannot be missed, because it always precedes the value change it causes: a Toggle flips
            // on the pointer UP. TrickleDown so the arm happens on the way to the target rather than after
            // the Clickable has had it.
            bool armed = false;
            toggle.RegisterCallback<PointerDownEvent>(_ => armed = true, TrickleDown.TrickleDown);
            toggle.RegisterCallback<KeyDownEvent>(_ => armed = true, TrickleDown.TrickleDown);

            // Where the knob must sit, as a transform offset from the pinned `left`.
            float OffsetFor(bool on) => on == pinnedOn ? 0f : (on ? travel : -travel);

            // FREEZE `left`, ONCE, AND NEVER ANIMATE IT AGAIN. This is the whole design, and the reason is a
            // race that has no fix inside it.
            //
            // The obvious version of this effect lets the stylesheet keep `left` — :checked jumps the knob to
            // the far side — and cancels that jump with a transform, then springs the transform home. It does
            // not work, because `left` is consumed by the LAYOUT pass and `translate` by the TRANSFORM pass,
            // and on a flip the two do not reliably land in the same frame. Measured on a Rounded switch
            // whose track spans x 125-169: at the instant of the flip the knob sat at x=104 — twenty-one
            // pixels clear of the left end of its own track, wearing the old layout position AND the full
            // pull-back at once. On screen that is the knob teleporting off the chip and flying back in, and
            // it is what was reported. Reversing which side is "late" just moves the bad frame.
            //
            // So the knob's position is made to depend on ONE property. `left` is frozen where the knob is
            // already standing, and every move after that is a pure transform — atomic, layout-free, and with
            // nothing left to disagree with it. The stylesheet's `:checked { left: 26px }` becomes inert for
            // the knob (an inline value outranks it); it still drives the track fill and the overlay.
            //
            // Pinning at the FIRST layout, while the offset is still 0, is what makes the pin itself
            // invisible: `left` is written back the same value it already resolved to, so even if the write
            // is picked up a frame later there is no combination of old and new that is wrong.
            knob.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (pinned || track == null || evt.newRect.width <= 0f) return;

                // The two resting positions are symmetric insets in the track — the stylesheet builds them
                // that way for every variant ("width 44 - knob 14 - margin 2 - border 2") — so one of them
                // plus the two widths gives the distance between them without C# ever learning either number.
                float guess = track.layout.width - evt.newRect.width - 2f * evt.newRect.x;
                if (Mathf.Abs(guess) <= 1f || Mathf.Abs(guess) > MaxToggleKnobTravel) return;

                travel = Mathf.Abs(guess);
                pinnedOn = toggle.value;

                // NOT `resolvedStyle.left`. That reports the USED value — the knob's computed position
                // inside its parent, which already has the track's 1px border folded in — so writing it
                // back as an authored `left` adds that border a second time and the knob rests one pixel
                // right of where the stylesheet put it, in BOTH states. Measured: the off seat moved 3 -> 4.
                // The layout rect minus the border is the authored value, and writing that is a true freeze.
                knob.style.left = evt.newRect.x - track.resolvedStyle.borderLeftWidth;
                pinned = true;
            });

            toggle.RegisterValueChangedCallback(evt =>
            {
                // Before the pin the stylesheet still owns the position and moves the knob correctly on its
                // own. Nothing to do, and nothing to animate — this is build-time.
                if (!pinned) return;

                float target = OffsetFor(evt.newValue);

                // Not a gesture, or motion is off: land it, instantly and exactly.
                if (!armed || !MotionEnabled)
                {
                    knob.CancelSprings();
                    knob.style.translate = new Translate(target, 0f);
                    knob.style.scale = new Scale(Vector3.one);
                    return;
                }

                // THE THROW. Springing to the new target from wherever the knob currently stands is what makes
                // a mid-flight re-flip continuous for free: the engine keeps the value and the velocity it
                // already had and simply re-aims. The earlier version re-seeded the offset by hand and lurched
                // 10px in the direction the user had just cancelled.
                CenterOrigin(knob);
                knob.SpringTranslate(new Vector2(target, 0f), Tune(ToggleKnobConfig));
                knob.PunchScale(ToggleKnobPopVelocity, Tune(SpringConfig.Bouncy));

                // The overlay sits the crossing out — see ToggleOnOffFadeDelayMs for why it has to.
                var onOff = track?.Q<Label>(className: "sq-toggle-onoff-label");
                if (onOff == null) return;
                onOff.style.opacity = 0f;
                overlayFade?.Pause();
                overlayFade = onOff.schedule.Execute(() => onOff.SpringOpacityIn(Tune(ToggleOnOffFadeConfig)));
                overlayFade.ExecuteLater(ToggleOnOffFadeDelayMs);
            });
        }

        /// <summary>
        /// Attention ping: the element starts slightly enlarged and springs back down to rest with a
        /// little bounce — a "pop into view". Distinct from <see cref="LandPunch"/>/PunchScale, which
        /// kick OUT from rest; this starts big and settles.
        /// </summary>
        public static void PingScale(VisualElement ve, float startScale = 1.2f)
        {
            if (ve == null || !MotionEnabled) return;
            CenterOrigin(ve);
            ve.style.scale = new Scale(new Vector3(startScale, startScale, 1f));
            ve.SpringScale(1f, Tune(SpringConfig.Bouncy));
        }

        private static void CenterOrigin(VisualElement ve)
        {
            ve.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f));
        }
    }
}
