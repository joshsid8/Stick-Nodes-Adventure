using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Beam : AbilityBase
{

    private bool isFacingRightDirection;
    
    [SerializeField] private List<GameObject> inContact = new List<GameObject>();
    [SerializeField] private BoxCollider2D beamCollider;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        coll = GetComponent<Collider2D>();
    }

    protected override void Start()
    {
        base.Start();
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? direction : -direction + 180f);
    }

    private void Update()
    {
        Lifetime();
        if (pa != null && typeFactor == 1) pa.currentAbilityCooldown = cooldown * pa.abilityCooldownMult;
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        transform.localEulerAngles = new Vector3(0, 0, 0);
        
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        Proc();
        HitBox();
    }
    
    public override void SetValues(int factor, bool facingRight)
    {
        typeFactor = factor;
        isFacingRightDirection = facingRight;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy")) 
            if (!inContact.Contains(collision.gameObject) && GetHealth(collision.gameObject) > 0) inContact.Add(collision.gameObject);
        
        if (collision.gameObject.layer == LayerMask.NameToLayer("Dead") || collision.gameObject.layer == LayerMask.NameToLayer("EDead")) 
            inContact.Remove(collision.gameObject);
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy")) 
            inContact.Remove(collision.gameObject);
    }

    private void HitBox()
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
                            damageable.Damage(damage, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y));
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.25f;
                        }
                    }
                    break;
                case 2:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.gameObject.GetComponent<PDamageable>();
                        if (damageable != null)
                        {
                            damageable.Damage(damage, knockbackForce, !PlayerHealth.Instance.pc.isFacingRight, damageFactor);
                            damageable.Stun(stunTime);
                            PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.25f;
                        }
                    }
                    break;
                default:
                    foreach (var hit in inContact)
                    {
                        var entity = hit;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var damageable = hit.gameObject.GetComponent<EDamageables>();
                            if (damageable != null)
                            {
                                damageable.Damage(damage, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y));
                                damageable.Stun(stunTime);
                                PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.25f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(damage, knockbackForce, !PlayerHealth.Instance.pc.isFacingRight, damageFactor);
                                pDmg.Stun(stunTime);
                                PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.25f;
                            }
                        }
                        else print("Clueless.");
                    }
                    break;
            }
            procTimer = proc;
        }
    }

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

}
