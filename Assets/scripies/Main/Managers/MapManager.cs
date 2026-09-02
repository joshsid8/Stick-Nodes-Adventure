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
    [SerializeField] private List<GameObject> doorObjects = new List<GameObject>();
    [SerializeField] private List<GameObject> spawnerObjects = new List<GameObject>();
    
    public static Dictionary<string, MapManager> Instances = new Dictionary<string, MapManager>();
    
    public List<EnemySceneData> enemySceneData = new List<EnemySceneData>();
    public List<int> enemyIds = new List<int>();
    public BossHealth bossHealth;
    public BossSceneData bossSceneData;
    private CinemachineCamera mainCamera;
    public List<NpcSceneData> npcSceneData = new List<NpcSceneData>();
    public List<CollectableSceneData> collectableSceneData = new List<CollectableSceneData>();
    public List<DoorSceneData> doorSceneData = new List<DoorSceneData>();
    public List<SpawnerSceneData> spawnerSceneData = new List<SpawnerSceneData>();
    #endregion

    private void Awake() { if (sectionObj == null || sectionObj.IsDestroyed()) sectionObj = GameObject.Find("Sections"); }
    
    private IEnumerator Start()
    {
        sceneName = gameObject.scene.name;
        mainCamera = SceneDataManager.Instance.mainCamera.GetComponent<CinemachineCamera>();
        weather = GetComponent<WeatherManager>();
        audioManager = AudioManager.Instance;
        SaveSceneData();
        yield return new WaitForSeconds(0.3f);
        if (SceneDataManager.Instance.sceneData.Count > 0) LoadSceneData();
        
    }

    private void Update()
    {
        #if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F1)) SaveSceneData();
        if (Input.GetKeyDown(KeyCode.F2)) MakeDataPersistent();
        if (Input.GetKeyDown(KeyCode.F3)) LoadSceneData();
        #endif

        if (!activeMap) return;
        var player = PlayerHealth.Instance;
        var playerCol = player.GetComponent<Collider2D>();
    }
    
    private void OnBossDefeated() => bossSceneData.isDead = true;

    public void OnDoorStatusChanged(DoorObjective doorObjective, bool isOpen)
    {
        var dData = doorSceneData.Find(x => x.doorObjective == doorObjective);
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
    }
    

    private void OnDisable()
    {
        if (Instances.ContainsKey(gameObject.scene.name) && Instances[gameObject.scene.name] == this)
            Instances.Remove(gameObject.scene.name);
        GameEvents.OnBossDefeated -= OnBossDefeated;
        GameEvents.OnPlayerSpawned -= StartMap;
        GameEvents.OnDialogueClose -= SaveDataOnDialogueClose;
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
        audioManager.TriggerFadeOut();
        weather.ClearDisasters();
        audioManager.previousScene = playerObject.scene;
        if (playerObject != null && SceneManager.GetSceneByName("Persistent Objects").isLoaded) 
            SceneManager.MoveGameObjectToScene(playerObject, SceneManager.GetSceneByName("Persistent Objects"));
        mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = PlayerHealth.Instance.gameObject.GetComponentInChildren<PolygonCollider2D>();
    }

    private void CollateObjects()
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        sceneObjects = scene.GetRootGameObjects();
        
        doorObjects.Clear();
        foreach (var obj in sceneObjects)
        {
            if (obj.CompareTag("Section"))
            {
                foreach (var section in obj.GetComponentsInChildren<DoorObjective>())
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
            if (obj.GetComponentsInChildren<EntitySpawner>() != null)
            {
                foreach (var spawner in obj.GetComponentsInChildren<EntitySpawner>()) 
                    spawner.enabled = false;
            }
            obj.gameObject.SetActive(false);
        }
    }
    
    private void SaveDataOnDialogueClose(ScriptableObject dialogue = null) 
    {
        SaveSceneData();
        Invoke(nameof(MakeDataPersistent), 0.1f);
    }
    
    public void SaveSceneData()
    {
        ClearSceneData();
        CollateObjects();
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
                        var npcId = obj.GetComponent<Identifier>();
                        var dTrigger = obj.GetComponent<NPChat>();
                        var newNpc = new NpcSceneData
                        {
                            activeNpcs = obj,
                            id = npcId.id,
                            willTravel = dTrigger.willTravel,
                            dialogue = new List<DialogueObject>(dTrigger.dialogue)
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
            var doorObjective = door.GetComponent<DoorObjective>();
            var newDoor = new DoorSceneData
            {
                activeDoors = door,
                doorObjective = doorObjective,
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
        if (PlayerHealth.Instance == null) return;
        var manager = SceneDataManager.Instance;
        var list = manager.sceneData.Find(x => x.sceneName == sceneName);

        if (list == null)
        {
            list = new SceneDataManager.SceneList();
            manager.sceneData.Add(list);
        }

        list.sceneName = sceneName;
        list.enemies.Clear();
        list.collectables.Clear();
        list.npcs.Clear();
        list.doors.Clear();
        list.spawners.Clear();

        foreach (var eData in enemySceneData)
        {
            if (eData.activeEnemies == null)
            {
                list.enemies.Add(new SceneDataManager.EnemySceneData
                {
                    activeEnemyID = eData.spawnId
                });
            }
        }

        foreach (var cData in collectableSceneData)
        {
            var newCollectable = new SceneDataManager.CollectableSceneData
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
            if (nData.activeNpcs != null && nData.id > 0)
            {
                list.npcs.Add(new SceneDataManager.NpcSceneData
                {
                    activeNpc = nData.id,
                    willTravel = nData.willTravel,
                    dialogue = nData.dialogue.ToArray()
                });
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
            list.doors.Add(new SceneDataManager.DoorSceneData
            {
                activeDoor = dData.id,
                isOpen = dData.isOpen
            });
        }

        foreach (var sData in spawnerSceneData)
        {
            list.spawners.Add(new SceneDataManager.SpawnerSceneData
            {
                activeSpawner = sData.id,
                isOneTime = sData.isOneTime
            });
        }
    }

    public void LoadSceneData()
    {
        var manager = SceneDataManager.Instance;

        foreach (var list in manager.sceneData)
        {
            if (list.sceneName == sceneName)
            {
                foreach (var enemyData in list.enemies)
                {
                    var enemy = enemySceneData.Find(e => e.spawnId == enemyData.activeEnemyID);
                    if (enemy != null && enemy.activeEnemies != null)
                    {
                        Destroy(enemy.activeEnemies);
                    }
                }

                foreach (var npcData in list.npcs)
                {
                    var npc = npcSceneData.Find(n => n.id == npcData.activeNpc);
                    if (npc != null && npc.activeNpcs != null)
                    {
                        var dTrigger = npc.activeNpcs.GetComponent<NPChat>();
                        if (dTrigger != null)
                        {
                            dTrigger.dialogue.Clear();
                            dTrigger.dialogue.AddRange(npcData.dialogue);
                        }
                        
                        if (npcData.willTravel)
                        {
                            Destroy(npc.activeNpcs);
                        }
                    }
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
                        if (door != null && door.doorObjective != null) door.doorObjective.InstantOpen();
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
                return;
            }
        }
        if (bossHealth != null && bossSceneData.isDead) 
            Destroy(bossHealth.gameObject);
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
        public GameObject activeNpcs;
        public int id;
        [Tooltip("Will this NPC travel throughout scenes (areas)?")] 
        public bool willTravel;
        public List<DialogueObject> dialogue = new List<DialogueObject>();
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
        public DoorObjective doorObjective;
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