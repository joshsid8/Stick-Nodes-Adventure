using System.Collections.Generic;
using UnityEngine;
public interface EDamageables
{
    public enum ElementType { Normal, Fire, Earth, Water, Air}
    public void Damage(float damageAmount, Vector2 knockbackDirection, int damageFactor = 0,
        ElementType elementType = ElementType.Normal, float elementFactor = 1, bool isTrue = false, bool damageOverTime = false, float procDmg = 0f, float procInterval = 0);
    public void Stun(float stunDuration = 0.5f);
}
