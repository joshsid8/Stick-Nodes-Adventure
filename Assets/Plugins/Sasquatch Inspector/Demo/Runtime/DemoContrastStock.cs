using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 8a — the control. Identical fields to <see cref="DemoContrastStyled"/> and
    /// <see cref="DemoContrastLegacy"/>, deliberately left with NO layout so the buyer sees Unity's default
    /// rendering right next to the designed one.
    ///
    /// The three contrast components duplicate their fields instead of sharing a base class on purpose: a
    /// shared base would let Apply To Subclasses push the styled layout onto this one, and the control would
    /// stop being a control.
    /// </summary>
    public class DemoContrastStock : MonoBehaviour
    {
        public enum TurretMode { Idle, Tracking, Suppressing }

        public string turretId = "TUR-04";
        public TurretMode mode = TurretMode.Tracking;

        [Range(0f, 500f)] public float range = 180f;
        [Range(0f, 20f)] public float fireRate = 6.5f;
        [Range(0f, 1f)] public float accuracy = 0.86f;
        public float health = 640f;

        [Tooltip("the range of the arc")]
        public Vector2 arcMinMax = new Vector2(-60f, 60f);
        public Vector3 muzzleOffset = new Vector3(0f, 1.2f, 0.6f);
        public Color beamColour = new Color(0.95f, 0.42f, 0.24f);
        public AnimationCurve spinUp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool overheats = true;
        [Range(0f, 30f)] public float coolDownSeconds = 4f;
    }
}
