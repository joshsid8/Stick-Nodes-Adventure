using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;

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
        scenesToLoad.Add(SceneManager.LoadSceneAsync(persistentGameplay));
        var lastActiveScene = "";
        yield return new WaitUntil(() => DataManager.Instance != null);
        if (!newGame)
        {
            // TODO: Make a choice to switch between save slots
            SaveSystem.SetSaveSlot();
            SaveSystem.Load();
            yield return new WaitUntil(() => DataManager.Instance.sceneData.Count > 0 && PlayerManager.Instance != null);
            foreach (var scene in DataManager.Instance.sceneData.Where(scene => scene.activeMap))
            {
                lastActiveScene = scene.sceneName;
                scenesToLoad.Add(SceneManager.LoadSceneAsync(lastActiveScene, LoadSceneMode.Additive));

                break;
            }
        }
        if (string.IsNullOrEmpty(lastActiveScene) && !newGame)
        {
            Debug.LogWarning("theres no scene name data. loading all scenes");
            scenesToLoad.Add(SceneManager.LoadSceneAsync(defaultScene.SceneName, LoadSceneMode.Additive));
            scenesToLoad.Add(SceneManager.LoadSceneAsync("Fire Teste", LoadSceneMode.Additive));
            scenesToLoad.Add(SceneManager.LoadSceneAsync("Water Teste", LoadSceneMode.Additive));
            scenesToLoad.Add(SceneManager.LoadSceneAsync("Earth Teste", LoadSceneMode.Additive));
            scenesToLoad.Add(SceneManager.LoadSceneAsync("Air Teste", LoadSceneMode.Additive));
        }
        else if (newGame)
        {
            scenesToLoad.Add(SceneManager.LoadSceneAsync(defaultScene.SceneName, LoadSceneMode.Additive));
            yield return new WaitUntil(() => scenesToLoad[0].isDone && MapManager.Instances.ContainsKey(defaultScene.SceneName) && MapManager.Instances.ContainsKey(defaultScene.SceneName)); // Hoping that the first sceneToLoad instance is the loaded map above
            StartCoroutine(DataManager.Instance.SpawnPlayer(MapManager.Instances[defaultScene.SceneName]));
        }
        
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

        yield return new WaitForSeconds(0.1f);

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
