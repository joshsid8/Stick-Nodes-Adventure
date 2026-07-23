using System;
using UnityEngine;

namespace scripies.Core
{
    /// <summary>
    /// A central hub for global game events to facilitate decoupling between assemblies.
    /// </summary>
    public static class GameEvents
    {
        //TODO: Will be tedious, but make all relevant scrips subscribe to these events
        //TODO: Add more relevant events to this class
        
        // Enemy events
        public static Action<GameObject> OnEnemySpawned;
        public static Action<GameObject> OnEnemyDefeated;
        
        // Player events
        public static Action<float> OnPlayerHealthChanged;
        public static Action OnPlayerDeath;
        public static Action<string> OnPlayerAction;
        
        // Methods to trigger events
        public static void TriggerEnemyDefeated(GameObject enemy) => OnEnemyDefeated?.Invoke(enemy);
        public static void TriggerEnemySpawn(GameObject enemy) => OnEnemySpawned?.Invoke(enemy);
        
        public static void TriggerPlayerHealthChanged(float currentHealth) => OnPlayerHealthChanged?.Invoke(currentHealth);
        public static void TriggerPlayerDeath() => OnPlayerDeath?.Invoke();
        public static void TriggerPlayerAction(string action) => OnPlayerAction?.Invoke(action);
    }
}
