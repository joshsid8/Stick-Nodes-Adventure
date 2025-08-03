using UnityEngine;

public class EnemyFirePillar : MonoBehaviour
{
    public float fireDamage;
    public float pillarLifeTime;
    private float pillarTimer;
    
    public Vector2 knockbackForce;
    public int pillarDamageType;
    bool playerFacingRight;

    private bool isErupting = true;
    Animator animator;
    [SerializeField] private AnimationClip eruptAnim;
    [SerializeField] private AnimationClip finishAnim;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        animator = GetComponent<Animator>();
        if (eruptAnim != null) animator.Play(eruptAnim.name);
        pillarTimer = pillarLifeTime;
    }

    // Update is called once per frame
    void Update()
    {
        playerFacingRight = !PlayerHealth.Instance.playerController.isFacingRight;
        Lifetime();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PDamageable damageable = collision.gameObject.GetComponent<PDamageable>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(fireDamage, knockbackForce * transform.up, playerFacingRight, pillarDamageType);
                damageable.Stun(0f);
                PlayerHealth.Instance.playerController.currentCombatTimer = +
                    PlayerHealth.Instance.playerController.combatTimer * 0.5f;
            }
        }
    }
    
    private void Lifetime()
    {
        pillarTimer -= Time.deltaTime;
        if (pillarTimer <= 0 && finishAnim != null)
        {
            isErupting = false;
            animator.Play(finishAnim.name);
        }
    }
    
    void Boom()
    {
        Destroy(gameObject);
    }

}
