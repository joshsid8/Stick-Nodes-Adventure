using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class AspectSlot : MonoBehaviour, IPointerClickHandler
{
    public string itemName;
    public int maxItemAmount;
    public Sprite itemSprite;
    
    public string itemDescription;
    public TMP_Text itemDescTitle;
    public TMP_Text itemDescInfo;

    public string itemType;
    public float restoreValue;
    public TMP_Text useButtonText;
    public bool usedSlot;
    
    [SerializeField] private Image itemImage;
    
    public GameObject selectedShader;
    
    [SerializeField] private InvManager invManager;
    public AspectSO aspectStats;
    
    private void Start()
    {
        if (invManager == null)
        {
            Debug.LogError("ItemSlot: where is my dad?! (invmanager)", this);
            invManager = FindAnyObjectByType<InvManager>();
            if (invManager == null) Debug.LogError("ItemSlot: HE WENT FOR THE MIIILLKKK!!!", this);
        }
    }

    private void Update()
    {
        UpdateSlotUI();
    }
    
    public void AddAspect(string item, Sprite sprite, string itemDesc,AspectSO aspect)
    {
        itemName = item;
        itemSprite = sprite;
        itemDescription = itemDesc;
        usedSlot = true;
        aspectStats = aspect;
        itemType = "Aspect";
    }

    private void UpdateSlotUI()
    {
        if (itemImage != null)
        {
            itemImage.enabled = true;
            itemImage.sprite = itemSprite;
        }
        else
        {
            usedSlot = false;
            itemName = string.Empty;
            itemSprite = null;
            if (itemImage != null) itemImage.enabled = false;
        }
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnLeftClick();
        }
    }

    private void OnLeftClick()
    {
        invManager.DeselectSlots();
        selectedShader.SetActive(true);

        itemDescTitle.text = itemName;
        itemDescInfo.text = itemDescription;
        invManager.SelectSlot();
        if (itemType == "Aspect") useButtonText.text = "Equip"; else useButtonText.text = "Use";
        invManager.useButton.onClick.AddListener(() => Aspect());
        invManager.destroyButton.onClick.AddListener(() => ClearSlot());
        
    }

    private void Aspect()
    {
        Debug.Log("uh...\nThat sounded better in my head.\nYou see, one of these 4 boxes in the inventory is meant to be filled in due, but there isnt even a script for it!\nits morbin time.");
    }
    public void ClearSlot()
    {
        itemName = string.Empty;
        itemDescription = string.Empty;
        itemSprite = null;
        usedSlot = false;
    }
}
