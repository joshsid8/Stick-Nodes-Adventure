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
    
    [Header("Persistent Gameplay")]
    [SerializeField] private SceneField persistentGameplay;
    [SerializeField] private SceneField levelScene;
    
    private List<AsyncOperation> scenesToLoad = new List<AsyncOperation>();
    
    void Awake()
    {
        loadingBar.SetActive(false);
    }

    public void StartGame()
    {
        HideMenu();
        
        loadingBar.SetActive(true);
        
        scenesToLoad.Add(SceneManager.LoadSceneAsync(persistentGameplay));
        scenesToLoad.Add(SceneManager.LoadSceneAsync(levelScene, LoadSceneMode.Additive));
        StartCoroutine(ProgressLoading());
    }

    void HideMenu()
    {
        for (int i = 0; i < objectsToHide.Length; i++) objectsToHide[i].SetActive(false);
    }

    IEnumerator ProgressLoading()
    {
        float progress = 0;
        for (int i = 0; i < scenesToLoad.Count; i++)
        {
            while (!scenesToLoad[i].isDone)
            {
                progress += scenesToLoad[i].progress;
                loadingBarImage.fillAmount = progress / scenesToLoad.Count;
                yield return null;
            }
        }
    }
}
