using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;

public class MapManager : MonoBehaviour
{
    
    #region Map Info and Ost

    [SerializeField] private string mapName;
    [SerializeField] [TextArea] private string mapDescription;
    [SerializeField] private Color mapTextColor = Color.white;
    [SerializeField] private Color mapBgColor = Color.white;
    [SerializeField] private AudioClip[] newOst;
    public float[] ostVolume = {0.5f,0.5f,0.5f};

    #endregion

    #region Scene Data
    
    private string sceneName; 
    [SerializeField] private GameObject[] sceneObjects;
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

    private IEnumerator Start()
    {
        sceneName = gameObject.scene.name;
        mainCamera = SceneDataManager.Instance.mainCamera.GetComponent<CinemachineCamera>();
        yield return new WaitForSeconds(0.05f);
        SaveSceneData();
        if (SceneDataManager.Instance.sceneData.Count > 0) LoadSceneData();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) SaveSceneData();
        if (Input.GetKeyDown(KeyCode.F2)) MakeDataPersistent();
        if (Input.GetKeyDown(KeyCode.F3)) LoadSceneData();
    }
    
    public void OnBossDefeated() => bossSceneData.isDead = true;

    public void OnDoorStatusChanged(DoorObjective doorObjective, bool isOpen)
    {
        var dData = doorSceneData.Find(x => x.doorObjective == doorObjective);
        if (dData != null) dData.isOpen = isOpen;
    }

    private void OnEnable()
    {
        if (!Instances.ContainsKey(gameObject.scene.name))
            Instances.Add(gameObject.scene.name, this);
    }

    private void OnDisable()
    {
        if (Instances.ContainsKey(gameObject.scene.name) && Instances[gameObject.scene.name] == this)
            Instances.Remove(gameObject.scene.name);
    }

    private IEnumerator OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && AreaManager.Instance.titleText.text != mapName || collision.CompareTag("Player") && AreaManager.Instance.titleText.text != null)
        {
            yield return new WaitForSeconds(0.1f);
            GetComponent<WeatherManager>().SetWeather();
        }
    }

    public void UpdateNormalAreaInfo() => UpdateAreaInfo(mapName, mapDescription, mapTextColor, mapBgColor, newOst, ostVolume);
    public void UpdateAreaInfo(string mName, string mDesc, Color mTextColor, Color mBgColor, AudioClip[] mOst, float[] mOstVolume)
    {
        AreaManager.Instance.StartCoroutine(AreaManager.Instance.Announce(mName, mDesc, mTextColor, mBgColor));
        AudioManager.Instance.musicSource.Stop();
        for (var i = 0; i < AudioManager.Instance.backgroundMusic.Length; i++)
        {
            AudioManager.Instance.backgroundMusic[i] = mOst[i];
            AudioManager.Instance.backgroundMusicVolume[i] = mOstVolume[i];
        }

        AudioManager.Instance.musicSource.Play();
        if (mainCamera != null) mainCamera.GetComponent<CinemachineConfiner2D>().BoundingShape2D = GetComponent<Collider2D>();
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player")) MakeDataPersistent();
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
                {
                    spawnerObjects.Add(spawner.gameObject);
                }
                
                foreach (var spawner in obj.GetComponentsInChildren<ItemSpawner>())
                {
                    spawnerObjects.Add(spawner.gameObject);
                }
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
                        var newNpc = new NpcSceneData
                        {
                            activeNpcs = obj,
                            id = npcId.id,
                            willTravel = obj.GetComponent<DialogueTrigger>().willTravel
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
            if (nData.activeNpcs == null)
            {
                list.npcs.Add(new SceneDataManager.NpcSceneData
                {
                    activeNpc = nData.id,
                    willTravel = nData.willTravel
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

    private void LoadSceneData()
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
                        Destroy(npc.activeNpcs);
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