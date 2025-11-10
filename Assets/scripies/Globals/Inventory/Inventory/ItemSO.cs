using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Collectable/Item")]
public class ItemSO : CollectableSOBase
{
    public string itemName;
    public int itemAmount = 1;
    public int maxItemAmount = 99;
    [TextArea] public string itemDescription;
    public Sprite itemSprite;
    
    public string itemType;
    public float restoreValue;
    
    public override void Collect(GameObject objectCollected)
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(itemName, itemAmount, itemSprite, itemDescription, maxItemAmount, itemType, this,null, null, 0);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }
}