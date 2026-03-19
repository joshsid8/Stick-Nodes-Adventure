using System;
using UnityEngine;

public class Beam : MonoBehaviour
{
    // jump yourself
    [Tooltip("This is for the whatever casts the beam. \n1: Enemies/Bosses\n2: Player\nDefault/0: neutral")]
    public int typeFactor;
    
    [SerializeField] private float beamDamage;
    [SerializeField] private float beamStunTime;
    [SerializeField] private Vector2 beamKnockbackForce;
    [SerializeField] private float beamDirection;
    [SerializeField] private float beamLifetime;
    public float energyRequired = 20f;
    public float abilityCooldown = 1f;
    private int damageType;
    private float beamTimer;
    public bool isHoldable;
    
    [SerializeField] private bool activeHitbox;
    public float beamProc;
    [SerializeField] private float beamProcTimer;
    [SerializeField] private float energyProc;
    [SerializeField] private float procTimes;
    private bool isFacingRightDirection;
    
    private Transform playerTransform;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    
    private RaycastHit2D[] inContact;
    [SerializeField] private BoxCollider2D beamCollider;
    [SerializeField] private GameObject parentObject;
    private PlayerAbility pa;

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        beamTimer = beamLifetime; 
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? beamDirection : -beamDirection + 180f);
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>(); 
        procTimes = beamLifetime / beamProc;
        procTimes = energyRequired / procTimes;
    }

    private void Update()
    {
        Lifetime();
        beamProcTimer -= Time.deltaTime;
        energyProc -= Time.deltaTime;
        pa.currentAbilityCooldown = abilityCooldown * pa.abilityCooldownMult;
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        transform.localEulerAngles = new Vector3(0, 0, 0);
        
        if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor == 1)
        {
            energyProc = beamProc;
            pa.energy -= procTimes;
        } 
        else if (isHoldable && energyProc <= 0 && activeHitbox && typeFactor != 1) energyProc = beamProc;
        
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        HitBox();
    }
    
    public void SetValues(bool facingRight, float direction, int type = 0,float damageMult = 0, float lifetimeMult = 1, float potency = 1)
    {
        isFacingRightDirection = facingRight;
        beamDirection = direction;
        beamLifetime *= lifetimeMult;
        beamKnockbackForce *= potency;
        beamStunTime *= potency;
        beamDamage *= damageMult;
    }
    
    private void HitBox()
    {
        inContact = Physics2D.CapsuleCastAll(transform.position, beamCollider.size, CapsuleDirection2D.Vertical, 0,
            Vector2.zero, 0, LayerMask.GetMask("Enemy", "Player"));
        if (inContact != null && beamProcTimer <= 0)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                        if (damageable != null)
                        {
                            damageable.Damage(beamDamage, new Vector2(beamKnockbackForce.x * transform.right.x, beamKnockbackForce.y * transform.up.y));
                            damageable.Stun(beamStunTime);
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
                            damageable.Damage(beamDamage, beamKnockbackForce * transform.up, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                            damageable.Stun(beamStunTime);
                            PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                        }
                    }
                    break;
                default:
                    foreach (var hit in inContact)
                    {
                        print("trying to hit "+ hit.collider.gameObject.name + " with the layer of " + LayerMask.LayerToName(hit.collider.gameObject.layer));
                        var entity = hit.collider;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                            if (damageable != null)
                            {
                                damageable.Damage(beamDamage, new Vector2(beamKnockbackForce.x * transform.right.x, beamKnockbackForce.y * transform.up.y));
                                damageable.Stun(beamStunTime);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.collider.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(beamDamage, beamKnockbackForce, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                                pDmg.Stun(beamStunTime);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.25f;
                            }
                        }
                        else print("Clueless.");
                    }
                    break;
            }
            beamProcTimer = beamProc;
        }
    }
    
    private void Lifetime()
    {
        if (pa.isHolding && isHoldable || !isHoldable)
        {
            beamTimer -= Time.deltaTime;
            if (beamTimer <= 0)
            {
                pa.isPerformingAbility = false;
                animator.Play(collideAnim.name);
            }
        }
    }

    private void Boom() => Destroy(parentObject);

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, beamCollider.size);
    }
}
