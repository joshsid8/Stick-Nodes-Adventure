using UnityEngine;

public interface PDamageable
{
    public void Damage(float damageAmount, Vector2 knockbackDirection, bool facingRight, int damageType = 0);
    public void Stun(float stunDuration = 0);
}