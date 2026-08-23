using System;
using System.Collections.Generic;
using UnityEngine;

public class ShopKeeper : MonoBehaviour
{
    public List<ShopItem> shopItems = new();
    
    private void LoadShopItems()
    {
        ShopManager.Instance.LoadShopItems(shopItems.ToArray(), this);
    }

    public void SaveShopItems(ShopItemSlot[] items)
    {
        shopItems.Clear();
        for (var i = 0; i < items.Length; i++)
        {
            shopItems.Add(new ShopItem());
            shopItems[i].itemName = items[i].itemName;
            shopItems[i].itemDescription = items[i].itemDescription;
            shopItems[i].itemSprite = items[i].itemSprite;
            shopItems[i].itemCollectable = items[i].collectableSo;
        }
    }
}

[Serializable]
public class ShopItem
{
    public string itemName;
    public string itemDescription;
    public Sprite itemSprite;
    public float itemPrice;
    public CollectableSOBase itemCollectable;
}

