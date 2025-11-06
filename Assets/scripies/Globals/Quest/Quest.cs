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

    [Tooltip("enemies to defeat")] public int[] eTd = Array.Empty<int>(); 
    [Tooltip("Number of enemies currently defeated")] [SerializeField] private int[] noOfE = Array.Empty<int>();
    [SerializeField] private GameObject[] targetEnemies;
    private Dictionary<int, int> requiredById;  
    private Dictionary<int, int> remainingById; 
    private Dictionary<GameObject, int> enemyToId;
    private InvIndex invIndex;
    
    [Tooltip("objects to take, give, or find")] public CollectableSOBase[] oTi;
    public int[] noOfO;

    [Tooltip("the npc you may have to speak to")] public DialogueObject nTi;

    public GameObject rewardObj;
    public CollectableSOBase[] questRewards;
    public enum QuestType
    { 
        DefeatEnemy, HaveInInventory, GiveTo, SpeakTo, NullQuest
    }
    private void Start()
    { 
        foreach (var text in titleTexts) text.text = questTitle;
    }
    private void Update()
    {
        QuestTrack();
        if (questType == QuestType.NullQuest) 
        {
            foreach (var text in titleTexts) text.text = "";
            questDescription = "";
        }
    }

    void UpdateUI()
    {
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
    }
    private void QuestTrack()
    {
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                var alive = new List<GameObject>(targetEnemies.Length);
                for (int i = 0; i < targetEnemies.Length; i++)
                {
                    var go = targetEnemies[i];
                    if (go == null) continue;
                    var eh = go.GetComponent<EnemyHealth>();
                    if (eh == null) continue;
                    if (!enemyToId.TryGetValue(go, out var id))
                    {
                        id = eh.enemyID;
                        enemyToId[go] = id;
                    }
                    if (eh.isDead)
                    {
                        if (remainingById.TryGetValue(id, out var rem) && rem > 0) remainingById[id] = rem - 1;
                        enemyToId.Remove(go);
                        continue;
                    }
                    alive.Add(go);
                }
                targetEnemies = alive.ToArray();
                var totalRequired = 0;
                var totalRemaining = 0;
                foreach (var kv in requiredById) totalRequired += kv.Value;
                foreach (var kv in remainingById) totalRemaining += kv.Value;
                var totalDefeated = totalRequired - totalRemaining;
                //objectiveText.text = $"Defeat {totalDefeated}/{totalRequired} enemies";
                if (totalRemaining <= 0 && questType == QuestType.DefeatEnemy)
                {
                    questCompleted = true;
                    targetEnemies = Array.Empty<GameObject>();
                }
                break;
            
            case QuestType.HaveInInventory:
                var needs = Array.Empty<(CollectableSOBase id, int count)>();
                for (var i = 0; i < oTi.Length; i++) needs = new (CollectableSOBase id, int count)[] {(oTi[i], noOfO[i])};
                
                if (InvManager.Instance == null || InvManager.Instance.Index == null)
                {Debug.LogWarning("Quest: InvManager/InvIndex doesnt exist. KILL YOURSELF!!!"); return;}
                
                var canCompleteQuest = InvManager.Instance.Index.MeetsAllCounts(needs);
                if (canCompleteQuest) questCompleted = true;
                break;
            
            case QuestType.GiveTo:
                
                break;
            case QuestType.SpeakTo:
                break;
            case QuestType.NullQuest:
                break;
        }
        if (questCompleted)
        {
            for (var i = 0; i < questRewards.Length; i++)
            {
                var reward = Instantiate(rewardObj, PlayerHealth.Instance.gameObject.transform.position, Quaternion.identity);
                reward.GetComponent<Collectable>().collectable = questRewards[i]; 
            }
            ClearUI();
        }
    }
    public void LogQuest(QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat, CollectableSOBase[] objects, int[] noOfObj, DialogueObject npc, CollectableSOBase[] rewards)
    {
        questType = type;
        questTitle = title;
        questDescription = description;
        nTi = npc;
        eTd = enemyIDs != null ? (int[])enemyIDs.Clone() : Array.Empty<int>();
        noOfE = eToDefeat != null ? (int[])eToDefeat.Clone() : Array.Empty<int>();
        oTi = objects != null ? (CollectableSOBase[])objects.Clone() : Array.Empty<CollectableSOBase>();
        noOfO = noOfObj != null ? (int[])noOfObj.Clone() : Array.Empty<int>();
        questRewards = rewards != null ? (CollectableSOBase[])rewards.Clone() : Array.Empty<CollectableSOBase>();

        foreach (var text in titleTexts) text.text = questTitle;
        
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                var enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
                var targets = new List<GameObject>(enemies.Length);
                foreach (var eh in enemies)
                {
                    if (eh == null) continue;
                    if (Array.IndexOf(eTd, eh.enemyID) >= 0) targets.Add(eh.gameObject);
                }
                targetEnemies = targets.ToArray();

                requiredById = new Dictionary<int, int>();
                remainingById = new Dictionary<int, int>();

                var len = Math.Min(eTd.Length, noOfE.Length);
                for (var i = 0; i < len; i++)
                {
                    var id = eTd[i];
                    var toDefeat = noOfE[i];
                    if (requiredById.TryGetValue(id, out var curReq)) requiredById[id] = curReq + toDefeat;
                    else requiredById[id] = toDefeat;
                    remainingById[id] = requiredById[id];
                }

                enemyToId = new Dictionary<GameObject, int>(targetEnemies.Length);
                for (var i = 0; i < targetEnemies.Length; i++)
                {
                    var go = targetEnemies[i];
                    if (go == null) continue;
                    var eh = go.GetComponent<EnemyHealth>();
                    if (eh == null) continue;
                    enemyToId[go] = eh.enemyID;
                }
                break;
            case QuestType.HaveInInventory:
                //print($"ensure you have {objects[0]} in your inventory!");
                break;
            case QuestType.NullQuest:
                // this aint nothin dawg
                break;
        }
    }

    public void ClearUI()
    {
        foreach (var text in titleTexts) text.text = "";
        questDescription = "";
        targetEnemies = Array.Empty<GameObject>();
        oTi = Array.Empty<CollectableSOBase>();
        nTi = null;
        eTd = Array.Empty<int>();
        noOfE = Array.Empty<int>();
        questRewards = Array.Empty<CollectableSOBase>();
        questCompleted = false;
        questType = QuestType.NullQuest;
        infoDescription.text = "";
        infoTitle.text = "";
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) UpdateUI();
    }
}