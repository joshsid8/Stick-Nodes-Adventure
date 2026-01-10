using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyAttack : MonoBehaviour
{
    public float styleFactor;

    public bool canFly;
    public bool isFlying;
    public int movesBeforeFlyingCount;
    public int[] movesBeforeFlying = new int[2];
    private string flyTrigger = "FlyJump";

    private bool activeMode; // The enemy attack/coming close to the player
    [Tooltip("When the player runs near")] public bool defensiveCheck; 
    [Tooltip("Certain actions performed if the player isn't close or far")] public bool passiveCheck; // Certain actions performed if the player isnt close or far
    private bool evasiveMode; // The enemy evading the player
    private bool passiveMode;

    [Tooltip("The enemy coming close to the player")] public string[] activeMoves; 
    [Tooltip("Certain actions performed if the player isn't close or far")] public string[] passiveMoves;
    [Tooltip("The enemy evading/backing up from the player")] public string[] evasiveMoves;

    public string[] flyingActiveMoves;
    public string[] flyingPassiveMoves;
    public string[] flyingEvasiveMoves;
    
    public string spareAnimation;
    
    public float defensiveModeRadius;
    public float modDefensiveModeRadius;
    private float originalDefensiveModeRadius;
    
    public float passiveModeRadius;
    public float modPassiveModeRadius;
    private float originalPassiveModeRadius;
    
    public float stunTime;
    public float damageAmount;
    public Vector2 knockbackForce;
    private RaycastHit2D hit;
    public float attackRange;
    public Transform attackTransform;
    public int damageType;
    
    public LayerMask pLayer;
    [HideInInspector] public Transform playerTransform;

    public bool canAttack;
    public bool isAttacking;
    public float currentAttackCooldown;
    public bool stunnable;
    
    [HideInInspector] public Rigidbody2D rb;
    private Animator animator;
    private EnemyPatrol ep;
    private EnemyMovement em;
    private EnemyFly ef;
    private EnemyHealth eh;

    public bool canFacePlayer;
    [SerializeField] private bool playerIsDead;
    [SerializeField] private bool canSpare = true;
    [SerializeField] private float spareTimer = 10;
    private float currentSpareTimer;
    
    // Start is called before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ep = GetComponent<EnemyPatrol>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        em = GetComponent<EnemyMovement>();
        ef = GetComponent<EnemyFly>();
        eh = GetComponent<EnemyHealth>();
        em.enabled = true;
        originalDefensiveModeRadius = defensiveModeRadius;
        originalPassiveModeRadius = passiveModeRadius;
        playerIsDead = false;
        currentSpareTimer = spareTimer;
        playerTransform = PlayerHealth.Instance.gameObject.transform;
    }
    
    private void Update()
    {
        FacePlayer();
        CheckModes();
        StyleFactor();
        CooldownTimer();
        if (passiveMode)
        {
            defensiveModeRadius = modDefensiveModeRadius;
            passiveModeRadius = originalPassiveModeRadius;
        }
        else if (evasiveMode)
        {
            passiveModeRadius = modPassiveModeRadius;
            defensiveModeRadius = 8f;
        }
        else
        {
            passiveModeRadius = originalDefensiveModeRadius;
            defensiveModeRadius = originalDefensiveModeRadius;
        }

        //isMoving = rb.linearVelocity.x > 0 && !ep.isPatrolling ? isMoving : !isMoving;
        
        if (!stunnable) GetComponent<EnemyHealth>().stunMultiplier = 0f;
        else if (stunnable && passiveMode) GetComponent<EnemyHealth>().stunMultiplier = 1.2f;
        else if (stunnable && evasiveMode) GetComponent<EnemyHealth>().stunMultiplier = 1.5f;
        else GetComponent<EnemyHealth>().stunMultiplier = 1f;

        if (PlayerHealth.Instance.isDead && !playerIsDead)
        {
            playerIsDead = true;
            ep.enabled = true;
            ep.isPatrolling = true;
            isFlying = false;
            enabled = false;
        }
        if (PlayerHealth.Instance.isDead! && playerIsDead) playerIsDead = false;
        
        animator.SetBool("isFlying", isFlying);
        ef.enabled = isFlying;
        rb.gravityScale = isFlying ? 0 : 8;
    }
    
    private void CheckModes()
     {
         defensiveCheck = Physics2D.OverlapCircle(transform.position, defensiveModeRadius, pLayer);
         passiveCheck = Physics2D.OverlapCircle(transform.position, passiveModeRadius, pLayer);
         
         if (defensiveCheck) passiveCheck = false;
         else if (passiveCheck) defensiveCheck = false;
         
         if (defensiveCheck && activeMode)
         {
             rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
             if (!activeMode) rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
         }

         if (canSpare)
         {
             if (!defensiveCheck && !passiveCheck) currentSpareTimer -= Time.deltaTime; else currentSpareTimer = spareTimer;
             if (currentSpareTimer <= 0)
             {
                 ep.enabled = true;
                 ep.isPatrolling = true;
                 isFlying = false;
                 enabled = false;
             }
         }
     }

    private void ActiveLogic()
    {
        if (canAttack && !isAttacking && !eh.isStunned)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            if (!defensiveCheck) animator.Play(!isFlying ? activeMoves[Random.Range(0, activeMoves.Length)]
                : flyingActiveMoves[Random.Range(0, flyingActiveMoves.Length)]);
        }
    }

    private void PassiveLogic()
    {
        if (canAttack && !isAttacking && !eh.isStunned)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            if (!passiveCheck && !defensiveCheck) animator.Play(!isFlying ? activeMoves[Random.Range(0, activeMoves.Length)] 
                : flyingActiveMoves[Random.Range(0, flyingActiveMoves.Length)]);
            else if (defensiveCheck && !passiveCheck) animator.Play(!isFlying ? evasiveMoves[Random.Range(0, evasiveMoves.Length)]
                : flyingEvasiveMoves[Random.Range(0, flyingEvasiveMoves.Length)]);
            else animator.Play(!isFlying ? passiveMoves[Random.Range(0, passiveMoves.Length)] 
                : flyingPassiveMoves[Random.Range(0, flyingPassiveMoves.Length)]);
        }
    }

    private void EvasiveLogic()
    {
        if (canAttack && !isAttacking && !eh.isStunned)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            if (defensiveCheck) animator.Play(!isFlying ? evasiveMoves[Random.Range(0, evasiveMoves.Length)]
                : flyingEvasiveMoves[Random.Range(0, flyingEvasiveMoves.Length)]);
            else if (passiveCheck) animator.Play(!isFlying ? passiveMoves[Random.Range(0, passiveMoves.Length)] 
                : flyingPassiveMoves[Random.Range(0, flyingPassiveMoves.Length)]);
            else animator.Play(!isFlying ? activeMoves[Random.Range(0, activeMoves.Length)] 
                : flyingActiveMoves[Random.Range(0, flyingActiveMoves.Length)]);
        }
    }

    private void ActivateCooldown(float atkCD)
    {
        currentAttackCooldown = atkCD;
        if (isFlying) ef.moveSpeed = Math.Abs(rb.linearVelocity.x) / 2;
    }

    void CooldownTimer()
    {
        if (currentAttackCooldown > 0) currentAttackCooldown -= Time.deltaTime;
        canAttack = currentAttackCooldown <= 0 ? canAttack = true : canAttack = false;
    }

    private void Attack()
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, new Vector2(transform.right.x, transform.up.y), 0f, LayerMask.GetMask("Player", "Invincible"));
        if (hit.collider != null)
        {
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(damageAmount, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y), ep.isFacingRight, damageType);
                damageable.Stun(stunTime);
                FindAnyObjectByType<PlayerController>().currentCombatTimer =+ 180f;
            }
        }
    }

    private void FollowupAttack(string attack)
    {
        animator.Play(attack);
    }
    
    private void FlyTrigger()
    {
        movesBeforeFlyingCount++;
        if (movesBeforeFlyingCount == movesBeforeFlying[0] && !isFlying && canFly)
        {
            animator.Play(flyTrigger);
            isFlying = true;
        }

        if (movesBeforeFlyingCount == movesBeforeFlying[1] && canFly)
        {
            isFlying = false;
            movesBeforeFlyingCount = 0;
        }
    }
    
    
    private void MoveDuringFight()
    {
        if (enabled && !em.isDashing && !isAttacking && !ep.isPatrolling && !isFlying)
        {
            var rng = Random.Range(0, 101);
            var rng2 = Random.Range(0, 101);
            if (rng <= 70)
            {
                rb.linearVelocity = new Vector2(rng2 <= 50 ? em.dashSpeed / 2 : -em.dashSpeed / 2, rb.linearVelocity.y);
                animator.Play("Strafe");
            }
        }
    }
    
    private void StyleFactor()
    {
        switch (styleFactor)
        {
            case 1:
                activeMode = true;
                passiveMode = false;
                evasiveMode = false;
                break;
            case 2:
                passiveMode = true;
                activeMode = false;
                evasiveMode = false;
                break;
            case 3:
                evasiveMode = true;
                activeMode = false;
                passiveMode = false;
                GetComponent<EnemyHealth>().stunMultiplier = 0.5f;
                break;
            default:
                activeMode = false;
                passiveMode = false;
                evasiveMode = false;
                Debug.Log("Clueless.");
                break;
        }

        if (activeMode) ActiveLogic();
        else if (passiveMode) PassiveLogic();
        else if (evasiveMode) EvasiveLogic();
        else Debug.Log("So Clueless.");
    }
    
    void FacePlayer()
    {
        if (canFacePlayer)
        {
            if (playerTransform == null) return;

            // Determine direction to player
            float directionToPlayer = playerTransform.position.x - transform.position.x;

            if (directionToPlayer > 0 && !ep.isFacingRight) Flip();
            else if (directionToPlayer < 0 && ep.isFacingRight) Flip();
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, passiveModeRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, defensiveModeRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
    
    void Flip()
    {
        ep.isFacingRight = !ep.isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }
}
