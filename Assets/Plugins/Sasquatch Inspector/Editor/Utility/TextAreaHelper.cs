using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>Shared helpers for the multi-line "Message" / body TextFields in the designer + quick-edit panels.</summary>
    internal static class TextAreaHelper
    {
        /// <summary>
        /// Turns a TextField into a wrapping, content-growing text area: long messages WRAP to the panel width
        /// (instead of clipping a single long line with a horizontal scrollbar) and the box grows with content
        /// between <paramref name="minHeight"/> and <paramref name="maxHeight"/>, then scrolls vertically.
        ///
        /// <para>The wrap is the fiddly bit: setting <c>white-space</c> on the field isn't enough — Unity's
        /// default field USS pins the inner input (and its nested text element) to <c>nowrap</c>, so we set it on
        /// the input AND every descendant. This mirrors the working pattern in <c>BindingFactory.Text</c>.</para>
        /// </summary>
        internal static void MakeWrappingTextArea(TextField field, float minHeight = 48f, float maxHeight = 180f)
        {
            if (field == null) return;
            field.multiline = true;
            field.verticalScrollerVisibility = ScrollerVisibility.Auto;
            field.style.minHeight = minHeight;     // grows with content from here…
            field.style.maxHeight = maxHeight;     // …up to here, then scrolls
            field.style.whiteSpace = WhiteSpace.Normal;
            // UITK's default flex-shrink is 0 (not 1 like CSS), so a multiline field with long content keeps its
            // large preferred width and bleeds WIDER than sibling rows (which leave room for their trailing ↺/T
            // button). flexShrink = 1 lets it shrink to the row width — matching the Tooltip/Label fields exactly.
            field.style.flexShrink = 1;

            ApplyWrap(field);
            // The input exists at construction, but re-apply on attach in case a Unity version builds it lazily.
            field.RegisterCallback<AttachToPanelEvent>(_ => ApplyWrap(field));
            ApplyMultilineSelectionBehaviour(field);
        }

        /// <summary>
        /// Makes a MULTI-LINE field's mouse selection behave the way a text area is expected to, and the way
        /// Unity's own <c>[TextArea]</c> PropertyField already does. Two separate defects, both measured:
        ///
        /// <para><b>1. Click placed a caret, then select-all stole it.</b> A bare <c>TextField</c> keeps
        /// <c>selectAllOnFocus</c>/<c>selectAllOnMouseUp</c> at their single-line-value defaults (both TRUE), so
        /// clicking into the box put the caret where you clicked on pointer-DOWN and then highlighted the whole
        /// body on pointer-UP — the next keystroke wipes everything you wrote. Unity's own multi-line
        /// <c>[TextArea]</c> field reports BOTH flags false (verified live), so this is a parity break, not a
        /// preference: a text area is an authoring surface, not a value you retype.</para>
        ///
        /// <para><b>2. A selection drag that never got its release stayed live forever.</b> Unity's text
        /// selecting manipulator enters "clicking" mode on pointer-down and only leaves it on a pointer-UP
        /// delivered to the text; a release it never sees — swallowed by a rebuild, lost to an IMGUI hot
        /// control, or made outside the window — leaves it selecting, so afterwards merely MOVING the mouse
        /// with no button held keeps extending the highlight. There is no recovery of its own and no public
        /// state to inspect, so we track the press ourselves and, on the first move that arrives with no
        /// button actually held, hand the element the pointer-up it never got. Same "the button is no longer
        /// pressed, so stop dragging" safety net <c>CanvasDragManipulator</c> already keeps.</para>
        /// </summary>
        internal static void ApplyMultilineSelectionBehaviour(TextField field)
        {
            if (field == null) return;
            Apply(field);
            // The inner text element can be built lazily, and a rebind can replace it — re-apply on attach.
            field.RegisterCallback<AttachToPanelEvent>(_ => Apply(field));
        }

        // Guard against double-wiring: ApplyMultilineSelectionBehaviour re-runs on every attach, and the text
        // element survives a detach, so a bare RegisterCallback would stack a fresh set on every dock/undock.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object> _selectionWired = new();

        private static void Apply(TextField field)
        {
            var te = SelectableTextElement(field);
            if (te?.selection == null) return;

            te.selection.selectAllOnFocus = false;
            te.selection.selectAllOnMouseUp = false;

            if (_selectionWired.TryGetValue(te, out _)) return;
            _selectionWired.Add(te, null);

            bool pressed = false;
            bool recoveryQueued = false;

            // The release has to be handed over from OUTSIDE an event dispatch: SendEvent during one is queued,
            // not run, and the pooled event would be recycled before it ever reached the manipulator. A
            // scheduler tick is the first moment that is safely outside. `pressed` deliberately stays true
            // until that release actually lands, so a lost tick can never leave the guard disarmed.
            void QueueRecovery()
            {
                if (recoveryQueued) return;
                recoveryQueued = true;
                te.schedule.Execute(() =>
                {
                    recoveryQueued = false;
                    if (!pressed || te.panel == null) return;
                    using (var up = PointerUpEvent.GetPooled(new Event
                           { type = EventType.MouseUp, button = 0, mousePosition = Vector2.zero, clickCount = 1 }))
                    {
                        up.target = te;
                        te.SendEvent(up);
                    }
                }).ExecuteLater(0);
            }

            // TrickleDown throughout, so these run BEFORE the selecting manipulator's own handlers on this
            // same element — which is what lets the move guard below suppress the phantom selection outright
            // rather than undoing it after the fact.
            te.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) pressed = true; }, TrickleDown.TrickleDown);
            te.RegisterCallback<PointerUpEvent>(_ => pressed = false, TrickleDown.TrickleDown);
            te.RegisterCallback<PointerMoveEvent>(evt =>
            {
                // The gate is the IMGUI event BEHIND this move, not `evt.pressedButtons`. Measured: in the
                // stuck state pressedButtons still reads 1, because UI Toolkit's own button state is cleared
                // by the very pointer-up that went missing — it is a restatement of the bug, not a test for
                // it. Unity only ever emits MouseDrag while a button is genuinely held and MouseMove when it
                // is not, so the underlying event tells the truth the pointer state cannot:
                //   clean hover  -> MouseMove, pressedButtons 0
                //   real drag    -> MouseDrag, pressedButtons 1
                //   STUCK        -> MouseMove, pressedButtons 1   <- only Event.current separates this one
                // A null Event.current means the move came from somewhere with no IMGUI event behind it; that
                // is no evidence either way, so leave it alone.
                var imgui = Event.current;
                if (!pressed || imgui == null || imgui.type != EventType.MouseMove) return;
                // Swallow the move so the manipulator cannot extend the selection with it (verified: a
                // TrickleDown StopImmediatePropagation here does stop it), then hand over the missing release.
                evt.StopImmediatePropagation();
                QueueRecovery();
            }, TrickleDown.TrickleDown);
            // Losing the capture without a release is the other way in (a mid-drag rebuild, another element
            // taking the pointer). Harmless after a normal release: `pressed` is already false by then.
            te.RegisterCallback<PointerCaptureOutEvent>(_ => { if (pressed) QueueRecovery(); });
        }

        /// <summary>
        /// The field's EDITABLE text element — the one carrying the selection and its manipulator. A plain
        /// <c>Q&lt;TextElement&gt;()</c> is wrong here: <c>Label</c> derives from <c>TextElement</c>, so on a
        /// field that kept its label (every Text widget except the rich-text one, which clears it) the label
        /// comes back first and the real input is never touched. Asking for the selectable one is exact.
        /// </summary>
        private static TextElement SelectableTextElement(TextField field)
        {
            TextElement found = null;
            field.Query<TextElement>().ForEach(el =>
            {
                if (found == null && el.selection != null && el.selection.isSelectable) found = el;
            });
            return found;
        }

        private static void ApplyWrap(TextField field)
        {
            var input = field.Q<VisualElement>(className: "unity-text-field__input")
                     ?? field.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input == null) return;
            input.style.whiteSpace = WhiteSpace.Normal;
            input.Query<VisualElement>().ForEach(el => el.style.whiteSpace = WhiteSpace.Normal);
        }

        // Dragging the grip to/below this height reverts the field to its natural (auto) height.
        private const float CollapseHeight = 28f;

        /// <summary>
        /// Like <see cref="MakeWrappingTextArea"/>, plus a bottom drag-grip so the author can DRAG the box
        /// taller/shorter — the same "expandable" feel as the runtime Text widget, but for the designer's
        /// Properties panel. The wrap keeps long text inside the panel width (no horizontal scrollbar); the
        /// grip lets a long message be grown to taste, and dragging it back down to ~one line returns it to
        /// the auto height. Resize is ephemeral (an authoring affordance — it resets when the panel rebuilds);
        /// the grip's cursor + look come from the shared <c>.sasq-text-resizer</c> USS class, mirroring
        /// <c>BindingFactory.Text</c>'s runtime grip.
        /// </summary>
        internal static void MakeWrappingResizableTextArea(TextField field, float minHeight = 56f, float maxHeight = 320f)
        {
            if (field == null) return;
            MakeWrappingTextArea(field, minHeight, maxHeight);
            AttachResizeGrip(field, minHeight, maxHeight);
        }

        // A discreet centered grabber bar pinned to the bottom edge of the input. The full-width outer strip
        // only POSITIONS the bar and is non-pickable; the inner "grab" zone is the sole drag target, so a click
        // anywhere else along the bottom edge falls through to the text field for selecting characters.
        private static void AttachResizeGrip(TextField field, float minHeight, float maxHeight)
        {
            var handle = new VisualElement { pickingMode = PickingMode.Ignore };
            handle.style.position = Position.Absolute;
            handle.style.left = 0;
            handle.style.right = 0;
            handle.style.bottom = 0;
            handle.style.height = 8;
            handle.style.flexDirection = FlexDirection.Row;
            handle.style.justifyContent = Justify.Center;
            handle.style.alignItems = Align.FlexEnd;

            var grab = new VisualElement();
            grab.AddToClassList("sasq-text-resizer"); // resize-vertical cursor (SasquatchWidgets.uss)
            grab.style.width = 34;
            grab.style.height = 8;
            grab.style.flexDirection = FlexDirection.Row;
            grab.style.alignItems = Align.FlexEnd;
            grab.style.justifyContent = Justify.Center;

            var pill = new VisualElement { pickingMode = PickingMode.Ignore };
            pill.style.height = 3;
            pill.style.marginBottom = 2;
            pill.style.borderTopLeftRadius = 2;
            pill.style.borderTopRightRadius = 2;
            pill.style.borderBottomLeftRadius = 2;
            pill.style.borderBottomRightRadius = 2;
            pill.style.backgroundColor = new Color(0.6f, 0.6f, 0.6f, 0.4f);
            pill.style.flexGrow = 1; // fills the grab zone's width
            grab.Add(pill);
            handle.Add(grab);

            grab.RegisterCallback<MouseEnterEvent>(_ => pill.style.backgroundColor = new Color(0.5f, 0.7f, 1f, 0.85f));
            grab.RegisterCallback<MouseLeaveEvent>(_ => pill.style.backgroundColor = new Color(0.6f, 0.6f, 0.6f, 0.4f));

            void AddHandle()
            {
                var input = field.Q<VisualElement>(className: "unity-text-field__input")
                         ?? field.Q<VisualElement>(className: "unity-base-text-field__input");
                if (input != null) { if (handle.parent != input) input.Add(handle); }
                else if (handle.parent == null) field.hierarchy.Add(handle);
            }
            AddHandle();
            // The input may build lazily on some Unity versions — re-attach on panel attach.
            field.RegisterCallback<AttachToPanelEvent>(_ => AddHandle());

            bool resizing = false;
            float startY = 0f, startHeight = 0f;
            grab.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                startY = evt.position.y;
                startHeight = field.worldBound.height > 1f ? field.worldBound.height : minHeight;
                field.style.maxHeight = StyleKeyword.None; // let the drag grow past the auto-grow cap
                grab.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grab.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                float raw = startHeight + (evt.position.y - startY);
                if (raw <= CollapseHeight)
                {
                    // Dragged to ~one line: clear the explicit height (back to auto) and restore the cap.
                    field.style.height = StyleKeyword.Null;
                    field.style.maxHeight = maxHeight;
                }
                else
                {
                    field.style.height = raw;
                }
                evt.StopPropagation();
            });
            grab.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (grab.HasPointerCapture(evt.pointerId)) grab.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grab.RegisterCallback<PointerCaptureOutEvent>(_ => resizing = false);
        }
    }
}
