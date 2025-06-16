using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System; // For String.Empty

public class ItemSlot : MonoBehaviour, IPointerClickHandler
{
    // Item Data
    public string itemName;
    public int itemAmount;
    public int maxItemAmount;
    public Sprite itemSprite;
    public string itemDescription;
    public string itemType;
    public float restoreValue;

    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText;

    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image itemImage;
    
    public GameObject selectedShader;
    public bool itemSelected;
    public bool usedSlot;
    
    [SerializeField] private InvManager invManager;
    
    public AspectSO aspectStats; 
    [SerializeField] AspectSlot[] aspectSlot;
    [SerializeField] private bool aspectSlotFull;

    private void Awake()
    {
        
        if (invManager == null)
        {
            invManager = FindAnyObjectByType<InvManager>();
            if (invManager == null) Debug.LogError("ItemSlot: InvManager not found on Awake!", this);
        }
        
        UpdateSlotUI();
    }
    
    public void AddItem(string item, int amount, Sprite sprite, string itemDesc, int maxAmount, string type, float value, AspectSO aspect)
    {
        itemName = item;
        maxItemAmount = maxAmount;
        itemSprite = sprite;
        itemDescription = itemDesc;
        itemType = type;
        restoreValue = value;
        
        if (itemType == "Aspect") aspectStats = aspect;
        itemAmount += amount;

        usedSlot = true;
        UpdateSlotUI();
    }
    
    private void UpdateSlotUI()
    {
        if (itemAmount > 0 && usedSlot)
        {
            if (itemImage != null)
            {
                itemImage.enabled = true;
                itemImage.sprite = itemSprite;
            }
            
            if (amountText != null)
            {
                amountText.enabled = true;
                amountText.text = (itemAmount == 1 && itemType != "Aspect") ? "" : itemAmount.ToString();
                if (itemType == "Aspect") amountText.text = "";
            }
        }
        else
        {
            ClearSlotVisuals();
        }
    }

    
    public void ClearSlot()
    {
        itemName = string.Empty;
        itemDescription = string.Empty;
        itemAmount = 0;
        maxItemAmount = 0;
        itemSprite = null;
        itemType = string.Empty;
        restoreValue = 0f;
        aspectStats = null;

        usedSlot = false;
        itemSelected = false;

        ClearSlotVisuals();
    }

    
    private void ClearSlotVisuals()
    {
        if (amountText != null) amountText.enabled = false;
        if (itemImage != null)
        {
            itemImage.enabled = false;
            itemImage.sprite = null;
        }
        if (selectedShader != null) selectedShader.SetActive(false);
        if (itemDescTitle != null) itemDescTitle.text = string.Empty;
        if (itemDescInfo != null) itemDescInfo.text = string.Empty;
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
        if (!usedSlot) return;

        invManager.DeselectAllSlots();
        
        if (selectedShader != null) selectedShader.SetActive(true);
        itemSelected = true;

        
        if (itemDescTitle != null) itemDescTitle.text = itemName;
        if (itemDescInfo != null) itemDescInfo.text = itemDescription;

        invManager.SelectSlot();

        
        if (useButtonText != null)
        {
            useButtonText.text = (itemType == "Aspect") ? "Equip" : "Use";
        }
        
        
        invManager.useButton.onClick.RemoveAllListeners();
        invManager.destroyButton.onClick.RemoveAllListeners();

        
        invManager.useButton.onClick.AddListener(() => ItemTypeAction());
        invManager.destroyButton.onClick.AddListener(() => ClearSlot());
    }

    
    private void ItemTypeAction()
    {
        if (itemType != null && itemType == "Aspect")
        {
            EquipAspect();
        }
        else
        {
            RestoreItem();
        }

        invManager.DeselectAllSlots();
    }

    private void RestoreItem()
    {
        if (invManager.playerManager != null)
        {
            invManager.playerManager.SetGlobalFloat(itemType, restoreValue);
        }
        else
        {
            Debug.LogError("PlayerManager not found in InvManager! Cannot restore item stat.");
        }
        
        itemAmount--;
        if (itemAmount <= 0)
        {
            ClearSlot();
        }
        else
        {
            UpdateSlotUI();
        }
    }

    private void EquipAspect()
    {
        bool equipped = false;
        if (aspectSlot != null)
        {
            for (int i = 0; i < aspectSlot.Length; i++)
            {
                if (aspectSlot[i] != null && !aspectSlot[i].usedSlot)
                {
                    aspectSlot[i].AddAspect(itemName, itemSprite, itemDescription, aspectStats);
                    
                    if (aspectStats != null && invManager.playerManager != null)
                    {
                        for (int j = 0; j < aspectStats.floatStatsName.Length; j++)
                        {
                            invManager.playerManager.SetGlobalFloat(aspectStats.floatStatsName[j], aspectStats.floatStats[j]);
                        }
                        
                    }
                    else if (aspectStats == null)
                    {
                         Debug.LogWarning("AspectSO not assigned to ItemSlot for equipping!");
                    }
                    else if (invManager.playerManager == null)
                    {
                         Debug.LogError("PlayerManager not found in InvManager. Cannot apply aspect stats.");
                    }

                    ClearSlot();
                    equipped = true;
                    break;
                }
            }
        }
        if (!equipped)
        {
            Debug.LogWarning("All Aspect Equipped slots are full! Cannot equip " + itemName);
        }
    }
    
    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
        itemSelected = false;
    }
}
