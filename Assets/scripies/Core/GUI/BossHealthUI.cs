using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BossHealthUI : MonoBehaviour
{
    public Image healthBar;
    public TMP_Text bossTitle;
    
    public static BossHealthUI Instance { get; private set; }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        Toggle(false);
    }

    public void Toggle(bool state)
    {
        healthBar.GetComponent<RectTransform>().parent.gameObject.SetActive(state);
        bossTitle.gameObject.SetActive(state);
    }
}