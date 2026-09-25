using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Collectable/Item")]
public class KeySO : CollectableSOBase
{
    public int itemAmount = 1;
    public int maxItemAmount = 99;
    
    public string doorToOpen; // Ill make this work later
    
    public override void Collect()
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(name, itemAmount, itemSprite, itemDescription, maxItemAmount, "Key", this);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }
}