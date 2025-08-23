using UnityEngine;

public class PlayerFirePillar : MonoBehaviour
{
    public float pillarDamage;
    public float pillarLifeTime;
    private float pillarTimer;
    
    public Vector2 knockbackForce;
    bool isFacingRight;
    
    public float pillarProc;
    [SerializeField] private float pillarProcTimer;
    
    [SerializeField] private bool isErupting;
    [SerializeField] private RaycastHit2D[] inContact;
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
        Hitbox();
    }
    private void Hitbox()
    {
        inContact = Physics2D.CapsuleCastAll(transform.position, pillarCollider.size, CapsuleDirection2D.Vertical, 0,
            Vector2.zero, 0, LayerMask.GetMask("Enemy", "Boss"));
        if (inContact != null && pillarProcTimer <= 0)
        {
            pillarProcTimer = pillarProc;
            foreach (RaycastHit2D hit in inContact)
            {
                EDamageables damageable = hit.collider.gameObject.GetComponent<EDamageables>();
                if (damageable != null && isErupting)
                {
                    damageable.Damage(pillarDamage, knockbackForce * transform.up);
                    damageable.Stun(0f);
                    PlayerHealth.Instance.playerController.currentCombatTimer = +
                        PlayerHealth.Instance.playerController.combatTimer * 0.5f;
                }
            }
        }
    }
    
    public void SetValues(float lifetimeMult, float damage, float procMult, Vector2 knockback)
    {
        pillarDamage = damage;
        pillarLifeTime *= lifetimeMult;
        pillarProc *= procMult;
        knockbackForce = knockback;
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
    
    void Boom()
    {
        Destroy(gameObject);
    }
    void ApplyTimer()
    {
        pillarTimer = pillarLifeTime;
    }
}
