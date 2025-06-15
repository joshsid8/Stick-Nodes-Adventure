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
    public string itemType; // e.g., "Consumable", "Weapon", "KeyItem"
    public float restoreValue; // e.g., health restored, energy restored

    // UI References (ASSIGN THESE IN INSPECTOR FOR EACH SLOT)
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText;

    [SerializeField] private TMP_Text amountText; // Actual TextMeshPro component
    [SerializeField] private Image itemImage;     // Actual Image component
    
    // UI Visuals/State
    public GameObject selectedShader; // Visual indicator when selected
    public bool itemSelected; // Tracks if this specific slot is selected
    public bool usedSlot; // Tracks if this slot contains an item (based on itemAmount > 0)
    
    // Manager Reference (ASSIGN THIS IN INSPECTOR FOR EACH SLOT)
    [SerializeField] private InvManager invManager;
    
    // Aspect related (specific to ItemSlot for handling AspectSOs being passed)
    public AspectSO aspectStats; // If an Aspect item uses this slot directly
    [SerializeField] AspectSlot[] aspectSlot; // Array of AspectSlot components for equip area
    [SerializeField] private bool aspectSlotFull; // This flag may be redundant if checking actual aspectSlots

    private void Awake() // Use Awake for finding manager to ensure order
    {
        // Try to find InvManager if not assigned (less ideal, but robust fallback)
        if (invManager == null)
        {
            invManager = FindAnyObjectByType<InvManager>();
            if (invManager == null) Debug.LogError("ItemSlot: InvManager not found on Awake!", this);
        }
        // Initialize UI state
        UpdateSlotUI();
    }

    // AddItem for regular inventory items
    public void AddItem(string item, int amount, Sprite sprite, string itemDesc, int maxAmount, string type, float value, AspectSO aspect)
    {
        itemName = item;
        maxItemAmount = maxAmount;
        itemSprite = sprite;
        itemDescription = itemDesc;
        itemType = type;
        restoreValue = value;
        
        if (itemType == "Aspect") aspectStats = aspect;
        // This is called AFTER InvManager has determined if it's a new stack or existing
        itemAmount += amount; // Just add the amount passed

        usedSlot = true; // Mark this slot as being used
        UpdateSlotUI(); // Update UI after data changes
    }

    // AddAspect for collecting aspects into an ItemSlot (e.g., before equipping)
    public void AddAspect(string item, Sprite sprite, string itemDesc, AspectSO aspect)
    {
        itemName = item;
        itemSprite = sprite;
        itemDescription = itemDesc;
        usedSlot = true;
        itemAmount = 1; // Aspects are generally not stackable, so amount is 1
        aspectStats = aspect;
        itemType = "Aspect";
        UpdateSlotUI(); // Update UI after data changes
    }
    
    // Reworked UpdateSlotUI for correct display logic and clarity
    private void UpdateSlotUI()
    {
        if (itemAmount > 0 && usedSlot) // Only display if there's a positive amount AND the slot is marked as used
        {
            if (itemImage != null) // Always update image if present
            {
                itemImage.enabled = true;
                itemImage.sprite = itemSprite;
            }
            
            if (amountText != null) // Always update amount text if present
            {
                amountText.enabled = true;
                amountText.text = (itemAmount == 1 && itemType != "Aspect") ? "" : itemAmount.ToString(); // Hide amount if 1 for non-aspects
                if (itemType == "Aspect") amountText.text = ""; // Aspects typically don't show numbers
            }
        }
        else // If itemAmount is 0 or less, or slot is not used, clear everything
        {
            ClearSlotVisuals();
        }
    }

    // Clears all internal data and then updates visuals
    public void ClearSlot()
    {
        itemName = String.Empty;
        itemDescription = String.Empty;
        itemAmount = 0;
        maxItemAmount = 0;
        itemSprite = null;
        itemType = String.Empty;
        restoreValue = 0f;
        aspectStats = null; // Clear aspect specific data

        usedSlot = false; // Slot is now empty
        itemSelected = false; // Ensure it's not selected

        ClearSlotVisuals(); // Update UI to reflect emptiness
    }

    // Helper to only clear visuals (called by ClearSlot or UpdateSlotUI when empty)
    private void ClearSlotVisuals()
    {
        if (amountText != null) amountText.enabled = false;
        if (itemImage != null)
        {
            itemImage.enabled = false;
            itemImage.sprite = null; // Clear the sprite
        }
        if (selectedShader != null) selectedShader.SetActive(false); // Hide selection shader
        // Clear description UI when slot is cleared
        if (itemDescTitle != null) itemDescTitle.text = String.Empty;
        if (itemDescInfo != null) itemDescInfo.text = String.Empty;
    }

    // --- Input Handling ---
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnLeftClick();
        }
    }

    private void OnLeftClick()
    {
        if (!usedSlot) return; // Don't allow selection of empty slots

        invManager.DeselectAllSlots(); // Deselect all other slots first
        
        if (selectedShader != null) selectedShader.SetActive(true);
        itemSelected = true;

        // Display item description
        if (itemDescTitle != null) itemDescTitle.text = itemName;
        if (itemDescInfo != null) itemDescInfo.text = itemDescription;

        invManager.SelectSlot(); // Notify InvManager that a slot is selected

        // Setup Use/Equip/Destroy buttons based on item type
        if (useButtonText != null)
        {
            useButtonText.text = (itemType == "Aspect") ? "Equip" : "Use";
        }
        
        // Remove existing listeners before adding new ones
        invManager.useButton.onClick.RemoveAllListeners();
        invManager.destroyButton.onClick.RemoveAllListeners();

        // Add listeners for this specific slot's actions
        invManager.useButton.onClick.AddListener(() => ItemTypeAction());
        invManager.destroyButton.onClick.AddListener(() => ClearSlot()); // Destroy logic
    }

    // Determines action based on itemType
    private void ItemTypeAction()
    {
        if (itemType != null && itemType == "Aspect")
        {
            EquipAspect();
        }
        else // For non-aspects (consumables, weapons, etc.)
        {
            RestoreItem();
        }
        // After action, you might want to deselect the slot automatically
        invManager.DeselectAllSlots();
    }

    private void RestoreItem()
    {
        // Null check for playerManager
        if (invManager.playerManager != null)
        {
            invManager.playerManager.SetGlobalFloat(itemType, restoreValue); // Assuming itemType is a stat name like "Health"
        }
        else
        {
            Debug.LogError("PlayerManager not found in InvManager! Cannot restore item stat.");
        }
        
        itemAmount--; // Reduce item count
        if (itemAmount <= 0)
        {
            ClearSlot(); // Clear the slot if amount reaches zero
        }
        else
        {
            UpdateSlotUI(); // Update UI to show new amount
        }
    }

    private void EquipAspect()
    {
        // Find an empty AspectSlot in the equipped area and move this aspect there
        bool equipped = false;
        if (aspectSlot != null) // aspectSlot is an array of equipped aspect slots
        {
            for (int i = 0; i < aspectSlot.Length; i++)
            {
                if (aspectSlot[i] != null && !aspectSlot[i].usedSlot)
                {
                    aspectSlot[i].AddAspect(itemName, itemSprite, itemDescription, aspectStats);
                    
                    // Apply stats via playerManager when equipped
                    if (aspectStats != null && invManager.playerManager != null)
                    {
                        for (int j = 0; j < aspectStats.floatStatsName.Length; j++)
                        {
                            invManager.playerManager.SetGlobalFloat(aspectStats.floatStatsName[j], aspectStats.floatStats[j]);
                        }
                        // Optionally apply bool stats here too if AspectSO has them
                    }
                    else if (aspectStats == null)
                    {
                         Debug.LogWarning("AspectSO not assigned to ItemSlot for equipping!");
                    }
                    else if (invManager.playerManager == null)
                    {
                         Debug.LogError("PlayerManager not found in InvManager. Cannot apply aspect stats.");
                    }

                    ClearSlot(); // Clear the item slot after moving to equipped slot
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

    // Call this from InvManager.DeselectAllSlots()
    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
        itemSelected = false;
    }
}
