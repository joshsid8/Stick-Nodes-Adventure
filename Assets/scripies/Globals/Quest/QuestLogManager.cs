using System;
using UnityEngine;
public class QuestLogManager : MonoBehaviour
{
    public static QuestLogManager Instance {get; private set;}
    
    public Quest[] questSlots;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    public void AssignQuest(QuestSO quest)
    {
        AddQuest(quest.questType, quest.questName, quest.questDescription, quest.enemiesToDefeat,
            quest.noOfEnemiesToDefeat, quest.objectsToInteract, quest.npcToInteract,quest.rewards);
    }

    private void AddQuest(Quest.QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat, string[] objects, string npc,CollectableSOBase[] reward)
    {
        for (var i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null)
            {
                questSlots[i].LogQuest(type, title, description, enemyIDs ,eToDefeat, objects, npc, reward);
                return;
            }
        }
    }

}
