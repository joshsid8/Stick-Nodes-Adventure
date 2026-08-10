using scripies.Player;
using UnityEngine;
using UnityEngine.UI;

public class AbilitySlot : MonoBehaviour
{
    // Ability Scroll Data
    public Button[] abilitySlotButtons;

    private void Start()
    {
        foreach (var button in abilitySlotButtons)
        {
            button.gameObject.GetComponent<Image>().fillAmount = 1;
        }
    }
    private void Update()
    {
        for (var i = 0; i < abilitySlotButtons.Length; i++)
        {
            if (i >= PlayerHealth.Instance.pa.currentAbilityCooldowns.Length) break;
            
            var currentCooldown = PlayerHealth.Instance.pa.currentAbilityCooldowns[i];
            var maxCooldown = PlayerHealth.Instance.pa.maxAbilityCooldowns[i];
            
            var img = abilitySlotButtons[i].GetComponent<Image>();
            if (img != null) img.fillAmount = maxCooldown > 0 ? currentCooldown / maxCooldown : 0;
        }
    }

    public void AddAbility(AbilitySO ability, int slot = 0)
    {
        var playerAbility = PlayerHealth.Instance.pa;
        playerAbility.ability.objects[slot] = ability.abilityObject;

        if (ability.abilityObject != null && ability.abilityObject.abilityPrefab != null)
        {
            var prefabBase = ability.abilityObject.abilityPrefab.GetComponent<AbilityBase>() ?? ability.abilityObject.abilityPrefab.GetComponentInChildren<AbilityBase>();
            if (prefabBase != null) playerAbility.maxAbilityCooldowns[slot] = prefabBase.cooldown;
        }
    }
}
