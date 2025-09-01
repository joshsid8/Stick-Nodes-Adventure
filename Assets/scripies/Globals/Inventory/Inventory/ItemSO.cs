using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Collectable/Item")]
public class ItemSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] private int itemAmount = 1;
    [SerializeField] private int maxItemAmount = 99;
    [SerializeField] [TextArea] private string itemDescription;
    [SerializeField] private Sprite itemSprite;
    
    [SerializeField] private string itemType;
    [SerializeField] private float restoreValue;
    
    public override void Collect(GameObject objectCollected)
    {
        if (InvManager.Instance != null)
        {
            // Call the InvManager's AddItem with ALL parameters
            InvManager.Instance.AddItem(itemName, itemAmount, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue, null, null, 0);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }
}