using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace SasquatchInspector.Editor
{
    // The one road to TrackPropertyValue / TrackSerializedObjectValue for everything that ships
    // (enforced by TestTrackedRefreshConformance). Unity's trackers fire on change-vs-baseline, and the
    // baseline silently resets to the CURRENT value when the tracked element is detached and reattached:
    // re-registration is deferred to the next binding tick, so a data change that lands between the
    // reattach and that tick is treated as the starting value and never fires the callback. Collection
    // drag-reorder does exactly that to the dragged row — BeginFloat reparents it into the overlay and
    // Commit's Restore reparents it home, then MoveArrayElement changes the data in the same frame — so
    // every change-driven widget in that row froze on its pre-drag value (BindProperty fields were fine:
    // bindings re-sync unconditionally on attach). Registering through this wrapper records the callback
    // so CollectionRowReorder.Commit can re-fire the dragged row's trackers against the post-move data.
    /// <summary>
    /// Registers Unity property/object trackers while ALSO recording the callback so an explicit
    /// <see cref="RefreshSubtree"/> can re-fire it — the recovery for value changes that Unity's
    /// tracker swallows when they race a detach/reattach (see CollectionRowReorder.Commit).
    /// </summary>
    internal static class TrackedRefresh
    {
        // Marker class = fast UQuery lookup; the weak table carries the actual refresh actions and
        // dies with the element (no unregistration bookkeeping needed on rebuild teardown).
        internal const string MarkerClass = "sasq-tracked-refresh";

        private static readonly ConditionalWeakTable<VisualElement, List<Action>> s_refreshers =
            new ConditionalWeakTable<VisualElement, List<Action>>();

        /// <summary>
        /// True only while <see cref="RefreshSubtree"/> is invoking recorded callbacks. Widgets whose
        /// value display normally GLIDES (the progress bar's fill ease) consult this to SNAP instead:
        /// an explicit refresh means the row's binding now holds a different element's value — animating
        /// from the old fill would read as "the value just changed", which is exactly the wrong story
        /// after a reorder. Values must appear to move WITH their row, never morph in place.
        /// </summary>
        public static bool IsExplicitRefresh { get; private set; }

        /// <summary>TrackPropertyValue + a recorded refresh so RefreshSubtree can re-fire it.</summary>
        public static void TrackPropertyValueWithRefresh(
            this VisualElement owner, SerializedProperty prop, Action<SerializedProperty> onChanged)
        {
            if (owner == null || prop == null || onChanged == null) return;
            owner.TrackPropertyValue(prop, onChanged);
            Record(owner, () => onChanged(prop));
        }

        /// <summary>TrackSerializedObjectValue + a recorded refresh so RefreshSubtree can re-fire it.</summary>
        public static void TrackSerializedObjectValueWithRefresh(
            this VisualElement owner, SerializedObject obj, Action<SerializedObject> onChanged)
        {
            if (owner == null || obj == null || onChanged == null) return;
            owner.TrackSerializedObjectValue(obj, onChanged);
            Record(owner, () => onChanged(obj));
        }

        private static void Record(VisualElement owner, Action refresh)
        {
            var list = s_refreshers.GetValue(owner, _ => new List<Action>());
            list.Add(refresh);
            owner.AddToClassList(MarkerClass);
        }

        /// <summary>
        /// Re-fires every recorded tracker callback on <paramref name="root"/> and its descendants,
        /// as if each tracked value had just changed. Callers use this exactly where a data change can
        /// race a reparent (today: the drag-reorder commit, on the dragged row only).
        /// </summary>
        public static void RefreshSubtree(VisualElement root)
        {
            if (root == null) return;
            var targets = root.Query<VisualElement>(className: MarkerClass).ToList();
            if (root.ClassListContains(MarkerClass) && !targets.Contains(root)) targets.Add(root);
            bool wasExplicit = IsExplicitRefresh; // nested RefreshSubtree (a refresher that refreshes) keeps the flag
            IsExplicitRefresh = true;
            try
            {
                foreach (var el in targets)
                {
                    // The target list is snapshotted UP FRONT, so an earlier refresher in this same pass may
                    // already have REMOVED this element — a structural rebuild driven by an array-size sync
                    // does exactly that when the array shrank. Its property paths are dead by now, and
                    // reading one logs a NATIVE error ("type is not a supported pptr value") that the catch
                    // below cannot intercept. Skip whatever left the subtree; its replacement is already in.
                    //
                    // The test is "still under root", NOT "has a panel": a detached tree (the designer's
                    // preview, a headless test) is perfectly valid to refresh — every element in it is
                    // panel-less, and gating on that would silently refresh nothing at all.
                    if (!StillUnder(el, root)) continue;
                    if (!s_refreshers.TryGetValue(el, out var actions)) continue;
                    foreach (var refresh in actions)
                    {
                        try { refresh(); }
                        catch (Exception)
                        {
                            // A refresh can land on a widget whose SerializedObject just died (target deleted
                            // mid-interaction) — that widget is about to be torn down anyway; never let it
                            // break the remaining refreshes or the reorder commit that asked for them.
                        }
                    }
                }
            }
            finally
            {
                IsExplicitRefresh = wasExplicit;
            }
        }

        /// <summary>True while <paramref name="el"/> is still <paramref name="root"/> or one of its
        /// descendants — i.e. an earlier refresher in this pass has not torn it out. Panel-agnostic, so a
        /// detached tree still refreshes normally.</summary>
        private static bool StillUnder(VisualElement el, VisualElement root)
        {
            for (var cur = el; cur != null; cur = cur.parent)
                if (cur == root) return true;
            return false;
        }
    }
}
