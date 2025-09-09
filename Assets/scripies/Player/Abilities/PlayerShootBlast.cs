using UnityEngine;
using System.Collections;

public class PlayerShootBlast : MonoBehaviour
{
    [SerializeField] private float projectileSpeed;
    [SerializeField] private float projectileMinSpeed;
    [SerializeField] private float projectileHeight;
    [SerializeField] private float airResistance = 1;
    [SerializeField] private float projectileDamage;
    [SerializeField] private float projectileStunTime;
    [SerializeField] private Vector2 projectileKnockbackForce;
    [SerializeField] private float projectileLifetime = 3;
    public float energyRequired = 20f;
    public float abilityCooldown = 1f;
    
    [SerializeField] private bool randomisation;
    [SerializeField] private float randomRes = 0.1f;
    [SerializeField] private float randomAngle = 2f;
    
    private RaycastHit2D[] hits;
    [SerializeField] private float projectileRange;
    [SerializeField] private LayerMask target;
    private float playerDirection;
    private bool isFacingRightDirection;

    private Rigidbody2D rb;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    private bool collided;

    private void Update()
    {
        if (projectileMinSpeed < projectileSpeed) projectileSpeed -= Time.deltaTime * airResistance;
        if (!collided && projectileSpeed > 0) rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, rb.linearVelocity.y);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name);
        ApplyInitialDirection();
        Destroy(gameObject, projectileLifetime);
        if (randomisation)
        {
            airResistance = Random.Range(airResistance - airResistance * randomRes, airResistance + airResistance * randomRes);
            projectileHeight = Random.Range(-projectileHeight - randomAngle, projectileHeight + randomAngle);
        }
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, projectileHeight);
    }
    public void SetValues(bool facingRight, float damageMult, float stunTimeMult, float knockbackForceMult)
    {
        isFacingRightDirection = facingRight;
        projectileDamage *= damageMult;
        projectileStunTime *= stunTimeMult;
        projectileKnockbackForce *= knockbackForceMult;
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
        collided = true;
        if (collideAnim != null) animator.Play(collideAnim.name);
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        GetComponent<Collider2D>().isTrigger = false;
        Destroy(gameObject, 0.2f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}