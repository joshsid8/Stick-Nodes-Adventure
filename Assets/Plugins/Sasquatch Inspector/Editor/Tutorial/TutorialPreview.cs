using System.Collections.Generic;
using SasquatchInspector.Runtime;
using SasquatchInspector.Samples;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// A live, in-memory inspector preview for the Tutorial. It builds a transient <see cref="PlayerStats"/> plus
    /// a transient <see cref="InspectorLayoutAsset"/> and renders them through <see cref="LayoutTreeBuilder"/>,
    /// tracking every transient object so the window can destroy them on step change / close. Mirrors the Showcase
    /// Sandbox's transient-object recipe (<see cref="HideFlags.DontSave"/> + <c>DestroyImmediate</c>).
    /// </summary>
    internal sealed class TutorialPreview
    {
        /// <summary>The built UI Toolkit tree to drop into the window.</summary>
        public readonly VisualElement Element;

        private readonly List<UnityEngine.Object> _owned;

        private TutorialPreview(VisualElement element, List<UnityEngine.Object> owned)
        {
            Element = element;
            _owned = owned;
        }

        /// <summary>Builds a preview of a sample <see cref="PlayerStats"/> rendered with <paramref name="root"/>.</summary>
        public static TutorialPreview Build(LayoutNode root)
        {
            var owned = new List<UnityEngine.Object>();

            var target = ScriptableObject.CreateInstance<PlayerStats>();
            target.hideFlags = HideFlags.DontSave;
            owned.Add(target);

            var layout = ScriptableObject.CreateInstance<InspectorLayoutAsset>();
            layout.hideFlags = HideFlags.DontSave;
            layout.Root = root;
            layout.TargetTypeName = typeof(PlayerStats).AssemblyQualifiedName;
            if (ScriptGuidUtility.TryGetGuid(target, out string guid)) layout.ScriptGuid = guid;
            owned.Add(layout);

            var so = new SerializedObject(target);
            // The preview is a teaching aid: no structural rebuild callback is needed (PlayerStats has no
            // collections), and individual field edits update themselves via UI Toolkit binding.
            var view = LayoutTreeBuilder.Build(layout, so, () => { }, playAppearAnimations: true);
            return new TutorialPreview(view, owned);
        }

        /// <summary>Destroys every transient object this preview created.</summary>
        public void Dispose()
        {
            foreach (var o in _owned)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _owned.Clear();
        }
    }
}
