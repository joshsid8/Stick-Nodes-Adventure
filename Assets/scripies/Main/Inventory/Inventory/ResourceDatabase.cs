using System;
using System.Collections.Generic;
using scripies.Enemy;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// A persistent Singleton that loads all CollectableSOBase assets from Resources
/// into a dictionary for fast lookup by name.
/// </summary>
public class ResourceDatabase : MonoBehaviour
{
    public static ResourceDatabase Instance { get; private set; }
    
    private readonly Dictionary<string, CollectableSOBase> itemDatabase = new();
    private readonly Dictionary<string, Enemy> enemyDatabase = new();
    private readonly Dictionary<string, QuestSO> questDatabase = new();
    private readonly Dictionary<string, NPC> npcDatabase = new();
    private readonly Dictionary<string, BadgeSO> badgeDatabase = new();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildDatabase();
    }

    private void BuildDatabase()
    {

        var allItems = Resources.LoadAll<CollectableSOBase>("Collectables");
        foreach (var item in allItems)
        {
            if (!itemDatabase.ContainsKey(item.name)) itemDatabase.Add(item.name, item); 
            else Debug.LogWarning($"[ItemDatabase] Duplicate item name found: {item.name}."); 
        }
        
        var allEnemies = Resources.LoadAll<Enemy>("Enemy");
        foreach (var enemy in allEnemies)
        {
            if (!enemyDatabase.ContainsKey(enemy.name)) enemyDatabase.Add(enemy.name, enemy);
            else Debug.LogWarning($"[EnemyDatabase] Duplicate enemy name found: {enemy.name}.");
        }
        
        var allNpcs = Resources.LoadAll<NPC>("NPC");
        foreach (var npc in allNpcs)
        {
            if (!npcDatabase.ContainsKey(npc.name)) npcDatabase.Add(npc.name, npc);
            else Debug.LogWarning($"[NPCDatabase] Duplicate npc name found: {npc.name}.");
        }
        
        var allQuests = Resources.LoadAll<QuestSO>("Quests");
        foreach (var quest in allQuests)
        {
            if (!questDatabase.ContainsKey(quest.questName)) questDatabase.Add(quest.questName, quest);
            else Debug.LogWarning($"[QuestDatabase] Duplicate quest name found: {quest.questName}.");
        }
        
        var allBadges = Resources.LoadAll<BadgeSO>("Badges");
        foreach (var badge in allBadges)
        {
            if (!badgeDatabase.ContainsKey(badge.name)) badgeDatabase.Add(badge.name, badge);
            else Debug.LogWarning($"[BadgeDatabase] Duplicate badge name found: {badge.name}.");
        }
    }
    
    // Items
    public CollectableSOBase GetRandomItem(List<CollectableSOBase> items)
    {
        return items[Random.Range(0, items.Count)];
    }
    
    public CollectableSOBase GetItemByName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
        {
            Debug.LogError("[ItemDatabase] ain nun here dawg");
            return null;
        }
        itemDatabase.TryGetValue(itemName, out var item);
        if (item == null) throw new MissingFieldException($"[ItemDatabase] We couldnt find {itemName} in our resources D:");
        return item;
    }
    
    // Enemies
    public Enemy GetEnemyByName(string enemyName)
    {
        if (string.IsNullOrEmpty(enemyName)) return null;
        enemyDatabase.TryGetValue(enemyName, out var enemy);
        return enemy;
    }
    
    // Quests
    public QuestSO GetQuestByName(string questName)
    {
        if (string.IsNullOrEmpty(questName)) return null;
        questDatabase.TryGetValue(questName, out var quest);
        return quest;
    }
    
    // NPCs
    public NPC GetNpcByName(string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return null;
        npcDatabase.TryGetValue(npcName, out var npc);
        return npc;
    }

    public BadgeSO GetBadgeByName(string badgeName)
    {
        if (string.IsNullOrEmpty(badgeName)) return null;
        badgeDatabase.TryGetValue(badgeName, out var badge);
        return badge;
    }
}