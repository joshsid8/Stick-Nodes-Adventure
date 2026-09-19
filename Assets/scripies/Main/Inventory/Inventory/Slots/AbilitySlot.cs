using System;
using scripies.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AbilitySlot : SlotBase, IPointerClickHandler
{
    // Ability Scroll Data
    public AbilitySO abilityStats;
    public int slotIndex;

    public Button abilitySlotButton;
    public Image abilitySlotShader;
    [SerializeField] private Image playerAbilitySlotImage;

    private void Update()
    {
        if (PlayerHealth.Instance == null || PlayerHealth.Instance.pa == null) return;

        if (abilityStats == null && PlayerHealth.Instance.pa.ability.objects[slotIndex] != null)
        {
            var autoItem = ResourceDatabase.Instance.GetItemByName(PlayerHealth.Instance.pa.ability.objects[slotIndex]?.abilityPrefab.name) as AbilitySO;
            if (autoItem == null)
            {
                Debug.LogError($"[AbilitySlot.Update] Dayum, we cant find {PlayerHealth.Instance.pa.ability.objects[slotIndex].name} as an abilitySO");
                return;
            }
            
            abilityStats = autoItem;
            itemName = autoItem.name;
            itemSprite = autoItem.itemSprite;
            itemDescription = autoItem.itemDescription;
            usedSlot = true;
            UpdateSlotUI();
        }

        var currentCooldown = PlayerHealth.Instance.pa.currentAbilityCooldowns[slotIndex];
        var maxCooldown = PlayerHealth.Instance.pa.maxAbilityCooldowns[slotIndex];
        
        if (abilitySlotShader != null)
            abilitySlotShader.fillAmount = maxCooldown > 0 ? currentCooldown / maxCooldown : 0;
        else if (maxCooldown <= 0) abilitySlotShader.fillAmount = 1;

        if (playerAbilitySlotImage != null && PlayerHealth.Instance.pa.ability.objects[slotIndex] != null)
        {
            if (PlayerHealth.Instance.pa.ability.objects[slotIndex].abilityPrefab.TryGetComponent<SpriteRenderer>(out var s))
                playerAbilitySlotImage.sprite = s.sprite;
            else
                playerAbilitySlotImage.sprite = PlayerHealth.Instance.pa.ability.objects[slotIndex]?.abilityPrefab
                    ?.GetComponentInChildren<SpriteRenderer>()?.sprite;
        }
        else if (PlayerHealth.Instance.pa.ability.objects[slotIndex] == null && abilityStats != null) 
            PlayerHealth.Instance.pa.ability.objects[slotIndex] = abilityStats.abilityObject;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    protected override void OnLeftClick()
    {
        base.OnLeftClick();
        if (useButtonText != null)
        {
            useButtonText.text = "Unequip";
            invManager.destroyButton.gameObject.SetActive(false);
        }
        if (!string.IsNullOrEmpty(itemName))
            invManager.useButton.onClick.AddListener(() => UnequipAbility(slotIndex));
    }

    protected override void UpdateSlotUI()
    {
        base.UpdateSlotUI();
        if (usedSlot && abilityStats != null && abilityStats.abilityObject != null &&
            abilityStats.abilityObject.abilityPrefab != null)
        {
            var prefab = abilityStats.abilityObject.abilityPrefab;
            var sr = prefab.GetComponent<Projectile>() ? prefab.GetComponent<SpriteRenderer>() 
                : prefab.GetComponentInChildren<SpriteRenderer>();

            if (sr != null)
            {
                if (abilitySlotButton != null)
                {
                    var parentImg = abilitySlotButton.GetComponent<Image>();
                    if (parentImg != null) parentImg.sprite = sr.sprite;
                }
            }
        }
        else
        {
            if (abilitySlotButton != null)
            {
                var img = abilitySlotButton.GetComponent<Image>();
                if (img != null) img.sprite = null;
            }

            if (playerAbilitySlotImage != null) playerAbilitySlotImage.sprite = null;
        }
    }

    public void AddAbility(string item, Sprite sprite, string itemDesc, AbilitySO ability, int slot = 0)
    {
        if (slotIndex != slot)
        {
            Debug.LogWarning($"Slot mismatch! {slotIndex} != {slot}");
            return;
        }

        itemName = item;
        itemSprite = sprite;
        itemDescription = itemDesc;
        abilityStats = ability;
        usedSlot = true;
        UpdateSlotUI();

        var playerAbility = PlayerHealth.Instance.pa;
        playerAbility.ability.objects[slot] = ability.abilityObject;

        if (ability.abilityObject != null && ability.abilityObject.abilityPrefab != null)
        {
            var prefabBase = ability.abilityObject.abilityPrefab.GetComponent<AbilityBase>() ??
                             ability.abilityObject.abilityPrefab.GetComponentInChildren<AbilityBase>();
            if (prefabBase != null) playerAbility.maxAbilityCooldowns[slot] = prefabBase.cooldown;
        }
    }

    public void UnequipAbility(int slot = 0)
    {
        if (slotIndex != slot)
        {
            Debug.LogWarning($"Slot mismatch! {slotIndex} != {slot}");
            return;
        }

        invManager.AddItem(itemName, 1, itemSprite, itemDescription, 16, "Ability", abilityStats);
        abilityStats = null;
        var playerAbility = PlayerHealth.Instance.pa;
        playerAbility.ability.objects[slot] = null;
        ClearSlot();
    }

    protected override void ClearSlotVisuals()
    {
        base.ClearSlotVisuals();
        if (playerAbilitySlotImage != null) playerAbilitySlotImage.sprite = null;
    }
}