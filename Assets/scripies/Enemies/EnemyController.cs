using Unity.VisualScripting;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public enum EnemyState { Idle, Patrolling, Zoning, Attacking, Stunned, Dead }
    public EnemyState enemyState;
    
    public Enemy enemy;
    private bool canFacePlayer;
    public bool isFacingRight = true;
    
    private EnemyPatrol patrol;
    private EnemyAttack attack;
    private EnemyHealth health;
    private EnemyZone zone;
    private EnemyJump jump;
    private EnemyFly fly;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        patrol = GetComponent<EnemyPatrol>();
        attack = GetComponent<EnemyAttack>();
        health = GetComponent<EnemyHealth>();
        zone = GetComponent<EnemyZone>();
        jump = GetComponent<EnemyJump>();
        fly = GetComponent<EnemyFly>();
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
        }
        else if (enemyState == EnemyState.Dead || enemyState == EnemyState.Idle || enemyState == EnemyState.Patrolling)
        {
            if (PlayerHealth.Instance.aggressiveEnemies.Contains(gameObject))
                PlayerHealth.Instance.aggressiveEnemies.Remove(gameObject);
            jump.isJumping = false;
        }
        jump.isJumping = PlayerHealth.Instance.aggressiveEnemies.Contains(gameObject) && PlayerHealth.Instance.aggressiveEnemies.Count > 1;
        canFacePlayer = attack.canFacePlayer;
        if (enemyState != EnemyState.Idle && enemyState != EnemyState.Patrolling && enemyState != EnemyState.Dead) FacePlayer();
        if (PlayerHealth.Instance.isDead && enemyState != EnemyState.Patrolling) ChangeState(EnemyState.Patrolling);
    }

    public void ChangeState(EnemyState newState)
    {
        if (enemyState == newState) return;
        enemyState = newState;
        switch (enemyState)
        {
            case EnemyState.Idle:
                patrol.isPatrolling = false;
                patrol.enabled = true;
                break;
            case EnemyState.Patrolling:
                patrol.enabled = true;
                patrol.isPatrolling = true;
                attack.enabled = false;
                break;
            case EnemyState.Zoning:
                patrol.enabled = false;
                attack.enabled = false;
                zone.enabled = true;
                break;
            case EnemyState.Attacking:
                patrol.enabled = false;
                zone.enabled = false;
                attack.enabled = true;
                break;
            case EnemyState.Stunned:
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
        if (canFacePlayer)
        {
            var directionToPlayer = PlayerHealth.Instance.transform.position.x - transform.position.x;
            if (directionToPlayer > 0 && !isFacingRight || directionToPlayer < 0 && isFacingRight) Flip();
        }
    }

    public void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }
}
