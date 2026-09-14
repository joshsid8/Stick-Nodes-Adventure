using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>Which icon a <see cref="ScriptHeaderNode"/> displays. <see cref="Custom"/> is the default (and
    /// back-compat) value, so pre-enum layouts keep their texture-or-script-icon look untouched.</summary>
    public enum ScriptHeaderIconSource
    {
        /// <summary>A custom texture (<see cref="ScriptHeaderNode.CustomIcon"/>); empty falls back to the script's own icon.</summary>
        Custom = 0,
        /// <summary>A built-in Sasquatch/Lucide glyph chosen by id (<see cref="ScriptHeaderNode.IconId"/>).</summary>
        BuiltIn = 1,
    }

    /// <summary>
    /// A header widget that surfaces the target's backing script — a nicer-looking replacement for Unity's
    /// default greyed-out "Script" field. Single click pings the script asset in the Project window, double
    /// click opens it; right-click offers the full action menu. It resolves the script from the serialized
    /// object's hidden <c>m_Script</c> property, so it needs no property path and works for both
    /// MonoBehaviours and ScriptableObjects. Auto-added to the top of new layouts — remove it if not wanted.
    /// </summary>
    [Serializable]
    public class ScriptHeaderNode : LayoutNode
    {
        [SerializeField] private bool hideIcon;
        [SerializeField] private Texture2D customIcon;
        [SerializeField] private string iconId = "";
        [SerializeField] private ScriptHeaderIconSource iconSource = ScriptHeaderIconSource.Custom;
        [SerializeField] private ImageAlignment textAlignment = ImageAlignment.Left;
        [SerializeField] private string textOverride;
        [SerializeField] private TextStyle textStyle;
        // On by default: the header carries the inspector field-search magnifier (hover-revealed, beside the
        // Live Debug bug). Turning it off removes the button AND the only way into the field search for
        // this layout — see InspectorFieldSearch. Serialized `true` on new nodes; pre-existing layouts that
        // predate the field also read back true because the field initialiser runs on deserialize.
        [SerializeField] private bool showSearch = true;
        // ON by default. This flag only says whether the AFFORDANCE exists; whether debug is currently
        // RECORDING is per-session editor state (SasquatchInspector.Editor.LiveDebug.DebugModeState), never
        // layout data — the same split the Condition Debugger uses. So defaulting it on adds a hover-revealed
        // glyph to the header and changes nothing else about what any inspector renders until it is clicked.
        // Discoverability is the whole argument: a debugging tool nobody can find is a debugging tool nobody has.
        //
        // NOTE for anyone flipping this again: EditorJsonUtility (via LayoutGraphStorage) writes EVERY
        // serialized field, defaults included, so a layout saved while this read false keeps reading false —
        // the initialiser only reaches layouts authored after the change, and layouts that predate the field
        // entirely. There is deliberately no auto-migration: rewriting user assets on load is exactly the
        // writes-on-open behaviour the package spent a pass removing.
        [SerializeField] private bool showDebugToggle = true;

        /// <summary>Hide the leading icon entirely.</summary>
        public bool HideIcon
        {
            get => hideIcon;
            set => hideIcon = value;
        }

        /// <summary>Show the field-search magnifier in the header (hover-revealed, beside the Live Debug bug).
        /// On by default. Off removes the button and — since the header is the sole entry point — disables the
        /// field search for this layout entirely.</summary>
        public bool ShowSearch
        {
            get => showSearch;
            set => showSearch = value;
        }

        /// <summary>Show the Live Debug toggle (bug glyph) in the header. On by default. On its own this only
        /// adds the button — the recording itself is per-session editor state, so this never changes what the
        /// inspector renders until the button is clicked.</summary>
        public bool ShowDebugToggle
        {
            get => showDebugToggle;
            set => showDebugToggle = value;
        }

        /// <summary>Custom texture icon shown when <see cref="IconSource"/> is <see cref="ScriptHeaderIconSource.Custom"/>
        /// (and <see cref="HideIcon"/> is off): it replaces the script's own icon; when null the script's icon is used.</summary>
        public Texture2D CustomIcon
        {
            get => customIcon;
            set => customIcon = value;
        }

        /// <summary>Built-in icon id (from the shared Sasquatch/Lucide set, same picker as Action Buttons) shown as the
        /// leading glyph when <see cref="IconSource"/> is <see cref="ScriptHeaderIconSource.BuiltIn"/>. Empty = unset
        /// (falls back to the script's own icon).</summary>
        public string IconId
        {
            get => iconId;
            set => iconId = value;
        }

        /// <summary>Which icon the header shows: a <see cref="CustomIcon"/> texture or a built-in <see cref="IconId"/>
        /// glyph. Defaults to <see cref="ScriptHeaderIconSource.Custom"/> so pre-existing layouts keep their look. The
        /// two are mutually exclusive by construction — the source selects which one is live (neither value is cleared).</summary>
        public ScriptHeaderIconSource IconSource
        {
            get => iconSource;
            set => iconSource = value;
        }

        /// <summary>Horizontal alignment of the script name within the bar (Left / Center / Right).</summary>
        [Obsolete("Use TextStyle.align instead.")]
        public ImageAlignment TextAlignment
        {
            get => textAlignment;
            set => textAlignment = value;
        }

        /// <summary>Custom display text override. Empty = unset (show script default); HiddenSentinel = hide text.</summary>
        public string TextOverride
        {
            get => textOverride;
            set => textOverride = value;
        }

        /// <summary>Text styling configuration for this script header.</summary>
        public TextStyle TextStyle
        {
            get => textStyle;
            set => textStyle = value;
        }

        /// <summary>
        /// Effective horizontal text anchor for the script-name label: prefers the modern
        /// <see cref="TextStyle"/>.align override, falling back to the legacy <c>textAlignment</c> field for
        /// layouts authored before text styling existed. Reads the private field directly, so it replaces all
        /// internal reads of the obsolete <see cref="TextAlignment"/> property.
        /// </summary>
        public TextAnchor ResolveTextAnchor()
        {
            if (textStyle.align.isOverridden) return textStyle.align.value;
            switch (textAlignment)
            {
                case ImageAlignment.Center: return TextAnchor.MiddleCenter;
                case ImageAlignment.Right:  return TextAnchor.MiddleRight;
                default:                    return TextAnchor.MiddleLeft;
            }
        }

        /// <summary>True when the display text is explicitly hidden.</summary>
        public bool IsTextHidden => textOverride == HiddenSentinel;

        /// <summary>Effective display text override, or null if unset/hidden.</summary>
        public string EffectiveTextOverride =>
            (string.IsNullOrEmpty(textOverride) || textOverride == HiddenSentinel) ? null : textOverride;
    }
}
