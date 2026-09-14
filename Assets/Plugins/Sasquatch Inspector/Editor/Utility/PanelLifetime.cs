using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    // A DetachFromPanelEvent is NOT a teardown signal. UI Toolkit sends it whenever an element's subtree
    // leaves a panel — including the many cases where the SAME element instances come straight back:
    //
    //   * docking a window: dragging its TAB (even just sliding it within its own dock strip) makes Unity
    //     re-parent the window's whole visual tree, so every descendant sees Detach then Attach,
    //   * collection drag-reorder floats the dragged row into an overlay and restores it home,
    //   * any code that Removes an element and Inserts it again to change sibling order.
    //
    // MEASURED in a live editor: a same-panel re-parent of a window root fires Detach on every
    // descendant, then Attach, with the element instances and their children fully intact.
    //
    // So a detach handler that FREES something the element still needs — an owned texture, a scratch
    // ScriptableObject, an Editor instance, a PreviewRenderUtility — silently kills the widget: the
    // element survives, holding a destroyed resource, and nothing rebuilds it. That is the bug behind
    // "the gradient overlay on my Frames disappears the moment I release the window tab": the overlay's
    // baked ramp texture was destroyed on detach, and the re-attached overlay kept pointing at it.
    /// <summary>
    /// Runs <paramref name="release"/> when <paramref name="el"/> leaves its panel FOR GOOD, skipping the
    /// re-parents that merely look like teardown. The check is deferred to the next editor tick and
    /// re-tested there: Unity re-adds a re-parented element inside the same GUI event, so an element that
    /// is still panel-less a tick later really is gone.
    ///
    /// Use this for anything whose loss BREAKS the surviving element. Plain scheduled items need nothing —
    /// UI Toolkit already pauses them on detach and resumes them on attach (and an explicit
    /// <c>Pause()</c> in a detach handler is a PERMANENT stop, never resumed: measured).
    /// </summary>
    internal static class PanelLifetime
    {
        private sealed class Watch
        {
            public VisualElement Element;
            public Action Release;
            public bool Queued;

            /// <summary>
            /// Registered while off-panel and not seen on one since. See the note in <see cref="OnRealDetach"/>
            /// — this watch is queued speculatively and gets one drain of grace before it is released.
            /// </summary>
            public bool NeverAttached;
        }

        private static readonly List<Watch> s_pending = new List<Watch>();
        private static bool s_hooked;

        // ---- QUARANTINE: a subtree that is detached but genuinely COMING BACK -------------------------------
        //
        // Every re-parent this class exists to skip brings the element back inside the SAME GUI event, which is
        // exactly why re-testing `panel` one tick later settles them all. ONE case does not fit that shape: the
        // takeover's detached-body reuse slot (SasquatchForceTakeover) deliberately parks a torn-out inspector
        // body OUTSIDE any panel and re-homes it on a LATER tick — the anti-flash path.
        //
        // MEASURED: the gap between that detach and the re-home is never less than one full editor tick (18 ms),
        // so the drain always ran first. Every watch inside the parked body was released and its Release nulled,
        // and the body came back undo-deaf and holding freed textures / cleaned-up previews. Neither obvious
        // timing tweak helps: the failure already occurs at ONE tick (so a shorter reuse window changes nothing)
        // and Drain is already last in the update invocation list (so running later changes nothing).
        //
        // So the parking code marks the body's root QUARANTINED, and the drain — which runs after the WHOLE
        // detach batch is queued, whatever order UI Toolkit dispatched it in — re-queues any watch sitting
        // under a quarantined root instead of releasing it. Order-independent by construction: it never has to
        // know which watches were queued when, only where the element lives now.
        //
        // THE CONTRACT ON THE CALLER: every path that gives up on re-homing must Unquarantine, and the very
        // next drain then releases the subtree exactly as if the quarantine had never existed. A quarantine
        // that is never lifted holds the subtree's resources for good — so it belongs only on state with an
        // enforced END (the reuse slot's expiry sweep).
        private static readonly List<VisualElement> s_quarantined = new List<VisualElement>();

        /// <summary>
        /// Protects <paramref name="root"/> and everything under it from release while it is parked off-panel
        /// but still re-homeable. Idempotent. The caller MUST <see cref="Unquarantine"/> on every path that
        /// stops intending to put it back — see the contract note above.
        /// </summary>
        internal static void Quarantine(VisualElement root)
        {
            if (root == null) return;
            for (int i = 0; i < s_quarantined.Count; i++)
                if (ReferenceEquals(s_quarantined[i], root)) return;
            s_quarantined.Add(root);
        }

        /// <summary>
        /// Lifts the protection. If the subtree came back to a panel the next drain simply skips it (the
        /// ordinary re-parent path); if it did not, the next drain releases it normally. Safe on a root that
        /// was never quarantined.
        /// </summary>
        internal static void Unquarantine(VisualElement root)
        {
            if (root == null) return;
            for (int i = 0; i < s_quarantined.Count; i++)
                if (ReferenceEquals(s_quarantined[i], root)) { s_quarantined.RemoveAt(i); return; }
        }

        private static bool IsQuarantined(VisualElement el)
        {
            if (s_quarantined.Count == 0) return false;
            // hierarchy.parent, not parent: a watched element can sit inside a contentContainer, and only the
            // hierarchy walk reaches the quarantined root through one.
            for (var e = el; e != null; e = e.hierarchy.parent)
                for (int i = 0; i < s_quarantined.Count; i++)
                    if (ReferenceEquals(s_quarantined[i], e)) return true;
            return false;
        }

        /// <summary>Tests: how many roots are currently quarantined (0 in steady state — a stuck root means a
        /// re-home path forgot to <see cref="Unquarantine"/>).</summary>
        internal static int QuarantinedRootCount => s_quarantined.Count;

        /// <summary>Tests: how many watches are waiting for their deciding tick (a re-queued quarantined watch
        /// stays counted, which is what keeps the drain hook alive).</summary>
        internal static int PendingWatchCount => s_pending.Count;

        /// <summary>Tests: whether the editor-tick hook is live. Pairs with <see cref="PendingWatchCount"/> —
        /// re-queuing a quarantined watch is only safe BECAUSE it keeps s_pending non-empty and therefore stops
        /// <see cref="Unhook"/> running. If a future refactor parks quarantined watches in a separate list, this
        /// goes false with work still outstanding and nothing ever re-checks the parked body.</summary>
        internal static bool DrainHookedForTests => s_hooked;

        /// <summary>Tests: runs the RELOAD drain (clear the quarantine, then settle everything). The real caller
        /// is <c>AssemblyReloadEvents.beforeAssemblyReload</c>, which a test cannot raise — without this seam the
        /// quarantine-clearing line is unreachable from the suite and could be deleted silently.
        /// <para>⚠ CALLERS MUST RETIRE ANY LIVE PARKED BODY FIRST. This clears the PROCESS-GLOBAL registry, and
        /// the sole production writer of it is the takeover's reuse slot. Run against a LIVE editor that happens
        /// to have a body parked (the ~1s window after clicking away from a displaced row) and this releases
        /// everything inside that body while <c>s_Detached</c> still holds it — nothing here touches the slot —
        /// so the next scan re-homes a HOLLOW body: freed textures, undo-deaf, healed only by a full rebuild.
        /// Call <c>SasquatchForceTakeover.ClearDetachedBodyForTests()</c> first; it routes the body out through
        /// <c>SetDetachedSlot</c>, which un-quarantines it properly.</para></summary>
        internal static int FlushForReloadForTests() => DrainForReloadCounted();

        /// <summary>Registers a teardown that must survive a re-parent. Idempotent per detach: a second
        /// detach before the check runs does not queue a second release.</summary>
        /// <returns>
        /// A RE-ARM callback. The release runs at most once per arming and the watch is spent afterwards, which is
        /// right for the usual caller (released == gone for good) but wrong for the one shape that can come BACK
        /// from its own teardown: a widget that re-acquires the resource the release freed. Nothing else re-arms it,
        /// so that widget's second real detach would free nothing and leak the re-acquired resource for the rest of
        /// the session. Invoke this in the same breath as taking the resource back. A no-op while the watch is still
        /// armed or queued, so calling it on every poll is safe; ignore the return value everywhere else.
        /// </returns>
        /// <param name="releaseIfNeverAttached">
        /// OPT IN to also releasing when <paramref name="el"/> never reaches a panel at all.
        ///
        /// <para>The mechanism below is driven entirely by <c>DetachFromPanelEvent</c>, so a tree that is built
        /// and thrown away without ever being shown never detaches, never queues, and its release is simply
        /// unreachable. For most callers that is harmless or even required — a probe that hands a resource back
        /// through its own teardown must not have it taken away underneath it. It is NOT harmless for a caller
        /// whose release drops GLOBAL event subscriptions: each discarded build leaks its whole set for the
        /// rest of the domain, and every leaked handler keeps the dead tree alive and keeps running on it.
        /// Those callers pass true, and get one drain of grace before the release runs — an inspector root is
        /// attached inside the same frame it is built, so only a genuinely discarded tree is still panel-less
        /// a full drain later.</para>
        /// </param>
        internal static Action OnRealDetach(VisualElement el, Action release, bool releaseIfNeverAttached = false)
        {
            if (el == null || release == null) return () => { };

            var watch = new Watch { Element = el, Release = release };
            el.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (watch.Queued || watch.Release == null) return;
                watch.Queued = true;
                s_pending.Add(watch);
                Hook();
            });
            // Opted in and registered off-panel: queue speculatively so the drain can settle it. See the
            // parameter note. The takeover's deliberately parked body is protected by the quarantine, exactly
            // as it is on every other path through the drain.
            if (releaseIfNeverAttached && el.panel == null)
            {
                watch.Queued = true;
                watch.NeverAttached = true;
                s_pending.Add(watch);
                Hook();
            }

            // Re-arms the SAME watch rather than registering a second callback: another OnRealDetach on the same
            // element would stack a detach handler per recovery cycle, and every one of them would queue its own
            // release on the next teardown.
            return () => { if (!watch.Queued && watch.Release == null) watch.Release = release; };
        }

        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            EditorApplication.update += Drain;
            // A domain reload drops the update hook, so settle anything still pending first — otherwise a
            // texture / preview scene queued on the last tick before a recompile would leak un-released.
            AssemblyReloadEvents.beforeAssemblyReload += DrainForReload;
        }

        private static void Unhook()
        {
            if (!s_hooked) return;
            s_hooked = false;
            EditorApplication.update -= Drain;
            AssemblyReloadEvents.beforeAssemblyReload -= DrainForReload;
        }

        private static void Drain() { DrainCounted(); }

        // The reload drain must settle EVERYTHING, quarantine included: the reload wipes the statics that would
        // have un-quarantined these roots (and the parked body they belong to), so a re-queue here is a release
        // that never happens at all. Nothing can be re-homed across a domain reload anyway.
        private static void DrainForReload() { DrainForReloadCounted(); }

        // ONE implementation, shared with the test seam. Keep it that way: if the seam clears the quarantine
        // itself instead of calling this, the test passes while the real reload path silently loses the clear.
        private static int DrainForReloadCounted()
        {
            s_quarantined.Clear();
            return DrainCounted();
        }

        private static int DrainCounted()
        {
            if (s_pending.Count == 0) { Unhook(); return 0; }

            // Copy first: a release may build/teardown UI and queue more work, and must not mutate the
            // list we are walking.
            var batch = s_pending.ToArray();
            s_pending.Clear();

            int released = 0;
            for (int i = 0; i < batch.Length; i++)
            {
                var watch = batch[i];
                watch.Queued = false;
                // Came back: that detach was a re-parent (dock drag, row reorder, sibling reshuffle).
                // Leave the resource alone — the element is live again and still using it.
                if (watch.Element != null && watch.Element.panel != null)
                {
                    // It has now been on a panel, so it is an ordinary watch from here on and a future real
                    // detach must release it on the very next drain, not one later.
                    watch.NeverAttached = false;
                    continue;
                }

                // Off-panel but QUARANTINED: the owner is holding this subtree for a re-home on a later tick
                // (the takeover's reuse slot). Put the watch back in the queue untouched — Release must stay
                // non-null, because nulling it here is precisely how a re-homed body ended up undo-deaf and
                // holding destroyed textures. Re-queuing also keeps s_pending non-empty, so the drain hook
                // survives to run the check again next tick.
                if (IsQuarantined(watch.Element))
                {
                    watch.Queued = true;
                    s_pending.Add(watch);
                    continue;
                }

                // Speculatively queued at registration and still panel-less: spend its one pass of grace. A
                // build that is on its way to a panel gets there well inside a single drain, so anything still
                // here on the next one was genuinely discarded. See OnRealDetach.
                if (watch.NeverAttached)
                {
                    watch.NeverAttached = false;
                    watch.Queued = true;
                    s_pending.Add(watch);
                    continue;
                }

                var release = watch.Release;
                watch.Release = null; // release exactly once per element, ever
                released++;
                try { release?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); } // one bad teardown never starves the rest
            }

            if (s_pending.Count == 0) Unhook();
            return released;
        }

        /// <summary>Test hook: runs the pending real-detach checks now instead of on the next editor tick
        /// (a synchronous test never yields to the editor loop). Returns how many releases ran.</summary>
        internal static int FlushPendingReleases()
        {
            return DrainCounted();
        }
    }
}
