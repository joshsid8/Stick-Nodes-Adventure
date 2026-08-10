using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SlotBase : MonoBehaviour
{
    [Header("Slot Data")]
    public string itemName;
    public Sprite itemSprite;
    public string itemDescription;
    public bool usedSlot;
    
    [Header("UI References")]
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText;
    public Image itemImage;
    public GameObject selectedShader;

    [Header("Manager References")]
    [SerializeField] protected InvManager invManager;
    protected PlayerManager playerManager;

    private bool switchFrom;

    protected virtual void Awake()
    {
        if (invManager == null) invManager = InvManager.Instance;
        playerManager = FindAnyObjectByType<PlayerManager>();
    }

    protected virtual void OnLeftClick()
    {
        invManager.DeselectAllSlots();
        UpdateSlotUI();
        if (selectedShader != null) selectedShader.SetActive(true);
        invManager.SelectSlot();
        
        invManager.useButton.onClick.RemoveAllListeners();
        invManager.destroyButton.onClick.RemoveAllListeners();
        
        if (!string.IsNullOrEmpty(itemName))
        {
            invManager.destroyButton.gameObject.SetActive(true);
            invManager.useButton.gameObject.SetActive(true);
        }
        else
        {
            invManager.destroyButton.gameObject.SetActive(false);
            invManager.useButton.gameObject.SetActive(false);
        }
    }
    
    protected virtual void UpdateSlotUI()
    {
        if (itemDescTitle != null) itemDescTitle.text = itemName;
        if (itemDescInfo != null) itemDescInfo.text = itemDescription;
        
        if (usedSlot && itemSprite != null)
        {
            if (itemImage != null)
            {
                itemImage.enabled = true;
                itemImage.sprite = itemSprite;
            }
        }
        else if (!usedSlot) ClearSlotVisuals();
    }

    public virtual void ClearSlot()
    {
        itemName = string.Empty;
        itemDescription = string.Empty;
        itemSprite = null;
        usedSlot = false;
        
        if (!switchFrom)
        {
            switchFrom = true;
            ClearSlotVisuals();
        }
    }

    protected virtual void ClearSlotVisuals()
    {
        if (itemImage != null)
        {
            itemImage.enabled = false;
            itemImage.sprite = null;
        }
        if (selectedShader != null) selectedShader.SetActive(false);
        if (itemDescTitle != null) itemDescTitle.text = string.Empty;
        if (itemDescInfo != null) itemDescInfo.text = string.Empty;
    }

    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
    }
}
