using System;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class ItemSpawner : MonoBehaviour
{
    [SerializeField] private ItemSpawn[] iSpawn;
    [SerializeField] private SceneField scene;

    public bool spawnAbility = true;
    
    public GameObject[] spawnPoints;
    public float spawnTime;
    public bool spawnOnStart = true;
    public bool canSpawn = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start() => Spawn();

    private void Spawn()
    {
        if (canSpawn && spawnAbility)
        {
            foreach (var spawn in iSpawn)
            {
                var instance = Instantiate(spawn.objectToSpawn,
                    spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position, Quaternion.identity);
                var targetScene = SceneManager.GetSceneByName(scene.SceneName);
                if (targetScene.IsValid() && targetScene.isLoaded)
                {
                    SceneManager.MoveGameObjectToScene(instance, targetScene);
                    instance.GetComponent<Identifier>().AutomateID(targetScene);
                }
            }
        }
    }

    public void Despawn()
    {
        foreach (var spawn in iSpawn) Destroy(spawn.objectToSpawn);
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy")) canSpawn = false;
    }
    
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("EDead") || collision.CompareTag("Enemy") && isActiveAndEnabled)
        {
            canSpawn = true;
            Spawn();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        foreach (var spawn in spawnPoints)
            Gizmos.DrawWireSphere(spawn.transform.position, 1f);
    }

    [Serializable]
    private class ItemSpawn
    {
        public GameObject objectToSpawn;
        public bool oneTime;
    }
}