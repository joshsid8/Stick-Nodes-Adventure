using System;
using System.Collections.Generic;
using scripies.Player;
using UnityEngine;

public class QuestLogManager : GUIBase
{
    public static QuestLogManager Instance { get; private set; }

    public QuestSlot[] questSlots;

    public GameObject questMenu;
    
    // add this variable into the data file for future use (e.g checking if the player has already done a quest)
    public List<QuestSO> completedQuests = new();
    public AudioClip questCompleteSound;
    
    protected override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        base.Awake();
        player = FindAnyObjectByType<PlayerController>();
        
        if (isActive) ToggleQuestLog();
        
        questSlots[0].titleTexts[0].text = "";
        questSlots[0].infoDescription.text = "Pick a quest, if applicable";
        questSlots[0].taskText.text = "<---";
    }

    public void AssignQuest(QuestSO quest)
    {
        if (quest == null || completedQuests.Contains(quest)) return;
        for (var i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null && questSlots[i].questType == QuestSlot.QuestType.NullQuest)
            {
                questSlots[i].gameObject.SetActive(true);
                questSlots[i].LogQuest(quest.questType, quest.questName, quest.questDescription, quest.enemiesToDefeat,
                    quest.noOfEnemiesToDefeat, quest.objectsToInteract, quest.numberOfObjects, quest.npcToInteract, quest.rewards);
                return;
            }
        }
    }

    private void Update()
    {
        if (Input.GetButtonDown("Quest")) ToggleQuestLog();
        else if (Input.GetButtonDown("Cancel") && isActive)
        {
            isActive = true;
            ToggleQuestLog();
        }
    }

    public void ToggleQuestLog()
    {
        isActive = !isActive;
        if (questMenu != null) 
            rt.anchoredPosition = new Vector2(isActive ? 0 : -2000, 0);
        GetActive(isActive);
        if (isActive) CloseCurrentMenus();
    }

    public void Save(ref PlayerSaveQuests data)
    {
        data.activeQuests = new List<QuestSaveData>();
        foreach (var slot in questSlots)
        {
            if (slot != null && slot.questType != QuestSlot.QuestType.NullQuest)
            {
                data.activeQuests.Add(new QuestSaveData
                {
                    questTitle = slot.questTitle,
                    questDescription = slot.questDescription,
                    questType = slot.questType,
                    enemiesToDefeat = slot.enemiesToDefeat != null ? slot.enemiesToDefeat.ConvertAll(e => e != null && e.enemy != null ? e.enemy.name : "").ToArray() : Array.Empty<string>(),
                    noOfEnemiesToDefeat = slot.enemiesToDefeat != null ? slot.enemiesToDefeat.ConvertAll(e => e.count).ToArray() : Array.Empty<int>(),
                    objectsToInteract = slot.oTi != null ? Array.ConvertAll(slot.oTi, o => o != null ? o.name : "") : Array.Empty<string>(),
                    numberOfObjects = slot.noOfO,
                    npcToInteract = slot.nTi != null ? slot.nTi.name : "",
                    rewards = slot.questRewards != null ? Array.ConvertAll(slot.questRewards, r => r != null ? r.name : "") : Array.Empty<string>(),
                    isCompleted = slot.questCompleted
                });
            }
        }
    }

    public void Load(PlayerSaveQuests data)
    {
        for (int i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null) questSlots[i].ClearUI();
        }

        if (data.activeQuests == null) return;

        for (int i = 0; i < Math.Min(data.activeQuests.Count, questSlots.Length); i++)
        {
            var qData = data.activeQuests[i];
            var enemies = Array.ConvertAll(qData.enemiesToDefeat, n => ResourceDatabase.Instance.GetEnemyByName(n));
            var objects = Array.ConvertAll(qData.objectsToInteract, id => ResourceDatabase.Instance.GetItemByName(id));
            var npc = qData.questType == QuestSlot.QuestType.SpeakTo || qData.questType == QuestSlot.QuestType.GiveTo ? ResourceDatabase.Instance.GetNpcByName(qData.npcToInteract) : null;
            var rewards = Array.ConvertAll(qData.rewards, id => ResourceDatabase.Instance.GetItemByName(id));
            
            questSlots[i].LogQuest(qData.questType, qData.questTitle, qData.questDescription, enemies, 
                qData.noOfEnemiesToDefeat, objects, qData.numberOfObjects, npc, rewards);
            questSlots[i].questCompleted = qData.isCompleted;
        }
    }
}

[Serializable]
public struct QuestSaveData
{
    public string questTitle;
    public string questDescription;
    public QuestSlot.QuestType questType;
    public string[] enemiesToDefeat;
    public int[] noOfEnemiesToDefeat;
    public string[] objectsToInteract;
    public int[] numberOfObjects;
    public string npcToInteract;
    public string[] rewards;
    public bool isCompleted;
}

[Serializable]
public struct PlayerSaveQuests
{
    public List<QuestSaveData> activeQuests;
}