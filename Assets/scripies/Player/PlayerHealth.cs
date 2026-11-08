using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace scripies.Player
{
    public class PlayerHealth : HealthBase, IPlayerStatus, IDamageable
    {
        public static PlayerHealth Instance { get; private set; }

        public Image[] healthBars;
        public float playerExtraHealth;
        public float rawDamageResistance = 1f;
        public float regenerationMultiplier;

        [Tooltip("0 = normal, 1 = fire, 2 = earth, 3 = water, 4 = air")]
        public List<float> defElementFactors = new(5) { 1, 1, 1, 1, 1 };

        [Header("Death!")]
        public AudioClip deathClip;
        public float respawnTime;
        public bool justSpawned;

        [Header("Shock")] [SerializeField] private bool getStunned;
        public float originalShockMultiplier;
        public float shockMultiplier = 1;
        
        [Header("Combat & Jumping")] public bool endangered;
        public float combatTimer;
        public float currentCombatTimer;
        private readonly List<ITarget> aggressiveEnemies = new();
        public int aggressiveEnemyID;
        public float jumpRotationTimer;
        
        // Components
        [HideInInspector] public PlayerController pc;
        [HideInInspector] public PlayerDefense pd;
        [HideInInspector] public PlayerAbility pa;
        [HideInInspector] public PlayerCombat pf;
        [HideInInspector] public PlayerCurrency pm;

        #region Getters and Setters
        public bool JustSpawned => justSpawned;
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
        public float NodeBucks => pm.nodeBucks;
        public AbilityObjSO[] CurrentAbilities => pa.ability.objects; 
        #endregion

        protected override void Awake()
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
            pm = GetComponent<PlayerCurrency>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameEvents.OnPlayerDeath -= ResetCombat;
            GameEvents.OnEnemyDefeated -= ResetCombatViaEnemy;
            GameEvents.OnAggressiveStatusChanged -= UpdateAggressiveEnemies;
            GameEvents.AddPlayerCombat -= ApplyCombat;
            GameEvents.OnPlayerHealthChanged -= CheckStatus;
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        protected override void Start()
        {
            currentStunTime = stunTime * shockMultiplier;
            originalColor = spriteRenderer.color;
            originalShockMultiplier = shockMultiplier;
            
            GameEvents.OnPlayerDeath += ResetCombat;
            GameEvents.OnEnemyDefeated += ResetCombatViaEnemy;
            GameEvents.OnAggressiveStatusChanged += UpdateAggressiveEnemies;
            GameEvents.AddPlayerCombat += ApplyCombat;
            GameEvents.OnPlayerHealthChanged += CheckStatus;
        }

        // Update is called once per frame
        private void Update()
        {
            Animation();
            Stunned();
            Tint();
            Regeneration();
            JumpRotate();
            health = Mathf.Clamp(health, 0, maxHealth);
            if (isDead)
            {
                pc.canAct = false;
                gameObject.layer = LayerMask.NameToLayer("Dead");
                spriteRenderer.color = Color.white;
            }
            healthBars[0].fillAmount = health / maxHealth;
            healthBars[1].fillAmount = playerExtraHealth / maxHealth;
        }

        private void OnValidate()
        {
            if (!isDead && Application.isPlaying) CheckStatus();
        }

        private void Animation()
        {
            pc.animator.SetBool("isStunned", isStunned);
            pc.animator.SetBool("isDead", isDead);
        }

        [ContextMenu("Debug Spawn")] public void SpawnPlayer() => StartCoroutine(Spawn(0 , true));
        
        public IEnumerator Spawn(float timer = 0, bool realSpawn = false)
        {
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            yield return new WaitForSeconds(timer);
            gameObject.layer = LayerMask.NameToLayer("Player");
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;

            if (pc.currentSpawnPoint.gameObject.name == "Default SpawnPoint" && !SceneManager.GetSceneByName("Default Teste").isLoaded) 
                SceneManager.LoadSceneAsync("Default Teste", LoadSceneMode.Additive);
            
            var spawnTarget = realSpawn ? pc.currentSpawnPoint : pc.currentCheckPoint;
            if (spawnTarget != null) transform.position = spawnTarget.transform.position;
            else
            {
                spawnTarget = pc.currentSpawnPoint;
                if (spawnTarget != null) transform.position = spawnTarget.transform.position;
                else Debug.LogError("[PlayerHealth] We cant find the right spawn object!");
            }

            if (Shenanigans.Instance != null) Shenanigans.Instance.ToggleFreezeCamera(false);
            if (realSpawn)
            {
                GameEvents.TriggerPlayerSpawn();
                GameHUD.Instance.ActivateShader(0.5f, 1);
            }
            StartCoroutine(SpawnPeriod());
        }

        private IEnumerator SpawnPeriod()
        {
            justSpawned = true;
            yield return new WaitForSeconds(1f);
            justSpawned = false;
            GameHUD.Instance.SetShaderColour(Color.black);
        }
        [ContextMenu("End the Player's Life!")] public void Kill() => StartCoroutine(Death());
        protected override IEnumerator Death()
        {
            isDead = true;
            health = 0;
            GameEvents.TriggerPlayerDeath();
            var deathSound = (deathClip, true);
            if (pc.debugMode && Application.isEditor)
            {
                GameEvents.TriggerJumpScare();
                deathSound = (null, false);
            }
            GameEvents.TriggerMuteMusic();
            GameEvents.TriggerPlaySound(deathSound.deathClip, randomPitch: deathSound.Item2);
            //Shenanigans.Instance.ToggleFreezeCamera();
            GameHUD.Instance.ActivateShader(respawnTime / 2, -1f);
            
            yield return new WaitForSeconds(respawnTime / 2);
            //GameHUD.Instance.SetShaderColour(Color.white, 1.5f);
            yield return new WaitForSeconds(respawnTime / 2);
            
            StartCoroutine(Spawn(0, true));
            GameEvents.TriggerMuteMusic(false);
            health = maxHealth * 0.25f;
            isDead = false;
            pc.canAct = true;
            ResetCombat();
        }

        public override void Damage(Damage dm, bool isFacingRight, string aggressor = "")
        {
            var calcDamage = dm.damageAmount * dm.elementFactor;
            if (dm.elementType != 0)
            {
                // No! I am NNNNNNOTTT turning this into a LINQ expression!!!
                for (var i = 0; i < defElementFactors.Count; i++)
                {
                    if ((int)dm.elementType == i) continue;
                    calcDamage *= defElementFactors[i];
                }
            }

            var isFacingAttacker = isFacingRight && !pc.isFacingRight || !isFacingRight && pc.isFacingRight;
            switch (isFacingAttacker)
            {
                case true when pd.isBlocking && !pd.counterWindow && dm.damageFactor < 1:
                    // blocking successfully
                    StartCoroutine(ApplyDamage(calcDamage * pd.blockDamageResistance, false, dm.damageOverTime, dm.procDmg * pd.blockDamageResistance,
                        dm.procInterval, dm.isTrue));
                    break;
                case true when pd.isBlocking && pd.counterWindow && dm.damageFactor < 2:
                    // perfect blocking / counter
                    pc.animator.SetTrigger("Counter");
                    pd.counterWindow = false;
                    StartCoroutine(ApplyDamage(calcDamage * pd.counterDamageResistance, false, dm.damageOverTime, dm.procDmg * pd.counterDamageResistance,
                        dm.procInterval, dm.isTrue));
                    GameEvents.TriggerStopTime(0.5f);
                    break;
                case true:
                case false:
                    // the player blocking but getting hit at the back / the player not blocking at all
                    StartCoroutine(ApplyDamage(calcDamage * rawDamageResistance, true, dm.damageOverTime, dm.procDmg * rawDamageResistance,
                        dm.procInterval, dm.isTrue));
                    pc.animator.SetTrigger("Damaged");
                    pa.isPerformingAbility = false;
                    break;
            }
            
            if (aggressor.Contains("Enemy") || aggressor.Contains("Boss")) ApplyCombat(30, true, aggressor);
            else if (aggressor.Contains("End")) ResetCombat();

            pc.currentDashTime = 0f;
            pa.isPerformingAbility = false;
        }

        private IEnumerator ApplyDamage(float damageAmount, bool blockBreak = false, bool dOt = false,
            float procDmg = 0f, float procInterval = 0.2f, bool isTrue = false)
        {
            if (playerExtraHealth > 0 && !isTrue) playerExtraHealth -= damageAmount;
            else health -= damageAmount;
            GameEvents.TriggerPlayerHealthChanged(damageAmount);
            GameEvents.TriggerDamageText(transform.position + Vector3.up * 0.8f, damageAmount, new Color(1f, 0.35f, 0.35f), false);
            if (blockBreak)
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
                else health -= procAmount;
                procTaken -= procAmount;
                GameEvents.TriggerPlayerHealthChanged(procAmount);
                GameEvents.TriggerDamageText(transform.position + Vector3.up * 0.8f, procAmount, new Color(0.85f, 0.45f, 1f), false);
                yield return new WaitForSeconds(procInterval);
            }
            
            // We dont worry about this
            GameEvents.TriggerOnDamageDealt(damageAmount);
        }

        public override void Shock(float stunDuration = 0f, Vector2 knockbackDirection = default, float knockbackForce = 20f, float knockbackTime = 0.2f, bool targetPosition = false)
        {
            var force = knockbackForce * shockMultiplier;
            if (pd.isBlocking) force = knockbackForce * pd.blockDamageResistance;
            else if (pd.counterWindow) force = knockbackForce * pd.counterDamageResistance;
            stunTime = stunDuration * shockMultiplier;
            if (stunTime > 0 && !pd.isBlocking) getStunned = true;
            kb.ApplyKnockback(knockbackDirection, force, knockbackTime * shockMultiplier, targetPosition);
        }

        private void Stunned()
        {
            if (getStunned)
            {
                currentStunTime = stunTime * shockMultiplier;
                getStunned = false;
                if (stunTime > 0)
                {
                    isStunned = true;
                    GameEvents.TriggerPlayerPerformAbility(false);
                }
            }

            if (!isStunned) return;
            pc.canAct = false;
            currentStunTime -= Time.deltaTime;
            if (!(currentStunTime <= 0)) return;
            pc.canAct = true;
            pf.canAttack = true;
            shockMultiplier = originalShockMultiplier;
            isStunned = false;
            pc.rb.linearVelocity = new Vector2(0, pc.rb.linearVelocity.y);
        }
        
        public void ApplyCombat(float combatDuration = 5, bool additive = false, string aggressor = "")
        {
            if (combatDuration <= 0 || aggressor.Contains("End"))
            {
                ResetCombat();
                return;
            }
            currentCombatTimer = additive ? currentCombatTimer + combatDuration : combatDuration;
            pc.combatIndicator.SetActive(true);
            var am = GameObject.Find("AudioManager").GetComponent<IAudioStatus>();
            if (am.musicType == IAudioStatus.MusicType.Area && aggressor.Contains("Enemy")) 
                GameEvents.TriggerPlayMusic(1, false, aggressor.Contains("Immediate") || aggressor.Contains("Ambush"));
            else if (aggressor.Contains("Boss"))
                GameEvents.TriggerPlayMusic(2, false, true);
                
            if (!endangered) StartCoroutine(CombatTimer());
        }

        private IEnumerator CombatTimer()
        {
            while (currentCombatTimer > 0)
            {
                endangered = true;
                currentCombatTimer -= Time.deltaTime;
                yield return null;
            }
            endangered = false;
            ResetCombat();
        }
        
        private void ResetCombat()
        {
            if (!isActiveAndEnabled || pc == null) return;
            endangered = false;
            currentCombatTimer = 0;
            pc.combatIndicator.SetActive(false);
            GameEvents.TriggerPlayMusic(0, false);
        }
        private void ResetCombatViaEnemy(GameObject enemy)
        {
            var am = GameObject.Find("AudioManager")?.GetComponent<IAudioStatus>();
            if (aggressiveEnemies.Count > 1 || (am != null && am.musicType == IAudioStatus.MusicType.Boss)) return;
            ResetCombat();
        }

        private void Regeneration()
        {
            if (health > 0 && health < maxHealth)
            {
                if (!endangered) health += Time.deltaTime * 1.5f * regenerationMultiplier;
                else health += Time.deltaTime * regenerationMultiplier;
            }

            if (health > maxHealth) health = maxHealth;
        }
        
        private void UpdateAggressiveEnemies(ITarget target, bool isAggressive)
        {
            if (isAggressive)
            {
                if (!aggressiveEnemies.Contains(target)) aggressiveEnemies.Add(target);
            }
            else
            {
                if (aggressiveEnemies.Contains(target) || target == null) aggressiveEnemies.Remove(target);
            }
        }

        private void JumpRotate()
        {
            if (jumpRotationTimer > 0) jumpRotationTimer -= Time.deltaTime;

            if (aggressiveEnemies.Count <= 1 || !(jumpRotationTimer <= 0)) return;
            var aggTarget = aggressiveEnemies[aggressiveEnemyID];
            foreach (var target in aggressiveEnemies)
            {
                if (!target.Transform.TryGetComponent<IEnemyJump>(out var jump)) return;
                if (target != aggTarget)
                {
                    jump.MyTurn = false;
                    jump.SetState("Zoning", jumpRotationTimer);
                }
                else
                {
                    jump.MyTurn = true;
                    jump.SetState("Attacking");
                    jump.SetPrevStyle();
                    jumpRotationTimer = jump.TurnTime;
                }
            }

            aggressiveEnemyID++;
            if (aggressiveEnemyID >= aggressiveEnemies.Count) aggressiveEnemyID = 0;
        }

        public override void Heal(float amount)
        {
            if (health < maxHealth) health += amount;
            else playerExtraHealth += amount;
        }
    }
}