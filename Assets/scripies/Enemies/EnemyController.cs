using Unity.VisualScripting;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public enum EnemyState { Idle, Patrolling, Zoning, Attacking, Jumping, Stunned, Dead }
    public EnemyState enemyState;

    private EnemyPatrol patrol;
    private EnemyAttack attack;
    private EnemyHealth health;
    private EnemyZone zone;
    private EnemyJump jump;
    [HideInInspector] public string idName;
    public Enemy enemy;

    public bool isFacingRight = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        patrol = GetComponent<EnemyPatrol>();
        attack = GetComponent<EnemyAttack>();
        health = GetComponent<EnemyHealth>();
        zone = GetComponent<EnemyZone>();
        if (enemy != null) enemy.SetProperties(attack);
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
        
        if (enemyState == EnemyState.Attacking || enemyState == EnemyState.Zoning)
        {
            if (!PlayerHealth.Instance.aggressiveEnemies.Contains(gameObject))
                PlayerHealth.Instance.aggressiveEnemies.Add(gameObject);
            if (PlayerHealth.Instance.aggressiveEnemies.Count > 1) ChangeState(EnemyState.Jumping);
        }
        else
        {
            if (PlayerHealth.Instance.aggressiveEnemies.Contains(gameObject))
                PlayerHealth.Instance.aggressiveEnemies.Remove(gameObject);
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
                patrol.isPatrolling = false;
                jump.enabled = false;
                patrol.enabled = true;
                break;
            case EnemyState.Patrolling:
                patrol.isPatrolling = true;
                jump.enabled = false;
                patrol.enabled = true;
                break;
            case EnemyState.Zoning:
                patrol.enabled = false;
                attack.enabled = false;
                jump.enabled = false;
                zone.enabled = true;
                break;
            case EnemyState.Attacking:
                patrol.enabled = false;
                zone.enabled = false;
                jump.enabled = false;
                attack.enabled = true;
                break;
            case EnemyState.Jumping:
                patrol.enabled = false;
                zone.enabled = false;
                jump.enabled = true;
                break;
            case EnemyState.Stunned:
            case EnemyState.Dead:
                patrol.enabled = false;
                zone.enabled = false;
                attack.enabled = false;
                jump.enabled = false;
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
