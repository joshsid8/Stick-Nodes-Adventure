using scripies.Player;
using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    public bool spawnPoint;
    public bool oneTime;
    
    public SceneField spawnScene;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            if (!spawnPoint) PlayerHealth.Instance.pc.currentCheckPoint = gameObject;
            else PlayerHealth.Instance.pc.currentSpawnPoint = gameObject;
            if (oneTime) GetComponent<Collider2D>().enabled = false;
        }
    }
}
