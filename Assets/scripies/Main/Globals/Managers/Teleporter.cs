using scripies.Player;
using UnityEngine;

public class Teleporter : MonoBehaviour
{
    public Transform teleportTarget;

    public bool doesDamage;
    public float damage = 20;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PlayerHealth.Instance.StartCoroutine(PlayerHealth.Instance.Spawn(0, false));
            if (doesDamage) collision.GetComponent<PlayerHealth>().playerHealth -= damage;
        }
        else if (collision.CompareTag("Enemy"))
        {
            if (doesDamage) collision.GetComponent<EnemyHealth>().enemyHealth -= damage * 9001;
        }
    }

}
