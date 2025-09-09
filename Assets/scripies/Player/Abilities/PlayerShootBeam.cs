using UnityEngine;

public class PlayerShootBeam : MonoBehaviour
{
    [SerializeField] private float beamDamage;
    [SerializeField] private float beamStunTime;
    [SerializeField] private Vector2 beamKnockbackForce;
    [SerializeField] private bool isDirectional;
    [SerializeField] private float beamDirection;
    [SerializeField] private float beamLifetime;
    public float energyRequired = 20f;
    public float abilityCooldown = 1f;
    
    private float beamTimer;
    [SerializeField] private bool activeHitbox;
    public float beamProc;
    [SerializeField] private float beamProcTimer;
    public int beamDamageType;
    private bool isFacingRightDirection;
    
    private Transform playerTransform;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    
    [SerializeField] private GameObject parentObject;

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        beamTimer = beamLifetime;
        if (isDirectional) parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? beamDirection : -beamDirection + 180f);
    }

    private void Update()
    {
        Lifetime();
        beamProcTimer -= Time.deltaTime;
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        if (isDirectional) transform.localEulerAngles = new Vector3(0, 0, 0);
    }
    
    public void SetValues(bool facingRight, float lifetimeMult, float knockbackMult, float direction, bool directional)
    {
        isFacingRightDirection = facingRight;
        beamLifetime *= lifetimeMult;
        beamKnockbackForce *= knockbackMult;
        isDirectional = directional; 
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

                        PlayerHealth.Instance.pc.currentCombatTimer = +
                            PlayerHealth.Instance.pc.combatTimer * 0.5f;
                    }
                }
            }
        }
    }
    
    private void Lifetime()
    {
        beamTimer -= Time.deltaTime;
        if (beamTimer <= 0 && collideAnim != null) animator.Play(collideAnim.name);
    }

    void Boom()
    {
        Destroy(parentObject);
    }
}
