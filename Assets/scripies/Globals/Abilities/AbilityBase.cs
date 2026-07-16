using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class AbilityBase : MonoBehaviour //at this point this is just the pillar script
{
    [Tooltip("This is for the whatever gets hurt. \n1: Enemies/Bosses\n2: Player\nDefault/0: neutral/all")]
    public int typeFactor;
    public int elementType;
    public int elementFactor;
    public int damageFactor;
    public bool trueDamage;
    [FormerlySerializedAs("beamDamage")]
    [FormerlySerializedAs("projectileDamage")]
    [FormerlySerializedAs("pillarDamage")]
    public float damage;
    
    public bool damageOverTime;
    public float procDmg;
    public float procInterval;
    
    [FormerlySerializedAs("beamKnockbackForce")]
    [FormerlySerializedAs("projectileKnockbackForce")]
    public Vector2 knockbackForce;
    
    [FormerlySerializedAs("beamDirection")]
    [FormerlySerializedAs("projectileDirection")]
    public float direction;
    public bool isDirectional;
    
    [FormerlySerializedAs("beamStunTime")]
    [FormerlySerializedAs("projectileStunTime")]
    [FormerlySerializedAs("pillarStunTime")]
    public float stunTime;
    
    public float energyRequired;
    
    [FormerlySerializedAs("abilityCooldown")]
    public float cooldown;
    
    [FormerlySerializedAs("beamProc")]
    [FormerlySerializedAs("pillarProc")]
    public float proc;

    [FormerlySerializedAs("beamProcTimer")]
    [FormerlySerializedAs("pillarProcTimer")]
    public float procTimer;
    
    [FormerlySerializedAs("isErupting")]
    public bool activeHitbox;
    
    public float energyProc;
    public float procTimes;
    
    [FormerlySerializedAs("beamLifetime")]
    [FormerlySerializedAs("projectileLifeTime")]
    [FormerlySerializedAs("pillarLifeTime")]
    public float lifeTime;
    
    [FormerlySerializedAs("beamTimer")]
    [FormerlySerializedAs("pillarTimer")]
    public float lifeTimer;
    public bool isHoldable;
    public bool abilityOnCollide;
    public AbilityObjSO[] subAbilities;
    
    private List<GameObject> inContact = new List<GameObject>();
    public bool isFacingRightDirection;
    public Transform playerTransform;
    public Animator animator;
    
    public GameObject parentObject;
    [FormerlySerializedAs("pillarCollider")]
    [FormerlySerializedAs("beamCollider")]
    public Collider2D coll;
    public PlayerAbility pa;
    
    [FormerlySerializedAs("eruptAnim")]
    public AnimationClip anim;
    
    [FormerlySerializedAs("collideAnim")]
    [FormerlySerializedAs("finishAnim")]
    public AnimationClip endAnim;

    public virtual void SetValues(int factor, bool facingRight)
    {
        typeFactor = factor;
        isFacingRightDirection = facingRight;
    }

    protected virtual void Awake()
    {
        animator = GetComponent<Animator>();
        coll = GetComponent<Collider2D>();
    }
    
    protected virtual void Start()
    {
        procTimer = 0;
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>(); 
        parentObject = transform.parent.gameObject;
        if (anim != null) animator.Play(anim.name);
        lifeTimer = lifeTime;
        playerTransform = PlayerHealth.Instance.gameObject.transform;

        procTimes = lifeTime / proc;
        procTimes = energyRequired / procTimes;
    }

    protected virtual void Update()
    {
        Lifetime();
        Proc();
        HitBox();
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
    }
    
    protected virtual void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy")) 
            if (!inContact.Contains(collision.gameObject) && GetHealth(collision.gameObject) > 0) 
                inContact.Add(collision.gameObject);
        
        if (collision.gameObject.layer == LayerMask.NameToLayer("Dead") || collision.gameObject.layer == LayerMask.NameToLayer("EDead")) 
            inContact.Remove(collision.gameObject);
    }
    protected virtual void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy")) 
            inContact.Remove(collision.gameObject);
    }
    
    protected virtual void HitBox()
    {
        if (inContact.Count != 0 && procTimer <= 0 && activeHitbox)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.gameObject.GetComponent<EDamageables>();
                        Damageable(eDamageables: damageable);
                    }
                    break;
                case 2:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.gameObject.GetComponent<PDamageable>();
                        Damageable(damageable);
                    }
                    break;
                default:
                    foreach (var hit in inContact)
                    {
                        var entity = hit;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var eDmg = hit.gameObject.GetComponent<EDamageables>();
                            Damageable(eDamageables: eDmg);
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.gameObject.GetComponent<PDamageable>();
                            Damageable(pDmg);
                        }
                        else print("Clueless.");
                    }
                    break;
            }
            procTimer = proc;
        }
    }

    protected virtual void Damageable(PDamageable pDamageable = null, EDamageables eDamageables = null)
    {
        if (pDamageable != null)
        {
            pDamageable.Damage(damage, true, typeFactor, (PDamageable.ElementType)elementType, elementFactor, trueDamage, damageOverTime, procDmg, procInterval);
            pDamageable.Shock(stunTime, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y));
        }
        else if (eDamageables != null)
        {
            eDamageables.Damage(damage, typeFactor, (EDamageables.ElementType)elementType, elementFactor, trueDamage, damageOverTime, procDmg, procInterval);
            eDamageables.Shock(stunTime, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y));
        }
        PlayerHealth.Instance.ApplyCombat(additive: true);
    }
    
    private void Proc()
    {
        procTimer -= Time.deltaTime;
        energyProc -= Time.deltaTime;
        if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor == 1)
        {
            energyProc = proc;
            if (pa != null && typeFactor == 1) pa.energy -= procTimes;
        }
        else if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor != 1) energyProc = proc;
    }
    
    void ApplyTimer() => lifeTimer = lifeTime;

    private void Lifetime()
    {
        if (lifeTimer > 0)
        {
            lifeTimer -= Time.deltaTime;
            if (!pa.isHolding && isHoldable && typeFactor == 1) lifeTimer = 0;
            if (lifeTimer <= 0 && endAnim != null)
            {
                if (pa != null && typeFactor == 1) pa.isPerformingAbility = false;
                animator.Play(endAnim.name);
            }
        }
    }
    
    private static float GetHealth(GameObject entity)
    {
        switch (entity.tag)
        {
            case "Player":
                return PlayerHealth.Instance.playerHealth;
            case "Enemy":
                return entity.GetComponent<EnemyHealth>().enemyHealth;
            case "Boss":
                return entity.GetComponent<BossHealth>().bossHealth;
            default:
                return 0;
        }
    }
    
    public void Boom() => Destroy(parentObject);

}
