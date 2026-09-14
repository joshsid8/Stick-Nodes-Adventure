using SasquatchInspector.Editor;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Showcase
{
    // Target for the "Layout Elements" showcase group. Its OWN hand-written [CustomEditor] (below) draws an
    // interactive compass; the "Embedded Editor" card re-inlines JUST that compass via a [SasquatchEmbed] slice
    // — which is the whole point of the Embedded Editor element: surface a section of a custom editor a user
    // already has, right inside a Sasquatch layout. The heading is in degrees, 0 = North, increasing clockwise.
    internal class ShowcaseElementsTarget : ScriptableObject
    {
        [Range(0f, 360f)] public float heading = 40f;
    }

    [CustomEditor(typeof(ShowcaseElementsTarget))]
    internal class ShowcaseElementsTargetEditor : UnityEditor.Editor
    {
        private const float Size = 200f;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new PropertyField(serializedObject.FindProperty("heading"), "Heading"));
            root.Add(Compass());
            root.Bind(serializedObject);
            return root;
        }

        // A [SasquatchEmbed] slice: a self-contained piece of THIS editor that Sasquatch's Embedded Editor
        // element can re-inline on its own. Returns a UI Toolkit element, so Sasquatch hosts + binds it directly.
        [SasquatchEmbed("Compass")]
        public VisualElement Compass()
        {
            var wrap = new VisualElement();
            wrap.style.alignItems = Align.Center;
            wrap.style.paddingTop = 8; wrap.style.paddingBottom = 8;

            var dial = new VisualElement { name = "compass" };
            dial.style.width = Size; dial.style.height = Size;
            dial.generateVisualContent += ctx => Draw(ctx, Heading());

            AddLabel(dial, "N", Size / 2f - 6f, 0f);
            AddLabel(dial, "E", Size - 15f, Size / 2f - 9f);
            AddLabel(dial, "S", Size / 2f - 5f, Size - 20f);
            AddLabel(dial, "W", 3f, Size / 2f - 9f);

            // Drag anywhere on the dial to spin the needle — a genuinely interactive custom-editor section.
            dial.RegisterCallback<PointerDownEvent>(e =>
            {
                SetHeading(dial, e.localPosition);
                dial.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            dial.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dial.HasPointerCapture(e.pointerId)) SetHeading(dial, e.localPosition);
            });
            dial.RegisterCallback<PointerUpEvent>(e =>
            {
                if (dial.HasPointerCapture(e.pointerId)) dial.ReleasePointer(e.pointerId);
            });

            var headingProp = serializedObject.FindProperty("heading");
            if (headingProp != null) dial.TrackPropertyValueWithRefresh(headingProp, _ => dial.MarkDirtyRepaint());

            wrap.Add(dial);
            return wrap;
        }

        private float Heading()
        {
            var p = serializedObject.FindProperty("heading");
            return p != null ? p.floatValue : ((ShowcaseElementsTarget)target).heading;
        }

        private void SetHeading(VisualElement el, Vector3 local)
        {
            Vector2 c = new Vector2(Size / 2f, Size / 2f);
            Vector2 d = new Vector2(local.x - c.x, local.y - c.y);
            if (d.sqrMagnitude < 1f) return;
            float deg = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg; // 0 = up (North), clockwise
            if (deg < 0f) deg += 360f;
            var p = serializedObject.FindProperty("heading");
            if (p == null) return;
            p.floatValue = deg;
            serializedObject.ApplyModifiedProperties();
            el.MarkDirtyRepaint();
        }

        private static void AddLabel(VisualElement parent, string text, float left, float top)
        {
            var l = new Label(text);
            l.style.position = Position.Absolute;
            l.style.left = left; l.style.top = top;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            // Theme-aware: dark-blue label on the light instrument, pale-blue on the dark one.
            l.style.color = WidgetPalette.IsLight ? new Color(0.20f, 0.32f, 0.48f) : new Color(0.78f, 0.85f, 0.93f);
            l.pickingMode = PickingMode.Ignore;
            parent.Add(l);
        }

        // Painter2D: 8 perfectly even 45° sectors (alternating fills, each centered on a compass point), an
        // outer ring, a two-tone needle, and a center hub.
        private static void Draw(MeshGenerationContext ctx, float headingDeg)
        {
            var p = ctx.painter2D;
            Vector2 c = new Vector2(Size / 2f, Size / 2f);
            float R = Size / 2f - 18f;

            // Theme-aware instrument: the light branch renders a pale instrument so the demo doesn't sit as a
            // heavy dark block in a light editor.
            bool isLight = WidgetPalette.IsLight;

            for (int i = 0; i < 8; i++)
            {
                float a0 = i * 45f - 22.5f;
                float a1 = i * 45f + 22.5f;
                p.fillColor = (i % 2 == 0)
                    ? (isLight ? new Color(0.82f, 0.87f, 0.93f) : new Color(0.20f, 0.27f, 0.36f))
                    : (isLight ? new Color(0.89f, 0.92f, 0.96f) : new Color(0.15f, 0.20f, 0.28f));
                p.BeginPath();
                p.MoveTo(c);
                for (int s = 0; s <= 8; s++)
                    p.LineTo(OnDial(c, R, Mathf.Lerp(a0, a1, s / 8f)));
                p.ClosePath();
                p.Fill();
            }

            p.strokeColor = isLight ? new Color(0.20f, 0.45f, 0.85f, 0.9f) : new Color(0.42f, 0.68f, 1f, 0.9f);
            p.lineWidth = 2f;
            p.BeginPath();
            for (int s = 0; s <= 64; s++)
            {
                Vector2 pt = OnDial(c, R, s / 64f * 360f);
                if (s == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Stroke();

            // Needle — North half warm red, South half cool grey.
            Vector2 tip = OnDial(c, R - 14f, headingDeg);
            Vector2 tail = OnDial(c, R - 14f, headingDeg + 180f);
            Vector2 left = OnDial(c, 11f, headingDeg + 90f);
            Vector2 right = OnDial(c, 11f, headingDeg - 90f);
            p.fillColor = isLight ? new Color(0.85f, 0.25f, 0.22f) : new Color(0.92f, 0.36f, 0.34f);
            p.BeginPath(); p.MoveTo(tip); p.LineTo(left); p.LineTo(right); p.ClosePath(); p.Fill();
            p.fillColor = isLight ? new Color(0.45f, 0.50f, 0.58f) : new Color(0.78f, 0.82f, 0.88f);
            p.BeginPath(); p.MoveTo(tail); p.LineTo(left); p.LineTo(right); p.ClosePath(); p.Fill();

            // Center hub.
            p.fillColor = isLight ? new Color(0.35f, 0.45f, 0.56f) : new Color(0.09f, 0.13f, 0.19f);
            p.BeginPath();
            for (int s = 0; s <= 24; s++)
            {
                Vector2 pt = OnDial(c, 6f, s / 24f * 360f);
                if (s == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        // Compass space: 0° = North (up), increasing clockwise.
        private static Vector2 OnDial(Vector2 center, float radius, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(center.x + radius * Mathf.Sin(rad), center.y - radius * Mathf.Cos(rad));
        }
    }
}
