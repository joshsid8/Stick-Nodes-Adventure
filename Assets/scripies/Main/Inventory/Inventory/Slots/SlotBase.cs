using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class SlotBase : MonoBehaviour
{
    [HideInInspector] public string itemName;
    [HideInInspector] public Sprite itemSprite;
    [HideInInspector] public string itemDescription;
    [HideInInspector] public bool usedSlot;
    
    [Header("UI References")]
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText;
    public Image itemImage;
    public GameObject selectedShader;
    public GameObject usedShader;
    
    // Manager References
    protected InvManager invManager;
    protected PlayerManager playerManager;

    //private bool switchFrom;
    
    protected virtual void LateUpdate()
    {
        if (usedSlot != usedShader.activeSelf) usedShader.SetActive(usedSlot);
        if (itemImage.enabled != usedSlot) itemImage.enabled = usedSlot;
    }
    
    protected virtual void Start()
    {
        playerManager = PlayerManager.Instance ?? FindAnyObjectByType<PlayerManager>();
        invManager = InvManager.Instance ?? FindAnyObjectByType<InvManager>();
    }

    protected void EnsureManagers()
    {
        if (playerManager == null) playerManager = PlayerManager.Instance ?? FindAnyObjectByType<PlayerManager>();
        if (invManager == null) invManager = InvManager.Instance ?? FindAnyObjectByType<InvManager>();
    }
    
    public void ItemSelect() => OnLeftClick();

    protected virtual void OnLeftClick()
    {
        EnsureManagers();
        if (invManager == null) return;
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
        if (usedSlot && itemSprite != null && itemImage != null) itemImage.sprite = itemSprite;
        else if (!usedSlot) ClearSlotVisuals();
    }

    private void OnDisable() => ClearSlotVisuals();

    public virtual void ClearSlot()
    {
        itemName = string.Empty;
        itemDescription = string.Empty;
        itemSprite = null;
        usedSlot = false;
        ClearSlotVisuals();
    }

    protected virtual void ClearSlotVisuals()
    {
        if (itemImage != null) itemImage.sprite = null;
        if (selectedShader != null) selectedShader.SetActive(false);
        if (itemDescTitle != null) itemDescTitle.text = string.Empty;
        if (itemDescInfo != null) itemDescInfo.text = string.Empty;
    }

    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
    }
}
