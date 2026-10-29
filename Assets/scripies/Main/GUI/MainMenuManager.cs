using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using scripies.Core;
using scripies.Player;
using TMPro;

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
    
    private Animator animator;
    private bool isNewGameMode;

    private void Awake()
    {
        Time.timeScale = 1f;

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return; 
        }
        Instance = this;
        
        if (SceneManager.GetSceneByName("Persistent Objects").isLoaded) SceneManager.UnloadSceneAsync("Persistent Objects");
        
        input = new PlayerInput();
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            animator.enabled = true;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
    }

    private void OnDestroy()
    {
        if (input != null)
        {
            input.Disable();
            input.Dispose();
        }
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

    public void StartGame(int saveSlot = 0)
    {  
        if (isNewGameMode)
        {
            isNewGameMode = false;
            StartNewGameInSlot(saveSlot);
            return;
        }

        HideMenu();
        loadingBar.SetActive(true);
        SaveSystem.SetSaveSlot(saveSlot);
        StartCoroutine(ProgressLoading());
    }

    public void NewGame()
    {
        isNewGameMode = !isNewGameMode;
        if (animator != null) animator.Play("PickSlot");
        UpdateSlotButtonsForMode();
    }

    public void StartNewGameInSlot(int saveSlot)
    {
        HideMenu();
        loadingBar.SetActive(true);
        SaveSystem.SetSaveSlot(saveSlot);
        SaveSystem.DeleteSave();
        PlayerPrefs.DeleteKey("LoadedCheckPointId");
        StartCoroutine(ProgressLoading(true));
    }

    private void UpdateSlotButtonsForMode()
    {
        var slotsTransform = transform.Find("Slots");
        if (slotsTransform != null)
        {
            foreach (var img in slotsTransform.GetComponentsInChildren<Image>())
            {
                img.color = isNewGameMode
                    ? new Color(1f, 0.72f, 0.42f, 1f)
                    : new Color(0.487f, 0.868f, 0.506f, 1f);
            }
        }

        var newGameTransform = transform.Find("New Game/Text (TMP)");
        if (newGameTransform != null && newGameTransform.TryGetComponent<TMP_Text>(out var label))
        {
            label.text = isNewGameMode ? "Pick a Slot!" : "New Game";
        }
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
        
        yield return new WaitUntil(() => persistentLoad is { isDone: true } && DataManager.Instance != null);

        string targetSceneName;
        if (!newGame)
        {
            SaveSystem.Load();
            
            var lastActiveScene = DataManager.Instance.GetActiveSceneName();
            if (string.IsNullOrEmpty(lastActiveScene) && DataManager.Instance.sceneData.Count > 0)
            {
                lastActiveScene = DataManager.Instance.sceneData[0].sceneName;
                DataManager.Instance.sceneData[0].activeMap = true;
            }
            
            if (string.IsNullOrEmpty(lastActiveScene))
            {
                Debug.LogWarning($"[MainMenuManager] The last active scene wasn't fetched. Using {defaultScene.SceneName}, the default scene");
                lastActiveScene = defaultScene.SceneName;
            }

            targetSceneName = lastActiveScene;
            var mapLoad = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
            scenesToLoad.Add(mapLoad);
            
            yield return new WaitUntil(() => mapLoad is { isDone: true });
            yield return new WaitUntil(() => MapManager.Instances.ContainsKey(targetSceneName));
            MapManager.Instances[targetSceneName].activeMap = true;
            yield return new WaitForSeconds(0.1f);
        }
        else
        {
            targetSceneName = defaultScene.SceneName;
            var mapLoad = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
            scenesToLoad.Add(mapLoad);
            
            // For new game, we wait for both persistent and the default map
            yield return new WaitUntil(() => persistentLoad is { isDone: true } && mapLoad is { isDone: true });
            
            // Wait for MapManager to register itself
            yield return new WaitUntil(() => MapManager.Instances.ContainsKey(targetSceneName));
            MapManager.Instances[targetSceneName].activeMap = true;
        }
        
        // Final progress bar update
        while (scenesToLoad.Any(s => !s.isDone))
        {
            var totalProgress = scenesToLoad.Sum(s => s.progress);
            loadingBarImage.fillAmount = totalProgress / scenesToLoad.Count;
            yield return null;
        }

        if (MapManager.Instances.TryGetValue(targetSceneName, out var activeMapManager))
        {
            yield return new WaitUntil(() => activeMapManager.sceneLoaded);
        }

        yield return new WaitForSeconds(0.1f);
        
        yield return StartCoroutine(DataManager.Instance.SpawnPlayerToActiveMap());
        
        SaveSystem.Save();
        SceneManager.UnloadSceneAsync(gameObject.scene);
        Destroy(gameObject.transform.root.gameObject);
    }

    public void Save(ref MainMenuData data)
    {
        data.fps = fps > 0 ? fps : 60;
        data.vsync = vsync;
        if (!data.initialized)
        {
            data.masterVolume = 1f;
            data.musicVolume = 1f;
            data.sfxVolume = 1f;
            data.initialized = true;
        }
    }
    
    public void Load(MainMenuData data)
    {
        if (!data.initialized) return;
        fps = data.fps > 0 ? data.fps : 60;
        vsync = data.vsync;
    }
}

[Serializable]
public struct MainMenuData
{
    public bool initialized;
    public int fps;
    public bool vsync;
    public float masterVolume;
    public float musicVolume;
    public float sfxVolume;
}
