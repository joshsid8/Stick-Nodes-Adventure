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
	
	public ShopManager shopManager;

	protected override void Awake()
	{
		shopManager = ShopManager.Instance;
	}

    public void BuyItem()
    {
        if (itemPrice > PlayerHealth.Instance.NodeBucks)
        {
	        print("Not enough money!!!!!!!!");
	        return;
        }
        print("Buying item");
    }

    protected override void OnLeftClick()
    {
	    if (!isBuyable) return;
	    shopManager.SelectSlot();
	    UpdateSlotUI();
    }
    protected override void UpdateSlotUI()
    {
        itemDescTitle.text ??= collectableSo.itemName;
        itemDescInfo.text ??= collectableSo.itemDescription;
        if (amountText != null) amountText.text = itemAmount.ToString();
        if (itemImage != null) itemImage.sprite = collectableSo.itemSprite;
    }
}
