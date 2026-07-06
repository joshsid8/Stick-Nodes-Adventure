using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Variables
    public bool canAct = true;
    public bool canFlip = true;
    public bool isFrozen;
    
    // Simply move speed
    public bool canMove = true;
    public float movementSpeed = 10f;
    public float movementSpeedMult = 1f;
    public float originalMovementSpeed;
    
    [SerializeField] private bool isMoving;
    
    // Jumping
    public float originalJumpForce;
    public float jumpForce = 8.0f;
    public float jumpForceMult = 1f;
    public float maxFallSpeed;
    public float fallMultiplier;
    public float lowJumpMultiplier;
    [SerializeField] private bool isJumping;
    private bool hasJumped;
    public bool canDoubleJump;
    private bool doubleJump;
    
    // Jump optimisations
    [SerializeField] private float coyoteTime = 0.2f; // Adjust as needed
    private float coyoteTimeCounter;
    [SerializeField] private float jumpBufferTime = 0.1f; // Adjust as needed
    private float jumpBufferCounter;
    
    // Wall Jumping
    public float wallSlideSpeed;
    public float wallJumpForce;

    public bool canWallJump;
    
    private bool isTouchingWall;
    [HideInInspector] public bool isWallSliding;
    [SerializeField] private bool isWallJumping;
    [SerializeField] private int availableWalls;
    [SerializeField] private int availableWallsLeft;
    [SerializeField] private Transform wallCheck;
    [SerializeField] private LayerMask whatIsWall;
    [SerializeField] private float wallCheckRay;

    // Ground Check
    [SerializeField] private float groundCheckCircle;
    public Transform groundCheck;
    public LayerMask whatIsGround;
    [HideInInspector] public bool isGrounded;
    
    // Dashing
    public float dashSpeed;
    public float dashTime;
    public float dashCooldown;
    public bool dashAbility;
    [HideInInspector] public float currentDashTime;
    [HideInInspector] public bool isDashing;
    [HideInInspector] public bool canDash;
    private float dashDirection; 
    private float dashCooldownTimer; 
    private bool hasDashedInAir;
    public float dashEnergyCost;
    
    // Physics and QoL
    [HideInInspector] public float defaultGravity;
    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public Animator animator;
    public float inputDirection;
    
    public LayerMask oneWayPlatformLayer;
    public float dropThroughDuration = 0.3f;
    public GameObject currentCheckPoint;
    public GameObject currentSpawnPoint;

    // Related to running and flipping
    private bool isRunning;
    public bool isFacingRight = true;
    
	//Extra Components
	private PlayerCombat pco;
    private PlayerDefense pdf;
    private PlayerAbility pab;
    private Knockback kb;
    public GameObject combatIndicator;
    #endregion
    #region Main Methods
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        pco = GetComponent<PlayerCombat>();
        pab = GetComponent<PlayerAbility>();
        kb = GetComponent<Knockback>();
    }

    // Start is called before the first frame update
    private void Start()
    {
        canAct = true;
        defaultGravity = rb.gravityScale;
        
        originalMovementSpeed = movementSpeed;
        originalJumpForce = jumpForce;
        availableWallsLeft = availableWalls;
        
        PlayerHealth.Instance.currentCombatTimer = 0;
    }

    // Update is called once per frame
    private void Update()
    {
        CheckInput();
        CheckMovementDirection();
        UpdateAnimation();
        CheckIfCanJump();
        if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen)
        {
            Dash();
            Jump();
        }
        VariableJumpHeight();
        WallSlide();
        Other();
        InCombat();
    }

    private void FixedUpdate()
    {
        ApplyMovement();
        CheckEnvironment();
        if (isDashing)
        {
            rb.gravityScale = 0;
            rb.linearVelocity = transform.right * dashDirection * dashSpeed;
            canFlip = false;
            currentDashTime -= Time.deltaTime * 1.75f;
            if (currentDashTime <= 0)
            {
                isDashing = false;
                isMoving = true;
                if (isMoving)
                {
                    rb.gravityScale = defaultGravity;
                    canFlip = true;
                    isMoving = false;
                    rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
                }
            }
        }
    }
    
    private void CheckInput()
    {
        inputDirection = Input.GetAxisRaw("Horizontal");

        if (Input.GetButtonDown("Jump")) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= Time.deltaTime;
    }

    private void Dash()
    {
        if (Input.GetButtonDown("Dash") && canDash && dashAbility && !pco.isAttacking && !pco.isAirAttacking &&
            pab.energy >= dashEnergyCost)
        {
            canDash = false;
            isDashing = true;
            pab.energy -= dashEnergyCost;
            currentDashTime = dashTime;
            if (isWallSliding)
            {
                isWallSliding = false;
                Flip();
                dashDirection =
                    (int)Mathf.Sign(isFacingRight ? -Input.GetAxisRaw("Horizontal") : Input.GetAxisRaw("Horizontal"));
            }
            else
                dashDirection =
                    (int)Mathf.Sign(isFacingRight ? Input.GetAxisRaw("Horizontal") : -Input.GetAxisRaw("Horizontal"));

            dashCooldownTimer = dashCooldown;

            if (!isGrounded) hasDashedInAir = true;
        }

        if (hasDashedInAir) canDash = false;
        
        if (dashCooldownTimer >= 0)
        {
            dashCooldownTimer -= Time.deltaTime;
            canDash = false; // Disable dash while on cooldown
        }
        else if (dashCooldownTimer <= 0 && hasDashedInAir) canDash = false;
        else canDash = dashAbility; // Allow dash when cooldown is over
    }

    private void WallSlide()
    {
        if (isTouchingWall && !isGrounded && rb.linearVelocity.y < 0 && canWallJump)
        {
            jumpBufferCounter = 0f;
            isWallSliding = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -wallSlideSpeed);
            if (pab.isPerformingAbility) pab.isPerformingAbility = false;
            if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen) WallJump();
        }
        else isWallSliding = false;
    }

    private void WallJump()
    {
        if (isWallSliding && Input.GetButtonDown("Jump") && canWallJump && availableWallsLeft > 0 && !isDashing)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, wallJumpForce);
            isWallSliding = false;
            isWallJumping = true;
            availableWallsLeft--;
        }
    }

    private void VariableJumpHeight()
    {
        if (rb.linearVelocity.y < 0)
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1) * Time.deltaTime;
        else if (rb.linearVelocity.y > 0 && !Input.GetButton("Jump") && !pco.isAirAttacking)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (lowJumpMultiplier - 1) * Time.deltaTime;
            coyoteTimeCounter = 0f;
        }
        // Coyote and jump buffer
        if (isGrounded) coyoteTimeCounter = coyoteTime;
        else coyoteTimeCounter -= Time.deltaTime;
        
    }
    
    private void Jump()
    {
        if (coyoteTimeCounter > 0f && jumpBufferCounter > 0)
        {
            isJumping = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * jumpForceMult);
            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f;
        }
        else if (doubleJump && canDoubleJump && !kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen)
            DoubleJump();
    }
    private void CheckIfCanJump()
    {
        if (isGrounded && rb.linearVelocity.y < 0.01f)
        {
            doubleJump = canDoubleJump;
            isWallJumping = false;
            isJumping = false;
        }
        
        if (isJumping && isGrounded)
        {
            isWallJumping = false;
        }
    }

    private void DoubleJump()
    {
        if (!isGrounded && jumpBufferCounter > 0f && !isDashing && !isWallSliding)
        {
            isJumping = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * jumpForceMult);
            doubleJump = false;
        }
    }
    
    private void ApplyMovement()
    {
        if (canMove && !isFrozen && !kb.IsBeingKnocked)
        {
            if (inputDirection != 0)
            {
                isMoving = true;
                rb.linearVelocity = new Vector2(movementSpeed * movementSpeedMult * inputDirection, rb.linearVelocity.y);
            }
            if (inputDirection == 0 && isMoving)
            {
                isMoving = false;
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            }
        }
    }

    private void CheckMovementDirection()
    {
        if (isFacingRight && inputDirection < 0) Flip();
        else if (!isFacingRight && inputDirection > 0) Flip();
        
        if (rb.linearVelocity.x <= -0.5f || rb.linearVelocity.x >= 0.5f) isRunning = true;
        else isRunning = false;
    }

    private void Other()
    {
        if (rb.linearVelocity.y > 0)
        {
            rb.gravityScale = defaultGravity;
        }
        else if (rb.linearVelocity.y < 0)
        {
            isJumping = false;
            isWallJumping = false;
            rb.gravityScale = defaultGravity - 1f;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(rb.linearVelocity.y, -maxFallSpeed));
        }
        //standing still
        if (!canAct)
        {
            canMove = false;
            jumpForce = 0f; 
            dashAbility = false;
            pco.enabled = false;
            canDash = false;
            if (isMoving)
            {
                isMoving = false;
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            }
        }
        else
        {
            canMove = true;
            jumpForce = originalJumpForce; 
            dashAbility = true;
            pco.enabled = true;
        }
        
        if (isGrounded)
        {
            isWallJumping = false;
            hasDashedInAir = false;
            availableWallsLeft = availableWalls;
        }
        
        if (Input.GetAxisRaw("Vertical") < 0 && Input.GetButtonDown("Jump") && isGrounded)
        {
            RaycastHit2D hit = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckCircle + 0.1f, oneWayPlatformLayer);
            if (hit.collider != null && hit.collider.GetComponent<PlatformEffector2D>() != null)
            {
                StartCoroutine(DropThroughPlatform(hit.collider));
                jumpBufferCounter = 0f;
                coyoteTimeCounter = 0f;
            }
        }
        if (isFrozen) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        if (inputDirection == 0 && !isDashing && !GetComponent<Knockback>().IsBeingKnocked && rb.linearVelocity.x != 0)
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    }
    
    private void UpdateAnimation()
    {
        animator.SetBool("isRunning", isRunning);
        animator.SetBool("isJumping", isJumping);
        animator.SetBool("isGrounded", isGrounded);
        animator.SetBool("isWallSliding", isWallSliding);
        animator.SetBool("isDashing", isDashing);
        animator.SetBool("isWallJumping", isWallJumping);
    }

    private void Flip()
    {
        if (canFlip)
        {
            isFacingRight = !isFacingRight;
            transform.Rotate(0.0f, 180.0f, 0.0f);
        }
    }

    private void InCombat()
    {
        if (PlayerHealth.Instance.currentCombatTimer > 0) PlayerHealth.Instance.inCombat = true;
        if (PlayerHealth.Instance.inCombat)
        {
            combatIndicator.SetActive(true);
            PlayerHealth.Instance.currentCombatTimer -= Time.deltaTime;
            if (PlayerHealth.Instance.currentCombatTimer <= 0) PlayerHealth.Instance.inCombat = false;
        }
        if (!PlayerHealth.Instance.inCombat) combatIndicator.SetActive(false);
    }
    private IEnumerator DropThroughPlatform(Collider2D platformCollider)
    {
        Physics2D.IgnoreCollision(GetComponent<BoxCollider2D>(), platformCollider, true);
        
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, -5f);

        yield return new WaitForSeconds(dropThroughDuration);
        
        Physics2D.IgnoreCollision(GetComponent<BoxCollider2D>(), platformCollider, false);
    }
    private void CheckEnvironment()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckCircle, whatIsGround);
        isTouchingWall = Physics2D.Raycast(wallCheck.position, Vector2.up * transform.localScale.y, wallCheckRay, whatIsWall); 
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckCircle);
        Gizmos.color = Color.green;
        Vector3 rayDirection = Vector2.up * transform.localScale.y * wallCheckRay;
        Gizmos.DrawLine(wallCheck.position, wallCheck.position + rayDirection);
    }
    #endregion
    
    public void Save(ref PlayerSaveCurrentData data)
    {
        data.position = transform.position;
        data.health = PlayerHealth.Instance.playerHealth;
        data.energy = pab.energy;
        data.money = GetComponent<PlayerCurrency>().nodeBucks;
    }
    public void Load(PlayerSaveCurrentData data)
    {
        transform.position = data.position;
        PlayerHealth.Instance.playerHealth = data.health;
        pab.energy = data.energy;
        GetComponent<PlayerCurrency>().nodeBucks = data.money;
    }
}

[System.Serializable]
public struct PlayerSaveCurrentData
{
    public Vector3 position;
    public float health;
    public float energy;
    public float money;
}