using System;
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

    #endregion

    #region Scene Data
    
    [SerializeField] private SceneField sceneName; 
    [SerializeField] private GameObject[] sceneObjects;
    
    public List<EnemySceneData> enemySceneData = new List<EnemySceneData>();
    public List<int> enemyIds = new List<int>();
    public BossSceneData bossSceneData;
    public List<NpcSceneData> npcSceneData = new List<NpcSceneData>();
    public List<CollectableSceneData> collectableSceneData = new List<CollectableSceneData>();
    public List<DoorSceneData> doorSceneData = new List<DoorSceneData>();
    
    #endregion

    void Start()
    {
        if (SceneDataManager.Instance.sceneData != null) LoadSceneData();
    }

    private void Update()
    {
        var scene = SceneManager.GetSceneByName(sceneName.SceneName);
        sceneObjects = scene.GetRootGameObjects();
        SaveSceneData();
        MakeDataPersistent();
        
        if (doorSceneData.Count > 0 || doorSceneData != null)
        {
            foreach (var dData in doorSceneData)
                foreach (var door in dData.activeDoors.gameObject.GetComponent<DoorObjective>().doorObj) 
                    dData.isOpen = door.objectiveComplete;
            
            if (!string.IsNullOrEmpty(bossSceneData.activeBoss) && GameObject.Find(bossSceneData.activeBoss) != null)
            {
                var boss = GameObject.Find(bossSceneData.activeBoss);
                if (boss.gameObject.GetComponent<BossHealth>().bossHealth <= 0) bossSceneData.isDead = true; //else print($"Clueless: {boss.name} is alive with {boss.gameObject.GetComponent<BossHealth>().bossHealth} health");
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && AreaManager.Instance.titleText.text != mapName || collision.CompareTag("Player") && AreaManager.Instance.titleText.text != null)
        {
            AreaManager.Instance.StartCoroutine(AreaManager.Instance.Announce(mapName, mapDescription, mapTextColor, mapBgColor));
            AudioManager.Instance.musicSource.Stop();
            AudioManager.Instance.backgroundMusic[0] = newOst[0];
            AudioManager.Instance.backgroundMusic[1] = newOst[1];
            AudioManager.Instance.backgroundMusic[2] = newOst[2];
            AudioManager.Instance.musicSource.Play();
            GameObject.Find("Main Camera").GetComponent<CinemachineConfiner2D>().BoundingShape2D = GetComponent<Collider2D>();
        }
        
        //if (collision.CompareTag("Player")) LoadSceneData();
        
    }

    private void ClearSceneData()
    {
        enemySceneData.Clear();
        npcSceneData.Clear();
        collectableSceneData.Clear();
    }

    private void SaveSceneData()
    {
        ClearSceneData();
        foreach (var obj in sceneObjects)
        {
            if (obj != null)
                switch (obj.tag)
                {
                    case "Enemy":
                        var newEnemy = new EnemySceneData
                        {
                            activeEnemies = obj.gameObject,
                            //isOneTime = obj.gameObject.GetComponent<EnemyHealth>().isOneTime
                        };
                        enemySceneData.Add(newEnemy);
                        if (!enemyIds.Contains(obj.gameObject.GetComponent<Identifier>().spawnId)) enemyIds.Add(obj.gameObject.GetComponent<Identifier>().spawnId);
                        break;
                    case "Boss":
                        bossSceneData.activeBoss = obj.name;
                        break;
                    case "NPC":
                        var newNpc = new NpcSceneData
                        {
                            activeNpcs = obj.gameObject,
                            willTravel = obj.GetComponent<DialogueTrigger>().willTravel
                        };
                        npcSceneData.Add(newNpc);
                        break;
                    case "Item":
                        var newCollectable = new CollectableSceneData
                        {
                            activeCollectables = obj.gameObject,
                            itemColour = obj.GetComponent<SpriteRenderer>().color,
                            position = obj.transform.position
                        };
                        collectableSceneData.Add(newCollectable);
                        break;
                }
        }
    }
    
    private void MakeDataPersistent()
    {
        var manager = SceneDataManager.Instance;
        var list = manager.sceneData.Find(x => x.sceneName == sceneName.SceneName);

        if (list == null)
        {
            list = new SceneDataManager.SceneList();
            manager.sceneData.Add(list);
        }

        list.sceneName = sceneName.SceneName;
        list.enemies.Clear();
        list.collectables.Clear();
        list.npcs.Clear();
        list.doors.Clear();

        for (var i = 0; i < enemySceneData.Count; i++)
        {
            list.enemies.Add(new SceneDataManager.EnemySceneData());
            list.enemies[i].activeEnemyID = enemySceneData[i].activeEnemies.GetComponent<Identifier>().spawnId;
            //list.enemies[i].isOneTime = enemySceneData[i].isOneTime;
        }

        for (var i = 0; i < collectableSceneData.Count; i++)
        {
            list.collectables.Add(new SceneDataManager.CollectableSceneData());
            if (collectableSceneData[i].activeCollectables != null) list.collectables[i].activeCollectable = collectableSceneData[i].activeCollectables.gameObject.GetComponent<Collectable>().collectable;
            list.collectables[i].colour = collectableSceneData[i].itemColour;
            list.collectables[i].position = collectableSceneData[i].position;
            
        }

        for (var i = 0; i < npcSceneData.Count; i++)
        {
            list.npcs.Add(new SceneDataManager.NpcSceneData());
            list.npcs[i].activeNpc = npcSceneData[i].activeNpcs.GetComponent<Identifier>().id;
            list.npcs[i].willTravel = npcSceneData[i].willTravel;
        }
        
        if (bossSceneData.activeBoss != null)
        {
            list.activeBoss = bossSceneData.activeBoss;
            if (bossSceneData.isDead) list.isBossDead = true;
            list.willBeNpc = bossSceneData.willBeNpc;
        }
        
        for (var i = 0; i < doorSceneData.Count; i++)
        {
            if (list.doors == null) break;
            list.doors.Add(new SceneDataManager.DoorSceneData());
            list.doors[i].activeDoor = doorSceneData[i].activeDoors.gameObject.GetComponent<Identifier>().id;
            list.doors[i].isOpen = doorSceneData[i].isOpen;
        }
    }

    public void LoadSceneData()
    {
        var manager = SceneDataManager.Instance;

        foreach (var list in manager.sceneData)
        {
            if (list.sceneName == sceneName.SceneName)
            {
                for (var i = 0; i < list.enemies.Count; i++)
                {
                    /*if (list.enemies[i].isOneTime && list.enemies[i].activeEnemyID ==
                        enemySceneData[i].activeEnemies.gameObject.GetComponent<Identifier>().spawnId)
                    {
                        print($"{enemySceneData[i].activeEnemies.name} has been killed prior!");
                        Destroy(enemySceneData[i].activeEnemies.gameObject);
                    }*/
                }

                for (var i = 0; i < list.collectables.Count; i++)
                {
                    var item = Instantiate(list.ItemToStore, list.collectables[i].position, Quaternion.identity);
                    item.GetComponent<SpriteRenderer>().color = list.collectables[i].colour;
                    item.GetComponent<Collectable>().collectable = list.collectables[i].activeCollectable;
                    SceneManager.MoveGameObjectToScene(item, SceneManager.GetSceneByName(sceneName.SceneName));
                }

                for (var i = 0; i < list.doors.Count; i++)
                {
                    if (list.doors[i].isOpen && doorSceneData[i].activeDoors.gameObject.GetComponent<Identifier>().id == list.doors[i].activeDoor) 
                        doorSceneData[i].activeDoors.gameObject.GetComponent<DoorObjective>().InstantOpen();
                }
                
                bossSceneData.activeBoss = list.activeBoss;
                if (list.isBossDead && GameObject.Find(bossSceneData.activeBoss) != null) 
                    Destroy(GameObject.Find(bossSceneData.activeBoss).gameObject);
                bossSceneData.willBeNpc = list.willBeNpc;
                return;
            }
        }
        if (!string.IsNullOrEmpty(bossSceneData.activeBoss) && bossSceneData.activeBoss == 
            GameObject.Find(bossSceneData.activeBoss).name && bossSceneData.isDead) 
            Destroy(GameObject.Find(bossSceneData.activeBoss).gameObject);
    }
}
    
#region Scene data classes
    [Serializable]
    public class EnemySceneData
    {
        [Tooltip("Ensure these are spawners unless they are one-time enemies")] 
        public GameObject activeEnemies; 
        //[Tooltip("Will this enemy spawn again once the scene loads?")] 
        //public bool isOneTime;
        //[Tooltip("Has this enemy died before the player left the scene?")]
        //public bool isDead;
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
        [Tooltip("Will this NPC travel throughout scenes (areas)?")] 
        public bool willTravel;
    }

    [Serializable]
    public class CollectableSceneData
    {
        public GameObject activeCollectables;
        public Color itemColour = Color.white;
        public Vector3 position;
    }

    [Serializable]
    public class DoorSceneData
    {
        public GameObject activeDoors;
        public bool isOpen = true;
    }
#endregion
    





