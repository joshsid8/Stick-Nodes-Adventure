using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Abstract base for every node in a Sasquatch Inspector inspector layout tree (bindings, containers, collections,
    /// buttons, help boxes…). Holds the cross-cutting tooltip, style, visibility-condition, animation and
    /// resize/track state shared by all node kinds.
    /// </summary>
    [Serializable]
    public abstract class LayoutNode
    {
        /// <summary>
        /// Sentinel stored in an overridable text field (<see cref="Tooltip"/>, a binding's Label or Header)
        /// to mean "explicitly blank — render nothing, and do NOT fall back to the value declared in code".
        /// It is a deliberately unlikely string (wrapped in zero-width spaces, U+200B) so it never collides
        /// with real user text. An empty string still means "unset → use the code value"; this means "hidden".
        /// </summary>
        public static readonly string HiddenSentinel =
            (char)0x200B + "__SASQUATCH_HIDDEN__" + (char)0x200B;

        [SerializeField] private string styleToken;
        [SerializeField] private string tooltip;
        [SerializeField] private LayoutStyle style;
        [SerializeField] private List<VisualCondition> conditions = new();
        [SerializeField] private List<ConditionRule> conditionRules = new();
        [SerializeField] private List<NodeAnimation> animations = new();
        [SerializeField] private float columnWeight = 0f;
        [SerializeField] private float rowHeight = 0f;
        [SerializeField] private bool makeWidthResizable = false;
        [SerializeField] private bool makeHeightResizable = false;
        [SerializeField] private bool spaceImported = false;
        [SerializeField] private bool hiddenInInspector = false;

        public string StyleToken
        {
            get => styleToken;
            set => styleToken = value;
        }

        public string Tooltip
        {
            get => tooltip;
            set => tooltip = value;
        }

        /// <summary>True when <see cref="Tooltip"/> is explicitly blanked (hidden) rather than merely unset.</summary>
        public bool IsTooltipHidden => tooltip == HiddenSentinel;

        public LayoutStyle Style
        {
            get => style;
            set => style = value;
        }

        /// <summary>
        /// LEGACY flat condition list — each entry carried its own outcome and was folded independently. Superseded
        /// by <see cref="ConditionRules"/>. Kept only so pre-rules saved layouts deserialize; converted lazily by
        /// <see cref="ConditionModel.EnsureMigrated"/>. New code authors and reads <see cref="ConditionRules"/>.
        /// </summary>
        public List<VisualCondition> Conditions
        {
            get => conditions;
            set => conditions = value;
        }

        /// <summary>
        /// This node's conditional-display rules (Show / Hide / Enable / Disable / WarningHighlight). Each rule is a
        /// sentence — an outcome applied when an all/any expression of conditions (and one-level groups) holds — and
        /// a node may carry several. Populated from the legacy <see cref="Conditions"/> list on first access via
        /// <see cref="ConditionModel.EnsureMigrated"/>.
        /// </summary>
        public List<ConditionRule> ConditionRules
        {
            get => conditionRules;
            set => conditionRules = value;
        }

        public List<NodeAnimation> Animations
        {
            get => animations;
            set => animations = value;
        }

        /// <summary>
        /// Proportional width (flex weight) of this node when it is a direct child of a
        /// <see cref="ContainerType.Columns"/> container, or a column in a <see cref="CollectionDisplayMode.Table"/>.
        /// <c>0</c> means equal/auto share, so layouts authored before resizable columns keep equal widths.
        /// Set by dragging a column divider; travels with the layout.
        /// </summary>
        public float ColumnWeight
        {
            get => columnWeight;
            set => columnWeight = value;
        }

        /// <summary>
        /// Explicit height (px) of this node when it is a direct child of a <see cref="ContainerType.Rows"/>
        /// container. <c>0</c> means auto (the field's natural height), so layouts authored before resizable
        /// rows are unchanged. Set by dragging the row's resize divider; travels with the layout. Applied as a
        /// <c>min-height</c>, so taller content is never clipped — dragging only adds breathing room.
        /// </summary>
        public float RowHeight
        {
            get => rowHeight;
            set => rowHeight = value;
        }

        /// <summary>
        /// When true, the inspector/preview shows a drag grip on this node's free horizontal edge (right when
        /// left/center-aligned, left when right-aligned) so its width can be resized directly. The dragged width
        /// is stored in <see cref="Style"/>.width. Off by default.
        /// </summary>
        public bool MakeWidthResizable
        {
            get => makeWidthResizable;
            set => makeWidthResizable = value;
        }

        /// <summary>
        /// When true, the inspector/preview shows a bottom-edge drag grip on this node so its height can be
        /// resized directly. The dragged height is stored in <see cref="Style"/>.height. Off by default.
        /// </summary>
        public bool MakeHeightResizable
        {
            get => makeHeightResizable;
            set => makeHeightResizable = value;
        }

        /// <summary>
        /// True once a code <c>[Space]</c> attribute on this node's field has been accounted for — imported as a
        /// Space widget above it, or deliberately declined (the non-attribute importers, e.g. Flat Import). While
        /// true, auto-sync never re-imports the gap, so deleting the spacer in the designer sticks. The sync
        /// engine clears it when the attribute disappears from code, re-arming a future re-add. Riding the layout
        /// graph (rather than a per-layout ledger list) makes it survive renames, clones and undo for free. Only
        /// meaningful on field-bound nodes (bindings / collections).
        /// </summary>
        public bool SpaceImported
        {
            get => spaceImported;
            set => spaceImported = value;
        }

        /// <summary>
        /// When true this node — and, for a container, everything inside it — is NOT BUILT AT ALL in a live
        /// render, so the siblings around it close up exactly as if it had been deleted. Toggled by the eye
        /// button on the designer's hierarchy row, the "Visible in Inspector" toggle in General Settings, and
        /// the live inspector's quick-edit menu.
        /// <para>Skipped, never built-then-hidden: the sibling rhythm (ApplySectionGaps / ApplyGap) is computed
        /// from the elements that exist, so a hidden-but-present node would leave its gap behind as a dead
        /// strip. Same reasoning — and the same early return in <c>LayoutTreeBuilder.BuildNode</c> — as a group
        /// toggle's mirrored binding. The standalone export omits the node too.</para>
        /// <para>DESIGN-time and permanent, which is what separates it from a <c>ConditionOutcome.Hide</c> rule
        /// (runtime, per-value) and from <c>InspectorLayoutAsset.LayoutHidden</c> (the whole layout, temporarily).
        /// Design surfaces — the designer canvas, Showcase, the tutorial preview — deliberately still BUILD a
        /// hidden node, dimmed and pipped, because that is the only way the author can find it and bring it
        /// back.</para>
        /// </summary>
        public bool HiddenInInspector
        {
            get => hiddenInInspector;
            set => hiddenInInspector = value;
        }
    }
}
