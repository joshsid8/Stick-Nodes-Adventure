using UnityEngine;

public class Projectile : AbilityBase
{
    [Header("Physics")]
    [SerializeField] private float projectileHeight = 1;
    public float projectileSpeed = 20;
    [SerializeField] private float gravityScale;
    [SerializeField] private float airResistance = 1;
    [Tooltip("Turning this on will swap height with speed, making the projectile get shot upward.")]
    public bool swapForce;

    [Space]
    [Header("Randomisation")]
    [SerializeField] private bool randomisation;
    [SerializeField] private float randomRes = 0.1f;
    [SerializeField] private float randomAngle = 2f;
    [SerializeField] private bool upwardOnly = true;

    [Space]
    [Header("Targeting")]
    [SerializeField] private float projectileRange;
    [SerializeField] private LayerMask[] target;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[2];
    
    [Space]
    [Header("Direction & Rotation")]
    [SerializeField] private float rotationOffset = 90;
    private bool isFacingRightDirection;
    private float objectDirection;
    
    [Space]
    [Header("Checks")]
    private Rigidbody2D rb;
    private bool collided;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected override void Start()
    {
        rb =  GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        coll = GetComponent<Collider2D>();
        playerTransform = PlayerHealth.Instance.gameObject.transform;
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>();
        if (anim != null) animator.Play(anim.name); 
        if (randomisation)
        {
            airResistance = Random.Range(upwardOnly ? airResistance : airResistance - airResistance * randomRes, airResistance + airResistance * randomRes);
            projectileHeight = Random.Range(upwardOnly ? 0 : -projectileHeight - randomAngle, projectileHeight + randomAngle);
        }

        if (swapForce)
        {
            var temp = projectileSpeed;
            var temp2 = projectileHeight;
            projectileSpeed = temp2;
            projectileHeight = temp;
        }
        ApplyForce();
        Destroy(gameObject, lifeTime);
    }

    protected override void Update()
    {
        gameObject.layer = typeFactor switch
        {
            1 => LayerMask.NameToLayer("PVFX"),
            2 => LayerMask.NameToLayer("EVFX"),
            _ => LayerMask.NameToLayer("FX")
        };
        // This makes the object point to where its moving - something to learn (MAFFS)
        if (!collided)
        {
            var velocity = rb.linearVelocity;
            if (velocity.sqrMagnitude > 0.001f)
            {
                var angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
            }
        }
    }

