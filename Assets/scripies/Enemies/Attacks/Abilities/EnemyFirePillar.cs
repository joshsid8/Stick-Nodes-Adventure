using System;
using System.Collections;
using UnityEngine;

public class EnemyFirePillar : MonoBehaviour
{
    public float fireDamage;
    public float pillarLifeTime;
    public Vector2 knockbackForce;
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

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PDamageable damageable = collision.gameObject.GetComponent<PDamageable>();
            if (damageable != null && hasErupted)
            {
                damageable.Damage(fireDamage, knockbackForce * transform.up);
                damageable.Stun(0f);
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                player.GetComponent<PlayerController>().currentCombatTimer =+
                    player.GetComponent<PlayerController>().combatTimer * 0.5f;
            }
        }
    }

}
