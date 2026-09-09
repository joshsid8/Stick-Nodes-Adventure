using System;
using System.Collections.Generic;
using scripies.Core;
using scripies.Enemy;
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        InvokeRepeating(nameof(UpdateLists), 0, 0.1f);
    }

    // Update is called once per frame
    private void Update()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!allScenesList.Contains(scene.name)) allScenes.Add(scene);
            else allScenes.Remove(scene);
        }
        foreach (var scene in allScenes)
        {
            if (!allScenesList.Contains(scene.name))
                allScenesList.Add(scene.name);
        }
    }

    private void UpdateLists()
    {
        allEnemies = new List<GameObject>(GameObject.FindGameObjectsWithTag("Enemy"));
        allItems = new List<GameObject>(GameObject.FindGameObjectsWithTag("Item"));
        allNpcs = new List<GameObject>(GameObject.FindGameObjectsWithTag("NPC"));
        allTeledoors = new List<GameObject>(GameObject.FindGameObjectsWithTag("TeleDoor"));
    }

    public GameObject GetEnemyByName(string enemyName)
    {
        var target = allEnemies.Find(e => e.gameObject.GetComponent<Enemy>().enemyName == enemyName);
        if (target != null) return target;
        Debug.LogError($"[ObjectDatabase] We couldn't find an enemy with name {enemyName} in our enemy list");
        return null;
    }

    public GameObject GetNpcByName(string npcName)
    {
        var target = allNpcs.Find(n => n.gameObject.GetComponent<NPC>().npcName == npcName);
        if (target != null) return target;
        Debug.LogError($"[ObjectDatabase] We couldn't find an enemy with name {npcName} in our NPC list");
        return null;
    }

    public GameObject GetItemByName(string itemName)
    {
        var target = allItems.Find(i => i.gameObject.GetComponent<CollectableSOBase>().name == itemName);
        if (target != null) return target;
        Debug.LogError($"[ObjectDatabase] We couldn't find an item with name {itemName} in our item list");
        return null;
    }

    public GameObject GetTeleDoorByName(string teleDoorName)
    {
        var target = allTeledoors.Find(t => t.gameObject.GetComponentInChildren<DoorIdentifier>().doorNameID.Contains(teleDoorName));
        if (target != null) return target;
        Debug.LogError($"[ObjectDatabase] We couldn't find a TeleDoor that contains {teleDoorName} in our TeleDoor list");
        return null;
    }
}
