using System;
using UnityEngine;
using System.Collections;
using scripies.Core;
using scripies.Dialogue;
using TMPro;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class BossHealth : MonoBehaviour, IDamageable, IEnemyStatus
{
    [Header("Health")]
    private Image healthBar;
    private TMP_Text healthText;
    private TMP_Text bossTitle;
    public float bossMaxHealth = 500f;
    public float bossHealth = 500f;
    public bool isDead; 
    [HideInInspector] public bool deathToggle;
    public string bossName;
    
    [Header("Phases and Dialogue")]
    public bool phase2;
    public float phase2Health;
    public DialogueObject[] introDialogue;
    public DialogueObject deathDialogue;
    
    [Header("Damage Tinting")]
    public Color damageTint = Color.red;
    public float tintDuration = 0.1f;
    public float tintSpeed = 5f;
    private Color originalColor;
    [SerializeField] private bool isTinting; 
    private SpriteRenderer spriteRenderer;
    
    [Header("Defense status")]
    public bool isBlocking;
    public float shieldHealth;
    public float dmgRes;
    public bool isCountering;
    
    [Header("Shock")]
    float stunTime;
    public bool isStunned;
    private Knockback kb;
    public bool movable = true;
    
    [Header("References")]
    private BossAttack ba;
    private Animator animator;
    
    [Header("Getters and Setters")]
    public float CurrentHealth => bossHealth;
    public float MaxHealth => bossMaxHealth;
    public bool IsDead => isDead;
    public bool IsStunned => isStunned;
    public Vector3 Position => transform.position;
    public bool IsGrounded => ba.isGrounded;
    public bool IsFacingRight => ba.isFacingRight;

    // Start is called before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        healthBar = BossHealthUI.Instance.healthBar;
        healthText = BossHealthUI.Instance.healthText;
        bossTitle = BossHealthUI.Instance.bossTitle;
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        kb = GetComponent<Knockback>();
    }

    private void Start()
    {
        originalColor = spriteRenderer.color;
        bossMaxHealth = bossHealth;
    }

    // Update is called once per frame
    private void Update()
    {
        animator.SetBool("isDead", isDead);
        animator.SetBool("phase2", phase2);
        Tint();
        healthBar.fillAmount = bossHealth / bossMaxHealth;
        //healthText.text = bossHealth == bossMaxHealth ? bossHealth.ToString("F0") +  " / " + bossMaxHealth.ToString("F0")
        //    : bossHealth.ToString("F1") +  " / " + bossMaxHealth.ToString("F0");
        bossTitle.text = bossName;
        stunTime -= Time.deltaTime;
        isStunned = stunTime > 0;
    }
    
    private IEnumerator Defeat()
    {
        if (isDead)
        {
            spriteRenderer.color = Color.white;
            GameEvents.TriggerDropLoot(gameObject);
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            GetComponent<BossAttack>().isAttacking = false;
            GetComponent<BossAttack>().rangeRadius = 0;
            BossHealthUI.Instance.gameObject.SetActive(false);
            gameObject.layer = LayerMask.NameToLayer("EDead");
            gameObject.tag = "Dead";
            spriteRenderer.enabled = true;
            yield return new WaitForSeconds(3);
            isDead = false;
            GameEvents.TriggerAddPlayerTime("Combat", 0, false);
            animator.SetTrigger("Rise");
            GameEvents.TriggerShowDialogue(deathDialogue);
        }
    }

    public void Damage(float damageAmount, bool facingRight, int damageFactor = 0,
        IDamageable.ElementType elementType = 0, float elementFactor = 1f, bool isTrue = false,
        bool damageOverTime = false, float procDmg = 0f, float procInterval = 0f)
    {
        if (isBlocking && shieldHealth > 0 && damageFactor < 1)
        {
            bossHealth -= damageAmount * dmgRes;
            shieldHealth -= damageAmount;
            spriteRenderer.color = damageTint / 2;
            isTinting = true;
        }
        else if (isBlocking && shieldHealth <= 0 && !isTrue && damageFactor < 1)
        {
            animator.Play("Damaged");
            bossHealth -= damageAmount * 1.2f;
            spriteRenderer.color = damageTint;
            isTinting = true;
        }
        else if (isCountering && !isTrue && damageFactor < 2)
        {
            animator.Play("CSuccess");
            isCountering = false;
        }
        else
        {
            bossHealth -= damageAmount;
            if (ba.stunnable && movable)
            {
                var rng = Random.Range(0, 3);
                animator.SetInteger("dmgrng", rng);
                animator.SetTrigger("Damaged");
            }
            spriteRenderer.color = damageTint;
            isTinting = true;
        }
        if (damageOverTime) StartCoroutine(DamageOverTime(procDmg, procInterval));

        CheckStatus();
    }
    
    private IEnumerator DamageOverTime(float procDmg, float procInterval)
    {
        var procTaken = procDmg;
        var procAmount = procDmg * procInterval;
        while (procTaken > 0)
        {
            bossHealth -= procAmount;
            spriteRenderer.color = damageTint;
            isTinting = true;
            procTaken -= procAmount;
            CheckStatus();
            yield return new WaitForSeconds(procInterval);
        }
    }

    private void CheckStatus()
    {
        if (bossHealth <= 0 && !deathToggle)
        {
            deathToggle = true;
            bossHealth = 0;
            isDead = true;
            // TODO: I Kinda forgot what this does, i guess test the game and see what bugs arise?
            //if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm)) mm.OnBossDefeated();
            StartCoroutine(Defeat());
        }
        else if (bossHealth <= phase2Health) phase2 = true;
    }
    
    private void Tint()
    {
        if (isTinting && !isDead)
        {
            spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
            if (spriteRenderer.color == originalColor) isTinting = false;
        }
    }
    public void Shock(float stunDuration = 0.5F, Vector2 knockbackDirection = default, float knockbackForce = 0, float knockbackTime = 0)
    {
        stunTime = ba.stunnable ? stunDuration : 0;
        if (stunTime >= 69) print("jump yourself");
        if (movable)
        {
            if (isBlocking && shieldHealth > 0) kb.CallKnockback(knockbackDirection, knockbackForce / 4, knockbackTime);
            else kb.CallKnockback(knockbackDirection, knockbackForce / 2 , knockbackTime);
        }
    }
    public void Heal(float amount) => bossHealth += amount;
}
