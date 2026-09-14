using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using scripies.Core;
using Unity.VisualScripting;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class ItemSpawner : SpawnerBase
{
    [SerializeField] private bool disableOnCollect;
    [SerializeField] private ItemSpawn[] itemSpawner;
    private string scene;
    [SerializeField] private bool randomiseSpawn = true;
    [SerializeField] private GameObject[] spawnPoints;
    public List<GameObject> spawnedObjects = new();
    
    [SerializeField] private bool spawnOnTrigger;
    [EnumButtons] public TriggerTag triggerTag;
    public enum TriggerTag {Player, Enemy, Boss, Npc}
    [SerializeField] private float spawnInterval;

    private void OnEnable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.RegisterSpawner(gameObject);
        GameEvents.OnCollect += CheckItemsStatus;
        GameEvents.OnObjectDestroyed += OnDestroyedItem;
    }

    private void OnDisable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.UnregisterSpawner(gameObject);
        GameEvents.OnCollect -= CheckItemsStatus;
        GameEvents.OnObjectDestroyed -= OnDestroyedItem;
    }
    
    private void Start()
    {
        if (!isOneTime) StraySpawn();
    }
    
    [ContextMenu("Spawn")] private void StraySpawn() => StartCoroutine(Spawn());
    
    private void OnDestroyedItem(GameObject obj) => CheckItemsStatus();
    
    private void CheckItemsStatus()
    {
        if (!disableOnCollect) return;
        var allCollected = false;
        foreach (var obj in spawnedObjects)
        {
            if (!obj.IsDestroyed()) continue;
            allCollected = true;
        }
        if (allCollected) isOneTime = true;
    }
    protected override IEnumerator Spawn()
    {
        scene = gameObject.scene.name;
        if (isOneTime) yield break;
        yield return new WaitForSeconds(spawnInterval);
        foreach (var spawn in itemSpawner)
        {
            var spawnChoice = randomiseSpawn ? Random.Range(0, spawnPoints.Length) : Array.IndexOf(itemSpawner, spawn);
            if (spawnChoice == -1) spawnChoice = 0; // Apparently, IndexOf returns -1 if the index isn't found. good to know!
            
            var instance = Instantiate(spawn.objectToSpawn,
                spawnPoints[spawnChoice].transform.position, Quaternion.identity);
            
            if (spawn.collectable != null)
            {
                instance.GetComponent<Collectable>().collectable = spawn.collectable;
                instance.GetComponent<Collectable>().isStray = false;
            }
            
            var targetScene = SceneManager.GetSceneByName(scene);
            if (!targetScene.IsValid() || !targetScene.isLoaded) continue;
            instance.GetComponent<CollectableTrigger>().SetProperties(targetScene);
            spawnedObjects.Add(instance);
        }
        GameEvents.TriggerObjectSpawn();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag(triggerTag.ToString()) && spawnOnTrigger) 
            StartCoroutine(Spawn());
    }

    public override IEnumerator Despawn()
    {
        yield return new WaitUntil(() => spawnedObjects.Count == 0);
        foreach (var obj in spawnedObjects) Destroy(obj);
        CheckItemsStatus();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        foreach (var spawn in spawnPoints) Gizmos.DrawWireSphere(spawn.transform.position, 1f);
    }

    [Serializable]
    private class ItemSpawn
    {
        public GameObject objectToSpawn;
        public CollectableSOBase collectable;
    }
}