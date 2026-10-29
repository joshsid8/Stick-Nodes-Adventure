using System;
using System.Collections;
using scripies.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace scripies.Player
{
    public class PlayerController : MonoBehaviour
    {
        #region Variables

        public bool debugMode;
        [Header("Settings")] public bool canAct = true;
        public bool canFlip = true;
        public bool isFrozen;
        public bool isForceFrozen;
        public bool isPerformingAction;

        private Vector2 preFreezeVelocity;
        private float preFreezeGravityScale;
        private bool preFreezeIsJumping;
        private bool preFreezeIsWallJumping;
        private bool hasStoredFreezeState;

        public bool IsHitstopOrForceFrozen => isForceFrozen || (kb != null && kb.IsFrozenInHitstop);

        [Space] [Header("Movement")] public bool canMove = true;
        public float movementSpeed = 10f;
        public float movementSpeedMult = 1f;
        public float originalMovementSpeed;

        [SerializeField] private bool isMoving;

        [Space] [Header("Jumping")] public float originalJumpForce;
        public float jumpForce = 8.0f;
        public float jumpForceMult = 1f;
        public float maxFallSpeed;
        public float fallMultiplier;
        public float lowJumpMultiplier;
        [SerializeField] private bool isJumping;
        private bool hasJumped;
        public bool canDoubleJump;
        private bool doubleJump;

        [Space] [Header("Jump Optimisations")] [SerializeField]
        private float coyoteTime = 0.2f;

        private float coyoteTimeCounter;
        [SerializeField] private float jumpBufferTime = 0.1f;
        private float jumpBufferCounter;

        [Space] [Header("Wall Jumping")] public float wallSlideSpeed;
        public bool canWallJump;
        public float wallJumpForce;
        public Vector2 wallJumpDirection;
        public float wallJumpTime;

        private bool isTouchingWall;
        public bool isWallSliding;
        public bool isWallJumping;
        [SerializeField] private int availableWalls;
        [SerializeField] private int availableWallsLeft;
        [SerializeField] private Transform wallCheck;
        [SerializeField] private LayerMask whatIsWall;
        [SerializeField] private float wallCheckRay;

        [Space] [Header("Ground Check")] [SerializeField]
        private float groundCheckCircle;

        public Transform groundCheck;
        public LayerMask whatIsGround;
        [HideInInspector] public bool isGrounded;

        [Space] [Header("Dashing")] public float dashSpeed;
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

        [Space] [Header("Corner Correction")]
        public bool enableCornerCorrection = true;
        [Tooltip("Maximum vertical distance the player can be clipped upward onto a ledge corner.")]
        public float cornerCorrectionDistance = 0.55f;
        [Tooltip("Distance forward to check for ledge corners.")]
        public float cornerCorrectionForwardCheck = 0.35f;
        private Collider2D playerCollider;

        [Space] [Header("Physics and QoL")] [HideInInspector]
        public float defaultGravity;

        [HideInInspector] public Rigidbody2D rb;
        [HideInInspector] public Animator animator;
        public PlayerInput input;
        public float moveInputDir;

        public LayerMask oneWayPlatformLayer;
        public float dropThroughDuration = 0.3f;
        public GameObject currentCheckPoint;
        public GameObject currentSpawnPoint;

        [Space] [Header("Running and Flipping")]
        private bool isRunning;

        public bool isFacingRight = true;

        [Space] [Header("Audio & Extra Components")] 
        
        [SerializeField] private AudioClip[] jumpClips;
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] private AudioClip[] wallJumpClips;
        [SerializeField] private AudioClip[] dashClips;

        private PlayerHealth ph;
        private PlayerCombat pco;
        private PlayerDefense pdf;
        private PlayerAbility pab;
        private PlayerCurrency pm;
        private Knockback kb;
        public GameObject combatIndicator;

        #endregion

        #region Main Methods

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            animator = GetComponent<Animator>();
            ph = GetComponent<PlayerHealth>();
            pco = GetComponent<PlayerCombat>();
            pab = GetComponent<PlayerAbility>();
            kb = GetComponent<Knockback>();
            pdf = GetComponent<PlayerDefense>();
            pm = GetComponent<PlayerCurrency>();
            input = new PlayerInput();

            var colliders = GetComponents<Collider2D>();
            foreach (var c in colliders)
            {
                if (!c.isTrigger)
                {
                    playerCollider = c;
                    break;
                }
            }
            if (playerCollider == null) playerCollider = GetComponent<Collider2D>();
        }

        private void OnEnable()
        {
            GameEvents.PlayerAction += OnPlayerAction;
            GameEvents.AddPlayerTime += AddCooldown;
            GameEvents.OnForceFreezePlayer += ForceFreeze;
            input.Enable();
            input.Player.Enable();
            input.Player.Jump.performed += _ => jumpBufferCounter = jumpBufferTime;
            input.Player.Dash.performed += _ =>
            {
                if (!IsHitstopOrForceFrozen) StartCoroutine(Dash());
            };
        }
        private void OnDisable() 
        {
            GameEvents.PlayerAction -= OnPlayerAction;
            GameEvents.AddPlayerTime -= AddCooldown;
            GameEvents.OnForceFreezePlayer -= ForceFreeze;
            isForceFrozen = false;
            hasStoredFreezeState = false;
            input.Disable(); 
            //input.Player.RemoveCallbacks();
        }

        public void ForceFreeze(bool freeze, bool preserveMomentum = true)
        {
            if (rb == null) return;

            if (freeze)
            {
                if (!isForceFrozen)
                {
                    isForceFrozen = true;
                    if (preserveMomentum && !hasStoredFreezeState)
                    {
                        preFreezeVelocity = rb.linearVelocity;
                        preFreezeGravityScale = rb.gravityScale > 0f ? rb.gravityScale : defaultGravity;
                        preFreezeIsJumping = isJumping;
                        preFreezeIsWallJumping = isWallJumping;
                        hasStoredFreezeState = true;
                    }
                }
                else if (preserveMomentum && hasStoredFreezeState)
                {
                    if (rb.linearVelocity.y > preFreezeVelocity.y)
                        preFreezeVelocity.y = rb.linearVelocity.y;
                    if (Mathf.Abs(rb.linearVelocity.x) > Mathf.Abs(preFreezeVelocity.x))
                        preFreezeVelocity.x = rb.linearVelocity.x;
                }

                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = 0f;
            }
            else
            {
                isForceFrozen = false;
                if (hasStoredFreezeState)
                {
                    hasStoredFreezeState = false;
                    isJumping = preFreezeIsJumping;
                    isWallJumping = preFreezeIsWallJumping;
                    rb.gravityScale = preFreezeGravityScale > 0f ? preFreezeGravityScale : defaultGravity;

                    if (preserveMomentum && (kb == null || !kb.IsBeingKnocked))
                    {
                        rb.linearVelocity = preFreezeVelocity;
                    }
                }
                else
                {
                    rb.gravityScale = isDashing ? 0f : defaultGravity;
                }
            }
        }

        // Start is called before the first frame update
        private IEnumerator Start()
        {
            canAct = true;
            defaultGravity = rb.gravityScale;

            originalMovementSpeed = movementSpeed;
            originalJumpForce = jumpForce;
            availableWallsLeft = availableWalls;

            if (currentCheckPoint == null) currentCheckPoint = currentSpawnPoint;
            
            GameHUD.Instance.ActivateShader();
            #if UNITY_EDITOR
            if (debugMode) SetDebugMode();
            yield return new WaitForSeconds(0.5f);
            var isFalling = rb.linearVelocity.y < 0;
            if (isFalling)
            {
                yield return new WaitForSeconds(1);
                if (rb.linearVelocity.y < 0)
                {
                    Debug.LogWarning("Falling for a bit too long! respawning");
                    StartCoroutine(ph.Spawn());
                }
            }
            #endif
            yield return null;
        }
        
        [ContextMenu("Debug Mode")] public void SetDebugMode()
        {
            debugMode = true;
            ph.maxHealth = 1000;
            ph.health = 1000;
            ph.respawnTime = 2;
            ph.pm.nodeBucks = 1000;
            ph.deathClip = null;
            pab.energyRefill = 50;
            canWallJump = true;
            canDoubleJump = true;
        }
        
        // Update is called once per frame
        private void Update()
        {
            if (IsHitstopOrForceFrozen)
            {
                CheckInput();
                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = 0f;
                return;
            }

            UpdateAnimation();
            if (rb.bodyType == RigidbodyType2D.Static) return;
            CheckInput();
            CheckMovementDirection();
            CheckIfCanJump();
            if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen)
            {
                Jump();
                DashSettings();
            }
            Other();
            //print($"P: {input.Player.enabled}, UI: {input.UI.enabled}");
        }

        private void OnPlayerAction(string action, bool play)
        {
            if (!play) animator.SetTrigger(action);
            else animator.Play(action);
        }

        private void FixedUpdate()
        {
            if (IsHitstopOrForceFrozen)
            {
                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = 0f;
                return;
            }

            CheckEnvironment();
            ApplyMovement();
            WallSlide();
            VariableJumpHeight();
        }

        private void CheckInput()
        {
            var rawMoveX = input.Player.Move.ReadValue<Vector2>().x;
            moveInputDir = Mathf.Abs(rawMoveX) > 0.01f ? Mathf.Sign(rawMoveX) : 0f;

            if (!IsHitstopOrForceFrozen && jumpBufferCounter > -0.5) jumpBufferCounter -= Time.deltaTime;
        }

        #region Dashing
        private IEnumerator Dash()
        {
            if (!canDash || !dashAbility || (isPerformingAction && !isWallSliding) || IsHitstopOrForceFrozen || pab.energy < dashEnergyCost) yield break;
            canDash = false;
            pab.energy -= dashEnergyCost;
            currentDashTime = dashTime;
            if (isWallSliding)
            {
                Flip(true);
                isWallSliding = false;
            }
            dashCooldownTimer = dashCooldown;

            if (!isGrounded) hasDashedInAir = true;
            rb.gravityScale = 0;
            isDashing = true;
            var currentDashSpeed = dashSpeed;
            while (currentDashTime > 0)
            {
                if (IsHitstopOrForceFrozen)
                {
                    rb.linearVelocity = Vector2.zero;
                    yield return null;
                    continue;
                }
                CheckCornerCorrection();
                rb.linearVelocity = new Vector2(currentDashSpeed, 0) * transform.right;
                currentDashTime -= Time.deltaTime * 1.75f;
                if (currentDashTime <= dashTime * 0.25f) currentDashSpeed -= Time.deltaTime;
                yield return new WaitForFixedUpdate();
            }
            isDashing = false;
            rb.gravityScale = defaultGravity;
            if (!isRunning) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }

        private void CheckCornerCorrection()
        {
            if (!enableCornerCorrection) return;
            if (playerCollider == null)
            {
                var cols = GetComponents<Collider2D>();
                foreach (var c in cols)
                {
                    if (!c.isTrigger) { playerCollider = c; break; }
                }
                if (playerCollider == null) playerCollider = GetComponent<Collider2D>();
                if (playerCollider == null) return;
            }

            Vector2 dir = isFacingRight ? Vector2.right : Vector2.left;
            Bounds b = playerCollider.bounds;
            float frontX = isFacingRight ? b.max.x : b.min.x;
            float bottomY = b.min.y;
            float topY = b.max.y;
            float height = b.size.y;
            LayerMask checkMask = whatIsGround | whatIsWall;

            float checkDist = Mathf.Max(cornerCorrectionForwardCheck, Mathf.Abs(rb.linearVelocity.x) * Time.fixedDeltaTime + 0.1f);

            // If an obstacle blocks the head/upper level, upward ledge correction should not apply
            RaycastHit2D headHit = Physics2D.Raycast(new Vector2(frontX, topY + 0.05f), dir, checkDist, checkMask);
            if (headHit.collider != null) return;

            // Check if lower or mid body is hitting an obstacle corner
            RaycastHit2D hitLow = Physics2D.Raycast(new Vector2(frontX, bottomY + 0.08f), dir, checkDist, checkMask);
            RaycastHit2D hitMid = Physics2D.Raycast(new Vector2(frontX, (bottomY + topY) * 0.5f), dir, checkDist, checkMask);

            RaycastHit2D wallHit = hitLow.collider != null ? hitLow : hitMid;
            if (wallHit.collider == null) return;

            // Find the top surface of the ledge by raycasting downward
            float obstacleX = wallHit.point.x + (isFacingRight ? 0.06f : -0.06f);
            float startY = topY + cornerCorrectionDistance + 0.1f;
            float rayLength = height + cornerCorrectionDistance + 0.3f;
            RaycastHit2D ledgeHit = Physics2D.Raycast(new Vector2(obstacleX, startY), Vector2.down, rayLength, checkMask);

            if (ledgeHit.collider != null)
            {
                float ledgeY = ledgeHit.point.y;
                float diff = ledgeY - bottomY;

                // If the ledge corner is above the player's feet and within the tolerance:
                if (diff > 0.02f && diff <= cornerCorrectionDistance)
                {
                    float nudgeUp = diff + 0.06f;

                    // Ensure there is room above the player to avoid clipping into a ceiling
                    Vector2 targetCenter = new Vector2(b.center.x, b.center.y + nudgeUp);
                    Collider2D ceilingCheck = Physics2D.OverlapBox(targetCenter, new Vector2(b.size.x * 0.85f, b.size.y * 0.9f), 0f, checkMask);

                    if (ceilingCheck == null)
                    {
                        transform.position += new Vector3(0, nudgeUp, 0);
                        rb.position += new Vector2(0, nudgeUp);
                    }
                }
            }
        }

        private void DashSettings()
        {
            if (hasDashedInAir) canDash = false;

            if (dashCooldownTimer >= 0)
            {
                dashCooldownTimer -= Time.deltaTime;
                canDash = false;
            }
            else if (dashCooldownTimer <= 0 && hasDashedInAir) canDash = false;
            else canDash = dashAbility;
        }
        

        #endregion
        private void WallSlide()
        {
            if (isTouchingWall && !isGrounded && rb.linearVelocity.y < 0 && canWallJump) isWallSliding = true;
            else isWallSliding = false;

            if (!isWallSliding) return;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -wallSlideSpeed);
            if (pab.isPerformingAbility) pab.isPerformingAbility = false;
            if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen && !IsHitstopOrForceFrozen)
                StartCoroutine(WallJump());
        }

        private IEnumerator WallJump()
        {
            yield return new WaitForSeconds(0.05f);
            if (!isWallSliding || !(jumpBufferCounter >= 0) || !canWallJump || isDashing) yield break;
            var wjForce = new Vector2(wallJumpForce * wallJumpDirection.x * (isFacingRight ? -1 : 1),
                wallJumpForce * wallJumpDirection.y);
            var weakForce = new Vector2(wallJumpForce * wallJumpDirection.x * (isFacingRight ? -1 : 1),
                wallJumpForce * wallJumpDirection.y / 3);
            var wallJumpTimer = wallJumpTime;
            jumpBufferCounter = 0f;
            isWallSliding = false;
            isWallJumping = true;
            GameEvents.TriggerPlaySound(wallJumpClips[Random.Range(0, wallJumpClips.Length)], gameObject);
            Flip(true);
            while (wallJumpTimer > 0)
            {
                if (IsHitstopOrForceFrozen)
                {
                    rb.linearVelocity = Vector2.zero;
                    yield return null;
                    continue;
                }
                canFlip = false;
                rb.linearVelocity = availableWallsLeft > 0 ? wjForce : weakForce;
                wallJumpTimer -= Time.deltaTime;
                yield return new WaitForFixedUpdate();
            }
            canFlip = true;
            availableWallsLeft--;

            // lerp stuff i must learn
            const float easeDuration = 0.1f;
            var elapsed = 0f;
            var velocityAtEnd = rb.linearVelocity;
            while (elapsed < easeDuration && moveInputDir == 0 && !isGrounded)
            {
                if (IsHitstopOrForceFrozen)
                {
                    rb.linearVelocity = Vector2.zero;
                    yield return null;
                    continue;
                }
                elapsed += Time.fixedDeltaTime;
                var t = elapsed / easeDuration;
                var newX = Mathf.Lerp(velocityAtEnd.x, 0, t);
                rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
                yield return new WaitForFixedUpdate();
            }

            if (moveInputDir == 0) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            isWallJumping = false;
        }

        private void VariableJumpHeight()
        {
            if (rb.linearVelocity.y < 0)
                rb.linearVelocity += Vector2.up * (Physics2D.gravity.y * (fallMultiplier - 1) * Time.deltaTime);
            else if (rb.linearVelocity.y > 0 && !Input.GetButton("Jump") && !pco.isAirAttacking && !isWallJumping)
            {
                rb.linearVelocity += Vector2.up * (Physics2D.gravity.y * (lowJumpMultiplier - 1) * Time.deltaTime);
                coyoteTimeCounter = 0f;
            }

            // Coyote and jump buffer
            if (isGrounded) coyoteTimeCounter = coyoteTime;
            else coyoteTimeCounter -= Time.deltaTime;

        }

        private void Jump()
        {
            if (coyoteTimeCounter > 0f && jumpBufferCounter > 0 && !isDashing && !isWallSliding)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * jumpForceMult);
                if (rb.linearVelocity.y <= 0) return;
                isJumping = true;
                GameEvents.TriggerPlaySound(jumpClips[Random.Range(0, jumpClips.Length)], gameObject);
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

            if (isJumping && isGrounded) isWallJumping = false;
        }

        private void DoubleJump()
        {
            if (isGrounded || !(jumpBufferCounter > 0f) || isDashing || isWallSliding) return;
            isJumping = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * jumpForceMult * 0.85f);
            doubleJump = false;
            if (jumpClips.Length > 0) GameEvents.TriggerPlaySound(jumpClips[Random.Range(0, jumpClips.Length)], gameObject);
        }

        private void ApplyMovement()
        {
            if (!canMove || isFrozen || IsHitstopOrForceFrozen || kb.IsBeingKnocked || isWallJumping || isDashing || rb.bodyType == RigidbodyType2D.Static) return;

            if (moveInputDir != 0)
            {
                isMoving = true;
                rb.linearVelocity = new Vector2(movementSpeed * movementSpeedMult * moveInputDir, rb.linearVelocity.y);
            }

            if (moveInputDir != 0 || !isMoving) return;
            isMoving = false;
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }


        private void CheckMovementDirection()
        {
            if (isFacingRight && moveInputDir < 0) Flip();
            else if (!isFacingRight && moveInputDir > 0) Flip();

            if (rb.linearVelocity.x <= -0.5f && moveInputDir < 0 || rb.linearVelocity.x >= 0.5f && moveInputDir > 0) isRunning = true;
            else isRunning = false;
        }

        private void Other()
        {
            switch (rb.linearVelocity.y)
            {
                case > 0:
                    rb.gravityScale = defaultGravity;
                    break;
                case <= 0:
                {
                    isJumping = false;
                    isWallJumping = false;
                    if (!isDashing) rb.gravityScale = defaultGravity - 1f;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(rb.linearVelocity.y, -maxFallSpeed));
                    break;
                }
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
            else if (isWallSliding)
            {
                hasDashedInAir = false;
            }

            if (Input.GetAxisRaw("Vertical") < 0 && input.Player.Jump.WasPressedThisFrame() && isGrounded)
            {
                var hit = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckCircle + 0.1f,
                    oneWayPlatformLayer);
                if (hit.collider != null && hit.collider.GetComponent<PlatformEffector2D>() != null)
                {
                    StartCoroutine(DropThroughPlatform(hit.collider));
                    jumpBufferCounter = 0f;
                    coyoteTimeCounter = 0f;
                }
            }

            if (isFrozen) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            if (moveInputDir == 0 && !isDashing && !isWallJumping && !GetComponent<Knockback>().IsBeingKnocked &&
                rb.linearVelocity.x != 0)
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

            if (pab.isPerformingAbility || pco.isAttacking || pco.isAirAttacking || pdf.isBlocking 
                || ph.isStunned || kb.IsBeingKnocked || ph.isDead || isDashing || isWallSliding)
                isPerformingAction = true;
            else isPerformingAction = false;
        }

        private void UpdateAnimation()
        {
            animator.SetBool("isRunning", isRunning);
            //animator.SetFloat("movementSpeed", Mathf.Abs(rb.linearVelocity.x));
            //animator.SetFloat("verticalVelocity", rb.linearVelocity.y);
            animator.SetBool("isJumping", isJumping);
            animator.SetBool("isGrounded", isGrounded);
            animator.SetBool("isWallSliding", isWallSliding);
            animator.SetBool("isDashing", isDashing);
            animator.SetBool("isWallJumping", isWallJumping);
            animator.SetBool("isPerformingAbility", pab.isPerformingAbility);
            animator.SetBool("isHolding", pab.isHolding);
        }

        private void Flip(bool forced = false)
        {
            if (IsHitstopOrForceFrozen) return;
            if ((!canFlip || forced) && !forced) return;
            isFacingRight = !isFacingRight;
            transform.Rotate(0.0f, 180.0f, 0.0f);
            if (isWallSliding) isWallSliding = false;
        }

        private void AddCooldown(string variable, float cooldown, bool additive = false)
        {
            switch (variable)
            {
                case "Dash":
                    dashCooldown = additive ? dashCooldown + cooldown : cooldown;
                    break;
                case "Ability":
                    pab.currentAbilityCooldowns[pab.ability.currentSlot] = additive ? pab.currentAbilityCooldowns[pab.ability.currentSlot] + cooldown * pab.abilityCooldownMult : cooldown * pab.abilityCooldownMult;
                    break;
                case "Combat":
                    ph.ApplyCombat(cooldown, additive);
                    break;
            }
        }

        private IEnumerator DropThroughPlatform(Collider2D platformCollider)
        {
            Physics2D.IgnoreCollision(GetComponent<Collider2D>(), platformCollider, true);

            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -5f);
            
            yield return new WaitForSeconds(0.5f);
            
            var distToPlayer = transform.position.y - platformCollider.transform.position.y;
            var playerBelowPlatform = distToPlayer <= 6;
            
            if (playerBelowPlatform)
            {
                while (distToPlayer <= 0 && platformCollider != null)
                {
                    distToPlayer = transform.position.y - platformCollider.transform.position.y;
                    yield return new WaitForFixedUpdate();
                }
            } 
            yield return new WaitForSeconds(0.1f);
            if (platformCollider != null) Physics2D.IgnoreCollision(GetComponent<Collider2D>(), platformCollider, false);
        }

        private void CheckEnvironment()
        {
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckCircle, whatIsGround);
            isTouchingWall = Physics2D.Raycast(wallCheck.position, Vector2.up * transform.localScale.y, wallCheckRay, whatIsWall);
        }

        private void PlaySound(string fx)
        {
            switch (fx)
            {
                case "Footstep":
                    if (footstepClips.Length > 0) 
                        GameEvents.TriggerPlaySound(footstepClips[Random.Range(0, footstepClips.Length)], gameObject);
                    break;
                case "Dash":
                    if (dashClips.Length > 0) 
                        GameEvents.TriggerPlaySound(dashClips[Random.Range(0, dashClips.Length)], gameObject, 0.6f);
                    break;
            }
        }

        #endregion
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckCircle);
            Gizmos.color = Color.green;
            Vector3 rayDirection = Vector2.up * transform.localScale.y * wallCheckRay;
            Gizmos.DrawLine(wallCheck.position, wallCheck.position + rayDirection);

            if (enableCornerCorrection)
            {
                Gizmos.color = Color.yellow;
                var colToDraw = playerCollider != null ? playerCollider : GetComponent<Collider2D>();
                if (colToDraw != null)
                {
                    Bounds b = colToDraw.bounds;
                    float fx = isFacingRight ? b.max.x : b.min.x;
                    Vector3 d = (isFacingRight ? Vector3.right : Vector3.left) * cornerCorrectionForwardCheck;
                    Gizmos.DrawLine(new Vector3(fx, b.min.y + 0.08f, 0), new Vector3(fx, b.min.y + 0.08f, 0) + d);
                    Gizmos.DrawLine(new Vector3(fx, (b.min.y + b.max.y) * 0.5f, 0), new Vector3(fx, (b.min.y + b.max.y) * 0.5f, 0) + d);
                }
            }
        }

        public void Save(ref PlayerSaveCurrentData data)
        {
            if (currentSpawnPoint != null) data.position = currentSpawnPoint.transform.position;
            data.health = ph.health;
            data.energy = pab.energy;
            data.money = pm.nodeBucks;
            data.abilities = ph.CurrentAbilities;
            data.wallJumpAbility = canWallJump;
            data.doubleJumpAbility = canDoubleJump;
            
            if (currentCheckPoint != null)
            {
                var ident = currentCheckPoint.GetComponent<Identifier>();
                if (ident != null) data.currentCheckPointId = ident.id;
            }
        }

        public void Load(PlayerSaveCurrentData data)
        {
            if (data.position != Vector3.zero && data.position != default) transform.position = data.position;
            ph.health = data.health;
            pab.energy = data.energy;
            pm.nodeBucks = data.money;
            pab.ability.objects = data.abilities;
            canWallJump = data.wallJumpAbility;
            canDoubleJump = data.doubleJumpAbility;

            if (data.currentCheckPointId != 0)
            {
                // We'll search for the checkpoint in the scene when it's loaded.
                // For now, we just store the ID to be used by MapManager.InitialSpawnPlayer
                PlayerPrefs.SetInt("LoadedCheckPointId", data.currentCheckPointId);
            }
        }
    }

    [Serializable]
    public struct PlayerSaveCurrentData
    {
        public Vector3 position;
        public float health;
        public float energy;
        public float money;
        
        public AbilityObjSO[] abilities;

        public bool wallJumpAbility;
        public bool doubleJumpAbility;

        public int currentCheckPointId;
    }
}
