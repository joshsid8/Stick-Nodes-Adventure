using System;
using System.Collections.Generic;
using scripies.Dialogue;
using UnityEngine;

public class DataManager : MonoBehaviour
{
    public static DataManager Instance { get; private set; }
    public GameObject mainCamera;
    [Header("Scene Data")]
    public List<SceneList> sceneData = new();
    [Header("NPC Data")]
    public List<NpcData> npcs = new();
    
    [Serializable]
    public class SceneList
    {
        public string sceneName;
        public List<EnemySceneData> enemies = new();
        public List<CollectableSceneData> collectables = new();
        
        public List<DoorSceneData> doors = new();
        public List<SpawnerSceneData> spawners = new();

        public GameObject ItemToStore => GameObject.Find("persistentItem");

        public string activeBoss;
        public bool isBossDead;
        public bool willBeNpc;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        scripies.Core.GameEvents.SaveGame += SaveDisk;
    }

    private void OnDestroy()
    {
        if (Instance == this) scripies.Core.GameEvents.SaveGame -= SaveDisk;
    }

    private static void SaveDisk() => SaveSystem.Save();

    #region Scene data classes

    [Serializable]
    public class EnemySceneData
    {
        public int activeEnemyID;
        public bool isOneTime;
    }

    [Serializable]
    public class NpcData
    {
        public string activeNpcName;
        public bool willTravel;
        public DialogueObject[] dialogue;
        public ShopItem[] shopItems;
    }

    [Serializable]
    public class CollectableSceneData
    {
        public CollectableSOBase activeCollectable;
        public Color colour = Color.white;
        public Vector3 position;
        public bool isStray;
    }

    [Serializable]
    public class DoorSceneData
    {
        public int activeDoor;
        public bool isOpen;
    }

    [Serializable]
    public class SpawnerSceneData
    {
        public int activeSpawner;
        public bool isOneTime;
    }

    #endregion

    public void Save(ref SceneData data) => data.realSceneData = new List<SceneList>(sceneData);
    public void Load(SceneData data) => sceneData = new List<SceneList>(data.realSceneData);
}

[Serializable]
public struct SceneData
{
    public List<DataManager.SceneList> realSceneData;

    public SceneData(bool initialize)
    {
        realSceneData = new List<DataManager.SceneList>();
    }
}
