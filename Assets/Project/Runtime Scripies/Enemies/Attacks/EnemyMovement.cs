using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyMovement : MonoBehaviour
{
    [Header("Dashing")]
    public float dashSpeed = 10f;
    public bool isDashing;
    
    [SerializeField] private Vector2 dashDirection;
    public bool isFlyDashing;
    
    [Space]
    [Header("Jumping")]
    public float jumpForceY;
    public float jumpForceX;
    public bool isRandomised;
    
    [Space]
    [Header("Checks")]
    [SerializeField] private bool isHomingPlayer;
    [SerializeField] private bool inRange;
    [SerializeField] private bool isFrozen;
    
    // Components
    private EnemyAttack ea;
    private EnemyPatrol ep;
    private Animator animator;
    
    private void Awake()
    {
        animator = GetComponent<Animator>();
        ea = GetComponent<EnemyAttack>();
        ep = GetComponent<EnemyPatrol>();
    }

    // Update is called once per frame
    void Update()
    {
        Dash();
        FlyDash();
        if (isHomingPlayer) dashDirection = (ea.playerTransform.position - transform.position).normalized;
        inRange = Physics2D.OverlapCircle(ea.attackTransform.position, ea.attackRange * 1.5f, LayerMask.GetMask("Player", "Invincible"));
        if (isFrozen) Freeze();
    }
    private void Dash()
    {
        if (isDashing) ea.rb.linearVelocity = ep.ec.isFacingRight ? new Vector2(dashSpeed, ea.rb.linearVelocity.y)
            : new Vector2(-dashSpeed, ea.rb.linearVelocity.y);
        else if (!isDashing) ea.rb.linearVelocity = new Vector2(ea.rb.linearVelocity.x, ea.rb.linearVelocity.y);
    }

    private void FlyDash()
    {
        if (isFlyDashing)
        {
            ea.rb.linearVelocity = dashDirection * dashSpeed;
            if (inRange)
            {
                isFlyDashing = false;
                animator.SetBool("inRange", true);
            }
            else animator.SetBool("inRange", false);
        }
        else ea.rb.linearVelocity = new Vector2(ea.rb.linearVelocity.x, ea.rb.linearVelocity.y);
    }

    private void Jump()
    {
        if (ep.isGrounded)
        {
            if (isRandomised)
            {
                jumpForceX = Random.Range(-16, 16);
                jumpForceY = Random.Range(20, 31);
            }
            ea.rb.linearVelocity = new Vector2(jumpForceX, jumpForceY);
        }
    }

    public void FreezeX()
    {
        ea.rb.linearVelocity = new Vector2(0, ea.rb.linearVelocity.y);
    }
    private void FreezeY()
    {
        ea.rb.linearVelocity = new Vector2(ea.rb.linearVelocity.x, 0);
    }
    private void Freeze()
    {
        ea.rb.linearVelocity = Vector2.zero;
    }
}