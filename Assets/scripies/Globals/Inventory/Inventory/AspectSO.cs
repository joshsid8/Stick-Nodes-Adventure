using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Aspect")]
public class AspectSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] [TextArea] private string itemDescription;
    [SerializeField] private Sprite itemSprite;

    public string[] floatStatsName;
    public float[] floatStats;
    
    private PlayerManager stats;
    public override void Collect(GameObject objectCollected)
    {
        stats = FindAnyObjectByType<PlayerManager>();
        InvManager manager = FindAnyObjectByType<InvManager>();
        if (manager != null)
        {
            manager.AddItem(itemName, 1, itemSprite, itemDescription, 1, "Aspect", 0, this);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }

}