using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A persistent Singleton that loads all CollectableSOBase assets from Resources
/// into a dictionary for fast lookup by name.
/// </summary>
public class ItemDatabase : MonoBehaviour
{
    public static ItemDatabase Instance { get; private set; }
    
    private Dictionary<string, CollectableSOBase> itemDatabase = new Dictionary<string, CollectableSOBase>();

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
        
        //Debug.Log($"[ItemDatabase] Database built with {itemDatabase.Count} items.");
    }
    
    public CollectableSOBase GetItemByName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
        {
            Debug.LogError("[ItemDatabase] ain nun here dawg");
            return null;
        }
        itemDatabase.TryGetValue(itemName, out var item);
        if (item == null) Debug.LogError($"[ItemDatabase] We couldnt find {itemName} in our resources D:");
        return item;
    }
}