using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Reflection helpers for <c>PropertyNode</c> (a reflected, non-serialized C# property): resolve a C#
    /// property by name, read/write its value, decide which value types we can render an editable control for,
    /// and enumerate a type's readable properties for the designer picker. Sibling of <see cref="ButtonInvoker"/>
    /// — the difference is properties have no <see cref="SerializedProperty"/>, so values are reflected and polled.
    /// </summary>
    public static class PropertyInvoker
    {
        private const BindingFlags PropFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Finds a readable, non-indexer property by name, walking the inheritance chain with DeclaredOnly so
        /// that private base-declared properties (which a derived-type lookup would miss) and shadowed members
        /// resolve unambiguously. Returns null when not found or not readable.
        /// </summary>
        public static PropertyInfo ResolveProperty(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var p = t.GetProperty(name, PropFlags | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p;
            }
            return null;
        }

        /// <summary>Resolves against the first non-null target of a <see cref="SerializedObject"/>.</summary>
        public static PropertyInfo ResolveProperty(SerializedObject serializedObject, string name)
        {
            if (serializedObject == null) return null;
            foreach (var t in serializedObject.targetObjects)
                if (t != null) return ResolveProperty(t.GetType(), name);
            return null;
        }

        /// <summary>
        /// Types we have a built-in UI Toolkit field for, so a settable property of this type can be
        /// rendered editable. Anything else (including complex/custom types) displays read-only via ToString().
        /// </summary>
        public static bool IsSimpleEditableType(Type t)
        {
            if (t == null) return false;
            if (t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) ||
                t == typeof(bool) || t == typeof(string) ||
                t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) ||
                t == typeof(Vector2Int) || t == typeof(Vector3Int) || t == typeof(Color))
                return true;
            if (t.IsEnum) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true;
            return false;
        }

        /// <summary>
        /// The type's readable, non-indexer properties declared OUTSIDE UnityEngine/UnityEditor, so the
        /// designer's property picker isn't flooded with Unity base-class noise (enabled, name, transform…).
        /// One entry per name (most-derived wins).
        /// </summary>
        public static IEnumerable<PropertyInfo> GetReadableProperties(Type type)
        {
            if (type == null) return Array.Empty<PropertyInfo>();
            return type.GetProperties(PropFlags)
                .Where(p => p.CanRead
                            && p.GetIndexParameters().Length == 0
                            && p.DeclaringType != null
                            && (p.DeclaringType.Namespace == null
                                || (!p.DeclaringType.Namespace.StartsWith("UnityEngine")
                                    && !p.DeclaringType.Namespace.StartsWith("UnityEditor"))))
                .GroupBy(p => p.Name)
                .Select(g => g.First());
        }

        /// <summary>Reads the property value; returns false (and null) if the getter throws.</summary>
        public static bool TryGetValue(object target, PropertyInfo prop, out object value)
        {
            value = null;
            if (target == null || prop == null) return false;
            try { value = prop.GetValue(target); return true; }
            catch { return false; }
        }

        /// <summary>Reads the property value, or an "&lt;error: …&gt;" string if the getter throws.</summary>
        public static object GetValueOrError(object target, PropertyInfo prop)
        {
            if (target == null || prop == null) return null;
            try { return prop.GetValue(target); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                return $"<error: {inner.Message}>";
            }
        }

        /// <summary>Writes the property via its setter (no-op if not writable); logs on failure.</summary>
        public static void SetValue(object target, PropertyInfo prop, object value)
        {
            if (target == null || prop == null || !prop.CanWrite) return;
            try { prop.SetValue(target, value); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Debug.LogError($"[Sasquatch Inspector] Error setting property '{prop.Name}' on '{target}': {inner.Message}");
            }
        }
    }
}
