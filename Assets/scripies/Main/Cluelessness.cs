using System;
using TMPro;
using UnityEngine;
    
/// <summary>
/// This is a script for some sort of debugging. If im CLUELESS on a bug in a script, use this.
/// Displays the value of a variable as gui text. '=' Gets added for ya
/// Kinda like a friend of Tomfoolery this script is
/// </summary>
public class Cluelessness : MonoBehaviour
{
    public static Cluelessness Instance {get; private set;}
    [SerializeField] private TMP_Text[] cluelessTexts = new TMP_Text[2];
    //public float[] cluelessFloatValue = new float[2];
    //public bool[] cluelessBoolValue = new bool[2];
    [SerializeField] private int cluelessIndex;

    [SerializeField] private bool alterTime;
    [SerializeField] private float timeScale = 1;

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
        foreach (var text in cluelessTexts)
        {
            if (text.text.Contains("Clueless1")) text.text = "";
            else if (text.text.Contains("Clueless2")) text.text = "";
        }
        
        if (alterTime) Time.timeScale = timeScale;
    }

    public void CluelessF(float value, string variableName, int index = 0)
    {
        cluelessTexts[index].text = "Clueless: " + variableName + " = " + value;
    }
    public void CluelessB(bool value, string variableName, int index = 0)
    {
        cluelessTexts[index].text = "Clueless: " + variableName + " = " + value;
    }
}
