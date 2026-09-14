using System;
using System.Collections.Generic;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>One held card's display info for the stacked drag ghost (icon/label/kind + its true on-canvas height).</summary>
    public struct GhostCardInfo
    {
        public string Icon;
        public string Label;
        public string Kind;
        public float Height;
    }

    // A deliberate facsimile, not a clone: UI Toolkit can't clone elements, and rebuilding the tree
    // for a ghost would re-register spring tabs/drop zones and need a bound SerializedObject.
    /// <summary>
    /// The card facsimile that follows the cursor during a drag (header + translucent body), positioned via
    /// transform rather than layout. Multi-drags stack one facsimile per held card at its true height.
    /// </summary>
    public class DragGhost
    {
        private readonly VisualElement _overlay;
        private VisualElement _root;
        private VisualElement _body;
        private Action _flightDone;
        private bool _hasFollowed;

        // Cursor follow: fast with a trace of organic lag, so the stack drags like it
        // has a little weight. Drop targeting always uses the real cursor, not the
        // ghost, so the lag is purely cosmetic. (Pixel springs — 0.5px settle epsilon
        // so settle-gated callbacks fire at visual arrival, not after ~1s of
        // sub-pixel ringing.)
        private static readonly SpringConfig FollowConfig = new SpringConfig(0.18f, 0f, 0.5f);
        // Cancel flight home: quick, with a hint of elastic landing.
        private static readonly SpringConfig FlightConfig = new SpringConfig(0.3f, 0.25f, 0.5f);

        // Stack-mode state, kept so the ghost can morph between canvas presentation (true heights +
        // spacing) and hierarchy presentation (uniform row height, touching) as the pointer crosses
        // regions mid-drag.
        private readonly List<VisualElement> _stackCards = new();
        private readonly List<VisualElement> _stackBodies = new();
        private readonly List<float> _stackHeights = new();
        private float _stackSpacing;
        private int _grabbedIndex;
        private bool _hierMode;
        private float _hierRowH = 18f;
        // Region-local WIDTH morph: full card width over the canvas, the region's row width when compact
        // (organizer column / hierarchy row). The root drives every stack card's width (they stretch).
        private float _canvasWidth = 200f;
        private float _compactWidth = 180f;
        private float _targetWidth;   // resolved landing-cell width (0 = none; shrink-only vs the mode default)

        /// <summary>Creates a ghost that draws on <paramref name="overlay"/>.</summary>
        public DragGhost(VisualElement overlay)
        {
            _overlay = overlay;
        }

        // Stack mode: the ghost is every held card, true heights, top-to-bottom in drop order.
        // Cards beyond the second fade progressively so a tall stack never fully hides the canvas.
        // startCompact builds the cards directly at the compact row width+height (touching) — an
        // organizer/hierarchy pickup must never flash canvas-sized for a frame and shrink.
        /// <summary>Shows a stacked multi-card ghost at the cards' true heights and spacing.</summary>
        public void Show(List<GhostCardInfo> cards, int grabbedIndex, float canvasWidth, float spacing,
            bool startCompact = false, float compactRowW = 180f, float compactRowH = 18f)
        {
            CancelFlight();
            Remove();

            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.AddToClassList("drag-ghost");
            _root.AddToClassList("drag-ghost--stack");

            _canvasWidth = Mathf.Clamp(canvasWidth, 120f, 320f);
            _compactWidth = Mathf.Clamp(compactRowW, 80f, 320f);
            _targetWidth = 0f;
            _hierMode = startCompact;
            if (startCompact) _hierRowH = Mathf.Max(12f, compactRowH);
            Width = _hierMode ? _compactWidth : _canvasWidth;
            _root.style.width = Width;

            _stackSpacing = spacing;
            _grabbedIndex = Mathf.Clamp(grabbedIndex, 0, cards.Count - 1);

            for (int i = 0; i < cards.Count; i++)
            {
                var info = cards[i];
                float trueH = Mathf.Max(info.Height, 24f);
                float h = _hierMode ? _hierRowH : trueH;

                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.AddToClassList("drag-ghost-stack-card");
                card.style.height = h; // exact: header + flex body fill the box
                ApplyCardPadding(card, _hierMode);
                if (i > 0) card.style.marginTop = _hierMode ? 0f : spacing;
                if (i >= 2) card.style.opacity = Mathf.Max(0.5f, 1f - 0.15f * (i - 1));

                var header = new VisualElement { pickingMode = PickingMode.Ignore };
                header.AddToClassList("drag-ghost-header");
                card.Add(header);

                // Tint passed in code: the icon element ignores USS `color`, so .drag-ghost-icon can't set it.
                var icon = SasquatchIcons.Create(info.Icon, 12, new Color32(0x2f, 0x7a, 0xd4, 0xff));
                icon.AddToClassList("drag-ghost-icon");
                header.Add(icon);

                var text = new Label(info.Label) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("drag-ghost-text");
                header.Add(text);

                var kindLbl = new Label(info.Kind) { pickingMode = PickingMode.Ignore };
                kindLbl.AddToClassList("drag-ghost-kind");
                header.Add(kindLbl);

                var body = new VisualElement { pickingMode = PickingMode.Ignore };
                body.AddToClassList("drag-ghost-body");
                body.style.flexGrow = 1f;
                if (_hierMode || trueH < 34f) body.style.display = DisplayStyle.None; // chip-sized: header only
                card.Add(body);

                _root.Add(card);
                _stackCards.Add(card);
                _stackBodies.Add(body);
                _stackHeights.Add(trueH); // canvas height, restored by SetCanvasMode
            }
            GrabbedOffsetY = StackOffsetAt(_grabbedIndex);

            _overlay.Add(_root);

            // Spawn flourish: one frame at scale .96 / opacity 0, then the USS transition eases in.
            var captured = _root;
            _root.schedule.Execute(() => captured.AddToClassList("drag-ghost--live"));
        }

        // Compact presentation (organizer/hierarchy): every card at the region's row WIDTH and HEIGHT,
        // touching (no spacing), body hidden — what the rows will actually look like when dropped there.
        // Re-applies whenever the region's dims change (organizer columns vs hierarchy rows differ), so
        // the ghost stays "local" to whatever it's over. Inline width/height/margin ride the transition.
        /// <summary>Switches the ghost to a compact row-sized chip (list/table row drags).</summary>
        public void SetCompactMode(float rowW, float rowH)
        {
            if (_stackCards.Count == 0) return;
            rowH = Mathf.Max(12f, rowH);
            rowW = Mathf.Clamp(rowW, 80f, 320f);
            if (_hierMode && Mathf.Approximately(rowH, _hierRowH) && Mathf.Approximately(rowW, _compactWidth)) return;
            _hierMode = true;
            _hierRowH = rowH;
            _compactWidth = rowW;
            ApplyWidth();
            for (int i = 0; i < _stackCards.Count; i++)
            {
                _stackCards[i].style.height = rowH;
                if (i > 0) _stackCards[i].style.marginTop = 0f;
                _stackBodies[i].style.display = DisplayStyle.None;
                ApplyCardPadding(_stackCards[i], true);
            }
            GrabbedOffsetY = StackOffsetAt(_grabbedIndex);
        }

        // Morph back to canvas presentation: full card width + true heights + canvas spacing.
        /// <summary>Switches the ghost back to full canvas-card size.</summary>
        public void SetCanvasMode()
        {
            if (!_hierMode || _stackCards.Count == 0) return;
            _hierMode = false;
            ApplyWidth();
            for (int i = 0; i < _stackCards.Count; i++)
            {
                _stackCards[i].style.height = _stackHeights[i];
                if (i > 0) _stackCards[i].style.marginTop = _stackSpacing;
                _stackBodies[i].style.display = _stackHeights[i] < 34f ? DisplayStyle.None : DisplayStyle.Flex;
                ApplyCardPadding(_stackCards[i], false);
            }
            GrabbedOffsetY = StackOffsetAt(_grabbedIndex);
        }

        // Target-cell sizing: the resolved drop reports the landing cell/pane width and the ghost
        // shrinks to match — a card headed into a half-width column shouldn't dangle over its
        // neighbor. Shrink-only: the mode default is the ceiling, so a full-width drop keeps it.
        // 0 restores the default. The .drag-ghost USS width transition animates the morph.
        /// <summary>Shrinks the ghost toward the resolved landing cell's width (0 = mode default).</summary>
        public void SetTargetWidth(float w)
        {
            _targetWidth = w;
            ApplyWidth();
        }

        private void ApplyWidth()
        {
            if (_root == null) return;
            float baseW = _hierMode ? _compactWidth : _canvasWidth;
            float w = _targetWidth > 0f ? Mathf.Clamp(_targetWidth, 90f, baseW) : baseW;
            if (Mathf.Approximately(w, Width)) return;
            Width = w;
            _root.style.width = w;
        }

        // Compact rows are short — trim vertical padding and center the single header line so the label
        // never clips at the bottom; restore the roomier canvas padding when expanded.
        private static void ApplyCardPadding(VisualElement card, bool compact)
        {
            if (compact)
            {
                card.style.paddingTop = 2;
                card.style.paddingBottom = 2;
                card.style.justifyContent = Justify.Center;
            }
            else
            {
                card.style.paddingTop = 5;
                card.style.paddingBottom = 6;
                card.style.justifyContent = Justify.FlexStart;
            }
        }

        // Tints the whole ghost red while it's over a remove target (Bindings panel / delete zone),
        // signaling that releasing here will unbind/remove rather than move.
        /// <summary>Tints the ghost red while it's over the remove zone.</summary>
        public void SetRemoveTint(bool on)
        {
            if (_root == null) return;
            if (on) _root.AddToClassList("drag-ghost--remove");
            else _root.RemoveFromClassList("drag-ghost--remove");
        }

        // Each card's CURRENT Y offset within the stack (mode-aware) — settle glides depart from
        // where the facsimiles actually are.
        /// <summary>The vertical offsets of the stacked ghost cards, so the indicator lines can match them.</summary>
        public float[] GetCardOffsets()
        {
            if (_stackCards.Count == 0) return null;
            var offsets = new float[_stackCards.Count];
            for (int i = 0; i < offsets.Length; i++) offsets[i] = StackOffsetAt(i);
            return offsets;
        }

        private float StackOffsetAt(int index)
        {
            if (_hierMode) return index * _hierRowH;
            float y = 0f;
            for (int i = 0; i < index && i < _stackHeights.Count; i++) y += _stackHeights[i] + _stackSpacing;
            return y;
        }

        /// <summary>Shows a single-card ghost for <paramref name="payload"/> picked up from <paramref name="source"/>.</summary>
        public void Show(CanvasDragPayload payload, VisualElement source, int groupCount)
        {
            CancelFlight();
            Remove();
            GrabbedOffsetY = 0f;

            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.AddToClassList("drag-ghost");

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("drag-ghost-header");
            _root.Add(header);

            // Tint passed in code: the icon element ignores USS `color`, so .drag-ghost-icon can't set it.
            var icon = SasquatchIcons.Create(payload.Icon, 12, new Color32(0x2f, 0x7a, 0xd4, 0xff));
            icon.AddToClassList("drag-ghost-icon");
            header.Add(icon);

            var text = new Label(payload.Label) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("drag-ghost-text");
            header.Add(text);

            var kind = new Label(KindLabel(payload)) { pickingMode = PickingMode.Ignore };
            kind.AddToClassList("drag-ghost-kind");
            header.Add(kind);

            if (groupCount > 1)
            {
                var count = new Label($"×{groupCount}") { pickingMode = PickingMode.Ignore };
                count.AddToClassList("drag-ghost-count");
                header.Add(count);
            }

            float srcW = source != null && source.worldBound.width > 1f ? source.worldBound.width : 200f;
            float srcH = source != null && source.worldBound.height > 1f ? source.worldBound.height : 40f;
            Width = Mathf.Clamp(srcW, 120f, 320f);
            _canvasWidth = Width; // ApplyWidth's ceiling — a single-card ghost's "mode default" is its shown size
            _targetWidth = 0f;
            _root.style.width = Width;

            _body = new VisualElement { pickingMode = PickingMode.Ignore };
            _body.AddToClassList("drag-ghost-body");
            float bodyH = Mathf.Clamp(srcH - 26f, 0f, 64f);
            if (bodyH < 8f)
            {
                // Chip-sized sources (palette tokens, slim rows) stay a pure chip.
                _body.style.display = DisplayStyle.None;
            }
            else
            {
                _body.style.height = bodyH;
            }
            _root.Add(_body);

            _overlay.Add(_root);

            // Spawn flourish: one frame at scale .96 / opacity 0, then the USS transition eases in.
            var captured = _root;
            _root.schedule.Execute(() => captured.AddToClassList("drag-ghost--live"));
        }

        public float Width { get; private set; } = 200f;

        // Y distance from the stack's top to the top of the card the user actually grabbed —
        // commit/cancel animations target the grabbed card, not the stack top. 0 for a single-card ghost.
        public float GrabbedOffsetY { get; private set; }

        public bool IsShown => _root != null;

        /// <summary>Moves the ghost to follow the pointer at <paramref name="overlayLocal"/>.</summary>
        public void Follow(Vector2 overlayLocal)
        {
            if (_root == null) return;

            // First placement snaps to the cursor — the ghost must never fly in from
            // (0,0). The spring takes over from there.
            if (!_hasFollowed || !EditorSpringFX.MotionEnabled)
            {
                _hasFollowed = true;
                _root.style.translate = new Translate(overlayLocal.x, overlayLocal.y);
                return;
            }

            _root.SpringTranslate(overlayLocal, EditorSpringFX.Tune(FollowConfig));
        }

        /// <summary>The ghost's current world-space top-left position.</summary>
        public Vector2 CurrentWorldPosition => _root != null ? (Vector2)_root.worldBound.position : Vector2.zero;

        // A sideways "no" impulse layered onto whatever the translate spring is doing —
        // combined with FlyBackTo it reads as the stack being tossed back.
        /// <summary>Plays a refusal shake (invalid drop).</summary>
        public void Shake()
        {
            if (_root == null || !EditorSpringFX.MotionEnabled) return;
            _root.PunchTranslateX(160f, EditorSpringFX.Tune(new SpringConfig(0.4f, 1.0f)));
        }

        // Cancel gesture: the ghost flies home to the (dimmed) source, then onDone restores it.
        /// <summary>Springs the ghost back to <paramref name="overlayLocal"/>, then invokes <paramref name="onDone"/> (drag cancelled).</summary>
        public void FlyBackTo(Vector2 overlayLocal, Action onDone)
        {
            if (_root == null) { onDone?.Invoke(); return; }
            CancelFlight();

            if (!EditorSpringFX.MotionEnabled)
            {
                _root.style.translate = new Translate(overlayLocal.x, overlayLocal.y);
                onDone?.Invoke();
                return;
            }

            _flightDone = onDone;
            WatchFlight(_root.SpringTranslate(overlayLocal, EditorSpringFX.Tune(FlightConfig)));
        }

        // The spring departs from wherever the lagging ghost visually is — no "from"
        // bookkeeping needed. _flightDone is claimed exactly once: CancelFlight or a
        // superseding flight may take it first, in which case this watcher no-ops.
        private async void WatchFlight(MotionHandleVector3 handle)
        {
            try
            {
                await handle.WaitFor(AwaitStrategy.PhysicalSettling);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                return; // ghost removed / engine shut down — CancelFlight handled the callback
            }

            var done = _flightDone;
            _flightDone = null;
            done?.Invoke();
        }

        /// <summary>Fades the ghost out and removes it (successful drop).</summary>
        public void FadeOutAndRemove()
        {
            if (_root == null) return;
            CancelFlight();
            var captured = _root;
            _root = null;
            _body = null;
            // Same bookkeeping as Remove() — _root is already null, so the next Show()'s Remove()
            // early-outs and would otherwise inherit this drag's stack state.
            _stackCards.Clear();
            _stackBodies.Clear();
            _stackHeights.Clear();
            _hierMode = false;
            _hasFollowed = false;

            if (!EditorSpringFX.MotionEnabled)
            {
                captured.RemoveFromHierarchy();
                return;
            }
            RemoveWhenFaded(captured);
        }

        private static async void RemoveWhenFaded(VisualElement captured)
        {
            try
            {
                await captured.SpringOpacity(0f, EditorSpringFX.Tune(SpringConfig.Snappy)).WaitFor(AwaitStrategy.VisualArrival);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                // already detached — fall through to the (idempotent) removal
            }
            captured.RemoveFromHierarchy();
        }

        // If a fly-back is still in the air when something else needs the ghost (a new drag), finish
        // its bookkeeping immediately so lifted sources never stay dimmed.
        /// <summary>Cancels any in-flight fly-back / fade animation.</summary>
        public void CancelFlight()
        {
            var done = _flightDone;
            _flightDone = null;
            done?.Invoke();
        }

        /// <summary>Removes the ghost immediately.</summary>
        public void Remove()
        {
            if (_root == null) return;
            _root.RemoveFromHierarchy();
            _root = null;
            _body = null;
            _stackCards.Clear();
            _stackBodies.Clear();
            _stackHeights.Clear();
            _hierMode = false;
            _hasFollowed = false;
        }

        // Also used by the controller to build per-card stack infos from synthesized payloads.
        internal static string KindLabel(CanvasDragPayload payload)
        {
            switch (payload.Kind)
            {
                case CanvasDragPayload.DragKind.MoveNode:
                    if (payload.Node is ContainerNode cn)
                        return cn.Type == ContainerType.Tabs ? "TABS" : "CONTAINER";
                    if (payload.Node is CollectionNode) return "COLLECTION";
                    if (payload.Node is ButtonNode) return "BUTTON";
                    if (payload.Node is HelpBoxNode) return "HELP BOX";
                    // Without these two arms a dragged Text Block or C# Property fell through to the
                    // "BINDING" default and the ghost announced the wrong widget kind mid-drag. The audit
                    // reported TextBlockNode (also missing from BuildHierarchyRowLabel); PropertyNode turned
                    // up when the regression test enumerated every kind instead of only the reported one.
                    // BindingNode is the only kind the default is FOR.
                    if (payload.Node is TextBlockNode) return "TEXT";
                    if (payload.Node is PropertyNode) return "PROPERTY";
                    if (payload.Node is ScriptHeaderNode) return "HEADER";
                    if (payload.Node is SpaceNode) return "SPACE";
                    if (payload.Node is LabeledDividerNode) return "DIVIDER";
                    if (payload.Node is ImageBannerNode) return "IMAGE";
                    if (payload.Node is EmbeddedEditorNode) return "EDITOR";
#if SASQ_CUSTOM_WIDGET
                    if (payload.Node is CustomWidgetNode) return "WIDGET";
#endif
                    return "BINDING";
                case CanvasDragPayload.DragKind.AddBinding: return "BINDING";
                case CanvasDragPayload.DragKind.AddWidget: return "WIDGET";
                case CanvasDragPayload.DragKind.MoveCollectionItem: return "ITEM";
                default: return "";
            }
        }
    }
}
