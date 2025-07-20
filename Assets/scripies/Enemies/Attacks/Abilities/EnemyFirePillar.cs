using System;
using System.Collections;
using UnityEngine;

public class EnemyFirePillar : MonoBehaviour
{
    public float fireDamage;
    public float pillarLifeTime;
    public Vector2 knockbackForce;
    public int pillarDamageType;
    [SerializeField] private bool hasErupted;
    private GameObject player;
    bool playerFacingRight;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
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
        playerFacingRight = !player.GetComponent<PlayerController>().isFacingRight;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PDamageable damageable = collision.gameObject.GetComponent<PDamageable>();
            if (damageable != null && hasErupted)
            {
                damageable.Damage(fireDamage, knockbackForce * transform.up, playerFacingRight, pillarDamageType);
                damageable.Stun(0f);
                player.GetComponent<PlayerController>().currentCombatTimer =+
                    player.GetComponent<PlayerController>().combatTimer * 0.5f;
            }
        }
    }

}
