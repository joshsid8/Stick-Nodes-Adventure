using System.Collections;
using UnityEngine;

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

    //Tint variables
    public Color damageTint = Color.red; // The color to tint when damaged.
    public float tintDuration = 0.1f;    // How long the tint lasts.
    public float tintSpeed = 5f;       // Speed of the color transition.
    private Color originalColor;
    [SerializeField] private bool isTinting = false;
    private SpriteRenderer spriteRenderer;

    //other
    Animator animator;

    // Start is called before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        currentStunTime = stunTime * stunMultiplier;
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color; // Store the original color.
    }

    // Update is called once per frame
    private void Update()
    {
        animator.SetBool("isStunned", isStunned);
        animator.SetBool("isDead", isDead);
        Stunned();
        Tint();
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
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
            GetComponent<EnemyAttack>().enabled = false;
            gameObject.layer = LayerMask.NameToLayer("Dead");
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

    public void Damage(float damageAmount)
    {
        enemyHealth -= damageAmount;
        if (enemyHealth <= 0)
        {
            enemyHealth = 0;
            isDead = true;
            StartCoroutine(Death());
        }
        spriteRenderer.color = damageTint;
        isTinting = true; 
    }

    public void Stun(float stunDuration)
    {
        stunTime = stunDuration * stunMultiplier;
        if (stunTime > 0)
            getStunned = true;
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
