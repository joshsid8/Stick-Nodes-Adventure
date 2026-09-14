using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Demo.Editor
{
    /// <summary>
    /// A hand-written custom editor whose interesting half is the path editor — the case where displacing the
    /// whole editor would throw away the only part that mattered.
    ///
    /// <see cref="DrawPathPreview"/> carries <c>[SasquatchEmbed]</c>, which makes it an embeddable "slice": a
    /// designed layout can place an Embedded Editor node that re-hosts exactly this drawing, inside a layout
    /// that handles all the ordinary fields. The slice is self-contained on purpose — it reads the live target,
    /// writes back through <c>serializedObject</c>, and repaints on change, so it behaves identically whether
    /// Unity hosts it here or Sasquatch re-inlines it.
    ///
    /// The pad is not a picture of the data, it IS an editor of it: drag a waypoint to move it, click a leg to
    /// insert one, Alt+click to remove one, drag the wedge handle to set sight range and cone. A simulated
    /// guard walks the route so that every other field on the component — mode, speed, wait, turn rate — is
    /// visibly doing something. That two-way link is the reason a buyer would refuse to give this editor up,
    /// and the reason the Embedded Editor node exists.
    ///
    /// Public editor API only, per Asset Store guideline 2.5.g.
    /// </summary>
    [CustomEditor(typeof(DemoPatrolRoute))]
    public class DemoPatrolRouteInspector : UnityEditor.Editor
    {
        private const float PadHeight = 210f;
        private const float PadInset = 20f;    // px of breathing room between the route's extreme and the edge
        private const float BaseExtent = 7f;   // world units mapped to the pad's half-width before auto-fit
        private const float NodeRadius = 5f;
        private const float StartRadius = 7f;
        private const float GrabRadius = 11f;  // pointer slop when picking a waypoint
        private const float LegGrab = 7f;      // pointer slop when picking a leg to insert into

        private const string Hint =
            "Drag a waypoint  ·  click a leg to insert  ·  Alt+click to remove  ·  drag the wedge handle for sight";

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var note = new HelpBox(
                "Hand-written custom editor. The interactive path editor below is marked [SasquatchEmbed] — a " +
                "designed layout can re-host just that piece with an Embedded Editor node, and keep it fully live.",
                HelpBoxMessageType.Info);
            root.Add(note);

            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var spacer = new VisualElement();
            spacer.style.height = 10f;
            root.Add(spacer);

            root.Add(DrawPathPreview());
            return root;
        }

        /// <summary>
        /// The embeddable slice: a live, editable top-down view of the patrol route. Zero parameters, returns a
        /// bound VisualElement — the shape <c>EmbeddedEditorNode</c> requires. Every piece of mutable state is a
        /// local captured by the closures below, never a field, so two hosts (Unity's inspector and a Sasquatch
        /// layout) can each build one without fighting over a shared cursor.
        /// </summary>
        [SasquatchEmbed("Patrol Path Preview")]
        public VisualElement DrawPathPreview()
        {
            // ---- per-call interaction + simulation state -------------------------------------------------
            var sim = new Sim();
            int hoverNode = -1;
            int dragNode = -1;
            int insertAfter = -1;                // leg index the ghost waypoint would follow, -1 for none
            Vector2 insertAt = Vector2.zero;     // world position of that ghost
            bool hoverHandle = false;
            bool dragHandle = false;
            float pinnedExtent = 0f;             // the view scale is pinned mid-drag so the pad can't zoom under the cursor
            int undoGroup = 0;
            string dragLabel = "Move Waypoint";  // what the whole collapsed drag lands in the undo stack as
            double lastTick = EditorApplication.timeSinceStartup;

            DemoPatrolRoute Route() => target as DemoPatrolRoute;
            List<Vector2> Points() { var r = Route(); return r != null ? r.waypoints : null; }

            // ---- chrome ----------------------------------------------------------------------------------
            var card = new VisualElement();
            card.style.backgroundColor = new StyleColor(new Color(0.10f, 0.12f, 0.16f, 0.95f));
            SetBorder(card, new Color(0.24f, 0.32f, 0.44f, 0.7f), 1f, 8f);
            card.style.paddingLeft = 12f;
            card.style.paddingRight = 12f;
            card.style.paddingTop = 10f;
            card.style.paddingBottom = 10f;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8f;
            card.Add(header);

            var title = new Label("PATROL PATH");
            title.style.fontSize = 11f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.45f, 0.72f, 1f);
            header.Add(title);

            var tools = new VisualElement();
            tools.style.flexDirection = FlexDirection.Row;
            header.Add(tools);

            var pad = new VisualElement();
            pad.style.height = PadHeight;
            pad.style.flexShrink = 0f;
            pad.style.backgroundColor = new StyleColor(new Color(0.07f, 0.08f, 0.11f, 1f));
            SetBorder(pad, new Color(0.20f, 0.42f, 0.66f, 0.6f), 1f, 6f);
            pad.style.overflow = Overflow.Hidden;   // the sight wedge is allowed to run past the view; clip it here
            card.Add(pad);

            var empty = new Label("No waypoints yet — press Add");
            empty.style.position = Position.Absolute;
            empty.style.left = 0f; empty.style.right = 0f; empty.style.top = 0f; empty.style.bottom = 0f;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.fontSize = 11f;
            empty.style.color = new Color(0.45f, 0.50f, 0.60f);
            empty.pickingMode = PickingMode.Ignore;
            pad.Add(empty);

            var readout = new Label();
            readout.style.fontSize = 11f;
            readout.style.color = new Color(0.66f, 0.73f, 0.83f);
            readout.style.marginTop = 8f;
            card.Add(readout);

            var hint = new Label(Hint);
            hint.style.fontSize = 10f;
            hint.style.color = new Color(0.42f, 0.47f, 0.56f);
            hint.style.marginTop = 2f;
            hint.style.whiteSpace = WhiteSpace.Normal;   // the pad narrows inside a designed layout; let this wrap
            card.Add(hint);

            // ---- view mapping ----------------------------------------------------------------------------
            // Auto-fit: the view widens to hold whatever the buyer types into the waypoint list, so the picture
            // never silently crops. Pinned during a drag, or the pad would zoom out as you pull a node outward
            // and the node would crawl away from the cursor.
            float ViewExtent()
            {
                if (pinnedExtent > 0f) return pinnedExtent;
                float e = BaseExtent;
                var pts = Points();
                if (pts != null)
                    foreach (var p in pts)
                        e = Mathf.Max(e, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) * 1.12f);
                return e;
            }

            View ViewFor(Rect rect)
            {
                float half = Mathf.Max(8f, Mathf.Min(rect.width, rect.height) * 0.5f - PadInset);
                return new View(rect.center, half / ViewExtent());
            }

            // ---- geometry --------------------------------------------------------------------------------
            // The drawn route: the waypoints, plus the closing leg when the guard loops.
            List<Vector2> RoutePolyline()
            {
                var pts = Points();
                var line = new List<Vector2>();
                if (pts == null) return line;
                line.AddRange(pts);
                var r = Route();
                if (r != null && r.mode == DemoPatrolRoute.RouteMode.Loop && pts.Count > 1) line.Add(pts[0]);
                return line;
            }

            // Where the sight wedge sits: on the first waypoint, facing the way the guard sets off. Deliberately
            // static — a handle anchored to the moving guard would be a moving target.
            bool SightWedge(View view, Rect rect, out Vector2 origin, out float faceDeg, out float radius,
                            out Vector2 handle)
            {
                origin = Vector2.zero; faceDeg = 0f; radius = 0f; handle = Vector2.zero;
                var r = Route();
                var pts = Points();
                if (r == null || !r.alertOnSight || pts == null || pts.Count < 2) return false;

                origin = view.ToPad(pts[0]);
                faceDeg = PadAngle(pts[1] - pts[0]);
                radius = Mathf.Max(8f, r.sightRange * view.Scale);
                float edge = faceDeg + r.sightConeDegrees * 0.5f;

                // Keep the handle reachable even when the range runs off the pad — it stops at the edge, and a
                // drag past it still widens the range because the pointer is captured.
                float reach = Mathf.Min(radius, MaxReach(origin, Dir(edge), rect) - 8f);
                handle = origin + Dir(edge) * Mathf.Max(10f, reach);
                return true;
            }

            // ---- writes ----------------------------------------------------------------------------------
            // Everything goes through SerializedProperty so the undo stack, prefab overrides and the fields
            // above (or the designed layout around us) all stay in step with the pad.
            SerializedProperty Waypoints()
            {
                if (serializedObject == null || serializedObject.targetObject == null) return null;
                serializedObject.Update();
                return serializedObject.FindProperty("waypoints");
            }

            void WriteWaypoint(int index, Vector2 value)
            {
                var list = Waypoints();
                if (list == null || index < 0 || index >= list.arraySize) return;
                list.GetArrayElementAtIndex(index).vector2Value = value;
                serializedObject.ApplyModifiedProperties();
            }

            void InsertWaypoint(int index, Vector2 value)
            {
                var list = Waypoints();
                if (list == null) return;
                index = Mathf.Clamp(index, 0, list.arraySize);
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).vector2Value = value;
                serializedObject.ApplyModifiedProperties();
                Undo.SetCurrentGroupName("Insert Waypoint");
            }

            void RemoveWaypoint(int index)
            {
                var list = Waypoints();
                if (list == null || index < 0 || index >= list.arraySize || list.arraySize <= 2) return;
                list.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                Undo.SetCurrentGroupName("Remove Waypoint");
            }

            void WriteFloat(string path, float value)
            {
                if (serializedObject == null || serializedObject.targetObject == null) return;
                serializedObject.Update();
                var p = serializedObject.FindProperty(path);
                if (p == null) return;
                p.floatValue = value;
                serializedObject.ApplyModifiedProperties();
            }

            // ---- readouts --------------------------------------------------------------------------------
            void Sync()
            {
                var r = Route();
                var pts = Points();
                if (r == null) return;

                int count = pts != null ? pts.Count : 0;
                empty.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

                float length = 0f;
                var line = RoutePolyline();
                for (int i = 1; i < line.Count; i++) length += Vector2.Distance(line[i - 1], line[i]);
                if (r.mode == DemoPatrolRoute.RouteMode.PingPong) length *= 2f;

                string lap = "—";
                if (r.moveSpeed > 0.01f && count > 1)
                    lap = (length / r.moveSpeed + r.waitAtWaypoint * count).ToString("0.0") + " s";

                readout.text = $"{count} waypoints  ·  {r.mode}  ·  {r.moveSpeed:0.0} m/s  ·  " +
                               $"{length:0.0} m circuit  ·  {lap} lap";
                pad.MarkDirtyRepaint();
            }

            void SyncHint()
            {
                var r = Route();
                var pts = Points();
                int active = dragNode >= 0 ? dragNode : hoverNode;

                if (dragHandle || hoverHandle)
                {
                    hint.text = r == null
                        ? Hint
                        : $"Sight  ·  range {r.sightRange:0.0} m  ·  cone {r.sightConeDegrees:0}°  " +
                          "(drag out for range, around for width)";
                }
                else if (active >= 0 && pts != null && active < pts.Count)
                {
                    hint.text = $"Waypoint {active + 1} of {pts.Count}  ·  " +
                                $"({pts[active].x:0.0}, {pts[active].y:0.0})" +
                                (dragNode >= 0 ? "  ·  hold Ctrl to snap" : "  ·  Alt+click to remove");
                }
                else if (insertAfter >= 0)
                {
                    hint.text = $"Click to insert a waypoint here  ·  ({insertAt.x:0.0}, {insertAt.y:0.0})";
                }
                else
                {
                    hint.text = Hint;
                }
            }

            // ---- simulation ------------------------------------------------------------------------------
            // The guard exists so the numeric fields aren't inert. Mode picks the circuit, speed drives it,
            // waitAtWaypoint parks it at each corner, turnRateDegrees makes its facing lag behind the turn.
            List<Vector2> TravelPath()
            {
                var pts = Points();
                var path = new List<Vector2>();
                if (pts == null || pts.Count < 2) return path;
                var r = Route();
                path.AddRange(pts);
                if (r == null) return path;
                if (r.mode == DemoPatrolRoute.RouteMode.PingPong)
                    for (int i = pts.Count - 2; i >= 1; i--) path.Add(pts[i]);
                // Loop and PingPong both close on a repeat of the first point, so the wrap is a no-op in space
                // rather than a teleport across the pad.
                if (r.mode != DemoPatrolRoute.RouteMode.Once) path.Add(pts[0]);
                return path;
            }

            void Tick(float dt)
            {
                var r = Route();
                var path = TravelPath();
                int last = path.Count - 1;
                if (r == null || last < 1) return;
                if (sim.Node > last) sim.Reset();      // the list shrank under us

                // Facing chases the current leg, rate-limited — this is what turnRateDegrees buys you.
                int from = Mathf.Clamp(sim.Node, 0, last);
                int to = Mathf.Min(from + 1, last);
                if (from != to)
                    sim.Heading = Mathf.MoveTowardsAngle(sim.Heading, PadAngle(path[to] - path[from]),
                                                         Mathf.Max(1f, r.turnRateDegrees) * dt);

                if (sim.Finished) return;
                if (sim.Wait > 0f) { sim.Wait -= dt; sim.Pulse += dt; return; }
                if (r.moveSpeed <= 0.01f) return;

                float remaining = r.moveSpeed * dt;
                for (int guard = 0; remaining > 0f && guard < 64; guard++)
                {
                    int a = sim.Node, b = sim.Node + 1;
                    if (b > last)
                    {
                        if (r.mode == DemoPatrolRoute.RouteMode.Once) { sim.Finished = true; break; }
                        sim.Node = 0; sim.Travelled = 0f;
                        continue;
                    }

                    float legLength = Vector2.Distance(path[a], path[b]);
                    if (legLength < 0.0001f) { sim.Node = b; sim.Travelled = 0f; continue; }

                    float need = legLength - sim.Travelled;
                    if (remaining < need) { sim.Travelled += remaining; remaining = 0f; }
                    else
                    {
                        sim.Node = b;
                        sim.Travelled = 0f;
                        sim.Wait = r.waitAtWaypoint;
                        sim.Pulse = 0f;
                        break;
                    }
                }
            }

            Vector2 WalkerAt(List<Vector2> path)
            {
                int last = path.Count - 1;
                int a = Mathf.Clamp(sim.Node, 0, last);
                int b = Mathf.Min(a + 1, last);
                float legLength = Vector2.Distance(path[a], path[b]);
                return legLength < 0.0001f ? path[a] : Vector2.Lerp(path[a], path[b], sim.Travelled / legLength);
            }

            // ---- painting --------------------------------------------------------------------------------
            void Paint(MeshGenerationContext ctx)
            {
                var r = Route();
                if (r == null) return;
                var rect = pad.contentRect;
                if (rect.width < 4f || rect.height < 4f) return;

                var view = ViewFor(rect);
                var p = ctx.painter2D;
                p.lineJoin = LineJoin.Round;
                p.lineCap = LineCap.Round;

                // Grid, with the world origin picked out so dragging reads against something fixed.
                p.strokeColor = new Color(0.22f, 0.42f, 0.62f, 0.14f);
                p.lineWidth = 1f;
                p.BeginPath();
                for (int i = 1; i < 6; i++)
                {
                    float x = rect.xMin + rect.width * i / 6f;
                    float y = rect.yMin + rect.height * i / 6f;
                    p.MoveTo(new Vector2(x, rect.yMin)); p.LineTo(new Vector2(x, rect.yMax));
                    p.MoveTo(new Vector2(rect.xMin, y)); p.LineTo(new Vector2(rect.xMax, y));
                }
                p.Stroke();

                var origin2 = view.ToPad(Vector2.zero);
                p.strokeColor = new Color(0.30f, 0.52f, 0.74f, 0.30f);
                p.BeginPath();
                p.MoveTo(new Vector2(origin2.x - 5f, origin2.y)); p.LineTo(new Vector2(origin2.x + 5f, origin2.y));
                p.MoveTo(new Vector2(origin2.x, origin2.y - 5f)); p.LineTo(new Vector2(origin2.x, origin2.y + 5f));
                p.Stroke();

                var pts = Points();
                if (pts == null || pts.Count == 0) return;
                var line = RoutePolyline();

                // Sight wedge, under the route so the path stays legible on top of it.
                if (SightWedge(view, rect, out var eye, out var faceDeg, out var radius, out var handle))
                {
                    float half = r.sightConeDegrees * 0.5f;
                    p.BeginPath();
                    p.MoveTo(eye);
                    p.Arc(eye, radius, faceDeg - half, faceDeg + half);
                    p.ClosePath();
                    p.fillColor = new Color(r.routeTint.r, r.routeTint.g, r.routeTint.b, 0.13f);
                    p.Fill();

                    p.strokeColor = new Color(r.routeTint.r, r.routeTint.g, r.routeTint.b, 0.35f);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(eye);
                    p.Arc(eye, radius, faceDeg - half, faceDeg + half);
                    p.ClosePath();
                    p.Stroke();

                    bool hot = hoverHandle || dragHandle;
                    p.BeginPath();
                    p.Arc(handle, hot ? 6f : 4.5f, 0f, 360f);
                    p.fillColor = hot ? new Color(1f, 0.86f, 0.45f) : new Color(0.85f, 0.72f, 0.40f, 0.9f);
                    p.Fill();
                }

                if (line.Count > 1)
                {
                    // A wide translucent pass under the crisp one — cheap glow, keeps the route readable over
                    // the wedge fill.
                    p.strokeColor = new Color(r.routeTint.r, r.routeTint.g, r.routeTint.b, 0.18f);
                    p.lineWidth = 7f;
                    StrokePolyline(p, view, line);

                    p.strokeColor = r.routeTint;
                    p.lineWidth = 2f;
                    StrokePolyline(p, view, line);

                    // Direction of travel. PingPong gets a second, opposed head so the mode reads at a glance.
                    bool pingPong = r.mode == DemoPatrolRoute.RouteMode.PingPong;
                    for (int i = 1; i < line.Count; i++)
                    {
                        var a = view.ToPad(line[i - 1]);
                        var b = view.ToPad(line[i]);
                        if ((b - a).sqrMagnitude < 400f) continue;
                        float deg = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                        Arrow(p, Vector2.Lerp(a, b, pingPong ? 0.38f : 0.5f), deg, r.routeTint);
                        if (pingPong) Arrow(p, Vector2.Lerp(a, b, 0.62f), deg + 180f, r.routeTint);
                    }

                    // The ghost the insert click would drop.
                    if (insertAfter >= 0 && dragNode < 0 && !dragHandle)
                    {
                        var at = view.ToPad(insertAt);
                        p.strokeColor = new Color(0.95f, 0.95f, 1f, 0.85f);
                        p.lineWidth = 1.5f;
                        p.BeginPath(); p.Arc(at, 6.5f, 0f, 360f); p.Stroke();
                        p.BeginPath();
                        p.MoveTo(new Vector2(at.x - 3f, at.y)); p.LineTo(new Vector2(at.x + 3f, at.y));
                        p.MoveTo(new Vector2(at.x, at.y - 3f)); p.LineTo(new Vector2(at.x, at.y + 3f));
                        p.Stroke();
                    }
                }

                // The guard.
                var path = TravelPath();
                if (path.Count > 1)
                {
                    var walker = view.ToPad(WalkerAt(path));
                    var warm = new Color(1f, 0.78f, 0.35f);

                    if (sim.Wait > 0f && r.waitAtWaypoint > 0.01f)
                    {
                        // Pulse while parked, so waitAtWaypoint isn't an invisible field.
                        float t = Mathf.Clamp01(sim.Pulse / Mathf.Max(0.25f, r.waitAtWaypoint));
                        p.strokeColor = new Color(warm.r, warm.g, warm.b, 0.55f * (1f - t));
                        p.lineWidth = 1.5f;
                        p.BeginPath(); p.Arc(walker, 6f + 12f * t, 0f, 360f); p.Stroke();
                    }

                    p.BeginPath(); p.Arc(walker, 9f, 0f, 360f);
                    p.fillColor = new Color(warm.r, warm.g, warm.b, 0.18f); p.Fill();

                    p.BeginPath(); p.Arc(walker, 4.5f, 0f, 360f);
                    p.fillColor = warm; p.Fill();

                    // Facing tick — lags the corner by turnRateDegrees.
                    var nose = walker + Dir(sim.Heading) * 11f;
                    p.strokeColor = warm; p.lineWidth = 2f;
                    p.BeginPath(); p.MoveTo(walker); p.LineTo(nose); p.Stroke();
                }

                // Waypoints last — they are the thing you grab, so nothing draws over them.
                for (int i = 0; i < pts.Count; i++)
                {
                    var at = view.ToPad(pts[i]);
                    bool isStart = i == 0;
                    bool hot = i == dragNode || (dragNode < 0 && i == hoverNode);
                    float rad = (isStart ? StartRadius : NodeRadius) + (hot ? 2.5f : 0f);

                    if (hot)
                    {
                        p.BeginPath(); p.Arc(at, rad + 5f, 0f, 360f);
                        p.fillColor = new Color(1f, 1f, 1f, 0.14f); p.Fill();
                    }

                    p.BeginPath(); p.Arc(at, rad, 0f, 360f);
                    p.fillColor = isStart ? new Color(0.42f, 0.82f, 0.55f) : r.routeTint;
                    p.Fill();

                    p.BeginPath(); p.Arc(at, rad, 0f, 360f);
                    p.strokeColor = hot ? Color.white : new Color(0.05f, 0.06f, 0.09f, 0.85f);
                    p.lineWidth = hot ? 2f : 1.5f;
                    p.Stroke();
                }
            }

            pad.generateVisualContent += Paint;

            // ---- hit testing -----------------------------------------------------------------------------
            int NodeUnder(View view, Vector2 at)
            {
                var pts = Points();
                if (pts == null) return -1;
                int best = -1;
                float bestDistance = GrabRadius;
                for (int i = 0; i < pts.Count; i++)
                {
                    float d = Vector2.Distance(view.ToPad(pts[i]), at);
                    if (d <= bestDistance) { bestDistance = d; best = i; }
                }
                return best;
            }

            // Nearest leg of the drawn route, as (index of the waypoint the new one would follow, world point).
            bool LegUnder(View view, Vector2 at, out int after, out Vector2 world)
            {
                after = -1; world = Vector2.zero;
                var line = RoutePolyline();
                if (line.Count < 2) return false;

                float bestDistance = LegGrab;
                for (int i = 1; i < line.Count; i++)
                {
                    var a = view.ToPad(line[i - 1]);
                    var b = view.ToPad(line[i]);
                    var ab = b - a;
                    float lenSq = ab.sqrMagnitude;
                    if (lenSq < 1f) continue;
                    float t = Mathf.Clamp01(Vector2.Dot(at - a, ab) / lenSq);
                    var closest = a + ab * t;
                    float d = Vector2.Distance(closest, at);
                    if (d > bestDistance) continue;
                    bestDistance = d;
                    after = i;                       // insert BEFORE line[i]; for the closing leg that appends
                    world = view.ToWorld(closest);
                }
                return after >= 0;
            }

            bool HandleUnder(View view, Rect rect, Vector2 at)
                => SightWedge(view, rect, out _, out _, out _, out var handle) &&
                   Vector2.Distance(handle, at) <= 9f;

            // ---- pointer ---------------------------------------------------------------------------------
            void ClearHover()
            {
                if (hoverNode < 0 && insertAfter < 0 && !hoverHandle) return;
                hoverNode = -1; insertAfter = -1; hoverHandle = false;
                SyncHint();
                pad.MarkDirtyRepaint();
            }

            pad.RegisterCallback<PointerDownEvent>(evt =>
            {
                var r = Route();
                var rect = pad.contentRect;
                if (r == null || rect.width < 4f) return;
                if (evt.button != 0) return;

                var view = ViewFor(rect);
                var at = (Vector2)evt.localPosition;

                if (HandleUnder(view, rect, at))
                {
                    undoGroup = Undo.GetCurrentGroup();
                    dragLabel = "Adjust Sight Cone";
                    dragHandle = true;
                    pinnedExtent = ViewExtent();
                    pad.CapturePointer(evt.pointerId);
                    SyncHint();
                    evt.StopPropagation();
                    return;
                }

                int node = NodeUnder(view, at);
                if (node >= 0)
                {
                    if (evt.altKey)
                    {
                        RemoveWaypoint(node);
                        hoverNode = -1;
                        Sync(); SyncHint();
                        evt.StopPropagation();
                        return;
                    }
                    undoGroup = Undo.GetCurrentGroup();
                    dragLabel = "Move Waypoint";
                    dragNode = node;
                    pinnedExtent = ViewExtent();
                    pad.CapturePointer(evt.pointerId);
                    SyncHint();
                    pad.MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }

                if (LegUnder(view, at, out int after, out var world))
                {
                    undoGroup = Undo.GetCurrentGroup();
                    dragLabel = "Insert Waypoint";
                    InsertWaypoint(after, world);
                    insertAfter = -1;
                    dragNode = after;                 // grab the new one straight away — insert then place
                    pinnedExtent = ViewExtent();
                    pad.CapturePointer(evt.pointerId);
                    Sync(); SyncHint();
                    evt.StopPropagation();
                }
            });

            pad.RegisterCallback<PointerMoveEvent>(evt =>
            {
                var r = Route();
                var rect = pad.contentRect;
                if (r == null || rect.width < 4f) return;

                var view = ViewFor(rect);
                var at = (Vector2)evt.localPosition;

                if (dragHandle)
                {
                    if (!SightWedge(view, rect, out var eye, out var faceDeg, out _, out _)) return;
                    var v = at - eye;
                    WriteFloat("sightRange", Mathf.Clamp(v.magnitude / view.Scale, 0f, 40f));
                    if (v.sqrMagnitude > 25f)
                    {
                        float offset = Mathf.Abs(Mathf.DeltaAngle(faceDeg, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg));
                        WriteFloat("sightConeDegrees", Mathf.Clamp(offset * 2f, 0f, 180f));
                    }
                    Sync(); SyncHint();
                    evt.StopPropagation();
                    return;
                }

                if (dragNode >= 0)
                {
                    float extent = ViewExtent();
                    var world = view.ToWorld(at);
                    if (evt.ctrlKey || evt.commandKey)
                        world = new Vector2(Mathf.Round(world.x * 2f) * 0.5f, Mathf.Round(world.y * 2f) * 0.5f);
                    world.x = Mathf.Clamp(world.x, -extent, extent);
                    world.y = Mathf.Clamp(world.y, -extent, extent);
                    WriteWaypoint(dragNode, world);
                    Sync(); SyncHint();
                    evt.StopPropagation();
                    return;
                }

                // Hover: handle beats waypoint beats leg, matching what a press would do.
                int node = -1, after = -1;
                var ghost = Vector2.zero;
                bool handle = HandleUnder(view, rect, at);
                if (!handle)
                {
                    node = NodeUnder(view, at);
                    if (node < 0) LegUnder(view, at, out after, out ghost);
                }

                bool changed = handle != hoverHandle || node != hoverNode || after != insertAfter ||
                               (after >= 0 && (ghost - insertAt).sqrMagnitude > 0.0001f);
                if (!changed) return;
                hoverHandle = handle; hoverNode = node; insertAfter = after; insertAt = ghost;
                SyncHint();
                pad.MarkDirtyRepaint();
            });

            void EndDrag(int pointerId)
            {
                if (dragNode < 0 && !dragHandle) return;
                dragNode = -1;
                dragHandle = false;
                pinnedExtent = 0f;
                if (pad.HasPointerCapture(pointerId)) pad.ReleasePointer(pointerId);
                Undo.CollapseUndoOperations(undoGroup);   // one drag, one undo step
                Undo.SetCurrentGroupName(dragLabel);
                Sync(); SyncHint();
            }

            pad.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (dragNode < 0 && !dragHandle) return;
                EndDrag(evt.pointerId);
                evt.StopPropagation();
            });

            pad.RegisterCallback<PointerCaptureOutEvent>(evt => EndDrag(evt.pointerId));
            pad.RegisterCallback<PointerLeaveEvent>(_ => { if (dragNode < 0 && !dragHandle) ClearHover(); });

            // ---- toolbar ---------------------------------------------------------------------------------
            Button play = null;
            play = ToolButton("Pause", () =>
            {
                sim.Playing = !sim.Playing;
                play.text = sim.Playing ? "Pause" : "Play";
            });
            tools.Add(play);

            tools.Add(ToolButton("Restart", () => { sim.Reset(); pad.MarkDirtyRepaint(); }));

            tools.Add(ToolButton("Reverse", () =>
            {
                var list = Waypoints();
                if (list == null || list.arraySize < 2) return;
                var buffer = new List<Vector2>();
                for (int i = 0; i < list.arraySize; i++) buffer.Add(list.GetArrayElementAtIndex(i).vector2Value);
                buffer.Reverse();
                for (int i = 0; i < list.arraySize; i++) list.GetArrayElementAtIndex(i).vector2Value = buffer[i];
                serializedObject.ApplyModifiedProperties();
                Undo.SetCurrentGroupName("Reverse Patrol Route");
                sim.Reset();
                Sync();
            }));

            tools.Add(ToolButton("Add", () =>
            {
                var pts = Points();
                Vector2 next;
                if (pts == null || pts.Count == 0) next = Vector2.zero;
                else if (pts.Count == 1) next = pts[0] + new Vector2(2f, 0f);
                else next = Vector2.Lerp(pts[pts.Count - 1], pts[0], 0.5f);   // lands on the closing leg
                InsertWaypoint(pts != null ? pts.Count : 0, next);
                Sync();
            }));

            // ---- lifecycle -------------------------------------------------------------------------------
            // The ticker is owned by the pad, so it dies with the panel — nothing to unregister, and both hosts
            // get their own. It stays scheduled while paused (a no-op call) so resuming doesn't jump.
            pad.schedule.Execute(() =>
            {
                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - lastTick), 0f, 0.1f);
                lastTick = now;
                if (!sim.Playing || Route() == null) return;
                Tick(dt);
                pad.MarkDirtyRepaint();
            }).Every(16);

            Sync();
            SyncHint();

            // Keep the drawing honest while the buyer edits the fields above (or the designed layout around it).
            card.TrackSerializedObjectValue(serializedObject, _ => Sync());
            return card;
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>Simulation cursor for the walking guard. One per built slice — never shared, never static.</summary>
        private sealed class Sim
        {
            public bool Playing = true;
            public int Node;            // index into the travel polyline
            public float Travelled;     // metres into the current leg
            public float Wait;          // seconds left parked at the current waypoint
            public float Pulse;         // seconds since parking, for the wait ring
            public float Heading = 90f; // pad-space degrees, chases the leg at turnRateDegrees
            public bool Finished;       // Once mode, end of the line

            public void Reset()
            {
                Node = 0; Travelled = 0f; Wait = 0f; Pulse = 0f; Finished = false;
            }
        }

        /// <summary>World ⇄ pad mapping. World +Y is north; pad +Y is down, hence the flip.</summary>
        private readonly struct View
        {
            public readonly Vector2 Centre;
            public readonly float Scale;

            public View(Vector2 centre, float scale) { Centre = centre; Scale = scale; }

            public Vector2 ToPad(Vector2 world)
                => Centre + new Vector2(world.x * Scale, -world.y * Scale);

            public Vector2 ToWorld(Vector2 pad)
                => new Vector2((pad.x - Centre.x) / Scale, -(pad.y - Centre.y) / Scale);
        }

        /// <summary>Pad-space direction of a world vector, in degrees (0 = right, growing clockwise on screen).</summary>
        private static float PadAngle(Vector2 worldDelta)
            => Mathf.Atan2(-worldDelta.y, worldDelta.x) * Mathf.Rad2Deg;

        private static Vector2 Dir(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        /// <summary>Distance from a point to the pad's edge along a direction — used to keep handles reachable.</summary>
        private static float MaxReach(Vector2 from, Vector2 direction, Rect rect)
        {
            float best = Mathf.Max(rect.width, rect.height);
            if (Mathf.Abs(direction.x) > 0.0001f)
            {
                float edge = direction.x > 0f ? rect.xMax : rect.xMin;
                best = Mathf.Min(best, (edge - from.x) / direction.x);
            }
            if (Mathf.Abs(direction.y) > 0.0001f)
            {
                float edge = direction.y > 0f ? rect.yMax : rect.yMin;
                best = Mathf.Min(best, (edge - from.y) / direction.y);
            }
            return Mathf.Max(12f, best);
        }

        private static void StrokePolyline(Painter2D p, View view, List<Vector2> world)
        {
            p.BeginPath();
            p.MoveTo(view.ToPad(world[0]));
            for (int i = 1; i < world.Count; i++) p.LineTo(view.ToPad(world[i]));
            p.Stroke();
        }

        private static void Arrow(Painter2D p, Vector2 at, float degrees, Color colour)
        {
            var forward = Dir(degrees);
            var side = new Vector2(-forward.y, forward.x);
            p.BeginPath();
            p.MoveTo(at + forward * 4.5f);
            p.LineTo(at - forward * 3f + side * 3f);
            p.LineTo(at - forward * 3f - side * 3f);
            p.ClosePath();
            p.fillColor = new Color(colour.r, colour.g, colour.b, 0.9f);
            p.Fill();
        }

        private static Button ToolButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.fontSize = 9f;
            button.style.height = 18f;
            button.style.minWidth = 0f;
            button.style.paddingLeft = 7f;
            button.style.paddingRight = 7f;
            button.style.marginLeft = 3f;
            button.style.marginRight = 0f;
            button.style.marginTop = 0f;
            button.style.marginBottom = 0f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return button;
        }

        private static void SetBorder(VisualElement element, Color colour, float width, float radius)
        {
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
