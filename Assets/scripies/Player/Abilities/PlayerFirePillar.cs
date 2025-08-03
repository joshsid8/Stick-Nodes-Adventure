using System;
using System.Collections;
using UnityEngine;

public class PlayerFirePillar : MonoBehaviour
{
    private float fireDamage;
    public Vector2 pillarKnockbackForce;
    private float pillarLifeTime;
    private float pillarTimer;
    [SerializeField] private bool isErupting = true;
    Animator animator;
    [SerializeField] private AnimationClip eruptAnim;
    [SerializeField] private AnimationClip finishAnim;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        if (eruptAnim != null) animator.Play(eruptAnim.name);
        pillarTimer = pillarLifeTime;
    }

    private void Update()
    {
        Lifetime();
    }

    public void SetValues(float lifeTime, float damage, Vector2 knockbackForce)
    {
        pillarLifeTime = lifeTime;
        fireDamage = damage;
        pillarKnockbackForce = knockbackForce;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy") || collision.CompareTag("Boss"))
        {
            EDamageables damageable = collision.gameObject.GetComponent<EDamageables>();
            if (damageable != null && isErupting)
            {
                damageable.Damage(fireDamage, pillarKnockbackForce);
                damageable.Stun(0f);
            }
        }
    }

    private void Lifetime()
    {
        pillarTimer -= Time.deltaTime;
        if (pillarTimer <= 0 && finishAnim != null) animator.Play(finishAnim.name);
    }
    
    void Boom()
    {
        Destroy(gameObject);
    }
}
