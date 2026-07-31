using System;
using UnityEngine;
using UnityEngine.UI;

namespace scripies.Core
{
    /// <summary>
    /// A central hub for global game events to facilitate decoupling between assemblies.
    /// </summary>
    public static class GameEvents
    {
        // Enemy events
        public static Action<GameObject> OnEnemySpawned;
        public static Action<GameObject> OnEnemyDefeated;
        public static Action<bool, GameObject> OnBossAttack;
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
        
        // GUI events
        public static Action OnEnableGUI;
        public static Action OnDisableGUI;
        public static Action<GameObject, bool> OnToggleDialogueTrigger;
        public static Action<ScriptableObject, NPC> ShowDialogue;
        public static Action OnDialogueClose;
        
        // Misc events
        public static Action<AudioClip, bool> PlaySound;
        public static Action<int, bool> PlayMusic;
        
        public static Action<GameObject> DropLoot;
        public static Action<float> StopTime;
        public static Action<Image, float, float> JumpScare;
        
        // Methods to trigger events
        public static void TriggerPlayerSpawn() => OnPlayerSpawned?.Invoke();
        public static void TriggerPlayerHealthChanged(float currentHealth) => OnPlayerHealthChanged?.Invoke(currentHealth);
        public static void TriggerPlayerDeath() => OnPlayerDeath?.Invoke();
        public static void TriggerPlayerAction(string action, bool play = false) => PlayerAction?.Invoke(action, play);
        public static void TriggerPlayerCounter() => OnPlayerCounter?.Invoke();
        public static void TriggerAddPlayerEnergy(float energy) => AddPlayerEnergy?.Invoke(energy);
        public static void TriggerPlayerPerformAbility(bool ability) => PlayerPerformAbility?.Invoke(ability);
        public static void TriggerAddPlayerTime(string variable = "Ability", float cooldown = 1f, bool additive = false) => AddPlayerTime?.Invoke(variable, cooldown, additive);
        public static void TriggerAddPlayerCombat(float damage, bool additive, string type) => AddPlayerCombat?.Invoke(damage, additive, type);
        public static void TriggerAggressiveStatusChanged(ITarget target, bool isAggressive) => OnAggressiveStatusChanged?.Invoke(target, isAggressive);
        
        public static void TriggerEnemyDefeated(GameObject enemy) => OnEnemyDefeated?.Invoke(enemy);
        public static void TriggerEnemySpawn(GameObject enemy) => OnEnemySpawned?.Invoke(enemy);
        public static void TriggerBossAttack(bool toggle, GameObject boss) => OnBossAttack?.Invoke(toggle, boss);
        public static void TriggerBossDefeated() => OnBossDefeated?.Invoke();
        
        public static void TriggerEnableGUI() => OnEnableGUI?.Invoke();
        public static void TriggerDisableGUI() => OnDisableGUI?.Invoke();
        public static void TriggerToggleDialogue(GameObject owner, bool state) => OnToggleDialogueTrigger?.Invoke(owner, state);
        public static void TriggerShowDialogue(ScriptableObject dialogue, NPC npc = null) => ShowDialogue?.Invoke(dialogue, npc);
        public static void TriggerDialogueClose() => OnDialogueClose?.Invoke();
        
        public static void TriggerSound(AudioClip clip = null, bool randomPitch = true) => PlaySound?.Invoke(clip, randomPitch);
        public static void TriggerMusic(int index, bool preFade = true) => PlayMusic?.Invoke(index, preFade);
        
        public static void TriggerDropLoot(GameObject loot) => DropLoot?.Invoke(loot);
        public static void TriggerStopTime(float time) => StopTime?.Invoke(time);
        public static void TriggerJumpScare(Image img = null, float duration = 0.08f, float fadeMult = 1.5f) => JumpScare?.Invoke(img, duration, fadeMult);
    }
}
