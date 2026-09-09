using UnityEngine;

[CreateAssetMenu(fileName = "NewAspect", menuName = "Collectable/Aspect")]
public class AspectSO : CollectableSOBase
{
    public AspectProperty[] aspectProperties;
    
    public override void Collect(GameObject objectCollected)
    {
        var invManager = FindAnyObjectByType<InvManager>();
        if (invManager != null)
            invManager.AddItem(itemName, 1, itemSprite, SetDescription(), 16, "Aspect", this);
        else
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }
    
private string SetDescription()
{
    var desc = $"{itemDescription}\n\n---\n\n\n";

    foreach (var prop in aspectProperties)
    {
        var displayName = StatsManager.Instance.GetDisplayName(prop.statName);
        desc += displayName + " ";
        
        desc += prop.statValue > 0 ? $"+{prop.statValue}" : $"{prop.statValue}";
        if (prop != aspectProperties[^1]) desc += "\n";
    }
    return desc;
}
}

[System.Serializable]
public class AspectProperty
{
    public string statName;
    public float statValue;
}