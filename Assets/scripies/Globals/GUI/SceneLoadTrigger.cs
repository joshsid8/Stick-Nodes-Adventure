using System;
using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneLoadTrigger : MonoBehaviour
{
    [SerializeField] private SceneField[] scenesToLoad;
    [SerializeField] private SceneField[] scenesToUnload;
    
    private GameObject player;
    public GameObject[] sceneObjs;
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
        for (var i = 0; i < scenesToLoad.Length; i++)
        {
            var isSceneLoaded = false;
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name == scenesToLoad[i].SceneName)
                {
                    isSceneLoaded = true;
                    break;
                }
            }

            if (!isSceneLoaded) SceneManager.LoadSceneAsync(scenesToLoad[i].SceneName, LoadSceneMode.Additive);
            
        }
    }

    private void UnloadScenes()
    {
        for (var i = 0; i < scenesToUnload.Length; i++)
        {
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name == scenesToUnload[i]) SceneManager.UnloadSceneAsync(scenesToUnload[i]);
            }
        }
    }
}
