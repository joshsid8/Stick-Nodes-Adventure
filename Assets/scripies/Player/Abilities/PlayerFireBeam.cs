using UnityEngine;

public class PlayerFireBeam : MonoBehaviour
{
    public float beamDamage;
    public float beamStunTime;
    public Vector2 beamKnockbackForce;
    public bool isDirectional;
    public Vector2 beamDirection;
    public float beamLifetime;
    private float beamTimer;
    public bool activeHitbox;
    public float beamProc;
    [SerializeField] private float beamProcTimer;
    public int beamDamageType;
    private bool isFacingRightDirection;
    
    private Transform playerTransform;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        beamTimer = beamLifetime;
    }

    private void Update()
    {
        Lifetime();
        beamProcTimer -= Time.deltaTime;
        if (isDirectional) transform.eulerAngles = beamDirection;
    }
    
    public void SetValues(bool facingRight, float lifetime, float proc, Vector2 direction, bool directional)
    {
        isFacingRightDirection = facingRight;
        beamLifetime = lifetime;
        beamProc = proc;
        isDirectional = directional;
        if (isDirectional) beamDirection = direction;
    }
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && activeHitbox)
        {
            if (beamProcTimer <= 0)
            {
                beamProcTimer = beamProc;
                if (collision != null)
                {
                    PDamageable damageable = collision.gameObject.GetComponent<PDamageable>();
                    if (damageable != null)
                    {
                        damageable.Damage(beamDamage, new Vector2(beamKnockbackForce.x * transform.right.x, beamKnockbackForce.y * transform.up.y), isFacingRightDirection, beamDamageType);
                        damageable.Stun(beamStunTime);

                        PlayerHealth.Instance.playerController.currentCombatTimer = +
                            PlayerHealth.Instance.playerController.combatTimer * 0.5f;
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
        Destroy(gameObject);
    }
}
