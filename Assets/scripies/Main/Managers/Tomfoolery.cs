using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using scripies.Core;
using scripies.Dialogue;
using scripies.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
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
    
    [FormerlySerializedAs("itemsToAddOnEvent")] 
    [SerializeField] private List<NpcItemsOnEvent> npcItemsToConfigureOnEvent = new();
    [SerializeField] private List<ItemsToConfigure> itemsToConfigure = new();


    private void OnEnable()
    {
        GameEvents.ToggleDoor += ToggleDoor;
        GameEvents.AddShopItems += AddShopItems;
        GameEvents.OnEnterMap += ConfigureNpcItemsOnMapEnter;
        GameEvents.OnBossDefeated += ConfigureOnBossDefeat;
        GameEvents.OnDialogueClose += ConfigureOnDialogueSpoken;
        GameEvents.OnDamageDealt += IsDamageTooHigh;
    }
    
    private void OnDisable()
    {
        GameEvents.ToggleDoor -= ToggleDoor;
        GameEvents.AddShopItems -= AddShopItems;
        GameEvents.OnEnterMap -= ConfigureNpcItemsOnMapEnter;
        GameEvents.OnBossDefeated -= ConfigureOnBossDefeat;
        GameEvents.OnDialogueClose -= ConfigureOnDialogueSpoken;
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
        if (keeper == null || shopItems == null) yield break;
        yield return new WaitUntil(() => DataManager.Instance != null && DataManager.Instance.npcDataLoaded);
        add:
        foreach (var npc in DataManager.Instance.npcs)
        {
            if (string.IsNullOrEmpty(npc.activeNpcName) || !npc.activeNpcName.Contains(keeper.name)) continue;
            var newShopItems = npc.shopItems != null ? npc.shopItems.ToList() : new List<ShopKeeperItem>();
            newShopItems.AddRange(shopItems);

            GameObject activeNpc = null;
            if (ObjectDatabase.Instance != null)
            {
                foreach (var entity in ObjectDatabase.Instance.allEntities)
                {
                    if (entity == null) continue;
                    entity.TryGetComponent(out NPChat npcChat);
                    if (npcChat == null || npcChat.npc != keeper) continue;
                    activeNpc = entity.gameObject;
                    break;
                }
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
        yield return new WaitUntil(() => DataManager.Instance != null && DataManager.Instance.npcs.Exists(x =>
            (!string.IsNullOrEmpty(x.activeNpcName) && x.activeNpcName.Contains(keeper.name)) ||
            (ObjectDatabase.Instance != null && ObjectDatabase.Instance.allEntities.Any(y => y != null && y.TryGetComponent<NPChat>(out var chat) && chat.npc == keeper))));
        if (DataManager.Instance.npcs.Any(x => !string.IsNullOrEmpty(x.activeNpcName) && x.activeNpcName.Contains(keeper.name))) goto add;
        if (ObjectDatabase.Instance != null && ObjectDatabase.Instance.allEntities.Any(y => y != null && y.TryGetComponent<NPChat>(out var chat) && chat.npc == keeper))
        {
            GameEvents.TriggerPersistGameData();
            goto add;
        }
    }

    private void ExecuteEventConfig(ItemsToConfigure item, NPC fallbackNpc = null)
    {
        var npc = item.targetNpc != null ? item.targetNpc : fallbackNpc;
        if (npc != null && item.itemsToAdd is { Length: > 0 })
            AddShopItems(item.itemsToAdd, npc);

        if (item.dialogueToUse is { Length: > 0 })
            GameEvents.TriggerChangeDialogue(item.dialogueToUse);

        if (item.objectsToToggle is { Length: > 0 })
            ToggleObjects(item.objectsToToggle);
    }

    private static bool MatchesBoss(string configuredName, string defeatedBossName)
    {
        if (string.IsNullOrEmpty(configuredName) || string.IsNullOrEmpty(defeatedBossName)) return false;
        return defeatedBossName.Contains(configuredName, StringComparison.OrdinalIgnoreCase) ||
               configuredName.Contains(defeatedBossName, StringComparison.OrdinalIgnoreCase);
    }

    private static void ToggleObjects(ObjectToToggle[] objectsToToggle)
    {
        foreach (var objectToToggle in objectsToToggle)
        {
            if (objectToToggle.obj != null)
            {
                objectToToggle.obj.SetActive(objectToToggle.toggle);
                continue;
            }

            if (string.IsNullOrEmpty(objectToToggle.objectName)) continue;
            var found = FindObjectAcrossLoadedScenes(objectToToggle.objectName);
            if (found != null) found.SetActive(objectToToggle.toggle);
        }
    }

    private static GameObject FindObjectAcrossLoadedScenes(string objectName)
    {
        for (var s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == objectName || t.name.Contains(objectName))
                        return t.gameObject;
                }
            }
        }
        return null;
    }

    private void ConfigureOnBossDefeat(string bossName)
    {
        foreach (var item in itemsToConfigure.Where(i => i.type == EventType.BossDefeated && MatchesBoss(i.bossName, bossName)))
        {
            ExecuteEventConfig(item);
        }

        foreach (var itemsOnEvent in npcItemsToConfigureOnEvent)
        {
            if (itemsOnEvent.itemsToConf == null) continue;
            foreach (var item in itemsOnEvent.itemsToConf.Where(i => i.type == EventType.BossDefeated && MatchesBoss(i.bossName, bossName)))
            {
                ExecuteEventConfig(item, itemsOnEvent.targetNpc);
            }
        }
    }
    
    private void ConfigureNpcItemsOnMapEnter(string sceneName, bool firstTime)
    {
        foreach (var itemsOnEvent in npcItemsToConfigureOnEvent)
        {
            if (itemsOnEvent.itemsToConf == null) continue;
            foreach (var item in itemsOnEvent.itemsToConf.Where(i => i.type == EventType.PlayerMapEnter && i.targetScene != null && i.targetScene.SceneName == sceneName))
            {
                if (!firstTime && item.requireFirstTime) continue;
                ExecuteEventConfig(item, itemsOnEvent.targetNpc);
            }
        }

        foreach (var item in itemsToConfigure.Where(i => i.type == EventType.PlayerMapEnter && i.targetScene != null && i.targetScene.SceneName == sceneName))
        {
            if (!firstTime && item.requireFirstTime) continue;
            ExecuteEventConfig(item);
        }
    }

    private void ConfigureOnDialogueSpoken(ScriptableObject dialogue, NPC npcWeDontWorryAbout)
    {
        var dia = dialogue as DialogueObject;
        if (dia == null) return;
        
        foreach (var item in itemsToConfigure.Where(i => i.type == EventType.OnDialogueSpoken && i.dialogueSpoken == dia))
        {
            ExecuteEventConfig(item);
        }

        foreach (var itemsOnEvent in npcItemsToConfigureOnEvent)
        {
            if (itemsOnEvent.itemsToConf == null) continue;
            foreach (var item in itemsToConfigure.Where(i => i.type == EventType.OnDialogueSpoken && i.dialogueSpoken == dia))
            {
                ExecuteEventConfig(item, itemsOnEvent.targetNpc);
            }
        }
    }

    private void IsDamageTooHigh(float damage, float threshold = 50)
    {
        if (damage > threshold) GameEvents.TriggerBoom(shenanigans.jumpScareClips[Range(0, shenanigans.jumpScareClips.Length)]);
    }
    
    public enum EventType { PlayerMapEnter, BossDefeated, OnDialogueSpoken }

    [Serializable]
    public struct NpcItemsOnEvent
    {
        public NPC targetNpc;
        [FormerlySerializedAs("itemsToAdd")] public ItemsToConfigure[] itemsToConf;
    }
    
    [Serializable]
    public struct ItemsToConfigure
    {
        public EventType type;
        [Tooltip("For OnMapEnter for now, but others later")] public bool requireFirstTime;
        [Header("For OnMapEnter")] public SceneField targetScene;
        [Header("For BossDefeated")] public string bossName;
        [Header("For DialogueSpoken")] public DialogueObject dialogueSpoken;
        
        [Header("NPC & Shop Actions (Optional)")]
        public NPC targetNpc;
        public ExternalDialogue[] dialogueToUse;
        public ShopKeeperItem[] itemsToAdd;

        [Header("World Actions (Optional)")]
        public ObjectToToggle[] objectsToToggle;
    }
    
    [Serializable]
    public struct ObjectToToggle
    {
        public GameObject obj;
        [Tooltip("Used if the object is in another scene and cannot be dragged into Persistent Objects")]
        public string objectName;
        public bool toggle;
    }
}
