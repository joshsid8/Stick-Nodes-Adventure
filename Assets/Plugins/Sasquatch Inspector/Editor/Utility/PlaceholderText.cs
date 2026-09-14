using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Overlays greyed placeholder/hint text on an empty <see cref="TextField"/> (like an HTML placeholder), so a
    /// blank field reads as "type here" instead of looking like an already-filled value. The hint hides on focus
    /// and whenever the field has text, and returns when the field is left empty. Purely visual — it never touches
    /// the field's value.
    /// </summary>
    public static class PlaceholderText
    {
        private const string PlaceholderClass = "sasq-placeholder-hint";

        /// <summary>Shows greyed <paramref name="hint"/> text over <paramref name="field"/> while it is empty and unfocused.</summary>
        public static void Attach(TextField field, string hint)
        {
            if (field == null || string.IsNullOrEmpty(hint)) return;
            // Guard against a double attach on the same field (e.g. setup re-running on a reused element).
            if (field.Q<Label>(className: PlaceholderClass) != null) return;

            var placeholder = new Label(hint);
            placeholder.AddToClassList(PlaceholderClass);
            placeholder.pickingMode = PickingMode.Ignore;          // clicks fall through to the field
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 3;
            placeholder.style.top = 0;
            placeholder.style.bottom = 0;
            placeholder.style.unityTextAlign = TextAnchor.MiddleLeft;
            placeholder.style.color = DesignerPalette.TextDisabled; // dimmest text = clearly a placeholder
            placeholder.style.fontSize = 11;

            // Overlay onto the field's input box so it aligns with typed text, not the (usually absent) field label.
            var host = field.Q<VisualElement>(className: "unity-base-text-field__input")
                       ?? field.Q<VisualElement>(className: "unity-base-field__input")
                       ?? (VisualElement)field;
            host.Add(placeholder);

            // Focus hides it; leaving it empty brings it back; typing any text hides it. Deleting all text while
            // still focused leaves it hidden (FocusOut restores it).
            field.RegisterCallback<FocusInEvent>(_ => placeholder.style.display = DisplayStyle.None);
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (string.IsNullOrEmpty(field.value)) placeholder.style.display = DisplayStyle.Flex;
            });
            field.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue)) placeholder.style.display = DisplayStyle.None;
            });

            placeholder.style.display = string.IsNullOrEmpty(field.value) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
