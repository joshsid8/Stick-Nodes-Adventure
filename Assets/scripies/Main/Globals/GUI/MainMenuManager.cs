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
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    public void StartGame()
    {  
        HideMenu();
        loadingBar.SetActive(true);
        StartCoroutine(ProgressLoading());
    }
    
    public void QuitGame()
    {
        Application.Quit();
    }

    private void HideMenu()
    {
        foreach (var obj in objectsToHide)
            obj.SetActive(false);
            
        //foreach (var obj in objectsToDestroy) Destroy(obj);
    }

    private IEnumerator ProgressLoading()
    {
        // 1. Start loading the scenes first
        scenesToLoad.Add(SceneManager.LoadSceneAsync(persistentGameplay));
        scenesToLoad.Add(SceneManager.LoadSceneAsync(levelScene, LoadSceneMode.Additive));

        // 2. Wait for all scenes in the list to finish
        float totalProgress;
        var allDone = false;
        
        while (!allDone)
        {
            totalProgress = 0;
            allDone = true;

            foreach (var scene in scenesToLoad)
            {
                totalProgress += scene.progress;
                if (!scene.isDone) allDone = false;
            }

            loadingBarImage.fillAmount = totalProgress / scenesToLoad.Count;
            yield return null;
        }

        // 3. Finalize
        yield return new WaitForSeconds(0.1f);
        SaveSystem.Load();
        SceneManager.UnloadSceneAsync(gameObject.scene);
        Destroy(gameObject.transform.root.gameObject);
    }
}
