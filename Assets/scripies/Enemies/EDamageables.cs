using UnityEngine;
public interface EDamageables
{
    public void Damage(float damageAmount, Vector2 knockbackDirection);
    public void Stun(float stunDuration);
}
