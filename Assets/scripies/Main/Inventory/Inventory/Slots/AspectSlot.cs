using UnityEngine;
using UnityEngine.EventSystems;

public class AspectSlot : SlotBase, IPointerClickHandler
{
    // Aspect Data
    public AspectSO aspectStats;
    
    protected override void Start()
    {
        base.Start();
        if (invManager == null) Debug.LogError("AspectSlot: InvManager not found on Awake!", this);
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
    
    public override void ClearSlot()
    {
        aspectStats = null;
        base.ClearSlot();
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    protected override void OnLeftClick()
    {
        base.OnLeftClick();
        if (useButtonText != null) useButtonText.text = "Unequip";
        invManager.destroyButton.gameObject.SetActive(false);
        if (!string.IsNullOrEmpty(itemName))
        {
            invManager.useButton.onClick.AddListener(() => UnequipAspect());
            //invManager.destroyButton.onClick.AddListener(() => StartCoroutine(DestroyItem()));
        }
    }

    private void UnequipAspect()
    {
        invManager.UnequipAspect(itemName, itemSprite, itemDescription, aspectStats);
        ClearSlot();
    }
    
    public void AspectSelect() => OnLeftClick();
}
