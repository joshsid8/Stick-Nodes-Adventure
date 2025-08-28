using System;
using System.Collections;
using UnityEngine;

public class EnemyPatrol : MonoBehaviour
{
    public bool isWalking;
    public float walkSpeed = 5f;
    public float eyeSight = 10f;
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
    
    private Rigidbody2D rb;
    private Animator animator; 
    public bool isFacingRight = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        isFacingRight = true;
        isWalking = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (isWalking) Patrol();
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        
        animator.SetBool("isWalking", isWalking);
    }

    void Patrol()
    {
        if (isFacingRight) rb.linearVelocity = new Vector2(walkSpeed, rb.linearVelocity.y);
        else rb.linearVelocity = new Vector2(-walkSpeed, rb.linearVelocity.y);
        
        touchingWall = Physics2D.Raycast(wallCheck.position, Vector2.down * transform.localScale.y, wallCheckRay, whatIsWall);
        if (touchingWall) Flip(); 
        
        isGrounded = Physics2D.Raycast(edgeCheck.position, Vector2.down * transform.localScale.y, edgeCheckRay, groundLayer);
        if (!isGrounded) Flip();
        
        hasFoundPlayer = Physics2D.Raycast(playerCheck.position, isFacingRight ? Vector2.right : Vector2.left * transform.localScale.x , eyeSight, playerLayer);
        if (hasFoundPlayer  && isWalking) 
            StartCoroutine(FoundPlayer());
    }

    public IEnumerator FoundPlayer()
    {
        rb.linearVelocity = Vector2.zero;
        isWalking = false;
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        player.GetComponent<PlayerController>().currentCombatTimer = player.GetComponent<PlayerController>().combatTimer;
        
        yield return new WaitForSeconds(0.5f);
        GetComponent<EnemyAttack>().enabled = true;
        enabled = false;
    }

    void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }

    void OnDrawGizmosSelected()
    {
        if (!GetComponent<EnemyAttack>())
        {
            Gizmos.color = Color.grey;
            Vector3 rayDirection = isFacingRight ? Vector2.right : Vector2.left * transform.localScale.x * eyeSight;
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
