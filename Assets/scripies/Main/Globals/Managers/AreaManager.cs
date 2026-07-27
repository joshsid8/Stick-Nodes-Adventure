using scripies.Core;
using UnityEngine;
using TMPro;

public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance {get; private set;}
    
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    [SerializeField] private Camera actualCamera;

    private Animator animator;
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
    
    public void Announce(string title, string description, Color textColor, Color bgColor)
    {
        titleText.text = title;
        descriptionText.text = description;
        titleText.color = textColor;
        actualCamera.backgroundColor = bgColor;
        animator.SetTrigger("Announce");
    }
}
