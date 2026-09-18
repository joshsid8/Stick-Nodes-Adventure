using System.Collections;
using scripies.Core;
using scripies.Player;
using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    public bool spawnPoint;
    public bool oneTime;
    
    private IEnumerator OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) yield break;
        if (oneTime) GetComponent<Collider2D>().enabled = false;
        yield return new WaitUntil(() => collision.gameObject.scene.name != "Persistent Objects");
        MapManager.Instances[collision.gameObject.scene.name].activeMap = true;
        yield return new WaitForSeconds(0.2f);
        GameEvents.TriggerSpawnPointChanged(gameObject, spawnPoint);
    }
}
