using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Reveals exactly one drag grip at a time: the one belonging to the innermost grip-owning
    /// element under the cursor. A plain USS :hover rule cannot express this — :hover matches
    /// every ancestor card in the chain at once, so nested hierarchies lit a whole column of
    /// grips simultaneously (the "glow cluster"). Watches pointer moves on the canvas container
    /// and pins the .grip-hot class to the single deepest .grip-host; DesignerCanvas.uss turns
    /// that class into the grip's resting visibility.
    /// Public rather than internal because the verification tests live in a separate assembly.
    /// </summary>
    public sealed class GripHoverTracker
    {
        public const string HostClass = "grip-host";
        public const string HotClass = "grip-hot";

        private VisualElement _hot;

        /// <summary>Starts tracking pointer moves on <paramref name="canvasContainer"/>, revealing the deepest grip under the cursor.</summary>
        public GripHoverTracker(VisualElement canvasContainer)
        {
            canvasContainer.RegisterCallback<PointerMoveEvent>(evt => SetHot(ResolveHost(evt.target as VisualElement)));
            canvasContainer.RegisterCallback<PointerLeaveEvent>(_ => SetHot(null));
        }

        /// <summary>
        /// The innermost grip-owning ancestor of the pointer target (the element itself counts).
        /// Public + static so headless tests can drive the resolution directly — synthesizing
        /// pointer events doesn't work there because dispatch re-picks the target by position.
        /// </summary>
        public static VisualElement ResolveHost(VisualElement from)
        {
            for (var v = from; v != null; v = v.parent)
            {
                if (v.ClassListContains(HostClass)) return v;
            }
            return null;
        }

        /// <summary>Pins the "hot" grip to <paramref name="host"/> (clears it when null).</summary>
        public void SetHot(VisualElement host)
        {
            if (host == _hot) return;
            _hot?.RemoveFromClassList(HotClass);
            _hot = host;
            _hot?.AddToClassList(HotClass);
        }
    }
}
