using System;
using System.Collections;
using scripies.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneLoadTrigger : MonoBehaviour
{
    [SerializeField] private SceneField[] scenesToLoad;
    [SerializeField] private SceneField[] scenesToUnload;
    
    private GameObject player;
    private void Start() => player = PlayerHealth.Instance.gameObject;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject == player)
        { 
            LoadScenes();
            StartCoroutine(UnloadScenes());
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
                
            }
            if (!isSceneLoaded) SceneManager.LoadSceneAsync(scene.SceneName, LoadSceneMode.Additive);
        }
    }

    private IEnumerator UnloadScenes()
    {
        foreach (var scene in scenesToUnload)
        {
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name == scene.SceneName && loadedScene.isLoaded)
                {
                    if (MapManager.Instances.TryGetValue(scene.SceneName, out var mapManager))
                    {
                        mapManager.SaveSceneData();
                        mapManager.DisableSpawners();
                        yield return new WaitForSeconds(0.1f);
                    }
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
        }
    }
}
