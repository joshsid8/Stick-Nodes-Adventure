using System;
using System.Collections.Generic;
using System.IO;
using scripies.Core;
using UnityEngine;
using Random = UnityEngine.Random;

public class Projectile : AbilityBase
{
    [Header("Physics")]
    [SerializeField] private float projectileHeight = 1;
    public float projectileSpeed = 20;
    [SerializeField] private float gravityScale;
    [SerializeField] private float airResistance = 1;
    [Tooltip("Turning this on will swap height with speed, making the projectile get shot upward.")]
    public bool swapForce;
    public int pierceAmount;

    [Space]
    [Header("Randomisation")]
    [SerializeField] private bool randomisation;
    [SerializeField] private float randomRes = 0.1f;
    [SerializeField] private float randomAngle = 2f;
    [SerializeField] private bool upwardOnly = true;

    [Space]
    [Header("Targeting")]
    [SerializeField] private LayerMask[] target;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[2];
    
    [Space]
    [Header("Direction & Rotation")]
    [SerializeField] private float rotationOffset = 90;
    
    [Space]
    [Header("Checks")]
    private Rigidbody2D rb;
    private bool collided;
    private CircleCollider2D ccoll;
    [SerializeField] private float objectRange;
    
    protected override void Awake()
    {
        base.Awake();
        rb =  GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        ccoll = GetComponent<CircleCollider2D>();
    }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected override void Start()
    {
        if (anim != null) animator.Play(anim.name); 
        if (randomisation)
        {
            airResistance = Random.Range(upwardOnly ? airResistance : airResistance - airResistance * randomRes, airResistance + airResistance * randomRes);
            projectileHeight = Random.Range(upwardOnly ? 0 : -projectileHeight - randomAngle, projectileHeight + randomAngle);
        }

        ApplyForce(isDirectional,!swapForce ? projectileSpeed : projectileHeight, swapForce ? projectileSpeed : projectileHeight);
        Destroy(gameObject, lifeTime);
        objectRange = ccoll.radius / 3 * sizeMult + 0.3f;
        transform.localScale *= sizeMult;
        if (startSfx != null) GameEvents.TriggerPlaySound(startSfx, gameObject);
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
        if (collided) return;
        var velocity = rb.linearVelocity;
        if (!(velocity.sqrMagnitude > 0.001f)) return;
        var angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
    }
    
    protected void OnTriggerEnter2D(Collider2D collision)
    {
        if (typeFactor == 0 && collision.GetComponentInChildren<AbilityBase>() != null)
            if (abilityId == collision.GetComponentInChildren<AbilityBase>().abilityId && !perceiveAllObjects) return;
        
        switch (typeFactor)
        {
            case 1:
                if (collision.CompareTag("Enemy") || collision.CompareTag("Boss"))
                {
                    Collided();
                    pierceAmount--;
                }
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("EVFX")) Crash();
                break;
            case 2:
                if (collision.CompareTag("Player")) Collided();
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == LayerMask.NameToLayer("PVFX")) Crash();
                break;
            default:
                if (collision.CompareTag("Player") || collision.CompareTag("Enemy") || collision.CompareTag("Boss")) Collided();
                else if (collision.CompareTag("Terrain") || collision.gameObject.layer == 
                         LayerMask.NameToLayer(typeFactor switch { 0 => "FX", 1 => "PVFX", _ => "EVFX" })) 
                    Crash();
                break; 
        }
    }
    
    // We leave these empty so projectile doesnt use beam/pillar logic (YET???)
    protected override void OnTriggerStay2D(Collider2D collision){}
    protected override void OnTriggerExit2D(Collider2D collision){}
    protected override void HitBox(){}
    
    public void ApplyForce(bool directional, float projSpeed, float projHeight)
    {
        rb.gravityScale = gravityScale;
        rb.linearDamping = airResistance;

        if (!isDirectional)
        {
            var angleDeg = isFacingRightDirection ? 90 + randomAngle : -90 + randomAngle;
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, angleDeg);
            rb.linearVelocity = new Vector2(isFacingRightDirection ? projectileSpeed : -projectileSpeed, projectileHeight);
        }
        else
        {
            transform.eulerAngles = new Vector3(gameObject.transform.eulerAngles.x, gameObject.transform.eulerAngles.y, direction);
            // Holy maths bro
            var angleRad = direction * Mathf.Deg2Rad;
            rb.linearVelocity = new Vector2(Mathf.Cos(angleRad) * (isFacingRightDirection ? 1 : -1), Mathf.Sin(angleRad)) * projectileSpeed;
        }
    }
        
    private void Collided()
    {
        hits[0] = Physics2D.CircleCast(transform.position,objectRange, transform.right, 0f, target[0]);
        hits[1] = Physics2D.CircleCast(transform.position, objectRange, transform.right, 0f, target[1]);
        var deflected = false;
        
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.gameObject == abilityCaster) continue;
            var damageable = hit.collider.gameObject.GetComponent<IDamageable>();
            var pStatus = hit.collider.gameObject.GetComponent<IPlayerStatus>();
            if (pStatus is { IsCountering: true })
            {
                var facingProj = pStatus.IsFacingRight && rb.linearVelocity.x < 0 || !pStatus.IsFacingRight && rb.linearVelocity.x > 0;
                if (facingProj)
                {
                    deflected = true;
                    typeFactor = 1;
                    GameEvents.TriggerPlayerAction("Counter");
                    GameEvents.TriggerPlayerCounter();
                    break;
                }
            }
            Damageable(damageable);
        }
        switch (deflected)
        {
            case false when pierceAmount <= 0:
                Crash();
                break;
            case true:
                Deflect(typeFactor);
                break;
        }
    }
    
    private void Crash()
    {
        collided = true;
        if (explodeOnImpact) RadiusProc();
        if (endAnim != null) animator.Play(endAnim.name);
        rb.linearVelocity = Vector2.zero;
        isDirectional = false;
        if (abilityOnCollide) 
        {
            foreach (var ability in subAbilities)
            {
                var ab = Instantiate(ability.abilityPrefab, transform.position, transform.rotation);
                ability.SetProperties(ab.GetComponent<AbilityBase>());
                ab.GetComponent<AbilityBase>().SetValues(typeFactor, isFacingRightDirection, gameObject);
            }
        }
        if (contactSfx != null) GameEvents.TriggerPlaySound(contactSfx, gameObject);
        Destroy(gameObject, 0.1f);
    }

    private void Deflect(int factor)
    {
        SetValues(factor, !isFacingRightDirection, factor != 1 ? abilityCaster : GameObject.FindGameObjectWithTag("Player"), abilityId);
        projectileSpeed += 5f;
        ApplyForce(isDirectional, !swapForce ? projectileSpeed : projectileHeight, swapForce ? projectileSpeed : projectileHeight);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, objectRange);
    }
}
