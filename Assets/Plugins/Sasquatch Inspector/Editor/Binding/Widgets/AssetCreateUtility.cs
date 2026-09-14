using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared engine for the per-binding "Create Asset Button" option (<see cref="Runtime.BindingNode.CreateAssetEnabled"/>):
    /// a small Create button shown while a ScriptableObject-reference field is null. Clicking creates a new
    /// asset of the field's declared type (an abstract/base type first offers a picker of its concrete
    /// subclasses), saves it via a project save-file dialog and assigns it to the property. Undoing the
    /// assignment intentionally leaves the created asset on disk (standard Unity behavior — AssetDatabase
    /// operations are not undoable).
    /// </summary>
    internal static class AssetCreateUtility
    {
        internal const string ButtonClass = "sasq-create-asset-btn";

        /// <summary>
        /// True when <paramref name="prop"/> can host the Create button: a single-object edit of an
        /// object-reference field whose declared type is a ScriptableObject subclass with at least one
        /// creatable (concrete, non-generic) type.
        /// </summary>
        internal static bool IsEligible(SerializedProperty prop, out Type fieldType)
        {
            fieldType = null;
            if (prop == null || prop.serializedObject == null) return false;
            if (prop.serializedObject.isEditingMultipleObjects) return false; // one Create can't honestly serve N targets
            if (prop.propertyType != SerializedPropertyType.ObjectReference) return false;

            var target = prop.serializedObject.targetObject;
            if (target == null) return false;

            var fieldInfo = BindingFactory.GetFieldInfoFromPropertyPath(target.GetType(), prop.propertyPath, out var declared);
            if (fieldInfo == null || declared == null) return false;
            if (!typeof(ScriptableObject).IsAssignableFrom(declared)) return false;

            if (GetCreatableTypes(declared).Count == 0) return false;
            fieldType = declared;
            return true;
        }

        /// <summary>
        /// The concrete, non-generic ScriptableObject types assignable to <paramref name="fieldType"/> that the
        /// Create button offers — the type itself (when concrete) plus its TypeCache-derived subclasses.
        /// Unity's own editor/engine ScriptableObjects (editor windows etc.) are filtered out: they are not
        /// sensible user-created assets and would flood the picker for broadly-typed fields.
        /// </summary>
        internal static List<Type> GetCreatableTypes(Type fieldType)
        {
            var result = new List<Type>();
            if (fieldType == null || !typeof(ScriptableObject).IsAssignableFrom(fieldType)) return result;

            bool Creatable(Type t) =>
                !t.IsAbstract && !t.IsGenericTypeDefinition &&
                (t.FullName == null || (!t.FullName.StartsWith("UnityEditor") && !t.FullName.StartsWith("UnityEngine")));

            if (Creatable(fieldType)) result.Add(fieldType);
            foreach (var t in TypeCache.GetTypesDerivedFrom(fieldType))
            {
                if (Creatable(t)) result.Add(t);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        /// <summary>
        /// Builds the Create button for an eligible property. One creatable type goes straight to the save
        /// dialog; several first show a dropdown picker at the button.
        /// </summary>
        internal static Button CreateButton(SerializedProperty prop, Type fieldType)
        {
            var btn = new Button { text = "Create" };
            btn.tooltip = $"Create a new {fieldType.Name} asset and assign it to this field.";
            btn.AddToClassList(ButtonClass);
            btn.style.flexShrink = 0;
            btn.clicked += () =>
            {
                var creatable = GetCreatableTypes(fieldType);
                if (creatable.Count == 0) return;
                if (creatable.Count == 1)
                {
                    PromptCreateAndAssign(prop, creatable[0]);
                }
                else
                {
                    var menu = new GenericMenu();
                    foreach (var t in creatable)
                    {
                        var chosen = t;
                        menu.AddItem(new GUIContent(chosen.Name), false, () => PromptCreateAndAssign(prop, chosen));
                    }
                    menu.DropDown(btn.worldBound);
                }
            };
            EditorSpringFX.ButtonSpring(btn);
            return btn;
        }

        /// <summary>Asks where to save a new <paramref name="type"/> asset, then creates + assigns it. Cancel-safe.</summary>
        internal static void PromptCreateAndAssign(SerializedProperty prop, Type type)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create " + type.Name, type.Name, "asset",
                $"Choose where to save the new {type.Name} asset.");
            if (string.IsNullOrEmpty(path)) return;
            CreateAssetAndAssign(prop, type, path);
        }

        /// <summary>
        /// Dialog-free core (unit-testable): creates the asset at <paramref name="assetPath"/> and assigns it
        /// to <paramref name="prop"/>. ApplyModifiedProperties records the assignment for undo; the asset
        /// itself stays on disk if the user undoes.
        /// </summary>
        internal static UnityEngine.Object CreateAssetAndAssign(SerializedProperty prop, Type type, string assetPath)
        {
            var instance = ScriptableObject.CreateInstance(type);
            instance.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(instance, assetPath);

            prop.serializedObject.Update();
            prop.objectReferenceValue = instance;
            prop.serializedObject.ApplyModifiedProperties();
            return instance;
        }

        /// <summary>Shows the button only while the reference is null (initial state + live via TrackPropertyValue).</summary>
        internal static void AttachVisibility(Button btn, SerializedProperty prop)
        {
            void Sync()
            {
                bool show;
                try { show = prop.objectReferenceValue == null; }
                catch (Exception) { show = false; } // SerializedObject disposed mid-rebuild — hide and move on
                btn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Sync();
            btn.TrackPropertyValueWithRefresh(prop, _ => Sync());
        }
    }
}
