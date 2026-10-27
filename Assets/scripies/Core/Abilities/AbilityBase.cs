using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public abstract class AbilityBase : MonoBehaviour //at this point this is just the pillar script
{
    [Header("Factors")]
    public GameObject abilityCaster;
    [Tooltip("This is for the whatever gets hurt. \n1: Enemies/Bosses\n2: Player\nDefault/0: neutral/all")]
    public int typeFactor;
    public int elementType;
    public int elementFactor = 1;
    public int damageFactor;
    public bool trueDamage;
    
    [Space]
    [Header("Damage")]
    [FormerlySerializedAs("damage")]
    public float abilityDamage;
    public bool damageOverTime;
    public float procDmg;
    public float procInterval;
    public float sizeMult = 1;
    [Space]
    [Header("Properties")]
    public Vector2 knockbackDirection;
    public float knockbackForce = 20f;
    public float knockbackTime = 0.2f;

    public float direction;
    public bool isDirectional;
    public bool isFacingRightDirection;
    
    public float stunTime;
    
    public float energyRequired;

    [Space] [Header("Explosion Properties")]
    public bool explodeOnImpact;
    public bool useRadialKnockback;
    public float explosionUpwardForce = 2;
    public float explosionForce = 20f;
    public float explosionTime = 0.2f;
    
    public float cooldown;
    [Space]
    [Header("Proccing")]
    public float procTime;

    public float currentProcTimer;
    
    public bool activeHitbox;
    
    private float energyProc;
    private float procTimes;
    [Space]
    [Header("Lifetime")]
    public float lifeTime;
    
    public float lifeTimer;
    public bool isHoldable;
    public float minHoldTime;
    public bool abilityOnCollide;
    public AbilityObjSO[] subAbilities;
    
    private readonly List<GameObject> inContact = new();
    [Space]
    [Header("References")]
    public Animator animator;
    
    public GameObject parentObject;
    public Collider2D coll;
    protected IPlayerStatus pa;
    
    public AnimationClip anim;
    
    public AnimationClip endAnim;
    
    public AudioClip startSfx;
    public AudioClip loopingSfx;
    public AudioClip endSfx;
    public AudioClip contactSfx;
    
    [HideInInspector] public AbilityObjSO abilityId;
    public bool perceiveAllObjects;    
    public virtual void SetValues(int factor, bool facingRight, GameObject caster = null, AbilityObjSO id = null)
    {
        typeFactor = factor;
        isFacingRightDirection = facingRight;
        
        if (caster == null) return;
        abilityCaster = caster;
        SceneManager.MoveGameObjectToScene(this is not Projectile ? transform.parent.gameObject : gameObject, abilityCaster.scene); // Lol pseudocode at this point "this is not"
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
        parentObject = transform.parent != null ? transform.parent.gameObject : gameObject;
        
        if (anim != null) GetComponent<SpriteRenderer>().sprite = null;
        GetComponent<SpriteRenderer>().color = new Color(1, 1, 1, 1);
        if (anim != null) animator.Play(anim.name);
        
        lifeTimer = lifeTime;
        procTimes = lifeTime / procTime;
        procTimes = energyRequired / procTimes;
        transform.localScale = new Vector3(transform.localScale.x * sizeMult,transform.localScale.y * sizeMult, 1);
        
        if (startSfx != null) GameEvents.TriggerPlaySound(startSfx, gameObject);
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
        if (collision.gameObject == abilityCaster) return;
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

    public virtual void Explode()
    {
        if (useRadialKnockback) RadiusProc();
        else
        {
            var objects = Physics2D.OverlapCircleAll(transform.position, sizeMult * 2); // default radius if not using RadiusProc
            foreach (var obj in objects)
            {
                if (obj.gameObject == abilityCaster) continue;
                switch (typeFactor)
                {
                    case 1 when !obj.CompareTag("Enemy") && !obj.CompareTag("Boss"):
                    case 2 when !obj.CompareTag("Player"):
                        continue;
                }
                var damageable = obj.GetComponent<IDamageable>();
                Damageable(damageable);
            }
        }
    }

    protected void Damageable(IDamageable damageable = null)
    {
        if (damageable == null) return;
        string aggressor;
        if (abilityCaster != null && abilityCaster.CompareTag("Boss")) aggressor = "BossAbility";
        else aggressor = typeFactor switch { 0 => "Ability", 1 => "PlayerAbility", 2 => "EnemyAbility", _ => "Ability" };
        
        var abDamage = Shenanigans.Instance.RawDamage(abilityDamage, damageFactor, (IDamageable.ElementType)elementType, elementFactor, trueDamage, damageOverTime, procDmg, procInterval);
        GameEvents.TriggerApplyDamage(damageable, abDamage, isFacingRightDirection, aggressor);
        
        Vector2 calcDirection;
        if (useRadialKnockback) calcDirection = damageable.Position - transform.position;
        else calcDirection = new Vector2(isFacingRightDirection ? knockbackDirection.x : -knockbackDirection.x, knockbackDirection.y).normalized;
        damageable.Shock(stunTime, calcDirection, knockbackForce, knockbackTime);
        if (damageable is MonoBehaviour targetMb)
            GameEvents.TriggerHitImpact(gameObject, targetMb.gameObject, abDamage);
        if (contactSfx != null) GameEvents.TriggerPlaySound(contactSfx, gameObject);
    }

    protected void RadiusProc()
    {
        var objects = inContact;
        foreach (var obj in objects.Where(obj => obj != abilityCaster))
        {
            switch (typeFactor)
            {
                case 1 when !obj.CompareTag("Enemy") && !obj.CompareTag("Boss"):
                case 2 when !obj.CompareTag("Player"):
                    continue;
            }

            var damageable = obj.GetComponent<IDamageable>();
            
            if (damageable == null || !(currentProcTimer <= 0) || !activeHitbox) continue;
            var radialDir = new Vector2(damageable.Position.x - transform.position.x, 
                (damageable.Position.y - transform.position.y) + explosionUpwardForce).normalized;
            
            var dm = Shenanigans.Instance.RawDamage(abilityDamage, damageFactor, (IDamageable.ElementType)elementType, elementFactor, trueDamage, damageOverTime, procDmg, procInterval);
            GameEvents.TriggerApplyDamage(damageable, dm, isFacingRightDirection, typeFactor switch { 0 => "", 1 => "Player", 2 => "Enemy", _ => "" });
            damageable.Shock(explosionTime, radialDir, explosionForce, explosionTime);
            if (damageable is MonoBehaviour targetMb)
                GameEvents.TriggerHitImpact(gameObject, targetMb.gameObject, dm);
            currentProcTimer = procTime;
        }
    }
    
    private void Proc()
    {
        currentProcTimer -= Time.deltaTime;
        energyProc -= Time.deltaTime;
        switch (isHoldable)
        {
            case true when energyProc <= 0 && activeHitbox && typeFactor == 1:
            {
                energyProc = procTime;
                if (pa != null && typeFactor == 1) GameEvents.AddPlayerEnergy(-procTimes);
                break;
            }
            case true when energyProc <= 0 && activeHitbox && typeFactor != 1:
                energyProc = procTime;
                break;
        }
    }

    private void ApplyTimer() => lifeTimer = lifeTime;

    private void Lifetime()
    {
        if (lifeTimer > 0)
        {
            lifeTimer -= Time.deltaTime;
            
            // Holdable logic: Release early only if we've passed minHoldTime
            if (isHoldable && typeFactor == 1 && pa != null)
            {
                float timeActive = lifeTime - lifeTimer;
                if (!pa.IsHoldingAbility && timeActive >= minHoldTime)
                {
                    lifeTimer = 0;
                }
            }

            if (lifeTimer <= 0 && endAnim != null)
            {
                if (explodeOnImpact) Explode();
                animator.Play(endAnim.name);
            }
            else if (lifeTimer <= 0)
            {
                if (explodeOnImpact) Explode();
                Destroy(parentObject);
            }
        }
    }
    
    protected virtual void OnDestroy()
    {
        if (pa != null && typeFactor == 1) GameEvents.TriggerPlayerPerformAbility(false);
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
