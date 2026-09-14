using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Closes the takeover FLASH RACE for good: displaces competitor inspector bodies in the SAME frame their
    /// rows are (re)built, BEFORE the panel paints them — so the competitor editor is never visible, not even
    /// for one tick.
    ///
    /// <para>WHY the update-loop scan alone can flash (measured live, Unity 6000.3): the inspector rebuilds its
    /// editor rows in two distinct places. (1) On a SELECTION change the rows are rebuilt during native selection
    /// processing, BEFORE <c>Selection.selectionChanged</c> fires — so the immediate scan in
    /// <see cref="GlobalInspectorHeaderGUI"/> always wins that path. (2) On every OTHER row-set change (component
    /// add/remove/paste/reorder, undo, play-mode transitions, lock/debug toggles) the rows are rebuilt inside the
    /// inspector window's own update — AFTER that tick's <c>EditorApplication.update</c> scan has already run and
    /// BEFORE that tick's paint. The old design could only catch those on the NEXT tick, leaving exactly one
    /// painted frame of the competitor body: the occasional "blip".</para>
    ///
    /// <para>THE HOOK: an <see cref="IVisualElementScheduler"/> item on the
    /// window's root. A panel runs its scheduler phase at the START of a panel update, before styling, layout and
    /// paint — measured: elements created by a mid-tick rebuild are visible to the scheduler BEFORE their first
    /// paint. And a row rebuild always dirties the panel, so the very panel update that would first PAINT the new
    /// rows runs our tick FIRST. Displacing there is structurally pre-paint — no polling race left to lose.</para>
    ///
    /// <para>WHAT MAKES IT FREE AT REST — and it is NOT that the scheduler sleeps. Measured on a real editor: an
    /// <c>Every(1)</c> item on an EditorWindow root fires ~9.4x/second with the editor completely IDLE and up to
    /// ~57x/second while it is busy; it rides the editor update loop, NOT panel dirtiness. The driver is free at
    /// rest because the tick BODY is a dirty-check that costs a bool read, a panel null-check, a childCount
    /// compare and N <c>ReferenceEquals</c> — nothing else. That distinction is load-bearing: any work added to
    /// the tick, and any state the tick can leave permanently "dirty", runs ~10-57 times a second forever.</para>
    ///
    /// <para>THE TICK stays O(rows) cheap: it compares the editors-list container's child list against a snapshot
    /// (reference equality) and only runs the full <see cref="SasquatchInspectorHeaderPencil.ScanRoot"/> when the
    /// row set actually changed (or a displaced body was torn out — see <see cref="NotifyMarkerDetached"/>). The
    /// throttled update-loop scan in <see cref="GlobalInspectorHeaderGUI"/> stays on as the discovery/fallback
    /// path: it installs this driver on every window it touches and still covers windows born between scans.
    /// Every scan ends by re-baselining the snapshot ITSELF (see <c>Tick</c>) — ScanRoot's own re-baseline is
    /// skipped when it finds no rows, and "no rows" is exactly what a deselect leaves behind.</para>
    ///
    /// <para>IN-PLACE REBINDS are the one rebuild the tick cannot see coming: on a component reorder (or removing
    /// a component above), Unity keeps the row host elements in place and REBINDS the InspectorElements to different
    /// editors during the panel's BINDINGS phase — after this driver's scheduler tick, before that same update's
    /// paint — stripping foreign classes/styleSheets as it goes (measured live). Those are caught by the geometry
    /// rescue in <see cref="SasquatchForceTakeover"/> (a claim-time GeometryChangedEvent hook that survives the
    /// rebind), which calls <see cref="RequestImmediateRescan"/> from the layout phase — still pre-render.</para>
    ///
    /// <para>Remaining (accepted) slivers: a row created DURING a panel's layout phase by pathological user IMGUI
    /// (an OnInspectorGUI that adds components while measuring) paints once before the next update; an in-place
    /// rebind whose old and new content have pixel-identical heights (no geometry event — fixed one tick later by
    /// the marker-detach flag); and the first tick after a domain reload before the driver is re-armed.</para>
    /// </summary>
    internal static class SasquatchTakeoverPrePaintDriver
    {
        private sealed class Driver
        {
            public VisualElement Root;
            public VisualElement Container;                       // the editors-list (parent of the per-editor hosts)
            public readonly List<VisualElement> Snapshot = new(); // Container's children at the last scan
            public bool Scheduled;
            public bool MarkerDetached;                           // a displaced body left THIS window's panel (best effort: broadcast)
        }

        // Keyed by window root. ConditionalWeakTable so a closed window's root (and its driver with it) is
        // collectable — entries never outlive their window. Values may reference their key safely (ephemerons).
        private static readonly ConditionalWeakTable<VisualElement, Driver> s_Drivers = new();

        // Weak enumeration of every live driver, so a marker-detach can be broadcast (CWT itself is not reliably
        // enumerable across Unity runtimes). Dead entries are pruned on every broadcast.
        private static readonly List<System.WeakReference<Driver>> s_AllDrivers = new();

        // Diagnostics + tests: how many times a tick actually green-lit a scan. Steady state must NOT climb.
        internal static int s_PrePaintScans;
        // Diagnostics: which dirty condition green-lit the LAST scan ("flag" = marker detach broadcast,
        // "container-lost", "count", "ref", "rescue"). Steady state should leave this untouched.
        internal static string s_LastDirtyReason;
        // Diagnostics: RequestImmediateRescan entries, INCLUDING ones the re-entrancy guard or a missing driver
        // swallowed — s_PrePaintScans counts only completed scans, so the delta between these two localises a miss.
        internal static int s_RescueRequests;

        /// <summary>
        /// Flags every live driver to rescan on its next tick: a displaced body was torn out of a live panel
        /// (Unity repopulated its InspectorElement, a row was rebuilt, a window closed). Broadcast because the
        /// detach callback can't cheaply know which window it fired in; a marker detach is rare and a spare
        /// no-change scan costs microseconds. The repopulation case mutates INSIDE a row, invisible to the
        /// container child-list compare — this flag is what catches it.
        /// </summary>
        internal static void NotifyMarkerDetached()
        {
            for (int i = s_AllDrivers.Count - 1; i >= 0; i--)
            {
                if (s_AllDrivers[i].TryGetTarget(out var d)) d.MarkerDetached = true;
                else s_AllDrivers.RemoveAt(i);
            }
        }

        /// <summary>
        /// Installs (idempotently) the scheduler tick on <paramref name="root"/> and re-snapshots the current
        /// editors-list container. Called from the tail of every <see cref="SasquatchInspectorHeaderPencil.ScanRoot"/>
        /// pass, so whichever path scans a window first — the throttled update loop, the selection-change scan, or
        /// this driver itself — arms and re-baselines the watch.
        /// </summary>
        internal static void EnsureInstalled(VisualElement root, VisualElement editorsContainer)
        {
            if (root == null) return;

            var d = s_Drivers.GetValue(root, r => new Driver { Root = r });
            if (!d.Scheduled)
            {
                d.Scheduled = true;
                s_AllDrivers.Add(new System.WeakReference<Driver>(d));
                // Every(1): rides the editor update loop — measured ~9.4x/second with the editor IDLE, up to
                // ~57x/second when busy. It is NOT event-driven and it does NOT stop while the panel is clean.
                // What keeps it free is the tick BODY: a bool read, a panel null-check, a childCount compare and
                // N ReferenceEquals. Anything that leaves the tick permanently dirty runs at that rate forever.
                root.schedule.Execute(() => Tick(d)).Every(1);
            }

            if (editorsContainer != null)
                d.Container = editorsContainer;
            TakeSnapshot(d);
        }

        // The per-panel-update dirty check. Runs in the panel's scheduler phase — before styling/layout/paint of
        // the same update — so a scan triggered here displaces rebuilt rows before they can ever paint. Returns
        // whether it actually scanned (the test seam asserts steady-state quiet + change-driven scans).
        private static bool Tick(Driver d)
        {
            bool dirty = d.MarkerDetached;
            if (dirty) s_LastDirtyReason = "flag";

            if (!dirty)
            {
                var container = d.Container;
                if (container == null) return false; // dormant (no rows yet) — the global scan re-arms us

                if (container.panel == null)
                {
                    dirty = true; // the whole editors list was rebuilt (selection change, lock/debug toggle…)
                    s_LastDirtyReason = "container-lost";
                }
                else
                {
                    int n = container.childCount;
                    if (n != d.Snapshot.Count) { dirty = true; s_LastDirtyReason = "count"; }
                    else
                        for (int i = 0; i < n; i++)
                            if (!ReferenceEquals(container.ElementAt(i), d.Snapshot[i])) { dirty = true; s_LastDirtyReason = "ref"; break; }
                }
            }

            if (!dirty) return false;

            d.MarkerDetached = false;
            s_PrePaintScans++;
            SasquatchInspectorHeaderPencil.ScanRoot(d.Root); // its tail re-Ensures: container + snapshot re-baseline

            // ScanRoot found no rows (e.g. everything deselected while our container ref went stale): drop the
            // stale ref so the driver goes dormant instead of rescanning on every subsequent panel update.
            if (d.Container != null && d.Container.panel == null)
            {
                d.Container = null;
                d.Snapshot.Clear();
            }
            else
            {
                // RE-BASELINE AGAINST REALITY. Do not assume ScanRoot re-baselined us: its tail (EnsureInstalled)
                // sits AFTER an early `if (rows.Count == 0) return;`, so a scan that finds NO rows never
                // re-snapshots. And a deselect does NOT detach the editors list — measured: Unity keeps the SAME
                // container element ATTACHED and merely empties its children — so the branch above cannot catch
                // it either. Without this the tick saw childCount != Snapshot.Count forever and ran a full
                // ScanRoot on every panel update while nothing was selected (measured: 9.8 scans/second,
                // indefinitely). TakeSnapshot clears and re-adds, so it is idempotent: on the normal path where
                // ScanRoot's tail already re-baselined it recomputes the identical list at O(rows) cost (and
                // correctly follows a container it just adopted); on the zero-row path it is what quiets the loop.
                TakeSnapshot(d);
            }
            return true;
        }

        private static void TakeSnapshot(Driver d)
        {
            d.Snapshot.Clear();
            var container = d.Container;
            if (container == null || container.panel == null) return;
            int n = container.childCount;
            for (int i = 0; i < n; i++)
                d.Snapshot.Add(container.ElementAt(i));
        }

        // ---- Row stamps: the in-place-rebind detector ----
        // Unity REUSES InspectorElement objects across editors (reorder, removing a component above): rows are
        // REBOUND during the panel's BINDINGS phase — after this driver's scheduler tick, before that update's
        // paint. The rebound element may never have been taken over before (the RECEIVING row of a swap), so no
        // claim-history-based hook can catch it. What identifies EVERY rebind, on ANY row, is its children being
        // REPLACED since the last scan. Each scan stamps each row's content identity (child count + child ref
        // hash) and hooks GeometryChangedEvent once (callbacks survive rebinds; classes don't). The hook compares
        // at LAYOUT time — after bindings, before render — and a mismatch rescans the window immediately, so the
        // frame paints already displaced/released. Self-quieting: the stamp is refreshed before the rescan and the
        // rescan re-stamps every row after its mutations, so steady-state geometry events (resize, foldouts)
        // compare equal and cost two field reads.
        private sealed class RowStamp
        {
            // Actual child REFERENCES, compared by identity — NOT a reference-hash digest: Mono's object hash can
            // be address-derived, so a rebind that frees a child and allocates its replacement at the same address
            // hashes identically and the rebind goes undetected (caught by a test). Holding the old children
            // strongly until the next stamp is bounded and mirrors the container snapshot above.
            public readonly List<VisualElement> Children = new();
            public bool Hooked;
        }
        private static readonly ConditionalWeakTable<VisualElement, RowStamp> s_RowStamps = new();

        /// <summary>Stamps a row's content identity and (once) installs the rebind-rescue geometry hook. Called
        /// by <see cref="SasquatchInspectorHeaderPencil.ScanRoot"/> for every row it visits, AFTER any takeover
        /// mutation, so the stamp always reflects the post-scan state.</summary>
        internal static void StampRow(VisualElement ie)
        {
            if (ie == null) return;
            var stamp = s_RowStamps.GetValue(ie, _ => new RowStamp());
            TakeRowStamp(ie, stamp);
            if (!stamp.Hooked)
            {
                stamp.Hooked = true;
                ie.RegisterCallback<GeometryChangedEvent>(OnStampedRowGeometry);
            }
        }

        private static void TakeRowStamp(VisualElement ie, RowStamp stamp)
        {
            stamp.Children.Clear();
            int n = ie.childCount;
            for (int i = 0; i < n; i++)
                stamp.Children.Add(ie.ElementAt(i));
        }

        private static bool RowStampMatches(VisualElement ie, RowStamp stamp)
        {
            int n = ie.childCount;
            if (n != stamp.Children.Count) return false;
            for (int i = 0; i < n; i++)
                if (!ReferenceEquals(ie.ElementAt(i), stamp.Children[i])) return false;
            return true;
        }

        // Fires during the LAYOUT phase whenever a stamped row's geometry changes. Steady state (resize, foldout,
        // value edits) leaves the child set identical → an O(children) identity compare and out. A mismatch means
        // the row's content was replaced outside a scan — an in-place rebind — and the window is rescanned NOW,
        // before this update's render. The stamp is refreshed first so a failed correlation can't loop the rescue.
        private static void OnStampedRowGeometry(GeometryChangedEvent evt)
        {
            var ie = evt.currentTarget as VisualElement;
            if (ie == null || !s_RowStamps.TryGetValue(ie, out var stamp)) return;
            if (RowStampMatches(ie, stamp)) return;
            TakeRowStamp(ie, stamp);
            RequestImmediateRescan(ie);
        }

        // Re-entrancy guard for RequestImmediateRescan: the rescan's own mutations can raise further
        // GeometryChangedEvents while it runs.
        private static bool s_Rescanning;

        /// <summary>
        /// Runs the scan for the window containing <paramref name="from"/> RIGHT NOW. Called by the geometry
        /// rescue in <see cref="SasquatchForceTakeover"/> from the LAYOUT phase of a panel update whose BINDINGS
        /// phase rebound a claimed row (component reorder / removal above) — after this update's scheduler tick,
        /// so waiting for the next tick would paint one competitor frame. Layout runs before render: mutations
        /// made here re-dirty layout (bounded re-iteration) and the frame renders already displaced. The window
        /// root is found by walking up to the ancestor a driver is registered on; without one (bare test rows)
        /// this is a no-op.
        /// </summary>
        internal static void RequestImmediateRescan(VisualElement from)
        {
            s_RescueRequests++;
            if (s_Rescanning) return;
            for (var ve = from; ve != null; ve = ve.parent)
            {
                if (!s_Drivers.TryGetValue(ve, out var d)) continue;
                s_Rescanning = true;
                try
                {
                    d.MarkerDetached = false; // this scan covers the pending flag for THIS window
                    s_PrePaintScans++;
                    s_LastDirtyReason = "rescue";
                    SasquatchInspectorHeaderPencil.ScanRoot(d.Root);
                }
                finally { s_Rescanning = false; }
                return;
            }
        }

        // ---- Test seams (headless tests can't pump panel schedulers) ----

        /// <summary>Runs one dirty-check tick for <paramref name="root"/>'s driver NOW. Returns whether it scanned.</summary>
        internal static bool TickForTest(VisualElement root)
            => root != null && s_Drivers.TryGetValue(root, out var d) && Tick(d);

        /// <summary>Whether a driver is installed for <paramref name="root"/>.</summary>
        internal static bool IsInstalledForTest(VisualElement root)
            => root != null && s_Drivers.TryGetValue(root, out _);
    }
}
