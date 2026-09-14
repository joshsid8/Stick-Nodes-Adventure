using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchSpring;              // SpringMath / SpringConfig for the landing flash

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The list EYEDROPPER: arm it from a collection's header, then click — or click and DRAG — across rows in
    /// the Project or Hierarchy window, and every compatible object pops into the list.
    ///
    /// <para>Why this exists: the only way to bulk-fill a list used to be LOCK the Inspector, go multi-select
    /// somewhere else, and drag the pile back. Locking is a mode the user has to remember to turn on <i>and</i>
    /// off, and it lives in a different window from the list being filled. The eyedropper removes the lock
    /// entirely — <see cref="Event"/>.<see cref="Event.Use"/> inside the per-item GUI callbacks consumes the
    /// click <i>before</i> the tree view's own selection handling sees it, so <see cref="Selection"/> never
    /// changes and the Inspector never navigates away from the object being edited.</para>
    ///
    /// <para><b>Asset Store §2.5.g:</b> this rests entirely on public API —
    /// <c>EditorApplication</c>'s per-item GUI events (the <c>*ItemInstanceOnGUI</c> / <c>*ItemOnGUI</c> pair
    /// up to 6000.4, their <c>*ItemByEntityIdOnGUI</c> twins from 6000.5 — see the adapters by
    /// <c>OnProjectItemGUI</c>) and the two Repaint* methods beside them. No editor-internal type is named,
    /// cast, or reflected.</para>
    ///
    /// <para>The state machine below is deliberately separated from the two IMGUI hooks: everything except
    /// <see cref="OnProjectItemGUI"/>/<see cref="OnHierarchyItemGUI"/> can be driven from a test with no
    /// window open at all.</para>
    ///
    /// <para><b>TRIAL SCOPE (2026-08-26):</b> live List collections with an object-reference element type.
    /// Tag Chips, the Scene view, and the standalone export all deliberately sit this one out until the idea
    /// proves itself.</para>
    ///
    /// <para><b>The Table renderer is not "not yet" — it is UNREACHABLE, so do not wire it.</b> The designer
    /// only offers Table when <c>SasquatchReflectionUtility.IsGenericCollection</c> says so, which is
    /// <c>!IsLeafType(elementType)</c> — and EVERY <see cref="UnityEngine.Object"/> type is a leaf. So Table
    /// is offered only for multi-field element types, this button appears only for object-reference element
    /// types, and the two sets can never overlap. It WAS wired at both Table header sites and then reverted:
    /// the call sites are dead through the UI, and dead lockstep sites still have to be kept in sync forever.
    /// A code-authored (in-memory API) layout can still set Table on an object list — the renderer handles it
    /// with its single-"Value"-column fallback — but that is the only way in, and it does not earn the tax.</para>
    /// </summary>
    internal static class CollectionEyedropper
    {
        /// <summary>USS class on the header button, and the modifier it wears while armed.</summary>
        internal const string ButtonClass = "sasq-list-pick-header";
        internal const string ArmedClass = "sasq-list-pick-header--armed";

        private const string IdleTooltip = "Eyedropper — pick objects straight out of the Project or Hierarchy window (hold ALT to remove)";
        private const string ArmedTooltip = "Picking… click or drag over objects.  Hold ALT to remove.  Esc to stop.";

        // ---------------------------------------------------------------------------------------------
        // Armed state. ONE picker per domain: a second Arm disarms the first, so there is never a stale
        // mode painting over a window for a list the user has already navigated away from.
        // ---------------------------------------------------------------------------------------------

        private static VisualElement s_container;
        private static SerializedObject s_serializedObject;
        private static string s_propertyPath;
        private static Type s_elementType;
        private static Action s_onChanged;
        private static Action s_onAppended;
        private static Button s_button;

        // False once the armed SerializedObject's NATIVE side has been freed under us. Latched, because there
        // is no public "is this handle still valid" query — the first touch is how you find out, and it throws
        // rather than returning null. See TryArmedOwner.
        private static bool s_soAlive;

        // One SerializedObject per selected target, for a MULTI-SELECTION only (empty otherwise). Built once
        // at Arm rather than per pump tick: RefreshAlreadyIn runs ~30 times a second and a fresh handle per
        // target per tick is a native allocation the hover colour does not need.
        private static readonly List<SerializedObject> s_targetViews = new List<SerializedObject>();

        // Queued picks, flushed once per editor frame (see Offer/Flush). Removals ride the same coalescing
        // so an ALT sweep is one rebuild per frame exactly like an add sweep.
        private static readonly List<UnityEngine.Object> s_pending = new List<UnityEngine.Object>();
        private static readonly List<UnityEngine.Object> s_pendingRemoval = new List<UnityEngine.Object>();
        private static bool s_flushScheduled;

        // Everything this STROKE has already taken. Separate from s_pending, and cleared by BeginStroke
        // rather than by Flush — that distinction IS the feature; see Offer.
        private static readonly HashSet<UnityEngine.Object> s_strokePicked = new HashSet<UnityEngine.Object>();

        private static double s_lastRepaint;
        private static Action s_reArmDetachWatch;

        // Undo group open for the current stroke, or -1 when no stroke is in flight. See BeginStroke/EndStroke.
        private static int s_undoGroup = -1;

        // When either hook last saw a real mouse event, so the pump can settle a stroke whose release landed
        // where NO hook fires (the pointer left both windows entirely). See RepaintPump.
        private static double s_lastPickEventAt;

        // What the array ALREADY holds, so a row can say "you have this one" at a glance. Rebuilt on the
        // repaint pump rather than per row: the item callbacks run once per visible row per repaint.
        private static readonly HashSet<UnityEngine.Object> s_alreadyIn = new HashSet<UnityEngine.Object>();

        // When each object was picked, for the landing flash. Trimmed as entries expire.
        private static readonly Dictionary<UnityEngine.Object, double> s_flashAt =
            new Dictionary<UnityEngine.Object, double>();

        internal static bool IsArmed => s_container != null;

        internal static bool IsArmedFor(VisualElement container) => s_container != null && s_container == container;

        /// <summary>The element type the armed picker accepts, or null when nothing is armed. Test hook.</summary>
        internal static Type ArmedElementType => s_elementType;

        // ---------------------------------------------------------------------------------------------
        // The header button
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The eyedropper button for a List header, or <c>null</c> when this collection cannot use one.
        ///
        /// <para><b>OPT IN, NOT OPT OUT.</b> <paramref name="liveInspector"/> is true for the two REAL
        /// Inspector paths (<c>VisualInspectorEditor</c> and <c>SasquatchForceTakeover</c>) and for the
        /// Showcase Sandbox, whose whole job is to behave like one. <c>!designMode</c> is not the same
        /// question — the Layout Designer's Preview button sets <c>designMode = false</c> on purpose, the
        /// preset editor hard-wires it false, and nested in-line editor builds leave it at its default.
        /// Those would otherwise arm a mode that installs FIVE process-global hooks and writes its picks
        /// into a throwaway preview target the user never asked to edit. Same rule, and the same reasoning,
        /// as the Live Debug gate in <c>LayoutTreeBuilder.Build</c>.</para>
        ///
        /// <para>Null in DESIGN mode as well — the designer canvas is a preview and its own card drag-drop
        /// owns those events, the same rule <see cref="CollectionObjectDrop.Attach"/> follows. Null for value
        /// collections (<c>List&lt;int&gt;</c>, a struct list, a string list), which have no object slot to
        /// drop into and would light up promising something they cannot do.</para>
        /// </summary>
        /// <param name="onAppended">
        /// Invoked BEFORE <paramref name="onChanged"/>, and only when a pick actually appended something: the
        /// reveal that makes the new rows visible (expand the foldout, clear a stale search/sort, jump to the
        /// last page). Separate from <paramref name="onChanged"/> so an ALT-only strip — which appends nothing
        /// — never resets a view the user set up on purpose.
        /// </param>
        internal static Button CreateHeaderButton(VisualElement container, SerializedProperty arrayProp,
            SerializedObject serializedObject, Action onChanged, Action onAppended, bool designMode,
            bool liveInspector)
        {
            if (designMode || !liveInspector) return null;
            if (container == null || arrayProp == null || serializedObject == null) return null;

            var elementType = CollectionObjectDrop.ResolveObjectElementType(arrayProp);
            if (elementType == null) return null;

            string path = arrayProp.propertyPath;

            // Per-button, NOT a shared static: two collections on one inspector each get their own watch, and
            // storing only the last one would have list A's Arm re-arming list B's watch.
            Action reArm = null;

            Button button = null;
            button = new Button(() =>
            {
                if (IsArmedFor(container)) Disarm();
                else Arm(container, path, serializedObject, elementType, onChanged, button, reArm, onAppended);
            });
            button.AddToClassList(ButtonClass);
            button.tooltip = IdleTooltip;
            button.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Pipette, 11, WidgetPalette.LabelMuted));
            // CreateCollectionAddButton's 8px inline padding is what makes the neighbouring "+" sit in a text
            // button's worth of empty space until StyleListAddButton clears it; start narrow instead.
            button.style.paddingLeft = 4;
            button.style.paddingRight = 4;
            EditorSpringFX.ButtonSpring(button);

            // Escape while the INSPECTOR holds focus. The IMGUI hooks handle Escape for the other case (the
            // Project/Hierarchy window focused), because a KeyDown only ever reaches the focused window.
            container.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape || !IsArmedFor(container)) return;
                Disarm();
                evt.StopPropagation();
            });

            // A sticky mode outliving its own list is the failure this guards. The watch is spent after one
            // release, so Arm() re-arms it — otherwise a re-parent survivor that gets armed a second time
            // would hold the global EditorApplication handlers for the rest of the domain.
            reArm = PanelLifetime.OnRealDetach(container, () => { if (IsArmedFor(container)) Disarm(); });

            return button;
        }

        // ---------------------------------------------------------------------------------------------
        // Arm / disarm
        // ---------------------------------------------------------------------------------------------

        internal static void Arm(VisualElement container, string propertyPath, SerializedObject serializedObject,
            Type elementType, Action onChanged, Button button, Action reArmDetachWatch = null,
            Action onAppended = null)
        {
            if (container == null || serializedObject == null || elementType == null) return;

            if (IsArmed) Disarm(); // one picker per domain

            s_container = container;
            s_propertyPath = propertyPath;
            s_serializedObject = serializedObject;
            s_elementType = elementType;
            s_onChanged = onChanged;
            s_onAppended = onAppended;
            s_button = button;
            s_reArmDetachWatch = reArmDetachWatch;
            s_pending.Clear();
            s_pendingRemoval.Clear();
            s_strokePicked.Clear();
            s_flashAt.Clear();
            s_undoGroup = -1;
            s_lastRepaint = 0d;
            s_lastPickEventAt = 0d;
            s_soAlive = true;   // a fresh handle: the latch starts clean, exactly as DebugRecorder's does
            BuildTargetViews();
            RefreshAlreadyIn();

            if (s_button != null)
            {
                s_button.AddToClassList(ArmedClass);
                s_button.tooltip = ArmedTooltip;
                EditorSpringFX.PopIn(s_button, 0.8f);
            }

            // 6000.4 added the EntityId twins of these two events and 6000.6 made the int originals
            // ERROR-level obsolete (CS0619) — not a warning, a build failure that takes the whole package
            // with it. Same events, same rows, only the id type changed. See OnProjectItemGUI's adapters.
            //
            // 6000_4, NOT 6000_5 (corrected 2026-09-04). The guard has to name the version the REPLACEMENT
            // first exists in, not the one that retired the original, or every editor in between takes the
            // deprecated branch. Measured on the installed editors: projectWindowItemByEntityIdOnGUI and
            // hierarchyWindowItemByEntityIdOnGUI are absent on 6000.3 and present from 6000.4. At 6000.5 this
            // left 6000.4 subscribing to the int events after Unity had begun warning on them — 14 CS0618
            // warnings in a buyer's console, which the cross-version matrix's shipped-warning scan caught.
#if UNITY_6000_4_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI += OnProjectItemGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyItemGUI;
#else
            EditorApplication.projectWindowItemInstanceOnGUI += OnProjectItemGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#endif
            EditorApplication.update += RepaintPump;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Disarm;

            // PanelLifetime spends its watch on the first release, so an element that came back from a detach
            // and is being armed AGAIN needs the watch taken back — otherwise its second real detach frees
            // nothing and this mode keeps the global EditorApplication handlers for the rest of the domain.
            // A no-op while the watch is still armed, so calling it on every Arm is safe.
            if (s_reArmDetachWatch != null) s_reArmDetachWatch();

            RepaintBothWindows();
        }

        internal static void Disarm()
        {
            if (!IsArmed) return;

            EndStroke();   // a mode turned off mid-gesture still leaves ONE undo step behind

            // Must mirror Arm's subscribe block exactly — unsubscribing the OTHER event leaves the handler
            // live for the rest of the domain, which is the leak PanelLifetime exists to prevent. That
            // includes the VERSION GATE: if these two guards ever disagree, the mismatch is invisible on the
            // editor you are sitting on and leaks on the one you are not.
#if UNITY_6000_4_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI -= OnProjectItemGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyItemGUI;
#else
            EditorApplication.projectWindowItemInstanceOnGUI -= OnProjectItemGUI;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItemGUI;
#endif
            EditorApplication.update -= RepaintPump;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Disarm;

            if (s_button != null)
            {
                s_button.RemoveFromClassList(ArmedClass);
                s_button.tooltip = IdleTooltip;
            }

            s_container = null;
            s_serializedObject = null;
            s_propertyPath = null;
            s_elementType = null;
            s_onChanged = null;
            s_onAppended = null;
            s_button = null;
            s_reArmDetachWatch = null;
            s_targetViews.Clear();
            s_pending.Clear();
            s_pendingRemoval.Clear();
            s_strokePicked.Clear();
            s_alreadyIn.Clear();
            s_flashAt.Clear();

            RepaintBothWindows();
        }

        private static void OnPlayModeChanged(PlayModeStateChange _) => Disarm();

        // ---------------------------------------------------------------------------------------------
        // Picking
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// What <paramref name="candidate"/> would actually become in the armed collection, or null when it
        /// cannot go in. Shares <see cref="CollectionObjectDrop.Coerce"/> so "does this fit?" has one answer
        /// package-wide, and applies <see cref="CollectionObjectDrop.RefusedAsSceneRefIntoAsset"/> on top.
        ///
        /// <para>That second half matters MORE here than anywhere else: this picker's whole job is clicking
        /// rows in the Hierarchy, and the collection it is armed for may live on an ASSET. Painting a stroke
        /// across ten scene objects into a ScriptableObject's list wrote ten references the serializer then
        /// discarded — the same silent loss the collection drop had, with a gesture built to repeat it.</para>
        /// </summary>
        private static UnityEngine.Object ResolveForPick(UnityEngine.Object candidate)
        {
            if (s_elementType == null) return null;
            var resolved = CollectionObjectDrop.Coerce(candidate, s_elementType);
            if (resolved == null) return null;
            return CollectionObjectDrop.RefusedAsSceneRefIntoAsset(ArmedOwner(), resolved) ? null : resolved;
        }

        /// <summary>The armed collection's owner, or null once the SerializedObject is gone.</summary>
        private static UnityEngine.Object ArmedOwner()
        {
            UnityEngine.Object owner;
            return TryArmedOwner(out owner) ? owner : null;
        }

        /// <summary>
        /// The armed owner, and whether the handle is still usable at all. THE guard for every
        /// <see cref="s_serializedObject"/> read in this file.
        ///
        /// <para>When the inspector that created a SerializedObject is torn down, Unity frees the NATIVE side
        /// and leaves the managed wrapper alive: <c>so != null</c> is true and <c>so.targetObject</c> THROWS.
        /// So the idiomatic <c>if (so == null || so.targetObject == null) return;</c> is not a guard — it is
        /// the line that throws, and this class is exactly the shape that gets bitten: static state swept from
        /// <see cref="EditorApplication.update"/> at 30Hz, which keeps running for the tick or two between the
        /// Editor going away and <see cref="PanelLifetime"/> draining the detach.</para>
        ///
        /// <para>There is no public "is this handle valid" query, so the answer is LATCHED rather than
        /// re-asked: try once, remember. Nothing disarms from in here — <see cref="Disarm"/> runs
        /// <see cref="EndStroke"/>, which flushes, which reads the same handle. The single global sweeper
        /// (<see cref="RepaintPump"/>) drops the mode on the next tick instead.</para>
        ///
        /// <para>The two failure states are genuinely different and both matter: a FREED handle throws (and is
        /// latchable), a DESTROYED-but-valid target returns Unity's fake-null (and is not).</para>
        /// </summary>
        private static bool TryArmedOwner(out UnityEngine.Object owner)
        {
            owner = null;
            if (!s_soAlive || s_serializedObject == null) return false;
            try { owner = s_serializedObject.targetObject; }
            catch (NullReferenceException) { s_soAlive = false; return false; }
            return owner != null;
        }

        /// <summary>Would the armed collection take <paramref name="candidate"/>?</summary>
        internal static bool WouldAccept(UnityEngine.Object candidate) => ResolveForPick(candidate) != null;

        /// <summary>
        /// Strips every element holding one of <paramref name="objects"/> and applies. Returns how many went.
        /// <para>Routed through <see cref="CollectionObjectDrop.RemoveFromEvery"/>, which is where the ADD
        /// half's measured multi-selection warning already lives — the two halves of one hazard belong
        /// together. Walking the shared multi-target property here decided the strip from target[0]'s contents
        /// and then deleted that INDEX out of every selected object, destroying whatever the others held
        /// there. See the note on that method.</para>
        /// </summary>
        internal static int Remove(SerializedProperty arrayProp, SerializedObject serializedObject,
            IList<UnityEngine.Object> objects)
            => CollectionObjectDrop.RemoveFromEvery(arrayProp, serializedObject, objects);

        /// <summary>
        /// Starts a new pick STROKE, forgetting what the last one took. Called on every <c>MouseDown</c>: one
        /// press-drag-release is one stroke, and a stroke is the unit BOTH the no-duplicates rule and the undo
        /// step are measured in.
        /// </summary>
        internal static void BeginStroke()
        {
            EndStroke();                 // settle the previous one if its mouse-up went astray
            s_strokePicked.Clear();
            // INCREMENT FIRST, then capture. CollapseUndoOperations merges everything from the captured index
            // onward, and nothing advances that index on its own — so without this the group opens ALREADY
            // holding whatever the user did last (a field they typed into a moment ago), and the sweep's one
            // Ctrl+Z rewinds that too. One line above the capture, exactly as SasquatchCurveControl,
            // SasquatchGradientControl and ButtonInvoker do it.
            Undo.IncrementCurrentGroup();
            s_undoGroup = Undo.GetCurrentGroup();
            s_lastPickEventAt = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// Ends the current stroke and folds everything it wrote into ONE undo step.
        /// <para>Without this a paint is one undo step PER EDITOR FRAME, because the flush that makes a drag
        /// feel instant is also a separate <c>ApplyModifiedProperties</c> — so undoing a six-object sweep took
        /// three or four Ctrl+Z presses, each rewinding an arbitrary slice of one gesture. One gesture should
        /// be one undo, which is what every other drag in this package does
        /// (<c>SasquatchCurveControl</c>, <c>SasquatchGradientControl</c>, <c>ButtonInvoker</c>).</para>
        /// <para>Idempotent, and called from the mouse-up, the next BeginStroke, and Disarm — a stroke that
        /// ends off a row, or with the pointer outside the window entirely, still gets collapsed.</para>
        /// </summary>
        internal static void EndStroke()
        {
            if (s_undoGroup < 0) return;
            int group = s_undoGroup;
            s_undoGroup = -1;
            Flush();                     // anything still queued belongs to THIS gesture's undo step
            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Offers one object to the armed collection. Coerces, refuses anything this stroke already took, and
        /// schedules a single flush.
        ///
        /// <para><b>ONE OBJECT CAN NEVER LAND TWICE IN ONE DRAG.</b> That is a hard rule, not a nicety.
        /// <c>MouseDrag</c> fires on the SLIGHTEST movement — many times a second while the pointer sits still
        /// inside a single row — so without it, resting on one row re-appends it every frame and the list runs
        /// away under the cursor.</para>
        ///
        /// <para>The guard therefore lives in <see cref="s_strokePicked"/>, which only
        /// <see cref="BeginStroke"/> clears — NOT in <see cref="s_pending"/>, which <see cref="Flush"/> empties
        /// every editor frame. Checking the queue looks identical in a test that offers everything before
        /// flushing once, and is useless in the editor, where the flush lands between two ticks of the same
        /// drag and takes the guard with it. That was the shipped bug.</para>
        ///
        /// <para>Duplicates across SEPARATE strokes are still allowed: a list of references may legitimately
        /// hold the same object twice, deliberately clicking a row a second time is a deliberate act, and
        /// <see cref="CollectionObjectDrop.Accepted"/> and Unity's own list both permit it.</para>
        ///
        /// <para>The queue itself is what keeps a paint feeling good: a stroke across fifteen rows produces
        /// fifteen Offers in a couple of frames, and appending each separately would mean fifteen full row
        /// rebuilds. Coalescing collapses that to one append and one rebuild per frame.</para>
        /// </summary>
        internal static void Offer(UnityEngine.Object candidate)
        {
            if (!IsArmed) return;
            UnityEngine.Object owner;
            if (!TryArmedOwner(out owner)) { Disarm(); return; }

            var resolved = ResolveForPick(candidate);
            if (resolved == null) return;
            if (!s_strokePicked.Add(resolved)) return; // already taken by THIS drag

            s_pending.Add(resolved);
            ScheduleFlush();
        }

        /// <summary>
        /// The ALT twin of <see cref="Offer"/>: take this object back OUT of the list.
        /// <para>Painting to add and alt-painting to remove are the same gesture in opposite directions, and
        /// the "already in the list" colour is what makes it usable - you can see what you are about to strip
        /// before you press. Shares the stroke ledger, so one alt-drag can never touch the same row twice, and
        /// shares the flush, so a sweep is one rebuild and one undo step either way.</para>
        /// </summary>
        internal static void OfferRemoval(UnityEngine.Object candidate)
        {
            if (!IsArmed) return;
            UnityEngine.Object owner;
            if (!TryArmedOwner(out owner)) { Disarm(); return; }

            // The SAME resolution the add half uses. This deliberately used plain Coerce, so that alt could
            // still strip a scene reference an older build let into an ASSET's list — but that branch was
            // never reachable: both hooks classify the row FIRST, Classify routes through ResolveForPick, and
            // a refused scene ref comes back Blocked, which both hooks bail on before they ever get here. A
            // second answer to "does this fit?" that nothing can reach is worse than one answer.
            var resolved = ResolveForPick(candidate);
            if (resolved == null) return;
            if (!s_alreadyIn.Contains(resolved)) return;   // nothing to take out
            if (!s_strokePicked.Add(resolved)) return;

            s_pendingRemoval.Add(resolved);
            ScheduleFlush();
        }

        private static void ScheduleFlush()
        {
            if (s_flushScheduled) return;
            s_flushScheduled = true;
            EditorApplication.delayCall += () => { s_flushScheduled = false; Flush(); };
        }

        /// <summary>
        /// Appends everything queued in ONE go and pops the new rows. Returns how many landed.
        /// <para>The property is re-found by PATH rather than reused from a captured
        /// <see cref="SerializedProperty"/>, for the same reason
        /// <see cref="CollectionObjectDrop.Attach"/> does it: a row rebuild, an Undo, or an external
        /// array-size change between arming and picking invalidates the captured one.</para>
        /// </summary>
        internal static int Flush()
        {
            if (s_pending.Count == 0 && s_pendingRemoval.Count == 0) return 0;
            UnityEngine.Object flushOwner;
            if (!IsArmed || !TryArmedOwner(out flushOwner))
            {
                s_pending.Clear();
                s_pendingRemoval.Clear();
                return 0;
            }

            var batch = new List<UnityEngine.Object>(s_pending);
            var strip = new List<UnityEngine.Object>(s_pendingRemoval);
            s_pending.Clear();
            s_pendingRemoval.Clear();

            var prop = s_serializedObject.FindProperty(s_propertyPath);
            if (prop == null || !prop.isArray) return 0;

            int removed = Remove(prop, s_serializedObject, strip);

            // Re-found after a strip rather than reused: the multi-selection branch inside Remove applies
            // through each target's OWN SerializedObject and then Updates this one, so the handle from before
            // it describes a state that no longer exists. The same reason the property is found by path here
            // at all, one step further in.
            if (removed > 0) prop = s_serializedObject.FindProperty(s_propertyPath);
            if (prop == null || !prop.isArray) return 0;

            // Where the appended run STARTS — read after the strip and before the append, because both change
            // it. The only honest way to tell the rows that just landed from the rows that merely happen to be
            // last (see PopNewRows).
            int firstNewIndex = prop.arraySize;

            int added = CollectionObjectDrop.Append(prop, s_serializedObject, batch);
            if (added <= 0 && removed <= 0) return 0;

            // Stamp the landing flash and fold the new elements into the "already in" set immediately, so the
            // very next repaint shows both — waiting for the pump would drop the first frame of the flash.
            double now = EditorApplication.timeSinceStartup;
            for (int i = 0; i < added && i < batch.Count; i++)
            {
                s_flashAt[batch[i]] = now;
                s_alreadyIn.Add(batch[i]);
            }

            foreach (var gone in strip) s_alreadyIn.Remove(gone);

            var container = s_container;
            var onChanged = s_onChanged;
            // REVEAL BEFORE THE REBUILD, and only for an APPEND. An append lands at the END of the array, and
            // four separate pieces of view state can park it out of sight — a collapsed foldout, a stale
            // search query, a view sort, and a page that is not the last one (pagination is ON by default at
            // 25 rows). The user then sees the count tick and nothing else, which reads as "the pick did
            // nothing" — and re-clicking a row IS a new stroke, so it lands a second time. The drop path has
            // had this since the invisible-append fix; the header pick was the site it missed.
            // Deliberately NOT wired to onChanged: an ALT-only strip appends nothing and must never throw away
            // a search or a page the user set up on purpose.
            if (added > 0) s_onAppended?.Invoke();
            onChanged?.Invoke();
            if (added > 0) PopNewRows(container, firstNewIndex);
            RepaintBothWindows();
            return added + removed;
        }

        /// <summary>
        /// The payoff: the rows that just landed spring in. Deferred one tick because the rebuild above
        /// re-creates every row element — the ones queried now are about to be thrown away.
        /// <para>The count pill is punched unconditionally, because the new element is NOT guaranteed to be on
        /// screen: sort, search and pagination can all park it on another page, and a pick with no feedback
        /// at all reads as a pick that did not work.</para>
        /// </summary>
        private static void PopNewRows(VisualElement container, int firstNewIndex)
        {
            if (container == null || container.panel == null) return;

            container.schedule.Execute(() =>
            {
                var pill = container.Q<Label>(className: LayoutTreeBuilder.ListCountPillClass);
                if (pill != null) EditorSpringFX.LandPunch(pill);

                var rows = container.Query<VisualElement>(className: LayoutTreeBuilder.ListRowClass).ToList();
                if (rows.Count == 0) return;

                // The rows that actually landed, by their ARRAY INDEX rather than by position. Taking the tail
                // of the query was two separate lies: a search, a sort or a page that is not the last one
                // renders some other slice entirely, and a UQuery walks the whole SUBTREE in document order —
                // so a collection nested inside an In-Line Editor row contributes its own rows AFTER the outer
                // row that contains them. Either way the spring fired on an untouched row and said "this one
                // is new". A row with no index, or one an inner list owns, is simply not ours.
                var fresh = new List<VisualElement>();
                foreach (var row in rows)
                {
                    if (!(row.userData is int index) || index < firstNewIndex) continue;
                    if (HasRowAncestor(row, container)) continue;
                    fresh.Add(row);
                }

                // Nothing on screen is a legitimate answer, not a reason to pop something else: the count pill
                // above is punched unconditionally precisely because the new element may be off-page.
                if (fresh.Count == 0) return;
                if (fresh.Count == 1) EditorSpringFX.PopIn(fresh[0]);
                else EditorSpringFX.StaggerIn(fresh);
            }).ExecuteLater(0);
        }

        // True when ANOTHER list row sits between this one and the collection box — i.e. it belongs to a
        // collection nested inside one of our rows, not to us. Mirrors CollectionObjectDrop's inner-host walk.
        private static bool HasRowAncestor(VisualElement row, VisualElement container)
        {
            for (var ve = row.parent; ve != null && ve != container; ve = ve.parent)
                if (ve.ClassListContains(LayoutTreeBuilder.ListRowClass)) return true;
            return false;
        }

        // ---------------------------------------------------------------------------------------------
        // The two IMGUI hooks
        //
        // Spiked in the live editor before any of this was written. What the spike settled:
        //  - these callbacks fire on Layout, Repaint, ScrollWheel, MouseDown and MouseDrag — but NOT on
        //    MouseMove, which a window without `wantsMouseMove` discards before OnGUI runs. Hover therefore
        //    needs the repaint pump below; it cannot ride on mouse-move events;
        //  - `Event.current.mousePosition` is in the SAME coordinate space as `selectionRect`, so the two are
        //    compared directly and never converted;
        //  - `Use()` on a MouseDown genuinely suppresses the tree view's selection change (verified against a
        //    control click that DID change it);
        //  - MouseDrag keeps arriving after a MouseDown was consumed, which is what makes drag-paint work.
        //    Confirmed again with a REAL mouse afterwards: 609 drag events over one gesture, and `hotControl`
        //    stays 0 throughout — this needs no IMGUI mouse capture of its own.
        //  - the callback fires for BOTH Project panes in one pass, each in its OWN coordinate space. That is
        //    the trap RowHitBand exists for; read its note before touching any hit geometry here.
        // ---------------------------------------------------------------------------------------------

        // ---------------------------------------------------------------------------------------------
        // ROW-ID ADAPTERS. Unity's per-item GUI events hand over a row ID, and the ID TYPE changed across
        // the supported range: an int instance id up to 6000.3, a UnityEngine.EntityId from 6000.4 (and by
        // 6000.6 EditorUtility.InstanceIDToObject(int) is ERROR-obsolete, CS0619). Both handlers only
        // ever used the id to resolve the row's Object, so the version split is confined to these two
        // one-line adapters and the real bodies below take the resolved Object and never see an id at all.
        // EntityId does not exist on 6000.0, so the parameter type itself has to sit inside the #if.
        //
        // THREE TIERS, NOT TWO, and the middle one is the whole point (2026-09-04). The two replacements
        // arrive in DIFFERENT versions - measured by compiling against each installed editor:
        //
        //     EditorUtility.EntityIdToObject      absent on 6000.2, present and clean from 6000.3
        //     ...ByEntityIdOnGUI (the events)     absent on 6000.3, present from 6000.4
        //
        // So on 6000.3 the event still hands over an int while the modern resolver already exists, and an int
        // implicitly converts to EntityId there (verified: it compiles with no warning). Collapsing that case
        // into the old branch is what put four CS0618 warnings in a 6000.3 buyer's console. Note the middle
        // tier is 6000.3 ONLY - the same int call on 6000.4 warns, because the implicit conversion is itself
        // deprecated by then, which is exactly why the top tier takes over there.
        // ---------------------------------------------------------------------------------------------
#if UNITY_6000_4_OR_NEWER
        private static void OnProjectItemGUI(EntityId rowId, Rect selectionRect)
            => OnProjectItemGUI(EditorUtility.EntityIdToObject(rowId), selectionRect);
#elif UNITY_6000_3_OR_NEWER
        private static void OnProjectItemGUI(int instanceID, Rect selectionRect)
            => OnProjectItemGUI(EditorUtility.EntityIdToObject(instanceID), selectionRect);
#else
        private static void OnProjectItemGUI(int instanceID, Rect selectionRect)
            => OnProjectItemGUI(EditorUtility.InstanceIDToObject(instanceID), selectionRect);
#endif

        private static void OnProjectItemGUI(UnityEngine.Object obj, Rect selectionRect)
        {
            var e = Event.current;
            if (e == null || !IsArmed) return;
            if (HandleEscape(e)) return;
            StampMouseActivity(e);

            var hit = RowHitBand(selectionRect);
            bool over = hit.Contains(e.mousePosition);
            if (!NeedsClassifying(e, over)) return;

            // The INSTANCE/EntityId event, NOT the guid twin. Both fire for exactly the same rows (measured:
            // 35 and 35), but this one hands over the ROW'S OWN OBJECT, and that buys two things the guid
            // cannot:
            //   * EXACT typing. A guid only reaches the MAIN asset type, so a List<SomeComponent> had to answer
            //     "a prefab MIGHT have that component" and light every prefab up, then quietly do nothing for
            //     the ones that did not. Component lists are common; a highlight that lies is worse than none.
            //   * SUB-ASSETS. Sprites sliced out of a sheet, meshes inside an FBX. A guid resolves those rows to
            //     the PARENT, so picking sprite #5 out of an expanded sheet used to add sprite #1.
            // Cost, measured before switching: 20 full passes over 735 row resolutions = 6ms total.
            // (The id -> Object resolution itself now happens in the version-guarded adapter above.)
            var kind = ClassifyProjectItem(obj);
            PaintRow(hit, kind, FlashAmount(obj));

            // NAVIGABLE rows are handed straight back to Unity - no paint, no claim. Folders are how you get
            // around, and measuring settled it: clicking a folder in the project TREE does not touch Selection
            // at all, so browsing to another folder keeps the inspector (and this whole session) exactly where
            // it is. Blocking them was the thing that made the armed mode feel like a cage. UNADDRESSABLE rows
            // (the tree's synthetic roots, which resolve to no Object at all) go the same way: they are not
            // candidates in either direction, so swallowing their clicks would be a cage for no gain.
            bool claimable = kind != RowKind.Navigable && kind != RowKind.Unaddressable;

            // THE MOUSE-UP CLOSES THE STROKE WHEREVER IT LANDS, so this sits ABOVE the geometry guards below.
            // Behind them, a release in the blank area under the last row, in an icon-grid gap, or on a folder
            // never reached EndStroke — the undo group stayed open, and the NEXT collapse then reached forward
            // over everything the user had done in between and folded it into the sweep's one Ctrl+Z. The
            // CLAIM stays exactly as narrow as the press that opened the gesture was.
            if (HandleStrokeEnd(e, over && claimable)) return;

            if (!claimable) return;
            if (!over) return;
            if (!IsPickEvent(e)) return;
            if (e.type == EventType.MouseDown) BeginStroke();
            // Claimed even when BLOCKED: letting a wrong-type asset through would select it, navigate the
            // Inspector off the list, and end the session over a misclick. Swallowing makes that a no-op.
            e.Use();

            if (kind == RowKind.Blocked) return;
            if (e.alt) OfferRemoval(obj); else Offer(obj);
        }

#if UNITY_6000_4_OR_NEWER
        private static void OnHierarchyItemGUI(EntityId rowId, Rect selectionRect)
            => OnHierarchyItemGUI(EditorUtility.EntityIdToObject(rowId), selectionRect);
#elif UNITY_6000_3_OR_NEWER
        private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
            => OnHierarchyItemGUI(EditorUtility.EntityIdToObject(instanceID), selectionRect);
#else
        private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
            => OnHierarchyItemGUI(EditorUtility.InstanceIDToObject(instanceID), selectionRect);
#endif

        private static void OnHierarchyItemGUI(UnityEngine.Object obj, Rect selectionRect)
        {
            var e = Event.current;
            if (e == null || !IsArmed) return;
            if (HandleEscape(e)) return;
            StampMouseActivity(e);

            var hit = RowHitBand(selectionRect);
            bool over = hit.Contains(e.mousePosition);
            bool inGutter = InRowIndentGutter(hit, e.mousePosition);
            if (!NeedsClassifying(e, over || inGutter)) return;

            var kind = Classify(obj);
            PaintRow(hit, kind, FlashAmount(obj));

            if (HandleStrokeEnd(e, over)) return;   // above the guards — see the note in the project hook

            // THIS ROW'S OWN BLANK INDENT, claimed but never picked from. RowHitBand hands back exactly the
            // rect Unity gave us, which on a nested row begins AFTER the fold-out triangle — and a press in
            // the strip left of it is not "nothing", as the note there assumed. Unity's tree view runs its
            // selection against the FULL row, so a near-miss click selected that object, navigated the
            // Inspector off the list and ended the session. Bounded strictly left of the triangle, and the
            // HIERARCHY only: the Project window's two panes live in different coordinate spaces, so a band
            // reaching toward x = 0 there is the drag theft RowHitBand's note exists to prevent.
            if (e.type == EventType.MouseDown && e.button == 0 && inGutter && !over)
            {
                e.Use();
                return;
            }

            if (!over) return;
            if (!IsPickEvent(e)) return;
            if (e.type == EventType.MouseDown) BeginStroke();
            e.Use();   // blocked rows are swallowed too — see the note in the project hook

            // UNADDRESSABLE here is a SCENE HEADER row (the callback gets the scene's handle, not an instance
            // id, so it resolves to nothing). Unlike the project window's tree roots it is still CLAIMED:
            // clicking one selects the SceneAsset, which is a Selection change, which navigates the Inspector
            // off the list and ends the session — the exact thing the swallow above exists to prevent. What it
            // must not have is the paint; see PaintRow.
            if (kind == RowKind.Blocked || kind == RowKind.Unaddressable) return;
            if (e.alt) OfferRemoval(obj); else Offer(obj);
        }

        // One indent step, matching the measured 16 → 30 → 44 progression in RowHitBand's note: the fold-out
        // triangle sits in the step immediately left of the row rect, and it has to keep working while armed.
        private const float IndentStep = 16f;

        // The blank strip left of a nested row's own rect — this row's line, and nothing else's.
        private static bool InRowIndentGutter(Rect band, Vector2 mouse)
            => mouse.y >= band.y && mouse.y < band.yMax
               && mouse.x >= 0f && mouse.x < band.x - IndentStep;

        /// <summary>
        /// Is this row's classification actually going to be USED this event?
        /// <para>Resolving a row costs an id-to-Object lookup, and in the project
        /// window an <c>AssetDatabase.GetAssetPath</c> + <c>IsValidFolder</c> + a <c>Coerce</c> on top. The
        /// callbacks run once per visible row per event, and one real drag is 609 <c>MouseDrag</c> events —
        /// so on anything that is not a Repaint, the only row whose answer matters is the one the pointer is
        /// on. <see cref="PaintRow"/> needs every row, but only on Repaint; a MouseUp is once per gesture and
        /// closes the stroke wherever it lands, so it is never skipped.</para>
        /// </summary>
        private static bool NeedsClassifying(Event e, bool wanted)
            => wanted || e.type == EventType.Repaint || e.type == EventType.MouseUp;

        /// <summary>
        /// The clickable band for one row: the rect the pointer is tested against, and the rect the highlight
        /// paints.
        ///
        /// <para><b>NEVER widen this to the RIGHT.</b> The two-column Project browser draws its left tree and
        /// its right pane in DIFFERENT coordinate spaces (the pane is a nested GUI group), and the callback
        /// fires for BOTH in the same event pass — each seeing an <c>Event.current.mousePosition</c> expressed
        /// in its own space. A band widened past the row's own right edge therefore matches on Y alone: the
        /// left tree draws first, a folder row claims the drag with <c>Use()</c>, and the prefab the pointer is
        /// genuinely over sees <c>EventType.Used</c> and never picks. Measured 2026-08-26 against a real mouse
        /// drag: 609 drag events, every one stolen by a left-tree folder, every right-pane prefab robbed —
        /// which is exactly why drag-paint appeared to do nothing while a single click worked.</para>
        ///
        /// <para>Nor LEFT. <c>selectionRect.x</c> steps one indent per nesting level (measured: 16 → 30 → 44),
        /// so the rect begins AFTER the fold-out triangle — and that triangle is how the user expands a folder
        /// or a GameObject while the picker is armed. Reaching left to grab the indent costs expand/collapse
        /// in both windows, which is a far worse trade than a press in blank indent doing nothing.</para>
        ///
        /// <para>What is left is the identity function, and that is the point: this is the ONE place row hit
        /// geometry is decided, and the answer is "exactly the rect Unity handed you". It stays a named method
        /// so the reasoning has somewhere to live and the invariant has something to test.</para>
        /// </summary>
        internal static Rect RowHitBand(Rect selectionRect) => selectionRect;

        // A left-button press or the drag that continues it. Right/middle are left alone so the context menu
        // still works, and a right-click is the natural "get me out of here" reflex.
        private static bool IsPickEvent(Event e)
            => e.button == 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag);

        // The last sign of a live gesture, for the pump's stroke timeout. Only while a stroke is actually in
        // flight, and only for real mouse events — a Repaint would keep a STRANDED stroke looking alive
        // forever, which is precisely the case the timeout exists to catch.
        private static void StampMouseActivity(Event e)
        {
            if (s_undoGroup < 0) return;
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
                s_lastPickEventAt = EditorApplication.timeSinceStartup;
        }

        // Mouse-up closes the gesture: the stroke's edits become ONE undo step. The END is unconditional and
        // the CLAIM is not - `claim` is true only where the press that opened the gesture would itself have
        // been claimed, because handing a release back on its own invites the tree view to treat a press it
        // never saw as a completed click, while claiming one over a folder or in blank space steals a click
        // this mode never had any business in.
        private static bool HandleStrokeEnd(Event e, bool claim)
        {
            if (e.type != EventType.MouseUp || e.button != 0) return false;
            EndStroke();
            if (claim) e.Use();
            return true;
        }

        private static bool HandleEscape(Event e)
        {
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.Escape) return false;
            Disarm();
            e.Use();
            return true;
        }

        /// <summary>What one row is, while the picker is armed. Drives BOTH the paint and whether the click
        /// is claimed, so the colour a row wears is always the truth about what clicking it will do.</summary>
        internal enum RowKind
        {
            /// <summary>A folder. Unity keeps it: no paint, no claim, browse freely.</summary>
            Navigable,
            /// <summary>Fits, and is not in the list yet.</summary>
            Pickable,
            /// <summary>Fits, and the list ALREADY holds it.</summary>
            Already,
            /// <summary>Cannot go in this list. Claimed and swallowed so a misclick costs nothing.</summary>
            Blocked,
            /// <summary>
            /// Not backed by an Object at all — a Hierarchy SCENE HEADER row (which hands the callback the
            /// scene's handle, not an instance id) or one of the Project tree's synthetic roots. Distinct from
            /// <see cref="Blocked"/>, which means a real object that does not fit: these rows are not
            /// candidates in either direction, so painting them the "wrong type" scrim was a dark band across
            /// the scene name saying "refused" about something nobody offered.
            /// </summary>
            Unaddressable
        }

        internal static RowKind Classify(UnityEngine.Object candidate)
        {
            if (candidate == null) return RowKind.Unaddressable;
            // Blocked, not Pickable: a scene object aimed at an ASSET's collection paints as refused, so the
            // user is told BEFORE they press rather than after the next domain reload empties the row.
            var resolved = ResolveForPick(candidate);
            if (resolved == null) return RowKind.Blocked;
            return s_alreadyIn.Contains(resolved) ? RowKind.Already : RowKind.Pickable;
        }

        /// <summary>A project row: folders are Unity's, everything else goes through the shared
        /// <see cref="Classify"/> so the project and hierarchy windows can never disagree about a type.</summary>
        private static RowKind ClassifyProjectItem(UnityEngine.Object obj)
        {
            if (obj == null) return RowKind.Unaddressable;
            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path)) return RowKind.Navigable;
            return Classify(obj);
        }

        // ---------------------------------------------------------------------------------------------
        // Paint
        // ---------------------------------------------------------------------------------------------

        /// <summary>"You already have this one." Deliberately NOT the accent - accent means "available", and
        /// the whole value of the colour is telling those two apart at a glance while you sweep.</summary>
        private static Color AlreadyWash => WidgetPalette.IsLight
            ? new Color(0.16f, 0.52f, 0.28f, 0.20f)
            : new Color(0.26f, 0.74f, 0.42f, 0.18f);

        /// <summary>"Alt is down and this one is coming back out." Red because it is destructive - amber in
        /// this package means "this pointed somewhere and no longer does", which is a different statement.</summary>
        private static Color RemoveWash => WidgetPalette.IsLight
            ? new Color(0.72f, 0.18f, 0.18f, 0.20f)
            : new Color(0.86f, 0.30f, 0.30f, 0.18f);

        private static Color RemoveEdge => WidgetPalette.IsLight
            ? new Color(0.62f, 0.13f, 0.13f, 0.85f)
            : new Color(0.90f, 0.38f, 0.38f, 0.75f);

        private static Color AlreadyEdge => WidgetPalette.IsLight
            ? new Color(0.13f, 0.45f, 0.24f, 0.85f)
            : new Color(0.32f, 0.82f, 0.50f, 0.75f);

        /// <summary>"Nothing to do here." A scrim has to move toward the WINDOW's own background, and that
        /// polarity flips with the skin - the same rule WidgetPalette's RowDivider/RowRail follow. Black over
        /// a light row is a 0.27 luminance step against 0.08 on dark, so the rows the user CANNOT click ended
        /// up the loudest thing on a light editor while the pickable ones sat at accent-soft's 0.14.</summary>
        private static Color RecedeWash => WidgetPalette.IsLight
            ? new Color(1f, 1f, 1f, 0.45f)
            : new Color(0f, 0f, 0f, 0.35f);

        /// <summary>The hovered row's fill. The *Edge colours are 0.70-0.85 alpha, which as a full-rect fill
        /// over a row Unity has ALREADY drawn leaves the icon and label about 15% visible - so the one row the
        /// user is pointing at, the one whose name decides whether they press, was the least readable row in
        /// the window. Capped to sit clearly hotter than the 0.18-0.20 resting wash and still legible.</summary>
        private static Color HoverFill(Color edge)
        {
            edge.a = WidgetPalette.IsLight ? 0.32f : 0.28f;
            return edge;
        }

        // Drawing during a mouse event corrupts the IMGUI draw list, and a Layout pass has nothing to show.
        private static void PaintRow(Rect band, RowKind kind, float flash)
        {
            if (Event.current.type != EventType.Repaint) return;

            // A navigable row is left completely alone. Dimming it would say "blocked", which would be a lie
            // now that folder clicks pass straight through. An UNADDRESSABLE row is left alone for a stronger
            // reason: it is not a candidate in either direction, so any wash at all is a statement about a
            // scene header or a tree root that nothing was ever going to do anything with.
            if (kind == RowKind.Navigable || kind == RowKind.Unaddressable) return;

            bool hovered = band.Contains(Event.current.mousePosition);

            // ALT INVERTS THE WHOLE READING. Holding alt turns the gesture into "take these back out", so the
            // rows that were the goal (green, already in the list) become the targets, and the rows that were
            // the targets have nothing to give and recede. Painting the add colours while alt is down would
            // promise the opposite of what the click does.
            bool removing = Event.current.alt;
            Color wash;
            if (removing)
            {
                wash = kind == RowKind.Already
                    ? (hovered ? HoverFill(RemoveEdge) : RemoveWash)
                    : RecedeWash;   // nothing here to remove
            }
            else
            {
                switch (kind)
                {
                    case RowKind.Already:
                        wash = hovered ? HoverFill(AlreadyEdge) : AlreadyWash;
                        break;
                    case RowKind.Pickable:
                        wash = hovered ? HoverFill(WidgetPalette.AccentEdge) : WidgetPalette.AccentSoft;
                        break;
                    default:
                        wash = RecedeWash; // recede rather than shout
                        break;
                }
            }

            EditorGUI.DrawRect(band, wash);

            // The LANDING FLASH: a bright wash over the resting colour that springs away. Never a SCALE -
            // Unity draws the row's icon and label itself and they cannot move, so a box growing around
            // stationary text reads as a rendering glitch rather than a pop. A decaying flash is the motion
            // IMGUI can tell honestly.
            // MotionEnabled, like every other animation in this gesture (the button's PopIn, the count pill's
            // LandPunch, the new rows' StaggerIn) - EditorSpringFX gates those itself, and a hand-rolled IMGUI
            // effect has to ask. It was the LOUDEST motion here and the only piece that could not be turned
            // off, so with reduce-motion on the whole feature went still except the rows, which strobed.
            if (flash > 0.001f && EditorSpringFX.MotionEnabled)
            {
                var pop = AlreadyEdge;
                pop.a = Mathf.Clamp01(flash) * 0.85f;
                EditorGUI.DrawRect(band, pop);
            }

            bool actionable = removing ? kind == RowKind.Already : kind != RowKind.Blocked;
            if (actionable) EditorGUIUtility.AddCursorRect(band, MouseCursor.Link);
        }

        // ---------------------------------------------------------------------------------------------
        // The landing flash, on a real Sasquatch spring
        // ---------------------------------------------------------------------------------------------

        private const float FlashSeconds = 0.30f;
        private static float[] s_flashCurve;

        /// <summary>
        /// The flash envelope, sampled ONCE from <see cref="SpringMath"/> into a small table.
        /// <para>The house springs are UI Toolkit-bound (<see cref="EditorSpringFX"/> animates a
        /// <see cref="VisualElement"/>), and there is no VisualElement here - the Project and Hierarchy rows
        /// are IMGUI that Unity draws. Baking the same integrator into a curve keeps the FEEL genuinely
        /// Sasquatch instead of substituting a linear fade, and costs one table for the session. Same trick
        /// <c>SpringConfigDrawer</c> uses to draw its preview curve.</para>
        /// </summary>
        private static float[] FlashCurve()
        {
            if (s_flashCurve != null) return s_flashCurve;

            // Bounce 0.2, not the house pop's 0.45: a flash is CLAMPED at 0, so an overshoot below it is
            // simply discarded and buys nothing but dead time on the end of the window. Sampled: 0.45
            // reaches zero at 0.19s and leaves the last 0.23s flat, while 0.2 decays smoothly and lands
            // on zero exactly as the window closes.
            var config = new SpringConfig(FlashSeconds, 0.2f);
            float freq = config.Frequency, damp = config.Damping, eps = config.ResolvedSettleEpsilon;

            const int Samples = 48;
            var table = new float[Samples];
            float dt = FlashSeconds / (Samples - 1);
            float pos = 1f, vel = 0f;
            for (int i = 0; i < Samples; i++)
            {
                table[i] = Mathf.Clamp01(pos);
                SpringMath.Evaluate(dt, pos, vel, 0f, freq, damp, eps, out pos, out vel);
            }
            s_flashCurve = table;
            return table;
        }

        internal static float FlashAmount(UnityEngine.Object obj)
        {
            if (obj == null || s_flashAt.Count == 0) return 0f;
            double at;
            if (!s_flashAt.TryGetValue(obj, out at)) return 0f;

            float age = (float)(EditorApplication.timeSinceStartup - at);
            if (age < 0f || age >= FlashSeconds) return 0f;

            var table = FlashCurve();
            int i = Mathf.Clamp(Mathf.RoundToInt(age / FlashSeconds * (table.Length - 1)), 0, table.Length - 1);
            return table[i];
        }

        // ---------------------------------------------------------------------------------------------
        // Repaint
        // ---------------------------------------------------------------------------------------------

        // Without mouse-move events there is no other way to keep the hover highlight current. Throttled to
        // ~30Hz and torn down on Disarm — an always-on repaint pump on two windows is not acceptable.
        private static void RepaintPump()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - s_lastRepaint < 0.033d) return;
            s_lastRepaint = now;

            // THE ONE SELF-HEAL SITE. Nothing disarms from inside a guard (Disarm flushes, and the flush reads
            // the same handle), so the single global sweeper does it: the tick after the inspector's
            // SerializedObject was freed under us — or its target was simply deleted — the mode drops itself
            // rather than painting both windows for a list that no longer exists. Offer/OfferRemoval/Flush all
            // treat a missing owner exactly this way; this one used to shrug and keep going.
            UnityEngine.Object pumpOwner;
            if (!TryArmedOwner(out pumpOwner)) { Disarm(); return; }

            // A stroke whose release landed where NO hook fires - the pointer left both windows before the
            // button came up - would otherwise leave the undo group open until the next press or Disarm, and
            // that collapse then reaches forward over everything the user did in between. Worst case here is a
            // long enough mid-drag pause splitting one sweep into two undo steps.
            if (s_undoGroup >= 0 && now - s_lastPickEventAt > StrokeIdleSeconds) EndStroke();

            // Re-read the array HERE, not in the item callback: the callbacks run once per visible ROW per
            // repaint, so a per-row read would re-walk the whole array tens of times a frame. Once per pump
            // tick also picks up edits made anywhere else - an Undo, a row deleted in the inspector, a script.
            RefreshAlreadyIn();
            ExpireFlashes(now);
            RepaintBothWindows();
        }

        // How long a stroke may go without a single mouse event before the pump settles it. Long enough that
        // a deliberate pause mid-sweep is not split, short enough that a stranded group cannot swallow a
        // later edit.
        private const double StrokeIdleSeconds = 0.5d;

        /// <summary>
        /// Re-reads what the array currently holds, for the "you already have this one" colour.
        /// <para>Across a MULTI-SELECTION this is the UNION of every selected object's array, read through
        /// their own views. The shared multi-edit property answers for target[0] alone, so both things that
        /// stand on this set lied about the rest of the selection: an object only the SECOND object's list
        /// held painted as Pickable, and <see cref="OfferRemoval"/>'s gate then refused to strip it at all.
        /// Union, never intersection — alt-clicking a row means "take it out of whichever of these hold it".</para>
        /// </summary>
        internal static void RefreshAlreadyIn()
        {
            s_alreadyIn.Clear();

            if (s_targetViews.Count > 0)
            {
                foreach (var view in s_targetViews) CollectAlreadyIn(view);
                return;
            }

            UnityEngine.Object owner;
            if (!TryArmedOwner(out owner)) return;
            CollectAlreadyIn(s_serializedObject);
        }

        /// <summary>Folds one object's array into <see cref="s_alreadyIn"/>.</summary>
        private static void CollectAlreadyIn(SerializedObject view)
        {
            if (view == null) return;

            SerializedProperty prop;
            // Guarded as a unit: a non-null targetObject does not prove the handle is usable, and these two
            // dereference the same freed pointer. The per-target views are ours and never freed, so in
            // practice this only ever catches the inspector-owned one.
            try
            {
                if (view.targetObject == null) return;
                view.UpdateIfRequiredOrScript();
                prop = view.FindProperty(s_propertyPath);
            }
            catch (NullReferenceException)
            {
                if (ReferenceEquals(view, s_serializedObject)) s_soAlive = false;
                return;
            }

            if (prop == null || !prop.isArray) return;

            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                if (element == null || element.propertyType != SerializedPropertyType.ObjectReference) continue;
                var value = element.objectReferenceValue;
                if (value != null) s_alreadyIn.Add(value);
            }
        }

        /// <summary>
        /// One view per selected target, built ONCE at <see cref="Arm"/> — empty for the ordinary single
        /// selection, which reads through the inspector's own SerializedObject as before.
        /// <para>Built here rather than in <see cref="RefreshAlreadyIn"/> because that runs on the 30Hz pump,
        /// and a fresh <c>SerializedObject</c> per target per tick is a native allocation the hover colour
        /// does not need. The selection cannot change while armed without tearing the list out of the panel,
        /// which disarms.</para>
        /// </summary>
        private static void BuildTargetViews()
        {
            s_targetViews.Clear();
            if (s_serializedObject == null) return;

            UnityEngine.Object[] targets;
            try { targets = s_serializedObject.targetObjects; }
            catch (NullReferenceException) { s_soAlive = false; return; }

            if (targets == null || targets.Length <= 1) return;
            foreach (var t in targets)
                if (t != null) s_targetViews.Add(new SerializedObject(t));
        }

        // Spent flashes are dropped so the dictionary cannot grow across a long session, and so the per-row
        // FlashAmount lookup stays a cheap miss on the common path.
        private static void ExpireFlashes(double now)
        {
            if (s_flashAt.Count == 0) return;
            List<UnityEngine.Object> done = null;
            foreach (var kv in s_flashAt)
            {
                if (now - kv.Value < FlashSeconds && kv.Key != null) continue;
                if (done == null) done = new List<UnityEngine.Object>();
                done.Add(kv.Key);
            }
            if (done == null) return;
            foreach (var k in done) s_flashAt.Remove(k);
        }

        private static void RepaintBothWindows()
        {
            EditorApplication.RepaintProjectWindow();
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
