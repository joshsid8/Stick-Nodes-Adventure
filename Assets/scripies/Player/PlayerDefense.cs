using UnityEngine;

public class PlayerDefense : MonoBehaviour
{
    public PlayerStats playerStats;
    
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
    public float damageResistance;
    [HideInInspector] public float originalDamageResistance;
    private float blockStunMultiplier;

    public float blockMovementSpeed;
    
    private PlayerHealth hp;
    private PlayerController pc;
    private PlayerAbility pa;
    private Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        SetValues();
        pc = GetComponent<PlayerController>();
        hp = GetComponent<PlayerHealth>();
        pa = GetComponent<PlayerAbility>();
        animator = GetComponent<Animator>();
        
        canCounter = true;
        originalDamageResistance = damageResistance;
    }

    // Update is called once per frame
    private void Update()
    {
        if (Input.GetButton("Block") && !pc.isWallSliding && pc.canAct) isBlocking = true;
        Block();
        animator.SetBool("isBlocking", isBlocking);
    }
    private void SetValues()
    {
        if (playerStats != null)
        {
            damageResistance = playerStats.BlockDamageResistance;
            counterAbility = playerStats.CounterAbility;
            counterTime = playerStats.CounterTime;
            counterDamageResistance = playerStats.CounterDamageResistance;
            counterDamage = playerStats.CounterDamage;
            counterStun = playerStats.CounterStun;
        }
        else
        {
            Debug.LogError("no scriptable object found");
            enabled = false;
        }
    }
    private void Block()
    {
        if (isBlocking && canBlock && !pc.isDashing && !pc.isWallSliding)
        {
            pc.rb.linearVelocity = new Vector2(0, pc.rb.linearVelocity.y);
            if (pc.canMove) pc.canMove = false;
            pc.jumpForce = 0f;
            hp.stunMultiplier = 0f;
            pa.energyRefill = 0f;
            if (counterAbility && canCounter && isBlocking)
            {
                currentCounterTime = counterTime;
                canCounter = false;
                counterWindow = true;
            }
            
            if (Input.GetButtonUp("Block") || !pc.canAct || pc.isWallSliding || pc.isDashing || hp.isStunned)
            {
                isBlocking = false;
                pc.canMove = true;
                hp.stunMultiplier = hp.originalStunMultiplier;
                pc.jumpForce = pc.originalJumpForce;
                pa.energyRefill = pc.inCombat ? pa.modEnergyRefill : pa.originalEnergyRefill;
                currentCounterTime = 0f;
                canCounter = true;
            }
        }
        
        if (counterWindow && !canCounter)
        {
            currentCounterTime -= Time.deltaTime;
        }
        if (currentCounterTime <= 0)
        {
            counterWindow = false;
        }
    }

    public void CounterAttack()
    {
        pa.energy += 5f;
        hits = Physics2D.CircleCastAll(transform.position, counterRadius, transform.right, 0f, eLayer);
        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();
            Vector2 distance = hits[i].collider.gameObject.transform.position - transform.position;
            int num;
            if (distance.x >= 0) num = 1; else num = -1;
            if (eDamageable != null)
            {
                pa.energy += 10f;
                eDamageable.Damage(counterDamage, counterKnockbackForce * num);
                eDamageable.Stun(counterStun);
                pc.currentCombatTimer += pc.combatTimer / 2;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, counterRadius);
    }
}
