using UnityEngine;

[CreateAssetMenu(fileName = "NewAspect", menuName = "Collectable/Aspect")]
public class AspectSO : CollectableSOBase
{
    public AspectProperty[] aspectProperties;
    
    public override void Collect()
    {
        var invManager = FindAnyObjectByType<InvManager>();
        if (invManager != null)
            invManager.AddItem(name, 1, itemSprite, SetDescription(), 16, "Aspect", this);
        else
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
    }
    
    private string SetDescription()
    {
        var desc = $"{itemDescription}\n\n---\n\n";
        var completed = false;
        foreach (var prop in aspectProperties)
        {
            var displayName = StatsManager.Instance.GetDisplayName(prop.statName);
            desc += displayName + " ";
            
            desc += prop.statValue > 0 ? $"+{prop.statValue}" : $"{prop.statValue}";
            if (prop != aspectProperties[^1]) desc += "\n";
            completed = true;
        }
        if (!completed) desc += "None";
        return desc;
    }
}

[System.Serializable]
public class AspectProperty
{
    public string statName;
    public float statValue;
}