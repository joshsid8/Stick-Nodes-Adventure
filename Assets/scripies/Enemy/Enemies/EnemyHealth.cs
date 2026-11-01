using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using UnityEngine;

namespace scripies.Enemy
{
    public class EnemyHealth : MonoBehaviour, IDamageable, IEnemyStatus
    {
        public static event Action<Enemy> OnEnemyKilled;

        [Header("Health")] public float enemyMaxHealth = 100f;
        public float enemyHealth = 100f;
        public bool isDead;

        [Tooltip("0 = normal, 1 = fire, 2 = earth, 3 = water, 4 = wind")]
        public List<float> defElementFactors = new(5) { 1, 1, 1, 1, 1 };

        [Header("Shock")] private bool getStunned;
        public bool isStunned;
        private float stunTime;
        [SerializeField] private float currentStunTime;
        public float stunMultiplier = 1;
        private Knockback kb;
        public bool movable = true;

        [Header("Defense status")] public bool isCountering;
        public bool isBlocking;

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
        public bool isOneTime;
        [SerializeField] private AudioClip deathClip;

        [Header("Getters & Setters")] 
        public float CurrentHealth => enemyHealth;
        public float MaxHealth => enemyMaxHealth;
        public bool IsDead => isDead;
        public bool IsStunned => isStunned;
        public Vector3 Position => transform.position;
        public bool IsGrounded => ep.isGrounded;
        public bool IsFacingRight => ec.isFacingRight;
        public bool IsMovable => movable;

        // Start is called before the first execution of Update after the MonoBehaviour is created
        private void Start()
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
            if (isDead) yield break;
            isDead = true;
            try
            {
                if (ec != null && ec.id != null)
                    OnEnemyKilled?.Invoke(ec.id.entity as Enemy);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnemyHealth] OnEnemyKilled subscriber threw an exception: {ex.Message}");
            }
            GameEvents.TriggerDropLoot(gameObject);
            GameEvents.TriggerEnemyDefeated(gameObject);
            if (ec != null) ec.ChangeState(EnemyController.EnemyState.Dead);
            if (TryGetComponent<Rigidbody2D>(out var rb)) rb.gravityScale = 8;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
                spriteRenderer.enabled = true;
            }
            gameObject.layer = LayerMask.NameToLayer("EDead");
            gameObject.tag = "Dead";
            GameEvents.TriggerPlaySound(deathClip, gameObject, randomPitch: false, volume: 0.5f);
            yield return new WaitForSeconds(2f);
            for (var i = 0; i < 25; i++)
            {
                if (spriteRenderer != null) spriteRenderer.enabled = false;
                yield return new WaitForSeconds(0.025f);
                if (spriteRenderer != null) spriteRenderer.enabled = true;
                yield return new WaitForSeconds(0.025f);
            }

            GameEvents.TriggerAddPlayerCombat(0, false, "EnemyFightEnd");
            Destroy(gameObject);
        }

        public void Damage(Damage dm, bool facingRight, string aggressor = "")
        {
            var calcDamage = dm.damageAmount * dm.elementFactor;
            if (dm.elementType != IDamageable.ElementType.Normal)
            {
                for (var i = 0; i < defElementFactors.Count; i++)
                {
                    if ((int)dm.elementType == i) continue;
                    calcDamage *= defElementFactors[i];
                }
            }

            var isCrit = dm.damageFactor >= 1 || dm.isTrue;
            if (isBlocking && !dm.isTrue && dm.damageFactor < 1)
            {
                var blockedDmg = calcDamage * blockResistance;
                enemyHealth -= blockedDmg;
                GameEvents.TriggerDamageText(transform.position + Vector3.up * 0.7f, blockedDmg, Color.gray, false);
            }
            else if (isCountering && !dm.isTrue && dm.damageFactor < 2) animator.SetTrigger("Counter");
            else
            {
                enemyHealth -= calcDamage;
                if (ea.stunnable) ea.currentAttackCooldown = 0.1f;
                var textColor = isCrit ? new Color(1f, 0.85f, 0.2f) : Color.white;
                GameEvents.TriggerDamageText(transform.position + Vector3.up * 0.7f, calcDamage, textColor, isCrit);
            }
            
            CheckStatus();
            
            if (aggressor.Contains("Player") && !isDead)
            {
                if (!ea.isAttacking) StartCoroutine(ep.FoundPlayer());
                GameEvents.TriggerAddPlayerCombat(30, false);
            }
            //else print($"aggressor doesnt contain player: {aggressor}");

            Flash();
            if (dm.damageOverTime) StartCoroutine(DamageOverTime(dm.procDmg, dm.procInterval));
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
                GameEvents.TriggerDamageText(transform.position + Vector3.up * 0.5f, procAmount, new Color(0.85f, 0.45f, 1f), false);
                CheckStatus();
                yield return new WaitForSeconds(procInterval);
            }
        }

        public void Shock(float stunDuration = 0.5F, Vector2 knockbackDirection = default, float knockbackForce = 20f,
            float knockbackTime = 0.2f, bool targetPosition = false)
        {
            stunTime = stunDuration * stunMultiplier;
            if (stunTime > 0) getStunned = true;
            if (movable)
                kb.ApplyKnockback(knockbackDirection * (isBlocking ? blockResistance : 1f), knockbackForce, knockbackTime, targetPosition);
        }

        private void CheckStatus()
        {
            if (!Application.isPlaying) return;
            if (enemyHealth > 0) return;
            enemyHealth = 0;
            StartCoroutine(Death());
        }
        
        public void Heal(float amount) => enemyHealth += amount;

        private void Flash()
        {
            spriteRenderer.color = damageTint;
            isTinting = true;
        }

        private void Tint()
        {
            if (!isTinting) return;
            spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
            if (spriteRenderer.color == originalColor) isTinting = false;
        }
    }
}
