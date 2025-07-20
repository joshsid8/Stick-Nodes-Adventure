using UnityEngine;
using System.Collections;

public class PlayerFireBall : MonoBehaviour
{
    public bool collided;
    public float projectileSpeed;
    private float projectileDamage;
    private float projectileStunTime;
    public Vector2 projectileKnockbackForce;
    private RaycastHit2D[] hits;
    [SerializeField] private float projectileRange;
    public LayerMask target;
    private float playerDirection;

    private Rigidbody2D rb;
    private Animator animator;

    // New variable to store the direction passed from PlayerFireProjectile
    private bool isFacingRightDirection;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        collided = false;
        
        Destroy(gameObject, 5f);

        // Use the passed-in direction to set rotation and initial velocity
        ApplyInitialDirection();
    }
    public void SetValues(bool facingRight, float damage, float stunTime, Vector2 knockbackForce)
    {
        isFacingRightDirection = facingRight;
        projectileDamage = damage;
        projectileStunTime = stunTime;
    }

    private void ApplyInitialDirection()
    {
        if (isFacingRightDirection)
        {
            transform.eulerAngles = new Vector3(0, 0, 90);
            playerDirection = 1f;
        }
        else
        {
            transform.eulerAngles = new Vector3(0, 0, -90);
            playerDirection = -1f;
        }
        // Apply initial velocity based on the direction
        rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, rb.linearVelocity.y);
    }
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.tag == "Enemy" || collision.gameObject.tag == "Boss")
        {
            Collided();
        }
        else if (collision.gameObject.tag == "Terrain" || collision.gameObject.layer == LayerMask.NameToLayer("EVFX"))
        {
            Destroy(gameObject);
        }
    }

    private void Collided()
    {
        collided = true;
        rb.linearVelocity = Vector2.zero;
        hits = Physics2D.CircleCastAll(transform.position, projectileRange, transform.right, 0f, target);

        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();

            if (eDamageable != null)
            {
                eDamageable.Stun(projectileStunTime);
                eDamageable.Damage(projectileDamage, projectileKnockbackForce * playerDirection);
            }
        }
        // Destroy the fireball after it has performed its collision logic (e.g., dealt damage)
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}