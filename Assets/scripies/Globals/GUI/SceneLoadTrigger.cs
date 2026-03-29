using System;
using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneLoadTrigger : MonoBehaviour
{
    [SerializeField] private SceneField[] scenesToLoad;
    [SerializeField] private SceneField[] scenesToUnload;
    
    private GameObject player;
    void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player");
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject == player)
        { 
            LoadScenes();
            UnloadScenes();
        }
    }

    private void LoadScenes()
    {
        foreach (var scene in scenesToLoad)
        {
            var isSceneLoaded = false;
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name == scene.SceneName)
                {
                    isSceneLoaded = true;
                    break;
                }

                /*var sceneObjects = loadedScene.GetRootGameObjects();
                foreach (var obj in sceneObjects)
                {
                    if (obj.name == "Map")
                    {
                        obj.GetComponent<MapManager>().LoadSceneData();
                        print($"Loaded {scene.SceneName}");
                        break;
                    }
                    print($"This aint a map, its {obj.name} >:(");
                }*/
            }
            if (!isSceneLoaded) SceneManager.LoadSceneAsync(scene.SceneName, LoadSceneMode.Additive);
        }
    }

    private void UnloadScenes()
    {
        foreach (var scene in scenesToUnload)
        {
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name == scene.SceneName)
                {
                    var sceneObjects = loadedScene.GetRootGameObjects();
                    foreach (var obj in sceneObjects)
                    {
                        if (obj.name == "Map")
                        {
                            obj.GetComponent<MapManager>().SaveSceneData();
                            obj.GetComponent<MapManager>().MakeDataPersistent();
                            break;
                        }
                    }
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
        }
    }
}
