using scripies.Player;
using UnityEngine.UI;
using TMPro;

public class ShopItemSlot : SlotBase
{
	public bool isBuyable;

	public float itemPrice;
    public int itemAmount;
    public CollectableSOBase collectableSo;
    
	public TMP_Text amountText;
	public TMP_Text priceText;
	
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
	    
	    var itemToGet = ItemDatabase.Instance.GetItemByName(collectableSo.itemName);
	    invManager.PresetAddItem(itemToGet);
	    itemAmount--;
	    if (itemAmount <= 0)
	    {
		    ClearSlot();
		    shopManager.ClearDisplay();
	    }
	    else UpdateSlotUI();
    }

    protected override void OnLeftClick()
    {
	    //print("Selected " + (collectableSo != null ? collectableSo.itemName : itemName));
	    if (!usedSlot) return;
	    //if (!isBuyable) return;
	    if (selectedShader != null) selectedShader.SetActive(true);
	    shopManager.DeselectAllSlots();
	    shopManager.SelectSlot();
	    shopManager.buyButton.onClick.AddListener(BuyItem);
	    shopManager.selectedAmountText.text = itemAmount.ToString();
	    shopManager.selectedPriceText.text = "N" + itemPrice.ToString("F0");
	    UpdateSlotUI();
    }
    protected override void UpdateSlotUI()
    {
        base.UpdateSlotUI();
        if (amountText != null) amountText.text = usedSlot ? itemAmount.ToString() : "";
        if (priceText != null) priceText.text = usedSlot ? "N" + itemPrice.ToString("F0") : "";
	    if (usedSlot && shopManager != null && shopManager.displayImage != null) 
		    shopManager.displayImage.sprite = itemSprite ?? (collectableSo != null ? collectableSo.itemSprite : null);
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
