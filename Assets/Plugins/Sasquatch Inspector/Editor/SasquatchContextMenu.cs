using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Adds an "Edit Sasquatch Layout" entry to a component's / ScriptableObject's context menu.
    /// In Unity the right-click header menu and the ⋮ (3-dots) header menu are the same menu, so this
    /// one set of CONTEXT/ items covers both. There is no always-on toolbar button: the inspector
    /// renders normally until the user deliberately opens the designer from here.
    /// </summary>
    public static class SasquatchContextMenu
    {
        private const string ComponentItem = "CONTEXT/Component/Edit Sasquatch Layout";
        private const string ScriptableObjectItem = "CONTEXT/ScriptableObject/Edit Sasquatch Layout";
        private const string ComponentRemoveItem = "CONTEXT/Component/Remove Sasquatch Layout";
        private const string ScriptableObjectRemoveItem = "CONTEXT/ScriptableObject/Remove Sasquatch Layout";

        [MenuItem(ComponentItem, false, 2000)]
        private static void EditComponentLayout(MenuCommand command) => Open(command.context);

        [MenuItem(ComponentItem, true, 2000)]
        private static bool ValidateEditComponentLayout(MenuCommand command) => IsEditable(command.context);

        [MenuItem(ScriptableObjectItem, false, 2000)]
        private static void EditScriptableObjectLayout(MenuCommand command) => Open(command.context);

        [MenuItem(ScriptableObjectItem, true, 2000)]
        private static bool ValidateEditScriptableObjectLayout(MenuCommand command) => IsEditable(command.context);

        // Recovery path: drop a type's layout so its inspector reverts to Unity's stock inspector.
        // Enabled only when a layout actually exists, so it's a safe escape hatch if a layout breaks.
        [MenuItem(ComponentRemoveItem, false, 2001)]
        private static void RemoveComponentLayout(MenuCommand command) => Remove(command.context);

        [MenuItem(ComponentRemoveItem, true, 2001)]
        private static bool ValidateRemoveComponentLayout(MenuCommand command) => HasLayout(command.context);

        [MenuItem(ScriptableObjectRemoveItem, false, 2001)]
        private static void RemoveScriptableObjectLayout(MenuCommand command) => Remove(command.context);

        [MenuItem(ScriptableObjectRemoveItem, true, 2001)]
        private static bool ValidateScriptableObjectRemoveLayout(MenuCommand command) => HasLayout(command.context);

        private static void Open(Object context)
        {
            if (context == null) return;
            SasquatchLayoutDesignerWindow.OpenWindowForType(context.GetType(), context);
        }

        private static void Remove(Object context)
        {
            if (context == null) return;
            var type = context.GetType();
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Sasquatch Layout",
                $"Remove the Sasquatch layout for {type.Name}?\n\nIts inspector will revert to Unity's default. This can't be undone.",
                "Remove", "Cancel");
            if (!confirmed) return;

            LayoutRegistry.Remove(type);
            // Rebuild live inspectors so the reverted object shows Unity's default immediately.
            ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        private static bool HasLayout(Object context)
        {
            // Own layouts only: "Remove layout" must not be offered for a layout merely INHERITED from an
            // ancestor — Remove(type) would find no entry for this type and silently no-op after the
            // confirm dialog. (Removing the ancestor's layout is done on the ancestor itself.)
            return IsEditable(context) && LayoutRegistry.TryGetOwn(context.GetType(), out _);
        }

        // Hide the entry for Sasquatch Inspector's OWN internal objects — the layout/theme/database assets in
        // SasquatchInspector.Runtime and the editor windows/editors in SasquatchInspector.Editor — so you don't try to
        // design a layout for a layout asset. Must NOT exclude the whole "SasquatchInspector" prefix: the
        // user-facing samples live in SasquatchInspector.Samples and ARE legitimate objects to design layouts for.
        /// <summary>True when <paramref name="type"/> can have a Sasquatch Inspector layout — a user MonoBehaviour/ScriptableObject (excludes Sasquatch Inspector's own internal assets).</summary>
        public static bool IsTypeEditable(System.Type type)
        {
            if (type == null) return false;

            // Must inherit from MonoBehaviour or ScriptableObject
            if (!typeof(MonoBehaviour).IsAssignableFrom(type) && !typeof(ScriptableObject).IsAssignableFrom(type))
                return false;

            var fullName = type.FullName;
            if (string.IsNullOrEmpty(fullName)) return true;

            // Hide the entry for Sasquatch Inspector's OWN internal objects
            if (fullName.StartsWith("SasquatchInspector.Runtime") || fullName.StartsWith("SasquatchInspector.Editor"))
                return false;

            // Must not be a built-in Unity component
            if (fullName.StartsWith("UnityEngine") || fullName.StartsWith("UnityEditor"))
            {
                // Except if it is in the SasquatchInspector.Samples namespace
                if (fullName.StartsWith("SasquatchInspector.Samples"))
                {
                    return true;
                }
                return false;
            }

            return true;
        }

        private static bool IsEditable(Object context)
        {
            if (context == null) return false;
            return IsTypeEditable(context.GetType());
        }

        /// <summary>
        /// Every type in the project that can have a Sasquatch layout — the union of all MonoBehaviour and
        /// ScriptableObject types, filtered through <see cref="IsTypeEditable"/>. Sorted by friendly name.
        /// Backs the blank-state "pick a script" picker (the Tools ▸ Layout Designer entry point), where there's
        /// no selected object to derive a target from. Abstract types are KEPT — an abstract base is a valid
        /// layout target (authored once, inherited by subclasses), matching the type-switcher's ancestor climb;
        /// open generic definitions are dropped (no concrete inspector, and their <c>MonoScript.GetClass</c> is null).
        /// </summary>
        internal static IEnumerable<System.Type> EnumerateEditableTypes()
        {
            return TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .Concat(TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                .Where(t => !t.IsGenericTypeDefinition && IsTypeEditable(t))
                .Distinct()
                .OrderBy(t => SasquatchReflectionUtility.GetFriendlyTypeName(t), System.StringComparer.OrdinalIgnoreCase);
        }
    }
}
