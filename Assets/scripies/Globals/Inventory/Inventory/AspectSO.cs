using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Aspect")]
public class AspectSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] [TextArea] private string itemDescription;
    [SerializeField] private Sprite itemSprite;

    [SerializeField] private string[] floatStatsName;
    [SerializeField] private string[] floatStats;
    
    private PlayerManager stats;
    public override void Collect(GameObject objectCollected)
    {
        stats = FindAnyObjectByType<PlayerManager>();
        InvManager manager = FindAnyObjectByType<InvManager>();
        if (manager != null)
        {
            manager.AddAspect(itemName, itemSprite, itemDescription, this);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }

}