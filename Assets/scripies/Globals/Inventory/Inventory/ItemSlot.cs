using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ItemSlot : MonoBehaviour, IPointerClickHandler
{
    public string itemName;
    public int itemAmount;
    public int maxItemAmount;
    public Sprite itemSprite;
    
    public string itemDescription;
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;

    public string itemType;
    public float restoreValue;
    
    public bool usedSlot;
    
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image itemImage;
    
    public GameObject selectedShader;
    public bool itemSelected;
    
    [SerializeField] private InvManager invManager;

    private void Start()
    {
        if (invManager == null)
        {
            Debug.LogError("ItemSlot: where is my dad?! (invmanager)", this);
            invManager = FindAnyObjectByType<InvManager>();
            if (invManager == null) Debug.LogError("ItemSlot: HE WENT FOR THE MIIILLKKK!!!", this);
        }
    }

    private void Update()
    {
        UpdateSlotUI();
    }
    
    public void AddItem(string item, int amount, Sprite sprite, string itemDesc, int maxAmount, string type, float value)
    {
        itemName = item;
        maxItemAmount = maxAmount;
        itemSprite = sprite;
        itemDescription = itemDesc;
        itemType = type;
        restoreValue = value;
        
        usedSlot = true;
        
        int potentialNewAmount = itemAmount + amount;

        if (potentialNewAmount > maxItemAmount)
        {
            itemAmount = maxItemAmount;
            int remainingAmount = potentialNewAmount - maxItemAmount;
            
            if (invManager != null)
            {
                invManager.AddItem(item, remainingAmount, sprite, itemDesc, maxAmount, itemType, restoreValue);
            }
            else
            {
                Debug.LogError("But where did my dad go (invmanager)?", this);
            }
        }
        else
        {
            itemAmount = potentialNewAmount;
        }
    }
    
    private void UpdateSlotUI()
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
            usedSlot = false;
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
        usedSlot = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnLeftClick();
        }
    }

    public void OnLeftClick()
    {
        invManager.DeselectSlots();
        selectedShader.SetActive(true);
        itemSelected = true;

        itemDescTitle.text = itemName;
        itemDescInfo.text = itemDescription;
        invManager.SelectSlot();
        invManager.useButton.onClick.AddListener(() => Restore());
        invManager.destroyButton.onClick.AddListener(() => ClearSlot());
        
    }

    public void Restore()
    {
        if (itemType != null)
        {
            invManager.playerManager.SetGlobalFloat(itemType, restoreValue);
            itemAmount--;
            if (itemAmount <= 0) ClearSlot();
        }
    }
}
