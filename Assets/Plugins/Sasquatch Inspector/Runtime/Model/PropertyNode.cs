using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A layout node that displays a non-serialized C# PROPERTY (getter), reflected by name (computed
    /// values, derived state). It is the sibling of <see cref="ButtonNode"/> (which reflects a
    /// METHOD by name): there is no <see cref="UnityEditor.SerializedProperty"/> behind it, so the value is
    /// read via reflection and polled live. If the property has a usable setter and is a simple type, the
    /// rendered control is editable and writes back through the setter.
    /// </summary>
    [Serializable]
    public class PropertyNode : LayoutNode
    {
        [SerializeField] private string propertyName;
        [SerializeField] private string labelOverride;
        [SerializeField] private bool hideLabel;
        [SerializeField] private bool forceReadOnly;
        [SerializeField] private TextStyle labelTextStyle;
        [SerializeField] private TextStyle valueTextStyle;

        /// <summary>Name of the C# property to reflect and display on the target object.</summary>
        public string PropertyName
        {
            get => propertyName;
            set => propertyName = value;
        }

        /// <summary>Optional label text; falls back to a nicified property name when null/empty.</summary>
        public string LabelOverride
        {
            get => labelOverride;
            set => labelOverride = value;
        }

        /// <summary>When <c>true</c>, the label column is hidden and the value spans the row.</summary>
        public bool HideLabel
        {
            get => hideLabel;
            set => hideLabel = value;
        }

        /// <summary>When <c>true</c>, the value is shown read-only even if the property has a usable setter.</summary>
        public bool ForceReadOnly
        {
            get => forceReadOnly;
            set => forceReadOnly = value;
        }

        /// <summary>Text styling for the label.</summary>
        public TextStyle LabelTextStyle
        {
            get => labelTextStyle;
            set => labelTextStyle = value;
        }

        /// <summary>Text styling for the displayed value.</summary>
        public TextStyle ValueTextStyle
        {
            get => valueTextStyle;
            set => valueTextStyle = value;
        }
    }
}
