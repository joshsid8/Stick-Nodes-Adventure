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

    //an overload, something to learn more about
    public void AutomateID() => AutomateID(gameObject.scene);
    
    public void AutomateID(Scene targetScene)
    {
        if (mapManager == null)
        {
            var maps = targetScene.GetRootGameObjects();
            foreach (var m in maps)
                if (m.name == "Map")
                {
                    mapManager = m.GetComponent<MapManager>(); 
                    break;
                }
        }
        var idn = 1;
        foreach (var eID in mapManager.enemyIds) if (idn != eID) break; else idn++;

        for (var i = 0; i < mapManager.enemyIds.Count; i++)
        {
            if (mapManager.enemyIds[i] == 0)
            {
                mapManager.enemyIds[i] = idn;
                spawnId = idn;
                return;
            }
        }

        mapManager.enemyIds.Add(idn);
        spawnId = idn;
    }
}
