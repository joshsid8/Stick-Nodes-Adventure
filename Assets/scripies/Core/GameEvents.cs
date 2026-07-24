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
        
        // Player events
        public static Action<float> OnPlayerHealthChanged;
        public static Action OnPlayerDeath;
        public static Action<string, bool> PlayerAction;
        public static Action OnPlayerCounter;
        public static Action<float> AddPlayerEnergy;
        public static Action<bool> PlayerPerformAbility;
        public static Action<string, float, bool> AddPlayerTime;
        
        // Misc events
        public static Action<GameObject, bool> OnToggleDialogueTrigger;
        public static Action<ScriptableObject, NPC> ShowDialogue;
        public static Action<GameObject> DropLoot;
        public static Action<AudioClip> PlaySound;
        public static Action<float> StopTime;
        public static Action<Image, float, float> JumpScare;
        
        // Methods to trigger events
        public static void TriggerPlayerHealthChanged(float currentHealth) => OnPlayerHealthChanged?.Invoke(currentHealth);
        public static void TriggerPlayerDeath() => OnPlayerDeath?.Invoke();
        public static void TriggerPlayerAction(string action, bool play = false) => PlayerAction?.Invoke(action, play);
        public static void TriggerPlayerCounter() => OnPlayerCounter?.Invoke();
        public static void TriggerAddPlayerEnergy(float energy) => AddPlayerEnergy?.Invoke(energy);
        public static void TriggerPlayerPerformAbility(bool ability) => PlayerPerformAbility?.Invoke(ability);
        public static void TriggerAddPlayerTime(string variable = "Ability", float cooldown = 1f, bool additive = false) => AddPlayerTime?.Invoke(variable, cooldown, additive);
        
        public static void TriggerEnemyDefeated(GameObject enemy) => OnEnemyDefeated?.Invoke(enemy);
        public static void TriggerEnemySpawn(GameObject enemy) => OnEnemySpawned?.Invoke(enemy);
        
        public static void TriggerToggleDialogue(GameObject owner, bool state) => OnToggleDialogueTrigger?.Invoke(owner, state);
        public static void TriggerShowDialogue(ScriptableObject dialogue, NPC npc = null) => ShowDialogue?.Invoke(dialogue, npc);
        public static void TriggerDropLoot(GameObject loot) => DropLoot?.Invoke(loot);
        
        public static void TriggerSound(AudioClip clip = null) => PlaySound?.Invoke(clip);
        public static void TriggerStopTime(float time) => StopTime?.Invoke(time);
        public static void TriggerJumpScare(Image img = null, float duration = 0.08f, float fadeMult = 1.5f) => JumpScare?.Invoke(img, duration, fadeMult);
    }
}
