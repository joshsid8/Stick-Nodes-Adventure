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
    private TMP_Text bossTitle;
    public float bossMaxHealth = 500f;
    public float bossHealth = 500f;
    public bool isDead; 
    [HideInInspector] public bool deathToggle;
    public string bossName;
    [SerializeField] private Color[] healthColours = new Color[2];
    
    [Header("Phases and Dialogue")]
    public bool phase2;
    public float phase2Health;
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
    private float stunTime;
    public bool isStunned;
    private Knockback kb;
    public bool movable = true;
    
    [Header("References")]
    [SerializeField] private AudioClip deathClip;

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
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        kb = GetComponent<Knockback>();
    }

    private void Start()
    {
        healthBar = GameHUD.Instance.bossHealthBar;
        bossTitle = GameHUD.Instance.bossTitleText;
        originalColor = spriteRenderer.color;
        bossMaxHealth = bossHealth;
    }

    // Update is called once per frame
    private void Update()
    {
        animator.SetBool("isDead", isDead);
        animator.SetBool("phase2", phase2);
        Tint();
        
        if (!ba.isAttacking) return;
        healthBar.fillAmount = bossHealth / bossMaxHealth;
        bossTitle.text = bossName;
        stunTime -= Time.deltaTime;
        isStunned = stunTime > 0;
    }

    private void OnValidate()
    {
        if (ba == null) return;
        if (!isDead) CheckStatus();
    }

    private IEnumerator Defeat()
    {
        if (!isDead) yield break;
        ba.isAttacking = false;
        ba.ResetTriggers();
        ba.currentBackupMoveTimer = 10 ^ 10;
        //ba.enabled = false;
        spriteRenderer.color = Color.white;
        GameEvents.TriggerDropLoot(gameObject);
        GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        GetComponent<BossAttack>().isAttacking = false;
        GetComponent<BossAttack>().rangeRadius = 0;
        gameObject.layer = LayerMask.NameToLayer("EDead");
        gameObject.tag = "Dead";
        spriteRenderer.enabled = true;
        GameEvents.TriggerPlaySound(deathClip, gameObject, randomPitch: false);
        yield return new WaitForSeconds(3);
        GameHUD.Instance.ToggleBossHealth(false);
        isDead = false;
        GameEvents.TriggerAddPlayerTime("Combat", 0);
        animator.SetTrigger("Rise");
        GameEvents.TriggerShowDialogue(deathDialogue, ba.dt.Speaker as NPC);
    }

    public void Damage(float damageAmount, bool facingRight, int damageFactor = 0,
        IDamageable.ElementType elementType = IDamageable.ElementType.Normal, float elementFactor = 1F, bool isTrue = false,
        bool damageOverTime = false, float procDmg = 0F, float procInterval = 0F, string aggressor = "")
    {
        switch (isBlocking, shieldHealth > 0, isTrue, damageFactor, isCountering) // where '_' means null/ignore
        {
            case (true, true, _, < 1, _):
                bossHealth -= damageAmount * dmgRes;
                shieldHealth -= damageAmount;
                spriteRenderer.color = damageTint / 2;
                isTinting = true;
                break;
            case (true, false, false, < 1, _):
                animator.SetTrigger("Damaged");
                bossHealth -= damageAmount * 1.2f;
                spriteRenderer.color = damageTint;
                isTinting = true;
                break;
            case (_, _, _, < 2, true):
                animator.SetTrigger("CSuccess");
                isCountering = false;
                break;
            default:
                bossHealth -= damageAmount;
                if (ba.stunnable && movable)
                {
                    var rng = Random.Range(0, 3);
                    animator.SetInteger("dmgrng", rng);
                    animator.SetTrigger("Damaged");
                }
                spriteRenderer.color = damageTint;
                isTinting = true;
                break;
        }
        if (damageOverTime) StartCoroutine(DamageOverTime(procDmg, procInterval));
        if (!ba.isAttacking && bossHealth > 0) ba.StartFight();
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
        if (!Application.isPlaying || !ba.isAttacking) return;
        if (bossHealth <= 0 && !deathToggle)
        {
            deathToggle = true;
            bossHealth = 0;
            isDead = true;
            ba.isAttacking = false;
            GameEvents.TriggerBossDefeated();
            StartCoroutine(Defeat());
        }
        else if (bossHealth <= phase2Health) StartCoroutine(PhaseToggle(true));
        if (!ba.IsInvokingAttackSystem() && ba.isAttacking)
            ba.InvokeAttackSystem(0.2f);
    }

    public IEnumerator PhaseToggle(bool toggle)
    {
        phase2 = toggle;
        switch (phase2)
        {
            case true:
                while (healthBar.color != healthColours[1])
                {
                    healthBar.color = Color.Lerp(healthBar.color, healthColours[1], Time.deltaTime);
                    yield return new WaitForFixedUpdate();
                }
                break;
            case false:
                healthBar.color = healthColours[0];
                break;
        }
    }
    
    private void Tint()
    {
        if (isTinting && !isDead)
        {
            spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
            if (spriteRenderer.color == originalColor) isTinting = false;
        }
    }
    public void Shock(float stunDuration = 0.5F, Vector2 knockbackDirection = default, float knockbackForce = 0, float knockbackTime = 0, bool targetPosition = false)
    {
        stunTime = ba.stunnable ? stunDuration : 0;
        if (stunTime >= 69) print("jump yourself");
        if (movable)
        {
            if (isBlocking && shieldHealth > 0) kb.ApplyKnockback(knockbackDirection, knockbackForce / 4, knockbackTime, targetPosition);
            else kb.ApplyKnockback(knockbackDirection, knockbackForce / 2 , knockbackTime, targetPosition);
        }
    }
    public void Heal(float amount) => bossHealth += amount;
}
