using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using UnityEngine;
using UnityEngine.UI;

namespace scripies.Player
{
    public class PlayerHealth : MonoBehaviour, IDamageable, IPlayerStatus
    {
        public static PlayerHealth Instance { get; private set; }

        [Header("Health & Damage Resistance")] public Image[] healthBars;
        public float playerMaxHealth = 100;
        public float playerHealth = 100;
        public float playerExtraHealth;
        public float rawDamageResistance = 1f;
        public float regenerationMultiplier;

        [Tooltip("0 = normal, 1 = fire, 2 = earth, 3 = water, 4 = wind")]
        public List<float> defElementFactors = new(5) { 1, 1, 1, 1, 1 };

        [Header("Death!")] public bool isDead;
        private bool deathToggle;
        public float respawnTime;
        public bool justSpawned;

        [Header("Shock")] [SerializeField] private bool getStunned;
        [HideInInspector] public bool isStunned;
        public float stunTime;
        private float currentStunTime;
        public float originalShockMultiplier;
        public float shockMultiplier = 1;

        [Header("Tinting")] public Color damageTint = Color.red;
        public float tintSpeed = 5f;
        private Color originalColor;
        [SerializeField] private bool isTinting;
        private SpriteRenderer spriteRenderer;
        [SerializeField] private bool spawnOnAwake = true;

        [Header("Combat & Jumping")] public bool inCombat;
        public float combatTimer;
        public float currentCombatTimer;
        private List<ITarget> aggressiveEnemies = new();
        public int aggressiveEnemyID;
        public float jumpRotationTimer;

        // Components
        private Knockback kb;
        [HideInInspector] public PlayerController pc;
        [HideInInspector] public PlayerDefense pd;
        [HideInInspector] public PlayerAbility pa;
        [HideInInspector] public PlayerCombat pf;

        #region Getters and Setters
        [Header("Getters and Setters")] 
        public float CurrentHealth => playerHealth;
        public float MaxHealth => playerMaxHealth;
        public bool JustSpawned => justSpawned;
        public bool IsDead => isDead;
        public bool IsStunned => isStunned;
        public Vector3 Position => transform.position;
        public bool IsGrounded => pc != null && pc.isGrounded;
        public bool IsFacingRight => pc != null && pc.isFacingRight;
        public bool IsCountering => pd != null && pd.counterWindow;
        public bool IsPerformingAction => pc != null && pc.isPerformingAction;
        public bool IsHoldingAbility => pa != null && pa.isHolding;
        public bool IsPerformingAbility => pa != null && pa.isPerformingAbility;
        public float CurrentEnergy => pa != null ? pa.energy : 0f;
        public float[] CurrentAbilityCooldown => pa.currentAbilityCooldowns;
        public float[] MaxAbilityCooldown => pa.maxAbilityCooldowns;
        public int AggressiveEnemies => aggressiveEnemies.Count;
        public float NodeBucks => GetComponent<PlayerCurrency>().nodeBucks;
        #endregion

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            pc = GetComponent<PlayerController>();
            pd = GetComponent<PlayerDefense>();
            kb = GetComponent<Knockback>();
            pa = GetComponent<PlayerAbility>();
            pf = GetComponent<PlayerCombat>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        private void Start()
        {
            currentStunTime = stunTime * shockMultiplier;
            originalColor = spriteRenderer.color;
            originalShockMultiplier = shockMultiplier;
            
            #if UNITY_EDITOR 
            spawnOnAwake = false;
            #endif
            if (spawnOnAwake) StartCoroutine(Spawn(1, true));
            
            GameEvents.OnPlayerDeath += ResetCombat;
            GameEvents.OnEnemyDefeated += ResetCombatViaEnemy;
            GameEvents.OnAggressiveStatusChanged += UpdateAggressiveEnemies;
            GameEvents.AddPlayerCombat += ApplyCombat;
        }

        private void UpdateAggressiveEnemies(ITarget target, bool isAggressive)
        {
            if (isAggressive)
            {
                if (!aggressiveEnemies.Contains(target)) aggressiveEnemies.Add(target);
            }
            else
            {
                if (aggressiveEnemies.Contains(target)) aggressiveEnemies.Remove(target);
            }
        }

