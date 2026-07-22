using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Identifier : MonoBehaviour
{
    //These will be variables of what this object is, just for efficient queries and filters 
    [Tooltip("This is for whenever you want to target a certain object externally. The ID format is currently 000 for enemies..")]
    public int id;
    public int spawnId;
    public ObjectType typeOfObject;
    public enum ObjectType{Entity, Item, Door, Npc, Spawner}
    
    [HideInInspector] public MapManager mapManager;

    [ContextMenu("Automate ID")]
    public void AutomateID() => AutomateID(gameObject.scene);
    
    public void AutomateID(Scene targetScene)
    {
        if (mapManager == null)
        {
            var rootObjects = targetScene.GetRootGameObjects();
            foreach (var obj in rootObjects)
            {
                var mm = obj.GetComponentInChildren<MapManager>(true);
                if (mm != null)
                {
                    mapManager = mm;
                    break;
                }
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
            
            var idn = 1;
            while (mapManager.enemyIds.Contains(idn)) idn++;
            
            mapManager.enemyIds.Add(idn);
            spawnId = idn;
        }
        else
        {
            if (id != 0) return;

            // Simple unique ID logic for non-entities: find max and increment
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
