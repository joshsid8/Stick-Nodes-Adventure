using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Player;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMenuManager : MonoBehaviour
{
    public static GameMenuManager Instance;
    
    public GameObject gameMenu;
    public bool pausedGame;
    
    [SerializeField] private Prompt menuPrompt;
    private GameSettingsManager settingsManager;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        settingsManager = GameSettingsManager.Instance ?? FindAnyObjectByType<GameSettingsManager>();
        PlayerHealth.Instance.pc.input.Player.Menu.performed += _ => OnMenuToggle(true);
        PlayerHealth.Instance.pc.input.UI.Cancel.performed += _ => OnMenuToggle(false);
        OnMenuToggle(false);
    }

    public void OnMenuToggle(bool toggle)
    {
        pausedGame = toggle;
        Shenanigans.Instance.ToggleFreezeCamera(toggle);
        gameMenu.SetActive(toggle);
        settingsManager.ToggleSettings(false);
        if (!toggle)
        {
            PlayerHealth.Instance.pc.input.Player.Enable();
            PlayerHealth.Instance.pc.input.UI.Disable();
        }
        else
        {
            PlayerHealth.Instance.pc.input.Player.Disable();
            PlayerHealth.Instance.pc.input.UI.Enable();
        }
    }
    
    public void ExitGame() => StartCoroutine(AskBeforeExit());

    private IEnumerator AskBeforeExit()
    {
        menuPrompt.Choices("Exit Game?", "Are you sure you want to exit?", "Exit", "Cancel");
        yield return new WaitUntil(() => menuPrompt.choice != 0);
        if (menuPrompt.choice == 2)
        {
            SaveSystem.Save();
            
            var aud = AudioManager.Instance;
            if (aud != null) aud.StopAudio();
            //GameHUD.Instance.ActivateShader(.2f, 0.75f);
            yield return new WaitForSecondsRealtime(1.5f);
            
            // Collect all persistent objects to destroy
            var persistentObjects = new List<GameObject>();
            
            // Find common persistent objects by singleton instances
            if (AudioManager.Instance != null) persistentObjects.Add(AudioManager.Instance.gameObject);
            if (Shenanigans.Instance != null) persistentObjects.Add(Shenanigans.Instance.gameObject);
            if (DataManager.Instance != null) persistentObjects.Add(DataManager.Instance.gameObject);
            if (InvManager.Instance != null) persistentObjects.Add(InvManager.Instance.transform.root.gameObject);
            if (PlayerHealth.Instance != null) persistentObjects.Add(PlayerHealth.Instance.gameObject);
            if (StatsManager.Instance != null) persistentObjects.Add(StatsManager.Instance.gameObject);
            if (QuestLogManager.Instance != null) persistentObjects.Add(QuestLogManager.Instance.gameObject);
            if (ResourceDatabase.Instance != null) persistentObjects.Add(ResourceDatabase.Instance.gameObject);
            if (ObjectDatabase.Instance != null)
            {
                ObjectDatabase.Instance.ClearAll();
                persistentObjects.Add(ObjectDatabase.Instance.gameObject);
            }
            // Idea: adding a scene with absolutely nothing, so we have can allow main menu to take over after everythings gone
            yield return SceneManager.LoadSceneAsync("Nothing Scene", LoadSceneMode.Additive);
            
            // Unload all scenes except Main Menu
            var scenesToUnload = new List<AsyncOperation>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name is "Main Menu" or "Nothing Scene") continue; // holy pseudocode!!!
                scenesToUnload.Add(SceneManager.UnloadSceneAsync(scene));
            }
            
            while (scenesToUnload.Any(s => !s.isDone)) yield return null;
            
            // Destroy persistent objects
            foreach (var obj in persistentObjects.Where(obj => obj != null)) 
                Destroy(obj);
            
            yield return SceneManager.LoadSceneAsync("Main Menu", LoadSceneMode.Additive);
            yield return new WaitUntil(() => SceneManager.GetSceneByName("Main Menu").isLoaded);
            SceneManager.SetActiveScene(SceneManager.GetSceneByName("Main Menu"));
            SceneManager.UnloadSceneAsync("Nothing Scene");
            if (SceneManager.GetSceneByName("Nothing Scene").isLoaded)
            {
                print("son");
                SceneManager.UnloadSceneAsync("Nothing Scene");
                if (SceneManager.GetSceneByName("Nothing Scene").isLoaded) print("SON");
            }

            
            yield return new WaitForSecondsRealtime(0.1f);
            print("Cleaned up thus exited game");
        }
        else OnMenuToggle(false);
    }
}
