using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, PDamageable
{
    public static PlayerHealth Instance {get; private set;}
    
    public Image healthBar;
    public TMP_Text healthText;
    public float playerMaxHealth = 100;
    public float playerHealth = 100;
    public float playerExtraHealth;
    public float rawDamageResistance = 1f;

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
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (spawnOnAwake) StartCoroutine(Spawn(0, true));
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        pc = GetComponent<PlayerController>();
        pd = GetComponent<PlayerDefense>();
        pc.animator = GetComponent<Animator>();
        kb = GetComponent<Knockback>();
        currentStunTime = stunTime * shockMultiplier;
        spriteRenderer = GetComponent<SpriteRenderer>();
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
        healthBar.fillAmount = playerHealth / playerMaxHealth;
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

    void Animation()
    {
        pc.animator.SetBool("isStunned", isStunned);
        pc.animator.SetBool("isDead", isDead);
    }

    public IEnumerator Spawn(float timer, bool realSpawn)
    {
        yield return new WaitForSeconds(timer);
        if (!realSpawn) transform.position = pc.currentCheckPoint.transform.position;
        else transform.position = pc.currentSpawnPoint.transform.position;
    }

    IEnumerator Death()
    {
        if (playerHealth <= 0 && !deathToggle)
        {
            deathToggle = true;
            isDead = true;
            yield return new WaitForSeconds(respawnTime);
            StartCoroutine(Spawn(0, true));
            playerHealth = playerMaxHealth * 0.25f;
            deathToggle = false;
            isDead = false;
            gameObject.layer = LayerMask.NameToLayer("Player");
            pc.canAct = true;
            pc.currentCombatTimer = 0;
        }
    }

    public void Damage(float damageAmount, Vector2 knockbackForce, bool facingRight, int damageType)
    {
        if (facingRight && !pc.isFacingRight || !facingRight && pc.isFacingRight)
        {
            if (pd.isBlocking && !pd.counterWindow && damageType < 1)
            {
                // blocking successfully
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * pd.blockDamageResistance;
                else playerHealth -= damageAmount * pd.blockDamageResistance;
                kb.CallKnockback(knockbackForce * pd.blockDamageResistance, pc.inputDirection);
            }
            else if (pd.isBlocking && pd.counterWindow && damageType < 2)
            {
                // perfect blocking / counter
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * pd.counterDamageResistance;
                else playerHealth -= damageAmount * pd.counterDamageResistance;
                pc.animator.SetTrigger("Counter");
                pd.counterWindow = false;
                pd.isBlocking = false;
            }
            else
            {
                // the player not blocking at all
                pd.isBlocking = false;
                GetComponent<PlayerAbility>().isPerformingAbility = false;
                if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * rawDamageResistance;
                else playerHealth -= damageAmount * rawDamageResistance;
                spriteRenderer.color = damageTint;
                isTinting = true;
                if (knockbackForce != Vector2.zero)
                {
                    pc.animator.SetTrigger("Damaged");
                    kb.CallKnockback(knockbackForce * shockMultiplier, pc.inputDirection);
                    pc.currentDashTime = 0f;
                }
            }
        }
        /*else if (GetComponent<PlayerPerformAbility>().isReinforcing)
        {
            // reinforcing
            if (playerExtraHealth > 0) playerExtraHealth -= damageAmount; // * reinforcementDamageResistance;
            else playerHealth -= damageAmount; // * reinforcementDamageResistance;
            kb.CallKnockback(knockbackForce * shockMultiplier, pc.inputDirection);
        }*/
        else
        {
            // the player blocking but getting hit at the back
            pd.isBlocking = false;
            GetComponent<PlayerAbility>().isPerformingAbility = false;
            if (playerExtraHealth > 0) playerExtraHealth -= damageAmount * rawDamageResistance;
            else playerHealth -= damageAmount * rawDamageResistance;
            spriteRenderer.color = damageTint;
            isTinting = true;
            if (knockbackForce != Vector2.zero)
            {
                pc.animator.SetTrigger("Damaged");
                kb.CallKnockback(knockbackForce * shockMultiplier, pc.inputDirection);
                pc.currentDashTime = 0f;
            }
        }
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
            if (pc.inCombat) playerHealth += Time.deltaTime * 3 * regenerationMultiplier;
            else playerHealth += Time.deltaTime * 1 * regenerationMultiplier;
        }
        if (playerHealth > playerMaxHealth) playerHealth = playerMaxHealth;
    }
}
