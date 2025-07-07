using System;
using System.Collections;
using UnityEngine;

public class PlayerFirePillar : MonoBehaviour
{
    private float fireDamage;
    public Vector2 pillarKnockbackForce;
    private float pillarLifeTime;
    [SerializeField] private bool hasErupted;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        hasErupted = true;
        yield return new WaitForSeconds(pillarLifeTime);
        hasErupted = false;
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    // Update is called once per frame
    void Update()
    {
        GetComponent<Animator>().SetBool("hasErupted", hasErupted);
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
            if (damageable != null && hasErupted)
            {
                damageable.Damage(fireDamage, pillarKnockbackForce);
                damageable.Stun(0f);
            }
        }
    }

}
