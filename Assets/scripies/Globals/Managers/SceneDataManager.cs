using System;
using System.Collections.Generic;
using UnityEngine;

public class SceneDataManager : MonoBehaviour
{
    public static SceneDataManager Instance { get; private set; }

    public List<SceneList> sceneData = new List<SceneList>();
    
    [Serializable]
    public class SceneList
    {
        public string sceneName;
        public List<EnemySceneData> enemies = new List<EnemySceneData>();
        public List<CollectableSceneData> collectables = new List<CollectableSceneData>();
        public List<NpcSceneData> npcs = new List<NpcSceneData>();
        public List<DoorSceneData> doors = new List<DoorSceneData>();

        public GameObject ItemToStore => GameObject.Find("persistentItem");
        
        public string activeBoss;
        public bool isBossDead;
        public bool willBeNpc;
    }
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    #region Scene data classes

    [Serializable]
    public class EnemySceneData
    {
        public int activeEnemyID;
        public bool isOneTime;
    }
    
    [Serializable]
    public class NpcSceneData
    {
        public int activeNpc;
        public bool willTravel;
    }

    [Serializable]
    public class CollectableSceneData
    {
        public CollectableSOBase activeCollectable;
        public Color colour = Color.white;
        public Vector3 position;
    }

    [Serializable]
    public class DoorSceneData
    {
        public int activeDoor;
        public bool isOpen = true;
    }

    #endregion

    public void Save(ref SceneData data)
    {
        for (var i = 0; i < data.sceneData.Count; i++)
        {
            data.sceneData[i].sceneName = sceneData[i].sceneName;
            
        }
    }
}

[Serializable]
public class SceneData
{
    public List<SceneList> sceneData = new List<SceneList>();
    public class SceneList
    {
        public string sceneName;
        public List<EnemySceneData> enemies = new List<EnemySceneData>();
        public List<CollectableSceneData> collectables = new List<CollectableSceneData>();
        public List<NpcSceneData> npcs = new List<NpcSceneData>();
        public List<DoorSceneData> doors = new List<DoorSceneData>();
        
        public string activeBoss;
        public bool isDead;
        public bool willBeNpc;
    }
}