    public override void SetValues(int factor, bool facingRight)
    {
        typeFactor = factor;
        isFacingRightDirection = facingRight;
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
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("PVFX")) Crash();
                break;
            default:
                if (collision.CompareTag("Player") || collision.CompareTag("Enemy") || collision.CompareTag("Boss")) Collided();
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == 
                         LayerMask.NameToLayer(typeFactor == 0 ? "FX" : typeFactor == 1 ? "PVFX" : "EVFX")) Crash();
                break; 
        }
    }

    private void ApplyForce()
    {
        rb.gravityScale = gravityScale;
        rb.linearDamping = airResistance;

        if (!isDirectional)
        {
            float angleDeg = isFacingRightDirection ? 90 + randomAngle : -90 + randomAngle;
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, angleDeg);
            objectDirection = isFacingRightDirection ? 1f : -1f;
            rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, projectileHeight);
        }
        else
        {
            transform.eulerAngles = new Vector3(gameObject.transform.eulerAngles.x, gameObject.transform.eulerAngles.y, direction);
            var angleRad = direction * Mathf.Deg2Rad;
            rb.linearVelocity = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * projectileSpeed;
        }
    }
        
    private void Collided()
    {
        hits[0] = Physics2D.CircleCast(transform.position, projectileRange, transform.right, 0f, target[0]);
        hits[1] = Physics2D.CircleCast(transform.position, projectileRange, transform.right, 0f, target[1]);
        var deflected = false;
        
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
                            eDamageable.Stun(stunTime);
                            eDamageable.Damage(damage, knockbackForce * objectDirection);
                            PlayerHealth.Instance.currentCombatTimer = +PlayerHealth.Instance.combatTimer * 0.5f;
                            //Add a global combat trigger method
                        }
                        else print($"Clueless: {hit.collider.gameObject.name} doesnt have a damageable component.");

                        break;
                    case 2:
                        if (hit.collider.gameObject.GetComponent<PlayerDefense>().counterWindow)
                        {
                            deflected = true;
                            typeFactor = 1;
                            PlayerHealth.Instance.pc.animator.SetTrigger("Counter");
                            hit.collider.gameObject.GetComponent<PlayerDefense>().counterWindow = false;
                            hit.collider.gameObject.GetComponent<PlayerDefense>().isBlocking = false;
                            break;
                        }
                        var pDamageable = hit.collider.gameObject.GetComponent<PDamageable>();
                        if (pDamageable != null)
                        {
                            pDamageable.Damage(damage, knockbackForce * objectDirection,
                                isFacingRightDirection, elementType, 0F);
                            pDamageable.Stun(stunTime);

                            PlayerHealth.Instance.currentCombatTimer += PlayerHealth.Instance.combatTimer * 0.5f;
                        }
                        break;
                    default:
                        var entity = hit.collider;
                        if (entity.CompareTag("Enemy") || entity.CompareTag("Boss"))
                        {
                            var eDmg = hit.collider.gameObject.GetComponent<EDamageables>();
                            if (eDmg != null)
                            {
                                eDmg.Stun(stunTime);
                                eDmg.Damage(damage, knockbackForce * objectDirection);
                                PlayerHealth.Instance.currentCombatTimer =
                                    +PlayerHealth.Instance.combatTimer * 0.5f;
                            }
                        }
                        else if (entity.CompareTag("Player"))
                        {
                            if (hit.collider.gameObject.GetComponent<PlayerDefense>().counterWindow)
                            {
                                deflected = true;
                                typeFactor = 1;
                                PlayerHealth.Instance.pc.animator.SetTrigger("Counter");
                                hit.collider.gameObject.GetComponent<PlayerDefense>().counterWindow = false;
                                hit.collider.gameObject.GetComponent<PlayerDefense>().isBlocking = false;
                                break;
                            }
                            var pDmg = hit.collider.gameObject.GetComponent<PDamageable>();
                            if (pDmg != null)
                            {
                                pDmg.Damage(damage, knockbackForce * objectDirection,
                                    isFacingRightDirection, elementType, 0F);
                                pDmg.Stun(stunTime);

                                PlayerHealth.Instance.currentCombatTimer +=
                                    PlayerHealth.Instance.combatTimer * 0.5f;
                            }
                        }
                        else print("Clueless.");
                        break;
                }
            }
        }
        if (!deflected) Crash(); else Deflect(typeFactor);
    }

    private void Crash()
    {
        collided = true;
        if (endAnim != null) animator.Play(endAnim.name);
        rb.linearVelocity = Vector2.zero;
        isDirectional = false;
        if (abilityOnCollide) 
        {
            foreach (var ability in subAbilities)
            {
                var ab = Instantiate(ability.abilityPrefab, transform.position, transform.rotation);
                ability.SetProperties(ab.GetComponent<AbilityBase>());
                ab.GetComponent<AbilityBase>().SetValues(typeFactor, isFacingRightDirection);
            }
        }
        Destroy(gameObject, 0.2f);
    }

    public void Deflect(int factor)
    {
        typeFactor = factor;
        isFacingRightDirection = !isFacingRightDirection;
        projectileSpeed += 5f;
        if (swapForce)
        {
            var temp = projectileSpeed;
            var temp2 = projectileHeight;
            projectileSpeed = temp2;
            projectileHeight = temp;
        }
        ApplyForce();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, projectileRange);
    }
}
