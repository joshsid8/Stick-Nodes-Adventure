using UnityEngine;
using UnityEngine.Search;

[CreateAssetMenu(fileName = "NewShopItem", menuName = "ShopItem")]
public class ShopItem : ScriptableObject
{
    public string itemName;
    [TextArea(4, 10)] public string itemDescription;
    public Sprite itemSprite;
    public float itemPrice;
    [SearchContext("dir:Resources/Collectables")] public ScriptableObject itemCollectable;
    public int itemAmount = 1;
    public BadgeSO badgeSoRequired;
}
