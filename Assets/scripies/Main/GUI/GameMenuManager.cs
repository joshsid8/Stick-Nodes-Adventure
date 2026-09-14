using System;
using System.Collections;
using scripies.Player;
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
            Application.Quit();
        }
        else OnMenuToggle(false);
    }
}
