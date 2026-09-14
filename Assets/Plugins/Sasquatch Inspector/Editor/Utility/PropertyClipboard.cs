using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Copy/paste of a SerializedProperty's VALUE through the system clipboard, used by the unified
    /// right-click menu's "Copy Value" / "Paste Value" entries. Values are written as Unity-compatible
    /// text via <see cref="EditorGUIUtility.systemCopyBuffer"/>, covering the common primitive/struct
    /// property types (int, float, bool, string, enum, LayerMask, Character, Color, Vector2-4). Callers
    /// apply + undo: Paste() only writes the property; it does NOT call ApplyModifiedProperties.
    /// </summary>
    public static class PropertyClipboard
    {
        /// <summary>A no-op, retained for source compatibility: the textual path is the only path.</summary>
        public static bool ForceFallbackForTests;

        /// <summary>Copies <paramref name="prop"/>'s value to the clipboard as Unity-compatible text.</summary>
        public static bool Copy(SerializedProperty prop)
        {
            if (prop == null) return false;
            return CopyFallback(prop);
        }

        /// <summary>True when the clipboard holds a value compatible with <paramref name="prop"/>.</summary>
        public static bool CanPaste(SerializedProperty prop)
        {
            if (prop == null) return false;
            return CanPasteFallback(prop);
        }

        /// <summary>Pastes the clipboard value into <paramref name="prop"/>.</summary>
        public static bool Paste(SerializedProperty prop)
        {
            if (prop == null) return false;
            return PasteFallback(prop);
        }

        // ---- Fallback: Unity's textual clipboard formats for the common primitive types ----

        private static bool CopyFallback(SerializedProperty prop)
        {
            var inv = CultureInfo.InvariantCulture;
            string text;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: text = prop.longValue.ToString(inv); break; // 64-bit safe: int AND long serialize as Integer
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character: text = prop.intValue.ToString(inv); break;
                case SerializedPropertyType.Boolean:   text = prop.boolValue ? "True" : "False"; break;
                case SerializedPropertyType.Float:     text = prop.doubleValue.ToString("R", inv); break;
                case SerializedPropertyType.String:    text = prop.stringValue; break;
                case SerializedPropertyType.Enum:      text = prop.intValue.ToString(inv); break;
                case SerializedPropertyType.Color:
                {
                    var c = prop.colorValue;
                    text = string.Format(inv, "RGBA({0},{1},{2},{3})", c.r, c.g, c.b, c.a); break;
                }
                case SerializedPropertyType.Vector2:
                {
                    var v = prop.vector2Value;
                    text = string.Format(inv, "Vector2({0},{1})", v.x, v.y); break;
                }
                case SerializedPropertyType.Vector3:
                {
                    var v = prop.vector3Value;
                    text = string.Format(inv, "Vector3({0},{1},{2})", v.x, v.y, v.z); break;
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = prop.vector4Value;
                    text = string.Format(inv, "Vector4({0},{1},{2},{3})", v.x, v.y, v.z, v.w); break;
                }
                default: return false;
            }
            EditorGUIUtility.systemCopyBuffer = text;
            return true;
        }

        private static bool CanPasteFallback(SerializedProperty prop)
        {
            string clip = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clip)) return prop.propertyType == SerializedPropertyType.String && clip != null;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return long.TryParse(clip, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.Enum:    return int.TryParse(clip, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                case SerializedPropertyType.Boolean: return bool.TryParse(clip, out _);
                case SerializedPropertyType.Float:   return double.TryParse(clip, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                case SerializedPropertyType.String:  return true;
                case SerializedPropertyType.Color:   return TryParseTuple(clip, "RGBA", 4, out _);
                case SerializedPropertyType.Vector2: return TryParseTuple(clip, "Vector2", 2, out _);
                case SerializedPropertyType.Vector3: return TryParseTuple(clip, "Vector3", 3, out _);
                case SerializedPropertyType.Vector4: return TryParseTuple(clip, "Vector4", 4, out _);
                default: return false;
            }
        }

        private static bool PasteFallback(SerializedProperty prop)
        {
            string clip = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
            var inv = CultureInfo.InvariantCulture;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!long.TryParse(clip, NumberStyles.Integer, inv, out long l)) return false;
                    prop.longValue = l; return true; // 64-bit safe: covers long/ulong-range values
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.Enum:
                    if (!int.TryParse(clip, NumberStyles.Integer, inv, out int i)) return false;
                    prop.intValue = i; return true;
                case SerializedPropertyType.Boolean:
                    if (!bool.TryParse(clip, out bool b)) return false;
                    prop.boolValue = b; return true;
                case SerializedPropertyType.Float:
                    if (!double.TryParse(clip, NumberStyles.Float, inv, out double d)) return false;
                    prop.doubleValue = d; return true;
                case SerializedPropertyType.String:
                    prop.stringValue = clip; return true;
                case SerializedPropertyType.Color:
                    if (!TryParseTuple(clip, "RGBA", 4, out float[] c)) return false;
                    prop.colorValue = new Color(c[0], c[1], c[2], c[3]); return true;
                case SerializedPropertyType.Vector2:
                    if (!TryParseTuple(clip, "Vector2", 2, out float[] v2)) return false;
                    prop.vector2Value = new Vector2(v2[0], v2[1]); return true;
                case SerializedPropertyType.Vector3:
                    if (!TryParseTuple(clip, "Vector3", 3, out float[] v3)) return false;
                    prop.vector3Value = new Vector3(v3[0], v3[1], v3[2]); return true;
                case SerializedPropertyType.Vector4:
                    if (!TryParseTuple(clip, "Vector4", 4, out float[] v4)) return false;
                    prop.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]); return true;
                default:
                    return false;
            }
        }

        // Parses "Prefix(a,b,...)" with exactly `count` invariant floats.
        private static bool TryParseTuple(string clip, string prefix, int count, out float[] values)
        {
            values = null;
            if (string.IsNullOrEmpty(clip)) return false;
            clip = clip.Trim();
            if (!clip.StartsWith(prefix + "(", StringComparison.Ordinal) || !clip.EndsWith(")", StringComparison.Ordinal)) return false;
            var parts = clip.Substring(prefix.Length + 1, clip.Length - prefix.Length - 2).Split(',');
            if (parts.Length != count) return false;
            var result = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) return false;
            values = result;
            return true;
        }
    }
}
