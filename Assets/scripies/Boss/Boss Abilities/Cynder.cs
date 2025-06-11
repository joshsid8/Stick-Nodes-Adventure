using System.Collections;
using UnityEngine;

public class Cynder : MonoBehaviour
{
    // Dashing
    public float dashCooldown;
    public float dashSpeed = 10f;
    public bool isDashing;
    
    private Vector2 dashDirection;
    public bool isFlyDashing;
    
    // Shing shing
    public float slashCooldown;
    public float stunTime;
    public float damageAmount;
    private RaycastHit2D hit;
    [SerializeField] private float attackRange;
    public Transform attackTransform;
    
    // Checks
    [HideInInspector] public bool isInRange;
    [HideInInspector] public bool freezeMovement;
    [HideInInspector] public bool isFalling;
    
    // Throwing stuff and stomping
    public float throwCooldown;
    public Transform projectilePoint;
    public GameObject projectile;
    public float projectileSpeed;
    [HideInInspector] public float xVector;
    public GameObject stationaryElement;
    
    public float secondaryProjectileSpeed;
    private Vector2 originalProjectileDirection;
    public Vector2 projectileDirection;
    private Vector2 reverseProjectileDirection;
    
    // Jumping
    public float jumpCooldown;
    public bool isRandomised;
    public float jumpForceY;
    public float jumpForceX;

    public float backflipForceY;
    public float backflipForceX;
    
    // Teleporting and dropping
    public Transform teleportPoint;
    public float dropSpeed;
    
    // Components
    private Rigidbody2D rb;
    private Animator animator;
    private BossAttack ba;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        originalProjectileDirection = projectileDirection;
        reverseProjectileDirection = -projectileDirection;
    }

    // Update is called once per frame
    private void Update()
    {
        Dash();
        FlyDash();
        isInRange = Physics2D.OverlapCircle(transform.position, attackRange * 1.5f, ba.playerLayer);
        if (freezeMovement) rb.linearVelocity = Vector2.zero;
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        dashDirection = (ba.playerTransform.position - transform.position).normalized;
        if (rb.linearVelocity.y < 0) isFalling = true; else isFalling = false;
        animator.SetBool("isFalling", isFalling);
        if (ba.isFacingRight)
        {
            projectileDirection = new Vector2(reverseProjectileDirection.x, projectileDirection.y);
        }

        if (!ba.isFacingRight)
        {
            projectileDirection = new Vector2(originalProjectileDirection.x, projectileDirection.y);
        }
    }
    
    private void Dash()
    {
        if (isDashing)
        {
            rb.linearVelocity = ba.isFacingRight ? new Vector2(dashSpeed, rb.linearVelocity.y) : new Vector2(-dashSpeed, rb.linearVelocity.y);
        }
        else if (!isDashing) rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }

    private void FlyDash()
    {
        if (isFlyDashing)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            player.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            rb.linearVelocity = dashDirection * dashSpeed;
            if (isInRange)
            {
                isFlyDashing = false;
                animator.SetTrigger("HAttack");
                ba.ActionCooldown(dashCooldown);
            }
        }

        if (!isFlyDashing)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            player.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
        }
    }
    
    public void ProjectileShoot()
    {
        GameObject newProjectile = Instantiate(projectile, projectilePoint.position, projectilePoint.rotation);
        EnemyFireBall fireballScript = newProjectile.GetComponent<EnemyFireBall>();
        if (fireballScript != null)
        {
            fireballScript.SetValues(ba.isFacingRight, projectileSpeed, projectileDirection, false);
        }
        else
        {
            Debug.LogError("where is bro's ability");
        }
        ba.ActionCooldown(throwCooldown);
    }
    
    public void SecondaryProjectileShoot()
    {
        GameObject newProjectile = Instantiate(projectile, projectilePoint.position, projectilePoint.rotation);
        EnemyFireBall fireballScript = newProjectile.GetComponent<EnemyFireBall>();
        if (fireballScript != null)
        {
            float rng = Random.Range(-2.0f, 2.0f);
            projectileDirection = new Vector2(projectileDirection.x + rng, projectileDirection.y);
            fireballScript.SetValues(ba.isFacingRight, secondaryProjectileSpeed, projectileDirection, true);
        }
        else
        {
            Debug.LogError("where is bro's ability");
        }
        ba.ActionCooldown(throwCooldown);
    }
    private void Stomp()
    {
        if (ba.isFacingRight) Instantiate(stationaryElement, projectilePoint.position + new Vector3(xVector, -1f, 0f), Quaternion.identity);
        else Instantiate(stationaryElement, projectilePoint.position + new Vector3(-xVector, -1f, 0f), Quaternion.identity);
    }
    
    private void Slash()
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, transform.right, 0f, LayerMask.GetMask("Player"));
        if (hit.collider != null)
        {
            ba.ActionCooldown(slashCooldown * 0.5f);
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(damageAmount);
                damageable.Stun(stunTime);
            }
        }
        ba.ActionCooldown(slashCooldown * 0.5f);
        
    }

    private void Jump()
    {
        if (ba.isGrounded)
        {
            if (isRandomised)
            {
                jumpForceX = Random.Range(-16, 16);
                jumpForceY = Random.Range(20, 31);
            }

            rb.linearVelocity = new Vector2(jumpForceX, jumpForceY);
            ba.ActionCooldown(jumpCooldown);
        }
    }
    
    private void BackFlip()
    {
        rb.linearVelocity = ba.isFacingRight ? new Vector2(-backflipForceX, backflipForceY) : new Vector2(backflipForceX, backflipForceY);
    }

    private void Gamble()
    {
        if (ba.directionToPlayer >= 8f || ba.directionToPlayer <= -8f)
        {
            int rng = Random.Range(0, 101);
            if (rng <= 20)
            {
                animator.SetTrigger("Homing");
                ba.ActionCooldown(3f);
            }
        }
    }
    

    private void Teleport()
    {
        animator.SetTrigger("DAttack");
        transform.position = new Vector3(ba.playerTransform.position.x + 2f, teleportPoint.position.y, transform.position.z);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, -dropSpeed);
    }

    private void FreezeMovement()
    {
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
}
