using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "Ability")]
public class AbilityObjSOBase : ScriptableObject
{
     public GameObject abilityPrefab;
     
     // ability object properties
     public float damage;
     public Vector2 knockbackForce;
     public float stunTime;
     public float direction;
     public bool isDirectional;
     public float energyRequired;
     public float cooldown;
     public float lifeTime;
     public float proc;
     
     // caster properties
     public int amount = 1;
     public float spawnInterval = 0.5f;
     public float speedMult = 1;
     public float abilityCooldownMult = 1;
     public float lifetimeMult = 1;
     public float damageMult = 1;
     public float sizeMult = 1;
     public bool isHoldable;
     public bool abilityOnCollide;

     public void SetProperties(AbilityBase ab)
     {
          ab.damage = damage;
          ab.knockbackForce = knockbackForce;
          ab.stunTime = stunTime;
          ab.direction = direction;
          ab.isDirectional = isDirectional;
          ab.energyRequired = energyRequired;
          ab.cooldown = cooldown;
          ab.lifeTime = lifeTime;
          ab.proc = proc;
          if (speedMult != 1) ab.gameObject.GetComponent<Projectile>().projectileSpeed *= speedMult; 
     }
}