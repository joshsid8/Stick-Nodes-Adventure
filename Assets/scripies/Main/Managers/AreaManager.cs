using System.Collections;
using UnityEngine;
using TMPro;

public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance {get; private set;}
    
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public Camera actualCamera;

    public TMP_Text sectionTitle;
    
    public Animator animator;
    
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
    
    public void Announce(string title = "", string description = "", Color textColor = default, Color bgColor = default)
    {
        switch (string.IsNullOrEmpty(title), string.IsNullOrEmpty(description))
        {
            case (false, false):
                titleText.text = title;
                titleText.color = textColor;
                descriptionText.text = description;
                animator.SetTrigger("Announce");
                break;
            case (true, false):
                sectionTitle.text = title;
                sectionTitle.color = textColor;
                animator.SetTrigger("SectionAnnounce");
                break;
            case (true, true):
                break;
        }
        
        if (actualCamera.backgroundColor != bgColor && bgColor != default)
        {
            //Maybe change background logic into an active sprite/image behind the player, and use CrossFadeColor to efficiently change it
            TriggerColorFade(bgColor);
        }
    }

    public void TriggerColorFade(Color targetColor)
    {
        if (colorCoroutine != null) StopCoroutine(colorCoroutine);
        colorCoroutine = StartCoroutine(LerpColours(targetColor));
    }

    public IEnumerator LerpColours(Color bgColor)
    {
        while (actualCamera.backgroundColor != bgColor)
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

    private float ColorDistance(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.b);
    }
}
