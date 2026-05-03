using System.Collections.Generic;
using UnityEngine;

public class Pillar : AbilityBase
{
    [SerializeField] private float pillarTimer;
    [SerializeField] private float procTimes;
    [SerializeField] private float energyProc;
    
    [SerializeField] private bool activeHitbox;
    [SerializeField] private bool isErupting;
    [SerializeField] private List<GameObject> inContact = new List<GameObject>();
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
    
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy")) 
            if (!inContact.Contains(collision.gameObject) && GetHealth(collision.gameObject) > 0) 
                inContact.Add(collision.gameObject);
        
        if (collision.gameObject.layer == LayerMask.NameToLayer("Dead") || collision.gameObject.layer == LayerMask.NameToLayer("EDead")) 
            inContact.Remove(collision.gameObject);
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy")) 
            inContact.Remove(collision.gameObject);
    }
    
    private void Hitbox()
    {
        if (inContact != null && procTimer <= 0)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.gameObject.GetComponent<EDamageables>();
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
                        var damageable = hit.gameObject.GetComponent<PDamageable>();
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
                        var entity = hit;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var eDmg = hit.gameObject.GetComponent<EDamageables>();
                            if (eDmg != null)
                            {
                                eDmg.Stun(stunTime);
                                eDmg.Damage(damage, knockbackForce);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.gameObject.GetComponent<PDamageable>();
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
    
    void Boom() => Destroy(GetComponent<FindFloor>().bottomPoint.transform.gameObject);
    void ApplyTimer() => pillarTimer = lifeTime;
}
