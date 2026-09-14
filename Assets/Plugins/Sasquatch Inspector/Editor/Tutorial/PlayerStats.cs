using UnityEngine;

namespace SasquatchInspector.Samples
{
    /// <summary>
    /// A tiny sample ScriptableObject the in-editor Tutorial practices on. It deliberately mixes field types —
    /// a rich-text description, a ranged level, a health bar, an acceleration curve, a toggle that reveals a
    /// conditional field, an enum, a color — so "Smart Attribute Import" + a little styling produce a satisfying
    /// spread of widgets. It ships with the package so the Tutorial's final step can open the real Layout Designer
    /// on a real, selectable asset.
    ///
    /// It intentionally carries NO <c>[CustomEditor]</c>, so Sasquatch Inspector's inspector takes over automatically
    /// (the tutorial shows the clean happy-path; a type that declares its own <c>[CustomEditor]</c> keeps it,
    /// since Sasquatch Inspector defers to real custom editors). Safe to delete once you've found your feet — it's only
    /// here to learn on.
    /// </summary>
    public class PlayerStats : ScriptableObject
    {
        public enum CharacterClass { Warrior, Mage, Rogue, Ranger }

        [Header("Identity")]
        [Tooltip("Shown in menus, save slots, and the party screen.")]
        public string displayName = "Sir Reginald";

        [Tooltip("Flavor text for the character. Supports rich text — bold, italics, and color.")]
        [TextArea(3, 5)]
        public string characterDescription =
            "A weathered <b>{characterClass}</b> of the <color=#5BA0F2>Northern Reach</color> — " +
            "quick on their feet and <i>deadly</i> with a <color=#E07A3C>longbow</color>.";

        [Range(1, 99)]
        public int level = 12;

        public CharacterClass characterClass = CharacterClass.Ranger;

        [Header("Vitals")]
        [Range(0f, 100f)]
        public float health = 72f;

        public bool invincible = false;

        [Tooltip("How long invincibility lasts once triggered, in seconds.")]
        [Range(0f, 30f)]
        public float invincibilityDuration = 5f;

        [Header("Appearance")]
        [Tooltip("Movement speed in tiles per second.")]
        public float moveSpeed = 6.5f;

        [Tooltip("How the character's speed ramps up from a standstill.")]
        public AnimationCurve characterAcceleration = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public Color tint = new Color(0.36f, 0.62f, 0.93f, 1f);
    }
}
