using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Object drag-and-drop INTO a collection: drop one object — or twenty — anywhere on a List/Table widget and
    /// every compatible one is appended, exactly like Unity's own list drop area.
    ///
    /// <para>Why this exists: a Sasquatch collection draws its own rows, so the ONLY object-drop surface it used to
    /// offer was each row's inner field. That meant a multi-object drag assigned exactly one object (a field holds
    /// one reference), and an EMPTY collection — which has no rows at all — could not be filled by dragging at
    /// all. The stock inspector accepts a whole drag onto the list box; this restores that.</para>
    ///
    /// <para>Division of labour with the rows, decided by HOW MANY objects the drag carries (see
    /// <see cref="CollectionTakesDrop"/>): a SINGLE object dropped on a row's own object field is that FIELD's
    /// business — it replaces that one element, exactly as Unity does. A MULTI-object drag is ALWAYS the
    /// collection's, wherever the pointer happens to sit, because a field holds exactly one reference and would
    /// silently keep the first object and throw the rest away.</para>
    /// </summary>
    internal static class CollectionObjectDrop
    {
        /// <summary>Name of the highlight overlay injected into a collection that accepts object drops.</summary>
        internal const string HighlightName = "sasq-collection-drop-highlight";

        /// <summary>
        /// Stamped on every container that carries a collection drop zone, so a NESTED collection can be seen
        /// by the arbitration walk. Collections really do nest (a list inside an In-Line Editor row renders a
        /// whole second layout, drop zone and all), and the handlers are TRICKLE-DOWN — the OUTER container
        /// therefore sees the event first and, for a multi-object drag, used to claim it unconditionally. The
        /// innermost zone must win: that is the one the pointer is actually over.
        /// </summary>
        internal const string DropHostClass = "sasq-collection-drop-host";

        /// <summary>
        /// The element type of an array/List property when — and only when — it holds object references. Returns
        /// null for value collections (List&lt;int&gt;, a struct list, a string list…), which take no object drop,
        /// and for any path reflection cannot walk (a collection nested under a managed reference), where the
        /// honest answer is "don't offer a drop zone" rather than a guess.
        /// </summary>
        internal static Type ResolveObjectElementType(SerializedProperty arrayProp)
        {
            if (arrayProp == null || !arrayProp.isArray) return null;
            // A string is an "array" of chars to SerializedProperty; it is never a collection here.
            if (arrayProp.propertyType == SerializedPropertyType.String) return null;

            var target = arrayProp.serializedObject != null ? arrayProp.serializedObject.targetObject : null;
            if (target == null) return null;

            BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), arrayProp.propertyPath, out var declared);
            if (declared == null) return null;

            if (!SasquatchReflectionUtility.IsCollectionType(declared, out var elementType)) return null;
            if (elementType == null || !typeof(UnityEngine.Object).IsAssignableFrom(elementType)) return null;

            return elementType;
        }

        /// <summary>
        /// Maps one dragged object onto <paramref name="elementType"/> the way Unity's own object fields do, or
        /// null when it simply doesn't fit. Beyond the direct match this covers the courtesies users expect:
        /// dragging a GameObject (or prefab) onto a Component collection picks the matching component off it,
        /// dragging a Component onto a GameObject collection uses the GameObject it lives on, and dragging a
        /// sprite-mode TEXTURE at a Sprite slot contributes the texture's first sprite.
        /// <para>Shared by the collection drop zone and <see cref="ObjectSlotSurface"/> (the single-reference
        /// surfaces), so "does this drag fit?" has one answer everywhere.</para>
        /// </summary>
        internal static UnityEngine.Object Coerce(UnityEngine.Object dragged, Type elementType)
        {
            if (dragged == null || elementType == null) return null;
            if (elementType.IsInstanceOfType(dragged)) return dragged;

            var go = dragged as GameObject;
            if (go != null && typeof(Component).IsAssignableFrom(elementType))
            {
                var component = go.GetComponent(elementType);
                if (component != null) return component;
            }

            var comp = dragged as Component;
            if (comp != null && comp.gameObject != null && elementType.IsInstanceOfType(comp.gameObject))
                return comp.gameObject;

            // Unity's own object field converts a dragged Texture2D into that texture's sprite when the slot
            // is a Sprite (internally SpriteUtility.TextureToSprite). Users drag the texture row far more often
            // than the sprite sub-asset under it, so a Sprite slot that refuses the parent reads as broken.
            if (elementType == typeof(Sprite) && dragged is Texture2D texture)
                return FirstSpriteOf(texture);

            return null;
        }

        // LoadAllAssetsAtPath walks the whole importer output and DragUpdated fires on every tick of a drag, so
        // the answers are memoized. Two things the original ONE-ENTRY memo got wrong, both in the exact gesture
        // this conversion exists for — dragging a folder of sprite frames onto a List<Sprite>:
        //
        //   * ONE entry cannot hold a MULTI-object drag. Accepted() walks the whole payload each tick, so
        //     eleven frames evicted each other eleven times a tick and the cache never once hit. A Dictionary
        //     costs nothing and actually caches.
        //   * The NEGATIVE answer never expired. Flip a texture from Default to Sprite in the importer and the
        //     cached "no sprites here" survived the reimport, so the drag kept being refused until some other
        //     texture happened to evict it. That is a "worked yesterday, not today" bug with no visible cause.
        //     SpriteMemoInvalidator clears the table on any asset import.
        private static readonly Dictionary<Texture2D, Sprite> _spriteMemo = new Dictionary<Texture2D, Sprite>();

        /// <summary>Drops the sprite memo whenever anything is (re)imported — see the note above.</summary>
        private sealed class SpriteMemoInvalidator : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                string[] movedTo, string[] movedFrom)
            {
                _spriteMemo.Clear();
            }
        }

        /// <summary>The first Sprite sub-asset of <paramref name="texture"/>, or null when it has none.</summary>
        internal static Sprite FirstSpriteOf(Texture2D texture)
        {
            if (texture == null) return null;
            if (_spriteMemo.TryGetValue(texture, out var memo)) return memo;

            Sprite found = null;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(assetPath))
            {
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (sub is Sprite sprite) { found = sprite; break; }
                }
            }

            _spriteMemo[texture] = found; // the negative answer is worth caching too — most drags are refusals
            return found;
        }

        /// <summary>
        /// THE asset-cannot-hold-a-scene-reference rule, in one place so it cannot drift again.
        ///
        /// <para>A serialized reference from an ASSET to a SCENE object cannot round-trip: Unity writes it, the
        /// serializer discards it, and the row reloads as <c>None</c> with no warning and no undo hint. Unity's
        /// own object field and list drop area both refuse that drag outright.</para>
        ///
        /// <para>This lived only inside <see cref="ObjectSlotSurface.FirstAccepted"/> for three weeks while the
        /// collection drop and the eyedropper — the two surfaces that can take MANY objects at once, and so do
        /// the most damage — had no idea about it. Every surface that writes an object reference calls THIS.</para>
        /// </summary>
        /// <param name="owner">The object being inspected. Null = owner unknown, and the rule does not apply.</param>
        /// <param name="candidate">The already-coerced object about to be written.</param>
        internal static bool RefusedAsSceneRefIntoAsset(UnityEngine.Object owner, UnityEngine.Object candidate)
            => owner != null && candidate != null
               && EditorUtility.IsPersistent(owner) && !EditorUtility.IsPersistent(candidate);

        /// <summary>
        /// Everything a drag carries that this collection can take, in drag order. Duplicates within the SAME drag
        /// are dropped (dragging a GameObject and one of its components at a Component list would otherwise add the
        /// same component twice); duplicates against elements ALREADY in the collection are deliberately kept —
        /// a list of references is free to hold the same object twice, and Unity's own list allows it.
        /// </summary>
        /// <param name="dragged">The drag payload (<c>DragAndDrop.objectReferences</c>).</param>
        /// <param name="elementType">The collection's element type.</param>
        /// <param name="owner">
        /// The object being inspected. An ASSET cannot hold a scene reference: Unity's own list drop area and
        /// our own single-slot surface (<see cref="ObjectSlotSurface.FirstAccepted"/>) both REFUSE that drag
        /// rather than write a reference that reloads as null. Without this the collection was the one surface
        /// in the package that accepted it — and because a multi-object drag is always the collection's, it was
        /// the only way left to create the bad data. Null owner = no check (pure-logic callers).
        /// </param>
        internal static List<UnityEngine.Object> Accepted(UnityEngine.Object[] dragged, Type elementType,
            UnityEngine.Object owner = null)
        {
            var accepted = new List<UnityEngine.Object>();
            if (dragged == null || elementType == null) return accepted;

            foreach (var d in dragged)
            {
                var resolved = Coerce(d, elementType);
                if (resolved == null) continue;
                if (RefusedAsSceneRefIntoAsset(owner, resolved)) continue;
                if (accepted.Contains(resolved)) continue;
                accepted.Add(resolved);
            }
            return accepted;
        }

        /// <summary>
        /// Appends <paramref name="objects"/> to the array and applies. Returns how many landed.
        /// InsertArrayElementAtIndex copies the element it inserts at, so every new slot is overwritten
        /// immediately — otherwise a drop onto a non-empty list would clone the last element's other fields.
        /// </summary>
        internal static int Append(SerializedProperty arrayProp, SerializedObject serializedObject,
            IList<UnityEngine.Object> objects)
        {
            if (arrayProp == null || serializedObject == null || objects == null || objects.Count == 0) return 0;

            // MULTI-SELECTION IS A SEPARATE ALGORITHM, AND MUST BE. On a SerializedObject holding several
            // targets, `arraySize` reads the FIRST target's length and `InsertArrayElementAtIndex` then forces
            // EVERY target to that length. Measured: A=[SeedA0], B=[SeedB0, SeedB1], drop one object →
            // A=[SeedA0, Dropped], B=[SeedB0, Dropped]. **SeedB1 is destroyed** — not shifted, overwritten,
            // with no warning. Select a few similar objects, drag a shared material onto the list, and the
            // longest list quietly loses its tail.
            //
            // So each target is appended to through its OWN SerializedObject, which knows its own length. The
            // whole fan-out collapses into ONE undo step, because to the user it was one gesture.
            var targets = serializedObject.targetObjects;
            if (targets != null && targets.Length > 1)
            {
                int undoGroup = Undo.GetCurrentGroup();
                string path = arrayProp.propertyPath;
                int landed = 0;

                foreach (var target in targets)
                {
                    if (target == null) continue;
                    var single = new SerializedObject(target);
                    var prop = single.FindProperty(path);
                    if (prop == null || !prop.isArray) continue;

                    int n = AppendToOne(prop, objects);
                    if (n > 0) single.ApplyModifiedProperties();
                    if (n > landed) landed = n; // the count the USER sees is "how many objects went in"
                }

                Undo.CollapseUndoOperations(undoGroup);
                // The caller still holds the multi-edit view; refresh it or the rebuild draws stale rows.
                serializedObject.Update();
                return landed;
            }

            int added = AppendToOne(arrayProp, objects);
            if (added > 0) serializedObject.ApplyModifiedProperties();
            return added;
        }

        /// <summary>
        /// The single-target append, which never applies — its caller owns that, because the multi-selection
        /// path above runs this once per target and applies each in turn. Split out so that path can work
        /// against each target's OWN array length; see the note there for why sharing one is destructive.
        /// </summary>
        private static int AppendToOne(SerializedProperty arrayProp, IList<UnityEngine.Object> objects)
        {
            int added = 0;
            foreach (var o in objects)
            {
                int at = arrayProp.arraySize;
                arrayProp.InsertArrayElementAtIndex(at);
                var element = arrayProp.GetArrayElementAtIndex(at);
                if (element == null || element.propertyType != SerializedPropertyType.ObjectReference)
                {
                    // The element isn't an object slot after all — undo the insert and stop rather than leave
                    // blank rows behind (defensive: ResolveObjectElementType already proved the element type).
                    arrayProp.DeleteArrayElementAtIndex(at);
                    break;
                }
                element.objectReferenceValue = o;
                added++;
            }
            return added;
        }

        /// <summary>
        /// Removes every element holding one of <paramref name="objects"/> and applies. Returns how many of
        /// them came out.
        ///
        /// <para>The SHRINK twin of <see cref="Append"/>, and it lives next to it because it needs the SAME
        /// multi-selection fan-out for the same reason — the note above is half the hazard, this is the other
        /// half. Walking the shared multi-target property reads `arraySize` and `objectReferenceValue` off the
        /// FIRST target while the null-and-delete writes through to EVERY one, so a strip decided from
        /// target[0]'s contents deletes whatever the other selected objects happened to hold at that index.
        /// A=[X, P], B=[Q, X], alt-strip X → A=[P] (right) and B=[X] — Q destroyed, and the X the user
        /// actually asked for still in there.</para>
        /// </summary>
        internal static int RemoveFromEvery(SerializedProperty arrayProp, SerializedObject serializedObject,
            IList<UnityEngine.Object> objects)
        {
            if (arrayProp == null || serializedObject == null || objects == null || objects.Count == 0) return 0;

            var targets = serializedObject.targetObjects;
            if (targets != null && targets.Length > 1)
            {
                int undoGroup = Undo.GetCurrentGroup();
                string path = arrayProp.propertyPath;
                int most = 0;

                foreach (var target in targets)
                {
                    if (target == null) continue;
                    var single = new SerializedObject(target);
                    var prop = single.FindProperty(path);
                    if (prop == null || !prop.isArray) continue;

                    int n = RemoveFromOne(prop, objects);
                    if (n > 0) single.ApplyModifiedProperties();
                    if (n > most) most = n; // the count the USER sees is "how many objects came out"
                }

                Undo.CollapseUndoOperations(undoGroup);
                // The caller still holds the multi-edit view; refresh it or the rebuild draws stale rows.
                serializedObject.Update();
                return most;
            }

            int removed = RemoveFromOne(arrayProp, objects);
            if (removed > 0) serializedObject.ApplyModifiedProperties();
            return removed;
        }

        /// <summary>
        /// The single-target removal, which never applies — the twin of <see cref="AppendToOne"/>, split out
        /// for the same reason: the multi-selection path above runs it once per target, against that target's
        /// OWN array length and OWN values.
        /// <para>GOTCHA: <c>DeleteArrayElementAtIndex</c> on a NON-NULL object-reference element does not
        /// remove it - it nulls it, and the slot stays. Alt-sweeping a list would have left a column of empty
        /// rows behind instead of shrinking it. Nulling first makes the delete an actual delete.</para>
        /// <para>ALL occurrences go, not just the first: duplicates are allowed in these lists, and "take this
        /// out" plainly means the object should not be in there when the gesture ends.</para>
        /// </summary>
        private static int RemoveFromOne(SerializedProperty arrayProp, IList<UnityEngine.Object> objects)
        {
            int removed = 0;
            for (int i = arrayProp.arraySize - 1; i >= 0; i--)   // backwards: indices below i stay valid
            {
                var element = arrayProp.GetArrayElementAtIndex(i);
                if (element == null || element.propertyType != SerializedPropertyType.ObjectReference) continue;
                var value = element.objectReferenceValue;
                if (value == null || !objects.Contains(value)) continue;

                element.objectReferenceValue = null;             // see the gotcha above
                arrayProp.DeleteArrayElementAtIndex(i);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Wires object-drop onto a live collection container: hover highlight, drop-mode arbitration, and the
        /// append itself. A no-op for value collections (no object element type) — those simply never light up.
        /// </summary>
        /// <param name="container">The collection's outer box; the whole widget becomes the drop surface.</param>
        /// <param name="arrayProp">The collection property (used for its path — see the re-find below).</param>
        /// <param name="serializedObject">The object being inspected.</param>
        /// <param name="onChanged">Row-rebuild callback, invoked only when something was actually added.</param>
        internal static void Attach(VisualElement container, SerializedProperty arrayProp,
            SerializedObject serializedObject, Action onChanged)
        {
            if (container == null || arrayProp == null || serializedObject == null) return;

            var elementType = ResolveObjectElementType(arrayProp);
            if (elementType == null) return;

            // The captured SerializedProperty can outlive the SerializedObject state it was made from (a row
            // rebuild, an Undo, an external array-size change), so the drop re-finds it by PATH at drop time —
            // the same reason the collection's other structural ops re-read rather than cache.
            string path = arrayProp.propertyPath;

            // Seen by CollectionTakesDrop's inner-host walk, so a NESTED collection out-ranks this one.
            container.AddToClassList(DropHostClass);

            // Disposal-guarded: the SerializedObject can be torn down while a drag is still in flight (the same
            // reason the drop re-finds the property by path below). Mirrors ObjectSlotSurface.Owner().
            Func<UnityEngine.Object> owner = () =>
            {
                try { return serializedObject.targetObject; }
                catch (Exception) { return null; }
            };

            var highlight = BuildHighlight();
            container.Add(highlight);

            Action hide = () => { if (highlight.style.display.value != DisplayStyle.None) highlight.style.display = DisplayStyle.None; };

            // TRICKLE-DOWN, not bubble. A multi-object drag has to out-rank whatever row field the pointer happens
            // to be over, and claiming the event on the way DOWN is the only way to be certain the field never
            // handles it as well (stopping it here means the field's own handlers never run at all — we don't have
            // to reason about what they do). A single-object drag is deliberately left alone here and reaches the
            // field exactly as it did before.
            container.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var accepted = Accepted(DragAndDrop.objectReferences, elementType, owner());
                bool mine = accepted.Count > 0 &&
                            CollectionTakesDrop(evt.target as VisualElement, container, accepted.Count);

                // Guarded write: DragUpdated fires every tick of the drag, and re-setting an unchanged display
                // would dirty the style on each one. The ring also has to go DARK while a row's own field is the
                // one taking the drop — promising an append there would be a lie.
                var want = mine ? DisplayStyle.Flex : DisplayStyle.None;
                if (highlight.style.display.value != want) highlight.style.display = want;
                if (!mine) return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                highlight.BringToFront(); // absolute + out of flow, so this reorders paint order only
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            container.RegisterCallback<DragPerformEvent>(evt =>
            {
                hide();

                var accepted = Accepted(DragAndDrop.objectReferences, elementType, owner());
                if (accepted.Count == 0) return;
                if (!CollectionTakesDrop(evt.target as VisualElement, container, accepted.Count)) return;

                DragAndDrop.AcceptDrag();
                evt.StopPropagation();

                var prop = serializedObject.FindProperty(path);
                if (prop == null || !prop.isArray) return;

                // Where the appended run STARTS, read before the append — the drop lands a whole RUN at the
                // end of the array, and that range is what springs in when onChanged rebuilds the rows. Armed
                // HERE rather than inside Append, because the eyedropper calls Append too and already pops its
                // own landed rows (CollectionEyedropper.PopNewRows); arming there would play two entrances on
                // one row. See CollectionRowEntrance.
                int firstNewIndex = prop.arraySize;
                int landed = Append(prop, serializedObject, accepted);
                if (landed > 0)
                {
                    CollectionRowEntrance.Arm(prop, firstNewIndex, landed);
                    onChanged?.Invoke();
                }
            }, TrickleDown.TrickleDown);

            // Both endings matter: DragLeave fires when the pointer leaves the box mid-drag, DragExited when the
            // drag ends elsewhere entirely (including a cancel) — without the second the highlight can stick.
            container.RegisterCallback<DragLeaveEvent>(_ => hide());
            container.RegisterCallback<DragExitedEvent>(_ => hide());
        }

        // The accept ring: an absolutely-positioned, click-through overlay so lighting it up can never nudge a
        // single row (a border or padding change on the container itself would re-flow the whole collection).
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

        /// <summary>
        /// WHO takes this drop — the collection (append every object) or the row object field the pointer is over
        /// (replace that one element)? The deciding fact is that a field holds exactly ONE reference:
        /// <list type="bullet">
        /// <item>ONE accepted object over a row's field ⇒ the FIELD takes it. That is Unity's behaviour and the
        /// only way to retarget a single row by dragging.</item>
        /// <item>TWO OR MORE ⇒ the COLLECTION takes it, wherever the pointer is. Handing a multi-object drag to a
        /// field means it keeps the first object and silently discards the rest — the user aimed a drag of five
        /// prefabs at a list and got one, with no hint that four were dropped on the floor.</item>
        /// <item>Anywhere that is NOT a row field ⇒ the collection, at any count (the header, the background,
        /// the gaps between rows, and an empty collection, which has no rows to aim at).</item>
        /// <item>A NESTED collection between the pointer and this box ⇒ that one, at any count. The handlers
        /// are trickle-down, so this OUTER box is asked first; without the check it swallowed every
        /// multi-object drag aimed at an inner list and appended to the wrong collection.</item>
        /// </list>
        /// </summary>
        /// <param name="target">The drag event's target (its deepest element).</param>
        /// <param name="container">The collection's outer box.</param>
        /// <param name="acceptedCount">How many dragged objects this collection can actually take.</param>
        internal static bool CollectionTakesDrop(VisualElement target, VisualElement container, int acceptedCount)
        {
            // The innermost collection always wins — before the count rule, which is otherwise unconditional.
            if (CrossesAnInnerCollection(target, container)) return false;
            return acceptedCount > 1 || !HandledByAField(target, container);
        }

        // True when ANOTHER collection's drop zone sits between the event target and this box — i.e. this one is
        // an ancestor of the collection the pointer is really over.
        private static bool CrossesAnInnerCollection(VisualElement target, VisualElement container)
        {
            for (var ve = target; ve != null && ve != container; ve = ve.parent)
                if (ve.ClassListContains(DropHostClass)) return true;
            return false;
        }

        // True when the drag is sitting on a SINGLE-REFERENCE surface between the event target and the
        // collection box. Walking the chain ourselves keeps the split deterministic instead of depending on
        // whether the field stopped the event's propagation.
        //
        // `ObjectField` is not the whole answer: a widget that draws its OWN slot registers through
        // ObjectSlotSurface instead, and in the Asset Preview / Particle Preview EXPANDABLE modes the thumbnail
        // lives in the foldout CONTENT while the ObjectField is a sibling branch of the header — so the field is
        // never on the walk and the collection used to claim a single-object drop that plainly belonged to that
        // one row. The marker class covers every such surface uniformly.
        private static bool HandledByAField(VisualElement target, VisualElement container)
        {
            for (var ve = target; ve != null && ve != container; ve = ve.parent)
                if (ve is ObjectField || ve.ClassListContains(ObjectSlotSurface.SlotHostClass)) return true;
            return false;
        }
    }
}
