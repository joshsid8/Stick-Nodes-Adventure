using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class QuestLogManager : MonoBehaviour
{
    public static QuestLogManager Instance { get; private set; }

    public Quest[] questSlots;

    public GameObject questMenu;
    public bool isActivated;
    private PlayerController player;
    private RectTransform rt;

    string[] questNames;
    string[] questDescriptions;
    Quest.QuestType[] questTypes;

    int[][] questEnemies;
    int[][] questNoOfEnemies;
    CollectableSOBase[][] questObjects;
    int[][] questNoOfObjects;
    DialogueObject[] questNpCs;
    CollectableSOBase[][] questRewards;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        player = FindAnyObjectByType<PlayerController>();
        rt = GetComponent<RectTransform>();

        var n = questSlots != null && questSlots.Length > 0 ? questSlots.Length : 8;

        questNames = new string[n];
        questDescriptions = new string[n];
        questTypes = new Quest.QuestType[n];

        questEnemies = new int[n][];
        questNoOfEnemies = new int[n][];
        questObjects = new CollectableSOBase[n][];
        questNoOfObjects = new int[n][];
        questNpCs = new DialogueObject[n];
        questRewards = new CollectableSOBase[n][];

    }

    public void AssignQuest(QuestSO quest)
    {
        AddQuest(quest.questType, quest.questName, quest.questDescription, quest.enemiesToDefeat,
            quest.noOfEnemiesToDefeat, quest.objectsToInteract, quest.numberOfObjects, quest.npcToInteract,
            quest.rewards);
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

    private void AddQuest(Quest.QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat,
        CollectableSOBase[] objects, int[] noOfO, DialogueObject npc, CollectableSOBase[] reward)
    {
        for (var i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null && questSlots[i].questDescription == string.Empty)
            {
                questSlots[i].LogQuest(type, title, description, enemyIDs, eToDefeat, objects, noOfO, npc, reward);

                // Save data into our parallel storage
                questNames[i] = title;
                questDescriptions[i] = description;
                questTypes[i] = type;

                questEnemies[i] = (int[])enemyIDs.Clone();
                questNoOfEnemies[i] = (int[])eToDefeat.Clone();
                questObjects[i] = (CollectableSOBase[])objects.Clone();
                questNoOfObjects[i] = (int[])noOfO.Clone();
                questNpCs[i] = npc;
                questRewards[i] = (CollectableSOBase[])reward.Clone();

                return;
            }
        }

    }

    public void Save(ref PlayerSaveQuests data)
    {
        data.name = questNames;
        data.description = questDescriptions;
        data.type = questTypes;
        data.npcs = questNpCs;
        
        data.enemies = ConvertJaggedArrayToSerializableList(questEnemies);
        data.enemiesToDefeat = ConvertJaggedArrayToSerializableList(questNoOfEnemies);
        
        data.objectIDs = ConvertCollectableJaggedArrayToStrings(questObjects);
        data.noOfObjects = ConvertJaggedArrayToSerializableList(questNoOfObjects);
        data.rewardIDs = ConvertCollectableJaggedArrayToStrings(questRewards);
    }

    public void Load(PlayerSaveQuests data)
    {
        questNames = data.name;
        questDescriptions = data.description;
        questTypes = data.type;
        questNpCs = data.npcs;
        
        questEnemies = ConvertSerializableListToJaggedArray(data.enemies);
        questNoOfEnemies = ConvertSerializableListToJaggedArray(data.enemiesToDefeat);
        
        questObjects = ConvertStringJaggedArrayToCollectableSO(data.objectIDs);
        questNoOfObjects = ConvertSerializableListToJaggedArray(data.noOfObjects);
        questRewards = ConvertStringJaggedArrayToCollectableSO(data.rewardIDs);
        
        for (var i = 0; i < questSlots.Length; i++)
        {
            if (questSlots[i] != null)
            {
                if (i < questNames.Length && !string.IsNullOrEmpty(questNames[i]))
                    questSlots[i].LogQuest(questTypes[i], questNames[i], questDescriptions[i], questEnemies[i], 
                        questNoOfEnemies[i], questObjects[i], questNoOfObjects[i], questNpCs[i], questRewards[i]);
                else questSlots[i].ClearUI();
            }
        }
    }
    private List<PlayerSaveQuests.SerializableIntArray> ConvertJaggedArrayToSerializableList(int[][] jaggedArray)
    {
        if (jaggedArray == null) return new List<PlayerSaveQuests.SerializableIntArray>();
        
        var serializableList = new List<PlayerSaveQuests.SerializableIntArray>(jaggedArray.Length);
        
        for (var i = 0; i < jaggedArray.Length; i++)
            serializableList.Add(new PlayerSaveQuests.SerializableIntArray { array = jaggedArray[i] ?? Array.Empty<int>() });
        return serializableList;
    }

    private int[][] ConvertSerializableListToJaggedArray(List<PlayerSaveQuests.SerializableIntArray> serializableList)
    {
        if (serializableList == null) return Array.Empty<int[]>();
        
        var jaggedArray = new int[serializableList.Count][];
        for (var i = 0; i < serializableList.Count; i++) jaggedArray[i] = serializableList[i].array ?? Array.Empty<int>();
        return jaggedArray;
    }

    private List<PlayerSaveQuests.SerializableStringArray> ConvertCollectableJaggedArrayToStrings(CollectableSOBase[][] collectableArray)
    {
        if (collectableArray == null) return new List<PlayerSaveQuests.SerializableStringArray>();
        
        var serializableList = new List<PlayerSaveQuests.SerializableStringArray>(collectableArray.Length);
        
        for (var i = 0; i < collectableArray.Length; i++)
        {
            var innerArray = collectableArray[i];
            if (innerArray == null) serializableList.Add(new PlayerSaveQuests.SerializableStringArray { array = Array.Empty<string>() });
            else
            {
                // Convert CollectableSOBase references to their names (IDs)
                var stringArray = new string[innerArray.Length];
                for(var j = 0; j < innerArray.Length; j++) stringArray[j] = innerArray[j] != null ? innerArray[j].name : string.Empty;
                serializableList.Add(new PlayerSaveQuests.SerializableStringArray { array = stringArray });
            }
        }
        return serializableList;
    }

    private CollectableSOBase[][] ConvertStringJaggedArrayToCollectableSO(List<PlayerSaveQuests.SerializableStringArray> stringList)
    {
        if (stringList == null) return Array.Empty<CollectableSOBase[]>();
        
        if (ItemDatabase.Instance == null)
        {
            Debug.LogError("[QuestLogManager] ItemDatabase not initialized. Cannot load quest items/rewards.");
            return Array.Empty<CollectableSOBase[]>();
        }
        
        var collectableArray = new CollectableSOBase[stringList.Count][];
        
        for (var i = 0; i < stringList.Count; i++)
        {
            var stringArray = stringList[i].array;
            if (stringArray == null) collectableArray[i] = Array.Empty<CollectableSOBase>();
            else
            {
                var soArray = new CollectableSOBase[stringArray.Length];
                for(var j = 0; j < stringArray.Length; j++) soArray[j] = ItemDatabase.Instance.GetItemByName(stringArray[j]);
                collectableArray[i] = soArray;
            }
        }
        return collectableArray;
    }
}

[Serializable]
public struct PlayerSaveQuests
{
    public string[] name;
    public string[] description;
    public Quest.QuestType[] type;
    public DialogueObject[] npcs;
    [Serializable]
    public struct SerializableIntArray { public int[] array; }
    [Serializable]
    public struct SerializableStringArray { public string[] array; }
    public List<SerializableIntArray> enemies;
    public List<SerializableIntArray> enemiesToDefeat;
    public List<SerializableIntArray> noOfObjects;
    public List<SerializableStringArray> objectIDs;
    public List<SerializableStringArray> rewardIDs;
}