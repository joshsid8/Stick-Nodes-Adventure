using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public abstract class BossBase : MonoBehaviour
{
    // Strafing/Dashing
    [FormerlySerializedAs("dashSpeed")]
    public float strafeSpeed = 10f;
    public bool isStrafing;
    public float strafeCooldown;
    
    public bool isFlyDashing;
    public Vector2 dashDirection;
    [SerializeField] private string inRangeAnim = "HAttack";
    
    // Melee & Ability
    public float meleeCooldown;
    public float stunTime;
    public float damageAmount;
    public Vector2 knockbackForce;
    private RaycastHit2D hit;
    public float attackRange;
    public Transform attackTransform;
    
    public List<GameObject> storedObjects = new List<GameObject>();
    public int storedObjectIndex;
    public Transform fireTransform;
    
    // Jumping
    public float jumpCooldown;
    public bool isRandomised;
    public float jumpForceY;
    public float jumpForceX;
    
    // Teleporting and Dropping
    public Transform teleportPoint;
    public float dropSpeed;
    [SerializeField] private string teleportAnim = "DAttack";
    
    // Checks
    [HideInInspector] public bool isInRange;
    [HideInInspector] public bool freezeMovement;
    [HideInInspector] public bool isFalling;
    public int damageType;
    public LayerMask terrainLayers;
    
    // Components
    protected Rigidbody2D rb;
    protected Animator animator;
    protected BossAttack ba;
    protected BossHealth bh;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected void Awake()
    {
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        bh = GetComponent<BossHealth>();
    }

    protected virtual void Update()
    {
        for (var i = storedObjects.Count - 1; i >= 0; i--) 
            if (storedObjects[i].IsDestroyed()) storedObjects.RemoveAt(i); 
        Strafe();
    }

    protected void FindPlayerPosition()
    {
        var rng = Random.Range(0, 101);
        var prediction = rng <= 70;
        if (!prediction) dashDirection = (ba.playerTransform.position - transform.position).normalized;
        else
        {
            var x = PlayerHealth.Instance.pc.rb.linearVelocity.x;
            var y = PlayerHealth.Instance.pc.rb.linearVelocity.y;
            var predictedDistanceX = x > 0 ? x * 0.2f : x * 0.2f * -1;
            var predictedDistanceY = y > 0 ? y * 0.2f : y * 0.2f * -1;
            dashDirection = new Vector2(ba.playerTransform.position.x + predictedDistanceX - transform.position.x,
                ba.playerTransform.position.y + predictedDistanceY - transform.position.y).normalized;
        }
    }
    
    protected void Teleport()
    {
        animator.SetTrigger(teleportAnim);
        var rng = Random.Range(0, 101);
        var x = rng <= 50 ? 3 : -3;
        transform.position = new Vector3(ba.playerTransform.position.x + x, teleportPoint.position.y, transform.position.z);
        
        var rng2 = Random.Range(0, 101);
        var px = PlayerHealth.Instance.pc.rb.linearVelocity.x;
        var predictedDistanceX = px > 0 ? px * 0.2f : px * 0.2f * -1;
        if (rng2 <= 50 || px == 0) rb.linearVelocity = new Vector2(rb.linearVelocity.x + predictedDistanceX, !bh.phase2 ? -dropSpeed * 1.5f : -dropSpeed * 2f);
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, !bh.phase2 ? -dropSpeed * 1.5f : -dropSpeed * 2f);
    }
    
    private void Strafe()
    {
        if (isStrafing) rb.linearVelocity = ba.isFacingRight ? new Vector2(strafeSpeed, rb.linearVelocity.y) :
            new Vector2(-strafeSpeed, rb.linearVelocity.y);
        else if (!isStrafing) rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }
    
    protected void FlyDash()
    {
        if (isFlyDashing)
        {
            rb.linearVelocity = bh.phase2 ? dashDirection * strafeSpeed * 1.2f : dashDirection * strafeSpeed;
            if (isInRange)
            {
                isFlyDashing = false;
                animator.SetTrigger(inRangeAnim);
            }
        }
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }
    
    protected void Jump()
    {
        if (ba.isGrounded)
        {
            if (isRandomised)
            {
                jumpForceX = Random.Range(-16, 16);
                jumpForceY = Random.Range(20, 31);
            }

            rb.linearVelocity = new Vector2(jumpForceX, jumpForceY);
        }
    }
    
    protected void MeleeAttack()
    {
        hit = Physics2D.CircleCast(attackTransform.position, attackRange, new Vector2(transform.right.x, transform.up.y), 0f, LayerMask.GetMask("Player"));
        if (hit.collider != null)
        {
            PDamageable damageable = hit.collider.gameObject.GetComponent<PDamageable>();
            if (damageable != null)
            {
                damageable.Damage(damageAmount, new Vector2(knockbackForce.x * transform.right.x, knockbackForce.y * transform.up.y), ba.isFacingRight, damageType, 0F);
                damageable.Stun(stunTime);
                PlayerHealth.Instance.currentCombatTimer =+ 180f;
            }
        }
    }
    protected void FollowupMove(string move) => animator.SetTrigger(move);

    protected void StoreObject(GameObject obj)
    {
        var projHit = Physics2D.CircleCast(fireTransform.position, 10f,
            new Vector2(fireTransform.transform.right.x, fireTransform.transform.up.y), 0f, 
            LayerMask.GetMask("FX", "EVFX", "PVFX"));
        if (projHit.collider != null) storedObjects.Add(projHit.collider.gameObject);
    }

    protected void ApplyForce(float force) => Shenanigans.Instance.ApplyForce(new Vector2(ba.isFacingRight ? force : -force, force), obj: storedObjects[storedObjectIndex]);
    
    public abstract void Gamble();
    
    public void FreezeMovement() => rb.linearVelocity = Vector2.zero;
    public void FreezeMovementX() => rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    public void FreezeMovementY() => rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
}
