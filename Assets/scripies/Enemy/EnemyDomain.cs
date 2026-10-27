using System;
using scripies.Enemy;
using UnityEngine;

// Vibe-coded by Gemini, implement later
public class EnemyDomain : MonoBehaviour
{
    [Header("Aggro")] 
    public bool enemyInBounds;
    public Collider2D domainCollider;
    
    public PlayerBounds playerBounds = new();

    private EnemyController ec;
    private EnemyPatrol ep;
    private EnemyMovement em;
    private EnemyFly ef;
    private Animator animator;
    private Rigidbody2D rb;
    public ParkourFeatures parkourFeatures;
    public enum ReturnType
    {
        Parkour, Flying, Teleporting
    }
    [EnumButtons] public ReturnType returnMethod = ReturnType.Parkour;
    [Tooltip("For Teleporting/Flying Only")] public string returnAnim;

    private Collider2D homeDomainCollider;
    private Vector2 initialSpawnPoint;
    private bool isReturning;
    private float returnElapsed;
    private float stuckTimer;
    private float jumpCooldown;
    private LayerMask terrainMask;

    private void Start()
    {
        ec = GetComponent<EnemyController>();
        ep = GetComponent<EnemyPatrol>();
        em = GetComponent<EnemyMovement>();
        ef = GetComponent<EnemyFly>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();

        initialSpawnPoint = transform.position;
        terrainMask = LayerMask.GetMask("Ground");
        if (terrainMask == 0) terrainMask = ~LayerMask.GetMask("Player", "Enemy", "EDead", "Ignore Raycast", "FX", "PVFX", "EVFX");

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerBounds.playerCollider = playerObj.GetComponent<Collider2D>();

        if (domainCollider == null)
            domainCollider = FindClosestSpawnerDomain();

        homeDomainCollider = domainCollider;
        playerBounds.aggroTimer = playerBounds.aggroPeriod;
    }

    private void Update()
    {
        EnsureValidDomain();
        if (domainCollider == null || ec == null || ec.vegetable || ec.enemyState == EnemyController.EnemyState.Dead) return;

        if (playerBounds.playerCollider == null)
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerBounds.playerCollider = playerObj.GetComponent<Collider2D>();
        }

        enemyInBounds = domainCollider.OverlapPoint(transform.position);
        playerBounds.playerInBounds = playerBounds.playerCollider != null &&
                                      domainCollider.OverlapPoint(playerBounds.playerCollider.transform.position);

        if (jumpCooldown > 0f) jumpCooldown -= Time.deltaTime;

        if (playerBounds.playerInBounds)
        {
            playerBounds.aggroTimer = playerBounds.aggroPeriod;
            if (isReturning && enemyInBounds)
            {
                FinishReturn();
                return;
            }
        }
        else
        {
            playerBounds.aggroTimer = Mathf.Clamp(playerBounds.aggroTimer - Time.deltaTime, 0f, playerBounds.aggroPeriod);
        }

        // If currently returning to domain, update return movement until safely back inside
        if (isReturning)
        {
            if (IsInsideDomainMargin())
            {
                FinishReturn();
            }
            else
            {
                UpdateReturnMovement();
            }
            return;
        }

        // Keep patrolling enemies inside their domain even outside of combat
        if (!enemyInBounds && (ec.enemyState == EnemyController.EnemyState.Patrolling || ec.enemyState == EnemyController.EnemyState.Idle))
        {
            ReturnToDomain();
            return;
        }

        if (playerBounds.aggroTimer > 0f) return;

