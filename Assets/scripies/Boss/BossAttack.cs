using System.Collections;
using UnityEngine;
public class BossAttack : MonoBehaviour
{
    // Smartness (get it? haha...)
    public float actionFactor;
    private bool closeRange;
    public float rangeRadius;
    
    // boss attack system
    public bool isAttacking;
    [SerializeField] private bool canAttack;
    public float attackCooldown;
    public float currentAttackCooldown;
    
    public float attackFactor;
    private Animator animator;
    
    public string activeMovement;
    public string defensiveMovement;
    
    public string atk1;
    public string atk2;
    public string atk3;
    public string atk4;
    public string atk5;
    public string atk6;
    
    // Checks
    public LayerMask playerLayer;
    [SerializeField] private float groundCheckRadius;
    public Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    public bool isGrounded;
    private BossHealth bh;
    [HideInInspector] public float directionToPlayer;

    [HideInInspector] public bool isTouchingWall;
    [SerializeField] private Transform wallCheck;
    [SerializeField] private float wallCheckRay;

    public bool isFacingRight;
    public bool canFacePlayer;
    [HideInInspector] public Transform playerTransform;

    [SerializeField] private bool playerIsDead;
    private DialogueTrigger dt;
    [SerializeField] private int dialogueNo = -1;
    [SerializeField] private bool inRange;
    private bool noMercy;
    private Vector3 originalPosition;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        bh = GetComponent<BossHealth>();
        animator = GetComponent<Animator>();
        currentAttackCooldown = attackCooldown;
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null) playerTransform = playerObject.transform;
        else Debug.LogError("bruh");
        dt = GetComponent<DialogueTrigger>();
        originalPosition = transform.position;
    }

    // Update is called once per frame
    private void Update()
    {
        Checks();
        FacePlayer();
        FactorCheck();
        if (isAttacking) AttackSystem();
        StartCoroutine(Restart());
        if (!PlayerHealth.Instance.isDead && playerIsDead) playerIsDead = false;
        inRange = Physics2D.OverlapCircle(dt.chatPoint.position, dt.chatRadius, LayerMask.GetMask("Player"));
        if (inRange && dialogueNo >= bh.introDialogue.Length && !dt.enabled && noMercy && !bh.isDead) isAttacking = true;
    }

    private void FactorCheck()
    {
        closeRange = Physics2D.OverlapCircle(transform.position, rangeRadius, playerLayer);
    }
    
    private void AttackSystem()
    {
        GetComponent<DialogueTrigger>().enabled = false;
        PlayerHealth.Instance.pc.currentCombatTimer = 5;
        canFacePlayer = true;
        if (canAttack)
        {
            attackFactor = Random.Range(1, 6);
            if (closeRange)
            {
                switch (attackFactor)
                {
                    case 1:
                        if (atk1 != null) animator.SetTrigger(atk1); // Active 1
                        break;
                    case 2:
                        if (atk2 != null) animator.SetTrigger(atk2); // Active 2
                        break;
                    case 3:
                        if (atk5 != null) animator.SetTrigger(atk5); // Proactive 1
                        break;
                    case 4:
                        if (atk6 != null) animator.SetTrigger(atk6); // Proactive 2
                        break;
                    case 5:
                        if (defensiveMovement != null) animator.SetTrigger(defensiveMovement); // Movement
                        ActionCooldown(2f);
                        break;
                    default:
                        attackFactor = 0;
                        ActionCooldown(2f);
                        break;
                }
            }
            else if (!closeRange)
            {
                switch (attackFactor)
                {
                    case 1:
                        if (atk3 != null) animator.SetTrigger(atk3); // Passive 1
                        break;
                    case 2:
                        if (atk4 != null) animator.SetTrigger(atk4); // Passive 2
                        break;
                    case 3:
                        if (atk5 != null) animator.SetTrigger(atk5); // Proactive
                        break;
                    case 4:
                        if (atk6 != null) animator.SetTrigger(atk6); // Proactive 2
                        break;
                    case 5:
                        if (defensiveMovement != null) animator.SetTrigger(activeMovement); // Movement
                        ActionCooldown(2f);
                        break;
                    default:
                        actionFactor = 0;
                        ActionCooldown(2f);
                        break;
                }
            }
            ActionCooldown(2f);
        }
    }
    
    private void Checks()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        isTouchingWall = Physics2D.Raycast(wallCheck.position, Vector2.up * transform.localScale.y, wallCheckRay, LayerMask.GetMask("Wall"));
        animator.SetBool("isGrounded", isGrounded);
        BossHealthUI.Instance.gameObject.SetActive(isAttacking);
        
        if (currentAttackCooldown > 0 && isAttacking)
        {
            canAttack = false;
            currentAttackCooldown -= Time.deltaTime;
        }
        else if (currentAttackCooldown <= 0 && isAttacking && isGrounded) canAttack = true;
    }
    
    void FacePlayer()
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
        currentAttackCooldown = bh.phase2 ?  cooldownTime * 0.5f : cooldownTime;
    }

    IEnumerator Restart()
    {
        if (PlayerHealth.Instance.isDead && !playerIsDead)
        {
            playerIsDead = true;
            isAttacking = false;
            noMercy = false;
            yield return new WaitForSeconds(PlayerHealth.Instance.respawnTime);
            transform.position = originalPosition;
            bh.bossHealth = bh.bossMaxHealth;
            yield return new WaitForSeconds(1f);
            noMercy = true;
            if (dialogueNo <= bh.introDialogue.Length - 1)
            {
                dt.enabled = true;
                if (dialogueNo == -1)
                {
                    dt.dialogue = bh.introDialogue[dialogueNo + 1];
                    dialogueNo++;
                }
                else if (dialogueNo == bh.introDialogue.Length) dt.dialogue = bh.introDialogue[dialogueNo - 1];
                else dt.dialogue = bh.introDialogue[dialogueNo];
                dt.isForced = true;
                dialogueNo++;
            }
        }
    }

    void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }

    private void OnDrawGizmos()
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
