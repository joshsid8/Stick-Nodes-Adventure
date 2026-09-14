using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // The nested object embedded inline by the Media page's In-Line Editor widget.
    internal class ShowcaseStatBlock : ScriptableObject
    {
        public string statName = "Vitality";

        [Range(0, 100)] public int value = 72;

        public Color barColor = new Color(0.4f, 0.85f, 1f);
    }
}