        // Update is called once per frame
        private void Update()
        {
            Animation();
            StartCoroutine(Death());
            Stunned();
            Tint();
            Regeneration();
            JumpRotate();
            healthBars[0].fillAmount = playerHealth / playerMaxHealth;
            healthBars[1].fillAmount = playerExtraHealth / playerMaxHealth;
            if (playerHealth < 0) playerHealth = 0;
            if (playerHealth > playerMaxHealth) playerExtraHealth = playerHealth - playerMaxHealth;
            if (isDead)
            {
                pc.canAct = false;
                gameObject.layer = LayerMask.NameToLayer("Dead");
                spriteRenderer.color = Color.white;
            }
        }

        private void Animation()
        {
            pc.animator.SetBool("isStunned", isStunned);
            pc.animator.SetBool("isDead", isDead);
        }

        public IEnumerator Spawn(float timer = 0, bool realSpawn = false)
        {
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            yield return new WaitForSeconds(timer);
            gameObject.layer = LayerMask.NameToLayer("Player");
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;

            var spawnTarget = realSpawn ? pc.currentSpawnPoint : pc.currentCheckPoint;
            if (spawnTarget != null) transform.position = spawnTarget.transform.position;
            else Debug.LogWarning("[PlayerHealth] We cant find the right spawn object!");

            if (Shenanigans.Instance != null) Shenanigans.Instance.ToggleFreezeCamera(false);
            if (realSpawn) GameEvents.TriggerPlayerSpawn(pc.currentCheckPoint);
            StartCoroutine(SpawnPeriod());
        }

        private IEnumerator SpawnPeriod()
        {
            justSpawned = true;
            yield return new WaitForSeconds(1f);
            justSpawned = false;
        }

        private IEnumerator Death()
        {
            if (playerHealth <= 0 && !deathToggle)
            {
                deathToggle = true;
                isDead = true;
                GameEvents.TriggerPlayerDeath();
                GameEvents.TriggerJumpScare();
                GameEvents.TriggerSound();
                Shenanigans.Instance.ToggleFreezeCamera();
                yield return new WaitForSeconds(respawnTime);
                StartCoroutine(Spawn(0, true));
                playerHealth = playerMaxHealth * 0.25f;
                deathToggle = false;
                isDead = false;
                pc.canAct = true;
                ResetCombat();
            }
        }

        public void Damage(float damageAmount, bool facingRight, int damageFactor = 0,
            IDamageable.ElementType elementType = 0, float elementFactor = 1f, bool isTrue = false,
            bool damageOverTime = false, float procDmg = 0f, float procInterval = 0f)
        {
            var calcDamage = damageAmount * elementFactor;
            if (elementType != 0)
            {
                for (var i = 0; i < defElementFactors.Count; i++)
                {
                    if ((int)elementType == i) continue;
                    calcDamage *= defElementFactors[i];
                }
            }

            var isFacingAttacker = facingRight && !pc.isFacingRight || !facingRight && pc.isFacingRight;
            switch (isFacingAttacker)
            {
                case true when pd.isBlocking && !pd.counterWindow && damageFactor < 1:
                    // blocking successfully
                    StartCoroutine(ApplyDamage(calcDamage * pd.blockDamageResistance, false, damageOverTime, procDmg * pd.blockDamageResistance,
                        procInterval, isTrue));
                    break;
                case true when pd.isBlocking && pd.counterWindow && damageFactor < 2:
                    // perfect blocking / counter
                    pc.animator.SetTrigger("Counter");
                    pd.counterWindow = false;
                    GameEvents.TriggerStopTime(0.5f);
                    StartCoroutine(ApplyDamage(calcDamage * pd.counterDamageResistance, false, damageOverTime, procDmg * pd.counterDamageResistance,
                        procInterval, isTrue));
                    break;
                case true:
                case false:
                    // the player blocking but getting hit at the back OR the player not blocking at all
                    StartCoroutine(ApplyDamage(calcDamage * rawDamageResistance, true, damageOverTime, procDmg * rawDamageResistance,
                        procInterval, isTrue));
                    break;
            }
            pc.currentDashTime = 0f;
            pa.isPerformingAbility = false;
        }

        private IEnumerator ApplyDamage(float damageAmount, bool blockBreak = false, bool dOt = false,
            float procDmg = 0f, float procInterval = 0.2f, bool isTrue = false)
        {
            if (playerExtraHealth > 0 && !isTrue) playerExtraHealth -= damageAmount;
            else playerHealth -= damageAmount;
            GameEvents.TriggerPlayerHealthChanged(damageAmount);
            if (blockBreak || isTrue)
            {
                spriteRenderer.color = damageTint;
                pd.StopBlocking();
                isTinting = true;
            }

            var procTaken = procDmg;
            var procAmount = procDmg * procInterval;
            while (procTaken > 0 && dOt)
            {
                if (playerExtraHealth > 0 && !isTrue) playerExtraHealth -= procAmount;
                else playerHealth -= procAmount;
                procTaken -= procAmount;
                GameEvents.TriggerPlayerHealthChanged(procAmount);
                yield return new WaitForSeconds(procInterval);
            }
        }

