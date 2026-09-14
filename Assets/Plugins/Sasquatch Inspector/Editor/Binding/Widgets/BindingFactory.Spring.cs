using System.Collections.Generic;
using SasquatchSpring;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // Spring widget: a cohesive UI-Toolkit editor for the bonus SasquatchSpring SpringConfig — a preset
        // dropdown, Duration/Bounce sliders, a live spring-response graph (SasquatchSpringControl), and a novel
        // "drag & release" tester slider (SasquatchSpringTester) whose handle springs back to center using the
        // configured spring. SpringConfig is a plain [Serializable] struct, so values are read/written through
        // FindPropertyRelative. Every SpringConfig property routes here — even a Default/legacy node — so the
        // standalone SpringConfigDrawer only ever draws outside a Sasquatch layout.
        private static VisualElement CreateSpringEditor(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            // Guard: must be a SpringConfig-shaped struct; otherwise fall back to the default (drawer) field.
            if (prop.propertyType != SerializedPropertyType.Generic
                || prop.FindPropertyRelative("Duration") == null
                || prop.FindPropertyRelative("Bounce") == null)
                return new PropertyField(prop) { label = resolvedLabel };

            var pDuration = prop.FindPropertyRelative("Duration");
            var pBounce = prop.FindPropertyRelative("Bounce");

            // A freshly-added SpringConfig field deserializes as an all-zero struct — Duration 0, which the 0.01
            // slider floor can never otherwise produce, so it's an unambiguous "never configured" sentinel. It is
            // DISPLAYED as the friendly "Boing" preset so a new field reads lively instead of a dead 0.01s /
            // 0-bounce "Custom" — but it is NOT written here. Building an inspector must perform zero property
            // writes; a display-time stamp fakes a prefab override on every instance the user merely LOOKS at.
            // SpringConfig.Resolved folds the same sentinel at runtime, so the undrawn data already behaves as
            // Boing in a player. MaterialiseUnconfiguredSpring bakes it on the user's first real edit.
            float shownDuration = ShownSpringDuration(prop);
            float shownBounce = ShownSpringBounce(prop);

            var root = new VisualElement();
            root.AddToClassList("sasq-spring-field");

            if (!string.IsNullOrEmpty(resolvedLabel))
            {
                var lbl = new Label(resolvedLabel);
                lbl.AddToClassList("sasq-spring-label");
                // Same reason as the curve/gradient wells — anchor the affixes to the label, not the tail.
                lbl.AddToClassList(FieldAffixes.AffixAnchorClass);
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.marginBottom = 2;
                root.Add(lbl);
            }

            var names = SpringPresetNames();
            var preset = new DropdownField("Preset", names, MatchSpringPreset(shownDuration, shownBounce));

            var durationSlider = new Slider("Duration", 0.01f, 10f) { value = shownDuration, showInputField = true };
            var bounceSlider = new Slider("Bounce", 0f, 2f) { value = shownBounce, showInputField = true };

            // THESE THREE ARE STOCK UNITY FIELDS, AND A STOCK FIELD CANNOT GIVE WIDTH BACK. This widget is
            // routinely handed a narrow host — a grid cell, a Columns child, a frame — and every one of these
            // rows is a fixed 120px label plus a fixed input, so the row simply paints out of it. MEASURED in
            // a live 423pt inspector on a 129px grid cell: the Duration row wanted 196px (120 label + 2
            // margin + 74 input) and spilled 70px past the inspector edge, which is exactly what summons
            // HorizontalOverflowRescue's rail. Twin of the same defect in ExportedWidgetKit's SpringWell.
            LetStockFieldShrink(preset);
            LetStockFieldShrink(durationSlider);
            LetStockFieldShrink(bounceSlider);

            var graph = new SasquatchSpringControl(prop);
            graph.style.height = 64;
            graph.style.marginTop = 4;
            graph.style.marginBottom = 6;

            // Multi-object mixed: native "—" on the sliders/preset + a ghosted graph (no single spring shown).
            // RE-EVALUATED, never stamped once at build: the first edit resolves the mix and an undo can bring
            // it back, so a widget frozen in whichever chrome it was born with lies for the rest of its life.
            // Driven from the same tracker that pushes the values (see the bottom of this method).
            void SyncMixedChrome()
            {
                bool springMixed = MixedValueUI.IsMixed(prop);
                preset.showMixedValue = springMixed;
                durationSlider.showMixedValue = springMixed;
                bounceSlider.showMixedValue = springMixed;
                graph.style.opacity = springMixed ? 0.25f : 1f;
            }
            SyncMixedChrome();

            void SyncPreset() =>
                preset.SetValueWithoutNotify(names[MatchSpringPreset(ShownSpringDuration(prop), ShownSpringBounce(prop))]);

            // Every edit path materialises the never-configured sentinel FIRST: it only holds while EVERY field
            // is still zero, so writing one field alone would leave Duration at 0 — a dead 0.01s snap.
            durationSlider.RegisterValueChangedCallback(e =>
            {
                MaterialiseUnconfiguredSpring(prop);
                pDuration.floatValue = e.newValue;
                prop.serializedObject.ApplyModifiedProperties();
                bounceSlider.SetValueWithoutNotify(pBounce.floatValue);
                graph.Refresh();
                SyncPreset();
            });
            bounceSlider.RegisterValueChangedCallback(e =>
            {
                MaterialiseUnconfiguredSpring(prop);
                pBounce.floatValue = e.newValue;
                prop.serializedObject.ApplyModifiedProperties();
                durationSlider.SetValueWithoutNotify(pDuration.floatValue);
                graph.Refresh();
                SyncPreset();
            });
            preset.RegisterValueChangedCallback(e =>
            {
                int idx = names.IndexOf(e.newValue);
                if (idx < 0 || idx >= SpringPresets.Length) return; // "Custom"
                pDuration.floatValue = SpringPresets[idx].d;
                pBounce.floatValue = SpringPresets[idx].b;
                prop.serializedObject.ApplyModifiedProperties();
                durationSlider.SetValueWithoutNotify(SpringPresets[idx].d);
                bounceSlider.SetValueWithoutNotify(SpringPresets[idx].b);
                graph.Refresh();
            });

            root.Add(preset);
            root.Add(durationSlider);
            root.Add(bounceSlider);
            root.Add(graph);

            // The novel "feel it" slider — drag the dot, release, watch it spring home with THIS config.
            var tester = new SasquatchSpringTester(() => ReadSpringConfig(prop));
            root.Add(tester);
            var hint = new Label("Drag the dot and release — it springs back to center using your spring.");
            hint.style.fontSize = 10;
            hint.style.color = WidgetPalette.LabelDim;
            hint.style.marginTop = 3;
            hint.style.whiteSpace = WhiteSpace.Normal;
            root.Add(hint);

            // Advanced sub-fields (rarely needed) in the project's own expandable group — same caret + spring
            // open/close as the Curve/Gradient widgets, rather than Unity's plain Foldout.
            var advContent = new VisualElement();
            advContent.Add(new PropertyField(prop.FindPropertyRelative("SettleEpsilon"), "Settle Epsilon"));
            advContent.Add(new PropertyField(prop.FindPropertyRelative("FrequencyScale"), "Frequency Scale"));
            advContent.Add(new PropertyField(prop.FindPropertyRelative("Unscaled"), "Unscaled Time"));
            var adv = BuildExpandableGroup("Advanced", null, advContent,
                FoldoutStateStore.Key(prop.serializedObject.targetObject, prop.propertyPath, "SpringAdvanced"),
                fallbackExpanded: false,
                listRowEligible: false); // a sub-group inside the spring widget, not a row in the inspector's run
            adv.style.marginTop = 4;
            root.Add(adv);

            // External changes (undo, multi-edit, an advanced field) → resync the controls + graph + the
            // mixed chrome (an edit can resolve the mix, an undo can reintroduce it).
            root.TrackPropertyValueWithRefresh(prop, _ =>
            {
                durationSlider.SetValueWithoutNotify(ShownSpringDuration(prop));
                bounceSlider.SetValueWithoutNotify(ShownSpringBounce(prop));
                graph.Refresh();
                SyncPreset();
                SyncMixedChrome();
            });

            return root;
        }

        /// <summary>
        /// The smallest a slider's number box may be squeezed to — wide enough to still read a value like
        /// "0.65". A truncated LABEL is a minor inconvenience; a truncated NUMBER is the widget lying.
        /// </summary>
        private const float SpringNumberBoxFloor = 34f;

        /// <summary>
        /// The narrowest a stock field's INPUT half may be starved to before the LABEL has to give way
        /// instead. Without it the label wins outright and the fix does nothing: Unity gives
        /// <c>.unity-base-field__input</c> <c>flex-basis: 0</c> with <c>flex-grow: 1</c>, so the input's base
        /// size is ZERO and it is simply handed whatever the label leaves over — MEASURED at 7px in a 129px
        /// cell, with the slider's own 20px track and 34px value box then spilling 51px out of it. Giving the
        /// input a floor is what turns the label into the part that yields.
        /// </summary>
        private const float SpringInputFloor = SpringNumberBoxFloor + 10f;

        /// <summary>
        /// Lets one of the Spring widget's stock Unity sub-fields GIVE WAY in a narrow host rather than
        /// painting straight out of it.
        /// <para>
        /// This is the <c>flexgrow-without-flexshrink</c> family again, and the recipe is the one that fixed
        /// the other five widgets: <c>flexShrink 1</c> so the part can yield, and <c>minWidth 0</c> because a
        /// <c>min-width: auto</c> floor silently re-pins it at its content width. Unity's default sheet gives
        /// <c>.unity-base-field__label</c> a 120px min-width with NO shrink, which on its own is more than a
        /// narrow cell has to give — so the label is the part that matters most here, and it also gets the
        /// clip trio so a squeezed label truncates instead of shoving its input off the row.
        /// </para>
        /// <para>
        /// Deliberately NOT a general BindingFactory helper: it reaches into stock Unity field internals by
        /// USS class, and the rest of the package routes its labels through the label-column engine instead.
        /// This widget is the exception because it hosts raw Unity fields inside its own body.
        /// </para>
        /// </summary>
        private static void LetStockFieldShrink(VisualElement field)
        {
            if (field == null) return;
            field.style.flexShrink = 1f;
            field.style.minWidth = 0f;

            // Depth-first, so this is the field's OWN input container (the slider's inner number box carries
            // the same class further down, and must not be the one that matches).
            var input = field.Q(className: "unity-base-field__input");
            if (input != null)
            {
                input.style.flexShrink = 1f;
                input.style.minWidth = SpringInputFloor;
            }

            // Inside a slider's input, the TRACK is the first thing to go — a 20px track in a narrow cell was
            // never draggable — and the value box is the last, because the number is what the user is
            // actually reading. Both must be shrinkable or the input's own floor is a lie: its children
            // would keep their stock sizes and spill out of it.
            var dragContainer = field.Q(className: "unity-base-slider__drag-container");
            if (dragContainer != null)
            {
                dragContainer.style.flexShrink = 1f;
                dragContainer.style.minWidth = 0f;
            }

            var numberBox = field.Q(className: "unity-base-slider__text-field");
            if (numberBox != null)
            {
                numberBox.style.flexShrink = 1f;
                numberBox.style.minWidth = SpringNumberBoxFloor;
            }

            var label = field.Q<Label>(className: "unity-base-field__label");
            if (label == null) return;

            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;

            // TRANSPLANT THE STYLESHEET'S LABEL FLOOR ONTO FLEX-BASIS, THEN RELEASE THE FLOOR.
            //
            // Simply zeroing `min-width` is NOT the fix, and the first cut of this proved it: the floor is
            // also the only thing giving the label a size, so releasing it made every label hug its own text
            // and the three rows went ragged — Preset 40px, Bounce 46px, Duration 52px, with their inputs
            // starting at three different x. A widget that only looks right when squeezed is not fixed.
            // So the floor becomes a PREFERENCE (flex-basis, which shrink CAN cross) rather than a hard stop.
            //
            // Read rather than hard-coded: the number is Unity's, from its own default sheet, and this
            // package certifies across 6.0/6.1/6.2 where a default is exactly the kind of thing that moves.
            // Deferred to the first geometry pass because that is the first moment the USS value has actually
            // resolved — reading it at construction gets whatever the element was born with.
            void ReleaseLabelFloor(GeometryChangedEvent _)
            {
                label.UnregisterCallback<GeometryChangedEvent>(ReleaseLabelFloor);

                // The floor in pixels. Falls back to the label's CURRENT laid-out width, which before the
                // release IS the floor (the label is pinned at it in a roomy host and clamped to it in a
                // narrow one) — so a version where `min-width` resolves to `auto` still lands on a sane
                // preference instead of collapsing to a hug.
                float floor = label.resolvedStyle.minWidth.value;
                if (float.IsNaN(floor) || floor <= 0f) floor = label.layout.width;
                if (!float.IsNaN(floor) && floor > 0f) label.style.flexBasis = floor;

                label.style.minWidth = 0f;
                label.style.flexShrink = 1f;
            }
            label.RegisterCallback<GeometryChangedEvent>(ReleaseLabelFloor);
        }

        // ----- Presets (mirrors SpringConfig's static presets / SpringConfigDrawer) -----
        private struct SpringPreset { public string name; public float d; public float b; }

        private static readonly SpringPreset[] SpringPresets =
        {
            new SpringPreset { name = "Default", d = 0.6f,  b = 0.25f },
            new SpringPreset { name = "Snappy",  d = 0.3f,  b = 0f    },
            new SpringPreset { name = "Bouncy",  d = 0.6f,  b = 0.6f  },
            new SpringPreset { name = "Boing",   d = 0.8f,  b = 1.3f  },
            new SpringPreset { name = "Glide",   d = 1.2f,  b = 0.5f  },
            new SpringPreset { name = "Heavy",   d = 1.5f,  b = 0f    },
            new SpringPreset { name = "Direct",  d = 0.15f, b = 0f    },
            new SpringPreset { name = "Soft",    d = 1.0f,  b = 0.1f  },
        };

        private static List<string> SpringPresetNames()
        {
            var list = new List<string>();
            foreach (var p in SpringPresets) list.Add(p.name);
            list.Add("Custom");
            return list;
        }

        private static int MatchSpringPreset(float d, float b)
        {
            for (int i = 0; i < SpringPresets.Length; i++)
                if (Mathf.Approximately(SpringPresets[i].d, d) && Mathf.Approximately(SpringPresets[i].b, b))
                    return i;
            return SpringPresets.Length; // Custom
        }

        // ===== The never-configured sentinel, resolved at READ time =====
        // Unity's serializer never runs a struct's constructor, so a SpringConfig field added to an existing
        // component arrives entirely zero-filled — a legitimate-looking but dead 0.01s / no-bounce spring.
        // SpringConfig.Resolved folds that to the friendly Boing preset at RUNTIME; these are the editor half.
        //
        // Both editors used to STAMP the preset into the SerializedProperty the first time they drew. That is a
        // write performed by merely displaying an object: on a prefab instance it invented two override arrows
        // nobody made, and on a plain object it dirtied the scene on selection. The stamp is now deferred to
        // MaterialiseUnconfiguredSpring, which only ever runs from an edit callback.

        /// <summary>
        /// True while a serialized SpringConfig still carries the never-configured all-zero pattern — the same
        /// test <see cref="SpringConfig.Resolved"/> applies at runtime, read straight off the properties.
        /// A mixed multi-selection is never "unconfigured": one answer cannot speak for several objects.
        /// </summary>
        internal static bool IsUnconfiguredSpring(SerializedProperty springProp)
        {
            var d = springProp != null ? springProp.FindPropertyRelative("Duration") : null;
            var b = springProp != null ? springProp.FindPropertyRelative("Bounce") : null;
            if (d == null || b == null) return false;
            if (d.hasMultipleDifferentValues || b.hasMultipleDifferentValues) return false;

            var fs = springProp.FindPropertyRelative("FrequencyScale");
            var se = springProp.FindPropertyRelative("SettleEpsilon");
            var un = springProp.FindPropertyRelative("Unscaled");
            if (fs != null && fs.hasMultipleDifferentValues) return false;
            if (se != null && se.hasMultipleDifferentValues) return false;
            if (un != null && un.hasMultipleDifferentValues) return false;

            return d.floatValue == 0f && b.floatValue == 0f
                && (fs == null || fs.floatValue == 0f)
                && (se == null || se.floatValue == 0f)
                && (un == null || !un.boolValue);
        }

        /// <summary>The Duration a SpringConfig property should be DRAWN with (the sentinel resolves to Boing).</summary>
        internal static float ShownSpringDuration(SerializedProperty springProp)
        {
            if (IsUnconfiguredSpring(springProp)) return SpringConfig.Boing.Duration; // 0.8
            var d = springProp != null ? springProp.FindPropertyRelative("Duration") : null;
            return d != null ? d.floatValue : SpringConfig.Default.Duration;
        }

        /// <summary>The Bounce a SpringConfig property should be DRAWN with (the sentinel resolves to Boing).</summary>
        internal static float ShownSpringBounce(SerializedProperty springProp)
        {
            if (IsUnconfiguredSpring(springProp)) return SpringConfig.Boing.Bounce; // 1.3
            var b = springProp != null ? springProp.FindPropertyRelative("Bounce") : null;
            return b != null ? b.floatValue : SpringConfig.Default.Bounce;
        }

        /// <summary>
        /// Bakes the never-configured sentinel into the data, so the config the user is about to edit is the one
        /// they have been LOOKING at. Call from an edit callback only — never from a build/draw path.
        /// </summary>
        /// <remarks>
        /// Writing a single field on a zero-filled config destroys the sentinel and strands Duration at 0 (an
        /// instant snap), so both halves land together before the edit itself. The caller applies.
        /// </remarks>
        internal static void MaterialiseUnconfiguredSpring(SerializedProperty springProp)
        {
            if (!IsUnconfiguredSpring(springProp)) return;
            springProp.FindPropertyRelative("Duration").floatValue = SpringConfig.Boing.Duration;
            springProp.FindPropertyRelative("Bounce").floatValue = SpringConfig.Boing.Bounce;
        }

        /// <summary>True when the property is a SasquatchSpring SpringConfig struct (so it should always render
        /// with the Spring widget, regardless of the node's stored DisplayType).</summary>
        internal static bool IsSpringConfigProperty(SerializedProperty prop)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Generic) return false;
            if (prop.type != "SpringConfig") return false;
            return prop.FindPropertyRelative("Duration") != null && prop.FindPropertyRelative("Bounce") != null;
        }

        /// <summary>Read a live <see cref="SpringConfig"/> from a bound SpringConfig SerializedProperty.</summary>
        /// <remarks>
        /// Duration is passed through RAW, never floored to the 0.01 slider minimum: an all-zero struct IS the
        /// never-configured sentinel, and flooring it destroys the sentinel so <c>Resolved</c> can no longer see
        /// it — the graph and the tester would animate a dead 0.01s snap while the runtime played Boing.
        /// <c>SpringConfig.GetFrequency</c> applies its own 0.01 floor, so nothing divides by zero here.
        /// </remarks>
        internal static SpringConfig ReadSpringConfig(SerializedProperty prop)
        {
            try
            {
                float d = prop.FindPropertyRelative("Duration")?.floatValue ?? 0.6f;
                float b = prop.FindPropertyRelative("Bounce")?.floatValue ?? 0.5f;
                float fs = prop.FindPropertyRelative("FrequencyScale")?.floatValue ?? 0f;
                float se = prop.FindPropertyRelative("SettleEpsilon")?.floatValue ?? 0f;
                bool un = prop.FindPropertyRelative("Unscaled")?.boolValue ?? false;
                return new SpringConfig(d, b) { FrequencyScale = fs, SettleEpsilon = se, Unscaled = un }.Resolved;
            }
            catch
            {
                return SpringConfig.Bouncy;
            }
        }
    }
}
