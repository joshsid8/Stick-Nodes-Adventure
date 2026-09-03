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
        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].usedSlot) continue;
            
            shopItems.Add(new ShopItem
            {
                itemName = items[i].itemName,
                itemDescription = items[i].itemDescription,
                itemSprite = items[i].itemSprite ?? items[i].collectableSo.itemSprite,
                itemCollectable = items[i].collectableSo,
                itemPrice = items[i].itemPrice,
                itemAmount = items[i].itemAmount,
                badgeRequired = items[i].badgeRequired
            });
        }
    }

    public void AddShopItem(ShopItem item,bool inNextRestock = false, GameObject keeper = null)
    {
        if (keeper != gameObject) return;
        if (inNextRestock) backroomItems.Add(item);
        else shopItems.Add(item);
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

[Serializable]
public class ShopItem
{
    public string itemName;
    [TextArea(1, 2)] public string itemDescription;
    public Sprite itemSprite;
    public float itemPrice;
    public CollectableSOBase itemCollectable;
    public int itemAmount = 1;
    public Badge badgeRequired;
}

