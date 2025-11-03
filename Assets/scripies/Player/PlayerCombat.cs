using System.Collections;
using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    public bool m1Ability;
    public float groundDamageAmount = 20f;
    [SerializeField] private Transform attackTransform;
    [SerializeField] private float attackRange = 1.5f;
    [HideInInspector] public bool isAttacking;
    public bool canAttack;
    private float currentAttackCooldown;
    public float attackEnergy;
    
    public float attackCooldown;
    public float airAttackCooldown;
    public float airDamageAmount = 20f;
    [SerializeField] private Transform airAttackTransform;
    [SerializeField] private float airAttackRange = 1f;
    public float airAttackForce;
    [HideInInspector] public bool isAirAttacking;
    public bool isAirComboing;
    [SerializeField] private bool hasAirBoosted;
    public int airComboAmount = 3;
    
    [SerializeField] private LayerMask enemyLayer;
    public float stunTime = 0.2f;
    public Vector2 knockbackForce;
    public Vector2 airKnockbackForce;
    
    public float attackPotency = 1f;
    public float attackDamageMult = 1f;
    public float attackCooldownMult = 1f;
    
    private RaycastHit2D[] hits;
    private Animator animator;
    private PlayerController pc;
    private PlayerAbility pa;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pc = GetComponent<PlayerController>();
        pa = GetComponent<PlayerAbility>();
        animator = GetComponent<Animator>();
        canAttack = true;
    }

    // Update is called once per frame
    private void Update()
    {
        animator.SetBool("isAttacking", isAttacking);
        animator.SetBool("isAirAttacking", isAirAttacking);
        animator.SetBool("isAirComboing", isAirComboing);
        animator.SetInteger("airComboAmount", airComboAmount);
        if (pc.isGrounded)
        {
            hasAirBoosted = false;
            airComboAmount = 3;
        }
        if (currentAttackCooldown > 0) canAttack = false;
        Attack();
    }

    private void GroundAttack()
    {
        hits = Physics2D.CircleCastAll(attackTransform.position, attackRange, new Vector2(transform.right.x, transform.up.y), 0f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();

            if (eDamageable != null)
            {
                eDamageable.Damage(groundDamageAmount * attackDamageMult, new Vector2(knockbackForce.x * attackPotency * transform.right.x, knockbackForce.y * attackPotency * transform.up.y));
                eDamageable.Stun(stunTime * attackPotency);
                GetComponent<PlayerAbility>().energy += attackEnergy;
                pc.currentCombatTimer += pc.combatTimer / 3;
            }
        }
        currentAttackCooldown = attackCooldown * attackCooldownMult;
    }
    
    private void AirAttack()
    {
        if (pc.rb.linearVelocity.y < 0 && !hasAirBoosted && !pc.isGrounded)
        {
            pc.rb.linearVelocity = new Vector2(pc.rb.linearVelocity.x, airAttackForce);
            hasAirBoosted = true;
        }

        hits = Physics2D.CircleCastAll(airAttackTransform.position, airAttackRange, new Vector2(transform.right.x, transform.up.y), 0f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();

            if (eDamageable != null)
            {
                eDamageable.Damage(airDamageAmount * attackDamageMult, Vector2.zero);
                eDamageable.Stun(stunTime * attackPotency);
                GetComponent<PlayerAbility>().energy += attackEnergy;
                pc.currentCombatTimer += pc.combatTimer / 3;
                if (hits[i].collider.gameObject.layer == LayerMask.NameToLayer("Enemy") && airComboAmount > 0)
                {
                    StartCoroutine(AirCombo(hits[i].collider.gameObject));
                    pc.rb.linearVelocity = new Vector2(pc.rb.linearVelocity.x, airKnockbackForce.y);
                    eDamageable.Damage(0, new Vector2(airKnockbackForce.x * attackPotency * transform.right.x * 1.5f, -airKnockbackForce.y * attackPotency * transform.up.y));
                    eDamageable.Stun(stunTime * 1.25f);
                }
                else eDamageable.Damage(0, new Vector2(airKnockbackForce.x * attackPotency * transform.right.x, airKnockbackForce.y * attackPotency * transform.up.y));
                airComboAmount--;

            }
        }
        currentAttackCooldown = airAttackCooldown * attackCooldownMult;
    }

    private IEnumerator AirCombo(GameObject target)
    {
        while (airComboAmount > 0 && !pc.isGrounded && target.GetComponent<EnemyHealth>().movable)
        {
            target.transform.position = new Vector3(airAttackTransform.transform.position.x + 1f * transform.right.x, airAttackTransform.transform.position.y, 0);
            isAirComboing = true;
            yield return new WaitForFixedUpdate();
        }
        isAirComboing = false;
    }
    
    private void Attack()
    {   
        if (Input.GetMouseButtonDown(0) && currentAttackCooldown
            <= 0 && !pc.isDashing && !pc.isWallSliding && m1Ability && !pa.isPerformingAbility && !GetComponent<PlayerDefense>().isBlocking)
            if (canAttack) animator.SetTrigger("Attack");
        currentAttackCooldown -= Time.deltaTime * 0.5f; if (currentAttackCooldown <= 0) canAttack = true; 
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(airAttackTransform.position, airAttackRange);
    }
}
