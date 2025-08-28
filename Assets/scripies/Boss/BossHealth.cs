using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.UI;

public class BossHealth : MonoBehaviour, EDamageables
{
    //boss health
    private Image healthBar;
    private TMP_Text healthText;
    private TMP_Text bossTitle;
    public float bossMaxHealth = 500f;
    public float bossHealth = 500f;
    public bool isDead; 
    [HideInInspector] public bool deathToggle;
    public string bossName;
    
    //phases
    public bool phase2;
    public float phase2Health;
    public DialogueObject[] introDialogue;
    public DialogueObject deathDialogue;
    
    //tint variables
    public Color damageTint = Color.red;
    public float tintDuration = 0.1f;
    public float tintSpeed = 5f;
    private Color originalColor;
    [SerializeField] private bool isTinting = false; 
    private SpriteRenderer spriteRenderer;
    
    //defense
    public bool isBlocking;
    public float shieldHealth;
    public float dmgRes;
    public bool isCountering;
    
    //other
    private BossAttack ba;
    Animator animator;
    float stunTime;
    private Knockback kb;
    public bool movable = true;
    
    // Start is called before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        healthBar = BossHealthUI.Instance.healthBar;
        healthText = BossHealthUI.Instance.healthText;
        bossTitle = BossHealthUI.Instance.bossTitle;
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;
        bossMaxHealth = bossHealth;
        kb = GetComponent<Knockback>();
    }

    // Update is called once per frame
    private void Update()
    {
        animator.SetBool("isDead", isDead);
        animator.SetBool("phase2", phase2);
        Tint();
        healthBar.fillAmount = bossHealth / bossMaxHealth;
        healthText.text = bossHealth == bossMaxHealth ? bossHealth.ToString("F0") +  " / " + bossMaxHealth.ToString("F0")
            : bossHealth.ToString("F1") +  " / " + bossMaxHealth.ToString("F0");
        bossTitle.text = bossName;
        
    }
    
    private IEnumerator Defeat()
    {
        if (isDead)
        {
            spriteRenderer.color = Color.white;
            GetComponent<Loot>().DropLoot();
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            GetComponent<BossAttack>().isAttacking = false;
            GetComponent<BossAttack>().rangeRadius = 0;
            BossHealthUI.Instance.gameObject.SetActive(false);
            gameObject.layer = LayerMask.NameToLayer("EDead");
            gameObject.tag = "Dead";
            spriteRenderer.enabled = true;
            yield return new WaitForSeconds(3);
            isDead = false;
            PlayerHealth.Instance.pc.currentCombatTimer = 0;
            animator.SetTrigger("Rise");
            DialogueUI.Instance.ShowDialogue(deathDialogue);
        }
    }

    public void Damage(float damageAmount, Vector2 knockbackForce)
    {
        if (isBlocking && shieldHealth > 0)
        {
            bossHealth -= damageAmount * dmgRes;
            shieldHealth -= damageAmount;
            if (movable) kb.CallKnockback(knockbackForce / 4, 0);
            spriteRenderer.color = damageTint / 2;
            isTinting = true;
        }
        else if (isBlocking && shieldHealth <= 0)
        {
            animator.SetTrigger("Guard Break");
            bossHealth -= damageAmount * 1.2f;
            if (movable) kb.CallKnockback(knockbackForce / 2, 0);
            spriteRenderer.color = damageTint;
            isTinting = true;
        }
        else if (isCountering)
        {
            animator.SetTrigger("CSuccess");
            isCountering = false;
        }
        else
        {
            bossHealth -= damageAmount;
            if (movable) kb.CallKnockback(knockbackForce / 2, 0);
            spriteRenderer.color = damageTint;
            isTinting = true;
        }
        if (bossHealth <= 0 && !deathToggle)
        {
            deathToggle = true;
            bossHealth = 0;
            isDead = true;
            StartCoroutine(Defeat());
        }
        else if (bossHealth <= phase2Health) phase2 = true;
    }

    
    private void Tint()
    {
        if (isTinting && !isDead)
        {
            spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
            if (spriteRenderer.color == originalColor)
            {
                isTinting = false;
            }
        }
    }
    public void Stun(float stunDuration)
    {
        // bosses cant get stunned so dis useless fr
        stunTime = stunDuration;
        stunTime = 0;
        if (stunTime >= 69) Debug.Log("jump yourself");
    }
}
