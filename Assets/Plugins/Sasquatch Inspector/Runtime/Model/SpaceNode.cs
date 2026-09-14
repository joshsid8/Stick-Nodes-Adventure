using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>Which direction a <see cref="SpaceNode"/> occupies. Vertical inserts a gap between stacked
    /// (Rows) items; Horizontal inserts a gap between side-by-side (Columns/Grid) items.</summary>
    public enum SpaceAxis
    {
        Vertical,
        Horizontal
    }

    /// <summary>
    /// A pure decorative spacer that inserts a blank gap into a layout — a first-class alternative to nudging
    /// a neighbour's padding/margins. Size it two ways: drag the grip on its free edge, or type an exact pixel
    /// amount in the designer. <see cref="Axis"/> picks the direction (the grip follows it: bottom edge for
    /// Vertical, right edge for Horizontal). <see cref="Flexible"/> switches it from a fixed px size to a
    /// flex-grow spacer that fills all leftover room / pushes siblings apart (like <c>GUILayout.FlexibleSpace</c>).
    ///
    /// Deliberately a plain leaf (not a <see cref="ContainerNode"/>, and it never sets
    /// <see cref="LayoutNode.MakeHeightResizable"/>) so its size is applied as a literal fixed height/width in
    /// the builder rather than being routed to a grow-only <c>min-height</c> floor by StyleApplier.
    /// </summary>
    [Serializable]
    public class SpaceNode : LayoutNode
    {
        /// <summary>Clamp bounds for a fixed-size space, shared by the setter, the drag handle and the builder.</summary>
        public const float MinSpace = 0f;
        public const float MaxSpace = 800f;

        [SerializeField] private float spaceAmount = 12f;
        [SerializeField] private SpaceAxis axis = SpaceAxis.Vertical;
        [SerializeField] private bool flexible = false;
        [SerializeField] private string ownerFieldPath = "";

        /// <summary>Fixed size of the gap in pixels (used when <see cref="Flexible"/> is off). Clamped to
        /// [<see cref="MinSpace"/>..<see cref="MaxSpace"/>].</summary>
        public float SpaceAmount
        {
            get => spaceAmount;
            set => spaceAmount = Mathf.Clamp(value, MinSpace, MaxSpace);
        }

        /// <summary>Direction the space occupies (Vertical = height, Horizontal = width).</summary>
        public SpaceAxis Axis
        {
            get => axis;
            set => axis = value;
        }

        /// <summary>When true, ignore <see cref="SpaceAmount"/> and grow to fill the parent's leftover room
        /// along its main axis (flex-grow), pushing siblings apart.</summary>
        public bool Flexible
        {
            get => flexible;
            set => flexible = value;
        }

        /// <summary>
        /// Non-empty ⇒ this spacer MIRRORS a code <c>[Space]</c> attribute: it was imported for the field at
        /// this resolved path and is "owned" by that attribute. Owned spacers are locked in the designer (a
        /// direct delete is blocked with a "remove the attribute instead" prompt) and are auto-managed by the
        /// sync engine — created when the attribute appears, removed when it (or its field) goes away. Empty ⇒
        /// a free, hand-authored designer space with no code association (fully user-controlled).
        /// </summary>
        public string OwnerFieldPath
        {
            get => ownerFieldPath;
            set => ownerFieldPath = value ?? "";
        }

        /// <summary>True when this spacer mirrors a code <c>[Space]</c> attribute (see <see cref="OwnerFieldPath"/>).</summary>
        public bool IsAttributeOwned => !string.IsNullOrEmpty(ownerFieldPath);
    }
}
