using System.Collections;
using scripies.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace scripies.Player
{
    public class PlayerController : MonoBehaviour
    {
        #region Variables

        public bool debugMode;
        [Header("Settings")] public bool canAct = true;
        public bool canFlip = true;
        public bool isFrozen;
        public bool isPerformingAction;

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
        public float wallJumpForce;
        public Vector2 wallJumpDirection;
        public float wallJumpTime;
        public bool canWallJump;

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

        [Space] [Header("Physics and QoL")] [HideInInspector]
        public float defaultGravity;

        [HideInInspector] public Rigidbody2D rb;
        [HideInInspector] public Animator animator;
        public float inputDirection;

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
        [SerializeField] private AudioClip[] wallClips;
        [SerializeField] private AudioClip[] dashClips;

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
            pco = GetComponent<PlayerCombat>();
            pab = GetComponent<PlayerAbility>();
            kb = GetComponent<Knockback>();
            pdf = GetComponent<PlayerDefense>();
            pm = GetComponent<PlayerCurrency>();
        }

        // Start is called before the first frame update
        private void Start()
        {
            canAct = true;
            defaultGravity = rb.gravityScale;

            originalMovementSpeed = movementSpeed;
            originalJumpForce = jumpForce;
            availableWallsLeft = availableWalls;
            
            
            #if UNITY_EDITOR
            if (debugMode)
            {
                GameHUD.Instance.ActivateShader();
                PlayerHealth.Instance.playerMaxHealth = 1000;
                PlayerHealth.Instance.playerHealth = 1000;
                PlayerHealth.Instance.respawnTime = 2;
                PlayerHealth.Instance.pm.nodeBucks = 1000;
                PlayerHealth.Instance.spawnOnAwake = false;
                PlayerHealth.Instance.deathClip = null;
                pab.energyRefill = 50;
                canWallJump = true;
                canDoubleJump = true;
            }
            #endif
            
            GameEvents.PlayerAction += OnPlayerAction;
            GameEvents.AddPlayerTime += AddCooldown;
        }

        // Update is called once per frame
        private void Update()
        {
            UpdateAnimation();
            if (rb.bodyType == RigidbodyType2D.Static) return;
            CheckInput();
            CheckMovementDirection();
            CheckIfCanJump();
            if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen)
            {
                DashSettings();
                Jump();
            }
            Other();
        }

        private void OnPlayerAction(string action, bool play)
        {
            if (!play) animator.SetTrigger(action);
            else animator.Play(action);
        }

        private void FixedUpdate()
        {
            CheckEnvironment();
            ApplyMovement();
            WallSlide();
            VariableJumpHeight();
        }

        private void CheckInput()
        {
            inputDirection = Input.GetAxisRaw("Horizontal");

            if (Input.GetButtonDown("Jump")) jumpBufferCounter = jumpBufferTime;
            else jumpBufferCounter -= Time.deltaTime;
        }

        #region Dashing
        private IEnumerator Dash()
        {
            rb.gravityScale = 0;
            isDashing = true;
            var currentDashSpeed = dashSpeed;
            while (currentDashTime > 0)
            {
                rb.linearVelocity = new Vector2(currentDashSpeed, 0) * transform.right;
                currentDashTime -= Time.deltaTime * 1.75f;
                if (currentDashTime <= dashTime * 0.25f) currentDashSpeed -= Time.deltaTime;
                yield return new WaitForFixedUpdate();
            }
            isDashing = false;
            rb.gravityScale = defaultGravity;
            if (!isRunning) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }

        private void DashSettings()
        {
            if (Input.GetButtonDown("Dash") && canDash && dashAbility && !isPerformingAction && pab.energy >= dashEnergyCost)
            {
                canDash = false;
                pab.energy -= dashEnergyCost;
                currentDashTime = dashTime;
                if (isWallSliding)
                {
                    Flip(true);
                    isWallSliding = false;
                    //dashDirection = isFacingRight ? -1 : 1;
                }
                //else dashDirection = isFacingRight ? 1 : -1;
                dashCooldownTimer = dashCooldown;

                if (!isGrounded) hasDashedInAir = true;
                StartCoroutine(Dash());
            }

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

            if (isWallSliding)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, -wallSlideSpeed);
                if (pab.isPerformingAbility) pab.isPerformingAbility = false;
                if (!kb.IsBeingKnocked && !pab.isPerformingAbility && !isFrozen)
                    StartCoroutine(WallJump());
            }
        }

        private IEnumerator WallJump()
        {
            if (isWallSliding && jumpBufferCounter > 0 && canWallJump && !isDashing)
            {
                var wjForce = new Vector2(wallJumpForce * wallJumpDirection.x * (isFacingRight ? -1 : 1),
                    wallJumpForce * wallJumpDirection.y);
                var weakForce = new Vector2(wallJumpForce * wallJumpDirection.x * (isFacingRight ? -1 : 1),
                    wallJumpForce * wallJumpDirection.y / 3);
                var wallJumpTimer = wallJumpTime;
                jumpBufferCounter = 0f;
                isWallSliding = false;
                isWallJumping = true;
                Flip(true);
                while (wallJumpTimer > 0)
                {
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
                while (elapsed < easeDuration && inputDirection == 0 && !isGrounded)
                {
                    elapsed += Time.fixedDeltaTime;
                    var t = elapsed / easeDuration;
                    var newX = Mathf.Lerp(velocityAtEnd.x, 0, t);
                    rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
                    yield return new WaitForFixedUpdate();
                }

                if (inputDirection == 0) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
                isWallJumping = false;
            }
        }

        private void VariableJumpHeight()
        {
            if (rb.linearVelocity.y < 0)
                rb.linearVelocity += Vector2.up * Physics2D.gravity.y * (fallMultiplier - 1) * Time.deltaTime;
            else if (rb.linearVelocity.y > 0 && !Input.GetButton("Jump") && !pco.isAirAttacking && !isWallJumping)
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
            if (coyoteTimeCounter > 0f && jumpBufferCounter > 0 && !isDashing && !isWallSliding)
            {
                isJumping = true;
                if (jumpClips.Length > 0) AudioSource.PlayClipAtPoint(jumpClips[0], transform.position);
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

            if (isJumping && isGrounded) isWallJumping = false;
        }

        private void DoubleJump()
        {
            if (!isGrounded && jumpBufferCounter > 0f && !isDashing && !isWallSliding)
            {
                isJumping = true;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * jumpForceMult);
                doubleJump = false;
                if (jumpClips.Length > 0) AudioSource.PlayClipAtPoint(jumpClips[1] ?? jumpClips[0], transform.position);
            }
        }

        private void ApplyMovement()
        {
            if (canMove && !isFrozen && !kb.IsBeingKnocked && !isWallJumping && !isDashing)
            {
                if (inputDirection != 0)
                {
                    isMoving = true;
                    rb.linearVelocity = new Vector2(movementSpeed * movementSpeedMult * inputDirection,
                        rb.linearVelocity.y);
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

            if (rb.linearVelocity.x <= -0.5f && inputDirection < 0 || rb.linearVelocity.x >= 0.5f && inputDirection > 0) isRunning = true;
            else isRunning = false;
        }

        private void Other()
        {
            if (rb.linearVelocity.y > 0)
            {
                rb.gravityScale = defaultGravity;
            }
            else if (rb.linearVelocity.y <= 0)
            {
                isJumping = false;
                isWallJumping = false;
                if (!isDashing) rb.gravityScale = defaultGravity - 1f;
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
                RaycastHit2D hit = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckCircle + 0.1f,
                    oneWayPlatformLayer);
                if (hit.collider != null && hit.collider.GetComponent<PlatformEffector2D>() != null)
                {
                    StartCoroutine(DropThroughPlatform(hit.collider));
                    jumpBufferCounter = 0f;
                    coyoteTimeCounter = 0f;
                }
            }

            if (isFrozen) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            if (inputDirection == 0 && !isDashing && !isWallJumping && !GetComponent<Knockback>().IsBeingKnocked &&
                rb.linearVelocity.x != 0)
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

            if (isWallJumping || isDashing || pab.isPerformingAbility || pco.isAttacking || pco.isAirAttacking ||
                pdf.isBlocking || PlayerHealth.Instance.isStunned || kb.IsBeingKnocked || PlayerHealth.Instance.isDead)
                isPerformingAction = true;
            else isPerformingAction = false;
        }

        private void UpdateAnimation()
        {
            animator.SetBool("isRunning", isRunning);
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
            if (canFlip && !forced || forced)
            {
                isFacingRight = !isFacingRight;
                transform.Rotate(0.0f, 180.0f, 0.0f);
                if (isWallSliding) isWallSliding = false;
            }
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
                    PlayerHealth.Instance.ApplyCombat(cooldown, additive);
                    break;
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

        private void PlaySound(string fx)
        {
            switch (fx)
            {
                case "Footstep":
                    AudioSource.PlayClipAtPoint(footstepClips[Random.Range(0, footstepClips.Length)], transform.position);
                    break;
                case "Dash":
                    AudioSource.PlayClipAtPoint(dashClips[Random.Range(0, dashClips.Length)], transform.position);
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
        }

        public void Save(ref PlayerSaveCurrentData data)
        {
            data.position = transform.position;
            data.health = PlayerHealth.Instance.playerHealth;
            data.energy = pab.energy;
            data.money = pm.nodeBucks;
            data.wallJumpAbility = canWallJump;
            data.doubleJumpAbility = canDoubleJump;
            
            if (currentSpawnPoint != null)
            {
                var ident = currentSpawnPoint.GetComponent<Identifier>();
                if (ident != null) data.currentSpawnPointId = ident.id;
            }
        }

        public void Load(PlayerSaveCurrentData data)
        {
            transform.position = data.position;
            PlayerHealth.Instance.playerHealth = data.health;
            pab.energy = data.energy;
            pm.nodeBucks = data.money;
            canWallJump = data.wallJumpAbility;
            canDoubleJump = data.doubleJumpAbility;

            if (data.currentSpawnPointId != 0)
            {
                var identifiers = FindObjectsByType<Identifier>(FindObjectsSortMode.None);
                foreach (var ident in identifiers)
                {
                    if (ident.typeOfObject == Identifier.ObjectType.Checkpoint && ident.id == data.currentSpawnPointId)
                    {
                        currentSpawnPoint = ident.gameObject;
                        if (!SceneManager.GetSceneByName(currentSpawnPoint.GetComponent<Checkpoint>().spawnScene.SceneName).isLoaded) SceneManager.LoadScene(currentSpawnPoint.GetComponent<Checkpoint>().spawnScene.SceneName, LoadSceneMode.Additive);
                        //else print($"Scene {SceneManager.GetSceneByName(currentSpawnPoint.GetComponent<Checkpoint>().spawnScene.SceneName)} already loaded");
                        break;
                    }
                }
            }
        }
    }

    [System.Serializable]
    public struct PlayerSaveCurrentData
    {
        public Vector3 position;
        public float health;
        public float energy;
        public float money;
        public int currentSpawnPointId;

        public bool wallJumpAbility;
        public bool doubleJumpAbility;
    }
}