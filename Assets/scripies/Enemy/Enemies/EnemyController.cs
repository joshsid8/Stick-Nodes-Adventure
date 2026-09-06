using scripies.Core;
using Unity.VisualScripting;
using UnityEngine;

namespace scripies.Enemy
{
    public class EnemyController : MonoBehaviour, ITarget
    {
        
        public enum EnemyState
        {
            Idle,
            Patrolling,
            Zoning,
            Attacking,
            Stunned,
            Dead
        }

        [EnumButtons] public EnemyState enemyState;

        public Enemy enemy;
        private bool canFacePlayer;
        public bool isFacingRight = true;
        public bool vegetable;
        private EnemyPatrol patrol;
        [HideInInspector] public EnemyAttack attack;
        private EnemyHealth health;
        private EnemyZone zone;
        private EnemyJump jump;
        
        private Transform playerTransform;
        private IPlayerStatus ps;
        private bool isAggressive;
        public float backupSpareTimer;

        // ITarget implementation
        public Transform Transform => transform;
        public bool IsActive => !health.isDead;
        public int Priority => 0; // Default priority

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            patrol = GetComponent<EnemyPatrol>();
            attack = GetComponent<EnemyAttack>();
            health = GetComponent<EnemyHealth>();
            zone = GetComponent<EnemyZone>();
            jump = GetComponent<EnemyJump>();
            backupSpareTimer = attack.spareTimer;
            var player = GameObject.FindGameObjectWithTag("Player");
            playerTransform = player.transform;
            ps = player.GetComponent<IPlayerStatus>();
            if (enemy != null) enemy.SetProperties(attack);
            if (patrol != null && patrol.isPatrolling) ChangeState(EnemyState.Patrolling);
            else ChangeState(EnemyState.Idle);
            isFacingRight = true;
        }

        private void Update()
        {
            if (vegetable) return;
            
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
                        ChangeState(rng <= 50 ? EnemyState.Attacking : EnemyState.Zoning);
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
                if (!isAggressive)
                {
                    isAggressive = true;
                    GameEvents.TriggerAggressiveStatusChanged(this, true);
                }
            }
            else if (enemyState == EnemyState.Dead || enemyState == EnemyState.Idle || enemyState == EnemyState.Patrolling)
            {
                if (isAggressive)
                {
                    isAggressive = false;
                    GameEvents.TriggerAggressiveStatusChanged(this, false);
                }
                if (jump != null) jump.isJumping = false;
            }

            if (jump != null && ps.AggressiveEnemies > 1) jump.isJumping = isAggressive; 

            canFacePlayer = attack.canFacePlayer;
            
            if (backupSpareTimer > 0) backupSpareTimer -= Time.deltaTime / 1.5f;
            else if (backupSpareTimer <= 0 && enemyState == EnemyState.Attacking) ChangeState(EnemyState.Patrolling);
            
            if (enemyState != EnemyState.Idle && enemyState != EnemyState.Patrolling &&
                enemyState != EnemyState.Dead) FacePlayer();
            if (ps.IsDead && enemyState != EnemyState.Patrolling) ChangeState(EnemyState.Patrolling);
        }

        public void ChangeState(EnemyState newState)
        {
            if (enemyState == newState || vegetable) return;
            enemyState = newState;
            switch (enemyState)
            {
                case EnemyState.Idle:
                    patrol.isPatrolling = false;
                    patrol.enabled = true;
                    attack.enabled = false;
                    break;
                case EnemyState.Patrolling:
                    patrol.enabled = true;
                    patrol.hasFoundPlayer = false;
                    patrol.isPatrolling = true;
                    zone.enabled = false;
                    //if (!jump.isJumping) GameEvents.TriggerAddPlayerCombat(0, false, "EnemyLostPlayer");
                    attack.canAttack = true;
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
                case EnemyState.Dead:
                    patrol.enabled = false;
                    zone.enabled = false;
                    attack.enabled = false;
                    break;
                case EnemyState.Stunned:
                default:
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
                var directionToPlayer = playerTransform.position.x - transform.position.x;
                if (directionToPlayer > 0 && !isFacingRight || directionToPlayer < 0 && isFacingRight) Flip();
            }
        }

        public void Flip()
        {
            isFacingRight = !isFacingRight;
            transform.Rotate(0.0f, 180.0f, 0.0f);
        }
    }
}