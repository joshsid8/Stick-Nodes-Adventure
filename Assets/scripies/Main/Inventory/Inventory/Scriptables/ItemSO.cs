using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Collectable/Item")]
public class ItemSO : CollectableSOBase
{
    public int itemAmount = 1;
    public int maxItemAmount = 99;
    
    public string itemType;
    
    public override void Collect()
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(name, itemAmount, itemSprite, SetDescription(), maxItemAmount, itemType, this);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }

    public override string SetDescription()
    {
        return itemDescription;
    }
}