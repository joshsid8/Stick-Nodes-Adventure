using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Builds and injects the "Edit / Create Sasquatch Layout" pencil button that sits inline in a
    /// component's inspector header row, for both the per-editor path (owned types) and the global scan
    /// (Unity's default inspector for not-yet-owned types). See <see cref="GlobalInspectorHeaderGUI"/>
    /// for the ScriptableObject/asset IMGUI header path and the throttled scan that drives <see cref="ScanRoot"/>.
    ///
    ///   • The header host is reached via the public <see cref="InspectorElement"/> type and its public
    ///     <c>parent</c> — which IS the per-component header host.
    ///   • A header's target type is read from that row's own greyed Script field: a public
    ///     <c>ObjectField</c> bound to <c>m_Script</c> whose value is the row's <see cref="MonoScript"/>
    ///     (<see cref="MonoScript.GetClass"/> → the type). Built-in components, the GameObject header,
    ///     materials, and previews have no <c>m_Script</c> field, so this naturally self-selects exactly
    ///     the managed MonoBehaviour/ScriptableObject rows — no order-guessing, correct in
    ///     locked and multiple inspectors.
    /// </summary>
    internal static class SasquatchInspectorHeaderPencil
    {
        internal const string ButtonName = "sasquatch-header-button";

        // Matches the resting tint of Unity's own header icons (help/presets/kebab) so the button reads as
        // part of the native row — per EDITOR skin (a property, so a live skin flip restyles on the next
        // header build). Glyph is 13px inside the 16px button for the same reason. Shared with the IMGUI
        // ScriptableObject/asset header path in GlobalInspectorHeaderGUI.OnFinishedDefaultHeaderGUI.
        internal static Color HeaderIconTint => EditorGUIUtility.isProSkin
            ? new Color(0.78f, 0.78f, 0.78f, 1f)
            : new Color(0.32f, 0.32f, 0.32f, 1f);
        internal const float HeaderGlyphSize = 13f;

        // ── Title-clip backdrop ──────────────────────────────────────────────────────────────────────────
        // Unity clips a component row's title text just before ITS OWN leftmost header button — it has no idea
        // our pencil claimed another 16px further left, so a long enough component name draws straight under the
        // pencil glyph. The backdrop repaints the titlebar background across the pencil's box plus a soft
        // fade-out band on its left, so long titles cut off before the icon exactly like they do before
        // Unity's own buttons (the fade makes the cut read as intentional). Geometry note: the clip edge lands
        // exactly on the pencil box's right edge (Unity's leftmost header icon minus 4px spacing), so covering
        // just the box + fade provably covers every pixel the title can reach — which is exactly why the
        // backdrop must share the button's LIVE inset (PencilInset) rather than a constant. When Unity declines
        // an icon the clip edge moves by that same slot, and a backdrop left behind stops covering the gap.
        internal const string BackdropName = "sasquatch-header-backdrop";
        internal const float BackdropFadeWidth = 12f;
        // The component titlebar is a fixed 22px strip: row 0 = top border, rows 1..20 = fill, row 21 =
        // bottom border (measured live on Unity 6000.3). The backdrop covers exactly the fill rows.
        private const float TitlebarHeight = 22f;

        // Distance from the header's right edge to the pencil's right edge WHEN Unity draws its full icon
        // strip (⋮ + preset + "?"). Not used raw: Unity 6.6 stopped drawing "?" for user scripts, which
        // compacts the strip one slot to the right, so the live inset comes from PencilInset below. See
        // NativeInspectorHeaderIcons for the whole mechanism.
        internal const float PencilInsetWithHelp = 62f;

        /// <summary>
        /// The pencil's (and its backdrop's) live <c>right</c> inset for one row — one native icon slot
        /// narrower when Unity declines to draw the header help "?" for this target. Backdrop and button MUST
        /// share it: the backdrop exists to cover exactly the strip the title can now reach, and Unity's title
        /// clip edge moves by the same slot.
        /// </summary>
        internal static float PencilInset(Type type, UnityEngine.Object instance) =>
            NativeInspectorHeaderIcons.InsetFromHelpPresent(
                PencilInsetWithHelp, NativeInspectorHeaderIcons.ComponentTitlebarSlot, type, instance);

        // Fallbacks mirror Unity's documented editor-theme palette (foundations.unity.com color palette:
        // --unity-colors-inspector_titlebar-background / -hover). Used only if the live USS variables can't
        // be resolved from the panel — never observed in practice, but the pencil must not vanish with it.
        private static Color TitlebarFillFallback => EditorGUIUtility.isProSkin
            ? (Color)new Color32(0x3E, 0x3E, 0x3E, 0xFF) : (Color)new Color32(0xCB, 0xCB, 0xCB, 0xFF);
        private static Color TitlebarHoverFillFallback => EditorGUIUtility.isProSkin
            ? (Color)new Color32(0x47, 0x47, 0x47, 0xFF) : (Color)new Color32(0xD6, 0xD6, 0xD6, 0xFF);

        /// <summary>
        /// Resolves a Unity editor theme color variable (e.g. "--unity-colors-inspector_titlebar-background")
        /// through the <see cref="ICustomStyle"/> API. The theme variables surface on the panel's root
        /// element, not on arbitrary descendants, so this climbs the ancestor chain. Reading the live theme
        /// means light/dark skins — and any future Unity retune of these greys — are picked up automatically.
        /// </summary>
        internal static bool TryResolveThemeColor(VisualElement from, string variableName, out Color color)
        {
            var prop = new CustomStyleProperty<Color>(variableName);
            for (var ve = from; ve != null; ve = ve.parent)
            {
                if (ve.customStyle.TryGetValue(prop, out color))
                    return true;
            }
            color = default;
            return false;
        }

        /// <summary>
        /// Walks every <see cref="InspectorElement"/> under <paramref name="root"/>, injects the pencil into each
        /// managed row that doesn't already have one, and drives per-row inspector takeover
        /// (<see cref="SasquatchForceTakeover"/>). For a row it can correlate to the current selection it passes the
        /// exact tracked editor (target instance, <c>targets[]</c>, the editor object) so takeover can displace even
        /// pure-IMGUI competitor bodies and bind multi-select exactly; otherwise it falls back to the row's own
        /// <c>m_Script</c> field (pencil only, plus the selection-tied genuine Force-Takeover path).
        /// </summary>
        internal static void ScanRoot(VisualElement root)
        {
            if (root == null) return;

            var rows = root.Query<InspectorElement>().ToList();
            // Harden: drop any InspectorElement a user's embedded editor internally spawns (under an
            // .sasq-embedded-editor ancestor). Leaving them in would let our own scan pencil/displace the embedded
            // content AND shift the DOM-index↔ActiveEditorTracker correlation for the real rows below. See
            // EmbeddedEditorHost (which itself NEVER hosts an InspectorElement, only plain containers).
            rows.RemoveAll(HasEmbeddedEditorAncestor);
            if (rows.Count == 0) return;

            // Per-scan snapshot. The ActiveEditorTracker gives us the exact per-row Editor — its target
            // instance, its multi-target set, and the editor object itself — including pure-IMGUI rows that hide the
            // UITK m_Script field. We correlate by DOM order and TRUST a tracked editor only when it provably
            // belongs to the row, so a mis-correlation can never displace a wrong-typed row.
            var tracker = ActiveEditorTracker.sharedTracker.activeEditors;
            var selectedGo = Selection.activeGameObject;
            var selectedGos = Selection.gameObjects;
            var selectedObjects = Selection.objects;

            // THE TRACKER — NOT THE SELECTION — IS THE AUTHORITY on what a row is showing. The selection is only a
            // cheap proxy for it, and that proxy breaks the moment an inspector is LOCKED: the tracker stays pinned
            // to the locked object while the selection moves on. Every "is this in the selection" test below then
            // goes false at once, rows resolve to a null (or plain wrong) instance, and a live displaced body gets
            // torn out — the "I locked the inspector, clicked an asset, and parts of my layout disappeared" bug.
            // So consult the selection ONLY while the tracker is actually following it.
            bool trackerFollowsSelection = !ActiveEditorTracker.sharedTracker.isLocked;

            // Each row's own m_Script type, resolved once and reused by the correlation proof and the loop below.
            var rowTypes = new Type[rows.Count];
            for (int i = 0; i < rows.Count; i++) rowTypes[i] = ReadScriptType(rows[i]);

            // With a locked tracker the by-index correlation has to be PROVEN some other way, because the selection
            // can no longer vouch for it. The rows' own m_Script types do exactly that (see TryProveCorrelation) —
            // EXCEPT on a row set where not one row carries an m_Script field at all (a GameObject header +
            // Transform + a component whose IMGUI [CustomEditor] draws no script slot). For that shape the second
            // route below recalls the correlation THIS window already earned while it was still followable.
            //
            // That second route is not a nicety. "Uncorrelatable" sounds harmless — the row is left alone, nothing
            // is released — but MEASURED: the first selection change after a lock makes Unity DESTROY and RE-CREATE
            // every InspectorElement in the window (the tracker and the lock are untouched), and the displaced body
            // goes out with the dead row. A row we can no longer name is never handed to ApplyOrRelease again, so
            // the replacement row keeps the competitor's inspector — permanently, until the user unlocks and
            // re-selects. With the correlation intact the very same trigger re-homes the parked body instead.
            int trackerOffset = 0;
            bool lockedCorrelates = !trackerFollowsSelection &&
                                    (TryProveCorrelation(rowTypes, tracker, out trackerOffset) ||
                                     TryRecallCorrelation(root, rowTypes, tracker, out trackerOffset));

            // Tracker entries claimed by a row this scan, so the ScriptableObject-asset recovery below can't hand the
            // same editor to two rows on a multi-object selection.
            var claimedTracker = new System.Collections.Generic.HashSet<int>();

            // Did any row provably belong to a tracker entry this scan? That — and only that — is what may be
            // remembered for the recall above.
            bool correlatedThisScan = false;

            for (int i = 0; i < rows.Count; i++)
            {
                var ie = rows[i];
                // Row stamp + rebind-rescue hook — for EVERY row, including ones the correlation below skips
                // (uncorrelatable or mid-binding rows still need their content-replacement detected). Stamped
                // again at the end of the iteration so takeover mutations are reflected in the stamp.
                SasquatchTakeoverPrePaintDriver.StampRow(ie);

                var host = ie.parent; // the per-component header host, via public InspectorElement.parent
                if (host == null) continue;

                Type fromScript = rowTypes[i];

                Type type = null;
                UnityEngine.Object instance = null;
                UnityEngine.Object[] targets = null;
                UnityEditor.Editor rowEditor = null;

                int trackerIndex = i + trackerOffset;
                var ed = (tracker != null && trackerIndex >= 0 && trackerIndex < tracker.Length) ? tracker[trackerIndex] : null;
                var target = ed != null ? ed.target : null;

                // A tracked editor is trusted for this row when it provably belongs to it: either the tracker is
                // following the selection and this target is in it, or the tracker is locked and the whole row set
                // already correlated by script type. Either way the row's own m_Script may never be contradicted.
                bool trusted = target != null &&
                               (trackerFollowsSelection
                                   ? IsInSelection(target, selectedGos, selectedObjects)
                                   : lockedCorrelates) &&
                               (fromScript == null || target.GetType() == fromScript);

                if (trusted)
                {
                    // Trusted tracker row: exact per-row instance (fixes duplicate same-type components) + multi-edit.
                    type = target.GetType();
                    instance = target;
                    targets = ed.targets;
                    rowEditor = ed;
                    claimedTracker.Add(trackerIndex);
                    correlatedThisScan = true;
                }
                else if (fromScript != null)
                {
                    // Fallback: the row's own m_Script (default / UITK FillDefaultInspector rows). Best-effort
                    // instance; the blanket branch stays inert here (null rowEditor), the genuine branch still works.
                    type = fromScript;
                    // The selection can only name this row's instance while the tracker is FOLLOWING it — with a
                    // locked tracker the selected object is some unrelated thing, and reading a component off it
                    // would bind the row to the WRONG object. Leaving the instance null is safe: takeover reads
                    // that as "unidentified row, leave it alone", never as "release" (SasquatchForceTakeover).
                    //
                    // GetComponent(type) throws for a non-Component type. A nested inlined ScriptableObject editor
                    // (e.g. the In-Line Editor widget on an SO reference, or an object field that draws the SO's
                    // own editor) surfaces a ScriptableObject m_Script here — guard so its row leaves instance null
                    // instead of crashing the whole scan (which Unity reports as "Save Changes Failed").
                    instance = (trackerFollowsSelection && selectedGo != null && typeof(Component).IsAssignableFrom(type))
                        ? selectedGo.GetComponent(type)
                        : null;
                }
                else
                {
                    // ScriptableObject-ASSET recovery. For an SO asset the tracker carries the AssetImporter editor
                    // at index 0, so the SO's own editor lands at tracker[1] while its InspectorElement is at rows[0]
                    // — the by-index correlation above points at the importer (not in the selection) and the row has
                    // no m_Script field (a custom editor), so it would otherwise be skipped and never take
                    // over. Recover by scanning the tracker for an unclaimed ScriptableObject editor that the
                    // inspector is actually showing.
                    rowEditor = FindShownScriptableObjectEditor(
                        tracker, selectedGos, selectedObjects, claimedTracker, trackerFollowsSelection);
                    if (rowEditor != null)
                    {
                        type = rowEditor.target.GetType();
                        instance = rowEditor.target;
                        targets = rowEditor.targets;
                    }
                }

                if (type == null) continue;

                // Pencil (idempotent — TryInject no-ops when already present).
                TryInject(host, type, instance);

                // Per-row takeover: draw the Sasquatch Inspector layout in place of the competitor's body. Runs every tick
                // to keep it hidden across Unity's rebuilds, and releases cleanly when the gate stops applying.
                // No-op for the ~all rows with no styled layout / no opt-in. See SasquatchForceTakeover.
                SasquatchForceTakeover.ApplyOrRelease(ie, host, type, instance, targets, rowEditor);

                // Re-stamp AFTER ApplyOrRelease so the stamp reflects this scan's mutations (body built/removed) —
                // otherwise the very next geometry event would read our own displacement as a foreign rebind.
                SasquatchTakeoverPrePaintDriver.StampRow(ie);
            }

            // Bank the correlation this window just earned, so the next scan can still name these rows when the
            // proof is gone (see TryRecallCorrelation). O(rows), and it allocates only when the tracker changed.
            if (correlatedThisScan) RememberCorrelation(root, tracker, rows.Count, trackerOffset);

            // Arm/re-baseline the pre-paint driver for this window: a panel-scheduler watch that re-runs this scan
            // synchronously INSIDE the panel update — before layout and paint — the moment the editor-row set
            // changes. This is what makes takeover flash-free on rebuilds the update-loop scan can only catch a
            // tick late (component add/paste/undo, play-mode transitions…). See SasquatchTakeoverPrePaintDriver.
            // The editors-list container is reached via public parents only: row (InspectorElement) → per-editor
            // host → the list that holds every editor's host.
            VisualElement editorsContainer = null;
            for (int i = 0; i < rows.Count && editorsContainer == null; i++)
                editorsContainer = rows[i].parent?.parent;
            SasquatchTakeoverPrePaintDriver.EnsureInstalled(root, editorsContainer);
        }

        // True when an InspectorElement sits inside an embedded-editor container (EmbeddedEditorHost.ContainerClass),
        // i.e. it belongs to a user editor we re-inlined — the scan must ignore it.
        private static bool HasEmbeddedEditorAncestor(VisualElement element)
        {
            for (var p = element?.parent; p != null; p = p.parent)
                if (p.ClassListContains(EmbeddedEditorHost.ContainerClass)) return true;
            return false;
        }

        /// <summary>
        /// Proves the by-index row↔tracker correlation from the rows' OWN <c>m_Script</c> types, for the case where
        /// the selection cannot vouch for it (a LOCKED tracker — see <see cref="ScanRoot"/>). Tries a small leading
        /// offset because Unity parks an asset's AssetImporter editor at <c>tracker[0]</c> while its
        /// InspectorElement is <c>rows[0]</c>.
        ///
        /// <para>The bar is deliberately high: EVERY row that names a type must agree with the tracker entry at its
        /// index, and at least one row must supply that proof. A second, UNLOCKED inspector window scanned against
        /// the same locked tracker is showing a different object, so it fails this and its rows stay on the
        /// uncorrelated path instead of binding the locked window's instances. A row set that names no types at all
        /// (built-ins only, or an asset drawn by a custom editor) proves nothing and is left to the recovery
        /// path below.</para>
        /// </summary>
        // internal (not private) so the locked-inspector tests can drive it directly — reproducing a genuinely
        // locked tracker headlessly is not possible, but its decision is the whole fix.
        internal static bool TryProveCorrelation(Type[] rowTypes, UnityEditor.Editor[] tracker, out int offset)
        {
            offset = 0;
            if (rowTypes == null || tracker == null || tracker.Length == 0) return false;

            for (int off = 0; off <= 1; off++)
            {
                if (rowTypes.Length + off > tracker.Length) continue;

                int proof = 0;
                bool agrees = true;
                for (int i = 0; i < rowTypes.Length && agrees; i++)
                {
                    if (rowTypes[i] == null) continue; // no claim made by this row — carries no evidence either way
                    var ed = tracker[i + off];
                    var t = ed != null ? ed.target : null;
                    if (t == null || t.GetType() != rowTypes[i]) agrees = false;
                    else proof++;
                }

                if (agrees && proof > 0) { offset = off; return true; }
            }
            return false;
        }

        // ── Correlation memo (the recall route for a locked window whose rows prove nothing) ─────────────────
        // What one window's rows were correlated to, banked at the moment they were still provably correlated.
        private sealed class CorrelationMemo
        {
            public UnityEditor.Editor[] Editors; // the tracker AS IT WAS when this window last correlated
            public int RowCount;                 // and how many rows it correlated
            public int Offset;                   // at which leading offset
        }

        // Keyed by window ROOT — the one element that survives Unity re-creating every row underneath it. A
        // ConditionalWeakTable (same idiom as SasquatchTakeoverPrePaintDriver's driver table) so a closed window's
        // memo dies with its root and we pin nothing alive.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, CorrelationMemo>
            s_Correlations = new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, CorrelationMemo>();

        // Weak roll of every live memo, so a recall can ask "does any OTHER window claim this exact tracker?"
        // (a CWT is not reliably enumerable across Unity runtimes — same pattern as the pre-paint driver's list).
        // Dead entries are pruned whenever it is walked.
        private static readonly System.Collections.Generic.List<System.WeakReference<CorrelationMemo>> s_AllCorrelations =
            new System.Collections.Generic.List<System.WeakReference<CorrelationMemo>>();

        /// <summary>
        /// Banks the correlation <paramref name="root"/>'s rows just earned (through the selection, or through
        /// <see cref="TryProveCorrelation"/>) so <see cref="TryRecallCorrelation"/> can hand it back while the
        /// tracker is provably unmoved. Overwrites in place; the array is cloned only when it actually changed.
        /// </summary>
        internal static void RememberCorrelation(VisualElement root, UnityEditor.Editor[] tracker, int rowCount, int offset)
        {
            if (root == null || tracker == null) return;
            if (!s_Correlations.TryGetValue(root, out var memo))
            {
                memo = new CorrelationMemo();
                s_Correlations.Add(root, memo);
                // One entry per window root ever scanned. Prune the collected ones before adding whenever the roll
                // has grown past any plausible number of live inspector windows (open/close cycles over a session).
                if (s_AllCorrelations.Count >= 32) PruneCorrelationRoll();
                s_AllCorrelations.Add(new System.WeakReference<CorrelationMemo>(memo));
            }
            if (!SameEditors(memo.Editors, tracker)) memo.Editors = (UnityEditor.Editor[])tracker.Clone();
            memo.RowCount = rowCount;
            memo.Offset = offset;
        }

        /// <summary>
        /// Hands back the correlation THIS window already established, for the one shape
        /// <see cref="TryProveCorrelation"/> cannot prove: a LOCKED tracker whose rows carry no <c>m_Script</c>
        /// field between them, so nothing is left to prove it with. Without it those rows stay unnamed forever and
        /// their displaced bodies never come back (see <see cref="ScanRoot"/>).
        ///
        /// <para>It never INVENTS a correlation — it only recalls one this very root earned while it was still
        /// followable, and only while the tracker is provably the same one: reference-identical editor entries in
        /// the same order, and the same row count. A locked tracker cannot change, which is exactly why that check
        /// is sufficient here; the moment the tracker moves the memo stops matching and the ordinary routes take
        /// over again. Three further bars, in order of what they rule out:</para>
        /// <list type="bullet">
        ///   <item>the remembered offset must still fit the row set inside the tracker;</item>
        ///   <item>AMBIGUITY: if more than one live window banked this same tracker state (two inspectors that
        ///   were showing the same object when one of them was locked, or a floating Properties window), we
        ///   cannot tell from the outside which of them the tracker still belongs to — and binding the wrong one
        ///   would draw another object's data under this row's header, far worse than the missed re-home. Both
        ///   are refused and fall back to the uncorrelated path;</item>
        ///   <item>and no row's own <c>m_Script</c> may be contradicted — the same bar
        ///   <see cref="TryProveCorrelation"/> applies.</item>
        /// </list>
        /// <para>Not covered, deliberately — in both cases the memo is simply absent or stale, and the row falls
        /// back to exactly today's behaviour rather than to a guess: a domain reload wipes the memo, so a window
        /// locked across a recompile is uncorrelatable again until the user unlocks or re-selects; and a locked
        /// tracker that gains or loses an editor (a component added to the locked object) no longer matches what
        /// was banked, and nothing may re-bank it while the lock holds. Inventing a correlation in either is the
        /// very guess this whole mechanism refuses to make. Note the memo is only ever overwritten by a scan that
        /// really correlated, so a transient partial row set cannot clobber a good one.</para>
        /// </summary>
        // internal so the locked-inspector tests can drive it directly (a genuinely locked, row-rebuilt window is
        // not reproducible headlessly), exactly like TryProveCorrelation.
        internal static bool TryRecallCorrelation(VisualElement root, Type[] rowTypes, UnityEditor.Editor[] tracker, out int offset)
        {
            offset = 0;
            if (root == null || rowTypes == null || tracker == null || tracker.Length == 0) return false;
            if (!s_Correlations.TryGetValue(root, out var memo) || memo.Editors == null) return false;

            // Nothing about the tracker may have moved since this window earned the correlation.
            if (!SameEditors(memo.Editors, tracker)) return false;
            if (memo.RowCount != rowTypes.Length) return false;
            if (memo.Offset < 0 || rowTypes.Length + memo.Offset > tracker.Length) return false;

            if (CountMemosMatching(tracker) > 1) return false; // ambiguous — see the remarks

            for (int i = 0; i < rowTypes.Length; i++)
            {
                if (rowTypes[i] == null) continue; // no claim made by this row
                var ed = tracker[i + memo.Offset];
                var t = ed != null ? ed.target : null;
                if (t == null || t.GetType() != rowTypes[i]) return false;
            }

            offset = memo.Offset;
            return true;
        }

        // Same editors, same order, same count — reference identity, never Unity's == (a destroyed Editor must
        // still compare as "the entry I remembered", so the memo is refused on a real change, not on a death).
        private static bool SameEditors(UnityEditor.Editor[] a, UnityEditor.Editor[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        /// <summary>
        /// Tests: forget every banked correlation. The ambiguity guard counts memos PROCESS-WIDE, including
        /// the user's own real Inspector window, so a test that scans its own throwaway window is otherwise
        /// asserting against whatever else happens to be open — which is a genuine, observed flake, not a
        /// theoretical one. Call this at test entry AND in the teardown so neither direction leaks.
        /// </summary>
        internal static void ClearCorrelationMemosForTests()
        {
            // Do NOT enumerate s_Correlations: a ConditionalWeakTable is not reliably enumerable across the
            // runtimes this ships to (the note on the roll below says so). Reach the memos through the weak
            // roll instead — they are the SAME objects the table holds — and blank each one. A memo with a
            // null Editors array can never satisfy a recall (SameEditors rejects it), which is exactly the
            // "forgotten" state a test wants, and the table entry then dies with its window as usual.
            for (int i = 0; i < s_AllCorrelations.Count; i++)
                if (s_AllCorrelations[i].TryGetTarget(out var memo) && memo != null)
                {
                    memo.Editors = null;
                    memo.RowCount = -1;
                }
            s_AllCorrelations.Clear();
        }

        // How many LIVE windows banked this exact tracker state. Prunes collected entries as it walks.
        private static int CountMemosMatching(UnityEditor.Editor[] tracker)
        {
            int n = 0;
            for (int i = s_AllCorrelations.Count - 1; i >= 0; i--)
            {
                if (!s_AllCorrelations[i].TryGetTarget(out var memo)) { s_AllCorrelations.RemoveAt(i); continue; }
                if (SameEditors(memo.Editors, tracker)) n++;
            }
            return n;
        }

        private static void PruneCorrelationRoll()
        {
            for (int i = s_AllCorrelations.Count - 1; i >= 0; i--)
                if (!s_AllCorrelations[i].TryGetTarget(out _)) s_AllCorrelations.RemoveAt(i);
        }

        // Finds a tracked editor whose target is a ScriptableObject the inspector is SHOWING and that no earlier row
        // has claimed this scan. The ScriptableObject-asset takeover path: the tracker interleaves the asset's
        // AssetImporter editor, so the by-index correlation misses the SO's own editor. Membership is judged against
        // the selection only while the tracker is FOLLOWING it (<paramref name="trackerFollowsSelection"/>); a locked
        // tracker IS the shown set, so any unclaimed SO editor in it qualifies — and, just as importantly, the
        // newly-selected asset can no longer be handed to a locked window's rows. Returns null when none fits.
        private static UnityEditor.Editor FindShownScriptableObjectEditor(
            UnityEditor.Editor[] tracker, GameObject[] selectedGos, UnityEngine.Object[] selectedObjects,
            System.Collections.Generic.HashSet<int> claimed, bool trackerFollowsSelection)
        {
            if (tracker == null) return null;
            for (int k = 0; k < tracker.Length; k++)
            {
                if (claimed.Contains(k)) continue;
                var ed = tracker[k];
                var t = ed != null ? ed.target : null;
                if (t is ScriptableObject &&
                    (!trackerFollowsSelection || IsInSelection(t, selectedGos, selectedObjects)))
                {
                    claimed.Add(k);
                    return ed;
                }
            }
            return null;
        }

        // True when a tracked editor's target belongs to the current selection: a Component whose GameObject is in
        // Selection.gameObjects (multi-select included), or a ScriptableObject that is itself in Selection.objects
        // (SOs and their IMGUI bodies carry no m_Script field, so tracker correlation is the only way to reach them).
        private static bool IsInSelection(UnityEngine.Object target, GameObject[] selectedGos, UnityEngine.Object[] selectedObjects)
        {
            if (target is Component comp)
            {
                var go = comp.gameObject;
                if (selectedGos != null)
                    foreach (var g in selectedGos)
                        if (g == go) return true;
                return false;
            }
            if (target is ScriptableObject)
            {
                if (selectedObjects != null)
                    foreach (var o in selectedObjects)
                        if (o == target) return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the component <see cref="Type"/> for an inspector row from its greyed Script field. Returns
        /// null for rows without a managed-script field (built-ins, GameObject header, materials, previews) or
        /// while the field is still binding — the caller simply skips and retries on the next scan tick.
        /// </summary>
        internal static Type ReadScriptType(InspectorElement ie)
        {
            if (ie == null) return null;

            // Filter on bindingPath == "m_Script" (not "value is MonoScript"): a user field of type MonoScript
            // would value-match but carries its own property path, so this can't false-positive on it.
            // ALSO skip any m_Script belonging to a NESTED editor inside this row — an In-Line Editor widget's
            // sub-inspector, an embedded editor, or a nested InspectorElement all carry their own m_Script, and
            // matching one mis-types the row (the exported-editor + inlineData case: the row read as the inline
            // SO's type, failed the tracker correlation, and lost its pencil/takeover).
            var scriptField = ie.Query<UnityEditor.UIElements.ObjectField>()
                .Where(f => f.bindingPath == "m_Script" && !IsInNestedEditorScope(f, ie))
                .First();

            return (scriptField?.value as MonoScript)?.GetClass();
        }

        /// <summary>Class marking a container that hosts ANOTHER object's editor content inside a row (the
        /// In-Line Editor widget's sub-inspector). <see cref="ReadScriptType"/> ignores m_Script fields below it.</summary>
        internal const string NestedEditorScopeClass = "sasq-nested-editor-scope";

        private static bool IsInNestedEditorScope(VisualElement element, InspectorElement row)
        {
            for (var p = element?.parent; p != null && p != row; p = p.parent)
            {
                if (p is InspectorElement) return true; // a nested editor's own body
                if (p.ClassListContains(NestedEditorScopeClass)) return true;
                if (p.ClassListContains(EmbeddedEditorHost.ContainerClass)) return true;
            }
            return false;
        }

        /// <summary>
        /// Injects the pencil into <paramref name="host"/> for <paramref name="type"/> unless a guard rejects
        /// it (null host, not a user MonoBehaviour, override disabled, or already injected). Returns whether a
        /// button was added. <paramref name="instance"/> is an optional live object for the designer preview.
        /// </summary>
        internal static bool TryInject(VisualElement host, Type type, UnityEngine.Object instance)
        {
            if (host == null || type == null) return false;
            // Components only — ScriptableObject/asset MAIN headers go through the IMGUI finishedDefaultHeaderGUI
            // path (they never reach here), and injecting for an SO here would double the pencil.
            if (!typeof(MonoBehaviour).IsAssignableFrom(type)) return false;
            if (!SasquatchContextMenu.IsTypeEditable(type)) return false;
            if (!SasquatchEditorMenu.IsOverrideEnabled()) return false;

            bool hasLayout = LayoutRegistry.TryGet(type, instance, out var headerLayout);

            // Already injected: the button PERSISTS across the continuous re-scan (ScanRoot runs every tick),
            // so RECONCILE its at-a-glance state against the current layout rather than no-op'ing. Without this
            // the glyph's line weight (thin=no layout / bold=layout) and tooltip freeze at whatever they were
            // when the row was first built, only refreshing on a full header teardown — the "boldness doesn't
            // update until you click off the GameObject and back on after adding/removing a layout" bug.
            // DIRECT CHILDREN ONLY, and that is exactness rather than a shortcut: the pencil is added to `host`
            // itself (see the Add below), so a direct-child scan sees precisely what a subtree query would. What
            // it does NOT do is descend the InspectorElement that sits beside it — which is the entire built
            // inspector body. host.Q<Button>(ButtonName) walked all of that, per row, on every scan tick, purely
            // to find a sibling appended two lines later.
            var existing = FindInjectedButton(host);
            if (existing != null)
            {
                ReconcileButtonState(existing, type, instance, hasLayout, headerLayout);
                SyncBackdropVisibility(host, instance, PencilInset(type, instance));
                return false;
            }

            float inset = PencilInset(type, instance);
            // Backdrop first so it paints beneath the button (sibling order = paint order).
            var backdrop = BuildTitleClipBackdrop(host, inset);
            SetBackdropVisible(backdrop, !IsAddedPrefabComponent(instance));
            host.Add(backdrop);
            host.Add(BuildButton(type, instance, hasLayout, headerLayout));
            return true;
        }

        /// <summary>
        /// The one thing a live pencil points at: re-pointed by <see cref="ReconcileButtonState"/> on every
        /// scan, dereferenced by the click handler. Held on the button's <c>userData</c> so a retained button
        /// on a re-used header host can never open a stale type. See <see cref="BuildButton"/>.
        /// </summary>
        internal sealed class PencilTarget
        {
            public Type Type;
            public UnityEngine.Object Instance;
        }

        /// <summary>The live click target of an injected pencil, or null when <paramref name="button"/> is not
        /// one of ours. What this returns IS what a click opens.</summary>
        internal static PencilTarget ReadTarget(Button button) => button?.userData as PencilTarget;

        /// <summary>
        /// The pencil already injected into <paramref name="host"/>, or null. Walks the host's direct children
        /// rather than its subtree — <see cref="TryInject"/> parents the button to the host itself, so the two
        /// find the same element, but this one never descends into the sibling <c>InspectorElement</c>.
        /// </summary>
        internal static Button FindInjectedButton(VisualElement host)
        {
            // contentContainer, not hierarchy: host.Add() is what put the button there, and Add routes through
            // contentContainer — which a host that overrides it (Unity's own per-component header host is not
            // a plain VisualElement) can place somewhere other than its own first level.
            var content = host.contentContainer ?? host;
            int count = content.childCount;
            for (int i = 0; i < count; i++)
                if (content[i] is Button b && b.name == ButtonName) return b;
            return null;
        }

        /// <summary>
        /// Refreshes a live pencil's at-a-glance state — bold (thick-stroke) glyph and tooltip — to match the
        /// CURRENT layout for its type. Called every scan for an already-injected button so a layout being
        /// created or removed propagates in place, without waiting for a full header rebuild
        /// (deselect/reselect). Both the <see cref="SasquatchIconElement.Bold"/> setter and the tooltip assign
        /// no-op internally when unchanged, so the steady-state cost is a dictionary lookup and two compares.
        /// </summary>
        private static void ReconcileButtonState(Button button, Type type, UnityEngine.Object instance, bool hasLayout, InspectorLayoutAsset layout)
        {
            // RE-POINT FIRST, and unconditionally. The retained button may now sit on a host Unity re-used for a
            // completely different component (see BuildButton) — refreshing the cosmetics while leaving the click
            // on the old type is worse than not reconciling at all, because the tooltip then names the right
            // component and the click opens the wrong one. Plain field writes; nothing here allocates.
            var target = ReadTarget(button);
            if (target != null)
            {
                target.Type = type;
                target.Instance = instance;
            }

            var icon = button.Q<SasquatchIconElement>();
            if (icon != null) icon.Bold = hasLayout; // Bold's setter skips the repaint when the value is unchanged.
            button.tooltip = PencilTooltip(type, hasLayout, layout);

            // RE-APPLY THE INSET, for the same reason the click target is re-pointed: this button may now sit on
            // a host Unity re-used for a different component, and whether Unity draws its header help "?" is a
            // per-TYPE answer (see NativeInspectorHeaderIcons). A component with [HelpURL] followed by one
            // without it would otherwise leave the pencil a full icon slot out of line. Assigning an unchanged
            // StyleLength is a no-op in UI Toolkit, so the steady state costs one compare.
            button.style.right = PencilInset(type, instance);
        }

        /// <summary>
        /// Shows/hides a retained backdrop to match the CURRENT row's instance. Same staleness as the click
        /// target: the backdrop is built once and the host it hangs on can be re-used for another component, so
        /// "is this an added prefab component" has to be re-asked per scan rather than baked at build time.
        /// The backdrop is always BUILT (so either direction of a re-use is reachable) and only its display flips.
        /// </summary>
        private static void SyncBackdropVisibility(VisualElement host, UnityEngine.Object instance, float inset)
        {
            var content = host.contentContainer ?? host;
            int count = content.childCount;
            for (int i = 0; i < count; i++)
                if (content[i].name == BackdropName)
                {
                    SetBackdropVisible(content[i], !IsAddedPrefabComponent(instance));
                    // Same staleness as the button's own inset (see ReconcileButtonState) — and the two must
                    // never disagree, or the backdrop stops covering the strip the title can reach.
                    content[i].style.right = inset;
                    return;
                }
        }

        private static void SetBackdropVisible(VisualElement backdrop, bool visible)
        {
            if (backdrop == null) return;
            backdrop.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Live paint state for one row's title-clip backdrop (see BackdropName docs above).
        private sealed class BackdropState
        {
            public Color Fill;
            public Color HoverFill;
            public bool Hovered;
        }

        // A component ADDED to a prefab instance gets Unity's full-width blue "override" wash behind its
        // titlebar — a flat grey backdrop would sit visibly wrong on it, so those rare rows hide the backdrop and
        // show the plain pencil. Public PrefabUtility API, mirroring Unity's own override-background behavior.
        private static bool IsAddedPrefabComponent(UnityEngine.Object instance)
        {
            var comp = instance as Component;
            return comp != null && PrefabUtility.IsAddedComponentOverride(comp);
        }

        // Builds the title-clip backdrop: a picking-transparent strip whose right edge shares the button's
        // live anchor (PencilInset), spanning the button's 16px box plus a BackdropFadeWidth fade-out band on the
        // left, covering the titlebar's fill rows (y 1..21). Colors come from the live editor theme via
        // TryResolveThemeColor (resolved on attach), and the fill mirrors Unity's titlebar hover tint by
        // watching pointer position over the host's top strip — Unity evaluates its hover on Repaint from raw
        // mouse position, so "pointer inside the 22px row" matches what the native titlebar actually renders.
        // Always builds. Whether it SHOWS is a per-scan question about the current row's instance, answered by
        // SetBackdropVisible/SyncBackdropVisibility — not baked in here, because this host can be re-used for a
        // different component and a backdrop that was never built could not come back.
        private static VisualElement BuildTitleClipBackdrop(VisualElement host, float inset)
        {
            var state = new BackdropState
            {
                Fill = TitlebarFillFallback,
                HoverFill = TitlebarHoverFillFallback,
            };

            var backdrop = new VisualElement { name = BackdropName, pickingMode = PickingMode.Ignore };
            backdrop.style.position = Position.Absolute;
            backdrop.style.right = inset;
            backdrop.style.top = 1;
            backdrop.style.width = 16 + BackdropFadeWidth;
            backdrop.style.height = TitlebarHeight - 2;

            backdrop.generateVisualContent += ctx =>
            {
                var rect = ctx.visualElement.contentRect;
                if (rect.width <= 0f || rect.height <= 0f) return;
                Color fill = state.Hovered ? state.HoverFill : state.Fill;
                Color32 solid = fill;
                Color32 clear = new Color(fill.r, fill.g, fill.b, 0f);
                float fadeEnd = rect.xMin + Mathf.Min(BackdropFadeWidth, rect.width);

                var mesh = ctx.Allocate(8, 12);
                // Fade quad: transparent at the left edge → solid where the box begins.
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = clear });
                mesh.SetNextVertex(new Vertex { position = new Vector3(fadeEnd, rect.yMin, Vertex.nearZ), tint = solid });
                mesh.SetNextVertex(new Vertex { position = new Vector3(fadeEnd, rect.yMax, Vertex.nearZ), tint = solid });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = clear });
                // Solid quad under the pencil box itself.
                mesh.SetNextVertex(new Vertex { position = new Vector3(fadeEnd, rect.yMin, Vertex.nearZ), tint = solid });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = solid });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = solid });
                mesh.SetNextVertex(new Vertex { position = new Vector3(fadeEnd, rect.yMax, Vertex.nearZ), tint = solid });
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
                mesh.SetNextIndex(4); mesh.SetNextIndex(5); mesh.SetNextIndex(6);
                mesh.SetNextIndex(4); mesh.SetNextIndex(6); mesh.SetNextIndex(7);
            };

            backdrop.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (TryResolveThemeColor(backdrop, "--unity-colors-inspector_titlebar-background", out var fill))
                    state.Fill = fill;
                if (TryResolveThemeColor(backdrop, "--unity-colors-inspector_titlebar-background-hover", out var hover))
                    state.HoverFill = hover;
                backdrop.MarkDirtyRepaint();
            });

            // Hover mirroring. NOT pointer events: Unity's titlebar evaluates its hover tint from the live
            // mouse position during IMGUI Repaint, so it updates on ANY repaint — including ones with no
            // pointer-move event (synthetic cursor jumps, content scrolling under a stationary cursor). An
            // invisible IMGUI probe reads the same source at the same cadence, keeping the backdrop tint in
            // exact lockstep with the native fill around it.
            //
            // THIS PROBE IS NOT FREE, and an earlier version of this comment claimed it was (2026-08-13). UI
            // Toolkit executes an IMGUIContainer's onGUI as an immediate command on every panel repaint — it
            // is not mesh-cached the way the sibling generateVisualContent above is — and an immediate command
            // breaks the render batch around it. One probe per injected pencil backdrop, per repaint.
            //
            // It is kept anyway, deliberately. The pointer-event alternative is cheaper and WRONG: it only
            // updates when the pointer moves, so the backdrop's tint would fall out of step with the native
            // titlebar fill it exists to blend into — visibly, on exactly the repaints the comment above
            // lists. A cost that buys correct rendering is not a defect; what was a defect was calling it
            // zero. If this ever needs to go, the replacement has to reproduce "re-evaluate on every repaint",
            // not "re-evaluate on pointer move".
            IMGUIContainer hoverProbe = null;
            hoverProbe = new IMGUIContainer(() =>
            {
                if (Event.current == null || Event.current.type != EventType.Repaint) return;
                var panelPos = hoverProbe.LocalToWorld(Event.current.mousePosition);
                var hostLocal = host.WorldToLocal(panelPos);
                bool hovered = hostLocal.y >= 0f && hostLocal.y < TitlebarHeight &&
                               hostLocal.x >= 0f && hostLocal.x <= host.worldBound.width;
                if (hovered != state.Hovered)
                {
                    state.Hovered = hovered;
                    backdrop.MarkDirtyRepaint();
                }
            }) { pickingMode = PickingMode.Ignore };
            hoverProbe.style.flexGrow = 1f;
            backdrop.Add(hoverProbe);

            return backdrop;
        }

        // "TypeName, Assembly…" → "TypeName" for tooltip copy (inherited-layout attribution).
        internal static string ShortTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "base class";
            string full = assemblyQualifiedName;
            int comma = full.IndexOf(',');
            if (comma > 0) full = full.Substring(0, comma);
            int dot = full.LastIndexOf('.');
            return dot >= 0 ? full.Substring(dot + 1) : full;
        }

        // Resolves the pencil tooltip's three states: create / edit own / edit inherited (saving overrides).
        internal static string PencilTooltip(Type type, bool hasLayout, InspectorLayoutAsset layout)
        {
            if (!hasLayout) return "Create Sasquatch Layout";
            if (layout != null && layout.IsInheritedCopy)
                return $"Edit Sasquatch Layout (inherited from {ShortTypeName(layout.InheritedFromTypeName)}) — saving creates an override for {type.Name}";
            return "Edit Sasquatch Layout";
        }

        // Bold (thick-stroke) glyph = a custom Sasquatch layout exists for this type; thin = none yet. Lets a
        // dev tell at a glance whether the inspector is custom or the default — both stay hollow, only the line
        // weight changes. Positioned absolutely inside the header host (right:PencilInset/top:2), mirroring
        // Unity's native header buttons.
        private static Button BuildButton(Type type, UnityEngine.Object instance, bool hasLayout, InspectorLayoutAsset layout)
        {
            // The click target lives in a MUTABLE holder on userData, and the handler dereferences it at click
            // time. It must never close over `type`/`instance` directly: the button OUTLIVES the row it was
            // built for. Unity re-uses a per-editor header host for a DIFFERENT editor (it re-inits the host and
            // swaps the InspectorElement inside it rather than rebuilding the host), most visibly on the first
            // selection change after an inspector lock — which provably destroys and re-creates every
            // InspectorElement while their hosts survive. The pencil is parented to the HOST, so it survives too,
            // and TryInject's "already injected" branch reconciles it in place instead of rebuilding it. With a
            // captured type that reconcile refreshed the tooltip to the NEW component while the click still
            // opened the OLD one — "I clicked the pencil on Lists and got the SpriteClaims designer."
            var target = new PencilTarget { Type = type, Instance = instance };
            var button = new Button(() =>
            {
                // Read the holder's FIELDS at click time — ReconcileButtonState re-points them every scan.
                if (target.Type == null) return;
                SasquatchLayoutDesignerWindow.OpenWindowForType(target.Type, target.Instance);
            });
            button.name = ButtonName;
            button.userData = target;
            button.tooltip = PencilTooltip(type, hasLayout, layout);

            var icon = SasquatchIcons.Create(SasquatchIcons.Ids.PencilRuler, HeaderGlyphSize, HeaderIconTint, bold: hasLayout);
            button.Add(icon);
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;

            button.style.position = Position.Absolute;
            button.style.right = PencilInset(type, instance);
            button.style.top = 2;
            button.style.width = 16;
            button.style.height = 16;
            button.style.backgroundColor = Color.clear;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderTopLeftRadius = 3;
            button.style.borderTopRightRadius = 3;
            button.style.borderBottomLeftRadius = 3;
            button.style.borderBottomRightRadius = 3;
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;

            button.RegisterCallback<MouseOverEvent>(e =>
            {
                button.style.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.1f)
                    : new Color(0f, 0f, 0f, 0.05f);
                // Hover brightens on dark, deepens on light (white would vanish on the light header).
                icon.Tint = EditorGUIUtility.isProSkin ? Color.white : new Color(0.05f, 0.05f, 0.05f, 1f);
            });
            button.RegisterCallback<MouseOutEvent>(e =>
            {
                button.style.backgroundColor = Color.clear;
                icon.Tint = HeaderIconTint;
            });

            // The pencil is its own affordance; don't let its click toggle the component foldout or select.
            button.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            button.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());

            return button;
        }
    }
}
