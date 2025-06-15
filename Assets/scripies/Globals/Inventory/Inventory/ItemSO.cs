using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Collectable/Item")] // Changed filename and menuname for better clarity
public class ItemSO : CollectableSOBase
{
    [SerializeField] private string itemName;
    [SerializeField] private int itemAmount = 1; // Default to 1
    [SerializeField] private int maxItemAmount = 99; // Added maxItemAmount here
    [SerializeField] [TextArea] private string itemDescription;
    [SerializeField] private Sprite itemSprite;
    
    [SerializeField] private string itemType; // e.g., "Health", "Energy", "Consumable"
    [SerializeField] private float restoreValue; // Value restored if consumable

    // ScriptableObjects should not hold direct references to scene objects or use FindAnyObjectByType in Awake/Start.
    // They are assets. The manager should be found when Collect is called.
    
    public override void Collect(GameObject objectCollected)
    {
        InvManager manager = FindAnyObjectByType<InvManager>();
        if (manager != null)
        {
            // Call the InvManager's AddItem with ALL parameters
            manager.AddItem(itemName, itemAmount, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue, null);
        }
        else
        {
            Debug.LogWarning("InvManager not found in scene! Cannot add item.");
        }
    }
}