using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
public class Quest : MonoBehaviour, IPointerClickHandler
{
    public string questTitle;
    [TextArea(5, 10)] public string questDescription;
    public QuestType questType;
    
    [SerializeField] private TMP_Text[] titleTexts;
    [SerializeField] private TMP_Text taskText;
    
    [SerializeField] private TMP_Text infoTitle;
    [SerializeField] private TMP_Text infoDescription;
    [SerializeField] private TMP_Text objectiveText;
    
    public bool questCompleted;

    [Tooltip("enemies to defeat")] public Enemy[] eTd = Array.Empty<Enemy>(); 
    [Tooltip("Number of enemies currently defeated")] public int[] noOfE = Array.Empty<int>();
    private Dictionary<Enemy, int> requiredByEnemy = new Dictionary<Enemy, int>();  
    private Dictionary<Enemy, int> currentKillsByEnemy = new Dictionary<Enemy, int>(); 
    
    [Tooltip("objects to take, give, or find")] public CollectableSOBase[] oTi;
    public int[] noOfO;

    [Tooltip("the npc you may have to speak to")] public DialogueObject nTi;

    public GameObject rewardObj;
    public CollectableSOBase[] questRewards;
    public enum QuestType
    { 
        DefeatEnemy, HaveInInventory, GiveTo, SpeakTo, NullQuest
    }

    private void OnEnable()
    {
        EnemyHealth.OnEnemyKilled += OnEnemyKilled;
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
            InvManager.Instance.invIndex.OnInventoryChanged += OnInventoryChanged;
    }

    private void OnDisable()
    {
        EnemyHealth.OnEnemyKilled -= OnEnemyKilled;
        if (InvManager.Instance != null && InvManager.Instance.invIndex != null)
            InvManager.Instance.invIndex.OnInventoryChanged -= OnInventoryChanged;
    }

    private void Start()
    { 
        foreach (var text in titleTexts) text.text = questTitle;
        RefreshQuestState();
    }

    private void OnEnemyKilled(Enemy enemy)
    {
        if (questType != QuestType.DefeatEnemy || questCompleted || enemy == null) return;

        if (requiredByEnemy.ContainsKey(enemy))
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

    private void CheckCompletion()
    {
        if (questCompleted || questType == QuestType.NullQuest) return;

        bool allMet = true;
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                foreach (var kvp in requiredByEnemy)
                {
                    int current = currentKillsByEnemy.ContainsKey(kvp.Key) ? currentKillsByEnemy[kvp.Key] : 0;
                    if (current < kvp.Value)
                    {
                        allMet = false;
                        break;
                    }
                }
                break;
            case QuestType.HaveInInventory:
                if (InvManager.Instance == null || InvManager.Instance.invIndex == null) return;
                for (int i = 0; i < oTi.Length; i++)
                {
                    if (!InvManager.Instance.invIndex.HasAtLeast(oTi[i], noOfO[i]))
                    {
                        allMet = false;
                        break;
                    }
                }
                break;
        }

        if (allMet)
        {
            questCompleted = true;
            GiveRewards();
            ClearUI();
        }
    }

    private void GiveRewards()
    {
        if (rewardObj == null) return;
        foreach (var rewardSO in questRewards)
        {
            var reward = Instantiate(rewardObj, PlayerHealth.Instance.gameObject.transform.position, Quaternion.identity);
            var collectable = reward.GetComponent<Collectable>();
            if (collectable != null) collectable.collectable = rewardSO;
        }
    }

    private void RefreshQuestState()
    {
        if (questType == QuestType.NullQuest) return;
        CheckCompletion();
    }

    void UpdateUI()
    {
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
    }
    
    public void LogQuest(QuestType type, string title, string description, Enemy[] enemies, int[] eToDefeat, CollectableSOBase[] objects, int[] noOfObj, DialogueObject npc, CollectableSOBase[] rewards)
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
            int len = Math.Min(eTd.Length, noOfE.Length);
            for (int i = 0; i < len; i++)
            {
                var enemy = eTd[i];
                if (enemy == null) continue;
                int count = noOfE[i];
                if (requiredByEnemy.ContainsKey(enemy)) requiredByEnemy[enemy] += count;
                else requiredByEnemy[enemy] = count;
            }
        }

        foreach (var text in titleTexts) text.text = questTitle;
        RefreshQuestState();
    }

    public void ClearUI()
    {
        foreach (var text in titleTexts) text.text = "";
        questDescription = "";
        oTi = Array.Empty<CollectableSOBase>();
        nTi = null;
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
        if (eventData.button == PointerEventData.InputButton.Left) UpdateUI();
    }
}