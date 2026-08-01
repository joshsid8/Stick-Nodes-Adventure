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
    
    [Space]
    [Header("Attack Check")]
    public bool isAttacking;
    public bool isPerformingAttack;
    public float attackCooldown;
    public float currentAttackCooldown;
    public int attackFactor;
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
    
    [Space]
    [Header("Stun and Dialogue")]
    public bool stunnable;
    public float stunnableTimer;
    [SerializeField] private int dialogueNo = -1;
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
        
        if (dt == null)
        {
            Debug.LogError($"[BossAttack] IDialogueTrigger component not found on {gameObject.name}!");
        }
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
            GameEvents.TriggerToggleDialogue(gameObject, false);
            GameEvents.TriggerAddPlayerCombat(690, false, "Boss");
            GameEvents.TriggerDialogueClose();
            canFacePlayer = true;
            InvokeRepeating(nameof(AttackSystem), initStart, currentAttackCooldown);
        }
    }
    
    private void AttackSystem()
    {
        if (!isPerformingAttack && !bh.isStunned && !bh.isDead && isAttacking)
        {
            stunnableTimer = 0;
            if (closeRange)
            {
                attackFactor = Random.Range(0, defensiveAtk.Length);
                if (defensiveAtk[attackFactor] != null) animator.SetTrigger(defensiveAtk[attackFactor]);
            }
            else if (!closeRange)
            {
                attackFactor = Random.Range(0, activeAtk.Length);
                if (activeAtk[attackFactor] != null) animator.SetTrigger(activeAtk[attackFactor]);
            }
        }
        if (!isAttacking) CancelInvoke(nameof(AttackSystem));
    }
    
    private void Checks()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        isTouchingWall = Physics2D.Raycast(wallCheck.position, Vector2.up * transform.localScale.y, wallCheckRay, LayerMask.GetMask("Wall"));
        animator.SetBool("isGrounded", isGrounded);
        animator.SetBool("isAttacking", isAttacking);
        
        BossHealthUI.Instance.gameObject.SetActive(isAttacking);
        
        inRange = Physics2D.OverlapCircle(dt.ChatPoint.position, dt.ChatRadius, LayerMask.GetMask("Player"));
        
        if (inRange && dialogueNo >= bh.introDialogue.Length && !dt.Enabled && noMercy) 
            if (!bh.isDead && !bh.deathToggle) isAttacking = true; 
        
        if (stunnableTimer > 0) stunnableTimer -= Time.deltaTime;
        stunnable = stunnableTimer > 0;
    }
    
    private void Movement()
    {
        var rng = Random.Range(0, 101);
        if (rng <= 70 && isAttacking)
        {
            var mf = Random.Range(0, 2);
            animator.SetInteger("moveFactor", mf == 0 ? 0 : 1);
            animator.SetTrigger("Strafe");
        }
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
    }

    private void GuardDown(float stunnableTime = 0.4f) => stunnableTimer = stunnableTime;

    private void ResetBoss() { if (isAttacking) StartCoroutine(Restart()); }
    private IEnumerator Restart()
    {
        isAttacking = false;
        noMercy = false;
        yield return new WaitUntil(() => !ps.IsDead);
        transform.position = originalPosition;
        bh.bossHealth = bh.bossMaxHealth;
        bh.phase2 = false;
        dt.IsForced = true;
        GameEvents.TriggerToggleDialogue(gameObject, true);
        yield return new WaitForSeconds(1f);
        noMercy = true;
        /*if (dialogueNo <= bh.introDialogue.Length - 1)
        {
            dt.AddDialogue(bh.introDialogue[dialogueNo]);
            dialogueNo++;
        }*/
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
