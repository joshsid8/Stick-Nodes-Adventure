using UnityEngine;
using UnityEngine.Search;

[CreateAssetMenu(fileName = "NewShopItem", menuName = "ShopItem")]
public class ShopItem : ScriptableObject
{
    public string itemName;
    [TextArea(4, 10)] public string itemDescription;
    public Sprite itemSprite;
    [SearchContext("dir:Resources/Collectables")] public ScriptableObject itemCollectable;
    public BadgeSO badgeSoRequired;
}

[System.Serializable]
public class ShopKeeperItem
{
    public ShopItem shopItem;
    public int amount = 1;
    public float price;
    [TextArea(3, 10)]
    public string overwrittenDescription;
}