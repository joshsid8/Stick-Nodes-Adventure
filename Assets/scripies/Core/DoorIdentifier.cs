using UnityEngine;

namespace scripies.Core
{
    public class DoorIdentifier : MonoBehaviour
    {
        [HideInInspector] public string doorNameID;
        private void Awake() => doorNameID = transform.parent.gameObject.name;
    }
}