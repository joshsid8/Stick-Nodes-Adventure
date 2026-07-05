using System.Collections;
using UnityEngine;

public class EnemyZone : MonoBehaviour
{
    public float currentZonePeriod;
    [SerializeField] private bool isZoning;
    [SerializeField] private float zoneTime = 1;
    public float activeZoneTime;
    [SerializeField] private float zoneSpeed = 10;
    [SerializeField] private float zoneInterval = 0.3f;
    private EnemyController ec;
    private EnemyAttack ea;
    private EnemyJump ej;
    
    private EnemyAttack.StyleMode styleMode;
    private bool defensiveCheck;
    private bool passiveCheck;
    private Rigidbody2D rb;
    private Animator animator;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start() => currentZonePeriod = ea.currentAttackCooldown; 
    // AWAKE HOWEVER IS BEFORREEEE
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
        animator.SetBool("isZoning", isZoning);
        if (currentZonePeriod <= 0 && !ej.isJumping || currentZonePeriod <= 0 && ej.isJumping && ej.myTurn) ec.ChangeState(EnemyController.EnemyState.Attacking);
        //else if (currentZonePeriod <= 0 && ej.isJumping && !ej.myTurn) then we dont know
        else currentZonePeriod -= Time.deltaTime;
    }

    private void FixedUpdate() => StartCoroutine(Zone());
    
    private void OnDisable()
    {
        isZoning = false;
        animator.SetBool("isZoning", false);
    }

    private IEnumerator Zone()
    {
        var playerDist = PlayerHealth.Instance.transform.position.x - transform.position.x;
        if (activeZoneTime > 0) activeZoneTime -= Time.deltaTime;
        else
        {
            activeZoneTime = zoneTime;
            isZoning = !isZoning;
            if (!isZoning) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }
        
        if (ec.enemyState == EnemyController.EnemyState.Zoning && activeZoneTime > 0 && isZoning)
        {
            switch (styleMode)
            {
                case EnemyAttack.StyleMode.Active:
                    if (!defensiveCheck)
                        rb.linearVelocity = new Vector2(playerDist > 0 ? zoneSpeed : -zoneSpeed, rb.linearVelocity.y);
                    break;
                case EnemyAttack.StyleMode.Passive:
                    if (!passiveCheck)
                        rb.linearVelocity = new Vector2(playerDist > 0 ? zoneSpeed : -zoneSpeed, rb.linearVelocity.y);
                    break;
                case EnemyAttack.StyleMode.Evasive:
                    if (defensiveCheck || passiveCheck)
                        rb.linearVelocity = new Vector2(playerDist > 0 ? -zoneSpeed : zoneSpeed, rb.linearVelocity.y);
                    else rb.linearVelocity = new Vector2(playerDist > 0 ? zoneSpeed : -zoneSpeed, rb.linearVelocity.y);
                    break;
                default:
                    int[] rng = {Random.Range(0, 101), Random.Range(0, 101)};
                    rb.linearVelocity = new Vector2(rng[0] <= 100 ? rng[1] <= 50 ? zoneSpeed : -zoneSpeed : 0, rb.linearVelocity.y);
                    break;
            }
            yield return new WaitForSeconds(zoneInterval);
        }
    }
}
