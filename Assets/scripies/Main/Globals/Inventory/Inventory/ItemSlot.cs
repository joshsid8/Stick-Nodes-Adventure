using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ItemSlot : MonoBehaviour, IPointerClickHandler
{
    [Header("Item Data")]
    public string itemName;
    public int itemAmount;
    public int maxItemAmount;
    public Sprite itemSprite;
    public string itemDescription;
    public string itemType;
    public float restoreValue;
    [Space]
    [Header("UI Properties")]
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText;

    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image itemImage;
    
    public GameObject selectedShader;
    public bool usedSlot;
    
    [SerializeField] private InvManager invManager;
    public Prompt choice;
    [Space]
    [Header("Aspect and Ability Stuff")]
    public AspectSO aspectStats;
    [SerializeField] private bool aspectSlotFull;
    [SerializeField] private AspectSlot[] equippedAspectSlots;
    
    public AbilitySO abilityStats;
    [SerializeField] private int abilitySlotType;

    [HideInInspector] public CollectableSOBase itemScriptableObject;
    
    private bool switchFrom;

    private void Awake()
    {
        if (invManager == null) Debug.LogError("ItemSlot: InvManager not found on Awake!", this);
        if (equippedAspectSlots == null || equippedAspectSlots.Length == 0)
            Debug.LogWarning($"ItemSlot {gameObject.name}: No equippedAspectSlots assigned. Aspects cannot be equipped from this slot.");
    }

    public void AddItem(string item, int amount, Sprite sprite, string itemDesc, int maxAmount, string type, ItemSO itemSo, AspectSO aspect, AbilitySO ability, int abilityType)
    {
        itemName = item;
        maxItemAmount = maxAmount;
        itemSprite = sprite;
        itemDescription = itemDesc;
        if (itemSo != null)
        {
            itemScriptableObject = itemSo;
            restoreValue = itemSo.restoreValue;
        }
        else if (aspect != null)
        {
            itemScriptableObject = aspect;
            aspectStats = aspect;
        }
        else if (ability != null)
        {
            itemScriptableObject = ability;
            abilityStats = ability;
        }
        itemType = type;
        abilitySlotType = abilityType;
        itemAmount += amount;

        if (invManager.invIndex != null && itemScriptableObject != null) invManager.invIndex.AddItem(itemScriptableObject, amount);
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
                itemImage.preserveAspect = true;
                if (itemSprite != null) itemImage.color = Color.white;
                itemImage.sprite = itemSprite;
                //itemImage.GetComponent<RectTransform>().sizeDelta = new Vector2(itemImage.GetComponent<RectTransform>().sizeDelta.x * 1.2f, itemImage.GetComponent<RectTransform>().sizeDelta.y * 1.2f);
            }
            
            if (amountText != null)
            {
                amountText.enabled = true;
                amountText.text = itemAmount == 1 && itemType != "Aspect" ? "" : itemAmount.ToString();
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
        useButtonText.gameObject.SetActive(itemType == "Key" ? false : true);
        
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
        else if (itemType != null && itemType == "Ability")
        {
            foreach (var slotButton in invManager.slotButtons) slotButton.onClick.RemoveAllListeners();
            invManager.slotButtons[0].onClick.AddListener(() => SwapAbility());
            invManager.slotButtons[1].onClick.AddListener(() => SwapAbility(1));
            invManager.slotButtons[2].onClick.AddListener(() => SwapAbility(2));
            print("Choose an ability to swap!");
        }
        else if (itemType != "Key") UseItem();
    }

    public void UseItem()
    {
        if (invManager.playerManager != null && itemType != "Key") invManager.playerManager.AlterGlobalFloat(itemType, restoreValue);
        else if (invManager.playerManager == null) Debug.LogError("PlayerManager not found in InvManager! Cannot restore item stat.");
        if (invManager.invIndex != null) invManager.invIndex.RemoveItem(itemScriptableObject);
        
        itemAmount--;
        if (itemAmount <= 0)
        {
            ClearSlot();
            invManager.DeselectAllSlots();
        }
        UpdateSlotUI();
    }

    private void EquipAspect()
    {
        var equipped = false;
        if (equippedAspectSlots != null)
        {
            foreach (var slot in equippedAspectSlots)
            {
                if (slot != null && !slot.usedSlot)
                {
                    string iName = itemName;
                    Sprite iSprite = itemSprite; string iDescription = itemDescription; AspectSO aStats = aspectStats;
                    if (invManager.invIndex != null) invManager.invIndex.RemoveItem(itemScriptableObject);
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

    public void ItemSelect() => OnLeftClick();

    private void SwapAbility(int slot = 0)
    {
        print("Ok uhh switching abilities");
        invManager.EquipAbility(abilityStats, slot);
        foreach (var slotButton in invManager.slotButtons) slotButton.onClick.RemoveAllListeners();
    }
    
    private IEnumerator DestroyItem()
    {
        choice.Choices(message: "Do you really want to destroy ALL of this item?");
        yield return new WaitUntil(() => choice.choice != 0);
        if (choice.choice == 2)
        {
            if (invManager.invIndex != null) invManager.invIndex.RemoveItem(itemScriptableObject, itemAmount);
            ClearSlot();
            UpdateSlotUI();
        }
    }
    
    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
    }

}
