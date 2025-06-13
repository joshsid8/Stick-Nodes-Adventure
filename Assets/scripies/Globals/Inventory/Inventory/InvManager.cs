using NUnit.Framework.Constraints;
using UnityEngine;

public class InvManager : MonoBehaviour
{
    public GameObject inventoryMenu;
    private bool isActivated;
    PlayerController player;
    public ItemSlot[] itemSlots;
    
    void Awake()
    {
        player = FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            Debug.LogWarning("InvManager: PlayerController not found in scene!");
        }

        if (inventoryMenu != null)
        {
            inventoryMenu.SetActive(false);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown("Inventory") && !isActivated)
        {
            inventoryMenu.SetActive(true);
            isActivated = true;
            player.canAct = false;
        }
        else if (Input.GetButtonDown("Inventory") && isActivated)
        {
            inventoryMenu.SetActive(false);
            isActivated = false;
            player.canAct = true;
        }
    }

    public void AddItem(string itemName, int itemAmount, Sprite itemSprite)
    {
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && itemSlots[i].itemName == itemName && !itemSlots[i].isFull)
            {
                itemSlots[i].AddItem(itemName, itemAmount, itemSprite);
                Debug.Log($"Added {itemAmount} to existing stack of {itemName} in slot {i}.");
                return;
            }
        }
        
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && !itemSlots[i].isFull)
            {
                Debug.Log($"Adding {itemName} to empty slot: {itemSlots[i].gameObject.name}");
                itemSlots[i].AddItem(itemName, itemAmount, itemSprite);
                return;
            }
        }
        Debug.LogWarning($"Inventory is full! Could not add {itemName}.");
    }
}
