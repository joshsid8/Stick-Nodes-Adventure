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
    public Vector2 knockbackForce;
    private RaycastHit2D hit;
    [SerializeField] private float attackRange;
    public Transform attackTransform;
    
    // Checks
    [HideInInspector] public bool isInRange;
    [HideInInspector] public bool freezeMovement;
    [HideInInspector] public bool isFalling;
    [SerializeField] private int damageType;
    
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
    
    private bool unavailable;
    [SerializeField] private LayerMask terrainLayers;
    
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
    private BossHealth bh;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        bh = GetComponent<BossHealth>();
        
        originalProjectileDirection = projectileDirection;
        reverseProjectileDirection = -projectileDirection;
    }

    // Update is called once per frame
    private void Update()
    {
        Dash();
        FlyDash();

        isInRange = Physics2D.OverlapCircle(transform.position, attackRange * 1.5f, ba.playerLayer);
        rb.linearVelocity = freezeMovement ? Vector2.zero : new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        //dashDirection = (ba.playerTransform.position - transform.position).normalized;
        
        isFalling = rb.linearVelocity.y < 0;
        
        animator.SetBool("isFalling", isFalling);
        
        animator.speed = bh.phase2 ? 1.15f : 1;
        
        if (ba.isFacingRight) projectileDirection = new Vector2(reverseProjectileDirection.x, projectileDirection.y);
        else projectileDirection = new Vector2(originalProjectileDirection.x, projectileDirection.y);
        
        unavailable = Physics2D.OverlapCircle(projectilePoint.position, 0.5f, terrainLayers);

    }
    
    private void Dash()
    {
        if (isDashing) rb.linearVelocity = ba.isFacingRight ? new Vector2(dashSpeed, rb.linearVelocity.y) :
            new Vector2(-dashSpeed, rb.linearVelocity.y);
        else if (!isDashing) rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }

    private void FindPlayerPosition()
    {
        var rng = Random.Range(0, 101);
        var prediction = rng <= 70;
        if (!prediction) dashDirection = (ba.playerTransform.position - transform.position).normalized;
        else
        {
            var x = PlayerHealth.Instance.pc.rb.linearVelocity.x;
            var y = PlayerHealth.Instance.pc.rb.linearVelocity.y;
            var predictedDistanceX = x > 0 ? x * 0.2f : x * 0.2f * -1;
            var predictedDistanceY = y > 0 ? y * 0.2f : y * 0.2f * -1;
            dashDirection = new Vector2(ba.playerTransform.position.x + predictedDistanceX - transform.position.x,
                ba.playerTransform.position.y + predictedDistanceY - transform.position.y).normalized;
        }
    }
    
    private void FlyDash()
    {
        if (isFlyDashing)
        {
            rb.linearVelocity = bh.phase2 ? dashDirection * dashSpeed * 1.2f : dashDirection * dashSpeed;
            if (isInRange)
            {
                isFlyDashing = false;
                animator.SetTrigger("HAttack");
                ba.ActionCooldown(dashCooldown);
            }
        }
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }
    
    public void ProjectileShoot()
    {
        var newProjectile = Instantiate(projectile, projectilePoint.position, projectilePoint.rotation);
        var fireballScript = newProjectile.GetComponent<EnemyFireProjectile>();
        if (fireballScript != null)
            fireballScript.SetValues(ba.isFacingRight, projectileSpeed, projectileDirection, false, damageType);
        else 
            Debug.LogError("where is bro's ability");
        
        ba.ActionCooldown(throwCooldown);
    }
    
    public void SecondaryProjectileShoot()
    {
        var newProjectile = Instantiate(projectile, projectilePoint.position, projectilePoint.rotation);
        var fireballScript = newProjectile.GetComponent<EnemyFireProjectile>();
        if (fireballScript != null)
        {
            var rng = Random.Range(-2.0f, 2.0f);
            projectileDirection = new Vector2(projectileDirection.x + rng, projectileDirection.y);
            fireballScript.SetValues(ba.isFacingRight, secondaryProjectileSpeed, projectileDirection, true, damageType);
        }
        else Debug.LogError("where is bro's ability");
        ba.ActionCooldown(throwCooldown);
    }

    private void Stomp()
    {
        if (!unavailable)
        {
            if (ba.isFacingRight)
                Instantiate(stationaryElement, projectilePoint.position + new Vector3(xVector, -1f, 0f),
                    Quaternion.identity);
            else
                Instantiate(stationaryElement, projectilePoint.position + new Vector3(-xVector, -1f, 0f),
                    Quaternion.identity);
        }
    }

    private void Slash()
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, new Vector2(transform.right.x, transform.up.y), 0f, LayerMask.GetMask("Player"));
        if (hit.collider != null)
        {
            ba.ActionCooldown(slashCooldown * 0.5f);
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(damageAmount, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y), ba.isFacingRight, damageType);
                damageable.Stun(stunTime);
                FindAnyObjectByType<PlayerController>().currentCombatTimer =+ 180f;
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
            var rng = Random.Range(0, 101);
            if (rng <= 40)
            {
                animator.SetTrigger("Homing");
                ba.ActionCooldown(3f);
            }
        }
    }
    

    private void Teleport()
    {
        animator.SetTrigger("DAttack");
        var rng = Random.Range(0, 101);
        var x = rng <= 50 ? 3 : -3;
        transform.position = new Vector3(ba.playerTransform.position.x + x, teleportPoint.position.y, transform.position.z);
        
        var rng2 = Random.Range(0, 101);
        var px = PlayerHealth.Instance.pc.rb.linearVelocity.x;
        var predictedDistanceX = px > 0 ? px * 0.2f : px * 0.2f * -1;
        if (rng2 <= 50 || px == 0) rb.linearVelocity = new Vector2(rb.linearVelocity.x + predictedDistanceX, !bh.phase2 ? -dropSpeed * 1.5f : -dropSpeed * 2f);
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, !bh.phase2 ? -dropSpeed * 1.5f : -dropSpeed * 2f);
    }

    private void FreezeMovement()
    {
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
}
