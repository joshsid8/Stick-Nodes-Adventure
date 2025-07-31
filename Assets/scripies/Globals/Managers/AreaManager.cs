using System;
using UnityEngine;
using System.Collections;
using TMPro;

public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance {get; private set;}

    public GameObject areaInfo;
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    [SerializeField] private float showTime;
    private bool isAnnouncing;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        Display();
    }

    public IEnumerator Announce(string title, string description, Color textColor , Color bgColor)
    {
        isAnnouncing = true;
        titleText.text = title;
        titleText.color = textColor;
        descriptionText.text = description;
        GameObject.Find("Actual Camera").GetComponent<Camera>().backgroundColor = bgColor;
        yield return new WaitForSeconds(showTime);
        isAnnouncing = false;
    }

    private void Display()
    {
        areaInfo.SetActive(isAnnouncing);
    }
}
