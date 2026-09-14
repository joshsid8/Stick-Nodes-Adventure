using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Keeps the Inspector window's scroll position still while an inspector body is torn down and rebuilt.
    ///
    /// <para>THE BUG THIS EXISTS FOR (measured live, not theorised): with the Inspector LOCKED on a component
    /// and scrolled down to a list, clicking a prefab in the Project window makes Unity repopulate that row.
    /// Our displaced body goes with it and is rebuilt on the next tick — the body element's identity changes
    /// while its bind key does NOT, which is what proves this is Unity tearing the body out rather than us
    /// choosing to rebuild. For at least one layout pass the row measures short, the Inspector's
    /// <see cref="ScrollView"/> clamps the scroll offset to that collapsed content, and when the body returns
    /// nothing puts the offset back. Traced: 2844 → 795, with the content back at its full 3789 the very next
    /// frame. The user is thrown partway up their layout and has to hunt for the list they were filling.</para>
    ///
    /// <para>ANCHORED ON THE SCROLLVIEW, NOT THE ROW. The first version of this guard hung off the
    /// <c>InspectorElement</c> and did nothing at all, because that row is precisely what Unity is replacing:
    /// the reservation landed on an element already on its way out, and the settle's scheduled item never ran
    /// because its host was detached. The ScrollView outlives every row rebuild, so it is the only stable
    /// anchor — and holding the height on its <c>contentContainer</c> reserves the whole inspector's content,
    /// which is what the clamp is computed from anyway. It therefore does not matter WHICH row collapsed.</para>
    ///
    /// <para>TWO LAYERS: the reservation stops the content shrinking, so there is nothing to clamp against and
    /// the user sees no movement at all; the settle then repairs a clamp that landed anyway (a row replaced
    /// wholesale, the first tick after a domain reload).</para>
    ///
    /// <para>WHY THERE IS A REPAIR WINDOW. A guard that waits until it has SEEN the scrollable range shrink can
    /// never fire here: the collapse and the recovery both happen inside a single editor frame, so by the guard's
    /// first tick the range is already back to full and only the offset is wrong. Measured — the first version
    /// read exactly that state as "the user scrolled" and stood down every time. So inside
    /// <see cref="RepairFrames"/> ticks of the rebuild, an offset sitting BELOW the protected position while the
    /// range allows that position is treated as the clamp it is. After the window, only an observed clamp
    /// justifies a write, and an offset that moved DOWN is always the user — so the guard can never fight them
    /// for more than a few frames immediately after a rebuild they did not ask for.</para>
    ///
    /// <para>The reservation is a plain <c>style.minHeight</c> write, safe here precisely because the element is
    /// the ScrollView's own content container — NOT one of our widget rows, where the three composing floors
    /// arbitrated by <see cref="MinHeightFloors"/> live. Nothing else claims min-height on it.</para>
    ///
    /// <para>ONLY EVER ENGAGES WHEN THERE IS A POSITION TO PROTECT, and only across a rebuild of the SAME
    /// content. Both limits exist because this guard shipped without them and caused a worse bug than the one
    /// it fixes: it reserved the OUTGOING inspector's height on every detach, including an ordinary selection
    /// change. Measured live on the demo scene — clicking from one GameObject to another pinned the incoming
    /// inspector to the outgoing one's content height (1182 against its own 854, with a 946 viewport), which
    /// forced the vertical scrollbar on for <see cref="ReleaseReservationAfterFrames"/> ticks and then took it
    /// away again. A 13px usable-width change reflows the whole layout in one frame: previews resize, curve
    /// fields re-render, slider knobs jump ~22px. See <see cref="Start"/> and
    /// <see cref="CancelIfArmedForAnother"/>.</para>
    /// </summary>
    internal static class InspectorScrollPreservation
    {
        // Frame-driven rather than wall-clock: an `Every(0)` item runs on each scheduler tick, which makes the
        // settle deterministic (and testable by pumping the scheduler) instead of dependent on editor timing.
        internal const int ReapplyIntervalMs = 0;

        /// <summary>How many scheduler ticks the settle watches before giving up.</summary>
        internal const int MaxSettleFrames = 20;

        /// <summary>
        /// How long after the rebuild an offset below the protected position is read as a clamp rather than as
        /// the user scrolling. See the class remarks — the clamp routinely lands before tick 1.
        /// </summary>
        internal const int RepairFrames = 6;

        /// <summary>
        /// When the reserved height is handed back. Deliberately EARLIER than <see cref="MaxSettleFrames"/> so
        /// the remaining ticks can still repair a clamp that the release itself causes — content that
        /// legitimately came back shorter shrinks the moment the reservation lifts.
        /// </summary>
        internal const int ReleaseReservationAfterFrames = 8;

        /// <summary>"Whoever armed this could not identify itself" — such a guard is never auto-cancelled by
        /// <see cref="CancelIfArmedForAnother"/>, because there is nothing to compare it against.</summary>
        internal const int NoOwner = 0;

        // Diagnostics + test seams: rebuilds guarded, and how many actually needed a repair.
        internal static int s_HoldCount, s_RestoreCount;

        private sealed class Settle
        {
            public float Target;       // the scroll offset being protected
            public float Reserved;     // height held on the content container, 0 once handed back
            public int Frame;
            public bool SawClamp;      // the range was observed below Target — a restore is owed
            public int OwnerKey;       // the body this guard was armed for; NoOwner when unidentified
            public IVisualElementScheduledItem Item;
        }

        private static readonly ConditionalWeakTable<ScrollView, Settle> s_Active =
            new ConditionalWeakTable<ScrollView, Settle>();

        /// <summary>The nearest ancestor <see cref="ScrollView"/>, or null when the element isn't inside one.</summary>
        internal static ScrollView FindScrollView(VisualElement from)
        {
            for (var ve = from?.hierarchy.parent; ve != null; ve = ve.hierarchy.parent)
                if (ve is ScrollView sv) return sv;
            return null;
        }

        /// <summary>
        /// Guards <paramref name="scroll"/> across an inspector body swap: holds the content height it had and
        /// protects the scroll position until the replacement has resolved. Call as the OLD body leaves — from
        /// its detach handler — so the reservation lands before the next layout pass measures the gap.
        /// </summary>
        /// <param name="scroll">The Inspector's ScrollView, resolved while the body was still attached.</param>
        /// <param name="knownContentHeight">The content height last seen with the body in place. Falls back to
        /// the container's current layout height, which is still the pre-collapse value when the detach handler
        /// runs before layout — but the caller's tracked value is preferred because that ordering is Unity's to
        /// change, not ours.</param>
        /// <param name="ownerKey">Identifies the body that is leaving, so a later build for a DIFFERENT body can
        /// hand the reservation straight back via <see cref="CancelIfArmedForAnother"/>. Pass
        /// <see cref="NoOwner"/> from callers that cannot identify themselves — they must rule the selection-change
        /// case out some other way (the fallback path uses its editor's own teardown).</param>
        internal static void HoldAcrossRebuild(ScrollView scroll, float knownContentHeight, int ownerKey = NoOwner)
        {
            if (scroll == null) return;
            float reserve = Mathf.Max(knownContentHeight, scroll.contentContainer.layout.height);
            Start(scroll, scroll.scrollOffset.y, reserve, ownerKey);
        }

        /// <summary>
        /// Protects an already-captured scroll position across a rebuild, with no height reservation — for the
        /// FALLBACK path, which clears and refills its own root container and captures the offset itself. Kept
        /// in parity with the displacement path on purpose: a single-shot re-apply one tick later lands while
        /// the rebuilt content is still mid-relayout, so on its own it restores a value that is promptly clamped.
        /// </summary>
        internal static void RestoreAcrossSettle(ScrollView scroll, float target)
        {
            if (scroll == null) return;
            Start(scroll, target, 0f, NoOwner);
        }

        /// <summary>Drops any guard, handing back a held height immediately.</summary>
        internal static void Cancel(ScrollView scroll)
        {
            if (scroll == null) return;
            if (s_Active.TryGetValue(scroll, out var state)) Finish(scroll, state);
        }

        /// <summary>
        /// Drops a guard that was armed for a DIFFERENT body than the one now building — the rebuild it is
        /// holding height for is not coming, because this turned out to be a genuine target change rather than
        /// the repopulation the guard exists for. Call from a render path's build site the moment it knows it is
        /// building fresh content rather than re-homing the body that just left.
        ///
        /// <para>Without this the guard carries one object's scroll position AND content height onto another
        /// object's inspector for <see cref="ReleaseReservationAfterFrames"/> ticks, then hands them back all at
        /// once — the layout pop this class documents. A guard armed by an unidentified caller
        /// (<see cref="NoOwner"/>) is left alone: there is nothing to compare, so cancelling would be a guess.</para>
        /// </summary>
        internal static void CancelIfArmedForAnother(ScrollView scroll, int ownerKey)
        {
            if (scroll == null || ownerKey == NoOwner) return;
            if (!s_Active.TryGetValue(scroll, out var state)) return;
            if (state.OwnerKey == NoOwner || state.OwnerKey == ownerKey) return;
            Finish(scroll, state);
        }

        /// <summary>Whether a content height is currently held (test seam).</summary>
        internal static bool HasReservation(ScrollView scroll)
            => scroll != null && s_Active.TryGetValue(scroll, out var s) && s.Reserved > 0f;

        private static void Start(ScrollView scroll, float target, float reservedHeight, int ownerKey)
        {
            // A second rebuild supersedes the first. Cancel hands back the previous reservation before the new
            // one is measured; callers capture their height while it is still in force, so it carries across a
            // burst of rebuilds instead of decaying toward the collapsed value.
            Cancel(scroll);

            // NOTHING TO PROTECT, NOTHING TO DO — and doing something anyway is actively harmful.
            //
            // A clamp can only ever REDUCE the scroll offset, so an offset already at 0 cannot be clamped: there
            // is no position to lose and therefore no reason to reserve height. The reservation is not free
            // insurance — it pins the INCOMING inspector to the OUTGOING one's height, which is simply wrong
            // whenever the two are different objects, and every ordinary selection change lands here at offset 0.
            //
            // Measured on the demo scene: switching between two GameObjects held the new inspector at 1182px
            // against its own 854px (viewport 946), forcing the vertical scrollbar on for
            // ReleaseReservationAfterFrames ticks and then off again — 13px of usable width appearing at once,
            // reflowing every widget in the layout. The guard is for a LOCKED inspector scrolled DOWN; at the
            // top of the inspector it has no work to do, so it must stay out of the way entirely.
            if (target <= 0.5f) return;

            if (reservedHeight > 0f) scroll.contentContainer.style.minHeight = reservedHeight;

            var state = new Settle
            {
                Target = target,
                Reserved = Mathf.Max(0f, reservedHeight),
                OwnerKey = ownerKey,
            };
            s_Active.Add(scroll, state);
            s_HoldCount++;

            state.Item = scroll.schedule.Execute(() => Tick(scroll, state)).Every(ReapplyIntervalMs);
        }

        private static void Tick(ScrollView scroll, Settle state)
        {
            state.Frame++;

            // Hand the reserved height back once the replacement has had time to lay out.
            if (state.Reserved > 0f && state.Frame >= ReleaseReservationAfterFrames)
            {
                scroll.contentContainer.style.minHeight = StyleKeyword.Null;
                state.Reserved = 0f;
            }

            if (state.Target > 0.5f)
            {
                float max = Mathf.Max(0f, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
                float now = scroll.scrollOffset.y;

                if (now > state.Target + 0.5f)
                {
                    // Moved DOWN past the position we were protecting. Only the user does that.
                    Finish(scroll, state);
                    return;
                }

                if (max < state.Target - 0.5f)
                {
                    // Too short to hold the position: writing now would only be clamped straight back.
                    state.SawClamp = true;
                }
                else if (now < state.Target - 0.5f)
                {
                    if (state.SawClamp || state.Frame <= RepairFrames)
                    {
                        scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, state.Target);
                        s_RestoreCount++;
                        state.SawClamp = false;
                        if (state.Reserved <= 0f) { Finish(scroll, state); return; }
                    }
                    else
                    {
                        // Past the repair window with no clamp ever observed — the user scrolled up.
                        Finish(scroll, state);
                        return;
                    }
                }
            }

            if (state.Frame >= MaxSettleFrames) Finish(scroll, state);
        }

        private static void Finish(ScrollView scroll, Settle state)
        {
            if (state.Reserved > 0f)
            {
                scroll.contentContainer.style.minHeight = StyleKeyword.Null;
                state.Reserved = 0f;
            }
            state.Item?.Pause();
            state.Item = null;
            s_Active.Remove(scroll);
        }
    }
}
