using SasquatchSpring;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Friendly inspector for <see cref="SpringConfig"/>: a preset dropdown (Snappy / Bouncy / … / Custom),
    /// the Duration + Bounce sliders, a live spring-curve preview sampled from <see cref="SpringMath"/>, and
    /// an advanced foldout for SettleEpsilon / FrequencyScale / Unscaled. Replaces the four raw float fields.
    /// </summary>
    [CustomPropertyDrawer(typeof(SpringConfig))]
    public sealed class SpringConfigDrawer : PropertyDrawer
    {
        private struct Preset { public string Name; public float Duration; public float Bounce; }

        // Mirrors SpringConfig's static presets (duration, bounce). Keep in sync with SpringConfig.cs.
        private static readonly Preset[] Presets =
        {
            new Preset { Name = "Default", Duration = 0.6f,  Bounce = 0.25f },
            new Preset { Name = "Snappy",  Duration = 0.3f,  Bounce = 0f    },
            new Preset { Name = "Bouncy",  Duration = 0.6f,  Bounce = 0.6f  },
            new Preset { Name = "Boing",   Duration = 0.8f,  Bounce = 1.3f  },
            new Preset { Name = "Glide",   Duration = 1.2f,  Bounce = 0.5f  },
            new Preset { Name = "Heavy",   Duration = 1.5f,  Bounce = 0f    },
            new Preset { Name = "Direct",  Duration = 0.15f, Bounce = 0f    },
            new Preset { Name = "Soft",    Duration = 1.0f,  Bounce = 0.1f  },
        };

        private const float PreviewHeight = 46f;

        /// <summary>IMGUI: total height of the drawer (presets, sliders, curve preview, advanced foldout).</summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float gap = EditorGUIUtility.standardVerticalSpacing;
            // header + preset + duration + bounce + preview + advanced foldout
            float h = (line + gap) * 5f + PreviewHeight + gap + line;
            if (property.isExpanded)
                h += (line + gap) * 3f; // SettleEpsilon, FrequencyScale, Unscaled
            return h;
        }

        /// <summary>IMGUI: draws the friendly <see cref="SpringConfig"/> editor.</summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var pDuration = property.FindPropertyRelative("Duration");
            var pBounce = property.FindPropertyRelative("Bounce");
            var pEpsilon = property.FindPropertyRelative("SettleEpsilon");
            var pFreq = property.FindPropertyRelative("FrequencyScale");
            var pUnscaled = property.FindPropertyRelative("Unscaled");

            // A freshly-added SpringConfig field deserializes as an all-zero struct — Duration 0, which the 0.01
            // slider floor can never otherwise produce, so it's an unambiguous "never configured" sentinel. It is
            // DRAWN as the friendly "Boing" preset so a new field reads lively instead of a dead 0.01s / 0-bounce
            // "Custom" — but NOT written here. This drawer runs in the STOCK inspector, for users who never made a
            // layout, and a stamp on first draw invented two prefab overrides on every instance merely selected,
            // and dirtied the scene for anyone who only looked. SpringConfig.Resolved folds the same sentinel at
            // runtime, so undrawn data already behaves as Boing in a player; the write waits for a real edit.
            bool unconfigured = BindingFactory.IsUnconfiguredSpring(property);
            float shownDuration = BindingFactory.ShownSpringDuration(property);
            float shownBounce = BindingFactory.ShownSpringBounce(property);

            float line = EditorGUIUtility.singleLineHeight;
            float gap = EditorGUIUtility.standardVerticalSpacing;
            var r = new Rect(position.x, position.y, position.width, line);

            // ---- Header (bold label) ----
            EditorGUI.LabelField(r, label, EditorStyles.boldLabel);
            r.y += line + gap;

            EditorGUI.indentLevel++;

            // ---- Preset dropdown ----
            int current = MatchPreset(shownDuration, shownBounce);
            string[] names = new string[Presets.Length + 1];
            for (int i = 0; i < Presets.Length; i++) names[i] = Presets[i].Name;
            names[Presets.Length] = "Custom";

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(r, "Preset", current, names);
            if (EditorGUI.EndChangeCheck() && picked < Presets.Length)
            {
                pDuration.floatValue = Presets[picked].Duration;
                pBounce.floatValue = Presets[picked].Bounce;
            }
            r.y += line + gap;

            // ---- Duration + Bounce ----
            // An unconfigured config is drawn from the RESOLVED values, and the sentinel is materialised on the
            // first edit — writing one half alone would strand Duration at 0 (a dead snap). A configured one keeps
            // the property overloads, which carry the prefab-override bar and the right-click Revert.
            var durationLabel = new GUIContent("Duration", "Time to settle (seconds).");
            var bounceLabel = new GUIContent("Bounce", "0 = no overshoot, 1 = elastic, up to 2 = very springy.");
            if (unconfigured)
            {
                EditorGUI.BeginChangeCheck();
                float newDuration = EditorGUI.Slider(r, durationLabel, shownDuration, 0.01f, 10f);
                r.y += line + gap;
                float newBounce = EditorGUI.Slider(r, bounceLabel, shownBounce, 0f, 2f);
                r.y += line + gap;
                if (EditorGUI.EndChangeCheck())
                {
                    pDuration.floatValue = newDuration;
                    pBounce.floatValue = newBounce;
                    shownDuration = newDuration;
                    shownBounce = newBounce;
                }
            }
            else
            {
                EditorGUI.Slider(r, pDuration, 0.01f, 10f, durationLabel);
                r.y += line + gap;
                EditorGUI.Slider(r, pBounce, 0f, 2f, bounceLabel);
                r.y += line + gap;
                shownDuration = pDuration.floatValue;
                shownBounce = pBounce.floatValue;
            }

            // ---- Preview ----
            var previewRect = new Rect(r.x + EditorGUIUtility.labelWidth, r.y, r.width - EditorGUIUtility.labelWidth, PreviewHeight);
            EditorGUI.LabelField(new Rect(r.x, r.y, EditorGUIUtility.labelWidth, line), " ");
            DrawPreview(previewRect, shownDuration, shownBounce, pFreq.floatValue);
            r.y += PreviewHeight + gap;

            // ---- Advanced foldout ----
            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, "Advanced", true);
            r.y += line + gap;
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(r, pEpsilon, new GUIContent("Settle Epsilon", "Arrival threshold in the value's units (0 = engine default). Pixel-space springs want ~0.5."));
                r.y += line + gap;
                EditorGUI.PropertyField(r, pFreq, new GUIContent("Frequency Scale", "Wobble speed without changing settle time (0 = 1). <1 = looser/slower, >1 = tighter/faster."));
                r.y += line + gap;
                EditorGUI.PropertyField(r, pUnscaled, new GUIContent("Unscaled Time", "Keep animating while the game is paused (Time.timeScale 0) — pause menus / UI."));
                r.y += line + gap;
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        private static int MatchPreset(float duration, float bounce)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Mathf.Approximately(Presets[i].Duration, duration) && Mathf.Approximately(Presets[i].Bounce, bounce))
                    return i;
            return Presets.Length; // Custom
        }

        // Samples the actual spring physics (0 -> 1 step) and draws the response curve, with the target
        // line at the top, so the bounce/settle character is visible at a glance.
        private static void DrawPreview(Rect rect, float duration, float bounce, float frequencyScale)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));

            var cfg = new SpringConfig(Mathf.Max(0.01f, duration), bounce) { FrequencyScale = frequencyScale };
            float freq = cfg.Frequency;
            float damp = cfg.Damping;

            const int samples = 64;
            float window = Mathf.Max(0.05f, duration) * 1.35f; // a little past settle to show the tail
            float dt = window / samples;

            // Pad vertically so overshoot above 1 (and any dip below 0) stays inside the box.
            float top = rect.y + 6f;        // maps to value ~1.25
            float bottom = rect.yMax - 6f;  // maps to value ~-0.25
            float ValueToY(float v) => Mathf.Lerp(bottom, top, Mathf.InverseLerp(-0.25f, 1.25f, v));

            // Target (value = 1) reference line.
            float targetY = ValueToY(1f);
            EditorGUI.DrawRect(new Rect(rect.x, targetY, rect.width, 1f), new Color(1f, 1f, 1f, 0.18f));

            var pts = new Vector3[samples + 1];
            float pos = 0f, vel = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float x = Mathf.Lerp(rect.x, rect.xMax, i / (float)samples);
                pts[i] = new Vector3(x, ValueToY(pos), 0f);
                SpringMath.Evaluate(dt, pos, vel, 1f, freq, damp, SpringConfig.DefaultSettleEpsilon, out pos, out vel);
            }

            Handles.color = new Color(0.45f, 0.78f, 1f);
            Handles.DrawAAPolyLine(2f, pts);
        }
    }
}
