using scripies.Enemy;
using scripies.Player;
using UnityEngine;

public class Teleporter : MonoBehaviour
{
    public Transform teleportTarget;

    public bool doesDamage;
    public float damage = 20;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (collision.tag)
        {
            case "Player":
            {
                PlayerHealth.Instance.StartCoroutine(PlayerHealth.Instance.Spawn(0, false));
                if (doesDamage) collision.GetComponent<PlayerHealth>().playerHealth -= damage;
                break;
            }
            case "Enemy":
            case "Boss":
            {
                collision.transform.position = teleportTarget.position;
                if (doesDamage)
                {
                    var e = collision.GetComponent<EnemyHealth>();
                    var b = collision.GetComponent<BossHealth>();
                    if (e != null) collision.GetComponent<EnemyHealth>().enemyHealth -= damage;
                    if (b != null) collision.GetComponent<BossHealth>().bossHealth -= damage;
                }
                break;
            }
            case "Terrain": case "Door": case "Untagged": case "Spawner": case "MainCamera": case "NPC": case "Section": case "camera":
                break;
            default:
                collision.transform.position = teleportTarget.position;
                break;
            
        }
    }

}
