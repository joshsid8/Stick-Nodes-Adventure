using System;
using scripies.Core;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace scripies.Enemy
{
    public class EnemyAttack : MonoBehaviour
    {
        public enum StyleMode { Active, Passive, Evasive }

        [SerializeField] private bool testing;
        [SerializeField] private string[] testMoves;
        [SerializeField] private int testIndex;
        
        [EnumButtons] 
        [Tooltip(
            "When the Enemy is:\n" +
            "Active: they strive to come as close as possible to the player and attack at close range.\n" + 
            "Passive: they try to keep a certain distance between them and the player, using any moves at their disposal to do so. This can also be considered Defensive\n" + 
            "Evasive: similar to Passive, they focus on trying to stay as far away as possible from the player, always trying to back away if close.")] 
        public StyleMode styleMode;
        [Space]
        [Header("Fly stuff")] public bool canFly;
        public int movesBeforeFlyingCount;
        public int[] movesBeforeFlying = new int[2];
        [SerializeField] private string flyTrigger = "FlyJump";
        
        [Space]
        [Header("Moves and Checks")] [Tooltip("When the player runs near")]
        public bool defensiveCheck;

        [Tooltip("Certain actions performed if the player isn't close or far")]
        public bool passiveCheck;

        [Tooltip("The enemy coming close to the player")]
        public string[] activeMoves;

        [Tooltip("Certain actions performed if the player isn't close or far")]
        public string[] passiveMoves;

        [Tooltip("The enemy evading/backing up from the player")]
        public string[] evasiveMoves;

        public string[] flyingActiveMoves;
        public string[] flyingPassiveMoves;
        public string[] flyingEvasiveMoves;

        [Space] public float defensiveModeRadius;
        public float modDefensiveModeRadius;
        private float originalDefensiveModeRadius;

        public float passiveModeRadius;
        public float modPassiveModeRadius;
        private float originalPassiveModeRadius;

        [Space] [Header("Melee Attack")] 
        public float stunTime;
        public float damageAmount;
        public int elementType;
        [FormerlySerializedAs("knockbackForce")] 
        public Vector2 knockbackDirection = new(1, 0);
        public float knockbackForce = 20f;
        public float knockbackTime = 0.2f;
        public bool positionKnockback;
        public Transform knockbackTransform;
        [SerializeField] private AudioClip[] combatSfx = new AudioClip[2];
        
        public bool isTrue;
        public bool damageOverTime;
        public float procDmg;
        public float procInterval;
        private RaycastHit2D hit;
        public float attackRange;
        public Transform attackTransform;
        public int damageFactor;

        public LayerMask pLayer;
        [HideInInspector] public Transform playerTransform;
        
        [Space] [Header("Combat Checks")] 
        public bool canAttack;
        public bool isAttacking;
        public float currentAttackCooldown;
        public bool stunnable;
        public bool canFacePlayer;

        [Space] [Header("References")] [HideInInspector]
        public Rigidbody2D rb;

        public Animator animator;
        [HideInInspector] public EnemyPatrol ep;
        [HideInInspector] public EnemyMovement em;
        [HideInInspector] public EnemyFly ef;
        [HideInInspector] public EnemyHealth eh;
        private EnemyController ec;
        [HideInInspector] public EnemyZone ez; // ez lol git gud

        [Space] [Header("Sparing (To complete)")]
        public string spareAnimation;

        [SerializeField] private bool canSpare = true;
        public float spareTimer = 10;
        public float currentSpareTimer;

        private void Awake()
        {
            ep = GetComponent<EnemyPatrol>();
            animator = GetComponent<Animator>();
            rb = GetComponent<Rigidbody2D>();
            em = GetComponent<EnemyMovement>();
            ef = GetComponent<EnemyFly>();
            eh = GetComponent<EnemyHealth>();
            ec = GetComponent<EnemyController>();
            ez = GetComponent<EnemyZone>();
        }

        private void OnEnable()
        {
            originalDefensiveModeRadius = defensiveModeRadius;
            originalPassiveModeRadius = passiveModeRadius;
            playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
            em.enabled = true;
        }
        
        public void SetProperties(Enemy e)
        {
            damageAmount *= e.damageMult;
            eh.enemyHealth *= e.healthMult;
            em.dashSpeed *= e.speedMult;
            if (ef != null) ef.currentFlySpeed *= e.speedMult;
            ep.walkSpeed *= e.speedMult;
        }

        private void OnDisable()
        {
            isAttacking = false;
            if (canFly) ef.CeaseFlying();
        }

        private void Update()
        {
            CheckModes();

            if (ec.enemyState == EnemyController.EnemyState.Attacking)
            {
                if (testing)
                {
                    if (canAttack && !isAttacking && !eh.isStunned)
                    {
                        em.FreezeX();
                        animator.Play(testMoves[testIndex] ?? testMoves[0] ?? passiveMoves[Random.Range(0, passiveMoves.Length)]);
                    }
                }
                else
                {
                    switch (styleMode)
                    {
                        case StyleMode.Active:
                            passiveModeRadius = originalDefensiveModeRadius;
                            defensiveModeRadius = originalDefensiveModeRadius;
                            ActiveLogic();
                            break;
                        case StyleMode.Passive:
                            defensiveModeRadius = modDefensiveModeRadius;
                            passiveModeRadius = originalPassiveModeRadius;
                            PassiveLogic();
                            break;
                        case StyleMode.Evasive:
                            passiveModeRadius = modPassiveModeRadius;
                            defensiveModeRadius = 8f;
                            EvasiveLogic();
                            break;
                        default:
                            passiveModeRadius = originalDefensiveModeRadius;
                            defensiveModeRadius = originalDefensiveModeRadius;
                            break;
                    }
                }
            }

            if (!stunnable) GetComponent<EnemyHealth>().stunMultiplier = 0f;
            else if (stunnable && styleMode == StyleMode.Passive) GetComponent<EnemyHealth>().stunMultiplier = 1.2f;
            else if (stunnable && styleMode == StyleMode.Evasive) GetComponent<EnemyHealth>().stunMultiplier = 1.5f;
            else GetComponent<EnemyHealth>().stunMultiplier = 1f;

            //if (canFly) animator.SetBool("ef.isFlying", ef.isFlying);
            //rb.gravityScale = ef.isFlying ? 0 : 8;

            if (currentAttackCooldown > 0) currentAttackCooldown -= Time.deltaTime;
            canAttack = currentAttackCooldown <= 0;
        }

        private void CheckModes()
        {
            defensiveCheck = Physics2D.OverlapCircle(transform.position, defensiveModeRadius, pLayer);
            passiveCheck = Physics2D.OverlapCircle(transform.position, passiveModeRadius, pLayer);

            if (defensiveCheck) passiveCheck = false;
            else if (passiveCheck) defensiveCheck = false;

            if (defensiveCheck) ec.backupSpareTimer = spareTimer;
            
            if (!canSpare) return;
            
            if (!defensiveCheck && !passiveCheck) currentSpareTimer -= Time.deltaTime;
            else currentSpareTimer = spareTimer;
            if (currentSpareTimer <= 0) ec.LostPlayer();
        }

        private void ActiveLogic()
        {
            if (!canAttack || isAttacking || eh.isStunned) return;

            if (!defensiveCheck)
            {
                em.FreezeX();
                animator.Play(!ef.isFlying
                    ? activeMoves[Random.Range(0, activeMoves.Length)]
                    : flyingActiveMoves[Random.Range(0, flyingActiveMoves.Length)]);
            }
            else
            {
                em.FreezeX();
                animator.Play(!ef.isFlying
                    ? evasiveMoves[Random.Range(0, evasiveMoves.Length)]
                    : flyingEvasiveMoves[Random.Range(0, flyingEvasiveMoves.Length)]);
            }
        }

        private void PassiveLogic()
        {
            if (!canAttack || isAttacking || eh.isStunned) return;
            em.FreezeX();
            if (!passiveCheck && !defensiveCheck)
                animator.Play(!ef.isFlying
                    ? activeMoves[Random.Range(0, activeMoves.Length)]
                    : flyingActiveMoves[Random.Range(0, flyingActiveMoves.Length)]);
            else if (defensiveCheck && !passiveCheck)
                animator.Play(!ef.isFlying
                    ? evasiveMoves[Random.Range(0, evasiveMoves.Length)]
                    : flyingEvasiveMoves[Random.Range(0, flyingEvasiveMoves.Length)]);
            else
                animator.Play(!ef.isFlying
                    ? passiveMoves[Random.Range(0, passiveMoves.Length)]
                    : flyingPassiveMoves[Random.Range(0, flyingPassiveMoves.Length)]);

        }

        private void EvasiveLogic()
        {
            if (!canAttack || isAttacking || eh.isStunned) return;
            em.FreezeX();
            if (defensiveCheck)
                animator.Play(!ef.isFlying
                    ? evasiveMoves[Random.Range(0, evasiveMoves.Length)]
                    : flyingEvasiveMoves[Random.Range(0, flyingEvasiveMoves.Length)]);
            else if (passiveCheck)
                animator.Play(!ef.isFlying
                    ? passiveMoves[Random.Range(0, passiveMoves.Length)]
                    : flyingPassiveMoves[Random.Range(0, flyingPassiveMoves.Length)]);
            else
                animator.Play(!ef.isFlying
                    ? activeMoves[Random.Range(0, activeMoves.Length)]
                    : flyingActiveMoves[Random.Range(0, flyingActiveMoves.Length)]);
        }


        private void Attack(AudioClip sfx = null)
        {
            hit = Physics2D.CircleCast(attackTransform.position, attackRange,
                new Vector2(transform.right.x, transform.up.y), 0f, LayerMask.GetMask("Player"));
            if (hit.collider == null) return;
            var damageable = hit.collider.gameObject.GetComponent<IDamageable>();
            if (damageable == null) return;
            var dm = Shenanigans.Instance.RawDamage(damageAmount, damageFactor, (IDamageable.ElementType)elementType, 1,
                isTrue, damageOverTime, procDmg, procInterval);
            GameEvents.TriggerApplyDamage(damageable, dm, ec.isFacingRight, "Enemy");
            damageable.Shock(stunTime, knockbackTransform != null && positionKnockback ? knockbackTransform.position : knockbackDirection * transform.right, knockbackForce, knockbackTime, positionKnockback);
            GameEvents.TriggerHitImpact(gameObject, hit.collider.gameObject, dm);
            PlaySound(sfx);
        }

        private void PlaySound(AudioClip sfx) => GameEvents.TriggerPlaySound(sfx ?? combatSfx[Random.Range(0, combatSfx.Length)], gameObject);
        
        private void FollowupAttack(string attack)
        {
            var rng = Random.Range(0, 101);
            if (rng <= 70) animator.Play(attack);
            else ActivateCooldown(0.5f);
        }

        private void ActivateCooldown(float atkCd = 1)
        {
            currentAttackCooldown = atkCd;
            if (ef.isFlying) ef.currentFlySpeed = Math.Abs(rb.linearVelocity.x) / 2;
            else
            {
                ez.enabled = true;
                ez.currentZonePeriod = currentAttackCooldown;
                ec.ChangeState(EnemyController.EnemyState.Zoning);
            }
        }
        private void FlyTrigger()
        {
            movesBeforeFlyingCount++;
            if (movesBeforeFlyingCount == movesBeforeFlying[0] && !ef.isFlying && canFly)
            {
                animator.Play(flyTrigger);
                ef.isFlying = true;
            }

            if (movesBeforeFlyingCount == movesBeforeFlying[1] && canFly)
            {
                ef.isFlying = false;
                movesBeforeFlyingCount = 0;
            }
        }

        public void ApplyStyle(float factor)
        {
            switch (factor)
            {
                case 1:
                    styleMode = StyleMode.Active;
                    break;
                case 2:
                    styleMode = StyleMode.Passive;
                    break;
                case 3:
                    styleMode = StyleMode.Evasive;
                    GetComponent<EnemyHealth>().stunMultiplier = 0.5f;
                    break;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, passiveModeRadius);
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, defensiveModeRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(attackTransform.position, attackRange);
        }
    }
}
