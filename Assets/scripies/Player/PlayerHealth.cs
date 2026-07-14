using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, PDamageable
{
    public static PlayerHealth Instance {get; private set;}
    
    public Image[] healthBars;
    public TMP_Text healthText;
    public float playerMaxHealth = 100;
    public float playerHealth = 100;
    public float playerExtraHealth;
    public float rawDamageResistance = 1f;
    public enum ElementFactor {Fire, Earth, Water, Air}

    public Dictionary<ElementFactor, float> elementFactors = new Dictionary<ElementFactor, float>();

    public float regenerationMultiplier;
    
    [HideInInspector] public PlayerController pc;
    private PlayerDefense pd;

    public bool isDead;
    private bool deathToggle;
    public float respawnTime;
    
    [SerializeField] private bool getStunned;
    [HideInInspector] public bool isStunned;
    public float stunTime;
    private float currentStunTime;
    public float originalShockMultiplier;
    public float shockMultiplier = 1;
    
    private Knockback kb;

    public Color damageTint = Color.red;
    public float tintSpeed = 5f;
    private Color originalColor;
    [SerializeField] private bool isTinting;
    private SpriteRenderer spriteRenderer;
    [SerializeField] private bool spawnOnAwake = true;
    
    public bool inCombat;
    public float combatTimer;
    public float currentCombatTimer;
    public List<GameObject> aggressiveEnemies = new List<GameObject>();
    public int aggressiveEnemyID;
    public float jumpRotationTimer;
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
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        //TODO: apply this by making this dictionary some form of multiplier. once damaged, the dmg taken will be multiplied by the attackers element factor. make enemies have this too.
        elementFactors.Add(ElementFactor.Fire, 1f);
        elementFactors.Add(ElementFactor.Earth, 1f);
        elementFactors.Add(ElementFactor.Water, 1f);
        elementFactors.Add(ElementFactor.Air, 1f);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentStunTime = stunTime * shockMultiplier;
        originalColor = spriteRenderer.color;
        originalShockMultiplier = shockMultiplier;
    }

    // Update is called once per frame
    void Update()
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

    public void Damage(float damageAmount, Vector2 knockbackForce, bool facingRight, int damageFactor, int elementFactor)
    {
        var isFacingAttacker = facingRight && !pc.isFacingRight || !facingRight && pc.isFacingRight;
        switch (isFacingAttacker)
        {
            case true when pd.isBlocking && !pd.counterWindow && damageFactor < 1:
                // blocking successfully
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * pd.blockDamageResistance;
                else playerHealth -= damageAmount * pd.blockDamageResistance;
                kb.CallKnockback(knockbackForce * pd.blockDamageResistance);
                break;
            case true when pd.isBlocking && pd.counterWindow && damageFactor < 2:
                // perfect blocking / counter
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * pd.counterDamageResistance;
                else playerHealth -= damageAmount * pd.counterDamageResistance;
                pc.animator.SetTrigger("Counter");
                pd.counterWindow = false;
                break;
            case true:
                // the player not blocking at all
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * rawDamageResistance;
                else playerHealth -= damageAmount * rawDamageResistance;
                spriteRenderer.color = damageTint;
                isTinting = true;
                if (knockbackForce != Vector2.zero)
                {
                    pc.animator.SetTrigger("Damaged");
                    kb.CallKnockback(knockbackForce * shockMultiplier);
                }
                break;
            case false:
                // the player blocking but getting hit at the back
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * rawDamageResistance;
                else playerHealth -= damageAmount * rawDamageResistance;
                spriteRenderer.color = damageTint;
                isTinting = true;
                pd.StopBlocking();
                if (knockbackForce != Vector2.zero)
                {
                    pc.animator.SetTrigger("Damaged");
                    kb.CallKnockback(knockbackForce * shockMultiplier);
                }
                break;
        }
        pc.currentDashTime = 0f;
        GetComponent<PlayerAbility>().isPerformingAbility = false;
    }
    
    public void Stun(float stunDuration)
    {
        stunTime = stunDuration * shockMultiplier;
        if (stunTime > 0 && !pd.isBlocking) getStunned = true;
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
                GetComponent<PlayerCombat>().canAttack = true;
                shockMultiplier = originalShockMultiplier;
                isStunned = false;
                pc.rb.linearVelocity = new Vector2(0, pc.rb.linearVelocity.y);
            }
        }
    }

    public void ApplyCombat(float combatDuration = 5, bool additive = false) => combatTimer = additive ? combatTimer + combatDuration : combatDuration;
    
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
            if (!inCombat) playerHealth += Time.deltaTime * 3 * regenerationMultiplier;
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
}
