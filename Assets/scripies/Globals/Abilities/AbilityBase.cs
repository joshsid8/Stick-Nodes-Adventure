using UnityEngine;
using UnityEngine.Serialization;

public abstract class AbilityBase : MonoBehaviour
{
    [Tooltip("This is for the whatever gets hurt. \n1: Enemies/Bosses\n2: Player\nDefault/0: neutral/all")]
    public int typeFactor;
    public int elementType;
    public int damageFactor;
    [FormerlySerializedAs("beamDamage")]
    [FormerlySerializedAs("projectileDamage")]
    [FormerlySerializedAs("pillarDamage")]
    public float damage;
    
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
    
    public Transform playerTransform;
    public Animator animator;
    
    public GameObject parentObject;
    public Collider2D coll;
    public PlayerAbility pa;
    
    [FormerlySerializedAs("eruptAnim")]
    public AnimationClip anim;
    
    [FormerlySerializedAs("collideAnim")]
    [FormerlySerializedAs("finishAnim")]
    public AnimationClip endAnim;

    public abstract void SetValues(int factor, bool facingRight);

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
    
    protected void Proc()
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
    protected void Lifetime()
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
    
    public void Boom() => Destroy(parentObject);

}
