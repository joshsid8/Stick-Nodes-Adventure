using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Player;

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

    private PlayerInput input;
    
    [Header("Settings")] 
    private int fps = 60;
    private bool vsync;

    [SerializeField] private SceneField defaultScene;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return; 
        }
        Instance = this;
        
        if (SceneManager.GetSceneByName("Persistent Objects").isLoaded) SceneManager.UnloadSceneAsync("Persistent Objects");
        
        input = new PlayerInput();
    }

    private IEnumerator Start()
    {
        loadingBar.SetActive(false);
        yield return new WaitForSeconds(0.1f); 
        SaveSystem.LoadSettings();

        DontDestroyOnLoad(gameObject.transform.root.gameObject);
        if (!vsync) QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fps;
        
        input.Enable();
        input.UI.Enable();
        input.Player.Disable();
    }

    public void StartGame()
    {  
        HideMenu();
        loadingBar.SetActive(true);
        StartCoroutine(ProgressLoading());
    }

    public void NewGame()
    {
        HideMenu();
        loadingBar.SetActive(true);
        StartCoroutine(ProgressLoading(true));
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

    private IEnumerator ProgressLoading(bool newGame = false)
    {
        var persistentLoad = SceneManager.LoadSceneAsync(persistentGameplay);
        scenesToLoad.Add(persistentLoad);
        
        yield return new WaitUntil(() => DataManager.Instance != null);

        if (!newGame)
        {
            SaveSystem.SetSaveSlot();
            SaveSystem.Load();
            
            // Wait for data to be loaded into DataManager
            yield return new WaitUntil(() => DataManager.Instance.sceneData.Count > 0 && DataManager.Instance.sceneData.Exists(x => x.activeMap));
            var lastActiveScene = DataManager.Instance.GetActiveSceneName();
            
            if (string.IsNullOrEmpty(lastActiveScene))
            {
                Debug.LogWarning($"[MainMenuManager] The last active scene wasn't fetched. using {defaultScene.SceneName}, the default scene");
                lastActiveScene = defaultScene.SceneName;
            }
            
            var mapLoad = SceneManager.LoadSceneAsync(lastActiveScene, LoadSceneMode.Additive);
            scenesToLoad.Add(mapLoad);
            
            yield return new WaitUntil(() => mapLoad is { isDone: true });
            yield return new WaitForSeconds(0.1f);
        }
        else
        {
            var mapLoad = SceneManager.LoadSceneAsync(defaultScene.SceneName, LoadSceneMode.Additive);
            scenesToLoad.Add(mapLoad);
            
            // For new game, we wait for both persistent and the default map
            yield return new WaitUntil(() => persistentLoad is { isDone: true } && mapLoad is { isDone: true });
            
            // Wait for MapManager to register itself
            yield return new WaitUntil(() => MapManager.Instances.ContainsKey(defaultScene.SceneName));
            MapManager.Instances[defaultScene.SceneName].activeMap = true;
        }
        
        // Final progress bar update
        while (scenesToLoad.Any(s => !s.isDone))
        {
            var totalProgress = scenesToLoad.Sum(s => s.progress);
            loadingBarImage.fillAmount = totalProgress / scenesToLoad.Count;
            yield return null;
        }

        yield return new WaitForSeconds(0.2f);
        
        if (!newGame)
        {
            yield return StartCoroutine(DataManager.Instance.SpawnPlayerToActiveMap());
        }
        else
        {
            yield return StartCoroutine(PlayerHealth.Instance.Spawn(0.1f, true));
        }
        
        SaveSystem.Save();
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

[Serializable]
public struct MainMenuData
{
    public int fps;
    public bool vsync;
}
