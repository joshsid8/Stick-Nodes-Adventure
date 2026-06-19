using UnityEngine;

public class EnemyBeam : MonoBehaviour
{
    public float beamDamage;
    public float beamStunTime;
    public Vector2 beamKnockbackForce;
    public bool isDirectional;
    public float beamDirection;
    public float beamLifetime;
    private float beamTimer;
    public bool activeHitbox;
    public float beamProc;
    [SerializeField] private float beamProcTimer;
    public int beamDamageType;
    
    private Transform playerTransform;
    private bool isFacingRightDirection;
    private float playerDirection;
    [SerializeField] private GameObject parentObject;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        beamTimer = beamLifetime;
        ApplyInitialDirection();
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? beamDirection : -beamDirection + 180f);
    }

    private void Update()
    {
        Lifetime();
        beamProcTimer -= Time.deltaTime;
    }

    public void SetValues(bool facingRight, float lifetime, float proc, float direction, bool directional, int damageType)
    {
        isFacingRightDirection = facingRight;
        beamLifetime = lifetime;
        beamProc = proc;
        isDirectional = directional;
        beamDirection = direction;
        beamDamageType = damageType;
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
                        damageable.Damage(beamDamage, beamKnockbackForce * playerDirection, isFacingRightDirection, beamDamageType);
                        damageable.Stun(beamStunTime);

                        GameObject player = GameObject.FindGameObjectWithTag("Player");
                        PlayerHealth.Instance.currentCombatTimer = +
                            PlayerHealth.Instance.combatTimer * 0.5f;
                    }
                }
            }
        }
    }

    private void ApplyInitialDirection()
    {
        if (isFacingRightDirection && !isDirectional)
        {
            playerDirection = 1f;
        }
        else if (!isFacingRightDirection && !isDirectional)
        {
            playerDirection = -1f;
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
