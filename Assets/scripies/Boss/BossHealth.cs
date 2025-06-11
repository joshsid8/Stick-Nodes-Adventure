using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.UI;

public class BossHealth : MonoBehaviour, EDamageables
{
    //boss health
    public Image healthBar;
    public TMP_Text healthText;
    public TMP_Text bossTitle;
    public float bossMaxHealth = 500f;
    public float bossHealth = 500f;
    public bool isDead = false;
    public string bossName;
    
    //phases
    public bool phase2;
    public float phase2Health;
    public DialogueObject phase2Dialogue;
    public DialogueObject deathDialogue;
    
    //Tint variables
    public Color damageTint = Color.red; // The color to tint when damaged.
    public float tintDuration = 0.1f;    // How long the tint lasts.
    public float tintSpeed = 5f;       // Speed of the color transition.
    private Color originalColor;
    [SerializeField] private bool isTinting = false; 
    private SpriteRenderer spriteRenderer;
    
    //other
    private BossAttack ba;
    Animator animator;
    float stunTime;
    DialogueUI dialogue;
    
    // Start is called before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ba = GetComponent<BossAttack>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;
        bossMaxHealth = bossHealth;
        dialogue = GameObject.Find("Canvas").GetComponent<DialogueUI>();
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
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            GetComponent<BossAttack>().isAttacking = false;
            GetComponent<BossAttack>().rangeRadius = 0;
            ba.bossBar.SetActive(false);
            gameObject.layer = LayerMask.NameToLayer("Dead");
            gameObject.tag = "Dead";
            spriteRenderer.enabled = true;
            
            GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerController>().currentCombatTimer = 0;
            yield return new WaitForSeconds(3);
            dialogue.ShowDialogue(deathDialogue);
        }
    }

    public void Damage(float damageAmount)
    {
        bossHealth -= damageAmount;
        if (bossHealth <= 0)
        {
            bossHealth = 0;
            isDead = true;
            StartCoroutine(Defeat());
        }
        else if (bossHealth <= phase2Health) phase2 = true;
        spriteRenderer.color = damageTint;
        isTinting = true; 
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
