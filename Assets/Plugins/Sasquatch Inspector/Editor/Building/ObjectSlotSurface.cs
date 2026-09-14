using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Makes a widget's OWN box behave like an object slot — the affordances Unity attaches to
    /// <c>ObjectFieldDisplay</c> and nowhere else, restored for a widget that draws its own box instead.
    ///
    /// <para>Why this exists: for an <see cref="UnityEditor.UIElements.ObjectField"/>, only
    /// <c>ObjectFieldDisplay</c> (the element carrying <c>unity-object-field-display</c>) handles drags, and only
    /// it is focusable and clears on Delete. Every widget that draws its own box beside — or instead of — that
    /// display therefore loses BOTH. The Asset Preview is the worst case: <c>Thumbnail Only</c> hides the display
    /// outright, so the one thing the user can see (the thumbnail) is the one thing that neither takes a drop nor
    /// answers Delete. Reported as "I can NOT drag and drop a sprite onto the thumbnail", then "hitting Delete to
    /// UNassign — a common and well known shortcut — does NOT work with our asset preview thumbnail".</para>
    ///
    /// <para>Two independent affordances, wired separately because a surface may want one without the other:
    /// <see cref="AttachDropTarget"/> (assign by dragging) and <see cref="AttachClearShortcut"/> (focus + Delete
    /// to unassign). Neither is attached over a LIVE object-field display: where Unity's own handling is present
    /// and visible it stays in charge — it knows every assignment rule the editor has. These fill the gaps around
    /// it, and answer "does this drag fit?" through the same <see cref="CollectionObjectDrop.Coerce"/> the
    /// collection drop zone uses.</para>
    /// </summary>
    internal static class ObjectSlotSurface
    {
        /// <summary>Name of the accept-ring overlay injected into a surface that takes object drops.</summary>
        internal const string HighlightName = "sasq-object-drop-highlight";

        /// <summary>Name of the focus-ring overlay injected into a surface that answers Delete.</summary>
        internal const string FocusRingName = "sasq-object-slot-focus";

        /// <summary>
        /// Stamped on every surface that takes a SINGLE object reference, so an enclosing collection's drop
        /// arbitration can see it (<see cref="CollectionObjectDrop.CollectionTakesDrop"/> walks for it by name).
        /// A collection's zone is an ANCESTOR of these and its handlers are trickle-down, so without a marker it
        /// claimed a one-object drop aimed squarely at a row's own slot and appended instead of retargeting —
        /// wherever the slot is not physically inside its <c>ObjectField</c> (the Asset Preview and Particle
        /// Preview EXPANDABLE modes put the thumbnail in the foldout content, a sibling branch of the header).
        /// </summary>
        internal const string SlotHostClass = "sasq-object-drop-host";

        /// <summary>
        /// The first dragged object this slot can take, or null when the drag carries nothing that fits. A
        /// field holds exactly one reference, so — like Unity — the rest of a multi-object drag is ignored
        /// rather than silently discarded somewhere the user can't see.
        /// </summary>
        /// <param name="dragged">The drag payload (<c>DragAndDrop.objectReferences</c>).</param>
        /// <param name="fieldType">The slot's accepted type (an ObjectField's <c>objectType</c>).</param>
        /// <param name="owner">The object being inspected — an ASSET cannot hold a scene reference.</param>
        internal static UnityEngine.Object FirstAccepted(UnityEngine.Object[] dragged, Type fieldType,
            UnityEngine.Object owner)
        {
            if (dragged == null || fieldType == null) return null;

            foreach (var d in dragged)
            {
                var resolved = CollectionObjectDrop.Coerce(d, fieldType);
                if (resolved == null) continue;
                // A serialized reference from an asset to a scene object cannot round-trip. The rule itself
                // lives in CollectionObjectDrop so every drop surface shares one implementation of it.
                if (CollectionObjectDrop.RefusedAsSceneRefIntoAsset(owner, resolved)) continue;
                return resolved;
            }
            return null;
        }

        /// <summary>
        /// Wires object-drop onto <paramref name="surface"/>: hover ring, claim, and the assignment itself.
        /// </summary>
        /// <param name="surface">The element that becomes the drop surface (the ring is parented to it).</param>
        /// <param name="prop">The object-reference property to assign (used for its path — see the re-find).</param>
        /// <param name="fieldType">The slot's accepted type.</param>
        /// <param name="trickleDown">
        /// TrickleDown for the INNERMOST surface of a widget (it claims the drop before any outer surface sees
        /// it); NoTrickleDown for an outer fallback surface, which then only ever runs for drops the inner one
        /// didn't claim.
        /// </param>
        /// <param name="onAssigned">Invoked only when a reference actually landed.</param>
        internal static void AttachDropTarget(VisualElement surface, SerializedProperty prop, Type fieldType,
            TrickleDown trickleDown = TrickleDown.TrickleDown, Action onAssigned = null)
        {
            if (surface == null || prop == null || fieldType == null) return;
            if (prop.propertyType != SerializedPropertyType.ObjectReference) return;

            // Seen by an enclosing collection's drop arbitration — see SlotHostClass.
            surface.AddToClassList(SlotHostClass);

            var serializedObject = prop.serializedObject;
            if (serializedObject == null) return;
            // The captured SerializedProperty can outlive the SerializedObject state it was made from (a
            // rebuild, an Undo, an external change), so the drop re-finds it by PATH at drop time — the same
            // reason CollectionObjectDrop re-reads rather than caches.
            string path = prop.propertyPath;

            var highlight = BuildHighlight();
            surface.Add(highlight);

            void Hide()
            {
                if (highlight.style.display.value != DisplayStyle.None) highlight.style.display = DisplayStyle.None;
            }

            // Disposal-guarded: the SerializedObject can be torn down while a drag is in flight.
            UnityEngine.Object Owner()
            {
                try { return serializedObject.targetObject; }
                catch (Exception) { return null; }
            }

            surface.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var accepted = FirstAccepted(DragAndDrop.objectReferences, fieldType, Owner());

                // Guarded write: DragUpdated fires every tick of the drag, and re-setting an unchanged display
                // would dirty the style on each one.
                var want = accepted != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (highlight.style.display.value != want) highlight.style.display = want;
                if (accepted == null) return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                highlight.BringToFront(); // absolute + out of flow, so this reorders paint order only
                evt.StopPropagation();
            }, trickleDown);

            surface.RegisterCallback<DragPerformEvent>(evt =>
            {
                Hide();

                var accepted = FirstAccepted(DragAndDrop.objectReferences, fieldType, Owner());
                if (accepted == null) return;

                DragAndDrop.AcceptDrag();
                evt.StopPropagation();

                var live = serializedObject.FindProperty(path);
                if (live == null || live.propertyType != SerializedPropertyType.ObjectReference) return;
                live.objectReferenceValue = accepted;
                serializedObject.ApplyModifiedProperties(); // registers the undo step for us
                onAssigned?.Invoke();
            }, trickleDown);

            // Both endings matter: DragLeave fires when the pointer leaves the surface mid-drag, DragExited
            // when the drag ends elsewhere entirely (including a cancel) — without the second the ring sticks.
            surface.RegisterCallback<DragLeaveEvent>(_ => Hide());
            surface.RegisterCallback<DragExitedEvent>(_ => Hide());
        }

        /// <summary>
        /// Makes the slot selectable and clears the reference on <c>Delete</c> / <c>Backspace</c> — the stock
        /// un-assign shortcut, which Unity implements on <c>ObjectFieldDisplay</c> (focusable, clears on those
        /// two keys) and therefore loses along with it.
        /// </summary>
        /// <param name="surface">What the user CLICKS and what shows the focus ring — the widget's own box.</param>
        /// <param name="focusHost">
        /// What actually HOLDS focus, normally the widget's <see cref="UnityEditor.UIElements.ObjectField"/>.
        /// These are two different elements on purpose: a <c>BaseField</c> is a COMPOSITE FOCUS ROOT, so nothing
        /// parented inside it can ever be the focused element — UI Toolkit resolves focus up to the field, by
        /// design, so a field behaves as one focus unit. Measured: pressing the thumbnail left
        /// <c>focusedElement == ObjectField</c> no matter how the surface asked for focus, which is why the
        /// first attempt at this (a focusable thumbnail with the key handler on it) could never have worked.
        /// Pass null for a surface that has no field around it.
        /// </param>
        /// <param name="prop">The object-reference property to clear.</param>
        /// <param name="onCleared">Invoked only when a reference was actually cleared.</param>
        internal static void AttachClearShortcut(VisualElement surface, VisualElement focusHost,
            SerializedProperty prop, Action onCleared = null)
        {
            if (surface == null || prop == null) return;
            if (prop.propertyType != SerializedPropertyType.ObjectReference) return;

            var serializedObject = prop.serializedObject;
            if (serializedObject == null) return;
            string path = prop.propertyPath; // re-found at key time, like the drop

            var host = focusHost ?? surface;
            host.focusable = true;
            surface.focusable = true;

            // Clicking the box must SELECT the slot, and getting there took measuring a live Inspector rather
            // than reasoning, three times over:
            //   1. Focus-on-press is a DEFAULT action, and the interactive preview's IMGUIContainer calls
            //      Event.Use() on MouseDown to start its orbit drag, which makes IMGUIContainer PreventDefault().
            //   2. Worse, the IMGUI pass then SYNCS UI Toolkit focus to its own IMGUI keyboard control — which is
            //      nothing — so focus is granted and immediately DROPPED inside the same press. Recorded through
            //      the real window pipeline: FocusIn fired, and `focusedElement` was <NONE> a moment later.
            //   3. With nothing focused, the panel never receives the key at all: `SendEvent(KeyDown Delete)`
            //      returned used=false and no KeyDownEvent was dispatched anywhere. THAT is why Delete did
            //      nothing — not the handler, which clears correctly the instant a key reaches it.
            // So: ask on the way down, then RECLAIM on the next tick, which is after the IMGUI pass has blurred.
            // Only when focus ended up nowhere — never steal focus the user deliberately moved on.
            surface.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return; // left-click selects, like a stock object field
                surface.Focus();
                // ONE-SHOT reclaim on the next scheduler tick — see the note above for why nothing earlier works.
                surface.schedule.Execute(() =>
                {
                    if (surface.panel == null) return;
                    var fc = surface.panel.focusController;
                    if (fc != null && fc.focusedElement == null) surface.Focus();
                }).ExecuteLater(0);
            }, TrickleDown.TrickleDown);

            // THE RECLAIM, and it has to be on the SCHEDULER. Recorded sequence for one press on the thumbnail:
            //     box PointerDown  ->  FocusIn(box)  ->  FocusOut(box)     ... all inside the MouseDown
            //     MouseUp: no PointerUp reaches the box at all
            // The IMGUI viewport consumes the press (Event.Use() + hotControl) and, in doing so, syncs UI Toolkit
            // focus to its own empty IMGUI keyboard control — blurring us in the same event. And because IMGUI
            // then owns the hot control, the pointer-UP is never dispatched to this subtree, so a pointer-up
            // reclaim can never run. A scheduled tick is the first moment after the IMGUI pass that we get.
            // Guarded on "focus ended up nowhere", so focus the user deliberately moved is never stolen.

            var ring = BuildFocusRing();
            surface.Add(ring);
            // The ring lives on the SURFACE but tracks the HOST's focus — that is the pair the user perceives as
            // one control. FocusIn/FocusOut bubble, so a focusable child of the host lights it just the same.
            host.RegisterCallback<FocusInEvent>(_ => ring.style.display = DisplayStyle.Flex);
            host.RegisterCallback<FocusOutEvent>(_ => ring.style.display = DisplayStyle.None);

            // TRICKLE-DOWN on the HOST: the key event targets whatever holds focus, and anything below the host
            // (an IMGUIContainer, which runs a whole IMGUI pass that can consume the key) would otherwise get
            // first refusal. Claiming on the way down is the only certain route.
            host.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace) return;

                // Consumed either way — stock does the same, and a Delete aimed at a focused slot must never
                // fall through to whatever the surrounding window does with Delete.
                evt.StopPropagation();

                var live = serializedObject.FindProperty(path);
                if (live == null || live.propertyType != SerializedPropertyType.ObjectReference) return;
                if (live.objectReferenceValue == null) return; // already empty

                live.objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties(); // registers the undo step for us
                onCleared?.Invoke();
            }, TrickleDown.TrickleDown);
        }

        // The accept ring: an absolutely-positioned, click-through overlay so lighting it up can never nudge
        // the widget's layout (a border or padding change on the surface itself would re-flow the row).
        private static VisualElement BuildHighlight()
        {
            var highlight = new VisualElement { name = HighlightName };
            highlight.pickingMode = PickingMode.Ignore;
            highlight.style.position = Position.Absolute;
            highlight.style.left = 0;
            highlight.style.top = 0;
            highlight.style.right = 0;
            highlight.style.bottom = 0;
            highlight.style.display = DisplayStyle.None;

            highlight.style.backgroundColor = WidgetPalette.AccentSoft;
            highlight.style.borderTopWidth = 1;
            highlight.style.borderBottomWidth = 1;
            highlight.style.borderLeftWidth = 1;
            highlight.style.borderRightWidth = 1;
            var accent = WidgetPalette.Accent;
            highlight.style.borderTopColor = accent;
            highlight.style.borderBottomColor = accent;
            highlight.style.borderLeftColor = accent;
            highlight.style.borderRightColor = accent;
            highlight.style.borderTopLeftRadius = 4;
            highlight.style.borderTopRightRadius = 4;
            highlight.style.borderBottomLeftRadius = 4;
            highlight.style.borderBottomRightRadius = 4;
            return highlight;
        }

        // The focus ring: quieter than the drop ring (no wash, 1px) because it is a resting state, not an
        // invitation — it says "Delete lands here", nothing more.
        private static VisualElement BuildFocusRing()
        {
            var ring = new VisualElement { name = FocusRingName };
            ring.pickingMode = PickingMode.Ignore;
            ring.style.position = Position.Absolute;
            ring.style.left = 0;
            ring.style.top = 0;
            ring.style.right = 0;
            ring.style.bottom = 0;
            ring.style.display = DisplayStyle.None;

            var accent = WidgetPalette.Accent;
            ring.style.borderTopWidth = 1;
            ring.style.borderBottomWidth = 1;
            ring.style.borderLeftWidth = 1;
            ring.style.borderRightWidth = 1;
            ring.style.borderTopColor = accent;
            ring.style.borderBottomColor = accent;
            ring.style.borderLeftColor = accent;
            ring.style.borderRightColor = accent;
            ring.style.borderTopLeftRadius = 4;
            ring.style.borderTopRightRadius = 4;
            ring.style.borderBottomLeftRadius = 4;
            ring.style.borderBottomRightRadius = 4;
            return ring;
        }
    }
}
