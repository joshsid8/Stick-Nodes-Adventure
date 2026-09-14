using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Pointer manipulator on draggable elements (designer cards, collection rows, palette rows): owns the
    /// press → threshold → capture handshake and forwards to the <see cref="CanvasDragController"/>. No pointer
    /// is captured until a drag actually begins, so buttons and clicks keep working.
    /// </summary>
    public class CanvasDragManipulator : PointerManipulator
    {
        private readonly CanvasDragController _controller;
        private readonly CanvasDragPayload _payload;
        private readonly bool _dragAnywhere;

        private Vector2 _start;
        private int _pointerId;
        private bool _pending;
        private bool _dragging;
        private bool _canDrag;
        private VisualElement _releaseWatchRoot; // panel root we listen to for "released somewhere else"

        // dragAnywhere is used by the hierarchy tree, whose rows have no interactive body — only the canvas
        // needs the grip rule.
        /// <summary>Creates the manipulator; <paramref name="dragAnywhere"/> lets the whole row start a drag (no grip required).</summary>
        public CanvasDragManipulator(CanvasDragController controller, CanvasDragPayload payload, bool dragAnywhere = false)
        {
            _controller = controller;
            _payload = payload;
            _dragAnywhere = dragAnywhere;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            if (target.panel != null) HookPanelRelease(target.panel); // added to an already-live element
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            UnhookPanelRelease();
            _pending = false;
        }

        // ---- Stale-press safety net --------------------------------------------------------------------
        //
        // A press arms _pending and the matching RELEASE is what disarms it — but there is no guarantee the
        // release reaches this element. The press handler's own click callbacks routinely rebuild the panel
        // (selecting a hierarchy row re-renders the whole tree; double-clicking one also scrolls and springs
        // it), so by mouse-up the pressed element can be detached, replaced, or moved out from under the
        // cursor. The release then lands somewhere else, this manipulator never hears it, and _pending sits
        // armed — so the NEXT bare mouse move (no button held) starts a phantom drag of whatever was pressed
        // however long ago. Two independent guards below, because one release can miss in more than one way.

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            HookPanelRelease(evt.destinationPanel);
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // GUARD 1 — the pressed element left the panel (almost always a rebuild triggered by the very
            // click that armed us). A detached element receives no further events, so this press can never
            // legitimately become a drag, and the flag must not survive into whatever reuses this instance.
            _pending = false;
            UnhookPanelRelease();
        }

        private void HookPanelRelease(IPanel panel)
        {
            var root = panel?.visualTree;
            if (root == null || ReferenceEquals(root, _releaseWatchRoot)) return;
            UnhookPanelRelease();
            _releaseWatchRoot = root;
            // TrickleDown so we see the release before it reaches whatever it landed on — including elements
            // that StopPropagation it (the case the OnPointerMove comment below already had to work around).
            root.RegisterCallback<PointerUpEvent>(OnPanelPointerUp, TrickleDown.TrickleDown);
        }

        private void UnhookPanelRelease()
        {
            if (_releaseWatchRoot == null) return;
            _releaseWatchRoot.UnregisterCallback<PointerUpEvent>(OnPanelPointerUp, TrickleDown.TrickleDown);
            _releaseWatchRoot = null;
        }

        // GUARD 2 — a release ANYWHERE in the window disarms a pending press. Releases that actually land on
        // us are left alone: OnPointerUp still needs _pending to route the click (and this runs first, in the
        // trickle-down phase). _dragging is likewise untouched — a live drag holds pointer capture, so its
        // release is delivered here regardless of where the cursor is, and CommitDrag owns that path.
        private void OnPanelPointerUp(PointerUpEvent evt)
        {
            if (!_pending) return;
            for (var v = evt.target as VisualElement; v != null; v = v.hierarchy.parent)
                if (ReferenceEquals(v, target)) return;
            _pending = false;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            if (_controller.IsDragging) return;
            if (IsOnButton(evt.target as VisualElement)) return;

            // In Interact mode / while Alt is held, leaf field cards on the canvas yield to their field
            // so the value can be edited. Containers still drag from their grip. Hierarchy rows
            // (dragAnywhere) have no editable body, so they ignore this.
            if (!_dragAnywhere && _payload.Kind == CanvasDragPayload.DragKind.MoveNode && _controller.FieldsInteractive &&
                !(_payload.Node is ContainerNode) && !(_payload.Node is CollectionNode))
                return;

            // For node moves, only the deepest node under the cursor arms (handlers exist on
            // every ancestor); otherwise a parent container would hijack the drag.
            if (_payload.Kind == CanvasDragPayload.DragKind.MoveNode && !IsDeepestNode(evt.target as VisualElement))
                return;

            _start = evt.position;
            _pointerId = evt.pointerId;
            _pending = true;
            _dragging = false;
            // A double-click press still ARMS (the release routes the click, and the Tab Organizer + the
            // hierarchy's ping both act on clickCount >= 2) but can never become a DRAG. Nobody double-clicks
            // and then drags without letting go, and this is exactly the press whose handler is most likely to
            // yank the element out from under the cursor — pinging a hierarchy row scrolls and springs it.
            _canDrag = evt.clickCount < 2 && ComputeCanDrag(evt.target as VisualElement);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragging)
            {
                // Safety check: if mouse button is no longer pressed, commit the drag to prevent sticking!
                if ((evt.pressedButtons & 1) == 0)
                {
                    _dragging = false;
                    _controller.CommitDrag();
                    if (target.HasPointerCapture(_pointerId)) target.ReleasePointer(_pointerId);
                    evt.StopPropagation();
                    return;
                }

                _controller.UpdateDrag(evt.position);
                evt.StopPropagation();
                return;
            }

            if (!_pending) return;

            // A drag may only BEGIN while the primary button is genuinely held. If it's already up,
            // this _pending is stale: the release never reached us — a descendant StopPropagation'd it
            // (lists nest a MoveNode body card inside a MoveCollectionItem row), or the pressed element
            // was rebuilt/moved out from under the cursor before mouse-up. Clear it so a stray
            // post-release move can't start a phantom drag — the "whole list flashes a drop outline and
            // snaps back on click-release" bug, and its twin where a row drags itself with no button held.
            //
            // This test sits ABOVE the _canDrag gate on purpose: a press that can't drag still has to clean
            // up after itself, or the flag lives on until the element dies.
            if ((evt.pressedButtons & 1) == 0)
            {
                _pending = false;
                return;
            }

            if (!_canDrag) return;
            if (_controller.IsDragging) return;
            if (Vector2.Distance(evt.position, _start) <= DesignerPalette.DragThreshold) return;

            _pending = false;
            _dragging = true;
            target.CapturePointer(_pointerId);
            _controller.BeginDrag(_payload, target, evt.position);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragging)
            {
                _dragging = false;
                _controller.CommitDrag();
                if (target.HasPointerCapture(_pointerId)) target.ReleasePointer(_pointerId);
                evt.StopPropagation();
            }
            else if (_pending)
            {
                _controller.HandleClick(_payload, evt.actionKey, evt.shiftKey, evt.clickCount);
                evt.StopPropagation();
            }
            _pending = false;
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragging)
            {
                _dragging = false;
                _controller.CancelDrag();
            }
            _pending = false;
        }

        private bool ComputeCanDrag(VisualElement pressed)
        {
            if (_payload.Kind != CanvasDragPayload.DragKind.MoveNode) return true;
            if (_dragAnywhere) return true;

            bool leaf = !(_payload.Node is ContainerNode) && !(_payload.Node is CollectionNode);
            if (leaf) return true;

            // Containers / collections drag only from the grip handle.
            VisualElement v = pressed;
            while (v != null && v != target)
            {
                if (v.ClassListContains("designer-grip") || v.ClassListContains("collection-item-grip")) return true;
                v = v.parent;
            }
            return false;
        }

        private bool IsDeepestNode(VisualElement from)
        {
            VisualElement v = from;
            while (v != null)
            {
                if (v.userData is LayoutNode ln) return ln == _payload.Node;
                v = v.parent;
            }
            return false;
        }

        private bool IsOnButton(VisualElement from)
        {
            VisualElement v = from;
            while (v != null)
            {
                if (v is Button) return true;
                if (v == target) return false;
                v = v.parent;
            }
            return false;
        }
    }
}
