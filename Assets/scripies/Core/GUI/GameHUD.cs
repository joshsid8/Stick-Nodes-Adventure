using System.Collections;
using scripies.Core;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }
    // this will just be all/some of the necessary data shown on the screen
    
    private IPlayerStatus player;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private GameObject abilityArsenal;

    [SerializeField] private Image screenShader;
    [SerializeField] private Color screenShaderColour;
    
    private IEnemyStatus boss;
    public Image bossHealthBar;
    [SerializeField] private Image bossHealthBackBar;
    public TMP_Text bossTitleText;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        ToggleBossHealth(false);
        ToggleShader(true);
        SetShaderColour(screenShaderColour, 0.75f);
    }
    
    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<IPlayerStatus>();
    }

    private void Update()
    {
        currencyText.text = "₦odeBucks: " + player.NodeBucks.ToString("F0");
        bossHealthBackBar.color = new Color(bossHealthBar.color.r * 0.4f, bossHealthBar.color.g * 0.4f, bossHealthBar.color.b * 0.4f, 1);
    }
    
    #region Shader Stuff
    private void ToggleShader(bool state) => screenShader.enabled = state;
    public void ActivateShader(float holdTime = 1, float fadeTime = 1.5f, bool fadeInAndOut = false, float fadeOutTime = 1.5f)
    {
        StartCoroutine(fadeTime <= 0
            ? FadeInShader(holdTime, fadeTime, fadeInAndOut, fadeOutTime)
            : FadeOutShader(holdTime, fadeTime));
    }
    
    private IEnumerator FadeOutShader(float holdTime, float fadeTime)
    {
        if (!screenShader.enabled) screenShader.enabled = true;
        yield return new WaitForSeconds(holdTime);
        screenShader.CrossFadeAlpha(0, Mathf.Abs(fadeTime), false);
    }
    
    private IEnumerator FadeInShader(float holdTime, float fadeTime, bool fadeInAndOut = false, float fadeOutTime = 1.5f)
    {
        if (!screenShader.enabled) screenShader.enabled = true;
        yield return new WaitForSeconds(holdTime);
        screenShader.CrossFadeAlpha(1, Mathf.Abs(fadeTime), false);
        if (!fadeInAndOut) yield break;
        yield return new WaitForSeconds(fadeTime);
        StartCoroutine(FadeOutShader(holdTime, fadeOutTime));
    }
    
    private void SetShaderColour(Color colour, float fadeTime = 1) => screenShader.CrossFadeColor(colour, fadeTime, false, false);
    
    #endregion
    
    public void ToggleBossHealth(bool state)
    {
        bossHealthBar.GetComponent<RectTransform>().parent.gameObject.SetActive(state);
        bossTitleText.gameObject.SetActive(state);
    }
}