        // Player has been outside the domain for aggroPeriod
        if (!enemyInBounds)
        {
            ReturnToDomain();
        }
        else if (ec.enemyState == EnemyController.EnemyState.Attacking || ec.enemyState == EnemyController.EnemyState.Zoning)
        {
            ec.LostPlayer(false);
            playerBounds.aggroTimer = playerBounds.aggroPeriod;
        }
    }

    private void EnsureValidDomain()
    {
        // If our original home domain is active again, return to using it
        if (homeDomainCollider != null && homeDomainCollider.enabled && homeDomainCollider.gameObject.activeInHierarchy)
        {
            domainCollider = homeDomainCollider;
            return;
        }

        if (domainCollider != null && domainCollider.enabled && domainCollider.gameObject.activeInHierarchy)
            return;

        // If the domain was deleted or disabled, find a neighbouring domain in the same scene
        domainCollider = FindClosestSpawnerDomain();
    }

    private Collider2D FindClosestSpawnerDomain()
    {
        var spawners = GameObject.FindGameObjectsWithTag("Spawner");
        Collider2D bestCollider = null;
        var bestDist = float.MaxValue;

        foreach (var spawner in spawners)
        {
            if (spawner == null || spawner.gameObject.scene != gameObject.scene || !spawner.activeInHierarchy) continue;
            if (!spawner.TryGetComponent<Collider2D>(out var col) || !col.enabled) continue;

            if (col.OverlapPoint(transform.position))
                return col;

            var dist = Vector2.Distance(transform.position, col.bounds.center);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestCollider = col;
            }
        }
        return bestCollider;
    }

    private void ReturnToDomain()
    {
        if (domainCollider == null) return;
        isReturning = true;
        returnElapsed = 0f;
        stuckTimer = 0f;
        ec.LostPlayer(true);

        if (returnMethod == ReturnType.Teleporting)
        {
            TeleportToDomain();
            return;
        }

        if (returnMethod == ReturnType.Flying)
        {
            if (!string.IsNullOrEmpty(returnAnim) && animator != null)
                animator.Play(returnAnim);
            if (ef != null)
            {
                ef.isFlying = true;
                ef.isChasing = false;
                ef.isTargeting = false;
            }
            else if (rb != null)
            {
                rb.gravityScale = 0f;
            }
        }
        else if (returnMethod == ReturnType.Parkour)
        {
            parkourFeatures.running = true;
        }
    }

    private void UpdateReturnMovement()
    {
        returnElapsed += Time.deltaTime;
        var targetPoint = GetReturnTargetPoint();
        var toTarget = targetPoint - (Vector2)transform.position;

        // Face toward the domain horizontally
        if ((toTarget.x > 0.2f && !ec.isFacingRight) || (toTarget.x < -0.2f && ec.isFacingRight))
            ec.Flip();

        // If the enemy is out of sight of the player after trying to return (or stuck for too long), teleport home
        if ((returnElapsed > 1.5f && IsOffScreen()) || returnElapsed > 8f)
        {
            TeleportToDomain();
            return;
        }

        switch (returnMethod)
        {
            case ReturnType.Teleporting:
                TeleportToDomain();
                break;

            case ReturnType.Flying:
            {
                var flySpeed = ef != null && ef.currentFlySpeed > 0f
                    ? ef.currentFlySpeed
                    : (parkourFeatures.speed > 0f ? parkourFeatures.speed : 10f);
                var dir = toTarget.normalized;

                // Simple obstacle avoidance raycast while flying back
                var hit = Physics2D.CircleCast(transform.position, 0.8f, dir, 3f, terrainMask);
                if (hit.collider != null && hit.collider != domainCollider)
                {
                    var normal = hit.normal;
                    var steerUp = new Vector2(-normal.y, Mathf.Abs(normal.x) + 0.75f).normalized;
                    dir = (dir + steerUp * 1.25f).normalized;
                }

                rb.gravityScale = 0f;
                rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, dir * flySpeed, Time.deltaTime * 6f);
                break;
            }

            case ReturnType.Parkour:
            {
                parkourFeatures.running = true;
                var moveSpeed = parkourFeatures.speed > 0f
                    ? parkourFeatures.speed
                    : (ep != null ? ep.walkSpeed * 1.35f : 7f);
                var horizDir = Mathf.Sign(toTarget.x);
                rb.linearVelocity = new Vector2(horizDir * moveSpeed, rb.linearVelocity.y);

                if (animator != null)
                {
                    animator.SetBool("isPatrolling", true);
                    animator.SetBool("isWalking", true);
                }

                var isGrounded = Physics2D.Raycast(transform.position, Vector2.down, 2.2f, terrainMask);
                var wallAhead = Physics2D.Raycast(transform.position, new Vector2(horizDir, 0f), 2.0f, terrainMask);
                var groundAhead = Physics2D.Raycast((Vector2)transform.position + new Vector2(horizDir * 1.5f, 0f), Vector2.down, 3.0f, terrainMask);

                // Jump over walls, gaps, or up toward a higher domain platform
                if (isGrounded && jumpCooldown <= 0f && (wallAhead.collider != null || (!groundAhead.collider && toTarget.y >= -1f) || toTarget.y > 2.5f))
                {
                    var jumpVel = em != null && em.jumpForceY > 0f ? em.jumpForceY : 20f;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVel);
                    jumpCooldown = 0.6f;
                }

                if (Mathf.Abs(rb.linearVelocity.x) < 0.15f)
                {
                    stuckTimer += Time.deltaTime;
                    if (stuckTimer >= 2.0f)
                    {
                        TeleportToDomain();
                        return;
                    }
                }
                else
                {
                    stuckTimer = 0f;
                }
                break;
            }
        }
    }

    private void TeleportToDomain()
    {
        if (!string.IsNullOrEmpty(returnAnim) && animator != null)
            animator.Play(returnAnim);

        var dest = GetGroundedReturnPoint();
        transform.position = new Vector3(dest.x, dest.y, transform.position.z);
        if (rb != null) rb.linearVelocity = Vector2.zero;
        FinishReturn();
    }

    private void FinishReturn()
    {
        isReturning = false;
        parkourFeatures.running = false;
        stuckTimer = 0f;
        returnElapsed = 0f;
        playerBounds.aggroTimer = playerBounds.aggroPeriod;

        if (ef != null) ef.CeaseFlying();
        else if (rb != null && rb.gravityScale == 0f) rb.gravityScale = 8f;

        if (ec != null && ec.enemyState != EnemyController.EnemyState.Dead)
            ec.ChangeState(EnemyController.EnemyState.Patrolling);
    }

    private bool IsInsideDomainMargin()
    {
        if (domainCollider == null || !domainCollider.OverlapPoint(transform.position)) return false;
        var center = (Vector2)domainCollider.bounds.center;
        return Mathf.Abs(transform.position.x - center.x) <= domainCollider.bounds.extents.x * 0.8f;
    }

    private Vector2 GetReturnTargetPoint()
    {
        if (domainCollider == null) return initialSpawnPoint;
        return domainCollider.bounds.center;
    }

    private Vector2 GetGroundedReturnPoint()
    {
        var center = GetReturnTargetPoint();
        var hit = Physics2D.Raycast(center + Vector2.up * 2f, Vector2.down, 15f, terrainMask);
        if (hit.collider != null && domainCollider != null && domainCollider.OverlapPoint(hit.point + Vector2.up * 1f))
            return hit.point + Vector2.up * 1.2f;
        return center;
    }

    private bool IsOffScreen()
    {
        var cam = Camera.main;
        if (cam == null) return true;
        var vp = cam.WorldToViewportPoint(transform.position);
        return vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f;
    }
    
    [Serializable]
    public class PlayerBounds
    {
        public bool playerInBounds;
        public Collider2D playerCollider;
        public float aggroPeriod = 5f;
        public float aggroTimer;
    }
    
    [Serializable]
    public struct ParkourFeatures
    {
        public bool running;
        public float speed;
    }
}
