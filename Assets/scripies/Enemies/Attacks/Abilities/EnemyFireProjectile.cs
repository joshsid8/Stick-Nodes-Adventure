using System;
using UnityEngine;
using System.Collections;

public class EnemyFireProjectile : MonoBehaviour
{
    public GameObject projectile; 
    public float projectileSpeed;
    public float projectileDamage;
    public float projectileStunTime;
    public int projectileDamageType;
    public Vector2 projectileKnockbackForce;
    public bool isDirectional;
    public Vector2 projectileDirection;
    public float projectileLifeTime = 3f;
    
    private RaycastHit2D hit;
    [SerializeField] private float projectileRange;
    public LayerMask target;
    
    private Transform playerTransform; 
    private bool isFacingRightDirection;
    private float playerDirection;
    private Rigidbody2D rb;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb =  GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name); 
        Destroy(gameObject, projectileLifeTime);
        
        transform.eulerAngles = new Vector3(0, 0, 0);
        ApplyInitialDirection();
    }

    public void SetValues(bool facingRight, float speed, Vector2 direction, bool directional, int damageType)
    {
        isFacingRightDirection = facingRight;
        projectileSpeed = speed;
        isDirectional = directional;
        if (isDirectional) projectileDirection = direction;
        projectileDamageType = damageType;
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            Collided();
        }
        else if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("PVFX"))
        {
            if (collideAnim != null) animator.Play(collideAnim.name);
            rb.linearVelocity = Vector2.zero;
            Destroy(gameObject, 0.2f);
        }
    }

    private void ApplyInitialDirection()
    {
        if (isFacingRightDirection && !isDirectional)
        {
            transform.eulerAngles = new Vector3(0, 0, 90);
            playerDirection = 1f;
        }
        else if (!isFacingRightDirection && !isDirectional)
        {
            transform.eulerAngles = new Vector3(0, 0, -90);
            playerDirection = -1f;
        }
        else transform.eulerAngles = new Vector3(projectileDirection.x, projectileDirection.y, 0);
        // Apply initial velocity based on the direction
        if (!isDirectional) rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, rb.linearVelocity.y);
        else
        {
            if (isFacingRightDirection) rb.linearVelocity = projectileDirection * projectileSpeed;
            else
            {
                rb.linearVelocity = projectileDirection * projectileSpeed;
            }
        }
    }
        
    private void Collided()
    {
        hit = Physics2D.CircleCast(transform.position, projectileDamage, transform.right, 0f, target);
        if (hit.collider != null)
        {
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(projectileDamage, projectileKnockbackForce * playerDirection, isFacingRightDirection, projectileDamageType);
                damageable.Stun(projectileStunTime);

                GameObject player = GameObject.FindGameObjectWithTag("Player");
                player.GetComponent<PlayerController>().currentCombatTimer =+
                    player.GetComponent<PlayerController>().combatTimer * 0.5f;
            }
        }
        if (collideAnim != null) animator.Play(collideAnim.name);
        rb.linearVelocity = Vector2.zero;
        Destroy(gameObject, 0.2f);;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}
