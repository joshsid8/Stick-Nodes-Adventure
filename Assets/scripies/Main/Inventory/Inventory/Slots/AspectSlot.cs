using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class AspectSlot : SlotBase, IPointerClickHandler
{
    // Aspect Data
    // TODO: Aspect inventory needs shaders to show that items are there, especially since all current aspects have no sprite yet

    public AspectSO aspectStats;

    public Prompt choice;
    
    protected override void Awake()
    {
        base.Awake();
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
        
        if (!string.IsNullOrEmpty(itemName))
        {
            invManager.useButton.onClick.AddListener(() => UnequipAspect());
            invManager.destroyButton.onClick.AddListener(() => StartCoroutine(DestroyItem()));
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
}
