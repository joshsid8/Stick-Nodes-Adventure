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
    public List<GameObject> allEnemies = new();
    public List<GameObject> allItems = new();
    public List<GameObject> allNpcs = new();
    public List<GameObject> allTeledoors = new();
    
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

    private void UpdateLists()
    {
        loaded = false;
        allEnemies = new List<GameObject>(GameObject.FindGameObjectsWithTag("Enemy"));
        allItems = new List<GameObject>(GameObject.FindGameObjectsWithTag("Item"));
        allNpcs = new List<GameObject>(GameObject.FindGameObjectsWithTag("NPC"));
        allTeledoors = new List<GameObject>(GameObject.FindGameObjectsWithTag("TeleDoor"));
        everything.Clear();
        everything.AddRange(allEnemies);
        everything.AddRange(allItems);
        everything.AddRange(allNpcs);
        everything.AddRange(allTeledoors);
        everything.Add(GameObject.FindGameObjectWithTag("Player"));
        loaded = true;
    }

    public GameObject GetEnemyObjByName(string enemyName)
    {
        if (string.IsNullOrEmpty(enemyName)) return null;
        var target = allEnemies.Find(e => e != null && e.GetComponent<Enemy>().enemyName == enemyName);
        if (target != null) return target;
        Debug.LogWarning($"[ObjectDatabase] We couldn't find an enemy with name {enemyName} in our enemy list");
        return null;
    }

    public GameObject GetNpcObjByName(string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return null;
        var target = allNpcs.Find(n => n != null && n.GetComponent<NPC>().npcName == npcName);
        if (target != null) return target;
        Debug.LogWarning($"[ObjectDatabase] We couldn't find an NPC with name {npcName} in our NPC list");
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
        for (var i = 0; i < allTeledoors.Count; i++)
        {
            var door = allTeledoors[i];
            if (door == null || door.IsDestroyed())
            {
                allTeledoors.Remove(door);
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
