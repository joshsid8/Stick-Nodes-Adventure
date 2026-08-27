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
        // Had to use a while loop instead of CrossFadeAlpha because it was not working properly, cant have anything nice round ere
        while (screenShader.color.a > 0)
        {
            screenShader.color -= new Color(0, 0, 0, Time.deltaTime * fadeTime);
            yield return null;
        }
    }
    
    private IEnumerator FadeInShader(float holdTime, float fadeTime, bool fadeInAndOut = false, float fadeOutTime = 1.5f)
    {
        if (!screenShader.enabled) screenShader.enabled = true;
        yield return new WaitForSeconds(holdTime);
        while (screenShader.color.a < 1)
        {
            screenShader.color += new Color(0, 0, 0, Time.deltaTime * fadeTime);
            yield return null;
        }
        if (!fadeInAndOut) yield break;
        yield return new WaitForSeconds(fadeTime);
        StartCoroutine(FadeOutShader(holdTime, fadeOutTime));
    }

    public void SetShaderColour(Color colour = default, float fadeTime = 1)
    {
        //This wont work properly
        //StartCoroutine(ShaderColour(colour, fadeTime));
    }

    private IEnumerator ShaderColour(Color colour, float fadeTime = 1)
    {
        while (screenShader.color != new Color(colour.r, colour.g, colour.b))
        {
            screenShader.color = Color.Lerp(screenShader.color, new Color(colour.r, colour.g, colour.b), Time.deltaTime * fadeTime * 2);
            yield return null;
        }
    }
    
    #endregion

    public void ToggleBossHealth(bool state)
    {
        bossHealthBar.GetComponent<RectTransform>().parent.gameObject.SetActive(state);
        bossTitleText.gameObject.SetActive(state);
    }
}
