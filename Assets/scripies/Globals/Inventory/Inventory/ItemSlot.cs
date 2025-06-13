using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemSlot : MonoBehaviour
{
    public string itemName;
    public int itemAmount;
    public Sprite itemSprite;

    public bool isFull;
    
    // UI References - THESE MUST BE ASSIGNED IN THE INSPECTOR FOR EACH ItemSlot GameObject!
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image itemImage;

    // Optional: Max stack size for this slot (if you implement stacking)
    // public int maxStackSize = 99;

    public void AddItem(string item, int amount, Sprite sprite)
    {
        itemName = item;
        itemAmount += amount;
        itemSprite = sprite;
        isFull = true;
        
        UpdateSlotUI();
    }
    
    public void UpdateSlotUI()
    {
        if (itemAmount > 0) 
        {
            if (amountText != null)
            {
                amountText.enabled = true;
                amountText.text = itemAmount.ToString();
            }
            if (itemImage != null)
            {
                itemImage.enabled = true;
                itemImage.sprite = itemSprite;
            }
        }
        else
        {
            isFull = false;
            itemName = string.Empty;
            itemSprite = null;
            if (amountText != null) amountText.enabled = false;
            if (itemImage != null) itemImage.enabled = false;
        }
    }

    public void ClearSlot()
    {
        itemName = string.Empty;
        itemAmount = 0;
        itemSprite = null;
        isFull = false;
        UpdateSlotUI(); 
    }
    
    void Start()
    {
        UpdateSlotUI();
    }
}
