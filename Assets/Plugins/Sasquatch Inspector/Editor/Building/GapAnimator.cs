using System.Collections.Generic;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    // Opens the "make room" gap with two coordinated effects:
    //  1. Direct siblings at/after the slot TRANSLATE out of the way (animated by the USS translate
    //     transitions on .designer-card / .collection-item-row / .organizer-card — translate
    //     interpolates fine from its initial 'none').
    //  2. The receiving container genuinely EXPANDS — driven by a keyed min-height spring (NOT a
    //     USS transition: the start value is the keyword 'auto', which transitions can't
    //     interpolate, so a transition silently fails to grow the box). One spring per container,
    //     keyed, so the gap GLIDES with preserved velocity when it retargets between slots (a plain
    //     tween would restart its curve on every move).
    //
    // Every pixel of movement is something WE applied — the offsets dictionary records it all, and
    // GetOffset lets the resolver correct its frozen geometry snapshot by the known displacement.
    // No hit-testing against live layout ever happens, so the no-feedback-loop guarantee holds.
    /// <summary>
    /// Animates the drag-and-drop "make room" gap: translates siblings aside and springs the receiving
    /// container's min-height open, recording every applied offset so the drop resolver can correct its
    /// frozen geometry snapshot (no hit-testing against live layout — keeps the no-feedback-loop guarantee).
    /// </summary>
    public class GapAnimator
    {
        private const string GapKey = "gap.minHeight";
        // Fast, near-critically damped: visible bounce on the gap reads as jitter while aiming.
        // 0.5px settle epsilon — pixel spring; the default epsilon would ring sub-pixel for
        // ~1s and delay WatchRelease's min-height restore by that long.
        private static readonly SpringConfig GapConfig = new SpringConfig(0.25f, 0.15f, 0.5f);

        private readonly HashSet<VisualElement> _translated = new();
        private readonly Dictionary<VisualElement, Vector2> _offsets = new();

        // Current expansion
        private VisualElement _expanded;
        private StyleLength _expandedPrevMinHeight;
        private float _expandNatural;
        private float _expandValue;

        // Containers springing back to natural after the gap moved elsewhere. Tracked so a quick
        // return to the same container reuses the ORIGINAL inline min-height (no pollution).
        private readonly Dictionary<VisualElement, StyleLength> _releasing = new();

        private List<VisualElement> _curTranslateSet;
        private Vector2 _curOffset;
        private VisualElement _curExpandEl;
        private float _curExpandTo;

        // Per-element reflow: each card slides by its OWN offset to the track it will occupy. Apply()
        // cannot express this — it moves a whole set by one uniform vector, which is right for a gap
        // (everything after the seam shifts by the same amount) and wrong for a reorder, where tracks
        // can differ in size and a card may travel a different distance than its neighbour.
        //
        // Same mechanism as Apply's translations: `style.translate` is a PAINT-time transform, so
        // nothing here invalidates layout and no bound widget re-measures. Re-targets on every pointer
        // move, so the USS translate transition on .designer-card makes the cards glide rather than jump.
        /// <summary>Slides each of <paramref name="els"/> by its own offset (a reordering drop's live preview).</summary>
        public void ApplyReflow(List<VisualElement> els, List<Vector2> offsets)
        {
            var next = new HashSet<VisualElement>();
            if (els != null && offsets != null)
                for (int i = 0; i < els.Count && i < offsets.Count; i++)
                    if (els[i] != null) next.Add(els[i]);

            // Anything that was shifted and no longer is slides home.
            foreach (var el in _translated)
                if (!next.Contains(el)) { el.style.translate = new Translate(0f, 0f); _offsets.Remove(el); }

            if (els != null && offsets != null)
                for (int i = 0; i < els.Count && i < offsets.Count; i++)
                {
                    var el = els[i];
                    if (el == null) continue;
                    el.style.translate = new Translate(offsets[i].x, offsets[i].y);
                    _offsets[el] = offsets[i];
                }

            _translated.Clear();
            foreach (var el in next) _translated.Add(el);

            // A reorder never grows the box (that is the whole promise of a declared track count), so any
            // expansion left over from a previous target must let go.
            ReleaseExpansion(animated: true);

            // Poison Apply's "nothing changed" cache. Apply and ApplyReflow share _translated but keep
            // independent caches, and the natural reset values here (null set, zero offset, no expansion)
            // are EXACTLY what a following Apply(null, zero, null, 0, null) compares against — so it
            // decided it had nothing to do and returned without releasing the cards this method moved.
            // The symptom: slide a card aside, then aim at a target that displaces nothing, and the card
            // stayed parked instead of springing home. NaN never compares equal, so the next Apply always
            // does its work.
            _curTranslateSet = null;
            _curOffset = new Vector2(float.NaN, float.NaN);
            _curExpandEl = null;
            _curExpandTo = float.NaN;
        }

        /// <summary>Opens or retargets the gap — translates <paramref name="translateSet"/> by <paramref name="offset"/> and springs <paramref name="expandEl"/> open to <paramref name="expandTo"/>.</summary>
        public void Apply(List<VisualElement> translateSet, Vector2 offset,
            VisualElement expandEl, float expandTo, List<VisualElement> layoutPush)
        {
            bool same = ReferenceEquals(translateSet, _curTranslateSet)
                        && offset == _curOffset
                        && expandEl == _curExpandEl
                        && Mathf.Approximately(expandTo, _curExpandTo);
            if (same) return;

            // Translations: slide home anything no longer shifted, (re)target the new set. Because
            // the values re-target, moving between slots makes the gap GLIDE instead of teleporting.
            var next = new HashSet<VisualElement>();
            if (translateSet != null) foreach (var el in translateSet) next.Add(el);
            foreach (var el in _translated)
                if (!next.Contains(el)) el.style.translate = new Translate(0f, 0f);
            foreach (var el in next)
                el.style.translate = new Translate(offset.x, offset.y);
            _translated.Clear();
            foreach (var el in next) _translated.Add(el);

            // Expansion: spring-driven min-height growth on the receiving container.
            if (_expanded != expandEl)
            {
                ReleaseExpansion(animated: true);
                if (expandEl != null)
                {
                    float gap = Mathf.Max(0f, offset.y);
                    StyleLength prev;
                    if (_releasing.TryGetValue(expandEl, out var relPrev))
                    {
                        // It was on its way back down — the keyed spring resumes from wherever
                        // it is (velocity included); keep the ORIGINAL inline value for the
                        // eventual restore.
                        _releasing.Remove(expandEl);
                        prev = relPrev;
                    }
                    else
                    {
                        prev = expandEl.style.minHeight;
                    }

                    _expanded = expandEl;
                    _expandedPrevMinHeight = prev;
                    _expandNatural = Mathf.Max(0f, expandTo - gap);
                    _expandValue = ReadCurrentMinHeight(expandEl, _expandNatural);
                    StartExpandSpring(expandEl, expandTo);
                }
            }
            else if (expandEl != null && !Mathf.Approximately(_curExpandTo, expandTo))
            {
                StartExpandSpring(expandEl, expandTo);
            }

            // Known-displacement bookkeeping for the resolver. Offsets are registered on subtree
            // roots; GetOffset sums over an element's ancestor chain.
            _offsets.Clear();
            foreach (var el in next) _offsets[el] = offset;
            if (layoutPush != null)
                foreach (var el in layoutPush)
                    if (!_offsets.ContainsKey(el)) _offsets[el] = offset;

            _curTranslateSet = translateSet;
            _curOffset = offset;
            _curExpandEl = expandEl;
            _curExpandTo = expandTo;
        }

        // instant:true snaps everything home without animating (used right before a commit rebuild,
        // where animating into a discarded tree would just smear).
        /// <summary>Closes the gap, springing everything back to rest (immediately when <paramref name="instant"/>).</summary>
        public void ClearGap(bool instant)
        {
            foreach (var el in _translated)
            {
                if (el == null) continue;
                if (instant)
                {
                    el.AddToClassList("drag-no-transition");
                    el.style.translate = new Translate(0f, 0f);
                    var captured = el;
                    el.schedule.Execute(() => captured.RemoveFromClassList("drag-no-transition"));
                }
                else
                {
                    el.style.translate = new Translate(0f, 0f);
                }
            }
            _translated.Clear();

            if (instant)
            {
                // Finalize every pending release immediately, then the current expansion.
                foreach (var kv in _releasing)
                {
                    Spring.Cancel(kv.Key, GapKey);
                    kv.Key.style.minHeight = kv.Value;
                }
                _releasing.Clear();

                if (_expanded != null)
                {
                    Spring.Cancel(_expanded, GapKey);
                    _expanded.style.minHeight = _expandedPrevMinHeight;
                    _expanded = null;
                }
            }
            else
            {
                ReleaseExpansion(animated: true);
                // Pending releases keep springing home on their own.
            }

            _offsets.Clear();
            _curTranslateSet = null;
            _curOffset = Vector2.zero;
            _curExpandEl = null;
            _curExpandTo = 0f;
        }

        // The visual displacement currently applied to an element (directly or via a moved ancestor).
        /// <summary>The displacement currently applied to <paramref name="el"/> by the gap (zero if none).</summary>
        public Vector2 GetOffset(VisualElement el)
        {
            if (_offsets.Count == 0 || el == null) return Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (var cur = el; cur != null; cur = cur.parent)
                if (_offsets.TryGetValue(cur, out var v)) sum += v;
            return sum;
        }

        private void StartExpandSpring(VisualElement el, float to)
        {
            if (!EditorSpringFX.MotionEnabled)
            {
                _expandValue = to;
                el.style.minHeight = to;
                return;
            }

            // The cached keyed handle keeps its ORIGINAL closures, which captured this same
            // element — retargets glide with preserved velocity across expand/release cycles.
            // Max(0, v): the slight underdamping can swing a near-zero target negative,
            // and a negative min-height is invalid styling.
            Spring.To((object)el,
                () => ReadCurrentMinHeight(el, _expandNatural),
                v =>
                {
                    el.style.minHeight = Mathf.Max(0f, v);
                    if (ReferenceEquals(el, _expanded)) _expandValue = v;
                },
                to, EditorSpringFX.Tune(GapConfig), GapKey);
        }

        private void ReleaseExpansion(bool animated)
        {
            if (_expanded == null) return;
            var el = _expanded;
            var prev = _expandedPrevMinHeight;
            float from = _expandValue;
            float to = _expandNatural;
            _expanded = null;

            if (!animated || Mathf.Approximately(from, to) || !EditorSpringFX.MotionEnabled)
            {
                Spring.Cancel(el, GapKey);
                el.style.minHeight = prev;
                return;
            }

            _releasing[el] = prev;
            var handle = Spring.To((object)el,
                () => ReadCurrentMinHeight(el, to),
                v => el.style.minHeight = Mathf.Max(0f, v),
                to, EditorSpringFX.Tune(GapConfig), GapKey);
            WatchRelease(el, prev, handle);
        }

        private async void WatchRelease(VisualElement el, StyleLength prev, MotionHandleFloat handle)
        {
            try
            {
                await handle.WaitFor(AwaitStrategy.PhysicalSettling);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                return; // canceled by an instant clear (which already restored minHeight)
            }

            // Only finalize if this element is still releasing — it may have been re-expanded
            // or finalized by an instant clear while the spring was in the air.
            if (!_releasing.TryGetValue(el, out var cur) || cur != prev) return;
            _releasing.Remove(el);
            el.style.minHeight = prev;
        }

        // Best-effort read of the box's current min-height in px (it may be mid-release-spring);
        // falls back to the known natural height when no pixel value is set.
        private static float ReadCurrentMinHeight(VisualElement el, float fallback)
        {
            var inline = el.style.minHeight;
            if (inline.keyword == StyleKeyword.Undefined && inline.value.unit == LengthUnit.Pixel && inline.value.value > 0f)
                return inline.value.value;
            return fallback;
        }
    }
}
