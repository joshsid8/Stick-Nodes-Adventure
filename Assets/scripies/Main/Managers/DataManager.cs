using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Dialogue;
using UnityEngine;
using scripies.Core;
using UnityEngine.SceneManagement;

public class DataManager : MonoBehaviour
{
    public static DataManager Instance { get; private set; }
    public GameObject mainCamera;
    [Header("Scene Data")]
    public List<SceneList> sceneData = new();
    [Header("NPC Data")]
    public List<NpcData> npcs = new();

    public bool npcDataLoaded;
    
    [Serializable]
    public class SceneList
    {
        public string sceneName;
        public bool activeMap;
        public WeatherManager.WeatherType currentWeatherType;
        public float currentWeatherDuration;
        public List<EnemySceneData> enemies = new();
        public List<CollectableSceneData> collectables = new();
        
        public List<DoorSceneData> doors = new();
        public List<SpawnerSceneData> spawners = new();
        public List<DoorSpawnerHybridSceneData> doorSpawnerHybrids = new();
        public List<CheckPointSceneData> checkPoints = new();
        public bool undiscoveredMap;
        public GameObject ItemToStore => GameObject.Find("persistentItem");

        public string activeBoss;
        public bool isBossDead;
        public bool willBeNpc;
    }

    public string GetActiveSceneName()
    {
        var activeMap = sceneData.Find(map => map.activeMap);
        return activeMap?.sceneName;
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
        GameEvents.SaveNpcData += SaveNpcData;
        GameEvents.LoadNpcData += LoadNpcData;
        GameEvents.SaveGameToDisk += SaveDisk;
        GameEvents.PersistGameData += PersistData;
        GameEvents.OnEnterMap += StuffOnMapEnter;
    }
    
    private void OnDestroy()
    {
        if (Instance != this) return;
        GameEvents.SaveNpcData -= SaveNpcData;
        GameEvents.SaveGameToDisk -= SaveDisk;
        GameEvents.PersistGameData -= PersistData;
        GameEvents.LoadNpcData -= LoadNpcData;
        GameEvents.OnEnterMap -= StuffOnMapEnter;
    }
    
    private void SaveNpcData() => StartCoroutine(SaveNpcDataC());
    private IEnumerator SaveNpcDataC()
    {
        if (npcs.Count > 0)
        {
            yield return new WaitUntil(() => npcDataLoaded);
            yield return new WaitForSecondsRealtime(0.1f);
        }
        foreach (var map in MapManager.Instances.Values)
        {
            foreach (var nData in map.npcSceneData)
            {
                if (nData.activeNpc == null || string.IsNullOrEmpty(nData.npcName)) continue;
                var npc = npcs.Find(x => x.activeNpcName.Contains(nData.npcName));
                if (npc == null)
                {
                    npcs.Add(new NpcData
                    {
                        activeNpcName = nData.npcName,
                        willTravel = nData.willTravel,
                        dialogue = nData.dialogue.ToArray(),
                        spokenDialogue = nData.spokenDialogue.ToArray(),
                        shopItems = nData.shopItems
                    });
                }
                else
                {
                    npc.willTravel = nData.willTravel;
                    npc.dialogue = nData.dialogue.ToArray();
                    npc.spokenDialogue = nData.spokenDialogue.ToArray();
                    npc.shopItems = nData.shopItems;
                }
            }
        }
    }

    private void StuffOnMapEnter(string mapName, bool firstTime)
    {
        Invoke(nameof(LoadNpcData), 0.1f);
    }
    
    private void LoadNpcData()
    {
        npcDataLoaded = false;
        // That's a whole sentence bro
        foreach (var npcData in from npc in npcs
                 from map in MapManager.Instances.Values
                 from npcData in map.npcSceneData.Where(x => x.npcName == npc.activeNpcName)
                 select npcData)
        {
            var dataNpc = npcs.Find(x => x.activeNpcName.Contains(npcData.npcName));
            if (npcData.activeNpc.TryGetComponent<NPChat>(out var dTrigger))
            {
                dTrigger.dialogue.Clear();
                dTrigger.dialogue.AddRange(dataNpc.dialogue);
            }
            
            if (!npcData.activeNpc.TryGetComponent<ShopKeeper>(out var shopKeeper)) continue;
            shopKeeper.shopItems.Clear();
            shopKeeper.shopItems.AddRange(dataNpc.shopItems);
        }
        npcDataLoaded = true;
    }
    
    private static void SaveDisk() => SaveSystem.Save();
    private void PersistData(bool _) => StartCoroutine(PersistDataC());
    private static IEnumerator PersistDataC()
    {
        for (var i = 0; i < MapManager.Instances.Count; i++)
        {
            var map = MapManager.Instances.Values.ElementAt(i);
            map.SaveSceneData();
            // yield return new WaitUntil(() => map.sceneLoaded);
            // if (map.npcDataInitialised) 
            // print($"Saving NPC data for {map.sceneName}");
            // map.SaveNpcData();
        }
        yield return null;
    }

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
        public DialogueObject[] spokenDialogue;
        public ShopKeeperItem[] shopItems;
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
    
    [Serializable]
    public class DoorSpawnerHybridSceneData
    {
        public int activeDsh;
        public bool isOpen;
        public bool isOneTime;
    }

    [Serializable]
    public class CheckPointSceneData
    {
        public int activeCheckpoint;
        public bool isCurrent;
        public bool activated;
    }
    #endregion

    public void Save(ref SceneData data, ref GlobalNpcData npcData)
    {
        data.realSceneData = new List<SceneList>(sceneData);
        npcData.realNpcData = new List<NpcData>(npcs);
    }
    public void Load(SceneData data, GlobalNpcData npcData)
    {
        sceneData = new List<SceneList>(data.realSceneData);
        npcs = new List<NpcData>(npcData.realNpcData);
    }
    
    public IEnumerator SpawnPlayerToActiveMap()
    {
        var activeSceneName = GetActiveSceneName();
        if (string.IsNullOrEmpty(activeSceneName)) yield break;
        
        yield return new WaitUntil(() => MapManager.Instances.ContainsKey(activeSceneName));
        
        var map = MapManager.Instances[activeSceneName];
        map.InitialSpawnPlayer(activeSceneName);
    }
}

[Serializable]
public struct SceneData
{
    public List<DataManager.SceneList> realSceneData;
    public SceneData(bool initialize) => realSceneData = new List<DataManager.SceneList>();
}

[Serializable]
public struct GlobalNpcData
{
    public List<DataManager.NpcData> realNpcData;
    public GlobalNpcData(bool initialize) => realNpcData = new List<DataManager.NpcData>();
}