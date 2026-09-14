using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Where Live Debug's floating cards get parented, and — the whole reason this exists — <b>in what
    /// order</b>.
    ///
    /// <para><b>UI Toolkit has no z-index: paint order is child order.</b> The moment card, the settings card
    /// and the code peek all parent themselves to the same build root as the transport bar, and a child added
    /// later paints later. So opening any card put it on top of the pinned header — the one piece of chrome
    /// that is supposed to stay reachable no matter what else is on screen. Scrolling with a card open made it
    /// obvious: the card sat over the frozen toolbar and clipped its own title against it.</para>
    ///
    /// <para><b>Insert before the bar, do not add after it.</b> The transport bar re-asserts itself as the last
    /// child whenever it syncs, which fixes the order — but only on a tick, and the redraw tick deliberately
    /// stops while the game is not playing. Scrubbing a finished recording is exactly when nothing is ticking
    /// and exactly when the cards are used, so the self-heal could not be the primary mechanism. Placing the
    /// card correctly in the first place needs no tick at all.</para>
    /// </summary>
    internal static class DebugOverlayHost
    {
        /// <summary>The transport bar's marker class. Matched rather than referenced to keep this a leaf.</summary>
        private const string TransportClass = "sasq-debug-transport";

        /// <summary>
        /// Parents a floating card to the build root, underneath the pinned transport bar when there is one.
        /// </summary>
        internal static void Add(VisualElement root, VisualElement overlay)
        {
            if (root == null || overlay == null) return;

            for (int i = 0; i < root.childCount; i++)
            {
                if (!root[i].ClassListContains(TransportClass)) continue;
                root.Insert(i, overlay);
                return;
            }
            root.Add(overlay);
        }

        /// <summary>
        /// True when <paramref name="element"/> still has a meaningful amount of itself inside the scroll
        /// viewport. Used to close a card whose subject has scrolled away: a card describing one sample of one
        /// graph is debris once that graph is off screen, and worse than debris while it floats over unrelated
        /// fields still claiming to describe something.
        /// </summary>
        /// <param name="minVisible">Pixels of the element that must remain on screen to count as visible.</param>
        internal static bool IsVisibleIn(ScrollView scroll, VisualElement element, float minVisible = 8f)
        {
            if (scroll == null || element == null || element.panel == null) return true;

            var viewport = scroll.contentViewport.worldBound;
            var rect = element.worldBound;
            // NEGATED `>`, not `<=`, and that is the whole point. `worldBound` is (0, 0, NaN, NaN) before the
            // first layout pass, and `NaN <= 0f` is FALSE under IEEE — so a NaN height fell straight past this
            // guard into the two comparisons below, where every test against NaN is also false, and the method
            // returned NOT VISIBLE. That is the one direction this gate must never resolve an uncertain answer
            // to: the caller closes the moment card on a false, so an open card vanished with no cause the user
            // could see. Reachable whenever a scroll tick lands between an attach and its layout — a rebuild, a
            // condition revealing a row, a container expanding.
            if (!(viewport.height > 0f) || !(rect.height > 0f)) return true;

            return rect.yMax > viewport.yMin + minVisible
                && rect.yMin < viewport.yMax - minVisible;
        }
    }
}
