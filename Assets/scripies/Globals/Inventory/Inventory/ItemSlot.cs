using System;
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
    public TMP_Text useButtonText;
    public bool usedSlot;
    
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image itemImage;
    
    public GameObject selectedShader;
    public bool itemSelected;
    
    [SerializeField] private InvManager invManager;
    public AspectSO aspectStats;
    [SerializeField] AspectSlot[] aspectSlot;
    [SerializeField] private bool aspectSlotFull;
    
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
    public void AddAspect(string item, Sprite sprite, string itemDesc,AspectSO aspect)
    {
        itemName = item;
        itemSprite = sprite;
        itemDescription = itemDesc;
        usedSlot = true;
        itemAmount = 1;
        aspectStats = aspect;
        itemType = "Aspect";
    }
    
    private void UpdateSlotUI()
    {
        if (itemAmount > 0) 
        {
            if (amountText != null)
            {
                amountText.enabled = true;
                amountText.text = itemAmount.ToString();
                if (itemAmount == 1) amountText.text = "";
            }
            else if (itemImage != null)
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
        itemDescription = string.Empty;
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

    private void OnLeftClick()
    {
        invManager.DeselectSlots();
        selectedShader.SetActive(true);
        itemSelected = true;

        itemDescTitle.text = itemName;
        itemDescInfo.text = itemDescription;
        invManager.SelectSlot();
        if (itemType == "Aspect") useButtonText.text = "Equip"; else useButtonText.text = "Use";
        invManager.useButton.onClick.AddListener(() => ItemType());
        invManager.destroyButton.onClick.AddListener(() => ClearSlot());
        
    }

    private void ItemType()
    {
        if (itemType != null && itemType != "Aspect") Restore();
        else Aspect();
    }

    private void Restore()
    {
        invManager.playerManager.SetGlobalFloat(itemType, restoreValue);
        itemAmount--;
        if (itemAmount <= 0)
        {
            itemDescription = string.Empty;
            itemName = string.Empty;
            ClearSlot();
        }

    }

    private void Aspect()
    {
        for (int i = 0; i < aspectSlot.Length; i++)
        {
            if (aspectSlot[i] != null && !aspectSlot[i].usedSlot)
            {
                aspectSlot[i].AddAspect(itemName, itemSprite, itemDescription, aspectStats);
            }
        }
    }
}
