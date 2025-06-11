using UnityEngine;

public class Teleporter : MonoBehaviour
{
    public Transform teleportTarget; // The destination point for the teleportation.  Set this in the Inspector

    public bool doesDamage;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            collision.transform.position = teleportTarget.position;
            if (doesDamage) collision.GetComponent<PlayerHealth>().playerHealth = collision.GetComponent<PlayerHealth>().playerHealth - 20;
        }
        else if (collision.CompareTag("Enemy"))
        {
            collision.transform.position = teleportTarget.position;
            if (doesDamage) collision.GetComponent<EnemyHealth>().enemyHealth = collision.GetComponent<EnemyHealth>().enemyHealth - 20;
        }
    }

}
