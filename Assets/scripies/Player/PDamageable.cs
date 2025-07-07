using UnityEngine;

public interface PDamageable
{
    public void Damage(float damageAmount, Vector2 knockbackDirection);
    public void Stun(float stunDuration);
}