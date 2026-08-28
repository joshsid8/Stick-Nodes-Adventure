using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using scripies.Core;
using Unity.VisualScripting;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class ItemSpawner : MonoBehaviour
{
    public bool isOneTime;
    [SerializeField] private bool disableOnCollect;
    [SerializeField] private ItemSpawn[] itemSpawner;
    private string scene;
    [SerializeField] private GameObject[] spawnPoints;
    public List<GameObject> spawnedObjects = new List<GameObject>();
    
    [SerializeField] private bool spawnOnTrigger;
    public TriggerTag triggerTag;
    public enum TriggerTag {Player, Enemy, Boss, Npc}
    [SerializeField] private float spawnInterval;

    private void OnEnable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.RegisterSpawner(gameObject);
    }

    private void OnDisable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.UnregisterSpawner(gameObject);
    }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()  {
        StartCoroutine(Spawn());
    }

    private void Update()
    {
        foreach (var obj in spawnedObjects)
        {
            if (obj.IsDestroyed() && disableOnCollect) isOneTime = true;
        }
    }

    private IEnumerator Spawn()
    {
        scene = gameObject.scene.name;
        if (!isOneTime)
        {
            yield return new WaitForSeconds(spawnInterval);
            foreach (var spawn in itemSpawner)
            {
                var instance = Instantiate(spawn.objectToSpawn,
                    spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position, Quaternion.identity);
                
                if (spawn.collectable != null)
                {
                    instance.GetComponent<Collectable>().collectable = spawn.collectable;
                    instance.GetComponent<Collectable>().isStray = false;
                }
                
                var targetScene = SceneManager.GetSceneByName(scene);
                if (targetScene.IsValid() && targetScene.isLoaded)
                {
                    SceneManager.MoveGameObjectToScene(instance, targetScene);
                    instance.GetComponent<scripies.Core.Identifier>().AutomateID(targetScene);
                    instance.GetComponent<scripies.Core.Identifier>().typeOfObject = Identifier.ObjectType.Item;
                    spawnedObjects.Add(instance);
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag(triggerTag.ToString()) && spawnOnTrigger) 
            StartCoroutine(Spawn());
    }

    public void Despawn()
    {
        foreach (var obj in spawnedObjects) Destroy(obj);
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