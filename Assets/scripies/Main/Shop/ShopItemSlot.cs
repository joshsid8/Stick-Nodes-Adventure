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

	protected override void Awake()
	{
		base.Awake();
		shopManager = ShopManager.Instance;
	}

    public void BuyItem()
    {
	    var bought = PlayerHealth.Instance.pm.CashFlow(itemPrice, true);
	    if (!bought) return;
	    print("Bought " + collectableSo.itemName);
	    
	    var itemToGet = ItemDatabase.Instance.GetItemByName(collectableSo.itemName);
	    invManager.PresetAddItem(itemToGet);
	    itemAmount--;
	    if (itemAmount <= 0)
	    {
		    amountText.text = "";
		    ClearSlot();
	    }
	    else UpdateSlotUI();
    }

    protected override void OnLeftClick()
    {
	    //if (!isBuyable) return;
	    if (selectedShader != null) selectedShader.SetActive(true);
	    print("Selected " + collectableSo.itemName);
	    shopManager.DeselectAllSlots();
	    shopManager.SelectSlot();
	    shopManager.buyButton.onClick.AddListener(BuyItem);
	    UpdateSlotUI();
    }
    protected override void UpdateSlotUI()
    {
        itemDescTitle.text = itemName ?? collectableSo.itemName;
        itemDescInfo.text = itemDescription ?? collectableSo.itemDescription;
        if (amountText != null) amountText.text = itemAmount.ToString();
        if (itemImage != null) itemImage.sprite = itemSprite ?? collectableSo.itemSprite;
        if (priceText != null) priceText.text = "N" + itemPrice.ToString("F0");
	    if (shopManager != null) shopManager.displayImage.sprite = itemSprite ?? collectableSo.itemSprite;
    }
}
