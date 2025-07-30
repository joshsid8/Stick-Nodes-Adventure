using UnityEngine;
using UnityEngine.UI;

public class InvManager : MonoBehaviour
{
    public static InvManager Instance {get; private set;}
    
    public GameObject inventoryMenu;
    public bool isActivated;
    private PlayerController player;
    public ItemSlot[] itemSlots;
    public AspectSlot[] aspectSlots;

    public Button useButton;
    public Button destroyButton;

    public PlayerManager playerManager;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        player = FindAnyObjectByType<PlayerController>();
        playerManager = FindAnyObjectByType<PlayerManager>();

        if (player == null) Debug.LogWarning("InvManager: PlayerController not found.");
        if (playerManager == null) Debug.LogWarning("InvManager: PlayerManager not found.");

        if (inventoryMenu != null)
        {
            inventoryMenu.SetActive(false);
        }

        InitializeAllSlots();
    }

    private void InitializeAllSlots()
    {
        foreach (ItemSlot slot in itemSlots)
        {
            if (slot != null) slot.ClearSlot();
        }
        foreach (AspectSlot slot in aspectSlots)
        {
            if (slot != null) slot.ClearSlot();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown("Inventory"))
        {
            ToggleInventory();
        }
        else if (Input.GetButtonDown("Cancel"))
        {
            isActivated = false;
            inventoryMenu.SetActive(isActivated);
        }
    }

    private void ToggleInventory()
    {
        isActivated = !isActivated;
        if (inventoryMenu != null)
        {
            inventoryMenu.SetActive(isActivated);
        }
        if (player != null)
        {
            player.canAct = !isActivated;
        }
    }

    public void AddItem(string itemName, int itemAmount, Sprite itemSprite, string itemDescription, int maxItemAmount, string itemType, float restoreValue, AspectSO aspect)
    {
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && itemSlots[i].usedSlot && itemSlots[i].itemName == itemName && itemSlots[i].itemAmount < itemSlots[i].maxItemAmount)
            {
                int amountToAdd = Mathf.Min(itemAmount, itemSlots[i].maxItemAmount - itemSlots[i].itemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue, aspect);
                itemAmount -= amountToAdd;

                if (itemAmount <= 0)
                {
                    itemSlots[i].ItemSelect();
                    return;
                }
            }
        }
        
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && !itemSlots[i].usedSlot)
            {
                int amountToAdd = Mathf.Min(itemAmount, maxItemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue, aspect);
                itemAmount -= amountToAdd;

                if (itemAmount <= 0)
                {
                    itemSlots[i].ItemSelect();
                    return;
                }
            }
        }
        
        if (itemAmount > 0)
        {
            Debug.LogWarning($"Inventory is full! Could not add {itemAmount} of {itemName}.");
        }
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
                AddItem(itemName, 1, itemSprite, itemDescription, 1, "Aspect", 0, aspect);
                break;
            }
        }
        //Debug.LogWarning($"Aspect equipped slots are full! Cannot equip {itemName}.");
    }
    
    public void UnequipAspect(string itemName, Sprite itemSprite, string itemDescription, AspectSO aspect)
    {
        if (aspect != null && playerManager != null)
        {
            playerManager.RemoveAspectModifiers(aspect);
        }
        AddItem(itemName, 1, itemSprite, itemDescription, 1, "Aspect", 0, aspect);
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
        foreach (ItemSlot slot in itemSlots)
        {
            if (slot != null) slot.Deselect();
        }
        foreach (AspectSlot slot in aspectSlots)
        {
            if (slot != null) slot.Deselect();
        }
        useButton.gameObject.SetActive(false);
        destroyButton.gameObject.SetActive(false);
    }
}
