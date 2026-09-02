using System;
using System.Collections;
using scripies.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMenuManager : MonoBehaviour
{
    public static GameMenuManager Instance;
    
    [SerializeField] private GameObject gameMenu;
    public bool pausedGame;
    
    public Button[] menuButtons;
    [SerializeField] private Prompt menuPrompt;
    
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
        OnMenuToggle(false);
        PlayerHealth.Instance.pc.input.Player.Menu.performed += _ => OnMenuToggle(true);
        PlayerHealth.Instance.pc.input.UI.Cancel.performed += _ => OnMenuToggle(false);
    }

    private void OnMenuToggle(bool toggle)
    {
        pausedGame = toggle;
        Shenanigans.Instance.ToggleFreezeCamera(toggle);
        gameMenu.SetActive(toggle);
        if (!toggle)
        {
            PlayerHealth.Instance.pc.input.Player.Enable();
            PlayerHealth.Instance.pc.input.UI.Disable();
            foreach (var button in menuButtons) button.onClick.RemoveAllListeners();
        }
        else
        {
            foreach(var button in menuButtons)
            {
                switch (button.name)
                {
                    case "Continue":
                        button.onClick.AddListener(() => OnMenuToggle(false));
                        break;
                    case "Exit":
                        button.onClick.AddListener(ExitGame);
                        break;
                }
            }
            PlayerHealth.Instance.pc.input.Player.Disable();
            PlayerHealth.Instance.pc.input.UI.Enable();
        }
    }
    
    private void ExitGame() => StartCoroutine(AskBeforeExit());

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
