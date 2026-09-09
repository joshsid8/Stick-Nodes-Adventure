using System;
using System.Collections;
using scripies.Player;
using UnityEngine;
using UnityEngine.UI;

public class InvManager : GUIBase
{
    public static InvManager Instance {get; private set;}
    
    public GameObject inventoryMenu;
    public ItemSlot[] itemSlots;
    public AspectSlot[] aspectSlots;
    public AbilitySlot[] abilitySlots;
    public InvIndex invIndex;

    public Button useButton;
    public Button destroyButton;
    public Button[] slotButtons;

    public bool swappingAbilities;

    public GameObject persistentItem;
    
    public PlayerManager playerManager;
    
    protected override void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        base.Awake();
        invIndex = new InvIndex();
        playerManager = FindAnyObjectByType<PlayerManager>();
        if (playerManager == null) Debug.LogError("InvManager: We cant find the player manager D:");
        if (isActive) ToggleInventory();
        InitializeAllSlots();
    }

    private void Start()
    {
        if (input == null)
        {
            input = PlayerHealth.Instance.pc.input;
            if (input == null)
                throw new NullReferenceException("InvManager: After trying TTTWWIIICCEEE, we still couldnt find input.");
        }
        persistentItem = GameObject.Find("persistentItem");
        
        input.Player.Inventory.performed += _ => ToggleInventory();
    }

    private void InitializeAllSlots()
    {
        foreach (var slot in itemSlots) if (slot != null) slot.ClearSlot(); 
        foreach (var slot in aspectSlots) if (slot != null) slot.ClearSlot();
        foreach (var slot in abilitySlots) if (slot != null) slot.ClearSlot();
    }

    private void Update()
    {
        #if UNITY_EDITOR
        if (Input.GetKeyDown("o")) SaveSystem.Save();
        if (Input.GetKeyDown("p")) SaveSystem.Load();
        #endif
    }

    public void ToggleInventory()
    {
        isActive = !isActive;
        if (inventoryMenu == null) return;
        rt.anchoredPosition = new Vector2(0, isActive ? 0 : -1000);
        GetActive(isActive);
        if (isActive) CloseCurrentMenus();
        CancelSwap();
    }

    public void AddItem(string itemName , int itemAmount = 1, Sprite itemSprite = null, string itemDescription = "", int maxItemAmount = 1, string itemType = null, CollectableSOBase collectable = null)
    {
        for (var i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && itemSlots[i].usedSlot && itemSlots[i].itemName == itemName && itemSlots[i].itemAmount < itemSlots[i].maxItemAmount)
            {
                var amountToAdd = Mathf.Min(itemAmount, itemSlots[i].maxItemAmount - itemSlots[i].itemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, collectable);
                itemAmount -= amountToAdd;
                if (itemAmount <= 0)
                {
                    itemSlots[i].ItemSelect(); 
                    return;
                }
            }
        }
        
        for (var i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && !itemSlots[i].usedSlot)
            {
                var amountToAdd = Mathf.Min(itemAmount, maxItemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, collectable);
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

    public void PresetAddItem(CollectableSOBase collectable)
    {
        if (collectable == null) throw new ArgumentNullException(nameof(collectable) + " doent exist!");
        switch (collectable)
        {
            case ItemSO item: AddItem(item.itemName, item.itemAmount, item.itemSprite, item.itemDescription, item.maxItemAmount, item.itemType, item); break;
            case ConsumableSO consumable: AddItem(consumable.itemName, 1, consumable.itemSprite, consumable.itemDescription, consumable.maxItemAmount, "Consumable", consumable); break;
            case AspectSO aspect: AddItem(aspect.itemName, 1, aspect.itemSprite, aspect.itemDescription, 16, "Aspect", aspect); break;
            case AbilitySO ability: AddItem(ability.itemName, 1, ability.itemSprite, ability.itemDescription, 16, "Ability", ability); break;
            default: throw new ArgumentException($"[InvManager] We cant accept type {collectable.GetType()} dawg");
        }
    }
    
    public void EquipAspect(string itemName, Sprite itemSprite, string itemDescription, AspectSO aspect)
    {
        for (var i = 0; i < aspectSlots.Length; i++)
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
                AddItem(itemName, 1, itemSprite, itemDescription, 16, "Aspect", aspect);
                break;
            }
        }
        //Debug.LogWarning($"Aspect equipped slots are full! Cannot equip {itemName}.");
    }
    
    public void UnequipAspect(string itemName, Sprite itemSprite, string itemDescription, AspectSO aspect)
    {
        if (aspect != null && playerManager != null) playerManager.RemoveAspectModifiers(aspect);
        AddItem(itemName, 1, itemSprite, itemDescription, 16, "Aspect", aspect);
    }

    private void EquipAbility(string itemName, Sprite itemSprite, string itemDescription, AbilitySO ability, int slot = 0)
    {
        if (abilitySlots.Length > 0) abilitySlots[slot].AddAbility(itemName, itemSprite, itemDescription, ability, slot);
    }
    
    public void SwapAbility(int slot, ItemSlot item)
    {
        if (item.abilityStats == null) throw new Exception("SwapAbility: Seems like the ability was removed.");
        if (swappingAbilities) swappingAbilities = false;
        var ability = abilitySlots[slot].abilityStats;
        EquipAbility(item.abilityStats.itemName, item.abilityStats.itemSprite, item.abilityStats.itemDescription, item.abilityStats, slot);
        
        if (ability != null) 
            AddItem(ability.itemName, 1, ability.itemSprite, ability.itemDescription, 16, "Ability", ability);
        
        foreach (var slotButton in slotButtons) slotButton.onClick.RemoveAllListeners();
    }
    
    public void CancelSwap()
    {
        if (!swappingAbilities) return;
        print("Ok i guess not then >:(");
        swappingAbilities = false;
        foreach (var slotButton in slotButtons) slotButton.onClick.RemoveAllListeners();
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
                    item.itemType, item);
            
            else if (data.storedItems[i] is AspectSO)
                AddItem(aspect.itemName, 1, aspect.itemSprite, aspect.itemDescription, 16, "Aspect", aspect);
            
            else if (data.storedItems[i] is AbilitySO)
                AddItem(ability.itemName, 1, ability.itemSprite, ability.itemDescription, 16, "Ability", ability);
        }
    }
}

[Serializable]
public struct PlayerSaveInventory
{
    public CollectableSOBase[] storedItems;
}
