using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Item")]
public class ItemSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] private int itemAmount;
    [SerializeField] private string itemDescription;
    [SerializeField] private Sprite itemSprite;
    public override void Collect(GameObject objectCollected)
    {
        InvManager manager = FindAnyObjectByType<InvManager>();
        if (manager != null)
        {
            manager.AddItem(itemName, itemAmount, itemSprite);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }
}