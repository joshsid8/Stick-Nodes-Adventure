using System;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Marks a zero-parameter instance method on a custom <c>UnityEditor.Editor</c> as an embeddable "slice" — a
    /// self-contained piece of that editor's visuals (e.g. just a dash-direction graph) that an
    /// <c>EmbeddedEditorNode</c> can re-inline inside a Sasquatch Inspector layout without hosting the whole inspector.
    /// The method must return a <c>UnityEngine.UIElements.VisualElement</c> (hosted + bound) or be <c>void</c>
    /// (wrapped in an IMGUIContainer that invokes it on the live editor instance). Lives in the Runtime assembly so
    /// user Editor code (which references Runtime) can apply it; it is discovered via TypeCache reflection over
    /// USER code only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class SasquatchEmbedAttribute : Attribute
    {
        /// <summary>Optional friendly name shown in the composer's slice dropdown (defaults to the method name).</summary>
        public string DisplayName { get; }

        public SasquatchEmbedAttribute(string displayName = null)
        {
            DisplayName = displayName;
        }
    }
}
