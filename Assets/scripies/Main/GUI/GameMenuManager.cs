using System.Collections;
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
        OnMenuToggle(false);
    }

    private void Update()
    {
        if (Input.GetButtonDown("Cancel")) OnMenuToggle(!pausedGame);
    }

    private void OnMenuToggle(bool toggle)
    {
        pausedGame = toggle;
        Time.timeScale = pausedGame ? 0 : 1;
        gameMenu.SetActive(toggle);
        if (!toggle) foreach (var button in menuButtons) button.onClick.RemoveAllListeners();
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
