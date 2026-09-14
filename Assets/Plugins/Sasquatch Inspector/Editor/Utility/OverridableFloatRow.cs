using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The numeric sibling of <see cref="OverridableTextRow"/>: a <see cref="FloatField"/> that shows the code
    /// default as a real (normal-weight) value when unset, bold when overridden, and a ↺ reset button that
    /// restores the code default (clearing the override). Used by the designer's option panels and the inspector
    /// quick-edit popup so an <see cref="OverridableFloat"/> option reads the same "untouched vs. set" way as an
    /// overridable text field.
    ///
    /// The <paramref name="commit"/> delegate receives the new <see cref="OverridableFloat"/> (overridden with the
    /// typed value, or unset at the code default) and owns undo recording, persistence, and any refresh — this row
    /// only manages its own display state (bold weight + reset-button visibility).
    /// </summary>
    public static class OverridableFloatRow
    {
        /// <summary>
        /// Builds an overridable-float row: a field pre-filled with <paramref name="current"/> (or showing
        /// <paramref name="codeDefault"/> when unset), plus a reset button that commits an unset value.
        /// </summary>
        /// <param name="label">The field label.</param>
        /// <param name="current">The current stored value (its <c>isOverridden</c> flag drives the chrome).</param>
        /// <param name="codeDefault">The value shown (and reset to) when the option is not overridden.</param>
        /// <param name="commit">Receives the new stored value; owns undo/persist/refresh.</param>
        /// <param name="fieldTooltip">Optional tooltip for the field.</param>
        public static VisualElement Create(string label, OverridableFloat current, float codeDefault,
            Action<OverridableFloat> commit, string fieldTooltip = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            // Delayed so a value is only committed on Enter/blur — never on the partial keystrokes of typing "-0.5".
            var field = new FloatField(label) { isDelayed = true };
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            if (!string.IsNullOrEmpty(fieldTooltip)) field.tooltip = fieldTooltip;

            var reset = OverridableTextRow.CreateResetButton("Reset to the script default");

            // The one piece of state this row tracks locally: the override flag (the field itself holds the value).
            OverridableFloat stored = current;

            // Sync the bold (override) weight and the reset-arrow visibility to the stored state. Never rewrites the
            // field text here (that would jump the caret mid-typing); the display value is seeded/reset explicitly.
            void UpdateChrome()
            {
                bool isOverride = stored.isOverridden;
                field.style.unityFontStyleAndWeight = isOverride ? FontStyle.Bold : FontStyle.Normal;
                reset.style.display = isOverride ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Any edit marks the option overridden at the typed value — there is no "typed back to the
            // default ⇒ unset" gesture for numbers (the ↺ button is the way to unset).
            field.RegisterValueChangedCallback(evt =>
            {
                stored = new OverridableFloat { isOverridden = true, value = evt.newValue };
                commit(stored);
                UpdateChrome();
            });

            // Reset clears the override and shows the code default again as a plain (editable) value.
            reset.clicked += () =>
            {
                stored = new OverridableFloat { isOverridden = false, value = codeDefault };
                commit(stored);
                field.SetValueWithoutNotify(codeDefault);
                UpdateChrome();
            };

            field.SetValueWithoutNotify(stored.isOverridden ? stored.value : codeDefault);
            UpdateChrome();

            row.Add(field);
            row.Add(reset);
            return row;
        }
    }
}
