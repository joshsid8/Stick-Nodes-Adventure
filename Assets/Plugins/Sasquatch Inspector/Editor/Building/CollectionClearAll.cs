using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The collection header's "empty this list" control: a quiet trash icon beside the "+" and the
    /// eyedropper that only fires when it is HELD, with a ring sweeping around it to show how long is left.
    ///
    /// <para><b>Why a hold and not a dialog.</b> Emptying a list is destructive and irreversible-looking, so
    /// it needs a confirmation — but a modal <c>EditorUtility.DisplayDialog</c> stops the editor, steals
    /// focus, and asks a question the user has already answered by reaching for the button. The hold IS the
    /// confirmation: it cannot be produced by a mis-click, it is cancelled by simply letting go, and the
    /// ring shows the commitment building instead of announcing it after the fact. Same reasoning as the
    /// hold-to-confirm in a game's UI, which is where the shape comes from.</para>
    ///
    /// <para><b>Nothing is written until the ring closes.</b> A release, a pointer that wanders off the
    /// button, or a lost capture (a modal opens, the window blurs) all cancel with no data change at all —
    /// the array is only ever touched from <see cref="Fire"/>, at the end of a completed hold.</para>
    ///
    /// <para><b>Asset Store §2.5.g:</b> public UI Toolkit API only — no editor-internal type is named, cast
    /// or reflected.</para>
    /// </summary>
    public static class CollectionClearAll
    {
        /// <summary>USS class on the header button, so a restyle and the tests share one handle.</summary>
        internal const string ButtonClass = "sasq-list-clear-header";

        /// <summary>The out-of-the-box hold duration, in seconds, before the user has touched the setting.
        /// The kit's baked twin (<c>KitClearHoldSeconds</c> in ExportedWidgetKit) carries the same number.</summary>
        internal const float DefaultHoldSeconds = 0.65f;

        /// <summary>
        /// Test-only override of <see cref="HoldSeconds"/>. The hold is driven by real wall-clock time
        /// (<c>EditorApplication.timeSinceStartup</c>), which no synchronous test can fast-forward, so a gesture
        /// test that cannot reach the end of the hold cannot prove the hold ever fires — it shortens the
        /// DURATION instead. Null (the shipped state) ⇒ the project setting is used. Always restore to null:
        /// leaving a number here pins the duration for the rest of the session.
        /// </summary>
        internal static float? HoldSecondsOverride;

        /// <summary>
        /// How long the button must be held, in seconds, before the list is emptied — Project Settings ▸
        /// Sasquatch Inspector ▸ Inspectors ▸ "List Clear Hold Time". Read ONCE per gesture (at pointer-down),
        /// not per tick, so the ring's duration cannot change underneath a hold already in progress.
        /// </summary>
        internal static float HoldSeconds
        {
            get
            {
                if (HoldSecondsOverride.HasValue) return HoldSecondsOverride.Value;
                var settings = SasquatchSettingsManager.Current;
                return settings != null ? settings.ClearHoldSeconds : DefaultHoldSeconds;
            }
        }

        /// <summary>Tick interval of the hold's progress ring, in ms. ~60fps while held, and only while held.</summary>
        private const long TickMs = 16;

        private const string IdleTooltip = "Hold to clear the list";
        private const string HoldingTooltip = "Keep holding to clear…";

        // ---------------------------------------------------------------------------------------------
        // The hold outline
        // ---------------------------------------------------------------------------------------------

        /// <summary>Stroke width of the hold outline, in points.</summary>
        internal const float OutlineWidth = 2f;

        /// <summary>
        /// The hold-progress outline: a stroke that grows around the BUTTON'S OWN ROUNDED-RECT EDGE, from
        /// top centre, clockwise, closing on itself when the hold completes.
        ///
        /// <para><b>Why not a circle.</b> A circular dial inside a rectangular icon button is a second shape
        /// competing with the one already on screen — on a 20&#215;16 header button it reads as a red blob
        /// behind the glyph rather than as progress. Tracing the button's own border makes the button itself
        /// the progress bar, so there is nothing extra to parse.</para>
        ///
        /// <para><b>Why <see cref="Painter2D"/> and not four animated child elements.</b> Same reason
        /// <see cref="GripGlyph"/> is painted: the layout engine rounds every child's rect to the device
        /// pixel grid independently, so an outline assembled from four edge divs shimmers and breaks at its
        /// corners at fractional display scaling. One path has one quantization.</para>
        ///
        /// <para>Public because the verification tests live in a separate assembly.</para>
        /// </summary>
        public sealed class HoldRing : VisualElement
        {
            private float _progress;
            private Color _tint = Color.white;

            /// <summary>0 → nothing drawn, 1 → the outline closed all the way round. Clamped; repaints only on a real change.</summary>
            public float Progress
            {
                get => _progress;
                set
                {
                    float v = Mathf.Clamp01(value);
                    if (Mathf.Approximately(_progress, v)) return;
                    _progress = v;
                    MarkDirtyRepaint();
                }
            }

            /// <summary>Stroke colour. Mesh data bakes the colour, so a change needs a repaint.</summary>
            public Color Tint
            {
                get => _tint;
                set
                {
                    if (_tint == value) return;
                    _tint = value;
                    MarkDirtyRepaint();
                }
            }

            /// <summary>Creates the outline as a picking-disabled overlay that fills its parent.</summary>
            public HoldRing()
            {
                pickingMode = PickingMode.Ignore; // the BUTTON owns the gesture; this is pure paint
                style.position = Position.Absolute;
                style.left = 0;
                style.right = 0;
                style.top = 0;
                style.bottom = 0;
                generateVisualContent += OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext mgc)
            {
                if (_progress <= 0.001f) return;
                var rect = contentRect;
                if (rect.width < 3f || rect.height < 3f) return;

                // The CORNER RADIUS IS READ FROM THE PARENT, not from this element. The outline's whole job
                // is to sit on the button's edge, and the button is what carries the radius — this overlay
                // is a bare VisualElement with none of its own. Reading it live means a restyle of
                // `.sasq-list-clear-header` moves the outline with it, for free.
                float radius = 0f;
                if (parent != null)
                {
                    var ps = parent.resolvedStyle;
                    radius = Mathf.Max(Mathf.Max(ps.borderTopLeftRadius, ps.borderTopRightRadius),
                                       Mathf.Max(ps.borderBottomLeftRadius, ps.borderBottomRightRadius));
                    if (float.IsNaN(radius) || radius < 0f) radius = 0f;
                }

                // Inset by HALF the stroke, so a centred stroke lands fully inside the button rather than
                // straddling its edge and painting into the neighbouring eyedropper.
                float half = OutlineWidth * 0.5f;
                var path = rect;
                path.xMin += half; path.yMin += half; path.xMax -= half; path.yMax -= half;
                if (path.width <= 0.5f || path.height <= 0.5f) return;

                var pts = TracePath(path, Mathf.Max(0f, radius - half), _progress);
                if (pts.Count < 2) return;

                var painter = mgc.painter2D;
                painter.lineWidth = OutlineWidth;
                painter.strokeColor = _tint;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                painter.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) painter.LineTo(pts[i]);
                painter.Stroke();
            }
        }

        /// <summary>
        /// The rounded-rectangle perimeter of <paramref name="rect"/> as a polyline, starting at TOP CENTRE
        /// and running CLOCKWISE, truncated to <paramref name="progress"/> of its total length.
        ///
        /// <para>Top centre and clockwise because that is which way every progress dial in every OS turns,
        /// and starting mid-edge rather than at a corner means the two ends meet in the middle of a straight
        /// run — a seam on a corner is visible as a notch, a seam on a flat edge is not.</para>
        ///
        /// <para>The corners are FLATTENED into short segments rather than emitted as arcs. Truncating a
        /// path at an arbitrary fraction of its length needs a length-parameterised path, and a polyline is
        /// one; a mix of lines and arcs is not without integrating each arc anyway. Segment count scales
        /// with the radius, so a 3px corner costs three segments and never looks faceted.</para>
        ///
        /// <para>Pure geometry — no panel, no element — which is what makes it testable on its own.</para>
        /// </summary>
        internal static List<Vector2> TracePath(Rect rect, float radius, float progress)
        {
            var outPts = new List<Vector2>();
            progress = Mathf.Clamp01(progress);
            if (progress <= 0f || rect.width <= 0f || rect.height <= 0f) return outPts;

            // A radius can never exceed half the shorter side, or the two corners on that side overlap and
            // the straight run between them runs backwards.
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);

            var loop = new List<Vector2>();
            float l = rect.xMin, t = rect.yMin, rt = rect.xMax, b = rect.yMax;
            float cx = rect.center.x;

            // Screen space: +y is DOWN, so 0° is right, 90° is down. These four sweeps are therefore
            // clockwise ON SCREEN, which is the opposite of the usual maths convention — check any change
            // against that, not against intuition.
            loop.Add(new Vector2(cx, t));                                   // top centre — the seam
            loop.Add(new Vector2(rt - r, t));                               // …to the top-right corner
            AppendArc(loop, new Vector2(rt - r, t + r), r, -90f, 0f);       // top-right
            loop.Add(new Vector2(rt, b - r));                               // right edge
            AppendArc(loop, new Vector2(rt - r, b - r), r, 0f, 90f);        // bottom-right
            loop.Add(new Vector2(l + r, b));                                // bottom edge, right to left
            AppendArc(loop, new Vector2(l + r, b - r), r, 90f, 180f);       // bottom-left
            loop.Add(new Vector2(l, t + r));                                // left edge
            AppendArc(loop, new Vector2(l + r, t + r), r, 180f, 270f);      // top-left
            loop.Add(new Vector2(cx, t));                                   // …and back to the seam

            float total = 0f;
            for (int i = 1; i < loop.Count; i++) total += Vector2.Distance(loop[i - 1], loop[i]);
            if (total <= 0f) return outPts;

            float budget = total * progress;
            outPts.Add(loop[0]);
            for (int i = 1; i < loop.Count; i++)
            {
                float seg = Vector2.Distance(loop[i - 1], loop[i]);
                if (seg <= 0f) continue;
                if (budget >= seg)
                {
                    outPts.Add(loop[i]);
                    budget -= seg;
                    continue;
                }
                // The last, partial segment — this is what makes the outline grow smoothly instead of
                // snapping from one vertex to the next.
                outPts.Add(Vector2.Lerp(loop[i - 1], loop[i], budget / seg));
                return outPts;
            }
            return outPts;
        }

        /// <summary>
        /// Appends a flattened arc to <paramref name="into"/>, EXCLUDING its first point (the caller has
        /// already added it as the end of the previous run) and including its last.
        /// </summary>
        private static void AppendArc(List<Vector2> into, Vector2 centre, float r, float fromDeg, float toDeg)
        {
            if (r <= 0f) return; // a square corner is already described by the two edges meeting at it
            int steps = Mathf.Clamp(Mathf.CeilToInt(r * 1.5f), 3, 12);
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)steps);
                into.Add(new Vector2(centre.x + Mathf.Cos(a) * r, centre.y + Mathf.Sin(a) * r));
            }
        }

        // ---------------------------------------------------------------------------------------------
        // The header button
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The hold-to-clear button for a collection header. Never null — unlike the eyedropper this works
        /// for every element type, so there is no collection it cannot serve.
        /// </summary>
        /// <param name="onCleared">Redraws the collection after the array is emptied.</param>
        /// <param name="suppress">
        /// Design select-only (Interact OFF): make the button inert so a click falls through to SELECT the
        /// collection card, exactly as the neighbouring "+" does.
        /// </param>
        internal static Button CreateHeaderButton(SerializedProperty arrayProp, SerializedObject serializedObject,
            Action onCleared, bool suppress = false)
        {
            if (arrayProp == null || serializedObject == null) return null;

            // The PATH, not the SerializedProperty. This button lives in the HEADER, which survives every
            // in-place rows rebuild — so it outlives the iterator it was built from and has to re-resolve
            // one each time it acts. Same rule, and the same reason, as CollectionEyedropper's stored path.
            string arrayPath = arrayProp.propertyPath;

            // NO CLICKABLE AT ALL — and this is not a tidy-up, it is the fix for a bug that made the whole
            // gesture dead on arrival.
            //
            // `Clickable.ProcessDownEvent` calls `evt.StopPropagation()`, and at the TARGET element that
            // skips every callback registered after it — not just the rest of the propagation path. So a
            // `PointerDownEvent` handler added to a stock `Button` never runs, and neither does
            // `EditorSpringFX.ButtonSpring`'s press-dip. Measured 2026-08-29 against a real panel: the
            // handler saw nothing, the hold never started, and the ring sat at 0 forever.
            //
            // The fix is to register in the TRICKLE-DOWN phase, which at the target runs BEFORE the
            // manipulator's bubble-phase handler. Every pointer callback below therefore says
            // `TrickleDown.TrickleDown`, and removing one is enough to make the gesture dead again.
            //
            // The Clickable is deliberately LEFT IN PLACE rather than nulled: `Button` reaches for it from
            // its own default action handling (a focused button submitted from the keyboard), and this
            // control has no click action worth trading that risk for. It simply never activates now — the
            // trickle handler stops the event before it — so the pointer CAPTURE is taken explicitly below,
            // which is what keeps move and up arriving once the cursor leaves the button mid-hold.
            var button = new Button();
            button.AddToClassList(ButtonClass);
            button.tooltip = IdleTooltip;

            var glyph = SasquatchIcons.Create(SasquatchIcons.Ids.Trash2, 11, WidgetPalette.LabelMuted);
            button.Add(glyph);

            var ring = new HoldRing { Tint = WidgetPalette.DangerText };
            button.Add(ring);

            // Match the eyedropper: start narrow, because the stock Button padding is sized for text.
            button.style.paddingLeft = 4;
            button.style.paddingRight = 4;
            EditorSpringFX.ButtonSpring(button);

            if (suppress)
            {
                DisableInteractivityRecursively(button);
                Sync(button, arrayProp);
                return button;
            }

            // --- the hold -----------------------------------------------------------------------------
            double startedAt = 0d;
            bool holding = false;
            IVisualElementScheduledItem ticker = null;
            // Snapshotted at pointer-down rather than read per tick: the setting is a project asset, and a hold
            // whose target duration moved mid-gesture would make the ring speed up or stall under the finger.
            float holdSeconds = DefaultHoldSeconds;

            void Stop(bool fired)
            {
                holding = false;
                ticker?.Pause();
                ring.Progress = 0f;
                button.tooltip = IdleTooltip;
                // Back to the resting tint either way: on a cancel because nothing happened, and on a fire
                // because the button is about to be hidden by Sync (the list is now empty).
                SetGlyphTint(button, WidgetPalette.LabelMuted);
            }

            ticker = button.schedule.Execute(() =>
            {
                if (!holding) return;
                float t = (float)((EditorApplication.timeSinceStartup - startedAt) / holdSeconds);
                ring.Progress = t;
                if (t < 1f) return;

                // The ring closed: this is the confirmation. Stop FIRST — Fire triggers a rebuild, and a
                // ticker left running against a torn-down subtree is the one way this could fire twice.
                Stop(fired: true);
                // Let the pointer go: the hold completed while the button was still held down, so nothing
                // else would release it until the next press. Guarded, because a fire reached from a lost
                // capture no longer holds one.
                if (button.HasPointerCapture(PointerId.mousePointerId))
                    button.ReleasePointer(PointerId.mousePointerId);
                Fire(serializedObject.FindProperty(arrayPath), serializedObject, button, onCleared);
            }).Every(TickMs);
            ticker.Pause();

            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                var live = serializedObject.FindProperty(arrayPath);
                if (live == null || live.arraySize == 0) return; // nothing to clear; it is hidden anyway
                holding = true;
                holdSeconds = Mathf.Max(0.001f, HoldSeconds); // never divide by zero, whatever the asset says
                startedAt = EditorApplication.timeSinceStartup;
                ring.Progress = 0f;
                button.tooltip = HoldingTooltip;
                SetGlyphTint(button, WidgetPalette.DangerText);
                // Capture explicitly, now that no Clickable does it for us: without it a cursor that slips
                // off the button mid-hold stops sending move and up, and the gesture could neither complete
                // nor cancel — it would simply hang with the ring frozen part-way round.
                button.CapturePointer(evt.pointerId);
                ticker.Resume();
                // The header bar folds the collection on PointerDown; a press meant for this button must
                // not also collapse the list out from under the ring the user is watching.
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            // A release short of the full hold is a CANCEL, not a clear — that is the whole point.
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!holding) return;
                Stop(fired: false);
                if (button.HasPointerCapture(evt.pointerId)) button.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            // A pointer that wanders off the button mid-hold cancels too, which is how every hold-to-confirm
            // in every game lets you back out without lifting your finger. Checked on MOVE rather than on
            // PointerLeave because the Clickable has CAPTURED the pointer, and a captured pointer sends no
            // leave event — the gesture would otherwise complete with the cursor halfway across the window.
            button.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!holding) return;
                if (button.ContainsPoint(button.WorldToLocal(evt.position))) return;
                Stop(fired: false);
                if (button.HasPointerCapture(evt.pointerId)) button.ReleasePointer(evt.pointerId);
            }, TrickleDown.TrickleDown);

            // A gesture TAKEN AWAY — a modal opens, the window blurs, the inspector rebuilds under the
            // cursor — never sends PointerUp. Without this the ticker keeps running and the list empties
            // itself while the user is looking at something else entirely.
            button.RegisterCallback<PointerCaptureOutEvent>(_ => { if (holding) Stop(fired: false); });

            // Nothing to clear ⇒ nothing to offer.
            Sync(button, arrayProp);
            return button;
        }

        /// <summary>
        /// Brings the button into agreement with the array: an EMPTY collection hides it outright rather
        /// than showing a disabled control, because a greyed trash can beside a "+" reads as a feature that
        /// is broken rather than one that has nothing to do.
        ///
        /// <para>Called on every in-place rows rebuild, beside the count pill's retext — the header itself
        /// is NOT rebuilt by a live add/remove, so without this the button would keep whatever visibility it
        /// was built with for the life of the inspector.</para>
        /// </summary>
        internal static void Sync(Button button, SerializedProperty arrayProp)
        {
            if (button == null) return;
            bool any = arrayProp != null && arrayProp.arraySize > 0;
            button.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Empties the array as ONE undo step and redraws. Split out from the gesture so a test can drive
        /// the data half without a synthetic 850ms press, and so the hold above has exactly one place it can
        /// touch the user's data.
        /// </summary>
        internal static void Fire(SerializedProperty arrayProp, SerializedObject serializedObject,
            VisualElement host, Action onCleared)
        {
            if (arrayProp == null || serializedObject == null) return;
            if (!arrayProp.isArray) return;

            // Sync BEFORE the count is read: the array can have shrunk under a hold that has been running
            // for the best part of a second (an undo, another inspector, a script).
            serializedObject.Update();
            int removed = arrayProp.arraySize;
            if (removed == 0) return;
            arrayProp.ClearArray();
            serializedObject.ApplyModifiedProperties();
            onCleared?.Invoke();

            // Say what happened, and — because the ring is gone the instant it fires — say that Ctrl+Z puts
            // it back. A destructive action with no visible receipt is what makes people distrust a button.
            if (host != null)
            {
                SasquatchToast.Show(host,
                    removed == 1 ? "Cleared 1 item — Ctrl+Z to undo" : $"Cleared {removed} items — Ctrl+Z to undo");
            }
        }

        /// <summary>Re-tints the trash glyph. Icons bake their colour, so this asks the element to redraw.</summary>
        private static void SetGlyphTint(VisualElement button, Color tint)
        {
            var glyph = button?.Q<SasquatchIconElement>();
            if (glyph != null) glyph.Tint = tint;
        }

        /// <summary>
        /// Design select-only sweep — the local twin of <c>LayoutTreeBuilder.DisableInteractivityRecursively</c>,
        /// kept here so this file has no ordering dependency on the builder's private helpers.
        /// </summary>
        private static void DisableInteractivityRecursively(VisualElement element)
        {
            if (element == null) return;
            element.pickingMode = PickingMode.Ignore;
            element.focusable = false;
            foreach (var child in element.Children()) DisableInteractivityRecursively(child);
        }
    }
}
