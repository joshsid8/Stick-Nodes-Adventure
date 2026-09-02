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
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.GetComponent<IPlayerStatus>();
        }
    }

    private void Update()
    {
        if (player != null)
        {
            currencyText.text = "₦odeBucks: " + player.NodeBucks.ToString("F0");
        }
        bossHealthBackBar.color = new Color(bossHealthBar.color.r * 0.4f, bossHealthBar.color.g * 0.4f, bossHealthBar.color.b * 0.4f, 1);
    }
    
    #region Shader Stuff
    private void ToggleShader(bool state) => screenShader.enabled = state;
    public void ActivateShader(float holdTime = 1, float fadeTime = 1.5f, bool fadeInAndOut = false, float fadeOutTime = 1.5f)
    {
        if (fadeTime <= 0)
        {
            StartCoroutine(FadeInShader(holdTime, Mathf.Abs(fadeTime), fadeInAndOut, fadeOutTime));
        }
        else
        {
            StartCoroutine(FadeOutShader(holdTime, fadeTime));
        }
    }

    public IEnumerator FadeInShader(float holdTime, float fadeTime, bool fadeInAndOut = false, float fadeOutTime = 1.5f)
    {
        if (!screenShader.enabled) screenShader.enabled = true;
        
        Color targetColor = screenShader.color;
        targetColor.a = 0;
        screenShader.color = targetColor;
        
        yield return new WaitForSeconds(holdTime);
        
        float progress = 0;
        while (progress < 1)
        {
            progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime * fadeTime);
            Color c = screenShader.color;
            c.a = progress;
            screenShader.color = c;
            yield return null;
        }
        
        if (fadeInAndOut)
        {
            yield return new WaitForSeconds(fadeOutTime); // use fadeOutTime as hold time before fading out
            StartCoroutine(FadeOutShader(0, fadeOutTime));
        }
    }

    public IEnumerator FadeOutShader(float holdTime, float fadeTime)
    {
        if (!screenShader.enabled) screenShader.enabled = true;
        yield return new WaitForSeconds(holdTime);
        
        float progress = screenShader.color.a;
        while (progress > 0)
        {
            progress = Mathf.MoveTowards(progress, 0f, Time.deltaTime * fadeTime);
            Color c = screenShader.color;
            c.a = progress;
            screenShader.color = c;
            yield return null;
        }
        screenShader.enabled = false;
    }

    public void SetShaderColour(Color colour = default, float fadeTime = 1)
    {
        if (fadeTime <= 0)
        {
            screenShader.color = colour;
        }
        else
        {
            screenShader.CrossFadeColor(colour, fadeTime, true, false, true);
        }
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
