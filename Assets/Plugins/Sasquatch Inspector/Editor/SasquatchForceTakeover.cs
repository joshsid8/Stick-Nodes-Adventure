using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The SINGLE inspector-takeover mechanism: draw a type's saved Sasquatch Inspector layout IN PLACE of whatever editor
    /// body another party drew, by PANEL DISPLACEMENT. Driven by the throttled header scan
    /// (<see cref="SasquatchInspectorHeaderPencil.ScanRoot"/>), it fires under two conditions:
    ///   • BLANKET competitor (a plugin that injects an editor for every MonoBehaviour/ScriptableObject with NO
    ///     <c>[CustomEditor]</c> attribute): taken over when the global "Take Over Other Inspectors" setting is on;
    ///   • the type's OWN genuine per-type <c>[CustomEditor]</c>: taken over only on an explicit per-type
    ///     <c>Force Takeover</c> opt-in (set in the designer).
    /// Which branch applies is decided by <see cref="SasquatchGeneratedEditors.GetGenuinePerTypeEditor"/>.
    ///
    /// <para>WHY DISPLACEMENT (not editor resolution): winning the resolution tie against a same-type editor — or
    /// beating a third-party plugin's attribute-less injection — isn't achievable through Unity's public
    /// editor-resolution API. Displacement instead lets the other editor resolve and draw, then swaps the body using
    /// the <see cref="VisualElement"/> / <see cref="InspectorElement"/> API. This also means ZERO code
    /// generation and ZERO recompiles: saving/removing a layout or toggling the setting is picked up on the next
    /// scan tick.</para>
    ///
    /// <para>HOW the swap is done (subtle — the naive version flickers): we hide the competing editor's CONTENT —
    /// the <see cref="InspectorElement"/>'s child elements — and add our layout as a child of the same
    /// InspectorElement. We must NOT hide the InspectorElement itself: Unity owns its <c>display</c> and rewrites
    /// it EVERY frame from the component's expanded state, so hiding the element is
    /// fought back each frame (a visible flicker). Its child elements are ours to hide and stay hidden. When Unity
    /// repopulates the InspectorElement (a genuine rebuild), our body is gone and the next scan tick rebuilds it.</para>
    ///
    /// <para>ACCEPTED TRADE-OFFS of the displacement-only design:
    ///   1. Coverage is only as wide as the scan's row↔editor correlation — and that correlation is LOAD-BEARING,
    ///      not merely nice. A LOCKED inspector is covered by two routes in
    ///      <see cref="SasquatchInspectorHeaderPencil.ScanRoot"/>: the rows' own <c>m_Script</c> types prove it
    ///      (<see cref="SasquatchInspectorHeaderPencil.TryProveCorrelation"/>), and where not one row carries an
    ///      <c>m_Script</c> field to prove anything with — every row drawn by an IMGUI <c>[CustomEditor]</c> — the
    ///      correlation the window already earned is recalled
    ///      (<see cref="SasquatchInspectorHeaderPencil.TryRecallCorrelation"/>). A row that STILL cannot be
    ///      correlated has a null <c>rowEditor</c>, the blanket branch stays inert there, and the row is left
    ///      alone rather than released (the null-instance rule in <see cref="ApplyOrRelease"/>) — but "left alone"
    ///      does NOT mean the displacement survives. MEASURED: the first selection change after a lock makes Unity
    ///      destroy and re-create every InspectorElement in the window, so an uncorrelatable row comes back as a
    ///      fresh, native, never-displaced row and the competitor shows from then on. Coverage of the correlation
    ///      IS coverage of the takeover.
    ///   2. Displacement has to land pre-paint, in the SAME frame a row is (re)built, or the competing body
    ///      flashes for a tick. Three cooperating mechanisms get it there: selection rebuilds
    ///      happen before the immediate selection-change scan (measured); row-set changes are caught inside the
    ///      panel update — before layout/paint — by <see cref="SasquatchTakeoverPrePaintDriver"/>; and IN-PLACE
    ///      REBINDS (component reorder / removing a component above — Unity reuses InspectorElements for different
    ///      editors in the bindings phase, after the driver's tick) are caught by the driver's claim-agnostic ROW
    ///      STAMPS (<see cref="SasquatchTakeoverPrePaintDriver.StampRow"/>), whose geometry hook rescans during
    ///      that same update's layout phase. The claim sheet (below) additionally keeps repopulated content blank
    ///      until re-displacement, so the competitor body never paints. What remains: rows born pathologically
    ///      inside a panel's layout phase, an in-place rebind whose old and new content have pixel-identical
    ///      heights (no geometry event; fixed next tick), and the first tick after a domain reload before the
    ///      driver re-arms.
    ///   3. The competitor editor is still instantiated underneath (hidden; no paint cost, but its <c>OnEnable</c>
    ///      side effects run).
    ///   4. One migration recompile the first time the legacy generated file is deleted, then never again.
    ///   5. Multi-select displaces via <see cref="UnityEditor.Editor.targets"/>, and duplicate same-type
    ///      components bind exactly per row (never the first component on the object).</para>
    /// </summary>
    internal static class SasquatchForceTakeover
    {
        /// <summary>Name of the container we inject (as a child of the InspectorElement) to carry the replacement body.</summary>
        internal const string BodyMarkerName = "sasq-takeover-body";

        /// <summary>
        /// Class stamped on a CLAIMED InspectorElement, paired with the SasquatchTakeoverClaim.uss sheet added to
        /// the same element: `.sasq-takeover-claimed &gt; *` hides every direct child at STYLE-RESOLUTION time, so
        /// when Unity repopulates a claimed row its fresh native content is born hidden — it can never paint while
        /// waiting for the next scan to re-displace. The injected body is exempted by name (plus inline display,
        /// which always outranks USS). Class and sheet are removed together on release.
        /// </summary>
        internal const string ClaimClass = "sasq-takeover-claimed";
        internal const string ClaimSheetName = "SasquatchTakeoverClaim";

        // IMGUI-host padding parity (live-measured). A real UI-Toolkit inspector host (InspectorElement --uie
        // variant — both a UITK custom editor and our own fallback) is inset L=15 R=6 T=2; an IMGUI host
        // (--imgui / --imgui-custom) is flush (0). When we displace an IMGUI host we add these paddings
        // to our injected body so a displaced layout lines up with a normally-drawn Sasquatch inspector.
        internal const float ImguiHostPadLeft = 15f;
        internal const float ImguiHostPadRight = 6f;
        internal const float ImguiHostPadTop = 2f;

        // Diagnostics + the steady-state loop test: how often we BUILD a replacement body, and how often the layout's
        // rebuild/layout-edit callbacks fire. In steady state (repeated scans of an unchanged row) these must NOT climb.
        internal static int s_BuildCount, s_RebuildReqCount, s_LayoutEditCount;

        /// <summary>Diagnostics + tests: rebuilds AVOIDED by putting a torn-out body straight back (see the
        /// detached-body slot below). A Project-window click on a locked inspector should bump this, not
        /// <see cref="s_BuildCount"/>.</summary>
        internal static int s_ReuseCount;

        // ---- Detached-body reuse (the anti-flash path) -------------------------------------------------------
        // When Unity repopulates a claimed row it rips our body out, and the next scan used to BUILD A NEW ONE:
        // a full LayoutTreeBuilder pass whose fresh widget tree then settles over a couple of layout passes. The
        // row paints EMPTY across the gap — the claim sheet correctly hides Unity's fresh native content, and
        // ours is not back yet — which reads as a hard flash on every Project-window click.
        //
        // But the body Unity threw away is still CORRECT. Its bind key (instance set + layout generation) is
        // unchanged, and that is the very condition the rebuild path already trusts to decide a body may be
        // kept. So keep it and put it straight back: re-attaching an element that still holds its resolved
        // layout paints identically to the frame before — no build, no settle, nothing to see.
        //
        // ONE slot, SHORT-LIVED, on purpose. The pre-paint driver re-displaces on the same or the very next
        // tick (NotifyMarkerDetached), so anything older is not a re-attach candidate — it is just a detached
        // widget tree and a SerializedObject being held alive. Any mismatch drops the slot and builds fresh.
        //
        // "Short-lived" is ENFORCED, not merely intended: ArmDetachedExpiry hangs a self-unsubscribing editor
        // tick off every parked body that empties the slot the moment DetachedBodyReuseWindowSeconds has passed.
        // Without it the last body to detach with no successor — the window closed, the row is gone — stayed
        // parked until the next build/Release/detach/domain reload, which can be never.
        private sealed class DetachedBody
        {
            public int BindKey;
            public VisualElement Container;
            public SerializedObject SerializedObject;
            public bool FromImguiHost;   // the host padding is baked in at build time; only re-home like for like
            public double DetachedAt;
        }

        private static DetachedBody s_Detached;

        /// <summary>How long a torn-out body stays eligible for re-attachment. Comfortably longer than the
        /// one-or-two ticks the pre-paint driver needs, far too short to be a cache.</summary>
        internal const double DetachedBodyReuseWindowSeconds = 1.0;

        /// <summary>
        /// The ONE writer of <see cref="s_Detached"/>. Parking a body is not just remembering it: everything
        /// inside it that registered a <see cref="PanelLifetime.OnRealDetach"/> teardown — the container's own
        /// undo subscription, and every baked texture / second Editor / preview utility further down — was
        /// queued for release by the very detach that parked it, and MEASURED, the drain always wins that race
        /// by a full editor tick. So the parked root is QUARANTINED for as long as it sits in the slot, and
        /// emptying the slot lifts that immediately.
        /// <para>Routing every write through here is what makes the "lift it on EVERY exit" contract hold:
        /// a re-home, a near-miss in <see cref="TakeDetachedBody"/>, the expiry sweep, a
        /// <see cref="Release"/> and the test seam all empty the slot through this one line. On a re-home the
        /// body is back on a panel before the next drain, so that drain skips it by the ordinary re-parent
        /// rule; on every other exit the next drain releases it exactly as it always did.</para>
        /// </summary>
        private static void SetDetachedSlot(DetachedBody slot)
        {
            var prev = s_Detached;
            if (prev != null && !ReferenceEquals(prev, slot) && prev.Container != null)
                PanelLifetime.Unquarantine(prev.Container);

            s_Detached = slot;

            if (slot != null && slot.Container != null)
                PanelLifetime.Quarantine(slot.Container);
        }

        // Per-owner-type "layout generation", bumped by InvalidateType when a type's layout is saved/removed. It is
        // folded into each row's bind key (below), so a bump makes a DISPLACED body's stored key stop matching and
        // the very next header scan rebuilds it against the new graph — the TARGETED replacement for the old global
        // selection nudge (see SasquatchEditorRegistrar.RefreshInspectorsForType). Keyed by AssemblyQualifiedName; an
        // inherited-copy row keys off the ancestor that OWNS the layout (its InheritedFromTypeName), so bumping the
        // ancestor also invalidates every subclass currently showing its layout via Apply To Subclasses.
        private static readonly System.Collections.Generic.Dictionary<string, int> s_LayoutGeneration = new();

        /// <summary>
        /// Marks <paramref name="type"/>'s DISPLACED inspector bodies stale so the next header scan rebuilds them
        /// against the freshly-saved (or removed) layout. Cheap O(1) bump; the rebuild itself happens on the scan.
        /// Called by the targeted post-save refresh instead of tearing down every editor with a selection nudge.
        /// </summary>
        internal static void InvalidateType(Type type)
        {
            string key = type?.AssemblyQualifiedName;
            if (string.IsNullOrEmpty(key)) return;
            s_LayoutGeneration.TryGetValue(key, out int g);
            s_LayoutGeneration[key] = g + 1;
        }

        private static int GenerationFor(string assemblyQualifiedName)
            => !string.IsNullOrEmpty(assemblyQualifiedName) && s_LayoutGeneration.TryGetValue(assemblyQualifiedName, out int g) ? g : 0;

        /// <summary>
        /// 4-argument compatibility overload (single instance, no tracked editor). Forwards to the full form with
        /// <c>targets = {instance}</c> and a null <c>rowEditor</c> — so the blanket branch stays inert (as on an
        /// uncorrelatable row) and only the genuine-editor Force-Takeover branch can fire.
        /// </summary>
        internal static void ApplyOrRelease(InspectorElement ie, VisualElement host, Type type, UnityEngine.Object instance)
            => ApplyOrRelease(ie, host, type, instance,
                              instance != null ? new[] { instance } : null, null);

        /// <summary>
        /// Applies or releases the body swap for one inspector row, driven by the throttled header scan. Idempotent
        /// and cheap in steady state: it re-hides any freshly-repopulated native content, rebuilds our body only
        /// when it's missing or the row was rebound to a different instance/selection, and cleanly restores the
        /// native content the moment the gate stops applying.
        /// </summary>
        /// <param name="ie">The competing editor's inspector body (the row's <see cref="InspectorElement"/>).</param>
        /// <param name="host">The per-component header host (<c>ie.parent</c>) — reserved; the swap lives inside <paramref name="ie"/>.</param>
        /// <param name="type">The row's type (resolved by the caller).</param>
        /// <param name="instance">The primary inspected instance. NULL means "the scan could not identify this row",
        /// which leaves the row untouched — it is never read as "the gate turned off" (see the body).</param>
        /// <param name="targets">All inspected instances for this row (multi-select); single-element for one object.</param>
        /// <param name="rowEditor">The tracked editor that drew this row, or null when the scan couldn't correlate one.</param>
        internal static void ApplyOrRelease(InspectorElement ie, VisualElement host, Type type,
            UnityEngine.Object instance, UnityEngine.Object[] targets, UnityEditor.Editor rowEditor)
        {
            if (ie == null || type == null) return;

            var marker = FindMarker(ie);

            // UNKNOWN IS NOT THE SAME AS OFF. A null instance means the scan could not identify WHAT this row is
            // showing — not that the takeover gate stopped applying. The two used to collapse into the same
            // release path, and that is precisely how a LOCKED inspector fell apart: the scan resolves a row's
            // instance through the current selection, a locked window by definition is not showing the selection,
            // so clicking anything else in the Project window resolved every locked row to null and tore its live
            // displaced body out (the user sees parts of their layout vanish). An unidentifiable row must be left
            // EXACTLY as it is until a scan can identify it again.
            //
            // The one thing still worth undoing is a claim with no body left to justify it: Unity repopulated the
            // row and our marker went with it, and without an identity we cannot rebuild one — releasing there
            // shows the native content instead of leaving the row blank (the claim sheet hides native children).
            if (instance == null)
            {
                if (marker == null && ie.ClassListContains(ClaimClass)) Release(ie, null);
                return;
            }

            // Cheapest gates first; the pricier per-type-editor classifier is short-circuited so it only runs once
            // the type is confirmed styled + editable. The classifier decides WHICH takeover condition applies:
            //   • genuine per-type editor present → require the explicit per-type Force Takeover opt-in;
            //   • only a blanket competitor → require the global setting AND a correlated non-ours rowEditor.
            InspectorLayoutAsset asset = null;
            bool shouldTakeover =
                SasquatchEditorMenu.IsOverrideEnabled() &&
                SasquatchContextMenu.IsTypeEditable(type) &&
                LayoutRegistry.TryGet(type, instance, out asset) && asset != null && asset.Root != null &&
                // HIDDEN (the designer's eye button) — hand the row straight back to whatever was drawing it. This
                // sits with the cheap gates deliberately: hiding must cost nothing and must beat BOTH takeover
                // branches, per-type opt-in and blanket alike. PARITY: the fallback path answers the same flag by
                // drawing Unity's default inspector (VisualInspectorEditor).
                !asset.LayoutHidden &&
                (SasquatchGeneratedEditors.GetGenuinePerTypeEditor(type) != null
                    ? asset.ForceTakeover                                             // genuine per-type editor: per-type opt-in
                    : SasquatchSettingsManager.Current.TakeoverFromOtherInspectors    // blanket competitor:
                      && rowEditor != null && !(rowEditor is VisualInspectorEditor)); // global setting + a real competing body

            if (!shouldTakeover)
            {
                // Full restore whenever there is anything to undo. Checking the claim class too (not just the
                // marker) matters: a claim can outlive its marker (Unity repopulated the row, THEN the gate went
                // off) and any children we inline-hid must still be un-hidden. Unclaimed rows exit on two cheap
                // checks, so this stays free for the ~all rows takeover never touched.
                if (marker != null || ie.ClassListContains(ClaimClass)) Release(ie, marker);
                return;
            }

            // Claim the row FIRST (class + sheet): from here on, any native child — present or repopulated later —
            // resolves hidden before it can paint. Idempotent and cheap in steady state.
            EnsureClaim(ie);

            // Rebuild only when our body is gone (Unity repopulated the InspectorElement) or it was bound to a
            // different instance/selection (a reused row or a changed multi-select). Otherwise reuse it — no churn.
            int bindKey = ComputeBindKey(targets, instance);

            // Fold in the layout generation for the type that OWNS this row's layout (the ancestor for an inherited
            // copy, else the row's own type): a Save/Remove of that layout bumps the generation via InvalidateType,
            // so the stored key stops matching here and the body rebuilds against the new graph on THIS scan. This is
            // what lets the targeted post-save refresh skip the global selection nudge for displaced types.
            string ownerKey = asset.IsInheritedCopy ? asset.InheritedFromTypeName : type.AssemblyQualifiedName;
            bindKey = bindKey * 397 + GenerationFor(ownerKey);
            if (marker != null && (!(marker.userData is int boundKey) || bindKey != boundKey))
            {
                ie.Remove(marker);
                marker = null;
            }
            if (marker == null)
            {
                // Put the torn-out body back if this is the very row it just left (same bind key, same host
                // kind, still detached). That is the difference between a seamless re-displacement and a
                // visible flash: a rebuild blanks the row for a frame and then settles over the next one.
                var reusable = TakeDetachedBody(bindKey, IsImguiHost(ie));
                if (reusable != null)
                {
                    ie.Add(reusable.Container);
                    // Bind what you put back: a subtree that left the panel left the binding system with it.
                    reusable.Container.Bind(reusable.SerializedObject);
                    marker = reusable.Container;
                    s_ReuseCount++;
                }
                else
                {
                    // Building FRESH means the body that just left was for a different row, instance or layout
                    // generation — so this is a genuine target change, not the repopulation the scroll guard
                    // exists for, and the rebuild it armed itself for is never coming. Hand its reservation back
                    // now rather than letting it expire on its own timer: eight ticks of the OUTGOING inspector's
                    // content height forces the vertical scrollbar on and then off again, and that 13px of
                    // usable width reflows every widget in the incoming layout at once.
                    InspectorScrollPreservation.CancelIfArmedForAnother(
                        InspectorScrollPreservation.FindScrollView(ie), bindKey);

                    // A THROW HERE USED TO BLANK THE ROW FOREVER. EnsureClaim has already stamped the claim
                    // class and its sheet, which hides every direct child of the InspectorElement — so if
                    // BuildBody throws, the competitor's own body is hidden and our marker was never added.
                    // The next scan finds marker == null again, rebuilds, throws again: a permanently empty
                    // component re-logging on every tick. Worse, the exception escaped into
                    // SasquatchInspectorHeaderPencil's scan loop, so every LATER row in that window lost its
                    // takeover maintenance for the tick and the pre-paint driver stopped being re-armed.
                    //
                    // VisualInspectorEditor has wrapped its whole build since it shipped; this is the parity
                    // gap. The degrade differs though: Release, NOT DrawDefaultFallback. Drawing Unity's
                    // default inspector over a DISPLACED row would replace a competitor's custom editor with
                    // the stock one, which is a second, different wrong answer. Release drops the claim and
                    // un-hides what was already there, so the row falls back to exactly what it would show if
                    // Sasquatch were not installed.
                    try
                    {
                        marker = BuildBody(asset, type, instance, targets, bindKey, ie);
                        ie.Add(marker);
                        s_BuildCount++;
                    }
                    catch (System.Exception ex)
                    {
                        WarnTakeoverBuildFailedOnce(type, ex);
                        Release(ie, null);
                        return;
                    }
                }
            }

            // Hide the competing editor's own content (every non-marker child). NEVER hide the InspectorElement
            // itself — Unity resets its display every frame (see the class remarks). Guarded so an
            // already-hidden child isn't re-set each tick (no per-tick layout dirtying).
            int count = ie.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = ie.ElementAt(i);
                if (child != marker && child.style.display.value != DisplayStyle.None)
                    child.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Claims the detached body for <paramref name="bindKey"/>, or null when there isn't one that can be
        /// safely re-homed. ALWAYS empties the slot: a body is offered exactly once, so a near-miss can never
        /// leave a stale tree waiting to be grafted onto some later row.
        /// </summary>
        private static DetachedBody TakeDetachedBody(int bindKey, bool toImguiHost)
        {
            // ALWAYS through SetDetachedSlot: this empties the slot on EVERY exit below (the re-home and each
            // near-miss alike), and that is also what lifts the parked body's quarantine. A re-home is put
            // straight back on a panel by the caller, so the next drain skips it; a near-miss body is on its
            // way out and the next drain releases it normally.
            var slot = s_Detached;
            SetDetachedSlot(null);
            if (slot == null) return null;

            // Same inspected instance(s) AND the same layout generation — the exact condition the bind-key
            // check above uses to decide an EXISTING body may be kept, applied to a body that just left.
            if (slot.BindKey != bindKey) return null;

            // Already re-homed (or never really left): re-parenting a live element would steal it from its row.
            if (slot.Container == null || slot.Container.panel != null) return null;

            // The IMGUI-host insets are baked onto the container at build time, so a body may only go back to
            // the same kind of host it was built for — otherwise a displaced layout lands mis-aligned.
            if (slot.FromImguiHost != toImguiHost) return null;

            if (EditorApplication.timeSinceStartup - slot.DetachedAt > DetachedBodyReuseWindowSeconds) return null;

            // The inspected object can be destroyed while the body is detached (delete the GameObject the
            // locked inspector is showing); a SerializedObject over a dead target throws on use.
            if (slot.SerializedObject == null || slot.SerializedObject.targetObject == null) return null;
            slot.SerializedObject.Update();

            return slot;
        }

        /// <summary>
        /// Arms the expiry sweep for the body just parked in <paramref name="slot"/>. Without it the slot has no
        /// timer at all: when the last displaced body detaches with NO successor coming (the window closed, the
        /// row is gone for good), the slot keeps one whole widget tree and its SerializedObject alive until the
        /// next takeover build, Release, detach, or domain reload — which may be never.
        /// <para>It cannot race the legitimate re-home, and that matters, because the 1-second window is a real
        /// feature (it is what stops a visible rebuild flash): the tick only ever clears the slot it was armed
        /// FOR, and only once <see cref="DetachedBodyReuseWindowSeconds"/> has genuinely elapsed — the very same
        /// expiry <see cref="TakeDetachedBody"/> applies, so anything this drops was already ineligible to be
        /// re-homed. Inside the window it does nothing whatsoever.</para>
        /// </summary>
        private static void ArmDetachedExpiry(DetachedBody slot)
        {
            if (slot == null) return;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                // Someone else already emptied or replaced the slot (a take, a Release, a later detach). This arm
                // is finished; a replacement carries its own.
                if (!ReferenceEquals(s_Detached, slot)) { EditorApplication.update -= tick; return; }
                if (EditorApplication.timeSinceStartup - slot.DetachedAt <= DetachedBodyReuseWindowSeconds) return;
                EditorApplication.update -= tick;
                SweepDetachedBody();
            };
            EditorApplication.update += tick;
        }

        /// <summary>
        /// Drops the reuse slot once its window has passed, releasing the widget tree and the SerializedObject it
        /// holds. A no-op inside the window, and a no-op on the (impossible-by-construction, cheap-to-check) case
        /// of a body that has somehow been re-attached to a live panel. Internal so tests can run the sweep
        /// deterministically rather than waiting out a real second of editor time.
        /// </summary>
        internal static void SweepDetachedBody()
        {
            var slot = s_Detached;
            if (slot == null) return;
            if (EditorApplication.timeSinceStartup - slot.DetachedAt <= DetachedBodyReuseWindowSeconds) return;
            // Mirrors TakeDetachedBody's own liveness check: a body that is back on a panel belongs to a row now.
            if (slot.Container != null && slot.Container.panel != null) { SetDetachedSlot(null); return; }

            // Emptying the slot lifts the quarantine, so the pending teardowns inside this body — textures,
            // second Editors, preview utilities, the undo subscription — are released by the very next drain.
            // This is the END that stops a quarantine from holding a dead body's resources indefinitely.
            SetDetachedSlot(null);
            // Drop the references rather than Dispose() the SerializedObject: nothing in this package disposes a
            // SerializedObject (they are all left to the GC), and a body's bindings can outlive the detach by a
            // tick — a disposed SerializedObject under a still-registered binding throws.
            slot.Container = null;
            slot.SerializedObject = null;
        }

        // ---- Test seams (internal → visible to SasquatchInspector.Tests.Editor via InternalsVisibleTo) -----

        /// <summary>Tests: true while a torn-out body is parked in the reuse slot.</summary>
        internal static bool HasDetachedBodyForTests => s_Detached != null;

        /// <summary>
        /// Tests: parks a body in the reuse slot exactly as the detach handler does — including arming the expiry
        /// sweep — but with a caller-chosen <paramref name="detachedAt"/>, so both "inside the window" and "the
        /// window has passed" are reachable without waiting out real editor time.
        /// </summary>
        internal static void SeedDetachedBodyForTests(int bindKey, VisualElement container, SerializedObject so,
            bool fromImguiHost, double detachedAt)
        {
            SetDetachedSlot(new DetachedBody
            {
                BindKey = bindKey,
                Container = container,
                SerializedObject = so,
                FromImguiHost = fromImguiHost,
                DetachedAt = detachedAt,
            });
            ArmDetachedExpiry(s_Detached);
        }

        /// <summary>Tests: the real re-home attempt; returns the container handed back, or null on a miss.</summary>
        internal static VisualElement TakeDetachedBodyForTests(int bindKey, bool toImguiHost)
            => TakeDetachedBody(bindKey, toImguiHost)?.Container;

        /// <summary>Tests: empties the slot unconditionally (lifting the parked body's quarantine with it), so
        /// a test can leave the editor exactly as found.</summary>
        internal static void ClearDetachedBodyForTests() => SetDetachedSlot(null);

        private static bool IsImguiHost(InspectorElement ie)
            => ie != null && (ie.ClassListContains(InspectorElement.iMGUIInspectorVariantUssClassName) ||
                              ie.ClassListContains(InspectorElement.iMGUICustomVariantUssClassName));

        // A stable key for the currently-bound instance set: the instance id for a single object, or an
        // order-sensitive hash of every target's id for a multi-select. A different key ⇒ the row was rebound and
        // our body must rebuild against the new SerializedObject.
        private static int ComputeBindKey(UnityEngine.Object[] targets, UnityEngine.Object instance)
        {
            if (targets != null && targets.Length > 1)
            {
                int h = 17;
                foreach (var o in targets)
                    h = h * 31 + UnityObjectId.Of(o);
                return h;
            }
            return UnityObjectId.Of(instance);
        }

        // Builds the replacement body: a named container holding the type's Sasquatch Inspector layout, bound to the
        // inspected instance(s). A multi-target row binds a multi-edit SerializedObject (mixed-value widgets ship),
        // so edits write to every selected object.
        private static VisualElement BuildBody(InspectorLayoutAsset asset, Type type, UnityEngine.Object instance,
            UnityEngine.Object[] targets, int bindKey, InspectorElement ie)
        {
            var so = (targets != null && targets.Length > 1)
                ? new SerializedObject(targets)   // public multi-edit ctor
                : new SerializedObject(instance);

            var container = new VisualElement { name = BodyMarkerName };
            container.userData = bindKey;
            container.style.flexGrow = 1;
            // Inline display so the claim sheet's universal hide can never match the body itself (inline > USS).
            container.style.display = DisplayStyle.Flex;
            // If Unity tears the body out of a live panel (an InspectorElement repopulation, a row rebuild), flag
            // the pre-paint driver so its next tick re-displaces — the claim sheet keeps the row's fresh native
            // content hidden in the meantime. Also fires on our own removals/window closes: one spare rescan, no-op.
            //
            // AND hold the inspector's content height + scroll position across the gap. Without this the row
            // measures short for at least one layout pass (the claim sheet hides the native content Unity just
            // put back, and our replacement lands on the following tick), the Inspector's ScrollView clamps the
            // user's scroll offset to that collapsed content, and the offset is never restored — clicking a
            // prefab in the Project window with a LOCKED inspector threw the user partway up their layout.
            //
            // The guard is anchored on the SCROLLVIEW, resolved on attach and remembered: by detach time this
            // row is the very thing Unity is replacing, so anything hung off it is already on its way out (the
            // first version of this guard anchored on the InspectorElement and did nothing at all). The content
            // height is tracked the same way — read at detach it can already be the collapsed value, since when
            // that handler runs relative to layout is Unity's business, not ours.
            //
            // AND park the body itself in the reuse slot, through SetDetachedSlot — never a bare assignment.
            // Parking QUARANTINES this container with PanelLifetime, because THIS SAME DETACH queued every
            // OnRealDetach teardown inside the body (the undo subscription at the bottom of this method, plus
            // the frame-overlay ramps, gradient strips, embedded Editors and preview utilities further down)
            // and the drain wins that race by a full editor tick, MEASURED. Without the quarantine a re-homed
            // body comes back undo-deaf and holding freed resources. Emptying the slot — a re-home, a
            // near-miss, the expiry sweep, a Release — lifts it again; see SetDetachedSlot.
            //
            // TakeDetachedBody re-checks everything that could make this the wrong body to re-home, and
            // ArmDetachedExpiry gives the slot an END: "SHORT-LIVED on purpose" was only ever true when a
            // successor came along to empty it, and a body that detaches with nobody following it sat here
            // indefinitely (which, with a quarantine on it, would also pin its resources indefinitely).
            ScrollView hostScroll = null;
            float lastContentHeight = 0f;
            container.RegisterCallback<AttachToPanelEvent>(_ =>
                hostScroll = InspectorScrollPreservation.FindScrollView(container));
            container.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (hostScroll != null) lastContentHeight = hostScroll.contentContainer.layout.height;
            });
            container.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // Tagged with this body's bind key so the next scan can tell "the row I was holding for came
                // back" from "a different object is being shown now" and drop the guard immediately.
                InspectorScrollPreservation.HoldAcrossRebuild(hostScroll, lastContentHeight, bindKey);

                // Offer this body back to the very next scan, and quarantine it meanwhile (see above).
                SetDetachedSlot(new DetachedBody
                {
                    BindKey = bindKey,
                    Container = container,
                    SerializedObject = so,
                    FromImguiHost = IsImguiHost(ie),
                    DetachedAt = EditorApplication.timeSinceStartup,
                });
                // …and give it an END (see above).
                ArmDetachedExpiry(s_Detached);

                SasquatchTakeoverPrePaintDriver.NotifyMarkerDetached();
            });

            // IMGUI-host padding parity: an IMGUI host is flush (0 padding) while a UITK host is inset, so a body
            // displaced onto an IMGUI host would sit tight to the edges. Add the measured UITK inset back so the
            // displaced layout lines up with a normally-drawn Sasquatch inspector. Variant classes are fixed for
            // the InspectorElement's lifetime, so applying at build time is safe.
            if (IsImguiHost(ie))
            {
                container.style.paddingLeft = ImguiHostPadLeft;
                container.style.paddingRight = ImguiHostPadRight;
                container.style.paddingTop = ImguiHostPadTop;
            }

            // Build (or REBUILD in place) the layout content into the container and wire the right-click quick-edit
            // menu onto it. This is a REAL inspector, so it must get the same unified menu as the fallback path
            // (VisualInspectorEditor) — without it, a type taken over by displacement (its OWN [CustomEditor], or
            // a third-party blanket editor) falls through to the raw native context menu instead of Sasquatch's.
            //
            // Factored into a local Rebuild so the menu's RebuildInspector can refresh the DISPLACED body in place.
            // That path is USER-action-driven (Paste Value that resizes an array, prefab-revert, collection-row
            // Duplicate/Insert/Delete, quick-edit-popup layout edits), which is safe to rebuild here.
            //
            // Appear animations play on the FIRST build of this body only — replaying the card pop-ins on every
            // user-action rebuild reads as a flicker (parity with VisualInspectorEditor's `!_hasBuiltContent` gate).
            bool firstBuild = true;

            // The builder's onRebuildRequired, in PARITY with the fallback path (VisualInspectorEditor passes
            // ForceVisualRebuild — a REAL rebuild). This used to be a bare `s_RebuildReqCount++` that nothing
            // ever read, so a structural edit raised from inside a DISPLACED body persisted to disk while the
            // stale UI stayed on screen — the inline broken-button fix row (LayoutTreeBuilder's relink/remove)
            // was the live instance: the button vanished from the asset but not from the inspector, and a
            // second Remove click silently did nothing.
            //
            // DEFERRED, not direct: the signal is raised from a callback on an element INSIDE `container`, and
            // rebuilding synchronously would clear the element mid-event. One tick later the handler has
            // returned and the teardown is safe. `rebuildPending` coalesces several signals in one frame into
            // one rebuild. The counter stays for the tests that assert the signal reached this path at all.
            //
            // Declared BEFORE Rebuild: RequestRebuild captures it and Rebuild hands RequestRebuild to the
            // builder, so every call site has to sit after the capture is definitely assigned.
            bool rebuildPending = false;
            // Declared here for the same reason rebuildPending is: Rebuild captures it (to re-baseline at the
            // end of every build) and the undo handler below reads it, so it has to precede both.
            var undoGate = new UndoRebuildGate();
            void RequestRebuild()
            {
                s_RebuildReqCount++;
                if (rebuildPending) return;
                rebuildPending = true;
                container.schedule.Execute(() =>
                {
                    rebuildPending = false;
                    if (container.panel == null || instance == null) return;
                    Rebuild();
                }).ExecuteLater(0);
            }

            void Rebuild()
            {
                container.Clear();
                so.Update();

                // Unbound Field Handling (Project Settings ▸ Sasquatch Inspector), in PARITY with the fallback inspector
                // (VisualInspectorEditor). A DISPLACED body — a type with its OWN [CustomEditor], or a third-party
                // blanket competitor — reaches the inspector through here and NOT through VisualInspectorEditor, so
                // without this call it skips sync entirely: newly-added script fields would never auto-append and
                // the drift banner would never show for exactly those types. AutoAppend reconciles the layout with the
                // current script BEFORE the build (a strict no-op when already in sync); NotificationBanner returns a
                // drift banner that sits above the body and Sync-nows through this same Rebuild.
                var syncBanner = ApplyUnboundFieldHandling(type, asset, Rebuild);
                if (syncBanner != null) container.Add(syncBanner);

                bool debugConditions = ConditionBinder.LayoutHasConditions(asset.Root)
                    && EditorPrefs.GetBool(ConditionDebugOverlay.LivePrefKey, false);

                var view = LayoutTreeBuilder.Build(asset, so,
                    RequestRebuild,
                    onLayoutEdited: () => { s_LayoutEditCount++; LayoutRegistry.PersistLayoutEdit(asset); },
                    debugConditions: debugConditions,
                    playAppearAnimations: firstBuild,
                    // Live Debug in PARITY with the fallback path (VisualInspectorEditor). This is one of only
                    // two Build callers that is a real Inspector, and the feature is opt-in for exactly that
                    // reason — see LayoutTreeBuilder's note where EnabledForBuild is derived.
                    liveInspector: true);
                firstBuild = false;
                // Overflow rescue in PARITY with the fallback path (VisualInspectorEditor): right-edge painted
                // overflow gets a pan scroller instead of being unreachably clipped by the vertical-only window.
                container.Add(HorizontalOverflowRescue.Wrap(view));

                // Field search in PARITY with the fallback path (VisualInspectorEditor): the search UI lives in
                // the Script Header widget and Attach binds it to the filter engine (null when the layout has
                // no search-enabled Script Header).
                InspectorFieldSearch.Attach(view, instance);

                QuickEditMenu.Attach(view, new QuickEditContext
                {
                    LayoutAsset = asset,
                    SerializedObject = so,
                    Target = instance,
                    RebuildInspector = Rebuild,
                    StableRoot = container,
                    GetDebugConditions = () => EditorPrefs.GetBool(ConditionDebugOverlay.LivePrefKey, false),
                    SetDebugConditions = v => { EditorPrefs.SetBool(ConditionDebugOverlay.LivePrefKey, v); Rebuild(); },
                });

                container.Bind(so); // bind explicitly — the InspectorElement isn't bound to OUR SerializedObject

                // Baseline for the undo gate: what this build just put on screen. Last statement in Rebuild so
                // it can never record a state the user is not looking at.
                undoGate.Capture(so, asset);
            }

            // Undo/redo redraw, in PARITY with the fallback path (VisualInspectorEditor subscribes
            // ForceVisualRebuild in OnEnable). A layout mutation made FROM a live inspector — quick-edit
            // Commit, palette assign, button relink/remove — opens an undo group, but an undo does NOT change
            // this body's bind key, so nothing else here would ever notice: Ctrl+Z reverted the asset while
            // the inspector kept rendering the pre-undo layout.
            //
            // Unsubscribing on ANY detach would leave a re-homed body deaf to undo for the rest of its life,
            // so it goes through PanelLifetime.OnRealDetach. That alone was NOT enough, and the comment that
            // used to sit here claimed otherwise. MEASURED (45/45 re-homes): the deferred release is decided
            // on the next editor tick, the re-home never lands sooner than that tick, so the drain always won
            // — every re-homed body arrived with this subscription already dropped and its watch spent, and
            // stayed undo-deaf until the next layout save for the type bumped its bind key. Neither timing
            // knob helps: the failure is already at ONE tick, and the drain is already last in the update list.
            //
            // What actually protects it now is the QUARANTINE (PanelLifetime.Quarantine): parking this body in
            // the reuse slot marks the container, and the drain re-queues every watch underneath a marked root
            // instead of releasing it — so the subscription is still live when the body goes back on a row, and
            // is released the moment the slot is emptied by anything other than a re-home.
            // PARITY with the fallback path (VisualInspectorEditor.OnUndoRedo) — undo is a global, payload-free
            // editor event, so without a gate a Scene-view transform move rebuilds this displaced body too.
            // Same gate, same reasoning, same conservative default: anything it cannot prove is redundant
            // rebuilds. so.Update() runs first and unconditionally, because the comparison has to be against
            // the post-undo state and the live bindings need the refreshed cache either way.
            Undo.UndoRedoCallback onUndoRedo = null;
            onUndoRedo = () =>
            {
                if (container.panel == null) return; // torn out, or momentarily re-parented
                if (instance == null) { Undo.undoRedoPerformed -= onUndoRedo; return; } // undone out of existence
                try { so.Update(); }
                catch { RequestRebuild(); return; }
                if (undoGate.ShouldRebuild(so, asset)) RequestRebuild();
            };
            Undo.undoRedoPerformed += onUndoRedo;
            PanelLifetime.OnRealDetach(container, () => Undo.undoRedoPerformed -= onUndoRedo);

            Rebuild();

            return container;
        }

        /// <summary>
        /// Applies the project's Unbound Field Handling to a DISPLACED body, exactly as
        /// <see cref="VisualInspectorEditor"/> does for the fallback path. In AutoAppend mode it reconciles the
        /// layout asset with the current script in place (returns null); in NotificationBanner mode it returns a
        /// drift banner to prepend to the body. Suppressed while the designer edits this type: the designer holds an
        /// in-memory clone whose Save would clobber changes made here — they re-sync on the next rebuild after it
        /// closes (the same guard VisualInspectorEditor uses). <paramref name="rebuild"/> is the banner's Sync-now
        /// callback (the body's own local Rebuild), so a Sync refreshes the displaced body in place.
        /// </summary>
        private static VisualElement ApplyUnboundFieldHandling(Type type, InspectorLayoutAsset asset, Action rebuild)
        {
            var settings = SasquatchSettingsManager.Current;
            if (settings == null || type == null) return null;
            if (SasquatchLayoutDesignerWindow.IsEditingType(type)) return null;

            // Button rename self-heal — runs in EVERY mode (repairs existing bindings, imports nothing),
            // mirroring VisualInspectorEditor's fallback-path call so both render paths stay in parity.
            ButtonMethodSync.HealLayout(type, asset);

            switch (settings.UnboundFieldHandling)
            {
                case UnboundFieldSyncMode.AutoAppend:
                    UnboundFieldSync.ReconcileLayout(type, asset, settings.ApplyScriptAttributesOnSync);
                    break;
                case UnboundFieldSyncMode.NotificationBanner:
                    return UnboundFieldBanner.TryCreate(type, asset, settings.ApplyScriptAttributesOnSync, rebuild);
            }
            return null;
        }

        // Restores the native body: remove our container, drop the claim, and un-hide the content we hid.
        // Warn-once per displaced type + exception type. The scan runs many times a second, so an unguarded
        // log here would bury the console under one entry per tick — the same reasoning, and the same shape,
        // as VisualInspectorEditor.WarnInspectorBuildFailedOnce. Separate wording on purpose: this path hands
        // the row back to the OTHER package's inspector, not to Unity's default one.
        private static readonly System.Collections.Generic.HashSet<string> s_WarnedBuildFailures =
            new System.Collections.Generic.HashSet<string>();
        private static void WarnTakeoverBuildFailedOnce(System.Type targetType, System.Exception ex)
        {
            string typeName = targetType != null ? targetType.FullName : "?";
            if (s_WarnedBuildFailures.Add(typeName + "|" + ex.GetType().Name))
            {
                UnityEngine.Debug.LogWarning($"[Sasquatch Inspector] Building the styled inspector for " +
                                 $"'{(targetType != null ? targetType.Name : "?")}' threw — releasing this row " +
                                 $"back to its existing custom inspector. {ex}");
            }
        }

        private static void Release(InspectorElement ie, VisualElement marker)
        {
            if (marker != null && marker.parent == ie) ie.Remove(marker);
            ReleaseClaim(ie);
            int count = ie.childCount;
            for (int i = 0; i < count; i++)
                ie.ElementAt(i).style.display = StyleKeyword.Null;

            // Takeover is genuinely over for this row, so the body the removal above just parked in the reuse
            // slot must NOT be grafted back on by the next scan. (The bind-key check would almost always catch
            // it anyway; dropping it here means we never rely on "almost".) Through SetDetachedSlot, so the
            // quarantine that removal put on the body is lifted in the same breath — a body nobody is going to
            // re-home must have its teardowns run by the very next drain, exactly as before the quarantine.
            SetDetachedSlot(null);

            // NOT cancelling the scroll guard here is deliberate. The removal above fires the body's detach
            // handler, which arms a guard for a rebuild that is NOT coming — but that guard resolves itself
            // correctly on its own: the native content is shorter, so the range never comes back up to the
            // protected offset, no restore is ever written, and the held height is handed back a few ticks
            // later. Cancelling would be worse than pointless, because the guard is keyed on the SCROLLVIEW —
            // one row releasing would tear down a guard another row legitimately armed on the same inspector.
        }

        // Stamps the claim (class + sheet) on a taken-over InspectorElement — see the ClaimClass docs. Both checks
        // are cheap, so calling this every scan tick costs nothing in steady state and self-heals a stripped claim.
        // In-place REBINDS (Unity reusing this InspectorElement for a different editor) are detected and rescued
        // by the claim-agnostic row stamps in SasquatchTakeoverPrePaintDriver — a rebind's receiving row may never
        // have been claimed at all, so nothing claim-history-based can catch it (learned the hard way).
        private static void EnsureClaim(InspectorElement ie)
        {
            if (!ie.ClassListContains(ClaimClass)) ie.AddToClassList(ClaimClass);
            var sheet = SasquatchPaths.LoadStyleSheet(ClaimSheetName);
            if (sheet != null && !ie.styleSheets.Contains(sheet)) ie.styleSheets.Add(sheet);
        }

        // Removes the claim (class + sheet). Split from Release because a repopulated row can hold a claim with no
        // marker left — the gate turning off must still restore it to fully native. Early-outs on the class check
        // (this runs for every unclaimed row each scan tick, and without the class every sheet rule is inert).
        private static void ReleaseClaim(InspectorElement ie)
        {
            if (!ie.ClassListContains(ClaimClass)) return;
            ie.RemoveFromClassList(ClaimClass);
            var sheet = SasquatchPaths.LoadStyleSheet(ClaimSheetName);
            if (sheet != null && ie.styleSheets.Contains(sheet)) ie.styleSheets.Remove(sheet);
        }

        private static VisualElement FindMarker(VisualElement parent)
        {
            foreach (var ch in parent.Children())
                if (ch.name == BodyMarkerName) return ch;
            return null;
        }
    }
}
