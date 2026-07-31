using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

public class MainMenuManager : MonoBehaviour
{
    [Header("Menu Objects")]
    [SerializeField] private GameObject loadingBar;
    [SerializeField] private Image loadingBarImage;
    [SerializeField] private GameObject[] objectsToHide;
    [SerializeField] private GameObject[] objectsToDestroy;
    
    [Header("Persistent Gameplay")]
    [SerializeField] private SceneField persistentGameplay;
    [SerializeField] private SceneField levelScene;
    
    private readonly List<AsyncOperation> scenesToLoad = new List<AsyncOperation>();

    private void Awake()
    {
        loadingBar.SetActive(false);
        DontDestroyOnLoad(gameObject.transform.root.gameObject);
    }

    public void StartGame()
    {  
        HideMenu();
        loadingBar.SetActive(true);
        StartCoroutine(ProgressLoading());
    }

    private void HideMenu()
    {
        foreach (var obj in objectsToHide)
            obj.SetActive(false);
            
        //foreach (var obj in objectsToDestroy) Destroy(obj);
    }

    private IEnumerator ProgressLoading()
    {
        float progress = 0;
        foreach (var scene in scenesToLoad)
        {
            while (scene.isDone)
            {
                progress += scene.progress;
                loadingBarImage.fillAmount = progress / scenesToLoad.Count;
                yield return null;
            }
        }
        scenesToLoad.Add(SceneManager.LoadSceneAsync(persistentGameplay));
        scenesToLoad.Add(SceneManager.LoadSceneAsync(levelScene, LoadSceneMode.Additive));
        yield return new WaitForSeconds(0.1f);
        SaveSystem.Load();
        SceneManager.UnloadSceneAsync(gameObject.scene);
        Destroy(gameObject.transform.root.gameObject);
    }
}
