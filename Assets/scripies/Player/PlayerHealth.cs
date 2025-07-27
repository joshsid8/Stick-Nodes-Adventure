using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour, PDamageable
{
    public static PlayerHealth Instance {get; private set;}
    
    public PlayerStats playerStats;
    public Image healthBar;
    public TMP_Text healthText;
    public float playerMaxHealth = 100;
    public float playerHealth = 100;
    public float rawDamageResistance = 1f;

    public float regenerationMultiplier;
    public float modRegenerationMultiplier;
    public float originalRegenerationMultiplier;
    
    [HideInInspector] public PlayerController playerController;
    PlayerDefense playerDefense;

    [SerializeField] private bool isDead;
    
    [SerializeField] private bool getStunned;
    [HideInInspector] public bool isStunned;
    public float stunTime;
    private float currentStunTime;
    public float originalStunMultiplier;
    public float stunMultiplier = 1;
    
    private Knockback kb;
    public float knockbackMultiplier = 1;

    /*private bool invincibleTrigger;
    public bool invincible;
    public float invincibleDuration = 2f;
    private float invincibleTimer;*/
    
    public Color damageTint = Color.red;
    public float tintDuration = 0.1f;
    public float tintSpeed = 5f;
    private Color originalColor;
    [SerializeField] private bool isTinting;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SetValues();
        playerController = GetComponent<PlayerController>();
        playerDefense = GetComponent<PlayerDefense>();
        playerController.animator = GetComponent<Animator>();
        kb = GetComponent<Knockback>();
        originalRegenerationMultiplier = regenerationMultiplier;
        currentStunTime = stunTime * stunMultiplier;
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;
        originalStunMultiplier = 1f;
        //invincibleTrigger = false;
    }

    // Update is called once per frame
    void Update()
    {
        Animation();
        Death();
        Stunned();
        Tint();
        //StartCoroutine(IFrames());
        Regeneration();
        healthBar.fillAmount = playerHealth / playerMaxHealth;
        healthText.text = playerHealth == playerMaxHealth ? playerHealth.ToString("F0") +  " / " + playerMaxHealth.ToString("F0")
            : playerHealth.ToString("F1") +  " / " + playerMaxHealth.ToString("F0");
        if (playerHealth < 0) playerHealth = 0;
    }

    void SetValues()
    {
        if (playerStats != null)
        {
            playerMaxHealth = playerStats.MaxHealth;
            playerHealth = playerStats.MaxHealth;
            regenerationMultiplier = playerStats.RegenerationMultiplier;
            modRegenerationMultiplier = playerStats.ModRegenerationMultiplier;
            stunMultiplier = playerStats.StunMultiplier;
            rawDamageResistance = playerStats.RawDamageResistance;
        }
        else
        {
            Debug.LogError("i cant find any scriptable objects!");
            enabled = false;
        }
    }
    void Animation()
    {
        playerController.animator.SetBool("isStunned", isStunned);
        playerController.animator.SetBool("isDead", isDead);
    }

    void Death()
    {
        if (playerHealth <= 0)
        {
            isDead = true;
        }
        else
        {
            isDead = false;
        }

        if (isDead)
        {
            gameObject.layer = LayerMask.NameToLayer("Dead");
            playerController.canAct = false;
        }
    }

    public void Damage(float damageAmount, Vector2 knockbackForce, bool facingRight, int damageType)
    {
        if (facingRight && !playerController.isFacingRight || !facingRight && playerController.isFacingRight)
        {
            if (playerDefense.isBlocking && !playerDefense.counterWindow && damageType < 1)
            {
                // blocking successfully
                playerHealth -= damageAmount * playerDefense.damageResistance;
                kb.CallKnockback(knockbackForce * playerDefense.damageResistance, playerController.inputDirection);
            }
            else if (playerDefense.isBlocking && playerDefense.counterWindow && damageType < 2)
            {
                // perfect blocking / counter
                playerHealth -= damageAmount * playerDefense.counterDamageResistance;
                playerController.animator.SetTrigger("Counter");
                playerDefense.counterWindow = false;
                playerDefense.isBlocking = false;
            }
            else
            {
                // the player not blocking at all
                playerDefense.isBlocking = false;
                playerHealth -= damageAmount * rawDamageResistance;
                playerController.animator.SetTrigger("Damaged");
                spriteRenderer.color = damageTint;
                isTinting = true;
                playerController.currentDashTime = 0f;
                kb.CallKnockback(knockbackForce, playerController.inputDirection);
            }
        }
        else
        {
            // the player blocking but getting hit at the back
            playerDefense.isBlocking = false;
            playerHealth -= damageAmount * rawDamageResistance;
            playerController.animator.SetTrigger("Damaged");
            //invincibleTrigger = true;
            spriteRenderer.color = damageTint;
            isTinting = true;
            playerController.currentDashTime = 0f;
            kb.CallKnockback(knockbackForce, playerController.inputDirection);
        }
         
    }
    
    public void Stun(float stunDuration)
    {
        stunTime = stunDuration * stunMultiplier;
        if (stunTime > 0 && !playerDefense.isBlocking)
        {
            getStunned = true;
        }
    }

    private void Stunned()
    {
        if (getStunned)
        {
            currentStunTime = stunTime * stunMultiplier;
            getStunned = false;
            if (stunTime > 0)
                isStunned = true;
        }
        if (isStunned)
        {
            playerController.canAct = false;
            currentStunTime -= Time.deltaTime;
            if (currentStunTime <= 0)
            {
                playerController.canAct = true;
                GetComponent<PlayerCombat>().canAttack = true;
                stunMultiplier = originalStunMultiplier;
                isStunned = false;
                playerController.rb.linearVelocity = new Vector2(0, playerController.rb.linearVelocity.y);
            }
        }
    }
    private void Tint()
    {
        if (isTinting)
        {
            spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
            if (spriteRenderer.color == originalColor)
            {
                isTinting = false;
            }
            
        }

    }
    
    /*private IEnumerator IFrames()
    {
        if (invincibleTrigger)
        {
            invincibleTimer = invincibleDuration;
            invincibleTrigger = false;
            invincible = true;
        }

        if (invincible)
        {
            gameObject.layer = LayerMask.NameToLayer("Invincible");
            invincibleTimer -= Time.deltaTime;
            if (invincibleTimer <= 0)
            {
                gameObject.layer = LayerMask.NameToLayer("Player");
                spriteRenderer.enabled = true;
                invincible = false;
            }
        }

        if (invincible)
        {
            spriteRenderer.enabled = !spriteRenderer.enabled;
        }
        yield return null;
    }*/

    private void Regeneration()
    {
        if (playerHealth > 0 && playerHealth < playerMaxHealth) playerHealth += Time.deltaTime *  regenerationMultiplier;
        if (playerHealth > playerMaxHealth) playerHealth = playerMaxHealth;
    }
}
