using System;
using JetBrains.Annotations;
using scripies.Player;
using UnityEngine.UI;
using TMPro;

public class ShopItemSlot : SlotBase
{
	[CanBeNull] public BadgeSO badgeSoRequired;

	public float itemPrice;
    public int itemAmount;
    public CollectableSOBase collectableSo;
    
	public TMP_Text amountText;
	
	public ShopManager shopManager;

	protected void Start()
	{
		base.Awake();
		shopManager = ShopManager.Instance;
	}

    public void ItemUpdate() => UpdateSlotUI();

    private void BuyItem()
    {
	    if (!usedSlot || collectableSo == null) return;
	    var bought = PlayerHealth.Instance.pm.CashFlow(itemPrice, true);
	    if (!bought) return;
	    //print("Bought " + collectableSo.itemName);
	    
	    var itemToGet = ResourceDatabase.Instance.GetItemByName(collectableSo.name);
	    invManager.PresetAddItem(itemToGet);
	    itemAmount--;
	    if (itemAmount <= 0) shopManager.ClearDisplay();
	    else UpdateSlotUI();
    }

    protected override void OnLeftClick()
    {
	    //print("Selected " + (collectableSo != null ? collectableSo.itemName : itemName));
	    if (!usedSlot) return;
	    if (selectedShader != null) selectedShader.SetActive(true);
	    shopManager.currentlySelectedItem = this;
	    shopManager.DeselectAllSlots();
	    shopManager.SelectSlot();
	    UpdateSlotUI();
    }

    private void CheckLiquidity()
    {
		shopManager.buyButton.onClick.RemoveAllListeners();
	    var enoughMoney = PlayerHealth.Instance.pm.Liquidity(itemPrice);
	    if (enoughMoney)
	    {
		    shopManager.buyButton.onClick.AddListener(BuyItem);
		    shopManager.buyButton.interactable = true;
	    }
	    else shopManager.buyButton.interactable = false;
    }

    private void UpdateGlobalDetails()
    {
	    if (shopManager == null || shopManager.currentlySelectedItem != this) return;
	    if (itemDescTitle != null) itemDescTitle.text = itemName;
	    if (itemDescInfo != null) itemDescInfo.text = itemDescription;
	    shopManager.displayImage.sprite = itemSprite ?? (collectableSo != null
		    ? collectableSo.itemSprite : shopManager.defaultSprite);
	    shopManager.selectedAmountText.text = itemAmount.ToString();
	    shopManager.selectedPriceText.text = "N" + itemPrice.ToString("F0");
	    if (badgeSoRequired != null) shopManager.buyButton.interactable = StatsManager.Instance.HasBadge(badgeSoRequired);
    }
    
    protected override void UpdateSlotUI()
    {
	    if (!usedSlot)
	    {
		    if (shopManager.isActive) shopManager.SelectProximalSlot();
		    ClearSlotVisuals();
		    gameObject.SetActive(false);
		    return;
	    }
        if (collectableSo != null && itemSprite == null) itemSprite = collectableSo.itemSprite ?? shopManager.defaultSprite;
        if (itemSprite != null) itemImage.sprite = itemSprite;
	    if (amountText != null) amountText.text = usedSlot ? itemAmount.ToString() : "";
	    if (usedSlot) CheckLiquidity();
        if (shopManager.currentlySelectedItem == this) UpdateGlobalDetails();
    }

    public override void ClearSlot()
    {
	    itemPrice = 0;
	    itemAmount = 0;
	    collectableSo = null;
	    badgeSoRequired = null;
	    itemSprite = shopManager.defaultSprite;
	    base.ClearSlot();
	    UpdateSlotUI();
    }
}
