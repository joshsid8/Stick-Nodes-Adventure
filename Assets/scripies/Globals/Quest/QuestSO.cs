using UnityEngine;

[CreateAssetMenu(fileName = "New Quest", menuName = "UI/QuestSO")]
public class QuestSO : ScriptableObject
{
    public Quest.QuestType questType;
    public string questName;
    [TextArea(3, 5)] public string questDescription;
    
    [Tooltip("Enter IDs of enemies that are required for the quest. \n" +
             "The first two numbers are the enemy, and the last number is their buffs or debuffs. \n" +
             "010 is a hostile cynderian with no buffs or debuffs. \n" +
             "026 would probably be an enhanced fishman with extra strength.")]
    public int[] enemiesToDefeat;
    
    [Tooltip("number of amount for each enemy. so you can say 3 of x and 2 of y")] 
    public int[] noOfEnemiesToDefeat;

    [Tooltip("The objects to find, give to, or take from an npc")] 
    public CollectableSOBase[] objectsToInteract;
    public int[] numberOfObjects;
    
    public DialogueObject npcToInteract;
    
    public CollectableSOBase[] rewards = new CollectableSOBase[1];
}
