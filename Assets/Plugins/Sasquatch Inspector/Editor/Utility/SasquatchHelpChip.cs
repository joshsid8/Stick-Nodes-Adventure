using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shared "?" help affordance: a small round reveal chip paired with a hidden, spring-revealed info card —
    /// the pattern from the Project Settings page, factored out so the Info/Warning "Chip" collapsed style
    /// and the per-field help chip render identically. The card renders its text as Sasquatch markdown
    /// (bold / italics / <c>code</c> / links, plus raw Unity rich-text tags) unless markdown is disabled.
    /// </summary>
    internal static class SasquatchHelpChip
    {
        internal struct Parts
        {
            public Button Chip;
            public VisualElement Card;
        }

        /// <summary>A chip + hidden card, with the chip wired to spring-toggle the card. Add both to your layout
        /// (chip inline with the label/row; card on its own line below).</summary>
        internal static Parts Build(string text, bool warning = false, bool markdown = true,
                                    bool startOpen = false, string tooltip = "Show help")
        {
            var chip = MakeChip(tooltip);
            var card = MakeCard(text, warning, markdown);
            chip.clicked += () => Toggle(card, chip);
            if (startOpen) { card.style.display = DisplayStyle.Flex; StyleChip(chip, true); }
            return new Parts { Chip = chip, Card = card };
        }

        /// <summary>The round "?" reveal chip. Outlined accent at rest, solid accent when its card is open.</summary>
        internal static Button MakeChip(string tooltip = "Show help")
        {
            var chip = new Button { text = "?" };
            chip.style.width = 18; chip.style.height = 18;
            chip.style.minWidth = 18; chip.style.minHeight = 18;
            chip.style.marginLeft = 6; chip.style.marginRight = 0; chip.style.marginTop = 0; chip.style.marginBottom = 0;
            chip.style.paddingLeft = 0; chip.style.paddingRight = 0; chip.style.paddingTop = 0; chip.style.paddingBottom = 0;
            chip.style.fontSize = 11;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.unityTextAlign = TextAnchor.MiddleCenter;
            Round(chip, 9);
            chip.tooltip = tooltip;
            StyleChip(chip, false);
            EditorSpringFX.ButtonSpring(chip);
            return chip;
        }

        /// <summary>The hidden info card: an accent-soft (or amber warning) wash with a left accent stripe.</summary>
        internal static VisualElement MakeCard(string text, bool warning = false, bool markdown = true)
        {
            var card = new VisualElement();
            card.style.display = DisplayStyle.None;
            card.style.marginTop = 5; card.style.marginBottom = 3;
            card.style.paddingLeft = 9; card.style.paddingRight = 9; card.style.paddingTop = 7; card.style.paddingBottom = 7;
            Round(card, 6);

            var stripe = warning ? DesignerPalette.ToastWarning : DesignerPalette.Accent;
            card.style.backgroundColor = warning ? DesignerPalette.WarningBg : DesignerPalette.AccentSoft;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = stripe;

            var label = new Label(text ?? "");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            label.style.color = warning ? DesignerPalette.WarningText : DesignerPalette.TextBody;
            if (markdown && !string.IsNullOrEmpty(text)) SasquatchMarkdown.ApplyMarkdownToLabel(label, text);

            // UI Toolkit under-measures a wrapping Label revealed from display:none (it measures against an
            // unbounded width → one line), so the card ends up too short and clips. Recompute the true wrapped
            // height once the label's width is valid and pin the card's minHeight. (Mirrors the settings page.)
            float lastCardMinH = -1f;
            label.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float avail = label.contentRect.width;
                if (avail <= 1f) return;
                float textH = label.MeasureTextSize(label.text, avail, VisualElement.MeasureMode.Exactly,
                                                    0f, VisualElement.MeasureMode.Undefined).y;
                if (Mathf.Abs(textH - label.resolvedStyle.height) > 0.5f) label.style.height = textH;
                float needed = textH + card.resolvedStyle.paddingTop + card.resolvedStyle.paddingBottom;
                if (Mathf.Abs(needed - lastCardMinH) > 0.5f) { lastCardMinH = needed; card.style.minHeight = needed; }
            });

            card.Add(label);
            return card;
        }

        /// <summary>Toggles a card's visibility, springing it in and syncing the chip's active state.</summary>
        internal static void Toggle(VisualElement card, Button chip)
        {
            bool opening = card.style.display.value == DisplayStyle.None;
            card.style.display = opening ? DisplayStyle.Flex : DisplayStyle.None;
            if (opening) EditorSpringFX.PopIn(card, 0.97f, 0.35f);
            StyleChip(chip, opening);
        }

        private static void StyleChip(Button chip, bool open)
        {
            var accent = DesignerPalette.Accent;
            chip.style.backgroundColor = open ? accent : DesignerPalette.AccentSoft;
            chip.style.color = open ? Color.white : accent;
            SetBorder(chip, 1, accent);
        }

        private static void Round(VisualElement ve, float radius)
        {
            ve.style.borderTopLeftRadius = radius; ve.style.borderTopRightRadius = radius;
            ve.style.borderBottomLeftRadius = radius; ve.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement ve, float width, Color color)
        {
            ve.style.borderTopWidth = width; ve.style.borderBottomWidth = width;
            ve.style.borderLeftWidth = width; ve.style.borderRightWidth = width;
            ve.style.borderTopColor = color; ve.style.borderBottomColor = color;
            ve.style.borderLeftColor = color; ve.style.borderRightColor = color;
        }
    }
}
