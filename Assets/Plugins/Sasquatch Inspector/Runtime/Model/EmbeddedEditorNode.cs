using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Re-inlines a type's OWN custom <c>[CustomEditor(typeof(T))]</c> visuals inside a Sasquatch Inspector layout — the
    /// whole editor, or a single tagged "slice" (a <see cref="SasquatchEmbedAttribute"/> method, e.g. just a graph).
    /// The marquee use is with force-takeover: displace the type's editor, then re-embed only the part you liked.
    ///
    /// A plain leaf (like <see cref="SpaceNode"/>) — NOT a <see cref="DisplayType"/>, because it has no
    /// <c>SerializedProperty</c> (a fake path would poison the applicability / multi-edit / fuzzer invariants). The
    /// editor Type is re-resolved at build time from the layout's target type (so it self-heals across renames).
    /// </summary>
    [Serializable]
    public class EmbeddedEditorNode : LayoutNode
    {
        // "" = the whole editor; otherwise the name of a [SasquatchEmbed] method on the resolved editor type.
        [SerializeField] private string sliceMethodName = "";

        /// <summary>Which slice to embed: empty ⇒ the entire editor; otherwise a <see cref="SasquatchEmbedAttribute"/>
        /// method name on the type's genuine editor.</summary>
        public string SliceMethodName
        {
            get => sliceMethodName;
            set => sliceMethodName = value;
        }
    }
}
