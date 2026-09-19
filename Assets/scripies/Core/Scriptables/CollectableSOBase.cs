using UnityEngine;

public abstract class CollectableSOBase : ScriptableObject
{
    public string itemName;
    [TextArea] public string itemDescription;
    public Sprite itemSprite;
    public abstract void Collect();
}
