using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ItemSlot : SlotBase, IPointerClickHandler
{
    [Header("Item Data")]
    public int itemAmount;
    public int maxItemAmount;
    public string itemType;
    public float restoreValue;
    [Space]
    [SerializeField] private TMP_Text amountText;

    public Prompt choice;
    [Space]
    [Header("Aspect and Ability Stuff")]
    public AspectSO aspectStats;
    [SerializeField] private bool aspectSlotFull;
    [SerializeField] private AspectSlot[] equippedAspectSlots;
    
    public AbilitySO abilityStats;

    [HideInInspector] public CollectableSOBase itemScriptableObject;
    
    protected override void Awake()
    {
        base.Awake();
        if (invManager == null) Debug.LogError("ItemSlot: InvManager not found on Awake!", this);
        if (equippedAspectSlots == null || equippedAspectSlots.Length == 0)
            Debug.LogWarning($"ItemSlot {gameObject.name}: No equippedAspectSlots assigned. Aspects cannot be equipped from this slot.");
    }

    public void AddItem(string item, int amount, Sprite sprite, string itemDesc, int maxAmount, string type, CollectableSOBase collectable)
    {
        itemName = item;
        maxItemAmount = maxAmount;
        itemSprite = sprite;
        itemDescription = itemDesc;

        if (collectable != null)
        {
            switch (collectable)
            {
                case ItemSO itemSo:
                    itemScriptableObject = itemSo;
                    restoreValue = itemSo.restoreValue;
                    break;
                case AspectSO aspect:
                    itemScriptableObject = aspect;
                    aspectStats = aspect;
                    break;
                case AbilitySO ability:
                    itemScriptableObject = ability;
                    abilityStats = ability;
                    break;
            }
        }

        itemType = type;
        itemAmount += amount;

        if (invManager.invIndex != null && itemScriptableObject != null) invManager.invIndex.AddItem(itemScriptableObject, amount);
        usedSlot = true;
        UpdateSlotUI();
    }
    
    protected override void UpdateSlotUI()
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
            }
            
            if (amountText != null)
            {
                amountText.enabled = true;
                amountText.text = itemAmount == 1 && itemType != "Aspect" ? "" : itemAmount.ToString();
                if (itemType == "Aspect" || itemType == "Ability") amountText.text = "";
            }
        }
        else if (!usedSlot) ClearSlotVisuals();
    }
    
    public override void ClearSlot()
    {
        itemAmount = 0;
        maxItemAmount = 0;
        itemType = string.Empty;
        restoreValue = 0f;
        aspectStats = null;
        abilityStats = null;
        itemScriptableObject = null;
        base.ClearSlot();
    }

    protected override void ClearSlotVisuals()
    {
        if (amountText != null) amountText.enabled = false;
        base.ClearSlotVisuals();
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    protected override void OnLeftClick()
    {
        base.OnLeftClick();

        if (useButtonText != null) 
            useButtonText.text = itemType == "Aspect" || itemType == "Ability" ? "Equip" : "Use";
        useButtonText.gameObject.SetActive(itemType == "Key" ? false : true);
                    
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
        var ability = invManager.abilitySlots[slot].abilityStats;
        if (ability != null) 
            invManager.AddItem(ability.itemName, 1, ability.itemSprite, ability.itemDescription, 1, "Ability", ability);
        
        invManager.EquipAbility(abilityStats.itemName, abilityStats.itemSprite, abilityStats.itemDescription, abilityStats, slot);
        foreach (var slotButton in invManager.slotButtons) slotButton.onClick.RemoveAllListeners();
        if (invManager.invIndex != null) invManager.invIndex.RemoveItem(itemScriptableObject);

        ClearSlot();
        UpdateSlotUI();
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
}
