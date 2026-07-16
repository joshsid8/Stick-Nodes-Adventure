using UnityEngine;

public class PlayerDefense : MonoBehaviour
{
    public bool counterAbility;
    public bool canCounter;
    public bool counterWindow;
    public float counterTime;
    [SerializeField] private float currentCounterTime;
    public float counterDamageResistance;
    public float counterRadius;
    public float counterDamage;
    public float counterStun;
    public Vector2 counterKnockbackForce;
    private RaycastHit2D[] hits;
    public LayerMask eLayer;
    
    public bool canBlock;
    public bool isBlocking;
    public float blockDamageResistance;
    private float blockStunMultiplier;
    public float blockMoveSpeed;
    
    private PlayerHealth hp;
    private PlayerController pc;
    private PlayerCombat pf;
    private PlayerAbility pa;
    
    private Animator animator;
    
    private void Awake()
    {
        pc = GetComponent<PlayerController>();
        pf = GetComponent<PlayerCombat>();
        hp = GetComponent<PlayerHealth>();
        pa = GetComponent<PlayerAbility>();
        animator = GetComponent<Animator>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        canCounter = true;
    }

    // Update is called once per frame
    private void Update()
    {
        if (Input.GetButton("Block") && !pc.isWallSliding && pc.canAct) isBlocking = true;
        Block();
        animator.SetBool("isBlocking", isBlocking);
    }
    
    private void Block()
    {
        if (isBlocking)
        {
            if (canBlock && !pc.isDashing && !pc.isWallSliding && !pa.isPerformingAbility && !pf.isAirAttacking && !pf.isAttacking)
            {
                pc.movementSpeed = blockMoveSpeed;
                pc.jumpForce = 0f;
                hp.shockMultiplier = 0f;

                if (counterAbility && canCounter)
                {
                    currentCounterTime = counterTime;
                    canCounter = false;
                    counterWindow = true;
                }
            }
            else StopBlocking();

            if (Input.GetButtonUp("Block") || !pc.canAct || pc.isWallSliding || pc.isDashing || hp.isStunned) 
                StopBlocking();
        }

        if (counterWindow && !canCounter) currentCounterTime -= Time.deltaTime;
        if (currentCounterTime <= 0) counterWindow = false;
    }

    public void StopBlocking()
    {
        isBlocking = false;
        pc.canMove = true;
        hp.shockMultiplier = hp.originalShockMultiplier;
        pc.jumpForce = pc.originalJumpForce;
        pc.movementSpeed = pc.originalMovementSpeed;
        currentCounterTime = 0f;
        canCounter = true;
    }

    public void CounterAttack()
    {
        pa.energy += 5f;
        hits = Physics2D.CircleCastAll(transform.position, counterRadius, Vector2.one, 0f, eLayer);
        for (var i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.gameObject.layer == LayerMask.NameToLayer("Enemy"))
            {
                var eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();
                Vector2 distance = hits[i].collider.gameObject.transform.position - transform.position;
                int num;
                if (distance.x >= 0) num = 1;
                else num = -1;
                if (eDamageable != null)
                {
                    pa.energy += 10f;
                    eDamageable.Damage(counterDamage, 0);
                    eDamageable.Shock(counterStun * pf.attackPotency, counterKnockbackForce * num * pf.attackPotency);
                    PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer / 2;
                }
            }
            else if (hits[i].collider.gameObject.layer == LayerMask.NameToLayer("EVFX") || hits[i].collider.gameObject.layer == LayerMask.NameToLayer("FX"))
            {
                var projectile = hits[i].collider.gameObject.GetComponent<Projectile>();
                if (projectile != null) projectile.Deflect(1);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, counterRadius);
    }
}
