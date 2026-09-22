using System;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Dialogue;
using Unity.VectorGraphics;
using UnityEngine;

public class ShopKeeper : MonoBehaviour
{
    public List<ShopItem> shopItems = new();
    
    [SerializeField] private DialogueObject inStockDialogue;
    [SerializeField] private DialogueObject soldOutDialogue;
    
    public bool canRestock = true;
    private readonly List<ShopItem> backroomItems = new();
    
    [SerializeField] private List<ItemsToAddOnEvent> itemsToAddOnEvent = new();
    private NPChat npChat;
    private void Start()
    {
        backroomItems.AddRange(shopItems);
        npChat = GetComponent<NPChat>();
    }

    private void OnEnable()
    {
        GameEvents.OnEnterMap += AddItemsOnPlayerMapEnter;
    }
    private void OnDisable()
    {
        GameEvents.OnEnterMap -= AddItemsOnPlayerMapEnter;
    }

    /// <summary>
    /// me when "Can you check the back?"
    /// </summary>
    public void StartShop()
    {
        if (shopItems.Count == 0)
        {
            GameEvents.TriggerShowDialogue(soldOutDialogue, GetComponent<NPChat>().npc);
            if (canRestock && !IsInvoking(nameof(Restock))) Invoke(nameof(Restock), 3);
            return;
        }
        ShopManager.Instance.LoadShopItems(shopItems.ToArray(), this);
    }

    public void SaveShopItems(ShopItemSlot[] items)
    {
        shopItems.Clear();
        foreach (var item in items)
        {
            if (!item.usedSlot) continue;
            shopItems.Add(item.shopItem);
        }
    }

    public void AddShopItems(ShopItem[] item, GameObject keeper)
    {
        if (keeper != gameObject || item.Length == 0) return;
        backroomItems.Clear();
        shopItems.Clear();
        backroomItems.AddRange(item);
        shopItems.AddRange(item);
        print($"Added {item.Length} items to shopkeeper {gameObject.name}");
        GameEvents.TriggerPersistGameData();
    }
    
    /// <summary>
    /// me when "FIIINNNNEEEE ILL GO THEN >:("
    /// </summary>
    private void Restock()
    {
        shopItems.AddRange(backroomItems);
        GameEvents.TriggerAddDialogue(inStockDialogue, gameObject);
    }

    private void AddItemsOnPlayerMapEnter(string targetScene, bool firstTime)
    {
        if (!firstTime) return;
        foreach (var item in itemsToAddOnEvent.Where(i => i.type == ItemsToAddOnEvent.EventType.PlayerMapEnter && i.targetScene == targetScene))
        {
            foreach (var dialogue in item.dialoguesToAdd) npChat.AddDialogue(dialogue);
            AddShopItems(item.itemsToAdd, gameObject);
        }
    }
    
    [Serializable]
    public struct ItemsToAddOnEvent
    {
        public enum EventType { PlayerMapEnter /* More later*/}
        public EventType type;
        
        public SceneField targetScene;
        public bool firstTime;
        public DialogueObject[] dialoguesToAdd;
        public ShopItem[] itemsToAdd;
    }
}
