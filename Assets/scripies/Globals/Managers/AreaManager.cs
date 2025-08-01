using System;
using UnityEngine;
using System.Collections;
using TMPro;

public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance {get; private set;}

    public TMP_Text[] areaInfo;
    
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    [SerializeField] private Color areaInfoColor;
    [SerializeField] private float showTime;
    [SerializeField] private float lerpSpeed;
    [SerializeField] private bool isAnnouncing;

    [SerializeField] private bool[] toggle;
    
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
        descriptionText.text = description;
        titleText.color = textColor;
        areaInfoColor = titleText.color;
        GameObject.Find("Actual Camera").GetComponent<Camera>().backgroundColor = bgColor;
        toggle[1] = false;
        toggle[0] = false;
        yield return new WaitForSeconds(showTime);
        //toggle[1] = true;
        //yield return new WaitForSeconds(showTime * 0.25f);
        isAnnouncing = false;
    }

    private void Display()
    {
        /*if (isAnnouncing && !toggle[0] && !toggle[1])
        {
            for (int i = 0; i < areaInfo.Length; i++)
            {
                areaInfo[i].color = Color.Lerp(Color.clear, areaInfoColor, Time.deltaTime * lerpSpeed);
                print("lerping from clear to the area colour");
                if (areaInfo[i].color == areaInfoColor)
                {
                    toggle[0] = true;
                    print("lerping done");
                    toggle[1] = true;
                    if (toggle[1] && toggle[0])
                    {
                        areaInfo[i].color = Color.Lerp(areaInfoColor, Color.clear, Time.deltaTime * lerpSpeed);
                        print("lerping back to nothingness");
                        if (areaInfo[i].color == Color.clear)
                        {
                            isAnnouncing = false;
                            print("its done!");
                        }
                    }
                }
                else print("clueless");
            }
        }*/
        for (int i = 0; i < areaInfo.Length; i++)
        {
            areaInfo[i].gameObject.SetActive(isAnnouncing);
        }
    }
}
