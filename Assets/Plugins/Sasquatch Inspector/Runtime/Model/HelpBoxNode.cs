using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Runtime
{
    /// <summary>How an expandable Info/Warning shows its collapsed state: a one-line summary + chevron
    /// (the original), or a compact round "?" chip that reveals the message card on click.</summary>
    public enum HelpBoxCollapsedStyle
    {
        SummaryLine,
        Chip
    }

    /// <summary>
    /// A layout node that renders an informational / warning / error help box, optionally expandable from a
    /// collapsed one-line summary (or a "?" chip).
    /// </summary>
    [Serializable]
    public class HelpBoxNode : LayoutNode
    {
        [SerializeField] private string message = "Diagnostic Alert";
        [SerializeField] private HelpBoxMessageType messageType = HelpBoxMessageType.Info;

        [UnityEngine.Serialization.FormerlySerializedAs("isExpandable")] [SerializeField] private bool expandable;
        [SerializeField] private string collapsedSummary = "";
        [UnityEngine.Serialization.FormerlySerializedAs("startExpanded")] [SerializeField] private bool defaultToOpen;
        [SerializeField] private bool renderMarkdown = true;
        // When expandable, how the collapsed state is presented. SummaryLine (0) = original look, so old layouts
        // are unchanged; Chip = a round "?" reveal chip.
        [SerializeField] private HelpBoxCollapsedStyle collapsedStyle = HelpBoxCollapsedStyle.SummaryLine;
        // Horizontal placement of the "?" chip (Chip collapsed style only). Left = the default.
        [SerializeField] private ImageAlignment chipAlignment = ImageAlignment.Left;

        public string Message
        {
            get => message;
            set => message = value;
        }

        public HelpBoxMessageType MessageType
        {
            get => messageType;
            set => messageType = value;
        }

        public bool Expandable
        {
            get => expandable;
            set => expandable = value;
        }

        /// <summary>Summary-line text override for the expandable variant. Empty = unset (show the
        /// "{MessageType} Details" default); HiddenSentinel = hide the text.</summary>
        public string CollapsedSummary
        {
            get => collapsedSummary;
            set => collapsedSummary = value;
        }

        /// <summary>True when the summary text is explicitly hidden.</summary>
        public bool IsSummaryHidden => collapsedSummary == HiddenSentinel;

        /// <summary>Effective summary override, or null if unset/hidden.</summary>
        public string EffectiveSummaryOverride =>
            (string.IsNullOrEmpty(collapsedSummary) || collapsedSummary == HiddenSentinel) ? null : collapsedSummary;

        public bool DefaultToOpen
        {
            get => defaultToOpen;
            set => defaultToOpen = value;
        }

        /// <summary>When expandable, whether the collapsed state is a one-line summary + chevron
        /// (<see cref="HelpBoxCollapsedStyle.SummaryLine"/>) or a round "?" reveal chip
        /// (<see cref="HelpBoxCollapsedStyle.Chip"/>).</summary>
        public HelpBoxCollapsedStyle CollapsedStyle
        {
            get => collapsedStyle;
            set => collapsedStyle = value;
        }

        /// <summary>Horizontal placement of the "?" reveal chip when <see cref="CollapsedStyle"/> is Chip.</summary>
        public ImageAlignment ChipAlignment
        {
            get => chipAlignment;
            set => chipAlignment = value;
        }

        /// <summary>When true, <see cref="Message"/> (and the expandable variant's summary) render as lightweight
        /// markdown — **bold**, *italics*, `code`, {Color|text}, and [label](url) — plus raw Unity rich-text tags
        /// (&lt;b&gt;, &lt;color=…&gt;, &lt;size=…&gt;, …), which pass through to the renderer. Defaults on; safe
        /// for existing serialized data because nodes are stored [SerializeReference] (the managed-ref constructor
        /// runs this initializer for refs saved before the field existed). Plain text with no markers is
        /// unaffected.</summary>
        public bool RenderMarkdown
        {
            get => renderMarkdown;
            set => renderMarkdown = value;
        }

        [SerializeField] private TextStyle messageTextStyle;

        /// <summary>Text styling for this help box's message text.</summary>
        public TextStyle MessageTextStyle
        {
            get => messageTextStyle;
            set => messageTextStyle = value;
        }

        [SerializeField] private TextStyle summaryTextStyle;

        /// <summary>Text styling for the expandable variant's collapsed summary line.</summary>
        public TextStyle SummaryTextStyle
        {
            get => summaryTextStyle;
            set => summaryTextStyle = value;
        }
    }
}
