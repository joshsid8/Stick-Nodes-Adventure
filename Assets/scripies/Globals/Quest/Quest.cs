using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Quest : MonoBehaviour, IPointerClickHandler
{
    public string questTitle;
    [TextArea(5, 10)] public string questDescription;
    public QuestType questType;

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text taskText;
    
    [SerializeField] private TMP_Text infoTitle;
    [SerializeField] private TMP_Text infoDescription;
    
    public bool questCompleted;

    [Tooltip("Enter IDs of enemies that are required for the quest. \n" +
             "The first two numbers are the enemy, and the last number is their buffs or debuffs. \n" +
             "010 is a hostile cynderian with no buffs or debuffs. \n" +
             "026 would probably be an enhanced fishman with extra strength.")] 
    [SerializeField] private int[] enemiesToDefeat;
    
    [Tooltip("number of amount for each enemy. so you can say 3 of x and 2 of y")] 
    [SerializeField] private int[] noOfEnemiesToDefeat;

    [Tooltip("The objects to find, give to, or take from an npc")]
    public string[] objectsToInteract;
    
    private void Start()
    { 
        titleText.text = questTitle;
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
                //objectiveText.text = $"Defeat {quest.noOfEnemiesToDefeat}/{quest.enemiesToDefeat.Length} enemies";
                break;
            case QuestType.HaveInInventory:
                //objectiveText.text = $"Obtain {quest.objectsToFind.Length}";
                break;
        }
    }
    public void LogQuest(QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat,string[] objects ,CollectableSOBase[] rewards)
    {
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                
                break;
            case QuestType.HaveInInventory:
                
                break;
            case QuestType.GiveTo:
                
                break;
            case QuestType.TakeFrom:
                
                break;
            
        }
        
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) UpdateUI();
    }



}
