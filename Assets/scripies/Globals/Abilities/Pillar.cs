using UnityEngine;

public class Pillar : AbilityBase
{
    [SerializeField] private float pillarTimer;
    [SerializeField] private float procTimes;
    [SerializeField] private float energyProc;
    
    [SerializeField] private bool activeHitbox;
    [SerializeField] private bool isErupting;
    private RaycastHit2D[] inContact;
    [SerializeField] private BoxCollider2D pillarCollider;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        animator = GetComponent<Animator>();
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>(); 
        coll = GetComponent<Collider2D>();
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>();
        if (anim != null) animator.Play(anim.name);
        playerTransform = PlayerHealth.Instance.gameObject.transform;
        procTimes = lifeTime / proc;
        procTimes = energyRequired / procTimes;
    }

    void Update()
    {
        Lifetime();
        procTimer -= Time.deltaTime;
        energyProc -= Time.deltaTime;
        coll.enabled = procTimer <= 0 ? coll.enabled : !coll.enabled;
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        
        if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor == 1)
        {
            energyProc = proc;
            if (pa != null && typeFactor == 1) pa.energy -= procTimes;
        } 
        else if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor != 1) energyProc = proc;

        
        Hitbox();
    }
    private void Hitbox()
    {
        inContact = Physics2D.CapsuleCastAll(transform.position, pillarCollider.size, CapsuleDirection2D.Vertical, 0,
            Vector2.zero, 0, LayerMask.GetMask("Enemy", "Player"));
        if (inContact != null && procTimer <= 0)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                        if (damageable != null && isErupting)
                        {
                            damageable.Damage(damage, knockbackForce * transform.up);
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                        }
                    }
                    break;
                case 2:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.collider.gameObject.GetComponent<PDamageable>();
                        if (damageable != null && isErupting)
                        {
                            damageable.Damage(damage, knockbackForce * transform.up, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                        }
                    }
                    break;
                default:
                    foreach (var hit in inContact)
                    {
                        var entity = hit.collider;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var eDmg = hit.collider.gameObject.GetComponent<EDamageables>();
                            if (eDmg != null)
                            {
                                eDmg.Stun(stunTime);
                                eDmg.Damage(damage, knockbackForce);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.collider.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(damage, knockbackForce, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                                pDmg.Stun(stunTime);

                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else print("Clueless.");
                    }
                    break;
            }
            procTimer = proc;
        }
    }
    
    public override void SetValues(int factor, bool facingRight) => typeFactor = factor;
    
    private void Lifetime()
    {
        if (isErupting && pillarTimer > 0)
        {
            pillarTimer -= Time.deltaTime;
            if (!pa.isHolding && isHoldable && typeFactor == 1) pillarTimer = 0;
            if (pillarTimer <= 0 && endAnim != null)
            {
                isErupting = false;
                if (pa != null && typeFactor == 1) pa.isPerformingAbility = false;
                animator.Play(endAnim.name);
            }
        }
    }
    
    void Boom() => Destroy(GetComponent<FindFloor>().bottomPoint.transform.gameObject);
    
    void ApplyTimer() => pillarTimer = lifeTime;
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, pillarCollider.size);
    }
    
}
