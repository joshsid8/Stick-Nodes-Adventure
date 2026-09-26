using UnityEngine;

[CreateAssetMenu(fileName = "NewConsumable", menuName = "Collectable/Consumable")]
public class ConsumableSO : CollectableSOBase
{
    public int itemAmount = 1;
    public int maxItemAmount = 99;
    // TODO: Make below ensure the player will have to wait before using a consumable again
    public int restoreCooldown = 5;
    public RestoreAttribute[] restoreAttributes;
    
    public override void Collect()
    {
        if (InvManager.Instance != null)
            InvManager.Instance.AddItem(name, itemAmount, itemSprite, SetDescription(), maxItemAmount, "Consumable", this);
        else 
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }

    public override string SetDescription()
    {
        var desc = $"{itemDescription}\n\n---\n\n\n";
    
        foreach (var att in restoreAttributes)
        {
            desc += att.restoreValue > 0 ? $"+{att.restoreValue}" : $"{att.restoreValue}";
            desc += $" {StatsManager.Instance.GetDisplayName(att.attributeName) ?? att.attributeName}";
            if (att != restoreAttributes[^1]) desc += "\n";
        }
        return desc;
    }
}

[System.Serializable]
public class RestoreAttribute
{
    public string attributeName;
    public float restoreValue;
}