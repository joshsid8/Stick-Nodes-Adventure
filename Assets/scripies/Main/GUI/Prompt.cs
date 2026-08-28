using System;
using System.Collections;
using scripies.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Prompt : GUIBase
{
    public static Prompt Instance {get; private set;}
    
    [SerializeField] private GameObject prompt;
    public TMP_Text titleText;
    public TMP_Text messageText;
    
    public Button positiveButton;
    public Button negativeButton;

    public TMP_Text positiveButtonText;
    public TMP_Text negativeButtonText;

    [Tooltip("0 = Neutral, 1 = No, 2 = Yes")]
    public int choice;
    
    [SerializeField] AudioClip promptSound;
    
    protected override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(GetComponent<RectTransform>().parent.gameObject);
        base.Awake();
    }

    private void Start()
    {
        if (!isActive) return;
        choice = 0;
        prompt.SetActive(false);
        GetActive(false);
    }

    public void Choices(string title = "Are you sure?", string message = "Are you certain of what you want to do?", string positive = "Yes", string negative = "No")
    {
        prompt.SetActive(true);
        if (!gameObject.activeSelf) GetActive(true);
        choice = 0;
        titleText.text = title;
        messageText.text = message;
        positiveButtonText.text = positive;
        negativeButtonText.text = negative;
        positiveButton.onClick.AddListener(() => Positive());
        negativeButton.onClick.AddListener(() => Negative());
        
        GameEvents.TriggerPlaySound(promptSound);
    }

    private void Positive() => Finish(2);
    private void Negative() => Finish(1);

    private void Finish(int decision)
    {        
        choice = decision;
        positiveButton.onClick.RemoveAllListeners();
        negativeButton.onClick.RemoveAllListeners();

        titleText.text = "Are you sure?";
        messageText.text = "Are you certain of what you want to do?";
        positiveButtonText.text = "Yes";
        negativeButtonText.text = "No";
        prompt.SetActive(false);
        GetActive(false);
    }
}
