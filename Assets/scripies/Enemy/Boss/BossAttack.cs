using System;
using System.Collections;
using scripies.Core;
using Unity.VisualScripting;
using UnityEngine;
using Random = UnityEngine.Random;

public class BossAttack : MonoBehaviour
{
    [Header("Smartness")]
    private bool closeRange;
    public float rangeRadius;
    [SerializeField] private bool testing;
    [SerializeField] private int testIndex;
    [SerializeField] private string[] testAttacks;
    
    [Space]
    [Header("Attack Check")]
    public bool isAttacking;
    public bool isPerformingAttack;
    public float attackCooldown;
    public float currentAttackCooldown;
    public int attackFactor;
    private string prevAttack;
    private int prevRepeat;
    
    [Space]
    [Header("Attacks")]
    public string[] defensiveAtk = new string[5];
    public string[] activeAtk = new string[5];
    
    [Space]
    [Header("Checks")]
    public LayerMask playerLayer;
    [SerializeField] private float groundCheckRadius;
    public Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    public bool isGrounded;
    
    [HideInInspector] public float directionToPlayer;
    [HideInInspector] public bool isTouchingWall;
    [SerializeField] private Transform wallCheck;
    [SerializeField] private float wallCheckRay;
    
    public bool isFacingRight;
    public bool canFacePlayer;
    
    private IPlayerStatus ps;
    [HideInInspector] public Transform playerTransform;
    [SerializeField] private float initStart = 1;

    public float backupMoveTimer = 5;
    
    [Space]
    [Header("Stun and Dialogue")]
    public bool stunnable;
    public float stunnableTimer;
    [SerializeField] private int dialogueNo;
    [SerializeField] private bool inRange;
    private bool noMercy;
    private Vector3 originalPosition;
    
    private IDialogueTrigger dt;
    private BossHealth bh;
    private Animator animator;

    private void Awake()
    {
        bh = GetComponent<BossHealth>();
        animator = GetComponent<Animator>();
        dt = GetComponent<IDialogueTrigger>();
        
        if (dt == null) Debug.LogError($"[BossAttack] IDialogueTrigger component not found on {gameObject.name}!");
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        currentAttackCooldown = attackCooldown;
        ps = GameObject.FindGameObjectWithTag("Player").GetComponent<IPlayerStatus>();
        playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
        originalPosition = transform.position;
        
        GameEvents.OnDialogueClose += StartFight;
        GameEvents.OnPlayerDeath += ResetBoss;
    }

    private void OnDestroy()
    {
        GameEvents.OnDialogueClose -= StartFight;
        GameEvents.OnPlayerDeath -= ResetBoss;
    }

    // Update is called once per frame
    private void Update()
    {
        Checks();
        FacePlayer();
        FactorCheck();
    }

    private void FactorCheck() => closeRange = Physics2D.OverlapCircle(transform.position, rangeRadius, playerLayer);
    
    private void StartFight(ScriptableObject dialogueToDerive)
    {
        if (dt == null) return;
        
        if (dialogueToDerive == dt.Dialogue[^1])
        {
            isAttacking = true;
            canFacePlayer = true;
            GameEvents.TriggerToggleDialogue(gameObject, false);
            GameEvents.TriggerAddPlayerCombat(690, false, "Boss");
            GameEvents.TriggerDialogueClose();
            InvokeRepeating(nameof(Movement), 0, 1.5f);
            GameHUD.Instance.ToggleBossHealth(true);
            StartCoroutine(bh.PhaseToggle(false));
            Invoke(nameof(AttackSystem), initStart);
        }
    }
    
    public void AttackSystem()
    {
        if (!isPerformingAttack && !bh.isStunned && !bh.isDead && isAttacking && !testing)
        {
            stunnableTimer = 0;
            var prevPrev = prevAttack;
            if (closeRange)
            {
                var rng = 0;
                if (prevRepeat >= 2)
                {
                    while (prevAttack == defensiveAtk[rng]) rng = Random.Range(0, defensiveAtk.Length);
                }
                else rng = Random.Range(0, activeAtk.Length);
                attackFactor = rng;
                if (defensiveAtk[attackFactor] != null)
                {
                    animator.SetTrigger(defensiveAtk[attackFactor]);
                    prevAttack = defensiveAtk[attackFactor];
                }
                else throw new MissingFieldException($"defensive attack no. {attackFactor} is null!");
            }
            else if (!closeRange)
            {
                var rng = 0;
                if (prevRepeat >= 2)
                {
                    while (prevAttack == activeAtk[rng]) rng = Random.Range(0, activeAtk.Length);
                }
                else rng = Random.Range(0, activeAtk.Length);
                attackFactor = rng;
                if (activeAtk[attackFactor] != null)
                {
                    animator.SetTrigger(activeAtk[attackFactor]);
                    prevAttack = activeAtk[attackFactor];
                }
                else throw new MissingFieldException($"active attack no. {attackFactor} is null!");
            }
            if (prevAttack == prevPrev) prevRepeat++;
            else prevRepeat = 0;
            CancelInvoke(nameof(AttackSystem));
            backupMoveTimer = 5;
        }
        else if (testing && isAttacking)
        {
            animator.SetTrigger(testAttacks[testIndex]);
            CancelInvoke(nameof(AttackSystem));
            backupMoveTimer = 5;
        }
        if (!isAttacking) CancelInvoke(nameof(AttackSystem));
    }
    
