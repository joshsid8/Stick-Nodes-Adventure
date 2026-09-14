using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 2 — the character sheet. The densest exhibit in the scene: enough fields, of enough different
    /// kinds, to justify tabs, cards, progress bars, sliders, a curve and a conditional group all at once.
    ///
    /// This is what the Showcase Sandbox cannot show. The Sandbox demonstrates one widget per page, in
    /// isolation; a real inspector is a composition, and composition is what a buyer is actually judging.
    /// </summary>
    public class DemoPlayerStats : MonoBehaviour
    {
        public enum Element { Physical, Fire, Frost, Storm, Void }
        public enum Stance { Aggressive, Balanced, Defensive }

        // ---- Identity ------------------------------------------------------------------------------------
        public string displayName = "Wanderer";
        public Sprite portrait;
        [Range(1, 60)] public int level = 12;
        public Element affinity = Element.Frost;

        // ---- Vitals --------------------------------------------------------------------------------------
        public float health = 780f;
        public float maxHealth = 1000f;
        public float stamina = 42f;
        public float maxStamina = 100f;
        [Range(0f, 1f)] public float shieldCharge = 0.65f;

        // ---- Combat --------------------------------------------------------------------------------------
        public Stance stance = Stance.Balanced;
        [Range(0f, 200f)] public float attackPower = 84f;
        [Range(0f, 1f)] public float critChance = 0.18f;
        [Range(1f, 4f)] public float critMultiplier = 2.1f;

        [Tooltip("Damage falloff over distance — reads far better as a curve than as numbers.")]
        public AnimationCurve damageFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f);


        // ---- Progression ---------------------------------------------------------------------------------
        [Range(0, 5)] public int masteryStars = 3;
    }
}
