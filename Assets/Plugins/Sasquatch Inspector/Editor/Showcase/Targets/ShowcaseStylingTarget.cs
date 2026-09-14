using UnityEngine;

namespace SasquatchInspector.Showcase
{
    internal class ShowcaseStylingTarget : ScriptableObject
    {
        public string heading = "Styled Inspector";

        [Range(0f, 100f)] public float power = 65f;

        public bool active = true;

        public Color tint = new Color(0.95f, 0.55f, 0.25f);

        public string note = "Any node can carry its own colors, fonts, borders, and theme token.";

        public bool checkboxStyle = true;
        public bool modernCheckboxStyle = true;
        public bool roundedStyle = true;
        public bool squareStyle;
        public bool slimStyle = true;

        [Range(0f, 100f)] public float shields = 78f;

        public int armor = 24;
        public float critChance = 0.15f;
    }
}
