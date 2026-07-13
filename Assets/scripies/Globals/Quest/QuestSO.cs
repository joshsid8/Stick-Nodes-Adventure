using UnityEngine;

[CreateAssetMenu(fileName = "New Quest", menuName = "UI/QuestSO")]
public class QuestSO : ScriptableObject
{
    public Quest.QuestType questType;
    public string questName;
    [TextArea(3, 5)] public string questDescription;
    
    public Enemy[] enemiesToDefeat;
    
    [Tooltip("number of amount for each enemy. so you can say 3 of x and 2 of y")] 
    public int[] noOfEnemiesToDefeat;

    [Tooltip("The objects to find, give to, or take from an npc")] 
    public CollectableSOBase[] objectsToInteract;
    public int[] numberOfObjects;
    
    public DialogueObject npcToInteract;
    
    public CollectableSOBase[] rewards = new CollectableSOBase[1];
}
