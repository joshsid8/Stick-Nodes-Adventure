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
    public Image displayImage;

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

    public void ToggleShop(bool toggle)
    {
        if (shopMenu == null) return;
        isActive = toggle;
        rt.anchoredPosition = new Vector2(isActive ? 0 : 2000, 0);
        GetActive(isActive);
        if (!isActive) return;
        //closeShader.onClick.RemoveAllListeners(); 
        closeShader.onClick.AddListener(CloseShop);
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
            shopItems[i].itemAmount = items[i].itemAmount;
            shopItems[i].usedSlot = true;
            print($"Added {items[i].itemName} to shop.");
        }
        shopItems[0].ItemSelect();
        ToggleShop(true);
    }

    public void CloseShop()
    {
        currentShopKeeper.SaveShopItems(shopItems.ToArray());
        buyButton.onClick.RemoveAllListeners();
        shopItems.Clear();
        currentShopKeeper = null;
        ToggleShop(false);
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

