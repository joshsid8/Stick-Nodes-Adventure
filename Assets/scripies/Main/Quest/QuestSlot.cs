using System;
using System.Collections.Generic;
using scripies.Core;
using scripies.Dialogue;
using scripies.Enemy;
using scripies.Player;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
public class QuestSlot : MonoBehaviour, IPointerClickHandler // SlotBase: Join the family. QuestSlot: Nah, ima do my own thing.
{
    public string questTitle;
    [TextArea(5, 10)] public string questDescription;
    public enum QuestType { DefeatEnemy, HaveInInventory, GiveTo, SpeakTo, NullQuest }
    [EnumButtons] public QuestType questType;
    
    public bool questCompleted;

    public List<EnemyToDefeat> enemiesToDefeat = new();
    private readonly Dictionary<Enemy, int> requiredByEnemy = new();  
    private readonly Dictionary<Enemy, int> currentKillsByEnemy = new(); 
    
    [Tooltip("objects to take, give, or find")] 
    public List<ObjectToInteract> objectsToInteract = new();

    [Tooltip("the npc you may have to speak to")] public NPC nTi;
    private NPC npcBuffer;

    public GameObject rewardObj;
    public CollectableSOBase[] questRewards;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private QuestLogManager questLogManager;
    private void Start()
    { 
        if (summaryText == null) summaryText = GetComponentInChildren<TMP_Text>();
        summaryText.text = questTitle;
        RefreshQuestState();
    }

    private void OnEnemyKilled(Enemy enemy)
    {
        if (questType != QuestType.DefeatEnemy || questCompleted) return;
        if (requiredByEnemy.ContainsKey(enemy) && enemy != null)
        {
            if (!currentKillsByEnemy.ContainsKey(enemy)) currentKillsByEnemy[enemy] = 0;
            
            if (currentKillsByEnemy[enemy] < requiredByEnemy[enemy])
            {
                currentKillsByEnemy[enemy]++;
                CheckCompletion();
            }
        }
    }

    private void OnInventoryChanged()
    {
        if (questType != QuestType.HaveInInventory || questCompleted) return;
        CheckCompletion();
    }

    private void OnDialogueClosed(NPC npc)
    {
        if (questType != QuestType.SpeakTo && questType != QuestType.GiveTo) return;
        npcBuffer = npc;
        CheckCompletion(); 
    }

