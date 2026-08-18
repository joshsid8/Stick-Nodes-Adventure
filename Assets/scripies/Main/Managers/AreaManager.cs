using System.Collections;
using UnityEngine;
using TMPro;

public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance {get; private set;}
    
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    [SerializeField] private Camera actualCamera;

    public TMP_Text sectionTitle;
    
    public Animator animator;
    
    [SerializeField] private float lerpTime = 5f;
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
        if (!string.IsNullOrEmpty(description))
        {
            titleText.text = title;
            descriptionText.text = description;
            animator.SetTrigger("Announce");
        }
        else
        {
            sectionTitle.text = title;
            sectionTitle.color = textColor;
            animator.SetTrigger("SectionAnnounce");
        }
        titleText.color = textColor;
        
        if (actualCamera.backgroundColor != bgColor)
        {
            //TODO: Maybe change background logic into an active sprite/image behind the player, and use CrossFadeColor to efficiently change it
            StartCoroutine(LerpColours(bgColor));
        }
    }

    public IEnumerator LerpColours(Color bgColor)
    {
        var timer = 2f;
        while (actualCamera.backgroundColor != bgColor)
        {
            actualCamera.backgroundColor = Color.Lerp(actualCamera.backgroundColor, bgColor, lerpTime * Time.deltaTime);
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                actualCamera.backgroundColor = bgColor;
                StopCoroutine(LerpColours(bgColor));
                yield break;
            }
            yield return null;
        }
    }
}
