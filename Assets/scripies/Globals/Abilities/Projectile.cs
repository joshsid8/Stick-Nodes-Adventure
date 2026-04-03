using UnityEngine;

public class Projectile : MonoBehaviour
{
    public int typeFactor;
    
    public float projectileSpeed = 20;
    [SerializeField] private float projectileMinSpeed = 10;
    [SerializeField] private float projectileHeight = 1;
    [SerializeField] private float airResistance = 1;
    public float projectileDamage = 15;
    public float projectileStunTime = 0.5f;
    public Vector2 projectileKnockbackForce;
    public float projectileLifeTime = 3f;
    public float energyRequired = 20f;
    public float abilityCooldown = 1f;
    public int projectileAmount = 1;
    public int elementType;
    public bool isHoldable;
    
    [SerializeField] private bool randomisation;
    [SerializeField] private float randomRes = 0.1f;
    [SerializeField] private float randomAngle = 2f;
    [SerializeField] private bool upwardOnly = true;
    [SerializeField] private float projectileRange;
    public float projectileDirection;
    public bool isDirectional;
    
    private RaycastHit2D[] hits = new RaycastHit2D[2];
    [SerializeField] private LayerMask[] target;
    
    private bool isFacingRightDirection;
    private float objectDirection;
    private Rigidbody2D rb;
    private Animator animator;
    [SerializeField] private AnimationClip anim;
    [SerializeField] private AnimationClip collideAnim;
    private bool collided;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb =  GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (anim != null) animator.Play(anim.name); 
        if (randomisation)
        {
            airResistance = Random.Range(upwardOnly ? airResistance : airResistance - airResistance * randomRes, airResistance + airResistance * randomRes);
            projectileHeight = Random.Range(upwardOnly ? 0 : -projectileHeight - randomAngle, projectileHeight + randomAngle);
        }
        ApplyInitialDirection();
        Destroy(gameObject, projectileLifeTime);
    }
    
    private void Update()
    {
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        
        if (projectileMinSpeed < projectileSpeed) projectileSpeed -= Time.deltaTime * airResistance;
        if (!collided && projectileSpeed > 0) rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, rb.linearVelocity.y);
    }

    public void SetValues(int factor, bool facingRight, float direction = 0, bool directional = false, float speedMult = 1)
    {
        typeFactor = factor;
        isFacingRightDirection = facingRight;
        if (speedMult > 0) projectileSpeed *= speedMult;
        isDirectional = directional;
        if (isDirectional) projectileDirection = direction;
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (typeFactor)
        {
            case 1:
                if (collision.CompareTag("Enemy") || collision.CompareTag("Boss")) Collided();
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("EVFX")) Crash();
                break;
            case 2:
                if (collision.CompareTag("Player")) Collided();
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("PVFX")) 
                    Crash();
                break;
            default:
                if (collision.CompareTag("Player") || collision.CompareTag("Enemy") || collision.CompareTag("Boss")) Collided();
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == 
                         LayerMask.NameToLayer(typeFactor == 0 ? "FX" : typeFactor == 1 ? "PVFX" : "EVFX")) Crash();
                break; 
        }
    }

    private void ApplyInitialDirection()
    {
        if (!isDirectional)
        {
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, isFacingRightDirection ? 90 + randomAngle : -90 + randomAngle);
            objectDirection = isFacingRightDirection ? 1f : -1f;
            rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, projectileHeight);
        }
        else
        {
            transform.eulerAngles = new Vector3(gameObject.transform.eulerAngles.x, gameObject.transform.eulerAngles.y, projectileDirection);
            var angle = projectileDirection * Mathf.Deg2Rad;
            rb.linearVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * projectileSpeed;
        }
    }
        
    private void Collided()
    {
        hits[0] = Physics2D.CircleCast(transform.position, projectileRange, transform.right, 0f, target[0]);
        hits[1] = Physics2D.CircleCast(transform.position, projectileRange, transform.right, 0f, target[1]);
        
        foreach (var hit in hits) 
        {
            if (hit.collider != null)
            {
                switch (typeFactor)
                {
                    case 1:
                        var eDamageable = hit.collider.gameObject.GetComponent<EDamageables>();
                        if (eDamageable != null)
                        {
                            eDamageable.Stun(projectileStunTime);
                            eDamageable.Damage(projectileDamage, projectileKnockbackForce * objectDirection);
                            PlayerHealth.Instance.pc.currentCombatTimer = +PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            //Add a global combat trigger method
                        }
                        else print($"Clueless: {hit.collider.gameObject.name} doesnt have a damageable component.");

                        break;
                    case 2:
                        var pDamageable = hit.collider.gameObject.GetComponent<PDamageable>();
                        if (pDamageable != null)
                        {
                            pDamageable.Damage(projectileDamage, projectileKnockbackForce * objectDirection,
                                isFacingRightDirection, elementType);
                            pDamageable.Stun(projectileStunTime);

                            PlayerHealth.Instance.pc.currentCombatTimer += PlayerHealth.Instance.pc.combatTimer * 0.5f;
                        }

                        break;
                    default:
                        var entity = hit.collider;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var eDmg = hit.collider.gameObject.GetComponent<EDamageables>();
                            if (eDmg != null)
                            {
                                eDmg.Stun(projectileStunTime);
                                eDmg.Damage(projectileDamage, projectileKnockbackForce * objectDirection);
                                PlayerHealth.Instance.pc.currentCombatTimer =
                                    +PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            var pDmg = hit.collider.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(projectileDamage, projectileKnockbackForce * objectDirection,
                                    isFacingRightDirection, elementType);
                                pDmg.Stun(projectileStunTime);

                                PlayerHealth.Instance.pc.currentCombatTimer +=
                                    PlayerHealth.Instance.pc.combatTimer * 0.5f;
                            }
                        }
                        else print("Clueless.");
                        break;
                }
            }
        }
        Crash();
    }

    private void Crash()
    {
        collided = true;
        if (collideAnim != null) animator.Play(collideAnim.name);
        rb.linearVelocity = Vector2.zero;
        Destroy(gameObject, 0.2f);
    }

    public void Deflect(int factor)
    {
        typeFactor = factor;
        isFacingRightDirection = !isFacingRightDirection;
        ApplyInitialDirection();
        projectileSpeed += 5f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}
