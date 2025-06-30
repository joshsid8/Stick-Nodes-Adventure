using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StatInfo : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text statName;
    [SerializeField] private GameObject shader;
    [SerializeField] private TMP_Text statDescription;
    StatsManager statsManager;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        statsManager = FindAnyObjectByType<StatsManager>();
        statDescription.text = string.Empty;
        ClearInfo();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnLeftClick();
    }

    private void OnLeftClick()
    {
        shader.SetActive(true);
        
    }

    private void ClearInfo()
    {
        shader.SetActive(false);
    }
}
