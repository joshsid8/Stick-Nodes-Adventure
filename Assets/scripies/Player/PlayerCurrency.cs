using TMPro;
using UnityEngine;

public class PlayerCurrency : MonoBehaviour
{
    public float nodeBucks;
    public TMP_Text bucksText;

    // Update is called once per frame
    void Update()
    {
        bucksText.text = "₦odeBucks: " + nodeBucks.ToString("F0");
        if (nodeBucks <= 0) nodeBucks = 0;
    }
}
