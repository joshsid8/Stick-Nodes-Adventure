using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Custom property drawer for <see cref="SceneReference"/> using UI Toolkit.
    /// Renders a SceneAsset picker, a Build Settings check warning, and an "Add" quick-fix button.
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneReference))]
    public class SceneReferenceDrawer : PropertyDrawer
    {
        /// <summary>Builds the <see cref="SceneReference"/> field UI (SceneAsset picker + Build Settings warning + "Add" quick-fix).</summary>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            var sceneAssetProp = property.FindPropertyRelative("sceneAsset");
            var scenePathProp = property.FindPropertyRelative("scenePath");
            var sceneGuidProp = property.FindPropertyRelative("sceneGuid");

            // Self-heal: if the editor SceneAsset link was lost but we still have a GUID, re-resolve
            // it here. This used to live in SceneReference.OnAfterDeserialize, but AssetDatabase calls
            // are illegal during serialization and threw whenever a reference was cleared. The drawer
            // runs outside the serialization pipeline, so touching AssetDatabase is safe.
            // Note: a missing link with an *empty* GUID (i.e. the user just cleared the field) is left
            // alone, so clearing a reference is never "resurrected".
            if (sceneAssetProp.objectReferenceValue == null && !string.IsNullOrEmpty(sceneGuidProp.stringValue))
            {
                string resolvedPath = AssetDatabase.GUIDToAssetPath(sceneGuidProp.stringValue);
                if (!string.IsNullOrEmpty(resolvedPath))
                {
                    var resolvedAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(resolvedPath);
                    if (resolvedAsset != null)
                    {
                        sceneAssetProp.objectReferenceValue = resolvedAsset;
                        sceneAssetProp.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            // 1. Build Settings Warning Box
            var warningBox = new VisualElement();
            warningBox.style.flexDirection = FlexDirection.Row;
            warningBox.style.justifyContent = Justify.SpaceBetween;
            warningBox.style.alignItems = Align.Center;
            warningBox.style.backgroundColor = WidgetPalette.WarningCalloutBg; // theme-aware amber (light/dark)
            warningBox.style.borderTopWidth = 1;
            warningBox.style.borderBottomWidth = 1;
            warningBox.style.borderLeftWidth = 1;
            warningBox.style.borderRightWidth = 1;
            warningBox.style.borderTopColor = WidgetPalette.WarningCalloutBorder;
            warningBox.style.borderBottomColor = WidgetPalette.WarningCalloutBorder;
            warningBox.style.borderLeftColor = WidgetPalette.WarningCalloutBorder;
            warningBox.style.borderRightColor = WidgetPalette.WarningCalloutBorder;
            warningBox.style.borderTopLeftRadius = 3;
            warningBox.style.borderTopRightRadius = 3;
            warningBox.style.borderBottomLeftRadius = 3;
            warningBox.style.borderBottomRightRadius = 3;
            warningBox.style.paddingLeft = 8;
            warningBox.style.paddingRight = 6;
            warningBox.style.paddingTop = 3;
            warningBox.style.paddingBottom = 3;
            warningBox.style.marginBottom = 4;
            warningBox.style.marginTop = 2;

            var warnLabel = new Label("Scene is not in Build Settings");
            warnLabel.style.color = WidgetPalette.WarningCalloutText;
            warnLabel.style.fontSize = 11;
            warningBox.Add(warnLabel);

            var addBtn = new Button();
            addBtn.text = "Add to Build Settings";
            addBtn.tooltip = "Add this scene to Unity's Build Settings";
            addBtn.style.fontSize = 10;
            addBtn.style.paddingLeft = 6;
            addBtn.style.paddingRight = 6;
            addBtn.style.paddingTop = 1;
            addBtn.style.paddingBottom = 1;
            addBtn.style.height = 18;
            addBtn.style.backgroundColor = WidgetPalette.WarningCalloutButtonBg;
            addBtn.style.borderTopColor = WidgetPalette.WarningCalloutButtonBorder;
            addBtn.style.borderBottomColor = WidgetPalette.WarningCalloutButtonBorder;
            addBtn.style.borderLeftColor = WidgetPalette.WarningCalloutButtonBorder;
            addBtn.style.borderRightColor = WidgetPalette.WarningCalloutButtonBorder;
            addBtn.style.color = WidgetPalette.WarningCalloutButtonText;
            addBtn.clicked += () =>
            {
                var path = scenePathProp.stringValue;
                if (!string.IsNullOrEmpty(path))
                {
                    var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                    // Never add a duplicate entry.
                    bool alreadyAdded = false;
                    foreach (var s in scenes)
                    {
                        if (s.path == path) { alreadyAdded = true; break; }
                    }
                    if (!alreadyAdded)
                    {
                        scenes.Add(new EditorBuildSettingsScene(path, true));
                        EditorBuildSettings.scenes = scenes.ToArray();
                    }
                    UpdateWarning(warningBox, path);
                }
            };
            warningBox.Add(addBtn);
            container.Add(warningBox);

            // 2. Object Field picker for SceneAsset
            var objectField = new ObjectField(property.displayName)
            {
                objectType = typeof(SceneAsset),
                bindingPath = sceneAssetProp.propertyPath
            };
            objectField.style.flexGrow = 1f;

            // Handle alignment style if used inside standard aligned layout
            objectField.AddToClassList("unity-base-field__aligned");

            // Update warning visibility initially
            UpdateWarning(warningBox, scenePathProp.stringValue);

            objectField.RegisterValueChangedCallback(evt =>
            {
                var newScene = evt.newValue as SceneAsset;
                string newPath = newScene != null ? AssetDatabase.GetAssetPath(newScene) : "";
                
                // Write back string values immediately to properties
                scenePathProp.stringValue = newPath;
                sceneGuidProp.stringValue = newScene != null ? AssetDatabase.AssetPathToGUID(newPath) : "";
                property.serializedObject.ApplyModifiedProperties();

                UpdateWarning(warningBox, newPath);
            });

            // Track changes in serialized string properties (in case of undo/redo/external sync)
            objectField.TrackPropertyValueWithRefresh(scenePathProp, p =>
            {
                UpdateWarning(warningBox, p.stringValue);
            });

            container.Add(objectField);
            return container;
        }

        private void UpdateWarning(VisualElement warningBox, string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                warningBox.style.display = DisplayStyle.None;
                return;
            }

            // `s.enabled` matters as much as presence: a scene sitting in the Build Settings list with its
            // tickbox OFF is not included in the build, so SceneManager.LoadScene fails on it in the player
            // exactly as if it were never listed. Checking only the path would validate those green in the
            // editor and leave the failure to ship.
            bool inBuild = false;
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == scenePath && s.enabled)
                {
                    inBuild = true;
                    break;
                }
            }

            warningBox.style.display = inBuild ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
