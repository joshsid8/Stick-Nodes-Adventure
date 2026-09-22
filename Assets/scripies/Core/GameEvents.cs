using System;
using UnityEngine;
using UnityEngine.UI;

namespace scripies.Core
{
    /// <summary>
    /// A central hub for global game events to facilitate decoupling between assemblies.
    /// Ensure, when calling methods, it starts with "Trigger"; otherwise you'll be calling a static Action.
    /// AKA: Antics, the father of Shenanigans and Tomfoolery
    /// </summary>
    public static class GameEvents
    {
        // Enemy events
        public static Action<GameObject> OnEnemySpawned;
        public static Action<GameObject> OnEnemyDefeated;
        public static Action<float> OnBossHealthChanged;
        public static Action OnBossDefeated;
        
        // Player events
        public static Action OnPlayerSpawned;
        public static Action<float> OnPlayerHealthChanged;
        public static Action OnPlayerDeath;
        public static Action<string, bool> PlayerAction;
        public static Action OnPlayerCounter;
        public static Action<float> AddPlayerEnergy;
        public static Action<bool> PlayerPerformAbility;
        public static Action<string, float, bool> AddPlayerTime;
        public static Action<float, bool, string> AddPlayerCombat;
        public static Action<ITarget, bool> OnAggressiveStatusChanged;
        public static Action<GameObject, bool> OnSpawnPointChanged;
        
        // GUI events
        public static Action OnEnableGUI;
        public static Action OnDisableGUI;
        
        // Dialogue/NPC events
        public static Action<GameObject, bool> OnToggleDialogueTrigger;
        public static Action<ScriptableObject, NPC> ShowDialogue;
        public static Action<ScriptableObject, GameObject> AddDialogue;
        public static Action<ScriptableObject, GameObject> RemoveDialogue;
        public static Action ForceCloseDialogue;
        public static Action<ScriptableObject> OnDialogueClose;
        public static Action<ExternalDialogue[]> ChangeDialogue;
        public static Action<ShopItem[], NPC> AddShopItems;
        
        // Misc events
        public static Action LoadMapData;
        public static Action LoadNpcData;
        public static Action SaveGameToDisk;
        public static Action PersistGameData;
        public static Action<string, bool> OnEnterMap;
        
        public static Action<AudioClip, GameObject, float, bool> PlaySound;
        public static Action<int, bool> PlayMusic;
        public static Action<bool> MuteMusic;
        
        public static Action<GameObject> DropLoot;
        public static Action OnCollect;
        public static Action<GameObject> OnObjectSpawn;
        public static Action<GameObject> OnObjectDestroyed;
        public static Action<BadgeSO> OnBadgeCollect;

        public static Action<float> StopTime;
        public static Action<Image, float, float> JumpScare;
        public static Action<string, bool> ToggleDoor;
        
        // Methods to trigger events
        public static void TriggerPlayerSpawn() => OnPlayerSpawned?.Invoke();
        public static void TriggerPlayerHealthChanged(float currentHealth) => OnPlayerHealthChanged?.Invoke(currentHealth);
        public static void TriggerPlayerDeath() => OnPlayerDeath?.Invoke();
        public static void TriggerPlayerAction(string action, bool play = false) => PlayerAction?.Invoke(action, play);
        public static void TriggerPlayerCounter() => OnPlayerCounter?.Invoke();
        public static void TriggerAddPlayerEnergy(float energy) => AddPlayerEnergy?.Invoke(energy);
        public static void TriggerPlayerPerformAbility(bool ability) => PlayerPerformAbility?.Invoke(ability);
        public static void TriggerAddPlayerTime(string variable = "Ability", float cooldown = 1f, bool additive = false) => AddPlayerTime?.Invoke(variable, cooldown, additive);
        public static void TriggerAddPlayerCombat(float time = 5, bool additive = true, string type = "") => AddPlayerCombat?.Invoke(time, additive, type);
        public static void TriggerAggressiveStatusChanged(ITarget target, bool isAggressive) => OnAggressiveStatusChanged?.Invoke(target, isAggressive);
        public static void TriggerSpawnPointChanged(GameObject point, bool spawn = false) => OnSpawnPointChanged?.Invoke(point, spawn);
        
        public static void TriggerEnemyDefeated(GameObject enemy) => OnEnemyDefeated?.Invoke(enemy);
        public static void TriggerEnemySpawn(GameObject enemy) => OnEnemySpawned?.Invoke(enemy);
        public static void TriggerBossDefeated() => OnBossDefeated?.Invoke();
        
        public static void TriggerEnableGUI() => OnEnableGUI?.Invoke();
        public static void TriggerDisableGUI() => OnDisableGUI?.Invoke();
        public static void TriggerToggleNpChat(GameObject owner, bool state) => OnToggleDialogueTrigger?.Invoke(owner, state);
        public static void TriggerShowDialogue(ScriptableObject dialogue, NPC npc = null) => ShowDialogue?.Invoke(dialogue, npc);
        public static void TriggerAddDialogue(ScriptableObject dialogue, GameObject npc) => AddDialogue?.Invoke(dialogue, npc);
        public static void TriggerRemoveDialogue(ScriptableObject dialogue, GameObject npc) => RemoveDialogue?.Invoke(dialogue, npc);
        public static void TriggerForceCloseDialogue() => ForceCloseDialogue?.Invoke();
        public static void TriggerOnDialogueClose(ScriptableObject dialogue) => OnDialogueClose?.Invoke(dialogue);
        public static void TriggerChangeDialogue(ExternalDialogue[] externalInstance) => ChangeDialogue?.Invoke(externalInstance);
        public static void TriggerAddShopItems(ShopItem[] items, NPC npc) => AddShopItems?.Invoke(items, npc);
        
        public static void TriggerLoadMapData() => LoadMapData?.Invoke();
        public static void TriggerLoadNpcData() => LoadNpcData?.Invoke();
        public static void TriggerSaveGameToDisk() => SaveGameToDisk?.Invoke();
        public static void TriggerPersistGameData()
        {
            //Debug.Log("Persisting game data");
            PersistGameData?.Invoke();
        }
        public static void TriggerOnEnterMap(string sceneName, bool firstTime) => OnEnterMap?.Invoke(sceneName, firstTime);
        
        public static void TriggerPlaySound(AudioClip clip = null, GameObject source = null, float volume = 1, bool randomPitch = true) => PlaySound?.Invoke(clip, source, volume, randomPitch);
        public static void TriggerMusic(int index, bool preFade = true) => PlayMusic?.Invoke(index, preFade);
        public static void TriggerMuteMusic(bool toggle = true) => MuteMusic?.Invoke(toggle);
        
        public static void TriggerDropLoot(GameObject loot) => DropLoot?.Invoke(loot);
        public static void TriggerOnCollect() => OnCollect?.Invoke();
        public static void TriggerObjectSpawn(GameObject obj = null) => OnObjectSpawn?.Invoke(obj);
        public static void TriggerObjectDestroy(GameObject obj) => OnObjectDestroyed?.Invoke(obj);

        public static void TriggerStopTime(float time) => StopTime?.Invoke(time);
        public static void TriggerJumpScare(Image img = null, float duration = 0.08f, float fadeMult = 1.5f) => JumpScare?.Invoke(img, duration, fadeMult);
        public static void TriggerToggleDoor(string doorName, bool state) => ToggleDoor?.Invoke(doorName, state);
    }
}