using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Dialogue;
using scripies.Enemy;
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
    [SerializeField] internal Color mapBgColor = Color.white;
    
    [SerializeField] private AudioClip[] newOst;
    public float[] ostVolume = {0.5f,0.5f,0.5f};
    public float[] ostBpm = {130f,130f,130f};
    
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
    public GameObject[] sceneObjects = Array.Empty<GameObject>();
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
        sceneName = gameObject.scene.name;
        weather = GetComponent<WeatherManager>();
    }

    private void Start()
    {
        audioManager = AudioManager.Instance;
        if (DataManager.Instance != null && DataManager.Instance.mainCamera != null)
            mainCamera = DataManager.Instance.mainCamera.GetComponent<CinemachineCamera>();
    }
    
    private void OnEnable()
    {
        sceneName = gameObject.scene.name;
        if (sectionObj == null || sectionObj.IsDestroyed()) sectionObj = GameObject.Find("Sections");
        if (!Instances.ContainsKey(sceneName)) Instances.Add(sceneName, this);
        else Instances[sceneName] = this;

        GameEvents.OnPlayerSpawned += StartMapO;
        GameEvents.OnBossDefeated += OnBossDefeated;
        GameEvents.OnEnemyDefeated += OnEnemyDefeated;
        GameEvents.OnDialogueClose += SaveDataOnDialogueClose;
        GameEvents.LoadMapData += LoadSceneData;
        GameEvents.OnSpawnPointChanged += SetCurrentCheckPoint;

        CollateObjects();
        InvokeRepeating(nameof(CollateObjects), 0.1f, 0.25f);
        Invoke(nameof(Invocation), 0.1f);
        StartCoroutine(InitialiseSceneData());
    }

    private void Invocation()
    {
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
        {
            InvManager.Instance.invIndex.OnInventoryChanged -= SaveSceneData;
            InvManager.Instance.invIndex.OnInventoryChanged += SaveSceneData;
        }
    }

    private void OnDisable()
    {
        if (Instances.ContainsKey(gameObject.scene.name) && Instances[gameObject.scene.name] == this)
            Instances.Remove(gameObject.scene.name);
        CancelInvoke(nameof(CollateObjects));
        CancelInvoke(nameof(Invocation));
        GameEvents.OnBossDefeated -= OnBossDefeated;
        GameEvents.OnEnemyDefeated -= OnEnemyDefeated;
        GameEvents.OnPlayerSpawned -= StartMapO;
        GameEvents.OnDialogueClose -= SaveDataOnDialogueClose;
        GameEvents.LoadMapData -= LoadSceneData;
        GameEvents.OnSpawnPointChanged -= SetCurrentCheckPoint;
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
            InvManager.Instance.invIndex.OnInventoryChanged -= SaveSceneData;
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
            GameEvents.TriggerLoadNpcData();
        }
    }

    #region Map Announcing
    private void OnBossDefeated()
    {
        bossSceneData.isDead = true;
        GameEvents.TriggerPersistGameData();
    }

    private void OnEnemyDefeated(GameObject enemy)
    {
        if (enemy == null || enemy.scene.name != sceneName) return;
        var isOneTimeEnemy = enemy.TryGetComponent<EnemyHealth>(out var eh) && eh.isOneTime;
        if (enemy.TryGetComponent<Identifier>(out var ident))
        {
            var eData = enemySceneData.Find(e => e.spawnId == ident.spawnId);
            if (eData != null)
            {
                eData.activeEnemy = null;
                eData.isOneTime |= isOneTimeEnemy;
            }
            else if (isOneTimeEnemy)
            {
                enemySceneData.Add(new EnemySceneData
                {
                    activeEnemy = null,
                    spawnId = ident.spawnId,
                    isOneTime = true
                });
            }
        }
        SaveSceneData();
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
    }

    private IEnumerator MapTransition(Collider2D collision)
    {
        // FIIIIINNEEEE
        if (AreaManager.Instance.titleText.text == mapName && AreaManager.Instance.titleText.text == null) yield break;
        SceneManager.MoveGameObjectToScene(collision.gameObject, SceneManager.GetSceneByName(sceneName));
        if (mainCamera != null)
            mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = GetComponent<Collider2D>();
            
        var targetWeather = weather.weatherType;
        var targetColor = mapBgColor;
            
        if (targetWeather != WeatherManager.WeatherType.Clear)
        {
            var index = (int)targetWeather - 1;
            if (index >= 0 && index < weather.weatherProperties.Length) targetColor = weather.weatherProperties[index].weatherBgColor;
        }
            
        AreaManager.Instance.TriggerColorFade(targetColor);
            
        if (audioManager != null && audioManager.musicSource.volume == 0 && collision.gameObject.scene == audioManager.previousScene)
            audioManager.TriggerFadeIn(0.5f);
        yield return new WaitForSeconds(0.8f);
        if (collision != null && collision.gameObject.scene == gameObject.scene) 
            StartMap(targetWeather);
    }

    private void StartMapO() => StartMap(null);
    private void StartMap(WeatherManager.WeatherType? forcedWeather)
    {
        if (PlayerHealth.Instance == null) return;
        var player = PlayerHealth.Instance.gameObject;
        if (player == null || player.scene.name != gameObject.scene.name)
        {
            if (player != null && player.scene.name != "Persistent Objects") StartCoroutine(TryStartMapAgain(player));
            return;
        }
        activeMap = true;
        weather.ChangeWeather(forcedWeather);
    }

    private IEnumerator TryStartMapAgain(GameObject objectRef)
    {
        yield return new WaitForSeconds(0.5f);
        if (objectRef == null) yield break;
        Instances.TryGetValue(objectRef.gameObject.scene.name, out var otherMap);
        if (otherMap != null) StartCoroutine(otherMap.MapTransition(objectRef.GetComponent<Collider2D>()));
        if (audioManager != null) audioManager.TriggerFadeIn(0.5f);
    }
    
    public void UpdateNormalAreaInfo() => UpdateAreaInfo(mapName, mapDescription, mapTextColor, mapBgColor, newOst, ostVolume, false, ostBpm);
    public void UpdateAreaInfo(string mName, string mDesc, Color mTextColor, Color mBgColor, AudioClip[] mOst, float[] mOstVolume, bool weatherChanged = false, float[] mOstBpm = null)
    {
        if (mName == AreaManager.Instance.currentArea)
        {
            GameEvents.TriggerOnEnterMap(sceneName, undiscoveredMap);
            return;
        }
        
        if (weatherChanged || undiscoveredMap) AreaManager.Instance.Announce(mName, mDesc, mTextColor, mBgColor);
        else if (!undiscoveredMap) AreaManager.Instance.Announce(mName, mDesc, mTextColor, mBgColor, true);
        
        if (audioManager != null && mOst != null)
        {
            var count = Mathf.Min(audioManager.backgroundMusic.Length, mOst.Length);
            for (var i = 0; i < count; i++)
            {
                audioManager.backgroundMusic[i] = mOst[i];
                if (mOstVolume != null && i < mOstVolume.Length)
                    audioManager.backgroundMusicVolume[i] = mOstVolume[i];
                if (mOstBpm != null && audioManager.backgroundMusicBpm != null && i < mOstBpm.Length && i < audioManager.backgroundMusicBpm.Length && mOstBpm[i] > 0f)
                    audioManager.backgroundMusicBpm[i] = mOstBpm[i];
            }
        }
        
        GameEvents.TriggerPlayMusic(0, false);
        GameEvents.TriggerOnEnterMap(sceneName, undiscoveredMap);
        if (undiscoveredMap) undiscoveredMap = false;
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (GetComponent<Collider2D>().OverlapPoint(collision.transform.position)) return;
        HandleMapExit(collision.gameObject);
    }

    private void HandleMapExit(GameObject playerObject)
    {
        SaveSceneData();
        if (!mapToggle) return;
        mapToggle = false;
        weather.ClearDisasters();
        if (audioManager != null && !audioManager.gameObject.IsDestroyed())
        {
            audioManager.TriggerFadeOut();
            audioManager.previousScene = playerObject.scene;
        }
        if (playerObject != null && SceneManager.GetSceneByName("Persistent Objects").isLoaded) 
            SceneManager.MoveGameObjectToScene(playerObject, SceneManager.GetSceneByName("Persistent Objects"));
        if (mainCamera != null && PlayerHealth.Instance != null) 
            mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = PlayerHealth.Instance.gameObject.GetComponentInChildren<PolygonCollider2D>();
    }
    #endregion

    #region Data Management
    private void CollateObjects()
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded) return;
        sceneObjects = scene.GetRootGameObjects();
        
        doorSpawnerObjects.Clear();
        doorObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (obj == null || !obj.CompareTag("Section")) continue;
            foreach (var section in obj.GetComponentsInChildren<AccessManager>())
            {
                if (!doorObjects.Contains(section.gameObject)) doorObjects.Add(section.gameObject);
                if (section.gameObject.GetComponent<SpawnerBase>() == null) continue;
                if (!doorSpawnerObjects.Contains(section.gameObject)) doorSpawnerObjects.Add(section.gameObject);
                doorObjects.Remove(section.gameObject);
            }
        }
        
        spawnerObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (obj == null || !obj.CompareTag("Spawner")) continue;
            foreach (var spawner in obj.GetComponentsInChildren<SpawnerBase>())
            {
                if (!spawnerObjects.Contains(spawner.gameObject)) spawnerObjects.Add(spawner.gameObject);
                if (spawner.gameObject.GetComponent<AccessManager>() == null) continue;
                if (!doorSpawnerObjects.Contains(spawner.gameObject)) doorSpawnerObjects.Add(spawner.gameObject);
                spawnerObjects.Remove(spawner.gameObject);
            }
        }
        
        checkPointObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (obj == null || !obj.CompareTag("CheckPoint")) continue;
            foreach (var checkPoint in obj.GetComponentsInChildren<Checkpoint>())
            {
                if (!checkPointObjects.Contains(checkPoint.gameObject))
                    checkPointObjects.Add(checkPoint.gameObject);
            }
        }
    }

    private void ClearSceneData()
    {
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
        GameEvents.TriggerPersistGameData(false);
    }
    
    public void InitialSpawnPlayer(string scene)
    {
        if (scene != sceneName) return;
        CollateObjects();
        
        var spawn = mainSpawnPoint;
        if (spawn == null)
        {
            var foundSpawn = GameObject.Find("spawnPoint") ?? GameObject.Find("Default SpawnPoint");
            if (foundSpawn != null) spawn = foundSpawn;
        }
        
        // Priority 1: Check if we have a checkpoint ID loaded from save file
        var loadedCpId = PlayerPrefs.GetInt("LoadedCheckPointId", 0);
        if (loadedCpId != 0)
        {
            PlayerPrefs.DeleteKey("LoadedCheckPointId"); // Clear it so it doesn't persist forever
            var foundCp = checkPointObjects.Find(obj => obj != null && obj.TryGetComponent<Identifier>(out var ident) && ident.id == loadedCpId);
            if (foundCp != null)
            {
                spawn = foundCp;
                goto performSpawn;
            }
        }

        // Priority 2: Check current scene data for active checkpoint
        DataManager.CheckPointSceneData targetSpawnPoint = null;
        var sceneList = DataManager.Instance != null ? DataManager.Instance.sceneData.Find(list => list.sceneName == sceneName) : null;
        if (sceneList != null)
        {
            targetSpawnPoint = sceneList.checkPoints.Find(cp => cp.isCurrent) ?? sceneList.checkPoints.Find(cp => cp.activated);
        }

        if (targetSpawnPoint != null)
        {
            var foundCp = checkPointObjects.Find(obj => obj != null && obj.TryGetComponent<Identifier>(out var ident) && ident.id == targetSpawnPoint.activeCheckpoint);
            if (foundCp != null) spawn = foundCp;
        }

        performSpawn:
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.pc != null)
        {
            PlayerHealth.Instance.pc.currentSpawnPoint = spawn;
            PlayerHealth.Instance.pc.currentCheckPoint = spawn;
            StartCoroutine(PlayerHealth.Instance.Spawn(0.2f, true));
        }
    }
    
    private static void SaveDataOnDialogueClose(ScriptableObject dialogue = null, NPC npc = null) => GameEvents.TriggerPersistGameData();

    private IEnumerator InitialiseSceneData()
    {
        yield return new WaitUntil(() => gameObject.scene.isLoaded);
        // Wait a short moment so EntitySpawner/ItemSpawner Start() methods finish spawning initial scene entities
        yield return new WaitForSecondsRealtime(0.15f);
        yield return StartCoroutine(LoadSceneDataC());
    }

    public void SaveSceneData()
    {
        if (!sceneLoaded) return;
        UpdateLists();
        MakeDataPersistent();
    }

    public void UpdateLists()
    {
        CollateObjects();
        ClearSceneData();

        foreach (var eData in enemySceneData)
        {
            if (eData.activeEnemy != null && (eData.activeEnemy.IsDestroyed() || eData.activeEnemy.CompareTag("Dead")))
                eData.activeEnemy = null;
        }

        foreach (var obj in sceneObjects)
        {
            if (obj == null || obj.IsDestroyed()) continue;
            switch (obj.tag)
            {
                case "Enemy":
                    if (obj.name == "Imo")
                    {
                        // Imo is a character who is a test dummy, thus having the enemy layer and tag, but also is an NPC.
                        goto npcStuff;
                    }
                    if (!obj.TryGetComponent<Identifier>(out var enemyId)) break;
                    var isOneTimeEnemy = obj.TryGetComponent<EnemyHealth>(out var eh) && eh.isOneTime;
                    var existingEnemy = enemySceneData.Find(e => e.spawnId == enemyId.spawnId);
                    if (existingEnemy != null)
                    {
                        existingEnemy.activeEnemy = obj;
                        existingEnemy.isOneTime |= isOneTimeEnemy;
                    }
                    else
                    {
                        enemySceneData.Add(new EnemySceneData
                        {
                            activeEnemy = obj,
                            spawnId = enemyId.spawnId,
                            isOneTime = isOneTimeEnemy
                        });
                    }
                    if (!enemyIds.Contains(enemyId.spawnId))
                        enemyIds.Add(enemyId.spawnId);
                    break;
                case "Boss":
                    bossSceneData.bossHealth = obj.GetComponent<BossHealth>();
                    bossSceneData.activeBoss = obj.name;
                    if (obj.TryGetComponent<NPChat>(out _))
                        goto npcStuff;
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
                        isActive = obj.activeSelf,
                        willTravel = npChat.willTravel,
                        dialogue = new List<DialogueObject>(npChat.dialogue),
                        spokenDialogue = new List<DialogueObject>(npChat.spokenDialogue),
                        shopItems = shopKeeper != null ? shopKeeper.shopItems.ToArray() : null
                    };
                    npcSceneData.Add(newNpc);
                    break;
                case "Item":
                    if (!obj.TryGetComponent<Collectable>(out var collectable)) break;
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
            if (door == null || !door.TryGetComponent<Identifier>(out var doorId) || !door.TryGetComponent<AccessManager>(out var accManager)) continue;
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
            if (spawner == null || !spawner.TryGetComponent<Identifier>(out var spawnerId) || !spawner.TryGetComponent<SpawnerBase>(out var spawnerBase)) continue;
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
            if (dsh == null || !dsh.TryGetComponent<Identifier>(out var id) || !dsh.TryGetComponent<AccessManager>(out var accManager) || !dsh.TryGetComponent<SpawnerBase>(out var spawnerBase)) continue;
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
            if (checkPoint == null || !checkPoint.TryGetComponent<Identifier>(out var checkPointId) || !checkPoint.TryGetComponent<Checkpoint>(out var checkPointScript)) continue;
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
        if (!sceneLoaded || DataManager.Instance == null) return;
        var manager = DataManager.Instance;
        var list = manager.sceneData.Find(x => x.sceneName == sceneName);

        if (list == null)
        {
            list = new DataManager.SceneList { sceneName = sceneName };
            manager.sceneData.Add(list);
        }

        var deadEnemyIds = new HashSet<int>();
        foreach (var prevDead in list.enemies)
        {
            if (prevDead.activeEnemyID != 0 && prevDead.isOneTime)
                deadEnemyIds.Add(prevDead.activeEnemyID);
        }
        foreach (var eData in enemySceneData)
        {
            if (eData.spawnId == 0) continue;
            if (!eData.isOneTime)
            {
                deadEnemyIds.Remove(eData.spawnId);
                continue;
            }
            if (eData.activeEnemy == null || eData.activeEnemy.IsDestroyed() || eData.activeEnemy.CompareTag("Dead"))
                deadEnemyIds.Add(eData.spawnId);
            else
                deadEnemyIds.Remove(eData.spawnId);
        }

        list.enemies.Clear();
        list.collectables.Clear();
        list.doors.Clear();
        list.spawners.Clear();
        list.doorSpawnerHybrids.Clear();
        list.checkPoints.Clear();
        
        list.activeMap = activeMap;
        list.undiscoveredMap = undiscoveredMap;
        list.currentWeatherType = weather.weatherType;
        list.currentWeatherDuration = weather.weatherDurationTimer;
        
        foreach (var deadId in deadEnemyIds)
        {
            list.enemies.Add(new DataManager.EnemySceneData
            {
                activeEnemyID = deadId,
                isOneTime = true
            });
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
        
        if (!string.IsNullOrEmpty(bossSceneData.activeBoss))
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

    private void LoadSceneData() => StartCoroutine(LoadSceneDataC());
    private IEnumerator LoadSceneDataC()
    {
        yield return new WaitUntil(() => gameObject.scene.isLoaded);
        UpdateLists();

        var manager = DataManager.Instance;
        if (manager == null)
        {
            sceneLoaded = true;
            yield break;
        }

        var list = manager.sceneData.Find(x => x.sceneName == sceneName);
        if (list == null)
        {
            // If there isn't any saved scene data for this map yet, save its initial state to DataManager
            sceneLoaded = true;
            SaveSceneData();
            yield break;
        }

        activeMap = list.activeMap;
        undiscoveredMap = list.undiscoveredMap;
        weather.weatherDurationTimer = Mathf.Clamp(list.currentWeatherDuration, 5, Mathf.Max(5, list.currentWeatherDuration));
        weather.weatherType = list.currentWeatherType;
        
        foreach (var enemyData in list.enemies)
        {
            if (!enemyData.isOneTime) continue;
            var enemy = enemySceneData.Find(e => e.spawnId == enemyData.activeEnemyID);
            if (enemy != null)
            {
                if (enemy.activeEnemy != null && enemy.isOneTime)
                {
                    Destroy(enemy.activeEnemy);
                    enemy.activeEnemy = null;
                }
            }
            else if (enemyData.activeEnemyID != 0)
            {
                enemySceneData.Add(new EnemySceneData
                {
                    activeEnemy = null,
                    spawnId = enemyData.activeEnemyID,
                    isOneTime = true
                });
            }
        }

        // Remove existing stray items in the scene before restoring saved stray items so they don't duplicate
        foreach (var obj in sceneObjects)
        {
            if (obj == null || obj.IsDestroyed() || !obj.CompareTag("Item")) continue;
            if (obj.TryGetComponent<Collectable>(out var col) && col.isStray)
                Destroy(obj);
        }

        var itemPrefab = list.ItemToStore != null
            ? list.ItemToStore
            : InvManager.Instance != null ? InvManager.Instance.persistentItem : null;

        if (itemPrefab != null)
        {
            foreach (var collectableData in list.collectables)
            {
                if (!collectableData.isStray || collectableData.activeCollectable == null) continue;
                var item = Instantiate(itemPrefab, collectableData.position, Quaternion.identity);
                if (item.TryGetComponent<SpriteRenderer>(out var sr)) sr.color = collectableData.colour;
                if (item.TryGetComponent<Collectable>(out var col))
                {
                    col.collectable = collectableData.activeCollectable;
                    col.isStray = true;
                }
                SceneManager.MoveGameObjectToScene(item, SceneManager.GetSceneByName(sceneName));
            }
        }

        foreach (var doorData in list.doors)
        {
            if (!doorData.isOpen) continue;
            var door = doorSceneData.Find(d => d.id == doorData.activeDoor);
            if (door != null && door.accessManager != null) door.accessManager.InstantOpen();
            else Debug.LogWarning($"[MapManager] Door {doorData.activeDoor} not found in {sceneName}");
        }

        foreach (var spawnerData in list.spawners)
        {
            if (!spawnerData.isOneTime) continue;
            var ssd = spawnerSceneData.Find(s => s.id == spawnerData.activeSpawner);
            if (ssd != null && ssd.spawner != null)
            {
                ssd.spawner.isOneTime = true;
                StartCoroutine(ssd.spawner.Despawn());
            }
        }

        foreach (var dshData in list.doorSpawnerHybrids)
        {
            var dsh = doorSpawnerHybridSceneData.Find(d => d.id == dshData.activeDsh);
            if (dsh == null) continue;
            if (dshData.isOpen && dsh.accessManager != null) dsh.accessManager.InstantOpen();
            if (dshData.isOneTime && dsh.spawner != null)
            {
                dsh.spawner.isOneTime = true;
                StartCoroutine(dsh.spawner.Despawn());
            }
        }

        foreach (var cpData in list.checkPoints)
        {
            var cp = checkPointSceneData.Find(c => c.id == cpData.activeCheckpoint);
            if (cp == null || cp.activeCheckpoint == null) continue;
            var cpComponent = cp.activeCheckpoint.GetComponent<Checkpoint>();
            if (!cpData.activated)
            {
                if (cp.activeCheckpoint.TryGetComponent<Collider2D>(out var col)) col.enabled = false;
                cp.isCurrent = false;
                if (cpComponent != null) cpComponent.isCurrent = false;
                continue;
            }
            cp.isCurrent = cpData.isCurrent;
            if (cpComponent != null) cpComponent.isCurrent = cpData.isCurrent;
        }

        bossSceneData.activeBoss = list.activeBoss;
        bossSceneData.isDead = list.isBossDead;
        bossSceneData.willBeNpc = list.willBeNpc;
        if (list.isBossDead && bossSceneData.bossHealth != null)
        {
            Destroy(bossSceneData.bossHealth.gameObject);
        }
            
        sceneLoaded = true;
        GameEvents.TriggerLoadNpcData();
    }
    
    #endregion
    
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
        public bool isOneTime;
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
        public bool isActive;
        [Tooltip("Will this NPC travel throughout scenes (areas)?")] 
        public bool willTravel;
        public List<DialogueObject> dialogue = new();
        public List<DialogueObject> spokenDialogue = new();
        public ShopKeeperItem[] shopItems;
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
