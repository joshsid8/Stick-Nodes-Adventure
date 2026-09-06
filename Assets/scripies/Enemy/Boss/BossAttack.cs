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
    [SerializeField] private bool useMovement = true;
    
    [Space]
    [Header("Attack Check")]
    public bool isAttacking;
    public bool isPerformingAttack;
    public float currentAttackCooldown;
    public int attackFactor;
    [SerializeField] private string prevAttack;
    [SerializeField] private int prevRepeat;
    
    [Space]
    [Header("Attacks")]
    public string[] defensiveAtk = new string[5];
    public string[] activeAtk = new string[5];
    [Space] public string[] antiWallAtk = new string[1];
    
    [Space]
    [Header("Checks")]
    public LayerMask playerLayer;
    [HideInInspector] public float directionToPlayer;
    [SerializeField] private float groundCheckRadius;
    public Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    public bool isGrounded;
    
    public bool isTouchingWall;
    [SerializeField] private float antiWallTimer;
    [SerializeField] private float wallCheckRadius;
    
    public bool isFacingRight;
    public bool canFacePlayer;
    
    private IPlayerStatus ps;
    [HideInInspector] public Transform playerTransform;
    [SerializeField] private float initStart = 1;
    
    [SerializeField] private float backupMoveTime = 5;
    [SerializeField] private float currentBackupMoveTimer;
    
    [Space]
    [Header("Stun and Dialogue")]
    public bool stunnable;
    public float stunnableTimer;
    [SerializeField] private int dialogueNo;
    [SerializeField] private bool inRange;
    private bool forceStart;
    private Vector3 originalPosition;
    
    private IDialogueTrigger dt;
    private BossHealth bh;
    private Animator animator;

    private void Awake()
    {
        bh = GetComponent<BossHealth>();
        animator = GetComponent<Animator>();
        dt = GetComponent<IDialogueTrigger>();
        
        if (dt == null)
        {
            Debug.LogError($"[BossAttack] IDialogueTrigger component not found on {gameObject.name}!");
            return;
        }
        GameEvents.TriggerToggleDialogue(gameObject, false);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        ps = GameObject.FindGameObjectWithTag("Player").GetComponent<IPlayerStatus>();
        playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
        originalPosition = transform.position;
        
        GameEvents.OnDialogueClose += StartFightOnDialogueClose;
        GameEvents.OnPlayerDeath += ResetBoss;

        yield return new WaitForSeconds(1f);
        GameEvents.TriggerToggleDialogue(gameObject, true);
    }

    private void OnDestroy()
    {
        GameEvents.OnDialogueClose -= StartFightOnDialogueClose;
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

    private void StartFightOnDialogueClose(ScriptableObject dialogueToDerive)
    {
        if (dialogueToDerive == null || dt.Dialogue.Length == 0) return; 
        if (dialogueToDerive != dt.Dialogue[^1] || dt == null) return;
        StartFight();
    }
    
    public void StartFight()
    {
        isAttacking = true;
        canFacePlayer = true;
        GameEvents.TriggerToggleDialogue(gameObject, false);
        GameEvents.TriggerAddPlayerCombat(690, false, "Boss");
        GameEvents.TriggerDialogueClose();
        if (useMovement) InvokeRepeating(nameof(Movement), 0, 1.5f);
        GameHUD.Instance.ToggleBossHealth(true);
        StartCoroutine(bh.PhaseToggle(false));
        Invoke(nameof(AttackSystem), initStart);
    }
    
    public void InvokeAttackSystem(float time) => Invoke(nameof(AttackSystem), time);
    public bool IsInvokingAttackSystem()
    {
        return IsInvoking(nameof(AttackSystem));
    }
    
    public void AttackSystem()
    {
        if (!isPerformingAttack && !bh.isStunned && !bh.isDead && isAttacking && !testing)
        {
            if (isGrounded) animator.Play("Idle");
            stunnableTimer = 0;
            var prevPrev = prevAttack;
            var rng = 0;
            switch (closeRange)
            {
                case true:
                    if (prevRepeat >= 1)
                    {
                        //print("Spamming the saem moves!");
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
                    break;
                case false:
                    if (prevRepeat >= 1)
                    {
                        //print("Spamming the saem moves!!");
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
                    break;
            }
            if (prevAttack == prevPrev) prevRepeat++;
            else prevRepeat = 0;
        }
        else if (testing && isAttacking) animator.SetTrigger(testAttacks[testIndex]);
        if (!isAttacking) ResetTriggers();
    }
    
    private void Checks()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        isTouchingWall = Physics2D.OverlapCircle(transform.position, wallCheckRadius, groundLayer);
        if (isTouchingWall) antiWallTimer -= Time.deltaTime;
        
        animator.SetBool("isGrounded", isGrounded);
        animator.SetBool("isAttacking", isAttacking);
        
        inRange = Physics2D.OverlapCircle(dt.ChatPoint.position, dt.ChatRadius, LayerMask.GetMask("Player"));
        currentAttackCooldown -= Time.deltaTime;
        if (currentAttackCooldown < 0) currentAttackCooldown = 0;

        if (inRange && dt.Dialogue.Length == 0 && forceStart && !bh.isDead && !bh.deathToggle)
        {
            forceStart = false;
            StartFight();
        }

        if (isAttacking && !isPerformingAttack && currentBackupMoveTimer <= 0)
        {
            Debug.LogError($"Almost handicapped {gameObject.name} atk cooldown: {currentAttackCooldown}"); 
            currentAttackCooldown = backupMoveTime;
            AttackSystem();
        }

        if (isTouchingWall && isAttacking && antiWallTimer <= 0)
        {
            animator.SetTrigger(antiWallAtk[Random.Range(0, antiWallAtk.Length)]);
            antiWallTimer = 1;
        }
        
        if (isPerformingAttack)
        {
            currentBackupMoveTimer = backupMoveTime;
            currentAttackCooldown = 1f;
        }
        if (currentBackupMoveTimer > 0 && isAttacking && !isPerformingAttack) currentBackupMoveTimer -= Time.deltaTime;
    }

    public void ResetTriggers()
    {
        foreach (var anim in activeAtk) animator.ResetTrigger(anim);
        foreach (var anim in defensiveAtk) animator.ResetTrigger(anim);
        CancelInvoke(nameof(AttackSystem));
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
        if (playerTransform == null || !canFacePlayer) return;

        // Determine direction to player
        directionToPlayer = playerTransform.position.x - transform.position.x;
        if (directionToPlayer > 0 && !isFacingRight) Flip();
        else if (directionToPlayer < 0 && isFacingRight) Flip();
    }

    public void ActionCooldown(float cooldownTime = 0)
    {
        CancelInvoke(nameof(ActionCooldown));
        currentAttackCooldown = bh.phase2 ? cooldownTime * 0.8f : cooldownTime;
        GuardDown(cooldownTime);
        Invoke(nameof(AttackSystem), currentAttackCooldown);
    }

    private void GuardDown(float stunnableTime = 0.4f)
    {
        stunnableTimer = stunnableTime;
        StartCoroutine(Stunnability());
    }

    private IEnumerator Stunnability()
    {
        while (stunnableTimer > 0)
        {
            stunnable = true;
            stunnableTimer -= Time.deltaTime;
            yield return null;
        }
        stunnable = false;
    }

    private void ResetBoss() { if (isAttacking) StartCoroutine(Restart()); }
    private IEnumerator Restart()
    {
        isAttacking = false;
        forceStart = false;
        yield return new WaitUntil(() => !ps.IsDead);
        yield return new WaitForSeconds(0.2f);
        
        transform.position = originalPosition;
        bh.bossHealth = bh.bossMaxHealth;
        bh.phase2 = false;
        GameHUD.Instance.ToggleBossHealth(false);
        yield return new WaitForSeconds(0.5f);
        GameEvents.TriggerToggleDialogue(gameObject, true);
        dt.IsForced = true;
        
        if (dt.Dialogue.Length >= 1) yield break;
        dt.IsForced = false;
        forceStart = true;
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
        Gizmos.DrawWireSphere(transform.position, wallCheckRadius);
    }
}
