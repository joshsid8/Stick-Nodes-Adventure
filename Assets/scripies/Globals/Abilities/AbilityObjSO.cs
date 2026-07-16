using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "Ability")]
public class AbilityObjSO : ScriptableObject
{
     public GameObject abilityPrefab;
     
     [Header("Ability Object Properties")]
     [Tooltip("If this is false, all the properties below will be ignored and the game object will use its default values.")]
     public bool setProperties;
     public bool isDirectional;
     public float energyRequired;
     public float proc;

     [Space] [Header("Caster Properties")] 
     public int amount = 1;
     public float initSeparation;
     public float separation = 1.5f;
     public float spawnInterval = 0.1f;
     public float cooldownMult = 1;
     public float lifetimeMult = 1;
     public float damageMult = 1;
     public float potency = 1;
     public float sizeMult = 1;
     public bool isHoldable;
     public bool trueDamage;
     [Tooltip("Will another ability be spawned when this one collides?")]
     public bool abilityOnCollide;
     [Space] [Tooltip("Only for projectiles")]
     public bool swapForce;
     public float speedMult = 1;
     [Tooltip("Only for pillars")]
     public bool shockwave;
     

     public void SetProperties(AbilityBase ab)
     {
          if (setProperties)
          {
               ab.damage *= damageMult;
               ab.knockbackForce *= potency;
               ab.stunTime *= potency;
               ab.isDirectional = isDirectional;
               ab.energyRequired = energyRequired;
               ab.cooldown *= cooldownMult;
               ab.lifeTime *= lifetimeMult;
               ab.trueDamage = trueDamage;
               if (proc > 0) ab.proc = proc;
               if (speedMult != 1) ab.gameObject.GetComponent<Projectile>().projectileSpeed *= speedMult;
               if (swapForce) ab.gameObject.GetComponent<Projectile>().swapForce = true;
               if (shockwave)
               ab.isHoldable = isHoldable;
               ab.abilityOnCollide = abilityOnCollide;
               ab.transform.localScale *= sizeMult;
          }
     }
}