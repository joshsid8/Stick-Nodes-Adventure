using UnityEngine;

public class Beam : AbilityBase
{
    [SerializeField] private bool activeHitbox;
    [SerializeField] private float energyProc;
    [SerializeField] private float procTimes;
    private bool isFacingRightDirection;
    
    private RaycastHit2D[] inContact;
    [SerializeField] private BoxCollider2D beamCollider;

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        lifeTimer = lifeTime; 
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? direction : -direction + 180f);
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>(); 
        procTimes = lifeTime / proc;
        procTimes = energyRequired / procTimes;
        coll = GetComponent<Collider2D>();
    }

    private void Update()
    {
        Lifetime();
        procTimer -= Time.deltaTime;
        energyProc -= Time.deltaTime;
        coll.enabled = procTimer <= 0 ? coll.enabled : !coll.enabled;

        if (pa != null && typeFactor == 1) pa.currentAbilityCooldown = cooldown * pa.abilityCooldownMult;
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        transform.localEulerAngles = new Vector3(0, 0, 0);
        
        if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor == 1)
        {
            energyProc = proc;
            if (pa != null && typeFactor == 1) pa.energy -= procTimes;
        } 
        else if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor != 1) energyProc = proc;
        
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        HitBox();
    }
    
    public void SetValues(bool facingRight, float adirection, int type = 0,float damageMult = 0, float lifetimeMult = 1, float potency = 1)
    {
        isFacingRightDirection = facingRight;
        this.direction = adirection;
        typeFactor = type;
        lifeTime *= lifetimeMult;
        knockbackForce *= potency;
        stunTime *= potency;
        damage *= damageMult;
    }
    
    private void HitBox()
    {
        inContact = Physics2D.CapsuleCastAll(transform.position, beamCollider.size, CapsuleDirection2D.Horizontal, 0,
            Vector2.zero, 0, LayerMask.GetMask("Enemy", "Player"));
        if (inContact != null && procTimer <= 0)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                        if (damageable != null)
                        {
                            damageable.Damage(damage, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y));
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                        }
                    }
                    break;
                case 2:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.collider.gameObject.GetComponent<PDamageable>();
                        if (damageable != null)
                        {
                            damageable.Damage(damage, knockbackForce * transform.up, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                        }
                    }
                    break;
                default:
                    foreach (var hit in inContact)
                    {
                        var entity = hit.collider;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                            if (damageable != null)
                            {
                                damageable.Damage(damage, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y));
                                damageable.Stun(stunTime);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.collider.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(damage, knockbackForce, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                                pDmg.Stun(stunTime);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                            }
                        }
                        else print("Clueless.");
                    }
                    break;
            }
            procTimer = proc;
        }
    }

    private void Lifetime()
    {
        lifeTimer -= Time.deltaTime;
        if (!pa.isHolding && isHoldable && typeFactor == 1) lifeTimer = 0;
        if (lifeTimer <= 0)
        {
            if (pa != null && typeFactor == 1) pa.isPerformingAbility = false;
            animator.Play(endAnim.name);
        }
    }

    private void Boom() => Destroy(parentObject);

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, beamCollider.size);
    }
}
