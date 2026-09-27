using System;
using System.Collections;
using scripies.Core;
using scripies.Player;
using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    public bool isCurrent;
    public bool spawnPoint;
    public bool oneTime;

    private void Awake() => GetComponent<Collider2D>().enabled = false;
    
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.5f);
        GetComponent<Collider2D>().enabled = true;
    }
    
    private IEnumerator OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player") || PlayerHealth.Instance.justSpawned) yield break;
        
        PlayerHealth.Instance.pc.currentCheckPoint = gameObject;
        if (spawnPoint) PlayerHealth.Instance.pc.currentSpawnPoint = gameObject;
        yield return new WaitUntil(() => collision.gameObject.scene.name != "Persistent Objects");
        if (collision.gameObject.scene.name != "Persistent Objects") MapManager.Instances[collision.gameObject.scene.name].activeMap = true;
        yield return new WaitForSeconds(0.2f);
        GameEvents.TriggerSpawnPointChanged(gameObject, spawnPoint);
        if (oneTime) GetComponent<Collider2D>().enabled = false;
    }
}
