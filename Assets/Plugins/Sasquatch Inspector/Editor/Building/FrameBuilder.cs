using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds the element shell for a <see cref="ContainerType.Frame"/> container: a shaped clipping box
    /// (rounded rect / circle / pill / diagonal split) with an optional image fill and gradient overlay
    /// layered under the content. The frame ROOT is the clipping element, and StyleApplier later paints the
    /// node's backgroundColor / border / borderRadius directly on it, so those follow the shape for free.
    ///
    /// Layer order inside the root (child order = paint order): fill image, overlay, content host.
    /// The fill/overlay layers are absolutely positioned edge-to-edge; <see cref="PostStyleFixup"/> moves any
    /// node padding onto the content host so the layers stay flush with the frame edges.
    ///
    /// DiagonalSplit uses a rotated-clipper construction: an oversized
    /// overflow:hidden child rotated by the seam angle (its straight edge IS the seam), with the pane
    /// content counter-rotated inside so it renders axis-aligned. CRITICAL: a rotated clipper only truly
    /// clips when it carries a non-zero border-radius — with radius 0 the renderer silently falls back to
    /// an axis-aligned scissor rect and the pane spills across the seam (measured on Unity 6.3). The 1px
    /// radius lives on corners far outside the frame, so it is invisible.
    /// </summary>
    internal static class FrameBuilder
    {
        internal const string FrameClass = "sasq-frame";
        internal const string FillClass = "sasq-frame-fill";
        internal const string OverlayClass = "sasq-frame-overlay";
        internal const string TitleClass = "sasq-frame-title";
        internal const string ContentClass = "sasq-frame-content";
        internal const string PaneClipClass = "sasq-frame-pane-clip";
        internal const string PaneClass = "sasq-frame-pane";
        internal const string PaneFillClass = "sasq-frame-pane-fill";
        internal const string SeamClass = "sasq-frame-seam";
        internal const string PaneAName = "sasq-frame-pane-a";
        internal const string PaneBName = "sasq-frame-pane-b";

        /// <summary>Default grow-only height floor so an empty frame is visible and droppable.</summary>
        private const float DefaultMinHeight = 48f;
        /// <summary>Default corner radius for RoundedRect when the node doesn't override Style.borderRadius.</summary>
        private const float DefaultRoundedRadius = 8f;
        /// <summary>Forces the renderer's mask path on rotated clippers (see class summary).</summary>
        private const float RotatedClipMaskRadius = 1f;

        /// <summary>
        /// 1 - 1/√2: inset factor that turns a corner radius into the safe-area inset of the largest
        /// inscribed rectangle. Content (title + fields) placed inside that inset can never be cropped
        /// by the shape's curve — a circle of radius R yields its inscribed square (side R·√2).
        /// </summary>
        private const float CornerSafeInsetFactor = 0.29289322f;
        /// <summary>Breathing room panes keep from the split frame's straight edges.</summary>
        private const float SplitEdgePad = 6f;
        /// <summary>Extra clearance pane content keeps from the seam line (beyond the tilt span).</summary>
        private const float SplitSeamGap = 6f;
        /// <summary>Gap between the split title's baseline block and the pane content below it.</summary>
        private const float SplitTitleGap = 4f;

        /// <summary>
        /// How close an in-flow pane child's height may come to its pane subtree's height before
        /// <see cref="GrowSplitToContent"/> treats it as ECHOING the frame rather than measuring content.
        /// Catches only the EXACT-fill case (a child with flexGrow inside the subtree); a child that trails
        /// the frame by its own chrome sits well outside this and is caught by the ratchet pin instead.
        /// </summary>
        private const float SplitEchoEpsilon = 1f;

        /// <summary>
        /// How many strictly-increasing grow targets in a row count as a RATCHET rather than a resize.
        /// A genuine content change is ONE transition; feedback climbs forever, one step per settle.
        /// </summary>
        private const int MinRatchetRuns = 4;

        /// <summary>
        /// Frames whose grow loop was caught ratcheting, and the height to hold them at. Keyed by the frame
        /// ROOT ELEMENT (not the seed cache's node path) on purpose: the latch describes a live layout
        /// pathology, not a property of the layout, so a rebuild must get a fresh verdict — a CWT entry dies
        /// with its element. See the pin in <see cref="GrowSplitToContent"/>.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object>
            _splitGrowRatchetLatch = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object>();

        /// <summary>
        /// Absolute ceiling on a split frame's grown min-height. Pure safety net: with the echo skip in
        /// place the measure is no longer circular, but the oscillation pin CANNOT see a monotonic ratchet
        /// (it fires only when a target returns to a height it left — deliberately, so a resize can shrink),
        /// so nothing else bounds runaway growth. A frame this tall is already far past anything an
        /// inspector can show; clamping is strictly better than the 4-million-pixel frame that a circular
        /// measure produced before the skip existed.
        /// </summary>
        private const float MaxSplitGrowHeight = 4096f;

        // Last converged DiagonalSplit minHeight, keyed by inspected-object instance + structural node
        // path (both stable across rebuilds AND across the designer's save re-clone, unlike node object
        // references). Seeded onto the frame root at build time so a rebuilt frame STARTS at its settled
        // height instead of replaying the 48px-floor → measure → deferred-grow sequence across several
        // painted frames — that replay shifted everything below the frame up and back on EVERY full
        // rebuild (save, style edits, debounced-commit reconciles: the canvas "jerk"). Never
        // authoritative: GrowSplitToContent still measures every geometry pass and corrects a stale seed
        // (content/width changed) through the same guarded write. Static ⇒ cleared on domain reload; the
        // first build after a reload settles once, exactly like a first-ever open always did.
        private static readonly Dictionary<string, float> _splitMinHeightSeeds = new Dictionary<string, float>();

        // Recent grow targets per split frame, for the oscillation pin in GrowSplitToContent.
        private static readonly Dictionary<string, List<float>> _splitGrowHistory
            = new Dictionary<string, List<float>>();
        // Must span at least TWO transitions, because the pin's detector is run-length-collapsed: it fires
        // only when a value RE-APPEARS in an earlier non-adjacent run. Several geometry passes fire per
        // transition, so at 6 a slow 2-cycle (measured live: ~3 samples per dwell) filled the whole window
        // with a SINGLE transition — collapsing to runs [96,97], which has nothing to re-appear, so the pin
        // sat out the exact vibration it exists to kill. 12 holds two full dwell-transition-dwell cycles at
        // that rate. Widening is free: a monotonic resize still collapses to one run and is still never
        // pinned, which is what keeps a dragged row shrinkable.
        private const int SplitGrowWindow = 12;

        // Very generous soft cap on the two caches above, in the shape of the _smoothFillCopies cap below.
        // Entries are tiny (a string key plus a float / a ≤SplitGrowWindow-float list, and NO UnityEngine.Object
        // references, so nothing is held alive) — this is a bounded-growth backstop for a marathon session
        // that inspects thousands of distinct objects, never a working limit. It has to STAY generous: the
        // seed must be allowed to outlive its element (that is the whole point of the cache — see the
        // comment on _splitMinHeightSeeds), so evicting an entry a live frame still needs would put the
        // 48px-floor → measure → deferred-grow "canvas jerk" straight back. Both caps only ever fire on a
        // key that is NOT already present, so re-settling an on-screen frame can never trip one.
        internal const int MaxSplitSizeEntries = 512;

        /// <summary>Empties the DiagonalSplit seed + oscillation caches (reload / quit / soft cap). Purely
        /// managed state: the next build of a split frame re-settles once, exactly as the first build after
        /// a domain reload always has.</summary>
        internal static void ClearSplitSizeCaches()
        {
            _splitMinHeightSeeds.Clear();
            _splitGrowHistory.Clear();
        }

        /// <summary>
        /// Records a split frame's converged height under the soft cap. The cap only ever fires for a key
        /// the cache does NOT already hold: re-seeding a known key must never clear, or a cache sitting at
        /// the cap would wipe its whole working set on every geometry pass — the rebuild jerk this cache
        /// exists to prevent.
        /// </summary>
        internal static void RecordSplitSeed(string key, float target)
        {
            if (key == null) return;
            if (_splitMinHeightSeeds.Count >= MaxSplitSizeEntries && !_splitMinHeightSeeds.ContainsKey(key))
                _splitMinHeightSeeds.Clear();
            _splitMinHeightSeeds[key] = target;
        }

        /// <summary>
        /// The recent-target window for a split frame's oscillation pin, created on demand under the same
        /// soft cap. A key already in the cache short-circuits before the cap, so a frame that is actively
        /// settling keeps its window and the pin stays armed.
        /// </summary>
        internal static List<float> SplitGrowWindowFor(string key)
        {
            if (!_splitGrowHistory.TryGetValue(key, out var recent))
            {
                if (_splitGrowHistory.Count >= MaxSplitSizeEntries) _splitGrowHistory.Clear();
                recent = new List<float>(SplitGrowWindow);
                _splitGrowHistory[key] = recent;
            }
            return recent;
        }

        // Test-visible counters/readers for the two caches (see SasquatchVerificationTests.LifecycleCaches).
        internal static int SplitSeedCacheCount => _splitMinHeightSeeds.Count;
        internal static int SplitGrowHistoryCount => _splitGrowHistory.Count;
        internal static bool TryGetSplitSeed(string key, out float height)
        {
            height = 0f;
            return key != null && _splitMinHeightSeeds.TryGetValue(key, out height);
        }

        /// <summary>Seed-cache key for a DiagonalSplit frame's settled height (null when unresolvable).</summary>
        internal static string SplitSeedKey(SerializedObject serializedObject, string nodePath)
        {
            var target = serializedObject != null ? serializedObject.targetObject : null;
            return target == null ? null : UnityObjectId.KeyOf(target) + "|" + nodePath;
        }

        /// <summary>
        /// Builds the frame shell. For non-split shapes <paramref name="contentTarget"/> is the host the
        /// shared children loop fills. For DiagonalSplit it is null — the two pane hosts (query by
        /// <see cref="PaneAName"/>/<see cref="PaneBName"/>) each receive one pane subtree instead.
        /// </summary>
        internal static VisualElement BuildShell(
            ContainerNode node, SerializedObject serializedObject, bool designMode,
            out VisualElement contentTarget)
        {
            var settings = node.Frame;

            var frameRoot = new VisualElement();
            frameRoot.AddToClassList(FrameClass);
            frameRoot.style.flexDirection = FlexDirection.Column;
            frameRoot.style.overflow = Overflow.Hidden;
            frameRoot.style.minHeight = DefaultMinHeight;
            frameRoot.style.flexShrink = 0f;
            // Seed the RoundedRect default radius; StyleApplier overwrites it when the node/theme overrides.
            if (settings.Shape == FrameShape.RoundedRect)
                SetAllCorners(frameRoot, DefaultRoundedRadius);

            if (settings.Shape == FrameShape.DiagonalSplit)
            {
                BuildSplitLayers(frameRoot, node, serializedObject, designMode);
                contentTarget = null;
                return frameRoot;
            }

            AddFillLayer(frameRoot, settings, serializedObject, designMode);
            AddOverlayLayer(frameRoot, settings);

            // Title pins in-flow at the top of the clip (over fill/overlay, under nothing) — the
            // Card-label precedent, but outside the content host so content anchoring can't move it.
            var title = MakeTitleLabel(node);
            if (title != null) frameRoot.Add(title);

            var contentHost = new VisualElement();
            contentHost.AddToClassList(ContentClass);
            contentHost.style.flexDirection = FlexDirection.Column;
            contentHost.style.flexGrow = 1f;
            frameRoot.Add(contentHost);

            contentTarget = contentHost;
            return frameRoot;
        }

        /// <summary>The frame's optional Title, rendered INSIDE the clip as a bold header label (carries
        /// .sasq-node-title so the container's "T" text styling applies). Null when no title is set.</summary>
        private static Label MakeTitleLabel(ContainerNode node)
        {
            if (string.IsNullOrEmpty(node.Title)) return null;
            var title = new Label(node.Title);
            title.AddToClassList("sasq-node-title");
            title.AddToClassList(TitleClass);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = WidgetPalette.LabelBright;
            title.style.flexShrink = 0f;
            title.style.whiteSpace = WhiteSpace.Normal; // narrow shapes wrap the title, never crop it
            title.style.paddingTop = 8f;
            // 1px — the padding a bare Label carries by default — so the caption starts exactly where the rows
            // beneath it start. A frame can't take the fixed section gutter the other containers share
            // (LayoutTreeBuilder.SectionGutter): its horizontal inset is the chord fit's, computed per shape and
            // per y-band, and it already applies the SAME margin to the title and to the content rows. The
            // hardcoded 10 this replaces predated the fit and double-inset the caption by 9px against its own
            // content. Right stays wider — it only guards the wrap point, and nothing lines up against it.
            title.style.paddingLeft = 1f;
            title.style.paddingRight = 10f;
            return title;
        }

        /// <summary>
        /// Runs after <see cref="StyleApplier"/> in BuildNode's shared tail (the Columns fix-up precedent):
        /// moves node padding onto the content host (the absolute fill/scrim layers must stay edge-to-edge),
        /// transfers the alignment pair onto the content host so Style.alignItems / Style.justify anchor the
        /// CONTENT instead of shoving the whole layer stack, re-asserts the clip, and registers the geometry
        /// pass (circle/pill radius + aspect lock + split seam placement — all need resolved sizes).
        /// </summary>
        internal static void PostStyleFixup(VisualElement frameRoot, ContainerNode node, string splitSeedKey = null)
        {
            var settings = node.Frame;
            // Direct child only: a split frame has NO content host of its own, and a descendant Q
            // would grab a NESTED frame's — transferring this node's padding onto the wrong frame.
            var contentHost = DirectChildByClass(frameRoot, ContentClass);

            if (contentHost != null)
            {
                contentHost.style.paddingTop = frameRoot.style.paddingTop;
                contentHost.style.paddingBottom = frameRoot.style.paddingBottom;
                contentHost.style.paddingLeft = frameRoot.style.paddingLeft;
                contentHost.style.paddingRight = frameRoot.style.paddingRight;
                frameRoot.style.paddingTop = 0f;
                frameRoot.style.paddingBottom = 0f;
                frameRoot.style.paddingLeft = 0f;
                frameRoot.style.paddingRight = 0f;

                if (node.Style.alignItems.isOverridden)
                    contentHost.style.alignItems = frameRoot.style.alignItems;
                if (node.Style.justify.isOverridden)
                    contentHost.style.justifyContent = frameRoot.style.justifyContent;
                frameRoot.style.alignItems = Align.Stretch;
                frameRoot.style.justifyContent = Justify.FlexStart;
            }

            // The Alignment row (Style.alignSelf → Left/Center/Right) only means something for a
            // fixed-width frame. With Width unset the frame is documented to fill the row — but a
            // lingering alignment override would make Yoga shrink-wrap it to its content instead
            // (the "reset Width → frame collapses to half" bug). StyleApplier writes an inline width
            // only when the node/theme overrides one, so its absence is the reliable "no fixed width"
            // signal; stretch wins in that case.
            if (frameRoot.style.width.keyword == StyleKeyword.Null)
                frameRoot.style.alignSelf = Align.Stretch;

            // The clip IS the widget contract (contrast .sasq-group's deliberate overflow:visible).
            frameRoot.style.overflow = Overflow.Hidden;

            // Rebuilds start at the previously converged height, not the 48px floor (see the seed cache).
            if (settings.Shape == FrameShape.DiagonalSplit && splitSeedKey != null
                && _splitMinHeightSeeds.TryGetValue(splitSeedKey, out var settledMinHeight))
            {
                frameRoot.style.minHeight = settledMinHeight;
            }

            // Build-time settle seed for aspect-locked shapes with a KNOWN width: pre-apply the height
            // and safe-area band margins the geometry pass would otherwise derive over 2-3 chained
            // passes from the 48px-ish natural height. A rebuilt frame (the designer's debounced
            // commit) then passes FlowFitGeometryValid on its FIRST pass, so the chord fit runs — with
            // its seeded per-row state (_flowFitSeeds) — before the first paint: the commit lands
            // pixel-identical, with headroom left in Unity's per-update layout budget.
            if (settings.Shape != FrameShape.DiagonalSplit && AspectLocked(settings)
                && frameRoot.style.width.keyword == StyleKeyword.Undefined)
            {
                float w0 = frameRoot.style.width.value.value;
                if (w0 > 0f)
                {
                    float h0 = AspectTargetHeight(settings, w0);
                    frameRoot.style.minHeight = StyleKeyword.Null;
                    frameRoot.style.height = h0;
                    float inset = ShapeOuterRadius(frameRoot, settings, w0, h0) * CornerSafeInsetFactor;
                    var title = DirectChildByClass(frameRoot, TitleClass);
                    if (title != null)
                        SetMarginGuarded(title, top: inset, right: float.NaN, bottom: 0f, left: float.NaN);
                    if (contentHost != null)
                        SetMarginGuarded(contentHost,
                            top: title != null ? 0f : inset, right: 0f, bottom: inset, left: 0f);
                }
            }

            frameRoot.RegisterCallback<GeometryChangedEvent>(_ => ApplyShapeGeometry(frameRoot, node, splitSeedKey));

            // The panes are ABSOLUTE layers, so a change to pane CONTENT height — a live row-resize drag, a
            // field that grows/shrinks, a validation row appearing — never alters frameRoot's OWN geometry,
            // and the frameRoot GeometryChanged above therefore won't fire. Without another trigger the frame
            // only catches up on the next full rebuild (the "make a row bigger and the frame doesn't change
            // until I reopen the designer" half of the bug). Re-run the grow when a pane
            // SUBTREE resizes so the frame tracks its content live. GrowSplitToContent is self-contained and
            // its write is guarded (deferred, only on >0.5px change) with the run-length oscillation pin as a
            // backstop, so this feedback path converges instead of looping.
            if (settings.Shape == FrameShape.DiagonalSplit)
            {
                EventCallback<GeometryChangedEvent> reGrow = _ => GrowSplitToContent(frameRoot, node, splitSeedKey);
                foreach (var clip in frameRoot.Children())
                {
                    if (!clip.ClassListContains(PaneClipClass)) continue;
                    var pane = DirectChildByClass(clip, PaneClass);
                    if (pane == null) continue;
                    foreach (var subEl in pane.Children())
                    {
                        if (!(subEl.userData is LayoutNode)) continue;
                        subEl.RegisterCallback(reGrow);
                        // The subtree carries flexGrow:1 (its background fills the pane to the seam), so it
                        // does NOT shrink back when its content shrinks — its filled height stays pinned to
                        // the frame, so ITS GeometryChanged fires only on GROW (content overflowing the current
                        // frame). Register the in-flow rows too: a row's own box tracks its content in BOTH
                        // directions, so a row-resize back down fires here and the frame shrinks with it —
                        // without this the frame grew with a row but could never go small again (the core
                        // "row stuck large" bug).
                        foreach (var rowEl in subEl.Children())
                            if (rowEl.resolvedStyle.position != Position.Absolute)
                                rowEl.RegisterCallback(reGrow);
                    }
                }
            }
        }

        /// <summary>
        /// Designer live-poke entry: re-runs the style fix-up + shape geometry immediately after the
        /// designer re-applied node styles to an ALREADY-BUILT frame root (a continuous properties-panel
        /// drag). Radius/padding/alignment land inline with NO layout change, so the GeometryChanged-
        /// driven pass never fires — the canvas kept stale safe-area margins until the debounced commit
        /// snapped them in (the "release pop"). Running the pass here makes the clip + safe area track
        /// the drag in real time; margin writes that DO change layout re-fire the registered geometry
        /// pass, which converges exactly like a resize. Unlike <see cref="PostStyleFixup"/> the padding/
        /// alignment transfer is gated per-override: a null-theme live poke only rewrites OVERRIDDEN
        /// sides on the root, so unconditionally copying all four would wipe theme-driven padding the
        /// build-time transfer had parked on the content host.
        /// </summary>
        internal static void RefreshLiveGeometry(VisualElement frameRoot, ContainerNode node)
        {
            var contentHost = DirectChildByClass(frameRoot, ContentClass);
            if (contentHost != null)
            {
                var s = node.Style;
                if (s.paddingTop.isOverridden) { contentHost.style.paddingTop = frameRoot.style.paddingTop; frameRoot.style.paddingTop = 0f; }
                if (s.paddingBottom.isOverridden) { contentHost.style.paddingBottom = frameRoot.style.paddingBottom; frameRoot.style.paddingBottom = 0f; }
                if (s.paddingLeft.isOverridden) { contentHost.style.paddingLeft = frameRoot.style.paddingLeft; frameRoot.style.paddingLeft = 0f; }
                if (s.paddingRight.isOverridden) { contentHost.style.paddingRight = frameRoot.style.paddingRight; frameRoot.style.paddingRight = 0f; }
                if (s.alignItems.isOverridden) { contentHost.style.alignItems = frameRoot.style.alignItems; frameRoot.style.alignItems = Align.Stretch; }
                if (s.justify.isOverridden) { contentHost.style.justifyContent = frameRoot.style.justifyContent; frameRoot.style.justifyContent = Justify.FlexStart; }
            }

            if (frameRoot.style.width.keyword == StyleKeyword.Null)
                frameRoot.style.alignSelf = Align.Stretch;

            frameRoot.style.overflow = Overflow.Hidden;
            ApplyShapeGeometry(frameRoot, node, null);
        }

        /// <summary>
        /// Geometry-dependent shape work, re-run on every resize: Circle/Pill corner radii from the
        /// resolved size (UITK has no percentage radius), the aspect-lock height write, and the
        /// DiagonalSplit clipper/seam placement.
        /// </summary>
        private static void ApplyShapeGeometry(VisualElement frameRoot, ContainerNode node, string splitSeedKey)
        {
            var settings = node.Frame;
            float w = frameRoot.layout.width;
            float h = frameRoot.layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

            switch (settings.Shape)
            {
                case FrameShape.Circle:
                    SetAllCorners(frameRoot, Mathf.Min(w, h) * 0.5f);
                    break;
                case FrameShape.Pill:
                    SetAllCorners(frameRoot, h * 0.5f);
                    break;
                case FrameShape.DiagonalSplit:
                    LayoutSplit(frameRoot, settings, w, h);
                    GrowSplitToContent(frameRoot, node, splitSeedKey);
                    break;
            }

            ApplyContentSafeArea(frameRoot, settings, w, h);

            // Fill + overlay silhouettes: their visible edges must be their OWN antialiased rounded
            // geometry — the parent's rounded overflow clip is a binary stencil whose edge is a hard
            // pixel staircase (the "jagged circle" bug). Keep their radii concentric with the ring
            // (shape radius − border width; the layers sit inside the border) and size the fill's
            // background for the resolved box. Direct children only — frames nest.
            var fillLayer = DirectChildByClass(frameRoot, FillClass);
            if (fillLayer != null)
                LayoutFillBackground(frameRoot, fillLayer, settings); // sets the radii too
            var overlayLayer = DirectChildByClass(frameRoot, OverlayClass);
            if (overlayLayer != null)
                SetAllCorners(overlayLayer,
                    Mathf.Max(0f, ShapeOuterRadius(frameRoot, settings, w, h) - EffectiveBorderWidth(frameRoot)));

            // Aspect lock (Circle is always 1:1). This deliberately overrides the container "height is a
            // grow-only floor" convention: the shape owns its height, derived from the resolved width.
            if (AspectLocked(settings))
            {
                float targetH = AspectTargetHeight(settings, w);
                if (Mathf.Abs(h - targetH) > 0.5f) // convergence guard: only write when actually off
                {
                    frameRoot.style.minHeight = StyleKeyword.Null;
                    frameRoot.style.height = targetH;
                }
            }
        }

        private static bool AspectLocked(FrameSettings settings)
        {
            return settings.Shape == FrameShape.Circle
                || ((settings.Shape == FrameShape.RoundedRect || settings.Shape == FrameShape.Pill) && settings.LockAspect);
        }

        /// <summary>
        /// This node's height is DERIVED from its width, so nothing may offer to resize it vertically — the
        /// aspect pass clears <c>minHeight</c> and writes a computed <c>height</c> every geometry pass, so a
        /// bottom-edge grip drags a value that is overwritten before the next paint.
        /// <para>The node-level overload is what every caller outside this file wants: the designer's Size
        /// section, the live grip attach and the export's grip emitter all ask the same question about a
        /// <see cref="LayoutNode"/>, and three hand-rolled copies of the shape test is how one of them drifts
        /// out of step with the other two.</para>
        /// </summary>
        internal static bool AspectLocked(LayoutNode node)
            => node is ContainerNode c && c.Type == ContainerType.Frame && AspectLocked(c.Frame);

        private static float AspectTargetHeight(FrameSettings settings, float w)
        {
            float ratio = settings.Shape == FrameShape.Circle ? 1f : settings.AspectRatio;
            return w / Mathf.Max(FrameSettings.MinAspect, ratio);
        }

        /// <summary>
        /// Keeps the frame's in-flow content inside the shape's safe area. The VERTICAL band is still the
        /// largest-inscribed-rectangle inset (a circle of radius R → top/bottom margins of R·(1−1/√2)),
        /// but HORIZONTAL clearance is per visual row (<see cref="FitContentToShape"/>): a uniform
        /// inscribed-square inset was wrong in both directions at once — rows that overflow the band into
        /// the narrowing top/bottom of a circle still wrapped against the full square width and clipped
        /// (the rich-text toolbar "Size" button), while rows at the wide middle wasted the chord width the
        /// shape actually offers there ("Name could sit further left"). MARGINS, not padding: the node's
        /// own padding rides the content host (<see cref="PostStyleFixup"/>, pinned by tests) and must stay
        /// separable.
        /// Runs from the geometry pass — the radius needs resolved sizes. The band margins are guarded
        /// direct writes (two targets, converge immediately); the per-row fit runs INLINE here so a
        /// shape change paints fitted in the SAME update (deferring it painted one stale frame — the
        /// visible "snap" when a width drag is released). One fit per frame-level pass is within the
        /// layout budget; the per-row hooks are the unbounded path and go through the once-per-update
        /// gate (<see cref="RequestFlowFit"/>).
        /// DiagonalSplit is straight-edged — its panes get seam-aware padding in <see cref="LayoutSplit"/>.
        /// </summary>
        private static void ApplyContentSafeArea(VisualElement frameRoot, FrameSettings settings, float w, float h)
        {
            if (settings.Shape == FrameShape.DiagonalSplit) return;

            float inset = ShapeOuterRadius(frameRoot, settings, w, h) * CornerSafeInsetFactor;

            // Direct children only — a descendant Q would grab a NESTED frame's title/content host
            // when this frame lacks its own (frames nest inside frames).
            var title = DirectChildByClass(frameRoot, TitleClass) as Label;
            var contentHost = DirectChildByClass(frameRoot, ContentClass);

            // Title left/right are NaN = untouched: the chord fit owns the title's horizontal margins,
            // and re-zeroing them every geometry pass would fight it — alternating 0/chord on resize.
            if (title != null)
                SetMarginGuarded(title, top: inset, right: float.NaN, bottom: 0f, left: float.NaN);
            if (contentHost != null)
                SetMarginGuarded(contentHost,
                    top: title != null ? 0f : inset, right: 0f, bottom: inset, left: 0f);

            // Self-gating: RunFlowFitInline verifies the OBSERVED geometry (aspect height settled, band
            // margins applied) before inking row margins — writes made just above land next pass, and a
            // fit against pre-write observations over-insets wildly (the "36 → 56" rebuild over-fit).
            RunFlowFitInline(frameRoot, settings, w, h);
        }

        /// <summary>Hook-only walk (no fitting, idempotent): title + content host + its direct rows.
        /// Used when a fit is SKIPPED on mid-flight geometry so the skipped pass's own writes can still
        /// trigger the first real fit in the next pass of the same update.</summary>
        private static void HookFlowWave(VisualElement frameRoot, FrameSettings settings)
        {
            var title = DirectChildByClass(frameRoot, TitleClass);
            if (title != null) HookFlowRefit(title, frameRoot, settings);
            var contentHost = DirectChildByClass(frameRoot, ContentClass);
            if (contentHost == null) return;
            HookFlowRefit(contentHost, frameRoot, settings);
            foreach (var child in contentHost.Children())
                HookFlowRefit(child, frameRoot, settings);
        }

        /// <summary>Writes the four margins only when they actually change (>0.5px) — the geometry pass
        /// re-runs on every resize, and an unconditional write would re-dirty layout forever.
        /// NaN = this side is not ours to write (another pass owns it). Returns whether anything was
        /// written, i.e. whether row positions observed THIS pass are now stale.</summary>
        private static bool SetMarginGuarded(VisualElement el, float top, float right, float bottom, float left)
        {
            bool wrote = false;
            if (!float.IsNaN(top) && MarginDiffers(el.style.marginTop, top)) { el.style.marginTop = top; wrote = true; }
            if (!float.IsNaN(right) && MarginDiffers(el.style.marginRight, right)) { el.style.marginRight = right; wrote = true; }
            if (!float.IsNaN(bottom) && MarginDiffers(el.style.marginBottom, bottom)) { el.style.marginBottom = bottom; wrote = true; }
            if (!float.IsNaN(left) && MarginDiffers(el.style.marginLeft, left)) { el.style.marginLeft = left; wrote = true; }
            return wrote;
        }

        private static bool MarginDiffers(StyleLength current, float target)
        {
            // keyword != Undefined means "no inline pixel value yet" (Undefined IS the has-value state).
            return current.keyword != StyleKeyword.Undefined
                || Mathf.Abs(current.value.value - target) > 0.5f;
        }

        // ---------------- shape flow (per-row horizontal safe area) ----------------

        /// <summary>Minimum breathing room a fitted row keeps from the curve, ON TOP of the chord math.
        /// Only applied where the curve actually reaches (straight edges stay flush, so plain
        /// RoundedRect layouts keep their current spacing).</summary>
        private const float FlowEdgePad = 2f;
        /// <summary>Clearance inside the CLIP radius so antialiased edges never kiss the curve.</summary>
        private const float CurveClearancePad = 1f;
        /// <summary>Chord insets quantize UP to this step so sub-step band drift can't re-dirty layout.</summary>
        private const float FlowInsetStep = 2f;
        /// <summary>Children at most this tall are one visual row and are fitted whole; taller PLAIN
        /// containers are descended so each of their rows meets the curve at its own height.</summary>
        private const float FlowRowMaxHeight = 44f;
        /// <summary>Never squeeze a row narrower than this — past it the frame is simply too small and
        /// clipping is the honest outcome (the pre-existing tiny-frame behavior).</summary>
        private const float MinFlowRowWidth = 40f;

        /// <summary>
        /// The width a split pane's content box is never padded below, mirroring <see cref="MinFlowRowWidth"/>
        /// for the curved shapes: past it, clipping at the seam is the honest outcome and a zero-width field is
        /// not. WITHOUT this clamp the seam pad is a positive feedback loop, MEASURED live at −26°: the pad
        /// narrows the pane → a cell hits ColumnCellMinFieldWidth and stacks its label above → that row is 16px
        /// taller → the frame is 16px taller → tiltSpan (= |tan(angle)|·h/2) grows by tan(26°)·16/2 ≈ 3.9px →
        /// the pad widens by ~4px → repeat, flipping every tick forever. The capture showed pane height
        /// alternating 63↔79 and pane content width 128↔124 at 39 flips per 40 ticks, in BOTH the live
        /// Inspector and the designer canvas. Clamping the pad breaks the ratchet at its only unbounded term.
        /// </summary>
        private const float MinPaneContentWidth = 40f;
        private const int FlowMaxDepth = 8;
        /// <summary>Marker: this element already re-runs the fit when its geometry changes. Internal so
        /// PinCellFieldMargins can yield margin ownership to the fit (they'd alternate writes forever).</summary>
        internal const string FlowHookedClass = "sasq-flow-hooked";

        /// <summary>Per-element fit bookkeeping.</summary>
        private sealed class FlowFitState
        {
            public float floorL, floorR;                     // the element's own margins, kept as a floor
            public float lastL = float.NaN, lastR = float.NaN; // last margin target WE computed
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, FlowFitState>
            _flowFitStates = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, FlowFitState>();

        /// <summary>
        /// Fit-state CONTINUITY across in-place rebuilds: the designer's debounced commit swaps the
        /// frame for a freshly built subtree (RebuildNodeInPlace), and a fresh fit re-quantizes exact
        /// chord targets while the on-screen margins may sit shrink-dead-zone-HELD up to 4px away — a
        /// one-frame ≤4px shift at commit that reads as a jolt, and near a wrap threshold (a toolbar
        /// button hopping rows at frame width ~242) flips a whole row: a huge jolt from a tiny delta.
        /// Seeds are keyed by the frame root's PARENT element (RebuildNodeInPlace inserts the new
        /// element into the same parent at the same index, so the parent is what survives) → a
        /// structural path (frame child index + title/content + child-index chain). A rebuilt element
        /// at the same path adopts the SAME FlowFitState instance, so the dead-zone reproduces the
        /// exact drag-held margins — identical widths, identical wrap state, pixel-identical commit.
        /// Structure changes shift paths and simply miss (fresh exact fit — the change is its own
        /// motion); a full canvas rebuild drops the parent and the seeds with it.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Dictionary<string, FlowFitState>>
            _flowFitSeeds = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Dictionary<string, FlowFitState>>();

        private static FlowFitState GetFlowFitState(VisualElement el, VisualElement frameRoot, string path)
        {
            if (_flowFitStates.TryGetValue(el, out var st)) return st;
            var host = frameRoot.parent;
            if (host != null)
            {
                var seeds = _flowFitSeeds.GetValue(host, _ => new Dictionary<string, FlowFitState>());
                if (!seeds.TryGetValue(path, out st))
                {
                    st = new FlowFitState();
                    seeds[path] = st;
                }
            }
            else st = new FlowFitState();
            _flowFitStates.Add(el, st);
            return st;
        }

        /// <summary>Per-frame-root fit gating (weak keys — rebuilt frames drop out).</summary>
        private sealed class FlowFitRequest
        {
            public FrameSettings settings; // latest requester wins — a live poke may have swapped the shape
            public bool scheduled;         // the scheduler-phase catch-up (+ flag reset) is queued
            public bool ranInline;         // an inline fit already ran during THIS panel update
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, FlowFitRequest>
            _flowFitRequests = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, FlowFitRequest>();

        /// <summary>
        /// The HOOK path into <see cref="FitContentToShape"/>: the fit runs INLINE — corrections land in
        /// the same painted frame as the geometry change that caused them — but AT MOST ONCE per panel
        /// update per frame root. History of this trade-off, both halves user-visible: (1) the first cut
        /// ran the fit synchronously from EVERY hooked row's GeometryChanged, so one geometry burst
        /// fanned out into N whole-frame fits whose margin writes each chained another layout pass onto
        /// the SAME update — blew Unity's per-update layout budget ("Layout update is struggling…" spam
        /// on every repaint near a shaped frame). (2) Deferring ALL fits to the scheduler killed the
        /// spam but painted every correction one update late — invisible mid-drag, but the debounced
        /// commit's RebuildNodeInPlace re-settles the fresh subtree over a couple of updates and each
        /// step then showed unfitted-then-corrected: the visible "snap" on width-drag release. The
        /// once-per-update flag keeps both properties: bounded write-rounds (≤1 hook fit per update —
        /// budget-safe) AND same-update correctness. Hooks that fire after the flag is set fall through
        /// to the scheduled catch-up (also the flag reset), which usually no-ops.
        /// </summary>
        private static void RequestFlowFit(VisualElement frameRoot, FrameSettings settings)
        {
            var req = _flowFitRequests.GetValue(frameRoot, _ => new FlowFitRequest());
            req.settings = settings;
            EnsureFitScheduled(frameRoot, req);
            if (req.ranInline) return;
            float fw = frameRoot.layout.width, fh = frameRoot.layout.height;
            if (float.IsNaN(fw) || fw <= 0f || float.IsNaN(fh) || fh <= 0f) return;
            // Mid-flight geometry: DON'T consume the once-per-update slot — a later wave of this same
            // update (after the pending band/height writes lay out) must still be able to fit inline.
            // The armed catch-up backstops the case where no later wave arrives.
            if (!FlowFitGeometryValid(frameRoot, settings, fw, fh)) return;
            req.ranInline = true;
            FitContentToShape(frameRoot, req.settings, fw, fh);
        }

        /// <summary>The frame-level entry (<see cref="ApplyContentSafeArea"/>): fits inline when the
        /// observed geometry is valid, stamping the once-per-update flag so hook callbacks in the same
        /// update don't re-run. When the geometry is mid-flight (the safe-area pass just moved the
        /// bands, or the aspect height write is pending), it hooks the wave instead — the pending
        /// writes re-fire GeometryChanged next pass of the SAME update, and that wave fits against
        /// valid bands, so there is still no stale paint.</summary>
        private static void RunFlowFitInline(VisualElement frameRoot, FrameSettings settings, float w, float h)
        {
            var req = _flowFitRequests.GetValue(frameRoot, _ => new FlowFitRequest());
            req.settings = settings;
            EnsureFitScheduled(frameRoot, req);
            if (!FlowFitGeometryValid(frameRoot, settings, w, h))
            {
                // A freshly (re)built subtree carries NO hooks yet — the fit traversal is what installs
                // them — so without this hook-only walk the pending writes' wave would fire events
                // nobody listens to and the first fit would wait a whole update (stale first paint).
                // The content host is the load-bearing hook: the band margin write moves ITS rect even
                // when the rows' local rects stay put.
                HookFlowWave(frameRoot, settings);
                return;
            }
            req.ranInline = true;
            FitContentToShape(frameRoot, settings, w, h);
        }

        /// <summary>
        /// Layout-based validity gate for the chord fit: the fit OBSERVES row positions from the last
        /// completed layout pass, so it must not run while those observations are known-stale — (a) an
        /// aspect-locked shape whose resolved height hasn't caught up with its width (the height write
        /// lands at the end of the geometry pass), or (b) safe-area band margins whose inline targets
        /// haven't been laid out yet (a rebuilt frame's first passes see rows at y≈0 and would ink
        /// wildly over-inset margins — the "36 → 56" rebuild over-fit — which the once-per-update gate
        /// would then freeze for the whole update). Compares OBSERVED layout offsets, never
        /// resolvedStyle, against the inset the CURRENT (w,h) demands; only the top side matters (row
        /// y-bands stack from the top — the bottom band margin never moves a row). Tolerance 1px: the
        /// guarded band writes legitimately leave up to 0.5px drift.
        /// </summary>
        private static bool FlowFitGeometryValid(VisualElement frameRoot, FrameSettings settings, float w, float h)
        {
            if (AspectLocked(settings) && Mathf.Abs(h - AspectTargetHeight(settings, w)) > 0.5f)
                return false;

            float inset = ShapeOuterRadius(frameRoot, settings, w, h) * CornerSafeInsetFactor;
            float frameTop = frameRoot.resolvedStyle.borderTopWidth + frameRoot.resolvedStyle.paddingTop;
            if (float.IsNaN(frameTop)) frameTop = 0f;
            var title = DirectChildByClass(frameRoot, TitleClass);
            if (title != null
                && (float.IsNaN(title.layout.y) || Mathf.Abs(title.layout.y - (frameTop + inset)) > 1f))
                return false;
            var contentHost = DirectChildByClass(frameRoot, ContentClass);
            if (contentHost != null)
            {
                float expectedY = title != null ? title.layout.yMax : frameTop + inset;
                if (float.IsNaN(contentHost.layout.y) || Mathf.Abs(contentHost.layout.y - expectedY) > 1f)
                    return false;
            }
            return true;
        }

        /// <summary>Arms the scheduler-phase visit for this frame root (coalesced): clears the
        /// once-per-update flag — the scheduler runs before layout, so it marks the update boundary —
        /// and runs a catch-up fit for anything a post-flag hook saw that the inline fit missed.
        /// Re-arms itself while the geometry is still mid-flight instead of fitting from stale bands.</summary>
        private static void EnsureFitScheduled(VisualElement frameRoot, FlowFitRequest req)
        {
            if (req.scheduled) return;
            req.scheduled = true;
            frameRoot.schedule.Execute(() =>
            {
                req.scheduled = false;
                req.ranInline = false;
                if (frameRoot.panel == null) return;
                float fw = frameRoot.layout.width, fh = frameRoot.layout.height;
                if (float.IsNaN(fw) || fw <= 0f || float.IsNaN(fh) || fh <= 0f) return;
                if (!FlowFitGeometryValid(frameRoot, req.settings, fw, fh))
                {
                    EnsureFitScheduled(frameRoot, req);
                    return;
                }
                FitContentToShape(frameRoot, req.settings, fw, fh);
            });
        }

        /// <summary>
        /// Per-row horizontal safe-area: gives every visual row inside the content host (and the title)
        /// margins derived from the shape's actual chord at that row's own height. Rows near a circle's
        /// top/bottom get narrower — so wrapping controls (the rich-text toolbar, chip rows) wrap BEFORE
        /// the curve can crop them — while rows at the wide middle extend into width the old uniform
        /// square inset threw away. Tall PLAIN containers (cells, cards, group bodies) are descended so
        /// each of their rows is fitted at its own height; widget roots (any derived VisualElement type),
        /// flex-wrap rows and nested frames are fitted as single units. Positions are OBSERVED from
        /// layout (no padding bookkeeping), so the pass self-corrects across geometry passes; writes are
        /// quantized, floored at the element's own margins, guarded (>0.5px) and shrink-hysteresis'd, so
        /// the feedback loop converges instead of vibrating (the split-grow lessons). NEVER call this
        /// directly — every trigger goes through the gated entries (<see cref="RunFlowFitInline"/> for
        /// frame-level passes, <see cref="RequestFlowFit"/> for per-element hooks): unbounded synchronous
        /// fits per update is what blew the layout budget, and fully-deferred fits painted one-frame-
        /// stale margins (the release snap). The gate keeps it to ≤1 hook fit per update, inline.
        /// </summary>
        private static void FitContentToShape(VisualElement frameRoot, FrameSettings settings, float w, float h)
        {
            float rad = Mathf.Min(ShapeOuterRadius(frameRoot, settings, w, h), Mathf.Min(w, h) * 0.5f);
            if (rad < 0.5f) return; // square-cornered — straight edges can't crop rows
            float rClip = Mathf.Max(0f, rad - EffectiveBorderWidth(frameRoot) - CurveClearancePad);

            // Seed-path root: the frame's slot in its surviving parent (see _flowFitSeeds).
            string basePath = frameRoot.parent != null ? frameRoot.parent.IndexOf(frameRoot).ToString() : "x";

            var title = DirectChildByClass(frameRoot, TitleClass);
            if (title != null)
            {
                HookFlowRefit(title, frameRoot, settings);
                FitFlowChild(title, frameRoot, settings, w, h, rad, rClip, fitLeft: true, fitRight: true,
                    depth: 0, path: basePath + ":t");
            }
            var contentHost = DirectChildByClass(frameRoot, ContentClass);
            if (contentHost != null)
                FitFlowChildren(contentHost, frameRoot, settings, w, h, rad, rClip, depth: 0, path: basePath + ":c");
        }

        private static void FitFlowChildren(VisualElement container, VisualElement frameRoot,
            FrameSettings settings, float w, float h, float rad, float rClip, int depth, string path)
        {
            HookFlowRefit(container, frameRoot, settings);
            bool row = container.resolvedStyle.flexDirection == FlexDirection.Row;

            // In a ROW, only the first/last rendered child own the outer edges; interior children sit
            // between siblings, where a margin would just shove its neighbor instead of clearing the curve.
            VisualElement first = null, last = null;
            if (row)
            {
                foreach (var c in container.Children())
                {
                    if (c.resolvedStyle.position == Position.Absolute
                        || c.resolvedStyle.display == DisplayStyle.None) continue;
                    if (first == null) first = c;
                    last = c;
                }
            }

            int ci = -1; // raw child index — skipped children still consume one, so paths stay stable
            foreach (var child in container.Children())
            {
                ci++;
                HookFlowRefit(child, frameRoot, settings); // hook even hidden rows — showing one must refit
                if (child.resolvedStyle.position == Position.Absolute
                    || child.resolvedStyle.display == DisplayStyle.None) continue;
                bool fl = !row || child == first;
                bool fr = !row || child == last;
                FitFlowChild(child, frameRoot, settings, w, h, rad, rClip, fl, fr, depth + 1, path + "/" + ci);
            }
        }

        private static void FitFlowChild(VisualElement el, VisualElement frameRoot,
            FrameSettings settings, float w, float h, float rad, float rClip, bool fitLeft, bool fitRight,
            int depth, string path)
        {
            var band = RectInFrame(el, frameRoot);
            if (float.IsNaN(band.x) || float.IsNaN(band.height) || band.width <= 0f) return;

            // Descend tall PLAIN containers (structural wrappers are always bare VisualElements —
            // cells, cards, the rich-text stacks) so each inner row fits its own height. Everything
            // else — widget roots, flex-wrap rows (they re-flow themselves), nested frames (they flow
            // their OWN content) — is one visual unit and is fitted whole.
            bool descend = depth < FlowMaxDepth
                && band.height > FlowRowMaxHeight
                && el.GetType() == typeof(VisualElement)
                && el.resolvedStyle.flexWrap == Wrap.NoWrap
                && !el.ClassListContains(FrameClass)
                && HasInFlowChild(el);
            if (descend)
            {
                // A container can cross the row threshold at runtime (a toolbar column growing when a
                // chip row opens): if it was previously FITTED, hand its margins back to its own floor —
                // from here on its children fit individually and a stale whole-unit inset would ratchet.
                if (_flowFitStates.TryGetValue(el, out var prev))
                {
                    if (!float.IsNaN(prev.lastL) && MarginDiffers(el.style.marginLeft, prev.floorL))
                        el.style.marginLeft = prev.floorL;
                    if (!float.IsNaN(prev.lastR) && MarginDiffers(el.style.marginRight, prev.floorR))
                        el.style.marginRight = prev.floorR;
                    prev.lastL = float.NaN;
                    prev.lastR = float.NaN;
                }
                FitFlowChildren(el, frameRoot, settings, w, h, rad, rClip, depth, path);
                return;
            }
            if (!fitLeft && !fitRight) return;

            ChordInterval(w, h, rad, rClip, band.yMin, band.yMax, out float safeL, out float safeR);

            var rs = el.resolvedStyle;
            float curML = float.IsNaN(rs.marginLeft) ? 0f : rs.marginLeft;
            float curMR = float.IsNaN(rs.marginRight) ? 0f : rs.marginRight;
            var st = GetFlowFitState(el, frameRoot, path);

            // (Re)baseline the floor from the element's OWN margins whenever we haven't written yet or
            // someone else (StyleApplier, a live poke) wrote since — authored spacing stays the minimum.
            // A foreign INLINE write re-baselines from that inline value, never from resolvedStyle:
            // resolved still shows OUR previous chord inset until the next layout completes, and
            // absorbing it into the floor would ratchet the margin (it could never shrink again after
            // a debounced-commit style re-application).
            float inlineL = InlineMarginOrNaN(el.style.marginLeft);
            float inlineR = InlineMarginOrNaN(el.style.marginRight);
            if (float.IsNaN(st.lastL) || float.IsNaN(inlineL) || Mathf.Abs(inlineL - st.lastL) > 0.6f)
                st.floorL = float.IsNaN(inlineL) ? curML : inlineL;
            if (float.IsNaN(st.lastR) || float.IsNaN(inlineR) || Mathf.Abs(inlineR - st.lastR) > 0.6f)
                st.floorR = float.IsNaN(inlineR) ? curMR : inlineR;
            // Base-fields keep PinCellFieldMargins' normalization (floor 0, not the panel USS margins —
            // Unity's Inspector styles them -2px vs the default sheet's +3px, the cross-surface drift the
            // pin exists to kill). The pin itself stands down once this element is flow-hooked.
            if (el.ClassListContains("unity-base-field")) { st.floorL = 0f; st.floorR = 0f; }

            if (fitLeft)
            {
                float naturalL = band.xMin - curML; // where the box would sit with margin 0 (observed)
                float target = Mathf.Max(st.floorL, QuantizeInset(safeL - naturalL));
                // Shrink dead-zone (2 steps): a row whose fit changes its own height can wobble the band
                // by a step; growing stays immediate so a clipped row is never left clipped.
                if (!float.IsNaN(st.lastL) && target < st.lastL && target > st.lastL - 2f * FlowInsetStep)
                    target = st.lastL;
                if (MarginDiffers(el.style.marginLeft, target)) el.style.marginLeft = target;
                st.lastL = target;
            }
            if (fitRight)
            {
                float naturalR = band.xMax + curMR; // stretch rows: right margin insets the right edge
                float target = Mathf.Max(st.floorR, QuantizeInset(naturalR - safeR));
                if (!float.IsNaN(st.lastR) && target < st.lastR && target > st.lastR - 2f * FlowInsetStep)
                    target = st.lastR;
                if (MarginDiffers(el.style.marginRight, target)) el.style.marginRight = target;
                st.lastR = target;
            }
        }

        /// <summary>
        /// The horizontal interval [safeL..safeR] (frame border-box coords) guaranteed inside the clip
        /// curve for every y in the band. Uniform corner-circle math covers all round shapes: Circle
        /// (radius = R, every band is in the curve), Pill (cap circles of radius h/2 at each end) and
        /// RoundedRect (corner circles of the effective radius; bands between the corner strips get 0 —
        /// straight edges never crop). Bands overflowing the frame get the honest full inset.
        /// </summary>
        private static void ChordInterval(float w, float h, float rad, float rClip, float y0, float y1,
            out float safeL, out float safeR)
        {
            float insetTop = 0f, insetBottom = 0f;
            if (y0 < rad) // band reaches the top corner curve — worst y is the band's top
            {
                float dy = rad - y0;
                float chord = dy >= rClip ? 0f : Mathf.Sqrt(rClip * rClip - dy * dy);
                insetTop = rad - chord;
            }
            if (y1 > h - rad) // band reaches the bottom corner curve — worst y is the band's bottom
            {
                float dy = y1 - (h - rad);
                float chord = dy >= rClip ? 0f : Mathf.Sqrt(rClip * rClip - dy * dy);
                insetBottom = rad - chord;
            }
            float inset = Mathf.Max(insetTop, insetBottom);
            if (inset > 0.01f) inset += FlowEdgePad; // pad only where the curve actually reaches
            safeL = inset;
            safeR = w - inset;
            if (safeR - safeL < MinFlowRowWidth)
            {
                float c = (safeL + safeR) * 0.5f;
                safeL = c - MinFlowRowWidth * 0.5f;
                safeR = c + MinFlowRowWidth * 0.5f;
            }
        }

        /// <summary>Layout-space rect of <paramref name="el"/> in frameRoot coords (transform-free —
        /// spring scales/lifts must not skew the fit). NaN rect when not a layout descendant.</summary>
        private static Rect RectInFrame(VisualElement el, VisualElement frameRoot)
        {
            var r = el.layout;
            float x = r.x, y = r.y;
            for (var p = el.parent; p != frameRoot; p = p.parent)
            {
                if (p == null) return new Rect(float.NaN, float.NaN, 0f, 0f);
                x += p.layout.x;
                y += p.layout.y;
            }
            return new Rect(x, y, r.width, r.height);
        }

        /// <summary>Re-REQUESTS the fit when a hooked element's geometry changes. The frame's own
        /// GeometryChanged can't cover this: content-only changes (a chip row opening, a field growing)
        /// never resize the fixed-size frame, so rows would keep stale insets until a rebuild — the same
        /// no-trigger trap as the split-grow pass. Goes through
        /// <see cref="RequestFlowFit"/>'s once-per-update gate on purpose: a geometry burst fires EVERY
        /// hooked row's callback, and each one running its own whole-frame fit is what blew the
        /// budget.</summary>
        private static void HookFlowRefit(VisualElement el, VisualElement frameRoot, FrameSettings settings)
        {
            if (el.ClassListContains(FlowHookedClass)) return;
            el.AddToClassList(FlowHookedClass);
            el.RegisterCallback<GeometryChangedEvent>(_ => RequestFlowFit(frameRoot, settings));
        }

        private static bool HasInFlowChild(VisualElement el)
        {
            foreach (var c in el.Children())
                if (c.resolvedStyle.position != Position.Absolute
                    && c.resolvedStyle.display != DisplayStyle.None) return true;
            return false;
        }

        private static float QuantizeInset(float v)
        {
            return v <= 0f ? 0f : Mathf.Ceil(v / FlowInsetStep) * FlowInsetStep;
        }

        private static float InlineMarginOrNaN(StyleLength sl)
        {
            // Undefined IS the has-inline-value state (see MarginDiffers).
            return sl.keyword == StyleKeyword.Undefined ? sl.value.value : float.NaN;
        }

        // ---------------- fill + scrim layers ----------------

        // Smooth display copies of fill textures, keyed by source texture instance ID + source region.
        // Frame fills routinely MINIFY a large texture into a small shape (a 2048px image inside a
        // ~220px circle), and textures imported as Sprite/UI ship WITHOUT mipmaps — bilinear-sampled at
        // ~1/10 scale each screen pixel reads 4 of ~100 covered texels, which renders as severe
        // "pixelated" aliasing. The copy is a downscale-chained, mipmapped, trilinear Texture2D so
        // minification samples a pre-filtered chain instead. Point-filtered sources get the same copy
        // (a decorative backdrop should never render blocky). A sprite's sub-rect is baked INTO its
        // copy (backgrounds can't express a source rect). Static per domain, never saved.
        private static readonly Dictionary<(int id, int x, int y, int w, int h), Texture2D> _smoothFillCopies
            = new Dictionary<(int, int, int, int, int), Texture2D>();

        /// <summary>Copies never need to exceed the size a fill is realistically displayed at; caps the
        /// GPU/CPU cost of an 8K source to a sane editor budget.</summary>
        private const int MaxSmoothCopySize = 1024;

        // Generous soft cap on distinct cached copies. Real projects use a handful of fill images, so this is a
        // memory backstop, not a working limit (hitting it drops the lot and the next rebuild re-creates the
        // few in-view fills — a one-time re-blit). The per-recompile leak is handled by the reload/quit clear.
        private const int MaxSmoothFillCopies = 64;

        static FrameBuilder()
        {
            // The copies are HideAndDontSave Texture2Ds, so without this they orphan as leaked native objects on
            // every recompile/domain reload. Destroy them on reload/quit (mirrors TutorialImageScaler).
            AssemblyReloadEvents.beforeAssemblyReload += ClearSmoothFillCopies;
            EditorApplication.quitting += ClearSmoothFillCopies;
            // The split size caches hold no native objects, so this is hygiene rather than a leak fix —
            // but the hook is already here, and sweeping them with everything else means "cleared on
            // reload" is an enforced contract rather than an accident of them being static.
            AssemblyReloadEvents.beforeAssemblyReload += ClearSplitSizeCaches;
            EditorApplication.quitting += ClearSplitSizeCaches;
        }

        /// <summary>Destroys all cached smooth-fill copies and empties the cache (reload / quit / soft-cap).</summary>
        internal static void ClearSmoothFillCopies()
        {
            foreach (var tex in _smoothFillCopies.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            _smoothFillCopies.Clear();
        }

        /// <summary>
        /// A smooth-sampling stand-in for a fill texture (or a sprite's sub-region of one): the texture
        /// itself when it already minifies cleanly (has mips, not point-filtered), else a cached
        /// mipmapped copy (see _smoothFillCopies). <paramref name="region"/> is in texture pixels,
        /// bottom-left origin (<see cref="Sprite.textureRect"/> convention); null means the full image.
        /// Callers must check ReferenceEquals to know whether the sub-rect is already baked in.
        /// </summary>
        internal static Texture2D SmoothFillTexture(Texture2D tex, Rect? region = null)
        {
            if (tex == null) return null;
            int rx = 0, ry = 0, rw = tex.width, rh = tex.height;
            if (region.HasValue)
            {
                rx = Mathf.Clamp(Mathf.RoundToInt(region.Value.x), 0, tex.width);
                ry = Mathf.Clamp(Mathf.RoundToInt(region.Value.y), 0, tex.height);
                rw = Mathf.Clamp(Mathf.RoundToInt(region.Value.width), 1, tex.width - rx);
                rh = Mathf.Clamp(Mathf.RoundToInt(region.Value.height), 1, tex.height - ry);
            }

            bool pointFiltered = tex.filterMode == FilterMode.Point;
            // Mipped + smooth-filtered minifies fine as-is; small mipless regions barely alias.
            if (!pointFiltered && tex.mipmapCount > 1) return tex;
            if (!pointFiltered && rw <= 256 && rh <= 256) return tex;

            var key = (UnityObjectId.Of(tex), rx, ry, rw, rh);
            if (_smoothFillCopies.TryGetValue(key, out var cached) && cached != null) return cached;

            float scale = Mathf.Min(1f, MaxSmoothCopySize / (float)Mathf.Max(rw, rh));
            int cw = Mathf.Max(1, Mathf.RoundToInt(rw * scale));
            int ch = Mathf.Max(1, Mathf.RoundToInt(rh * scale));

            var prevActive = RenderTexture.active;
            // Sample the source bilinear for the copy even when it's point-imported (restored after) —
            // this is a runtime-instance setting, nothing touches the importer.
            var prevFilter = tex.filterMode;
            tex.filterMode = FilterMode.Bilinear;
            int w = rw, h = rh;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            // The first hop decodes/uncompresses the source and crops the region (UV scale + offset).
            Graphics.Blit(tex, rt,
                new Vector2(rw / (float)tex.width, rh / (float)tex.height),
                new Vector2(rx / (float)tex.width, ry / (float)tex.height));
            tex.filterMode = prevFilter;

            // Downscale in ≤2× hops: one big blit skips texels and bakes in the very aliasing this
            // copy exists to remove; each halving is a clean bilinear reduction.
            while (w > cw * 2 || h > ch * 2)
            {
                int nw = Mathf.Max(cw, w / 2);
                int nh = Mathf.Max(ch, h / 2);
                var next = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rt, next);
                RenderTexture.ReleaseTemporary(rt);
                rt = next; w = nw; h = nh;
            }
            if (w != cw || h != ch)
            {
                var next = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rt, next);
                RenderTexture.ReleaseTemporary(rt);
                rt = next;
            }

            // Read back into a plain mipmapped Texture2D (an RT would drop to black on a GPU device
            // reset). One-time cost per unique fill texture per domain.
            RenderTexture.active = rt;
            var copy = new Texture2D(cw, ch, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = tex.name + " (sasq smooth fill)",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                wrapMode = TextureWrapMode.Clamp
            };
            copy.ReadPixels(new Rect(0, 0, cw, ch), 0, 0);
            copy.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            if (_smoothFillCopies.Count >= MaxSmoothFillCopies) ClearSmoothFillCopies(); // soft cap: drop the lot
            _smoothFillCopies[key] = copy;
            return copy;
        }

        /// <summary>
        /// Adds the absolute edge-to-edge fill layer when a fill source is configured. The bound field
        /// (each object shows its own image) wins over the static GUID texture and live-updates via
        /// TrackPropertyValue — no rebuild. The image renders as the element's BACKGROUND (not an Image
        /// child quad): backgrounds honor the element's own border-radius with ANTIALIASED geometry,
        /// which is the whole trick — UITK's rounded overflow clip is a binary stencil whose edge is a
        /// hard pixel staircase (the "jagged circle fill" bug), so the fill's visible silhouette must
        /// come from its own rounded corners (kept concentric with the frame ring by the geometry pass),
        /// never from the parent mask. Scale mode / zoom / focus are likewise expressed through
        /// background-size/position (<see cref="LayoutFillBackground"/>) instead of transforms, which
        /// would drag the rounded corners out under the jagged mask again.
        /// </summary>
        private static void AddFillLayer(
            VisualElement frameRoot, FrameSettings settings, SerializedObject serializedObject, bool designMode)
        {
            bool hasBinding = !string.IsNullOrEmpty(settings.FillBindingPath);
            bool hasStatic = !string.IsNullOrEmpty(settings.FillTextureGuid);
            if (!hasBinding && !hasStatic) return;

            var fill = new VisualElement();
            fill.AddToClassList(FillClass);
            fill.style.position = Position.Absolute;
            fill.style.left = 0f; fill.style.top = 0f; fill.style.right = 0f; fill.style.bottom = 0f;
            fill.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            if (!designMode) fill.pickingMode = PickingMode.Ignore;

            SerializedProperty boundProp = hasBinding ? serializedObject.FindProperty(settings.FillBindingPath) : null;
            ApplyFillImage(frameRoot, fill, boundProp, settings);

            frameRoot.Add(fill);

            // A configured-but-unresolved fill renders an in-frame hint in Design mode (never live). The fill
            // LIVE-UPDATES from the tracker below, so the hint's visibility has to follow it: decided once at
            // build, assigning the bound field painted the image UNDER a stale "field is empty" banner.
            Label hint = null;
            if (designMode)
            {
                hint = new Label(hasBinding ? "Fill: field is empty" : "Fill: missing texture");
                hint.style.position = Position.Absolute;
                hint.style.left = 0f; hint.style.top = 0f; hint.style.right = 0f; hint.style.bottom = 0f;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                hint.style.color = WidgetPalette.DropSlotText;
                hint.style.backgroundColor = WidgetPalette.PanelWash;
                hint.pickingMode = PickingMode.Ignore;
                // userData carries the resolved image's pixel size (see ApplyFillImage) — null means nothing resolved.
                hint.style.display = fill.userData == null ? DisplayStyle.Flex : DisplayStyle.None;
                frameRoot.Add(hint);
            }

            if (boundProp != null)
            {
                var hintRef = hint;
                fill.TrackPropertyValueWithRefresh(boundProp, p =>
                {
                    ApplyFillImage(frameRoot, fill, p, settings);
                    if (hintRef != null)
                        hintRef.style.display = fill.userData == null ? DisplayStyle.Flex : DisplayStyle.None;
                });
            }
        }

        /// <summary>
        /// Resolves the fill (bound field first, static GUID fallback) onto the fill element's
        /// background. Mipless/point sources route through a smooth mipmapped copy (see
        /// _smoothFillCopies); a sprite's sub-rect rides Background.FromSprite when the source passes
        /// through, or is baked into the smoothed copy otherwise. The image's pixel size is stashed in
        /// userData for <see cref="LayoutFillBackground"/>'s scale-mode math (userData is free here —
        /// only NODE elements carry LayoutNode userData), and sizing is re-applied immediately when the
        /// frame already has a resolved layout (bound-field swaps arrive between geometry passes).
        /// </summary>
        private static void ApplyFillImage(VisualElement frameRoot, VisualElement fill, SerializedProperty boundProp, FrameSettings settings)
        {
            Texture2D tex = null;
            Sprite sprite = null;

            if (boundProp != null && boundProp.propertyType == SerializedPropertyType.ObjectReference)
            {
                var obj = boundProp.objectReferenceValue;
                if (obj is Texture2D t2d) tex = t2d;
                else if (obj is Sprite spr && spr.texture != null) sprite = spr;
            }
            if (tex == null && sprite == null)
                tex = ResolveTextureByGuid(settings.FillTextureGuid);

            bool resolved = false;
            Vector2 imageSize = Vector2.zero;
            if (sprite != null)
            {
                var region = sprite.textureRect; // bottom-left origin, matching the copy blit's UV space
                var smooth = SmoothFillTexture(sprite.texture, region);
                fill.style.backgroundImage = ReferenceEquals(smooth, sprite.texture)
                    ? new StyleBackground(Background.FromSprite(sprite))
                    : new StyleBackground(smooth);
                imageSize = new Vector2(region.width, region.height);
                resolved = true;
            }
            else if (tex != null)
            {
                fill.style.backgroundImage = new StyleBackground(SmoothFillTexture(tex));
                imageSize = new Vector2(tex.width, tex.height);
                resolved = true;
            }
            else
            {
                fill.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            }

            fill.userData = resolved ? (object)imageSize : null;
            fill.style.display = resolved ? DisplayStyle.Flex : DisplayStyle.None;

            float w = frameRoot.layout.width, h = frameRoot.layout.height;
            if (resolved && !float.IsNaN(w) && !float.IsNaN(h) && w > 0f && h > 0f)
                LayoutFillBackground(frameRoot, fill, settings);
        }

        /// <summary>
        /// Sizes and rounds the fill background for the frame's resolved size: corner radii concentric
        /// with the frame ring (shape radius − border width — the fill box sits inside the border), so
        /// the fill's own antialiased rounded geometry forms the visible edge; and background-size /
        /// -position implementing Fit / Stretch / Crop × zoom × focus in pixels (the UITK keywords
        /// can't express zoom). Runs from the geometry pass and on fill re-resolves.
        /// </summary>
        private static void LayoutFillBackground(VisualElement frameRoot, VisualElement fill, FrameSettings settings)
        {
            float w = frameRoot.layout.width, h = frameRoot.layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

            float borderW = EffectiveBorderWidth(frameRoot);
            SetAllCorners(fill, Mathf.Max(0f, ShapeOuterRadius(frameRoot, settings, w, h) - borderW));

            if (!(fill.userData is Vector2 imageSize) || imageSize.x <= 0f || imageSize.y <= 0f) return;
            float fw = Mathf.Max(1f, w - 2f * borderW);
            float fh = Mathf.Max(1f, h - 2f * borderW);

            float zoom = Mathf.Clamp(settings.FillZoom, FrameSettings.MinZoom, FrameSettings.MaxZoom);
            float sw, sh;
            switch (settings.FillScaleMode)
            {
                case ImageBannerScaleMode.StretchToFill:
                    sw = fw * zoom; sh = fh * zoom;
                    break;
                case ImageBannerScaleMode.ScaleAndCrop:
                {
                    float s = Mathf.Max(fw / imageSize.x, fh / imageSize.y) * zoom;
                    sw = imageSize.x * s; sh = imageSize.y * s;
                    break;
                }
                default: // ScaleToFit
                {
                    float s = Mathf.Min(fw / imageSize.x, fh / imageSize.y) * zoom;
                    sw = imageSize.x * s; sh = imageSize.y * s;
                    break;
                }
            }

            // Focus: 0 pins the image's left/top edge in view, 1 the right/bottom, 0.5 centers — the
            // mapping the old transform pan used for zoom>1, now uniform for any zoom/scale mode.
            float lx = settings.FillFocus.x * (fw - sw);
            float ly = settings.FillFocus.y * (fh - sh);

            fill.style.backgroundSize = new BackgroundSize(new Length(sw), new Length(sh));
            fill.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(lx));
            fill.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(ly));
        }

        /// <summary>The frame's OUTER corner radius at the resolved size — the same value the shape pass
        /// paints on the root (Circle/Pill derive it from the size; RoundedRect/DiagonalSplit read the
        /// inline style, which BuildShell seeds and StyleApplier overwrites — inline first because a
        /// designer live poke runs in the same update as the write, when resolvedStyle is stale).</summary>
        private static float ShapeOuterRadius(VisualElement frameRoot, FrameSettings settings, float w, float h)
        {
            switch (settings.Shape)
            {
                case FrameShape.Circle: return Mathf.Min(w, h) * 0.5f;
                case FrameShape.Pill: return h * 0.5f;
                default:
                    var inlineRadius = frameRoot.style.borderTopLeftRadius;
                    return inlineRadius.keyword == StyleKeyword.Undefined
                        ? Mathf.Max(0f, inlineRadius.value.value)
                        : Mathf.Max(0f, frameRoot.resolvedStyle.borderTopLeftRadius);
            }
        }

        /// <summary>Frame ring width, inline value first (same stale-resolvedStyle reasoning as
        /// <see cref="ShapeOuterRadius"/>). Frames only ever paint uniform borders (Style.borderWidth).</summary>
        private static float EffectiveBorderWidth(VisualElement frameRoot)
        {
            var inlineWidth = frameRoot.style.borderTopWidth;
            if (inlineWidth.keyword == StyleKeyword.Undefined) return Mathf.Max(0f, inlineWidth.value);
            float resolved = frameRoot.resolvedStyle.borderTopWidth;
            return float.IsNaN(resolved) ? 0f : Mathf.Max(0f, resolved);
        }

        /// <summary>GUID → Texture2D (or a Sprite's texture) resolve, the ImageBannerNode pattern.</summary>
        internal static Texture2D ResolveTextureByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) tex = sprite.texture;
            }
            return tex;
        }

        // The overlay wash is baked into a 1×512 RGBA ramp — the gradient's OWN color+alpha per row. The
        // overlay element is FULL-BLEED with the shape's rounded corners (its own antialiased geometry forms
        // the visible edge; a height-% block clipped by the rounded overflow stencil drew a jagged staircase
        // along the curve), so the ramp is stretched 100%×100% and WHICH part of the frame it washes lives
        // in the alpha. The gradient runs from the named edge (t=0) inward across OverlaySizePercent of the
        // height; beyond the covered span the ramp is transparent. Full spans the whole height (bottom→top).
        // 512 rows ⇒ ≤~0.5px boundary softness at typical frame sizes. Each overlay OWNS its ramp texture
        // (destroyed on REAL detach — see PanelLifetime; a re-parent must not free it) and rebuilds it IN
        // PLACE so a live gradient/size drag never churns textures.
        private const int OverlayRampRows = 512;

        // Internal (not private): the standalone-editor exporter bakes the SAME ramp at export time and
        // ships it as a PNG inside the generated file, so the exported wash is pixel-identical to live.
        internal static Texture2D BakeOverlayRamp(Gradient g, FrameOverlayEdge edge, float percent, Texture2D reuse)
        {
            var ramp = reuse != null ? reuse : new Texture2D(1, OverlayRampRows, TextureFormat.RGBA32, false)
            {
                name = "sasq-frame-overlay-ramp",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            bool full = edge == FrameOverlayEdge.Full;
            bool topEdge = edge == FrameOverlayEdge.Top;
            int covered = full
                ? OverlayRampRows
                : Mathf.Clamp(Mathf.RoundToInt(OverlayRampRows * percent / 100f), 1, OverlayRampRows);

            for (int y = 0; y < OverlayRampRows; y++)
            {
                // Row 0 = texture bottom = element bottom (stretched 100%×100%, no repeat).
                Color c;
                if (full) c = g.Evaluate(y / (float)(OverlayRampRows - 1)); // whole frame, t=0 at bottom
                else if (!topEdge) c = y < covered ? g.Evaluate(y / (float)covered) : Color.clear; // grow up from bottom
                else
                {
                    int fromTop = OverlayRampRows - 1 - y; // top edge: t=0 at the very top row
                    c = fromTop < covered ? g.Evaluate(fromTop / (float)covered) : Color.clear;
                }
                ramp.SetPixel(0, y, c);
            }
            ramp.Apply();
            return ramp;
        }

        /// <summary>Adds the gradient overlay wash between fill and content (disabled ⇒ none). Always a
        /// FULL-BLEED element whose corner radii the geometry pass keeps concentric with the frame ring, so
        /// its silhouette is antialiased geometry rather than the jagged stencil clip; the gradient's colors
        /// and coverage live in a baked alpha ramp background (<see cref="BakeOverlayRamp"/>).</summary>
        private static void AddOverlayLayer(VisualElement frameRoot, FrameSettings settings)
        {
            if (!settings.OverlayEnabled) return;

            var overlay = new VisualElement();
            overlay.AddToClassList(OverlayClass);
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.right = 0f;
            overlay.style.top = 0f;
            overlay.style.bottom = 0f;
            overlay.pickingMode = PickingMode.Ignore;
            // The overlay owns its baked ramp texture (stashed in userData) — free it when the layer is torn
            // down for good. NOT on any detach: a window tab drag (and a collection row reorder) re-parents
            // the tree, so the very same overlay element comes back holding a destroyed texture and the wash
            // renders as nothing — the "overlays vanish when I release the tab" bug. See PanelLifetime.
            PanelLifetime.OnRealDetach(overlay, () =>
            {
                if (overlay.userData is Texture2D owned) UnityEngine.Object.DestroyImmediate(owned);
                overlay.userData = null;
            });

            ApplyOverlayVisual(overlay, settings);
            frameRoot.Add(overlay);
        }

        /// <summary>(Re)bakes the overlay's gradient ramp for the current settings — shared with the designer's
        /// gradient / direction / length live pokes so an edit re-bakes in place (reusing the owned texture)
        /// without a full rebuild.</summary>
        internal static void ApplyOverlayVisual(VisualElement overlay, FrameSettings settings)
        {
            var existing = overlay.userData as Texture2D;
            var ramp = BakeOverlayRamp(settings.OverlayGradient, settings.OverlayEdge, settings.OverlaySizePercent, existing);
            overlay.userData = ramp;
            overlay.style.backgroundColor = Color.clear;
            overlay.style.backgroundImage = Background.FromTexture2D(ramp);
            overlay.style.unityBackgroundImageTintColor = Color.white; // the ramp carries the gradient's own colors
            overlay.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            overlay.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
        }

        // ---------------- diagonal split ----------------

        /// <summary>
        /// Builds the split layer stack: base fill, then two rotated clippers each holding a counter-rotated
        /// full-frame pane host, then the optional seam line. All positions depend on the resolved frame
        /// size, so they are written by <see cref="LayoutSplit"/> from the geometry pass; here the elements
        /// are created with their rotation/radius (the size-independent parts).
        /// </summary>
        private static void BuildSplitLayers(
            VisualElement frameRoot, ContainerNode node, SerializedObject serializedObject, bool designMode)
        {
            var settings = node.Frame;

            AddFillLayer(frameRoot, settings, serializedObject, designMode);

            frameRoot.Add(MakePaneClipper(settings, isPaneA: true));
            frameRoot.Add(MakePaneClipper(settings, isPaneA: false));

            if (settings.SeamWidth > 0f && settings.SeamColor.isOverridden)
            {
                var seam = new VisualElement();
                seam.AddToClassList(SeamClass);
                seam.style.position = Position.Absolute;
                seam.style.backgroundColor = settings.SeamColor.value;
                seam.pickingMode = PickingMode.Ignore;
                seam.style.rotate = new Rotate(new Angle(settings.SplitAngle, AngleUnit.Degree));
                frameRoot.Add(seam);
            }

            // Title paints ABOVE the panes (added last), laid out in-flow at the top of the frame.
            var title = MakeTitleLabel(node);
            if (title != null) frameRoot.Add(title);
        }

        private static VisualElement MakePaneClipper(FrameSettings settings, bool isPaneA)
        {
            var clip = new VisualElement();
            clip.AddToClassList(PaneClipClass);
            clip.style.position = Position.Absolute;
            clip.style.overflow = Overflow.Hidden;
            // THE load-bearing line: radius 0 ⇒ axis-aligned scissor, pane spills across the seam.
            SetAllCorners(clip, RotatedClipMaskRadius);
            clip.style.rotate = new Rotate(new Angle(settings.SplitAngle, AngleUnit.Degree));
            // The clipper rotates about the seam midpoint: its seam-side edge center (right edge for pane A,
            // left for pane B). LayoutSplit positions that point on the seam midpoint M.
            clip.style.transformOrigin = isPaneA
                ? new TransformOrigin(Length.Percent(100f), Length.Percent(50f))
                : new TransformOrigin(Length.Percent(0f), Length.Percent(50f));

            var pane = new VisualElement();
            pane.AddToClassList(PaneClass);
            pane.name = isPaneA ? PaneAName : PaneBName;
            pane.style.position = Position.Absolute;
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.rotate = new Rotate(new Angle(-settings.SplitAngle, AngleUnit.Degree));
            clip.Add(pane);

            // Per-pane image fill: painted under the pane's content, clipped at the seam like
            // everything else in the pane. Static GUID (the ImageBanner pattern).
            string paneFillGuid = isPaneA ? settings.PaneAFillGuid : settings.PaneBFillGuid;
            var paneTex = ResolveTextureByGuid(paneFillGuid);
            if (paneTex != null)
            {
                // Same smooth-minification treatment as the main fill (see _smoothFillCopies).
                var paneFill = new Image { image = SmoothFillTexture(paneTex) };
                paneFill.AddToClassList(PaneFillClass);
                paneFill.scaleMode = ToScaleMode(settings.FillScaleMode);
                paneFill.style.position = Position.Absolute;
                paneFill.style.left = 0f; paneFill.style.top = 0f;
                paneFill.style.right = 0f; paneFill.style.bottom = 0f;
                paneFill.pickingMode = PickingMode.Ignore;
                pane.Add(paneFill);
            }

            return clip;
        }

        /// <summary>
        /// Grows the split frame to fit its tallest pane's content. The panes are ABSOLUTE rotated
        /// layers, so they contribute nothing to the frame's natural height — without this the frame
        /// sits at its 48px floor and any pane content deeper than that gets flex-CRUSHED (fields
        /// collapse to slivers; the designer's card chrome pushed content over the floor first, which
        /// is why it read as a Design-mode-only bug). The pane subtrees carry flexShrink:0 (set by
        /// the builder's pane wiring), so their layout height IS their natural content height even
        /// mid-overflow — one measure, one guarded write, converged by the next pass. Grow-only:
        /// never below the node's own height / the default floor.
        /// </summary>
        private static void GrowSplitToContent(VisualElement frameRoot, ContainerNode node, string splitSeedKey)
        {
            float padTop = SplitPaneTopPad(frameRoot);
            float need = 0f;
            foreach (var clip in frameRoot.Children())
            {
                if (!clip.ClassListContains(PaneClipClass)) continue;
                var pane = DirectChildByClass(clip, PaneClass);
                if (pane == null) continue;
                foreach (var sub in pane.Children())
                {
                    var subNode = sub.userData as LayoutNode;
                    if (subNode == null) continue; // pane fill / hints, not a pane subtree

                    // Measure the subtree's true CONTENT height from the lowest bottom edge of its
                    // IN-FLOW children — NOT sub.layout.height. sub carries flexGrow:1 so its background
                    // reaches the seam, which means it FILLS the pane and sub.layout.height merely echoes
                    // the frame's current height: measuring it made the grow CIRCULAR (frame is tall
                    // because sub is tall because frame is tall). Consequences the user hit: the frame
                    // could never SHRINK when content shrank (stuck tall in the inspector), and a pane
                    // row resize grew the row but sub's fill absorbed it so the frame never grew LIVE —
                    // only a rebuild-from-floor (close/reopen the designer) re-derived the height. In-flow
                    // children stack at their own natural/min heights,
                    // independent of the frame, so their extent is the real need; absolute overlays (the
                    // designer hover-ring + grips) span the whole pane and ARE the circular feedback —
                    // skip them.
                    float contentBottom = 0f;
                    bool sawInflow = false;
                    bool sawEcho = false;
                    float subH = sub.layout.height;
                    foreach (var gc in sub.Children())
                    {
                        if (gc.resolvedStyle.position == Position.Absolute) continue;
                        float b = gc.layout.yMax;
                        if (float.IsNaN(b)) continue;

                        // ECHO SKIP — the NARROW case only. Being in-flow is not enough to make a
                        // child a safe measure: sub carries flexGrow:1, so sub.layout.height IS the frame's
                        // height, and a child with its own flexGrow inside sub fills it exactly and hands
                        // that height straight back as "content".
                        //
                        // This catches ONLY the exact-fill shape. The case found in the wild trails the
                        // frame by its own chrome instead — MEASURED: frame 4096, pane subtree 4292, the
                        // List-mode collection's shell 4169, its search+sort toolbar line 4044 (i.e. always
                        // frame−52), leaving the shell 123px short of the subtree. No epsilon separates
                        // that from a healthy sibling (a real 270px shell in a 449px frame sits 179px
                        // short), so the general defence is the RATCHET PIN below, not this skip. Proof the
                        // child is driven TOP-DOWN: forcing that frame's min-height to 48 made it climb
                        // straight back through 1724 → 3880 → 4096 with the line tracking it at
                        // 1678 / 3828 / 4044 the whole way.
                        if (!float.IsNaN(subH) && subH > 0f && gc.layout.height >= subH - SplitEchoEpsilon)
                        {
                            sawEcho = true;
                            continue;
                        }

                        sawInflow = true;
                        if (b > contentBottom) contentBottom = b;
                    }

                    // Strip whatever top padding the completed pass used (contentBottom is measured from
                    // sub's border-box top, so it includes that offset), then re-add the padding
                    // LayoutSplit imposes below — the same max-with-user-padding formula. Doing it this
                    // way means pass one already targets the final height even before the seam/title
                    // padding has resolved (a raw measure underestimated on the first post-rebuild pass,
                    // stepping the frame through several growing writes).
                    //
                    // The no-in-flow-child fallback is SPLIT IN TWO, because the two cases it used to share
                    // are opposites. A subtree with children that are merely all ABSOLUTE still has real
                    // content, so the filled height is the right conservative measure — keep it. But an EMPTY
                    // subtree (childCount 0 — the default Pane B, or a pane whose only child is hidden this
                    // frame) contributes NO content, and reading sub.layout.height there is CIRCULAR: the
                    // pane subtree is stretched to the pane, which is stretched to the frame, so the measure
                    // returns the frame's own current height. `need` could then never fall below the frame's
                    // high-water mark and the frame grew but never shrank — a dead empty band that only a
                    // domain reload cleared.
                    //
                    // THIRD case, ahead of both: every in-flow child ECHOED (see the skip above). There is
                    // no non-circular evidence of content at all here, and the all-absolute fallback below
                    // reads sub.layout.height — the very value the skip just refused — so routing an echo
                    // through it would re-open the ratchet at the fallback instead of the loop. Contribute
                    // nothing: the pane drops to its pads, the frame settles to the floor, and the stretched
                    // child comes down with it — at which point it is shorter than sub and measures normally.
                    float contentH = sawInflow
                        ? contentBottom - sub.resolvedStyle.paddingTop
                        : sawEcho || sub.childCount == 0
                            ? 0f
                            : sub.layout.height - sub.resolvedStyle.paddingTop - sub.resolvedStyle.paddingBottom;
                    if (float.IsNaN(contentH)) continue;

                    var ps = subNode.Style;
                    float effPadTop = Mathf.Max(ps.paddingTop.isOverridden ? ps.paddingTop.value : 0f, padTop);
                    float effPadBottom = Mathf.Max(ps.paddingBottom.isOverridden ? ps.paddingBottom.value : 0f, SplitEdgePad);
                    need = Mathf.Max(need,
                        contentH + effPadTop + effPadBottom
                        + sub.resolvedStyle.marginTop + sub.resolvedStyle.marginBottom);
                }
            }

            float floor = Mathf.Max(DefaultMinHeight,
                node.Style.height.isOverridden ? node.Style.height.value : 0f);
            // A measure at or over the ceiling is not credible — no inspector frame needs it, so it can
            // only be feedback. LATCH at the ceiling rather than clamping every pass: clamping alone left
            // the loop live (measure says 4292, clamp says 4096, forever), and PROBING back down to the
            // floor was worse still — the frame climbed, hit the ceiling, dropped, and climbed again,
            // sawing 940↔3292 in the live inspector (measured). Latching stops the loop dead, which is what
            // makes the frame STILL. It is a backstop for a pathology, not a sizing rule: the frame is left
            // visibly too tall on purpose, because a silent wrong-but-plausible height would hide the bug.
            bool measureIsSuspect = need >= MaxSplitGrowHeight;
            if (measureIsSuspect && !_splitGrowRatchetLatch.TryGetValue(frameRoot, out _))
                _splitGrowRatchetLatch.Add(frameRoot, MaxSplitGrowHeight);
            float target = Mathf.Max(floor, Mathf.Min(need, MaxSplitGrowHeight));
            // OSCILLATION PIN (the "split frame vibrates" bug): the panes' inner width depends on the
            // frame's own height (seam pad carries tan(angle)·h/2), so pane content whose height flips
            // with its width (a column cell's label placement, a wrapping row) can make the measure
            // BISTABLE — state A targets B, state B targets A, and the deferred write below then
            // re-fires every frame, ±10–15px forever. Detect the cycle from the recent target window and
            // pin the LARGER of the values: content fits at the larger height in either state, and once
            // parked there no geometry pass re-fires, so the loop dies.
            if (splitSeedKey != null)
            {
                // Fetched through the capped accessor (see SplitGrowWindowFor / MaxSplitSizeEntries).
                var recent = SplitGrowWindowFor(splitSeedKey);
                recent.Add(target);
                if (recent.Count > SplitGrowWindow) recent.RemoveAt(0);

                // Distinguish a real 2-CYCLE (the target keeps RETURNING to a height it already left —
                // the vibration) from a deliberate RESIZE (the target steps to a new height and stays).
                // Run-length-collapse the window first: several geometry passes fire per transition, so
                // raw samples read 97,97,544,544 — dedup consecutive near-equal samples into runs. Then
                // it is an oscillation iff some run's value re-appears in an EARLIER, NON-ADJACENT run
                // (the frame came BACK to a height it had left). A monotonic shrink/grow (544→300→97)
                // never repeats an earlier value, so it is never pinned — which is what lets a user drag
                // a resized row back DOWN again. (The previous count-based test — "window holds the new
                // value ≥2× and some other value ≥2×" — could not tell the two apart: a shrink transiently
                // holds both the old-large and the new-small value, so it was misread as an oscillation
                // and pinned to the old max forever, the "stuck large" regression.) A one-time content
                // change is a single transition (no repeat), so it escapes
                // the pin with no explicit history flush needed.
                float maxSeen = target;
                var runs = new List<float>(SplitGrowWindow);
                foreach (var v in recent)
                {
                    if (v > maxSeen) maxSeen = v;
                    if (runs.Count == 0 || Mathf.Abs(runs[runs.Count - 1] - v) > 0.5f)
                        runs.Add(v);
                }
                bool oscillating = false;
                for (int i = 2; i < runs.Count && !oscillating; i++)
                    for (int j = 0; j <= i - 2; j++)
                        if (Mathf.Abs(runs[i] - runs[j]) <= 0.5f) { oscillating = true; break; }

                // RATCHET PIN — the sibling the oscillation pin never had, and the one that catches the
                // real feedback loops. The detector above fires only when a target RE-APPEARS in an earlier
                // run, deliberately, so that a one-way resize is free to shrink. A FEEDBACK loop is also
                // one-way, so it walked straight past: pane content that is tall only BECAUSE the pane is
                // tall re-measures bigger every settle and climbs forever (measured in the wild: ~196px per
                // round, all the way to the MaxSplitGrowHeight ceiling).
                //
                // The tell that separates the two is the number of TRANSITIONS, not their direction. A
                // genuine content change is ONE step and then flat; a ratchet is step after step, each
                // built on the last. So: strictly-increasing across the whole run-length-collapsed window
                // (MinRatchetRuns of them) means the measure is reading back our own previous write.
                //
                // Pin to runs[0] — the height from BEFORE the climb started, which is the last one the
                // content justified on its own. Then LATCH it: without the latch the pin releases as soon
                // as the window flattens, the climb restarts, and the frame saws up and down forever
                // (visible as the inspector "animating" on every rebuild). The latch is keyed by the frame
                // ELEMENT, so a rebuild gets a fresh verdict and this can never become the permanent
                // "stuck large, can't shrink" pin that the count-based oscillation test once was.
                bool ratcheting = runs.Count >= MinRatchetRuns;
                for (int i = 1; i < runs.Count && ratcheting; i++)
                    if (runs[i] <= runs[i - 1] + 0.5f) ratcheting = false;

                if (ratcheting && !_splitGrowRatchetLatch.TryGetValue(frameRoot, out _))
                    _splitGrowRatchetLatch.Add(frameRoot, runs[0]);

                if (_splitGrowRatchetLatch.TryGetValue(frameRoot, out var latched) && latched is float pinned)
                    target = Mathf.Min(target, pinned);   // Min, so a genuine shrink still gets through.
                else if (oscillating && recent.Count >= 4)
                    target = maxSeen;
            }
            // Remember the converged height so the NEXT build of this frame starts there (the seed cache
            // that keeps full rebuilds visually still — see _splitMinHeightSeeds).
            // A suspect measure never becomes a seed: the whole point of the probe above is that the
            // next build must start from an honest height rather than inherit the bad one.
            if (!measureIsSuspect) RecordSplitSeed(splitSeedKey, target); // capped write; null key is a no-op
            // Compared against our OWN last claim, not the applied min-height: now that this goes through
            // MinHeightFloors the applied value is the max of every claim, so a taller Row floor would read
            // as "the grow target did not take" and re-schedule a correcting write on every pass.
            float cur = MinHeightFloors.WidgetFloorOf(frameRoot);
            // DEFERRED write: this runs inside a layout pass, and a direct style write here chains
            // another full pass onto the SAME panel update — stacked with LayoutSplit's writes and
            // the cell label re-flow it blows Unity's per-update layout budget ("Layout update is
            // struggling…"). The scheduler phase runs before layout on the NEXT update, so the write
            // costs one clean pass there instead (converges across a couple of frames, invisibly).
            // ...and written as a WIDGET CLAIM, never as a raw style write. This runs from a
            // GeometryChangedEvent, so it re-asserts on every layout pass and therefore always gets the last
            // word — precisely the hazard MinHeightFloors exists to arbitrate. A split Frame is a
            // ContainerNode, so NeedsFloorHost gives it no RowFloorHost wrapper and a parent Rows container's
            // row divider writes its floor onto THIS very element: a raw write here erased it, and the frame
            // snapped back to its content height on the next pass while the model kept the height the user
            // dragged (the "row divider is inert on a split frame" bug). Claimed, the two floors compose and
            // the divider can hold the frame taller than its content.
            if (Mathf.Abs(cur - target) > 0.5f)
                frameRoot.schedule.Execute(() =>
                {
                    if (frameRoot.panel == null) return;
                    MinHeightFloors.SetWidget(frameRoot, target);
                });
        }

        /// <summary>Top padding <see cref="LayoutSplit"/> imposes on pane subtrees: below the title when
        /// one is present, else the plain edge pad. Shared with <see cref="GrowSplitToContent"/> so the
        /// measured height and the padding that will be applied can never drift apart.</summary>
        private static float SplitPaneTopPad(VisualElement frameRoot)
        {
            var title = DirectChildByClass(frameRoot, TitleClass); // direct child — never a nested frame's title
            float titleH = title != null ? title.layout.height : float.NaN;
            return float.IsNaN(titleH) ? SplitEdgePad : titleH + SplitTitleGap;
        }

        /// <summary>
        /// Positions the split clippers/panes/seam for the resolved frame size.
        /// Seam midpoint M = (p·W, H/2); clippers are S×2S with S = W+H (covers any angle ≤ 60°); each pane
        /// is a full W×H rect counter-rotated about M so it renders axis-aligned exactly over the frame,
        /// clipped at the seam by its clipper's straight edge.
        /// </summary>
        private static void LayoutSplit(VisualElement frameRoot, FrameSettings settings, float w, float h)
        {
            float s = w + h;
            float mx = Mathf.Clamp(settings.SplitPosition, FrameSettings.MinSplitPos, FrameSettings.MaxSplitPos) * w;
            float my = h * 0.5f;

            // Safe-area padding for the panes. The panes are FULL-frame rects clipped at the seam, so
            // content laid out near the seam gets cut — each pane pads its seam side by the seam's worst
            // horizontal excursion over the frame's height (the tilt span) plus the drawn line + a gap,
            // so fields always end before the seam. The title is an in-flow frame child painting ABOVE
            // the absolute panes; reserving its height as pane top-padding keeps title and fields from
            // ever overlapping (title height is resolved by the time this geometry pass runs).
            float padTop = SplitPaneTopPad(frameRoot);
            float tiltSpan = Mathf.Abs(Mathf.Tan(settings.SplitAngle * Mathf.Deg2Rad)) * h * 0.5f;
            float seamHalf = (settings.SeamColor.isOverridden ? Mathf.Max(0f, settings.SeamWidth) : 0f) * 0.5f;
            float seamGuard = tiltSpan + seamHalf + SplitSeamGap;

            // DIRECT children only — frames NEST, and a descendant query here would collect an inner split's
            // clippers too and write this frame's geometry onto them (a DiagonalSplit inside another
            // DiagonalSplit's pane had its panes positioned by the OUTER pass). Same rule the title, content
            // host, fill and overlay lookups already follow via DirectChildByClass, and the same rule the two
            // sibling loops in this file (the reGrow hook and GrowSplitToContent) already use.
            foreach (var clip in frameRoot.Children())
            {
                if (!clip.ClassListContains(PaneClipClass)) continue;
                var pane = DirectChildByClass(clip, PaneClass);
                bool isPaneA = pane != null && pane.name == PaneAName;

                clip.style.top = my - s;
                clip.style.width = s;
                clip.style.height = 2f * s;
                clip.style.left = isPaneA ? mx - s : mx;
                // transformOrigin percent already targets the seam-side edge center == M.

                if (pane != null)
                {
                    pane.style.width = w;
                    pane.style.height = h;
                    pane.style.top = s - my;
                    pane.style.left = isPaneA ? s - mx : -mx;
                    // Counter-rotate about the same world point M, expressed in the pane's local coords
                    // (pane local == frame coords, since the pane sits exactly over the frame rect).
                    pane.style.transformOrigin = new TransformOrigin(
                        new Length(mx, LengthUnit.Pixel), new Length(my, LengthUnit.Pixel));

                    // Safe-area padding rides the pane SUBTREE (the node-built container inside the
                    // host), NOT the pane host: host padding would inset the absolute pane fill image
                    // (Yoga insets absolute children by parent padding — the PostStyleFixup lesson) and
                    // shrink the subtree, whose background must run all the way to the seam. Padding on
                    // the subtree keeps its background/fill full-bleed while its fields stop short of
                    // the seam and below the title. Max with the pane node's own padding so a user-set
                    // value still wins when it's larger.
                    // Clamped so the pane always keeps MinPaneContentWidth to lay fields out in. Both panes
                    // pad their seam side and SplitEdgePad the outer side, so the content box is
                    // w - SplitEdgePad - seamPad either way, and one ceiling covers both. Unclamped, this is
                    // the ratchet documented on MinPaneContentWidth — it grows with h, h grows when a starved
                    // cell stacks its label, and the pair never settles.
                    float seamPad = isPaneA ? (w - mx) + seamGuard : mx + seamGuard;
                    seamPad = Mathf.Clamp(seamPad, 0f, Mathf.Max(0f, w - SplitEdgePad - MinPaneContentWidth));
                    foreach (var sub in pane.Children())
                    {
                        var subNode = sub.userData as LayoutNode;
                        if (subNode == null) continue; // pane fill / hints, not a pane subtree
                        var ps = subNode.Style;
                        sub.style.paddingTop = Mathf.Max(ps.paddingTop.isOverridden ? ps.paddingTop.value : 0f, padTop);
                        sub.style.paddingBottom = Mathf.Max(ps.paddingBottom.isOverridden ? ps.paddingBottom.value : 0f, SplitEdgePad);
                        sub.style.paddingLeft = Mathf.Max(ps.paddingLeft.isOverridden ? ps.paddingLeft.value : 0f,
                            isPaneA ? SplitEdgePad : seamPad);
                        sub.style.paddingRight = Mathf.Max(ps.paddingRight.isOverridden ? ps.paddingRight.value : 0f,
                            isPaneA ? seamPad : SplitEdgePad);
                    }
                }
            }

            // Direct child, for the same nesting reason as the clippers above.
            var seam = DirectChildByClass(frameRoot, SeamClass);
            if (seam != null)
            {
                float seamW = Mathf.Max(1f, settings.SeamWidth);
                seam.style.left = mx - seamW * 0.5f;
                seam.style.top = my - s;
                seam.style.width = seamW;
                seam.style.height = 2f * s;
                seam.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f));
            }
        }

        // ---------------- helpers ----------------

        private static ScaleMode ToScaleMode(ImageBannerScaleMode mode)
        {
            return mode == ImageBannerScaleMode.ScaleAndCrop ? ScaleMode.ScaleAndCrop
                 : mode == ImageBannerScaleMode.StretchToFill ? ScaleMode.StretchToFill
                 : ScaleMode.ScaleToFit;
        }

        private static void SetAllCorners(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        /// <summary>First DIRECT child carrying the class — frames nest, so descendant queries for
        /// frame-internal classes (title / content host) can land on an inner frame's element.</summary>
        private static VisualElement DirectChildByClass(VisualElement parent, string className)
        {
            foreach (var child in parent.Children())
                if (child.ClassListContains(className)) return child;
            return null;
        }
    }
}
