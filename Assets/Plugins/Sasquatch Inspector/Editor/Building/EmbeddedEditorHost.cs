using System;
using System.Collections.Generic;
using System.Reflection;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Hosts a type's OWN custom editor (whole, or a <see cref="SasquatchEmbedAttribute"/> slice) inside a
    /// Sasquatch Inspector layout for <see cref="EmbeddedEditorNode"/>. Uses the exact editor type from
    /// <see cref="SasquatchGeneratedEditors.GetGenuinePerTypeEditor"/>, <see cref="Editor.CreateEditor(UnityEngine.Object[], Type)"/>,
    /// Unity's own <see cref="InspectorElement"/> to host the WHOLE editor, and <c>CreateInspectorGUI()</c> /
    /// a <c>[SasquatchEmbed]</c> method for a SLICE.
    ///
    /// The whole-editor case hosts through <see cref="InspectorElement"/> rather than a bare
    /// <see cref="IMGUIContainer"/> on purpose: a bare container does not reproduce the inspector GUI context Unity
    /// pins across the IMGUI layout+repaint passes (<c>hierarchyMode</c> / <c>wideMode</c> / context width), so a
    /// variable-height field (e.g. a Vector2 that draws one row when wide, two when narrow) desyncs the cached hit
    /// rects from what is painted — and every control BELOW it (a button far down a tall editor) becomes
    /// mis-clickable, its hit zone drifting down off the glyph. InspectorElement is what the real Inspector uses, so
    /// it stays correct at any height.
    ///
    /// Nesting an <see cref="InspectorElement"/> here is safe: the host container carries the
    /// <see cref="ContainerClass"/>, and the header-pencil scan DROPS any InspectorElement under it (see
    /// <c>SasquatchInspectorHeaderPencil.HasEmbeddedEditorAncestor</c>) — so our own scan never pencils, displaces,
    /// or shifts tracker correlation for the embedded editor, and any InspectorElement a user editor internally
    /// spawns is skipped too. InspectorElement does not own the editor we pass, so we still destroy it on detach.
    /// </summary>
    internal static class EmbeddedEditorHost
    {
        public const string ContainerClass = "sasq-embedded-editor";

        // Target instance ids whose embed is mid-build, to break a cycle (an editor that embeds its own target).
        private static readonly HashSet<int> _inFlight = new HashSet<int>();

        public static VisualElement Build(EmbeddedEditorNode node, SerializedObject serializedObject, bool designMode)
        {
            var target = serializedObject != null ? serializedObject.targetObject : null;
            if (target == null) return Info("Embedded Editor: no target object to embed.");

            var targetType = target.GetType();
            var genuineType = SasquatchGeneratedEditors.GetGenuinePerTypeEditor(targetType);
            if (genuineType == null)
                return Info($"Embedded Editor: no [CustomEditor(typeof({targetType.Name}))] found. This node re-inlines a type's own editor; the type has none.");

            var targets = serializedObject.targetObjects;
            if (targets != null && targets.Length > 1 && !Attribute.IsDefined(genuineType, typeof(CanEditMultipleObjects)))
                return Info($"Embedded Editor: {genuineType.Name} lacks [CanEditMultipleObjects]; can't embed for a multi-selection.");

            int id = UnityObjectId.Of(target);
            if (!_inFlight.Add(id))
                return Info("Embedded Editor: skipped (an outer embed of the same object is already open — circular).");

            var container = new VisualElement();
            container.AddToClassList(ContainerClass);
            container.style.flexGrow = 1;
            container.style.minWidth = 0;

            UnityEditor.Editor ed = null;
            try
            {
                ed = UnityEditor.Editor.CreateEditor(targets, genuineType);
                if (ed == null) { return Info("Embedded Editor: failed to create the editor instance."); }

                var content = string.IsNullOrEmpty(node.SliceMethodName)
                    ? BuildWhole(ed)
                    : BuildSlice(ed, genuineType, node.SliceMethodName);
                container.Add(content);
            }
            catch (Exception e)
            {
                container.Add(Info("Embedded Editor: build failed — " + e.Message));
            }
            finally
            {
                _inFlight.Remove(id);
            }

            // Lifecycle: destroy the second editor instance when the container is torn down for good.
            // Idempotent. Deliberately NOT on any detach — a re-parent (window tab drag, collection row
            // reorder) re-attaches this container with its InspectorElement still pointing at the editor, so
            // destroying it there left a dead embed. See PanelLifetime.
            var capturedEd = ed;
            bool destroyed = false;
            PanelLifetime.OnRealDetach(container, () =>
            {
                if (destroyed) return;
                destroyed = true;
                if (capturedEd != null) UnityEngine.Object.DestroyImmediate(capturedEd);
            });

            return container;
        }

        private static VisualElement BuildWhole(UnityEditor.Editor ed)
        {
            // Host the type's WHOLE editor the way the real Inspector does — via InspectorElement — not a bare
            // IMGUIContainer. This transparently handles both editor shapes (UITK CreateInspectorGUI and IMGUI
            // OnInspectorGUI), binds itself, and — crucially — pins the inspector GUI context across the IMGUI
            // layout+repaint passes, so controls low in a TALL editor stay correctly clickable (a bare container
            // drifts their hit rects down; see the class remarks). Safe to nest: the scan skips any InspectorElement
            // under our ContainerClass, and InspectorElement doesn't destroy the editor we created — Build() does, on detach.
            var inspector = new InspectorElement(ed);
            inspector.style.flexGrow = 1;
            inspector.style.minWidth = 0;
            return inspector;
        }

        private static VisualElement BuildSlice(UnityEditor.Editor ed, Type genuineType, string methodName)
        {
            var method = FindSliceMethod(genuineType, methodName);
            if (method == null)
                return Info($"Embedded Editor: slice '{methodName}' not found (needs a [SasquatchEmbed] zero-arg method on {genuineType.Name}).");
            if (method.GetParameters().Length != 0)
                return Info($"Embedded Editor: slice '{methodName}' must take no parameters.");

            if (typeof(VisualElement).IsAssignableFrom(method.ReturnType))
            {
                object result;
                try { result = method.Invoke(ed, null); }
                catch (Exception e) { return Info("Embedded Editor: slice threw — " + (e.InnerException ?? e).Message); }
                if (result is VisualElement ve)
                {
                    var host = new VisualElement();
                    host.style.flexGrow = 1;
                    host.style.minWidth = 0;
                    host.Add(ve);
                    host.Bind(ed.serializedObject);
                    return host;
                }
                return Info($"Embedded Editor: slice '{methodName}' returned null.");
            }

            if (method.ReturnType == typeof(void))
            {
                var imgui = new IMGUIContainer(() => { try { method.Invoke(ed, null); } catch { } });
                imgui.style.flexGrow = 1;
                imgui.style.minWidth = 0;
                return imgui;
            }

            return Info($"Embedded Editor: slice '{methodName}' must return a VisualElement or be void.");
        }

        /// <summary>Find a zero-arg [SasquatchEmbed] method by name on the genuine editor type (inherited allowed).</summary>
        public static MethodInfo FindSliceMethod(Type genuineType, string methodName)
        {
            if (genuineType == null || string.IsNullOrEmpty(methodName)) return null;
            foreach (var m in TypeCache.GetMethodsWithAttribute<SasquatchEmbedAttribute>())
                if (m.DeclaringType != null && m.DeclaringType.IsAssignableFrom(genuineType) &&
                    m.Name == methodName && m.GetParameters().Length == 0)
                    return m;
            return null;
        }

        /// <summary>The embeddable slices declared on a type's genuine editor: (methodName, displayName) pairs.</summary>
        public static List<(string method, string display)> DiscoverSlices(Type genuineType)
        {
            var list = new List<(string, string)>();
            if (genuineType == null) return list;
            foreach (var m in TypeCache.GetMethodsWithAttribute<SasquatchEmbedAttribute>())
            {
                if (m.DeclaringType == null || !m.DeclaringType.IsAssignableFrom(genuineType)) continue;
                if (m.GetParameters().Length != 0) continue;
                var attr = m.GetCustomAttribute<SasquatchEmbedAttribute>();
                string display = attr != null && !string.IsNullOrEmpty(attr.DisplayName) ? attr.DisplayName : m.Name;
                list.Add((m.Name, display));
            }
            return list;
        }

        private static VisualElement Info(string message)
            => new HelpBox(message, HelpBoxMessageType.Info);
    }
}
