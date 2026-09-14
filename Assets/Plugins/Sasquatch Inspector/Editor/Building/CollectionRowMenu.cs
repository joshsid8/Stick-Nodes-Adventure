using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    // Row actions for a LIVE collection row (List & Table): Duplicate / Insert Above / Insert Below /
    // Delete. These all change the array SIZE, so they go through onStructuralChange (a full rebuild)
    // — exactly like the +/− buttons. (Only reorder, which keeps the count, refreshes in place; see
    // CollectionRowReorder.) The entries surface in the unified right-click menu via
    // QuickMenuContributors — never a ContextualMenuManipulator, which the capture-phase takeover in
    // QuickMenuController would suppress. Editor-only; each edit is recorded for undo via
    // ApplyModifiedProperties.
    /// <summary>
    /// Right-click row actions for a live collection row (Duplicate / Insert Above-Below / Delete). All change
    /// the array size, so they trigger a structural rebuild; surfaced through <see cref="QuickMenuContributors"/>.
    /// </summary>
    public static class CollectionRowMenu
    {
        /// <summary>
        /// Stamped on a row that carries its own <c>ContextualMenuManipulator</c> because no Sasquatch
        /// layout — and therefore no <see cref="QuickMenuController"/> — is above it. See the
        /// <c>standalone</c> parameter of <see cref="Attach"/>.
        /// </summary>
        internal const string StandaloneMenuClass = "sasq-row-menu-standalone";


        /// <summary>
        /// Registers the Duplicate/Insert/Delete menu actions on <paramref name="row"/> for array element
        /// <paramref name="index"/>.
        /// </summary>
        /// <param name="onRevealInserted">
        /// Optional. Invoked by Insert Above / Insert Below only, between the edit and the rebuild, to clear
        /// view state that would hide the new row. Insert creates a BLANK element, and a blank matches no
        /// search query and carries no sort key — so behind a live filter the insert rendered nothing at all
        /// and read as a dead menu item. Deliberately NOT wired to Duplicate: a clone keeps the original's
        /// values, so it still matches the same query and sorts alongside its twin, and silently dropping the
        /// user's filter for it would be the surprising move. Also deliberately does NOT move the page — the
        /// row that was right-clicked is on screen, so the insertion point already is.
        /// </param>
        /// <param name="standalone">
        /// Also hang a <see cref="ContextualMenuManipulator"/> on the row, for a renderer that can appear
        /// with NO Sasquatch layout above it — today only <see cref="SerializedDictionaryDrawer"/>, which is
        /// a plain <c>CustomPropertyDrawer</c> and therefore draws in a stock inspector, in an exported
        /// editor, and under somebody else's <c>[CustomEditor]</c>. In all three the contributor registry is
        /// dead: <see cref="QuickMenuContributors.Collect"/> has exactly one caller, and it only runs under
        /// <see cref="QuickMenuController"/>, which only a live layout installs. The row still drew its grip
        /// and its ✕, so Duplicate / Insert Above / Insert Below / Delete N Rows were the four commands with
        /// no other affordance anywhere — silently missing, on the path a buyer sees FIRST.
        /// <para>
        /// Opt-in rather than unconditional for one reason: the two <see cref="LayoutTreeBuilder"/> call
        /// sites are not design-mode gated, and a manipulator there would inject these entries into the
        /// Layout Designer canvas's own right-click menu. Inside a LIVE layout it costs nothing either way —
        /// QuickMenuController stops <c>ContextualMenuPopulateEvent</c> in the capture phase, so the
        /// manipulator never fires and the contributor path serves the menu exactly as before.
        /// </para>
        /// </param>
        /// <param name="onNewElement">
        /// Optional. Handed the freshly-cloned element by <b>Duplicate</b> only, before the apply, so a
        /// caller whose rows are not interchangeable can fix up the copy. Today's only user is the
        /// dictionary table: <c>InsertArrayElementAtIndex</c> clones the element verbatim, and a cloned KEY
        /// is a key the runtime is guaranteed to discard — so Duplicate always produced a row that was red
        /// before the user had typed anything. Duplicating the VALUE and asking for a fresh key is the only
        /// reading of the command that yields a usable row, and it is the same decision the table's Add
        /// button already makes (<c>SerializedDictionaryDrawer.TableSpec.ClearKeyOnNewRow</c>).
        /// <para>
        /// NOT wired to Insert, which needs nothing: <see cref="ResetToDefault"/> already blanks the whole
        /// element, key included.
        /// </para>
        /// </param>
        public static void Attach(VisualElement row, SerializedProperty arrayProp, SerializedObject serializedObject, int index, Action onStructuralChange, Action onRevealInserted = null, bool standalone = false, Action<SerializedProperty> onNewElement = null)
        {
            if (row == null || arrayProp == null || serializedObject == null) return;

            if (standalone)
            {
                // Marker for tests and for eyeballing a live tree. UI Toolkit exposes no way to enumerate an
                // element's manipulators, and the alternative — synthesising a ContextualMenuPopulateEvent —
                // needs constructors that are not public API, which §2.5.g rules out of shipped code.
                row.AddToClassList(StandaloneMenuClass);
                row.AddManipulator(new ContextualMenuManipulator(evt =>
                    PopulateRowActions(
                        evt.target as VisualElement, row, arrayProp, serializedObject, index,
                        onStructuralChange, onRevealInserted, onNewElement,
                        (text, action) => evt.menu.AppendAction(text, _ => action()),
                        () => evt.menu.AppendSeparator())));
            }

            QuickMenuContributors.Register(row, (request, menu) =>
                PopulateRowActions(
                    request?.DeepTarget, row, arrayProp, serializedObject, index,
                    onStructuralChange, onRevealInserted, onNewElement,
                    (text, action) => menu.Add(text, action), menu.AddSeparator));
        }

        /// <summary>
        /// The row's four actions, written once and fed to either menu surface through
        /// <paramref name="add"/>/<paramref name="addSeparator"/>. Two callers with two different menu types
        /// (<see cref="QuickMenuModel"/> live, <c>DropdownMenu</c> standalone) must not grow two copies of
        /// this logic — the selection rules and the innermost-wins bail below are exactly the kind of thing
        /// that drifts apart when duplicated.
        /// </summary>
        private static void PopulateRowActions(
            VisualElement deepTarget, VisualElement row,
            SerializedProperty arrayProp, SerializedObject serializedObject, int index,
            Action onStructuralChange, Action onRevealInserted, Action<SerializedProperty> onNewElement,
            Action<string, Action> add, Action addSeparator)
        {
                // THE INNERMOST ROW WINS. QuickMenuContributors.Collect walks EVERY ancestor of the
                // right-clicked element, so a collection nested in another collection's row — a tags list
                // inside an inventory row — reached this registration TWICE: once for the tags row under
                // the cursor, once for the inventory row containing it. The menu showed Duplicate / Insert
                // Above / Insert Below / Delete twice with nothing saying which list each set belonged to,
                // and the second set operated on the ENCLOSING element, so its Delete removed the whole
                // inventory item.
                //
                // Same rule, and deliberately the same walk shape, as CollectionObjectDrop.CollectionTakesDrop:
                // the innermost collection takes the drop. Qualifying the labels instead would have left that
                // Delete one click away, and would not have touched the write below.
                //
                // FIRST, before anything else in this callback, because CollectionRowSelection.Peek +
                // SelectOnly is a WRITE: without the early return, merely OPENING a menu on an inner row
                // collapsed the OUTER list's multi-row highlight onto a row nobody had clicked.
                //
                // The standalone manipulator needs this just as much as the contributor walk does, by a
                // different mechanism: ContextualMenuPopulateEvent BUBBLES, so a dictionary whose VALUES are
                // themselves dictionaries would have the inner row's callback and every enclosing row's
                // append to the same DropdownMenu. Same symptom, same bail — see
                // nested-row-menus-need-an-innermost-wins-bail, where the export kit's twin is recorded.
                if (CrossesAnInnerRow(deepTarget, row)) return;

                // THE MENU MUST NOT LIE ABOUT ITS SCOPE. Multi-row selection lights several rows at once,
                // and this menu used to act on the right-clicked row alone regardless — so right-clicking
                // one of three lit rows and choosing Delete removed ONE, then the structural rebuild wiped
                // the highlight, leaving no trace of which row it had taken. Two rules, both the ones every
                // file browser uses:
                //   * right-clicking a row OUTSIDE the selection collapses the selection onto it first, so
                //     no row is lit that the menu is not about to touch;
                //   * right-clicking a row INSIDE a multi-selection acts on the whole set, and says so.
                var selection = CollectionRowSelection.Peek(row.parent);
                var picked = (selection != null && selection.Count > 1 && selection.Contains(index))
                    ? selection.Sorted()
                    : null;
                if (picked == null && selection != null && selection.Count > 0 && !selection.Contains(index))
                {
                    selection.SelectOnly(index);
                    selection.Paint(row.parent);
                }

                add("Duplicate", () => Duplicate(arrayProp, serializedObject, index, onStructuralChange, onNewElement));
                add("Insert Above", () => Insert(arrayProp, serializedObject, index, onStructuralChange, onRevealInserted));
                add("Insert Below", () => Insert(arrayProp, serializedObject, index + 1, onStructuralChange, onRevealInserted));
                addSeparator();
                if (picked != null)
                {
                    add($"Delete {picked.Count} Rows",
                        () => DeleteMany(arrayProp, serializedObject, picked, onStructuralChange));
                }
                else
                {
                    add("Delete", () => Delete(arrayProp, serializedObject, index, onStructuralChange));
                }
        }

        /// <summary>
        /// True when ANOTHER collection row sits between the element under the cursor and
        /// <paramref name="row"/> — i.e. this row is an ANCESTOR of the row actually being right-clicked,
        /// and the menu it would contribute belongs to somebody else. The twin of
        /// <c>CollectionObjectDrop.CrossesAnInnerCollection</c>, which answers the same question for a drag.
        ///
        /// <para>Covers all three rows this menu is attached to: List rows and Table rows carry
        /// <c>ListRowClass</c> / <c>TableRowClass</c>, and the serialized-dictionary drawer reuses the table
        /// row marker for its entry rows.</para>
        ///
        /// <para>A null <paramref name="deepTarget"/> means the caller could not say what was under the
        /// cursor. The historic behaviour — contribute — is the right answer there: an extra group in the
        /// menu is a smaller failure than a row with no menu at all.</para>
        /// </summary>
        private static bool CrossesAnInnerRow(VisualElement deepTarget, VisualElement row)
        {
            if (deepTarget == null || row == null) return false;
            for (var ve = deepTarget; ve != null && ve != row; ve = ve.parent)
                if (ve.ClassListContains(LayoutTreeBuilder.ListRowClass) ||
                    ve.ClassListContains(LayoutTreeBuilder.TableRowClass)) return true;
            return false;
        }

        // Clone the element in place — Unity's InsertArrayElementAtIndex copies the element currently at
        // `index` into the new slot, so the values carry over (this is "Duplicate", not "Insert").
        /// <summary>Clones the element at <paramref name="index"/> (its values carry over).</summary>
        public static void Duplicate(SerializedProperty arrayProp, SerializedObject so, int index, Action onChange = null, Action<SerializedProperty> onNewElement = null)
        {
            if (index < 0 || index >= arrayProp.arraySize) return;
            arrayProp.InsertArrayElementAtIndex(index);
            // The CLONE is index + 1, for the same reason the entrance plays there (see below): position
            // `index` shows exactly what it showed before, and the row BELOW is the one that just became a
            // copy. A dictionary hands in a callback that blanks that row's key — see Attach's onNewElement.
            onNewElement?.Invoke(arrayProp.GetArrayElementAtIndex(index + 1));
            // index + 1, not index. Insert puts the copy AT index and shifts the original down, so the two
            // slots hold identical data and either could be called "the new one" — but only ONE of them looks
            // new: position `index` shows exactly what it showed before, and it is the row BELOW that has
            // changed into a duplicate. Springing `index` would animate the row the user right-clicked while
            // an identical row quietly appeared underneath it. Armed before onChange, which is the rebuild
            // that draws it — see CollectionRowEntrance.
            CollectionRowEntrance.Arm(arrayProp, index + 1);
            so.ApplyModifiedProperties();
            onChange?.Invoke();
        }

        // Insert a FRESH row at `insertIndex`. The inserted element starts as a copy of a neighbor, so we
        // clear it to defaults — that's what distinguishes Insert (blank) from Duplicate (keeps values).
        /// <summary>Inserts a fresh default element at <paramref name="insertIndex"/>.</summary>
        public static void Insert(SerializedProperty arrayProp, SerializedObject so, int insertIndex, Action onChange = null, Action onReveal = null)
        {
            insertIndex = Mathf.Clamp(insertIndex, 0, arrayProp.arraySize);
            arrayProp.InsertArrayElementAtIndex(insertIndex);
            ResetToDefault(arrayProp.GetArrayElementAtIndex(insertIndex));
            // Insert Above passes the row's own index, Insert Below passes index + 1 — either way the fresh
            // element ends up AT insertIndex, so that is the row the entrance plays on.
            CollectionRowEntrance.Arm(arrayProp, insertIndex);
            so.ApplyModifiedProperties();
            // Between the edit and the rebuild: the reveal drops the filter/sort that a blank element cannot
            // satisfy, then the rebuild draws it. See Attach's onRevealInserted.
            onReveal?.Invoke();
            onChange?.Invoke();
        }

        /// <summary>
        /// Removes every element in <paramref name="indices"/> as ONE undo step.
        ///
        /// <para>Walked DESCENDING, which is the whole trick: deleting index i shifts every element after
        /// it down by one, so an ascending walk would delete the wrong rows from the second one onward.
        /// Going high-to-low leaves the not-yet-deleted indices untouched.</para>
        /// </summary>
        public static void DeleteMany(SerializedProperty arrayProp, SerializedObject so, System.Collections.Generic.IReadOnlyList<int> indices, Action onChange = null)
        {
            if (arrayProp == null || so == null || indices == null || indices.Count == 0) return;

            var ordered = new System.Collections.Generic.List<int>(indices);
            ordered.Sort();
            bool any = false;
            for (int k = ordered.Count - 1; k >= 0; k--)
            {
                int i = ordered[k];
                if (i < 0 || i >= arrayProp.arraySize) continue;
                int oldSize = arrayProp.arraySize;
                arrayProp.DeleteArrayElementAtIndex(i);
                // Object-reference arrays need a second delete — see Delete.
                if (arrayProp.arraySize == oldSize) arrayProp.DeleteArrayElementAtIndex(i);
                any = true;
            }
            if (!any) return;
            // ONE ApplyModifiedProperties for the whole set, so the user's Ctrl+Z puts every row back at
            // once rather than one press per row.
            so.ApplyModifiedProperties();
            onChange?.Invoke();
        }

        /// <summary>Removes the element at <paramref name="index"/>.</summary>
        public static void Delete(SerializedProperty arrayProp, SerializedObject so, int index, Action onChange = null)
        {
            if (index < 0 || index >= arrayProp.arraySize) return;
            int oldSize = arrayProp.arraySize;
            arrayProp.DeleteArrayElementAtIndex(index);
            // Object-reference arrays need a second delete: the first only nulls the reference (mirrors the
            // existing −/✕ button behavior).
            if (arrayProp.arraySize == oldSize) arrayProp.DeleteArrayElementAtIndex(index);
            so.ApplyModifiedProperties();
            onChange?.Invoke();
        }

        // Reset a serialized value (or a whole struct/class subtree) to its type default, so a freshly
        // inserted element doesn't inherit the neighbor it was copied from.
        private static void ResetToDefault(SerializedProperty element)
        {
            switch (element.propertyType)
            {
                case SerializedPropertyType.Integer: element.intValue = 0; break;
                case SerializedPropertyType.Boolean: element.boolValue = false; break;
                case SerializedPropertyType.Float: element.floatValue = 0f; break;
                case SerializedPropertyType.String: element.stringValue = string.Empty; break;
                case SerializedPropertyType.Color: element.colorValue = default; break;
                case SerializedPropertyType.ObjectReference: element.objectReferenceValue = null; break;
                case SerializedPropertyType.LayerMask: element.intValue = 0; break;
                case SerializedPropertyType.Enum: element.enumValueIndex = 0; break;
                case SerializedPropertyType.Vector2: element.vector2Value = default; break;
                case SerializedPropertyType.Vector3: element.vector3Value = default; break;
                case SerializedPropertyType.Vector4: element.vector4Value = default; break;
                case SerializedPropertyType.Rect: element.rectValue = default; break;
                case SerializedPropertyType.Character: element.intValue = 0; break;
                case SerializedPropertyType.AnimationCurve: element.animationCurveValue = new AnimationCurve(); break;
                case SerializedPropertyType.Bounds: element.boundsValue = default; break;
                case SerializedPropertyType.Quaternion: element.quaternionValue = Quaternion.identity; break;
                case SerializedPropertyType.Vector2Int: element.vector2IntValue = default; break;
                case SerializedPropertyType.Vector3Int: element.vector3IntValue = default; break;
                case SerializedPropertyType.RectInt: element.rectIntValue = default; break;
                case SerializedPropertyType.BoundsInt: element.boundsIntValue = default; break;
                case SerializedPropertyType.ManagedReference: element.managedReferenceValue = null; break;
                case SerializedPropertyType.Generic:
                    if (element.isArray)
                    {
                        element.arraySize = 0;
                    }
                    else
                    {
                        // Walk the struct/class's direct children (depth-bounded by GetEndProperty) and
                        // reset each; recursion handles nested structs/arrays.
                        var it = element.Copy();
                        var end = element.GetEndProperty();
                        bool enterChildren = true;
                        while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
                        {
                            enterChildren = false;
                            ResetToDefault(it);
                        }
                    }
                    break;
                // Gradient and any future types without a public setter are left as the copied value.
            }
        }
    }
}
