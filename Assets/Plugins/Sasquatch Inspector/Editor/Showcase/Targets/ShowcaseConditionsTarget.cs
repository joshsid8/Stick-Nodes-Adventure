using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // Drives the "Condition Debugger" demo. Values are seeded so the dependent fields START hidden/disabled,
    // making the debug overlays visible the moment you turn on "Debug Conditions".
    //
    // CanFire is a computed bool used as a condition DRIVER with no serialized field behind it — the
    // ShowIf("IsValid") pattern. It starts false because hasAmmo does.
    //
    // Kept off the fields themselves: a comment directly above a member is lifted verbatim into the
    // sandbox's "C# Source" pane (ShowcaseSource.ExtendBackward), which shows bare declarations.
    internal class ShowcaseConditionsTarget : ScriptableObject
    {
        public enum WeaponMode { Melee, Ranged, Magic }

        public bool isActive = true;
        public bool hasAmmo = false;
        public WeaponMode mode = WeaponMode.Melee;

        public int ammoCount = 0;
        public float spread = 0.25f;
        public int chargeLevel = 3;
        public string reloadNote = "Out of ammo.";

        public bool CanFire => isActive && hasAmmo && ammoCount > 0;
    }
}
