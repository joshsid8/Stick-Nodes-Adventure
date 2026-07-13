using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

public class QuestLogManager : GUIBase
{
    public static QuestLogManager Instance { get; private set; }

    public Quest[] questSlots;

    public GameObject questMenu;

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
        rt = GetComponent<RectTransform>();
        
        if (isActive) ToggleQuestLog();
    }

    public void AssignQuest(QuestSO quest)
    {
        for (var i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null && questSlots[i].questType == Quest.QuestType.NullQuest)
            {
                questSlots[i].LogQuest(quest.questType, quest.questName, quest.questDescription, quest.enemiesToDefeat,
                    quest.noOfEnemiesToDefeat, quest.objectsToInteract, quest.numberOfObjects, quest.npcToInteract,
                    quest.rewards);
                return;
            }
        }
    }

    void Update()
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
        {
            rt.anchoredPosition = new Vector2(0, isActive ? 0 : 1000);
            questMenu.SetActive(isActive);
        }
        GetActive(isActive);
    }

    public void Save(ref PlayerSaveQuests data)
    {
        data.activeQuests = new List<QuestSaveData>();
        foreach (var slot in questSlots)
        {
            if (slot != null && slot.questType != Quest.QuestType.NullQuest)
            {
                data.activeQuests.Add(new QuestSaveData
                {
                    questTitle = slot.questTitle,
                    questDescription = slot.questDescription,
                    questType = slot.questType,
                    enemiesToDefeat = slot.eTd != null ? Array.ConvertAll(slot.eTd, e => e != null ? e.name : "") : Array.Empty<string>(),
                    noOfEnemiesToDefeat = slot.noOfE,
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
            var enemies = Array.ConvertAll(qData.enemiesToDefeat, name => EnemyDatabase.Instance.GetEnemyByName(name));
            var objects = Array.ConvertAll(qData.objectsToInteract, id => ItemDatabase.Instance.GetItemByName(id));
            var rewards = Array.ConvertAll(qData.rewards, id => ItemDatabase.Instance.GetItemByName(id));
            
            // Note: npcToInteract loading might need a database if it's a ScriptableObject
            // For now, we'll assume it's handled or we'll need a way to look up DialogueObject by name.
            
            questSlots[i].LogQuest(qData.questType, qData.questTitle, qData.questDescription, enemies, 
                qData.noOfEnemiesToDefeat, objects, qData.numberOfObjects, null, rewards);
            questSlots[i].questCompleted = qData.isCompleted;
        }
    }
}

[Serializable]
public struct QuestSaveData
{
    public string questTitle;
    public string questDescription;
    public Quest.QuestType questType;
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