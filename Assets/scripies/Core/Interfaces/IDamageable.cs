using scripies.Core;
using UnityEngine;

namespace scripies.Core
{
    public interface IDamageable
    {
        public enum ElementType { Normal, Fire, Earth, Water, Air}
        float CurrentHealth { get; }
        float MaxHealth { get; }
        bool IsDead { get; }
        Vector3 Position { get; }

        void Damage(Damage damage, bool facingRight, string aggressor = "");
        void Shock(float stunDuration = 0, Vector2 knockbackDirection = default, float knockbackForce = 20f, float knockbackTime = 0.2f, bool targetPosition = false);
        void Heal(float amount);
    }
}

    [System.Serializable]
    public class Damage
    {
        public float damageAmount;
        public int damageFactor;
        [EnumButtons] public IDamageable.ElementType elementType;
        public float elementFactor = 1;
        public bool isTrue;
        public bool damageOverTime;
        public float procDmg;
        public float procInterval;
    }
