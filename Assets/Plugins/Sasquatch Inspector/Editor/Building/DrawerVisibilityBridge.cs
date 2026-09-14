using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Makes a third-party "hide this field" attribute hide the WHOLE ROW, not just its own contents.
    ///
    /// A <c>[ShowIf]</c>-style <c>PropertyDrawer</c> hides by reporting zero height and drawing nothing. On a
    /// <see cref="DisplayType.Default"/> field that works — Unity's <c>PropertyField</c> collapses — but only
    /// the drawer's own content goes away. The layout node around it is still ours: its margin, padding,
    /// background and border are still laid out, so the user is left with a dead strip exactly where the
    /// field used to be. Fill a Card with three such fields and the empty Card still renders.
    ///
    /// This bridge closes that gap with NO code required from the user: it asks Unity what height the field
    /// would take (which runs the developer's own drawer), and when the answer is "none" it routes a hide
    /// through <see cref="ElementVisibility"/> — the same arbiter conditions and search use, on the same
    /// element <see cref="ConditionBinder"/> hides. The result is indistinguishable from a Sasquatch
    /// condition: the row is gone, chrome and all.
    ///
    /// SCOPE, deliberately narrow:
    ///  • Only fields left on <see cref="DisplayType.Default"/> ever reach here, because only those run the
    ///    developer's drawer at all. A field given an explicit Sasquatch widget never runs it — that is what
    ///    <see cref="CustomDrawerNoticeRow"/> declares in the authoring UI.
    ///  • Only IMGUI drawers (the ones that override <c>GetPropertyHeight</c>) can be read this way, which is
    ///    every hide-me attribute the ecosystem actually ships. A UI Toolkit-only drawer reports a normal
    ///    height, so it simply never triggers a hide — a no-op, never a false one.
    ///  • Never in design mode: the canvas has to show every field so the author can select and style it,
    ///    exactly as conditions behave there.
    ///
    /// The height question goes through <see cref="EditorGUI.GetPropertyHeight(SerializedProperty, GUIContent, bool)"/>.
    /// We ASK rather than MEASURE the built element because once the row is hidden its own subtree stops being
    /// laid out, so a measurement would latch at zero and the field could never come back.
    /// </summary>
    public static class DrawerVisibilityBridge
    {
        /// <summary>
        /// Poll cadence. There is nothing to subscribe to — a third-party drawer's condition is arbitrary user
        /// code, so it cannot be tracked the way <see cref="ConditionBinder"/> tracks named properties. 100ms
        /// is imperceptible for a visibility flip driven by a human editing another field, and the work is one
        /// height query on the small minority of fields that have a custom drawer at all.
        /// </summary>
        internal const long PollMilliseconds = 100;

        /// <summary>Below this, the drawer has drawn nothing — no control renders in under a pixel.</summary>
        internal const float HiddenHeightThreshold = 1f;

        /// <summary>
        /// Wires <paramref name="element"/> (the node element, the same one <see cref="ConditionBinder"/>
        /// binds) to its field's drawer visibility. A no-op — costing one reflection lookup — unless the field
        /// really is owned by a non-Unity drawer.
        /// </summary>
        /// <returns>
        /// The evaluator, or null when nothing was bound. Production ignores it (the poll drives it); tests
        /// pump it directly so a visibility flip can be asserted without waiting on the poll clock.
        /// </returns>
        public static Action Bind(VisualElement element, BindingNode node, string resolvedPath,
            SerializedObject serializedObject, bool designMode)
        {
            if (designMode) return null;
            if (element == null || node == null || serializedObject == null || string.IsNullOrEmpty(resolvedPath)) return null;

            // An explicit widget replaces the drawer outright, so there is no drawer opinion to honor.
            if (node.DisplayType != DisplayType.Default) return null;

            var target = serializedObject.targetObject;
            if (target == null) return null;

            var field = BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), resolvedPath, out _);
            if (!SasquatchReflectionUtility.TryGetCustomPropertyDrawer(field, out var drawerInfo)) return null;

            // Our own natively-bound types never hide themselves, so there is nothing here to poll for — and
            // asking anyway is actively harmful. GetPropertyHeight runs whatever drawer Unity picks, and an
            // ATTRIBUTE drawer outranks a type drawer: a [TextArea] on a SasquatchString lands in Unity's
            // TextAreaDrawer, which only handles string properties and LOGS before it throws. The catch below
            // swallows the exception but not the log, so a 100ms poll turns one mismatched attribute into
            // permanent console spam.
            if (!drawerInfo.isAttributeTargeted && SasquatchReflectionUtility.IsNativelyBoundType(drawerInfo.source))
                return null;

            var property = serializedObject.FindProperty(resolvedPath);
            if (property == null) return null;

            // Resolved once: displayName on a stale property throws, and the label is only ever passed
            // through to the drawer's own height calculation.
            var label = new GUIContent(property.displayName);

            // Mirrors ConditionBinder's gate: spring the row in only on a live hidden→visible flip, never on
            // the first pass (which would pop-in storm every time the inspector opens).
            bool? lastVisible = null;

            void Evaluate()
            {
                float height;
                try
                {
                    height = EditorGUI.GetPropertyHeight(property, label, true);
                }
                catch
                {
                    // The SerializedObject went away under us (rebuild, reimport, target destroyed) or the
                    // developer's own GetPropertyHeight threw. Either way this row is about to be rebuilt —
                    // leave the last state alone rather than flashing the field back on.
                    return;
                }

                bool visible = height >= HiddenHeightThreshold;
                ElementVisibility.SetHidden(element, ElementVisibility.Reason.ExternalDrawer, !visible);

                // Don't spring a row the search filter is also hiding — it isn't actually appearing on screen.
                if (lastVisible == false && visible &&
                    !ElementVisibility.IsHiddenBy(element, ElementVisibility.Reason.SearchFilter))
                {
                    EditorSpringFX.PopIn(element);
                }
                lastVisible = visible;
            }

            // Once up front so an already-hidden field never flashes visible on the first frame, then poll.
            Evaluate();
            element.schedule.Execute(Evaluate).Every(PollMilliseconds);
            return Evaluate;
        }
    }
}
