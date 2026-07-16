using UnityEngine;

public interface PDamageable
{
    public enum ElementType { Normal, Fire, Earth, Water, Air}
    public void Damage(float damageAmount, Vector2 knockbackDirection, bool facingRight, int damageFactor = 0,
        ElementType elementType = ElementType.Normal, float elementFactor = 1, bool isTrue = false, bool damageOverTime = false, float procDmg = 0f, float procInterval = 0);
    public void Stun(float stunDuration = 0);
}