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
    [SerializeField] private Image playerAbilitySlotImage;

    protected override void Awake()
    {
        base.Awake();

        if (abilitySlotButton != null)
        {
            var img = abilitySlotButton.gameObject.GetComponent<Image>();
            if (img != null) img.fillAmount = 1;
        }
    }

    private void Update()
    {
        var currentCooldown = PlayerHealth.Instance.pa.currentAbilityCooldowns[slotIndex];
        var maxCooldown = PlayerHealth.Instance.pa.maxAbilityCooldowns[slotIndex];

        var pab = PlayerHealth.Instance.pa.ability;
        SpriteRenderer sr = null;
        if (pab.objects[slotIndex] != null)
        {
            if (pab.objects[slotIndex].abilityPrefab.GetComponent<Projectile>())
                sr = pab.objects[slotIndex].abilityPrefab.GetComponent<SpriteRenderer>();
            else sr = pab.objects[slotIndex].abilityPrefab.GetComponentInChildren<SpriteRenderer>();
        }

        abilitySlotButton.GetComponentInParent<Image>().sprite = sr?.sprite;
        playerAbilitySlotImage.sprite = sr?.sprite;

        var img = abilitySlotButton.GetComponent<Image>();
        if (img != null) img.fillAmount = maxCooldown > 0 ? currentCooldown / maxCooldown : 0;

    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    protected override void OnLeftClick()
    {
        base.OnLeftClick();
        if (!string.IsNullOrEmpty(itemName))
        {
            invManager.useButton.onClick.AddListener(() => UnequipAbility(slotIndex));
        }
    }
    
    public void AddAbility(string item, Sprite sprite, string itemDesc, AbilitySO ability, int slot = 0)
    {
        if (slotIndex != slot) { Debug.LogWarning($"Slot mismatch! {slotIndex} != {slot}"); return; }
        
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
            var prefabBase = ability.abilityObject.abilityPrefab.GetComponent<AbilityBase>() ?? ability.abilityObject.abilityPrefab.GetComponentInChildren<AbilityBase>();
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
        invManager.AddItem(itemName, 1, itemSprite, itemDescription, 1, "Ability", null, null, abilityStats);
        abilityStats = null;
        var playerAbility = PlayerHealth.Instance.pa;
        playerAbility.ability.objects[slot] = null;
        
        ClearSlot();
    }
}
