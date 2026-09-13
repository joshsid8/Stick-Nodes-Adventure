using UnityEngine;
[CreateAssetMenu(fileName = "NewTeleDoor", menuName = "TeleDoor")]
public class TeleDoor : ScriptableObject
{
    // This serves as an ID for lookup. That's it!
    // Bear in mind the SO object's name is the ID itself
    public SceneField sceneToLoad;
}