using System.Collections;
using UnityEngine;


public class EnemyAttack : MonoBehaviour
{
    public float styleFactor;

    private bool activeMode; // The enemy attack/coming close to the player
    private bool defensiveCheck; // When the player runs near
    private bool passiveCheck; // Certain action performed if the player isnt close or far
    private bool evasiveMode; // The enemy evading the player
    private bool passiveMode;
    
    public float defensiveModeRadius;
    public float modDefensiveModeRadius;
    private float originalDefensiveModeRadius;
    
    public float passiveModeRadius;
    public float modPassiveModeRadius;
    private float originalPassiveModeRadius;
    
    public MonoBehaviour defensiveScript;
    
    public LayerMask pLayer;
    private Transform playerTransform;

    public bool canAttack;
    public float attackCooldown = 2f;
    private bool isOnAttackCooldown;
    private float currentAttackCooldown;
    public bool stunnable;
    
    private Rigidbody2D rb;
    private Animator animator;
    private EnemyPatrol ep;

    public bool canFacePlayer;
    
    // Start is called before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ep = GetComponent<EnemyPatrol>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        originalDefensiveModeRadius = defensiveModeRadius;
        originalPassiveModeRadius = passiveModeRadius;
        
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
        }
        else
        {
            Debug.LogError("bruh");
        }
    }
    
    private void Update()
    {
        FacePlayer();
        CheckModes();
        StyleFactor();
        AttackCooldown();
        if (!canAttack)
        {
            isOnAttackCooldown = true;
        }
        
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
        
        if (!stunnable)
        {
            GetComponent<EnemyHealth>().stunMultiplier = 0f;
        }
        else if (stunnable && passiveMode)
        {
            GetComponent<EnemyHealth>().stunMultiplier = 1.2f;
        }
        else if (stunnable && evasiveMode)
        {
            GetComponent<EnemyHealth>().stunMultiplier = 1.5f;
        }
        else
        {
            GetComponent<EnemyHealth>().stunMultiplier = 1f;
        }
    }
    
    private void CheckModes()
     {
         defensiveCheck = Physics2D.OverlapCircle(transform.position, defensiveModeRadius, pLayer);
         passiveCheck = Physics2D.OverlapCircle(transform.position, passiveModeRadius, pLayer);
         
         if (defensiveCheck)
         {
             passiveCheck = false;
         }
         else if (passiveCheck)
         {
             defensiveCheck = false;
         }
         
         if (defensiveCheck && !GetComponent<EnemyHealth>().isDead && !evasiveMode)
         {
             defensiveScript.enabled = true;
         }
         else
         {
             defensiveScript.enabled = false;
         }
         
         if (defensiveCheck && activeMode)
         {
             rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
             if (!activeMode)
             {
                 rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
             }
         }
     }

    private void ActiveLogic()
    {
        if (currentAttackCooldown <= 0)
        {
            if (!defensiveCheck)
            {
                animator.SetTrigger("ActiveMove");
            }
            
            currentAttackCooldown = attackCooldown;
            canAttack = false;
        }

    }

    private void PassiveLogic()
    {
        if (currentAttackCooldown <= 0)
        {
            if (!passiveCheck && !defensiveCheck)
            {
                animator.SetTrigger("ActiveMove");
            }
            else if (defensiveCheck && !passiveCheck)
            {
                animator.SetTrigger("EvasiveMove");
            }
            else
            {
                animator.SetTrigger("PassiveMove");
            }
            currentAttackCooldown = attackCooldown;
            canAttack = false;
        }

    }

    private void EvasiveLogic()
    {
        if (currentAttackCooldown <= 0)
        {
            if (defensiveCheck)
            {
                animator.SetTrigger("EvasiveMove");
            }
            else if (passiveCheck)
            {
                animator.SetTrigger("PassiveMove");
            }
            else
            {
                animator.SetTrigger("ActiveMove");
            }
            currentAttackCooldown = attackCooldown;
            canAttack = false;
        }
            

    }
    void AttackCooldown()
    {
        if (isOnAttackCooldown)
        { 
            currentAttackCooldown -= Time.deltaTime;
            if (currentAttackCooldown <= 0)
            {
                isOnAttackCooldown = false;
                canAttack = true;
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

        if (activeMode)
            ActiveLogic();
        else if (passiveMode) 
            PassiveLogic();
        else if (evasiveMode)
            EvasiveLogic();
        else
            Debug.Log("So Clueless.");
    }
    void FacePlayer()
    {
        if (canFacePlayer)
        {
            if (playerTransform == null) return;

            // Determine direction to player
            float directionToPlayer = playerTransform.position.x - transform.position.x;

            if (directionToPlayer > 0 && !ep.isFacingRight)
            {
                Flip();
            }
            else if (directionToPlayer < 0 && ep.isFacingRight)
            {
                Flip();
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, passiveModeRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, defensiveModeRadius);
    }
    
    void Flip()
    {
        ep.isFacingRight = !ep.isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }


}
