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
    public float damageResistance;
    private float blockStunMultiplier;
    
    private PlayerHealth hp;
    private PlayerController pc;
    private PlayerCombat pf;
    private PlayerAbility pa;
    private Animator animator;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pc = GetComponent<PlayerController>();
        pf = GetComponent<PlayerCombat>();
        hp = GetComponent<PlayerHealth>();
        pa = GetComponent<PlayerAbility>();
        animator = GetComponent<Animator>();
        
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
        if (isBlocking && canBlock && !pc.isDashing && !pc.isWallSliding)
        {
            pc.rb.linearVelocity = new Vector2(0, pc.rb.linearVelocity.y);
            if (pc.canMove) pc.canMove = false;
            pc.jumpForce = 0f;
            hp.shockMultiplier = 0f;
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
                hp.shockMultiplier = hp.originalStunMultiplier;
                pc.jumpForce = pc.originalJumpForce;
                currentCounterTime = 0f;
                canCounter = true;
            }
        }
        
        if (counterWindow && !canCounter) currentCounterTime -= Time.deltaTime;
        if (currentCounterTime <= 0) counterWindow = false;
    }

    public void CounterAttack()
    {
        pa.energy += 5f;
        hits = Physics2D.CircleCastAll(transform.position, counterRadius, Vector2.one, 0f, eLayer);
        for (int i = 0; i < hits.Length; i++)
        {
            EDamageables eDamageable = hits[i].collider.gameObject.GetComponent<EDamageables>();
            Vector2 distance = hits[i].collider.gameObject.transform.position - transform.position;
            int num;
            if (distance.x >= 0) num = 1; else num = -1;
            if (eDamageable != null)
            {
                pa.energy += 10f;
                eDamageable.Damage(counterDamage, counterKnockbackForce * num * pf.attackPotency);
                eDamageable.Stun(counterStun * pf.attackPotency);
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
