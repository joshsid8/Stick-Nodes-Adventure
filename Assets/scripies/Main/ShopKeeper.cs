using System;
using System.Collections.Generic;
using UnityEngine;

public class ShopKeeper : MonoBehaviour
{
    public List<ShopItemDetails> shopItems = new();
    
    private void LoadShopItems()
    {
        ShopManager.Instance.LoadShopItems(shopItems.ToArray(), this);
    }

    public void SaveShopItems(ManagerShopItemDetails[] items)
    {
        shopItems.Clear();
        foreach (var item in items)
        {
            shopItems.Add(new ShopItemDetails()
            {
                itemName = item.itemName,
                itemDescription = item.itemDescription,
                itemPrice = item.itemPrice,
                itemSprite = item.itemSprite,
                itemSo = item.itemSo
            });
        }
    }
}

