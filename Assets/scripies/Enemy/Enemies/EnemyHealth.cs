using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace scripies.Enemy
{
    public class EnemyHealth : MonoBehaviour, IDamageable, IEnemyStatus
    {
        public static event Action<Enemy> OnEnemyKilled;

        [Header("Health")] public float enemyMaxHealth = 100f;
        public float enemyHealth = 100f;
        public bool isDead;

        [Tooltip("0 = normal, 1 = fire, 2 = earth, 3 = water, 4 = wind")]
        public List<float> defElementFactors = new List<float>(5) { 1, 1, 1, 1, 1 };

        [Header("Shock")] private bool getStunned;
        public bool isStunned;
        private float stunTime;
        [SerializeField] private float currentStunTime;
        public float stunMultiplier = 1;
        private Knockback kb;
        public bool movable = true;

        [Header("Defense status")] public bool isCountering;
        public bool isBlocking;

        [FormerlySerializedAs("damageResistance")]
        public float blockResistance;

        [Header("Damage Tinting")] public Color damageTint = Color.red;
        public float tintSpeed = 5f;
        private Color originalColor;
        [SerializeField] private bool isTinting;
        private SpriteRenderer spriteRenderer;

        [Header("Phase 2")] public float phase2;
        public int phase2Style;

        [Header("References & Other")] private Animator animator;
        private EnemyController ec;
        private EnemyAttack ea;
        private EnemyPatrol ep;
        public bool isLight;
        public bool isOneTime;

        [Header("Getters & Setters")] public float CurrentHealth { get; private set; }
        public float MaxHealth { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsStunned { get; private set; }
        public Vector3 Position { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsFacingRight { get; private set; }

        // Start is called before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            animator = GetComponent<Animator>();
            currentStunTime = stunTime * stunMultiplier;
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalColor = spriteRenderer.color;
            kb = GetComponent<Knockback>();
            ea = GetComponent<EnemyAttack>();
            ep = GetComponent<EnemyPatrol>();
            ec = GetComponent<EnemyController>();
            GameEvents.TriggerEnemySpawn(gameObject);
        }

        // Update is called once per frame
        private void Update()
        {
            animator.SetBool("isStunned", isStunned);
            animator.SetBool("isDead", isDead);
            Stunned();
            Tint();
            if (enemyHealth <= phase2 && phase2Style > 0)
            {
                if (!ea.styleMode.Equals(phase2Style)) ea.ApplyStyle(phase2Style);
                else print($"Clueful: styleMode is {ea.styleMode} and phase2Style is {phase2Style}");
            }

            if (isDead && ep.isGrounded) ep.rb.linearVelocity = new Vector2(0, ep.rb.linearVelocity.y);

            // Interface refs D:
            IsDead = isDead;
            IsStunned = isStunned;
            Position = transform.position;
            CurrentHealth = enemyHealth;
            MaxHealth = enemyMaxHealth;
            IsGrounded = ep.isGrounded;
            IsFacingRight = ec.isFacingRight;
        }

        private void Stunned()
        {
            if (getStunned)
            {
                currentStunTime = stunTime;
                getStunned = false;
            }

            if (currentStunTime > 0) currentStunTime -= Time.deltaTime;
            isStunned = currentStunTime > 0;
        }

        private IEnumerator Death()
        {
            if (isDead)
            {
                OnEnemyKilled?.Invoke(ec.enemy);
                GetComponent<Loot>().DropLoot();
                ec.ChangeState(EnemyController.EnemyState.Dead);
                GetComponent<Rigidbody2D>().gravityScale = 8;
                spriteRenderer.color = Color.white;
                gameObject.layer = LayerMask.NameToLayer("EDead");
                gameObject.tag = "Dead";
                spriteRenderer.enabled = true;
                GameEvents.TriggerEnemyDefeated(gameObject);
                yield return new WaitForSeconds(3f);
                for (var i = 0; i < 25; i++)
                {
                    spriteRenderer.enabled = false;
                    yield return new WaitForSeconds(0.025f);
                    spriteRenderer.enabled = true;
                    yield return new WaitForSeconds(0.025f);
                }

                Destroy(gameObject);
            }
        }


        public void Damage(float damageAmount, bool facingRight = true, int damageFactor = 0,
            IDamageable.ElementType elementType = 0, float elementFactor = 1f, bool isTrue = false,
            bool damageOverTime = false, float procDmg = 0f, float procInterval = 0f)
        {
            var calcDamage = damageAmount * elementFactor;
            if (elementType != IDamageable.ElementType.Normal)
            {
                for (var i = 0; i < defElementFactors.Count; i++)
                {
                    if ((int)elementType == i) continue;
                    calcDamage *= defElementFactors[i];
                }
            }

            if (isBlocking && !isTrue && damageFactor < 1)
            {
                enemyHealth -= calcDamage * blockResistance;
            }
            else if (isCountering && !isTrue && damageFactor < 2) animator.SetTrigger("Counter");
            else
            {
                enemyHealth -= calcDamage;
                if (ea.stunnable) ea.currentAttackCooldown = 0.1f;
            }

            if (enemyHealth <= 0)
            {
                enemyHealth = 0;
                isDead = true;
                StartCoroutine(Death());
            }

            Flash();
            if (damageOverTime) StartCoroutine(DamageOverTime(procDmg, procInterval));
            if (GetComponent<EnemyPatrol>().enabled) ec.ChangeState(EnemyController.EnemyState.Attacking);
        }

        private IEnumerator DamageOverTime(float procDmg, float procInterval)
        {
            var procTaken = procDmg;
            var procAmount = procDmg * procInterval;
            while (procTaken > 0)
            {
                enemyHealth -= procAmount;
                Flash();
                procTaken -= procAmount;
                yield return new WaitForSeconds(procInterval);
            }
        }

        public void Shock(float stunDuration = 0.5F, Vector2 knockbackDirection = default, float knockbackForce = 20f,
            float knockbackTime = 0.2f)
        {
            stunTime = stunDuration * stunMultiplier;
            if (stunTime > 0) getStunned = true;
            if (movable)
                kb.CallKnockback(knockbackDirection * (isBlocking ? blockResistance : 1f), knockbackForce,
                    knockbackTime);
        }

        public void Heal(float amount) => enemyHealth += amount;

        private void Flash()
        {
            spriteRenderer.color = damageTint;
            isTinting = true;
        }

        private void Tint()
        {
            if (isTinting)
            {
                spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
                if (spriteRenderer.color == originalColor) isTinting = false;
            }
        }
    }
}