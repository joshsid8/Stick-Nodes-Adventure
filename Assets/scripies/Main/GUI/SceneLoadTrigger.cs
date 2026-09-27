using System;
using System.Collections;
using scripies.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneLoadTrigger : MonoBehaviour
{
    [SerializeField] private SceneField[] scenesToLoad;
    [SerializeField] private SceneField[] scenesToUnload;
    [SerializeField] private bool unloadEveryOtherScene;
    
    private GameObject player;
    private void Start() => player = PlayerHealth.Instance.gameObject;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject != player) return;
        if (unloadEveryOtherScene) 
            StartCoroutine(LoadSingularScene());
        else
        {
            LoadScenes();
            UnloadScenes();
        }
    }

    [ContextMenu("Load Scenes")]
    public void LoadScenes()
    {
        foreach (var scene in scenesToLoad)
        {
            var isSceneLoaded = false;
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name != scene.SceneName) continue;
                isSceneLoaded = true;
                break;
            }
            if (!isSceneLoaded) SceneManager.LoadSceneAsync(scene.SceneName, LoadSceneMode.Additive);
        }
    }
    
    [ContextMenu("Unload Scenes")] private void UnloadScenes() => StartCoroutine(UnloadScenesC());
    private IEnumerator UnloadScenesC()
    {
        foreach (var scene in scenesToUnload)
        {
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name != scene.SceneName || !loadedScene.isLoaded) continue;
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
    
    [ContextMenu("Singularise target Scene")] private IEnumerator LoadSingularScene()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene.name == gameObject.scene.name || scene.name == "Persistent Objects") continue;
            if (MapManager.Instances.TryGetValue(scene.name, out var mapManager))
            {
                mapManager.SaveSceneData();
                mapManager.DisableSpawners();
                yield return new WaitForSeconds(0.1f);
            }
            SceneManager.UnloadSceneAsync(scene);
        }
    } 
}
