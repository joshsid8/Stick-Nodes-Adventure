using UnityEngine;

public class EnemyBeam : MonoBehaviour
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
    public int beamDamageType;
    [SerializeField] private float beamProcTimer;

    
    private Transform playerTransform;
    private bool isFacingRightDirection;
    private float playerDirection;
    private Rigidbody2D rb;
    private Animator animator;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb =  GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        animator.SetBool("isShooting", true);
        beamTimer = beamLifetime;
        ApplyInitialDirection();
    }

    private void Update()
    {
        Lifetime();
        beamProcTimer -= Time.deltaTime;
    }

    public void SetValues(bool facingRight, float lifetime, float proc, Vector2 direction, bool directional, int damageType)
    {
        isFacingRightDirection = facingRight;
        beamLifetime = lifetime;
        beamProc = proc;
        isDirectional = directional;
        if (isDirectional) beamDirection = direction;
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
                        player.GetComponent<PlayerController>().currentCombatTimer = +
                            player.GetComponent<PlayerController>().combatTimer * 0.5f;
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
        else transform.eulerAngles = beamDirection;
    }

    private void Lifetime()
    {
        beamTimer -= Time.deltaTime;
        if (beamTimer <= 0) animator.SetBool("isShooting", false);
    }

    void Boom()
    {
        Destroy(gameObject);
    }
}
