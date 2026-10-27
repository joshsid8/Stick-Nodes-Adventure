using System.Collections;
using UnityEngine;
using TMPro;
using Unity.VisualScripting;

public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance {get; private set;}
    
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public Camera actualCamera;

    public TMP_Text sectionTitle;
    
    public Animator animator;

    public string currentArea;
    [SerializeField] private float lerpTime = 5f;
    private Coroutine colorCoroutine;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        animator = GetComponent<Animator>();
    }
    
    public void Announce(string title = "", string description = "", Color textColor = default, Color bgColor = default, bool announceSection = false)
    {
        if (!string.IsNullOrEmpty(title))
        {
            currentArea = title;
            switch (announceSection)
            {
                case false:
                    titleText.text = title;
                    titleText.color = textColor;
                    descriptionText.text = description;
                    descriptionText.color = new Color(textColor.r + 0.5f, textColor.g + 0.5f, textColor.b + 0.5f, 1);
                    animator.SetTrigger("Announce");
                    break;
                case true:
                    sectionTitle.text = title;
                    sectionTitle.color = textColor;
                    animator.SetTrigger("SectionAnnounce");
                    break;
            }
        }
        
        if (actualCamera != null && actualCamera.backgroundColor != bgColor && bgColor != default)
        {
            //Maybe change background logic into an active sprite/image behind the player, and use CrossFadeColor to easily change it
            TriggerColorFade(bgColor);
        }
    }

    public void TriggerColorFade(Color targetColor)
    {
        if (colorCoroutine != null) StopCoroutine(colorCoroutine);
        if (actualCamera != null) colorCoroutine = StartCoroutine(LerpColours(targetColor));
    }

    public IEnumerator LerpColours(Color bgColor)
    {
        if (actualCamera == null || actualCamera.IsDestroyed() || actualCamera.backgroundColor == bgColor) yield break;
        //print(actualCamera);
        while (actualCamera != null && !actualCamera.IsDestroyed() && actualCamera.backgroundColor != bgColor)
        {
            actualCamera.backgroundColor = Color.Lerp(actualCamera.backgroundColor, bgColor, lerpTime * Time.deltaTime);
            
            if (ColorDistance(actualCamera.backgroundColor, bgColor) < 0.001f)
            {
                actualCamera.backgroundColor = bgColor;
                break;
            }
            yield return null;
        }
        colorCoroutine = null;
    }

    private static float ColorDistance(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
    }
}
