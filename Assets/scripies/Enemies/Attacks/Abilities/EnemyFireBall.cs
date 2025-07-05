using System;
using UnityEngine;
using System.Collections;

public class EnemyFireBall : MonoBehaviour
{
    public bool collided;
    public GameObject projectile; 
    public float projectileSpeed;
    private float projectileOriginalSpeed;
    private float projectileReverseSpeed;
    public float projectileDamage;
    public float projectileStunTime;
    public bool isDirectional;
    public Vector2 projectileDirection;
    
    private RaycastHit2D hit;
    [SerializeField] private float projectileRange;
    public LayerMask target;
    
    private Transform playerTransform;
    private EnemyAttack ea;
    private bool isFacingRightDirection;
    private Rigidbody2D rb;
    private Animator animator;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb =  GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        ea = GetComponent<EnemyAttack>();
        collided = false;
        Destroy(gameObject, 5f);
        projectileOriginalSpeed = projectileSpeed;
        projectileReverseSpeed = -projectileSpeed;
        
        transform.eulerAngles = new Vector3(0, 0, 0);
        ApplyInitialDirection();
    }

    public void SetValues(bool facingRight, float speed, Vector2 direction, bool directional)
    {
        isFacingRightDirection = facingRight;
        projectileSpeed = speed;
        isDirectional = directional;
        if (isDirectional) projectileDirection = direction;
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.tag == "Player")
        {
            Collided();
        }
        else if (collision.gameObject.tag == "Terrain" || collision.gameObject.layer == LayerMask.NameToLayer("PVFX"))
        {
            Destroy(gameObject);
        }
    }

    private void ApplyInitialDirection()
    {
        if (isFacingRightDirection && !isDirectional)
        {
            transform.eulerAngles = new Vector3(0, 0, 90);
        }
        else if (!isFacingRightDirection && !isDirectional)
        {
            transform.eulerAngles = new Vector3(0, 0, -90);
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
        collided = true;
        hit = Physics2D.CircleCast(transform.position, projectileDamage, transform.right, 0f, target);
        if (hit.collider != null)
        {
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(projectileDamage);
                damageable.Stun(projectileStunTime);

                GameObject player = GameObject.FindGameObjectWithTag("Player");
                player.GetComponent<PlayerController>().currentCombatTimer =+
                    player.GetComponent<PlayerController>().combatTimer * 0.5f;
            }
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}
