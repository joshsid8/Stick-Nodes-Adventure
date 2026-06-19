using UnityEngine;

public class PlayerShootBeam : MonoBehaviour
{
    [SerializeField] private float beamDamage;
    [SerializeField] private float beamStunTime;
    [SerializeField] private Vector2 beamKnockbackForce;
    [SerializeField] private float beamDirection;
    [SerializeField] private float beamLifetime;
    public float energyRequired = 20f;
    public float abilityCooldown = 1f;
    
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
        
        if (isHoldable && energyProc <= 0 && activeHitbox)
        {
            energyProc = beamProc;
            pa.energy -= procTimes;
        } 
    }
    
    public void SetValues(bool facingRight, float lifetimeMult, float knockbackMult, float direction)
    {
        isFacingRightDirection = facingRight;
        beamLifetime *= lifetimeMult;
        beamKnockbackForce *= knockbackMult;
        beamDirection = direction;
    }
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy") || collision.CompareTag("Boss"))
        {
            if (beamProcTimer <= 0 && activeHitbox)
            {
                beamProcTimer = beamProc;
                if (collision != null)
                {
                    EDamageables damageable = collision.gameObject.GetComponent<EDamageables>();
                    if (damageable != null)
                    {
                        damageable.Damage(beamDamage, new Vector2(beamKnockbackForce.x * transform.right.x, beamKnockbackForce.y * transform.up.y));
                        damageable.Stun(beamStunTime);

                        PlayerHealth.Instance.currentCombatTimer = +
                            PlayerHealth.Instance.combatTimer * 0.5f;
                    }
                }
            }
        }
    }
    
    private void Lifetime()
    {
        if (pa.isHolding && isHoldable || !isHoldable)
        {
            beamTimer -= Time.deltaTime;
            if (beamTimer <= 0 && collideAnim != null)
            {
                pa.isPerformingAbility = false;
                animator.Play(collideAnim.name);
            }
        }
        else if (collideAnim != null)
        {
            pa.isPerformingAbility = false;
            animator.Play(collideAnim.name);
        }
    }

    private void Boom()
    {
        Destroy(parentObject);
    }
}