    private void CheckCompletion()
    {
        if (questCompleted || questType == QuestType.NullQuest) return;

        var allMet = true;
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                foreach (var enemy in requiredByEnemy)
                {
                    var currentKills = currentKillsByEnemy.GetValueOrDefault(enemy.Key, 0);
                    if (currentKills < enemy.Value) allMet = false;
                }
                break;
            case QuestType.HaveInInventory:
                if (InvManager.Instance == null || InvManager.Instance.invIndex == null) return;
                foreach (var obj in objectsToInteract)
                {
                    if (!InvManager.Instance.invIndex.HasAtLeast(obj.objects, obj.count)) allMet = false;
                }
                break;
            case QuestType.GiveTo:
            case QuestType.SpeakTo:
                if (npcBuffer != nTi || npcBuffer == null) allMet = false;
                break;
            case QuestType.NullQuest:
            default:
                break;
        }
        UpdateTaskText();
        if (!allMet) return;
        CompleteQuest();
    }

    public void CompleteQuest()
    {
        GameEvents.TriggerPlaySound(QuestLogManager.Instance.questCompleteSound);
        QuestLogManager.Instance.completedQuests.Add(ResourceDatabase.Instance.GetQuestByName(questTitle));
        OnCleared();
        GiveRewards();
        ClearUI();
    }
    
    private void OnAssigned()
    {
        summaryText.text = questTitle;
        DialogueUI.OnDialogueClose += OnDialogueClosed;
        EnemyHealth.OnEnemyKilled += OnEnemyKilled;
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
            InvManager.Instance.invIndex.OnInventoryChanged += OnInventoryChanged;
    }

    private void UpdateTaskText()
    {
        if (questType == QuestType.NullQuest || questLogManager.currentlySelectedQuest != this || questLogManager.taskText == null) return;
        string taskStr;
        switch (questType)
        {
            case QuestType.DefeatEnemy:
            {
                taskStr = "Defeat ";
                foreach (var enemy in enemiesToDefeat)
                {
                    if (enemy == enemiesToDefeat[^1] && enemiesToDefeat.Count > 1) taskStr += " & ";
                    var eName = enemy.enemy.enemyName;
                    if (eName.Contains("man") && enemy.count > 1) eName = eName.Replace("man", "men");
                    else if (enemy.count > 1) eName += "s";
                    var str = $"{enemy.count} {eName}";
                    taskStr += str;
                    if (enemy != enemiesToDefeat[^1] && enemy != enemiesToDefeat[^2]) taskStr += ", ";
                } 
                break;
            }
            case QuestType.SpeakTo:
                taskStr = $"Speak to {nTi.npcName}";
                break;
            case QuestType.HaveInInventory:
            {
                taskStr = "Have ";
                foreach (var item in objectsToInteract)
                {
                    if (item == objectsToInteract[^1] && objectsToInteract.Count > 1) taskStr += " & ";
                    taskStr += item.count;
                    if (item.count == 1 && item.objects.itemName.EndsWith("s")) taskStr += " set of ";
                    else if (item.count > 1 && item.objects.itemName.EndsWith("s")) taskStr += " sets of ";
                    else taskStr += " ";
                    taskStr += item.objects.itemName;
                    if (item.count > 1) taskStr += "s";
                    if (item != objectsToInteract[^1] && item != objectsToInteract[^2]) taskStr += ", ";
                }
                taskStr += " in your inventory";
                break;
            }
            case QuestType.GiveTo:
            {
                taskStr = "Give ";
                foreach (var item in objectsToInteract)
                {
                    if (item == objectsToInteract[^1] && objectsToInteract.Count > 1) taskStr += "& ";
                    taskStr += $"{item.count} {item.objects.itemName}";
                    if (item.count > 1) taskStr += "s";
                    if (item != objectsToInteract[^1]) taskStr += ", ";
                }
                taskStr += " to " + nTi.npcName;
                break;
            }
            case QuestType.NullQuest:
            default:
                taskStr = "";
                break;
        }
        questLogManager.taskText.text = taskStr;
    }
    
    private void OnCleared()
    {
        DialogueUI.OnDialogueClose -= OnDialogueClosed;
        EnemyHealth.OnEnemyKilled -= OnEnemyKilled;
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
            InvManager.Instance.invIndex.OnInventoryChanged -= OnInventoryChanged;
    }

    private void GiveRewards()
    {
        if (rewardObj == null) return;
        foreach (var rewardSo in questRewards)
        {
            var reward = Instantiate(rewardObj, PlayerHealth.Instance.gameObject.transform.position, Quaternion.identity);
            var collectable = reward.GetComponent<Collectable>();
            if (collectable != null) collectable.collectable = rewardSo;
        }
    }

    private void RefreshQuestState()
    {
        if (questType == QuestType.NullQuest) return;
        CheckCompletion();
    }

    private void UpdateUI()
    {
        questLogManager.titleText.text = questTitle;
        questLogManager.descText.text = questDescription;
        questLogManager.currentlySelectedQuest = this;
        UpdateTaskText();
    }
    
    public void LogQuest(QuestType type, string title, string description, Enemy[] enemies, int[] eToDefeat, CollectableSOBase[] objects, int[] noOfObj, NPC npc, CollectableSOBase[] rewards)
    {
        questType = type;
        questTitle = title;
        questDescription = description;
        nTi = npc;
        enemiesToDefeat.Clear();
        if (enemies != null && eToDefeat != null)
        {
            for (var i = 0; i < enemies.Length; i++)
            {
                enemiesToDefeat.Add(new EnemyToDefeat
                {
                    enemy = enemies[i],
                    count = eToDefeat[i]
                });
            }
        }
        objectsToInteract.Clear();
        if (objects != null && noOfObj != null)
        {
            for (var i = 0; i < objects.Length; i++)
            {
                objectsToInteract.Add(new ObjectToInteract
                {
                    objects = objects[i],
                    count = noOfObj[i]
                });
            }
        }
        questRewards = rewards != null ? (CollectableSOBase[])rewards.Clone() : Array.Empty<CollectableSOBase>();
        
        requiredByEnemy.Clear();
        currentKillsByEnemy.Clear();

        if (questType == QuestType.DefeatEnemy && enemiesToDefeat.Count > 0)
        {
            foreach (var enemyToDefeat in enemiesToDefeat)
            {
                var enemy = enemyToDefeat.enemy;
                if (enemy == null) continue;
                var count = enemyToDefeat.count;
                requiredByEnemy.TryAdd(enemy, count);
            }
        }
        OnAssigned();
        questLogManager.titleText.text = questTitle;
        RefreshQuestState();
    }

    public void ClearUI()
    {
        questTitle = "";
        questLogManager.titleText.text = "";
        summaryText.text = "";
        questDescription = "";
        nTi = null;
        npcBuffer = null;
        enemiesToDefeat.Clear();
        objectsToInteract.Clear();
        questRewards = Array.Empty<CollectableSOBase>();
        questCompleted = false;
        questType = QuestType.NullQuest;
        questLogManager.descText.text = "";
        questLogManager.titleText.text = "";
        requiredByEnemy.Clear();
        currentKillsByEnemy.Clear();
        if (questLogManager.currentlySelectedQuest == this) questLogManager.currentlySelectedQuest = null;
        gameObject.SetActive(false);
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && questType != QuestType.NullQuest) UpdateUI();
    }
}

[Serializable]
public class EnemyToDefeat
{
    public Enemy enemy;
    public int count;
}

[Serializable]
public class ObjectToInteract
{
    public CollectableSOBase objects;
    public int count;
}