using UnityEngine;
using UnityEngine.SceneManagement;

public class Identifier : MonoBehaviour
{
    //These will be variables of what this object is, just for efficient queries and filters
    [Tooltip("This is for whenever you want to target a certain object externally. The ID format is currently 000 for enemies..")]
    public int id;
    public int spawnId;
    public ObjectType typeOfObject;
    public enum ObjectType{Entity, Item, Door, Npc}
    
    [SerializeField] private MapInfo mapInfo;

    public void AutomateID(Scene targetScene)
    {
        if (mapInfo == null)
        {
            var maps = targetScene.GetRootGameObjects();
            foreach (var m in maps)
                if (m.name == "Map")
                {
                    mapInfo = m.GetComponent<MapInfo>(); 
                    break;
                }
        }
        var idn = 1;
        foreach (var eID in mapInfo.enemyIds) if (idn != eID) break; else idn++;

        for (var i = 0; i < mapInfo.enemyIds.Count; i++)
        {
            if (mapInfo.enemyIds[i] == 0)
            {
                mapInfo.enemyIds[i] = idn;
                spawnId = idn;
                return;
            }
        }

        mapInfo.enemyIds.Add(idn);
        spawnId = idn;
    }
}
