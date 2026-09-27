using System.Collections;
using scripies.Core;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class HealthBase : MonoBehaviour, IDamageable
{
    [Header("Health")] 
    [FormerlySerializedAs("playerMaxHealth")]
    [FormerlySerializedAs("enemyMaxHealth")]
    [FormerlySerializedAs("bossMaxHealth")]
    public float maxHealth;
    [FormerlySerializedAs("playerHealth")]
    [FormerlySerializedAs("enemyHealth")]
    [FormerlySerializedAs("bossHealth")]
    public float health;

    public bool isDead;
    public bool isStunned;
    public float stunTime;
    protected float currentStunTime;

    [Header("Damage Tinting")] public Color damageTint = Color.red;
    public float tintSpeed = 5f;
    protected Color originalColor;
    [SerializeField] protected bool isTinting;
    protected SpriteRenderer spriteRenderer;
    
    protected Knockback kb;
    
    
    public float CurrentHealth => health;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public Vector3 Position => transform.position;
    public bool IsStunned => isStunned;

    protected virtual void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        kb = GetComponent<Knockback>();
    }

    protected virtual void Start()
    {
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;
    }

    public virtual void Damage(Damage damage, bool facingRight, string aggressor = "") {
    }

    public virtual void Shock(float stunDuration = 0, Vector2 knockbackDirection = default, float knockbackForce = 20,
        float knockbackTime = 0.2f, bool targetPosition = false)
    {
        
    }

    protected virtual IEnumerator Death()
    {
        yield break;
    }

    public virtual void Heal(float amount)
    {
        health += amount;
    }

    public virtual void CheckStatus(float damage = 0)
    {
        if (health > 0 || isDead) return;
        StartCoroutine(Death());
    }
    protected void Tint()
    {
        if (!isTinting || spriteRenderer == null) return;
        spriteRenderer.color = Color.Lerp(spriteRenderer.color, originalColor, tintSpeed * Time.deltaTime);
        if (spriteRenderer.color == originalColor) isTinting = false;
    }
}
