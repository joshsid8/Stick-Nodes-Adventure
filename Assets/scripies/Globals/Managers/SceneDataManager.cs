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
        
        public string activeBoss;
        public bool isDead;
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
        public GameObject activeEnemies;
        public bool isOneTime;
    }
    
    [Serializable]
    public class NpcSceneData
    {
        public GameObject activeNpcs;
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

    public void Save(ref SceneList data)
    {
        
    }
}

