using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public enum EnemyState { Idle, Patrolling, Zoning, Attacking, Stunned, Dead }
    public EnemyState enemyState;

    private EnemyPatrol patrol;
    private EnemyAttack attack;
    private EnemyHealth health;
    private EnemyZone zone;

    public bool isFacingRight = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        patrol = GetComponent<EnemyPatrol>();
        attack = GetComponent<EnemyAttack>();
        health = GetComponent<EnemyHealth>();
        zone = GetComponent<EnemyZone>();
        if (patrol != null && patrol.isPatrolling) ChangeState(EnemyState.Patrolling);
        else ChangeState(EnemyState.Idle);
        isFacingRight = true;
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
            case EnemyState.Stunned:
                if (!health.isStunned)
                {
                    var rng = Random.Range(0, 101);
                    ChangeState(rng <= 50 ? EnemyState.Attacking: EnemyState.Zoning);
                }
                break;
            case EnemyState.Dead:
                health.isDead = true;
                break;
            default:
                InactiveState();
                break;
        }
        FacePlayer();
    }

    public void ChangeState(EnemyState newState)
    {
        if (enemyState == newState) return;
        enemyState = newState;

        switch (enemyState)
        {
            case EnemyState.Idle:
                patrol.enabled = true;
                patrol.isPatrolling = false;
                break;
            case EnemyState.Patrolling:
                patrol.enabled = true;
                patrol.isPatrolling = true;
                break;
            case EnemyState.Zoning:
                patrol.enabled = false;
                zone.enabled = true;
                attack.enabled = false;
                break;
            case EnemyState.Attacking:
                patrol.enabled = false;
                attack.enabled = true;
                zone.enabled = false;
                break;
            case EnemyState.Stunned:
                patrol.enabled = false;
                zone.enabled = false;
                attack.enabled = false;
                break;
            case EnemyState.Dead:
                patrol.enabled = false;
                zone.enabled = false;
                attack.enabled = false;
                break;
        }
    }

    private void InactiveState(int factor = 1)
    {
        if (enemyState != EnemyState.Idle && enemyState != EnemyState.Patrolling) return;
        if (!patrol.enabled) patrol.enabled = true;
        patrol.isPatrolling = factor != 1;
    }
    
    private void FacePlayer()
    {
        if (attack.canFacePlayer)
        {
            if (attack.playerTransform == null) return;
            var directionToPlayer = attack.playerTransform.position.x - transform.position.x;
            if (directionToPlayer > 0 && !isFacingRight || directionToPlayer < 0 && isFacingRight) Flip();
        }
    }

    public void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }
}
