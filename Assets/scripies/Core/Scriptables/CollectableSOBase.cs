using UnityEngine;

public abstract class CollectableSOBase : ScriptableObject
{
    [TextArea] public string itemDescription;
    public Sprite itemSprite;
    public abstract void Collect();
    public abstract string SetDescription();
}
