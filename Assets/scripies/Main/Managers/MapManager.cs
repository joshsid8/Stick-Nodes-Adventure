using System;
using System.Collections;
using System.Collections.Generic;
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
    
    private WeatherManager weather;
    private AudioManager audioManager;
    #endregion

    #region Scene Data
    private string sceneName; 
    public GameObject[] sceneObjects;
    [SerializeField] private List<GameObject> doorObjects = new();
    [SerializeField] private List<GameObject> spawnerObjects = new();
    
    public List<EnemySceneData> enemySceneData = new();
    public List<int> enemyIds = new();
    public BossHealth bossHealth;
    public BossSceneData bossSceneData;
    private CinemachineCamera mainCamera;
    public List<NpcSceneData> npcSceneData = new();
    public List<CollectableSceneData> collectableSceneData = new();
    public List<DoorSceneData> doorSceneData = new();
    public List<SpawnerSceneData> spawnerSceneData = new();
    
    private bool sceneLoaded;
    #endregion

    private void Awake() { if (sectionObj == null || sectionObj.IsDestroyed()) sectionObj = GameObject.Find("Sections"); }

    private void Start()
    {
        sceneName = gameObject.scene.name;
        mainCamera = DataManager.Instance.mainCamera.GetComponent<CinemachineCamera>();
        weather = GetComponent<WeatherManager>();
        audioManager = AudioManager.Instance;
        InitialiseSceneData();
        LoadSceneData();
    }

    private void LateUpdate()
    {
        #if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F1))
        {
            print("Saving data");
            SaveSceneData();
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            print("Persisting data");
            MakeDataPersistent();
        }
        if (Input.GetKeyDown(KeyCode.F3))
        {
            print("Loading data");
            LoadSceneData();
        }
        #endif

        // if (!activeMap) return;
        // var player = PlayerHealth.Instance;
        // var playerCol = player.GetComponent<Collider2D>();
    }

    #region Map Announcing
    private void OnBossDefeated()
    {
        bossSceneData.isDead = true;
        GameEvents.TriggerSaveGame();
    }

    public void OnDoorStatusChanged(DoorManager doorManager, bool isOpen)
    {
        var dData = doorSceneData.Find(x => x.doorManager == doorManager);
        if (dData != null) dData.isOpen = isOpen;
    }

    private void OnEnable()
    {
        if (!Instances.ContainsKey(gameObject.scene.name))
            Instances.Add(gameObject.scene.name, this);
        if (sectionObj == null || sectionObj.IsDestroyed()) sectionObj = GameObject.Find("Sections");
        
        GameEvents.OnPlayerSpawned += StartMap;
        GameEvents.OnBossDefeated += OnBossDefeated;
        GameEvents.OnDialogueClose += SaveDataOnDialogueClose;
        GameEvents.SaveMapData += SaveSceneData;
        GameEvents.LoadMapData += LoadSceneData;
    }

    private void OnDisable()
    {
        if (Instances.ContainsKey(gameObject.scene.name) && Instances[gameObject.scene.name] == this)
            Instances.Remove(gameObject.scene.name);
        
        GameEvents.OnBossDefeated -= OnBossDefeated;
        GameEvents.OnPlayerSpawned -= StartMap;
        GameEvents.OnDialogueClose -= SaveDataOnDialogueClose;
        GameEvents.SaveMapData -= SaveSceneData;
        GameEvents.LoadMapData -= LoadSceneData;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (activeMap) return;
        activeMap = true;
        StartCoroutine(MapTransition(collision));
    }

    private IEnumerator MapTransition(Collider2D collision)
    {
        // No! I am NNNNOT going to inverse the if statement!!!
        if (AreaManager.Instance.titleText.text != mapName || AreaManager.Instance.titleText.text != null)
        {
            SceneManager.MoveGameObjectToScene(collision.gameObject, SceneManager.GetSceneByName(sceneName));
            mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = GetComponent<Collider2D>();
            //StartCoroutine(AreaManager.Instance.LerpColours(mapBgColor));
            if (audioManager.musicSource.volume == 0 && collision.gameObject.scene == audioManager.previousScene) audioManager.TriggerFadeIn(0.5f);
            yield return new WaitForSeconds(0.8f);
            if (collision.gameObject.scene == gameObject.scene) 
                StartMap(collision.gameObject);
        }
    }

    private void StartMap(GameObject objectRef = null)
    {
        if (objectRef == null || objectRef.scene.name != gameObject.scene.name)
        {
            if (objectRef != null && objectRef.scene.name != "Persistent Objects") StartCoroutine(TryStartMapAgain(objectRef));
            return;
        }
        weather.SetWeather();
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
        audioManager.currentScene = gameObject.scene.name;
        GameEvents.TriggerMusic(0, false);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (GetComponent<Collider2D>().OverlapPoint(collision.transform.position)) return;
        //print($"Map {sceneName} LLIIIEEED to you! youre still in the map!"); }
        HandleMapExit(collision.gameObject);
    }

    private void HandleMapExit(GameObject playerObject)
    {
        if (!activeMap) return;
        activeMap = false;
        MakeDataPersistent();
        GameEvents.TriggerSaveGame();
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
        if (!scene.isLoaded) return;
        sceneObjects = scene.GetRootGameObjects();
        
        doorObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (obj.CompareTag("Section"))
            {
                foreach (var section in obj.GetComponentsInChildren<DoorManager>())
                    doorObjects.Add(section.gameObject);
            }
        }
        
        spawnerObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (obj.CompareTag("Spawner"))
            {
                foreach (var spawner in obj.GetComponentsInChildren<EntitySpawner>()) 
                    spawnerObjects.Add(spawner.gameObject);
                foreach (var spawner in obj.GetComponentsInChildren<ItemSpawner>()) 
                    spawnerObjects.Add(spawner.gameObject);
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
    
    private static void SaveDataOnDialogueClose(ScriptableObject dialogue = null) => GameEvents.TriggerSaveGame();

    private void InitialiseSceneData()
    {
        ClearSceneData(); // Clear any editor-time or dummy data
        CollateObjects(); // Find objects in the scene
        UpdateLists(); // Capture their initial state (but don't persist yet)
    }

    public void SaveSceneData()
    {
        if (!sceneLoaded) return;
        CollateObjects();
        UpdateLists();
    }

    private void UpdateLists()
    {
        ClearSceneData();
        foreach (var obj in sceneObjects)
        {
            if (obj != null)
                switch (obj.tag)
                {
                    case "Enemy":
                        var enemyId = obj.GetComponent<Identifier>();
                        var newEnemy = new EnemySceneData
                        {
                            activeEnemies = obj,
                            spawnId = enemyId.spawnId
                        };
                        enemySceneData.Add(newEnemy);
                        if (!enemyIds.Contains(enemyId.spawnId))
                            enemyIds.Add(enemyId.spawnId);
                        break;
                    case "Boss":
                        bossHealth = obj.GetComponent<BossHealth>();
                        bossSceneData.activeBoss = obj.name;
                        break;
                    case "NPC":
                        var npcId = obj.GetComponent<NPChat>().npc?.npcName;
                        if (npcId == null) continue;
                        var dTrigger = obj.GetComponent<NPChat>();
                        obj.TryGetComponent<ShopKeeper>(out var shopKeeper);
                        var newNpc = new NpcSceneData
                        {
                            activeNpc = obj,
                            npcName = npcId,
                            willTravel = dTrigger.willTravel,
                            dialogue = new List<DialogueObject>(dTrigger.dialogue),
                            isShopKeeper = shopKeeper != null,
                            shopItems = shopKeeper != null ? shopKeeper.shopItems.ToArray() : null
                        };
                        npcSceneData.Add(newNpc);
                        break;
                    case "Item":
                        var collectable = obj.GetComponent<Collectable>();
                        var newCollectable = new CollectableSceneData
                        {
                            activeCollectables = obj,
                            collectable = collectable.collectable,
                            itemColour = obj.GetComponent<SpriteRenderer>().color,
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
            var doorObjective = door.GetComponent<DoorManager>();
            var newDoor = new DoorSceneData
            {
                activeDoors = door,
                doorManager = doorObjective,
                id = doorId.id,
                isOpen = doorObjective.isOpen
            };
            doorSceneData.Add(newDoor);
        }
        foreach (var spawner in spawnerObjects)
        {
            var spawnerId = spawner.GetComponent<Identifier>();
            var itemSpawner = spawner.GetComponent<ItemSpawner>();
            var entitySpawner = spawner.GetComponent<EntitySpawner>();
            var newSpawner = new SpawnerSceneData
            {
                activeSpawner = spawner,
                itemSpawner = itemSpawner,
                id = spawnerId.id,
                isOneTime = itemSpawner != null ? itemSpawner.isOneTime : (entitySpawner != null && entitySpawner.isOneTime)
            };
            spawnerSceneData.Add(newSpawner);
        }
    }
    
    public void MakeDataPersistent()
    {
        if (!sceneLoaded || PlayerHealth.Instance == null) return;
        var manager = DataManager.Instance;
        var list = manager.sceneData.Find(x => x.sceneName == sceneName);

        if (list == null)
        {
            list = new DataManager.SceneList();
            manager.sceneData.Add(list);
        }

        list.sceneName = sceneName;
        list.enemies.Clear();
        list.collectables.Clear();
        list.doors.Clear();
        list.spawners.Clear();

        foreach (var eData in enemySceneData)
        {
            if (eData.activeEnemies == null)
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
                colour = cData.itemColour,
                position = cData.position,
                isStray = cData.isStray
            };
            list.collectables.Add(newCollectable);
        }

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
                    shopItems = nData.shopItems
                });
            }
            else
            {
                npc.dialogue = nData.dialogue.ToArray();
                npc.shopItems = nData.shopItems;
            }
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
    }

    public void LoadSceneData()
    {
        var manager = DataManager.Instance;
        if (manager == null || manager.sceneData.Count == 0 && manager.npcs.Count == 0)
        {
            sceneLoaded = true;
            return;
        }

        foreach (var npcData in manager.npcs)
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

        var hasSceneData = manager.sceneData.Exists(x => x.sceneName == sceneName);
        if (!hasSceneData) return;
        
        foreach (var list in manager.sceneData)
        {
            if (list.sceneName != sceneName) continue;
            
            foreach (var enemyData in list.enemies)
            {
                var enemy = enemySceneData.Find(e => e.spawnId == enemyData.activeEnemyID);
                if (enemy != null && enemy.activeEnemies != null) 
                    Destroy(enemy.activeEnemies);
            }

            foreach (var collectableData in list.collectables)
            {
                if (collectableData.isStray)
                {
                    var item = Instantiate(list.ItemToStore, collectableData.position, Quaternion.identity);
                    item.GetComponent<SpriteRenderer>().color = collectableData.colour;
                    item.GetComponent<Collectable>().collectable = collectableData.activeCollectable;
                    SceneManager.MoveGameObjectToScene(item, SceneManager.GetSceneByName(sceneName));
                }
            }

            foreach (var doorData in list.doors)
            {
                if (doorData.isOpen)
                {
                    var door = doorSceneData.Find(d => d.id == doorData.activeDoor);
                    if (door != null && door.doorManager != null) door.doorManager.InstantOpen();
                }
            }

            foreach (var spawnerData in list.spawners)
            {
                if (spawnerData.isOneTime)
                {
                    var spawner = spawnerSceneData.Find(s => s.id == spawnerData.activeSpawner);
                    if (spawner != null && spawner.itemSpawner != null)
                    {
                        spawner.itemSpawner.Despawn();
                    }
                }
            }

            bossSceneData.activeBoss = list.activeBoss;
            if (list.isBossDead && bossHealth != null) 
                Destroy(bossHealth.gameObject);
            bossSceneData.willBeNpc = list.willBeNpc;
            
            sceneLoaded = true;
            break;
        }
        if (bossHealth != null && bossSceneData.isDead) 
            Destroy(bossHealth.gameObject);
            
        sceneLoaded = true;
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
        [Tooltip("Ensure these are spawners unless they are one-time enemies")] 
        public GameObject activeEnemies; 
        public int spawnId;
    }

    [Serializable]
    public class BossSceneData
    {
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
        public bool isShopKeeper;
        public ShopItem[] shopItems;
    }

    [Serializable]
    public class CollectableSceneData
    {
        public GameObject activeCollectables;
        public CollectableSOBase collectable;
        public Color itemColour = Color.white;
        public Vector3 position;
        public bool isStray;
    }

    [Serializable]
    public class DoorSceneData
    {
        public GameObject activeDoors;
        public DoorManager doorManager;
        public int id;
        public bool isOpen;
    }

    [Serializable]
    public class SpawnerSceneData
    {
        public GameObject activeSpawner;
        public ItemSpawner itemSpawner;
        public int id;
        public bool isOneTime;
    }
#endregion