    private void Checks()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        isTouchingWall = Physics2D.Raycast(wallCheck.position, Vector2.up * transform.localScale.y, wallCheckRay, LayerMask.GetMask("Wall"));
        animator.SetBool("isGrounded", isGrounded);
        animator.SetBool("isAttacking", isAttacking);
        
        inRange = Physics2D.OverlapCircle(dt.ChatPoint.position, dt.ChatRadius, LayerMask.GetMask("Player"));
        
        if (inRange && dialogueNo >= bh.introDialogue.Length && noMercy)
        {
            if (!bh.isDead && !bh.deathToggle)
            {
                noMercy = false;
                StartFight(dt.Dialogue[^1]);
            }
        }
        
        if (stunnableTimer > 0) stunnableTimer -= Time.deltaTime;
        stunnable = stunnableTimer > 0;
        if (backupMoveTimer > 0 && isAttacking) backupMoveTimer -= Time.deltaTime;
        if (isAttacking && !isPerformingAttack && backupMoveTimer <= 0)
        {
            AttackSystem();
            print("Almost handicapped bro");
        }
    }
    
    private void Movement()
    {
        var rng = Random.Range(0, 101);
        var moveF = Random.Range(0, 2);
        var distanceToPlayer = playerTransform.position.x - transform.position.x;
        moveF = distanceToPlayer switch
        {
            >= 8 => 0,
            <= -8 => 0,
            _ => moveF
        };
        animator.SetInteger("moveFactor", moveF == 0 ? 0 : 1);
        
        if (rng <= 75 && isAttacking && !isPerformingAttack) 
            animator.SetTrigger("Strafe");
    }
    
    private void FacePlayer()
    {
        if (canFacePlayer)
        {
            if (playerTransform == null) return;

            // Determine direction to player
            directionToPlayer = playerTransform.position.x - transform.position.x;
            if (directionToPlayer > 0 && !isFacingRight) Flip();
            else if (directionToPlayer < 0 && isFacingRight) Flip();
        }
    }

    public void ActionCooldown(float cooldownTime)
    {
        currentAttackCooldown = bh.phase2 ? cooldownTime * 0.8f : cooldownTime;
        GuardDown(cooldownTime);
        Invoke(nameof(AttackSystem), currentAttackCooldown);
    }

    private void GuardDown(float stunnableTime = 0.4f) => stunnableTimer = stunnableTime;

    private void ResetBoss() { if (isAttacking) StartCoroutine(Restart()); }
    private IEnumerator Restart()
    {
        isAttacking = false;
        noMercy = false;
        yield return new WaitUntil(() => !ps.IsDead);
        yield return new WaitForSeconds(1f);
        transform.position = originalPosition;
        bh.bossHealth = bh.bossMaxHealth;
        bh.phase2 = false;
        dt.IsForced = true;
        GameHUD.Instance.ToggleBossHealth(false);
        GameEvents.TriggerToggleDialogue(gameObject, true);
        if (dt.Dialogue[0] != bh.introDialogue[^1])
        {
            dt.ClearDialogue();
           
            if (dialogueNo <= bh.introDialogue.Length)
            {
                dt.AddDialogue(bh.introDialogue[dialogueNo]);
                dialogueNo++;
            }
        }
        else
        {
            dt.IsForced = false;
            yield return new WaitForSeconds(1f);
            noMercy = true;
        }
    }

    private void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, rangeRadius);
        Gizmos.color = Color.green;
        Vector3 rayDirection = Vector2.up * transform.localScale.y * wallCheckRay;
        Gizmos.DrawLine(wallCheck.position, wallCheck.position + rayDirection);
    }
}
