using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Euler Angles widget: edits a <see cref="Quaternion"/> as three degree fields (X/Y/Z), so a rotation reads
    /// and edits in human degrees. Also serves Quaternion's DEFAULT arm, so this is what EVERY quaternion field
    /// renders as. Falls back to a plain field for a non-Quaternion property (fuzzer-safe).
    /// <para>
    /// The displayed triple is an EULER HINT, not a live decomposition — see the note on <c>hint</c> below.
    /// Re-deriving the row from <see cref="Quaternion.eulerAngles"/> on every refresh is what made a dragged
    /// axis rewrite the two axes beside it.
    /// </para>
    /// </summary>
    public static partial class BindingFactory
    {
        // How far the rotation may sit from the one the hint describes before the hint is stale and the row
        // gets a fresh decomposition. Quaternion.Angle already collapses float noise to a hard zero, and a
        // SerializedProperty write-then-read round trip was MEASURED at 0.00000000 deg of drift, so this only
        // has to absorb prefab/animation rounding. It must stay far below any nudge a user could see.
        private const float EulerHintTolerance = 0.01f;

        // Hints SURVIVE a widget rebuild, keyed by target + property path.
        //
        // A rebuild is not rare — every selection change, undo, and inspector repopulation calls
        // CreateEulerAngles afresh — and a hint seeded from `.eulerAngles` on each one hands the user
        // (89, 180, 180) again the moment they select away and back. That is the SAME defect the hint exists
        // to kill, just one selection change further out. Unity gets to serialise its equivalent onto the
        // Transform (m_LocalEulerAnglesHint); a buyer's own Quaternion field has nowhere to put one, so it
        // lives here for the session instead.
        //
        // Pure managed value types keyed by instance id, so there is nothing native to release and a domain
        // reload drops the whole table safely (this is NOT the transient-DontSave-orphan class). A recycled
        // instance id cannot mislead: SeedEulerHint only accepts a remembered triple that still describes the
        // CURRENT rotation, so a wrong entry self-heals into a fresh decomposition on first read.
        private static readonly Dictionary<(int, string), Vector3> s_eulerHints =
            new Dictionary<(int, string), Vector3>();

        // A long session should not grow the table without bound. There is no value in an LRU here — every
        // entry is reconstructible from the property in one decomposition — so the cheapest correct policy is
        // to drop the lot and let the live rows re-seed.
        private const int MaxEulerHints = 512;

        private static bool TryEulerHintKey(SerializedProperty prop, out (int, string) key)
        {
            key = default;
            try
            {
                var target = prop.serializedObject.targetObject;
                if (target == null) return false;
                // UnityObjectId, never GetInstanceID(): the raw call is ERROR-level obsolete from Unity
                // 6000.5 and would fail the whole package's build there. Its per-session identity is exactly
                // what an in-memory cache key wants. Pinned by TestNoDirectInstanceIdCalls.
                key = (UnityObjectId.Of(target), prop.propertyPath);
                return true;
            }
            catch (System.Exception)
            {
                return false; // dead SerializedObject — nothing to remember it by
            }
        }

        /// <summary>The triple to start this row on: the one we remember for this property when it still
        /// describes the stored rotation, otherwise a fresh decomposition.</summary>
        private static Vector3 SeedEulerHint(SerializedProperty prop)
        {
            var q = prop.quaternionValue;
            if (TryEulerHintKey(prop, out var key)
                && s_eulerHints.TryGetValue(key, out var remembered)
                && Quaternion.Angle(Quaternion.Euler(remembered), q) <= EulerHintTolerance)
                return remembered;
            return q.eulerAngles;
        }

        private static void StoreEulerHint(SerializedProperty prop, Vector3 hint)
        {
            if (!TryEulerHintKey(prop, out var key)) return;
            if (s_eulerHints.Count >= MaxEulerHints) s_eulerHints.Clear();
            s_eulerHints[key] = hint;
        }

        /// <summary>Test hook: forget every remembered triple, so a case starts from a known-empty table.</summary>
        internal static void ClearEulerHintsForTests() => s_eulerHints.Clear();

        private static VisualElement CreateEulerAngles(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.Quaternion)
                return new PropertyField(prop) { label = resolvedLabel };

            var field = new Vector3Field(resolvedLabel);

            // THE EULER HINT - the triple the user is actually working in.
            //
            // `Quaternion.eulerAngles` is a CANONICAL decomposition, not the angles that built the rotation.
            // Measured: Euler(91, 0, 0).eulerAngles == (89, 180, 180), and Euler(-30, 0, 0).eulerAngles ==
            // (330, 0, 0). The same rotation every time, wildly different numbers - and in the first case TWO
            // axes the user never touched. Re-deriving the row on every refresh therefore rewrote the whole
            // field the instant a value left the band the decomposition prefers, which read to the user as
            // "I dragged X, clicked away, and Y and Z changed by themselves".
            //
            // So: remember what we displayed, and keep showing it for as long as it still describes THIS
            // rotation. Only a genuinely EXTERNAL change (undo, another inspector, an animation clip) lands
            // far enough away to earn a fresh decomposition. Unity's own Transform inspector carries the same
            // crutch for the same reason (m_LocalEulerAnglesHint).
            var hint = SeedEulerHint(prop);
            StoreEulerHint(prop, hint);

            // The euler to SHOW for p - the hint while it still holds, otherwise a fresh decomposition.
            Vector3 Display(SerializedProperty p)
            {
                var q = p.quaternionValue;
                if (Quaternion.Angle(Quaternion.Euler(hint), q) <= EulerHintTolerance) return hint;
                hint = q.eulerAngles;
                StoreEulerHint(p, hint);
                return hint;
            }

            field.SetValueWithoutNotify(hint);
            // Multi-object: the fields show Unity's native "-" instead of the first object's rotation
            // dressed up as the whole selection's. This widget also serves Quaternion's DEFAULT arm
            // (see BindingFactory's Default case), so without it EVERY quaternion field reads as
            // authoritative on a multi-selection.
            field.showMixedValue = MixedValueUI.IsMixed(prop);

            field.RegisterValueChangedCallback(evt =>
            {
                // Euler is not a per-component write — the quaternion is rebuilt from all three angles —
                // so `prop.quaternionValue = Quaternion.Euler(newValue)` would give EVERY selected object
                // the first object's rotation with one axis changed, destroying the others' rotations.
                // Apply the component(s) the user actually moved to each object's OWN euler instead.
                // previousValue is what the field displayed, so an untouched axis compares equal.
                var before = evt.previousValue;
                var after = evt.newValue;
                // The user just told us which triple they mean. Adopt it BEFORE the write, so nothing the
                // write itself kicks off (the tracker can fire off the same Apply) finds a stale hint and
                // decomposes over the top of them.
                hint = after;
                StoreEulerHint(prop, hint);
                bool multi = MixedValueUI.IsMixed(prop) || prop.serializedObject.isEditingMultipleObjects;
                if (!multi)
                {
                    prop.serializedObject.Update();
                    prop.quaternionValue = Quaternion.Euler(after);
                    prop.serializedObject.ApplyModifiedProperties();
                }
                else
                {
                    MutatePerTarget(prop, p =>
                    {
                        var e = p.quaternionValue.eulerAngles;
                        for (int i = 0; i < 3; i++)
                            if (!Mathf.Approximately(after[i], before[i])) e[i] = after[i];
                        p.quaternionValue = Quaternion.Euler(e);
                    });
                    // The edit may have resolved (or introduced) the mix.
                    field.showMixedValue = MixedValueUI.IsMixed(prop);
                }
            });

            // Focus lands on a box's inner TEXT ELEMENT, and a Vector3Field's three boxes are three separate
            // focusables - so hopping X to Y fires FocusOutEvent on the VECTOR3FIELD (measured) even though the
            // user never left it. The bool this replaces read "blurred" at that moment and let the refresh run
            // mid-edit. Walk up from the focus controller instead. Same defect class as the one recorded in
            // multi-object-composite-write-clobber; the export kit's EulerField already had the walk-up.
            bool IsFocused()
            {
                var f = field.focusController?.focusedElement as VisualElement;
                for (; f != null; f = f.parent) if (f == field) return true;
                return false;
            }

            // Deferred a tick on purpose: at FocusOut time the NEW focus has not landed yet, so IsFocused()
            // would report false for an intra-field hop and defeat the check above.
            field.RegisterCallback<FocusOutEvent>(_ => field.schedule.Execute(() =>
            {
                if (IsFocused()) return;
                // The SerializedObject can die while this element is still attached (a target destroyed under
                // a live inspector); there is nothing to repaint if it has.
                try
                {
                    field.showMixedValue = MixedValueUI.IsMixed(prop);
                    field.SetValueWithoutNotify(Display(prop));
                }
                catch (System.Exception) { /* dead SerializedObject - the rebuild replaces this field */ }
            }));

            field.TrackPropertyValueWithRefresh(prop, p =>
            {
                // Re-evaluated on every external change (undo, a sibling edit, a selection re-bind), not
                // stamped once at build: a widget frozen in the chrome it was born with is its own bug.
                field.showMixedValue = MixedValueUI.IsMixed(p);
                if (!IsFocused()) field.SetValueWithoutNotify(Display(p));
            });

            return field;
        }
    }
}
