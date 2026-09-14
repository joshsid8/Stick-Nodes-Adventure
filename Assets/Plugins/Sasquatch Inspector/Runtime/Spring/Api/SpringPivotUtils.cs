using UnityEngine;

namespace SasquatchSpring
{
    /// <summary>Anchor point a scale/rotate animates around — corners, edges, center, or a custom local point.</summary>
    public enum SpringPivot
    {
        Center,
        Top, Bottom, Left, Right,
        TopLeft, TopRight, BottomLeft, BottomRight,
        Custom
    }

    /// <summary>Helpers for resolving a <see cref="SpringPivot"/> to a concrete pivot point for spring transforms.</summary>
    public static class SpringPivotUtils
    {
        /// <summary>Resolves <paramref name="pivot"/> to a local-space point on <paramref name="t"/> (or <paramref name="customPivot"/> for <see cref="SpringPivot.Custom"/>).</summary>
        public static Vector3 Resolve(Transform t, SpringPivot pivot, Vector3? customPivot = null)
        {
            if (pivot == SpringPivot.Custom && customPivot.HasValue)
            {
                return customPivot.Value;
            }

            Bounds b = GetHierarchicalBounds(t);

            Vector3 n = GetNormalizedVector(pivot);
            Vector3 worldPoint = new Vector3(
                Mathf.Lerp(b.min.x, b.max.x, n.x),
                Mathf.Lerp(b.min.y, b.max.y, n.y),
                Mathf.Lerp(b.min.z, b.max.z, n.z)
            );

            return t.InverseTransformPoint(worldPoint);
        }

        private static Bounds GetHierarchicalBounds(Transform t)
        {
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(t.position, Vector3.one);

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }

        private static Vector3 GetNormalizedVector(SpringPivot p)
        {
            switch (p)
            {
                case SpringPivot.Center: return new Vector3(0.5f, 0.5f, 0.5f);
                case SpringPivot.Top: return new Vector3(0.5f, 1.0f, 0.5f);
                case SpringPivot.Bottom: return new Vector3(0.5f, 0.0f, 0.5f);
                case SpringPivot.Left: return new Vector3(0.0f, 0.5f, 0.5f);
                case SpringPivot.Right: return new Vector3(1.0f, 0.5f, 0.5f);
                case SpringPivot.TopLeft: return new Vector3(0.0f, 1.0f, 0.5f);
                case SpringPivot.TopRight: return new Vector3(1.0f, 1.0f, 0.5f);
                case SpringPivot.BottomLeft: return new Vector3(0.0f, 0.0f, 0.5f);
                case SpringPivot.BottomRight: return new Vector3(1.0f, 0.0f, 0.5f);
                default: return new Vector3(0.5f, 0.5f, 0.5f);
            }
        }
    }
}
