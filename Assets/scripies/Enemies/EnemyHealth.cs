using System.Collections;
using UnityEngine;

[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyShootBlast))]
[RequireComponent(typeof(EnemyAttack))]
[RequireComponent(typeof(EnemyPatrol))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]

public class EnemyHealth : MonoBehaviour, EDamageables
{
    //Enemy health
    public float enemyMaxHealth = 100f;
    public float enemyHealth = 100f;
    public bool isDead = false;

    //Stun logic
    [SerializeField] private bool getStunned = false;
    private bool isStunned = false;
    private float stunTime = 0;
    [SerializeField] private float currentStunTime = 0;
    public float stunMultiplier = 1;
    
    //block logic
    public bool isCountering;
    public bool isBlocking;
    public float damageResistance;

    //Tint variables
    public Color damageTint = Color.red; // The color to tint when damaged.
    public float tintDuration = 0.1f;    // How long the tint lasts.
    public float tintSpeed = 5f;       // Speed of the color transition.
    private Color originalColor;
    [SerializeField] private bool isTinting = false;
    private SpriteRenderer spriteRenderer;
    private Knockback kb;
    public bool movable = true;

    //phase 2
    public float phase2;
    public int phase2Style;
    
    //other
    private Animator animator;
    private EnemyAttack ea;
    private EnemyPatrol ep;

    // Start is called before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        currentStunTime = stunTime * stunMultiplier;
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color; // Store the original color.
        kb = GetComponent<Knockback>();
        ea = GetComponent<EnemyAttack>();
        ep = GetComponent<EnemyPatrol>();
    }

    // Update is called once per frame
    private void Update()
    {
        animator.SetBool("isStunned", isStunned);
        animator.SetBool("isDead", isDead);
        Stunned();
        Tint();
        if (enemyHealth <= phase2 && phase2Style > 0) ea.styleFactor = phase2Style;
        if (isDead && ep.isGrounded) ep.rb.linearVelocity = new Vector2(0, ep.rb.linearVelocity.y);
    }
    
    private void Stunned()
    {
        if (getStunned)
        {
            currentStunTime = stunTime;
            getStunned = false;
            isStunned = true;
        }
        if (isStunned)
        {
            currentStunTime -= Time.deltaTime;
            if (currentStunTime <= 0)
            {
                isStunned = false;
            }
        }
    }
    private IEnumerator Death()
    {
        if (isDead)
        {
            GetComponent<Loot>().DropLoot();
            GetComponent<EnemyAttack>().enabled = false;
            spriteRenderer.color = Color.white;
            gameObject.layer = LayerMask.NameToLayer("EDead");
            gameObject.tag = "Dead";
            spriteRenderer.enabled = true;
            
            yield return new WaitForSeconds(5f);
            for (int i = 0; i < 25; i++)
            {
                spriteRenderer.enabled = false;
                yield return new WaitForSeconds(0.025f);
                spriteRenderer.enabled = true;
                yield return new WaitForSeconds(0.025f);
            }
            GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerController>().currentCombatTimer = 0;
            Destroy(gameObject);
        }
    }

    public void Damage(float damageAmount, Vector2 knockbackForce)
    {
        if (isBlocking)
        {
            enemyHealth -= damageAmount * damageResistance;
            if (movable) kb.CallKnockback(knockbackForce * 0.5f, 0);
        }
        else if (isCountering) animator.SetTrigger("Counter");
        else
        {
            enemyHealth -= damageAmount;
            if (movable) kb.CallKnockback(knockbackForce, 0);
            if (ea.stunnable) ea.currentAttackCooldown = 0.1f;
        }
        if (enemyHealth <= 0)
        {
            enemyHealth = 0;
            isDead = true;
            StartCoroutine(Death());
        }
        spriteRenderer.color = damageTint;
        isTinting = true;

        if (GetComponent<EnemyPatrol>().enabled) StartCoroutine(GetComponent<EnemyPatrol>().FoundPlayer());
    }

    public void Stun(float stunDuration)
    {
        stunTime = stunDuration * stunMultiplier;
        if (stunTime > 0) getStunned = true;
    }

    private void Flash()
    {
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
}
