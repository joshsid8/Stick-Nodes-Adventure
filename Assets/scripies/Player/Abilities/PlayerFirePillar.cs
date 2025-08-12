using UnityEngine;

public class PlayerFirePillar : MonoBehaviour
{
    public float pillarDamage;
    public float pillarLifeTime;
    private float pillarTimer;
    
    public Vector2 knockbackForce;
    public int pillarDamageType;
    bool isFacingRight;
    
    public float pillarProc;
    [SerializeField] private float pillarProcTimer;
    
    [SerializeField] private bool isErupting;
    [SerializeField] private AnimationClip eruptAnim;
    [SerializeField] private AnimationClip finishAnim;
    [SerializeField] private LayerMask enemyLayer;
    private Animator animator;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        if (eruptAnim != null) animator.Play(eruptAnim.name);
        pillarTimer = pillarLifeTime;
    }

    void Update()
    {
        Lifetime();
        pillarProcTimer -= Time.deltaTime;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.layer == enemyLayer && pillarProcTimer <= 0)
        {
            pillarProcTimer = pillarProc;
            EDamageables damageable = collision.gameObject.GetComponent<EDamageables>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(pillarDamage, knockbackForce * transform.up);
                damageable.Stun(0f);
                PlayerHealth.Instance.playerController.currentCombatTimer = +
                    PlayerHealth.Instance.playerController.combatTimer * 0.5f;
            }
        }
    }
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.layer == enemyLayer && pillarProcTimer <= 0)
        {
            pillarProcTimer = pillarProc;
            EDamageables damageable = collision.gameObject.GetComponent<EDamageables>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(pillarDamage, knockbackForce * transform.up);
                damageable.Stun(0f);
                PlayerHealth.Instance.playerController.currentCombatTimer = +
                    PlayerHealth.Instance.playerController.combatTimer * 0.5f;
            }
        }
    }
    
    public void SetValues(float lifetime, float damage, float proc, Vector2 knockback)
    {
        pillarDamage = damage;
        pillarLifeTime = lifetime;
        pillarProc = proc;
        knockbackForce = knockback;
    }
    
    private void Lifetime()
    {
        pillarTimer -= Time.deltaTime;
        if (pillarTimer <= 0 && finishAnim != null) animator.Play(finishAnim.name);
    }
    
    void Boom()
    {
        Destroy(gameObject);
    }
}
