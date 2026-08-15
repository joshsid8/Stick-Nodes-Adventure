using System.Collections.Generic;
using scripies.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class AbilityBase : MonoBehaviour //at this point this is just the pillar script
{
    [Header("Factors")]
    [Tooltip("This is for the whatever gets hurt. \n1: Enemies/Bosses\n2: Player\nDefault/0: neutral/all")]
    public int typeFactor;
    public int elementType;
    public int elementFactor = 1;
    public int damageFactor;
    public bool trueDamage;
    [FormerlySerializedAs("beamDamage")]
    [FormerlySerializedAs("projectileDamage")]
    [FormerlySerializedAs("pillarDamage")]
    public float damage;
    public bool damageOverTime;
    public float procDmg;
    public float procInterval;
    public float sizeMult = 1;
    [Space]
    [Header("Properties")]
    [FormerlySerializedAs("knockbackForce")]
    [FormerlySerializedAs("beamKnockbackForce")]
    [FormerlySerializedAs("projectileKnockbackForce")]
    [FormerlySerializedAs("knockbackForce")]
    public Vector2 knockbackDirection;
    public float knockbackForce = 20f;
    public float knockbackTime = 0.2f;
    
    [FormerlySerializedAs("beamDirection")]
    [FormerlySerializedAs("projectileDirection")]
    public float direction;
    public bool isDirectional;
    public bool isFacingRightDirection;
    
    [FormerlySerializedAs("beamStunTime")]
    [FormerlySerializedAs("projectileStunTime")]
    [FormerlySerializedAs("pillarStunTime")]
    public float stunTime;
    
    public float energyRequired;

    [Space] [Header("Explosion Properties")]
    public bool explodeOnImpact;
    public bool useRadialKnockback;
    public float explosionUpwardForce = 2;
    public float explosionForce = 20f;
    public float explosionTime = 0.2f;
    
    [FormerlySerializedAs("abilityCooldown")]
    public float cooldown;
    [Space]
    [Header("Proccing")]
    [FormerlySerializedAs("proc")]
    [FormerlySerializedAs("beamProc")]
    [FormerlySerializedAs("pillarProc")]
    public float procTime;

    [FormerlySerializedAs("procTimer")]
    [FormerlySerializedAs("beamProcTimer")]
    [FormerlySerializedAs("pillarProcTimer")]
    public float currentProcTimer;
    
    [FormerlySerializedAs("isErupting")]
    public bool activeHitbox;
    
    private float energyProc;
    private float procTimes;
    [Space]
    [Header("Lifetime")]
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
    
    private List<GameObject> inContact = new();
    [Space]
    [Header("References")]
    public Animator animator;
    
    public GameObject parentObject;
    [FormerlySerializedAs("pillarCollider")]
    [FormerlySerializedAs("beamCollider")]
    public Collider2D coll;
    protected IPlayerStatus pa;
    
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
        currentProcTimer = 0;
        pa = GameObject.FindGameObjectWithTag("Player").GetComponent<IPlayerStatus>(); 
        parentObject = transform.parent.gameObject;
        
        if (anim != null) GetComponent<SpriteRenderer>().sprite = null;
        GetComponent<SpriteRenderer>().color = new Color(1, 1, 1, 1);
        if (anim != null) animator.Play(anim.name);
        
        lifeTimer = lifeTime;
        procTimes = lifeTime / procTime;
        procTimes = energyRequired / procTimes;
        transform.localScale = new Vector3(transform.localScale.x * sizeMult,transform.localScale.y * sizeMult, 1);
        if (animator.runtimeAnimatorController == null) 
            Destroy(parentObject.gameObject ?? gameObject, lifeTime);
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
        {
            if (!inContact.Contains(collision.gameObject) && GetHealth(collision.gameObject) > 0) 
                inContact.Add(collision.gameObject);
        }
        
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
        if (useRadialKnockback) RadiusProc();
        if (inContact.Count != 0 && currentProcTimer <= 0 && activeHitbox)
        {   
            foreach (var hit in inContact)
            {
                switch (typeFactor)
                {
                    case 1:
                    {
                        if (!hit.CompareTag("Enemy") && !hit.CompareTag("Boss")) return;
                        break;
                    }
                    case 2:
                    {
                        if (!hit.CompareTag("Player")) return;
                        break;
                    }
                }
                var damageable = hit.GetComponent<IDamageable>();
                Damageable(damageable);
            }
            
            currentProcTimer = procTime;
        }
    }

    protected void Damageable(IDamageable damageable = null)
    {
        if (damageable != null)
        {
            damageable.Damage(damage, isFacingRightDirection, damageFactor, (IDamageable.ElementType)elementType,
                elementFactor, trueDamage, damageOverTime, procDmg, procInterval, typeFactor switch { 0 => "Ability", 1 => "PlayerAbility", 2 => "EnemyAbility", _ => "Ability" });
            Vector2 calcDirection;
            if (useRadialKnockback)
                calcDirection = damageable.Position - transform.position;
            else calcDirection = new Vector2(knockbackDirection.x, knockbackDirection.y).normalized;
            
            damageable.Shock(stunTime, calcDirection, knockbackForce, knockbackTime);
        }
    }

    protected void RadiusProc()
    {
        var objects = inContact;
        foreach (var obj in objects)
        {
            if (typeFactor == 1 && !obj.CompareTag("Enemy") && !obj.CompareTag("Boss")) continue;
            if (typeFactor == 2 && !obj.CompareTag("Player")) continue;
            
            var damageable = obj.GetComponent<IDamageable>();
            if (damageable != null && currentProcTimer <= 0 && activeHitbox)
            {
                var radialDir = new Vector2(damageable.Position.x - transform.position.x, 
                    (damageable.Position.y - transform.position.y) + explosionUpwardForce).normalized;

                damageable.Damage(damage, isFacingRightDirection, damageFactor, (IDamageable.ElementType)elementType, elementFactor, trueDamage, damageOverTime, procDmg, procInterval, typeFactor switch { 0 => "", 1 => "Player", 2 => "Enemy", _ => "" });
                damageable.Shock(explosionTime, radialDir, explosionForce, explosionTime);
                currentProcTimer = procTime;
            }
        }
    }
    
    private void Proc()
    {
        currentProcTimer -= Time.deltaTime;
        energyProc -= Time.deltaTime;
        if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor == 1)
        {
            energyProc = procTime;
            if (pa != null && typeFactor == 1) GameEvents.AddPlayerEnergy(-procTimes);
        }
        else if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor != 1) energyProc = procTime;
    }
    
    void ApplyTimer() => lifeTimer = lifeTime;

    private void Lifetime()
    {
        if (lifeTimer > 0)
        {
            lifeTimer -= Time.deltaTime;
            if (!pa.IsHoldingAbility && isHoldable && typeFactor == 1) lifeTimer = 0;
            if (lifeTimer <= 0 && endAnim != null)
            {
                if (explodeOnImpact) RadiusProc();
                animator.Play(endAnim.name);
            }
            else if (lifeTimer <= 0)
            {
                if (explodeOnImpact) RadiusProc();
                if (pa != null && typeFactor == 1) GameEvents.TriggerPlayerPerformAbility(false);
                Destroy(parentObject);
            }
        }
    }
    
    private float GetHealth(GameObject entity)
    {
        switch (entity.tag)
        {
            case "Player":
                return pa.CurrentHealth;
            case "Enemy":
            case "Boss":
                return entity.GetComponent<IEnemyStatus>().CurrentHealth;
            default:
                return 0;
        }
    }
    
    public void Boom() => Destroy(parentObject);

}
