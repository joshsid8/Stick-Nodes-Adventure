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
    /// The shared draw + edit surface for the Gradient editor widget — a sleek, spring-driven alternative to
    /// Unity's GradientField. The composited gradient-over-checkerboard bar is a generated <see cref="Texture2D"/>
    /// set as this element's <c>backgroundImage</c> (rounded + clipped by the well USS), and the draggable
    /// color/alpha PINS are vector-drawn above it with <see cref="Painter2D"/> (following the device-pixel-snapping
    /// discipline of <see cref="GripGlyph"/> and the filled-path technique of <see cref="SasquatchCurveControl"/>).
    ///
    /// One instance backs the compact inline strip; a second, larger one backs the resizable editor window; a tiny
    /// <see cref="Minimal"/> one backs the field preview. They all edit the SAME underlying property: each writes
    /// through its own <see cref="SerializedProperty"/> and the siblings refresh via TrackPropertyValue. The
    /// Gradient is NEVER stored on the BindingNode — it lives on the bound property.
    ///
    /// Reads/writes go through <see cref="SerializedProperty.boxedValue"/> — there is no dedicated
    /// gradientValue accessor. Persistence is disposal-safe and undo-grouped (a whole drag collapses to one
    /// Ctrl+Z). Every animation is gated on <see cref="EditorSpringFX.MotionEnabled"/>.
    /// </summary>
    internal sealed class SasquatchGradientControl : VisualElement
    {
        private enum DragMode { None, Pin }

        // --- wiring ---
        private readonly SerializedProperty _prop;
        private readonly BindingNode _node;
        private readonly bool _editable;

        // --- working state (editor-only; never serialized) ---
        private Gradient _gradient = new Gradient();
        private GradientColorKey[] _colorKeys = Array.Empty<GradientColorKey>();
        private GradientAlphaKey[] _alphaKeys = Array.Empty<GradientAlphaKey>();

        // Selection / hover are (channel, index). channel: 0 = color (or merged), 1 = alpha.
        private int _selChannel = -1, _selIndex = -1;
        private int _hoverChannel = -1, _hoverIndex = -1;

        // Drag state.
        private DragMode _dragMode = DragMode.None;
        private bool _dragActive;
        private bool _pendingDelete;
        private Vector2 _dragStartLocal;
        private int _dragChannel = -1, _dragIndex = -1;
        private int _undoGroup = -1;
        private Vector2 _lastLocalPointer;
        private int _dragPointerId = -1;                                 // captured pointer, so Escape can release it
        private GradientColorKey[] _dragSnapColor;                       // pre-drag snapshot for Escape-to-cancel
        private GradientAlphaKey[] _dragSnapAlpha;

        // Per-pin scale springs (selection/hover pop), parallel to the channel arrays.
        private float[] _colorScale = Array.Empty<float>();
        private float[] _alphaScale = Array.Empty<float>();

        // Glassmorphic live drag tooltip (absolutely-positioned child; NOT the static .tooltip).
        private Label _tip;

        // Cached strip texture + the signature/size it was built for.
        private Texture2D _stripTex;
        private int _stripSig, _stripW, _stripH;

        /// <summary>Raised after the selection or gradient changes, so a host can refresh its blend control / palette dots.</summary>
        public event Action Changed;

        /// <summary>When true, draw only the bar (no pins, no events) — for the tiny field preview.</summary>
        public bool Minimal { get; set; }

        /// <summary>Optional: supplies the active palette's colors so pins can draw a "matches a palette color" dot.</summary>
        public Func<IReadOnlyList<Color>> PaletteColorsProvider { get; set; }

        /// <summary>Optional: a click (no drag) on a pin requests the host to open the edit popover (color/alpha).</summary>
        public Action<int, int> OpenPinRequested { get; set; }

        // Checkerboard is ALWAYS on with these hardcoded squares (per design — not user-configurable).
        // Unity-style white + medium grey: the bright squares make low alpha values far easier to read at a
        // glance than a dark-on-dark checker did.
        public static readonly Color CheckerA = new Color(1f, 1f, 1f, 1f);
        public static readonly Color CheckerB = new Color(0.5f, 0.5f, 0.5f, 1f);

        /// <summary>Optional: a short warning (e.g. the 8-key ceiling) the host can surface as a toast.</summary>
        public Action<string> OnWarn { get; set; }

        // Visual constants (points).
        private const float PinHitRadius = 11f;
        private const float PinRadius = 6f;
        private const float SelectedScale = 1.30f;
        private const float HoverScale = 1.16f;
        private const float DeleteOffset = 22f;  // drag this far past the track to arm a delete
        private const float EdgeInset = 7f;       // keep end pins fully visible inside the rounded strip
        private const int MaxKeys = 8;            // Gradient stores at most 8 color + 8 alpha keys

        // --- Ruler / snap (editing aids; persisted per-widget in EditorPrefs, NOT on the gradient or the node) ---
        private bool _showRuler;
        private bool _snapToRuler;
        private int _rulerLines = 5;              // evenly-spaced vertical guide lines (2..10), counting both ends
        private bool _rulerPrefsLoaded;
        private const float SnapPixelThreshold = 8f; // magnetic radius (points) — drag past it to ignore the snap
        private const float RulerInset = 4f;     // px the guide lines sit in from the top/bottom edges
        // Staggered pop in/out when the ruler toggles (Painter2D lines, so animated via a progress value).
        private double _rulerAnimStart;
        private bool _rulerAnimating;
        private bool _rulerAnimReveal;
        private IVisualElementScheduledItem _rulerAnimItem;
        private const float RulerAnimDur = 0.30f;
        private const float RulerStagger = 0.05f;

        public SasquatchGradientControl(SerializedProperty prop, BindingNode node, bool editable)
        {
            _prop = prop;
            _node = node;
            _editable = editable;

            AddToClassList("sasq-gradient-surface");
            style.flexGrow = 1;
            style.height = Length.Percent(100);
            focusable = editable;
            pickingMode = PickingMode.Position;

            generateVisualContent += OnGenerateVisualContent;

            ReloadFromProperty();

            if (editable)
            {
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCaptureOutEvent>(_ => { _dragMode = DragMode.None; _dragActive = false; _pendingDelete = false; RemoveTip(); });
                RegisterCallback<KeyDownEvent>(OnKeyDown);
                this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
            }

            // External changes (undo, multi-edit, the sibling editing the same gradient) → silent reload.
            this.TrackPropertyValueWithRefresh(prop, _ =>
            {
                if (_dragActive) return;
                ReloadFromProperty();
                RebuildStrip();
                MarkDirtyRepaint();
            });

            RegisterCallback<DetachFromPanelEvent>(_ => Spring.CancelAll(this));
            // The strip texture IS this element's backgroundImage, so it must outlive a re-parent (window tab
            // drag, row reorder): those re-attach the same control, and RebuildStripIfNeeded only runs off a
            // GeometryChanged that a same-size re-attach never fires. See PanelLifetime.
            PanelLifetime.OnRealDetach(this, () =>
            {
                if (_stripTex != null) { UnityEngine.Object.DestroyImmediate(_stripTex); _stripTex = null; }
            });
            RegisterCallback<GeometryChangedEvent>(_ => { RebuildStripIfNeeded(); MarkDirtyRepaint(); });
        }

        private bool Merged => _node.GradientPinLayout == GradientPinLayout.Merged;

        // ===========================================================================================
        // Ruler / snap (editing aids) — public surface used by the gradient toolbar; persisted per-widget
        // ===========================================================================================
        /// <summary>Show evenly-spaced vertical guide lines on the strip.</summary>
        public bool ShowRuler
        {
            get { EnsureRulerPrefs(); return _showRuler; }
            set { EnsureRulerPrefs(); if (_showRuler == value) return; _showRuler = value; SaveRulerPrefs(); StartRulerAnim(value); }
        }

        /// <summary>Magnetically snap dragged/added keys to the nearest ruler line (only when the ruler is shown).</summary>
        public bool SnapToRuler
        {
            get { EnsureRulerPrefs(); return _snapToRuler; }
            set { EnsureRulerPrefs(); if (_snapToRuler == value) return; _snapToRuler = value; SaveRulerPrefs(); }
        }

        /// <summary>Number of evenly-spaced vertical guide lines (2..10, counting both ends).</summary>
        public int RulerLines
        {
            get { EnsureRulerPrefs(); return _rulerLines; }
            set { EnsureRulerPrefs(); int v = Mathf.Clamp(value, 2, 10); if (_rulerLines == v) return; _rulerLines = v; SaveRulerPrefs(); MarkDirtyRepaint(); }
        }

        private string RulerPrefKey()
        {
            var target = SafeTarget();
            string tid = target != null ? target.GetType().FullName : "x";
            return $"SasquatchInspector.GradientRuler.{tid}.{_prop?.propertyPath}";
        }

        private void EnsureRulerPrefs()
        {
            if (_rulerPrefsLoaded) return;
            _rulerPrefsLoaded = true;
            string k = RulerPrefKey();
            _showRuler = EditorPrefs.GetBool(k + ".show", false);
            _snapToRuler = EditorPrefs.GetBool(k + ".snap", false);
            _rulerLines = Mathf.Clamp(EditorPrefs.GetInt(k + ".lines", 5), 2, 10);
        }

        private void SaveRulerPrefs()
        {
            string k = RulerPrefKey();
            EditorPrefs.SetBool(k + ".show", _showRuler);
            EditorPrefs.SetBool(k + ".snap", _snapToRuler);
            EditorPrefs.SetInt(k + ".lines", _rulerLines);
        }

        // The guide-line times (0..1), evenly spaced including both ends.
        private IEnumerable<float> RulerTimes()
        {
            int n = Mathf.Clamp(_rulerLines, 2, 10);
            for (int i = 0; i < n; i++) yield return i / (float)(n - 1);
        }

        // Snap a time to the nearest ruler line IF the pointer is within the magnetic pixel radius of it; else the
        // raw time (so you can still drag a key anywhere between lines, like a video editor's magnetic timeline).
        private float MaybeSnapTime(float t, float pixelX)
        {
            if (!_showRuler || !_snapToRuler) return t;
            float bestT = t; float bestDist = SnapPixelThreshold;
            foreach (var gt in RulerTimes())
            {
                float d = Mathf.Abs(TimeToX(gt) - pixelX);
                if (d <= bestDist) { bestDist = d; bestT = gt; }
            }
            return bestT;
        }

        // Staggered pop in (reveal) / out (hide) for the guide lines. They're Painter2D-drawn, so we animate a
        // per-line progress value and repaint each tick (the same spirit as the pins' spring pop).
        private void StartRulerAnim(bool reveal)
        {
            if (!EditorSpringFX.MotionEnabled) { _rulerAnimating = false; _rulerAnimItem?.Pause(); MarkDirtyRepaint(); return; }
            _rulerAnimReveal = reveal;
            _rulerAnimStart = EditorApplication.timeSinceStartup;
            _rulerAnimating = true;
            _rulerAnimItem?.Pause();
            _rulerAnimItem = schedule.Execute(() =>
            {
                float total = RulerAnimDur + RulerStagger * Mathf.Max(0, _rulerLines - 1) + 0.06f;
                MarkDirtyRepaint();
                if (EditorApplication.timeSinceStartup - _rulerAnimStart >= total) { _rulerAnimating = false; _rulerAnimItem?.Pause(); }
            }).Every(16);
            MarkDirtyRepaint();
        }

        // 0..~1.1 per-line progress (ease-out-back overshoot gives the "pop"); reveal counts up, hide counts down.
        private float RulerLineProgress(int i)
        {
            if (!_rulerAnimating) return _showRuler ? 1f : 0f;
            float elapsed = (float)(EditorApplication.timeSinceStartup - _rulerAnimStart);
            float local = Mathf.Clamp01((elapsed - i * RulerStagger) / RulerAnimDur);
            float eased = EaseOutBack(local);
            return _rulerAnimReveal ? eased : (1f - Mathf.Clamp01(eased));
        }

        private static float EaseOutBack(float x)
        {
            // Bigger c1 = more overshoot = bouncier pop (default 1.70158 was too subtle).
            const float c1 = 3.0f, c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        private void DrawRuler(Painter2D p, float ppp)
        {
            bool animatingOut = _rulerAnimating && !_rulerAnimReveal;
            if (!_showRuler && !animatingOut) return;

            int n = Mathf.Clamp(_rulerLines, 2, 10);
            float midY = H * 0.5f;
            float coreTop = RulerInset, coreBottom = H - RulerInset;          // inset from the edges
            float haloTop = coreTop - 1.5f, haloBottom = coreBottom + 1.5f;   // dark halo extends past the core ends
            for (int i = 0; i < n; i++)
            {
                float prog = RulerLineProgress(i);
                if (prog <= 0.001f) continue;
                float gt = i / (float)(n - 1);
                float x = SnapPx(TimeToX(gt), ppp);
                bool edge = i == 0 || i == n - 1;
                float a = Mathf.Clamp01(prog);

                // Pop = scale each line's half-height out from the center by progress (overshoot adds the bounce).
                float ct = Mathf.Lerp(midY, coreTop, prog), cb = Mathf.Lerp(midY, coreBottom, prog);
                float ht = Mathf.Lerp(midY, haloTop, prog), hb = Mathf.Lerp(midY, haloBottom, prog);

                // Same "always visible" treatment as the key outlines: a dark halo (wrapping the ends) under a
                // light core, so the line reads against ANY underlying gradient color.
                p.strokeColor = new Color(0f, 0f, 0f, 0.55f * a);
                p.lineWidth = 3f / ppp;
                p.BeginPath(); p.MoveTo(new Vector2(x, ht)); p.LineTo(new Vector2(x, hb)); p.Stroke();

                p.strokeColor = new Color(1f, 1f, 1f, (edge ? 0.95f : 0.8f) * a); // ends a touch brighter
                p.lineWidth = 1f / ppp;
                p.BeginPath(); p.MoveTo(new Vector2(x, ct)); p.LineTo(new Vector2(x, cb)); p.Stroke();
            }
        }

        // The "HDR" tag on the strip — centered, drawn in-mesh BEFORE the pins so the color pins draw ON TOP of it
        // (an overlay element would sit over them). Shows only when a color key actually carries HDR intensity
        // (channel > 1), mirroring Unity's own gradient field. A translucent dark pill keeps the white text legible
        // over any underlying color, including a bright HDR stop.
        private void DrawHdrBadge(MeshGenerationContext mgc)
        {
            if (!BindingFactory.GradientHasHdrColor(_gradient)) return;
            float cx = W * 0.5f, cy = H * 0.5f;
            const float fontSize = 9f;
            const float halfW = 13f, halfH = 7f; // ~26x14 pill
            FillRoundedRect(mgc.painter2D, cx - halfW, cy - halfH, halfW * 2f, halfH * 2f, 2.5f, new Color(0f, 0f, 0f, 0.55f));
            // DrawText pos is the text's top-left; nudge so "HDR" reads centered in the pill.
            mgc.DrawText("HDR", new Vector2(cx - 9.5f, cy - fontSize * 0.5f - 1f), fontSize, Color.white);
        }

        private static void FillRoundedRect(Painter2D p, float x, float y, float w, float h, float r, Color fill)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(x + r, y));
            p.LineTo(new Vector2(x + w - r, y));
            p.Arc(new Vector2(x + w - r, y + r), r, Angle.Degrees(-90f), Angle.Degrees(0f));
            p.LineTo(new Vector2(x + w, y + h - r));
            p.Arc(new Vector2(x + w - r, y + h - r), r, Angle.Degrees(0f), Angle.Degrees(90f));
            p.LineTo(new Vector2(x + r, y + h));
            p.Arc(new Vector2(x + r, y + h - r), r, Angle.Degrees(90f), Angle.Degrees(180f));
            p.LineTo(new Vector2(x, y + r));
            p.Arc(new Vector2(x + r, y + r), r, Angle.Degrees(180f), Angle.Degrees(270f));
            p.ClosePath();
            p.Fill();
        }

        // ===========================================================================================
        // Property access (disposal-safe) + persistence
        // ===========================================================================================
        private UnityEngine.Object SafeTarget()
        {
            try { return _prop?.serializedObject?.targetObject; }
            catch { return null; }
        }

        private Gradient CurrentGradient()
        {
            try
            {
                if (_prop == null || _prop.serializedObject == null || _prop.serializedObject.targetObject == null)
                    return null;
                return _prop.boxedValue as Gradient;
            }
            catch { return null; }
        }

        private void ApplyGradient(Gradient g)
        {
            try
            {
                if (_prop == null || _prop.serializedObject == null || _prop.serializedObject.targetObject == null)
                    return;
                _prop.boxedValue = g;
                _prop.serializedObject.ApplyModifiedProperties();
            }
            catch { /* SerializedObject disposed mid-teardown */ }
        }

        private void RecordUndo(string label)
        {
            var t = SafeTarget();
            if (t != null) Undo.RecordObject(t, label);
        }

        // Discrete edits (add / delete / invert / recolor / mode / menu) — one undo step each.
        private void WriteDiscrete(Gradient g, string label)
        {
            RecordUndo(label);
            ApplyGradient(g);
            Adopt(g);
            Changed?.Invoke();
            RebuildStrip();
            MarkDirtyRepaint();
        }

        // Per-move during a drag (already wrapped in one undo group at the first threshold cross).
        private void WriteLive(Gradient g)
        {
            RecordUndo("Move Gradient Key");
            ApplyGradient(g);
            Adopt(g);
            Changed?.Invoke();
            RebuildStrip();
            MarkDirtyRepaint();
        }

        private void Adopt(Gradient g)
        {
            _gradient = g;
            _colorKeys = g.colorKeys;
            _alphaKeys = g.alphaKeys;
            SyncScaleLengths();
        }

        public void ReloadFromProperty()
        {
            var g = CurrentGradient();
            Adopt(g != null ? Clone(g) : DefaultGradient());
            if (_selChannel == 0 && _selIndex >= (Merged ? Mathf.Min(_colorKeys.Length, _alphaKeys.Length) : _colorKeys.Length)) ClearSelection();
            if (_selChannel == 1 && _selIndex >= _alphaKeys.Length) ClearSelection();
            Changed?.Invoke();
        }

        private void SyncScaleLengths()
        {
            _colorScale = Resize(_colorScale, _colorKeys.Length);
            _alphaScale = Resize(_alphaScale, _alphaKeys.Length);
        }

        private static float[] Resize(float[] src, int n)
        {
            if (src.Length == n) return src;
            var ns = new float[n];
            for (int i = 0; i < n; i++) ns[i] = i < src.Length ? src[i] : 1f;
            return ns;
        }

        // ===========================================================================================
        // Gradient model helpers
        // ===========================================================================================
        private static Gradient Clone(Gradient g)
        {
            var ng = new Gradient();
            if (g != null)
            {
                ng.SetKeys(g.colorKeys, g.alphaKeys); // getters return copies; SetKeys clamps to 8 each
                ng.mode = g.mode;
            }
            return ng;
        }

        private static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private Gradient Build(GradientColorKey[] ck, GradientAlphaKey[] ak)
        {
            var g = new Gradient();
            g.SetKeys(ck, ak);
            g.mode = _gradient.mode;
            return g;
        }

        // Pure color (RGB) at t — Evaluate's RGB IS the color-key channel under any mode.
        private Color EvalRGB(float t) { var c = _gradient.Evaluate(Mathf.Clamp01(t)); c.a = 1f; return c; }
        // Pure alpha at t — Evaluate's A is the alpha-key channel under any mode.
        private float EvalAlpha(float t) => _gradient.Evaluate(Mathf.Clamp01(t)).a;

        // ===========================================================================================
        // Public host API
        // ===========================================================================================
        public Gradient SnapshotGradient() => Clone(_gradient);
        public GradientMode Mode => _gradient.mode;

        public void SetMode(GradientMode m)
        {
            if (_gradient.mode == m) return;
            var g = Clone(_gradient);
            g.mode = m;
            WriteDiscrete(g, "Set Gradient Blend Mode");
        }

        public bool HasColorSelection => _selChannel == 0 && _selIndex >= 0;

        public Color SelectedPinColor()
        {
            if (_selChannel == 0 && _selIndex >= 0 && _selIndex < _colorKeys.Length) return _colorKeys[_selIndex].color;
            return Color.white;
        }

        /// <summary>Applies a color to the selected color/merged pin (palette row / popover).</summary>
        public void ApplySelectedColor(Color rgb)
        {
            if (_selChannel != 0 || _selIndex < 0) { Warn("Select a color pin first"); return; }
            SetPinColor(0, _selIndex, rgb);
        }

        public void SetPinColor(int ch, int idx, Color rgb)
        {
            if (ch != 0 || idx < 0 || idx >= _colorKeys.Length) return;
            rgb.a = 1f;
            var ck = (GradientColorKey[])_colorKeys.Clone();
            var k = ck[idx]; k.color = rgb; ck[idx] = k;
            WriteDiscrete(Build(ck, _alphaKeys), "Set Gradient Color");
        }

        /// <summary>Current alpha (0..1) of a pin — for the alpha popover. ch 1 = alpha pin; Merged reads the
        /// aligned alpha at the color index.</summary>
        public float PinAlpha(int ch, int idx)
        {
            if (Merged)
            {
                if (idx >= 0 && idx < _alphaKeys.Length) return _alphaKeys[idx].alpha;
                if (idx >= 0 && idx < _colorKeys.Length) return EvalAlpha(_colorKeys[idx].time);
                return 1f;
            }
            if (ch == 1 && idx >= 0 && idx < _alphaKeys.Length) return _alphaKeys[idx].alpha;
            return 1f;
        }

        /// <summary>Sets the alpha (0..1) of an alpha/merged pin (the alpha popover slider).</summary>
        public void SetPinAlpha(int ch, int idx, float a)
        {
            a = Mathf.Clamp01(a);
            if (Merged)
            {
                var pins = MergedPins();
                int mi = ColorIndexToMergedIndex(idx, pins);
                if (mi < 0) return;
                pins[mi] = (pins[mi].t, pins[mi].c, a);
                WriteMerged(pins, "Set Gradient Alpha");
                return;
            }
            if (ch != 1 || idx < 0 || idx >= _alphaKeys.Length) return;
            var ak = (GradientAlphaKey[])_alphaKeys.Clone();
            var k = ak[idx]; k.alpha = a; ak[idx] = k;
            WriteDiscrete(Build(_colorKeys, ak), "Set Gradient Alpha");
        }

        /// <summary>Replaces the whole gradient (preset apply).</summary>
        public void ApplyGradientFull(Gradient g)
        {
            if (g == null) return;
            WriteDiscrete(Clone(g), "Apply Gradient Preset");
            ClearSelection();
        }

        /// <summary>t → 1-t on both channels.</summary>
        public void ReverseKeys()
        {
            var ck = (GradientColorKey[])_colorKeys.Clone();
            for (int i = 0; i < ck.Length; i++) { var k = ck[i]; k.time = 1f - k.time; ck[i] = k; }
            var ak = (GradientAlphaKey[])_alphaKeys.Clone();
            for (int i = 0; i < ak.Length; i++) { var k = ak[i]; k.time = 1f - k.time; ak[i] = k; }
            WriteDiscrete(Build(ck, ak), "Reverse Gradient");
        }

        /// <summary>Spaces each channel's keys evenly between their first and last times.</summary>
        public void DistributeEvenly()
        {
            var ck = SortColor((GradientColorKey[])_colorKeys.Clone());
            if (ck.Length > 1)
            {
                float t0 = ck[0].time, t1 = ck[ck.Length - 1].time;
                for (int i = 0; i < ck.Length; i++) { var k = ck[i]; k.time = Mathf.Lerp(t0, t1, i / (float)(ck.Length - 1)); ck[i] = k; }
            }
            var ak = SortAlpha((GradientAlphaKey[])_alphaKeys.Clone());
            if (ak.Length > 1)
            {
                float t0 = ak[0].time, t1 = ak[ak.Length - 1].time;
                for (int i = 0; i < ak.Length; i++) { var k = ak[i]; k.time = Mathf.Lerp(t0, t1, i / (float)(ak.Length - 1)); ak[i] = k; }
            }
            WriteDiscrete(Build(ck, ak), "Distribute Gradient Keys");
        }

        /// <summary>Makes the color & alpha key times coincide so Merged can show one pin per key. Lossless for
        /// ≤8 union times; downsamples (keeping endpoints) with a warning beyond.</summary>
        public void NormalizeForMerged()
        {
            if (MergedAligned()) return;

            var set = new List<float>();
            foreach (var k in _colorKeys) AddUnique(set, k.time);
            foreach (var k in _alphaKeys) AddUnique(set, k.time);
            set.Sort();
            if (set.Count == 0) { set.Add(0f); set.Add(1f); }
            if (set.Count > MaxKeys)
            {
                set = DownsampleTo8(set);
                Warn("Merged view limited to 8 keys — extra keys dropped (Dual-Track keeps them all).");
            }

            var ck = new GradientColorKey[set.Count];
            var ak = new GradientAlphaKey[set.Count];
            for (int i = 0; i < set.Count; i++)
            {
                float t = set[i];
                ck[i] = new GradientColorKey(EvalRGB(t), t);
                ak[i] = new GradientAlphaKey(EvalAlpha(t), t);
            }
            WriteDiscrete(Build(ck, ak), "Reconcile Gradient For Merged View");
        }

        private bool MergedAligned()
        {
            if (_colorKeys.Length != _alphaKeys.Length) return false;
            for (int i = 0; i < _colorKeys.Length; i++)
                if (Mathf.Abs(_colorKeys[i].time - _alphaKeys[i].time) > 1e-4f) return false;
            return true;
        }

        private static List<float> DownsampleTo8(List<float> sorted)
        {
            var outList = new List<float> { sorted[0] };
            // Evenly sample interior, always keeping the last endpoint.
            for (int i = 1; i < MaxKeys - 1; i++)
            {
                float f = i / (float)(MaxKeys - 1);
                int idx = Mathf.Clamp(Mathf.RoundToInt(f * (sorted.Count - 1)), 1, sorted.Count - 2);
                if (!outList.Contains(sorted[idx])) outList.Add(sorted[idx]);
            }
            if (!outList.Contains(sorted[sorted.Count - 1])) outList.Add(sorted[sorted.Count - 1]);
            outList.Sort();
            return outList;
        }

        private static void AddUnique(List<float> list, float t)
        {
            for (int i = 0; i < list.Count; i++) if (Mathf.Abs(list[i] - t) < 1e-4f) return;
            list.Add(t);
        }

        // ===========================================================================================
        // Coordinate mapping (time domain is fixed 0..1 — no zoom/pan)
        // ===========================================================================================
        private float W => Mathf.Max(1f, contentRect.width);
        private float H => Mathf.Max(1f, contentRect.height);
        private float TimeToX(float t) => EdgeInset + Mathf.Clamp01(t) * (W - 2f * EdgeInset);
        private float XToTime(float x) => Mathf.Clamp01((x - EdgeInset) / Mathf.Max(1e-5f, W - 2f * EdgeInset));
        private float PinRadiusNow => Mathf.Min(PinRadius, H * 0.34f);
        private float AlphaTrackY => PinRadiusNow + 1f;
        private float ColorTrackY => H - PinRadiusNow - 1f;
        private float MergedTrackY => H * 0.5f;
        private float TrackY(int ch) => Merged ? MergedTrackY : (ch == 1 ? AlphaTrackY : ColorTrackY);

        // ===========================================================================================
        // Strip texture
        // ===========================================================================================
        private void RebuildStripIfNeeded()
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(W * EditorGUIUtility.pixelsPerPoint), 2, 4096);
            int h = Mathf.Clamp(Mathf.RoundToInt(H * EditorGUIUtility.pixelsPerPoint), 2, 256);
            int sig = SasquatchGradientStripRenderer.Signature(_gradient);
            if (sig == _stripSig && w == _stripW && h == _stripH && _stripTex != null) return;
            _stripSig = sig; _stripW = w; _stripH = h;
            RebuildStrip();
        }

        private void RebuildStrip()
        {
            int w = _stripW > 0 ? _stripW : 256;
            int h = _stripH > 0 ? _stripH : 32;
            _stripSig = SasquatchGradientStripRenderer.Signature(_gradient);

            // Re-render IN PLACE. The dirty check above keys on the pixel width, which changes on every frame
            // of a drag by construction, so this runs per frame — and it used to allocate a Texture2D, upload
            // it, and DestroyImmediate the previous one, every one of those frames. Reinitialise-and-refill
            // renders the identical pixels without the native allocate/free cycle. Ownership is unchanged:
            // this control still owns _stripTex and still tears it down in PanelLifetime.OnRealDetach.
            bool sameTexture = _stripTex != null;
            _stripTex = SasquatchGradientStripRenderer.RenderInto(_stripTex, _gradient, w, h, true, CheckerA, CheckerB);
            if (_stripTex == null) return;

            // Re-bind the background even when the texture INSTANCE did not change. style.backgroundImage
            // compares before writing (UI Toolkit's InlineStyleAccess), so assigning an equal Background is a
            // no-op — which would leave the renderer holding a binding made when the texture had its OLD
            // dimensions. Clearing first makes both writes real. Both happen before the panel repaints, so
            // there is no frame in which the strip is missing.
            if (sameTexture) style.backgroundImage = new Background();
            style.backgroundImage = Background.FromTexture2D(_stripTex);
        }

        // ===========================================================================================
        // Drawing
        // ===========================================================================================
        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (Minimal) return; // bar-only preview (background image already shows the gradient)
            var rect = contentRect;
            if (rect.width < 4f || rect.height < 4f) return;

            var p = mgc.painter2D;
            float ppp = EditorGUIUtility.pixelsPerPoint;

            DrawRuler(p, ppp); // guide lines under the pins

            // "HDR" tag, drawn BEFORE the pins so they paint over it; only once a key carries real intensity.
            DrawHdrBadge(mgc);

            if (Merged)
            {
                var pins = MergedPins();
                for (int i = 0; i < pins.Count; i++)
                    DrawMergedPin(p, ppp, i, pins[i].t, pins[i].c, pins[i].a);
            }
            else
            {
                for (int i = 0; i < _colorKeys.Length; i++)
                    DrawColorPin(p, ppp, i, _colorKeys[i].time, _colorKeys[i].color);
                for (int i = 0; i < _alphaKeys.Length; i++)
                    DrawAlphaPin(p, ppp, i, _alphaKeys[i].time, _alphaKeys[i].alpha);
            }
        }

        private void DrawColorPin(Painter2D p, float ppp, int idx, float time, Color rgb)
        {
            bool sel = _selChannel == 0 && _selIndex == idx;
            float scale = idx < _colorScale.Length ? _colorScale[idx] : 1f;
            Vector2 c = new Vector2(SnapPx(TimeToX(time), ppp), SnapPx(ColorTrackY, ppp));
            float r = PinRadiusNow * Mathf.Max(0.01f, scale);

            Color body = new Color(rgb.r, rgb.g, rgb.b, sel ? 1f : 0.92f);
            FillCircle(p, c, r, body);
            StrokeCircle(p, c, r, sel ? DesignerPalette.Accent : ContrastOutline(rgb), sel ? 1.75f : 1f);
            DrawPaletteHalo(p, c, r, rgb);
        }

        private void DrawAlphaPin(Painter2D p, float ppp, int idx, float time, float alpha)
        {
            bool sel = _selChannel == 1 && _selIndex == idx;
            float scale = idx < _alphaScale.Length ? _alphaScale[idx] : 1f;
            Vector2 c = new Vector2(SnapPx(TimeToX(time), ppp), SnapPx(AlphaTrackY, ppp));
            float r = PinRadiusNow * Mathf.Max(0.01f, scale);

            float g = Mathf.Clamp01(alpha);
            FillCircle(p, c, r, new Color(g, g, g, sel ? 1f : 0.92f));
            StrokeCircle(p, c, r, sel ? DesignerPalette.Accent : ContrastOutline(new Color(g, g, g)), sel ? 1.75f : 1f);
        }

        private void DrawMergedPin(Painter2D p, float ppp, int idx, float time, Color rgb, float alpha)
        {
            bool sel = _selChannel == 0 && _selIndex == idx;
            float scale = idx < _colorScale.Length ? _colorScale[idx] : 1f;
            Vector2 c = new Vector2(SnapPx(TimeToX(time), ppp), SnapPx(MergedTrackY, ppp));
            float r = PinRadiusNow * Mathf.Max(0.01f, scale);

            // Top half = solid color.
            FillHalf(p, c, r, true, new Color(rgb.r, rgb.g, rgb.b, sel ? 1f : 0.92f));
            // Bottom half = color × alpha over a neutral checker average.
            Color checkerAvg = Color.Lerp(CheckerA, CheckerB, 0.5f);
            Color lower = Color.Lerp(checkerAvg, new Color(rgb.r, rgb.g, rgb.b, 1f), Mathf.Clamp01(alpha));
            FillHalf(p, c, r, false, new Color(lower.r, lower.g, lower.b, sel ? 1f : 0.92f));

            StrokeCircle(p, c, r, sel ? DesignerPalette.Accent : ContrastOutline(rgb), sel ? 1.75f : 1f);
            // hairline across the diameter so the split reads
            p.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            p.lineWidth = 1f / ppp;
            p.BeginPath(); p.MoveTo(new Vector2(c.x - r, c.y)); p.LineTo(new Vector2(c.x + r, c.y)); p.Stroke();
            DrawPaletteHalo(p, c, r, rgb);
        }

        // A slim accent "halo" ring hugging any pin whose color is on the active palette. Drawn just OUTSIDE the pin
        // (the old dot sat BELOW it, at c.y + r + 2.5, which for color pins fell off the bottom of the strip and was
        // effectively invisible). A halo is always on-strip, reads at a glance, and stays distinct from the selection
        // ring (which sits ON the pin edge). A faint dark moat under the accent keeps it legible even when the
        // gradient behind the pin happens to share the accent hue. Color + merged pins only — alpha pins carry no
        // color to match, so DrawAlphaPin never calls this.
        private void DrawPaletteHalo(Painter2D p, Vector2 c, float r, Color rgb)
        {
            if (!_node.GradientShowPaletteDot || PaletteColorsProvider == null) return;
            var colors = PaletteColorsProvider();
            if (colors == null) return;
            bool match = false;
            for (int i = 0; i < colors.Count; i++) if (ColorApprox(colors[i], rgb)) { match = true; break; }
            if (!match) return;
            float haloR = r + 1.7f;
            StrokeCircle(p, c, haloR, new Color(0f, 0f, 0f, 0.28f), 2.2f); // soft moat: contrast on any background
            StrokeCircle(p, c, haloR, DesignerPalette.Accent, 1.2f);       // the accent halo itself
        }

        private static void FillCircle(Painter2D p, Vector2 c, float r, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }

        // A high-contrast outline for a pin drawn over `surface`: near-black on light colors, near-white on dark
        // ones (chosen by perceived luminance). Keeps every key visible — a white key on a white gradient stop
        // would vanish behind the old fixed white outline. Luminance (not RGB inversion, which leaves mid-greys
        // ~mid-grey) guarantees separation at both ends of the value range.
        private static Color ContrastOutline(Color surface)
        {
            float luma = 0.299f * surface.r + 0.587f * surface.g + 0.114f * surface.b;
            return luma > 0.5f ? new Color(0.05f, 0.05f, 0.05f, 0.95f) : new Color(1f, 1f, 1f, 0.95f);
        }

        private static void StrokeCircle(Painter2D p, Vector2 c, float r, Color stroke, float width)
        {
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        // top = top half (y up), else bottom half. (Screen y is down, so 180→360 = top, 0→180 = bottom.)
        private static void FillHalf(Painter2D p, Vector2 c, float r, bool top, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            if (top) p.Arc(c, r, Angle.Degrees(180f), Angle.Degrees(360f));
            else p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(180f));
            p.ClosePath();
            p.Fill();
        }

        private static float SnapPx(float v, float ppp) => (Mathf.Round(v * ppp - 0.5f) + 0.5f) / ppp;

        private static bool ColorApprox(Color a, Color b)
        {
            const float e = 1.5f / 255f;
            return Mathf.Abs(a.r - b.r) < e && Mathf.Abs(a.g - b.g) < e && Mathf.Abs(a.b - b.b) < e;
        }

        // Merged pins as an aligned, time-sorted list.
        private List<(float t, Color c, float a)> MergedPins()
        {
            var list = new List<(float t, Color c, float a)>();
            int n = Mathf.Min(_colorKeys.Length, _alphaKeys.Length);
            for (int i = 0; i < n; i++)
                list.Add((_colorKeys[i].time, _colorKeys[i].color, _alphaKeys[i].alpha));
            list.Sort((x, y) => x.t.CompareTo(y.t));
            return list;
        }

        // ===========================================================================================
        // Pointer / key interaction (editable only)
        // ===========================================================================================
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.target != this) return;
            Focus();
            Vector2 p = evt.localPosition;
            _lastLocalPointer = p;
            if (evt.button != 0) return;

            var (ch, idx) = HitTestPin(p);

            if (evt.clickCount >= 2 && ch < 0) { AddKeyAt(p); evt.StopPropagation(); return; }

            if (ch >= 0)
            {
                // Ctrl/Cmd-click inverts alpha (alpha pins, and merged pins which carry alpha).
                if ((evt.ctrlKey || evt.commandKey) && (Merged || ch == 1)) { InvertAlpha(ch, idx); evt.StopPropagation(); return; }
                Select(ch, idx);
                ArmDrag(ch, idx, p, evt);
            }
            else
            {
                Select(-1, -1);
            }
        }

        private void ArmDrag(int ch, int idx, Vector2 p, PointerDownEvent evt)
        {
            _dragMode = DragMode.Pin;
            _dragActive = false;
            _pendingDelete = false;
            _dragChannel = ch;
            _dragIndex = idx;
            _dragStartLocal = p;
            _dragPointerId = evt.pointerId;
            _dragSnapColor = (GradientColorKey[])_colorKeys.Clone(); // snapshot so Escape can restore the pre-drag state
            _dragSnapAlpha = (GradientAlphaKey[])_alphaKeys.Clone();
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        // Escape mid-drag: restore the pre-drag gradient and release the pointer (PointerCaptureOut clears the rest).
        private void CancelDrag()
        {
            if (!_dragActive) return;
            if (_dragSnapColor != null && _dragSnapAlpha != null)
                WriteDiscrete(Build(_dragSnapColor, _dragSnapAlpha), "Cancel Gradient Edit");
            _pendingDelete = false;
            RemoveTip();
            if (_dragPointerId >= 0 && this.HasPointerCapture(_dragPointerId)) this.ReleasePointer(_dragPointerId);
            ResetAllPinScales();
            MarkDirtyRepaint();
            // Collapse HERE, not in OnPointerUp. Releasing the capture above fires PointerCaptureOut, which
            // zeroes _dragMode — so the eventual PointerUp early-returns and never reaches its collapse.
            // Without this every per-move RecordObject from the cancelled drag stays its own undo step, and
            // undoing the cancel means Ctrl+Z once per mouse-move sample.
            if (_undoGroup >= 0) { Undo.CollapseUndoOperations(_undoGroup); _undoGroup = -1; }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            _lastLocalPointer = evt.localPosition;
            if (_dragMode == DragMode.None) { SetHover(HitTestPin(evt.localPosition)); return; }
            if (!this.HasPointerCapture(evt.pointerId)) return;

            Vector2 p = evt.localPosition;
            if (!_dragActive)
            {
                if ((p - _dragStartLocal).magnitude < DesignerPalette.DragThreshold) return;
                _dragActive = true;
                Undo.IncrementCurrentGroup();
                _undoGroup = Undo.GetCurrentGroup();
            }

            bool canDelete = CanDelete(_dragChannel);
            bool offBounds = Mathf.Abs(p.y - TrackY(_dragChannel)) > DeleteOffset;
            if (offBounds && canDelete)
            {
                if (!_pendingDelete) { _pendingDelete = true; SpringPin(_dragChannel, _dragIndex, 0.35f); }
                ShowTip(p, "Release to delete");
            }
            else
            {
                if (_pendingDelete) { _pendingDelete = false; SpringPin(_dragChannel, _dragIndex, SelectedScale); }
                MovePinTime(_dragChannel, _dragIndex, MaybeSnapTime(XToTime(p.x), p.x));
                ShowTip(p, DragTipText());
            }
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragMode == DragMode.None) return;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);

            bool wasActive = _dragActive;
            bool del = _pendingDelete;
            int ch = _dragChannel, idx = _dragIndex;
            _dragMode = DragMode.None;
            _dragActive = false;
            _pendingDelete = false;
            RemoveTip();

            if (wasActive)
            {
                int beforeColor = _colorKeys.Length, beforeAlpha = _alphaKeys.Length;
                if (del) DeleteKey(ch, idx);
                bool removed = _colorKeys.Length != beforeColor || _alphaKeys.Length != beforeAlpha;
                if (removed)
                {
                    // A key was deleted: cancel the in-flight shrink spring and reset all pin scales so the
                    // positional scale-array Resize can't strand a survivor at the dragged pin's shrunk scale.
                    Spring.CancelAll(this);
                    ResetAllPinScales();
                }
                else
                {
                    // Delete was declined (at the minimum) — the pin was left shrunk by the "release to delete"
                    // arm; spring it back to its resting/selected size instead of waiting for a hover to fix it.
                    bool sel = _selChannel == ch && _selIndex == idx;
                    if (idx >= 0 && idx < ScaleArr(ch).Length) SpringPin(ch, idx, sel ? SelectedScale : 1f);
                }
                if (_undoGroup >= 0) { Undo.CollapseUndoOperations(_undoGroup); _undoGroup = -1; }
            }
            else if (ch >= 0 && idx >= 0)
            {
                // A click (no drag) on ANY pin opens the edit popover (color pins → color; alpha pins → alpha slider).
                OpenPinRequested?.Invoke(ch, idx);
            }
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && _dragActive)
            {
                CancelDrag(); // put the key back where it was
                evt.StopPropagation();
                return;
            }
            if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && _selChannel >= 0)
            {
                if (CanDelete(_selChannel)) DeleteKey(_selChannel, _selIndex);
                evt.StopPropagation();
            }
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 p = _lastLocalPointer;
            var (ch, idx) = HitTestPin(p);
            if (ch >= 0)
            {
                Select(ch, idx);
                evt.menu.AppendAction("Edit…", _ => OpenPinRequested?.Invoke(ch, idx));
                evt.menu.AppendAction("Delete Key", _ => DeleteKey(ch, idx),
                    CanDelete(ch) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                if (Merged || ch == 1)
                    evt.menu.AppendAction("Invert Alpha", _ => InvertAlpha(ch, idx));
            }
            else if (Merged)
            {
                evt.menu.AppendAction("Add Key Here", _ => AddKeyAt(p));
            }
            else
            {
                evt.menu.AppendAction("Add Color Key Here", _ => AddKeyAt(p, forceChannel: 0));
                evt.menu.AppendAction("Add Alpha Key Here", _ => AddKeyAt(p, forceChannel: 1));
            }
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Reverse", _ => ReverseKeys());
            evt.menu.AppendAction("Distribute Evenly", _ => DistributeEvenly());
            evt.menu.AppendSeparator();
            foreach (GradientMode m in Enum.GetValues(typeof(GradientMode)))
            {
                var mm = m;
                evt.menu.AppendAction("Blend Mode/" + NiceMode(m), _ => SetMode(mm),
                    _gradient.mode == m ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
        }

        internal static string NiceMode(GradientMode m)
        {
            switch (m)
            {
                case GradientMode.Fixed: return "Fixed";
                case GradientMode.PerceptualBlend: return "Perceptual";
                default: return "Blend";
            }
        }

        private string DragTipText()
        {
            if (_dragChannel == 1) // alpha
            {
                if (_dragIndex >= 0 && _dragIndex < _alphaKeys.Length)
                    return Mathf.RoundToInt(_alphaKeys[_dragIndex].alpha * 100f) + "%";
            }
            else if (_dragIndex >= 0 && _dragIndex < _colorKeys.Length)
            {
                return ColorHexUtility.ToHex(_colorKeys[_dragIndex].color);
            }
            return "";
        }

        // ===========================================================================================
        // Hit testing
        // ===========================================================================================
        private (int ch, int idx) HitTestPin(Vector2 p)
        {
            int bestCh = -1, bestIdx = -1;
            float bestD = PinHitRadius;
            if (Merged)
            {
                var pins = MergedPins();
                for (int i = 0; i < pins.Count; i++)
                {
                    float d = Vector2.Distance(p, new Vector2(TimeToX(pins[i].t), MergedTrackY));
                    if (d < bestD) { bestD = d; bestCh = 0; bestIdx = MergedIndexToColorIndex(i, pins[i].t); }
                }
            }
            else
            {
                for (int i = 0; i < _alphaKeys.Length; i++)
                {
                    float d = Vector2.Distance(p, new Vector2(TimeToX(_alphaKeys[i].time), AlphaTrackY));
                    if (d < bestD) { bestD = d; bestCh = 1; bestIdx = i; }
                }
                for (int i = 0; i < _colorKeys.Length; i++)
                {
                    float d = Vector2.Distance(p, new Vector2(TimeToX(_colorKeys[i].time), ColorTrackY));
                    if (d < bestD) { bestD = d; bestCh = 0; bestIdx = i; }
                }
            }
            return (bestCh, bestIdx);
        }

        // The merged pin list is time-sorted; map back to the underlying color-key index for selection/edit.
        private int MergedIndexToColorIndex(int mergedIdx, float t)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _colorKeys.Length; i++)
            {
                float d = Mathf.Abs(_colorKeys[i].time - t);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ===========================================================================================
        // Key mutations
        // ===========================================================================================
        private bool CanDelete(int ch)
        {
            if (Merged) return Mathf.Min(_colorKeys.Length, _alphaKeys.Length) > 2;
            return ch == 1 ? _alphaKeys.Length > 1 : _colorKeys.Length > 1;
        }

        private void AddKeyAt(Vector2 p, int forceChannel = -1)
        {
            float t = MaybeSnapTime(XToTime(p.x), p.x);
            if (Merged)
            {
                if (Mathf.Min(_colorKeys.Length, _alphaKeys.Length) >= MaxKeys) { Warn("8 keys max — delete one first."); return; }
                var pins = MergedPins();
                pins.Add((t, EvalRGB(t), EvalAlpha(t)));
                WriteMerged(pins, "Add Gradient Key");
                SelectMergedNear(t);
                return;
            }

            int ch = forceChannel >= 0 ? forceChannel : (p.y < H * 0.5f ? 1 : 0);
            if (ch == 1)
            {
                if (_alphaKeys.Length >= MaxKeys) { Warn("8 alpha keys max — delete one first."); return; }
                var ak = Append(_alphaKeys, new GradientAlphaKey(EvalAlpha(t), t), out _);
                WriteDiscrete(Build(_colorKeys, SortAlpha(ak)), "Add Alpha Key");
                SelectColorOrAlphaNear(1, t);
            }
            else
            {
                if (_colorKeys.Length >= MaxKeys) { Warn("8 color keys max — delete one first."); return; }
                var ck = Append(_colorKeys, new GradientColorKey(EvalRGB(t), t), out _);
                WriteDiscrete(Build(SortColor(ck), _alphaKeys), "Add Color Key");
                SelectColorOrAlphaNear(0, t);
            }
        }

        private void MovePinTime(int ch, int idx, float t)
        {
            if (Merged)
            {
                var pins = MergedPins();
                int mi = ColorIndexToMergedIndex(idx, pins);
                if (mi < 0) return;
                pins[mi] = (Mathf.Clamp01(t), pins[mi].c, pins[mi].a);
                WriteMerged(pins, "Move Gradient Key", live: true);
                SelectMergedNear(Mathf.Clamp01(t));
                _dragIndex = _selIndex; // re-sort may have shifted the index; keep dragging the same pin
                return;
            }
            if (ch == 1)
            {
                if (idx < 0 || idx >= _alphaKeys.Length) return;
                var ak = (GradientAlphaKey[])_alphaKeys.Clone();
                var k = ak[idx]; k.time = Mathf.Clamp01(t); ak[idx] = k;
                ak = SortAlpha(ak);
                WriteLive(Build(_colorKeys, ak));
                _selChannel = 1; _selIndex = IndexOfAlpha(ak, Mathf.Clamp01(t));
            }
            else
            {
                if (idx < 0 || idx >= _colorKeys.Length) return;
                var ck = (GradientColorKey[])_colorKeys.Clone();
                var k = ck[idx]; k.time = Mathf.Clamp01(t); ck[idx] = k;
                ck = SortColor(ck);
                WriteLive(Build(ck, _alphaKeys));
                _selChannel = 0; _selIndex = IndexOfColor(ck, Mathf.Clamp01(t));
            }
            _dragIndex = _selIndex;
        }

        private void DeleteKey(int ch, int idx)
        {
            if (!CanDelete(ch)) return;
            if (Merged)
            {
                var pins = MergedPins();
                int mi = ColorIndexToMergedIndex(idx, pins);
                if (mi < 0) return;
                pins.RemoveAt(mi);
                WriteMerged(pins, "Delete Gradient Key");
                ClearSelection();
                return;
            }
            if (ch == 1)
            {
                if (idx < 0 || idx >= _alphaKeys.Length) return;
                WriteDiscrete(Build(_colorKeys, RemoveAtAlpha(_alphaKeys, idx)), "Delete Alpha Key");
            }
            else
            {
                if (idx < 0 || idx >= _colorKeys.Length) return;
                WriteDiscrete(Build(RemoveAtColor(_colorKeys, idx), _alphaKeys), "Delete Color Key");
            }
            ClearSelection();
        }

        private void InvertAlpha(int ch, int idx)
        {
            if (Merged)
            {
                var pins = MergedPins();
                int mi = ColorIndexToMergedIndex(idx, pins);
                if (mi < 0) return;
                float a = pins[mi].a > 0.5f ? 0f : 1f;
                pins[mi] = (pins[mi].t, pins[mi].c, a);
                WriteMerged(pins, "Invert Gradient Alpha");
                return;
            }
            if (ch != 1 || idx < 0 || idx >= _alphaKeys.Length) return;
            var ak = (GradientAlphaKey[])_alphaKeys.Clone();
            var k = ak[idx]; k.alpha = k.alpha > 0.5f ? 0f : 1f; ak[idx] = k;
            WriteDiscrete(Build(_colorKeys, ak), "Invert Gradient Alpha");
        }

        private void WriteMerged(List<(float t, Color c, float a)> pins, string undo, bool live = false)
        {
            pins.Sort((x, y) => x.t.CompareTo(y.t));
            var ck = new GradientColorKey[pins.Count];
            var ak = new GradientAlphaKey[pins.Count];
            for (int i = 0; i < pins.Count; i++)
            {
                ck[i] = new GradientColorKey(pins[i].c, pins[i].t);
                ak[i] = new GradientAlphaKey(pins[i].a, pins[i].t);
            }
            if (live) WriteLive(Build(ck, ak)); else WriteDiscrete(Build(ck, ak), undo);
        }

        private int ColorIndexToMergedIndex(int colorIdx, List<(float t, Color c, float a)> pins)
        {
            if (colorIdx < 0 || colorIdx >= _colorKeys.Length) return -1;
            float t = _colorKeys[colorIdx].time;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < pins.Count; i++)
            {
                float d = Mathf.Abs(pins[i].t - t);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ----- array helpers -----
        private static GradientColorKey[] Append(GradientColorKey[] src, GradientColorKey k, out int idx)
        { var n = new GradientColorKey[src.Length + 1]; Array.Copy(src, n, src.Length); n[src.Length] = k; idx = src.Length; return n; }
        private static GradientAlphaKey[] Append(GradientAlphaKey[] src, GradientAlphaKey k, out int idx)
        { var n = new GradientAlphaKey[src.Length + 1]; Array.Copy(src, n, src.Length); n[src.Length] = k; idx = src.Length; return n; }
        private static GradientColorKey[] RemoveAtColor(GradientColorKey[] src, int idx)
        { var n = new GradientColorKey[src.Length - 1]; int j = 0; for (int i = 0; i < src.Length; i++) if (i != idx) n[j++] = src[i]; return n; }
        private static GradientAlphaKey[] RemoveAtAlpha(GradientAlphaKey[] src, int idx)
        { var n = new GradientAlphaKey[src.Length - 1]; int j = 0; for (int i = 0; i < src.Length; i++) if (i != idx) n[j++] = src[i]; return n; }
        private static GradientColorKey[] SortColor(GradientColorKey[] a) { Array.Sort(a, (x, y) => x.time.CompareTo(y.time)); return a; }
        private static GradientAlphaKey[] SortAlpha(GradientAlphaKey[] a) { Array.Sort(a, (x, y) => x.time.CompareTo(y.time)); return a; }
        private static int IndexOfColor(GradientColorKey[] a, float t) { int b = 0; float bd = float.MaxValue; for (int i = 0; i < a.Length; i++) { float d = Mathf.Abs(a[i].time - t); if (d < bd) { bd = d; b = i; } } return b; }
        private static int IndexOfAlpha(GradientAlphaKey[] a, float t) { int b = 0; float bd = float.MaxValue; for (int i = 0; i < a.Length; i++) { float d = Mathf.Abs(a[i].time - t); if (d < bd) { bd = d; b = i; } } return b; }

        // ===========================================================================================
        // Selection / hover springs
        // ===========================================================================================
        private void Select(int ch, int idx)
        {
            if (ch == _selChannel && idx == _selIndex) return;
            if (_selChannel >= 0) SpringPin(_selChannel, _selIndex, 1f);
            _selChannel = ch; _selIndex = idx;
            if (ch >= 0) SpringPin(ch, idx, SelectedScale);
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        private void ClearSelection() { _selChannel = -1; _selIndex = -1; Changed?.Invoke(); MarkDirtyRepaint(); }

        private void SelectColorOrAlphaNear(int ch, float t)
        {
            _selChannel = ch;
            _selIndex = ch == 1 ? IndexOfAlpha(_alphaKeys, t) : IndexOfColor(_colorKeys, t);
            SpringPin(ch, _selIndex, SelectedScale);
            Changed?.Invoke();
        }

        private void SelectMergedNear(float t)
        {
            _selChannel = 0;
            _selIndex = IndexOfColor(_colorKeys, t);
            SpringPin(0, _selIndex, SelectedScale);
            Changed?.Invoke();
        }

        private void SetHover((int ch, int idx) hit)
        {
            if (hit.ch == _hoverChannel && hit.idx == _hoverIndex) return;
            bool prevWasSel = _hoverChannel == _selChannel && _hoverIndex == _selIndex;
            if (_hoverChannel >= 0 && !prevWasSel) SpringPin(_hoverChannel, _hoverIndex, 1f);
            _hoverChannel = hit.ch; _hoverIndex = hit.idx;
            bool isSel = hit.ch == _selChannel && hit.idx == _selIndex;
            if (hit.ch >= 0 && !isSel) SpringPin(hit.ch, hit.idx, HoverScale);
            MarkDirtyRepaint();
        }

        private float[] ScaleArr(int ch) => ch == 1 ? _alphaScale : _colorScale;

        // Snap every pin's transient scale back to resting (1). Used after a delete so an in-flight shrink spring
        // (and the positional scale-array Resize) can't leave a surviving pin stuck small.
        private void ResetAllPinScales()
        {
            // Cancel any in-flight pin springs FIRST — otherwise a running shrink spring (e.g. the "release to
            // delete" 0.35 shrink) keeps driving the scale back down after we set it to 1, which is what leaves
            // a pin stuck small after an Escape-cancel.
            Spring.CancelAll(this);
            for (int i = 0; i < _colorScale.Length; i++) _colorScale[i] = 1f;
            for (int i = 0; i < _alphaScale.Length; i++) _alphaScale[i] = 1f;
            MarkDirtyRepaint();
        }

        private void SpringPin(int ch, int idx, float target)
        {
            var arr = ScaleArr(ch);
            if (idx < 0 || idx >= arr.Length) return;
            int i = idx;
            if (!EditorSpringFX.MotionEnabled) { arr[i] = target; MarkDirtyRepaint(); return; }
            Spring.To((object)this,
                () => i < arr.Length ? arr[i] : target,
                v => { if (i < arr.Length) { arr[i] = v; MarkDirtyRepaint(); } },
                target, EditorSpringFX.Tune(SpringConfig.Bouncy), "pin" + ch + "_" + i);
        }

        // ===========================================================================================
        // Glassmorphic drag tooltip
        // ===========================================================================================
        private void EnsureTip()
        {
            if (_tip != null) return;
            _tip = new Label { pickingMode = PickingMode.Ignore };
            _tip.AddToClassList("sasq-gradient-tip");
            _tip.style.position = Position.Absolute;
            _tip.style.display = DisplayStyle.None;
            Add(_tip);
        }

        private void ShowTip(Vector2 p, string text)
        {
            EnsureTip();
            bool wasHidden = _tip.style.display == DisplayStyle.None;
            _tip.text = text;
            _tip.style.display = DisplayStyle.Flex;
            if (wasHidden) EditorSpringFX.PopIn(_tip); // spring the "release to delete" card in
            float left = Mathf.Clamp(TimeToX(XToTime(p.x)) - 16f, 2f, Mathf.Max(2f, W - 42f));
            // place opposite the dragged track so the tip never hides the pin (and stays inside the clipped well)
            float top = _dragChannel == 1 ? Mathf.Max(2f, H - 18f) : 2f;
            _tip.style.left = left;
            _tip.style.top = top;
        }

        private void RemoveTip() { if (_tip != null) _tip.style.display = DisplayStyle.None; }

        // ===========================================================================================
        // Screen-space helpers (for the host's color popover + drag-from-palette)
        // ===========================================================================================
        private Rect ElementScreenRect()
        {
            var wb = worldBound;
            if (panel != null)
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (w != null && w.rootVisualElement != null && w.rootVisualElement.panel == panel)
                    {
                        var origin = w.position.position;
                        return new Rect(origin.x + wb.x, origin.y + wb.y, wb.width, wb.height);
                    }
            }
            return wb;
        }

        private Vector2 PinCenter(int ch, int idx)
        {
            if (Merged)
            {
                if (idx >= 0 && idx < _colorKeys.Length) return new Vector2(TimeToX(_colorKeys[idx].time), MergedTrackY);
            }
            else if (ch == 1)
            {
                if (idx >= 0 && idx < _alphaKeys.Length) return new Vector2(TimeToX(_alphaKeys[idx].time), AlphaTrackY);
            }
            else if (idx >= 0 && idx < _colorKeys.Length)
            {
                return new Vector2(TimeToX(_colorKeys[idx].time), ColorTrackY);
            }
            return new Vector2(W * 0.5f, H * 0.5f);
        }

        /// <summary>A small screen-space rect centered on a pin, to anchor a popover (ShowAsDropDown).</summary>
        public Rect PinScreenRect(int ch, int idx)
        {
            var sr = ElementScreenRect();
            var c = PinCenter(ch, idx);
            return new Rect(sr.x + c.x - 9f, sr.y + c.y - 9f, 18f, 18f);
        }

        /// <summary>Hit-tests a pin under a SCREEN point (for drag-a-swatch-onto-a-pin).</summary>
        public (int ch, int idx) PinAtScreenPoint(Vector2 screenPoint)
        {
            var sr = ElementScreenRect();
            return HitTestPin(new Vector2(screenPoint.x - sr.x, screenPoint.y - sr.y));
        }

        public Color PinColor(int ch, int idx)
        {
            if (ch == 0 && idx >= 0 && idx < _colorKeys.Length) return _colorKeys[idx].color;
            return Color.white;
        }

        /// <summary>Delete a specific key by (channel, index), if the minimum-key rule allows it. Lets the pin
        /// popover honor the Delete key while it — not the control — holds focus.</summary>
        public void RequestDeleteKey(int ch, int idx)
        {
            if (ch < 0 || idx < 0) return;
            if (CanDelete(ch)) DeleteKey(ch, idx);
        }

        /// <summary>Return keyboard focus to the control (e.g. after the pin popover closes) so the still-selected
        /// key responds to Delete/Backspace. Selection survives the popover, so this simply re-arms the shortcut.</summary>
        public void RefocusForKeyboard()
        {
            if (panel != null && focusable) Focus();
        }

        private void Warn(string msg) => OnWarn?.Invoke(msg);
    }
}
