using System.Runtime.CompilerServices;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Element → LayoutNode mapping that nothing can clobber. LayoutTreeBuilder also stamps
    /// element.userData = node (the designer's drag/drop and grid systems read it), but userData
    /// is contested real estate: Unity's own controls and binding machinery may overwrite it on
    /// LIVE elements after build — which silently removed binding leaves from the quick menu's
    /// ancestor chain. The unified right-click menu therefore resolves nodes through this
    /// registry FIRST and falls back to userData. ConditionalWeakTable keys weakly, so rebuilt
    /// trees drop their entries with the old elements.
    /// </summary>
    public static class LayoutNodeRegistry
    {
        private static readonly ConditionalWeakTable<VisualElement, LayoutNode> Map =
            new ConditionalWeakTable<VisualElement, LayoutNode>();

        /// <summary>Maps <paramref name="element"/> to its <paramref name="node"/> (replacing any prior mapping).</summary>
        public static void SetNode(VisualElement element, LayoutNode node)
        {
            if (element == null) return;
            Map.Remove(element); // CWT.Add throws on duplicate keys
            if (node != null) Map.Add(element, node);
        }

        /// <summary>The <see cref="LayoutNode"/> mapped to <paramref name="element"/>, or null.</summary>
        public static LayoutNode GetNode(VisualElement element)
        {
            if (element == null) return null;
            return Map.TryGetValue(element, out var node) ? node : null;
        }
    }
}
