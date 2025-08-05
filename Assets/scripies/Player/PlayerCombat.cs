using System.Collections;
using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    public PlayerStats playerStats;
    private PlayerController pc;
    private PlayerHealth ph;
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
    
    [SerializeField] private LayerMask enemyLayer;
    public float stunTime = 0.2f;
    public Vector2 knockbackForce;
    private RaycastHit2D[] hits;
    private Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        SetValues();
        pc = GetComponent<PlayerController>();
        ph = GetComponent<PlayerHealth>();
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
        }

        if (currentAttackCooldown > 0)
        {
            canAttack = false;
        }
        else if (currentAirAttackCooldown > 0)
        {
            canAirAttack = false;
        }
        AttackCooldown();
        AirAttackCooldown();
        
    }

    private void SetValues()
    {
        if (playerStats != null)
        {
            m1Ability = playerStats.M1Ability;
            groundDamageAmount = playerStats.GroundDamageAmount;
            attackCooldown = playerStats.GroundAttackCooldown;
            attackEnergy = playerStats.GroundAttackEnergy;
            airDamageAmount = playerStats.AirDamageAmount;
            airAttackForce = playerStats.AirAttackForce;
            airAttackCooldown = playerStats.AirAttackCooldown;
            stunTime = playerStats.PlayerAttackStunTime;
        }
        else
        {
            Debug.LogError("WHERES THE DAAYUMM SCRIPTABLE OBJEEECCTTT");
            enabled = false;
            return;
        }
    }

    private void GroundAttack()
    {
        hits = Physics2D.CircleCastAll(attackTransform.position, attackRange, Vector2.one, 0f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();

            if (eDamageable != null)
            {
                eDamageable.Damage(groundDamageAmount, knockbackForce * Vector2.one);
                eDamageable.Stun(stunTime);
                GetComponent<PlayerAbility>().energy += attackEnergy;
                pc.currentCombatTimer += pc.combatTimer / 3;
            }
        }

        currentAttackCooldown = attackCooldown;

    }

    private void AttackCooldown()
    {   
        currentAttackCooldown -= Time.deltaTime * 0.5f;
        {
            if (currentAttackCooldown <= 0)
                canAttack = true;
        }
    }

    private void AirAttack()
    {
        if (pc.GetComponent<Rigidbody2D>().linearVelocity.y < 0 && !hasAirBoosted)
        {
            pc.GetComponent<Rigidbody2D>().linearVelocity =
                new Vector2(GetComponent<Rigidbody2D>().linearVelocity.x, airAttackForce);
            hasAirBoosted = true;
        }

        hits = Physics2D.CircleCastAll(airAttackTransform.position, airAttackRange, Vector2.one, 0f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();

            if (eDamageable != null)
            {
                eDamageable.Damage(airDamageAmount, new Vector2(knockbackForce.x, 5f) * transform.right);
                eDamageable.Stun(stunTime);
                GetComponent<PlayerAbility>().energy += attackEnergy;
                pc.currentCombatTimer += pc.combatTimer / 3;
            }
        }

        currentAirAttackCooldown = airAttackCooldown;

    }

    private void AirAttackCooldown()
    {
        currentAirAttackCooldown -= Time.deltaTime * 0.5f;
        {
            if (currentAirAttackCooldown <= 0)
                canAirAttack = true;
        }
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(airAttackTransform.position, airAttackRange);
    }
}
