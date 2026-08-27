using JetBrains.Annotations;
using scripies.Player;
using UnityEngine.UI;
using TMPro;

public class ShopItemSlot : SlotBase
{
	[CanBeNull] public Badge badgeRequired;

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
	    
	    var itemToGet = ResourceDatabase.Instance.GetItemByName(collectableSo.itemName);
	    invManager.PresetAddItem(itemToGet);
	    itemAmount--;
	    if (itemAmount <= 0)
	    {
		    ClearSlot();
		    shopManager.ClearDisplay();
	    }
	    else
	    {
		    UpdateGlobalDetails();
		    UpdateSlotUI();
	    }
    }

    protected override void OnLeftClick()
    {
	    //print("Selected " + (collectableSo != null ? collectableSo.itemName : itemName));
	    if (!usedSlot) return;
	    if (selectedShader != null) selectedShader.SetActive(true);
	    shopManager.DeselectAllSlots();
	    shopManager.SelectSlot();
	    UpdateGlobalDetails();
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
	    if (shopManager == null) return;
	    shopManager.selectedAmountText.text = itemAmount.ToString();
	    shopManager.selectedPriceText.text = "N" + itemPrice.ToString("F0");
	    shopManager.currentlySelectedItem = this;
	    if (badgeRequired != null) shopManager.buyButton.interactable = StatsManager.Instance.HasBadge(badgeRequired);
    }
    
    protected override void UpdateSlotUI()
    {
        base.UpdateSlotUI();
	    if (usedSlot && shopManager != null && shopManager.displayImage != null) 
		    shopManager.displayImage.sprite = itemSprite ?? (collectableSo != null ? collectableSo.itemSprite : null);
	    if (amountText != null) amountText.text = usedSlot ? itemAmount.ToString() : "";
	    CheckLiquidity();
        UpdateGlobalDetails();
    }

    public override void ClearSlot()
    {
	    base.ClearSlot();
	    itemPrice = 0;
	    itemAmount = 0;
	    collectableSo = null;
	    UpdateSlotUI();
    }
}
