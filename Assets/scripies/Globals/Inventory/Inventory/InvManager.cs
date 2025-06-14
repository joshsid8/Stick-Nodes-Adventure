using UnityEngine;
using UnityEngine.UI;

public class InvManager : MonoBehaviour
{
    public GameObject inventoryMenu;
    private bool isActivated;
    PlayerController player;
    public ItemSlot[] itemSlots;

    public Button useButton;
    public Button destroyButton;
    
    public Button skillTreeButton;
    
    public PlayerManager playerManager;
    void Awake()
    {
        player = FindAnyObjectByType<PlayerController>();
        playerManager = FindAnyObjectByType<PlayerManager>();
        if (player == null)
        {
            Debug.LogWarning("InvManager: where is bro");
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

    public void AddItem(string itemName, int itemAmount, Sprite itemSprite, string itemDescription, int maxItemAmount, string itemType, float restoreValue)
    {
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && itemSlots[i].itemName == itemName && itemSlots[i].itemAmount < itemSlots[i].maxItemAmount)
            {
                int amountToAdd = Mathf.Min(itemAmount, itemSlots[i].maxItemAmount - itemSlots[i].itemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue);
                itemAmount -= amountToAdd;
                if (itemAmount <= 0) return;
            }
        }
        
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null && !itemSlots[i].usedSlot)
            {
                int amountToAdd = Mathf.Min(itemAmount, maxItemAmount);
                itemSlots[i].AddItem(itemName, amountToAdd, itemSprite, itemDescription, maxItemAmount, itemType, restoreValue);
                itemAmount -= amountToAdd;
                if (itemAmount <= 0) return;
            }
        }

        if (itemAmount > 0)
        {
            Debug.LogWarning($"Inventory is full! Could not add {itemAmount} of {itemName}.");
        }
    }

    public void SelectSlot()
    {
        useButton.onClick.RemoveAllListeners();
        destroyButton.onClick.RemoveAllListeners();
        useButton.gameObject.SetActive(true);
        destroyButton.gameObject.SetActive(true);
    }

    public void DeselectSlots()
    {
        for (int i = 0; i < itemSlots.Length; i++)
        {
            itemSlots[i].selectedShader.SetActive(false);
            itemSlots[i].itemSelected = false;
            useButton.gameObject.SetActive(false);
            destroyButton.gameObject.SetActive(false);
        }
    }
}
