using UnityEngine;
using System.Collections;

public class PlayerShootBlast : MonoBehaviour
{
    [SerializeField] private float projectileSpeed;
    [SerializeField] private float projectileDamage;
    [SerializeField] private float projectileStunTime;
    [SerializeField] private Vector2 projectileKnockbackForce;
    public float energyRequired = 20f;
    public float abilityCooldown = 1f;
    
    private RaycastHit2D[] hits;
    [SerializeField] private float projectileRange;
    [SerializeField] private LayerMask target;
    private float playerDirection;

    private Rigidbody2D rb;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    
    // New variable to store the direction passed from PlayerPerformAbility
    private bool isFacingRightDirection;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        Destroy(gameObject, 3f);

        // Use the passed-in direction to set rotation and initial velocity
        ApplyInitialDirection();
    }
    public void SetValues(bool facingRight, float damage, float stunTime, Vector2 knockbackForce)
    {
        isFacingRightDirection = facingRight;
        projectileDamage *= damage;
        projectileStunTime *= stunTime;
        projectileKnockbackForce *= knockbackForce;
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
        if (collision.CompareTag("Enemy") || collision.CompareTag("Boss")) HitEntity();
        if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("EVFX")) Collide();
    }

    private void HitEntity()
    {
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
        Collide();
    }

    public void Collide()
    {
        if (collideAnim != null) animator.Play(collideAnim.name);
        rb.linearVelocity = Vector2.zero;
        Destroy(gameObject, 0.2f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}