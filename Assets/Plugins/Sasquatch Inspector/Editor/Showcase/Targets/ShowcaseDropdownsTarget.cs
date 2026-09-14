using UnityEngine;

namespace SasquatchInspector.Showcase
{
    internal class ShowcaseDropdownsTarget : ScriptableObject
    {
        public enum Difficulty { Easy, Normal, Hard, Nightmare }

        [System.Flags]
        public enum Abilities
        {
            None = 0,
            Melee = 1,
            Ranged = 2,
            Magic = 4,
            Stealth = 8,
            MeleeRanged = Melee | Ranged
        }

        public Difficulty difficulty = Difficulty.Normal;

        public Abilities abilities = Abilities.Melee | Abilities.Magic;

        public string targetTag = "Untagged";

        public int targetLayer = 0;

        public LayerMask collisionMask = ~0;

        public string weaponClass = "Blade";

        public string[] keywords = { "boss", "flying", "fire-immune" };

        public string[] WeaponClasses() => new[] { "Blade", "Blunt", "Ranged", "Magic", "Thrown" };
    }
}
