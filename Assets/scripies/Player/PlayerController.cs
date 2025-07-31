using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public PlayerStats playerStats;
    public bool canAct = true;
    [SerializeField] private bool canFlip = true;
    public bool isFrozen;
    
    // Simply move speed
    public bool canMove = true;
    public float movementSpeed = 12.0f;
    public float originalMovementSpeed;
    [SerializeField] private bool isMoving;
    
    // Jumping
    public float originalJumpForce;
    public float jumpForce = 8.0f;
    public float maxFallSpeed;
    public float fallMultiplier;
    public float lowJumpMultiplier;
    private int availableJumps = 1; 
    private int availableJumpsLeft;
    private bool isJumping;
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
    public float startDashTime;
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
    public bool inCombat;
    public float combatTimer;
    public float currentCombatTimer;
    public LayerMask oneWayPlatformLayer; // Assign the "OneWayPlatform" layer in the Inspector
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
    private PlayerHealth ph;
    private Knockback kb;
    public GameObject combatIndicator;
    
    // Start is called before the first frame update
    private void Start()
    {
        SetValues();
        canAct = true;
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        availableJumpsLeft = availableJumps;
        defaultGravity = GetComponent<Rigidbody2D>().gravityScale;
        originalMovementSpeed = movementSpeed;
        originalJumpForce = jumpForce;
        availableWallsLeft = availableWalls;
        
        pco = GetComponent<PlayerCombat>();
        pdf = GetComponent<PlayerDefense>();
        pab = GetComponent<PlayerAbility>();
        ph  = GetComponent<PlayerHealth>();
        kb = GetComponent<Knockback>();
        currentCombatTimer = 0;
    }

    // Update is called once per frame
    private void Update()
    {
        CheckInput();
        CheckMovementDirection();
        UpdateAnimation();
        CheckIfCanJump();
        if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen) Dash();
        if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen) Jump();
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

    private void SetValues()
    {
        if (playerStats != null)
        {
            movementSpeed = playerStats.MovementSpeed;
            dashSpeed = playerStats.DashSpeed;
            startDashTime = playerStats.StartDashTime;
            dashCooldown = playerStats.DashCooldown;
            dashEnergyCost = playerStats.DashEnergyCost;
        }
        else
        {
            Debug.LogError("There isn't a scriptable object found");
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
            currentDashTime = startDashTime;
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
        else if (rb.linearVelocity.y > 0 && !Input.GetButton("Jump"))
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
        if (coyoteTimeCounter > 0f && jumpBufferCounter > 0 && availableJumpsLeft > -1)
        {
            isJumping = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            availableJumpsLeft--;
            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f; // Reset coyote time after jumping
        }
        else if (doubleJump && canDoubleJump && !kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen)
            DoubleJump();
    }
    private void CheckIfCanJump()
    {
        if (isGrounded && rb.linearVelocity.y < 0.01f)
        {
            availableJumpsLeft = availableJumps;
            doubleJump = canDoubleJump; //reset the double jump on if the player can
            isWallJumping = false; // Reset wall jumping flag
        }
        
        if (isJumping && isGrounded)
        {
            isJumping = false;
            isWallJumping = false;
        }
    }

    private void DoubleJump()
    {
        if (!isGrounded && jumpBufferCounter > 0f && !isDashing && !isWallSliding)
        {
            isJumping = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            availableJumpsLeft--;
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
                rb.linearVelocity = new Vector2(movementSpeed * inputDirection, rb.linearVelocity.y);
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
            isJumping = true;
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
        if (currentCombatTimer > 0) inCombat = true;
        if (inCombat)
        {
            combatIndicator.SetActive(true);
            
            ph.regenerationMultiplier = ph.modRegenerationMultiplier;
            pab.energyRefill = pab.modEnergyRefill;
            
            currentCombatTimer -= Time.deltaTime;
            if (currentCombatTimer <= 0) inCombat = false;
        }
        if (!inCombat)
        {
            combatIndicator.SetActive(false);
            
            ph.regenerationMultiplier = ph.originalRegenerationMultiplier;
            pab.energyRefill = pab.originalEnergyRefill;
        }
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
}