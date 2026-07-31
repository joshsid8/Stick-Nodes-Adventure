using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class InvManager : GUIBase
{
    public static InvManager Instance {get; private set;}
    
    public GameObject inventoryMenu;
    public ItemSlot[] itemSlots;
    public AspectSlot[] aspectSlots;
    public AbilitySlot abilitySlot;
    public InvIndex invIndex;

    public Button useButton;
    public Button destroyButton;

    public PlayerManager playerManager;
    protected override void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        base.Awake();
        rt = GetComponent<RectTransform>();
        playerManager = FindAnyObjectByType<PlayerManager>();
        invIndex = new InvIndex();
        if (player == null) Debug.LogWarning("InvManager: PlayerController not found.");
        if (playerManager == null) Debug.LogWarning("InvManager: PlayerManager not found.");
        if (isActive) ToggleInventory();
        InitializeAllSlots();
    }

    private void InitializeAllSlots()
    {
        foreach (var slot in itemSlots) if (slot != null) slot.ClearSlot(); 
        foreach (var slot in aspectSlots) if (slot != null) slot.ClearSlot();
    }

    // Update is called once per frame
    void Update()
    {
        Inputs();
    }

    private void Inputs()
    {
        if (Input.GetButtonDown("Inventory")) ToggleInventory();
        if (Input.GetButtonDown("Cancel") && isActive)
        {
            isActive = true;
            ToggleInventory();
        }
        if (Input.GetKeyDown("o")) SaveSystem.Save();
        if (Input.GetKeyDown("p")) SaveSystem.Load();
    }

    public void ToggleInventory()
    {
        isActive = !isActive;
        if (inventoryMenu != null)
        {
            rt.anchoredPosition = new Vector2(0, isActive ? 0 : -1000);
            inventoryMenu.SetActive(isActive);
        }
        GetActive(isActive);
    }

    public void AddItem(string itemName, int itemAmount, Sprite itemSprite, string itemDescription, int maxItemAmount, string itemType = null, ItemSO itemSo = null,AspectSO aspect = null, AbilitySO ability = null, int abilitype = 0)
    {
        for (var i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && itemSlots[i].usedSlot && itemSlots[i].itemName == itemName && itemSlots[i].itemAmount < itemSlots[i].maxItemAmount)
            {
                var amountToAdd = Mathf.Min(itemAmount, itemSlots[i].maxItemAmount - itemSlots[i].itemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, itemSo,aspect, ability, abilitype);
                itemAmount -= amountToAdd;
                if (itemAmount <= 0)
                {
                    itemSlots[i].ItemSelect(); return;
                }
            }
        }
        
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && !itemSlots[i].usedSlot)
            {
                int amountToAdd = Mathf.Min(itemAmount, maxItemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, itemSo, aspect, ability, abilitype);
                itemAmount -= amountToAdd;

                if (itemAmount <= 0)
                {
                    itemSlots[i].ItemSelect();
                    return;
                }
            }
        }
        if (itemAmount > 0) Debug.LogWarning($"Inventory is full! Could not add {itemAmount} of {itemName}.");
    }
    
    public void EquipAspect(string itemName, Sprite itemSprite, string itemDescription, AspectSO aspect)
    {
        for (int i = 0; i < aspectSlots.Length; i++)
        {
            if (aspectSlots[i] != null && !aspectSlots[i].usedSlot && aspectSlots[i].itemName != itemName)
            {
                aspectSlots[i].AddAspect(itemName, itemSprite, itemDescription, aspect);
                //Debug.Log($"Equipped aspect {itemName} to slot {aspectSlots[i].gameObject.name}.");
                aspectSlots[i].AspectSelect();
                return;
            }
            if (aspectSlots[i].itemName == itemName)
            {
                //Debug.Log($"Seems like there is already a {itemName} in your slots");
                AddItem(itemName, 1, itemSprite, itemDescription, 1, "Aspect", null ,aspect, null, 0);
                break;
            }
        }
        //Debug.LogWarning($"Aspect equipped slots are full! Cannot equip {itemName}.");
    }
    
    public void UnequipAspect(string itemName, Sprite itemSprite, string itemDescription, AspectSO aspect)
    {
        if (aspect != null && playerManager != null) playerManager.RemoveAspectModifiers(aspect);
        AddItem(itemName, 1, itemSprite, itemDescription, 1, "Aspect", null ,aspect, null, 0);
    }

    public void EquipAbility(AbilitySO ability)
    {
        if (abilitySlot != null) abilitySlot.AddAbility(ability); 
    }

    public void SelectSlot()
    {
        useButton.onClick.RemoveAllListeners();
        destroyButton.onClick.RemoveAllListeners();
        
        useButton.gameObject.SetActive(true);
        destroyButton.gameObject.SetActive(true);
    }

    public void DeselectAllSlots()
    {
        foreach (var slot in itemSlots) if (slot != null) slot.Deselect();
        foreach (var slot in aspectSlots) if (slot != null) slot.Deselect();
        useButton.gameObject.SetActive(false);
        destroyButton.gameObject.SetActive(false);
    }

    public void Save(ref PlayerSaveInventory data)
    {
        data.storedItems = new CollectableSOBase[itemSlots.Length];
        for (var i = 0; i < itemSlots.Length; i++) data.storedItems[i] = itemSlots[i].itemScriptableObject;
    }
    
    public void Load(PlayerSaveInventory data)
    {
        foreach (var slot in itemSlots) if (slot != null) slot.ClearSlot();
        
        for (var i = 0; i < itemSlots.Length; i++)
        {
            var item = data.storedItems[i] as ItemSO;
            var aspect = data.storedItems[i] as AspectSO;
            var ability = data.storedItems[i] as AbilitySO;
            
            if (data.storedItems[i] is ItemSO) 
                AddItem(item.itemName, item.itemAmount, item.itemSprite, item.itemDescription, item.maxItemAmount, 
                    item.itemType, item, null, null, 0);
            
            else if (data.storedItems[i] is AspectSO)
                AddItem(aspect.itemName, 1, aspect.itemSprite, aspect.itemDescription, 1, "Aspect", null,
                    aspect, null, 0);
            
            else if (data.storedItems[i] is AbilitySO)
                AddItem(ability.itemName, 1, ability.itemSprite, ability.itemDescription, 1, 
                    "Ability", null, null, ability);
        }
    }
    
}

[System.Serializable]
public struct PlayerSaveInventory
{
    public CollectableSOBase[] storedItems;
}
