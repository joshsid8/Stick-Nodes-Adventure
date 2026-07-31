using System.Collections;
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
    public AspectSO aspectStats;

    // UI References
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;
    public TMP_Text useButtonText;

    [SerializeField] private Image itemImage;
    public GameObject selectedShader;
    public bool usedSlot;
    
    // Manager References
    [SerializeField] private InvManager invManager;
    private PlayerManager playerManager;
    public Prompt choice;

    private bool switchFrom;
    
    private void Awake()
    {
        if (invManager == null)
        {
            invManager = InvManager.Instance;
            if (invManager == null) Debug.LogError("AspectSlot: InvManager not found on Awake!", this);
        }
        playerManager = FindAnyObjectByType<PlayerManager>();
        if (playerManager == null) Debug.LogError("AspectSlot: PlayerManager not found on Awake!", this);
    }

    public void AddAspect(string item, Sprite sprite, string itemDesc, AspectSO aspect)
    {
        itemName = item;
        itemSprite = sprite;
        itemDescription = itemDesc;
        aspectStats = aspect;
        usedSlot = true;
        playerManager.ApplyAspectModifiers(aspectStats);
        UpdateSlotUI();
    }

    private void UpdateSlotUI()
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
        else if (!usedSlot)
        {
            ClearSlotVisuals();
        }
    }
    
    public void ClearSlot()
    {
        itemName = string.Empty;
        itemDescription = string.Empty;
        itemSprite = null;
        aspectStats = null;
        usedSlot = false;
        if (!switchFrom)
        {
            switchFrom = true;
            ClearSlotVisuals();
        }
    }

    private void ClearSlotVisuals()
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
        if (useButtonText != null) useButtonText.text = "Unequip";
        
        invManager.useButton.onClick.RemoveAllListeners();
        invManager.destroyButton.onClick.RemoveAllListeners();
        
        if (!string.IsNullOrEmpty(itemName))
        {
            invManager.destroyButton.gameObject.SetActive(true);
            invManager.useButton.gameObject.SetActive(true);
            invManager.useButton.onClick.AddListener(() => UnequipAspect());
            invManager.destroyButton.onClick.AddListener(() => StartCoroutine(DestroyItem()));
        }
        else
        {
            invManager.destroyButton.gameObject.SetActive(false);
            invManager.useButton.gameObject.SetActive(false);
        }
    }

    private void UnequipAspect()
    {
        invManager.UnequipAspect(itemName, itemSprite, itemDescription, aspectStats);
        ClearSlot();
    }
    private IEnumerator DestroyItem()
    {
        choice.Choices(message: "Do you really want to destroy this aspect?");
        yield return new WaitUntil(() => choice.choice != 0);
        if (choice.choice == 2)
        {
            invManager.UnequipAspect(itemName, itemSprite, itemDescription, aspectStats);
            ClearSlot();
            UpdateSlotUI();
        }
    }
    
    public void AspectSelect() => OnLeftClick();
    
    public void Deselect()
    {
        if (selectedShader != null) selectedShader.SetActive(false);
    }
}
