using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
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
    
    private void OnDestroyedItem(GameObject obj)
    {
        if (!gameObject.scene.isLoaded || !isActiveAndEnabled) return;
        if (spawnedObjects.Contains(obj))
            StartCoroutine(CheckItemsStatusDelayed(obj));
    }
    
    private void CheckItemsStatus()
    {
        if (!disableOnCollect || !isActiveAndEnabled) return;
        StartCoroutine(CheckItemsStatusDelayed(null));
    }

    private IEnumerator CheckItemsStatusDelayed(GameObject destroyedObj)
    {
        yield return new WaitForEndOfFrame();
        if (!disableOnCollect || spawnedObjects.Count == 0) yield break;
        var allCollected = spawnedObjects.All(obj => obj == null || obj.IsDestroyed() || obj == destroyedObj);
        if (allCollected && !isOneTime)
        {
            isOneTime = true;
            GameEvents.TriggerPersistGameData(false);
        }
    }

    protected override IEnumerator Spawn()
    {
        scene = gameObject.scene.name;
        if (isOneTime) yield break;
        yield return new WaitForSeconds(spawnInterval);
        if (isOneTime) yield break;
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
        isOneTime = true;
        yield return null;
        foreach (var obj in spawnedObjects)
        {
            if (obj != null && !obj.IsDestroyed()) Destroy(obj);
        }
        spawnedObjects.Clear();
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
