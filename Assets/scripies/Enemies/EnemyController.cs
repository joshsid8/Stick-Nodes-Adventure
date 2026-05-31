using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public enum EnemyState { Idle, Patrolling, Zoning, Attacking, Stunned, Dead }
    public EnemyState enemyState;

    [SerializeField] private EnemyPatrol patrol;
    [SerializeField] private EnemyAttack attack;
    [SerializeField] private EnemyHealth health;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        patrol = GetComponent<EnemyPatrol>();
        attack = GetComponent<EnemyAttack>();
        health = GetComponent<EnemyHealth>();
        
        // Initial state
        if (patrol != null && patrol.isPatrolling)
            ChangeState(EnemyState.Patrolling);
        else
            ChangeState(EnemyState.Idle);

        // Set initial style
        if (attack != null) attack.ApplyStyle(attack.styleFactor);
    }

    // Update is called once per frame
    void Update()
    {
        // 1. Global Checks (Interrupts)
        if (health.isDead && enemyState != EnemyState.Dead)
        {
            ChangeState(EnemyState.Dead);
            return;
        }

        if (health.isStunned && enemyState != EnemyState.Stunned && enemyState != EnemyState.Dead)
        {
            ChangeState(EnemyState.Stunned);
            return;
        }

        // 2. State Machine Logic
        switch (enemyState)
        {
            case EnemyState.Patrolling:
                HandlePatrolState();
                break;
            case EnemyState.Attacking:
                HandleAttackState();
                break;
            case EnemyState.Zoning:
                HandleZoningState();
                break;
            case EnemyState.Stunned:
                if (!health.isStunned) ChangeState(EnemyState.Attacking); // Resume attacking after stun
                break;
            case EnemyState.Idle:
                // Check if we should start patrolling or if player is near
                break;
        }
    }

    public void ChangeState(EnemyState newState)
    {
        if (enemyState == newState) return;
        enemyState = newState;

        // Enable/Disable scripts based on state
        if (patrol != null) patrol.enabled = (newState == EnemyState.Patrolling);
        if (attack != null) attack.enabled = (newState == EnemyState.Attacking || newState == EnemyState.Zoning);
    }

    void HandlePatrolState()
    {
        if (!patrol.enabled)
        {
            patrol.enabled = true;
            if (!attack.enabled) attack.enabled = true;
        }
    }

    void HandleAttackState()
    {
        if (attack.playerTransform == null) return;
        var dist = Vector2.Distance(transform.position, attack.playerTransform.position);
        if (dist >= attack.passiveModeRadius * 1.5f) ChangeState(EnemyState.Patrolling);
    }

    void HandleZoningState()
    {
        var dist = Vector2.Distance(transform.position, attack.playerTransform.position);
        
    }
}
