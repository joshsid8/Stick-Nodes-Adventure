using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

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
    public bool usedSlot;
    
    [SerializeField] private InvManager invManager;
    private InvIndex index;
    public Prompt choice;
    
    public AspectSO aspectStats;
    [SerializeField] private bool aspectSlotFull;
    [SerializeField] private AspectSlot[] equippedAspectSlots; // Reference to the equipped AspectSlots in the UI
    
    public AbilitySO abilityStats;
    [SerializeField] private int abilitySlotType;

    private CollectableSOBase itemScriptableObject;
    
    private bool switchFrom;

    private void Awake()
    {
        if (invManager == null) Debug.LogError("ItemSlot: InvManager not found on Awake!", this);
        if (equippedAspectSlots == null || equippedAspectSlots.Length == 0)
            Debug.LogWarning($"ItemSlot {gameObject.name}: No equippedAspectSlots assigned. Aspects cannot be equipped from this slot.");
    }

    public void AddItem(string item, int amount, Sprite sprite, string itemDesc, int maxAmount, string type, float value, ItemSO itemSo, AspectSO aspect, AbilitySO ability, int abilityType)
    {
        itemName = item;
        maxItemAmount = maxAmount;
        itemSprite = sprite;
        itemDescription = itemDesc;
        itemType = type;
        restoreValue = value;
        abilitySlotType = abilityType;
        itemAmount += amount;
        if (itemType == "Aspect") aspectStats = aspect;
        else if (itemType == "Ability") abilityStats = ability;
        if (itemSo != null) itemScriptableObject = itemSo;
        else if (aspect != null) itemScriptableObject = aspect;
        else if (ability != null) itemScriptableObject = ability;
        index = new InvIndex();
        index.AddItem(itemScriptableObject, itemAmount);
        usedSlot = true;
        UpdateSlotUI();
    }
    
    private void UpdateSlotUI()
    {
        if (itemDescTitle != null) itemDescTitle.text = itemName;
        if (itemDescInfo != null) itemDescInfo.text = itemDescription;
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
        else if (!usedSlot) ClearSlotVisuals();
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
        abilitySlotType = 0;
        aspectStats = null;
        abilityStats = null;
        selectedShader.SetActive(false);
        itemScriptableObject = null;
        usedSlot = false;
        if (!switchFrom)
        {
            switchFrom = true;
            ClearSlotVisuals();
        }
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
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    private void OnLeftClick()
    {
        invManager.DeselectAllSlots();
        
        UpdateSlotUI();
        if (selectedShader != null) selectedShader.SetActive(true);

        invManager.SelectSlot();
        if (useButtonText != null) 
            useButtonText.text = itemType == "Aspect" || itemType == "Ability" ? "Equip" : "Use";
        
        invManager.useButton.onClick.RemoveAllListeners();
        invManager.destroyButton.onClick.RemoveAllListeners();
                    
        if (itemName != "")
        {
            invManager.destroyButton.gameObject.SetActive(true);
            invManager.useButton.gameObject.SetActive(true);
            invManager.useButton.onClick.AddListener(() => ItemTypeAction());
            invManager.destroyButton.onClick.AddListener(() => StartCoroutine(DestroyItem()));
        }
        else
        {
            invManager.destroyButton.gameObject.SetActive(false);
            invManager.useButton.gameObject.SetActive(false);
        }
    }

    
    private void ItemTypeAction()
    {
        if (itemType != null && itemType == "Aspect") EquipAspect();
        else if (itemType != null && itemType == "Ability") invManager.EquipAbility(abilityStats, abilitySlotType);
        else RestoreItem();
    }

    private void RestoreItem()
    {
        if (invManager.playerManager != null) invManager.playerManager.AlterGlobalFloat(itemType, restoreValue);
        else Debug.LogError("PlayerManager not found in InvManager! Cannot restore item stat.");
        
        itemAmount--;
        if (itemAmount <= 0)
        {
            index.RemoveItem(itemScriptableObject, itemAmount);
            ClearSlot();
            invManager.DeselectAllSlots();
        }
        UpdateSlotUI();
    }


    private void EquipAspect()
    {
        bool equipped = false;
        if (equippedAspectSlots != null)
        {
            for (int i = 0; i < equippedAspectSlots.Length; i++)
            {
                if (equippedAspectSlots[i] != null && !equippedAspectSlots[i].usedSlot)
                {
                    string iName = itemName;
                    Sprite iSprite = itemSprite; string iDescription = itemDescription; AspectSO aStats = aspectStats;
                    ClearSlot();
                    invManager.EquipAspect(iName, iSprite, iDescription, aStats);
                    
                    equipped = true;
                    if (!usedSlot) itemImage.enabled = false;
                    break;
                }
            }
        }
        if (!equipped) Debug.LogWarning("All equipped Aspect slots are full! Cannot equip " + itemName);
    }

    public void ItemSelect()
    {
        OnLeftClick();
    }
    private IEnumerator DestroyItem()
    {
        choice.messageText.text = "Do you really want to destroy ALL of this item?";
        choice.Choices();
        yield return new WaitUntil(() => choice.yes || choice.no);
        if (choice.yes)
        {
            choice.yes = false;
            index.RemoveItem(itemScriptableObject, itemAmount);
            ClearSlot();
            choice.Disable();
            UpdateSlotUI();
        }
        else if (choice.no)
        {
            choice.no = false;
            choice.Disable();
        }
    }
    
    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
    }

}
