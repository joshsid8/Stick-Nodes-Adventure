using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;

public class MapInfo : MonoBehaviour
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

    IEnumerator Start()
    {
        yield return new WaitForSeconds(0.1f);
        var scene = SceneManager.GetSceneByName(sceneName.SceneName);
        sceneObjects = scene.GetRootGameObjects();
        
        SaveSceneData();
    }

    private void Update()
    {
        if (doorSceneData.Count > 0 || doorSceneData != null)
        {
            foreach (var dData in doorSceneData)
            {
                foreach (var door in dData.activeDoors.gameObject.GetComponent<DoorObjective>().doorObj)
                    dData.isOpen = door.objectiveComplete;
            }

            if (!string.IsNullOrEmpty(bossSceneData.activeBoss))
            {
                var boss = GameObject.Find(bossSceneData.activeBoss);
                if (boss.gameObject.GetComponent<BossHealth>().bossHealth <= 0) bossSceneData.isDead = true;
            }
            
        }
        
        //try {print($"we have found {GameObject.Find(bossSceneData.activeBoss).gameObject.name}!");} catch {print("clueless");}
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
        
        if (collision.CompareTag("Player")) LoadSceneData();
        
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") || collision.CompareTag("Dead")) SaveSceneData();
    }

    private void SaveSceneData()
    {
        foreach (var obj in sceneObjects)
        {
            if (obj != null)
                switch (obj.tag)
                {
                    case "Enemy":
                        enemySceneData.Add(new EnemySceneData());
                        for (var i = 0; i < enemySceneData.Count; i++)
                        {
                            if (enemySceneData[i].activeEnemies == null)
                            {
                                enemySceneData[i].activeEnemies = obj.gameObject;
                                enemySceneData[i].isOneTime = obj.gameObject.GetComponent<EnemyHealth>().isOneTime;
                            }
                        }
                        break;
                    case "Boss":
                        bossSceneData.activeBoss = obj.name;
                        //if (obj.gameObject.GetComponent<BossHealth>().bossHealth <= 0) bossSceneData.isDead = true;
                        break;
                    case "NPC":
                        npcSceneData.Add(new NpcSceneData());
                        for (var i = 0; i < npcSceneData.Count; i++)
                        {
                            if (npcSceneData[i].activeNpcs == null)
                            {
                                npcSceneData[i].activeNpcs = obj.gameObject;
                                npcSceneData[i].willTravel = obj.GetComponent<DialogueTrigger>().willTravel;
                            }
                        }
                        break;
                    case "Item":
                        collectableSceneData.Add(new CollectableSceneData());
                        foreach (var cData in collectableSceneData)
                        {
                            if (cData == null) break;
                            cData.activeCollectables = obj.gameObject;
                            cData.position = obj.transform.position;
                        }
                        break;
                    //default: print($"Clueless: {obj.name} is {obj.tag}"); break;
                }
        }
        
        var manager = SceneDataManager.Instance;
        manager.sceneData.Add(new SceneDataManager.SceneList());
        foreach (var list in manager.sceneData)
        {
            if (list.sceneName == null || list.sceneName == sceneName.SceneName)
            {
                list.sceneName = sceneName.SceneName;

                for (var i = 0; i < enemySceneData.Count; i++)
                {
                    list.enemies.Add(new SceneDataManager.EnemySceneData());
                    //list.enemies[i].activeEnemies = enemySceneData[i].activeEnemies.GetComponent<Identifier>().spawnId;
                    list.enemies[i].isOneTime = enemySceneData[i].isOneTime;
                }

                for (var i = 0; i < collectableSceneData.Count; i++)
                {
                    list.collectables.Add(new SceneDataManager.CollectableSceneData());
                    list.collectables[i].activeCollectables = collectableSceneData[i].activeCollectables;
                    list.collectables[i].position = collectableSceneData[i].position;
                }

                for (var i = 0; i < npcSceneData.Count; i++)
                {
                    list.npcs.Add(new SceneDataManager.NpcSceneData());
                    list.npcs[i].activeNpcs = npcSceneData[i].activeNpcs.GetComponent<Identifier>().id;
                    list.npcs[i].willTravel = npcSceneData[i].willTravel;
                }
             
                for (var i = 0; i < doorSceneData.Count; i++)
                {
                    if (list.doors == null) break;
                    list.doors.Add(new SceneDataManager.DoorSceneData());
                    //list.doors[i].activeDoors = doorSceneData[i].activeDoors.gameObject.GetComponent<Identifier>().id;
                    list.doors[i].isOpen = doorSceneData[i].isOpen;
                }
                
                if (bossSceneData.activeBoss != null)
                {
                    list.activeBoss = bossSceneData.activeBoss;
                    list.isBossDead = bossSceneData.isDead;
                    list.willBeNpc = bossSceneData.willBeNpc;
                }
                
                return;
            }
        }
        manager.sceneData.RemoveAt(manager.sceneData.Count);
    }

    private void LoadSceneData()
    {
        var manager = SceneDataManager.Instance;

        foreach (var list in manager.sceneData)
            if (list.sceneName == sceneName.SceneName)
            {
                for (var i = 0; i < list.enemies.Count; i++)
                {
                    if (list.enemies[i].isOneTime && list.enemies[i].activeEnemies == enemySceneData[i].activeEnemies.gameObject.GetComponent<Identifier>().spawnId)
                        Destroy(enemySceneData[i].activeEnemies.gameObject);
                }

                for (var i = 0; i < list.collectables.Count; i++)
                {
                    Instantiate(list.collectables[i].activeCollectables, list.collectables[i].position, Quaternion.identity);
                }
                
                for (var i = 0; i < list.doors.Count; i++)
                {
                    var correctDoor = FindObjectsByType<DoorObjective>(FindObjectsSortMode.None);
                    foreach (var d in correctDoor) 
                        if (list.doors[i].isOpen && doorSceneData[i].activeDoors.gameObject.GetComponent<Identifier>().id
                            == list.doors[i].activeDoors) d.CompleteObjective();
                }

                bossSceneData.activeBoss = list.activeBoss;
                if (list.isBossDead) Destroy(GameObject.Find(bossSceneData.activeBoss).gameObject);
                bossSceneData.willBeNpc = list.willBeNpc;
                return;
            }
        if (!string.IsNullOrEmpty(bossSceneData.activeBoss) && bossSceneData.activeBoss == GameObject.Find(bossSceneData.activeBoss).name && bossSceneData.isDead) 
            Destroy(GameObject.Find(bossSceneData.activeBoss).gameObject);
    }
}
    
#region Scene data classes
    [Serializable]
    public class EnemySceneData
    {
        [Tooltip("Ensure these are spawners unless they are one-time enemies")] 
        public GameObject activeEnemies; 
        [Tooltip("Will this enemy spawn again once the scene loads?")] 
        public bool isOneTime;
        [Tooltip("Has this enemy died before the player left the scene?")]
        public bool isDead;
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
        public Vector3 position;
    }

    [Serializable]
    public class DoorSceneData
    {
        public GameObject activeDoors;
        public bool isOpen = true;
    }
#endregion
    





