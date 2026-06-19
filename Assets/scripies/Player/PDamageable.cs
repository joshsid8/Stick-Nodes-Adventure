using UnityEngine;

public interface PDamageable
{
    public void Damage(float damageAmount, Vector2 knockbackDirection, bool facingRight, int damageFactor = 0, int elementFactor = 0);
    public void Stun(float stunDuration = 0);
}