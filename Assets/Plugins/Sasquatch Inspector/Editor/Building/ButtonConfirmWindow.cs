using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The "Confirm before execute" gate for an action button, as a UI Toolkit dialog sharing
    /// <see cref="SasquatchDialogChrome"/> with the argument prompt.
    ///
    /// Deliberately NOT <c>EditorUtility.DisplayDialog</c>: that draws a native, light-skinned box with a Unity
    /// logo in it — jarring next to the argument prompt, given both can fire from the SAME button click.
    /// Callback-based rather than a blocking modal, so the caller keeps its deferred-invoke shape.
    /// </summary>
    internal sealed class ButtonConfirmWindow : EditorWindow
    {
        private string _message;
        private Action _onConfirm;

        /// <summary>Opens the confirm gate; <paramref name="onConfirm"/> runs only if the user accepts.</summary>
        public static void Show(string title, string message, Action onConfirm)
        {
            var window = CreateInstance<ButtonConfirmWindow>();
            window.titleContent = new GUIContent(string.IsNullOrEmpty(title) ? "Confirm" : title);
            window._message = string.IsNullOrEmpty(message) ? "Are you sure you want to do this?" : message;
            window._onConfirm = onConfirm;

            // Rough wrap estimate at this width, clamped: the message sits in a ScrollView, so an underestimate
            // scrolls rather than clipping — it never silently swallows the end of a long warning.
            int lines = Mathf.Clamp(Mathf.CeilToInt(window._message.Length / 42f), 1, 8);
            float height = 66f + lines * 16f + 34f;
            SasquatchDialogChrome.CenterOnFocused(window, SasquatchDialogChrome.DialogWidth, height);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            if (root == null) return;

            // A domain reload revives the window without its callback — there is nothing left to confirm.
            if (_onConfirm == null)
            {
                EditorApplication.delayCall += Close;
                return;
            }

            SasquatchDialogChrome.PrepareRoot(root);
            root.Add(SasquatchDialogChrome.Heading(titleContent.text));

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            var message = new Label(_message);
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.color = DesignerPalette.TextBody;
            body.Add(message);
            root.Add(body);

            root.Add(SasquatchDialogChrome.ButtonRow("Cancel", Cancel, "Yes", Confirm));
            SasquatchDialogChrome.WireKeys(root, Confirm, Cancel);
            EditorSpringFX.PopIn(root);
        }

        private void Cancel() => Close();

        private void Confirm()
        {
            var run = _onConfirm;
            _onConfirm = null; // one-shot: Enter and a click must never both fire the action
            // Register the continuation BEFORE Close() — Close() destroys this window, so anything that still
            // needs doing is queued while the instance is alive. The hop is still deferred either way (the
            // caller's action may pop further UI, and it must not run inside this window's teardown).
            if (run != null) EditorApplication.delayCall += () => run();
            Close();
        }
    }
}
