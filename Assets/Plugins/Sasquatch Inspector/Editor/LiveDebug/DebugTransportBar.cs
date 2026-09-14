using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The playhead transport: step a frame, jump to the next change, read the frame number, choose the clock,
    /// export, clear.
    ///
    /// <para><b>Why this is not optional.</b> Twelve hundred samples across roughly three hundred pixels is
    /// four samples per pixel, so a value that moves on four frames of a whole run cannot be reached by
    /// dragging — the precision simply is not there. Stepping and jump-to-change are the only ways in, which
    /// makes this bar the feature's primary navigation rather than a convenience on top of it.</para>
    ///
    /// <para>One bar per inspector, not per row: the playhead is shared by every graph on the target, so a
    /// transport per field would be several controls fighting over one piece of state.</para>
    ///
    /// <para><b>Three rules this bar is built on, each one a defect it used to have.</b>
    /// (1) <b>Vector icons, never text glyphs.</b> The first version used ⏮ ◀ ▶ ⏭ ⟲, which render at whatever
    /// width the editor font happens to have — and <c>⟲</c> is not in that font at all, so one button was
    /// simply blank. (2) <b>A control that cannot act must LOOK like it cannot act.</b> Most of "I clicked it
    /// and nothing happened" was a live button with nothing left to do. (3) <b>Anything that succeeds silently
    /// must say so.</b> Copying the trace worked perfectly and looked broken, because a clipboard write has no
    /// visible effect anywhere on screen.</para>
    /// </summary>
    internal sealed class DebugTransportBar : VisualElement
    {
        private const float BarHeight = 22f;
        private const long FlashMs = 1800;

        private readonly DebugRecorder _recorder;
        private readonly Action _onClear;

        private readonly Label _readout;
        private readonly Label _notice;
        private readonly Button _record;
        private readonly SasquatchIconElement _recordIcon;
        private readonly Button _first;
        private readonly Button _prev;
        private readonly Button _next;
        private readonly Button _last;
        private readonly Button _live;
        private readonly Button _tips;
        private readonly Button _capture;
        private readonly Button _settings;
        private readonly Button _clear;

        /// <summary>
        /// The in-flow placeholder that reserves this bar's space in the build root.
        ///
        /// <para>The bar itself is absolutely positioned and is the LAST child of the root, because UI Toolkit
        /// paints in hierarchy order and has no z-index: as the FIRST child it was painted first, so every
        /// graph row it floated over painted straight on top of it. A header that is pinned to the top and
        /// simultaneously behind the content is not pinned to anything. The anchor holds the gap so nothing in
        /// the flow moves, and being last is what makes "always on top" true rather than aspirational.</para>
        /// </summary>
        private readonly VisualElement _anchor;

        /// <summary>Non-null while a confirmation message is standing in for the readout.</summary>
        private string _flash;
        private IVisualElementScheduledItem _flashClear;

        // Last rendered readout state, so an unchanged line is not rebuilt (and re-allocated) every tick.
        private int _lastReadoutAbs = int.MinValue;
        private int _lastReadoutCount = -1;
        private bool _lastReadoutParked;

        // Last rendered jump-button captions. Rebuilt only when the focused row or its answer changes —
        // Sync() runs up to sixty times a second and an interpolated tooltip allocates on every one of them.
        private string _lastFocusLabel;
        private bool _lastFocusHasLater;
        private bool _focusCaptionValid;

        /// <summary>Clear is a two-step: the first click arms it, the second commits. No modal, no lost trace.</summary>
        private bool _clearArmed;
        private IVisualElementScheduledItem _clearDisarm;

        // ---- sticky ("freeze panes") state -------------------------------------------------------------
        private ScrollView _scroll;
        private float _lift;
        private bool _pinned;

        internal DebugTransportBar(DebugRecorder recorder, Action onClear)
        {
            _recorder = recorder;
            _onClear = onClear;

            AddToClassList("sasq-debug-transport");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            // WRAPS when it has to. Ten controls plus a readout need roughly 250px, and an inspector docked
            // narrow can be under 200 — at which point non-shrinking buttons simply run off the end and vanish,
            // which is how the export button disappeared and then "reappeared" whenever the readout text
            // changed length. Wrapping drops the action group onto a second row instead: still one bar, still
            // everything reachable, and it costs nothing at a normal width because flex only wraps when the
            // content genuinely does not fit.
            style.flexWrap = Wrap.Wrap;
            // NO minHeight ON THE WRAPPING CONTAINER. Yoga applies a flex container's minimum CROSS SIZE to
            // EVERY FLEX LINE when it wraps, so `min-height: 22` meant "each line is at least 22px tall": a
            // three-line bar measured 62px where its content needs 47, and a two-line bar 42px where it needs
            // 34. That is up to 15px of empty, bordered, background-filled dead strip INSIDE the bar's own
            // drawn box — at exactly the narrow docked width the wrap exists to serve, where it reads as a
            // half-rendered toolbar. The anchor faithfully copies the wrong height, so the same 15px is stolen
            // from the inspector's first field too.
            //
            // It also sliced the settings card: the card anchors below the GEAR BUTTON, which sits on the last
            // CONTENT line, while the bar's BOX ran 15px further down and paints above every card — so the
            // card's top 12px was hidden behind the toolbar. Removing this line closes that gap to zero.
            //
            // The floor moves onto the `nav` group instead (see Group), which keeps a single-line bar at its
            // designed 22px without ever being multiplied by a line count.
            style.minHeight = StyleKeyword.Auto;
            // ABSOLUTE, and stretched across the root. See _anchor: the bar has to be the last child to paint
            // over the rows it floats above, and the last child cannot also be the first thing in the flow.
            // The margins live on the anchor instead, so the gap around the bar is still part of the layout.
            style.position = Position.Absolute;
            style.left = 0;
            style.right = 0;
            style.top = 0;
            style.paddingLeft = 3;
            style.paddingRight = 3;

            // THREE GROUPS, and the grouping is what makes wrapping survivable. Wrapping a flat row breaks
            // between individual BUTTONS - measured at 198px that produced four ragged rows with a lone eraser
            // stranded on the last one. Wrapping between groups gives at worst
            //     [ transport controls ]
            //     [ readout ][ actions ]
            // which still reads as one bar.
            _anchor = new VisualElement { name = "sasq-debug-transport-anchor" };
            _anchor.style.height = BarHeight;
            _anchor.style.marginTop = 2;
            _anchor.style.marginBottom = 4;
            _anchor.style.flexShrink = 0;
            _anchor.pickingMode = PickingMode.Ignore;

            var nav = Group();
            var actions = Group();
            // THE HEIGHT FLOOR LIVES ON `nav` ALONE — not on the wrapping bar, and not on every group.
            // A min-height on a wrapping container is applied per flex LINE, so on the bar it multiplied by
            // the line count (3 lines x 20 + 2 border = 62 where the content needs 47); on Group() it would
            // do the same thing one level down, since each group lands on its own line when the bar wraps.
            // On `nav` only, an UNWRAPPED bar still measures its designed 22px (nav is on that single line),
            // and a wrapped bar's later lines size to their own content instead of being padded out.
            nav.style.minHeight = BarHeight - 2f;

            // Capture leads: it decides whether anything is happening at all.
            _record = IconBtn(SasquatchIcons.Ids.CircleDot, "Start recording", ToggleCapture);
            _recordIcon = _record.childCount > 0 ? _record[0] as SasquatchIconElement : null;
            nav.Add(_record);
            nav.Add(Separator());

            _first = IconBtn(SasquatchIcons.Ids.SkipBack, "Jump to the first recorded change", () => Jump(int.MinValue, +1));
            _prev = IconBtn(SasquatchIcons.Ids.ChevronLeft, "Step back one sample", () => Step(-1));
            _next = IconBtn(SasquatchIcons.Ids.ChevronRight, "Step forward one sample", () => Step(+1));
            _last = IconBtn(SasquatchIcons.Ids.SkipForward, "Jump to the next change", () => Jump(_recorder.ReadoutAbsolute, +1));
            nav.Add(_first); nav.Add(_prev); nav.Add(_next); nav.Add(_last);
            nav.Add(Separator());

            // LIVE is a PILL, not an icon, because it has two jobs and an icon could only do one. Lit, it is
            // the answer to "are these numbers current, or am I looking at the past?" — the question a frozen
            // graph raises and nothing else on the bar answers. Unlit, it is the way back: scrubbing parks the
            // playhead, and without this the only route to live data again is to guess that the end of the
            // timeline means "resume". As an unlabelled circular arrow it was neither.
            _live = Pill("LIVE", () => _recorder.SetScrub(-1));
            nav.Add(_live);

            // Manual clock only: the button that takes the one sample. Hidden on every automatic clock rather
            // than shown-and-disabled, because a control that is dead by CONFIGURATION reads as broken, while a
            // control that is dead by STATE reads as unavailable. Different messages, different treatments.
            _capture = IconBtn(SasquatchIcons.Ids.CircleDot, "Capture one sample now", CaptureOne);
            _capture.style.display = DisplayStyle.None;
            nav.Add(_capture);
            Add(nav);

            // --- readout ---
            _readout = new Label("—");
            _readout.AddToClassList("sasq-debug-transport__readout");
            _readout.style.flexGrow = 1;
            // The one element allowed to give ground. minWidth 0 is required: a flex item's default minimum is
            // its CONTENT size, so without it the label refuses to shrink and pushes the buttons out anyway.
            _readout.style.flexShrink = 1;
            // Not zero: a readout squeezed to three pixels is worse than a wrap, because it silently stops
            // being the thing the whole bar exists to display. Below this it forces the wrap instead.
            _readout.style.minWidth = 96;
            _readout.style.overflow = Overflow.Hidden;
            _readout.style.marginLeft = 6;
            _readout.style.marginRight = 6;
            _readout.style.fontSize = 10;
            _readout.style.color = WidgetPalette.TextGray;
            _readout.style.unityTextAlign = TextAnchor.MiddleCenter;
            // Pickable, unlike every other label on this bar: the frame number and the elapsed zero point now
            // live in its tooltip, and an Ignore'd element is never the target of a TooltipEvent.
            _readout.pickingMode = PickingMode.Position;
            Add(_readout);

            _notice = new Label("");
            _notice.style.fontSize = 9;
            _notice.style.marginRight = 4;
            _notice.style.color = WidgetPalette.WarningCalloutText;
            _notice.pickingMode = PickingMode.Ignore;
            // Collapsed when empty, not merely blank: an empty label still occupies a flex slot, and it was
            // creating a wrap point of its own that split the action group in half.
            _notice.style.display = DisplayStyle.None;
            actions.Add(_notice);

            actions.Add(Separator());

            // Field tooltips open UNDERNEATH the row the pointer is resting on, which in debug mode is exactly
            // where the next two or three graphs are — so pointing at a graph to read it is also the gesture
            // that covers the graphs below. Worth having the rest of the time, hence a switch.
            _tips = Pill("TIPS", () => DebugTooltipMute.Muted = !DebugTooltipMute.Muted);
            actions.Add(_tips);

            _settings = IconBtn(SasquatchIcons.Ids.Settings, "Recording settings", ShowSettings);
            actions.Add(_settings);
            actions.Add(IconBtn(SasquatchIcons.Ids.Copy, "Copy the whole trace to the clipboard as CSV", CopyCsv));
            actions.Add(IconBtn(SasquatchIcons.Ids.Download, "Save the whole trace as a .csv file", SaveCsv));

            // ERASER, not a waste bin. A bin next to a "save as .csv" button reads as "delete that file", which
            // is precisely the wrong idea about a control that only ever discards an in-memory buffer.
            _clear = IconBtn(SasquatchIcons.Ids.Eraser, "Erase this script's recorded trace", ClearPressed);
            actions.Add(_clear);
            Add(actions);

            // Arrow keys step, once the bar has focus — the gesture anyone reaching for frame-by-frame expects.
            focusable = true;
            RegisterCallback<KeyDownEvent>(evt =>
            {
                // Shift steps ten. Twelve hundred samples is roughly four per pixel, so single-stepping across
                // a run is not a realistic gesture and dragging cannot land on anything precise — ten is the
                // coarse gear that makes the keyboard usable for the distance between those two.
                int stride = evt.shiftKey ? 10 : 1;
                switch (evt.keyCode)
                {
                    case KeyCode.LeftArrow:  Step(-stride); evt.StopPropagation(); break;
                    case KeyCode.RightArrow: Step(+stride); evt.StopPropagation(); break;
                    case KeyCode.Home:       _recorder.SetScrub(_recorder.OldestAbsolute); evt.StopPropagation(); break;
                    // End releases rather than parking on the newest sample. They look the same for one frame
                    // and then diverge: parked, the graphs freeze while the game runs on, which is the one
                    // thing nobody means by "End".
                    case KeyCode.End:        _recorder.SetScrub(-1); evt.StopPropagation(); break;
                }
            });

            RegisterCallback<AttachToPanelEvent>(_ => HookSticky());
            // Driven by events that can actually move the bar - scrolling, the viewport resizing, and the bar
            // itself being re-laid-out because content above it grew. Deliberately NOT called from Sync(),
            // which runs up to sixty times a second whether or not anything moved.
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // The bar wraps to two or three rows on a narrow inspector, so the gap it needs is not a
                // constant. Feeding its measured height back into the anchor cannot loop: the bar is
                // absolutely positioned, so its own size does not depend on the anchor's.
                float measured = resolvedStyle.height;
                if (!float.IsNaN(measured) && measured > 0f &&
                    !Mathf.Approximately(_anchor.resolvedStyle.height, measured))
                    _anchor.style.height = measured;
                UpdateSticky();
            });

            Action onSettings = Sync;
            DebugRecordSettings.Changed += onSettings;
            DebugCaptureController.Changed += onSettings;
            DebugTooltipMute.Changed += onSettings;
            _recorder.FocusChanged += onSettings;
            // Released even when this bar never reaches a panel: PanelLifetime queues a watch registered
            // off-panel and settles it a drain later. Before that, a bar built and thrown away — a speculative
            // rebuild, a preview replaced before it was shown — leaked all four of these for the rest of the
            // domain, each one keeping the dead bar alive and re-running Sync on it.
            PanelLifetime.OnRealDetach(this, () =>
            {
                DebugRecordSettings.Changed -= onSettings;
                DebugCaptureController.Changed -= onSettings;
                DebugTooltipMute.Changed -= onSettings;
                _recorder.FocusChanged -= onSettings;
                UnhookSticky();
            }, releaseIfNeverAttached: true);

            Sync();
        }

        // ---- installation ------------------------------------------------------------------------------

        /// <summary>
        /// Puts the bar on a freshly built inspector root: the anchor first in the flow, the bar itself LAST
        /// in the hierarchy. Both halves matter — see <see cref="_anchor"/>. Callers must use this rather than
        /// adding the bar themselves, because "first in the flow, last in the paint order" is not a property a
        /// single <c>Insert</c> can express.
        /// </summary>
        internal void InstallInto(VisualElement root)
        {
            if (root == null) return;
            root.Insert(0, _anchor);
            root.Add(this);

            // The anchor's position IS the bar's resting position, and it only moves when something above it
            // in the flow resizes — a foldout opening, a list growing.
            _anchor.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float top = _anchor.layout.yMin;
                if (!float.IsNaN(top)) style.top = top;
                UpdateSticky();
            });

            DebugTooltipMute.Install(root, this);
        }

        /// <summary>
        /// Shows or hides the bar. Goes through here rather than through <c>style.display</c> because the
        /// anchor has to vanish with it — a reserved gap above a hidden toolbar is a blank band nobody can
        /// explain.
        /// </summary>
        internal void SetVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            style.display = display;
            _anchor.style.display = display;
        }

        // ---- chrome ------------------------------------------------------------------------------------

        private Button IconBtn(string iconId, string tooltip, Action action)
        {
            var b = new Button(action) { tooltip = tooltip };
            b.AddToClassList("sasq-debug-transport__btn");
            // A flex row gives every child a share of the squeeze, and the readout in the middle is happy to
            // grow forever — so on a narrow inspector the LAST buttons were pushed clean off the end. They were
            // still there and still clickable, which made it worse: changing the readout text (a copy
            // confirmation is a different length) shifted the layout and a previously-invisible icon appeared
            // out of nowhere, reading as a bug in its own right. Controls never shrink; the readout does.
            b.style.flexShrink = 0;
            var icon = SasquatchIcons.Create(iconId, 12f, WidgetPalette.TextGray);
            icon.pickingMode = PickingMode.Ignore;
            b.Add(icon);

            // Hover and pressed washes live here rather than in USS: they are skin-relative (WidgetPalette.Ink
            // flips black-on-light / white-on-dark), and a new --sasq-* token would have to be mirrored in both
            // theme sheets forever — TestThemeTokenSheetParity enforces that, and caught the first attempt.
            var idle = new Color(0f, 0f, 0f, 0f);
            var hover = WidgetPalette.Ink(0.09f);
            var down = WidgetPalette.Ink(0.16f);
            b.style.backgroundColor = idle;
            b.RegisterCallback<PointerEnterEvent>(_ => { if (b.enabledSelf) b.style.backgroundColor = hover; });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = idle);
            b.RegisterCallback<PointerDownEvent>(_ => { if (b.enabledSelf) b.style.backgroundColor = down; });
            // Reset on UP as well as on leave: a click that ends outside the button never sends PointerLeave,
            // which would strand it looking permanently pressed.
            b.RegisterCallback<PointerUpEvent>(_ => b.style.backgroundColor = idle);
            return b;
        }

        /// <summary>
        /// A small rounded chip that reads as an on/off state as well as a button — the shape a status light
        /// has, rather than the shape a checkbox has. Two of them: LIVE and TIPS.
        ///
        /// <para>Deliberately NOT a <c>Toggle</c>. Unity's toggle is a checkbox plus a label, roughly sixty
        /// pixels wide with the tick drawn in the editor's own skin — on a bar that already wraps at 198px
        /// that is a third of a row spent on something that has to read at a glance from three feet away.</para>
        ///
        /// <para>Colours are applied in <see cref="Paint"/> rather than by a USS class, for the same reason
        /// the hover washes are inline: they are skin-relative <see cref="WidgetPalette"/> values, and a new
        /// --sasq-* token would have to be mirrored in both theme sheets forever.</para>
        /// </summary>
        private static Button Pill(string text, Action action)
        {
            var b = new Button(action) { text = text };
            b.AddToClassList("sasq-debug-transport__pill");
            b.style.flexShrink = 0;
            return b;
        }

        /// <summary>Repaints one pill for its on/off state. Colour only — nothing here changes layout.</summary>
        private static void Paint(Button pill, bool on, Color ink, string tooltip)
        {
            if (pill == null) return;
            var idle = new Color(ink.r, ink.g, ink.b, on ? 0.16f : 0f);
            pill.style.color = on ? ink : WidgetPalette.TextGray;
            pill.style.backgroundColor = idle;
            var edge = on ? ink : WidgetPalette.Border;
            pill.style.borderTopColor = edge;
            pill.style.borderBottomColor = edge;
            pill.style.borderLeftColor = edge;
            pill.style.borderRightColor = edge;
            pill.tooltip = tooltip;
        }

        /// <summary>A run of controls that must stay together on one line. The bar wraps BETWEEN these.</summary>
        private static VisualElement Group()
        {
            var g = new VisualElement();
            g.style.flexDirection = FlexDirection.Row;
            g.style.alignItems = Align.Center;
            g.style.flexShrink = 0;
            g.style.flexWrap = Wrap.NoWrap;
            return g;
        }

        private static VisualElement Separator()
        {
            var sep = new VisualElement();
            sep.AddToClassList("sasq-debug-transport__sep");
            sep.style.flexShrink = 0;
            sep.pickingMode = PickingMode.Ignore;
            return sep;
        }

        /// <summary>
        /// Enables or disables a button AND fades its glyph to match. <see cref="VisualElement.SetEnabled"/>
        /// alone dims Unity's own controls, but a Painter2D icon paints its own colour and would stay at full
        /// strength inside a greyed button — the exact "looks clickable, isn't" state this bar is trying to
        /// stop producing.
        /// </summary>
        private static void SetLive(Button b, bool enabled)
        {
            if (b == null) return;
            b.SetEnabled(enabled);
            b.style.opacity = enabled ? 1f : 0.3f;
        }

        // ---- navigation --------------------------------------------------------------------------------

        /// <summary>
        /// Moves the playhead by <paramref name="delta"/> samples, clamped INSIDE the ring.
        ///
        /// <para><b>The clamp is the whole method.</b> <see cref="DebugRecorder.SetScrub"/> reads any negative
        /// index as "release and follow live data", so handing it plain arithmetic means a step that runs off
        /// the oldest end does not stop at the oldest sample — it releases, and the next step walks backwards
        /// from the NEWEST one. Stepping left five times from the start of the trace landed on sample 19 of 60.
        /// The buttons hid this by disabling themselves at the edges; the keyboard has no such guard, which is
        /// how it surfaced.</para>
        /// </summary>
        private void Step(int delta)
        {
            if (_recorder.Count == 0) return;
            int target = Mathf.Clamp(_recorder.ReadoutAbsolute + delta,
                _recorder.OldestAbsolute, _recorder.Absolute - 1);
            _recorder.SetScrub(target);
        }

        /// <summary>
        /// Moves the playhead to the next recorded change — <b>in the row you last touched</b>, falling back to
        /// any row when that one has nothing left.
        ///
        /// <para><b>Why local beats global.</b> A global search is useless on a real script: several fields
        /// move on every single frame, so "is there a change after here?" is always "yes, next frame", and the
        /// button degenerates into a second Step Forward no matter which graph you were looking at. Scoped to
        /// the focused row it answers the question that was actually asked — a state enum that flips four
        /// times in a whole run is now four clicks away instead of twelve hundred.</para>
        ///
        /// <para>The fallback is what stops the scoping becoming a trap. A row that never changes again would
        /// otherwise leave a live-looking button that does nothing, which is the exact failure this bar's
        /// third rule exists to prevent — so it jumps globally instead and says so.</para>
        /// </summary>
        private void Jump(int from, int direction)
        {
            if (_recorder.Count == 0) return;
            int anchor = from == int.MinValue ? _recorder.OldestAbsolute - 1 : from;

            var focus = _recorder.FocusChannel;
            int target = focus != null ? _recorder.NearestChange(anchor, direction, focus) : -1;
            if (target < 0)
            {
                target = _recorder.NearestChange(anchor, direction);
                if (target >= 0 && focus != null) Flash($"{focus.Label}: none left · any field");
            }
            if (target >= 0) _recorder.SetScrub(target);
        }

        /// <summary>
        /// Record / Stop. Always available whatever the configured triggers say — a trigger decides what
        /// happens when you do nothing, never what you are allowed to do.
        /// </summary>
        private void ToggleCapture()
        {
            var type = _recorder.TargetType;
            if (type == null) return;
            if (!DebugSampleClock.ClockRunningFor(type)) { Flash("Recording needs Play Mode"); return; }

            bool wasRecording = DebugCaptureController.PhaseOf(type) == DebugCapturePhase.Recording;
            DebugCaptureController.Toggle(type);
            Flash(wasRecording ? "Stopped — buffer frozen" : "Recording");
            Sync();
        }

        private void CaptureOne()
        {
            if (!DebugSampleClock.ClockRunningFor(_recorder.TargetType)) { Flash("Manual capture needs Play Mode"); return; }
            _recorder.Sample(DebugTickSource.Manual);
            Sync();
        }

        // ---- settings ----------------------------------------------------------------------------------

        private void ShowSettings()
        {
            var type = _recorder.TargetType;
            if (type == null) return;
            // Parented to the build root, not to this bar: the bar is 22px tall and clips, and the card has to
            // float over the fields below it the same way the moment card does.
            var host = FindBuildRoot();
            if (host == null) return;
            DebugSettingsCard.Toggle(host, _settings, type);
        }

        /// <summary>The nearest ancestor tagged as a Sasquatch build root — the shared host every overlay uses.</summary>
        private VisualElement FindBuildRoot()
        {
            var el = parent;
            while (el != null)
            {
                if (el.ClassListContains("sasq-build-root")) return el;
                el = el.parent;
            }
            return parent;
        }

        // ---- clear -------------------------------------------------------------------------------------

        /// <summary>
        /// First click arms, second commits, and it disarms itself after a moment. A confirm dialog for a
        /// debug buffer is heavier than the thing it protects, and no confirmation at all throws away a
        /// recording someone spent a play session catching.
        /// </summary>
        private void ClearPressed()
        {
            if (_recorder.Count == 0) { Flash("Nothing recorded yet"); return; }

            if (!_clearArmed)
            {
                _clearArmed = true;
                Flash("Click again to discard the trace");
                _clearDisarm?.Pause();
                _clearDisarm = schedule.Execute(() => { _clearArmed = false; Sync(); }).StartingIn(FlashMs);
                return;
            }

            _clearArmed = false;
            _clearDisarm?.Pause();
            _recorder.ClearSamples();
            _onClear?.Invoke();
            Flash("Trace cleared");
        }

        // ---- readout -----------------------------------------------------------------------------------

        /// <summary>Shows a transient confirmation in place of the readout. The only feedback a clipboard write can have.</summary>
        private void Flash(string message)
        {
            _flash = message;
            _lastReadoutAbs = int.MinValue;   // the flash overwrote the readout; force a rebuild when it clears
            _readout.text = message;
            _readout.style.color = WidgetPalette.Accent;
            _flashClear?.Pause();
            _flashClear = schedule.Execute(() => { _flash = null; Sync(); }).StartingIn(FlashMs);
        }

        /// <summary>Refreshes the readout and every button's enabled state. Driven by the scrub event and the redraw tick.</summary>
        internal void Sync()
        {
            // Re-assert the bar as the last child. The moment card, the settings card and the code peek all
            // parent themselves to the same build root, and a child added later paints later — so opening any
            // one of them would otherwise bury the pinned header behind it. One reference compare per tick
            // buys "always on top" as a fact instead of a fact-until-you-click-something.
            if (parent != null && parent.childCount > 1 && parent[parent.childCount - 1] != this) BringToFront();

            int count = _recorder.Count;
            bool any = count > 0;
            bool parked = _recorder.ScrubAbsolute >= 0;
            int abs = _recorder.ReadoutAbsolute;

            bool manual = _recorder.TickSource == DebugTickSource.Manual;
            var type = _recorder.TargetType;
            var phase = DebugCaptureController.PhaseOf(type);
            // ClockRunningFor, not isPlaying: "is a clock writing samples for this type" is the question, and
            // in the Showcase Sandbox the answer is yes without Play Mode (see DebugSampleClock.SandboxType).
            bool clockRunning = DebugSampleClock.ClockRunningFor(type);
            bool recording = phase == DebugCapturePhase.Recording && clockRunning;
            // Capture only makes sense on the Manual clock AND while a recording is actually open.
            _capture.style.display = manual && recording ? DisplayStyle.Flex : DisplayStyle.None;

            // Record / Stop. A red dot means "press to start", a red square means "live, press to freeze" —
            // the same pair every recorder anyone has ever used shows, so it needs no explaining.
            if (_recordIcon != null)
            {
                _recordIcon.IconId = recording ? SasquatchIcons.Ids.Square : SasquatchIcons.Ids.CircleDot;
                _recordIcon.Tint = recording || clockRunning
                    ? WidgetPalette.DangerText
                    : WidgetPalette.TextGray;
            }
            _record.tooltip = recording
                ? "Stop recording and freeze the buffer (the samples stay, and stay scrubbable)"
                : clockRunning ? "Start recording" : "Recording needs Play Mode";
            SetLive(_record, clockRunning);

            SetLive(_prev, any && abs > _recorder.OldestAbsolute);
            SetLive(_next, any && abs < _recorder.Absolute - 1);
            // HasAnyChange / HasChangeAfter, not NearestChange: this runs up to sixty times a second forever,
            // and the nearest-change search walks every mark in every channel — measured ~3,000 entries on a
            // real trace. Both of these are O(channels) because the mark lists are append-only, so the last
            // entry IS the latest change.
            SetLive(_first, any && _recorder.HasAnyChange());
            // Live means the playhead is pinned to the newest sample, so there is nothing after it by
            // definition — no scan needed at all in the common case. Deliberately the GLOBAL question even
            // though the jump is scoped: the scoped jump falls back to any field, so a button disabled on the
            // focused row's behalf would be refusing to do something it is perfectly able to do.
            SetLive(_last, any && parked && _recorder.HasChangeAfter(abs));
            SetLive(_clear, any);

            // LIVE: lit while the graphs are showing current values, dim and clickable once scrubbing has
            // parked the playhead in the past. Enabled rather than SetLive'd, because SetLive fades the glyph
            // to 30% — right for a dead button, wrong for a status light that is doing its job.
            _live.SetEnabled(parked);
            Paint(_live, !parked, WidgetPalette.DangerText, parked
                ? $"Frozen at sample {(any ? abs - _recorder.OldestAbsolute + 1 : 0)}. Click to release the " +
                  "playhead and follow live data again."
                : "The graphs are showing live values. Drag or step a graph to park the playhead in the past.");

            bool tips = !DebugTooltipMute.Muted;
            Paint(_tips, tips, WidgetPalette.Accent, tips
                ? "Field tooltips are ON. They open under the row you are pointing at, which is where the next " +
                  "graphs are — click to stop them covering the traces."
                : "Field tooltips are OFF inside this inspector. Click to bring them back.");

            // Jump captions. Rebuilt only when the focused row or its answer changes: these are interpolated
            // strings and Sync() runs up to sixty times a second.
            var focus = _recorder.FocusChannel;
            string focusLabel = focus?.Label;
            bool focusHasLater = focus != null && any && _recorder.HasChangeAfter(abs, focus);
            if (!_focusCaptionValid || focusLabel != _lastFocusLabel || focusHasLater != _lastFocusHasLater)
            {
                _lastFocusLabel = focusLabel; _lastFocusHasLater = focusHasLater; _focusCaptionValid = true;
                _first.tooltip = focus == null
                    ? "Jump to the first recorded change, in any field"
                    : $"Jump to the first recorded change in “{focusLabel}”";
                _last.tooltip = focus == null
                    ? "Jump to the next recorded change, in any field — click a graph to scope this to one field"
                    : focusHasLater
                        ? $"Jump to the next change in “{focusLabel}”"
                        : $"“{focusLabel}” has no later change — this jumps to the next change in any field instead";
            }

            _clear.tooltip = _clearArmed
                ? "Click again to erase the trace"
                : "Erase this script's recorded trace";

            // Two separate warnings, deliberately never both: a degraded clock is a correctness problem and
            // outranks a merely expensive one.
            if (DebugSampleClock.Degraded)
            {
                _notice.text = "clock degraded";
                _notice.style.display = DisplayStyle.Flex;
                _notice.tooltip = "One of the player-loop sampling points could not be found in this editor " +
                                  "version, so at least one tick source is not recording. Please report this.";
                _notice.style.color = WidgetPalette.DangerText;
            }
            else if (_recorder.TargetCount > 1)
            {
                // Second in priority, above mere cost: a strip that looks like it describes the selection while
                // describing ONE object is the same class of quiet lie the drifting "t+" was. Say which one.
                _notice.text = "1 of " + _recorder.TargetCount;
                _notice.style.display = DisplayStyle.Flex;
                _notice.tooltip = $"Several objects are selected, and Live Debug records exactly one of them — " +
                    $"“{_recorder.TargetName}”. Every graph here shares a single sample clock, which is " +
                    "what makes the co-change readout a fact rather than a guess, and one clock cannot span " +
                    "several objects. Select a single object to be certain what you are reading.";
                _notice.style.color = WidgetPalette.WarningCalloutText;
            }
            else
            {
                // NOTHING. There used to be an "N watching" count here, meant to make a thirty-script mistake
                // visible rather than merely slow. It was wrong on both counts. It cost a whole extra ROW on a
                // bar that wraps — doubling the header's height in the ordinary case, to say something nobody
                // asked — and the number itself was inflated by stale runner tokens, so four watched scripts
                // reported nine, ten, eleven and twelve. A diagnostic that is both intrusive and untrue is
                // worse than no diagnostic. The two notices above stay, because both are correctness warnings
                // about what you are reading rather than trivia about what it costs.
                _notice.text = "";
                _notice.style.display = DisplayStyle.None;
            }


            if (_flash != null) return;   // a confirmation owns the readout until it expires

            // Skip the string rebuild when nothing observable moved. Interpolation allocates, and while the
            // playhead is parked this tick would otherwise re-render an identical line ~60x/second.
            if (any && abs == _lastReadoutAbs && count == _lastReadoutCount && parked == _lastReadoutParked) return;
            _lastReadoutAbs = abs; _lastReadoutCount = count; _lastReadoutParked = parked;

            _readout.style.color = WidgetPalette.TextGray;

            // The WAITING states get the readout to themselves. "no samples yet" was true but useless once
            // recording could be waiting on a trigger: the honest question is not "is there data" but "what
            // is this thing waiting for, and will it ever happen on its own?"
            if (!recording && !any)
            {
                if (!clockRunning) { _readout.text = "press play to record"; return; }

                double countdown = DebugCaptureController.CountdownFor(type);
                if (countdown >= 0d)
                {
                    _readout.text = $"recording in {countdown:0.0}s…";
                    _readout.style.color = WidgetPalette.Accent;
                    // A countdown is the one readout that must re-render even though nothing was sampled.
                    _lastReadoutAbs = int.MinValue;
                    return;
                }

                _readout.text = phase == DebugCapturePhase.Waiting
                    ? "waiting — " + WaitingOn(type)
                    : "stopped";
                return;
            }

            if (!any) { _readout.text = manual ? "press Capture to take a sample" : "recording…"; return; }

            // "frame 384 · 383/469" read as one number contradicting itself, because nothing said the second
            // pair was a POSITION IN THE BUFFER rather than another frame count. Naming it removes the whole
            // question — and now that recording starts on frame 1, the two line up anyway until the ring wraps.
            //
            // The play state is deliberately gone from here: you can see whether the game is running, and the
            // Record button already shows a red square while it is live. A word repeating that spent a third of
            // a narrow bar saying nothing.
            //
            // WHAT SURVIVED THE CUT, and why. Three numbers did not fit: at a docked inspector width the frame
            // count was simply clipped off the end, so the bar was spending its widest field on a number
            // nobody could read. Position-in-buffer stayed because it is the one the transport buttons move.
            // Elapsed stayed because it is the only way to line a graph up against something you watched
            // happen. The frame number went to the tooltip, where a number you look up twice a session belongs.
            //
            // "t+" went with it. It meant "seconds since this recording started" and read as a variable name.
            // The units are on the number; the zero point is in the tooltip.
            int shown = abs - _recorder.OldestAbsolute + 1;
            _readout.text = $"{shown} of {count}  ·  {_recorder.ElapsedAt(abs):0.00}s";
            _readout.tooltip =
                $"Sample {shown} of the {count} held in the buffer  ·  game frame {_recorder.FrameOf(abs)}  ·  " +
                $"{_recorder.ElapsedAt(abs):0.00}s after recording started" +
                "\n\nClick this bar, then: ← → step one sample, Shift+← → step ten, Home for the oldest " +
                "sample held, End to go back to live.";
            if (recording && !parked) _readout.style.color = WidgetPalette.DangerText;

        }

        /// <summary>Plain-language answer to "why is nothing happening?" — the readout's most useful line.</summary>
        private static string WaitingOn(Type type)
        {
            switch (DebugRecordSettings.StartTriggerFor(type))
            {
                case DebugStartTrigger.OnEnable: return "component is disabled";
                case DebugStartTrigger.Manual:   return "press Record";
                default:                         return "start trigger";
            }
        }

        // ---- sticky: keep the bar reachable in a long inspector -----------------------------------------

        /// <summary>
        /// Pins the bar to the top of the visible scroll area, the way a spreadsheet freezes a header row.
        ///
        /// <para>UI Toolkit has no <c>position: sticky</c>, so this translates the bar against the scroll
        /// offset instead. It deliberately stays a child of OUR build root rather than re-parenting into the
        /// inspector's own <see cref="ScrollView"/>: a foreign parent outlives our rebuild, and the orphan it
        /// leaves behind is a floating toolbar belonging to an inspector that no longer exists.</para>
        /// </summary>
        private void HookSticky()
        {
            if (_scroll != null) return;
            _scroll = GetFirstAncestorOfType<ScrollView>();
            if (_scroll == null) return;   // designer canvas, Showcase, tests — plain flow, no behaviour change

            _scroll.verticalScroller.valueChanged += OnScrolled;
            _scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometry);
            UpdateSticky();
        }

        private void UnhookSticky()
        {
            if (_scroll == null) return;
            _scroll.verticalScroller.valueChanged -= OnScrolled;
            _scroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometry);
            _scroll = null;
        }

        private void OnScrolled(float _) => UpdateSticky();
        private void OnViewportGeometry(GeometryChangedEvent _) => UpdateSticky();

        private void UpdateSticky()
        {
            if (_scroll == null || panel == null || parent == null) return;

            float h = resolvedStyle.height;
            if (float.IsNaN(h) || h <= 0f) return;

            var viewport = _scroll.contentViewport.worldBound;
            // Read from the ANCHOR, which is never translated and never moves under its own pin. The previous
            // version read the bar's own worldBound and subtracted the lift back out — correct, but it made
            // the pin's input depend on the pin's output, and any drift in that arithmetic walks the bar off
            // the screen a pixel per scroll event.
            float naturalTop = _anchor.worldBound.yMin;

            float want = Mathf.Max(0f, viewport.yMin - naturalTop);
            // Never travel past the region it belongs to: once the last graph has scrolled away the transport
            // has nothing left to drive, and a toolbar hovering over unrelated fields is just debris.
            float maxLift = Mathf.Max(0f, parent.worldBound.yMax - naturalTop - h);
            want = Mathf.Min(want, maxLift);

            if (!Mathf.Approximately(want, _lift))
            {
                _lift = want;
                // A transform, not a layout change: this runs on every scroll event, and dirtying layout there
                // would re-solve the whole inspector for a two-pixel nudge.
                style.translate = new Translate(0, _lift);
            }

            // Also here, not only in Sync(). Sync runs on the redraw tick, and that tick returns early while
            // the game is not playing — which is precisely when a finished recording is scrubbed and scrolled.
            // DebugOverlayHost places new cards correctly to begin with; this catches anything that did not
            // go through it.
            if (parent != null && parent.childCount > 1 && parent[parent.childCount - 1] != this) BringToFront();

            bool pinned = _lift > 0.5f;
            if (pinned == _pinned) return;
            _pinned = pinned;
            EnableInClassList("sasq-debug-transport--pinned", pinned);
        }

        // ---- export ------------------------------------------------------------------------------------

        private void CopyCsv()
        {
            if (_recorder.Count == 0) { Flash("Nothing recorded yet"); return; }
            string csv = BuildCsv();
            EditorGUIUtility.systemCopyBuffer = csv;
            Flash($"Copied {_recorder.Count} rows to the clipboard");
        }

        private void SaveCsv()
        {
            if (_recorder.Count == 0) { Flash("Nothing recorded yet"); return; }
            string suggested = (_recorder.TargetType != null ? _recorder.TargetType.Name : "trace") + ".csv";
            string path = EditorUtility.SaveFilePanel("Save Live Debug trace", "", suggested, "csv");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                System.IO.File.WriteAllText(path, BuildCsv());
                Flash($"Saved {_recorder.Count} rows");
            }
            catch (Exception e)
            {
                // Surfaced in the bar AND the console: the bar is where the user is looking, the console is
                // where the actual reason (permissions, a locked file) can be read.
                Flash("Save failed — see console");
                Debug.LogWarning($"[Sasquatch Inspector] Could not save the Live Debug trace to \"{path}\": {e.Message}");
            }
        }

        /// <summary>
        /// The whole trace as CSV — one row per sample, one column per series. Pastes into a spreadsheet, or
        /// straight onto a bug report, which the package's reporter already accepts as an attachment.
        /// </summary>
        private string BuildCsv()
        {
            var sb = new System.Text.StringBuilder();
            var channels = _recorder.Channels;

            sb.Append("frame,time");
            for (int c = 0; c < channels.Count; c++)
            {
                var ch = channels[c];
                if (ch.SeriesCount == 1) { sb.Append(',').Append(Escape(ch.Label)); continue; }
                // Escape the WHOLE header including the axis suffix. Appending ".x" after Escape put it
                // outside the closing quote — `"Speed, m/s".x` — which is neither a quoted field nor an
                // unquoted one, and no reader accepts it. Labels are free text the user types in the designer,
                // so a comma in one is ordinary.
                for (int s = 0; s < ch.SeriesCount; s++)
                    sb.Append(',').Append(Escape(ch.Label + "." + "xyzw"[Mathf.Min(s, 3)]));
            }
            sb.Append('\n');

            int count = _recorder.Count;
            for (int i = 0; i < count; i++)
            {
                int abs = _recorder.OldestAbsolute + i;
                int slot = _recorder.SlotOf(abs);
                if (slot < 0) continue;
                sb.Append(_recorder.FrameOf(abs)).Append(',')
                  .Append(_recorder.ElapsedAt(abs).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
                for (int c = 0; c < channels.Count; c++)
                {
                    var ch = channels[c];
                    // An Opaque channel has no numeric meaning, so it exports the text it actually held.
                    if (ch.Kind == DebugChannelKind.Opaque) { sb.Append(',').Append(Escape(ch.Format(slot, abs))); continue; }
                    for (int s = 0; s < ch.SeriesCount; s++)
                        sb.Append(',').Append(ch.Read(slot, s).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// One CSV field.
        ///
        /// <para><b>A newline needs quoting exactly as much as a comma does.</b> RFC 4180 allows CR and LF
        /// INSIDE a quoted field and nowhere else, so an unquoted <c>[TextArea]</c> value, a pasted multi-line
        /// name or a path with a stray line break split the row in two and shifted every column after it. The
        /// file then does not parse as CSV at all — which is the one thing an export button has to guarantee,
        /// and the failure appears in the spreadsheet rather than here.</para>
        /// </summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // NEWLINES ARE FLATTENED, not quoted. RFC 4180 does allow a line break inside a quoted field, but
            // the guarantee this export has to make is stronger and simpler: ONE LINE PER SAMPLE. A trace is
            // read by counting rows and by tools that split on '\n' before they parse quotes — and a
            // [TextArea] value, a pasted multi-line name or a path with a stray break would otherwise turn one
            // sample into two rows with every column after it shifted. Nothing meaningful is lost: a graph row
            // shows a value at an instant, not prose.
            if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

            return s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}
