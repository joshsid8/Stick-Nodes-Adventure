using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 9 — "Already has a [CustomEditor]".
    ///
    /// The first half of the interop story, and the question every buyer with an existing project asks: what
    /// happens to the components that already draw their own inspector? This one ships WITH a hand-written
    /// <c>[CustomEditor]</c> (DemoLegacyTurretInspector), and that editor's own banner tells the buyer the
    /// answer — turn on Force Takeover and Sasquatch displaces it.
    ///
    /// Its sibling <see cref="DemoPatrolRoute"/> answers the other half: keeping PART of a custom editor.
    /// </summary>
    public class DemoLegacyTurret : MonoBehaviour
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

        public bool overheats = true;
        [Range(0f, 30f)] public float coolDownSeconds = 4f;
    }
}
