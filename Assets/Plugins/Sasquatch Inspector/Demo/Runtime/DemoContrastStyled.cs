using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 8b — the same component as <see cref="DemoContrastStock"/>, field for field, with a designed
    /// layout applied. Put the two side by side and the pitch makes itself; no screenshot in the store listing
    /// argues as well as the buyer's own Inspector does.
    ///
    /// Fields are duplicated rather than inherited — see the note on <see cref="DemoContrastStock"/>.
    /// </summary>
    public class DemoContrastStyled : MonoBehaviour
    {
        public enum TurretMode { Idle, Tracking, Suppressing }

        public string turretId = "TUR-04";
        public TurretMode mode = TurretMode.Tracking;

        [Range(0f, 500f)] public float range = 180f;
        [Range(0f, 20f)] public float fireRate = 6.5f;
        [Range(0f, 1f)] public float accuracy = 0.86f;
        public float health = 640f;
        public float maxHealth = 800f;

        public Vector2 arcMinMax = new Vector2(-60f, 60f);
        public Vector3 muzzleOffset = new Vector3(0f, 1.2f, 0.6f);
        public Color beamColour = new Color(0.95f, 0.42f, 0.24f);
        public AnimationCurve spinUp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool overheats = true;
        [Range(0f, 30f)] public float coolDownSeconds = 4f;
    }
}