        public void Shock(float stunDuration = 0f, Vector2 knockbackDirection = default, float knockbackForce = 20f,
            float knockbackTime = 0.2f)
        {
            var force = knockbackForce * shockMultiplier;
            if (pd.isBlocking) force = knockbackForce * pd.blockDamageResistance;
            else if (pd.counterWindow) force = knockbackForce * pd.counterDamageResistance;
            stunTime = stunDuration * shockMultiplier;
            if (stunTime > 0 && !pd.isBlocking) getStunned = true;
            kb.CallKnockback(knockbackDirection, force, knockbackTime * shockMultiplier);
            if (!pd.isBlocking)
            {
                pc.animator.SetTrigger("Damaged");
                pa.isPerformingAbility = false;
            }
        }

        private void Stunned()
        {
            if (getStunned)
            {
                currentStunTime = stunTime * shockMultiplier;
                getStunned = false;
                if (stunTime > 0) isStunned = true;
            }

            if (isStunned)
            {
                pc.canAct = false;
                currentStunTime -= Time.deltaTime;
                if (currentStunTime <= 0)
                {
                    pc.canAct = true;
                    pf.canAttack = true;
                    shockMultiplier = originalShockMultiplier;
                    isStunned = false;
                    pc.rb.linearVelocity = new Vector2(0, pc.rb.linearVelocity.y);
                }
            }
        }
        
        public void ApplyCombat(float combatDuration = 5, bool additive = false, string aggressor = null)
        {
            if (combatDuration <= 0)
            {
                ResetCombat();
                return;
            }
            inCombat = true;
            currentCombatTimer = additive ? currentCombatTimer + combatDuration : combatDuration;
            pc.combatIndicator.SetActive(true);
            if (GameObject.Find("AudioManager").GetComponent<IAudioStatus>().musicType != IAudioStatus.MusicType.Combat && aggressor != "Boss") 
                GameEvents.TriggerMusic(1, false);
            else if (aggressor == "Boss") 
                GameEvents.TriggerMusic(2, false);
            StartCoroutine(CombatTimer());
        }

        private IEnumerator CombatTimer()
        {
            while (currentCombatTimer > 0)
            {
                currentCombatTimer -= Time.deltaTime;
                yield return null;
            }
            ResetCombat();
        }
        
        private void ResetCombat() 
        {
            inCombat = false;
            currentCombatTimer = 0;
            pc.combatIndicator.SetActive(false);
            GameEvents.TriggerMusic(0, false);
        }
        private void ResetCombatViaEnemy(GameObject enemy) => ResetCombat();
        
        private void Tint()
        {
            if (isTinting)
            {
                spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
                if (spriteRenderer.color == originalColor) isTinting = false;
            }
        }

        private void Regeneration()
        {
            if (playerHealth > 0 && playerHealth < playerMaxHealth)
            {
                if (!inCombat) playerHealth += Time.deltaTime * 1.5f * regenerationMultiplier;
                else playerHealth += Time.deltaTime * regenerationMultiplier;
            }

            if (playerHealth > playerMaxHealth) playerHealth = playerMaxHealth;
        }

        private void JumpRotate()
        {
            if (jumpRotationTimer > 0) jumpRotationTimer -= Time.deltaTime;
            
            if (aggressiveEnemies.Count > 1 && jumpRotationTimer <= 0)
            {
                var aggTarget = aggressiveEnemies[aggressiveEnemyID];
                foreach (var target in aggressiveEnemies)
                {
                    if (target.Transform.TryGetComponent<IEnemyJump>(out var jump))
                    {
                        if (target != aggTarget)
                        {
                            jump.MyTurn = false;
                            jump.SetState("Zoning", jumpRotationTimer);
                        }
                        else
                        {
                            jump.MyTurn = true;
                            jump.SetState("Attacking");
                            jump.SetAttackStyleFromJump();
                            jumpRotationTimer = jump.TurnTime;
                        }
                    }
                }

                aggressiveEnemyID++;
                if (aggressiveEnemyID >= aggressiveEnemies.Count) aggressiveEnemyID = 0;
            }
        }

        public void Heal(float amount)
        {
            if (playerHealth < playerMaxHealth) playerHealth += amount;
            else playerExtraHealth += amount;
        }
    }
}