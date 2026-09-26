using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewAbility", menuName = "Collectable/Ability")]
public class AbilitySO : CollectableSOBase
{
    public AbilityObjSO abilityObject;
    
    public override void Collect()
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(name, 1, itemSprite, SetDescription(), 16, "Ability", this);
        else 
            Debug.LogError("InvManager not found in scene! Cannot add ability.");
    }

    public override string SetDescription()
    {
        return itemDescription;
    }
}