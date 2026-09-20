using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using scripies.Enemy;
using UnityEngine.SceneManagement;
using scripies.Core;
using Random = UnityEngine.Random;

public class EntitySpawner : SpawnerBase
{
    [SerializeField] private List<GameObject> currentEnemies = new();
    [SerializeField] private EnemySpawn[] eSpawn;
    private string scene;

    public bool spawnAbility = true;
    
    public GameObject[] spawnPoints;
    [SerializeField] private float respawnTime = 20;
    [SerializeField] private float spawnTime = 10;
    public bool spawnOnStart = true;
    public bool canSpawn = true;

    [SerializeField] private bool playerIsClose;

    private void OnEnable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.RegisterSpawner(gameObject);
        
        GetComponent<Collider2D>().enabled = true;
    }

    private void OnDisable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.UnregisterSpawner(gameObject);
        
        GetComponent<Collider2D>().enabled = false;
    }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        scene = gameObject.scene.name;
        if (!canSpawn || !spawnAbility || isOneTime) yield break;
        foreach (var spawn in eSpawn)
        {
            var prevPoint = 0;
            for (var i = 0; i < spawn.spawnAmount; i++)
            {
                if (!spawnOnStart) continue;
                var rng = 0;
                while (rng == prevPoint)
                {
                    if (spawnPoints.Length == 1) break;
                    rng = Random.Range(0, spawnPoints.Length);
                    yield return null;
                }
                if (i == 0) rng = 0;
                prevPoint = rng;
                var instance = Instantiate(spawn.objectToSpawn, spawnPoints[rng].transform.position, Quaternion.identity);
                var targetScene = SceneManager.GetSceneByName(scene);
                if (!targetScene.IsValid() || !targetScene.isLoaded) continue;
                SceneManager.MoveGameObjectToScene(instance, targetScene);
                instance.GetComponent<Identifier>().AutomateID(targetScene);
                if (spawn.settings != null) instance.GetComponent<EnemyController>().enemy = spawn.settings;
                currentEnemies.Add(instance);
            }
        }
    }

    protected override IEnumerator Spawn()
    {
        if (!canSpawn || !spawnAbility) yield break;
        foreach (var spawn in eSpawn)
        {
            var prevPoint = 0;
            for (var i = 0; i < spawn.spawnAmount; i++)
            {
                if (currentEnemies.Count >= spawn.spawnAmount || playerIsClose) break;
                var rng = 0;
                while (rng == prevPoint)
                {
                    rng = Random.Range(0, spawnPoints.Length);
                    yield return null;
                }
                prevPoint = rng;
                var instance = Instantiate(spawn.objectToSpawn, spawnPoints[rng].transform.position, Quaternion.identity);
                var targetScene = SceneManager.GetSceneByName(scene);
                if (targetScene.IsValid() && targetScene.isLoaded)
                {
                    SceneManager.MoveGameObjectToScene(instance, targetScene);
                    instance.GetComponent<Identifier>().AutomateID(targetScene);
                    if (spawn.settings != null) instance.GetComponent<EnemyController>().enemy = spawn.settings;
                    currentEnemies.Add(instance);
                }
                yield return new WaitForSeconds(spawnTime);
            }
        }
    }

    public override IEnumerator Despawn()
    {
        yield return new WaitUntil(() => currentEnemies.Count == 0);
        foreach (var obj in currentEnemies) Destroy(obj);
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy"))
        {
            if (!currentEnemies.Contains(collision.gameObject))
                currentEnemies.Add(collision.gameObject);
        }
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player")) playerIsClose = true;
    }
    
    private IEnumerator OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Player")) playerIsClose = false;
        if (collision.gameObject.layer != LayerMask.NameToLayer("EDead") && (!collision.CompareTag("Enemy") || !isActiveAndEnabled)) yield break;
        canSpawn = true;
        currentEnemies.Remove(collision.gameObject);
        yield return new WaitForSeconds(respawnTime);
        StartCoroutine(Spawn());
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        foreach (var spawn in spawnPoints) 
            Gizmos.DrawWireSphere(spawn.transform.position, 1f);
    }

    private void OnDestroy() => currentEnemies.Clear();
    
    [Serializable]
    private class EnemySpawn
    {
        public GameObject objectToSpawn;
        public float spawnAmount;
        public Enemy settings;
    }
}