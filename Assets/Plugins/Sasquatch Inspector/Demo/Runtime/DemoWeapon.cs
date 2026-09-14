using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 3a — a ScriptableObject, so the demo proves layouts apply to assets and not just components.
    /// Selected on its own in the Project window it gets the same designed inspector it gets when previewed
    /// in-line from <see cref="DemoWeaponRack"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Sasquatch Inspector/Demo/Weapon", fileName = "DemoWeapon")]
    public class DemoWeapon : ScriptableObject
    {
        public enum WeaponClass { Blade, Bow, Stave, Hammer, Bazooka }
        public enum Rarity { Common, Rare, Epic, Legendary }

        public string weaponName = "Frostbite";
        public Sprite icon;
        public WeaponClass weaponClass = WeaponClass.Blade;
        public Rarity rarity = Rarity.Epic;

        [TextArea(2, 5)]
        public string flavourText = "Cold enough that the wound forgets to bleed.";

        [Range(0f, 300f)] public float baseDamage = 128f;
        [Range(0f, 3f)] public float attackSpeed = 1.35f;
        [Range(0f, 100f)] public float weight = 22f;

        public Gradient trailColour = new Gradient();
        public Vector2 rangeMinMax = new Vector2(1.2f, 3.6f);

        [Range(0, 3)] public int upgradeSlots = 2;
        public bool isTwoHanded;
    }
}
