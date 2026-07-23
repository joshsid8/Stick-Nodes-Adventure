using UnityEngine;
using UnityEngine.UI;

public class StatsManager : MonoBehaviour
{
    public Button statsButton;
    public GameObject statsGO;
    public bool isEnabled;
    
    public StatInfo[] stats;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        isEnabled = false;
        statsGO.SetActive(false);
        statsButton.onClick.AddListener(ToggleStats);
        ClearShaders();
    }

    private void Update()
    {
        if (Input.GetButtonDown("Inventory") && isEnabled) ToggleStats();
        else if (Input.GetButtonDown("Cancel"))
        {
            isEnabled = false;
            statsGO.SetActive(isEnabled);
        }
    }

    private void ToggleStats()
    {
        isEnabled = !isEnabled;
        statsGO.SetActive(isEnabled);
    }

    public void ClearShaders()
    {
        for (int i = 0; i < stats.Length; i++)
        {
            stats[i].ClearInfo();
        }
    }
}
