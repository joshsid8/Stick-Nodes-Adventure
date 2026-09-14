using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Property drawer for <see cref="SasquatchString"/> — the plain-inspector fallback, and the thing that
    /// makes field discovery treat the struct as ONE binding instead of drilling into its two serialized
    /// members (<c>SasquatchReflectionUtility.TypeHasCustomDrawer</c>, the same route
    /// <see cref="SceneReference"/> takes).
    ///
    /// Inside a Sasquatch layout the Text display type draws these fields instead, with the full rich-text
    /// toolbar and live preview. This drawer is what a buyer sees on a type they haven't designed a layout
    /// for yet, so it stays deliberately plain: the text, plus the resolved result underneath.
    /// </summary>
    [CustomPropertyDrawer(typeof(SasquatchString))]
    public class SasquatchStringDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();
            var templateProp = property.FindPropertyRelative("template");
            if (templateProp == null) return container;

            // ALWAYS multiline, and deliberately not driven by [TextArea]/[Multiline]. Unity resolves an
            // ATTRIBUTE drawer ahead of a TYPE drawer, so a [TextArea] on a SasquatchString is handled by
            // Unity's own TextAreaDrawer — which only supports string properties, renders "Use TextAreaDrawer
            // with string." instead of a field, and logs on every height query. This drawer never runs in that
            // case, so reading the attribute here would be dead code that implies support we cannot give.
            // Templates are multi-line by nature, so there is no reason to ask for it in the first place.
            var field = new TextField(property.displayName) { multiline = true };
            field.style.minHeight = 54f;
            field.tooltip = "Text with {fieldName} tokens. The tokens resolve to live field values wherever "
                          + "this string is used — including TextMeshPro at runtime.";
            field.BindProperty(templateProp);
            container.Add(field);

            // NO resolved preview here, deliberately. This drawer is the plain-inspector fallback and should
            // be exactly a text box — the live preview is a Text-display-type feature the user opts into with
            // Rich Text Preview, not something Default imposes. Keeping it out also means this drawer holds no
            // trackers, so nothing polls or re-resolves on a field the user only wanted to type into.
            // RegisterUserValueChanged, NOT RegisterValueChangedCallback: a BOUND field is handed a ChangeEvent
            // the moment it attaches to a panel, so the plain callback fired this bake on every build — a write
            // performed by merely displaying the object. See BindingFactory.RegisterUserValueChanged.
            BindingFactory.RegisterUserValueChanged<string>(field,
                _ => BindingFactory.BakeSasquatchStringSource(property, onlyWhenUnset: false));

            // NO owner bake here, deliberately. A template that has text but no owner (assigned from code, or
            // predating the field becoming a SasquatchString) would resolve better bound — but binding it at
            // BUILD time is a SerializedProperty write performed by merely displaying the object: on a prefab
            // instance it invents an override arrow nobody made, and on a plain object it dirties the scene on
            // selection. The bake rides the edit callback above instead, and code-built instances have
            // SasquatchString.Bind(owner) for exactly this.
            return container;
        }
    }
}
