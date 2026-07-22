using UnityEngine;
public class AbilitySlot : MonoBehaviour
{
    // Ability Scroll Data
    public AbilityObjSO abilityObj;
    
    public void AddAbility(AbilitySO ability)
    {
        abilityObj = ability.abilityObject;
        PlayerHealth.Instance.gameObject.GetComponent<PerformAbility>().objects[PlayerHealth.Instance.GetComponent<PlayerAbility>().blastIndex] = abilityObj;
    }
    
}
