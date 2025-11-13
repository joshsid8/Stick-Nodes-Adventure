using UnityEngine;

public class Identifier : MonoBehaviour
{
    //These will be variables of what this object is, just for efficient queries and filters
    [Tooltip("This is for whenever you want to target a certain object externally. The ID format is currently 000 for enemies..")]
    public int id;
    public ObjectType typeOfObject;
    public enum ObjectType{Entity, Item, Door, Npc}
}
