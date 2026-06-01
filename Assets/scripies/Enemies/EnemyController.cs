using System.Collections;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public enum EnemyState { Idle, Patrolling, Zoning, Attacking, Stunned, Dead }
    public EnemyState enemyState;

    [SerializeField] private EnemyPatrol patrol;
    [SerializeField] private EnemyAttack attack;
    [SerializeField] private EnemyHealth health;
    
    [SerializeField] private float altTimer;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        patrol = GetComponent<EnemyPatrol>();
        attack = GetComponent<EnemyAttack>();
        health = GetComponent<EnemyHealth>();
        
        if (patrol != null && patrol.isPatrolling) ChangeState(EnemyState.Patrolling);
        else ChangeState(EnemyState.Idle);
    }

    private void Update()
    {
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

        switch (enemyState)
        {
            case EnemyState.Idle:
                InactiveState();
                break;
            case EnemyState.Patrolling:
                InactiveState(2);
                break;
            case EnemyState.Attacking:
                AttackState();
                break;
            case EnemyState.Zoning:
                ZoningState();
                break;
            case EnemyState.Stunned:
                if (!health.isStunned) ChangeState(EnemyState.Attacking); 
                break;
            case EnemyState.Dead:
                health.isDead = true;
                break;
            default:
                InactiveState();
                break;
        }
        altTimer -= Time.deltaTime;
    }

    public void ChangeState(EnemyState newState)
    {
        if (enemyState == newState) return;
        enemyState = newState;

        if (patrol != null) patrol.enabled = newState == EnemyState.Patrolling;
        if (attack != null) attack.enabled = newState == EnemyState.Attacking || newState == EnemyState.Zoning;
    }

    private void InactiveState(int factor = 1)
    {
        if (!patrol.enabled) patrol.enabled = true;
        patrol.isPatrolling = factor != 1;
    }

    private void AttackState()
    {
        if (attack.playerTransform == null) return;
        var dist = Vector2.Distance(transform.position, attack.playerTransform.position);
        if (dist >= attack.passiveModeRadius * 10f) ChangeState(EnemyState.Patrolling);
        
        if (altTimer <= 0)
        {
            var rng = Random.Range(0, 101);
            if (rng <= 50)
            {
                ChangeState(EnemyState.Zoning);
                altTimer = 1f;
            }
            else altTimer = 1.5f;
        }
    }

    private void ZoningState()
    {
        if (altTimer <= 0)
        {
            var rng = Random.Range(0, 101);
            if (rng <= 50)
            {
                ChangeState(EnemyState.Attacking);
                altTimer = 2f;
            }
            else altTimer = 0.5f;
        }
    }
}
