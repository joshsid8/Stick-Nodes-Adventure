using System;
using System.Collections.Generic;
using scripies.Core;
using scripies.Dialogue;
using UnityEngine;

public class ShopKeeper : MonoBehaviour
{
    public List<ShopItem> shopItems = new();
    
    [SerializeField] private DialogueObject inStockDialogue;
    [SerializeField] private DialogueObject soldOutDialogue;
    
    public bool canRestock = true;
    private readonly List<ShopItem> backroomItems = new();

    private void Start()
    {
        backroomItems.AddRange(shopItems);
    }
    
    /// <summary>
    /// me when "Can you check the back?"
    /// </summary>
    public void StartShop()
    {
        if (shopItems.Count == 0)
        {
            GameEvents.TriggerShowDialogue(soldOutDialogue, GetComponent<NPChat>().npc);
            if (canRestock && !IsInvoking(nameof(Restock))) Invoke(nameof(Restock), 3);
            return;
        }
        ShopManager.Instance.LoadShopItems(shopItems.ToArray(), this);
    }

    public void SaveShopItems(ShopItemSlot[] items)
    {
        shopItems.Clear();
        foreach (var item in items)
        {
            if (!item.usedSlot) continue;
            shopItems.Add(item.shopItem);
        }
    }

    public void AddShopItems(ShopItem[] item, GameObject keeper)
    {
        if (keeper != gameObject || item.Length == 0) return;
        backroomItems.Clear();
        shopItems.Clear();
        backroomItems.AddRange(item);
        shopItems.AddRange(item);
        print($"Added {item.Length} items to shopkeeper {gameObject.name}");
        GameEvents.TriggerPersistGameData();
    }
    
    /// <summary>
    /// me when "FIIINNNNEEEE ILL GO THEN >:("
    /// </summary>
    private void Restock()
    {
        shopItems.AddRange(backroomItems);
        GameEvents.TriggerAddDialogue(inStockDialogue, gameObject);
    }
}
