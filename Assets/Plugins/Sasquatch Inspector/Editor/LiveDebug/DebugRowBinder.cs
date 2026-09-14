using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// Wraps a built field so it carries a Live Debug graph row underneath it.
    ///
    /// <para>Deliberately shaped as a near-twin of <c>ValidationBinder.Attach</c>: take the field, put it in a
    /// column wrapper, move the field's layout onto the wrapper, add a second row beneath, return the wrapper.
    /// Because <c>BuildNode</c> stamps <c>userData</c>, the node registries, conditions, animations and the
    /// design card onto <b>whatever element the branch ended up with</b>, adopting that same shape means the
    /// graph is hidden with its field by a condition or a search filter for free, with no new wiring.</para>
    ///
    /// <para>It also means BOTH render paths get this at once — <c>VisualInspectorEditor</c> (fallback) and
    /// <c>SasquatchForceTakeover</c> (displacement) share <c>LayoutTreeBuilder.Build</c>, so anything attached
    /// below it is inherited by both. That is the same reason the <c>IsInPlayMode</c> driver needed no parity
    /// work, and it is why this feature has no parity risk despite touching every field.</para>
    /// </summary>
    internal static class DebugRowBinder
    {
        /// <summary>
        /// Build-scope switch: true while building a layout whose Script Header has the debug toggle enabled.
        /// Saved and restored by <c>LayoutTreeBuilder.Build</c> alongside its other per-build statics — a
        /// nested build (in-line editor, embedded editor) must not inherit the outer layout's answer.
        /// </summary>
        internal static bool EnabledForBuild;

        /// <summary>Graphs created during the current build, handed to <see cref="DebugRunner"/> when it finishes.</summary>
        internal static readonly List<DebugGraphElement> BuiltGraphs = new List<DebugGraphElement>();

        /// <summary>True when this layout opts into Live Debug — i.e. its Script Header shows the toggle.</summary>
        internal static bool LayoutWantsDebug(LayoutNode root)
        {
            if (root == null) return false;
            bool found = false;

            // Depth-bounded because a layout graph is user data. The cyclic-graph guard has already run by the
            // time any builder walks the tree, but a walker that can't terminate on its own is a crash waiting
            // for the one layout that slips past it.
            void Walk(LayoutNode n, int depth)
            {
                if (found || n == null || depth > 64) return;
                // A hidden Script Header is not built, and the bug button ON that header is the ONLY writer of
                // DebugModeState. Answering "yes" for one therefore arms the whole feature — graph rows, the
                // recorder, the transport bar, the tooltip mute — with no control anywhere in the editor left
                // to turn it off again, and DebugModeState lives in SessionState, so it survives until a
                // restart. `return`, not `continue`: a hidden container takes its subtree with it, exactly as
                // BuildNode's skip does.
                if (n.HiddenInInspector) return;
                if (n is ScriptHeaderNode sh && sh.ShowDebugToggle) { found = true; return; }
                if (n is ContainerNode c && c.Children != null)
                {
                    for (int i = 0; i < c.Children.Count && !found; i++) Walk(c.Children[i], depth + 1);
                }
            }

            Walk(root, 0);
            return found;
        }

        /// <summary>
        /// Returns the element the caller should insert in place of <paramref name="fieldElement"/> — the field
        /// wrapped with a graph row, or the field unchanged when debug isn't applicable.
        /// </summary>
        internal static VisualElement Attach(VisualElement fieldElement, LayoutNode node, string resolvedPath,
            SerializedObject serializedObject, bool designMode, bool isTableCell)
        {
            // A table/column cell has no room for a second row, and wrapping one breaks the cell/field identity
            // the column-fit measurement depends on — the same exclusion validation makes, for the same reason.
            if (!EnabledForBuild || designMode || isTableCell) return fieldElement;
            if (fieldElement == null || string.IsNullOrEmpty(resolvedPath) || serializedObject == null) return fieldElement;

            var recorder = DebugRecorder.For(serializedObject);
            if (recorder == null) return fieldElement;

            var binding = node as BindingNode;
            bool hasRange = false;
            float rangeMin = 0f, rangeMax = 1f;
            if (binding != null && binding.RangeMin.isOverridden && binding.RangeMax.isOverridden)
            {
                // An authored range is a statement about what this number MEANS, so the graph honours it rather
                // than auto-framing: a health bar that never drops below 40 should still read against 0..100.
                hasRange = true;
                rangeMin = binding.RangeMin.value;
                rangeMax = binding.RangeMax.value;
            }

            string label = binding?.EffectiveLabelOverride;
            if (string.IsNullOrEmpty(label)) label = NiceName(resolvedPath);

            var channel = recorder.Register(resolvedPath, label, hasRange, rangeMin, rangeMax);
            if (channel == null) return fieldElement;

            var container = new VisualElement();
            container.AddToClassList("sasq-debug-wrapper");
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignSelf = Align.Stretch;
            container.style.minWidth = 0;
            container.style.flexShrink = 1f;

            // Carry the field's layout onto the wrapper so the row sits exactly where the field used to, then
            // let the field fill the wrapper. Verbatim from ValidationBinder — the two wrappers can nest, and
            // they must agree on this or the second one re-introduces the margins the first just moved.
            container.style.marginTop = fieldElement.style.marginTop;
            container.style.marginBottom = fieldElement.style.marginBottom;
            container.style.marginLeft = fieldElement.style.marginLeft;
            container.style.marginRight = fieldElement.style.marginRight;
            container.style.flexGrow = fieldElement.style.flexGrow;
            container.style.flexShrink = fieldElement.style.flexShrink;
            container.style.width = fieldElement.style.width;
            container.style.height = fieldElement.style.height;
            container.style.alignSelf = fieldElement.style.alignSelf;

            fieldElement.style.marginTop = 0;
            fieldElement.style.marginBottom = 0;
            fieldElement.style.marginLeft = 0;
            fieldElement.style.marginRight = 0;
            fieldElement.style.flexGrow = 1f;
            fieldElement.style.flexShrink = 1f;
            fieldElement.style.minWidth = 0;
            fieldElement.style.alignSelf = Align.Stretch;

            container.Add(fieldElement);

            var graph = new DebugGraphElement(recorder, channel);
            // Hidden until recording is switched on. Writing display directly is safe here because this row is
            // NOT a layout-node element — ElementVisibility owns display on those, and it targets the wrapper.
            graph.style.display = DisplayStyle.None;
            container.Add(graph);

            BuiltGraphs.Add(graph);
            return container;
        }

        /// <summary>"currentHealth" from "stats.currentHealth" — the label shown in the moment card's co-change list.</summary>
        private static string NiceName(string path)
        {
            int dot = path.LastIndexOf('.');
            string leaf = dot >= 0 && dot < path.Length - 1 ? path.Substring(dot + 1) : path;
            return ObjectNames.NicifyVariableName(leaf);
        }
    }
}
