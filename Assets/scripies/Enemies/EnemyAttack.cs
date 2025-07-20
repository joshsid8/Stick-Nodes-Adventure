using System.Collections;
using UnityEngine;


public class EnemyAttack : MonoBehaviour
{
    public float styleFactor;

    private bool activeMode; // The enemy attack/coming close to the player
    public bool defensiveCheck; // When the player runs near
    public bool passiveCheck; // Certain action performed if the player isnt close or far
    private bool evasiveMode; // The enemy evading the player
    private bool passiveMode;

    public string[] activeMoves; 
    public string[] passiveMoves;
    public string[] evasiveMoves;
    
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

    public bool canFacePlayer;
    
    // Start is called before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ep = GetComponent<EnemyPatrol>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (GetComponent<EnemyMovement>() != null) GetComponent<EnemyMovement>().enabled = true;
        originalDefensiveModeRadius = defensiveModeRadius;
        originalPassiveModeRadius = passiveModeRadius;
        
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null) playerTransform = playerObject.transform;
        else Debug.LogError("bruh");
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
        
        if (!stunnable) GetComponent<EnemyHealth>().stunMultiplier = 0f;
        else if (stunnable && passiveMode) GetComponent<EnemyHealth>().stunMultiplier = 1.2f;
        else if (stunnable && evasiveMode) GetComponent<EnemyHealth>().stunMultiplier = 1.5f;
        else GetComponent<EnemyHealth>().stunMultiplier = 1f;
        
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
     }

    private void ActiveLogic()
    {
        if (canAttack && !isAttacking) 
            if (!defensiveCheck) animator.SetTrigger(activeMoves[Random.Range(0, activeMoves.Length)]);
    }

    private void PassiveLogic()
    {
        if (canAttack && !isAttacking)
        {
            if (!passiveCheck && !defensiveCheck) animator.SetTrigger(activeMoves[Random.Range(0, activeMoves.Length)]);
            else if (defensiveCheck && !passiveCheck) animator.SetTrigger(evasiveMoves[Random.Range(0, evasiveMoves.Length)]);
            else animator.SetTrigger(passiveMoves[Random.Range(0, passiveMoves.Length)]);
        }

    }

    private void EvasiveLogic()
    {
        if (canAttack && !isAttacking)
        {
            if (defensiveCheck) animator.SetTrigger(evasiveMoves[Random.Range(0, evasiveMoves.Length)]);
            else if (passiveCheck) animator.SetTrigger(passiveMoves[Random.Range(0, passiveMoves.Length)]);
            else animator.SetTrigger(activeMoves[Random.Range(0, activeMoves.Length)]);
        }
    }

    private void ActivateCooldown(float atkCD)
    {
        currentAttackCooldown = atkCD;
    }

    void CooldownTimer()
    {
        currentAttackCooldown -= Time.deltaTime;
        if (currentAttackCooldown <= 0) canAttack = true;
        else canAttack = false;
    }

    private void Attack()
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, transform.right, 0f, LayerMask.GetMask("Player", "Invincible"));
        if (hit.collider != null)
        {
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(damageAmount, knockbackForce * transform.right, ep.isFacingRight, damageType);
                damageable.Stun(stunTime);
                FindAnyObjectByType<PlayerController>().currentCombatTimer =+ 180f;
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
