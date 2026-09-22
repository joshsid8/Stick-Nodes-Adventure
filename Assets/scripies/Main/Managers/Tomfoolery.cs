using System.Collections;
using System.Linq;
using scripies.Core;
using scripies.Dialogue;
using scripies.Player;
using UnityEngine;
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

    private void OnEnable()
    {
        GameEvents.ToggleDoor += ToggleDoor;
        GameEvents.AddShopItems += AddShopItems;
        GameEvents.OnEnterMap += AddShopItemsOnPlayerMapEnter;
    }
    
    private void OnDisable()
    {
        GameEvents.ToggleDoor -= ToggleDoor;
        GameEvents.AddShopItems -= AddShopItems;
        GameEvents.OnEnterMap -= AddShopItemsOnPlayerMapEnter;
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
    
    private void AddShopItems(ShopItem[] shopItems, NPC keeper) => StartCoroutine(AddShopItemsC(shopItems, keeper));

    private static IEnumerator AddShopItemsC(ShopItem[] shopItems, NPC keeper)
    {
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
    
    private void AddShopItemsOnPlayerMapEnter(string sceneName, bool firstTime) => StartCoroutine(AddShopItemsOnPlayerMapEnterC(sceneName, firstTime));
    private static IEnumerator AddShopItemsOnPlayerMapEnterC(string sceneName, bool firstTime)
    {
        // Not sure what to add here yet...
        yield return null;
    }
}
