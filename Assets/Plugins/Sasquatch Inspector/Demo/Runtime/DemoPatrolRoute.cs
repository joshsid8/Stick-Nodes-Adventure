using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 10 — "Still need PART of your [CustomEditor]?"
    ///
    /// The other half of the interop story. Displacing a whole custom editor (see <see cref="DemoLegacyTurret"/>)
    /// is the right answer when the editor was only ever grouping and labelling fields. It is the WRONG answer
    /// when the editor does something no layout could: here, a top-down pad you can drag waypoints around on,
    /// with a simulated guard walking the route so every field below is visibly driving something.
    ///
    /// So this component's editor marks that one piece with <c>[SasquatchEmbed]</c>, and the designed layout
    /// pulls it back in through an Embedded Editor node — fully interactive, not a screenshot of itself. The
    /// buyer keeps the bespoke tool and designs everything around it, rather than choosing between the two.
    /// </summary>
    public class DemoPatrolRoute : MonoBehaviour
    {
        public enum RouteMode { Loop, PingPong, Once }

        public string routeName = "Cavern Sweep";
        public RouteMode mode = RouteMode.Loop;

        [Tooltip("Edited by dragging on the pad this component's custom editor draws — the piece worth keeping.")]
        public List<Vector2> waypoints = new List<Vector2>
        {
            new Vector2(-4f, -3f),
            new Vector2( 2f, -4f),
            new Vector2( 5f,  1f),
            new Vector2( 1f,  4f),
            new Vector2(-3f,  2f),
        };

        [Range(0f, 12f)] public float moveSpeed = 3.2f;
        [Range(0f, 10f)] public float waitAtWaypoint = 1.5f;
        [Range(0f, 90f)] public float turnRateDegrees = 42f;

        public bool alertOnSight = true;
        [Range(0f, 40f)] public float sightRange = 14f;
        [Range(0f, 180f)] public float sightConeDegrees = 75f;

        public Color routeTint = new Color(0.36f, 0.66f, 0.98f);
        public bool drawGizmos = true;
    }
}
