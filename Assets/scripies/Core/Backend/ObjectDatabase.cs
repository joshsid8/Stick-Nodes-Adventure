using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    public List<GameObject> allSections = new();
    
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

    public void StopUpdating() => CancelInvoke(nameof(UpdateLists));
    
    private void Update()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!allScenesList.Contains(scene.name))
            {
                if (!allScenes.Contains(scene)) allScenes.Add(scene);
            }
            else allScenes.Remove(scene);
        }

        for (var i = 0; i < allScenes.Count; i++)
        {
            var scene = allScenes[i];
            if (!allScenesList.Contains(scene.name))
            {
                allScenesList.Add(scene.name);
            }
        }
    }

    public void ClearAll()
    {
        allItems.Clear();
        allEntities.Clear();
        allTeleDoors.Clear();
        allSections.Clear();
        everything.Clear();
    }

    private void UpdateLists()
    {
        loaded = false;
        
        allItems = new List<GameObject>(GameObject.FindGameObjectsWithTag("Item"));
        allEntities = new List<GameObject>(FindObjectsByType<Identifier>(FindObjectsSortMode.None).Where(entity => entity.typeOfObject == Identifier.ObjectType.Entity).Select(entity => entity.gameObject));
        allTeleDoors = new List<GameObject>(GameObject.FindGameObjectsWithTag("TeleDoor"));
        allSections = new List<GameObject>(UpdateSectionsList());
        
        everything.Clear();
        everything.AddRange(allItems);
        everything.AddRange(allTeleDoors);
        everything.AddRange(allEntities);
        everything.AddRange(allSections);
        
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) everything.Add(playerObj);
        loaded = true;
    }

    private static List<GameObject> UpdateSectionsList()
    {
        var sections =  new List<GameObject>();
        var sectionParents = GameObject.FindGameObjectsWithTag("Section");
        
        foreach (var sect in sectionParents) 
            sections.AddRange(sect.GetComponentsInChildren<GameObject>());
        
        return sections;
    }

    public GameObject GetAnythingByName(string entityName)
    {
        if (string.IsNullOrEmpty(entityName)) return null;
        var target = allEntities.Find(i => i != null && i.gameObject.name == entityName);
        if (target != null) return target;
        Debug.LogWarning($"[ObjectDatabase] Object with name {entityName} not found in ANY of our lists!");
        return null;
    }

    public GameObject GetEntityObjByName(string entityName, bool looped = false)
    {
        if (string.IsNullOrEmpty(entityName)) return null;
        GameObject target = null;
        for (var i = 0; i < allEntities.Count; i++)
        {
            var entity = allEntities[i];
            if (entity == null || entity.IsDestroyed()) continue;
            if (entity.gameObject.name != entityName) continue;
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
        var target = allItems.Find(i => i != null && i.gameObject.name == itemName);
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
                allTeleDoors.RemoveAt(i);
                i--;
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

    public GameObject GetSectionObjByName(string sectionName)
    {
        if (string.IsNullOrEmpty(sectionName)) return null;
        var target = allSections.Find(i => i != null && i.gameObject.name == sectionName);
        if (target != null) return target;
        Debug.LogWarning($"[ObjectDatabase] We couldn't find an item with name {sectionName} in our section list");
        return null;
    }
}
