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
    private void Awake() => player = PlayerHealth.Instance.gameObject;

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
            /*else
            {
                if (MapManager.Instances.TryGetValue(scene.SceneName, out var mapManager)) 
                    mapManager.LoadSceneData();
                
            }*/
        }
    }

    private IEnumerator UnloadScenes()
    {
        foreach (var scene in scenesToUnload)
        {
            for (var j = 0; j < SceneManager.sceneCount; j++)
            {
                var loadedScene = SceneManager.GetSceneAt(j);
                if (loadedScene.name == scene.SceneName)
                {
                    if (MapManager.Instances.TryGetValue(scene.SceneName, out var mapManager))
                    {
                        mapManager.SaveSceneData();
                        yield return new WaitForSeconds(0.1f);
                        mapManager.MakeDataPersistent();
                        foreach (var obj in mapManager.sceneObjects)
                        {
                            if (obj.GetComponentsInChildren<EntitySpawner>() != null)
                            {
                                foreach (var spawner in obj.GetComponentsInChildren<EntitySpawner>()) 
                                    spawner.enabled = false;
                            }
                            
                            obj.gameObject.SetActive(false);
                        }
                        yield return new WaitForSeconds(0.1f);
                    }
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
        }
    }
}
