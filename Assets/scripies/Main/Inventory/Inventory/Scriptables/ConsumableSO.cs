using UnityEngine;

[CreateAssetMenu(fileName = "NewConsumable", menuName = "Collectable/Consumable")]
public class ConsumableSO : CollectableSOBase
{
    public int itemAmount = 1;
    public int maxItemAmount = 99;
    
    public RestoreAttribute[] restoreAttributes;
    
    public override void Collect(GameObject objectCollected)
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(itemName, itemAmount, itemSprite, itemDescription, maxItemAmount, "Consumable", this);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }
}

[System.Serializable]
public class RestoreAttribute
{
    public string attributeName;
    public float restoreValue;
}