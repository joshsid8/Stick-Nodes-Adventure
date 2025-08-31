using UnityEngine;
public class AbilitySlot : MonoBehaviour
{
    // Ability Scroll Data
    public GameObject abilityPrefab;
    public int abilityType;
    
    public void AddAbility(AbilitySO ability, int type)
    {
        abilityType = type;
        abilityPrefab = ability.abilityPrefab;
        PlayerHealth.Instance.gameObject.GetComponent<PlayerPerformAbility>().objects[abilityType] = abilityPrefab;
    }
    
}
