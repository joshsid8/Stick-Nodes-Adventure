using UnityEngine;

public class Quest : MonoBehaviour
{
    public string questTitle;
    [TextArea(5, 10)] public string questDescription;
    public QuestType questType;
    
    public bool questCompleted;

    public GameObject[] enemiesToDefeat;
    public int[] noOfEnemiesToDefeat;

    public GameObject[] objectsToFind;

    public string[] objectsToGive;

    public GameObject[] objectsToTake;
    
    public enum QuestType
    { 
        DefeatEnemy, HaveInInventory, GiveTo, TakeFrom
    }

    // Update is called once per frame
    private void Update()
    {
        TypeOfQuest();
    }

    private void TypeOfQuest()
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
}
