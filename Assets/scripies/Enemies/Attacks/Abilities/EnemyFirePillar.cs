using UnityEngine;

public class EnemyFirePillar : MonoBehaviour
{
    public float pillarDamage;
    public float pillarLifeTime;
    private float pillarTimer;
    
    public Vector2 knockbackForce;
    public int pillarDamageType;
    bool playerFacingRight;
    
    public float pillarProc;
    [SerializeField] private float pillarProcTimer;
    
    [SerializeField] private bool isErupting;
    Animator animator;
    [SerializeField] private AnimationClip eruptAnim;
    [SerializeField] private AnimationClip finishAnim;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    { animator = GetComponent<Animator>();
        if (eruptAnim != null) animator.Play(eruptAnim.name);
    }

    // Update is called once per frame
    void Update()
    {
        playerFacingRight = !PlayerHealth.Instance.playerController.isFacingRight;
        Lifetime();
        pillarProcTimer -= Time.deltaTime;
    }
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && pillarProcTimer <= 0)
        {
            pillarProcTimer = pillarProc;
            PDamageable damageable = collision.gameObject.GetComponent<PDamageable>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(pillarDamage, knockbackForce * transform.up, playerFacingRight, pillarDamageType);
                damageable.Stun(0f);
                PlayerHealth.Instance.playerController.currentCombatTimer = +
                    PlayerHealth.Instance.playerController.combatTimer * 0.5f;
            }
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && pillarProcTimer <= 0)
        {
            pillarProcTimer = pillarProc;
            PDamageable damageable = collision.gameObject.GetComponent<PDamageable>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(pillarDamage, knockbackForce * transform.up, playerFacingRight, pillarDamageType);
                damageable.Stun(0f);
                PlayerHealth.Instance.playerController.currentCombatTimer = +
                    PlayerHealth.Instance.playerController.combatTimer * 0.5f;
            }
        }
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
