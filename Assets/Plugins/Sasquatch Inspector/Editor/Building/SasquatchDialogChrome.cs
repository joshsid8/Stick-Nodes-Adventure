using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The shared chrome for the small UI Toolkit dialogs an action button can raise — the argument prompt
    /// (<see cref="ButtonArgumentWindow"/>) and the confirm gate (<see cref="ButtonConfirmWindow"/>). Both fire
    /// from the SAME click, so they have to look like siblings; keeping the padding, heading, button row, key
    /// handling and centring in one place is what stops them drifting apart. Neither is a stock
    /// <c>EditorUtility.DisplayDialog</c> — that is a native, light-skinned box that can't carry this chrome.
    /// </summary>
    internal static class SasquatchDialogChrome
    {
        internal const float DialogWidth = 340f;

        /// <summary>
        /// Centres <paramref name="window"/> over the focused editor window at an INTEGRAL origin. The rounding
        /// is the point: a host with an odd width or height otherwise lands the dialog on a half-pixel, which
        /// offsets every glyph quad in it and visibly smears the text (it is not a UITK-vs-IMGUI artifact — a
        /// UITK dialog parked on .5 blurs just the same).
        /// </summary>
        internal static void CenterOnFocused(EditorWindow window, float width, float height)
        {
            if (window == null) return;
            var host = EditorWindow.focusedWindow;
            Rect anchor = host != null ? host.position : new Rect(300f, 300f, 600f, 400f);
            window.position = new Rect(
                Mathf.Round(anchor.x + Mathf.Max(0f, (anchor.width - width) / 2f)),
                Mathf.Round(anchor.y + Mathf.Max(0f, (anchor.height - height) / 2f)),
                width, height);
            window.minSize = new Vector2(width, Mathf.Min(height, 120f));
        }

        /// <summary>
        /// Applies the house skin + padding to a dialog root. These roots sit outside any build root, so the
        /// --sasq-* tokens have to be applied here for the widget classes to resolve (the aux-window pattern
        /// shared with the curve/gradient editors).
        /// </summary>
        internal static void PrepareRoot(VisualElement root)
        {
            if (root == null) return;
            SasquatchSkin.ApplyWithWidgetSheet(root);
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;
        }

        /// <summary>The bold heading line ("Enter arguments", the confirm title).</summary>
        internal static Label Heading(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = DesignerPalette.TextHeader;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 6;
            return label;
        }

        /// <summary>
        /// The right-aligned action row, in the HOST PLATFORM's button order.
        ///
        /// Order is an OS convention rather than a preference: Windows puts the commit button first
        /// (<c>[OK] [Cancel]</c>), while macOS puts the action button rightmost (<c>[Cancel] [OK]</c>, per Apple's
        /// HIG) — GNOME and Material agree with macOS. Unity's own <c>EditorUtility.DisplayDialog</c> defers to the
        /// platform the same way, so mirroring it is what keeps these dialogs agreeing with BOTH the OS and the
        /// rest of the editor (including the DisplayDialog calls still used elsewhere in this tool) on whichever
        /// machine a buyer is on. Keyboard order is unaffected — Enter always confirms, Escape always cancels.
        /// </summary>
        internal static VisualElement ButtonRow(string cancelText, Action onCancel, string confirmText, Action onConfirm)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 8;
            row.style.flexShrink = 0;

            var cancel = MakeDialogButton(cancelText, onCancel);
            var confirm = MakeDialogButton(confirmText, onConfirm);

#if UNITY_EDITOR_OSX
            VisualElement first = cancel, second = confirm;
#else
            VisualElement first = confirm, second = cancel;
#endif
            second.style.marginLeft = 6; // the gap belongs to whichever button ended up second
            row.Add(first);
            row.Add(second);

            return row;
        }

        private static Button MakeDialogButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.width = 80;
            button.style.height = 22;
            EditorSpringFX.ButtonSpring(button);
            return button;
        }

        /// <summary>
        /// Enter submits, Escape cancels — the keyboard mirror of the two buttons. TrickleDown so a focused
        /// text field can't swallow either key first; the submit path blurs the focused field so its edit is
        /// committed before the caller reads any collected values.
        /// </summary>
        internal static void WireKeys(VisualElement root, Action onSubmit, Action onCancel)
        {
            if (root == null) return;
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    (evt.target as VisualElement)?.Blur();
                    onSubmit?.Invoke();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    onCancel?.Invoke();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }
    }
}
