using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewAbility", menuName = "Collectable/Ability")]
public class AbilitySO : CollectableSOBase
{
    public int abilityType;
    public GameObject abilityPrefab;
    
    public override void Collect(GameObject objectCollected)
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(itemName, 1, itemSprite, itemDescription, 1, "Ability", null, null,this, abilityType);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add ability.");
    }
}