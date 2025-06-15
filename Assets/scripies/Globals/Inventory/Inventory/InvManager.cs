using UnityEngine;
using UnityEngine.UI;

public class InvManager : MonoBehaviour
{
    public GameObject inventoryMenu;
    private bool isActivated;
    private PlayerController player;
    public ItemSlot[] itemSlots; // Array for regular inventory item slots
    public AspectSlot[] aspectSlots; // NEW: Array for equipped aspect slots

    public Button useButton;
    public Button destroyButton;
    public Button skillTreeButton; // Not directly used in item/aspect logic, but kept for context

    public PlayerManager playerManager; // Reference to your PlayerManager (for applying stats)

    void Awake()
    {
        player = FindAnyObjectByType<PlayerController>();
        playerManager = FindAnyObjectByType<PlayerManager>();

        if (player == null) Debug.LogWarning("InvManager: PlayerController not found.");
        if (playerManager == null) Debug.LogWarning("InvManager: PlayerManager not found.");

        if (inventoryMenu != null)
        {
            inventoryMenu.SetActive(false);
        }

        // Initialize all slots to ensure their UI is cleared at start
        InitializeAllSlots();
    }

    private void InitializeAllSlots()
    {
        foreach (ItemSlot slot in itemSlots)
        {
            if (slot != null) slot.ClearSlot(); // Clears internal data and updates UI
        }
        foreach (AspectSlot slot in aspectSlots)
        {
            if (slot != null) slot.ClearSlot(); // Clears internal data and updates UI
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown("Inventory"))
        {
            ToggleInventory();
        }
    }

    public void ToggleInventory()
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

        // Deselect any active slot when opening/closing inventory
        DeselectAllSlots();
    }

    // This AddItem is specifically for regular inventory items
    public void AddItem(string itemName, int itemAmount, Sprite itemSprite, string itemDescription, int maxItemAmount, string itemType, float restoreValue, AspectSO aspect)
    {
        // 1. Try to find an existing stack to add to (if stackable)
        for (int i = 0; i < itemSlots.Length; i++)
        {
            // Ensure slot is not null, it's used, and the item name matches, and it's not full
            if (itemSlots[i] != null && itemSlots[i].usedSlot && itemSlots[i].itemName == itemName && itemSlots[i].itemAmount < itemSlots[i].maxItemAmount)
            {
                int amountToAdd = Mathf.Min(itemAmount, itemSlots[i].maxItemAmount - itemSlots[i].itemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue, aspect);
                itemAmount -= amountToAdd; // Reduce the amount that still needs to be added

                if (itemAmount <= 0) // All items have been added
                {
                    Debug.Log($"Added all {amountToAdd} to existing stack of {itemName}.");
                    return;
                }
            }
        }

        // 2. If items remain, find the first empty slot
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && !itemSlots[i].usedSlot) // Check if the slot is empty
            {
                int amountToAdd = Mathf.Min(itemAmount, maxItemAmount); // Add up to the max stack size for the new slot
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue, aspect);
                itemAmount -= amountToAdd; // Reduce remaining amount

                if (itemAmount <= 0) // All items have been added
                {
                    Debug.Log($"Added {amountToAdd} of {itemName} to new slot {itemSlots[i].gameObject.name}.");
                    return;
                }
            }
        }

        // 3. If loop finishes and items still remain
        if (itemAmount > 0)
        {
            Debug.LogWarning($"Inventory is full! Could not add {itemAmount} of {itemName}.");
        }
    }

    // This AddAspect is specifically for Aspect slots
    public void AddAspect(string itemName, Sprite itemSprite, string itemDescription, AspectSO aspect)
    {
        for (int i = 0; i < aspectSlots.Length; i++) // Loop through aspectSlots array
        {
            if (aspectSlots[i] != null && !aspectSlots[i].usedSlot) // Check if the aspect slot is empty
            {
                aspectSlots[i].AddAspect(itemName, itemSprite, itemDescription, aspect);
                Debug.Log($"Added aspect {itemName} to slot {aspectSlots[i].gameObject.name}.");
                return; // Aspect added, exit
            }
        }
        Debug.LogWarning($"Aspect inventory is full! Could not add {itemName}.");
    }

    // This method is called by ItemSlot/AspectSlot when they are clicked
    public void SelectSlot()
    {
        // Clear previous listeners from use/destroy buttons
        useButton.onClick.RemoveAllListeners();
        destroyButton.onClick.RemoveAllListeners();
        
        // Activate use/destroy buttons
        useButton.gameObject.SetActive(true);
        destroyButton.gameObject.SetActive(true);
    }

    // This method deselects ALL slots and hides action buttons
    public void DeselectAllSlots()
    {
        foreach (ItemSlot slot in itemSlots)
        {
            if (slot != null) slot.Deselect(); // Call Deselect on each ItemSlot
        }
        foreach (AspectSlot slot in aspectSlots)
        {
            if (slot != null) slot.Deselect(); // Call Deselect on each AspectSlot
        }
        useButton.gameObject.SetActive(false);
        destroyButton.gameObject.SetActive(false);
    }
}
