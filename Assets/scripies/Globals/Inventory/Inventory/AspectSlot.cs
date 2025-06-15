using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class AspectSlot : MonoBehaviour, IPointerClickHandler
{
    // Aspect Data
    public string itemName;
    public Sprite itemSprite;
    public string itemDescription;
    public AspectSO aspectStats; // Reference to the ScriptableObject for this equipped aspect

    // UI References
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText; // Reference to the text on the "Unequip" button

    [SerializeField] private Image itemImage;
    public GameObject selectedShader;
    public bool usedSlot; // Tracks if this slot contains an equipped aspect
    
    // Manager References
    [SerializeField] private InvManager invManager;
    private PlayerManager playerManager; // Renamed from playerStats for consistency with GameManager

    private void Awake()
    {
        if (invManager == null)
        {
            invManager = FindAnyObjectByType<InvManager>();
            if (invManager == null) Debug.LogError("AspectSlot: InvManager not found on Awake!", this);
        }
        playerManager = FindAnyObjectByType<PlayerManager>();
        if (playerManager == null) Debug.LogError("AspectSlot: PlayerManager not found on Awake!", this);

        UpdateSlotUI(); // Initialize UI state
    }

    public void AddAspect(string item, Sprite sprite, string itemDesc, AspectSO aspect)
    {
        itemName = item;
        itemSprite = sprite;
        itemDescription = itemDesc;
        aspectStats = aspect;
        usedSlot = true; // Mark as used
        UpdateSlotUI(); // Update visuals
    }

    private void UpdateSlotUI()
    {
        if (usedSlot && itemSprite != null)
        {
            if (itemImage != null)
            {
                itemImage.enabled = true;
                itemImage.sprite = itemSprite;
            }
        }
        else
        {
            ClearSlotVisuals();
        }
    }
    
    public void ClearSlot()
    {
        itemName = String.Empty;
        itemDescription = String.Empty;
        itemSprite = null;
        aspectStats = null;
        usedSlot = false;
        ClearSlotVisuals();
    }

    private void ClearSlotVisuals()
    {
        if (itemImage != null)
        {
            itemImage.enabled = false;
            itemImage.sprite = null;
        }
        if (selectedShader != null) selectedShader.SetActive(false);
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

        if (itemDescTitle != null) itemDescTitle.text = itemName;
        if (itemDescInfo != null) itemDescInfo.text = itemDescription;

        invManager.SelectSlot(); // Notify InvManager that a slot is selected

        if (useButtonText != null) useButtonText.text = "Unequip";
        
        invManager.useButton.onClick.RemoveAllListeners();
        invManager.destroyButton.onClick.RemoveAllListeners();

        invManager.useButton.onClick.AddListener(() => UnequipAspectAction()); // Renamed for clarity
        invManager.destroyButton.onClick.AddListener(() => ClearSlot()); // Destroy logic
    }

    private void UnequipAspectAction() // Renamed for clarity
    {
        invManager.AddItem(itemName, 1, itemSprite, itemDescription, 1,"Aspect",0,aspectStats);
        
        ClearSlot(); // Clear this equipped aspect slot after it's moved/processed
        invManager.DeselectAllSlots(); // Deselect after action
    }

    // Called by InvManager.DeselectAllSlots()
    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
    }
}
