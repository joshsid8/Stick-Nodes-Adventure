using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 3b — object references that are worth looking at. Stock Unity renders both of these as an
    /// identical grey object slot; the designed layout turns them into an in-line editor and an asset preview,
    /// so the referenced weapon can be edited without ever leaving this component.
    ///
    /// Deliberately references only ScriptableObjects, never art: the exhibit's point is that a reference can
    /// be EDITED in place, which needs no sprite to demonstrate.
    /// </summary>
    public class DemoWeaponRack : MonoBehaviour
    {
        [Tooltip("Rendered as an in-line editor in the designed layout — expand it and edit the asset here.")]
        public DemoWeapon equipped;

        [Tooltip("Rendered as an expandable asset preview rather than a bare object slot.")]
        public DemoWeapon sidearm;

        [Range(0, 8)] public int freeSlots = 3;
        public bool lockedToPlayer = true;
    }
}
