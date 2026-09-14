using System;
using System.Collections.Generic;
using SasquatchSpring;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The shared draw + edit surface for the Animation Curve editor widget — a sleek, spring-driven
    /// alternative to Unity's CurveField. Drawn entirely with <see cref="Painter2D"/> (curve stroke, soft fill,
    /// a square value/time grid, keyframe diamonds, and draggable in/out tangent handles for the selected key),
    /// following the device-pixel-snapping discipline of <see cref="SasquatchIconElement"/> and the filled-path
    /// technique of <see cref="GripGlyph"/>. Axis numbers and optional editable Max-X / Max-Y fields overlay as
    /// child elements (Painter2D can't draw text).
    ///
    /// One instance backs the compact inline well; a second, larger one backs the resizable editor window; a
    /// tiny <see cref="Minimal"/> one backs the field preview. They all edit the SAME underlying property: each
    /// writes through its own <see cref="SerializedProperty"/> and the siblings refresh via TrackPropertyValue.
    /// The AnimationCurve is NEVER stored on the BindingNode — it lives on the bound property.
    ///
    /// Persistence is disposal-safe and undo-grouped (a whole drag collapses to one Ctrl+Z). Every animation is
    /// gated on <see cref="EditorSpringFX.MotionEnabled"/> and degrades to an instant change when motion is off.
    /// </summary>
    internal sealed class SasquatchCurveControl : VisualElement
    {
        private enum DragMode { None, Key, TangentIn, TangentOut, Pan }

        // --- wiring ---
        private readonly SerializedProperty _prop;
        private readonly BindingNode _node;
        private readonly bool _editable;

        // Skin captured at construction — the constructor runs inside the build's WidgetPalette scope;
        // generateVisualContent runs later, when that scope is gone. Used to remap the node's
        // dark-authored serialized defaults (well/grid/line) and to pick the key-dot outline ink.
        private readonly bool _skinIsLight = WidgetPalette.IsLight;
        private readonly Color _inkStroke = WidgetPalette.Ink(1f);

        private Color WellColorResolved => WidgetPalette.ResolveLegacy(
            _skinIsLight, _node.CurveBackgroundColor, WidgetPalette.CurveWellDarkDefault, WidgetPalette.CurveWellLight);
        private Color GridColorResolved => WidgetPalette.ResolveLegacy(
            _skinIsLight, _node.CurveGridColor, WidgetPalette.CurveGridDarkDefault, WidgetPalette.CurveGridLight);
        private Color LineColorResolved => WidgetPalette.ResolveLegacy(
            _skinIsLight, _node.CurveLineColor, WidgetPalette.CurveLineDarkDefault, WidgetPalette.CurveLineLight);
        private Color FillColorResolved => WidgetPalette.ResolveLegacy(
            _skinIsLight, _node.CurveFillColor, WidgetPalette.CurveFillDarkDefault, WidgetPalette.CurveFillLight);

        // --- working state (editor-only; never serialized) ---
        private AnimationCurve _curve = new AnimationCurve();
        private int _selectedKey = -1;
        private int _hoverKey = -1;

        // Animated view frame (springs target these). Time on X, value on Y (inverted: max at top).
        private float _viewTimeMin, _viewTimeMax = 1f, _viewValMin, _viewValMax = 1f;
        // User-pinned axis maxima (from the editable Max fields); null = auto-frame. Cleared by Frame All.
        private float? _pinValMax;
        private float? _pinTimeMax;

        // Preset/reset morph: lerp from _morphFrom to _curve as _morphT springs 0 -> 1 (overshoot allowed).
        private float _morphT = 1f;
        private AnimationCurve _morphFrom;

        // Per-keyframe diamond scale (spring targets), parallel to _curve.keys.
        private float[] _handleScale = Array.Empty<float>();

        // The polyline buffer OnGenerateVisualContent samples the curve into. Grow-only and fully rewritten
        // per paint (see the sampling loop) so it never carries state between frames — it exists only to stop
        // a repaint allocating up to 513 Vector2s, which during a morph animation happens every frame.
        private Vector2[] _curvePoints;

        // Drag state.
        private DragMode _dragMode = DragMode.None;
        private bool _dragActive;
        private Vector2 _dragStartLocal;
        private int _undoGroup = -1;
        private float _panTMin, _panTMax, _panVMin, _panVMax;
        private Vector2 _lastLocalPointer;

        // Axis-number labels + editable Max fields (children; Painter2D can't draw text).
        private VisualElement _axisLayer;
        private readonly List<Label> _axisLabels = new List<Label>();
        private FloatField _maxYField;
        private FloatField _maxXField;
        private bool _refreshingMax;

        /// <summary>Raised after the selection or curve changes, so a host can refresh its tangent pills / fields.</summary>
        public event Action Changed;

        /// <summary>When true (the resizable window), scroll-wheel zoom and middle/empty-drag pan are enabled.</summary>
        public bool AllowZoomPan { get; set; }

        /// <summary>When true, draw only the curve + fill (no grid, diamonds, labels, or max fields) — for the tiny field preview.</summary>
        public bool Minimal { get; set; }

        // Handle-drag mode: false = "Both" (handles move together / smooth), true = "One" (broken, independent).
        // Alt while dragging temporarily inverts it. ON ("Both") by default.
        private bool _break;
        public bool BreakHandles
        {
            get => _break;
            set { if (_break == value) return; _break = value; Changed?.Invoke(); }
        }

        // Visual constants (points).
        private const float KeyHitRadius = 12f;
        private const float TangentHitRadius = 16f;
        private const float DiamondRadius = 5f;
        private const float TangentNubDist = 30f;
        private const float TangentNubRadius = 3.75f;
        private const float SelectedScale = 1.3f;
        private const float HoverScale = 1.18f;
        private const float MinValueSpan = 0.5f; // floor so a flat curve never collapses the view to ~0
        // Value-grid density floors (points): skip DRAWING gridlines closer than MinGridPx, and skip axis
        // LABELS closer than MinLabelPx — snapping always uses the true CurveGridSize regardless of either.
        private const float MinGridPx = 6f;
        private const float MinLabelPx = 26f;

        public SasquatchCurveControl(SerializedProperty prop, BindingNode node, bool editable)
        {
            _prop = prop;
            _node = node;
            _editable = editable;

            AddToClassList("sasq-curve-surface");
            style.flexGrow = 1;
            style.height = Length.Percent(100);
            focusable = editable;
            pickingMode = PickingMode.Position;

            generateVisualContent += OnGenerateVisualContent;

            ReloadFromProperty();
            ResetViewToFrame(animate: false);

            if (editable)
            {
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCaptureOutEvent>(_ => { _dragMode = DragMode.None; _dragActive = false; });
                RegisterCallback<KeyDownEvent>(OnKeyDown);
                RegisterCallback<WheelEvent>(OnWheel);
                this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
            }

            // External changes (undo, multi-edit, the sibling control editing the same curve) → silent reload.
            this.TrackPropertyValueWithRefresh(prop, _ =>
            {
                if (EditingCurve) return;
                ReloadFromProperty();
                if (Minimal) ResetViewToFrame(animate: false); // the tiny preview always fits the whole curve
                ViewDirty();
            });

            RegisterCallback<DetachFromPanelEvent>(_ => Spring.CancelAll(this));
            RegisterCallback<GeometryChangedEvent>(_ => { BuildOverlaysIfNeeded(); ViewDirty(); });
        }

        private bool EditingCurve => _dragActive && (_dragMode is DragMode.Key or DragMode.TangentIn or DragMode.TangentOut);

        // ===========================================================================================
        // Coordinate mapping
        // ===========================================================================================
        private float W => Mathf.Max(1f, contentRect.width);
        private float H => Mathf.Max(1f, contentRect.height);
        private float TimeToX(float t) => (t - _viewTimeMin) / Mathf.Max(1e-5f, _viewTimeMax - _viewTimeMin) * W;
        private float ValToY(float v) => (1f - (v - _viewValMin) / Mathf.Max(1e-5f, _viewValMax - _viewValMin)) * H;
        private float XToTime(float x) => _viewTimeMin + (x / W) * (_viewTimeMax - _viewTimeMin);
        private float YToVal(float y) => _viewValMin + (1f - y / H) * (_viewValMax - _viewValMin);

        // ===========================================================================================
        // Property access (disposal-safe) + persistence
        // ===========================================================================================
        private UnityEngine.Object SafeTarget()
        {
            try { return _prop?.serializedObject?.targetObject; }
            catch { return null; }
        }

        private AnimationCurve CurrentCurve()
        {
            try
            {
                if (_prop == null || _prop.serializedObject == null || _prop.serializedObject.targetObject == null)
                    return null;
                return _prop.animationCurveValue;
            }
            catch { return null; }
        }

        private void RecordUndo(string label)
        {
            var t = SafeTarget();
            if (t != null) Undo.RecordObject(t, label);
        }

        private void ApplyCurve(AnimationCurve c)
        {
            try
            {
                if (_prop == null || _prop.serializedObject == null || _prop.serializedObject.targetObject == null)
                    return;
                _prop.animationCurveValue = c;
                _prop.serializedObject.ApplyModifiedProperties();
            }
            catch { /* SerializedObject disposed mid-teardown */ }
        }

        private void WriteCurveDiscrete(AnimationCurve c, string label)
        {
            RecordUndo(label);
            ApplyCurve(c);
            _curve = c;
            SyncHandleScaleLength();
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        private void ReloadFromProperty()
        {
            var c = CurrentCurve();
            _curve = c ?? new AnimationCurve();
            SyncHandleScaleLength();
            if (_selectedKey >= _curve.length) _selectedKey = -1;
            Changed?.Invoke();
        }

        private void SyncHandleScaleLength()
        {
            int n = _curve?.length ?? 0;
            if (_handleScale.Length == n) return;
            var ns = new float[n];
            for (int i = 0; i < n; i++) ns[i] = i < _handleScale.Length ? _handleScale[i] : 1f;
            _handleScale = ns;
        }

        private static AnimationCurve CloneCurve(AnimationCurve c)
        {
            var nc = new AnimationCurve(c != null ? c.keys : Array.Empty<Keyframe>());
            if (c != null) { nc.preWrapMode = c.preWrapMode; nc.postWrapMode = c.postWrapMode; }
            return nc;
        }

        // ===========================================================================================
        // Public API (host UI)
        // ===========================================================================================
        public bool HasSelection => _selectedKey >= 0 && _curve != null && _selectedKey < _curve.length;
        public float SelectedTime => HasSelection ? _curve[_selectedKey].time : 0f;
        public float SelectedValue => HasSelection ? _curve[_selectedKey].value : 0f;

        public AnimationCurve SnapshotCurve() => CloneCurve(_curve);

        /// <summary>Spring-morphs the displayed curve to <paramref name="newCurve"/>, persists it, and reframes.</summary>
        public void MorphTo(AnimationCurve newCurve, string undoLabel)
        {
            if (newCurve == null) return;
            var from = CloneCurve(_curve);
            RecordUndo(undoLabel);
            ApplyCurve(newCurve);
            _curve = newCurve;
            _selectedKey = -1;
            SyncHandleScaleLength();

            if (EditorSpringFX.MotionEnabled)
            {
                _morphFrom = from;
                _morphT = 0f;
                // Bouncy so the morph itself visibly overshoots-and-settles every time (the spring "pop" the
                // shape gets, independent of whether the view reframes). The lerp below is UNclamped so the
                // overshoot is actually visible.
                Spring.To((object)this, () => _morphT, v => { _morphT = v; MarkDirtyRepaint(); },
                    1f, EditorSpringFX.Tune(SpringConfig.Bouncy), "morph");
            }
            else
            {
                _morphT = 1f;
            }

            Changed?.Invoke();
            ResetViewToFrame(animate: true);
            MarkDirtyRepaint();
        }

        public void FrameAll()
        {
            _pinValMax = null;
            _pinTimeMax = null;
            ResetViewToFrame(animate: true);
        }

        public CurveTangentMode SelectedTangentMode()
        {
            if (!HasSelection) return CurveTangentMode.Auto;
            int i = _selectedKey;
            if (AnimationUtility.GetKeyBroken(_curve, i)) return CurveTangentMode.Free;
            var lt = AnimationUtility.GetKeyLeftTangentMode(_curve, i);
            switch (lt)
            {
                case AnimationUtility.TangentMode.Linear: return CurveTangentMode.Linear;
                case AnimationUtility.TangentMode.Constant: return CurveTangentMode.Constant;
                case AnimationUtility.TangentMode.Free:
                    var k = _curve[i];
                    bool flat = Mathf.Abs(k.inTangent) < 1e-4f && Mathf.Abs(k.outTangent) < 1e-4f;
                    return flat ? CurveTangentMode.Flat : CurveTangentMode.Free;
                default: return CurveTangentMode.Auto;
            }
        }

        public void SetSelectedTangentMode(CurveTangentMode m)
        {
            if (!HasSelection) return;
            var nc = CloneCurve(_curve);
            int i = _selectedKey;
            switch (m)
            {
                case CurveTangentMode.Auto:
                    AnimationUtility.SetKeyBroken(nc, i, false);
                    AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto);
                    break;
                case CurveTangentMode.Linear:
                    AnimationUtility.SetKeyBroken(nc, i, false);
                    AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.Linear);
                    break;
                case CurveTangentMode.Constant:
                    AnimationUtility.SetKeyBroken(nc, i, false);
                    AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.Constant);
                    break;
                case CurveTangentMode.Flat:
                    AnimationUtility.SetKeyBroken(nc, i, false);
                    AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.Free);
                    AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.Free);
                    var fk = nc[i];
                    fk.inTangent = 0f;
                    fk.outTangent = 0f;
                    nc.MoveKey(i, fk);
                    break;
                case CurveTangentMode.Free:
                    AnimationUtility.SetKeyBroken(nc, i, true);
                    break;
            }
            WriteCurveDiscrete(nc, "Set Tangent Mode");
        }

        public void SetSelectedKeyTime(float t)
        {
            if (!HasSelection) return;
            var nc = CloneCurve(_curve);
            var k = nc[_selectedKey];
            k.time = t;
            _selectedKey = nc.MoveKey(_selectedKey, k);
            WriteCurveDiscrete(nc, "Set Keyframe Time");
        }

        public void SetSelectedKeyValue(float v)
        {
            if (!HasSelection) return;
            var nc = CloneCurve(_curve);
            var k = nc[_selectedKey];
            k.value = v;
            _selectedKey = nc.MoveKey(_selectedKey, k);
            WriteCurveDiscrete(nc, "Set Keyframe Value");
        }

        // ===========================================================================================
        // Framing / view springs
        // ===========================================================================================
        private void ComputeFrameBounds(out float tMin, out float tMax, out float vMin, out float vMax)
        {
            if (_curve == null || _curve.length == 0)
            {
                tMin = 0f; tMax = 1f; vMin = 0f; vMax = 1f;
            }
            else
            {
                tMin = _curve[0].time;
                tMax = _curve[_curve.length - 1].time;
                if (tMax - tMin < 1e-4f) { tMin -= 0.5f; tMax += 0.5f; }

                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var k in _curve.keys) { lo = Mathf.Min(lo, k.value); hi = Mathf.Max(hi, k.value); }
                const int n = 48;
                for (int i = 0; i <= n; i++)
                {
                    float v = SafeEval(_curve, Mathf.Lerp(tMin, tMax, i / (float)n));
                    lo = Mathf.Min(lo, v);
                    hi = Mathf.Max(hi, v);
                }
                if (lo > hi) { lo = 0f; hi = 1f; }

                float padV = Mathf.Max(1e-3f, hi - lo) * 0.10f;
                vMin = lo - padV;
                vMax = hi + padV;

                float padT = Mathf.Max(1e-3f, tMax - tMin) * 0.04f;
                tMin -= padT;
                tMax += padT;
            }

            // Persistent designer range overrides.
            if (_node.CurveRangeMin.isOverridden) vMin = _node.CurveRangeMin.value;
            if (_node.CurveRangeMax.isOverridden) vMax = _node.CurveRangeMax.value;
            // Live user pins (editable Max fields) win over auto-frame.
            if (_pinValMax.HasValue) vMax = _pinValMax.Value;
            if (_pinTimeMax.HasValue) tMax = _pinTimeMax.Value;

            // Floors so a flat curve / degenerate range never collapses to ~0 (which makes ValToY explode).
            if (vMax - vMin < MinValueSpan)
            {
                float c = (vMin + vMax) * 0.5f;
                vMin = c - MinValueSpan * 0.5f;
                vMax = c + MinValueSpan * 0.5f;
            }
            if (tMax - tMin < 1e-3f) tMax = tMin + 1f;
        }

        private void ResetViewToFrame(bool animate)
        {
            ComputeFrameBounds(out float tMin, out float tMax, out float vMin, out float vMax);
            CancelViewSprings();
            if (!animate || !EditorSpringFX.MotionEnabled)
            {
                _viewTimeMin = tMin; _viewTimeMax = tMax; _viewValMin = vMin; _viewValMax = vMax;
                ViewDirty();
                return;
            }
            var cfg = EditorSpringFX.Tune(SpringConfig.Bouncy);
            Spring.To((object)this, () => _viewTimeMin, v => { _viewTimeMin = v; ViewDirty(); }, tMin, cfg, "view.tmin");
            Spring.To((object)this, () => _viewTimeMax, v => { _viewTimeMax = v; ViewDirty(); }, tMax, cfg, "view.tmax");
            Spring.To((object)this, () => _viewValMin, v => { _viewValMin = v; ViewDirty(); }, vMin, cfg, "view.vmin");
            Spring.To((object)this, () => _viewValMax, v => { _viewValMax = v; ViewDirty(); }, vMax, cfg, "view.vmax");
        }

        private void CancelViewSprings()
        {
            Spring.Cancel(this, "view.tmin");
            Spring.Cancel(this, "view.tmax");
            Spring.Cancel(this, "view.vmin");
            Spring.Cancel(this, "view.vmax");
        }

        private void ViewDirty()
        {
            MarkDirtyRepaint();
            RefreshAxisLabels();
            RefreshMaxFields();
        }

        // ===========================================================================================
        // Selection / hover springs
        // ===========================================================================================
        private void SelectKey(int i)
        {
            if (i == _selectedKey) return;
            int prev = _selectedKey;
            _selectedKey = i;
            if (prev >= 0 && prev != _hoverKey) SpringHandle(prev, 1f);
            if (i >= 0) SpringHandle(i, SelectedScale);
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        private void SetHover(int i)
        {
            if (i == _hoverKey) return;
            int prev = _hoverKey;
            _hoverKey = i;
            if (prev >= 0 && prev != _selectedKey) SpringHandle(prev, 1f);
            if (i >= 0 && i != _selectedKey) SpringHandle(i, HoverScale);
            MarkDirtyRepaint();
        }

        private void SpringHandle(int i, float target)
        {
            if (i < 0 || i >= _handleScale.Length) return;
            int idx = i;
            if (!EditorSpringFX.MotionEnabled)
            {
                _handleScale[idx] = target;
                MarkDirtyRepaint();
                return;
            }
            Spring.To((object)this,
                () => idx < _handleScale.Length ? _handleScale[idx] : target,
                v => { if (idx < _handleScale.Length) { _handleScale[idx] = v; MarkDirtyRepaint(); } },
                target, EditorSpringFX.Tune(SpringConfig.Bouncy), "h" + idx);
        }

        // ===========================================================================================
        // Pointer / key / wheel interaction (editable only)
        // ===========================================================================================
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.target != this) return; // ignore clicks that land on overlay fields/labels
            Focus();
            Vector2 p = evt.localPosition;
            _lastLocalPointer = p;

            if (evt.button == 2 && AllowZoomPan) { ArmDrag(DragMode.Pan, p, evt); return; }
            if (evt.button != 0) return;

            if (evt.clickCount >= 2 && HitTestKey(p) < 0) { AddKeyAt(p); evt.StopPropagation(); return; }

            // Tangent handles take priority for the selected key — in ANY tangent mode (dragging breaks it to
            // Free), so you never have to set the mode first. Generous hit radius makes them easy to grab.
            if (HasSelection)
            {
                int side = HitTestTangent(p);
                if (side < 0) { ArmDrag(DragMode.TangentIn, p, evt); return; }
                if (side > 0) { ArmDrag(DragMode.TangentOut, p, evt); return; }
            }

            int key = HitTestKey(p);
            if (key >= 0) { SelectKey(key); ArmDrag(DragMode.Key, p, evt); }
            else if (AllowZoomPan) { ArmDrag(DragMode.Pan, p, evt); }
            else { SelectKey(-1); }
        }

        private void ArmDrag(DragMode mode, Vector2 p, PointerDownEvent evt)
        {
            _dragMode = mode;
            _dragActive = false;
            _dragStartLocal = p;
            if (mode == DragMode.Pan)
            {
                _panTMin = _viewTimeMin; _panTMax = _viewTimeMax; _panVMin = _viewValMin; _panVMax = _viewValMax;
                CancelViewSprings();
            }
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            _lastLocalPointer = evt.localPosition;
            if (_dragMode == DragMode.None)
            {
                SetHover(HitTestKey(evt.localPosition));
                return;
            }
            if (!this.HasPointerCapture(evt.pointerId)) return;

            Vector2 p = evt.localPosition;
            if (!_dragActive)
            {
                if ((p - _dragStartLocal).magnitude < DesignerPalette.DragThreshold) return;
                _dragActive = true;
                if (_dragMode is DragMode.Key or DragMode.TangentIn or DragMode.TangentOut)
                {
                    Undo.IncrementCurrentGroup();
                    _undoGroup = Undo.GetCurrentGroup();
                }
            }

            switch (_dragMode)
            {
                case DragMode.Key: DragKeyTo(p); break;
                case DragMode.TangentIn: DragTangent(p, -1, _break ^ evt.altKey); break;
                case DragMode.TangentOut: DragTangent(p, +1, _break ^ evt.altKey); break;
                case DragMode.Pan: DoPan(p); break;
            }
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragMode == DragMode.None) return;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);

            bool wasActive = _dragActive;
            var mode = _dragMode;
            _dragMode = DragMode.None;
            _dragActive = false;

            if (wasActive && _undoGroup >= 0 &&
                (mode is DragMode.Key or DragMode.TangentIn or DragMode.TangentOut))
            {
                Undo.CollapseUndoOperations(_undoGroup);
                _undoGroup = -1;
            }
            if (!wasActive && mode == DragMode.Pan) SelectKey(-1);
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) { DeleteSelected(); evt.StopPropagation(); }
            else if (evt.keyCode == KeyCode.F) { FrameAll(); evt.StopPropagation(); }
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 p = _lastLocalPointer;
            int hit = HitTestKey(p);
            if (hit >= 0)
            {
                SelectKey(hit);
                evt.menu.AppendAction("Delete Key", _ => DeleteSelected(),
                    _curve.length > 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
                var cur = SelectedTangentMode();
                foreach (CurveTangentMode m in Enum.GetValues(typeof(CurveTangentMode)))
                {
                    var mm = m;
                    evt.menu.AppendAction("Tangents/" + m, _ => SetSelectedTangentMode(mm),
                        cur == m ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                }
            }
            else
            {
                evt.menu.AppendAction("Add Key Here", _ => AddKeyAt(p));
            }
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Frame All", _ => FrameAll());
        }

        private void OnWheel(WheelEvent evt)
        {
            if (!AllowZoomPan) return;
            CancelViewSprings();
            _pinValMax = null;
            _pinTimeMax = null;
            float zoom = Mathf.Pow(1.1f, evt.delta.y * 0.5f);
            Vector2 p = evt.localMousePosition;
            float ct = XToTime(p.x), cv = YToVal(p.y);
            _viewTimeMin = ct - (ct - _viewTimeMin) * zoom;
            _viewTimeMax = ct + (_viewTimeMax - ct) * zoom;
            _viewValMin = cv - (cv - _viewValMin) * zoom;
            _viewValMax = cv + (_viewValMax - cv) * zoom;
            ViewDirty();
            evt.StopPropagation();
        }

        private void DoPan(Vector2 p)
        {
            float dt = (p.x - _dragStartLocal.x) / W * (_panTMax - _panTMin);
            float dv = (p.y - _dragStartLocal.y) / H * (_panVMax - _panVMin);
            _viewTimeMin = _panTMin - dt;
            _viewTimeMax = _panTMax - dt;
            _viewValMin = _panVMin + dv;
            _viewValMax = _panVMax + dv;
            ViewDirty();
        }

        private void DragKeyTo(Vector2 p)
        {
            if (!HasSelection) return;
            float t = XToTime(p.x);
            float v = YToVal(p.y);
            if (_node.CurveSnapToGrid) { t = SnapTime(t); v = SnapVal(v); }

            var nc = CloneCurve(_curve);
            var k = nc[_selectedKey];
            k.time = t;
            k.value = v;
            _selectedKey = nc.MoveKey(_selectedKey, k);
            RecordUndo("Move Keyframe");
            ApplyCurve(nc);
            _curve = nc;
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        // Grabbing a handle ALWAYS works (Blender/After-Effects model): it converts the key to manual tangents so
        // the dragged value sticks — otherwise an Auto/Linear/Constant key just recomputes and discards it. By
        // default both handles stay collinear (smooth, like Unity "Free Smooth" / Blender "Aligned"); hold Alt to
        // break them so each side moves independently (Unity "Broken" / Blender "Free").
        private void DragTangent(Vector2 p, int side, bool broken)
        {
            if (!HasSelection) return;
            int i = _selectedKey;
            var k = _curve[i];
            Vector2 keyPos = new Vector2(TimeToX(k.time), ValToY(k.value));
            Vector2 d = p - keyPos;
            float dx = d.x;
            if (side > 0 && dx < 1f) dx = 1f;
            if (side < 0 && dx > -1f) dx = -1f;

            float trange = _viewTimeMax - _viewTimeMin;
            float vrange = _viewValMax - _viewValMin;
            float m = -(d.y / dx) * (vrange * W) / (H * trange); // dValue/dTime

            var nc = CloneCurve(_curve);
            // CRITICAL: set the affected tangent MODE(s) to Free first, or Unity recomputes the tangent and the
            // drag appears to do nothing — the handles look immovable.
            AnimationUtility.SetKeyBroken(nc, i, broken);
            if (broken)
            {
                if (side > 0) AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.Free);
                else AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.Free);
            }
            else
            {
                AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.Free);
            }

            var nk = nc[i];
            if (broken)
            {
                if (side > 0) nk.outTangent = m; else nk.inTangent = m;
            }
            else
            {
                nk.inTangent = m;
                nk.outTangent = m; // collinear: the handle reads as one straight line through the key
            }
            nc.MoveKey(i, nk);
            RecordUndo("Edit Tangent");
            ApplyCurve(nc);
            _curve = nc;
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        // ===========================================================================================
        // Wrap modes (full editor only)
        // ===========================================================================================
        public WrapMode PreWrap => _curve != null ? _curve.preWrapMode : WrapMode.ClampForever;
        public WrapMode PostWrap => _curve != null ? _curve.postWrapMode : WrapMode.ClampForever;

        public void SetWrapModes(WrapMode pre, WrapMode post)
        {
            var nc = CloneCurve(_curve);
            nc.preWrapMode = pre;
            nc.postWrapMode = post;
            WriteCurveDiscrete(nc, "Set Curve Wrap Mode");
        }

        private void AddKeyAt(Vector2 p)
        {
            float t = XToTime(p.x);
            float v = YToVal(p.y);
            if (_node.CurveSnapToGrid) { t = SnapTime(t); v = SnapVal(v); }

            var nc = CloneCurve(_curve);
            int idx = nc.AddKey(t, v);
            if (idx < 0) idx = nc.AddKey(t + 1e-4f, v);
            if (idx < 0) return;

            AnimationUtility.SetKeyLeftTangentMode(nc, idx, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(nc, idx, AnimationUtility.TangentMode.ClampedAuto);

            WriteCurveDiscrete(nc, "Add Keyframe");
            _selectedKey = idx;
            if (idx < _handleScale.Length) _handleScale[idx] = 1.5f;
            SpringHandle(idx, SelectedScale);
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        private void DeleteSelected()
        {
            if (!HasSelection || _curve.length <= 1) return;
            var nc = CloneCurve(_curve);
            nc.RemoveKey(_selectedKey);
            _selectedKey = -1;
            _hoverKey = -1;
            WriteCurveDiscrete(nc, "Delete Keyframe");
        }

        // ===========================================================================================
        // Hit testing
        // ===========================================================================================
        private int HitTestKey(Vector2 p)
        {
            int best = -1;
            float bestD = KeyHitRadius;
            for (int i = 0; i < _curve.length; i++)
            {
                var k = _curve[i];
                float d = Vector2.Distance(p, new Vector2(TimeToX(k.time), ValToY(k.value)));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private int HitTestTangent(Vector2 p)
        {
            if (!HasSelection) return 0;
            var k = _curve[_selectedKey];
            // Infinite (Constant/stepped) tangents aren't draggable, so they aren't grab targets.
            if (_selectedKey > 0 && !float.IsInfinity(k.inTangent) && Vector2.Distance(p, TangentNubPos(k, -1)) < TangentHitRadius) return -1;
            if (_selectedKey < _curve.length - 1 && !float.IsInfinity(k.outTangent) && Vector2.Distance(p, TangentNubPos(k, +1)) < TangentHitRadius) return +1;
            return 0;
        }

        private Vector2 TangentNubPos(Keyframe k, int side)
        {
            Vector2 keyPos = new Vector2(TimeToX(k.time), ValToY(k.value));
            float m = side > 0 ? k.outTangent : k.inTangent;
            if (float.IsInfinity(m)) m = side * 1000f;
            float trange = _viewTimeMax - _viewTimeMin;
            float vrange = _viewValMax - _viewValMin;
            float screenSlope = -m * (H * trange) / Mathf.Max(1e-5f, vrange * W);
            Vector2 dir = new Vector2(side, side * screenSlope);
            if (dir.sqrMagnitude < 1e-6f) dir = new Vector2(side, 0f);
            dir.Normalize();
            return keyPos + dir * TangentNubDist;
        }

        // ===========================================================================================
        // Snapping helpers — snap to the SAME grid that's drawn, so a snapped key lands exactly on a
        // visible gridline. The VALUE axis steps at CurveGridSize (the user's Grid Size). The TIME
        // axis derives its step from the value step's PIXEL height, quantized to the nearest "nice"
        // number (1/2/2.5/5 ladder): cells come out square (within a few percent) in any well aspect,
        // while snapped times and axis labels stay clean values. The step re-tiers discretely as the
        // well resizes (like any graphing tool); snap always matches the drawn lines because painter,
        // labels and snapping all read these same two steps.
        // ===========================================================================================
        private float GridStep => Mathf.Max(1e-4f, _node.CurveGridSize);
        private float GridStepTime()
        {
            float w = W, h = H;
            if (w < 1f || h < 1f) return GridStep;
            float pxPerTime = w / Mathf.Max(1e-5f, _viewTimeMax - _viewTimeMin);
            float pxPerVal = h / Mathf.Max(1e-5f, _viewValMax - _viewValMin);
            float squareTarget = GridStep * pxPerVal / Mathf.Max(1e-5f, pxPerTime); // time units per square cell
            return NiceStepNear(squareTarget);
        }
        private float SnapTime(float t) => SnapTo(t, GridStepTime());
        private float SnapVal(float v) => SnapTo(v, GridStep);
        private static float SnapTo(float v, float step) => step > 0f ? Mathf.Round(v / step) * step : v;

        // Nearest 1/2/2.5/5·10^n to the target (log distance) — clean snap/label values at any aspect.
        private static float NiceStepNear(float target)
        {
            if (!(target > 1e-6f)) return 0.25f;
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(target)));
            float[] ladder = { 1f, 2f, 2.5f, 5f, 10f };
            float best = p, bestErr = float.MaxValue;
            for (int i = 0; i < ladder.Length; i++)
            {
                float cand = p * ladder[i];
                float err = Mathf.Abs(Mathf.Log(cand / target));
                if (err < bestErr) { bestErr = err; best = cand; }
            }
            return best;
        }

        // Fewest decimals that print the step's multiples exactly (0.25 needs 2 — "F1" would round
        // the 0.25/0.75 lines to a lying "0.3"/"0.8").
        private static int GridDecimals(float step)
        {
            for (int d = 0; d <= 4; d++)
            {
                float scaled = step * Mathf.Pow(10f, d);
                if (Mathf.Abs(scaled - Mathf.Round(scaled)) < 1e-4f) return d;
            }
            return 4;
        }

        // Prints a gridline value at the step's precision, then drops noise zeros ("0.50" -> "0.5").
        private static string FormatGridValue(float v, int decimals)
        {
            string s = v.ToString("F" + decimals);
            if (decimals > 0) s = s.TrimEnd('0').TrimEnd('.');
            return s == "-0" ? "0" : s;
        }

        // Label every Nth gridline instead of all-or-nothing: the smallest 1/2/5-ladder multiple whose
        // spacing clears MinLabelPx, so a dense (square) grid thins its numbers out instead of losing
        // them all. Returns 0 (no labels) only when the lines themselves are below the draw floor.
        private static int LabelEvery(float linePx)
        {
            if (!(linePx >= MinGridPx)) return 0;
            for (int i = 0; i < LabelLadder.Length; i++)
                if (LabelLadder[i] * linePx >= MinLabelPx) return LabelLadder[i];
            return 0;
        }

        // Hoisted out of LabelEvery: an array initialiser inside a method body allocates a fresh array on
        // every call, and this one is called per gridline axis on every repaint of every curve on screen.
        private static readonly int[] LabelLadder = { 1, 2, 5, 10, 20, 50, 100 };

        // True modulo for possibly-negative line indices (labels anchor at 0 on both sides of it).
        private static bool IsNthLine(int k, int every) => ((k % every) + every) % every == 0;

        // ===========================================================================================
        // Axis-number labels + editable Max fields (child overlays)
        // ===========================================================================================
        private void BuildOverlaysIfNeeded()
        {
            if (Minimal) return;
            if (_axisLayer == null)
            {
                _axisLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                _axisLayer.style.position = Position.Absolute;
                _axisLayer.style.left = 0; _axisLayer.style.top = 0; _axisLayer.style.right = 0; _axisLayer.style.bottom = 0;
                Add(_axisLayer);
            }
            if (_maxYField == null && _node.CurveEditableMaxY)
            {
                _maxYField = MakeMaxField("Max Y of the value axis (Frame All clears it)");
                _maxYField.style.left = 2;
                _maxYField.style.top = 2;
                _maxYField.RegisterValueChangedCallback(e =>
                {
                    if (_refreshingMax) return;
                    _pinValMax = e.newValue;
                    ResetViewToFrame(animate: true);
                });
                Add(_maxYField);
            }
            if (_maxXField == null && _node.CurveEditableMaxX)
            {
                _maxXField = MakeMaxField("Max X of the time axis (Frame All clears it)");
                _maxXField.style.right = 2;
                _maxXField.style.bottom = 2;
                _maxXField.RegisterValueChangedCallback(e =>
                {
                    if (_refreshingMax) return;
                    _pinTimeMax = e.newValue;
                    ResetViewToFrame(animate: true);
                });
                Add(_maxXField);
            }
        }

        private static FloatField MakeMaxField(string tooltip)
        {
            var f = new FloatField { isDelayed = true, tooltip = tooltip };
            f.AddToClassList("sasq-curve-maxfield");
            f.style.position = Position.Absolute;
            f.style.width = 48;
            return f;
        }

        private Label GetAxisLabel(int i)
        {
            while (_axisLabels.Count <= i)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList("sasq-curve-axis-label");
                l.style.position = Position.Absolute;
                _axisLayer.Add(l);
                _axisLabels.Add(l);
            }
            return _axisLabels[i];
        }

        private void RefreshAxisLabels()
        {
            if (Minimal || _axisLayer == null) return;
            float w = W, h = H;
            bool show = _node.CurveShowGrid && w >= 8f && h >= 8f;
            if (!show)
            {
                foreach (var l in _axisLabels) l.style.display = DisplayStyle.None;
                return;
            }

            float step = GridStep;
            float stepX = GridStepTime();
            int idx = 0;

            // Time labels under every Nth vertical gridline (LabelEvery keeps ≥MinLabelPx between
            // numbers, so a dense square grid thins its labels instead of losing them all). Right
            // margin clears the optional Max-X field's corner.
            float pxPerTime = w / Mathf.Max(1e-5f, _viewTimeMax - _viewTimeMin);
            int everyX = LabelEvery(stepX * pxPerTime);
            if (everyX > 0)
            {
                int decX = GridDecimals(stepX * everyX);
                for (int k = Mathf.CeilToInt(_viewTimeMin / stepX); ; k++)
                {
                    float t = k * stepX;
                    if (t > _viewTimeMax) break;
                    if (!IsNthLine(k, everyX)) continue;
                    float x = TimeToX(t);
                    if (x < 2f || x > w - 22f) continue;
                    var lbl = GetAxisLabel(idx++);
                    lbl.text = FormatGridValue(t, decX);
                    lbl.style.left = x + 2f;
                    lbl.style.top = h - 13f;
                    lbl.style.display = DisplayStyle.Flex;
                }
            }
            // Value labels beside every Nth horizontal gridline; top margin clears the optional Max-Y field's corner.
            float pxPerVal = h / Mathf.Max(1e-5f, _viewValMax - _viewValMin);
            int everyY = LabelEvery(step * pxPerVal);
            if (everyY > 0)
            {
                int decY = GridDecimals(step * everyY);
                for (int k = Mathf.CeilToInt(_viewValMin / step); ; k++)
                {
                    float v = k * step;
                    if (v > _viewValMax) break;
                    if (!IsNthLine(k, everyY)) continue;
                    float y = ValToY(v);
                    if (y < 18f || y > h - 4f) continue;
                    var lbl = GetAxisLabel(idx++);
                    lbl.text = FormatGridValue(v, decY);
                    lbl.style.left = 2f;
                    lbl.style.top = y - 7f;
                    lbl.style.display = DisplayStyle.Flex;
                }
            }
            for (; idx < _axisLabels.Count; idx++) _axisLabels[idx].style.display = DisplayStyle.None;
        }

        private void RefreshMaxFields()
        {
            _refreshingMax = true;
            if (_maxYField != null) _maxYField.SetValueWithoutNotify(_pinValMax ?? _viewValMax);
            if (_maxXField != null) _maxXField.SetValueWithoutNotify(_pinTimeMax ?? _viewTimeMax);
            _refreshingMax = false;
        }

        // ===========================================================================================
        // Drawing
        // ===========================================================================================
        private static float SafeEval(AnimationCurve c, float t)
        {
            if (c == null || c.length == 0) return 0f;
            float v = c.Evaluate(t);
            return (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v;
        }

        private float SampleValue(float t)
        {
            float v = SafeEval(_curve, t);
            if (_morphT < 1f && _morphFrom != null)
                v = Mathf.LerpUnclamped(SafeEval(_morphFrom, t), v, _morphT); // unclamped → the spring overshoot shows
            return v;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 2f || rect.height < 2f) return;

            var painter = mgc.painter2D;
            float ppp = EditorGUIUtility.pixelsPerPoint;
            float w = rect.width, h = rect.height;

            FillRect(painter, 0f, 0f, w, h, WellColorResolved);

            if (!Minimal && _node.CurveShowGrid) DrawGrid(painter, w, h, ppp);

            if (_curve == null || _curve.length == 0)
            {
                var faint = LineColorResolved;
                faint.a *= 0.25f;
                painter.strokeColor = faint;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, h * 0.5f));
                painter.LineTo(new Vector2(w, h * 0.5f));
                painter.Stroke();
                return;
            }

            int samples = Mathf.Clamp(Mathf.RoundToInt(w), 24, 512);
            // Grown, never reallocated per paint. This runs inside OnGenerateVisualContent — once per
            // content-dirty repaint, and TWICE per repaint while a morph animation is in flight — and the
            // buffer is fully overwritten below before anything reads it, so reuse cannot leak a stale point.
            // Capacity only ever grows (the clamp caps it at 513 entries), so a wide curve that later narrows
            // keeps its buffer rather than thrashing.
            if (_curvePoints == null || _curvePoints.Length < samples + 1)
                _curvePoints = new Vector2[samples + 1];
            var pts = _curvePoints;
            for (int i = 0; i <= samples; i++)
            {
                float x = w * i / samples;
                pts[i] = new Vector2(x, Mathf.Clamp(ValToY(SampleValue(XToTime(x))), -h, 2f * h));
            }

            if (_node.CurveFillColor.a > 0.001f)
            {
                painter.fillColor = FillColorResolved;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, h));
                for (int i = 0; i <= samples; i++) painter.LineTo(pts[i]);
                painter.LineTo(new Vector2(w, h));
                painter.ClosePath();
                painter.Fill();
            }

            painter.strokeColor = LineColorResolved;
            painter.lineWidth = Minimal ? 1.5f : 2f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(pts[0]);
            for (int i = 1; i <= samples; i++) painter.LineTo(pts[i]);
            painter.Stroke();

            if (!Minimal) DrawKeyframes(painter);
        }

        // A SQUARE value/time grid, anchored at 0, that Snap To Grid lands on. The value axis steps at
        // CurveGridSize; the time axis steps at GridStepTime() — the nice-number step whose pixel width
        // matches the value step's pixel height, so cells stay (near-)square in the landscape well at
        // any size. Guarded against absurd density (extreme zoom / huge range): lines closer than
        // MinGridPx are skipped; snapping still uses the true steps.
        private void DrawGrid(Painter2D painter, float w, float h, float ppp)
        {
            float step = GridStep;
            float stepX = GridStepTime();
            painter.strokeColor = GridColorResolved;
            painter.lineWidth = 1f / ppp;
            painter.BeginPath();

            float pxPerTime = w / Mathf.Max(1e-5f, _viewTimeMax - _viewTimeMin);
            if (stepX * pxPerTime >= MinGridPx)
            {
                for (int k = Mathf.CeilToInt(_viewTimeMin / stepX); ; k++)
                {
                    float t = k * stepX;
                    if (t > _viewTimeMax) break;
                    float sx = SnapPx(TimeToX(t), ppp);
                    if (sx < 0.5f || sx > w - 0.5f) continue;
                    painter.MoveTo(new Vector2(sx, 0f));
                    painter.LineTo(new Vector2(sx, h));
                }
            }

            float pxPerVal = h / Mathf.Max(1e-5f, _viewValMax - _viewValMin);
            if (step * pxPerVal >= MinGridPx)
            {
                for (int k = Mathf.CeilToInt(_viewValMin / step); ; k++)
                {
                    float v = k * step;
                    if (v > _viewValMax) break;
                    float sy = SnapPx(ValToY(v), ppp);
                    if (sy < 0.5f || sy > h - 0.5f) continue;
                    painter.MoveTo(new Vector2(0f, sy));
                    painter.LineTo(new Vector2(w, sy));
                }
            }
            painter.Stroke();
        }

        private void DrawKeyframes(Painter2D painter)
        {
            float dotAlpha = Mathf.Clamp01(_morphT);

            for (int i = 0; i < _curve.length; i++)
            {
                var k = _curve[i];
                Vector2 pos = new Vector2(TimeToX(k.time), ValToY(k.value));
                float scale = i < _handleScale.Length ? _handleScale[i] : 1f;
                float r = DiamondRadius * Mathf.Max(0.01f, scale);
                bool selected = i == _selectedKey;

                if (selected) DrawTangentHandles(painter, k); // the selected key always shows draggable handles

                Color fillC = selected ? DesignerPalette.Accent : LineColorResolved;
                fillC.a *= dotAlpha;
                DrawDiamond(painter, pos, r, fillC, true);

                painter.strokeColor = new Color(_inkStroke.r, _inkStroke.g, _inkStroke.b, (selected ? 0.95f : 0.8f) * dotAlpha);
                painter.lineWidth = selected ? 1.5f : 1f;
                DrawDiamond(painter, pos, r, default, false);
            }
        }

        private static void DrawDiamond(Painter2D painter, Vector2 c, float r, Color fill, bool doFill)
        {
            if (doFill) painter.fillColor = fill;
            painter.BeginPath();
            painter.MoveTo(new Vector2(c.x, c.y - r));
            painter.LineTo(new Vector2(c.x + r, c.y));
            painter.LineTo(new Vector2(c.x, c.y + r));
            painter.LineTo(new Vector2(c.x - r, c.y));
            painter.ClosePath();
            if (doFill) painter.Fill();
            else painter.Stroke();
        }

        private void DrawTangentHandles(Painter2D painter, Keyframe k)
        {
            Vector2 keyPos = new Vector2(TimeToX(k.time), ValToY(k.value));
            var accent = DesignerPalette.Accent;

            if (_selectedKey > 0 && !float.IsInfinity(k.inTangent))
            {
                Vector2 inPos = TangentNubPos(k, -1);
                painter.strokeColor = new Color(accent.r, accent.g, accent.b, 0.9f);
                painter.lineWidth = 1.5f;
                painter.BeginPath(); painter.MoveTo(keyPos); painter.LineTo(inPos); painter.Stroke();
                DrawNub(painter, inPos, accent);
            }
            if (_selectedKey < _curve.length - 1 && !float.IsInfinity(k.outTangent))
            {
                Vector2 outPos = TangentNubPos(k, +1);
                painter.strokeColor = new Color(accent.r, accent.g, accent.b, 0.9f);
                painter.lineWidth = 1.5f;
                painter.BeginPath(); painter.MoveTo(keyPos); painter.LineTo(outPos); painter.Stroke();
                DrawNub(painter, outPos, accent);
            }
        }

        private void DrawNub(Painter2D painter, Vector2 c, Color accent)
        {
            // Contrast ring (white on dark / near-black on light) + accent fill so the grab target
            // reads clearly and looks pickable over the themed well.
            painter.fillColor = accent;
            painter.BeginPath();
            painter.Arc(c, TangentNubRadius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
            painter.strokeColor = new Color(_inkStroke.r, _inkStroke.g, _inkStroke.b, 0.9f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(c, TangentNubRadius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();
        }

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

        private static float SnapPx(float v, float ppp) => (Mathf.Round(v * ppp - 0.5f) + 0.5f) / ppp;
    }
}
