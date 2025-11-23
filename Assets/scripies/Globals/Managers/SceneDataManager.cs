using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
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
        public string collectableName;
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
        data.realSceneData.Clear();
        data.realSceneData ??= new List<SceneList>();
        data.realSceneData.AddRange(sceneData);
    }

    public void Load(SceneData data)
    {
        sceneData.Clear();
        sceneData = data.realSceneData;

        SceneManager.LoadSceneAsync("Persistent Objects", LoadSceneMode.Single);
        foreach (var scene in sceneData) SceneManager.LoadSceneAsync(scene.sceneName, LoadSceneMode.Additive);
    }
}

[Serializable]
public struct SceneData
{
    public List<SceneDataManager.SceneList> realSceneData;

    public SceneData(bool initialize)
    {
        realSceneData = new List<SceneDataManager.SceneList>();
    }
}
