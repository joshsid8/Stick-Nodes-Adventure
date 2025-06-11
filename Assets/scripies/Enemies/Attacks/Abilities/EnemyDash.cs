using UnityEngine;

public class EnemyDash : MonoBehaviour
{
    private Rigidbody2D rb;
    private Transform playerTransform;
    public float dashSpeed = 10f; // Add a dash speed
    public float backDashSpeed = 10f;
    private bool isDashing;
    private bool isBackDashing;
    private bool canMoveAfterDash;

    public float dashDuration;
    public float backDashDuration;
    private float currentDashDuration;

    private EnemyPatrol ep;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        ep = GetComponent<EnemyPatrol>();
        playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
        canMoveAfterDash = true;
        currentDashDuration = 99999999999999999999f;
    }

    private void Update()
    {
        if (isDashing)
        {
            isDashing = false;
            rb.linearVelocity = ep.isFacingRight ? new Vector2(dashSpeed, rb.linearVelocity.y) : new Vector2(-dashSpeed, rb.linearVelocity.y);
        }
        if (isBackDashing)
        {
            isBackDashing = false;
            rb.linearVelocity = ep.isFacingRight ? new Vector2(-backDashSpeed, rb.linearVelocity.y) : new Vector2(backDashSpeed, rb.linearVelocity.y);
        }

        if (!isDashing)
        {
            currentDashDuration -= Time.deltaTime;
            if (currentDashDuration <= 0f)
            {
                canMoveAfterDash = false;
            }
        }
        else if (!isBackDashing)
        {
            currentDashDuration -= Time.deltaTime;
            if (currentDashDuration <= 0f)
            {
                canMoveAfterDash = false;
            }
        }

        if (!canMoveAfterDash)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            canMoveAfterDash = true;
        }
        else
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        }
    }
    
    public void FrontDash()
    {
        currentDashDuration = dashDuration;
        isDashing = true;
    }
    public void BackDash()
    {
        currentDashDuration = backDashDuration;
        isBackDashing = true;
    }
}