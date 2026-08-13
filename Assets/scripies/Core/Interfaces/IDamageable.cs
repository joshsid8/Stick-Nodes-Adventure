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

        void Damage(float damageAmount, bool facingRight, int damageFactor = 0,
            ElementType elementType = ElementType.Normal, float elementFactor = 1, bool isTrue = false, bool damageOverTime = false, float procDmg = 0f, float procInterval = 0, string aggressor = "");
        void Shock(float stunDuration = 0, Vector2 knockbackDirection = default, float knockbackForce = 20f, float knockbackTime = 0.2f, bool targetPosition = false);
        void Heal(float amount);
    }
}
