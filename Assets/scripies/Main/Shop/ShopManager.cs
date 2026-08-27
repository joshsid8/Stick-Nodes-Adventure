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

    public TMP_Text selectedPriceText;
    public TMP_Text selectedAmountText;
    public List<ShopItemSlot> shopItems = new();
    
    public ShopItemSlot currentlySelectedItem;
    
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
        if (!isActive)
        {
            CloseShop();
            return;
        }
        CloseCurrentMenus();
    }

    public void LoadShopItems(ShopItem[] items, ShopKeeper shopKeeper)
    {
        currentShopKeeper = shopKeeper;
        for (var i = 0; i < shopItems.Count; i++)
        {
            if (i < items.Length)
            {
                shopItems[i].itemName = items[i].itemName;
                shopItems[i].itemDescription = items[i].itemDescription;
                shopItems[i].itemPrice = items[i].itemPrice;
                shopItems[i].itemSprite = items[i].itemSprite ?? items[i].itemCollectable.itemSprite;
                shopItems[i].collectableSo = items[i].itemCollectable;
                shopItems[i].itemAmount = items[i].itemAmount;
                shopItems[i].badgeRequired = items[i].badgeRequired;
                shopItems[i].usedSlot = true;
                shopItems[i].ItemUpdate();
                shopItems[i].gameObject.SetActive(true);
            }
            else
            {
                shopItems[i].ClearSlot();
                shopItems[i].gameObject.SetActive(false);
            }
        }
        if (items.Length > 0) shopItems[0].ItemSelect();
        
        ToggleShop(true);
    }

    private void CloseShop()
    {
        if (currentShopKeeper != null) 
            currentShopKeeper.SaveShopItems(shopItems.ToArray());
        buyButton.onClick.RemoveAllListeners();
        currentShopKeeper = null;
        if (isActive) ToggleShop(false);
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
    
    public void ClearDisplay()
    {
        selectedPriceText.text = "";
        selectedAmountText.text = "";
        displayImage.sprite = null;
    }
    
    public bool CheckStock()
    {
        foreach (var item in shopItems)
        {
            if (item.usedSlot) 
                return true;
            if (item == shopItems[^1] && !item.usedSlot)
            {
                CloseShop();
                return false;
            }
        }
        return true;
    }
}

