using System;
using UnityEngine;

public class EnemyPillar : MonoBehaviour
{
    public float pillarDamage;
    public float pillarLifeTime;
    private float pillarTimer;
    
    public Vector2 knockbackForce;
    public int pillarDamageType;
    bool playerFacingRight;
    
    public float pillarProc;
    [SerializeField] private float pillarProcTimer;
    [SerializeField] private bool isErupting;
    [SerializeField] private bool inContact;
    [SerializeField] private BoxCollider2D pillarCollider;
    
    Animator animator;
    [SerializeField] private AnimationClip eruptAnim;
    [SerializeField] private AnimationClip finishAnim;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    { animator = GetComponent<Animator>();
        if (eruptAnim != null) animator.Play(eruptAnim.name);
    }

    // Update is called once per frame
    void Update()
    {
        playerFacingRight = !PlayerHealth.Instance.pc.isFacingRight;
        Lifetime();
        Hitbox();
        pillarProcTimer -= Time.deltaTime;
    }

    private void Hitbox()
    {
        inContact = Physics2D.CapsuleCast(transform.position, pillarCollider.size, CapsuleDirection2D.Vertical, 0,
            Vector2.zero, 0, LayerMask.GetMask("Player"));
        if (inContact && pillarProcTimer <= 0)
        {
            pillarProcTimer = pillarProc;
            PDamageable damageable = PlayerHealth.Instance.gameObject.GetComponent<PDamageable>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(pillarDamage, knockbackForce * transform.up, playerFacingRight, pillarDamageType);
                damageable.Stun(0f);
                PlayerHealth.Instance.pc.currentCombatTimer = +
                    PlayerHealth.Instance.pc.combatTimer * 0.5f;
            }
        }
    }
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("PVFX"))
            if (collision.GetComponent<PlayerShootBlast>() != null) collision.GetComponent<PlayerShootBlast>().Collide();
    }

    private void Lifetime()
    {
        if (isErupting && pillarTimer > 0)
        {
            pillarTimer -= Time.deltaTime;
            if (pillarTimer <= 0 && finishAnim != null)
            {
                isErupting = false;
                animator.Play(finishAnim.name);
            }
        }
    }

    void Boom()
    {
        Destroy(gameObject);
    }

    void ApplyTimer()
    {
        pillarTimer = pillarLifeTime;
    }
}
