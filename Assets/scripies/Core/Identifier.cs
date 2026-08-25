using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Junie went all out with this script. I need to figure out how this works lol
namespace scripies.Core
{
    public class Identifier : MonoBehaviour
    {
        //These will be variables of what this object is, just for efficient queries and filters 
        [Tooltip("This is for whenever you want to target a certain object externally. The ID format is currently 000 for enemies..")]
        public int id;
        public int spawnId;
        public ObjectType typeOfObject;
        public enum ObjectType{Entity, Item, Door, Npc, Spawner, Checkpoint}
        
        [HideInInspector] public MonoBehaviour mapManager;

        [ContextMenu("Automate ID")] // never used this before
        public void AutomateID() => AutomateID(gameObject.scene);
        
        public void AutomateID(Scene targetScene)
        {
            if (mapManager == null)
            {
                var rootObjects = targetScene.GetRootGameObjects();
                foreach (var obj in rootObjects)
                {
                    var components = obj.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (var c in components)
                    {
                        if (c != null && c.GetType().Name == "MapManager")
                        {
                            mapManager = c;
                            break;
                        }
                    }
                    if (mapManager != null) break;
                }
            }
            
            if (mapManager == null)
            {
                Debug.LogWarning($"MapManager not found in scene {targetScene.name} for {name}", this);
                return;
            }

            if (typeOfObject == ObjectType.Entity)
            {
                if (spawnId != 0) return;
                
                // Use reflection to access enemyIds if MapManager type is not resolved directly
                var mmType = mapManager.GetType();
                var enemyIdsField = mmType.GetField("enemyIds");
                if (enemyIdsField != null)
                {
                    var enemyIds = enemyIdsField.GetValue(mapManager) as List<int>;
                    if (enemyIds != null)
                    {
                        var idn = 1;
                        while (enemyIds.Contains(idn)) idn++;
                        enemyIds.Add(idn);
                        spawnId = idn;
                    }
                }
            }
            else
            {
                if (id != 0) return;

                int maxId = 0;
                var allIdentifiers = FindObjectsByType<Identifier>(FindObjectsSortMode.None);
                foreach (var ident in allIdentifiers)
                {
                    if (ident.gameObject.scene == targetScene && ident.typeOfObject != ObjectType.Entity && ident.id > maxId)
                        maxId = ident.id;
                }
                id = maxId + 1;
            }

    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                if (mapManager != null) UnityEditor.EditorUtility.SetDirty(mapManager);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
    #endif
        }
    }
}
