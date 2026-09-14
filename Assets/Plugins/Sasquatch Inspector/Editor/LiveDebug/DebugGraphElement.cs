using System;
using UnityEditor;
using SasquatchSpring;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor.LiveDebug
{
    /// <summary>
    /// The rolling graph well for one recorded field: a Painter2D trace of that field's history, a shared
    /// playhead, and a readout. Newest sample on the right; older data scrolls off the left edge.
    ///
    /// <para>Paint shape is borrowed from <c>SasquatchSpringControl</c> (N samples spread across the full
    /// width, auto-framed Y) and the pixel-snapping / grid helpers from <c>SasquatchCurveControl</c>. Two rules
    /// from those files carry over and are load-bearing here: <b>never allocate inside
    /// generateVisualContent</b> (the point buffer is grow-only), and <b>capture skin colours at
    /// construction</b> — the ctor runs inside the build's <c>WidgetPalette</c> scope, the paint callback
    /// does not.</para>
    ///
    /// <para>The playhead is a styled child element rather than a painted line, matching the audio waveform:
    /// moving it is a style write, not a repaint of the whole trace.</para>
    /// </summary>
    internal sealed class DebugGraphElement : VisualElement
    {
        internal const float WellHeight = 38f;
        private const float PadFraction = 0.10f;
        private const float MinGridPx = 8f;
        private const int MaxDrawSamples = 1024;
        /// <summary>How close a released drag has to land before it snaps onto a recorded change.</summary>
        private const float SnapRadiusPx = 7f;

        private readonly DebugRecorder _recorder;
        private readonly DebugChannel _channel;
        private readonly VisualElement _well;
        private readonly VisualElement _playhead;
        private readonly Label _valueLabel;
        private readonly Label _extentLabel;
        private readonly Label _ghost;
        private readonly VisualElement _readout;

        // Captured skin colours — see the class note.
        private readonly Color _wellBg;
        private readonly Color _gridInk;
        private readonly Color _trace;
        private readonly Color _traceFill;
        private readonly Color _mutedInk;
        // Log-pin inks. CAPTURED HERE for the reason in the class note — the paint callback runs outside the
        // build's WidgetPalette scope, so resolving them there would read the AMBIENT editor skin rather than
        // the skin this layout actually resolved to (a layout can pin a theme whose BaseSkin differs).
        private readonly Color _pinErrorInk;
        private readonly Color _pinWarningInk;
        private readonly Color _pinLogInk;

        private Vector2[] _points = new Vector2[256];
        // Pin x-positions from the last paint, so a pointer near the top edge can be matched to a log without
        // recomputing the mapping. Grow-only, like every other buffer a paint pass touches.
        private float[] _pinX = new float[16];
        private int _pinCount;

        // ---- pin swell ---------------------------------------------------------------------------------
        // Each pin carries a spring-driven scale that grows while the playhead is close enough for the moment
        // card to be SHOWING that log's message. It is not decoration: the card lists everything within a time
        // window, and without this there is nothing tying a row in the card to a tick on the timeline. The
        // swell answers "which of these pins am I reading?" without a legend, a colour or a label.
        private float[] _pinScale = Settled(16);
        private float[] _pinVel = new float[16];
        private double _pinStepAt;
        /// <summary>Runs while any pin is still moving, so a spring never freezes half-grown between repaints.</summary>
        private IVisualElementScheduledItem _pinPump;

        /// <summary>How big a pin gets at full swell. Enough to notice, not enough to obscure the trace under it.</summary>
        private const float PinSwell = 1.9f;

        /// <summary>Snappy, with a little overshoot — the same shape the designer's other micro-motions use.</summary>
        private static readonly SpringConfig PinSpring = new SpringConfig(0.22f, 0.45f);
        private float _frameMin, _frameMax;

        // ---- REPAINT GATE (item 7) -------------------------------------------------------------------
        // Sync() runs up to 60 times a second per graph and used to end in an unconditional
        // MarkDirtyRepaint(). A full 44-graph pass costs 229 ms (412 ms with a full log ring); the same pass
        // with nothing dirty costs 0.048 ms. So the question "did anything the PAINT reads actually change?"
        // is worth asking, and these fields are the answer's memory.
        //
        // The set below is exactly the paint's inputs, and it is exhaustive on purpose:
        //   · Count + OldestAbsolute — the ring. While recording with a full ring Count stops moving and
        //     OldestAbsolute is the only thing that says the samples slid, which is why BOTH are here.
        //   · ReadoutAbsolute + ScrubAbsolute — the playhead, the readout, and the pin swell test.
        //   · focused — the row tell is drawn into the mesh.
        //   · SeenMin/SeenMax — the only non-constant inputs to ResolveFrame, which is what sets the
        //     _frameMin/_frameMax that every ValueToY reads. (ResolveFrame stays INSIDE the paint; nothing
        //     outside the paint reads those two, so there is nothing to hoist and nothing else to re-check.)
        //   · ChangeSamples.Count — markers and ribbon ticks, including the ribbon's per-tick colours, which
        //     are appended alongside.
        //   · DebugLogCapture.Revision — NOT its Count. See that property.
        //   · a pin spring still in flight — its own pump drives those frames, but arming is one tick late
        //     without this.
        // Colours (_wellBg/_gridInk/_trace) are readonly and captured at construction: a skin change rebuilds
        // the element, so they cannot drift underneath the gate.
        // ---- VIEWPORT GATE (item 10) ------------------------------------------------------------------
        //
        // UI Toolkit does no occlusion culling of mesh generation, and a ScrollView scrolls by TRANSFORM — so
        // a row scrolled far out of sight keeps display:Flex and keeps regenerating its whole mesh. Measured
        // on the fixture: 10 wells intersected the viewport and 34 did not, 238 generateVisualContent
        // invocations over 7 passes with ZERO skipped, and the off-screen 34 cost 316.7 ms of a 412.3 ms pass.
        // An element with no visible pixels has no observable output, so not drawing it cannot be seen.
        //
        // THE ENTIRE RISK IS COMING BACK. Because a ScrollView scrolls by transform, children get NO
        // GeometryChangedEvent when they scroll back in — so a gate that only listens for geometry leaves a
        // STALE TRACE on the first visible frame, which is a defect and not a feel trade. Rather than
        // enumerate triggers and hope the list is complete, this owes a repaint and settles it from FIVE
        // independent signals, any one of which is enough:
        //
        //   1. the enclosing ScrollView's scrollers      — scrolling, dragging, ScrollTo, the mouse wheel
        //   2. the viewport's GeometryChangedEvent       — the panel or the inspector being resized
        //   3. this well's own GeometryChangedEvent      — an ancestor display flip, a row inserted above,
        //                                                  a group expanding: all of them MOVE this element
        //   4. Sync()                                    — every redraw tick while recording
        //   5. a 200 ms backstop, armed only while a repaint is actually owed and the well is off-screen
        //
        // Signal 5 exists precisely because the first four are a judgement call about completeness. It turns
        // "we missed a trigger" from a permanently stale trace into at most one late frame, at the cost of a
        // worldBound comparison five times a second on rows that are off-screen anyway.
        //
        // With no enclosing ScrollView (the designer canvas, Showcase, tests) nothing is gated at all — the
        // same conservative default DebugTransportBar's sticky pin uses.
        private ScrollView _viewportScroll;
        private bool _repaintOwed;
        private IVisualElementScheduledItem _visibilityBackstop;

        /// <summary>Generous slack on the viewport test, so a well one rounding error outside it still paints.</summary>
        private const float ViewportSlackPx = 8f;

        private bool _hasPaintKey;
        private int _keyCount, _keyOldest, _keyReadout, _keyScrub, _keyChanges, _keyLogRevision;
        private bool _keyFocused;
        private float _keySeenMin, _keySeenMax;
        private bool _framed;

        /// <summary>The recorder's <see cref="DebugRecorder.Generation"/> when the frame was last settled. See <see cref="ResolveFrame"/>.</summary>
        private int _framedGeneration = -1;

        /// <summary>Last rendered extent text, so an unchanged summary is not rebuilt (nor its tooltip).</summary>
        private string _lastExtent;

        /// <summary>Last rendered empty-state text, same reason.</summary>
        private string _lastGhost;

        /// <summary>Whether the well currently accepts a drag. Starts true to match the field's default.</summary>
        private bool _interactive = true;

        private Vector2 _pressLocal;
        private bool _pressed;
        private bool _dragging;

        /// <summary>Raised when the user clicks (rather than drags) a sample — the moment card's trigger.</summary>
        internal event Action<int> SampleClicked;

        internal DebugChannel Channel => _channel;

        internal DebugGraphElement(DebugRecorder recorder, DebugChannel channel)
        {
            _recorder = recorder;
            _channel = channel;

            _wellBg = WidgetPalette.WellBg;
            _gridInk = WidgetPalette.Ink(0.06f);
            _mutedInk = WidgetPalette.TextGray;
            _trace = WidgetPalette.Accent;
            _traceFill = new Color(_trace.r, _trace.g, _trace.b, 0.16f);
            // The SAME palette entries the moment card's severity chip uses, so a pin and the card that opens
            // when you click it can never disagree about the colour of one log.
            _pinErrorInk = WidgetPalette.DangerText;
            _pinWarningInk = WidgetPalette.WarningCalloutText;
            _pinLogInk = WidgetPalette.TextGray;

            AddToClassList("sasq-debug-row");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            _well = new VisualElement();
            _well.AddToClassList("sasq-debug-well");
            _well.style.flexGrow = 1f;
            _well.style.flexShrink = 1f;
            _well.style.minWidth = 0;
            _well.style.height = WellHeight;
            _well.style.position = Position.Relative;
            _well.style.overflow = Overflow.Hidden;
            _well.generateVisualContent += OnGenerateVisualContent;
            // SIZE only, not every geometry event. GeometryChangedEvent also fires on a pure MOVE — a row
            // above this one changing height, the inspector scrolling content, a group expanding — and a move
            // cannot change a single vertex here: every draw method takes only (w, h) from contentRect plus
            // recorder state, so a translation produces a bit-identical mesh that UI Toolkit then re-renders
            // for nothing. On the fixture this is 44 wells repainting because something unrelated moved.
            _well.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.oldRect.size != evt.newRect.size) MarkWellDirty();
                // A pure MOVE cannot change a vertex — but it is exactly how a well comes back into view when
                // a row above it appears, a group expands, or a condition reveals something. So it is not a
                // repaint, it is a re-entry check.
                else FlushOwedRepaint();
            });
            Add(_well);

            _playhead = new VisualElement();
            _playhead.AddToClassList("sasq-debug-playhead");
            _playhead.style.position = Position.Absolute;
            _playhead.style.top = 0;
            _playhead.style.bottom = 0;
            _playhead.style.width = 2;
            _playhead.style.display = DisplayStyle.None;
            _playhead.pickingMode = PickingMode.Ignore;
            _well.Add(_playhead);

            // Painter2D cannot draw text, so every label is a real child on a non-pickable layer.
            _ghost = new Label("press play to record");
            _ghost.AddToClassList("sasq-debug-ghost");
            _ghost.style.position = Position.Absolute;
            _ghost.style.left = 0; _ghost.style.right = 0; _ghost.style.top = 0; _ghost.style.bottom = 0;
            _ghost.style.unityTextAlign = TextAnchor.MiddleCenter;
            _ghost.style.fontSize = 10;
            _ghost.style.color = _mutedInk;
            _ghost.pickingMode = PickingMode.Ignore;
            _well.Add(_ghost);

            var readout = new VisualElement();
            _readout = readout;
            readout.AddToClassList("sasq-debug-readout");
            readout.style.width = ReadoutWidthFor(1);
            readout.style.flexShrink = 0;
            readout.style.marginLeft = 6;
            readout.style.justifyContent = Justify.Center;
            // Pickable, unlike the rest of the row's chrome. These two labels are the only unexplained
            // notation in the whole feature — "89.49", "↓38.64 ↑100", "2 changes", "never changed" — and the
            // cheapest place to say what they mean is the place the question occurs. An Ignore'd element is
            // never the target of a TooltipEvent, so without this they could not carry the answer.
            readout.pickingMode = PickingMode.Position;

            _valueLabel = new Label("—");
            _valueLabel.AddToClassList("sasq-debug-value");
            _valueLabel.style.fontSize = 11;
            _valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _valueLabel.style.color = WidgetPalette.TextStrong;
            _valueLabel.pickingMode = PickingMode.Position;
            // BOTH properties, or neither does anything: `text-overflow: Ellipsis` is inert without
            // `overflow: hidden`, which is why the old Clip value never showed and the label simply painted
            // past its box to be cut by the panel edge instead. The column is sized to fit below, so this is
            // the safety net for the cases sizing cannot cover — a flags enum reading "Fire | Frost | Shock",
            // or a vector with more digits than the estimate allows. An "…" is the difference between a value
            // you know is truncated and one you misread as complete.
            _valueLabel.style.overflow = Overflow.Hidden;
            _valueLabel.style.textOverflow = TextOverflow.Ellipsis;
            _valueLabel.tooltip = "The value of this field at the playhead. While the playhead is released it " +
                                  "is the newest sample, so this is the live value; once you scrub it turns " +
                                  "yellow to say you are reading the past.";
            readout.Add(_valueLabel);

            _extentLabel = new Label("");
            _extentLabel.AddToClassList("sasq-debug-extent");
            _extentLabel.style.fontSize = 9;
            _extentLabel.style.color = _mutedInk;
            _extentLabel.pickingMode = PickingMode.Position;
            _extentLabel.style.overflow = Overflow.Hidden;
            _extentLabel.style.textOverflow = TextOverflow.Ellipsis;
            readout.Add(_extentLabel);

            Add(readout);

            WireScrub();
        }

        /// <summary>
        /// How wide the readout column has to be for a channel drawing <paramref name="seriesCount"/> numbers.
        ///
        /// <para>74px was a hard constant, and it is right for ONE number — but a Vector3 readout measures
        /// 106px, so a third of it was cut off at EVERY inspector width, 250px and 1400px alike, with no
        /// ellipsis to admit it. Someone reading <c>371.69, -83.93, 1</c> off a debug tool will believe it.
        /// (The column was also sized 1px under the example string its own tooltip documents.)</para>
        ///
        /// <para>16px per extra number, from the measured 74 → 106 across two extra numbers.</para>
        /// </summary>
        internal static float ReadoutWidthFor(int seriesCount) => 74f + Mathf.Max(0, seriesCount - 1) * 16f;

        /// <summary>
        /// Sets this row's readout width. Pushed by <see cref="DebugRunner.Install"/> once per build with the
        /// width the WIDEST channel on the recorder needs, so every row in one inspector gets the same number.
        ///
        /// <para>Uniform on purpose: rows sizing themselves independently would stagger the wells, and the
        /// left edge of the well column is what the whole feature aligns to. Uniform also means a layout with
        /// no vector fields keeps the original 74px and gives up no graph width for a case it does not have —
        /// which is why this is derived per recorder rather than raised to a global constant.</para>
        /// </summary>
        internal void SetReadoutWidth(float px)
        {
            if (_readout == null) return;
            _readout.style.width = px;
        }

        // ---- scrub -------------------------------------------------------------------------------------

        private void WireScrub()
        {
            // The gate's subscriptions follow the row's panel membership. Raw Attach/Detach rather than
            // PanelLifetime: these only add and remove event handlers, so a re-parent re-hooking them is
            // free — there is no resource to lose, which is what PanelLifetime exists to protect.
            RegisterCallback<AttachToPanelEvent>(_ => HookViewport());
            RegisterCallback<DetachFromPanelEvent>(_ => UnhookViewport());

            _well.RegisterCallback<PointerDownEvent>(evt =>
            {
                // An empty well is INERT — see SetInteractive. This guard is the second line of defence: the
                // well stops being pickable at all, so in practice the event never arrives.
                if (evt.button != 0 || _recorder.Count == 0) return;
                // Touching a row scopes the transport's jump-to-change to it.
                _recorder.SetFocus(_channel.Path);
                _pressed = true;
                _dragging = false;
                _pressLocal = evt.localPosition;
                _well.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            _well.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_pressed || !_well.HasPointerCapture(evt.pointerId)) return;
                // Below the threshold this is still a click. Committing to a scrub too early would make every
                // attempt to OPEN a moment card also nudge the playhead off the sample being asked about.
                if (!_dragging &&
                    (new Vector2(evt.localPosition.x, evt.localPosition.y) - _pressLocal).magnitude < DesignerPalette.DragThreshold)
                    return;
                _dragging = true;
                _recorder.SetScrub(AbsoluteAt(evt.localPosition.x));
                evt.StopPropagation();
            });

            _well.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_pressed) return;
                _pressed = false;
                if (_well.HasPointerCapture(evt.pointerId)) _well.ReleasePointer(evt.pointerId);

                int abs = SnapToChange(AbsoluteAt(evt.localPosition.x));
                _recorder.SetScrub(abs);
                // A drag now opens the card too. Pass 1 only opened it on a click, which meant the card kept
                // rendering the first sample you ever picked while the playhead moved away underneath it.
                SampleClicked?.Invoke(abs);
                _dragging = false;
                evt.StopPropagation();
            });
        }

        /// <summary>
        /// Pulls a released drag onto the nearest recorded change within <see cref="SnapPx"/> pixels.
        ///
        /// <para>Without this the interesting frames are unreachable: ~1,200 samples across ~300 px is four
        /// samples per pixel, so a value that moves on four frames out of the whole trace cannot be landed on
        /// by hand. Snapping is what makes "click the drop" a gesture rather than a lottery.</para>
        /// </summary>
        private int SnapToChange(int absolute)
        {
            if (absolute < 0 || _recorder.Count < 2) return absolute;
            int nearest = _recorder.NearestChange(absolute, 0);
            if (nearest < 0) return absolute;

            float w = Mathf.Max(1f, _well.contentRect.width);
            float perSample = w / Mathf.Max(1, _recorder.Count - 1);
            float distancePx = Mathf.Abs(nearest - absolute) * perSample;
            return distancePx <= SnapRadiusPx ? nearest : absolute;
        }

        /// <summary>Maps a local X inside the well to the absolute sample index under it.</summary>
        private int AbsoluteAt(float localX)
        {
            float w = Mathf.Max(1f, _well.contentRect.width);
            float t = Mathf.Clamp01(localX / w);
            int count = _recorder.Count;
            if (count <= 0) return -1;
            return _recorder.OldestAbsolute + Mathf.Clamp(Mathf.RoundToInt(t * (count - 1)), 0, count - 1);
        }

        // ---- sync --------------------------------------------------------------------------------------

        /// <summary>Re-reads the recorder and repaints. Driven by the owning root's tick and by scrub changes.</summary>
        internal void Sync()
        {
            // The focus tell. Cheap enough to re-assert every tick (EnableInClassList early-outs when the
            // class is already where it should be), and re-asserting is what keeps it right after a rebuild.
            EnableInClassList("sasq-debug-row--focused", _recorder.FocusPath == _channel.Path);

            int count = _recorder.Count;
            SetInteractive(count > 0);

            _ghost.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (count == 0)
            {
                string ghost = GhostText();
                if (ghost != _lastGhost) { _lastGhost = ghost; _ghost.text = ghost; }
            }

            int readAbs = _recorder.ReadoutAbsolute;
            int slot = _recorder.SlotOf(readAbs);
            if (slot >= 0)
            {
                _valueLabel.text = _channel.Format(slot, readAbs);
                _valueLabel.style.color = _recorder.ScrubAbsolute >= 0 ? WidgetPalette.Accent : WidgetPalette.TextStrong;
            }
            else
            {
                _valueLabel.text = "—";
            }

            string extent = ExtentText(count);
            if (extent != _lastExtent)
            {
                // Only when it CHANGES: Sync runs up to sixty times a second, and both of these are
                // interpolated strings.
                _lastExtent = extent;
                _extentLabel.text = extent;
                _extentLabel.tooltip = ExtentTooltip();
            }

            // Playhead: shown only while parked. Live recording already puts "now" at the right edge, so a
            // playhead there would just be a line that never moves.
            if (_recorder.ScrubAbsolute >= 0 && count > 1)
            {
                float t = (_recorder.ScrubAbsolute - _recorder.OldestAbsolute) / (float)(count - 1);
                _playhead.style.display = DisplayStyle.Flex;
                _playhead.style.left = Length.Percent(Mathf.Clamp01(t) * 100f);
            }
            else
            {
                _playhead.style.display = DisplayStyle.None;
            }

            if (PaintInputsChanged()) MarkWellDirty();
            // Sync runs on the redraw tick while recording, which makes it one more independent chance to
            // notice the well came back on screen.
            else FlushOwedRepaint();
        }

        /// <summary>
        /// Requests a repaint, or records that one is OWED when the well cannot be seen. Every path that used
        /// to call <c>_well.MarkDirtyRepaint()</c> goes through here.
        /// </summary>
        private void MarkWellDirty()
        {
            if (WellIsVisible())
            {
                _repaintOwed = false;
                _well.MarkDirtyRepaint();
                return;
            }
            _repaintOwed = true;
            ArmVisibilityBackstop();
        }

        /// <summary>Pays back a repaint owed from while the well was off-screen, the moment it can be seen.</summary>
        private void FlushOwedRepaint()
        {
            if (!_repaintOwed || !WellIsVisible()) return;
            _repaintOwed = false;
            _well.MarkDirtyRepaint();
        }

        /// <summary>
        /// Does this well overlap the enclosing scroll viewport? Every uncertain answer resolves to TRUE:
        /// no ScrollView, no panel, or a layout that has not settled into real numbers all mean "paint it".
        /// A wrong TRUE costs one mesh; a wrong FALSE is a stale trace on screen.
        /// </summary>
        private bool WellIsVisible()
        {
            if (_viewportScroll == null || panel == null) return true;

            // NaN is tested EXPLICITLY, on width and height as well as position. Before a panel's first
            // layout pass a worldBound reads (0, 0, NaN, NaN) — and `NaN <= 0f` is FALSE under IEEE, so a
            // size test alone sails straight past it into Rect.Overlaps(NaN), which answers "no overlap" and
            // skips the paint of a row that simply has not been measured yet. Found by the hosted-panel test,
            // which is the only place that state is reachable.
            var wellBound = _well.worldBound;
            if (!IsUsableRect(wellBound)) return true;

            var viewport = _viewportScroll.contentViewport.worldBound;
            if (!IsUsableRect(viewport)) return true;

            // Inflated: the win comes from rows that are hundreds of pixels away, so a few pixels of slack
            // costs nothing measurable and removes any argument about boundary rounding.
            viewport.xMin -= ViewportSlackPx;
            viewport.yMin -= ViewportSlackPx;
            viewport.xMax += ViewportSlackPx;
            viewport.yMax += ViewportSlackPx;
            return wellBound.Overlaps(viewport);
        }

        /// <summary>A world rect with real, positive, measured numbers in it — see WellIsVisible on NaN.</summary>
        private static bool IsUsableRect(Rect r)
            => !float.IsNaN(r.x) && !float.IsNaN(r.y)
               && !float.IsNaN(r.width) && !float.IsNaN(r.height)
               && r.width > 0f && r.height > 0f;

        private void HookViewport()
        {
            if (_viewportScroll != null) return;
            _viewportScroll = GetFirstAncestorOfType<ScrollView>();
            if (_viewportScroll == null) return;   // designer canvas, Showcase, tests — nothing to gate against

            _viewportScroll.verticalScroller.valueChanged += OnViewportScrolled;
            _viewportScroll.horizontalScroller.valueChanged += OnViewportScrolled;
            _viewportScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometry);
            // Whatever happened while this row was unparented, settle it now.
            FlushOwedRepaint();
        }

        private void UnhookViewport()
        {
            if (_viewportScroll == null) return;
            _viewportScroll.verticalScroller.valueChanged -= OnViewportScrolled;
            _viewportScroll.horizontalScroller.valueChanged -= OnViewportScrolled;
            _viewportScroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometry);
            _viewportScroll = null;
        }

        private void OnViewportScrolled(float _) => FlushOwedRepaint();
        private void OnViewportGeometry(GeometryChangedEvent _) => FlushOwedRepaint();

        private void ArmVisibilityBackstop()
        {
            if (_visibilityBackstop == null)
                _visibilityBackstop = schedule.Execute(VisibilityBackstopTick).Every(200);
            else
                _visibilityBackstop.Resume();
        }

        // Runs ONLY while a repaint is owed. Paused from inside the tick (never from a detach handler, which
        // UI Toolkit would not resume) and resumed by ArmVisibilityBackstop when something is owed again.
        private void VisibilityBackstopTick()
        {
            if (!_repaintOwed)
            {
                _visibilityBackstop?.Pause();
                return;
            }
            FlushOwedRepaint();
        }

        /// <summary>
        /// Reads every input the mesh is built from and reports whether any of them moved since the last
        /// call, latching the new values. When they are all identical the mesh Painter2D would emit is
        /// bit-identical to the one already on screen, so skipping the repaint cannot be observed.
        ///
        /// <para>Deliberately NOT a saving while actively recording — everything really is changing then, so
        /// this returns true every tick and costs one comparison. The saving is the rest of the time: a
        /// restored trace being read in edit mode, a paused recording, an inspector sitting open.</para>
        /// </summary>
        private bool PaintInputsChanged()
        {
            int count = _recorder.Count;
            int oldest = _recorder.OldestAbsolute;
            int readout = _recorder.ReadoutAbsolute;
            int scrub = _recorder.ScrubAbsolute;
            bool focused = _recorder.FocusPath == _channel.Path;
            float seenMin = _channel.SeenMin;
            float seenMax = _channel.SeenMax;
            int changes = _channel.ChangeSamples.Count;
            int logRevision = DebugLogCapture.Revision;
            // A settling pin re-arms its own pump, which marks dirty directly; this keeps the FIRST frame of
            // a swell from waiting a tick for that pump to exist.
            bool pinsMoving = _pinPump != null && _pinPump.isActive;

            bool changed = !_hasPaintKey
                           || count != _keyCount
                           || oldest != _keyOldest
                           || readout != _keyReadout
                           || scrub != _keyScrub
                           || focused != _keyFocused
                           // .Equals, not ==: SeenMin/SeenMax start at +/-Infinity and a NaN sample would
                           // otherwise compare unequal to itself and mark dirty forever.
                           || !seenMin.Equals(_keySeenMin)
                           || !seenMax.Equals(_keySeenMax)
                           || changes != _keyChanges
                           || logRevision != _keyLogRevision
                           || pinsMoving;

            _hasPaintKey = true;
            _keyCount = count;
            _keyOldest = oldest;
            _keyReadout = readout;
            _keyScrub = scrub;
            _keyFocused = focused;
            _keySeenMin = seenMin;
            _keySeenMax = seenMax;
            _keyChanges = changes;
            _keyLogRevision = logRevision;
            return changed;
        }

        /// <summary>
        /// What the summary under the value actually means. Three different sentences, because the label is
        /// three different quantities depending on what kind of field this is — which is precisely why it
        /// needed explaining.
        /// </summary>
        private string ExtentTooltip()
        {
            if (!_channel.EverChanged)
                return "This field held the same value for the whole recording — nothing in the game wrote a " +
                       "different one while the trace was running.";
            switch (_channel.Kind)
            {
                case DebugChannelKind.Opaque:
                case DebugChannelKind.Bool:
                case DebugChannelKind.Enum:
                    return "How many times this field changed during the recording. Use the jump-to-change " +
                           "buttons on the toolbar to step between them — click this row first and they will " +
                           "follow THIS field rather than whatever moved most recently.";
                default:
                    return "The lowest (↓) and highest (↑) value this field reached during the recording. The " +
                           "graph above is framed to that range, so a flat-looking trace with a wide range " +
                           "here means the interesting part has already scrolled off the left edge.";
            }
        }

        /// <summary>
        /// Turns the well's interactivity on and off with the data.
        ///
        /// <para>An empty graph used to accept a drag: the playhead went nowhere, no card opened, and nothing
        /// on screen acknowledged the gesture — which reads as the feature being broken rather than as there
        /// being nothing to read. A control that cannot act must LOOK like it cannot act, which is the same
        /// rule the transport bar's buttons follow; here that means not being pickable at all, so the pointer
        /// does not even change over it.</para>
        /// </summary>
        private void SetInteractive(bool on)
        {
            if (on == _interactive) return;
            _interactive = on;
            _well.pickingMode = on ? PickingMode.Position : PickingMode.Ignore;
            // Released as well as blocked: a well that stops being pickable mid-drag would otherwise strand
            // the press state and swallow the next real click.
            if (!on) { _pressed = false; _dragging = false; }
        }

        /// <summary>
        /// Why this graph is empty, in the reader's terms. There are four different nothings here and they
        /// call for four different actions — the old text was a fixed "press play to record", which was a
        /// confident lie in three of them (you may already be in play mode, already recording, or stopped
        /// with the trigger never having fired).
        /// </summary>
        private string GhostText()
        {
            if (!DebugSampleClock.ClockRunningFor(_recorder.TargetType)) return "no data recorded — press play";

            switch (DebugCaptureController.PhaseOf(_recorder.TargetType))
            {
                case DebugCapturePhase.Recording:
                    return _recorder.TickSource == DebugTickSource.Manual
                        ? "recording — press Capture to take a sample"
                        : "recording…";
                case DebugCapturePhase.Waiting:
                    return "waiting for the start trigger";
                default:
                    return "no data recorded";
            }
        }

        private string ExtentText(int count)
        {
            if (count == 0) return "";
            // "never changed" is the highest-value thing this row can say, so it outranks the numbers.
            if (!_channel.EverChanged) return "never changed";
            switch (_channel.Kind)
            {
                case DebugChannelKind.Opaque:
                    return _channel.ChangeSamples.Count == 1
                        ? "1 change"
                        : $"{_channel.ChangeSamples.Count} changes";
                case DebugChannelKind.Bool:
                case DebugChannelKind.Enum:
                    return $"{_channel.ChangeSamples.Count} changes";
                default:
                    if (float.IsInfinity(_channel.SeenMin)) return "";
                    return $"↓{DebugChannel.Trim(_channel.SeenMin)}  ↑{DebugChannel.Trim(_channel.SeenMax)}";
            }
        }

        // ---- paint -------------------------------------------------------------------------------------

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            float w = Mathf.Max(1f, _well.contentRect.width);
            float h = Mathf.Max(1f, _well.contentRect.height);
            float ppp = EditorGUIUtility.pixelsPerPoint;

            FillRect(painter, 0f, 0f, w, h, _wellBg);

            int count = _recorder.Count;
            if (count < 2) return;

            ResolveFrame();
            DrawGrid(painter, w, h, ppp);

            if (_channel.Kind == DebugChannelKind.Opaque)
            {
                DrawChangeRibbon(painter, w, h, count);
                return;
            }

            bool stepped = _channel.Kind == DebugChannelKind.Bool || _channel.Kind == DebugChannelKind.Enum;
            for (int s = 0; s < _channel.SeriesCount; s++)
                DrawSeries(painter, w, h, count, s, stepped);

            DrawChangeMarkers(painter, w, h, count);
            DrawLogPins(painter, w, h, count);
        }

        /// <summary>
        /// A tick on the bottom edge wherever THIS channel changed.
        ///
        /// <para>These are the feature's signposts. A value that moves on four frames out of twelve hundred is
        /// invisible in the trace itself — the line is flat almost everywhere — so without markers the user is
        /// asked to find an event by scrubbing blind. With them, the interesting frames are the ones you can
        /// see, and <see cref="SnapToChange"/> makes them the ones you land on.</para>
        /// </summary>
        private void DrawChangeMarkers(Painter2D painter, float w, float h, int count)
        {
            var marks = _channel.ChangeSamples;
            if (marks.Count == 0) return;

            int oldest = _recorder.OldestAbsolute;
            float top = h - 5f;
            var ink = new Color(_trace.r, _trace.g, _trace.b, 0.85f);

            var batch = new QuadBatcher(painter);
            for (int i = 0; i < marks.Count; i++)
            {
                int abs = marks[i];
                if (abs < oldest) continue;
                float t = (abs - oldest) / (float)Mathf.Max(1, count - 1);
                if (t < 0f || t > 1f) continue;
                batch.Add(t * w - 0.5f, top, 1.5f, 5f, ink);
            }
            batch.Flush();
        }

        /// <summary>
        /// A tick on the top edge for every captured console log, coloured by severity.
        ///
        /// <para>This is what connects the two halves of a debugging session: the value you are watching and the
        /// <c>Debug.Log</c> you already wrote about it, on one timeline. Clicking a pin parks the playhead there
        /// and opens the card, so a log is one gesture away from the line of code that emitted it.</para>
        /// </summary>
        private void DrawLogPins(Painter2D painter, float w, float h, int count)
        {
            if (DebugLogCapture.Count == 0)
            {
                // PAUSE BEFORE THE EARLY-OUT, not after it. PumpPins(false) used to sit at the bottom of this
                // method, below this return — so once the log ring emptied it became unreachable, and a 60Hz
                // MarkDirtyRepaint pump armed by the last swelling pin ran forever with nothing left to
                // animate. Measured: 40 of 44 graphs holding an active pump doing no work.
                PumpPins(false);
                return;
            }

            int oldest = _recorder.OldestAbsolute;
            // Read the ring IN PLACE. DebugLogCapture.All() built a fresh List<Entry> here on every graph on
            // every paint — 29,368 bytes a call, 1.26 MB of garbage per repaint pass across 44 graphs — for a
            // list that was walked once and thrown away. Same entries, same order.
            int logCount = DebugLogCapture.Count;
            _pinCount = 0;
            if (_pinX.Length < logCount) GrowPinBuffers(Mathf.NextPowerOfTwo(Mathf.Max(8, logCount)));

            // The instant the readouts are describing, so "close enough" means exactly what the moment card
            // means by it rather than some second, invisible definition.
            double playheadTime = _recorder.TimeOf(_recorder.ReadoutAbsolute);
            float dt = StepPinClock();
            bool moving = false;

            var batch = new QuadBatcher(painter);
            for (int i = 0; i < logCount; i++)
            {
                var entry = DebugLogCapture.At(i);
                int abs = _recorder.AbsoluteNearestTime(entry.Time);
                if (abs < oldest) continue;
                float t = (abs - oldest) / (float)Mathf.Max(1, count - 1);
                if (t < 0f || t > 1f) continue;
                float x = t * w;

                int slot = _pinCount;
                // Target is the SAME test the moment card uses to decide whether to list this log, so a pin is
                // swollen if and only if its message is on screen.
                bool near = System.Math.Abs(entry.Time - playheadTime) <= DebugMomentCard.LogWindowSeconds;
                float target = near ? PinSwell : 1f;

                if (dt > 0f && slot < _pinScale.Length)
                {
                    SpringMath.Evaluate(dt, _pinScale[slot], _pinVel[slot], target,
                        PinSpring.Frequency, PinSpring.Damping, PinSpring.ResolvedSettleEpsilon,
                        out float pos, out float vel);
                    _pinScale[slot] = pos;
                    _pinVel[slot] = vel;
                    if (Mathf.Abs(pos - target) > 0.002f || Mathf.Abs(vel) > 0.002f) moving = true;
                }

                float scale = slot < _pinScale.Length && _pinScale[slot] > 0f ? _pinScale[slot] : 1f;
                float pw = 2f * scale;
                float ph = 6f * scale;
                batch.Add(x - pw * 0.5f, 0f, pw, ph, LogPinColor(entry.Type));
                if (_pinCount < _pinX.Length) _pinX[_pinCount++] = x;
            }
            batch.Flush();

            PumpPins(moving);
        }

        /// <summary>A pin-scale buffer whose entries all start settled at full size.</summary>
        private static float[] Settled(int size)
        {
            var a = new float[size];
            for (int i = 0; i < size; i++) a[i] = 1f;
            return a;
        }

        private void GrowPinBuffers(int size)
        {
            _pinX = new float[size];
            var scale = Settled(size);
            var vel = new float[size];
            // Carry the in-flight scales across, so a new log arriving does not restart every existing pin's
            // animation. Anything beyond the old length stays at the settled 1 from Settled().
            for (int i = 0; i < size && i < _pinScale.Length; i++) scale[i] = _pinScale[i];
            for (int i = 0; i < size && i < _pinVel.Length; i++) vel[i] = _pinVel[i];
            _pinScale = scale;
            _pinVel = vel;
        }

        /// <summary>
        /// Real elapsed seconds since the last pin step. Wall-clock, not <c>Time.deltaTime</c>: these springs
        /// run while the editor is PAUSED and while you scrub a frozen trace, where the game clock is stopped.
        /// </summary>
        private float StepPinClock()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_pinStepAt <= 0d) { _pinStepAt = now; return 0f; }
            float dt = (float)(now - _pinStepAt);
            _pinStepAt = now;
            // A domain reload or a stalled editor can hand over a huge gap; clamp so the spring integrates a
            // plausible frame instead of teleporting to its target.
            return Mathf.Clamp(dt, 0f, 0.1f);
        }

        /// <summary>
        /// Keeps repainting while a pin is mid-flight. The graph is otherwise only redrawn when something
        /// changes, so without this a spring stops wherever it happened to be when the last repaint landed —
        /// leaving pins frozen at odd half-grown sizes, which looks like a rendering fault rather than motion.
        /// </summary>
        private void PumpPins(bool moving)
        {
            if (moving)
            {
                if (_pinPump == null)
                    _pinPump = _well.schedule.Execute(MarkWellDirty).Every(16);
                else
                    _pinPump.Resume();
            }
            else
            {
                _pinPump?.Pause();
                // Deliberately NOT resetting _pinStepAt here. Doing so sent StepPinClock down its first-call
                // branch on every paint, so dt was permanently zero — which meant no pin could ever start
                // moving, which kept `moving` false, which reset the clock again. A self-sustaining freeze
                // that looks identical to the feature never having been wired up. The clamp inside
                // StepPinClock is what actually guards against a stale gap.
            }
        }

        /// <summary>
        /// The ink for one log pin.
        ///
        /// <para><b>Palette lookups, not literals, and they must be the SAME lookups the moment card's
        /// severity chip uses.</b> These were three hardcoded colours that did not flip in the light theme and
        /// did not match the card in EITHER theme — so clicking a pin on a light-skin graph opened a card that
        /// described the same log in a different colour. Colour is doing real work on this layer; it is the
        /// only thing the pin carries besides position.</para>
        /// </summary>
        private Color LogPinColor(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return _pinErrorInk;
                case LogType.Warning:
                    return _pinWarningInk;
                default:
                    return _pinLogInk;
            }
        }

        /// <summary>
        /// Settles the Y window. An authored <c>[Range]</c> wins; otherwise the frame auto-fits and then only
        /// ever GROWS — a window that re-fits every tick makes the shape unreadable, because the line stays
        /// mid-height while the axis silently moves under it.
        /// </summary>
        private void ResolveFrame()
        {
            // A NEW RECORDING RE-FRAMES. Growing-only is deliberate WITHIN one recording — a window that
            // re-fits every tick makes the shape unreadable, because the line stays mid-height while the axis
            // moves under it — but Clear, a fresh Record press and a mid-recording clock change all START a
            // recording, and inheriting the old range then draws the new one as a flat line pinned to the
            // bottom of the well: a run that peaked at 1000 leaves the next 0..1 field invisible.
            //
            // Keyed on the recorder's GENERATION, not on its sample counters. Clear resets `Absolute` to 0, so
            // two samples into the new recording it reads exactly what it read two samples into the old one —
            // there is no value of the counters that distinguishes the two. Checked here rather than in Sync
            // because a graph is repainted without being synced.
            int generation = _recorder.Generation;
            if (generation != _framedGeneration) _framed = false;
            _framedGeneration = generation;

            float lo, hi;
            switch (_channel.Kind)
            {
                case DebugChannelKind.Bool:
                    _frameMin = -0.15f; _frameMax = 1.15f; _framed = true;
                    return;
                case DebugChannelKind.Enum:
                {
                    // The member COUNT only frames the ring while the ring holds positional indices. Once the
                    // channel records the enum's real numbers (DebugChannel.EnumValues) a member can sit at any
                    // value — 0, 1, 2, 4, 8 for a [Flags] set, or a hand-numbered `Legacy = 99` — and a window
                    // of 0..count-1 puts most of the trace off the top of the well. Frame what was actually
                    // seen in that case, one step of padding either side, exactly as the members would get.
                    if (_channel.EnumValues != null)
                    {
                        lo = _channel.SeenMin; hi = _channel.SeenMax;
                        if (float.IsInfinity(lo) || float.IsInfinity(hi)) { lo = 0f; hi = 1f; }
                        if (hi - lo < 1e-6f) { lo -= 0.5f; hi += 0.5f; }
                        _frameMin = lo - 0.4f; _frameMax = hi + 0.4f; _framed = true;
                        return;
                    }
                    int n = _channel.EnumNames != null ? _channel.EnumNames.Length : 1;
                    _frameMin = -0.4f; _frameMax = Mathf.Max(1, n - 1) + 0.4f; _framed = true;
                    return;
                }
                default:
                    if (_channel.HasAuthoredRange)
                    {
                        _frameMin = _channel.AuthoredMin; _frameMax = _channel.AuthoredMax; _framed = true;
                        return;
                    }
                    lo = _channel.SeenMin; hi = _channel.SeenMax;
                    break;
            }

            if (float.IsInfinity(lo) || float.IsInfinity(hi)) { _frameMin = 0f; _frameMax = 1f; _framed = true; return; }

            float span = hi - lo;
            // A dead-flat channel still needs a non-zero window or every sample lands on one pixel row.
            if (span < 1e-6f) { lo -= 0.5f; hi += 0.5f; span = hi - lo; }
            float pad = span * PadFraction;
            lo -= pad; hi += pad;

            if (!_framed) { _frameMin = lo; _frameMax = hi; _framed = true; return; }
            _frameMin = Mathf.Min(_frameMin, lo);
            _frameMax = Mathf.Max(_frameMax, hi);
        }

        private float ValueToY(float v, float h)
        {
            // NaN FIRST, before anything that would carry it through. Every IEEE comparison against NaN is
            // false, so Mathf.InverseLerp, Mathf.Clamp01 and Mathf.Clamp all pass it out unchanged — and a NaN
            // vertex handed to Painter2D corrupts the whole subpath, taking the samples either side of the bad
            // one with it. A NaN reading is a real thing to want to SEE (it is usually the bug), so it is drawn
            // at the bottom of the well rather than skipped: the trace visibly bottoms out at exactly the
            // sample that went bad, and the readout still prints "NaN" in words.
            if (float.IsNaN(v)) return h;

            float t = Mathf.InverseLerp(_frameMin, _frameMax, v);
            // Clamped generously rather than to [0,h]: a spike should visibly leave the well rather than
            // flatten against its edge, but it must not blow the mesh out to infinity either.
            return Mathf.Clamp((1f - t) * h, -h, 2f * h);
        }

        private void DrawSeries(Painter2D painter, float w, float h, int count, int series, bool stepped)
        {
            // Round the stride UP. `count / draw` truncates, and a full 1200-sample ring against a
            // 1024-point budget gives 1200/1024 == 1 — so the loop stopped at sample 1023 of 1199, the
            // newest ~3 seconds never reached the well, and the right 15% of every graph was blank ink
            // that the playhead, the change markers and the log pins all still drew into, because every
            // one of those maps across the FULL width.
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / (float)MaxDrawSamples));
            int draw = Mathf.CeilToInt(count / (float)stride);
            int needed = stepped ? draw * 2 : draw;
            if (_points.Length < needed) _points = new Vector2[Mathf.NextPowerOfTwo(needed)];

            int p = 0;
            float prevY = 0f;
            for (int i = 0; i < draw; i++)
            {
                // The last point is pinned to the newest sample rather than to a stride multiple. The right
                // edge of the well IS "now" — the class doc says so and all five overlays assume it — and a
                // stride that does not divide the count exactly would otherwise stop a few samples short.
                int sampleIndex = i == draw - 1 ? count - 1 : Mathf.Min(count - 1, i * stride);
                int slot = _recorder.SlotAtOffset(sampleIndex);
                if (slot < 0) continue;
                float x = draw == 1 ? w : w * (sampleIndex / (float)(count - 1));
                float y = ValueToY(_channel.Read(slot, series), h);
                // A stepped trace holds its previous level until the instant it changes. That is what turns a
                // bool into a readable rectangle instead of a diagonal ramp between two states.
                if (stepped && p > 0) _points[p++] = new Vector2(x, prevY);
                _points[p++] = new Vector2(x, y);
                prevY = y;
            }
            if (p < 2) return;

            Color line = SeriesColor(series);

            // The soft under-fill reads as "level" and only makes sense for a single-series trace; stacking
            // three translucent fills for a Vector3 turns the well into mud.
            if (_channel.SeriesCount == 1)
            {
                painter.fillColor = new Color(line.r, line.g, line.b, _traceFill.a);
                painter.BeginPath();
                painter.MoveTo(new Vector2(_points[0].x, h));
                for (int i = 0; i < p; i++) painter.LineTo(_points[i]);
                painter.LineTo(new Vector2(_points[p - 1].x, h));
                painter.ClosePath();
                painter.Fill();
            }

            painter.strokeColor = line;
            painter.lineWidth = _channel.SeriesCount == 1 ? 1.6f : 1.2f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(_points[0]);
            for (int i = 1; i < p; i++) painter.LineTo(_points[i]);
            painter.Stroke();
        }

        /// <summary>Axis colours for a vector's series, so X/Y/Z read the same as everywhere else in the editor.</summary>
        private Color SeriesColor(int series)
        {
            if (_channel.SeriesCount == 1) return _trace;
            switch (series)
            {
                case 0: return Handles.xAxisColor;
                case 1: return Handles.yAxisColor;
                case 2: return Handles.zAxisColor;
                default: return _trace;
            }
        }

        /// <summary>A tick per recorded change — the readable form for a value that has no numeric shape.</summary>
        private void DrawChangeRibbon(Painter2D painter, float w, float h, int count)
        {
            int oldest = _recorder.OldestAbsolute;
            float top = h * 0.22f;
            float bottom = h * 0.78f;

            var batch = new QuadBatcher(painter);
            for (int i = 0; i < _channel.ChangeSamples.Count; i++)
            {
                int abs = _channel.ChangeSamples[i];
                if (abs < oldest) continue;
                float t = (abs - oldest) / (float)Mathf.Max(1, count - 1);
                if (t < 0f || t > 1f) continue;
                float x = t * w;
                Color tick = _channel.TryColorAt(abs, out var c) ? new Color(c.r, c.g, c.b, 1f) : _trace;
                batch.Add(x - 1f, top, 2f, bottom - top, tick);
            }
            batch.Flush();
        }

        private void DrawGrid(Painter2D painter, float w, float h, float ppp)
        {
            painter.strokeColor = _gridInk;
            painter.lineWidth = 1f / ppp;
            painter.BeginPath();

            // Three horizontal rules is enough structure to read a level against without competing with the
            // trace. Anything denser than MinGridPx is skipped, matching the curve editor's floor.
            int lines = 3;
            if (h / (lines + 1) >= MinGridPx)
            {
                for (int i = 1; i <= lines; i++)
                {
                    float y = SnapPx(h * i / (lines + 1f), ppp);
                    painter.MoveTo(new Vector2(0f, y));
                    painter.LineTo(new Vector2(w, y));
                }
            }
            painter.Stroke();
        }

        // Painter2D has no FillRect; this is the manual quad, same as SasquatchCurveControl's.
        private static void FillRect(Painter2D painter, float x, float y, float w, float h, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + w, y));
            painter.LineTo(new Vector2(x + w, y + h));
            painter.LineTo(new Vector2(x, y + h));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>
        /// Accumulates the SAME quads <see cref="FillRect"/> emits into SHARED <c>Fill()</c> calls. Every
        /// marker, ribbon tick and log pin is one four-point sub-path, and one <c>Fill()</c> apiece is where
        /// almost all of a busy graph's repaint goes - measured at 8.34us a marker, 211ms of a 229ms pass on
        /// a fixture holding 15,514 markers across 44 graphs. Nothing about the geometry changes; only the
        /// call boundaries move.
        ///
        /// <para>TWO rules keep that true, and both are load-bearing:</para>
        ///
        /// <para>1. <b>Flush before a quad that would touch or overlap what is already batched.</b> Sub-paths
        /// inside ONE <c>Fill()</c> are resolved by NONZERO winding, so overlapping quads merge into a single
        /// region and get painted ONCE - whereas separate Fills composite them, and this ink is translucent
        /// (markers are alpha 0.85), so an overlap is visibly darker today. Measured on 24 quads 0.25px
        /// apart, batching blind collapsed 672 vertices to 328. Exactly-TOUCHING quads merge too (672 to
        /// 319), so the test is <c>&lt;=</c>, not <c>&lt;</c>. Dense markers therefore fall back to one Fill
        /// each and gain nothing - correct, and the reason this is a bounding-box test and not a counter.</para>
        ///
        /// <para>2. <b>Flush on a colour change.</b> <c>fillColor</c> applies to the whole Fill, and both the
        /// ribbon and the log pins colour per tick. Flushing there also keeps draw ORDER identical, which
        /// matters precisely because those two DO overlap.</para>
        ///
        /// <para>The cap is not a tidiness limit. Painter2D's fill tessellator is SUPERLINEAR in sub-path
        /// count, so batching everything into one Fill is far worse than not batching at all. Re-measured
        /// 2026-08-20 (median of 15, disjoint quads, n=353): unbatched 1.799ms; cap 8 -> 2.34x, cap 12 ->
        /// 2.42x, <b>cap 16 -> 2.42x</b>, cap 32 -> 2.24x, cap 64 -> 1.08x, and one Fill for all 353 measured
        /// 4.19ms - 2.3x SLOWER than doing nothing. 16 sits on the plateau.</para>
        ///
        /// <para>Pinned by TestQuadBatcherMatchesPerQuadFills, which compares the emitted vertex and index
        /// buffers against a verbatim copy of the one-Fill-per-quad path.</para>
        /// </summary>
        internal struct QuadBatcher
        {
            /// <summary>Sub-paths per Fill. See above - a measured plateau, not a round number.</summary>
            internal const int MaxSubPaths = 16;

            private readonly Painter2D _painter;
            private Color _color;
            private int _count;
            private float _minX, _maxX, _minY, _maxY;
            private bool _open;

            internal QuadBatcher(Painter2D painter)
            {
                _painter = painter;
                _color = default;
                _count = 0;
                _minX = _maxX = _minY = _maxY = 0f;
                _open = false;
            }

            /// <summary>Queues one quad, flushing first if it cannot legally share the open Fill.</summary>
            internal void Add(float x, float y, float w, float h, Color color)
            {
                if (_open && (!SameColor(color, _color) || _count >= MaxSubPaths || Touches(x, y, w, h)))
                    Flush();

                if (!_open)
                {
                    _color = color;
                    // Set exactly where FillRect sets it - before the path, once per Fill.
                    _painter.fillColor = color;
                    _painter.BeginPath();
                    _open = true;
                    _count = 0;
                    _minX = x; _maxX = x + w; _minY = y; _maxY = y + h;
                }
                else
                {
                    if (x < _minX) _minX = x;
                    if (x + w > _maxX) _maxX = x + w;
                    if (y < _minY) _minY = y;
                    if (y + h > _maxY) _maxY = y + h;
                }

                _painter.MoveTo(new Vector2(x, y));
                _painter.LineTo(new Vector2(x + w, y));
                _painter.LineTo(new Vector2(x + w, y + h));
                _painter.LineTo(new Vector2(x, y + h));
                _painter.ClosePath();
                _count++;
            }

            /// <summary>Emits whatever is queued. MUST be called before the caller draws anything else, or
            /// the batch lands on top of geometry that was drawn after it.</summary>
            internal void Flush()
            {
                if (!_open) return;
                if (_count > 0) _painter.Fill();
                _open = false;
                _count = 0;
            }

            // Tested against the batch's BOUNDING BOX: a quad outside it cannot touch any member, whatever
            // order they arrived in. Conservative the other way (it can flush for quads that would have
            // missed each other), which costs one Fill and never costs correctness.
            private bool Touches(float x, float y, float w, float h)
                => x <= _maxX && x + w >= _minX && y <= _maxY && y + h >= _minY;

            // Component-exact, NOT Color's ==, which compares with a tolerance. "Near enough to be equal" is
            // a decision about pixels that this has no business making.
            private static bool SameColor(Color a, Color b)
                => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        // Half-pixel centring so a 1px rule lands on a device pixel instead of smearing across two.
        private static float SnapPx(float v, float ppp) => (Mathf.Round(v * ppp - 0.5f) + 0.5f) / ppp;
    }
}
