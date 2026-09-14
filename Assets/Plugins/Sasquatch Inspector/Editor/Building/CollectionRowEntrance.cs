using System;
using SasquatchSpring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// LIVE ROW ENTRANCE: a row ADDED to a collection in the real Inspector springs up into its slot instead
    /// of simply being there on the next frame. Wired to every add gesture the widget owns — the header "+",
    /// the "＋ Add Item" footer, the row menu's Duplicate / Insert Above / Insert Below, and an object drag
    /// dropped onto the box — on BOTH the List and the Table renderer.
    ///
    /// <para><b>Why an ARM/CONSUME pair rather than "animate whatever looks new".</b> Every one of those
    /// gestures goes through the localized rows-only rebuild (<c>liveRebuild</c>), which <c>Clear()</c>s the
    /// rows container and builds the whole visible page again from scratch. To the population pass EVERY row
    /// is new, so an entrance applied where rows are made turns a single add into a wave running down the
    /// entire list. The only code that knows WHICH element is new is the code that added it — an append
    /// lands at the array's size BEFORE the grow, an insert/duplicate at the index the menu was opened on —
    /// so that site <see cref="Arm"/>s the range one call before it triggers the rebuild, and the population
    /// pass <see cref="Consume"/>s the arm once and plays only the rows inside it.</para>
    ///
    /// <para><b>Why the arm is a static and not something hung on the rows container.</b> Every mutation
    /// site sits upstream of that container: the header "+" is built before the rows exist at all (it is
    /// shared by both renderers through <c>CreateCollectionAddButton</c>), and the row menu only ever sees
    /// one row. The hand-off is safe anyway, because arming and the rebuild it causes are ONE synchronous
    /// call stack — no other collection can repopulate in between. The property path plus the target object
    /// stop a nested collection from consuming its parent's arm, and <see cref="ArmLifetimeSeconds"/> retires
    /// an arm whose rebuild never came, so a stale one can never fire late on some unrelated later refresh.
    /// </para>
    ///
    /// <para><b>SCALE and TRANSLATE, never OPACITY.</b> <see cref="EditorSpringFX.PopIn"/> seeds
    /// <c>opacity = 0</c> before it springs, so a spring that never gets ticked leaves a permanently
    /// INVISIBLE row rather than a merely un-animated one. That is the same trap the live column grip's
    /// reveal is built around (see <see cref="TableColumnGrip"/>'s class note), and it is worse here because
    /// the casualty would be the user's actual DATA. Both properties used here fail visible: the worst case
    /// is a row resting a few pixels low at 96%.</para>
    ///
    /// <para><b>The scale is deliberately small.</b> A collection row is nearly as wide as the inspector and
    /// the house spring overshoots (<c>Tune</c> adds +0.7 bounce), so a <c>PopIn</c>-sized 0.9 start would
    /// peak around 2% past full width and push the row's trailing edge out through the collection frame —
    /// which is exactly how a horizontal scrollbar gets popped (measured, for full-width buttons, in
    /// <see cref="EditorSpringFX"/>'s Grow note). <see cref="StartScale"/> keeps the overshoot inside the
    /// frame's own 8px padding. The BOUNCE lives on the vertical rise instead, where an overshoot costs
    /// nothing but a couple of pixels of overlap with the neighbouring row.</para>
    ///
    /// <para><b>The eyedropper is NOT wired here.</b> It already pops its landed rows itself
    /// (<c>CollectionEyedropper.PopNewRows</c>), from a scheduled pass that runs after the rebuild — arming
    /// as well would play two entrances on one row.</para>
    ///
    /// <para><b>Asset Store §2.5.g:</b> public API only. No editor-internal type is named, cast or
    /// reflected.</para>
    /// </summary>
    internal static class CollectionRowEntrance
    {
        /// <summary>How far below its slot the row starts, in pixels. Transform-only, so nothing reflows —
        /// the slot is already at its final height the frame the row appears, and the rows below never
        /// move.</summary>
        internal const float RisePx = 9f;

        /// <summary>The scale the row starts at. See the class note for why this is 0.96 and not PopIn's
        /// 0.9.</summary>
        internal const float StartScale = 0.96f;

        // Quick and springy — the boing IS the feature. Tune() lays the house +0.7 bounce on top, which puts
        // this around damping 0.19: one clear overshoot, a small second wobble, settled in about a quarter of
        // a second. Stated as a RELATIVE character like every other call-site config in the package.
        private static readonly SpringConfig ScaleConfig = new SpringConfig(0.30f, 0.35f);
        // The same feel in PIXEL space, so it needs a pixel-space settle epsilon: the normalized default
        // (0.001) is a thousandth of a pixel and leaves a 9px rise ringing invisibly long after it has
        // visually arrived. 0.5 is the value every other pixel spring in the package uses.
        private static readonly SpringConfig RiseConfig = new SpringConfig(0.30f, 0.35f, 0.5f);

        // An arm is meant to be taken by the very next rebuild, which the arming site triggers itself. This
        // window only has to survive that hand-off (and, for a caller that defers its rebuild by a frame, the
        // tick in between) — it is deliberately far too short to still be standing when the user's NEXT
        // action (typing in the search box, hopping a page) repopulates the same rows.
        private const double ArmLifetimeSeconds = 0.25;

        private static string _armPath;
        private static WeakReference _armTarget;   // weak: an arm must never keep an inspected object alive
        private static int _armFrom = -1;
        private static int _armCount;
        private static double _armAt;

        /// <summary>
        /// The index range one row-population pass should spring in. <c>default</c> means "nothing to
        /// animate", which is what every rebuild that was not caused by an add gets.
        /// </summary>
        internal readonly struct Window
        {
            private readonly int _from;
            private readonly int _count;

            internal Window(int from, int count) { _from = from; _count = count; }

            /// <summary>True for a RAW array index inside the added run.</summary>
            internal bool Covers(int index) => _count > 0 && index >= _from && index < _from + _count;

            /// <summary>Springs <paramref name="row"/> in if <paramref name="index"/> is one of the added
            /// elements. Call it right after the row is added to its container — the seed has to be on the
            /// element before its first paint, and <see cref="Play"/> needs it attached to a panel.</summary>
            internal void Play(VisualElement row, int index)
            {
                if (Covers(index)) CollectionRowEntrance.Play(row);
            }
        }

        /// <summary>
        /// Declares that <paramref name="count"/> elements have just been added to
        /// <paramref name="arrayProp"/> starting at RAW index <paramref name="firstIndex"/>, so the rebuild
        /// this caller is about to trigger springs exactly those rows in.
        /// <para>Call it BEFORE the rebuild, and with the index the element really landed on: an append is
        /// the array's size taken BEFORE the grow, an insert/duplicate is the insert index. A wrong index
        /// does not fail loudly — it animates the neighbour, which reads as the wrong row having been
        /// added.</para>
        /// </summary>
        internal static void Arm(SerializedProperty arrayProp, int firstIndex, int count = 1)
        {
            if (arrayProp == null || firstIndex < 0 || count <= 0) { Disarm(); return; }
            _armPath = arrayProp.propertyPath;
            _armTarget = new WeakReference(TargetOf(arrayProp));
            _armFrom = firstIndex;
            _armCount = count;
            _armAt = EditorApplication.timeSinceStartup;
        }

        /// <summary>Drops any pending entrance. Public to the assembly so a test can start from a known
        /// state; nothing in the product needs to call it directly.</summary>
        internal static void Disarm()
        {
            _armPath = null;
            _armTarget = null;
            _armFrom = -1;
            _armCount = 0;
        }

        /// <summary>
        /// Takes the pending entrance for <paramref name="arrayProp"/> and clears it. Call once per
        /// row-population pass, before the row loop.
        /// <para>A <paramref name="designMode"/> collection never animates — a structural change there swaps
        /// the whole canvas out, so there is no surviving row to point at — but it still CONSUMES, so an arm
        /// left by the shared "+" cannot outlive the designer and fire on a later live rebuild.</para>
        /// </summary>
        internal static Window Consume(SerializedProperty arrayProp, bool designMode)
        {
            if (_armFrom < 0) return default;

            // Retire first, unconditionally: an arm nobody came back for is a landmine, and the collection
            // that finally repopulates is not necessarily the one that armed it.
            if (EditorApplication.timeSinceStartup - _armAt > ArmLifetimeSeconds) { Disarm(); return default; }

            if (arrayProp == null || _armPath != arrayProp.propertyPath) return default;
            if (!ReferenceEquals(_armTarget?.Target, TargetOf(arrayProp))) return default;

            var window = new Window(_armFrom, _armCount);
            Disarm();
            return designMode ? default(Window) : window;
        }

        /// <summary>
        /// Seeds the row's transform and springs it home.
        /// <para>Two guards, and both of them are the difference between an effect and a stranded inline
        /// style. Motion off means the row is left completely untouched — not settled, not written and
        /// released — so nothing of this feature survives on the element. No panel means no
        /// <c>SpringEditorDriver</c> tick, so a seed written there would never be walked back; a live
        /// rebuild always has a panel (the rows container stays attached while it repopulates) and the very
        /// first build of an inspector, which does not, is never armed anyway.</para>
        /// </summary>
        internal static void Play(VisualElement row)
        {
            if (row == null || !EditorSpringFX.MotionEnabled) return;
            if (row.panel == null) return;

            // Centre origin, so the scale grows the row symmetrically and the overshoot budget is split
            // across both edges instead of being spent entirely on the trailing one.
            row.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f));
            row.style.scale = new Scale(new Vector3(StartScale, StartScale, 1f));
            row.style.translate = new Translate(0f, RisePx);

            row.SpringScale(1f, EditorSpringFX.Tune(ScaleConfig));
            // Springs to 0, which is also the value a reorder drag reads back and writes
            // (CollectionRowReorder.GetTranslateY), so a settled entrance leaves the drag exactly the resting
            // offset it expects rather than a keyword it has to interpret.
            row.SpringTranslate(Vector2.zero, EditorSpringFX.Tune(RiseConfig));
        }

        // A SerializedObject can outlive its inspector and then THROW on .targetObject rather than returning
        // null, so every read of it here is guarded — an entrance is decoration and must never be the thing
        // that takes an inspector down.
        private static UnityEngine.Object TargetOf(SerializedProperty arrayProp)
        {
            try { return arrayProp?.serializedObject?.targetObject; }
            catch { return null; }
        }
    }
}
