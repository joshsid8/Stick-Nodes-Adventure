using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Stepper option for a numeric <see cref="DisplayType.Default"/> field: the bound value field flanked by
    /// −/+ buttons that step by <see cref="BindingNode.StepSize"/> at native precision (via
    /// <see cref="BindingFactory.StepNumeric"/>, so 64-bit long/ulong/double fields step losslessly). The inner
    /// PropertyField still binds/edits normally — the buttons just nudge it.
    ///
    /// <para>A press is bounded by <see cref="BindingFactory.ResolveStepperBounds"/> — the field's own
    /// <c>[Range]</c>/<c>[Min]</c> and an enforced validation range — because the buttons write the serialized
    /// property directly, behind whatever drawer paints it.</para>
    /// </summary>
    public static partial class BindingFactory
    {
        // alignTarget is the inner field so the label-column alignment lands there (not the wrapper row),
        // matching the Create-asset button wrapper in CreateBindingField.
        private static VisualElement CreateStepperField(BindingNode node, SerializedProperty prop, bool hideLabel,
            out VisualElement alignTarget)
        {
            double step = node.StepSize <= 0f ? 1.0 : node.StepSize;

            // The bounds a press may not cross — [Range] / [Min] on the field, and an enforced validation range.
            // Resolved once here (attribute reflection per click would be wasteful) because the field's own type
            // and attributes can't change without a rebuild.
            ResolveStepperBounds(node, prop, out float lo, out float hi, out bool hasLo, out bool hasHi);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var field = new PropertyField(prop);
            if (hideLabel) field.label = "";
            else if (node.EffectiveLabelOverride != null) field.label = node.EffectiveLabelOverride;
            field.style.flexGrow = 1;
            alignTarget = field;

            // Returns the button's HOST, not the button: the press wash has to be a sibling of the Button
            // rather than a child of it (see AttachPressFeedback). Everything downstream — the alignSelf
            // overrides, the row Add, the base-field injection and its parent guard — treats this as the
            // button, which is exactly what it is positionally.
            VisualElement MakeBtn(string text, double dir)
            {
                var b = new Button(() =>
                {
                    prop.serializedObject.Update();
                    if (StepNumeric(prop, dir * step, lo, hi, hasLo, hasHi))
                        prop.serializedObject.ApplyModifiedProperties();
                })
                { text = text };
                b.AddToClassList("sasq-stepper-btn");

                // Full Button-widget chrome (the "Action Button" pattern) via a transient ButtonNode, with a
                // per-button accent override (the enum-badge "individual styling" idea) so + and − can differ.
                // Applied BEFORE the compact-layout overrides below so those win — chrome sets its own margins /
                // alignSelf / min-height sized for full buttons, but a stepper button stays a small square.
                var perButtonColor = dir > 0.0 ? node.StepperPlusColor : node.StepperMinusColor;
                var styleNode = new ButtonNode
                {
                    ButtonText = text,
                    FullWidth = false,
                    StylePreset = node.StepperButtonStyle,
                    AccentColor = perButtonColor.isOverridden ? perButtonColor : node.StepperButtonAccent,
                    CornerRadius = node.StepperButtonCornerRadius,
                    Size = node.StepperButtonSize,
                    HoverAnimation = node.StepperButtonHoverAnimation,
                    HoverColor = node.StepperButtonHoverColor,
                    PressedColor = node.StepperButtonPressedColor,
                };
                LayoutTreeBuilder.ApplyButtonChrome(b, styleNode);

                b.style.width = 22;
                b.style.minWidth = 22;
                b.style.marginTop = 0;
                b.style.marginBottom = 0;
                b.style.marginLeft = 0;
                b.style.marginRight = 0;
                b.style.paddingLeft = 0;
                b.style.paddingRight = 0;

                // The 2px gutters and the vertical centring move to the HOST, so the wash — inset 0 inside it —
                // covers the button's box exactly rather than its box plus its margins.
                var host = new VisualElement();
                host.style.flexDirection = FlexDirection.Row;
                host.style.marginLeft = 2;
                host.style.marginRight = 2;
                host.style.alignSelf = Align.Center; // center vertically when injected into the field's base-field row
                host.Add(b);

                AttachPressFeedback(host, b);
                return host;
            }

            // − (minus sign, not hyphen) / +.
            var minus = MakeBtn("−", -1.0);
            var plus = MakeBtn("+", 1.0);

            row.Add(field);

            if (node.StepperPlacement == StepperPlacement.GroupedRight)
            {
                // Both buttons stay in the OUTER row to the right of the whole field, but BOTTOM-aligned
                // (alignSelf FlexEnd) so they land on the field's value line instead of vertically centering
                // across a taller [Header]/[Space]-decorated field (the reported "buttons float between the
                // header and the field" bug). The field's base-field is the PropertyField's LAST child, so the
                // row's bottom edge — where FlexEnd parks the buttons — is the input row.
                minus.style.alignSelf = Align.FlexEnd;
                plus.style.alignSelf = Align.FlexEnd;
                row.Add(minus);
                row.Add(plus);
                return row;
            }

            // GroupedLeft / Split: inject the −/+ as SIBLINGS of the field's .unity-base-field__input (via
            // .hierarchy, which bypasses BaseField's input-only contentContainer — same trick as the Min-Max
            // slider's value fields). The label stays at index 0, so the result is [label][−][+][input] (Grouped
            // Left) or [label][−][input][+] (Split) — always to the RIGHT of the label, never left of it. The
            // inner numeric field builds LAZILY on bind, so inject once its input exists and re-inject if the
            // field is ever rebuilt (the parent guard keeps it idempotent).
            bool split = node.StepperPlacement == StepperPlacement.Split;
            void InjectButtons()
            {
                var inputEl = field.Q(className: "unity-base-field__input");
                var bf = inputEl?.parent;
                if (bf == null || minus.parent == bf) return; // not built yet, or already injected into this field
                int i = bf.hierarchy.IndexOf(inputEl);
                if (split)
                {
                    bf.hierarchy.Insert(i, minus);        // − after the label, before the input
                    bf.hierarchy.Insert(i + 2, plus);     // + after the input (index shifted +1 by the − insert)
                }
                else // GroupedLeft
                {
                    bf.hierarchy.Insert(i, minus);
                    bf.hierarchy.Insert(i + 1, plus);
                }
            }
            field.RegisterCallback<GeometryChangedEvent>(_ => InjectButtons());
            InjectButtons(); // in case the inner field is already built
            return row;
        }

        /// <summary>
        /// Gives a stepper's −/+ a visible press. They had none: the buttons default to
        /// <see cref="ButtonStyle.Classic"/>, and <c>ApplyButtonChrome</c> returns early for Classic — before
        /// the hover colour, before the press colour, and before <c>ApplyButtonHover</c> — so no feedback
        /// handler was ever attached. A *styled* stepper was barely better, since the press colour only paints
        /// when the user has explicitly overridden it. Unity's own stock press tint does exist, but on a 22px
        /// square it is invisible.
        ///
        /// <para>TWO layers, and the wash is the load-bearing one: <see cref="EditorSpringFX.PressBounce"/> is
        /// a no-op whenever Motion is switched off in Settings, so a dip-only answer would leave exactly the
        /// users who cannot see motion exactly where they started.</para>
        ///
        /// <para>The wash is an OVERLAY, never a write to <c>backgroundColor</c>. ApplyButtonChrome owns that
        /// property through its own hover/press state machine and re-samples the "resting" colour on
        /// pointer-enter — a second writer would eventually hand it a press tint to memorise as the resting
        /// value. An overlay composites over whatever is beneath it, needs no save/restore, and therefore
        /// behaves identically on Classic and on every styled preset.</para>
        ///
        /// <para><b>It must be the button's SIBLING, never its child.</b> Yoga only runs a node's measure
        /// function while that node is a LEAF, and a Button is a TextElement that measures its own glyph. Add
        /// any child — even this one, absolutely positioned and entirely out of flow — and the Button stops
        /// measuring: MEASURED LIVE on 6000.0.79f1, a 22px stepper went from 22×19 to 22×4, which reads as the
        /// buttons having turned from squares into thin wide slabs. Hence the host wrapper in MakeBtn, which is
        /// a plain VisualElement and has no measure function to lose.</para>
        /// </summary>
        private static void AttachPressFeedback(VisualElement host, Button b)
        {
            // Lighter on a dark skin, darker on a light one — and deliberately stronger than the 7% lighten
            // ApplyButtonChrome uses for hover, so press reads as a step beyond hover rather than a repeat.
            var wash = new VisualElement { pickingMode = PickingMode.Ignore };
            wash.style.position = Position.Absolute;
            wash.style.left = 0;
            wash.style.right = 0;
            wash.style.top = 0;
            wash.style.bottom = 0;
            wash.style.backgroundColor = WidgetPalette.Ink(0.18f);
            wash.style.opacity = 0f;
            host.Add(wash);

            // Follow the button's resolved corner radius, which is USS on Classic and inline on a styled
            // preset — either way it is not known at build time, and a square wash on a rounded button shows
            // its corners the instant it lights up.
            b.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                wash.style.borderTopLeftRadius = b.resolvedStyle.borderTopLeftRadius;
                wash.style.borderTopRightRadius = b.resolvedStyle.borderTopRightRadius;
                wash.style.borderBottomLeftRadius = b.resolvedStyle.borderBottomLeftRadius;
                wash.style.borderBottomRightRadius = b.resolvedStyle.borderBottomRightRadius;
            });

            // CAPTURE phase. Button's built-in Clickable handles PointerDown in the bubble phase and calls
            // StopImmediatePropagation while it starts a click, so a bubble-phase handler here never runs —
            // the same trap that kept ButtonNode's PressedColor from ever showing.
            b.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !b.enabledSelf) return;
                wash.style.opacity = 1f;
                // The HOST dips, so the wash travels with the button instead of hanging still over a
                // shrinking one. A touch deeper than the 0.92 default — it is a small target.
                EditorSpringFX.PressBounce(host, 0.86f);
            }, TrickleDown.TrickleDown);

            b.RegisterCallback<PointerUpEvent>(_ => wash.style.opacity = 0f, TrickleDown.TrickleDown);
            // Clickable captures the pointer, so a press that drags off the button still gets its PointerUp
            // here — but releasing the wash on leave as well is what makes a drag-away read as "cancelled",
            // the way every OS button does.
            b.RegisterCallback<PointerLeaveEvent>(_ => wash.style.opacity = 0f);
        }
    }
}
