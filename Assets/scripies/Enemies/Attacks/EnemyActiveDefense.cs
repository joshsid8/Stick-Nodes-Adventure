using System.Collections;
using UnityEngine;

public class EnemyActiveDefense : MonoBehaviour
{
    [SerializeField] private float damageAmount;
    private RaycastHit2D hit;
    [SerializeField] private float stunTime = 0.5f;
    [SerializeField] private float attackRange;
    public Transform attackTransform;
    private EnemyAttack ea;
    [SerializeField] private bool canAttack;
    [SerializeField] private bool isOnCooldown;
    public float attackCooldown;
    private float currentAttackCooldown;
    
    public LayerMask playerLayer;
    private void Start()
    {
        ea = GetComponent<EnemyAttack>();
        canAttack = true;
    }
    
    private void Update()
    {
        if (canAttack)
        {
            Attack();
        }
        else if (!canAttack)
        {
            isOnCooldown = true;
        }

        if (isOnCooldown)
        {
            currentAttackCooldown -= Time.deltaTime;
            if (currentAttackCooldown <= 0)
            {
                isOnCooldown = false;
                canAttack = true;
            }
        }
    }

    private void Attack()
    {
        if (!isOnCooldown)
        {
            canAttack = false;
            GetComponent<Animator>().SetTrigger("DefensiveMove");
            currentAttackCooldown = attackCooldown;
        }
    }
    
    private void CloseAttack()
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, transform.right, 0f, playerLayer);
        if (hit.collider != null)
        {
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(damageAmount);
                damageable.Stun(stunTime);
                
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                player.GetComponent<PlayerController>().currentCombatTimer = player.GetComponent<PlayerController>().combatTimer;
            }
        }
        
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
    
}
