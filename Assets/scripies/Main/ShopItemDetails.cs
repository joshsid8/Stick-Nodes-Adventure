using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopItemDetails : MonoBehaviour
{
    public string itemName;
    public string itemDescription;
    public int itemAmount;
    public float itemPrice;
    public Sprite itemSprite;
    public CollectableSOBase itemSo;
	
	public TMP_Text titleText;
	public TMP_Text descText;
	public TMP_Text amountText;
	public Image spriteImage;

    public void BuyItem()
    {
        
    }
}
