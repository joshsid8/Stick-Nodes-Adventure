using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyPatrol : MonoBehaviour
{
    public bool isPatrolling = true;
    public float walkSpeed = 5f;
    public float eyeSight = 10f;
    
    [SerializeField] private bool canWalk = true;
    private bool isWalking = true;
    [SerializeField] private float[] walkTime = {2, 4f};
    private float currentWalkTime;
    [SerializeField] private float randomPause;
    [SerializeField] private float[] randomPauseChance = {20f, 101f};
    
    [SerializeField] private bool hasFoundPlayer;
    public Transform playerCheck;
    public LayerMask playerLayer;
    
    [SerializeField] private bool touchingWall;
    [SerializeField] private Transform wallCheck;
    [SerializeField] private LayerMask whatIsWall;
    [SerializeField] private float wallCheckRay = 2f;
    
    public bool isGrounded;
    [SerializeField] private Transform edgeCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float edgeCheckRay = 2f;
    
    [HideInInspector] public Rigidbody2D rb;
    private Animator animator; 
    public EnemyController ec;
    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        ec = GetComponent<EnemyController>();
    }

    private void OnDisable()
    {
        animator.SetBool("isWalking", false);
        animator.SetBool("isPatrolling", false);
    }

    // Update is called once per frame
    void Update()
    {
        //if (isPatrolling) Patrol();
        //else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        Patrol();
        animator.SetBool("isPatrolling", isPatrolling);
        animator.SetBool("isWalking", isWalking);
        
        currentWalkTime -= Time.deltaTime;
        if (currentWalkTime <= 0) isWalking = true; 
    }
    
    void Patrol()
    {
        if (isWalking && canWalk) rb.linearVelocity = new Vector2(ec.isFacingRight ? walkSpeed : -walkSpeed, rb.linearVelocity.y);
        
        randomPause -= Time.deltaTime;
        if (randomPause <= 0 && canWalk)
        {
            var chance = Random.Range(randomPauseChance[0], randomPauseChance[1]);
            var rng = Random.Range(0, 100);
            if (rng >= chance) StartCoroutine(Pause());
            randomPause = 2;
        }
        
        touchingWall = Physics2D.Raycast(wallCheck.position, Vector2.down * transform.localScale.y, wallCheckRay, whatIsWall);
        if (touchingWall) Flip();
        
        isGrounded = Physics2D.Raycast(edgeCheck.position, Vector2.down * transform.localScale.y, edgeCheckRay, groundLayer);
        if (!isGrounded && rb.linearVelocity.y == 0) Flip();
        
        hasFoundPlayer = Physics2D.Raycast(playerCheck.position, ec.isFacingRight ? Vector2.right : Vector2.left * transform.localScale.x , eyeSight, playerLayer);
        if (hasFoundPlayer && isPatrolling) StartCoroutine(FoundPlayer());
    }

    public IEnumerator FoundPlayer()
    {
        rb.linearVelocity = Vector2.zero;
        isPatrolling = false;
        isWalking = false;
        var player = PlayerHealth.Instance.gameObject;
        player.GetComponent<PlayerController>().currentCombatTimer = player.GetComponent<PlayerController>().combatTimer;
        
        yield return new WaitForSeconds(0.5f);
        if (TryGetComponent<EnemyController>(out var ec)) ec.ChangeState(EnemyController.EnemyState.Zoning);
        else print("uhhhh, no controller");
    }

    private IEnumerator Pause()
    {
        isWalking = false;
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        if (canWalk) currentWalkTime = Random.Range(walkTime[0], walkTime[1]);
        yield return new WaitForSeconds(currentWalkTime / 2);
        var rng = Random.Range(0, 101);
        var chance = Random.Range(0, 101);
        if (rng >= chance) Flip();
        randomPause = 3;
    }
    private void Flip() => ec.Flip();

    private void OnDrawGizmosSelected()
    {
        if (!GetComponent<EnemyAttack>())
        {
            Gizmos.color = Color.grey;
            Vector3 rayDirection = ec.isFacingRight ? Vector2.right : Vector2.left * transform.localScale.x * eyeSight;
            Gizmos.DrawLine(playerCheck.position, playerCheck.position + rayDirection);
            Gizmos.color = Color.red;
            Vector3 rayDirection2 = Vector2.down * transform.localScale.y * wallCheckRay;
            Gizmos.DrawLine(wallCheck.position, wallCheck.position + rayDirection2);
            Gizmos.color = Color.green;
            Vector3 rayDirection3 = Vector2.down * transform.localScale.y * edgeCheckRay;
            Gizmos.DrawLine(edgeCheck.position, edgeCheck.position + rayDirection3);
        }
    }
}
