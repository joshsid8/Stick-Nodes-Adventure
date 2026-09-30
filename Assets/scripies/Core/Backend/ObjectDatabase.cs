using System;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Enemy;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
/// <summary>
/// A Database that has all active and current GameObjects in all loaded Scenes
/// </summary>
public class ObjectDatabase : MonoBehaviour
{
    public static ObjectDatabase Instance { get; private set; }
    [SerializeField] private List<string> allScenesList = new();
    [HideInInspector] public List<Scene> allScenes = new();
    public List<GameObject> allItems = new();
    public List<GameObject> allEntities = new();
    public List<GameObject> allTeleDoors = new();
    
    public List<GameObject> everything = new();

    public bool loaded;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start() => InvokeRepeating(nameof(UpdateLists), 0, 0.1f);
    
    private void Update()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!allScenesList.Contains(scene.name)) allScenes.Add(scene);
            else allScenes.Remove(scene);
        }

        foreach (var scene in allScenes.Where(scene => !allScenesList.Contains(scene.name))) //WHERE?!?!!?!??
        {
            allScenesList.Add(scene.name);
        }
    }

    public void ClearAll()
    {
        allItems.Clear();
        allEntities.Clear();
        allTeleDoors.Clear();
        everything.Clear();
    }

    private void UpdateLists()
    {
        loaded = false;
        allItems = new List<GameObject>(GameObject.FindGameObjectsWithTag("Item"));
        allEntities = new List<GameObject>(FindObjectsByType<Identifier>(FindObjectsSortMode.None).Where(entity => entity.typeOfObject == Identifier.ObjectType.Entity).Select(entity => entity.gameObject));
        allTeleDoors = new List<GameObject>(GameObject.FindGameObjectsWithTag("TeleDoor"));
        everything.Clear();
        everything.AddRange(allItems);
        everything.AddRange(allTeleDoors);
        everything.AddRange(allEntities);
        everything.Add(GameObject.FindGameObjectWithTag("Player"));
        loaded = true;
    }

    public GameObject GetEntityObjByName(string entityName, bool looped = false)
    {
        if (string.IsNullOrEmpty(entityName)) return null;
        GameObject target = null;
        foreach (var entity in allEntities)
        {
            if (entity == null || entity.IsDestroyed()) continue;
            if (entity.GetComponent<Identifier>().name != entityName) continue;
            target = entity;
            break;
        }
        
        if (target != null) return target;
        if (!looped) Debug.LogWarning($"[ObjectDatabase] We couldn't find an Entity with name {entityName} in our Entity list");
        return null;
    }

    public GameObject GetItemObjByName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        var target = allItems.Find(i => i != null && i.GetComponent<CollectableSOBase>().name == itemName);
        if (target != null) return target;
        Debug.LogWarning($"[ObjectDatabase] We couldn't find an item with name {itemName} in our item list");
        return null;
    }

    public GameObject GetTeleDoorObjByID(TeleDoor teleDoorId, bool targetDoorId = true)
    {
        if (teleDoorId == null) return null;
        GameObject target = null;
        for (var i = 0; i < allTeleDoors.Count; i++)
        {
            var door = allTeleDoors[i];
            if (door == null || door.IsDestroyed())
            {
                allTeleDoors.Remove(door);
                continue;
            }

            if (door.gameObject.GetComponentInChildren<DoorIdentifier>().teleDoorID != teleDoorId) continue;
            target = door;
            break;
        }

        if (target == null)
        {
            Debug.LogWarning($"[ObjectDatabase] We couldn't find a TeleDoor that contains {teleDoorId} in our TeleDoor list");
            return null;
        }
        if (!targetDoorId) target = target.transform.parent.gameObject;
        return target;

    }
}
