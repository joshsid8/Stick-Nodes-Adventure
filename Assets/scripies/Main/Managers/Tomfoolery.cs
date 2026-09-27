using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Dialogue;
using scripies.Player;
using UnityEngine;
using static UnityEngine.Random;

/// <summary>
/// This is Shenanigans' brother
/// If there is a function that's inconvenient to place, AS WELL as not being fit for Shenanigans (because of assembly references), add it here!
/// Keep in mind it that this derives from Main, unlike Shenanigans, that derives from Core,
/// Meaning scripts that arent in Main cant access this script... TODO: Could there be some way to explicitly give shenanigans access to this, and vice versa?
/// </summary>
public class Tomfoolery : MonoBehaviour
{
    public static Tomfoolery Instance {get; private set;}
    private Shenanigans shenanigans;
    private PlayerHealth player;
    
    [SerializeField] private List<ItemsOnEvent> itemsToAddOnEvent = new();


    private void OnEnable()
    {
        GameEvents.ToggleDoor += ToggleDoor;
        GameEvents.AddShopItems += AddShopItems;
        GameEvents.OnEnterMap += AddShopItemsOnPlayerMapEnter;
        GameEvents.OnDamageDealt += IsDamageTooHigh;
    }
    
    private void OnDisable()
    {
        GameEvents.ToggleDoor -= ToggleDoor;
        GameEvents.AddShopItems -= AddShopItems;
        GameEvents.OnEnterMap -= AddShopItemsOnPlayerMapEnter;
        GameEvents.OnDamageDealt -= IsDamageTooHigh;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        shenanigans = Shenanigans.Instance ?? FindAnyObjectByType<Shenanigans>();
        player = PlayerHealth.Instance ?? FindAnyObjectByType<PlayerHealth>();
    }
    
    public void ToggleDoor(string doorName, bool open) => StartCoroutine(ToggleDoorC(doorName, open));

    private static IEnumerator ToggleDoorC(string doorName, bool open)
    {
        GameObject door = null;

        while (door == null)
        {
            foreach (var am in FindObjectsByType<AccessManager>(FindObjectsSortMode.None))
            {
                if (!am.gameObject.name.Contains(doorName)) continue;
                door = am.gameObject;
                //print("found our instance");
                break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return null;
        }
        yield return new WaitForSeconds(0.1f);
        if (!door.TryGetComponent<AccessManager>(out var accessManager)) yield break;
        //print($"toggling door {doorName}: {open}");
        switch (open)
        {
            case true:
                accessManager.OpenDoors();
                break;
            case false:
                accessManager.CloseDoors();
                break;
        }
    }
    
    private void AddShopItems(ShopKeeperItem[] shopItems, NPC keeper) => StartCoroutine(AddShopItemsC(shopItems, keeper));

    private static IEnumerator AddShopItemsC(ShopKeeperItem[] shopItems, NPC keeper)
    {
        yield return new WaitUntil(() => DataManager.Instance.npcDataLoaded);
        add:
        foreach (var npc in DataManager.Instance.npcs)
        {
            if (!npc.activeNpcName.Contains(keeper.name)) continue;
            var newShopItems = npc.shopItems.ToList();
            newShopItems.AddRange(shopItems);

            GameObject activeNpc = null;
            foreach (var entity in ObjectDatabase.Instance.allEntities)
            {
                entity.TryGetComponent(out NPChat npcChat);
                if (npcChat == null || npcChat.npc != keeper) continue;
                activeNpc = entity.gameObject;
                break;
            }
            
            if (activeNpc != null)
            {
                //print($"NPC {keeper.name} is already active. adding items directly");
                activeNpc.TryGetComponent(out ShopKeeper shopKeeper);
                if (shopKeeper == null) print($"NPC {keeper.name} is not a shopkeeper tho");
                else shopKeeper.AddShopItems(newShopItems.ToArray(), activeNpc);
            }
            else npc.shopItems = newShopItems.ToArray();
            
            GameEvents.TriggerPersistGameData();
            //print($"Added {shopItems.Length} items to {keeper.name}");
            yield break;
        }
        
        print($"Could not find {keeper.name} in the data manager. We'll wait for an artifact of it to load...");
        yield return new WaitUntil(() => DataManager.Instance.npcs.Exists(x =>
            x.activeNpcName.Contains(keeper.name) ||
            ObjectDatabase.Instance.allEntities.Any(y => y.GetComponent<NPChat>().npc == keeper)));
        if (DataManager.Instance.npcs.Any(x => x.activeNpcName.Contains(keeper.name))) goto add;
        if (ObjectDatabase.Instance.allEntities.Any(y => y.GetComponent<NPChat>().npc == keeper))
        {
            GameEvents.TriggerPersistGameData();
            goto add;
        }
    }
    
    private void AddShopItemsOnPlayerMapEnter(string sceneName, bool firstTime)
    {
        foreach (var itemsOnEvent in itemsToAddOnEvent)
        {
            foreach (var item in itemsOnEvent.itemsToAdd.Where(i => i.targetScene.SceneName == sceneName && i.type == EventType.PlayerMapEnter))
            {
                if (!firstTime && item.requireFirstTime)
                {
                    //print($"Skipping {item.targetScene.SceneName} because it requires first time, and the player has entered {sceneName} before");
                    continue;
                }
                
                if (item.itemsToAdd.Length > 0)
                    AddShopItems(item.itemsToAdd, itemsOnEvent.targetNpc);
                if (item.dialogueToUse.Length > 0) 
                    GameEvents.TriggerChangeDialogue(item.dialogueToUse);
                //print($"{itemsOnEvent.targetNpc.name} has been updated because the player entered {sceneName} (for the first time!)");
            }
        }
    }

    private void IsDamageTooHigh(float damage, float threshold = 50)
    {
        if (damage > threshold) GameEvents.TriggerBoom(shenanigans.jumpScareClips[Range(0, shenanigans.jumpScareClips.Length)]);
    }
    
    public enum EventType { PlayerMapEnter /* More later*/}

    [Serializable]
    public struct ItemsOnEvent
    {
        public NPC targetNpc;
        public ItemsToAdd[] itemsToAdd;
    }
    
    [Serializable]
    public struct ItemsToAdd
    {
        public EventType type;
        [Tooltip("For PlayerMapEnter")] public SceneField targetScene;
        [Tooltip("For PlayerMapEnter")] public bool requireFirstTime;
        
        public ExternalDialogue[] dialogueToUse;
        public ShopKeeperItem[] itemsToAdd;
    }
}
