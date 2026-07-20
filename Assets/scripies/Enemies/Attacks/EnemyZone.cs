using System.Collections;
using UnityEngine;

public class EnemyZone : MonoBehaviour
{
    [Header("Zoning")]
    public float currentZonePeriod;
    [SerializeField] private bool isZoning;
    [SerializeField] private float zoneTime = 1;
    public float activeZoneTime;
    [SerializeField] private float zoneSpeed = 10;
    [SerializeField] private float zoneInterval = 0.3f;
    [SerializeField] private float intervalTimer;
    
    //[Header("Checks (Being updated by enemyAttack)")]
    private EnemyAttack.StyleMode styleMode;
    private bool defensiveCheck;
    private bool passiveCheck;
    
    private EnemyController ec;
    private EnemyAttack ea;
    private EnemyJump ej;
    private Rigidbody2D rb;
    private Animator animator;
    
    void Awake()
    {
        ec = GetComponent<EnemyController>();
        ea = GetComponent<EnemyAttack>();
        rb = GetComponent<Rigidbody2D>();
        ej = GetComponent<EnemyJump>();
        animator = GetComponent<Animator>();
    }
    
    // Update is called once per frame
    private void Update()
    {
        styleMode = ea.styleMode;
        defensiveCheck = ea.defensiveCheck;
        passiveCheck = ea.passiveCheck;
        defensiveCheck = Physics2D.OverlapCircle(transform.position, ea.defensiveModeRadius, ea.pLayer);
        passiveCheck = Physics2D.OverlapCircle(transform.position, ea.passiveModeRadius, ea.pLayer);
        
        animator.SetBool("isZoning", rb.linearVelocity.x != 0);
        if (currentZonePeriod <= 0 && !ej.isJumping || currentZonePeriod <= 0 && ej.isJumping && ej.myTurn) ec.ChangeState(EnemyController.EnemyState.Attacking);
        //else if (currentZonePeriod <= 0 && ej.isJumping && !ej.myTurn) then we dont know
        else currentZonePeriod -= Time.deltaTime;
        intervalTimer -= Time.deltaTime;
        if (activeZoneTime > 0) activeZoneTime -= Time.deltaTime;
        else
        {
            isZoning = !isZoning;
            if (!isZoning) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            else activeZoneTime = zoneTime;
        }
    }

    private void FixedUpdate()
    {
        if (intervalTimer <= 0 && activeZoneTime > 0 && isZoning)
        {
            Zone();
            intervalTimer = zoneInterval;
        }
    }
    private void OnEnable()
    {
        intervalTimer = zoneInterval;
        currentZonePeriod = ea.currentAttackCooldown;
        isZoning = true;
    }
    
    private void OnDisable()
    {
        isZoning = false;
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        animator.SetBool("isZoning", false);
    }

    private void Zone()
    {
        var playerDist = PlayerHealth.Instance.transform.position.x - transform.position.x;

        switch (styleMode)
        {
            case EnemyAttack.StyleMode.Active:
                if (!defensiveCheck)
                    rb.linearVelocity = new Vector2(playerDist > 0 ? zoneSpeed : -zoneSpeed, rb.linearVelocity.y);
                else
                {
                    int[] rng = { Random.Range(0, 101), Random.Range(0, 101) };
                    rb.linearVelocity = new Vector2(rng[0] <= 50 ? rng[1] <= 50 ? zoneSpeed : -zoneSpeed : 0,
                        rb.linearVelocity.y);
                }
                break;
            case EnemyAttack.StyleMode.Passive:
                if (defensiveCheck)
                    rb.linearVelocity = new Vector2(playerDist > 0 ? -zoneSpeed : zoneSpeed, rb.linearVelocity.y);
                else if (!passiveCheck && !defensiveCheck)
                    rb.linearVelocity = new Vector2(playerDist > 0 ? zoneSpeed : -zoneSpeed, rb.linearVelocity.y);
                else if (passiveCheck)
                {
                    int[] rng = { Random.Range(0, 101), Random.Range(0, 101) };
                    rb.linearVelocity = new Vector2(rng[0] <= 50 ? rng[1] <= 50 ? zoneSpeed : -zoneSpeed : 0,
                        rb.linearVelocity.y);
                }
                break;
            case EnemyAttack.StyleMode.Evasive:
                if (defensiveCheck || passiveCheck)
                    rb.linearVelocity = new Vector2(playerDist > 0 ? -zoneSpeed : zoneSpeed, rb.linearVelocity.y);
                else
                {
                    rb.linearVelocity = new Vector2(playerDist > 0 ? zoneSpeed : -zoneSpeed, rb.linearVelocity.y);
                }
                break;
            default:
                int[] rng2 = { Random.Range(0, 101), Random.Range(0, 101) };
                rb.linearVelocity = new Vector2(rng2[0] <= 50 ? rng2[1] <= 50 ? zoneSpeed : -zoneSpeed : 0,
                    rb.linearVelocity.y);
                break;
        }
    }
}
