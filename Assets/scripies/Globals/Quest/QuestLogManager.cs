using System;
using UnityEngine;
public class QuestLogManager : MonoBehaviour
{
    public static QuestLogManager Instance {get; private set;}
    
    public Quest[] questSlots;
    
    public GameObject questMenu;
    public bool isActivated;
    private PlayerController player;
    private RectTransform rt;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); return;
        }
        Instance = this;
        player = FindAnyObjectByType<PlayerController>();
        rt = GetComponent<RectTransform>();
    }
    
    public void AssignQuest(QuestSO quest)
    {
        AddQuest(quest.questType, quest.questName, quest.questDescription, quest.enemiesToDefeat,
            quest.noOfEnemiesToDefeat, quest.objectsToInteract, quest.numberOfObjects, quest.npcToInteract,quest.rewards);
    }
    
    void Update()
    {
        if (Input.GetButtonDown("Quest")) ToggleQuestLog();
        else if (Input.GetButtonDown("Cancel") && isActivated)
        {
            isActivated = true;
            ToggleQuestLog();
        }
    }
    
    private void ToggleQuestLog()
    {
        isActivated = !isActivated;
        if (questMenu != null) rt.anchoredPosition = new Vector2(0, isActivated ? 0 : 1000);
        if (player != null) player.canAct = !isActivated;
    }

    private void AddQuest(Quest.QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat, CollectableSOBase[] objects, int[] noOfO, DialogueObject npc,CollectableSOBase[] reward)
    {
        for (var i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null && questSlots[i].questDescription == String.Empty)
            {
                questSlots[i].LogQuest(type, title, description, enemyIDs , eToDefeat, objects, noOfO,npc, reward); return;
            }
        }
    }

    public void Save(ref PlayerSaveQuests data)
    {
        data.quests = questSlots;
    }
    public void Load(PlayerSaveQuests data)
    {
        questSlots = data.quests;
    }
    
}
[System.Serializable]
public struct PlayerSaveQuests
{
    public Quest[] quests;
}