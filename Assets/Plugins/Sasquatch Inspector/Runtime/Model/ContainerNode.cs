using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A layout node that groups children into a Rows / Columns / Grid / Card / Foldout / Tabs / Frame
    /// container, with optional resizable tracks, collapsible framing, and (for Foldouts) a bound
    /// group-toggle checkbox.
    /// </summary>
    [Serializable]
    public class ContainerNode : LayoutNode, ISerializationCallbackReceiver
    {
        [SerializeField] private string propertyPath;
        [SerializeReference] private List<LayoutNode> children = new();
        [UnityEngine.Serialization.FormerlySerializedAs("kind")] [SerializeField] private ContainerType type = ContainerType.Rows;
        [SerializeField] private string title;
        // When true, this container (Rows/Columns/Grid/Card only) renders inside the collapsible
        // .sasq-group frame so it can be minimized/expanded by clicking its header. Defaults to false so
        // existing layouts are unchanged (opt-in). Foldout/Tabs ignore this (they have their own behavior).
        [UnityEngine.Serialization.FormerlySerializedAs("minimizable")] [SerializeField] private bool expandable = false;
        [UnityEngine.Serialization.FormerlySerializedAs("startExpanded")] [SerializeField] private bool defaultToOpen = true;
        [SerializeField] private int gridColumns = 0;
        [SerializeField] private float gap = 0f;
        [SerializeField] private TrackResizeMode trackResize = TrackResizeMode.Auto;
        [SerializeField] private GridConstraint gridConstraint = GridConstraint.Flexible;
        [SerializeField] private bool gridResizableColumns;
        [SerializeField] private bool gridResizableRows;
        // Per-track sizing for a resizable Grid. Column entries are flex WEIGHTS (proportional, reflow with
        // width); row entries are explicit pixel HEIGHTS (0 = auto). Empty/short ⇒ that track is equal/auto.
        [SerializeField] private System.Collections.Generic.List<float> gridColumnWeights = new();
        [SerializeField] private System.Collections.Generic.List<float> gridRowHeights = new();
        // Tabs only: the active tab header tint. Alpha 0 (the serialization default for pre-existing Tabs) ⇒
        // fall back to the built-in blue, so old layouts render unchanged.
        [SerializeField] private Color tabAccentColor = new Color(0.18f, 0.35f, 0.58f, 0.8f);
        // Section color-coding: an optional tint applied to a foldout/group/card section so complex inspectors
        // are easier to scan (header bar tinted stronger, body a faint wash, plus a full-color accent rail down
        // the left edge). Unset by default (OverridableColor.isOverridden == false) so existing layouts are
        // unchanged. Distinct from the generic LayoutNode.Style.backgroundColor (which paints the OUTER element
        // and is visually covered by the .sasq-group-content / -header USS and the Card's hardcoded background).
        // Appended → old layouts deserialize clean (Unity native [SerializeReference] root, no JSON schema).
        [SerializeField] private OverridableColor sectionTintColor;
        // Accent stripe: a full-color rail down the left edge ONLY (no header/body wash — the lighter-weight
        // sibling of SectionTintColor). Unset by default. When BOTH are set the stripe is applied last, so its
        // rail color wins while the tint keeps its header/body wash. Applied in
        // LayoutTreeBuilder.ApplyCardAccentStripe. Appended → old layouts clean.
        [SerializeField] private OverridableColor accentStripeColor;
        // When this container is a TAB (direct child of a Tabs container): the tab-strip label. Kept separate
        // from Title so naming a tab doesn't force an in-content header. Renamed in place (double-click / F2).
        [SerializeField] private string tabLabel;
        [SerializeField] private int activeTabIndex = 0;
        // A Foldout is a toggle group iff groupTogglePath is non-empty (the bound bool IS the header checkbox).
        // hasGroupToggle is a legacy/no-op flag kept only so old serialized layouts still deserialize cleanly;
        // behavior now gates on the path. groupToggleAlignment positions the header checkbox+name group.
        [SerializeField] private bool hasGroupToggle;
        [SerializeField] private string groupTogglePath;
        [SerializeField] private GroupToggleAlignment groupToggleAlignment = GroupToggleAlignment.Left;

        public string PropertyPath
        {
            get => propertyPath;
            set => propertyPath = value;
        }

        public List<LayoutNode> Children
        {
            get => children;
            set => children = value;
        }

        public ContainerType Type
        {
            get => type;
            set => type = value;
        }

        public string Title
        {
            get => title;
            set => title = value;
        }

        /// <summary>
        /// Rows/Columns/Grid/Card only: when true the container renders inside the collapsible
        /// <c>.sasq-group</c> frame (header + chevron) so it can be minimized/expanded by clicking its
        /// header, matching the project's other expandable objects. Defaults to false (opt-in). When
        /// true, <see cref="DefaultToOpen"/> seeds the initial collapsed/expanded state. Ignored by
        /// Foldout (already collapsible) and Tabs.
        /// </summary>
        public bool Expandable
        {
            get => expandable;
            set => expandable = value;
        }

        public bool DefaultToOpen
        {
            get => defaultToOpen;
            set => defaultToOpen = value;
        }

        public int GridColumns
        {
            get => gridColumns;
            set => gridColumns = value;
        }

        public float Gap
        {
            get => gap;
            set => gap = value;
        }

        /// <summary>
        /// Whether this Rows/Columns container's tracks can be drag-resized. <see cref="TrackResizeMode.Auto"/>
        /// (default) = resizable unless this is the root container. See <see cref="TrackResizeMode"/>.
        /// </summary>
        public TrackResizeMode TrackResize
        {
            get => trackResize;
            set => trackResize = value;
        }

        /// <summary>How a Grid decides its track count (Flexible / Fixed Columns / Fixed Rows). Grid only.</summary>
        public GridConstraint GridConstraint
        {
            get => gridConstraint;
            set => gridConstraint = value;
        }

        /// <summary>Grid only: allow dragging to resize column widths (stored in <see cref="GridColumnWeights"/>).</summary>
        public bool GridResizableColumns
        {
            get => gridResizableColumns;
            set => gridResizableColumns = value;
        }

        /// <summary>Grid only: allow dragging to resize row heights (stored in <see cref="GridRowHeights"/>).</summary>
        public bool GridResizableRows
        {
            get => gridResizableRows;
            set => gridResizableRows = value;
        }

        /// <summary>Per-column flex weights for a resizable Grid (proportional; empty/short ⇒ equal columns).</summary>
        public System.Collections.Generic.List<float> GridColumnWeights
        {
            get => gridColumnWeights;
            set => gridColumnWeights = value;
        }

        /// <summary>Per-row explicit pixel heights for a resizable Grid (0/empty ⇒ auto height).</summary>
        public System.Collections.Generic.List<float> GridRowHeights
        {
            get => gridRowHeights;
            set => gridRowHeights = value;
        }

        /// <summary>Tabs only: the active tab header tint. Alpha 0 ⇒ use the built-in default blue.</summary>
        public Color TabAccentColor
        {
            get => tabAccentColor;
            set => tabAccentColor = value;
        }

        /// <summary>Optional section color-coding for a Foldout / Card / Expandable group: tints the section's
        /// header + body and draws a full-color accent rail down its left edge. Unset (not overridden) ⇒ no tint,
        /// so existing layouts render unchanged. Applied in <c>LayoutTreeBuilder.ApplySectionTint</c>.</summary>
        public OverridableColor SectionTintColor
        {
            get => sectionTintColor;
            set => sectionTintColor = value;
        }

        /// <summary>Optional accent stripe: a full-color rail down the section's left edge only, with no
        /// header/body tint (the lighter-weight alternative to <see cref="SectionTintColor"/>). Unset ⇒ none.
        /// When both are set the stripe's rail color wins (it is applied last), while
        /// <see cref="SectionTintColor"/> keeps its header/body wash.</summary>
        public OverridableColor AccentStripeColor
        {
            get => accentStripeColor;
            set => accentStripeColor = value;
        }

        /// <summary>As a tab (child of a Tabs container): the tab-strip label. Separate from Title (which is an
        /// in-content header), edited in place via double-click / F2. Empty ⇒ falls back to Title, then "Tab N".</summary>
        public string TabLabel
        {
            get => tabLabel;
            set => tabLabel = value;
        }

        public int ActiveTabIndex
        {
            get => activeTabIndex;
            set => activeTabIndex = value;
        }

        /// <summary>Legacy/no-op flag from the original group-toggle design. Behavior now gates on
        /// <see cref="GroupTogglePath"/>; kept only for clean deserialization of older layouts.</summary>
        public bool HasGroupToggle
        {
            get => hasGroupToggle;
            set => hasGroupToggle = value;
        }

        /// <summary>Foldout only: binding path to a bool property. When non-empty the Foldout renders as a
        /// toggle group whose header checkbox IS that bool, greying the contents when false.</summary>
        public string GroupTogglePath
        {
            get => groupTogglePath;
            set => groupTogglePath = value;
        }

        /// <summary>Toggle-group Foldout only: horizontal placement of the header checkbox + name.</summary>
        public GroupToggleAlignment GroupToggleAlignment
        {
            get => groupToggleAlignment;
            set => groupToggleAlignment = value;
        }

        [SerializeField] private TextStyle titleTextStyle;

        /// <summary>Text styling (bold/italic, size, color, align) for this container's Title/Label text.</summary>
        public TextStyle TitleTextStyle
        {
            get => titleTextStyle;
            set => titleTextStyle = value;
        }

        // Provenance: the [Header] attribute text this container was created FROM by the import tools or
        // the header-aware auto-sync — a durable link that survives the user retitling the section. Empty
        // for user-made containers. The sync engine uses it to route newly-added code fields into their
        // header's section; the orphaned-header cleanup uses it to detect containers whose [Header] was
        // deleted from code (choosing "Keep" there clears it, opting the container out of both).
        // Appended → old layouts deserialize clean (Unity-native [SerializeReference] graph, no JSON schema).
        [SerializeField] private string sourceHeader;

        // Frame container settings (Type == ContainerType.Frame only): shape, image fill, scrim, split
        // params. A plain [Serializable] class, so Unity default-constructs it on layouts saved before the
        // field existed. Appended → old layouts deserialize clean.
        [SerializeField] private FrameSettings frame = new FrameSettings();

        /// <summary>The [Header] text this container was created from by import/auto-sync; empty for
        /// user-made containers. Survives Title edits; cleared when the user keeps an orphaned section.</summary>
        public string SourceHeader
        {
            get => sourceHeader;
            set => sourceHeader = value;
        }

        /// <summary>Frame container settings (<see cref="ContainerType.Frame"/> only). Never null: layouts
        /// saved before the field existed get a default instance on deserialize.</summary>
        public FrameSettings Frame
        {
            get => frame ?? (frame = new FrameSettings());
            set => frame = value;
        }

        // Designer-only cosmetic name shown in the Layout Designer's hierarchy tree (F2 / right-click →
        // Rename). Purely presentational: never rendered by the inspector and independent of Title (which
        // stays the in-inspector header). Appended → old layouts deserialize clean.
        [SerializeField] private string hierarchyName;

        /// <summary>Cosmetic name for this container in the Layout Designer's hierarchy tree only. Never
        /// rendered in the inspector; empty ⇒ the hierarchy falls back to <see cref="Title"/>.</summary>
        public string HierarchyName
        {
            get => hierarchyName;
            set => hierarchyName = value;
        }

        /// <summary>No-op; serialization state is kept directly on the serialized fields.</summary>
        public void OnBeforeSerialize() { }

        /// <summary>Back-compat: the retired Box container (int 3) clamps to Card on load, so any legacy
        /// layout that still stores a Box deserializes to a valid type instead of an undefined enum.</summary>
        public void OnAfterDeserialize()
        {
            if (!System.Enum.IsDefined(typeof(ContainerType), type))
            {
                type = ContainerType.Card;
            }
        }
    }
}
