using System.Collections;
using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    private PlayerController pc;
    public bool m1Ability;
    public float groundDamageAmount = 20f;
    [SerializeField] private Transform attackTransform;
    [SerializeField] private float attackRange = 1.5f;
    [HideInInspector] public bool isAttacking;
    public bool canAttack;
    private float currentAttackCooldown;
    public float attackEnergy;
    
    public float attackCooldown;
    public float airDamageAmount = 20f;
    [SerializeField] private Transform airAttackTransform;
    [SerializeField] private float airAttackRange = 1f;
    public float airAttackForce;
    [HideInInspector] public bool isAirAttacking;
    [SerializeField] private bool canAirAttack;
    [SerializeField] private bool hasAirBoosted;
    public float airAttackCooldown;
    private float currentAirAttackCooldown;
    public int airComboAmount = 3;
    
    [SerializeField] private LayerMask enemyLayer;
    public float stunTime = 0.2f;
    public Vector2 knockbackForce;
    public Vector2 airKnockbackForce;
    public float knockbackMult = 1f;
    private RaycastHit2D[] hits;
    private Animator animator;

    private bool inRange;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pc = GetComponent<PlayerController>();
        animator = GetComponent<Animator>();
        canAttack = true;
        canAirAttack = true;
    }

    // Update is called once per frame
    private void Update()
    {
        if (Input.GetMouseButtonDown(0) && pc.isGrounded && canAttack && currentAttackCooldown
            <= 0 && !isAirAttacking && !pc.isDashing && !pc.isWallSliding && m1Ability)
        { 
            animator.Play("Attack");
        }
        else if (Input.GetMouseButtonDown(0) && !pc.isGrounded && canAirAttack && currentAttackCooldown
                 <= 0 && !isAttacking && !pc.isDashing && !pc.isWallSliding && m1Ability)
        {
            if (!hasAirBoosted) animator.Play("AirAttack");
            else animator.Play("AltAirAttack");
        }
        animator.SetBool("isAttacking", isAttacking);
        animator.SetBool("isAirAttacking", isAirAttacking);
        if (pc.isGrounded)
        {
            hasAirBoosted = false;
            airComboAmount = 3;
        }
        if (currentAttackCooldown > 0) canAttack = false;
        else if (currentAirAttackCooldown > 0) canAirAttack = false;
        AttackCooldown();
    }

    private void GroundAttack()
    {
        hits = Physics2D.CircleCastAll(attackTransform.position, attackRange, new Vector2(transform.right.x, transform.up.y), 0f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();

            if (eDamageable != null)
            {
                eDamageable.Damage(groundDamageAmount, new Vector2(knockbackForce.x * knockbackMult * transform.right.x, knockbackForce.y * knockbackMult * transform.up.y));
                eDamageable.Stun(stunTime);
                GetComponent<PlayerAbility>().energy += attackEnergy;
                pc.currentCombatTimer += pc.combatTimer / 3;
            }
        }
        currentAttackCooldown = attackCooldown;
    }
    
    private void AirAttack()
    {
        if (pc.rb.linearVelocity.y < 0 && !hasAirBoosted)
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
                eDamageable.Damage(airDamageAmount, Vector2.zero);
                eDamageable.Stun(stunTime);
                GetComponent<PlayerAbility>().energy += attackEnergy;
                pc.currentCombatTimer += pc.combatTimer / 3;
                if (hits[i].collider.gameObject.layer == LayerMask.NameToLayer("Enemy") && airComboAmount > 0)
                {
                    StartCoroutine(AirCombo(hits[i].collider.gameObject));
                    pc.rb.linearVelocity = new Vector2(pc.rb.linearVelocity.x, airKnockbackForce.y);
                    eDamageable.Damage(0, new Vector2(airKnockbackForce.x * knockbackMult * transform.right.x * 1.5f, -airKnockbackForce.y * knockbackMult * transform.up.y));
                    eDamageable.Stun(stunTime * 1.25f);
                }
                else eDamageable.Damage(0, new Vector2(airKnockbackForce.x * knockbackMult * transform.right.x, airKnockbackForce.y * knockbackMult *transform.up.y));
                airComboAmount--;

            }
        }
        currentAirAttackCooldown = airAttackCooldown;
    }

    private IEnumerator AirCombo(GameObject target)
    {
        while (airComboAmount > 0 && !pc.isGrounded && target.GetComponent<EnemyHealth>().movable)
        {
            target.transform.position = new Vector3(airAttackTransform.transform.position.x + 1f * transform.right.x, airAttackTransform.transform.position.y, 0);
            yield return new WaitForFixedUpdate();
        }
    }
    
    private void AttackCooldown()
    {   
        currentAttackCooldown -= Time.deltaTime * 0.5f; if (currentAttackCooldown <= 0) canAttack = true; 
        currentAirAttackCooldown -= Time.deltaTime * 0.5f; if (currentAirAttackCooldown <= 0) canAirAttack = true;
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(airAttackTransform.position, airAttackRange);
    }
}
