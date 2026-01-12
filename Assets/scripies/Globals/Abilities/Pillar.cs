using UnityEngine;

public class Pillar : MonoBehaviour
{
    public int typeFactor;
    
    public float pillarDamage;
    public float pillarStunTime;
    public float pillarLifeTime;
    private float pillarTimer;
    public float energyRequired = 15f;
    public float abilityCooldown = 1f;
    
    public Vector2 knockbackForce;
    
    public float pillarProc;
    [SerializeField] private float pillarProcTimer;
    
    [SerializeField] private bool isErupting;
    private RaycastHit2D[] inContact;
    [SerializeField] private BoxCollider2D pillarCollider;

    [SerializeField] private AnimationClip eruptAnim;
    [SerializeField] private AnimationClip finishAnim;
    private Animator animator;
    private Collider2D coll;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        animator = GetComponent<Animator>();
        if (eruptAnim != null) animator.Play(eruptAnim.name);
        coll = GetComponent<Collider2D>();
    }

    void Update()
    {
        Lifetime();
        pillarProcTimer -= Time.deltaTime;
        coll.enabled = pillarProcTimer <= 0 ? coll.enabled : !coll.enabled;
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        
        Hitbox();
    }
    private void Hitbox()
    {
        inContact = Physics2D.CapsuleCastAll(transform.position, pillarCollider.size, CapsuleDirection2D.Vertical, 0,
            Vector2.zero, 0, LayerMask.GetMask("Enemy", "Player"));
        if (inContact != null && pillarProcTimer <= 0)
        {
            switch (typeFactor)
            {
                case 1:
                    foreach (var hit in inContact)
                    {
                        var damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                        if (damageable != null && isErupting)
                        {
                            damageable.Damage(pillarDamage, knockbackForce * transform.up);
                            damageable.Stun(pillarStunTime);
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
                            damageable.Damage(pillarDamage, knockbackForce * transform.up, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                            damageable.Stun(pillarStunTime);
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
                                eDmg.Stun(pillarStunTime);
                                eDmg.Damage(pillarDamage, knockbackForce);
                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.collider.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(pillarDamage, knockbackForce, !PlayerHealth.Instance.pc.isFacingRight, typeFactor);
                                pDmg.Stun(pillarStunTime);

                                PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else print("Clueless.");
                    }
                    break;
            }
            pillarProcTimer = pillarProc;
        }
    }
    
    public void SetValues(float lifetimeMult = 1, float damageMult = 1, float knockbackMult = 1, float stunTimeMult = 1)
    {
        pillarDamage *= damageMult;
        pillarLifeTime *= lifetimeMult;
        knockbackForce *= knockbackMult;
        pillarStunTime *= stunTimeMult;
    }
    
    private void Lifetime()
    {
        if (isErupting && pillarTimer > 0)
        {
            pillarTimer -= Time.deltaTime;
            if (pillarTimer <= 0 && finishAnim != null)
            {
                isErupting = false;
                animator.Play(finishAnim.name);
            }
        }
    }
    
    void Boom() => Destroy(GetComponent<FindFloor>().bottomPoint.transform.gameObject);
    
    void ApplyTimer() => pillarTimer = pillarLifeTime;
    
}
