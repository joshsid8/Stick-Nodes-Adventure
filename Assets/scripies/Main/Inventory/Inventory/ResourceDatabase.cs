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
    private readonly Dictionary<string, ShopItem> shopItemDatabase = new();
    private readonly Dictionary<string, BadgeSO> badgeDatabase = new();
    private readonly Dictionary<string, TeleDoor> teleDoorDatabase = new();

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
            if (!itemDatabase.TryAdd(item.name, item))
                Debug.LogWarning($"[ItemDatabase] Duplicate item name found: {item.name}.");
        }
        
        var allEnemies = Resources.LoadAll<Enemy>("Enemy");
        foreach (var enemy in allEnemies)
        {
            if (!enemyDatabase.TryAdd(enemy.name, enemy))
                Debug.LogWarning($"[EnemyDatabase] Duplicate enemy name found: {enemy.name}.");
        }
        
        var allNpcs = Resources.LoadAll<NPC>("NPC");
        foreach (var npc in allNpcs)
        {
            if (!npcDatabase.TryAdd(npc.name, npc))
                Debug.LogWarning($"[NPCDatabase] Duplicate npc name found: {npc.name}.");
        }
        
        var allShopItems = Resources.LoadAll<ShopItem>("Shop Items");
        foreach (var shopItem in allShopItems)
        {
            if (!shopItemDatabase.TryAdd(shopItem.name, shopItem))
                Debug.LogWarning($"[ShopItemDatabase] Duplicate shop item name found: {shopItem.name}.");
        }
        
        var allQuests = Resources.LoadAll<QuestSO>("Quests");
        foreach (var quest in allQuests)
        {
            if (!questDatabase.TryAdd(quest.questName, quest))
                Debug.LogWarning($"[QuestDatabase] Duplicate quest name found: {quest.questName}.");
        }
        
        var allBadges = Resources.LoadAll<BadgeSO>("Badges");
        foreach (var badge in allBadges)
        {
            if (!badgeDatabase.TryAdd(badge.name, badge))
                Debug.LogWarning($"[BadgeDatabase] Duplicate badge name found: {badge.name}.");
        }
        
        var allTeleDoors = Resources.LoadAll<TeleDoor>("TeleDoors");
        foreach (var door in allTeleDoors)
        {
            if (!teleDoorDatabase.TryAdd(door.name, door))
                Debug.LogWarning($"[TeleDoorDatabase] Duplicate TeleDoor name found: {door.name}.");
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
    
    // Shop Items
    public ShopItem GetShopItemByName(string shopItemName)
    {
        if (string.IsNullOrEmpty(shopItemName))
        {
            print($"[ShopItemDatabase] Shop item name is null trying to find {shopItemName}");
            return null;
        }
        shopItemDatabase.TryGetValue(shopItemName, out var shopItem);
        return shopItem;
    }
    
    // Badges
    public BadgeSO GetBadgeByName(string badgeName)
    {
        if (string.IsNullOrEmpty(badgeName)) return null;
        badgeDatabase.TryGetValue(badgeName, out var badge);
        return badge;
    }

    // TeleDoors
    public TeleDoor GetTeleDoorByName(string doorName)
    {
        if (string.IsNullOrEmpty(doorName)) return null;
        teleDoorDatabase.TryGetValue(doorName, out var door);
        return door;
    }
}