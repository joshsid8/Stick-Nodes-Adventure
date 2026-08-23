using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopManager : GUIBase
{
    public static ShopManager Instance { get; private set; }

    public GameObject shopMenu;
    
    public ShopKeeper currentShopKeeper;

    public TMP_Text itemPriceText;
    public List<ShopItemSlot> shopItems = new();
    
    public Button buyButton;

    protected override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        base.Awake();
    }

    public void ToggleShop()
    {
        if (shopMenu == null) return;
        isActive = !isActive;
        rt.anchoredPosition = new Vector2(0, isActive ? 0 : -1000);
        GetActive(isActive);
    }

    public void LoadShopItems(ShopItem[] items, ShopKeeper shopKeeper)
    {
        currentShopKeeper = shopKeeper;
        for (var i = 0; i < items.Length; i++)
        {
            shopItems[i].itemName = items[i].itemName;
            shopItems[i].itemDescription = items[i].itemDescription;
            shopItems[i].itemPrice = items[i].itemPrice;
            shopItems[i].itemSprite = items[i].itemSprite;
            shopItems[i].collectableSo = items[i].itemCollectable;
        }
    }

    public void CloseShop()
    {
        currentShopKeeper.SaveShopItems(shopItems.ToArray());
        isActive = false;
        GetActive(isActive);
    }

    public void UpdateShopUI()
    {
        
    }

    public void SelectSlot()
    {
        buyButton.onClick.RemoveAllListeners();
        buyButton.interactable = true;
    }
    
    public void DeselectAllSlots()
    {
        foreach (var slot in shopItems) slot.Deselect();
    }
}

