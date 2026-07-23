using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using scripies.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, IDamageable, IPlayerStatus
{
    public static PlayerHealth Instance {get; private set;}
    
    [Header("Health & Damage Resistance")]
    public Image[] healthBars;
    public TMP_Text healthText;
    public float playerMaxHealth = 100;
    public float playerHealth = 100;
    public float playerExtraHealth;
    public float rawDamageResistance = 1f;
    public float regenerationMultiplier;
    [Tooltip("0 = normal, 1 = fire, 2 = earth, 3 = water, 4 = wind")]
    public List<float> defElementFactors = new (5) {1,1,1,1,1};
    
    [Header("Death!")]
    public bool isDead;
    private bool deathToggle;
    public float respawnTime;
    
    [Header("Shock")]
    [SerializeField] private bool getStunned;
    [HideInInspector] public bool isStunned;
    public float stunTime;
    private float currentStunTime;
    public float originalShockMultiplier;
    public float shockMultiplier = 1;
    
    [Header("Tinting")]
    public Color damageTint = Color.red;
    public float tintSpeed = 5f;
    private Color originalColor;
    [SerializeField] private bool isTinting;
    private SpriteRenderer spriteRenderer;
    [SerializeField] private bool spawnOnAwake = true;
    
    [Header("Combat & Jumping")]
    public bool inCombat;
    public float combatTimer;
    public float currentCombatTimer;
    public List<GameObject> aggressiveEnemies = new();
    public int aggressiveEnemyID;
    public float jumpRotationTimer;
    
    // Components
    private Knockback kb;
    [HideInInspector] public PlayerController pc;
    [HideInInspector] public PlayerDefense pd;
    [HideInInspector] public PlayerAbility pa;
    [HideInInspector] public PlayerCombat pf;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (spawnOnAwake) StartCoroutine(Spawn(0, true));
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
        //healthText.text = playerHealth == playerMaxHealth ? playerHealth.ToString("F0") +  " / " + playerMaxHealth.ToString("F0")
        //    : playerHealth.ToString("F1") +  " / " + playerMaxHealth.ToString("F0");
        if (playerHealth < 0) playerHealth = 0;
        if (playerHealth > playerMaxHealth) playerExtraHealth = playerHealth - playerMaxHealth;
        if (isDead) 
        {
            pc.canAct = false; 
            gameObject.layer = LayerMask.NameToLayer("Dead");
            spriteRenderer.color = Color.white;
        }
        
        // Interface refs D:
        IsDead = isDead;
        IsStunned = isStunned;
        Position = transform.position;
        CurrentHealth = playerHealth;
        MaxHealth = playerMaxHealth;
    }

    private void Animation()
    {
        pc.animator.SetBool("isStunned", isStunned);
        pc.animator.SetBool("isDead", isDead);
    }

    public IEnumerator Spawn(float timer, bool realSpawn)
    {
        yield return new WaitForSeconds(timer);
        transform.position = realSpawn ? pc.currentSpawnPoint.transform.position : pc.currentCheckPoint.transform.position;
    }

    private IEnumerator Death()
    {
        if (playerHealth <= 0 && !deathToggle)
        {
            deathToggle = true;
            isDead = true;
            AudioManager.Instance.sfxSource.Play();
            GameEvents.TriggerPlayerDeath();
            StartCoroutine(Shenanigans.Instance.JumpScare(GameObject.Find("JumpScare")));
            yield return new WaitForSeconds(respawnTime);
            StartCoroutine(Spawn(0, true));
            playerHealth = playerMaxHealth * 0.25f;
            deathToggle = false;
            isDead = false;
            gameObject.layer = LayerMask.NameToLayer("Player");
            pc.canAct = true;
            currentCombatTimer = 0;
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
                StartCoroutine(ApplyDamage(calcDamage * pd.blockDamageResistance, false, damageOverTime, procDmg, procInterval, isTrue));
                break;
            case true when pd.isBlocking && pd.counterWindow && damageFactor < 2:
                // perfect blocking / counter
                pc.animator.SetTrigger("Counter");
                StartCoroutine(AreaManager.Instance.StopTime(0.5f));
                pd.counterWindow = false;
                StartCoroutine(ApplyDamage(calcDamage * pd.counterDamageResistance, false, damageOverTime, procDmg, procInterval, isTrue));
                break;
            case true:
            case false:
                // the player blocking but getting hit at the back OR the player not blocking at all
                StartCoroutine(ApplyDamage(calcDamage * rawDamageResistance, true, damageOverTime, procDmg, procInterval, isTrue));
                break;
        }
        
        pc.currentDashTime = 0f;
        pa.isPerformingAbility = false;
    }

    private IEnumerator ApplyDamage(float damageAmount, bool blockBreak = false, bool dOt = false, float procDmg = 0f, float procInterval = 0.2f, bool isTrue = false)
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
    
    public void Shock(float stunDuration = 0f, Vector2 knockbackDirection = default, float knockbackForce = 20f, float knockbackTime = 0.2f)
    {
        stunTime = stunDuration * shockMultiplier;
        if (stunTime > 0 && !pd.isBlocking) getStunned = true;
        kb.CallKnockback(knockbackDirection, knockbackForce * shockMultiplier / 2, knockbackTime * shockMultiplier);
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

    public void ApplyCombat(float combatDuration = 5, bool additive = false) => currentCombatTimer = additive ? currentCombatTimer + combatDuration : combatDuration;
    
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
            var agg = aggressiveEnemies[aggressiveEnemyID];
            foreach (var enemy in aggressiveEnemies)
            {
                if (enemy != agg) continue;
                enemy.GetComponent<EnemyJump>().myTurn = false;
                enemy.GetComponent<EnemyController>().ChangeState(EnemyController.EnemyState.Zoning);
                enemy.GetComponent<EnemyZone>().currentZonePeriod = jumpRotationTimer;
            }
            
            agg.GetComponent<EnemyJump>().myTurn = true;
            agg.GetComponent<EnemyController>().ChangeState(EnemyController.EnemyState.Attacking);
            agg.GetComponent<EnemyAttack>().styleMode = agg.GetComponent<EnemyJump>().prevState;
            jumpRotationTimer = agg.GetComponent<EnemyJump>().turnTime;
            aggressiveEnemyID++;
            if (aggressiveEnemyID >= aggressiveEnemies.Count) aggressiveEnemyID = 0;
        }
    }

    public float CurrentHealth { get; private set;}
    public float MaxHealth { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsStunned { get; private set;}
    public Vector3 Position { get; private set; }
    
    

    public void Heal(float amount)
    {
        if (playerHealth < playerMaxHealth) playerHealth += amount;
        else playerExtraHealth += amount;
    }
}
