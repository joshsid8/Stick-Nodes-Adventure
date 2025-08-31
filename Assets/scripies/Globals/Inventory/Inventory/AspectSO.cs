using UnityEngine;

[CreateAssetMenu(fileName = "NewAspect", menuName = "Collectable/Aspect")]
public class AspectSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] [TextArea] private string itemDescription;
    [SerializeField] private Sprite itemSprite;

    public string[] floatStatsName;
    public float[] floatStats;
    
    public override void Collect(GameObject objectCollected)
    {
        InvManager invManager = FindAnyObjectByType<InvManager>();
        if (invManager != null)
        {
            invManager.AddItem(itemName, 1, itemSprite, itemDescription, 1, "Aspect", 0, this, null);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }

}