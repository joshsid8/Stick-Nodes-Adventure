using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A layout node that renders a button which invokes a named method on the target, with optional
    /// confirmation prompt, parameter entry, and async (Task/IEnumerator) wait state.
    /// </summary>
    [Serializable]
    public class ButtonNode : LayoutNode
    {
        [SerializeField] private string methodName;
        [SerializeField] private string buttonText = "Execute Action";
        [SerializeField] private bool confirmBeforeExecute;
        [SerializeField] private string confirmTitle = "Confirm";
        [SerializeField] private string confirmMessage = "Are you sure you want to do this?";

        public string MethodName
        {
            get => methodName;
            set => methodName = value;
        }

        public string ButtonText
        {
            get => buttonText;
            set => buttonText = value;
        }

        /// <summary>
        /// When <c>true</c>, clicking the button shows a Yes/Cancel confirmation dialog and only invokes the
        /// method if the user confirms. Defaults to <c>false</c> so existing buttons execute immediately.
        /// </summary>
        public bool ConfirmBeforeExecute
        {
            get => confirmBeforeExecute;
            set => confirmBeforeExecute = value;
        }

        /// <summary>Title of the confirmation dialog (used only when <see cref="ConfirmBeforeExecute"/> is on).</summary>
        public string ConfirmTitle
        {
            get => confirmTitle;
            set => confirmTitle = value;
        }

        /// <summary>Body message of the confirmation dialog (used only when <see cref="ConfirmBeforeExecute"/> is on).</summary>
        public string ConfirmMessage
        {
            get => confirmMessage;
            set => confirmMessage = value;
        }

        [SerializeField] private TextStyle labelTextStyle;

        /// <summary>Text styling for this button's label text.</summary>
        public TextStyle LabelTextStyle
        {
            get => labelTextStyle;
            set => labelTextStyle = value;
        }

        // ---- Appearance (appended fields; initializers are the back-compat defaults for layouts
        // saved before these existed: Modern preset, medium, stretched, no icon). ----

        [SerializeField] private ButtonStyle buttonStyle = ButtonStyle.Modern;
        [SerializeField] private OverridableColor accentColor;
        [SerializeField] private OverridableFloat cornerRadius;
        [SerializeField] private ButtonSize size = ButtonSize.Medium;
        // 0 = follow the Size class (the ProgressBarThickness convention: "0 = the stylesheet default"),
        // which is also what layouts saved before this field existed deserialize to.
        [SerializeField] private float customHeight;
        [SerializeField] private bool fullWidth = true; // load-bearing: pre-preset buttons stretch
        [SerializeField] private string iconId = "";
        [SerializeField] private ButtonIconPlacement iconPlacement = ButtonIconPlacement.Left;
        [SerializeField] private ButtonHoverAnimation hoverAnimation = ButtonHoverAnimation.Lift;
        // Optional hover/pressed background overrides. Unset (isOverridden=false) keeps the built-in feedback
        // (a subtle lighten on hover, the spring press-dip). Ignored by Classic (stock Unity chrome).
        [SerializeField] private OverridableColor hoverColor;
        [SerializeField] private OverridableColor pressedColor;
        // The ProgressBar's Text Backdrop, on buttons: a soft dark pill behind the label AND the icon, so
        // they stay readable over a bright accent or a busy background. Off = the preset's own ink.
        [SerializeField] private bool textBackdrop;

        /// <summary>The visual preset (Modern filled accent, Classic stock Unity, Outline, Ghost, Soft, Danger).
        /// Named to avoid colliding with the base <see cref="LayoutNode.Style"/> (LayoutStyle box model).</summary>
        public ButtonStyle StylePreset { get => buttonStyle; set => buttonStyle = value; }

        /// <summary>Optional accent override; unset ⇒ the preset's default (house accent, or the danger red).</summary>
        public OverridableColor AccentColor { get => accentColor; set => accentColor = value; }

        /// <summary>Optional corner-radius override; unset ⇒ the preset default. Ignored by Classic (stock chrome).</summary>
        public OverridableFloat CornerRadius { get => cornerRadius; set => cornerRadius = value; }

        /// <summary>Height class, applied as a grow-only min-height. Overridden outright by
        /// <see cref="CustomHeight"/> when that is above 0.</summary>
        public ButtonSize Size { get => size; set => size = value; }

        /// <summary>The smallest height the Custom Height slider offers.</summary>
        public const float MinCustomHeight = 12f;

        /// <summary>The largest height the Custom Height slider offers.</summary>
        public const float MaxCustomHeight = 192f;

        /// <summary>An exact min-height in pixels that overrides the <see cref="Size"/> class.
        /// 0 (the default) = follow the class, which is what every layout saved before this field
        /// existed reads. Stored unclamped-safe: the renderer clamps to
        /// [<see cref="MinCustomHeight"/>, <see cref="MaxCustomHeight"/>].</summary>
        public float CustomHeight { get => customHeight; set => customHeight = value; }

        /// <summary>Stretch to the row width (the historical default) or hug the content.</summary>
        public bool FullWidth { get => fullWidth; set => fullWidth = value; }

        /// <summary>Sasquatch icon id rendered beside the label; empty = no icon.</summary>
        public string IconId { get => iconId; set => iconId = value; }

        /// <summary>Which side of the label the icon renders on (when <see cref="IconId"/> is set).</summary>
        public ButtonIconPlacement IconPlacement { get => iconPlacement; set => iconPlacement = value; }

        /// <summary>The hover/press feedback this button plays. Lift (a calm, low-overshoot rise + gentle press)
        /// is the default; pick Bounce for the original springy wobble. Buttons saved before this field existed
        /// deserialize to Lift as well — the managed-ref constructor runs this initializer for the absent field.</summary>
        public ButtonHoverAnimation HoverAnimation { get => hoverAnimation; set => hoverAnimation = value; }

        /// <summary>Optional hover background color; unset ⇒ the built-in subtle lighten. Ignored by Classic.</summary>
        public OverridableColor HoverColor { get => hoverColor; set => hoverColor = value; }

        /// <summary>Optional pressed (pointer-down) background color; unset ⇒ no color change on press (spring
        /// dip only). Ignored by Classic.</summary>
        public OverridableColor PressedColor { get => pressedColor; set => pressedColor = value; }

        /// <summary>When true, the label and the icon ride a soft dark pill inside the button, guaranteeing
        /// contrast over any accent or background (an accessibility option; off = the preset's own ink).
        /// The twin of <see cref="BindingNode.ProgressBarTextBackdrop"/>. Applies to every preset, Classic
        /// included — a bare button grows the same inner label an icon button has.</summary>
        public bool TextBackdrop { get => textBackdrop; set => textBackdrop = value; }

        // ---- Rename self-heal (appended field; "" for layouts saved before this existed — no heal data
        // until the name next resolves and the fingerprint is backfilled). ----

        [SerializeField] private string methodSignature = "";

        /// <summary>
        /// Fingerprint of the bound method's shape (return type + parameter types), stamped whenever
        /// <see cref="MethodName"/> resolves. When a compile renames the method, the name stops resolving but
        /// exactly one method with this fingerprint remains — the button relinks to it automatically. Empty =
        /// the name has never resolved (a typed-ahead method), which disables healing for this button.
        /// </summary>
        public string MethodSignature
        {
            get => methodSignature;
            set => methodSignature = value;
        }
    }
}
