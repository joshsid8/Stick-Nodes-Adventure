using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Item")]
public class ItemSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] private int itemAmount;
    private int maxItemAmount;
    [SerializeField] [TextArea] private string itemDescription;
    [SerializeField] private Sprite itemSprite;
    
    [SerializeField] private string itemType;
    [SerializeField] private float restoreValue;

    private PlayerManager stats;
    public override void Collect(GameObject objectCollected)
    {
        stats = FindAnyObjectByType<PlayerManager>();
        InvManager manager = FindAnyObjectByType<InvManager>();
        if (manager != null)
        {
            manager.AddItem(itemName, itemAmount, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }

}