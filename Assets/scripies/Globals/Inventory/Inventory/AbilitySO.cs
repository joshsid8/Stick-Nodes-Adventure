using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "Collectable/Ability")]
public class AbilitySO : CollectableSOBase
{
    [SerializeField] private string abilityName;
    [SerializeField] [TextArea] private string abilityDescription;
    [SerializeField] private Sprite abilitySprite;
    [SerializeField] private int abilityType;
    
    
    public GameObject abilityPrefab;
    public override void Collect(GameObject objectCollected)
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(abilityName, 1, abilitySprite, abilityDescription, 1, "Ability", null, null,this, abilityType);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add ability.");
    }
}