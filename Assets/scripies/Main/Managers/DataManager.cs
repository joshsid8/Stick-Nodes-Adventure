using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Dialogue;
using UnityEngine;
using scripies.Core;
using scripies.Player;
using Unity.VisualScripting;
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
        GameEvents.OnQuitToMainMenu += Cleanup;
    }
    
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GameEvents.SaveNpcData -= SaveNpcData;
        GameEvents.SaveGameToDisk -= SaveDisk;
        GameEvents.PersistGameData -= PersistData;
        GameEvents.LoadNpcData -= LoadNpcData;
        GameEvents.OnEnterMap -= StuffOnMapEnter;
        GameEvents.OnQuitToMainMenu -= Cleanup;
    }
    
    private void SaveNpcData() => StartCoroutine(SaveNpcDataC());
    private IEnumerator SaveNpcDataC()
    {
        yield return new WaitForSecondsRealtime(0.05f);
        var maps = MapManager.Instances.Values.ToArray();
        for (var m = 0; m < maps.Length; m++)
        {
            var map = maps[m];
            if (map == null || !map.sceneLoaded) continue;
            map.UpdateLists();
            for (var n = 0; n < map.npcSceneData.Count; n++)
            {
                var nData = map.npcSceneData[n];
                if (nData.activeNpc == null || string.IsNullOrEmpty(nData.npcName)) continue;
                var npc = npcs.Find(x =>
                    x.activeNpcName == nData.npcName &&
                    (nData.willTravel || string.IsNullOrEmpty(x.sceneName) || x.sceneName == map.sceneName));

                if (npc == null)
                {
                    npcs.Add(new NpcData
                    {
                        activeNpcName = nData.npcName,
                        sceneName = map.sceneName,
                        willTravel = nData.willTravel,
                        dialogue = nData.dialogue != null ? nData.dialogue.ToArray() : Array.Empty<DialogueObject>(),
                        spokenDialogue = nData.spokenDialogue != null ? nData.spokenDialogue.ToArray() : Array.Empty<DialogueObject>(),
                        shopItems = nData.shopItems
                    });
                }
                else
                {
                    npc.sceneName = map.sceneName;
                    npc.willTravel = nData.willTravel;
                    npc.dialogue = nData.dialogue != null ? nData.dialogue.ToArray() : Array.Empty<DialogueObject>();
                    npc.spokenDialogue = nData.spokenDialogue != null ? nData.spokenDialogue.ToArray() : Array.Empty<DialogueObject>();
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
        var maps = MapManager.Instances.Values.ToArray();
        for (var m = 0; m < maps.Length; m++)
        {
            var map = maps[m];
            if (map == null) continue;
            map.UpdateLists();
            for (var n = 0; n < map.npcSceneData.Count; n++)
            {
                var npcData = map.npcSceneData[n];
                if (npcData.activeNpc == null || npcData.activeNpc.IsDestroyed()) continue;
                var dataNpc = npcs.Find(x =>
                    x.activeNpcName == npcData.npcName &&
                    (npcData.willTravel || string.IsNullOrEmpty(x.sceneName) || x.sceneName == map.sceneName));
                if (dataNpc == null) continue;

                if (npcData.activeNpc.TryGetComponent<NPChat>(out var dTrigger))
                {
                    if (dataNpc.dialogue != null)
                    {
                        dTrigger.dialogue.Clear();
                        dTrigger.dialogue.AddRange(dataNpc.dialogue);
                    }
                    if (dataNpc.spokenDialogue != null)
                    {
                        dTrigger.spokenDialogue.Clear();
                        dTrigger.spokenDialogue.AddRange(dataNpc.spokenDialogue);
                    }
                }
                
                if (npcData.activeNpc.TryGetComponent<ShopKeeper>(out var shopKeeper) && dataNpc.shopItems != null)
                {
                    shopKeeper.shopItems.Clear();
                    shopKeeper.shopItems.AddRange(dataNpc.shopItems);
                }
            }
        }
        npcDataLoaded = true;
    }
    
    private static void SaveDisk() => SaveSystem.Save();
    private void PersistData(bool _) => StartCoroutine(PersistDataC());
    private static IEnumerator PersistDataC()
    {
        var maps = MapManager.Instances.Values.ToArray();
        for (var i = 0; i < maps.Length; i++)
        {
            var map = maps[i];
            if (map != null) map.SaveSceneData();
        }
        yield return null;
    }

    private void Cleanup() => StartCoroutine(CleanupC());
    private IEnumerator CleanupC()
    {
        SaveDisk();
        yield return new WaitUntil(() => !SaveSystem.isSaving);
        if (ObjectDatabase.Instance != null)
        {
            ObjectDatabase.Instance.StopUpdating();
            var thingsToDestroy = ObjectDatabase.Instance.everything
                .Where(x => x != null && x.name != "Player")
                .ToArray();
            for (var i = 0; i < thingsToDestroy.Length; i++)
            {
                var thing = thingsToDestroy[i];
                if (thing == null || thing.IsDestroyed()) continue;
                // DESTROY EVERYTHING!!!
                Destroy(thing);
                yield return new WaitUntil(() => thing == null || thing.IsDestroyed());
            }
        }
        if (PlayerHealth.Instance != null) Destroy(PlayerHealth.Instance.gameObject);
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
        public string sceneName;
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
        sceneData = data.realSceneData != null ? new List<SceneList>(data.realSceneData) : new List<SceneList>();
        npcs = npcData.realNpcData != null ? new List<NpcData>(npcData.realNpcData) : new List<NpcData>();
    }
    
    public IEnumerator SpawnPlayerToActiveMap()
    {
        var activeSceneName = GetActiveSceneName();
        if (string.IsNullOrEmpty(activeSceneName))
        {
            var fallbackMap = MapManager.Instances.Values.FirstOrDefault(m => m != null && m.activeMap)
                              ?? MapManager.Instances.Values.FirstOrDefault(m => m != null);
            activeSceneName = fallbackMap != null ? fallbackMap.sceneName : null;
        }
        if (string.IsNullOrEmpty(activeSceneName)) yield break;
        
        yield return new WaitUntil(() => MapManager.Instances.ContainsKey(activeSceneName));
        
        var map = MapManager.Instances[activeSceneName];
        map.activeMap = true;
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
