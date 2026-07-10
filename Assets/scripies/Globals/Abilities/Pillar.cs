using System.Collections.Generic;
using UnityEngine;

public class Pillar : AbilityBase
{ 
    [SerializeField] private bool shockwave;
    [SerializeField] private List<GameObject> inContact = new List<GameObject>();
    [SerializeField] private BoxCollider2D pillarCollider;
    
    private void Awake()
    {
        animator = GetComponent<Animator>();
        coll = GetComponent<Collider2D>();
    }

    void Update()
    {
        Lifetime();
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        Proc();
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
        if (inContact.Count != 0 && procTimer <= 0 && activeHitbox)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.gameObject.GetComponent<EDamageables>();
                        if (damageable != null)
                        {
                            damageable.Damage(damage, knockbackForce * transform.up);
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.5f;
                        }
                    }
                    break;
                case 2:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.gameObject.GetComponent<PDamageable>();
                        if (damageable != null)
                        {
                            damageable.Damage(damage, knockbackForce * transform.up, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.5f;
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
                                PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.5f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(damage, knockbackForce, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                                pDmg.Stun(stunTime);

                                PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.5f;
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
    
    private void OnDestroy()
    {
        if (pa != null && typeFactor == 1) pa.isPerformingAbility = false;
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
    
    void ApplyTimer() => lifeTimer = lifeTime;
}
