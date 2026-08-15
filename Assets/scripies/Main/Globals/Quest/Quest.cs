using System;
using System.Collections.Generic;
using scripies.Core;
using scripies.Dialogue;
using scripies.Enemy;
using scripies.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
public class Quest : MonoBehaviour, IPointerClickHandler
{
    public string questTitle;
    [TextArea(5, 10)] public string questDescription;
    public enum QuestType { DefeatEnemy, HaveInInventory, GiveTo, SpeakTo, NullQuest }
    public QuestType questType;
    
    [SerializeField] private TMP_Text[] titleTexts;
    [SerializeField] private TMP_Text taskText;
    
    [SerializeField] private TMP_Text infoTitle;
    [SerializeField] private TMP_Text infoDescription;
    [SerializeField] private TMP_Text objectiveText;
    
    public bool questCompleted;

    [Tooltip("enemies to defeat")] public Enemy[] eTd = Array.Empty<Enemy>(); 
    [Tooltip("Number of enemies currently defeated")] public int[] noOfE = Array.Empty<int>();
    private readonly Dictionary<Enemy, int> requiredByEnemy = new Dictionary<Enemy, int>();  
    private readonly Dictionary<Enemy, int> currentKillsByEnemy = new Dictionary<Enemy, int>(); 
    
    [Tooltip("objects to take, give, or find")] public CollectableSOBase[] oTi;
    public int[] noOfO;

    [Tooltip("the npc you may have to speak to")] public NPC nTi;
    private NPC npcBuffer;

    public GameObject rewardObj;
    public CollectableSOBase[] questRewards;
    
    //TODO: I think i will make it so that you have to speak to the quest giver if you want your quest completed now, it makes more sense, albeit more annoying for players
    public GameObject questGiver;
    public DialogueObject appreciationDialogue;
    
    private void Start()
    { 
        foreach (var text in titleTexts) text.text = questTitle;
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
        if (questType == QuestType.SpeakTo || questType == QuestType.GiveTo)
        {
            npcBuffer = npc;
            CheckCompletion();
        }
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
                    var current = currentKillsByEnemy.ContainsKey(enemy.Key) ? currentKillsByEnemy[enemy.Key] : 0;
                    if (current < enemy.Value)
                    {
                        allMet = false;
                        break;
                    }
                }
                break;
            case QuestType.HaveInInventory:
                if (InvManager.Instance == null || InvManager.Instance.invIndex == null) return;
                for (var i = 0; i < oTi.Length; i++)
                {
                    if (!InvManager.Instance.invIndex.HasAtLeast(oTi[i], noOfO[i]))
                    {
                        allMet = false;
                        break;
                    }
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

        if (allMet)
        {
            GameEvents.TriggerSound(QuestLogManager.Instance.questCompleteSound);
            OnCleared();
            GiveRewards();
            ClearUI();
        }
    }
    
    private void OnAssigned()
    {
        DialogueUI.OnDialogueClose += OnDialogueClosed;
        EnemyHealth.OnEnemyKilled += OnEnemyKilled;
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
            InvManager.Instance.invIndex.OnInventoryChanged += OnInventoryChanged;
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
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
    }
    
    public void LogQuest(QuestType type, string title, string description, Enemy[] enemies, int[] eToDefeat, CollectableSOBase[] objects, int[] noOfObj, NPC npc, CollectableSOBase[] rewards)
    {
        questType = type;
        questTitle = title;
        questDescription = description;
        nTi = npc;
        eTd = enemies != null ? (Enemy[])enemies.Clone() : Array.Empty<Enemy>();
        noOfE = eToDefeat != null ? (int[])eToDefeat.Clone() : Array.Empty<int>();
        oTi = objects != null ? (CollectableSOBase[])objects.Clone() : Array.Empty<CollectableSOBase>();
        noOfO = noOfObj != null ? (int[])noOfObj.Clone() : Array.Empty<int>();
        questRewards = rewards != null ? (CollectableSOBase[])rewards.Clone() : Array.Empty<CollectableSOBase>();
        
        requiredByEnemy.Clear();
        currentKillsByEnemy.Clear();

        if (questType == QuestType.DefeatEnemy && eTd != null && noOfE != null)
        {
            var len = Math.Min(eTd.Length, noOfE.Length);
            for (var i = 0; i < len; i++)
            {
                var enemy = eTd[i];
                if (enemy == null) continue;
                var count = noOfE[i];
                if (requiredByEnemy.ContainsKey(enemy)) requiredByEnemy[enemy] += count;
                else requiredByEnemy[enemy] = count;
            }
        }
        OnAssigned();
        foreach (var text in titleTexts) text.text = questTitle;
        RefreshQuestState();
    }

    public void ClearUI()
    {
        questTitle = "";
        foreach (var text in titleTexts) text.text = "";
        questDescription = "";
        oTi = Array.Empty<CollectableSOBase>();
        nTi = null;
        npcBuffer = null;
        eTd = Array.Empty<Enemy>();
        noOfE = Array.Empty<int>();
        questRewards = Array.Empty<CollectableSOBase>();
        questCompleted = false;
        questType = QuestType.NullQuest;
        infoDescription.text = "";
        infoTitle.text = "";
        requiredByEnemy.Clear();
        currentKillsByEnemy.Clear();
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && questType != QuestType.NullQuest) UpdateUI();
    }
}