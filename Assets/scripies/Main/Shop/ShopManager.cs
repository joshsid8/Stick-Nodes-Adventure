using System;
using System.Collections.Generic;
using scripies.Core;
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
    
    public Sprite defaultSprite;

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

    public void LoadShopItems(ShopKeeperItem[] items, ShopKeeper shopKeeper)
    {
        currentShopKeeper = shopKeeper;
        for (var i = 0; i < shopItems.Count; i++)
        {
            if (i < items.Length)
            {
                var collectable = items[i].shopItem?.itemCollectable as CollectableSOBase;
                if (collectable == null)
                {
                    Debug.LogError($"[ShopManager] Somehow the item {items[i].shopItem?.itemName} collectable is null.");
                    continue;
                }
                shopItems[i].shopItem = items[i];
                shopItems[i].collectableSo = collectable;
                shopItems[i].itemName = items[i].shopItem.itemName;
                shopItems[i].itemDescription = !string.IsNullOrEmpty(items[i].overwrittenDescription) ? items[i].overwrittenDescription : 
                    !string.IsNullOrEmpty(items[i].shopItem.itemDescription) ? items[i].shopItem.itemDescription : 
                    !string.IsNullOrEmpty(collectable.itemDescription) ? collectable.itemDescription : "Missing Desc >:(";
                shopItems[i].itemPrice = items[i].price;
                if (items[i].shopItem.itemSprite != null) shopItems[i].itemSprite = items[i].shopItem.itemSprite;
                shopItems[i].itemAmount = items[i].amount;
                shopItems[i].badgeSoRequired = items[i].shopItem.badgeSoRequired;
                shopItems[i].usedSlot = true;
                shopItems[i].gameObject.SetActive(true);
            }
            else
            {
                shopItems[i].ClearSlot();
                shopItems[i].gameObject.SetActive(false);
            }
        }

        foreach (var item in shopItems)
        {
            if (item.usedSlot)
            {
                item.ItemUpdate();
                continue;
            }
            item.ClearSlot();
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
        GameEvents.TriggerPersistGameData();
        if (isActive) ToggleShop(false);
        ClearEverything();
    }

    public void SelectSlot()
    {
        buyButton.onClick.RemoveAllListeners();
        buyButton.interactable = true;
    }
    
    public void DeselectAllSlots()
    {
        foreach (var slot in shopItems)
            slot.Deselect();
    }
    
    public void ClearDisplay(bool withSelectedItem = true)
    {
        selectedPriceText.text = "";
        selectedAmountText.text = "";
        displayImage.sprite = defaultSprite;
        if (!withSelectedItem || currentlySelectedItem == null) return;
        var depletedSlot = currentlySelectedItem;
        depletedSlot.ClearSlot();
        SelectNextAvailableSlot(depletedSlot);
    }

    private void ClearEverything()
    {
        currentlySelectedItem = null;
        foreach (var item in shopItems) item.ClearSlot();
        DeselectAllSlots();
    }
    
    public void CheckStock()
    {
        foreach (var item in shopItems)
        {
            if (item != null && item.usedSlot)
                return;
        }
        CloseShop();
    }

    public void SelectProximalSlot() => SelectNextAvailableSlot(currentlySelectedItem);

    public void SelectNextAvailableSlot(ShopItemSlot referenceSlot)
    {
        var startIndex = referenceSlot != null ? shopItems.IndexOf(referenceSlot) : -1;
        ShopItemSlot nextSlot = null;

        // 1. Look forward from the depleted slot for the next available item
        if (startIndex >= 0)
        {
            for (var i = startIndex + 1; i < shopItems.Count; i++)
            {
                if (shopItems[i] != null && shopItems[i].usedSlot)
                {
                    nextSlot = shopItems[i];
                    break;
                }
            }
        }

        // 2. If none forward, look backward for the closest previous available item
        if (nextSlot == null && startIndex > 0)
        {
            for (var i = startIndex - 1; i >= 0; i--)
            {
                if (shopItems[i] != null && shopItems[i].usedSlot)
                {
                    nextSlot = shopItems[i];
                    break;
                }
            }
        }

        // 3. Fallback: pick any remaining used slot
        if (nextSlot == null)
        {
            foreach (var slot in shopItems)
            {
                if (slot != null && slot.usedSlot)
                {
                    nextSlot = slot;
                    break;
                }
            }
        }

        if (nextSlot != null)
        {
            nextSlot.ItemSelect();
        }
        else
        {
            currentlySelectedItem = null;
            CloseShop();
        }
    }
}

