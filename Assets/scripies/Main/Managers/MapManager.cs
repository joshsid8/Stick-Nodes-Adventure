using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Dialogue;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;
using scripies.Player;
using Unity.VisualScripting;

public class MapManager : MonoBehaviour
{
    public static readonly Dictionary<string, MapManager> Instances = new();
    
    #region Map Info and Ost

    public bool activeMap;
    public bool mapToggle;
    [SerializeField] private string mapName;
    [SerializeField] [TextArea] private string mapDescription;
    [SerializeField] private Color mapTextColor = Color.white;
    [SerializeField] private Color mapBgColor = Color.white;
    
    [SerializeField] private AudioClip[] newOst;
    public float[] ostVolume = {0.5f,0.5f,0.5f};
    
    public string[] mapDisasterNames = { "Temperature", "Earthquake", "Wind", "Thunderstorm" };
    public float[] mapDisasterFactors = new float[4];
    
    [SerializeField] private GameObject sectionObj;
    public GameObject mainSpawnPoint;

    public bool undiscoveredMap = true;
    
    private WeatherManager weather;
    private AudioManager audioManager;
    #endregion

    #region Scene Data
    [HideInInspector] public string sceneName; 
    public GameObject[] sceneObjects;
    [SerializeField] private List<GameObject> doorObjects = new();
    [SerializeField] private List<GameObject> spawnerObjects = new();
    [SerializeField] private List<GameObject> doorSpawnerObjects = new();
    [SerializeField] private List<GameObject> checkPointObjects = new();
    
    public List<EnemySceneData> enemySceneData = new();
    public List<int> enemyIds = new();
    public BossSceneData bossSceneData;
    private CinemachineCamera mainCamera;
    public List<NpcSceneData> npcSceneData = new();
    public List<CollectableSceneData> collectableSceneData = new();
    public List<DoorSceneData> doorSceneData = new();
    public List<SpawnerSceneData> spawnerSceneData = new();
    public List<DoorSpawnerHybridData> doorSpawnerHybridSceneData = new();
    public List<CheckPointSceneData> checkPointSceneData = new();
    
    [Tooltip("Checks if the scene data has tried to find and load its scene data")]
    public bool sceneLoaded;
    #endregion

    private void Awake()
    {
        if (sectionObj == null || sectionObj.IsDestroyed()) sectionObj = GameObject.Find("Sections");
    }

    private void Start()
    {
        sceneName = gameObject.scene.name;
        mainCamera = DataManager.Instance.mainCamera.GetComponent<CinemachineCamera>();
        weather = GetComponent<WeatherManager>();
        audioManager = AudioManager.Instance;
        InitialiseSceneData();
        InvokeRepeating(nameof(CollateObjects), 0, 0.1f);
    }

    private void LateUpdate()
    {
        //#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F1))
        {
            SaveSceneData();
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            MakeDataPersistent();
        }
        if (Input.GetKeyDown(KeyCode.F3))
        {
            print("Loading data");
            LoadSceneData();
        }
    }

    #region Map Announcing
    private void OnBossDefeated()
    {
        bossSceneData.isDead = true;
        GameEvents.TriggerPersistGameData();
    }

    public void OnDoorStatusChanged(AccessManager accessManager, bool isOpen)
    {
        var dData = doorSceneData.Find(x => x.accessManager == accessManager);
        if (dData != null) dData.isOpen = isOpen;
    }
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Ability") || collision.gameObject.layer == LayerMask.NameToLayer("Enemy"))
        {
            if (collision.gameObject.scene.name == gameObject.scene.name) SceneManager.MoveGameObjectToScene(collision.gameObject, gameObject.scene);
        }
        
