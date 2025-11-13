using System;
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
    public List<EnemySceneData> enemySceneData = new List<EnemySceneData>();
    public BossSceneData bossSceneData;
    public List<NpcSceneData> npcSceneData = new List<NpcSceneData>();
    public List<CollectableSceneData> collectableSceneData = new List<CollectableSceneData>();
    public List<DoorSceneData> doorSceneData = new List<DoorSceneData>();
    
    #endregion

    private void Awake()
    {
        LoadSceneData();
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
        }
        if (!string.IsNullOrEmpty(bossSceneData.activeBoss)) bossSceneData.isDead = GameObject.Find(bossSceneData.activeBoss).gameObject.GetComponent<BossHealth>().isDead;
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

        if (collision.gameObject.layer == LayerMask.NameToLayer("Enemy") &&
            !collision.gameObject.GetComponent<EnemyHealth>().isDead)
        {
            enemySceneData.Add(new EnemySceneData());
            foreach (var eData in enemySceneData)
                if (eData.activeEnemies == null)
                {
                    eData.activeEnemies = collision.gameObject;
                    eData.isOneTime = collision.gameObject.GetComponent<EnemyHealth>().isOneTime;
                    return;
                }
        }

        if (collision.gameObject.layer == LayerMask.NameToLayer("Boss"))
            bossSceneData.activeBoss = collision.gameObject.name;

        if (collision.CompareTag("NPC"))
        {
            npcSceneData.Add(new NpcSceneData());
            foreach (var nData in npcSceneData)
            {
                nData.activeNpcs = collision.gameObject;
                nData.willTravel = collision.gameObject.GetComponent<DialogueTrigger>().willTravel;
            }
        }

        if (collision.gameObject.layer == LayerMask.NameToLayer("Loot"))
        {
            collectableSceneData.Add(new CollectableSceneData());
            foreach (var cData in collectableSceneData) cData.activeCollectables = collision.gameObject;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") || collision.CompareTag("Dead")) SaveSceneData();
    }

    private void SaveSceneData()
    {
        var manager = SceneDataManager.Instance;
        manager.sceneData.Add(new SceneDataManager.SceneList());
        foreach (var scene in manager.sceneData)
        {
            if (scene.sceneName != sceneName.SceneName)
            {
                scene.sceneName = sceneName.SceneName;
                
                for (var i = 0; i < enemySceneData.Count; i++)
                {
                    scene.enemies.Add(new SceneDataManager.EnemySceneData());
                    scene.enemies[i].activeEnemies = enemySceneData[i].activeEnemies.GetComponent<Identifier>().id;
                    scene.enemies[i].isOneTime = enemySceneData[i].isOneTime;
                }
                
                for (var i = 0; i < collectableSceneData.Count; i++)
                {
                    scene.collectables.Add(new SceneDataManager.CollectableSceneData());
                    scene.collectables[i].activeCollectables = collectableSceneData[i].activeCollectables;
                }
                
                for (var i = 0; i < npcSceneData.Count; i++)
                {
                    scene.npcs.Add(new SceneDataManager.NpcSceneData());
                    //scene.npcs[i].activeNpcs = npcSceneData[i].activeNpcs;
                    scene.npcs[i].willTravel = npcSceneData[i].willTravel;
                }
                
                for (var i = 0; i < doorSceneData.Count; i++)
                {
                    scene.doors.Add(new SceneDataManager.DoorSceneData());
                    scene.doors[i].activeDoors = doorSceneData[i].activeDoors.GetComponent<Identifier>().id;
                    scene.doors[i].isOpen = doorSceneData[i].isOpen;
                }
                
                scene.activeBoss = bossSceneData.activeBoss;
                scene.isDead = bossSceneData.isDead;
                scene.willBeNpc = bossSceneData.willBeNpc;
                
                return;
            }
        }
        manager.sceneData.RemoveAt(manager.sceneData.Count);
    }

    private void LoadSceneData()
    {
        var manager = SceneDataManager.Instance;

        foreach (var scene in manager.sceneData)
            if (scene.sceneName == sceneName.SceneName)
            {
                for (var i = 0; i < scene.enemies.Count; i++)
                {
                    if (scene.enemies[i].isOneTime && scene.enemies[i].activeEnemies == enemySceneData[i].activeEnemies.GetComponent<Identifier>().id)
                        Destroy(enemySceneData[i].activeEnemies.gameObject);
                }

                for (var i = 0; i < scene.collectables.Count; i++)
                    collectableSceneData[i].activeCollectables = scene.collectables[i].activeCollectables;

                for (var i = 0; i < scene.npcs.Count; i++)
                {
                    //npcSceneData[i].willTravel = scene.npcs[i].willTravel;
                }

                for (var i = 0; i < scene.doors.Count; i++)
                {
                    var correctDoor = FindObjectsByType<DoorObjective>(FindObjectsSortMode.None);
                    foreach (var d in correctDoor) 
                        if (scene.doors[i].isOpen && doorSceneData[i].activeDoors.GetComponent<Identifier>().id == scene.doors[i].activeDoors) 
                            d.CompleteObjective();
                }

                bossSceneData.activeBoss = scene.activeBoss;
                if (scene.isDead) Destroy(GameObject.Find(bossSceneData.activeBoss).gameObject);
                bossSceneData.willBeNpc = scene.willBeNpc;
                return;
            }
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
    }

    [Serializable]
    public class DoorSceneData
    {
        public GameObject activeDoors;
        public bool isOpen = true;
    }
    #endregion
    





