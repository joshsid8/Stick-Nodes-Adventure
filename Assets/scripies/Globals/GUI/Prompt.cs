using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Prompt : GUIBase
{
    public TMP_Text titleText;
    public TMP_Text messageText;
    
    public Button positiveButton;
    public Button negativeButton;

    public TMP_Text positiveButtonText;
    public TMP_Text negativeButtonText;

    public bool yes;
    public bool no;
    public bool neutral;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        neutral = true;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (yes) no = false;
        if (no) yes = false;
        if (neutral)
        {
            yes = false; 
            no = false;
        }
    }

    public void Choices()
    {
        gameObject.SetActive(true);
        neutral = false;
        positiveButton.onClick.AddListener(() => Positive());
        negativeButton.onClick.AddListener(() => Negative());
    }

    private void Positive()
    {
        yes = true;
        Disable();
    }

    private void Negative()
    {
        no = true;
        Disable();
    }

    public void Disable()
    {
        neutral = true;
        positiveButton.onClick.RemoveAllListeners();
        negativeButton.onClick.RemoveAllListeners();

        titleText.text = "Are you sure?";
        messageText.text = "Are you certain of what you want to do?";
        gameObject.SetActive(false);
    }
}
