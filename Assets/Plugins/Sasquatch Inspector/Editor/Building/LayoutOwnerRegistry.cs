using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The layout asset + SerializedObject a built node element belongs to. For a root node this is the
    /// registered layout and the inspected object's SerializedObject; for a node rendered INSIDE an inline
    /// editor (a BindingNode with DisplayType.InlineEditor embeds another object's inspector built from a
    /// SEPARATE layout asset — see BindingFactory.BuildNestedInlineLayout) it is that nested layout and the
    /// nested SerializedObject. The quick-edit menu reads it to route each edit's undo / persistence /
    /// target resolution to the asset that actually OWNS the node, instead of always the root (which
    /// silently dropped nested entries and would lose nested edits on domain reload).
    /// </summary>
    public class LayoutOwnerScope
    {
        public readonly InspectorLayoutAsset LayoutAsset;
        public readonly SerializedObject SerializedObject;

        /// <summary>Pairs a layout asset with the <see cref="SerializedObject"/> it is bound to.</summary>
        public LayoutOwnerScope(InspectorLayoutAsset layoutAsset, SerializedObject serializedObject)
        {
            LayoutAsset = layoutAsset;
            SerializedObject = serializedObject;
        }

        public UnityEngine.Object Target => SerializedObject != null ? SerializedObject.targetObject : null;
    }

    /// <summary>
    /// Element → owning <see cref="LayoutOwnerScope"/> mapping, stamped by LayoutTreeBuilder right next to
    /// LayoutNodeRegistry.SetNode. Mirrors <see cref="LayoutNodeRegistry"/>: a ConditionalWeakTable keys
    /// weakly, so rebuilt trees drop their entries with the old elements, and nothing Unity does to
    /// userData can clobber it.
    /// </summary>
    public static class LayoutOwnerRegistry
    {
        private static readonly ConditionalWeakTable<VisualElement, LayoutOwnerScope> Map =
            new ConditionalWeakTable<VisualElement, LayoutOwnerScope>();

        /// <summary>Maps <paramref name="element"/> to its owning <see cref="LayoutOwnerScope"/>.</summary>
        public static void SetScope(VisualElement element, LayoutOwnerScope scope)
        {
            if (element == null) return;
            Map.Remove(element); // CWT.Add throws on duplicate keys
            if (scope != null) Map.Add(element, scope);
        }

        /// <summary>The <see cref="LayoutOwnerScope"/> mapped to <paramref name="element"/>, or null.</summary>
        public static LayoutOwnerScope GetScope(VisualElement element)
        {
            if (element == null) return null;
            return Map.TryGetValue(element, out var scope) ? scope : null;
        }

        /// <summary>
        /// The nearest stamped scope at or above <paramref name="element"/> — a robust fallback for
        /// elements that aren't the exact node-root the scope was stamped on.
        /// </summary>
        public static LayoutOwnerScope ResolveScope(VisualElement element)
        {
            for (var el = element; el != null; el = el.parent)
            {
                var scope = GetScope(el);
                if (scope != null) return scope;
            }
            return null;
        }
    }
}
