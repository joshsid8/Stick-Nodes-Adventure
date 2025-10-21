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
    public bool isTracking;

    [Tooltip("enemies to defeat")] public int[] eTd;
    [Tooltip("Number of enemies currently defeated")] [SerializeField] private int[] noOfE;
    [SerializeField] private GameObject[] targetEnemies;

    [Tooltip("objects to take, give, or find")] public string[] oTi;

    [Tooltip("the npc you may have to speak to")] public string nTi;

    public GameObject rewardObj;
    public CollectableSOBase[] questRewards;
    
    private void Start()
    { 
        foreach (var text in titleTexts) text.text = questTitle;
    }

    public enum QuestType
    { 
        DefeatEnemy, HaveInInventory, GiveTo, TakeFrom
    }

    private void Update()
    {
        QuestTrack();
    }

    void UpdateUI()
    {
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
    }
    public void QuestTrack()
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
                    if (eh.isDead) Debug.Log($"the {go.name} is dead!");
                    else alive.Add(go);
                }
                targetEnemies = alive.ToArray();
                var total = eTd != null && eTd.Length > 0 ? eTd[0] : 0; // or track total elsewhere
                var remaining = targetEnemies.Length;
                noOfE[0] = remaining;
                objectiveText.text = $"Defeat {remaining}/{total} enemies";
                if (remaining == 0)
                {
                    targetEnemies = Array.Empty<GameObject>();
                    questCompleted = true;
                }
                break;
            case QuestType.HaveInInventory:
                objectiveText.text = $"Obtain {oTi.Length}";
                break;
        }
        if (questCompleted)
        {
            for (var i = 0; i < questRewards.Length; i++)
            {
                var reward = Instantiate(rewardObj, PlayerHealth.Instance.gameObject.transform.position, Quaternion.identity);
                reward.GetComponent<Collectable>().collectable = questRewards[i];
                ClearUI();
            }
        }
    }
    public void LogQuest(QuestType type, string title, string description, int[] enemyIDs, int[] eToDefeat, string[] objects, string npc, CollectableSOBase[] rewards)
    {
        questType = type;
        questTitle = title;
        questDescription = description;
        nTi = npc;
        
        eTd = enemyIDs != null ? (int[])enemyIDs.Clone() : Array.Empty<int>();
        noOfE = eToDefeat != null ? (int[])eToDefeat.Clone() : Array.Empty<int>();
        oTi = objects != null ? (string[])objects.Clone() : Array.Empty<string>();
        questRewards = rewards != null ? (CollectableSOBase[])rewards.Clone() : Array.Empty<CollectableSOBase>();

        foreach (var text in titleTexts) text.text = questTitle;
        
        switch (questType)
        {
            case QuestType.DefeatEnemy:
                var enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
                var targets = new List<GameObject>(enemies.Length);
                foreach (var eh in enemies) if (Array.IndexOf(eTd, eh.enemyID) >= 0) targets.Add(eh.gameObject);
                targetEnemies = targets.ToArray();
                break;
            case QuestType.HaveInInventory:
                print($"ensure you have {objects[0]} in your inventory!");
                break;
            case QuestType.GiveTo:
                print($"give {objects} to {npc}!");
                break;
            case QuestType.TakeFrom:
                print($"steal {objects} to from {npc}!");
                break;
        }
    }

    private void ClearUI()
    {
        questTitle = "";
        questDescription = "";
        isTracking = false;
        targetEnemies = Array.Empty<GameObject>();
        oTi = Array.Empty<string>();
        nTi = "";
        eTd = Array.Empty<int>();
        noOfE = Array.Empty<int>();
        questRewards = Array.Empty<CollectableSOBase>();
        questCompleted = false;
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) UpdateUI();
    }
}