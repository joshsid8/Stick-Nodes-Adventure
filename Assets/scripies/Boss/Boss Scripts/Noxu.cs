using System.Collections;
using UnityEngine;

public class Noxu : MonoBehaviour
{
    // Movement
    public float strafeSpeed = 10f;
    public bool isStrafing;
    
    // Melees
    public float meleeCooldown;
    public float stunTime;
    public Vector2 knockbackForce;
    private RaycastHit2D hit;
    [SerializeField] private float attackRange;
    public Transform attackTransform;
    
    // Throwing stuff and stomping
    public float throwCooldown;
    public Transform projectilePoint;
    public GameObject projectile;
    public float projectileSpeed;
    [HideInInspector] public float xVector;
    public GameObject stationaryElement;
    private Vector2 originalProjectileDirection;
    public Vector2 projectileDirection;
    private Vector2 reverseProjectileDirection;
    
    // Checks
    [HideInInspector] public bool freezeMovement;
    [SerializeField] private int damageType;
    
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
        Strafe();
        if (freezeMovement) rb.linearVelocity = Vector2.zero;
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        
        if (ba.isFacingRight)
        {
            projectileDirection = new Vector2(reverseProjectileDirection.x, projectileDirection.y);
        }
        if (!ba.isFacingRight)
        {
            projectileDirection = new Vector2(originalProjectileDirection.x, projectileDirection.y);
        }
    }
    
    private void Strafe()
    {
        if (isStrafing)
        {
            rb.linearVelocity = ba.isFacingRight ? new Vector2(strafeSpeed, rb.linearVelocity.y) : new Vector2(-strafeSpeed, rb.linearVelocity.y);
        }
        else if (!isStrafing) rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }
    
    private void ProjectileShoot()
    {
        GameObject newProjectile = Instantiate(projectile, projectilePoint.position, projectilePoint.rotation);
        EnemyFireProjectile fireballScript = newProjectile.GetComponent<EnemyFireProjectile>();
        if (fireballScript != null)
        {
            fireballScript.SetValues(ba.isFacingRight, projectileSpeed, projectileDirection, false, damageType);
        }
        else
        {
            Debug.LogError("where is bro's ability");
        }
        ba.ActionCooldown(throwCooldown);
    }
    
    private IEnumerator Stomp(int amount)
    {
        for (int i = 0; i < amount; i++)
        {
            if (ba.isFacingRight) Instantiate(stationaryElement, projectilePoint.position + new Vector3(xVector, -1f, 0f), Quaternion.identity);
            else Instantiate(stationaryElement, projectilePoint.position + new Vector3(-xVector, -1f, 0f), Quaternion.identity);
            xVector += 5f;
            yield return new WaitForSeconds(0.2f);
        }
        xVector = 5f;
    }
    
    private void MeleeAttack(float dmg)
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, transform.right, 0f, LayerMask.GetMask("Player"));
        if (hit.collider != null)
        {
            ba.ActionCooldown(meleeCooldown * 0.5f);
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(dmg, knockbackForce * transform.right, ba.isFacingRight, damageType);
                damageable.Stun(stunTime);
                FindAnyObjectByType<PlayerController>().currentCombatTimer =+ 180f;
            }
        }
        ba.ActionCooldown(meleeCooldown * 0.5f);
        
    }

    private void Gamble()
    {
        int rng = Random.Range(0, 3);
        animator.SetInteger("factor", rng);
    }

    private void FreezeMovementX()
    {
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
}
