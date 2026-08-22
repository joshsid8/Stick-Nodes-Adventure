using System.Collections;
using scripies.Core;
using UnityEngine;
//using UnityEngine.InputSystem;
namespace scripies.Player
{
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Ground Attack")] public float damageAmount = 20f;
        [SerializeField] private float attackRange = 1.5f;
        [HideInInspector] public bool isAttacking;
        public float attackCooldown;
        private float currentAttackCooldown;
        public float attackEnergy;
        [SerializeField] private Transform attackTransform;

        [Space] [Header("Air Attack")] public float airAttackCooldown;
        [SerializeField] private float airAttackRange = 1f;
        public float airAttackForce;
        [HideInInspector] public bool isAirAttacking;
        public bool isAirComboing;
        [SerializeField] private bool hasAirBoosted;
        public int airComboAmount = 3;
        [SerializeField] private Transform airAttackTransform;

        [Space] [Header("Attack Properties")] public bool m1Ability;
        public bool canAttack;
        [SerializeField] private int[] maxComboInterval = new int[2];
        [SerializeField] private int[] comboInterval = new int[2];
        [SerializeField] private LayerMask enemyLayer;

        [Space] [Header("Shock and Potency")] public float stunTime = 0.2f;
        public Vector2[] knockbackDirections = new Vector2[2];
        public float[] knockbackForces = new float[2];
        public float[] knockbackTimes = new float[2];

        [Space] public float attackPotency = 1f;
        public float attackDamageMult = 1f;
        public float attackCooldownMult = 1f;

        [Space] public int damageFactor;
        public int elementType;
        public float elementFactor = 1;
        public bool isTrue;
        public bool damageOverTime;
        public float procDmg;
        public float procInterval;
        
        [SerializeField] private AudioClip[] hitSfx = new AudioClip[2];
        [SerializeField] private AudioClip[] whooshSfx = new AudioClip[2];
        
        // Components
        private RaycastHit2D[] hits;
        private Animator animator;
        private PlayerController pc;
        private PlayerAbility pa;
        //public PlayerInput playerInput;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        private void Start()
        {
            pc = GetComponent<PlayerController>();
            pa = GetComponent<PlayerAbility>();
            animator = GetComponent<Animator>();
            canAttack = true;
            //playerInput = new PlayerInput();
        }

        // Update is called once per frame
        private void Update()
        {
            animator.SetBool("isAttacking", isAttacking);
            animator.SetBool("isAirAttacking", isAirAttacking);
            animator.SetBool("isAirComboing", isAirComboing);
            animator.SetInteger("airComboAmount", airComboAmount);
            animator.SetInteger("groundComboInterval", comboInterval[0]);
            animator.SetInteger("airComboInterval", comboInterval[1]);
            if (pc.isGrounded)
            {
                hasAirBoosted = false;
                airComboAmount = 3;
            }

            if (currentAttackCooldown > 0) canAttack = false;
            Attack();

            if (comboInterval[0] > maxComboInterval[0]) comboInterval[0] = 0;
            if (comboInterval[1] > maxComboInterval[1]) comboInterval[1] = 0;
        }

        private void GroundAttack()
        {
            hits = Physics2D.CircleCastAll(attackTransform.position, attackRange,
                new Vector2(transform.right.x, transform.up.y), 0f, enemyLayer);

            foreach (var hit in hits)
            {
                var eDamageable = hit.collider.gameObject.GetComponent<IDamageable>();

                if (eDamageable == null) return;
                eDamageable.Damage(damageAmount * attackDamageMult, pc.isFacingRight, damageFactor,
                    (IDamageable.ElementType)elementType, elementFactor, isTrue, damageOverTime, procDmg, procInterval, "Player");
                eDamageable.Shock(stunTime * attackPotency, knockbackDirections[0] * transform.right,
                    knockbackForces[0], knockbackTimes[0] * attackPotency);
                
                GetComponent<PlayerAbility>().energy += attackEnergy;
                GameEvents.TriggerPlaySound(hitSfx[Random.Range(0, hitSfx.Length)], hit.collider.gameObject);
            }

            comboInterval[0]++;
            currentAttackCooldown = attackCooldown * attackCooldownMult;
        }

        private void AirAttack()
        {
            if (pc.rb.linearVelocity.y < 0 && !hasAirBoosted && !pc.isGrounded)
                pc.rb.linearVelocity = new Vector2(pc.rb.linearVelocity.x, airAttackForce);

            hits = Physics2D.CircleCastAll(airAttackTransform.position, airAttackRange,
                new Vector2(transform.right.x, transform.up.y), 0f, enemyLayer);

            foreach (var hit in hits)
            {
                var eDamageable = hit.collider.gameObject.GetComponent<IDamageable>();

                if (eDamageable == null) return;
                eDamageable.Damage(damageAmount * attackDamageMult, pc.isFacingRight, damageFactor,
                    (IDamageable.ElementType)elementType, elementFactor, isTrue, damageOverTime, procDmg, procInterval, "Player"); 
                eDamageable.Shock(stunTime * attackPotency, new Vector2(knockbackDirections[1].x * attackPotency,
                        knockbackDirections[1].y * attackPotency) * transform.right, knockbackForces[1], knockbackTimes[1] * attackPotency);
                
                GetComponent<PlayerAbility>().energy += attackEnergy;
                airComboAmount--;
                GameEvents.TriggerPlaySound(hitSfx[Random.Range(0, hitSfx.Length)], hit.collider.gameObject);
            }

            hasAirBoosted = true;
            comboInterval[1]++;
            currentAttackCooldown = airAttackCooldown * attackCooldownMult;
        }

        private IEnumerator AirCombo(GameObject target)
        {
            while (airComboAmount > 0 && !pc.isGrounded)
            {
                target.transform.position = new Vector3(airAttackTransform.transform.position.x + 1f,
                    airAttackTransform.transform.position.y, 0);
                isAirComboing = true;
                yield return new WaitForFixedUpdate();
            }

            isAirComboing = false;
        }

        private void Attack()
        {
            if (Input.GetMouseButtonDown(0) && currentAttackCooldown <= 0 && m1Ability && !pc.isPerformingAction && canAttack)
            {
                animator.SetTrigger("Attack");
                GameEvents.TriggerPlaySound(whooshSfx[Random.Range(0, whooshSfx.Length)], true, gameObject);
            }
            currentAttackCooldown -= Time.deltaTime * 0.5f;
            if (currentAttackCooldown <= 0) canAttack = true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(attackTransform.position, attackRange);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(airAttackTransform.position, airAttackRange);
        }
    }
}