        if (!collision.CompareTag("Player")) return;
        if (mapToggle) return;
        mapToggle = true;
        foreach (var otherMap in Instances.Values.Where(m => m.sceneName != sceneName)) otherMap.activeMap = false;
        StartCoroutine(MapTransition(collision));
        GameEvents.TriggerPersistGameData();
    }

    private IEnumerator MapTransition(Collider2D collision)
    {
        // FIIIIINNEEEE
        if (AreaManager.Instance.titleText.text == mapName && AreaManager.Instance.titleText.text == null) yield break;
        SceneManager.MoveGameObjectToScene(collision.gameObject, SceneManager.GetSceneByName(sceneName));
        mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = GetComponent<Collider2D>();
            
        var targetWeather = weather.GetTargetWeather();
        var targetColor = mapBgColor;
            
        if (targetWeather != WeatherManager.WeatherType.Clear)
        {
            var index = (int)targetWeather - 1;
            if (index >= 0 && index < weather.weatherProperties.Length) targetColor = weather.weatherProperties[index].weatherBgColor;
        }
            
        AreaManager.Instance.TriggerColorFade(targetColor);
            
        if (audioManager.musicSource.volume == 0 && collision.gameObject.scene == audioManager.previousScene) audioManager.TriggerFadeIn(0.5f);
        yield return new WaitForSeconds(0.8f);
        if (collision.gameObject.scene == gameObject.scene) 
            StartMap(collision.gameObject, targetWeather);
    }

    private void StartMap(GameObject objectRef) => StartMap(objectRef, null);

    private void StartMap(GameObject objectRef, WeatherManager.WeatherType? forcedWeather)
    {
        if (objectRef == null || objectRef.scene.name != gameObject.scene.name)
        {
            if (objectRef != null && objectRef.scene.name != "Persistent Objects") StartCoroutine(TryStartMapAgain(objectRef));
            return;
        }
        weather.SetWeather(forcedWeather);
        if (undiscoveredMap) undiscoveredMap = false;
    }

    private IEnumerator TryStartMapAgain(GameObject objectRef)
    {
        yield return new WaitForSeconds(0.5f);
        Instances.TryGetValue(objectRef.gameObject.scene.name, out var otherMap);
        StartCoroutine(otherMap?.MapTransition(objectRef.GetComponent<Collider2D>()));
        audioManager.TriggerFadeIn(0.5f);
    }
    
    public void UpdateNormalAreaInfo() => UpdateAreaInfo(mapName, mapDescription, mapTextColor, mapBgColor, newOst, ostVolume);
    public void UpdateAreaInfo(string mName, string mDesc, Color mTextColor, Color mBgColor, AudioClip[] mOst, float[] mOstVolume)
    {
        AreaManager.Instance.Announce(mName, mDesc, mTextColor, mBgColor);
        for (var i = 0; i < audioManager.backgroundMusic.Length; i++)
        {
            audioManager.backgroundMusic[i] = mOst[i];
            audioManager.backgroundMusicVolume[i] = mOstVolume[i];
        }
        GameEvents.TriggerMusic(0, false);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (GetComponent<Collider2D>().OverlapPoint(collision.transform.position)) return;
        HandleMapExit(collision.gameObject);
    }

    private void HandleMapExit(GameObject playerObject)
    {
        GameEvents.TriggerPersistGameData();
        if (!mapToggle) return;
        mapToggle = false;
        weather.ClearDisasters();
        if (!audioManager.gameObject.IsDestroyed())
        {
            audioManager.TriggerFadeOut();
            audioManager.previousScene = playerObject.scene;
        }
        if (playerObject != null && SceneManager.GetSceneByName("Persistent Objects").isLoaded) 
            SceneManager.MoveGameObjectToScene(playerObject, SceneManager.GetSceneByName("Persistent Objects"));
        mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = PlayerHealth.Instance.gameObject.GetComponentInChildren<PolygonCollider2D>();
    }
    #endregion

    #region Data Management
    private void CollateObjects()
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        sceneObjects = scene.GetRootGameObjects();
        
        doorSpawnerObjects.Clear();
        doorObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (!obj.CompareTag("Section")) continue;
            foreach (var section in obj.GetComponentsInChildren<AccessManager>())
            {
                doorObjects.Add(section.gameObject);
                if (section.gameObject.GetComponent<SpawnerBase>() == null || doorSpawnerObjects.Contains(obj)) continue;
                doorSpawnerObjects.Add(section.gameObject);
                if (doorObjects.Contains(section.gameObject)) doorObjects.Remove(section.gameObject);
            }
        }
        
        spawnerObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (!obj.CompareTag("Spawner")) continue;
            foreach (var spawner in obj.GetComponentsInChildren<SpawnerBase>())
            {
                spawnerObjects.Add(spawner.gameObject);
                if (spawner.gameObject.GetComponent<AccessManager>() == null || doorSpawnerObjects.Contains(obj)) continue;
                doorSpawnerObjects.Add(spawner.gameObject);
                if (spawnerObjects.Contains(spawner.gameObject)) spawnerObjects.Remove(spawner.gameObject);
            }
        }
        
        checkPointObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (!obj.CompareTag("CheckPoint")) continue;
            foreach (var checkPoint in obj.GetComponentsInChildren<Checkpoint>())
            {
                checkPointObjects.Add(checkPoint.gameObject);
            }
        }
    }

    private void ClearSceneData()
    {
        enemySceneData.Clear();
        npcSceneData.Clear();
        collectableSceneData.Clear();
        doorSceneData.Clear();
        spawnerSceneData.Clear();
        doorSpawnerHybridSceneData.Clear();
        checkPointSceneData.Clear();
    }

    public void DisableSpawners()
    {
        foreach (var obj in sceneObjects)
        {
            if (obj == null || obj.CompareTag("Player")) continue;
            if (obj.GetComponentsInChildren<EntitySpawner>() != null)
            {
                foreach (var spawner in obj.GetComponentsInChildren<EntitySpawner>()) 
                    spawner.enabled = false;
            }
            obj.gameObject.SetActive(false);
        }
    }

    private void SetCurrentCheckPoint(GameObject checkPoint, bool realSpawn = true)
    {
        if (!activeMap) return;
        if (checkPoint == null) return;
        if (realSpawn)
        {
            foreach (var obj in checkPointObjects.Where(obj => obj != null && obj.GetComponent<Checkpoint>().spawnPoint))
            {
                obj.GetComponent<Checkpoint>().isCurrent = obj == checkPoint;
            }
        }
        GameEvents.TriggerPersistGameData();
    }
    
    public void InitialSpawnPlayer(string scene)
    {
        if (scene != sceneName) return;
        
        var spawn = mainSpawnPoint;
        
        // Priority 1: Check if we have a checkpoint ID loaded from save file
        var loadedCpId = PlayerPrefs.GetInt("LoadedCheckPointId", 0);
        if (loadedCpId != 0)
        {
            PlayerPrefs.DeleteKey("LoadedCheckPointId"); // Clear it so it doesn't persist forever
            var foundCp = checkPointObjects.Find(obj => obj.GetComponent<Identifier>().id == loadedCpId);
            if (foundCp != null)
            {
                spawn = foundCp;
                goto performSpawn;
            }
        }

        // Priority 2: Check current scene data for active checkpoint
        DataManager.CheckPointSceneData targetSpawnPoint = null;
        var sceneList = DataManager.Instance.sceneData.Find(list => list.sceneName == sceneName);
        if (sceneList != null)
        {
            targetSpawnPoint = sceneList.checkPoints.Find(cp => cp.isCurrent) ?? sceneList.checkPoints.Find(cp => cp.activated);
        }

        if (targetSpawnPoint != null)
        {
            var foundCp = checkPointObjects.Find(obj => obj.GetComponent<Identifier>().id == targetSpawnPoint.activeCheckpoint);
            if (foundCp != null) spawn = foundCp;
        }

        performSpawn:
        PlayerHealth.Instance.pc.currentSpawnPoint = spawn;
        PlayerHealth.Instance.pc.currentCheckPoint = spawn;
        //print($"Spawning player at {spawn.name}");
        StartCoroutine(PlayerHealth.Instance.Spawn(0.2f, true));
    }
    
    private static void SaveDataOnDialogueClose(ScriptableObject dialogue = null) => GameEvents.TriggerPersistGameData();

    private void InitialiseSceneData()
    {
        ClearSceneData();
        UpdateLists();
        LoadSceneData();
        LoadNpcData();
    }

    public void SaveSceneData()
    {
        //print("Saving data");
        if (!sceneLoaded) return;
        UpdateLists();
        MakeDataPersistent();
    }

    public void UpdateLists()
    {
        ClearSceneData();
        foreach (var obj in sceneObjects)
        {
            if (obj != null)
                switch (obj.tag)
                {
                    case "Enemy":
                        if (obj.name == "Imo")
                        {
                            // Imo is a character who is a test dummy, thus having the enemy layer and tag, but also is an NPC.
                            goto npcStuff;
                        }
                        var enemyId = obj.GetComponent<Identifier>();
                        var newEnemy = new EnemySceneData
                        {
                            activeEnemy = obj,
                            spawnId = enemyId.spawnId
                        };
                        enemySceneData.Add(newEnemy);
                        if (!enemyIds.Contains(enemyId.spawnId))
                            enemyIds.Add(enemyId.spawnId);
                        break;
                    case "Boss":
                        bossSceneData.bossHealth = obj.GetComponent<BossHealth>();
                        bossSceneData.activeBoss = obj.name;
                        break;
                    case "NPC":
                        npcStuff:
                        obj.TryGetComponent<NPChat>(out var npChat);
                        if (npChat == null || npChat.fixedDialogue) continue;
                        var npcId = npChat.name;
                        obj.TryGetComponent<ShopKeeper>(out var shopKeeper);
                        var newNpc = new NpcSceneData
                        {
                            activeNpc = obj,
                            npcName = npcId,
                            willTravel = npChat.willTravel,
                            dialogue = new List<DialogueObject>(npChat.dialogue),
                            spokenDialogue = new List<DialogueObject>(npChat.spokenDialogue),
                            shopItems = shopKeeper != null ? shopKeeper.shopItems.ToArray() : null
                        };
                        npcSceneData.Add(newNpc);
                        break;
                    case "Item":
                        var collectable = obj.GetComponent<Collectable>();
                        var newCollectable = new CollectableSceneData
                        {
                            collectable = collectable.collectable,
                            position = obj.transform.position,
                            isStray = collectable.isStray
                        };
                        collectableSceneData.Add(newCollectable);
                        break;
                }
        }
        foreach (var door in doorObjects)
        {
            var doorId = door.GetComponent<Identifier>();
            var accManager = door.GetComponent<AccessManager>();
            var newDoor = new DoorSceneData
            {
                accessManager = accManager,
                id = doorId.id,
                isOpen = accManager.isOpen
            };
            doorSceneData.Add(newDoor);
        }
        foreach (var spawner in spawnerObjects)
        {
            var spawnerId = spawner.GetComponent<Identifier>();
            var spawnerBase = spawner.GetComponent<SpawnerBase>();
            var newSpawner = new SpawnerSceneData
            {
                spawner = spawnerBase,
                id = spawnerId.id,
                isOneTime = spawnerBase.isOneTime
            };
            spawnerSceneData.Add(newSpawner);
        }

        foreach (var dsh in doorSpawnerObjects)
        {
            var id = dsh.GetComponent<Identifier>();
            var accManager = dsh.GetComponent<AccessManager>();
            var spawnerBase = dsh.GetComponent<SpawnerBase>();
            var newDsh = new DoorSpawnerHybridData
            {
                id = id.id,
                accessManager = accManager,
                spawner = spawnerBase,
                isOneTime = spawnerBase.isOneTime,
                isOpen = accManager.isOpen
            };
            doorSpawnerHybridSceneData.Add(newDsh);
        }

        foreach (var checkPoint in checkPointObjects)
        {
            checkPoint.TryGetComponent<Identifier>(out var checkPointId);
            if (checkPointId == null) continue;
            var checkPointScript = checkPoint.GetComponent<Checkpoint>();
            var newCheckPoint = new CheckPointSceneData
            {
                activeCheckpoint = checkPoint,
                id = checkPointId.id,
                isCurrent = checkPointScript.isCurrent,
                activated = checkPoint.GetComponent<Collider2D>().enabled
            };
            checkPointSceneData.Add(newCheckPoint);
        }
    }
    
    private void MakeDataPersistent()
    {
        //print("Persisting data");
        if (!sceneLoaded || PlayerHealth.Instance == null) return;
        var manager = DataManager.Instance;
        var list = manager.sceneData.Find(x => x.sceneName == sceneName);

        if (list == null)
        {
            list = new DataManager.SceneList();
            manager.sceneData.Add(list);
            list.sceneName = sceneName;
        }

        list.enemies.Clear();
        list.collectables.Clear();
        list.doors.Clear();
        list.spawners.Clear();
        list.doorSpawnerHybrids.Clear();
        list.checkPoints.Clear();
        
        list.activeMap = activeMap;
        list.undiscoveredMap = undiscoveredMap;
        
        foreach (var eData in enemySceneData)
        {
            if (eData.activeEnemy == null)
            {
                list.enemies.Add(new DataManager.EnemySceneData
                {
                    activeEnemyID = eData.spawnId
                });
            }
        }

        foreach (var cData in collectableSceneData)
        {
            var newCollectable = new DataManager.CollectableSceneData
            {
                activeCollectable = cData.collectable,
                position = cData.position,
                isStray = cData.isStray
            };
            list.collectables.Add(newCollectable);
        }
        
        if (bossSceneData.activeBoss != null)
        {
            list.activeBoss = bossSceneData.activeBoss;
            if (bossSceneData.isDead) list.isBossDead = true;
            list.willBeNpc = bossSceneData.willBeNpc;
        }
        
        foreach (var dData in doorSceneData)
        {
            list.doors.Add(new DataManager.DoorSceneData
            {
                activeDoor = dData.id,
                isOpen = dData.isOpen
            });
        }

        foreach (var sData in spawnerSceneData)
        {
            list.spawners.Add(new DataManager.SpawnerSceneData
            {
                activeSpawner = sData.id,
                isOneTime = sData.isOneTime
            });
        }

        foreach (var dshData in doorSpawnerHybridSceneData)
        {
            list.doorSpawnerHybrids.Add(new DataManager.DoorSpawnerHybridSceneData
            {
                activeDsh = dshData.id,
                isOpen = dshData.isOpen,
                isOneTime = dshData.isOneTime
            });
        }

        foreach (var cpData in checkPointSceneData)
        {
            list.checkPoints.Add(new DataManager.CheckPointSceneData
            {
                activeCheckpoint = cpData.id,
                isCurrent = cpData.isCurrent,
                activated = cpData.activated
            });
        }
    }

    private void LoadSceneData()
    {
        var manager = DataManager.Instance;
        if (manager == null || manager.sceneData.Count == 0)
        {
            sceneLoaded = true;
            return;
        }
        var hasSceneData = manager.sceneData.Exists(x => x.sceneName == sceneName);
        if (!hasSceneData)
        {
            // if there isnt any found scene data, it should save the current new state into the data manager
            // print($"Scene data for {sceneName} not found, saving current scene data to data manager.");
            sceneLoaded = true;
            SaveSceneData();
            return;
        }
        var sceneData = manager.sceneData.Find(x => x.sceneName == sceneName);
        activeMap = sceneData.activeMap;
        undiscoveredMap = sceneData.undiscoveredMap;
        
        foreach (var list in manager.sceneData.Where(list => list.sceneName == sceneName))
        {
            foreach (var enemyData in list.enemies)
            {
                var enemy = enemySceneData.Find(e => e.spawnId == enemyData.activeEnemyID);
                if (enemy != null && enemy.activeEnemy != null) 
                    Destroy(enemy.activeEnemy);
            }

            foreach (var collectableData in list.collectables)
            {
                if (!collectableData.isStray) continue;
                var item = Instantiate(list.ItemToStore, collectableData.position, Quaternion.identity);
                item.GetComponent<SpriteRenderer>().color = collectableData.colour;
                item.GetComponent<Collectable>().collectable = collectableData.activeCollectable;
                SceneManager.MoveGameObjectToScene(item, SceneManager.GetSceneByName(sceneName));
            }

            for (var i = 0; i < list.doors.Count; i++)
            {
                var doorData = list.doors[i];
                if (!doorData.isOpen) continue;
                var door = doorSceneData.Find(d => d.id == doorData.activeDoor);
                if (door != null && door.accessManager != null) door.accessManager.InstantOpen();
            }

            foreach (var spawnerData in list.spawners)
            {
                if (!spawnerData.isOneTime) continue;
                var ssd = spawnerSceneData.Find(s => s.id == spawnerData.activeSpawner);
                if (ssd != null && ssd.spawner != null) StartCoroutine(ssd.spawner.Despawn());
            }

            foreach (var dshData in list.doorSpawnerHybrids)
            {
                var dsh = doorSpawnerHybridSceneData.Find(d => d.id == dshData.activeDsh);
                if (dsh == null) continue;
                if (dshData.isOpen) dsh.accessManager.InstantOpen();
                if (!dshData.isOneTime) continue;
                StartCoroutine(dsh.spawner.Despawn());
            }

            foreach (var cpData in list.checkPoints)
            {
                var cp = checkPointSceneData.Find(c => c.id == cpData.activeCheckpoint);
                if (cp == null) continue;
                if (!cpData.activated)
                {
                    cp.activeCheckpoint.GetComponent<Collider2D>().enabled = false;
                    cp.isCurrent = false;
                    continue;
                }
                cp.isCurrent = cpData.isCurrent;
            }

            bossSceneData.activeBoss = list.activeBoss;
            if (list.isBossDead && bossSceneData.bossHealth != null) 
                Destroy(bossSceneData.bossHealth.gameObject);
            bossSceneData.willBeNpc = list.willBeNpc;
            
            sceneLoaded = true;
            break;
        }
        if (bossSceneData.bossHealth != null && bossSceneData.isDead) 
            Destroy(bossSceneData.bossHealth.gameObject);
            
        sceneLoaded = true;
    }

    public void SaveNpcData()
    {
        var manager = DataManager.Instance;
        if (manager == null) return;
        foreach (var nData in npcSceneData)
        {
            if (nData.activeNpc == null || string.IsNullOrEmpty(nData.npcName)) continue;
            var npc = manager.npcs.Find(x => x.activeNpcName.Contains(nData.npcName));
            if (npc == null)
            {
                manager.npcs.Add(new DataManager.NpcData
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
    
    private void LoadNpcData(string scene = null)
    {
        if (DataManager.Instance == null || DataManager.Instance.npcs.Count == 0) return;
        foreach (var npcData in DataManager.Instance.npcs)
        {
            var npc = npcSceneData.Find(n => n.npcName == npcData.activeNpcName);
            if (npc == null || npc.activeNpc == null) continue;
            npc.activeNpc.TryGetComponent<NPChat>(out var dTrigger);
            if (dTrigger != null)
            {
                dTrigger.dialogue.Clear();
                dTrigger.dialogue.AddRange(npcData.dialogue);
            }
            npc.activeNpc.TryGetComponent<ShopKeeper>(out var shopKeeper);
            if (shopKeeper != null)
            {
                shopKeeper.shopItems.Clear();
                shopKeeper.shopItems.AddRange(npcData.shopItems);
            }
        }
    }
    #endregion

    
    private void OnEnable()
    {
        if (!Instances.ContainsKey(gameObject.scene.name))
            Instances.Add(gameObject.scene.name, this);
        if (sectionObj == null || sectionObj.IsDestroyed()) sectionObj = GameObject.Find("Sections");
        
        GameEvents.OnPlayerSpawned += StartMap;
        GameEvents.OnBossDefeated += OnBossDefeated;
        GameEvents.OnDialogueClose += SaveDataOnDialogueClose;
        GameEvents.LoadMapData += LoadSceneData;
        GameEvents.LoadNpcData += LoadNpcData;
        InvManager.Instance.invIndex.OnInventoryChanged += SaveSceneData;
        GameEvents.OnSpawnPointChanged += SetCurrentCheckPoint;
    }

    private void OnDisable()
    {
        if (Instances.ContainsKey(gameObject.scene.name) && Instances[gameObject.scene.name] == this)
            Instances.Remove(gameObject.scene.name);
        
        GameEvents.OnBossDefeated -= OnBossDefeated;
        GameEvents.LoadNpcData -= LoadNpcData;
        GameEvents.OnPlayerSpawned -= StartMap;
        GameEvents.OnDialogueClose -= SaveDataOnDialogueClose;
        GameEvents.LoadMapData -= LoadSceneData;
        InvManager.Instance.invIndex.OnInventoryChanged -= SaveSceneData;
        GameEvents.OnSpawnPointChanged -= SetCurrentCheckPoint;
    }
    
    #region Door and Spawner Stuff
    public void RegisterDoor(GameObject door) { if (!doorObjects.Contains(door)) doorObjects.Add(door); }
    public void UnregisterDoor(GameObject door) => doorObjects.Remove(door);
    
    public void RegisterSpawner(GameObject spawner) { if (!spawnerObjects.Contains(spawner)) spawnerObjects.Add(spawner); }
    public void UnregisterSpawner(GameObject spawner) => spawnerObjects.Remove(spawner);
    #endregion
}
    
#region Scene data classes
    [Serializable]
    public class EnemySceneData
    {
        public GameObject activeEnemy; 
        public int spawnId;
    }

    [Serializable]
    public class BossSceneData
    {
        public BossHealth bossHealth;
        [Tooltip("Any boss that is active gets detected here")] 
        public string activeBoss;
        public bool isDead;
        [Tooltip("Will this boss become an npc once defeated?")] 
        public bool willBeNpc;
    }

    [Serializable]
    public class NpcSceneData
    {
        [Tooltip("Any NPCs that are active get detected here")]
        public GameObject activeNpc;
        public string npcName;
        [Tooltip("Will this NPC travel throughout scenes (areas)?")] 
        public bool willTravel;
        public List<DialogueObject> dialogue = new();
        public List<DialogueObject> spokenDialogue = new();
        public ShopItem[] shopItems;
    }

    [Serializable]
    public class CollectableSceneData
    {
        public CollectableSOBase collectable;
        public Vector3 position;
        public bool isStray;
    }

    [Serializable]
    public class DoorSceneData
    {
        public AccessManager accessManager;
        public int id;
        public bool isOpen;
    }

    [Serializable]
    public class SpawnerSceneData
    {
        public SpawnerBase spawner;
        public int id;
        public bool isOneTime;
    }

    [Serializable]
    public class CheckPointSceneData
    {
        public GameObject activeCheckpoint;
        public int id;
        public bool isCurrent;
        public bool activated;
    }

    [Serializable]
    public class DoorSpawnerHybridData
    {
        public int id;
        
        public bool isOpen;
        public AccessManager accessManager;
        
        public bool isOneTime;
        public SpawnerBase spawner;
    }
#endregion