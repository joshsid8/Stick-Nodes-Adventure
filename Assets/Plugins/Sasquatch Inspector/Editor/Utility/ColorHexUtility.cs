using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared hex ⇄ Color helpers so the ColorPalette widget and the Color Palette window format and parse
    /// hex identically. Thin wrappers over Unity's <see cref="ColorUtility"/>.
    /// </summary>
    public static class ColorHexUtility
    {
        /// <summary>"#RRGGBB" for opaque colors, "#RRGGBBAA" when there's transparency. Hash optional.</summary>
        public static string ToHex(Color color, bool includeHash = true)
        {
            bool opaque = color.a >= 0.999f;
            string body = opaque ? ColorUtility.ToHtmlStringRGB(color) : ColorUtility.ToHtmlStringRGBA(color);
            return includeHash ? "#" + body : body;
        }

        /// <summary>Parses "#RRGGBB", "#RRGGBBAA", "RRGGBB", etc. into a Color.</summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string s = hex.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out color);
        }
    }
}
