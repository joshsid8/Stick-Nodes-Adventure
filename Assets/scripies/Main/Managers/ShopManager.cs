using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class ShopManager : GUIBase, IPointerClickHandler
{
    public static ShopManager Instance { get; private set; }

    public GameObject shopMenu;
    
    public ShopKeeper currentShopKeeper;
    
    public TMP_Text itemTitleText;
    public TMP_Text itemDescText;
    public TMP_Text itemPriceText;
    public List<ManagerShopItemDetails> shopItems = new();

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

    public void LoadShopItems(ShopItemDetails[] items, ShopKeeper shopKeeper)
    {
        shopItems.Clear();
        currentShopKeeper = shopKeeper;
        foreach (var item in items)
        {
            shopItems.Add(new ManagerShopItemDetails
            {
                itemName = item.itemName,
                itemDescription = item.itemDescription,
                itemPrice = item.itemPrice,
                itemSprite = item.itemSprite,
                itemSo = item.itemSo
            });
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

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftCLick();
    }

    private void OnLeftCLick()
    {
        
    }
}

[Serializable]
public class ManagerShopItemDetails
{
    public string itemName;
    public string itemDescription;
    public int itemAmount;
    public float itemPrice;
    public Sprite itemSprite;
    public CollectableSOBase itemSo;
}