using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

public class MainMenuManager : MonoBehaviour
{
    public static MainMenuManager Instance;
    
    [Header("Menu Objects")]
    [SerializeField] private GameObject loadingBar;
    [SerializeField] private Image loadingBarImage;
    [SerializeField] private GameObject[] objectsToHide;
    [SerializeField] private GameObject[] objectsToDestroy;
    
    [Header("Persistent Gameplay")]
    [SerializeField] private SceneField persistentGameplay;
    private readonly List<AsyncOperation> scenesToLoad = new List<AsyncOperation>();
    [SerializeField] private AudioManager audioManager;

    [Header("Settings")] 
    private int fps = 60;
    private bool vsync;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return; 
        }
        Instance = this;
    }

    private IEnumerator Start()
    {
        loadingBar.SetActive(false);
        yield return new WaitForSeconds(0.5f); 
        var sceneBuffer = audioManager.currentScene;
        SaveSystem.LoadSettings();
        if (String.IsNullOrEmpty(audioManager.currentScene))
        {
            print("theres no scene name data. using buffer ._.");
            audioManager.currentScene = sceneBuffer;
        }
        DontDestroyOnLoad(gameObject.transform.root.gameObject);
        if (!vsync) QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fps;
    }

    public void StartGame()
    {  
        HideMenu();
        loadingBar.SetActive(true);
        StartCoroutine(ProgressLoading());
    }
    
    public void QuitGame()
    {
        SaveSystem.SaveSettings();
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
        scenesToLoad.Add(SceneManager.LoadSceneAsync(audioManager.currentScene, LoadSceneMode.Additive));
        scenesToLoad.Add(SceneManager.LoadSceneAsync("Water Teste", LoadSceneMode.Additive));

        // 2. Wait for all scenes in the list to finish
        var allDone = false;
        
        while (!allDone)
        {
            float totalProgress = 0;
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

    public void Save(ref MainMenuData data)
    {
        data.fps = fps;
        data.vsync = vsync;
    }
    
    public void Load(MainMenuData data)
    {
        fps = data.fps;
        vsync = data.vsync;
    }
}

[System.Serializable]
public struct MainMenuData
{
    public int fps;
    public bool vsync;
}
