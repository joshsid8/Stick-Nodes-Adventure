using System;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Quest : MonoBehaviour, IPointerClickHandler
{
    public string questTitle;
    [TextArea(5, 10)] public string questDescription;
    public QuestType questType;
    
    [SerializeField] private TMP_Text[] titleTexts;
    [SerializeField] private TMP_Text taskText;
    
    [SerializeField] private TMP_Text infoTitle;
    [SerializeField] private TMP_Text infoDescription;
    [SerializeField] private TMP_Text objectiveText;
    
    public bool questCompleted;
    public bool isTracking;

 
    [Tooltip("enemies to defeat")] [SerializeField] private int[] eTd;
    
    [Tooltip("Number of enemies currently defeated")] [SerializeField] private int[] noOfE;

    [Tooltip("objects to take, give, or find")] public string[] oTi;

    [Tooltip("the npc you may have to speak to")] public string nTi;
    
    public CollectableSOBase[] questRewards;
    
    private void Start()
    { 
        foreach (var text in titleTexts) text.text = questTitle;
    }

    public enum QuestType
    { 
        DefeatEnemy, HaveInInventory, GiveTo, TakeFrom
    }
    public void UpdateUI()
    {
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                objectiveText.text = $"Defeat {noOfE[0]}/{eTd.Length} enemies"; // change enemies into the names of the certain ones to defeat.
                break;
            case QuestType.HaveInInventory:
                objectiveText.text = $"Obtain {oTi.Length}";
                break;
        }
    }
    public void LogQuest(QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat, string[] objects, string npc, CollectableSOBase[] rewards)
    {
        questType = type;
        questTitle = title;
        questDescription = description;
        nTi = npc;
        for (var i = 0; i < eTd.Length; i++) eTd[i] = enemyIDs[i];
        for (var i = 0; i < noOfE.Length; i++) noOfE[i] = eToDefeat[i];
        for (var i = 0; i < oTi.Length; i++) oTi[i] = objects[i];
        for (var i = 0; i < questRewards.Length; i++) questRewards[i] = rewards[i];
        foreach (var text in titleTexts) text.text = questTitle;
        
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                foreach (var id in eTd)
                {
                    var enemy = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
                    foreach (var eh in enemy)
                    {
                        int identifier = eh.enemyID;
                        if (identifier == id) print("happyx3"); else print($"augh: id is {id}, while identifier is {identifier}");
                    }
                }
                break;
            case QuestType.HaveInInventory:
                print($"ensure you have {objects[0]} in your inventory!");
                break;
            case QuestType.GiveTo:
                print($"give {objects} to {npc}!");
                break;
            case QuestType.TakeFrom:
                print($"steal {objects} to from {npc}!");
                break;
            
        }
        
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) UpdateUI();
    }



}
