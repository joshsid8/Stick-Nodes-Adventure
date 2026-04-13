using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "Ability")]
public class AbilityObjSO : ScriptableObject
{
     public GameObject abilityPrefab;
     
     [Header("Ability Object Properties")]
     [Tooltip("If this is false, all the properties below will be ignored and the game object will use its default values.")]
     public bool setProperties;
     public float damage;
     public Vector2 knockbackForce;
     public float stunTime;
     public float direction;
     public bool isDirectional;
     public float energyRequired;
     public float cooldown;
     public float lifeTime;
     public float proc;
     
     [Space]
     [Header("Caster Properties")]
     public int amount = 1;
     public float spawnInterval = 0.1f;
     public float speedMult = 1;
     public float cooldownMult = 1;
     public float lifetimeMult = 1;
     public float damageMult = 1;
     public float potency = 1;
     public float sizeMult = 1;
     public bool isHoldable;
     [Tooltip("Will another ability be spawned when this one collides?")]
     public bool abilityOnCollide;

     public void SetProperties(AbilityBase ab)
     {
          if (setProperties)
          {
               ab.damage = damage * damageMult;
               ab.knockbackForce = knockbackForce * potency;
               ab.stunTime = stunTime * potency;
               ab.direction = direction;
               ab.isDirectional = isDirectional;
               ab.energyRequired = energyRequired;
               ab.cooldown = cooldown * cooldownMult;
               ab.lifeTime = lifeTime * lifetimeMult;
               ab.proc = proc;
               if (speedMult != 1) ab.gameObject.GetComponent<Projectile>().projectileSpeed *= speedMult;
               ab.isHoldable = isHoldable;
               ab.abilityOnCollide = abilityOnCollide;
               ab.transform.localScale *= sizeMult;
          }
     }
}